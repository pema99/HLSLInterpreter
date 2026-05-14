using System.ComponentModel;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Services;

public sealed class RunController : INotifyPropertyChanged
{
    private readonly IJSRuntime _js;
    private readonly DebuggerAppState _state;

    public RunController(IJSRuntime js, DebuggerAppState state)
    {
        _js = js;
        _state = state;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public HLSLRunner Runner { get; } = new();

    private string _output;
    public string Output { get => _output; set => Set(ref _output, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => Set(ref _hasError, value); }

    private string _errorMessage = "";
    public string ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value ?? ""); }

    private Exception _lastException;
    public Exception LastException { get => _lastException; set => Set(ref _lastException, value); }

    private bool _isGpuMode;
    public bool IsGpuMode { get => _isGpuMode; set => Set(ref _isGpuMode, value); }

    private bool _gpuPaused;
    public bool GpuPaused { get => _gpuPaused; set => Set(ref _gpuPaused, value); }

    public (float Time, int CanvasW, int CanvasH)? GpuCaptured { get; set; }

    private byte[] _imagePixels;
    public byte[] ImagePixels { get => _imagePixels; set => Set(ref _imagePixels, value); }

    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    private bool _hasImage;
    public bool HasImage { get => _hasImage; set => Set(ref _hasImage, value); }

    private ExecutionMetrics _metrics;
    public ExecutionMetrics Metrics { get => _metrics; set => Set(ref _metrics, value); }

    private DebugViewMode _viewMode = DebugViewMode.Color;
    public DebugViewMode ViewMode { get => _viewMode; set => Set(ref _viewMode, value); }

    private bool _isCancellableRun;
    public bool IsCancellableRun { get => _isCancellableRun; private set => Set(ref _isCancellableRun, value); }

    private bool _cancelRequested;
    public void RequestCancel() => _cancelRequested = true;

    private Task _currentRun = Task.CompletedTask;

    public async Task RunAsync(
        Func<Task<string>> getCode,
        HLSLParserConfig parserConfig,
        object dotNetRef,
        Func<Task> beforeGpuRender = null)
    {
        if (IsCancellableRun)
        {
            RequestCancel();
            try { await _currentRun; } catch { }
        }

        var run = RunInternal(getCode, parserConfig, dotNetRef, beforeGpuRender);
        _currentRun = run;
        try { await run; }
        finally { if (ReferenceEquals(_currentRun, run)) _currentRun = Task.CompletedTask; }
    }

    private async Task RunInternal(
        Func<Task<string>> getCode,
        HLSLParserConfig parserConfig,
        object dotNetRef,
        Func<Task> beforeGpuRender)
    {
        float initialTime = GpuCaptured?.Time ?? 0f;
        BeginRun();
        ReclaimMemory();
        try { await _js.InvokeVoidAsync("gpuStop"); } catch { }

        if (_state.GpuPreviewEnabled)
        {
            HasImage = true;
            IsGpuMode = true;
            if (beforeGpuRender != null) await beforeGpuRender();
            await RunGpuInternal(getCode, parserConfig, dotNetRef, initialTime);
            if (GpuPaused)
            {
                try { await _js.InvokeVoidAsync("gpuPause"); } catch { }
            }
        }
        else
        {
            await RunCpuInternal(getCode, parserConfig);
        }

        IsRunning = false;
    }

    public static void ReclaimMemory()
    {
        if (OperatingSystem.IsBrowser())
        {
            GC.Collect();
            return;
        }
        var prev = GCSettings.LargeObjectHeapCompactionMode;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GCSettings.LargeObjectHeapCompactionMode = prev;
    }

    private void BeginRun()
    {
        IsRunning = true;
        HasError = false;
        ErrorMessage = "";
        LastException = null;
        Output = "";
        ImagePixels = null;
        GpuCaptured = null;
        HasImage = false;
        IsGpuMode = false;
        Metrics = null;
        ViewMode = DebugViewMode.Color;
    }

