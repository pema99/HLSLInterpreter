using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// Debug effects: recording an execution trace and evaluating an immediate-window
// expression in the scope of the current debug step.
public sealed partial class EffectRunner
{
    private async Task RecordTraceEffect(RecordTrace c, Action<Msg> dispatch)
    {
        try
        {
            var captured = c.Captured;
            if (c.SnapshotGpu)
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

            var parserConfig = ShaderInvocationBuilder.MakeParserConfig(c.DocPath);
            var invocation = await _invocationBuilder.BuildAsync(c.Config, captured, c.DebugVertexIndex);
            var program = ShaderProgram.FromSource(c.Code, parserConfig);
            var trace = TraceRecorder.Record(_executor, new HLSLRunner(), program, invocation);

            int wx = Math.Max(1, c.Config.WarpX);
            int wy = Math.Max(1, c.Config.WarpY);
            ShaderImage image = null;
            if (!trace.HasError && trace.Result != null)
            {
                var pixels = ValueImageRenderer.TryExtractImage(trace.Result, wx, wy);
                if (pixels != null)
                {
                    image = new ShaderImage(pixels, wx, wy);
                    await _canvas.SetPixels(pixels, wx, wy);
                }
            }
            dispatch(new DebugTraceRecorded(trace, c.Code, c.DocumentId, captured, image));
        }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task EvaluateImmediateEffect(EvaluateImmediate c, Action<Msg> dispatch)
    {
        var (value, error) = await EvaluateExpression(c);

        int wx = Math.Max(1, c.Config.WarpX);
        int wy = Math.Max(1, c.Config.WarpY);
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
            resultStr = HLSLValueFormatter.Format(value, c.InspectedThread);
            isError = false;
            var resolved = value is ReferenceValue rv ? rv.Get() : value;
            byte[] rgba = null;
            try { rgba = ValueImageRenderer.RenderVariableImage(resolved, wx, wy); }
            catch { }
            if (rgba != null)
            {
                try
                {
                    imageDataUrl = await _browser.RgbaToDataUrl(
                        rgba, wx, wy, c.InspectedThread % wx, c.InspectedThread / wx, 0.7);
                }
                catch { }
            }
        }

        dispatch(new ImmediateEvalFinished(new ImmediateEntry(c.Expression, resultStr, isError, imageDataUrl)));
        try { await _browser.ScrollImmediateToBottom(); } catch { }
    }

    // Re-runs the shader up to the target step and evaluates an expression in
    // that scope. The hook aborts the run once the target step is reached.
    private async Task<(HLSLValue Value, string Error)> EvaluateExpression(EvaluateImmediate c)
    {
        try
        {
            var parserConfig = ShaderInvocationBuilder.MakeParserConfig(c.DocPath);
            var invocation = await _invocationBuilder.BuildAsync(c.Config, c.Captured, c.DebugVertexIndex);
            var runner = new HLSLRunner(Math.Max(1, c.Config.WarpX), Math.Max(1, c.Config.WarpY));
            invocation.SetUniforms(runner);

            HLSLValue result = null;
            Exception evalError = null;
            int stepCount = 0;
            int target = c.StepIndex;
            runner.DebugHookBeforeStatement = _ =>
            {
                if (stepCount == target)
                {
                    try { result = runner.EvaluateExpression(c.Expression); }
                    catch (Exception ex) { evalError = ex; }
                    throw new OperationCanceledException();
                }
                stepCount++;
            };

            using (new ConsoleCapture())
            {
                bool cancelledInLoad = false;
                try { runner.ProcessCode(c.DebugCode, parserConfig); }
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
