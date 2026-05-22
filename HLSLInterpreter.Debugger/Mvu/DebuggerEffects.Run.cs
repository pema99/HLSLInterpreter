using System.Threading;
using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Mvu;

// Run effects: GPU preview, CPU single-warp, and CPU tiled full-frame. The
// tiled run is detached so it can dispatch progress messages (it becomes
// cancellable, then finishes) while the dispatch pump stays free. Each run owns
// a cancellation source; starting a run cancels the previous one.
public sealed partial class DebuggerEffects
{
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
        RuntimeMemory.Reclaim();

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
        RuntimeMemory.Reclaim();

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
}
