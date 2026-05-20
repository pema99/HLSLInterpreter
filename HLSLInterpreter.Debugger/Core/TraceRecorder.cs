using HLSL;

namespace HLSLInterpreter.Debugger.Core;

// Records an ExecutionTrace by running the shader through ShaderExecutor with a
// before-statement hook that snapshots each step. Replaces DebuggerSession.Record.
public static class TraceRecorder
{
    public static ExecutionTrace Record(
        ShaderExecutor executor, HLSLRunner runner, ShaderProgram program, ShaderInvocation invocation)
    {
        var steps = new List<TraceStep>();
        var options = new ExecutionOptions
        {
            ObserveProgramLoad = true,
            BeforeStatement = e => steps.Add(BuildStep(e)),
        };
        var outcome = executor.Execute(runner, program, invocation, options);
        return new ExecutionTrace(steps, outcome.Output, outcome.HasError,
            outcome.ErrorMessage, outcome.Exception, outcome.Result);
    }

    private static TraceStep BuildStep(StatementEvent e)
    {
        var runner = e.Runner;
        return new TraceStep(
            Line: e.Node.Span.Start.Line,
            CallStack: runner.GetCallStack(),
            FrameVariables: runner.GetVariablesPerFrame().Select(CopyFrame).ToArray(),
            GlobalVariables: CopyFrame(runner.GetGlobalVariables()),
            OutputOffset: e.OutputLength,
            FrameThreadStates: runner.GetThreadStatesPerFrame());
    }

    private static Dictionary<string, HLSLValue> CopyFrame(Dictionary<string, HLSLValue> frame)
    {
        var copy = new Dictionary<string, HLSLValue>();
        foreach (var kvp in frame)
        {
            HLSLValue value = kvp.Value is ReferenceValue reference ? reference.Get() : kvp.Value;
            try { copy[kvp.Key] = value?.Copy() ?? value!; }
            catch { copy[kvp.Key] = value!; }
        }
        return copy;
    }
}