    private async Task RunCpuInternal(Func<Task<string>> getCode, HLSLParserConfig parserConfig)
    {
        var sw = new System.IO.StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(System.IO.TextWriter.Synchronized(sw));
        try
        {
            string code = await getCode();
            int wx = Math.Max(1, _state.WarpX);
            int wy = Math.Max(1, _state.WarpY);

            if (_state.CpuMode != CpuMode.SingleWarp)
            {
                await RunCpuFullFrame(code, parserConfig, wx, wy);
            }
            else
            {
                var invocation = await BuildShaderInvocationAsync();
                Runner.DebugHookBeforeStatement = null;
                Runner.DebugHookAfterStatement = null;
                Runner.Reset();
                Runner.SetWarpSize(wx, wy);
                invocation.SetUniforms(Runner);
                Runner.ProcessCode(code, parserConfig);

                HLSLValue result = invocation.Execute(Runner);
                TryExtractImage(result, wx, wy);
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally { Console.SetOut(oldOut); }

        Output = sw.ToString();
    }

    private async Task RunCpuFullFrame(string code, HLSLParserConfig parserConfig, int wx, int wy)
    {
        var (canvasW, canvasH) = await GetCpuCanvasSizeAsync(wx, wy);
        var invocation = (await BuildShaderInvocationAsync()) with { CanvasW = canvasW, CanvasH = canvasH };
        if (_state.ShaderRenderMode == ShaderRenderMode.VertFrag)
        {
            var projection = await _js.InvokeAsync<float[]>("gpuProjection", canvasW, canvasH);
            invocation = invocation with { Projection = projection };
        }
        Runner.DebugHookBeforeStatement = null;
        Runner.DebugHookAfterStatement = null;

        int tilesX = (canvasW + wx - 1) / wx;
        int tilesY = (canvasH + wy - 1) / wy;

        var fullPixels = new byte[canvasW * canvasH * 4];
        for (int i = 3; i < fullPixels.Length; i += 4) fullPixels[i] = 255;
        ImagePixels = fullPixels;
        ImageWidth = canvasW;
        ImageHeight = canvasH;
        HasImage = true;
        await _js.InvokeVoidAsync("imgAllocPixels", canvasW, canvasH);

        var metrics = _state.CpuMode == CpuMode.FullFrameWithMetrics ? new ExecutionMetrics(canvasW, canvasH, wx, wy) : null;

        _cancelRequested = false;
        IsCancellableRun = true;
        try
        {
            if (OperatingSystem.IsBrowser())
                await RunTilesSerial(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics);
            else
                await RunTilesParallel(code, parserConfig, invocation, wx, wy, canvasW, canvasH, tilesX, tilesY, fullPixels, metrics);
        }
        finally
        {
            IsCancellableRun = false;
            _cancelRequested = false;
        }
        Metrics = metrics;
    }

    // Each tile re-visits the AST after a fresh Reset so interpreter state cannot leak between warps.
    private async Task RunTilesSerial(string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY, byte[] fullPixels, ExecutionMetrics metrics)
    {
        var parsedNodes = ShaderParser.ParseTopLevelDeclarations(code, parserConfig);
        Runner.SetWarpSize(wx, wy);
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (_cancelRequested) return;
                var tilePixels = RenderTile(Runner, parsedNodes, invocation, tx, ty, wx, wy, metrics);
                if (tilePixels == null) continue;
                BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
                await _js.InvokeVoidAsync("imgSetPixelsRect", tilePixels, tx * wx, ty * wy, wx, wy);
                await Task.Yield();
            }
        }
    }

