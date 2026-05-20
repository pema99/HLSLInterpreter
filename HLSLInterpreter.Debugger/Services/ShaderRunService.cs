using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Services;

// Orchestrates a shader run: GPU preview, CPU single-warp, or CPU tiled
// full-frame. Replaces the run half of the old RunController.
public sealed class ShaderRunService
{
    private readonly AppStore _store;
    private readonly ShaderExecutor _executor;
    private readonly ShaderInvocationBuilder _invocationBuilder;
    private readonly IGpuInterop _gpu;
    private readonly ICanvasInterop _canvas;

    private readonly HLSLRunner _runner = new();
    private volatile bool _cancelRequested;
    private Task _currentRun = Task.CompletedTask;

    public ShaderRunService(
        AppStore store,
        ShaderExecutor executor,
        ShaderInvocationBuilder invocationBuilder,
        IGpuInterop gpu,
        ICanvasInterop canvas)
    {
        _store = store;
        _executor = executor;
        _invocationBuilder = invocationBuilder;
        _gpu = gpu;
        _canvas = canvas;
    }

    // Set by the shell so gpuRender can report click-to-debug back to .NET.
    public object DotNetRef { get; set; }

    public void RequestCancel() => _cancelRequested = true;

    public async Task RunAsync(string code)
    {
        if (_store.State.Run.Status == RunStatus.Cancellable)
        {
            RequestCancel();
            try { await _currentRun; } catch { }
        }
        var run = RunInternal(code);
        _currentRun = run;
        try { await run; }
        finally { if (ReferenceEquals(_currentRun, run)) _currentRun = Task.CompletedTask; }
    }

    private async Task RunInternal(string code)
    {
        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        var parserConfig = _invocationBuilder.MakeParserConfig();
        float initialTime = _store.State.Run.CapturedFrame?.Time ?? 0f;

        _store.BeginRun();
        RuntimeMemory.Reclaim();
        try { await _gpu.Stop(); } catch { }

        if (_store.State.Run.GpuPreviewEnabled)
        {
            _store.SetRunBackend(RunBackend.Gpu);
            await RunGpu(code, config, parserConfig, initialTime);
            if (_store.State.Run.GpuPaused)
            {
                try { await _gpu.Pause(); } catch { }
            }
        }
        else
        {
            await RunCpu(code, config, parserConfig);
        }

        _store.FinishRun();
    }

    private async Task RunCpu(string code, ShaderConfig config, HLSLParserConfig parserConfig)
    {
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        try
        {
            if (config.CpuMode != CpuMode.SingleWarp)
            {
                await RunCpuFullFrame(code, config, parserConfig, wx, wy);
                return;
            }

            var invocation = await _invocationBuilder.BuildAsync();
            var program = ShaderProgram.FromSource(code, parserConfig);
            var outcome = _executor.Execute(_runner, program, invocation, ExecutionOptions.None);

            _store.SetRunOutput(outcome.Output);
            if (outcome.HasError)
            {
                _store.SetRunError(outcome.ErrorMessage, outcome.Exception);
                return;
            }
            var pixels = ValueImageRenderer.TryExtractImage(outcome.Result, wx, wy);
            if (pixels != null)
            {
                _store.SetRunImage(new ShaderImage(pixels, wx, wy));
                await _canvas.SetPixels(pixels, wx, wy);
            }
        }
        catch (Exception ex)
        {
            _store.SetRunError(ex.Message, ex);
        }
    }

    private async Task RunCpuFullFrame(
        string code, ShaderConfig config, HLSLParserConfig parserConfig, int wx, int wy)
    {
        var (canvasW, canvasH) = await GetCanvasSizeAsync(wx, wy);
        var invocation = (await _invocationBuilder.BuildAsync()) with { CanvasW = canvasW, CanvasH = canvasH };
        if (config.RenderMode == ShaderRenderMode.VertFrag)
            invocation = invocation with { Projection = await _gpu.Projection(canvasW, canvasH) };

        int tilesX = (canvasW + wx - 1) / wx;
        int tilesY = (canvasH + wy - 1) / wy;

        var fullPixels = new byte[canvasW * canvasH * 4];
        for (int i = 3; i < fullPixels.Length; i += 4) fullPixels[i] = 255;
        await _canvas.AllocPixels(canvasW, canvasH);

        var metrics = config.CpuMode == CpuMode.FullFrameWithMetrics
            ? new ExecutionMetrics(canvasW, canvasH, wx, wy)
            : null;

        _cancelRequested = false;
        _store.SetRunStatus(RunStatus.Cancellable);

        RunOutcome tileError;
        string output;
        using (var capture = new ConsoleCapture())
        {
            try
            {
                tileError = OperatingSystem.IsBrowser()
                    ? await RunTilesSerial(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics)
                    : await RunTilesParallel(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics);
            }
            finally { _cancelRequested = false; }
            output = capture.ToString();
        }

        _store.SetRunOutput(output);
        if (tileError != null)
        {
            _store.SetRunError(tileError.ErrorMessage, tileError.Exception);
            return;
        }
        _store.SetRunImage(new ShaderImage(fullPixels, canvasW, canvasH));
        _store.SetRunMetrics(metrics);
    }

