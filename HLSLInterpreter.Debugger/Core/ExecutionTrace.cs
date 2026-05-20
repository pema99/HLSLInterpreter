using HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed record TraceStep(
    int Line,
    string[] CallStack,
    Dictionary<string, HLSLValue>[] FrameVariables,
    Dictionary<string, HLSLValue> GlobalVariables,
    int OutputOffset,
    HLSLExecutionState.ThreadState[][] FrameThreadStates)
{
    public int CallDepth => CallStack.Length;

    public HLSLExecutionState.ThreadState[] GetThreadStatesAt(int frameIndex)
    {
        if (FrameThreadStates.Length == 0) return Array.Empty<HLSLExecutionState.ThreadState>();
        int idx = Math.Clamp(frameIndex, 0, FrameThreadStates.Length - 1);
        return FrameThreadStates[idx];
    }
}

// An immutable recording of a shader run. The step cursor lives in DebugState,
// not here, so the trace can be shared freely.
public sealed record ExecutionTrace(
    IReadOnlyList<TraceStep> Steps,
    string Output,
    bool HasError,
    string ErrorMessage,
    Exception Exception,
    HLSLValue Result)
{
    public TraceStep StepAt(int index) =>
        index >= 0 && index < Steps.Count ? Steps[index] : null;

    public int LineAt(int index) => StepAt(index)?.Line ?? 0;

    public string OutputThrough(int index)
    {
        if (Output.Length == 0 || index < 0) return "";
        int endOffset = index < Steps.Count - 1
            ? Steps[index + 1].OutputOffset
            : Output.Length;
        return Output[..Math.Min(endOffset, Output.Length)];
    }
}
