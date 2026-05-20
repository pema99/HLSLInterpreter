using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Services;

public sealed record ImmediateResult(HLSLValue Value, string Error);

// Records and manages a debug session: trace recording, entering and exiting
// the session, and immediate-window expression evaluation. Replaces
// DebugController and DebuggerSession.Record.
public sealed class DebugSessionService
{
    private readonly AppStore _store;
    private readonly ShaderExecutor _executor;
    private readonly ShaderInvocationBuilder _invocationBuilder;
    private readonly ShaderRunService _runService;
    private readonly ICanvasInterop _canvas;

    private (int X, int Y)? _savedGroupOffsets;

    public DebugSessionService(
        AppStore store,
        ShaderExecutor executor,
        ShaderInvocationBuilder invocationBuilder,
        ShaderRunService runService,
        ICanvasInterop canvas)
    {
        _store = store;
        _executor = executor;
        _invocationBuilder = invocationBuilder;
        _runService = runService;
        _canvas = canvas;
    }

    // Click-to-debug at a pixel moves the group offset. Remember the original
    // so ExitSession can restore it.
    public void SaveGroupOffsetsForRestore()
    {
        var config = _store.State.Editor.ActiveDocument?.Config;
        if (config != null) _savedGroupOffsets = (config.GroupOffsetX, config.GroupOffsetY);
    }

    public async Task<bool> StartSessionAsync(string code, int documentId)
    {
        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);

        // BeginRun clears GpuCaptured, but the click that opened this debug
        // session stored the canvas size there. Keep it across the reset.
        var captured = _store.State.Run.GpuCaptured;
        _store.BeginRun();
        if (captured != null) _store.SetGpuCaptured(captured);
        await _runService.SnapshotGpuIfNeededAsync();
        await _runService.PauseGpuRendererAsync();
        RuntimeMemory.Reclaim();

        var parserConfig = _invocationBuilder.MakeParserConfig();
        var invocation = await _invocationBuilder.BuildAsync();
        var program = ShaderProgram.FromSource(code, parserConfig);
        var trace = TraceRecorder.Record(_executor, new HLSLRunner(), program, invocation);

        _store.SetRunOutput(trace.Output);
        _store.FinishRun();

        bool testFailure = trace.Exception is HLSLRunner.TestFailException;
        bool canDebug = !trace.HasError || (testFailure && trace.Steps.Count > 0);
        if (!canDebug)
        {
            _store.SetRunError(trace.ErrorMessage, trace.Exception);
            return false;
        }

        if (trace.HasError)
        {
            _store.SetRunError(trace.ErrorMessage, trace.Exception);
        }
        else if (trace.Result != null)
        {
            var pixels = ValueImageRenderer.TryExtractImage(trace.Result, wx, wy);
            if (pixels != null)
            {
                _store.SetRunImage(new ShaderImage(pixels, wx, wy));
                await _canvas.SetPixels(pixels, wx, wy);
            }
        }

        int stepIndex = trace.HasError ? TraceNavigator.End(trace) : 0;
        _store.StartDebug(trace, documentId, code, stepIndex);
        return true;
    }

    public void ExitSession()
    {
        _store.ExitDebug();
        _store.SetRunBackend(RunBackend.Cpu);
        if (_savedGroupOffsets is { } saved)
        {
            _store.SetGroupOffset(saved.X, saved.Y);
            _savedGroupOffsets = null;
        }
    }

    // Re-runs the shader up to the current step and evaluates an expression in
    // that scope. The hook aborts the run once the target step is reached.
    public async Task<ImmediateResult> EvaluateExpressionAsync(string expression)
    {
        var debug = _store.State.Debug;
        if (debug.Trace == null || debug.StepIndex < 0 || string.IsNullOrEmpty(debug.DebugCode))
            return new ImmediateResult(null, "(no active debug step)");

        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);

        try
        {
            var parserConfig = _invocationBuilder.MakeParserConfig();
            var invocation = await _invocationBuilder.BuildAsync();
            var runner = new HLSLRunner(wx, wy);
            invocation.SetUniforms(runner);

            HLSLValue result = null;
            Exception evalError = null;
            int stepCount = 0;
            int target = debug.StepIndex;
            runner.DebugHookBeforeStatement = _ =>
            {
                if (stepCount == target)
                {
                    try { result = runner.EvaluateExpression(expression); }
                    catch (Exception ex) { evalError = ex; }
                    throw new OperationCanceledException();
                }
                stepCount++;
            };

            using (new ConsoleCapture())
            {
                bool cancelledInLoad = false;
                try { runner.ProcessCode(debug.DebugCode, parserConfig); }
                catch (OperationCanceledException) { cancelledInLoad = true; }
                if (!cancelledInLoad)
                {
                    try { invocation.Execute(runner); }
                    catch (OperationCanceledException) { }
                }
            }

            if (evalError != null) return new ImmediateResult(null, evalError.Message);
            if (result == null)
                return new ImmediateResult(null, "(step not reached - expression may be after this point)");
            return new ImmediateResult(result, null);
        }
        catch (Exception ex)
        {
            return new ImmediateResult(null, ex.Message);
        }
    }
}
