using System.Runtime;
using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

// Builds the application's commands. Every method here returns a Cmd; the
// DebuggerProgram interpreter runs them. The active run's cancellation source is
// the one piece of effect state that must outlive a single command.
public sealed class DebuggerEffects
{
    private readonly ShaderExecutor _executor = new();
    private readonly HLSLRunner _runner = new();
    private readonly FileDialogService _fileDialogs;

    public DebuggerEffects(FileDialogService fileDialogs) => _fileDialogs = fileDialogs;

    // Gathers the per-frame inputs a ShaderInvocation needs (canvas size, camera
    // matrices, mouse). Everything model-derived is passed in.
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

    // Aggressively reclaims memory between runs. Interpreting a full frame
    // allocates heavily, so this keeps the working set down.
    private static void Reclaim()
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

    private CancellationTokenSource _runCts;

    public Cmd RunCpu(string code, ShaderConfig config, string docPath) =>
        Cmd.OfEffect((dispatch, _) => RunCpuImpl(code, config, docPath, dispatch));

    public Cmd RunGpu(string code, ShaderConfig config, float initialTime, bool paused, string docPath) =>
        Cmd.OfEffect((dispatch, _) => RunGpuImpl(code, config, initialTime, paused, docPath, dispatch));

    public Cmd RenderViewMode(DebugViewMode mode, ExecutionMetrics metrics, ShaderImage image) =>
        Cmd.OfTask(() => RenderViewModeImpl(mode, metrics, image));

    public Cmd SetMeshData(Mesh mesh) =>
        Cmd.OfTask(() => CanvasInterop.SetMeshData(mesh?.Positions, mesh?.Indices).AsTask());

    public Cmd SetGpuPaused(bool paused) =>
        Cmd.OfTask(() => SetGpuPausedImpl(paused));

    public Cmd RestartGpuTime() =>
        Cmd.OfTask(() => GpuInterop.Restart().AsTask());

    public Cmd CancelRun() =>
        Cmd.OfTask(() => { try { _runCts?.Cancel(); } catch { } return Task.CompletedTask; });

    private CancellationTokenSource BeginRun()
    {
        var previous = _runCts;
        _runCts = new CancellationTokenSource();
        try { previous?.Cancel(); } catch { }
        return _runCts;
    }

    private async Task RunCpuImpl(string code, ShaderConfig config, string docPath, Action<Msg> dispatch)
    {
        var cts = BeginRun();
        try { await GpuInterop.Stop(); } catch { }
        Reclaim();

        var parserConfig = MakeParserConfig(docPath);
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);

        if (config.CpuMode != CpuMode.SingleWarp)
        {
            _ = RunCpuFullFrameSafe(code, config, parserConfig, wx, wy, dispatch, cts);
            return;
        }

        try
        {
            var invocation = await BuildAsync(config, null, -1);
            var program = ShaderProgram.FromSource(code, parserConfig);
            var outcome = _executor.Execute(_runner, program, invocation, ExecutionOptions.None);

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
    }

