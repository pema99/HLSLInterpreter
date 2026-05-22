using System.Runtime;
using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

// Builds the Cmds that run, trace, and evaluate shaders, and drive the GPU preview.
public sealed class DebuggerExecutionEngine
{
    private readonly HLSLRunner _hlslRunner = new();

    // Forces a GC between runs, since interpreting a full frame allocates heavily!
    private static void ReclaimMemory()
    {
        if (OperatingSystem.IsBrowser())
        {
            GC.Collect();
            return;
        }
        var previous = GCSettings.LargeObjectHeapCompactionMode;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GCSettings.LargeObjectHeapCompactionMode = previous;
    }

    private async Task<ShaderInvocation> BuildAsync(ShaderConfig config, FrameCapture captured, int debugVertexIndex)
    {
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        int canvasW = captured?.CanvasW ?? wx;
        int canvasH = captured?.CanvasH ?? wy;

        float[] view = null;
        float[] projection = null;
        if (config.RenderMode == ShaderRenderMode.VertFrag)
        {
            view = await GpuInterop.View();
            projection = await GpuInterop.Projection(canvasW, canvasH);
        }

        float[] mouse;
        try { mouse = await GpuInterop.Mouse(); }
        catch { mouse = new float[] { 0f, 0f, 0f, 0f }; }

        return new ShaderInvocation(
            Mode: config.RenderMode,
            FragmentEntryPoint: config.FragmentEntryPoint,
            VertexEntryPoint: config.VertexEntryPoint,
            Mesh: config.Mesh,
            WarpX: wx,
            WarpY: wy,
            GroupOffsetX: config.GroupOffsetX,
            GroupOffsetY: config.GroupOffsetY,
            CanvasW: canvasW,
            CanvasH: canvasH,
            Time: captured?.Time ?? 0f,
            View: view,
            Projection: projection,
            Mouse: mouse,
            DebugVertexIndex: debugVertexIndex,
            Textures: config.Textures,
            Samplers: config.Samplers);
    }

    private static HLSLParserConfig MakeParserConfig(string docPath) =>
        new HLSLParserConfig
        {
            BasePath = docPath != null ? System.IO.Path.GetDirectoryName(docPath) ?? "" : "",
        };

    private CancellationTokenSource _runCts;

    public Cmd RunCpu(string code, ShaderConfig config, string docPath, int canvasW, int canvasH) =>
        Cmd.OfEffect(async (dispatch, ct) =>
        {
            var cts = BeginRun();
            try { await GpuInterop.Stop(); } catch { }
            ReclaimMemory();

            var parserConfig = MakeParserConfig(docPath);
            int wx = Math.Max(1, config.WarpX);
            int wy = Math.Max(1, config.WarpY);

            if (config.CpuMode != CpuMode.SingleWarp)
            {
                _ = RunCpuFullFrame(code, config, parserConfig, wx, wy, canvasW, canvasH, dispatch, cts);
                return;
            }

            try
            {
                var invocation = await BuildAsync(config, null, -1);
                var program = ShaderProgram.FromSource(code, parserConfig);
                var outcome = ShaderExecutor.Execute(_hlslRunner, program, invocation, ExecutionOptions.None);

                if (outcome.HasError)
                {
                    dispatch(new RunFinished(
                        outcome.Output, new RunError(outcome.ErrorMessage, outcome.Exception), null, null));
                    return;
                }
                var pixels = HLSLValueDisplay.RenderOutputImage(outcome.Result, wx, wy);
                var image = pixels != null ? new ShaderImage(pixels, wx, wy) : null;
                dispatch(new RunFinished(outcome.Output, null, image, null));
            }
            catch (Exception ex)
            {
                dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
            }
        });