    private async Task RunTilesParallel(string code, HLSLParserConfig parserConfig, ShaderInvocation invocation,
        int wx, int wy, int canvasW, int canvasH, int tilesX, int tilesY, byte[] fullPixels, ExecutionMetrics metrics)
    {
        var workQueue = Channel.CreateUnbounded<(int tx, int ty)>();
        for (int ty = 0; ty < tilesY; ty++)
            for (int tx = 0; tx < tilesX; tx++)
                workQueue.Writer.TryWrite((tx, ty));
        workQueue.Writer.Complete();

        var results = Channel.CreateUnbounded<(int tx, int ty, byte[] pixels)>();
        int workerCount = Math.Max(1, Environment.ProcessorCount - 1);
        var workers = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            workers[w] = Task.Run(async () =>
            {
                // Each worker owns its own Runner and AST copy to avoid cross-thread interpreter state.
                var runner = new HLSLRunner();
                runner.SetWarpSize(wx, wy);
                var parsedNodes = ShaderParser.ParseTopLevelDeclarations(code, parserConfig);
                await foreach (var (tx, ty) in workQueue.Reader.ReadAllAsync())
                {
                    if (_cancelRequested) break;
                    var pixels = RenderTile(runner, parsedNodes, invocation, tx, ty, wx, wy, metrics);
                    await results.Writer.WriteAsync((tx, ty, pixels));
                }
            });
        }
        var allWorkers = Task.WhenAll(workers);
        _ = allWorkers.ContinueWith(_ => results.Writer.Complete());