    private async Task RunCpuFullFrameSafe(
        string code, ShaderConfig config, HLSLParserConfig parserConfig,
        int wx, int wy, Action<Msg> dispatch, CancellationTokenSource cts)
    {
        try { await RunCpuFullFrame(code, config, parserConfig, wx, wy, dispatch, cts); }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task RunCpuFullFrame(
        string code, ShaderConfig config, HLSLParserConfig parserConfig,
        int wx, int wy, Action<Msg> dispatch, CancellationTokenSource cts)
    {
        var (canvasW, canvasH) = await GetCanvasSizeAsync(wx, wy);
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

    // Each tile re-visits the AST after a fresh Reset so interpreter state
    // cannot leak between warps. Returns the first tile that failed, or null.
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
                // Each worker owns its own runner and AST copy to avoid
                // cross-thread interpreter state.
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
                if (error == null) { error = outcome; try { cts.Cancel(); } catch { } }
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
                BeforeStatement = e => before(e.Node),
                AfterStatement = e => after(e.Node),
            };
            tileInvocation.OnTextureFetch = MakeTextureFetchHook(metrics, runner, tx, ty);
        }
        return _executor.Execute(runner, program, tileInvocation, options);
    }

    private static Action MakeTextureFetchHook(ExecutionMetrics metrics, HLSLRunner runner, int tx, int ty)
    {
        int threadCount = metrics.WarpX * metrics.WarpY;
        int warpW = metrics.WarpX, warpH = metrics.WarpY;
        int canvasW = metrics.CanvasW, canvasH = metrics.CanvasH;
        return () =>
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
        };
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

    private async Task<(int W, int H)> GetCanvasSizeAsync(int wx, int wy)
    {
        try
        {
            var size = await CanvasInterop.GetCpuCanvasSize();
            if (size != null && size.Length >= 2 && size[0] > 0 && size[1] > 0)
                return (size[0], size[1]);
        }
        catch { }
        return (Math.Max(wx, 256), Math.Max(wy, 256));
    }

    private async Task RunGpuImpl(
        string code, ShaderConfig config, float initialTime, bool paused, string docPath, Action<Msg> dispatch)
    {
        BeginRun();
        try { await GpuInterop.Stop(); } catch { }
        Reclaim();

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
    }

    private async Task RenderViewModeImpl(DebugViewMode mode, ExecutionMetrics metrics, ShaderImage image)
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
    }

    private async Task SetGpuPausedImpl(bool paused)
    {
        try
        {
            if (paused) await GpuInterop.Pause();
            else await GpuInterop.Resume();
        }
        catch { }
    }

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
            var snap = await GpuInterop.Snapshot();
            if (snap == null || snap.Length < 3) return;
            await GpuInterop.Pause();
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
                    var snap = await GpuInterop.Snapshot();
                    if (snap != null && snap.Length >= 3 && snap[1] > 0 && snap[2] > 0)
                        captured = new FrameCapture(snap[0], (int)snap[1], (int)snap[2]);
                }
                catch { }
            }
            try { await GpuInterop.Pause(); } catch { }
            Reclaim();

            var parserConfig = MakeParserConfig(docPath);
            var invocation = await BuildAsync(config, captured, debugVertexIndex);
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
                    imageDataUrl = await BrowserInterop.RgbaToDataUrl(
                        rgba, wx, wy, inspectedThread % wx, inspectedThread / wx, 0.7);
                }
                catch { }
            }
        }

        dispatch(new ImmediateEvalFinished(new ImmediateEntry(expression, resultStr, isError, imageDataUrl)));
        try { await BrowserInterop.ScrollImmediateToBottom(); } catch { }
    }

    // Re-runs the shader up to the target step and evaluates an expression in
    // that scope. The hook aborts the run once the target step is reached.
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

    public Cmd FetchEditorText(Func<string, Msg> then) =>
        Cmd.OfTask(async () => then(await GetEditorText()));

    private async Task<string> GetEditorText()
    {
        try { return await EditorInterop.GetValue(); }
        catch { return ""; }
    }

    // One Monaco model per document: the model owns the text and undo history.
    public Cmd CreateModel(int docId, string content) =>
        Cmd.OfTask(() => EditorInterop.CreateModel(docId, content).AsTask());

    public Cmd ShowModel(int docId) =>
        Cmd.OfTask(() => EditorInterop.ShowModel(docId).AsTask());

    public Cmd SetModelContent(int docId, string content) =>
        Cmd.OfTask(() => EditorInterop.SetModelContent(docId, content).AsTask());

    public Cmd DisposeModel(int docId) =>
        Cmd.OfTask(() => EditorInterop.DisposeModel(docId).AsTask());

    public Cmd SetEditorFontSize(int size) =>
        Cmd.OfTask(() => EditorInterop.SetFontSize(size).AsTask());

    public Cmd SetEditorReadOnly(bool readOnly) =>
        Cmd.OfTask(() => EditorInterop.SetReadOnly(readOnly).AsTask());

    public Cmd SetTheme(string theme) =>
        Cmd.OfTask(() => EditorInterop.SetTheme(theme).AsTask());

    public Cmd HighlightLine(int line) =>
        Cmd.OfTask(() => EditorInterop.HighlightLine(line).AsTask());

    public Cmd SetBreakpoints(int docId, IReadOnlyList<int> lines) =>
        Cmd.OfTask(() => EditorInterop.SetBreakpoints(docId, lines).AsTask());

    public Cmd OpenFileDialog() =>
        Cmd.OfTask(async () =>
        {
            var (path, content) = await _fileDialogs.OpenFile();
            return (Msg)new FileOpened(path, content);
        });

    public Cmd SaveFileDialog(string code, string currentPath, bool asNew) =>
        Cmd.OfEffect(async dispatch =>
        {
            string path = asNew
                ? await _fileDialogs.SaveFileAs(code)
                : await _fileDialogs.SaveFile(code, currentPath);
            if (path != null) dispatch(new FileSaved(path));
        });

    public Cmd DownloadFile(string fileName, string content) =>
        Cmd.OfTask(() => BrowserInterop.DownloadTextFile(fileName, content).AsTask());

    public Cmd PickObjFile() =>
        Cmd.OfTask(() => BrowserInterop.PickObj().AsTask());

    public Cmd CopyToClipboard(string text) =>
        Cmd.OfTask(() => BrowserInterop.CopyToClipboard(text).AsTask());
}