    public Cmd RunGpu(string code, ShaderConfig config, float initialTime, bool paused, string docPath) =>
        Cmd.OfEffect(async (dispatch, _) =>
        {
            BeginRun();
            try { await GpuInterop.Stop(); } catch { }
            ReclaimMemory();

            if (!await GpuInterop.IsAvailable())
            {
                dispatch(new RunFinished("", new RunError(
                    "WebGPU is not available in this browser. Use Debug to step through on the CPU interpreter instead.",
                    null), null, null));
                return;
            }
            try
            {
                int wx = Math.Max(1, config.WarpX);
                int wy = Math.Max(1, config.WarpY);
                var parserConfig = MakeParserConfig(docPath);
                var assembled = ShaderReflection.AssembleVertexShader(
                    code, config.VertexEntryPoint, config.FragmentEntryPoint, config.RenderMode, parserConfig);
                string mode = config.RenderMode == ShaderRenderMode.VertFrag ? "vertfrag" : "pixel";
                float[] meshVertices = null;
                uint[] meshIndices = null;
                if (config.RenderMode == ShaderRenderMode.VertFrag)
                {
                    meshVertices = config.Mesh.GetInterleavedVertices();
                    meshIndices = config.Mesh.Indices;
                }
                await GpuInterop.Render(new GpuRenderRequest(
                    CanvasId: "color-canvas-gpu",
                    Source: assembled.Source,
                    FragmentEntryPoint: config.FragmentEntryPoint,
                    WarpX: wx,
                    WarpY: wy,
                    Mode: mode,
                    VertexEntryPoint: assembled.VertexEntry,
                    VertexInputs: assembled.VertexInputs,
                    MeshVertices: meshVertices,
                    MeshIndices: meshIndices,
                    Time: initialTime,
                    Textures: config.Textures,
                    Samplers: config.Samplers));
                if (paused)
                {
                    try { await GpuInterop.Pause(); } catch { }
                }
                dispatch(new RunFinished("", null, null, null));
            }
            catch (Exception ex)
            {
                dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
            }
        });

    public Cmd RenderViewMode(DebugViewMode mode, ExecutionMetrics metrics, ShaderImage image) =>
        Cmd.OfTask(async () =>
        {
            if (mode != DebugViewMode.Color && metrics != null)
            {
                var pixels = metrics.Render(mode);
                if (pixels != null) await CanvasInterop.SetPixels(pixels, metrics.CanvasW, metrics.CanvasH);
            }
            else if (image != null)
            {
                await CanvasInterop.SetPixels(image.Pixels, image.Width, image.Height);
            }
        });

    public Cmd SetGpuPaused(bool paused) =>
        Cmd.OfTask(async () =>
        {
            try
            {
                if (paused) await GpuInterop.Pause();
                else await GpuInterop.Resume();
            }
            catch { }
        });

    public Cmd CancelRun() =>
        Cmd.OfTask(() => { _runCts?.Cancel(); return Task.CompletedTask; });

    private CancellationTokenSource BeginRun()
    {
        var previous = _runCts;
        _runCts = new CancellationTokenSource();
        previous?.Cancel();
        return _runCts;
    }