        await foreach (var (tx, ty, tilePixels) in results.Reader.ReadAllAsync())
        {
            if (tilePixels == null) continue;
            BlitTile(tilePixels, tx * wx, ty * wy, wx, wy, canvasW, canvasH, fullPixels);
            await _js.InvokeVoidAsync("imgSetPixelsRect", tilePixels, tx * wx, ty * wy, wx, wy);
        }
        await allWorkers;
    }

    private static byte[] RenderTile(HLSLRunner runner, IEnumerable<HLSLSyntaxNode> parsedNodes,
        ShaderInvocation invocation, int tx, int ty, int wx, int wy, ExecutionMetrics metrics)
    {
        runner.Reset();
        invocation.SetUniforms(runner);
        runner.ProcessCode(parsedNodes);
        var tileInvocation = invocation with { GroupOffsetX = tx, GroupOffsetY = ty };
        if (metrics != null)
        {
            runner.DebugHookBeforeStatement = metrics.MakeBeforeStatementHook(runner, tx, ty);
            runner.DebugHookAfterStatement = metrics.MakeAfterStatementHook(runner, tx, ty);
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
                        if (!state.IsThreadActive(threadIndex))
                            continue;
                        int px = tx * warpW + (threadIndex % warpW);
                        int py = ty * warpH + (threadIndex / warpW);
                        if (px < canvasW && py < canvasH)
                            metrics.PixelFetches[py * canvasW + px]++;
                    }
                }
            };
        }
        HLSLValue result;
        try
        {
            result = tileInvocation.Execute(runner);
        }
        finally
        {
            runner.DebugHookBeforeStatement = null;
            runner.DebugHookAfterStatement = null;
        }
        return ValueImageRenderer.TryExtractImage(result, wx, wy);
    }

    private async Task<(int W, int H)> GetCpuCanvasSizeAsync(int wx, int wy)
    {
        if (GpuCaptured.HasValue) return (GpuCaptured.Value.CanvasW, GpuCaptured.Value.CanvasH);
        try
        {
            var size = await _js.InvokeAsync<int[]>("cpuCanvasSize");
            if (size != null && size.Length >= 2 && size[0] > 0 && size[1] > 0)
                return (size[0], size[1]);
        }
        catch { }
        return (Math.Max(wx, 256), Math.Max(wy, 256));
    }

    private static void BlitTile(byte[] tile, int x0, int y0, int wx, int wy, int canvasW, int canvasH, byte[] full)
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

    private async Task RunGpuInternal(
        Func<Task<string>> getCode,
        HLSLParserConfig parserConfig,
        object dotNetRef,
        float initialTime)
    {
        bool hasGpu = false;
        try { hasGpu = await _js.InvokeAsync<bool>("gpuIsAvailable"); }
        catch { hasGpu = false; }
        if (!hasGpu)
        {
            HasError = true;
            ErrorMessage = "WebGPU is not available in this browser. Use Debug to step through on the CPU interpreter instead.";
            return;
        }

        try
        {
            string userCode = await getCode();
            int wx = Math.Max(1, _state.WarpX);
            int wy = Math.Max(1, _state.WarpY);
            var assembled = ShaderReflection.AssembleVertexShader(
                userCode,
                _state.VertexEntryPoint,
                _state.FragmentEntryPoint,
                _state.ShaderRenderMode,
                parserConfig);
            string mode = _state.ShaderRenderMode == ShaderRenderMode.VertFrag ? "vertfrag" : "pixel";
            float[] meshVertices = null;
            uint[] meshIndices = null;
            if (_state.ShaderRenderMode == ShaderRenderMode.VertFrag)
            {
                var mesh = _state.CurrentMesh;
                meshVertices = mesh.GetInterleavedVertices();
                meshIndices = mesh.Indices;
            }
            await _js.InvokeVoidAsync("gpuRender", "color-canvas-gpu", assembled.Source,
                _state.FragmentEntryPoint, wx, wy, dotNetRef, mode, assembled.VertexEntry,
                assembled.VertexInputs, meshVertices, meshIndices, initialTime,
                _state.Textures, _state.Samplers);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            LastException = ex;
        }
    }

    public async Task<ShaderInvocation> BuildShaderInvocationAsync()
    {
        int wx = Math.Max(1, _state.WarpX);
        int wy = Math.Max(1, _state.WarpY);
        int canvasW = GpuCaptured?.CanvasW ?? wx;
        int canvasH = GpuCaptured?.CanvasH ?? wy;
        float[] view = null;
        float[] projection = null;
        if (_state.ShaderRenderMode == ShaderRenderMode.VertFrag)
        {
            view = await _js.InvokeAsync<float[]>("gpuView");
            projection = await _js.InvokeAsync<float[]>("gpuProjection", canvasW, canvasH);
        }
        float[] mouse;
        try { mouse = await _js.InvokeAsync<float[]>("gpuMouse"); }
        catch { mouse = new float[] { 0f, 0f, 0f, 0f }; }

        return new ShaderInvocation(
            Mode: _state.ShaderRenderMode,
            FragmentEntryPoint: _state.FragmentEntryPoint,
            VertexEntryPoint: _state.VertexEntryPoint,
            Mesh: _state.CurrentMesh,
            WarpX: wx,
            WarpY: wy,
            GroupOffsetX: _state.GroupOffsetX,
            GroupOffsetY: _state.GroupOffsetY,
            CanvasW: canvasW,
            CanvasH: canvasH,
            Time: GpuCaptured?.Time ?? 0f,
            View: view,
            Projection: projection,
            Mouse: mouse,
            DebugVertexIndex: _state.DebugVertexIndex,
            Textures: _state.Textures,
            Samplers: _state.Samplers);
    }

    public bool TryExtractImage(HLSLValue result, int wx, int wy)
    {
        var pixels = ValueImageRenderer.TryExtractImage(result, wx, wy);
        if (pixels == null) return false;
        ImagePixels = pixels;
        ImageWidth = wx;
        ImageHeight = wy;
        HasImage = true;
        return true;
    }

    public async Task ToggleGpuPauseAsync()
    {
        if (GpuPaused)
        {
            try { await _js.InvokeVoidAsync("gpuResume"); } catch { }
            GpuPaused = false;
        }
        else
        {
            try { await _js.InvokeVoidAsync("gpuPause"); } catch { }
            GpuPaused = true;
        }
    }

    public Task RestartGpuTimeAsync() => _js.InvokeVoidAsync("gpuRestart").AsTask();

    public async Task StopGpuAsync()
    {
        try { await _js.InvokeVoidAsync("gpuStop"); } catch { }
        IsGpuMode = false;
        HasImage = false;
        ImagePixels = null;
    }

    public async Task PauseGpuRendererAsync()
    {
        try { await _js.InvokeVoidAsync("gpuPause"); } catch { }
    }

    public async Task SnapshotGpuIfNeededAsync()
    {
        if (!_state.GpuPreviewEnabled || GpuCaptured.HasValue) return;
        try
        {
            var snap = await _js.InvokeAsync<float[]>("gpuSnapshot");
            if (snap != null && snap.Length >= 3 && snap[1] > 0 && snap[2] > 0)
                GpuCaptured = (snap[0], (int)snap[1], (int)snap[2]);
        }
        catch { }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
