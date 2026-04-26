using HLSL;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

internal static class DebugDispatch
{
    public static VectorValue BuildThreadArg(int wx, int wy, int threadCount, int groupOffsetX = 0, int groupOffsetY = 0)
    {
        var threadIds = new RawValue[threadCount][];
        for (int i = 0; i < threadCount; i++)
            threadIds[i] = new RawValue[] {
                (float)(i % wx + groupOffsetX * wx) + 0.5f,
                (float)(i / wx + groupOffsetY * wy) + 0.5f,
                0.0f,
                1.0f
            };
        return new VectorValue(ScalarType.Float, new HLSLRegister<RawValue[]>(threadIds).Converge());
    }

    public static HLSLValue CallEntryPoint(HLSLRunner runner, VectorValue threadArg, string entryPoint)
    {
        try
        {
            return runner.CallFunction(entryPoint, threadArg);
        }
        catch (Exception ex) when (ex.Message.Contains($"Unknown function '{entryPoint}' called."))
        {
            return runner.CallFunction(entryPoint);
        }
    }
}