    private async Task RunCpuFullFrame(
        string code, ShaderConfig config, HLSLParserConfig parserConfig,
        int wx, int wy, int canvasW, int canvasH, Action<Msg> dispatch, CancellationTokenSource cts)
    {
        try
        {
            if (canvasW <= 0) canvasW = Math.Max(wx, 256);
            if (canvasH <= 0) canvasH = Math.Max(wy, 256);
            var invocation = (await BuildAsync(config, null, -1))
                with { CanvasW = canvasW, CanvasH = canvasH };
            if (config.RenderMode == ShaderRenderMode.VertFrag)
                invocation = invocation with { Projection = await GpuInterop.Projection(canvasW, canvasH) };

            int tilesX = (canvasW + wx - 1) / wx;
            int tilesY = (canvasH + wy - 1) / wy;

            var fullPixels = new byte[canvasW * canvasH * 4];
            for (int i = 3; i < fullPixels.Length; i += 4) fullPixels[i] = 255;
            await CanvasInterop.AllocPixels(canvasW, canvasH);

            var metrics = config.CpuMode == CpuMode.FullFrameWithMetrics
                ? new ExecutionMetrics(canvasW, canvasH, wx, wy)
                : null;

            dispatch(new RunBecameCancellable());

            RunOutcome tileError;
            string output;
            using (var capture = new ConsoleCapture())
            {
                tileError = OperatingSystem.IsBrowser()
                    ? await RunTilesSerial(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics, cts)
                    : await RunTilesParallel(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics, cts);
                output = capture.ToString();
            }

            if (tileError != null)
            {
                dispatch(new RunFinished(output, new RunError(tileError.ErrorMessage, tileError.Exception), null, null));
                return;
            }
            dispatch(new RunFinished(output, null, new ShaderImage(fullPixels, canvasW, canvasH), metrics));
        }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    // Each tile re-visits the AST after a fresh Reset so interpreter state cannot leak between warps.
    private async Task<RunOutcome> RunTilesSerial(
        string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY,
        byte[] fullPixels, ExecutionMetrics metrics, CancellationTokenSource cts)
    {
        var program = ShaderProgram.FromParsedNodes(ShaderProgram.Parse(code, parserConfig));
        var runner = new HLSLRunner();
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (cts.IsCancellationRequested) return null;
                var outcome = RenderTile(runner, program, invocation, tx, ty, metrics);
                if (outcome.HasError) return outcome;
                var tilePixels = HLSLValueDisplay.RenderOutputImage(outcome.Result, wx, wy);
                if (tilePixels == null) continue;
                BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
                await CanvasInterop.SetPixelsRect(tilePixels, tx * wx, ty * wy, wx, wy);
                await Task.Yield();
            }
        }
        return null;
    }

    private async Task<RunOutcome> RunTilesParallel(
        string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY,
        byte[] fullPixels, ExecutionMetrics metrics, CancellationTokenSource cts)
    {
        var workQueue = Channel.CreateUnbounded<(int tx, int ty)>();
        for (int ty = 0; ty < tilesY; ty++)
            for (int tx = 0; tx < tilesX; tx++)
                workQueue.Writer.TryWrite((tx, ty));
        workQueue.Writer.Complete();

        var results = Channel.CreateUnbounded<(int tx, int ty, RunOutcome outcome)>();
        int workerCount = Math.Max(1, Environment.ProcessorCount - 1);
        var workers = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            workers[w] = Task.Run(async () =>
            {
                // Each worker owns its runner and AST copy to avoid cross-thread state.
                var runner = new HLSLRunner();
                var program = ShaderProgram.FromParsedNodes(ShaderProgram.Parse(code, parserConfig));
                await foreach (var (tx, ty) in workQueue.Reader.ReadAllAsync())
                {
                    if (cts.IsCancellationRequested) break;
                    var outcome = RenderTile(runner, program, invocation, tx, ty, metrics);
                    await results.Writer.WriteAsync((tx, ty, outcome));
                }
            });
        }
        var allWorkers = Task.WhenAll(workers);
        _ = allWorkers.ContinueWith(_ => results.Writer.Complete());

        RunOutcome error = null;
        await foreach (var (tx, ty, outcome) in results.Reader.ReadAllAsync())
        {
            if (outcome.HasError)
            {
                if (error == null) { error = outcome; cts.Cancel(); }
                continue;
            }
            if (cts.IsCancellationRequested) continue;
            var tilePixels = HLSLValueDisplay.RenderOutputImage(outcome.Result, wx, wy);
            if (tilePixels == null) continue;
            BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
            await CanvasInterop.SetPixelsRect(tilePixels, tx * wx, ty * wy, wx, wy);
        }
        await allWorkers;
        return error;
    }

    private RunOutcome RenderTile(
        HLSLRunner runner, ShaderProgram program, ShaderInvocation invocation,
        int tx, int ty, ExecutionMetrics metrics)
    {
        var tileInvocation = invocation with { GroupOffsetX = tx, GroupOffsetY = ty };
        var options = new ExecutionOptions { CaptureConsole = false };
        if (metrics != null)
        {
            var before = metrics.MakeBeforeStatementHook(runner, tx, ty);
            var after = metrics.MakeAfterStatementHook(runner, tx, ty);
            options = new ExecutionOptions
            {
                CaptureConsole = false,
                BeforeStatement = (node, _) => before(node),
                AfterStatement = (node, _) => after(node),
            };
            int threadCount = metrics.WarpX * metrics.WarpY;
            int warpW = metrics.WarpX, warpH = metrics.WarpY;
            int canvasW = metrics.CanvasW, canvasH = metrics.CanvasH;
            tileInvocation = tileInvocation with
            {
                OnTextureFetch = () =>
                {
                    var state = runner.GetExecutionState();
                    for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                    {
                        if (!state.IsThreadActive(threadIndex)) continue;
                        int px = tx * warpW + (threadIndex % warpW);
                        int py = ty * warpH + (threadIndex / warpW);
                        if (px < canvasW && py < canvasH)
                            metrics.PixelFetches[py * canvasW + px]++;
                    }
                }
            };
        }
        return ShaderExecutor.Execute(runner, program, tileInvocation, options);
    }

    private static void BlitTile(
        byte[] tile, int x0, int y0, int wx, int wy, int canvasW, int canvasH, byte[] full)
    {
        int copyH = Math.Min(wy, canvasH - y0);
        int copyW = Math.Min(wx, canvasW - x0);
        if (copyW <= 0 || copyH <= 0) return;
        for (int row = 0; row < copyH; row++)
        {
            int srcOffset = row * wx * 4;
            int dstOffset = ((y0 + row) * canvasW + x0) * 4;
            Buffer.BlockCopy(tile, srcOffset, full, dstOffset, copyW * 4);
        }
    }

    public Cmd RecordTrace(
        string code, ShaderConfig config, FrameCapture captured, bool snapshotGpu,
        int debugVertexIndex, int documentId, string docPath) =>
        Cmd.OfEffect(async (dispatch, _) =>
        {
            try
            {
                if (snapshotGpu)
                {
                    try
                    {
                        var snap = await GpuInterop.Snapshot();
                        if (snap != null && snap.Length >= 3 && snap[1] > 0 && snap[2] > 0)
                            captured = new FrameCapture(snap[0], (int)snap[1], (int)snap[2]);
                    }
                    catch { }
                }
                try { await GpuInterop.Pause(); } catch { }
                ReclaimMemory();

                var parserConfig = MakeParserConfig(docPath);
                var invocation = await BuildAsync(config, captured, debugVertexIndex);
                var program = ShaderProgram.FromSource(code, parserConfig);
                var trace = TraceRecorder.Record(new HLSLRunner(), program, invocation);

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
        });

    // Snapshots the live GPU frame and pauses the preview, then debug
    public Cmd SnapshotGpuFrame(Func<float, int, int, Msg> make) =>
        Cmd.OfEffect(async dispatch =>
        {
            var snap = await GpuInterop.Snapshot();
            if (snap == null || snap.Length < 3) return;
            await GpuInterop.Pause();
            dispatch(make(snap[0], (int)snap[1], (int)snap[2]));
        });

    public Cmd EvaluateImmediate(
        string expression, string debugCode, int stepIndex, ShaderConfig config,
        FrameCapture captured, int inspectedThread, int debugVertexIndex, string docPath) =>
        Cmd.OfEffect(async (dispatch, _) =>
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
                        imageDataUrl = await BrowserInterop.RgbaToDataUrl(
                            rgba, wx, wy, inspectedThread % wx, inspectedThread / wx, 0.7);
                    }
                    catch { }
                }
            }

            dispatch(new ImmediateEvalFinished(new ImmediateEntry(expression, resultStr, isError, imageDataUrl)));
            try { await BrowserInterop.ScrollImmediateToBottom(); } catch { }
        });

    private async Task<(HLSLValue Value, string Error)> EvaluateExpression(
        string expression, string debugCode, int stepIndex, ShaderConfig config,
        FrameCapture captured, int debugVertexIndex, string docPath)
    {
        try
        {
            var parserConfig = MakeParserConfig(docPath);
            var invocation = await BuildAsync(config, captured, debugVertexIndex);
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
