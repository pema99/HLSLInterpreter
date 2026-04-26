using System.ComponentModel;
using System.Runtime.CompilerServices;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public HLSLRunner Runner { get; } = new();

    private string? _output;
    public string? Output { get => _output; set => Set(ref _output, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => Set(ref _hasError, value); }

    private string _errorMessage = "";
    public string ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value ?? ""); }

    private Exception? _lastException;
    public Exception? LastException { get => _lastException; set => Set(ref _lastException, value); }

    private bool _isGpuMode;
    public bool IsGpuMode { get => _isGpuMode; set => Set(ref _isGpuMode, value); }

    private bool _gpuPaused;
    public bool GpuPaused { get => _gpuPaused; set => Set(ref _gpuPaused, value); }

    public (float Time, int CanvasW, int CanvasH)? GpuCaptured { get; set; }

    private byte[]? _imagePixels;
    public byte[]? ImagePixels { get => _imagePixels; set => Set(ref _imagePixels, value); }

    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    private bool _hasImage;
    public bool HasImage { get => _hasImage; set => Set(ref _hasImage, value); }

    public async Task RunAsync(
        Func<Task<string>> getCode,
        Func<HLSLParserConfig> makeParserConfig,
        object? dotNetRef,
        Func<Task>? beforeGpuRender = null)
    {
        BeginRun();
        try { await _js.InvokeVoidAsync("gpuStop"); } catch { }

        if (_state.GpuPreviewEnabled)
        {
            HasImage = true;
            IsGpuMode = true;
            GpuPaused = false;
            if (beforeGpuRender != null) await beforeGpuRender();
            await RunGpuInternal(getCode, dotNetRef);
        }
        else
        {
            await RunCpuInternal(getCode, makeParserConfig);
        }

        IsRunning = false;
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
    }

    private async Task RunCpuInternal(Func<Task<string>> getCode, Func<HLSLParserConfig> makeParserConfig)
    {
        var sw = new System.IO.StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            string code = await getCode();
            int wx = Math.Max(1, _state.WarpX);
            int wy = Math.Max(1, _state.WarpY);
            int threadCount = wx * wy;

            Runner.DebugHook = null;
            Runner.Reset();
            Runner.SetWarpSize(wx, wy);
            SetSharedGlobals(Runner);
            Runner.ProcessCode(code, makeParserConfig());

            var threadArg = DebuggerSession.BuildThreadArg(wx, wy, threadCount, _state.GroupOffsetX, _state.GroupOffsetY);
            HLSLValue result = DebuggerSession.CallEntryPoint(Runner, threadArg, _state.EntryPoint);
            TryExtractImage(result, wx, wy);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally { Console.SetOut(oldOut); }

        Output = sw.ToString();
    }

    private async Task RunGpuInternal(Func<Task<string>> getCode, object? dotNetRef)
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
            string code = await getCode();
            int wx = Math.Max(1, _state.WarpX);
            int wy = Math.Max(1, _state.WarpY);
            await _js.InvokeVoidAsync("gpuRender", "color-canvas-gpu", code,
                _state.EntryPoint, wx, wy, dotNetRef);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            LastException = ex;
        }
    }

    public void SetSharedGlobals(HLSLRunner runner)
    {
        int wx = Math.Max(1, _state.WarpX);
        int wy = Math.Max(1, _state.WarpY);
        runner.SetVariable("_WarpSize", new VectorValue(ScalarType.Float,
            new HLSLRegister<RawValue[]>(new RawValue[] { (float)wx, (float)wy })));
        bool useCapture = _state.GpuPreviewEnabled && GpuCaptured.HasValue;
        float resW = useCapture ? GpuCaptured!.Value.CanvasW : wx;
        float resH = useCapture ? GpuCaptured!.Value.CanvasH : wy;
        float t = useCapture ? GpuCaptured!.Value.Time : 0f;
        runner.SetVariable("_Resolution", new VectorValue(ScalarType.Float,
            new HLSLRegister<RawValue[]>(new RawValue[] { resW, resH })));
        runner.SetVariable("_Time", new ScalarValue(ScalarType.Float, new HLSLRegister<RawValue>(t)));
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

    public async Task SnapshotGpuIfNeededAsync()
    {
        if (!_state.GpuPreviewEnabled || GpuCaptured.HasValue) return;
        try
        {
            var snap = await _js.InvokeAsync<float[]>("gpuSnapshot");
            if (snap != null && snap.Length == 3 && snap[1] > 0 && snap[2] > 0)
                GpuCaptured = (snap[0], (int)snap[1], (int)snap[2]);
        }
        catch { }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
