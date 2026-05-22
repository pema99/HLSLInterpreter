using System.Globalization;
using HLSL;
using HLSLInterpreter.Debugger.Execution;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Utils;

// Turns interpreter values into display text and preview images.
public static class HLSLValueDisplay
{
    public static string Format(HLSLValue value, int threadIndex)
    {
        if (value is ReferenceValue rv) value = rv.Get();
        if (value == null) return "null";

        return value switch
        {
            ScalarValue sv => FormatScalar(sv, threadIndex),
            VectorValue vv => FormatVector(vv, threadIndex),
            MatrixValue mv => FormatMatrix(mv, threadIndex),
            StructValue sv2 => FormatStruct(sv2, threadIndex),
            ArrayValue av => $"[{av.Values.Length}]",
            PredefinedObjectValue pov => pov.ToString(),
            _ => value.GetType().Name
        };
    }

    public static string FormatScalar(ScalarValue sv, int threadIndex)
    {
        try
        {
            int idx = Math.Min(threadIndex, sv.ThreadCount - 1);
            return FormatRawValue(sv.Value.Get(idx), sv.Type);
        }
        catch { return "?"; }
    }

    public static string FormatVector(VectorValue vv, int threadIndex)
    {
        try
        {
            int idx = Math.Min(threadIndex, vv.ThreadCount - 1);
            RawValue[] comps = vv.Values.Get(idx);
            if (comps == null) return "(null)";
            return $"({string.Join(", ", comps.Select(c => FormatRawValue(c, vv.Type)))})";
        }
        catch { return "?"; }
    }

    public static string FormatMatrix(MatrixValue mv, int threadIndex)
    {
        return $"[{mv.Rows}×{mv.Columns}]";
    }

    public static string FormatStruct(StructValue sv, int threadIndex)
    {
        try
        {
            var parts = sv.Members.Take(4).Select(kv => $"{kv.Key}: {Format(kv.Value, threadIndex)}");
            string body = string.Join(", ", parts);
            if (sv.Members.Count > 4) body += ", ...";
            return "{" + body + "}";
        }
        catch { return "{struct}"; }
    }

    public static string FormatRawValue(RawValue rv, ScalarType type)
    {
        if (HLSLTypeUtils.IsInt(type)) return rv.Int.ToString(CultureInfo.InvariantCulture);
        if (HLSLTypeUtils.IsUint(type)) return rv.Uint.ToString(CultureInfo.InvariantCulture);
        if (type == ScalarType.Double) return rv.Double.ToString("G6", CultureInfo.InvariantCulture);
        if (HLSLTypeUtils.IsFloat(type)) return rv.Float.ToString("G6", CultureInfo.InvariantCulture);
        if (type == ScalarType.Bool) return rv.Bool.ToString(CultureInfo.InvariantCulture);
        if (type == ScalarType.Char) return rv.Char.ToString(CultureInfo.InvariantCulture);
        return rv.Int.ToString(CultureInfo.InvariantCulture);
    }

    public static byte FloatToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    public static byte[] RenderPreviewImage(HLSLValue val, int wx, int wy)
    {
        int threadCount = wx * wy;
        if (val is VectorValue vv)
        {
            var asFloat = (VectorValue)vv.Cast(ScalarType.Float);
            int components = vv.Size;
            float peak = 1f;
            int rgbChannels = Math.Min(3, components);
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                for (int k = 0; k < rgbChannels; k++)
                    if (c[k].Float > peak) peak = c[k].Float;
            }
            float scale = 1f / peak;
            var rgba = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                rgba[i * 4 + 0] = FloatToByte((components > 0 ? c[0].Float : 0f) * scale);
                rgba[i * 4 + 1] = FloatToByte((components > 1 ? c[1].Float : 0f) * scale);
                rgba[i * 4 + 2] = FloatToByte((components > 2 ? c[2].Float : 0f) * scale);
                rgba[i * 4 + 3] = FloatToByte(components > 3 ? c[3].Float : 1f);
            }
            return rgba;
        }
        if (val is ScalarValue sv && sv.Type != ScalarType.Void)
        {
            float peak = 1f;
            for (int i = 0; i < threadCount; i++)
            {
                float v = sv.AsFloat(i);
                if (v > peak) peak = v;
            }
            float scale = 1f / peak;
            var rgba = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                byte gray = FloatToByte(sv.AsFloat(i) * scale);
                rgba[i * 4 + 0] = gray;
                rgba[i * 4 + 1] = gray;
                rgba[i * 4 + 2] = gray;
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }
        return null;
    }

    public static byte[] RenderOutputImage(HLSLValue result, int wx, int wy)
    {
        int threadCount = wx * wy;

        if (result is VectorValue vv)
        {
            int components = vv.Size;
            var pixels = new byte[threadCount * 4];
            var asFloat = (VectorValue)vv.Cast(ScalarType.Float);
            for (int i = 0; i < threadCount; i++)
            {
                var c = asFloat.Values.Get(i);
                pixels[i * 4 + 0] = FloatToByte(components > 0 ? c[0].Float : 0f);
                pixels[i * 4 + 1] = FloatToByte(components > 1 ? c[1].Float : 0f);
                pixels[i * 4 + 2] = FloatToByte(components > 2 ? c[2].Float : 0f);
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }
        if (result is ScalarValue sv && sv.Type != ScalarType.Void)
        {
            var pixels = new byte[threadCount * 4];
            for (int i = 0; i < threadCount; i++)
            {
                byte gray = FloatToByte(sv.AsFloat(i));
                pixels[i * 4 + 0] = gray;
                pixels[i * 4 + 1] = gray;
                pixels[i * 4 + 2] = gray;
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }
        return null;
    }

    // Resolves an identifier against a debug step into a hover-tooltip payload.
    public static HoverInfo BuildHoverInfo(
        TraceStep step, int warpX, int warpY, int inspectedThread, int debugVertexIndex, string identifier)
    {
        if (step == null) return null;

        HLSLValue value = null;
        if (step.FrameVariables.Length > 0 && step.FrameVariables[0].TryGetValue(identifier, out var local))
            value = local;
        else if (step.GlobalVariables.TryGetValue(identifier, out var global))
            value = global;
        if (value == null) return null;

        int wx = Math.Max(1, warpX);
        int wy = Math.Max(1, warpY);
        var resolved = value is ReferenceValue rv ? rv.Get() : value;

        byte[] rgba = null;
        try { rgba = RenderPreviewImage(resolved, wx, wy); }
        catch (Exception ex) { Console.WriteLine($"[hover] image render failed for '{identifier}': {ex.Message}"); }

        string[] perThread = null;
        if (debugVertexIndex >= 0)
            perThread = Enumerable.Range(0, wx * wy).Select(i => Format(resolved, i)).ToArray();

        return new HoverInfo
        {
            Value = Format(value, inspectedThread),
            Width = wx,
            Height = wy,
            Rgba = rgba,
            PerThreadValues = perThread,
            InspectedX = inspectedThread % wx,
            InspectedY = inspectedThread / wx,
        };
    }
}

public sealed class HoverInfo
{
    public string Value { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Rgba { get; set; }
    public int InspectedX { get; set; }
    public int InspectedY { get; set; }
    public string[] PerThreadValues { get; set; }
}
