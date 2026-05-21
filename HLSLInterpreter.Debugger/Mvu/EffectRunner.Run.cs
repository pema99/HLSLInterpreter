using System.Threading;
using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Services;
using HLSLInterpreter.Debugger.State;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Mvu;

// Run effects: GPU preview, CPU single-warp, and CPU tiled full-frame. The
// tiled run is detached so it can dispatch progress messages (it becomes
// cancellable, then finishes) while the dispatch pump stays free. Each run owns
// a cancellation source; starting a run cancels the previous one.
public sealed partial class EffectRunner
{
    private CancellationTokenSource _runCts;

    private CancellationTokenSource BeginRun()
    {
        var previous = _runCts;
        _runCts = new CancellationTokenSource();
        try { previous?.Cancel(); } catch { }
        return _runCts;
    }

    private void CancelRunEffect()
    {
        try { _runCts?.Cancel(); } catch { }
    }

    private async Task RunCpuEffect(RunCpu c, Action<Msg> dispatch)
    {
        var cts = BeginRun();
        try { await _gpu.Stop(); } catch { }
        RuntimeMemory.Reclaim();

        var parserConfig = ShaderInvocationBuilder.MakeParserConfig(c.DocPath);
        int wx = Math.Max(1, c.Config.WarpX);
        int wy = Math.Max(1, c.Config.WarpY);

        if (c.Config.CpuMode != CpuMode.SingleWarp)
        {
            _ = RunCpuFullFrameSafe(c, parserConfig, wx, wy, dispatch, cts);
            return;
        }

        try
        {
            var invocation = await _invocationBuilder.BuildAsync(c.Config, null, -1);
            var program = ShaderProgram.FromSource(c.Code, parserConfig);
            var outcome = _executor.Execute(_runner, program, invocation, ExecutionOptions.None);

            if (outcome.HasError)
            {
                dispatch(new RunFinished(
                    outcome.Output, new RunError(outcome.ErrorMessage, outcome.Exception), null, null));
                return;
            }
            var pixels = ValueImageRenderer.TryExtractImage(outcome.Result, wx, wy);
            ShaderImage image = null;
            if (pixels != null)
            {
                image = new ShaderImage(pixels, wx, wy);
                await _canvas.SetPixels(pixels, wx, wy);
            }
            dispatch(new RunFinished(outcome.Output, null, image, null));
        }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task RunCpuFullFrameSafe(
        RunCpu c, HLSLParserConfig parserConfig, int wx, int wy, Action<Msg> dispatch, CancellationTokenSource cts)
    {
        try { await RunCpuFullFrame(c, parserConfig, wx, wy, dispatch, cts); }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task RunCpuFullFrame(
        RunCpu c, HLSLParserConfig parserConfig, int wx, int wy, Action<Msg> dispatch, CancellationTokenSource cts)
    {
        var (canvasW, canvasH) = await GetCanvasSizeAsync(wx, wy);
        var invocation = (await _invocationBuilder.BuildAsync(c.Config, null, -1))
            with { CanvasW = canvasW, CanvasH = canvasH };
        if (c.Config.RenderMode == ShaderRenderMode.VertFrag)
            invocation = invocation with { Projection = await _gpu.Projection(canvasW, canvasH) };

        int tilesX = (canvasW + wx - 1) / wx;
        int tilesY = (canvasH + wy - 1) / wy;

        var fullPixels = new byte[canvasW * canvasH * 4];
        for (int i = 3; i < fullPixels.Length; i += 4) fullPixels[i] = 255;
        await _canvas.AllocPixels(canvasW, canvasH);

        var metrics = c.Config.CpuMode == CpuMode.FullFrameWithMetrics
            ? new ExecutionMetrics(canvasW, canvasH, wx, wy)
            : null;

        dispatch(new RunBecameCancellable());

        RunOutcome tileError;
        string output;
        using (var capture = new ConsoleCapture())
        {
            tileError = OperatingSystem.IsBrowser()
                ? await RunTilesSerial(c.Code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics, cts)
                : await RunTilesParallel(c.Code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics, cts);
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
        try
        {
            var size = await _canvas.GetCpuCanvasSize();
            if (size != null && size.Length >= 2 && size[0] > 0 && size[1] > 0)
                return (size[0], size[1]);
        }
        catch { }
        return (Math.Max(wx, 256), Math.Max(wy, 256));
    }

    private async Task RunGpuEffect(RunGpu c, Action<Msg> dispatch)
    {
        BeginRun();
        try { await _gpu.Stop(); } catch { }
        RuntimeMemory.Reclaim();

        if (!await _gpu.IsAvailable())
        {
            dispatch(new RunFinished("", new RunError(
                "WebGPU is not available in this browser. Use Debug to step through on the CPU interpreter instead.",
                null), null, null));
            return;
        }
        try
        {
            int wx = Math.Max(1, c.Config.WarpX);
            int wy = Math.Max(1, c.Config.WarpY);
            var parserConfig = ShaderInvocationBuilder.MakeParserConfig(c.DocPath);
            var assembled = ShaderReflection.AssembleVertexShader(
                c.Code, c.Config.VertexEntryPoint, c.Config.FragmentEntryPoint, c.Config.RenderMode, parserConfig);
            string mode = c.Config.RenderMode == ShaderRenderMode.VertFrag ? "vertfrag" : "pixel";
            float[] meshVertices = null;
            uint[] meshIndices = null;
            if (c.Config.RenderMode == ShaderRenderMode.VertFrag)
            {
                meshVertices = c.Config.Mesh.GetInterleavedVertices();
                meshIndices = c.Config.Mesh.Indices;
            }
            await _gpu.Render(new GpuRenderRequest(
                CanvasId: "color-canvas-gpu",
                Source: assembled.Source,
                FragmentEntryPoint: c.Config.FragmentEntryPoint,
                WarpX: wx,
                WarpY: wy,
                DotNetRef: DotNetRef,
                Mode: mode,
                VertexEntryPoint: assembled.VertexEntry,
                VertexInputs: assembled.VertexInputs,
                MeshVertices: meshVertices,
                MeshIndices: meshIndices,
                Time: c.InitialTime,
                Textures: c.Config.Textures,
                Samplers: c.Config.Samplers));
            if (c.Paused)
            {
                try { await _gpu.Pause(); } catch { }
            }
            dispatch(new RunFinished("", null, null, null));
        }
        catch (Exception ex)
        {
            dispatch(new RunFinished("", new RunError(ex.Message, ex), null, null));
        }
    }

    private async Task RenderViewModeEffect(RenderViewMode c)
    {
        if (c.Mode != DebugViewMode.Color && c.Metrics != null)
        {
            var pixels = c.Metrics.Render(c.Mode);
            if (pixels != null) await _canvas.SetPixels(pixels, c.Metrics.CanvasW, c.Metrics.CanvasH);
        }
        else if (c.Image != null)
        {
            await _canvas.SetPixels(c.Image.Pixels, c.Image.Width, c.Image.Height);
        }
    }

    private async Task SetGpuPausedEffect(bool paused)
    {
        try
        {
            if (paused) await _gpu.Pause();
            else await _gpu.Resume();
        }
        catch { }
    }
}
