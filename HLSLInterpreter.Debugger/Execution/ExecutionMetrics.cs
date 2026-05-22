using HLSL;
using UnityShaderParser.HLSL;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Execution;

public enum DebugViewMode
{
    Color,
    WarpExecutionDivergence,
    WarpInstructionCount,
    WarpAverageUtilization,
    PixelActiveInstructionCount,
    NanDetection,
    TextureFetches,
}

public enum PixelBadValue : byte
{
    Clean = 0,
    NaN,
    PositiveInfinity,
    NegativeInfinity,
}

public sealed class ExecutionMetrics
{
    public readonly int CanvasW;
    public readonly int CanvasH;
    public readonly int WarpX;
    public readonly int WarpY;
    public readonly int TilesX;
    public readonly int TilesY;

    public readonly int[] WarpTotal;
    public readonly int[] WarpCoherent;
    public readonly int[] WarpActiveSum;

    public readonly int[] PixelActive;
    public readonly int[] PixelFetches;
    public readonly PixelBadValue[] PixelBad;

    public ExecutionMetrics(int canvasW, int canvasH, int warpX, int warpY)
    {
        CanvasW = canvasW;
        CanvasH = canvasH;
        WarpX = warpX;
        WarpY = warpY;
        TilesX = (canvasW + warpX - 1) / warpX;
        TilesY = (canvasH + warpY - 1) / warpY;
        WarpTotal = new int[TilesX * TilesY];
        WarpCoherent = new int[TilesX * TilesY];
        WarpActiveSum = new int[TilesX * TilesY];
        PixelActive = new int[canvasW * canvasH];
        PixelFetches = new int[canvasW * canvasH];
        PixelBad = new PixelBadValue[canvasW * canvasH];
    }

