using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.Mvu;

// Debug effects: recording an execution trace and evaluating an immediate-window
// expression in the scope of the current debug step.
public sealed partial class DebuggerEffects
{
    public Cmd RecordTrace(
        string code, ShaderConfig config, FrameCapture captured, bool snapshotGpu,
        int debugVertexIndex, int documentId, string docPath) =>
        Cmd.OfEffect((dispatch, _) =>
            RecordTraceImpl(code, config, captured, snapshotGpu, debugVertexIndex, documentId, docPath, dispatch));

    // A canvas click in GPU mode: snapshot the live frame, pause the preview,
    // and start the debug session via the message `make` builds. CPU mode needs
    // no effect, so update maps that click straight to a message.
    public Cmd SnapshotGpuFrame(Func<float, int, int, Msg> make) =>
        Cmd.OfEffect(async dispatch =>
        {
            var snap = await _gpu.Snapshot();
            if (snap == null || snap.Length < 3) return;
            await _gpu.Pause();
            dispatch(make(snap[0], (int)snap[1], (int)snap[2]));
        });

    public Cmd EvaluateImmediate(
        string expression, string debugCode, int stepIndex, ShaderConfig config,
        FrameCapture captured, int inspectedThread, int debugVertexIndex, string docPath) =>
        Cmd.OfEffect((dispatch, _) =>
            EvaluateImmediateImpl(
                expression, debugCode, stepIndex, config, captured, inspectedThread, debugVertexIndex, docPath, dispatch));

    private async Task RecordTraceImpl(
        string code, ShaderConfig config, FrameCapture captured, bool snapshotGpu,
        int debugVertexIndex, int documentId, string docPath, Action<Msg> dispatch)
    {
        try
        {
            if (snapshotGpu)
            {
                try
                {
                    var snap = await _gpu.Snapshot();
                    if (snap != null && snap.Length >= 3 && snap[1] > 0 && snap[2] > 0)
                        captured = new FrameCapture(snap[0], (int)snap[1], (int)snap[2]);
                }
                catch { }
            }
            try { await _gpu.Pause(); } catch { }
            RuntimeMemory.Reclaim();

            var parserConfig = ShaderInvocationBuilder.MakeParserConfig(docPath);
            var invocation = await _invocationBuilder.BuildAsync(config, captured, debugVertexIndex);
            var program = ShaderProgram.FromSource(code, parserConfig);
            var trace = TraceRecorder.Record(_executor, new HLSLRunner(), program, invocation);

            int wx = Math.Max(1, config.WarpX);
            int wy = Math.Max(1, config.WarpY);
            ShaderImage image = null;
            if (!trace.HasError && trace.Result != null)
            {
                var pixels = HLSLValueDisplay.RenderOutputImage(trace.Result, wx, wy);
                if (pixels != null) image = new ShaderImage(pixels, wx, wy);
            }
            dispatch(new DebugTraceRecorded(trace, code, documentId, captured, image));
        }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task EvaluateImmediateImpl(
        string expression, string debugCode, int stepIndex, ShaderConfig config,
        FrameCapture captured, int inspectedThread, int debugVertexIndex, string docPath, Action<Msg> dispatch)
    {
        var (value, error) = await EvaluateExpression(
            expression, debugCode, stepIndex, config, captured, debugVertexIndex, docPath);

        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        string resultStr;
        bool isError;
        string imageDataUrl = null;
        if (error != null)
        {
            resultStr = error;
            isError = true;
        }
        else
        {
            resultStr = HLSLValueDisplay.Format(value, inspectedThread);
            isError = false;
            var resolved = value is ReferenceValue rv ? rv.Get() : value;
            byte[] rgba = null;
            try { rgba = HLSLValueDisplay.RenderPreviewImage(resolved, wx, wy); }
            catch { }
            if (rgba != null)
            {
                try
                {
                    imageDataUrl = await _browser.RgbaToDataUrl(
                        rgba, wx, wy, inspectedThread % wx, inspectedThread / wx, 0.7);
                }
                catch { }
            }
        }

        dispatch(new ImmediateEvalFinished(new ImmediateEntry(expression, resultStr, isError, imageDataUrl)));
        try { await _browser.ScrollImmediateToBottom(); } catch { }
    }

    // Re-runs the shader up to the target step and evaluates an expression in
    // that scope. The hook aborts the run once the target step is reached.
    private async Task<(HLSLValue Value, string Error)> EvaluateExpression(
        string expression, string debugCode, int stepIndex, ShaderConfig config,
        FrameCapture captured, int debugVertexIndex, string docPath)
    {
        try
        {
            var parserConfig = ShaderInvocationBuilder.MakeParserConfig(docPath);
            var invocation = await _invocationBuilder.BuildAsync(config, captured, debugVertexIndex);
            var runner = new HLSLRunner(Math.Max(1, config.WarpX), Math.Max(1, config.WarpY));
            invocation.SetUniforms(runner);

            HLSLValue result = null;
            Exception evalError = null;
            int stepCount = 0;
            runner.DebugHookBeforeStatement = _ =>
            {
                if (stepCount == stepIndex)
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
                try { runner.ProcessCode(debugCode, parserConfig); }
                catch (OperationCanceledException) { cancelledInLoad = true; }
                if (!cancelledInLoad)
                {
                    try { invocation.Execute(runner); }
                    catch (OperationCanceledException) { }
                }
            }

            if (evalError != null) return (null, evalError.Message);
            if (result == null)
                return (null, "(step not reached - expression may be after this point)");
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
