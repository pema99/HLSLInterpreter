using HLSL;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public sealed class DebuggerSession
{
    public sealed record Step(
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

    public IReadOnlyList<Step> Trace { get; }
    public string Output { get; }
    public bool HasError { get; }
    public string? ErrorMessage { get; }
    public Exception? LastException { get; }
    public HLSLValue? Result { get; }

    private int _stepIndex = -1;
    public int StepIndex
    {
        get => _stepIndex;
        set => _stepIndex = Trace.Count == 0 ? -1 : Math.Clamp(value, 0, Trace.Count - 1);
    }

    public Step? Current => _stepIndex >= 0 && _stepIndex < Trace.Count ? Trace[_stepIndex] : null;
    public int CurrentLine => Current?.Line ?? 0;

    public string OutputThroughCurrentStep
    {
        get
        {
            if (Output.Length == 0 || _stepIndex < 0) return "";
            int endOffset = _stepIndex < Trace.Count - 1
                ? Trace[_stepIndex + 1].OutputOffset
                : Output.Length;
            return Output[..Math.Min(endOffset, Output.Length)];
        }
    }

    private DebuggerSession(List<Step> trace, string output, bool hasError, string? errorMessage, Exception? lastException, HLSLValue? result)
    {
        Trace = trace;
        Output = output;
        HasError = hasError;
        ErrorMessage = errorMessage;
        LastException = lastException;
        Result = result;
        if (trace.Count > 0) _stepIndex = 0;
    }

    public static DebuggerSession Record(
        string code,
        int warpX, int warpY,
        ShaderInvocation invocation,
        HLSLParserConfig parserConfig)
    {
        var trace = new List<Step>();
        var sw = new StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);

        bool hasError = false;
        string? errorMessage = null;
        Exception? lastException = null;
        HLSLValue? result = null;

        var runner = new HLSLRunner();

        try
        {
            int wx = Math.Max(1, warpX);
            int wy = Math.Max(1, warpY);

            runner.Reset();
            runner.SetWarpSize(wx, wy);
            invocation.SetUniforms(runner);

            runner.DebugHook = node =>
            {
                var frameCopies = runner.GetVariablesPerFrame().Select(CopyFrame).ToArray();
                var globalCopy = CopyFrame(runner.GetGlobalVariables());

                trace.Add(new Step(
                    Line: node.Span.Start.Line,
                    CallStack: runner.GetCallStack(),
                    FrameVariables: frameCopies,
                    GlobalVariables: globalCopy,
                    OutputOffset: sw.GetStringBuilder().Length,
                    FrameThreadStates: runner.GetThreadStatesPerFrame()));
            };

            runner.ProcessCode(code, parserConfig, out var diagnostics, out _);
            var compileErrors = diagnostics.Where(d => (d.Kind & DiagnosticFlags.OnlyErrors) != 0).ToList();
            if (compileErrors.Count > 0)
            {
                hasError = true;
                errorMessage = string.Join("\n", compileErrors.Select(d => $"Line {d.Location.Line}, col {d.Location.Column}: {d.Text}"));
            }
            else
            {
                result = invocation.Execute(runner);
            }
        }
        catch (Exception ex)
        {
            hasError = true;
            errorMessage = ex.Message;
            lastException = ex;
        }
        finally
        {
            runner.DebugHook = null;
            Console.SetOut(oldOut);
        }

        return new DebuggerSession(trace, sw.ToString(), hasError, errorMessage, lastException, result);
    }

    private static Dictionary<string, HLSLValue> CopyFrame(Dictionary<string, HLSLValue> frame)
    {
        var copy = new Dictionary<string, HLSLValue>();
        foreach (var kvp in frame)
        {
            HLSLValue val = kvp.Value is ReferenceValue rv ? rv.Get() : kvp.Value;
            try { copy[kvp.Key] = val?.Copy() ?? val!; }
            catch { copy[kvp.Key] = val!; }
        }
        return copy;
    }

    public void SeekToStart() { if (Trace.Count > 0) _stepIndex = 0; }
    public void SeekToEnd() { if (Trace.Count > 0) _stepIndex = Trace.Count - 1; }

    public void StepForward()
    {
        if (_stepIndex < Trace.Count - 1) _stepIndex++;
    }

    public void StepBack()
    {
        if (_stepIndex > 0) _stepIndex--;
    }

    public void StepOver()
    {
        if (_stepIndex < 0 || _stepIndex >= Trace.Count - 1) return;
        int depth = Trace[_stepIndex].CallDepth;
        int next = _stepIndex + 1;
        if (depth > 0)
        {
            while (next < Trace.Count - 1 && Trace[next].CallDepth > depth)
                next++;
        }
        _stepIndex = next;
    }

    public void StepOut()
    {
        if (_stepIndex < 0 || _stepIndex >= Trace.Count - 1) return;
        int depth = Trace[_stepIndex].CallDepth;
        int next = _stepIndex + 1;
        while (next < Trace.Count - 1 && Trace[next].CallDepth >= depth)
            next++;
        _stepIndex = next;
    }

    public void StepOverBack()
    {
        if (_stepIndex <= 0) return;
        int depth = Trace[_stepIndex].CallDepth;
        int prev = _stepIndex - 1;
        while (prev > 0 && Trace[prev].CallDepth > depth)
            prev--;
        _stepIndex = prev;
    }

    public void StepOutBack()
    {
        if (_stepIndex <= 0) return;
        int depth = Trace[_stepIndex].CallDepth;
        int prev = _stepIndex - 1;
        while (prev > 0 && Trace[prev].CallDepth >= depth)
            prev--;
        _stepIndex = prev;
    }

    public void ContinueToBreakpoint(IReadOnlySet<int> breakpoints)
    {
        int next = _stepIndex + 1;
        while (next < Trace.Count && !breakpoints.Contains(Trace[next].Line))
            next++;
        _stepIndex = Math.Min(next, Trace.Count - 1);
    }

    public void ContinueToBreakpointBack(IReadOnlySet<int> breakpoints)
    {
        int prev = _stepIndex - 1;
        while (prev > 0 && !breakpoints.Contains(Trace[prev].Line))
            prev--;
        _stepIndex = Math.Max(prev, 0);
    }

}
