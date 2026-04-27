using HLSL;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record ShaderInvocation(
    ShaderRenderMode Mode,
    string EntryPoint,
    string VertexEntryPoint,
    Mesh Mesh,
    int WarpX,
    int WarpY,
    int GroupOffsetX,
    int GroupOffsetY,
    int CanvasW,
    int CanvasH,
    float Time,
    float CameraYaw,
    float CameraPitch,
    float CameraDistance)
{
    public void SetUniforms(HLSLRunner runner)
    {
        runner.SetVariable("_WarpSize", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)WarpX, (float)WarpY])));
        runner.SetVariable("_Resolution", new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>([(float)CanvasW, (float)CanvasH])));
        runner.SetVariable("_Time", new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(Time)));
    }

    public HLSLValue Execute(HLSLRunner runner)
    {
        if (Mode == ShaderRenderMode.VertFrag)
        {
            return SoftwareRenderer.Render(
                runner, Mesh, VertexEntryPoint, EntryPoint,
                WarpX, WarpY, CanvasW, CanvasH,
                GroupOffsetX, GroupOffsetY,
                CameraYaw, CameraPitch, CameraDistance);
        }
        else
        {
            int threadCount = WarpX * WarpY;
            var threadIds = new RawValue[threadCount][];
            for (int i = 0; i < threadCount; i++)
                threadIds[i] = [
                    (float)(i % WarpX + GroupOffsetX * WarpX) + 0.5f,
                (float)(i / WarpX + GroupOffsetY * WarpY) + 0.5f,
                0.0f,
                1.0f,
            ];
            var threadArg = new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>(threadIds).Converge());
            try { return runner.CallFunction(EntryPoint, threadArg); }
            catch (Exception ex) when (ex.Message.Contains($"Unknown function '{EntryPoint}' called."))
            {
                return runner.CallFunction(EntryPoint);
            }
        }
    }
}