    // Each tile re-visits the AST after a fresh Reset so interpreter state
    // cannot leak between warps. Returns the first tile that failed, or null.
    private async Task<RunOutcome> RunTilesSerial(
        string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY,
        byte[] fullPixels, ExecutionMetrics metrics)
    {
        var program = ShaderProgram.FromParsedNodes(ShaderProgram.Parse(code, parserConfig));
        var runner = new HLSLRunner();
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (_cancelRequested) return null;
                var outcome = RenderTile(runner, program, invocation, tx, ty, metrics);
                if (outcome.HasError) return outcome;
                var tilePixels = ValueImageRenderer.TryExtractImage(outcome.Result, wx, wy);
                if (tilePixels == null) continue;
                BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
                await _canvas.SetPixelsRect(tilePixels, tx * wx, ty * wy, wx, wy);
                await Task.Yield();
            }
        }
        return null;
    }

    private async Task<RunOutcome> RunTilesParallel(
        string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY,
        byte[] fullPixels, ExecutionMetrics metrics)
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
                    if (_cancelRequested) break;
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
                error ??= outcome;
                _cancelRequested = true;
                continue;
            }
            var tilePixels = ValueImageRenderer.TryExtractImage(outcome.Result, wx, wy);
            if (tilePixels == null) continue;
            BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
            await _canvas.SetPixelsRect(tilePixels, tx * wx, ty * wy, wx, wy);
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
        var captured = _store.State.Run.CapturedFrame;
        if (captured != null) return (captured.CanvasW, captured.CanvasH);
        try
        {
            var size = await _canvas.GetCpuCanvasSize();
            if (size != null && size.Length >= 2 && size[0] > 0 && size[1] > 0)
                return (size[0], size[1]);
        }
        catch { }
        return (Math.Max(wx, 256), Math.Max(wy, 256));
    }

    private async Task RunGpu(
        string code, ShaderConfig config, HLSLParserConfig parserConfig, float initialTime)
    {
        if (!await _gpu.IsAvailable())
        {
            _store.SetRunError(
                "WebGPU is not available in this browser. Use Debug to step through on the CPU interpreter instead.",
                null);
            return;
        }
        try
        {
            int wx = Math.Max(1, config.WarpX);
            int wy = Math.Max(1, config.WarpY);
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
            await _gpu.Render(new GpuRenderRequest(
                CanvasId: "color-canvas-gpu",
                Source: assembled.Source,
                FragmentEntryPoint: config.FragmentEntryPoint,
                WarpX: wx,
                WarpY: wy,
                DotNetRef: DotNetRef,
                Mode: mode,
                VertexEntryPoint: assembled.VertexEntry,
                VertexInputs: assembled.VertexInputs,
                MeshVertices: meshVertices,
                MeshIndices: meshIndices,
                Time: initialTime,
                Textures: config.Textures,
                Samplers: config.Samplers));
        }
        catch (Exception ex)
        {
            _store.SetRunError(ex.Message, ex);
        }
    }

    public async Task ToggleGpuPauseAsync()
    {
        if (_store.State.Run.GpuPaused)
        {
            try { await _gpu.Resume(); } catch { }
            _store.SetGpuPaused(false);
        }
        else
        {
            try { await _gpu.Pause(); } catch { }
            _store.SetGpuPaused(true);
        }
    }

    public Task RestartGpuTimeAsync() => _gpu.Restart().AsTask();

    public async Task StopGpuAsync()
    {
        try { await _gpu.Stop(); } catch { }
        _store.SetRunBackend(RunBackend.Cpu);
        _store.SetRunImage(null);
    }

    public async Task PauseGpuRendererAsync()
    {
        try { await _gpu.Pause(); } catch { }
    }

    public async Task SnapshotGpuIfNeededAsync()
    {
        if (!_store.State.Run.GpuPreviewEnabled || _store.State.Run.CapturedFrame != null) return;
        try
        {
            var snap = await _gpu.Snapshot();
            if (snap != null && snap.Length >= 3 && snap[1] > 0 && snap[2] > 0)
                _store.SetCapturedFrame(new FrameCapture(snap[0], (int)snap[1], (int)snap[2]));
        }
        catch { }
    }
}
