using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.Mvu;

// Carries out commands. This is the one impure component in the architecture.
// An async effect dispatches a follow-up Msg when it completes. A handler must
// never throw: a failed effect is reported by dispatching a message (or
// swallowed), so the dispatch pump cannot be broken by an effect.
public interface IEffectRunner
{
    Task RunAsync(Cmd command, Action<Msg> dispatch);
}

public sealed partial class EffectRunner : IEffectRunner
{
    private readonly ShaderExecutor _executor;
    private readonly ShaderInvocationBuilder _invocationBuilder;
    private readonly IGpuInterop _gpu;
    private readonly ICanvasInterop _canvas;
    private readonly IEditorInterop _editor;
    private readonly IBrowserInterop _browser;
    private readonly FileDialogService _fileDialogs;

    private readonly HLSLRunner _runner = new();

    // Set once by the shell so the GPU render loop can report click-to-debug
    // back into .NET.
    public object DotNetRef { get; set; }

    public EffectRunner(
        ShaderExecutor executor,
        ShaderInvocationBuilder invocationBuilder,
        IGpuInterop gpu,
        ICanvasInterop canvas,
        IEditorInterop editor,
        IBrowserInterop browser,
        FileDialogService fileDialogs)
    {
        _executor = executor;
        _invocationBuilder = invocationBuilder;
        _gpu = gpu;
        _canvas = canvas;
        _editor = editor;
        _browser = browser;
        _fileDialogs = fileDialogs;
    }

    public async Task RunAsync(Cmd command, Action<Msg> dispatch)
    {
        try
        {
            switch (command)
            {
                case FetchEditorText c: await FetchTextEffect(c, dispatch); break;
                case SetEditorText c: await Try(() => _editor.SetValue(c.Code)); break;
                case SetEditorFontSize c: await Try(() => _editor.SetFontSize(c.Size)); break;
                case SetEditorReadOnly c: await Try(() => _editor.SetReadOnly(c.ReadOnly)); break;
                case HighlightEditorLine c: await Try(() => _editor.HighlightLine(c.Line)); break;
                case SetEditorBreakpoints c: await Try(() => _editor.SetBreakpoints(c.Lines)); break;

                case RunCpu c: await RunCpuEffect(c, dispatch); break;
                case RunGpu c: await RunGpuEffect(c, dispatch); break;
                case CancelRun: CancelRunEffect(); break;
                case RenderViewMode c: await RenderViewModeEffect(c); break;
                case SetGpuPaused c: await SetGpuPausedEffect(c.Paused); break;
                case RestartGpuTime: await Try(() => _gpu.Restart()); break;

                case RecordTrace c: await RecordTraceEffect(c, dispatch); break;
                case EvaluateImmediate c: await EvaluateImmediateEffect(c, dispatch); break;

                case OpenFileDialog: await OpenFileDialogEffect(dispatch); break;
                case SaveFileDialog c: await SaveFileDialogEffect(c, dispatch); break;
                case DownloadFile c: await Try(() => _browser.DownloadTextFile(c.FileName, c.Content)); break;
                case PickObjFile: await Try(() => _browser.PickObj()); break;
                case CopyToClipboard c: await Try(() => _browser.CopyToClipboard(c.Text)); break;

                case SyncCanvas c: await SyncCanvasEffect(c.Projection); break;
                case DelayThenDispatch c: await DelayEffect(c, dispatch); break;
            }
        }
        catch
        {
            // An effect must never break the dispatch pump.
        }
    }

    private static async Task Try(Func<ValueTask> action)
    {
        try { await action(); }
        catch { }
    }

    private static async Task DelayEffect(DelayThenDispatch c, Action<Msg> dispatch)
    {
        await Task.Delay(c.DelayMs);
        dispatch(c.Message);
    }
}
