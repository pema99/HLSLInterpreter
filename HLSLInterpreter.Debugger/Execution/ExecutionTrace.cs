using HLSL;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Execution;

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

// An immutable recording of a shader run. The step cursor lives in DebugState.
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


// Records an execution trace
public static class TraceRecorder
{
    public static ExecutionTrace Record(
        HLSLRunner runner, ShaderProgram program, ShaderInvocation invocation)
    {
        var steps = new List<TraceStep>();
        var options = new ExecutionOptions
        {
            ObserveProgramLoad = true,
            BeforeStatement = (node, outputLength) => steps.Add(BuildStep(node, runner, outputLength)),
        };
        var outcome = ShaderExecutor.Execute(runner, program, invocation, options);
        return new ExecutionTrace(steps, outcome.Output, outcome.HasError,
            outcome.ErrorMessage, outcome.Exception, outcome.Result);
    }

    private static TraceStep BuildStep(HLSLSyntaxNode node, HLSLRunner runner, int outputLength)
    {
        return new TraceStep(
            Line: node.Span.Start.Line,
            CallStack: runner.GetCallStack(),
            FrameVariables: runner.GetVariablesPerFrame().Select(CopyFrame).ToArray(),
            GlobalVariables: CopyFrame(runner.GetGlobalVariables()),
            OutputOffset: outputLength,
            FrameThreadStates: runner.GetThreadStatesPerFrame());
    }

    private static Dictionary<string, HLSLValue> CopyFrame(Dictionary<string, HLSLValue> frame)
    {
        var copy = new Dictionary<string, HLSLValue>();
        foreach (var kvp in frame)
        {
            HLSLValue value = kvp.Value is ReferenceValue reference ? reference.Get() : kvp.Value;
            try { copy[kvp.Key] = value?.Copy() ?? value!; }
            catch (Exception ex)
            {
                Console.WriteLine($"[trace] copy of '{kvp.Key}' failed: {ex.Message}");
                copy[kvp.Key] = value!;
            }
        }
        return copy;
    }
}


// Stepping/cursor math for navigating a trace
public static class TraceNavigator
{
    public static int Clamp(ExecutionTrace trace, int index) =>
        trace.Steps.Count == 0 ? -1 : Math.Clamp(index, 0, trace.Steps.Count - 1);

    public static int End(ExecutionTrace trace) => trace.Steps.Count - 1;

    public static int Forward(ExecutionTrace trace, int index) =>
        index < trace.Steps.Count - 1 ? index + 1 : index;

    public static int Back(ExecutionTrace trace, int index) =>
        index > 0 ? index - 1 : index;

    public static int Over(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index < 0 || index >= steps.Count - 1) return index;
        int depth = steps[index].CallDepth;
        int next = index + 1;
        if (depth > 0)
            while (next < steps.Count - 1 && steps[next].CallDepth > depth)
                next++;
        return next;
    }

    public static int Out(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index < 0 || index >= steps.Count - 1) return index;
        int depth = steps[index].CallDepth;
        int next = index + 1;
        while (next < steps.Count - 1 && steps[next].CallDepth >= depth)
            next++;
        return next;
    }

    public static int OverBack(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index <= 0) return index;
        int depth = steps[index].CallDepth;
        int prev = index - 1;
        while (prev > 0 && steps[prev].CallDepth > depth)
            prev--;
        return prev;
    }

    public static int OutBack(ExecutionTrace trace, int index)
    {
        var steps = trace.Steps;
        if (index <= 0) return index;
        int depth = steps[index].CallDepth;
        int prev = index - 1;
        while (prev > 0 && steps[prev].CallDepth >= depth)
            prev--;
        return prev;
    }

    public static int ToBreakpoint(ExecutionTrace trace, int index, IReadOnlySet<int> breakpoints)
    {
        var steps = trace.Steps;
        int next = index + 1;
        while (next < steps.Count && !breakpoints.Contains(steps[next].Line))
            next++;
        return Math.Min(next, steps.Count - 1);
    }

    public static int ToBreakpointBack(ExecutionTrace trace, int index, IReadOnlySet<int> breakpoints)
    {
        var steps = trace.Steps;
        int prev = index - 1;
        while (prev > 0 && !breakpoints.Contains(steps[prev].Line))
            prev--;
        return Math.Max(prev, 0);
    }
}