    public Action<HLSLSyntaxNode> MakeBeforeStatementHook(HLSLRunner runner, int tx, int ty)
    {
        int warpIdx = ty * TilesX + tx;
        int threadCount = WarpX * WarpY;
        int canvasW = CanvasW, canvasH = CanvasH, wx = WarpX, wy = WarpY;
        return _ =>
        {
            var state = runner.GetExecutionState();
            WarpTotal[warpIdx]++;
            int activeCount = 0;
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                if (!state.IsThreadActive(threadIndex))
                    continue;
                activeCount++;
                int px = tx * wx + (threadIndex % wx);
                int py = ty * wy + (threadIndex / wx);
                if (px < canvasW && py < canvasH)
                    PixelActive[py * canvasW + px]++;
            }
            if (activeCount == threadCount)
                WarpCoherent[warpIdx]++;
            WarpActiveSum[warpIdx] += activeCount;
        };
    }

    public Action<HLSLSyntaxNode> MakeAfterStatementHook(HLSLRunner runner, int tx, int ty)
    {
        int threadCount = WarpX * WarpY;
        int wx = WarpX;
        return node =>
        {
            var state = runner.GetExecutionState();
            if (node is VariableDeclarationStatementNode varDecl)
            {
                foreach (var d in varDecl.Declarators)
                {
                    if (runner.GetVariable(d.Name) is NumericValue val)
                        MarkBadIfAny(val, state, tx, ty, threadCount, wx);
                }
            }
            else if (node is ExpressionStatementNode exprStmt
                && exprStmt.Expression is AssignmentExpressionNode assign
                && runner.EvaluateExpression(assign.Left) is NumericValue val2)
            {
                MarkBadIfAny(val2, state, tx, ty, threadCount, wx);
            }
        };
    }

    private void MarkBadIfAny(NumericValue value, HLSLExecutionState state, int tx, int ty, int threadCount, int wx)
    {
        if (!HLSLTypeUtils.IsFloat(value.Type))
            return;
        foreach (var component in value.ToScalars())
        {
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                if (!state.IsThreadActive(threadIndex))
                    continue;
                var kind = ClassifyBad(component.AsFloat(threadIndex));
                if (kind == PixelBadValue.Clean) continue;
                int px = tx * wx + (threadIndex % wx);
                int py = ty * WarpY + (threadIndex / wx);
                if (px < CanvasW && py < CanvasH)
                    PixelBad[py * CanvasW + px] = kind;
            }
        }
    }

    private static PixelBadValue ClassifyBad(float f)
    {
        if (float.IsNaN(f)) return PixelBadValue.NaN;
        if (float.IsPositiveInfinity(f)) return PixelBadValue.PositiveInfinity;
        if (float.IsNegativeInfinity(f)) return PixelBadValue.NegativeInfinity;
        return PixelBadValue.Clean;
    }

    public byte[] Render(DebugViewMode mode)
    {
        return mode switch
        {
            DebugViewMode.WarpExecutionDivergence => RenderPerWarp(i => WarpTotal[i] == 0 ? 0f : 1f - (float)WarpCoherent[i] / WarpTotal[i]),
            DebugViewMode.WarpInstructionCount => RenderPerWarpNormalized(WarpTotal),
            DebugViewMode.WarpAverageUtilization => RenderPerWarp(i => WarpTotal[i] == 0 ? 0f : 1f - (float)WarpActiveSum[i] / (WarpTotal[i] * WarpX * WarpY)),
            DebugViewMode.PixelActiveInstructionCount => RenderPerPixelNormalized(PixelActive),
            DebugViewMode.NanDetection => RenderBadValues(),
            DebugViewMode.TextureFetches => RenderPerPixelNormalized(PixelFetches),
            _ => null,
        };
    }

    private byte[] RenderPerWarp(Func<int, float> ratioForWarp)
    {
        var pixels = new byte[CanvasW * CanvasH * 4];
        for (int ty = 0; ty < TilesY; ty++)
        {
            for (int tx = 0; tx < TilesX; tx++)
            {
                FillTile(pixels, tx, ty, TurboGradient(ratioForWarp(ty * TilesX + tx)));
            }
        }
        return pixels;
    }

    private byte[] RenderPerWarpNormalized(int[] values)
    {
        int max = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > max)
                max = values[i];
        }
        return RenderPerWarp(i => max == 0 ? 0f : (float)values[i] / max);
    }

    private byte[] RenderPerPixelNormalized(int[] values)
    {
        int max = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > max)
                max = values[i];
        }
        var pixels = new byte[CanvasW * CanvasH * 4];
        for (int py = 0; py < CanvasH; py++)
        {
            for (int px = 0; px < CanvasW; px++)
            {
                float ratio = max == 0 ? 0f : (float)values[py * CanvasW + px] / max;
                WritePixel(pixels, px, py, TurboGradient(ratio));
            }
        }
        return pixels;
    }

    private byte[] RenderBadValues()
    {
        var pixels = new byte[CanvasW * CanvasH * 4];
        for (int py = 0; py < CanvasH; py++)
        {
            for (int px = 0; px < CanvasW; px++)
            {
                WritePixel(pixels, px, py, BadValueColor(PixelBad[py * CanvasW + px]));
            }
        }
        return pixels;
    }

    private static (byte R, byte G, byte B) BadValueColor(PixelBadValue kind) => kind switch
    {
        PixelBadValue.NaN => ((byte)255, (byte)0, (byte)0),
        PixelBadValue.PositiveInfinity => ((byte)255, (byte)255, (byte)0),
        PixelBadValue.NegativeInfinity => ((byte)0, (byte)255, (byte)255),
        _ => ((byte)0, (byte)0, (byte)0),
    };

    public static string TurboCssGradient()
    {
        var sb = new System.Text.StringBuilder("linear-gradient(to right");
        for (int i = 0; i <= 16; i++)
        {
            var (r, g, b) = TurboGradient(i / 16f);
            sb.Append($", rgb({r},{g},{b})");
        }
        sb.Append(')');
        return sb.ToString();
    }

    public (string Low, string High) ScaleLabels(DebugViewMode mode) => mode switch
    {
        DebugViewMode.WarpExecutionDivergence => ("0%", "100%"),
        DebugViewMode.WarpAverageUtilization => ("0%", "100%"),
        DebugViewMode.WarpInstructionCount => ("0", WarpTotal.Max().ToString()),
        DebugViewMode.PixelActiveInstructionCount => ("0", PixelActive.Max().ToString()),
        DebugViewMode.TextureFetches => ("0", PixelFetches.Max().ToString()),
        _ => ("", ""),
    };

    // https://gist.github.com/mikhailov-work/ee72ba4191942acecc03fe6da94fc73f
    private static (byte R, byte G, byte B) TurboGradient(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        float x2 = x * x;
        float x3 = x2 * x;
        float x4 = x2 * x2;
        float x5 = x4 * x;
        float r = 0.13572138f + 4.61539260f * x + -42.66032258f * x2 + 132.13108234f * x3 + -152.94239396f * x4 + 59.28637943f * x5;
        float g = 0.09140261f + 2.19418839f * x + 4.84296658f * x2 + -14.18503333f * x3 + 4.27729857f * x4 + 2.82956604f * x5;
        float b = 0.10667330f + 12.64194608f * x + -60.58204836f * x2 + 110.36276771f * x3 + -89.90310912f * x4 + 27.34824973f * x5;
        return (HLSLValueDisplay.FloatToByte(r), HLSLValueDisplay.FloatToByte(g), HLSLValueDisplay.FloatToByte(b));
    }

    private void FillTile(byte[] pixels, int tx, int ty, (byte R, byte G, byte B) color)
    {
        int x0 = tx * WarpX, y0 = ty * WarpY;
        int x1 = Math.Min(x0 + WarpX, CanvasW);
        int y1 = Math.Min(y0 + WarpY, CanvasH);
        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                WritePixel(pixels, px, py, color);
            }
        }
    }

    private void WritePixel(byte[] pixels, int px, int py, (byte R, byte G, byte B) color)
    {
        int o = (py * CanvasW + px) * 4;
        pixels[o + 0] = color.R;
        pixels[o + 1] = color.G;
        pixels[o + 2] = color.B;
        pixels[o + 3] = 255;
    }
}
