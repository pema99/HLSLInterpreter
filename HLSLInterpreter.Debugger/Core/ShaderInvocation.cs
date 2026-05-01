using HLSL;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record ShaderInvocation(
    ShaderRenderMode Mode,
    string FragmentEntryPoint,
    string VertexEntryPoint,
    Mesh Mesh,
    int WarpX,
    int WarpY,
    int GroupOffsetX,
    int GroupOffsetY,
    int CanvasW,
    int CanvasH,
    float Time,
    float[] ViewProjection,
    float[] Mouse,
    int DebugVertexIndex)
{
    public void SetUniforms(HLSLRunner runner)
    {
        runner.SetVariable("_WarpSize", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)WarpX, (float)WarpY])));
        runner.SetVariable("_Resolution", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)CanvasW, (float)CanvasH])));
        runner.SetVariable("_Time", new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(Time)));
        runner.SetVariable("_Mouse", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([Mouse[0], Mouse[1], Mouse[2], Mouse[3]])));
        if (Mode == ShaderRenderMode.VertFrag && ViewProjection != null)
        {
            var raws = new RawValue[16];
            for (int i = 0; i < 16; i++)
            {
                raws[i] = ViewProjection[i];
            }
            runner.SetVariable("_ViewProjection", new MatrixValue(ScalarType.Float, 4, 4, new HLSLRegister<RawValue[]>(raws)));
        }
    }

    public HLSLValue Execute(HLSLRunner runner)
    {
        int threadCount = WarpX * WarpY;
        if (Mode == ShaderRenderMode.VertFrag)
        {
            if (DebugVertexIndex >= 0)
            {
                int batchStart = (DebugVertexIndex / threadCount) * threadCount;
                int batchSize = Math.Min(threadCount, Mesh.VertexCount - batchStart);
                return SoftwareRenderer.RunVertOnly(runner, Mesh, VertexEntryPoint, WarpX, WarpY, batchStart, batchSize)[0];
            }
            else
            {
                return SoftwareRenderer.RunVertFrag(
                    runner, Mesh, VertexEntryPoint, FragmentEntryPoint,
                    WarpX, WarpY, CanvasW, CanvasH,
                    GroupOffsetX, GroupOffsetY);
            }
        }
        else
        {
            var fragFunc = runner.GetFunction(FragmentEntryPoint) ?? throw new InvalidOperationException($"Fragment function '{FragmentEntryPoint}' not found.");
            var fragArgs = ShaderReflection.BuildArgs(runner, fragFunc, (type, semantic, dim, modifiers) =>
            {
                ScalarType scalarType = ShaderReflection.GetScalarType(type);
                bool isPosition = semantic.Base == "SV_POSITION";

                var perThread = new RawValue[threadCount][];
                for (int threadIdx = 0; threadIdx < threadCount; threadIdx++)
                {
                    var row = new RawValue[dim];
                    if (isPosition)
                    {
                        if (dim > 0) row[0] = (float)(threadIdx % WarpX + GroupOffsetX * WarpX) + 0.5f;
                        if (dim > 1) row[1] = (float)(threadIdx / WarpX + GroupOffsetY * WarpY) + 0.5f;
                        if (dim > 2) row[2] = 0f;
                        if (dim > 3) row[3] = 1f;
                    }
                    perThread[threadIdx] = row;
                }

                if (dim == 1)
                {
                    var scalars = new RawValue[threadCount];
                    for (int threadIdx = 0; threadIdx < threadCount; threadIdx++)
                        scalars[threadIdx] = perThread[threadIdx][0];
                    return new ScalarValue(scalarType, HLSLValueUtils.MakeScalarVGPR(scalars));
                }
                return new VectorValue(scalarType, HLSLValueUtils.MakeVectorVGPR(perThread));
            });
            return runner.CallFunction(FragmentEntryPoint, fragArgs);
        }
    }
}
