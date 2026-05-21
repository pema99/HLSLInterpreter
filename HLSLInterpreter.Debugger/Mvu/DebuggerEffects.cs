using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.Mvu;

// Builds the application's commands. Every method here returns a Cmd expressed
// through the generic Cmd vocabulary; the DebuggerProgram interpreter is what runs
// them. This is also where effect state that must outlive a single command
// lives: the active run's cancellation source, the canvas-sync diff cache, and
// the .NET reference the GPU loop uses for click-to-debug. update reaches all
// effects through this object.
public sealed partial class DebuggerEffects
{
    private readonly ShaderExecutor _executor;
    private readonly ShaderInvocationBuilder _invocationBuilder;
    private readonly GpuInterop _gpu;
    private readonly CanvasInterop _canvas;
    private readonly EditorInterop _editor;
    private readonly BrowserInterop _browser;
    private readonly FileDialogService _fileDialogs;

    private readonly HLSLRunner _runner = new();

    // Set once by the shell so the GPU render loop can report click-to-debug
    // back into .NET.
    public object DotNetRef { get; set; }

    public DebuggerEffects(
        ShaderExecutor executor,
        ShaderInvocationBuilder invocationBuilder,
        GpuInterop gpu,
        CanvasInterop canvas,
        EditorInterop editor,
        BrowserInterop browser,
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
}
