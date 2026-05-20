using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.State;

public enum RunStatus { Idle, Running, Cancellable }
public enum RunBackend { Cpu, Gpu }
public enum DebugBottomMode { ThreadStates, PixelColors, Both }
public enum ModalKind { None, Settings, Hotkeys, Textures, Examples, ShaderToyImport, NewFile, LibraryPicker }

public sealed record ShaderImage(byte[] Pixels, int Width, int Height);
public sealed record RunError(string Message, Exception Exception);
public sealed record GpuCapture(float Time, int CanvasW, int CanvasH);

public sealed record EditorState
{
    public IReadOnlyList<ShaderDocument> Documents { get; init; } = Array.Empty<ShaderDocument>();
    public int ActiveIndex { get; init; }
    public int FontSize { get; init; } = 16;

    public ShaderDocument ActiveDocument =>
        ActiveIndex >= 0 && ActiveIndex < Documents.Count ? Documents[ActiveIndex] : null;
}

public sealed record RunState
{
    public RunStatus Status { get; init; } = RunStatus.Idle;
    public RunBackend Backend { get; init; } = RunBackend.Cpu;
    public bool GpuPreviewEnabled { get; init; }
    public bool GpuPaused { get; init; }
    public GpuCapture GpuCaptured { get; init; }
    public ShaderImage Image { get; init; }
    public ExecutionMetrics Metrics { get; init; }
    public DebugViewMode ViewMode { get; init; } = DebugViewMode.Color;
    public string Output { get; init; } = "";
    public RunError Error { get; init; }
}

public sealed record DebugState
{
    public bool IsActive { get; init; }
    public ExecutionTrace Trace { get; init; }
    public int StepIndex { get; init; }
    public IReadOnlySet<int> Breakpoints { get; init; } = new HashSet<int>();
    public int SelectedFrame { get; init; }
    public int InspectedThread { get; init; }
    public int DebugDocumentId { get; init; } = -1;
    public string DebugCode { get; init; } = "";
    public DebugBottomMode BottomMode { get; init; } = DebugBottomMode.ThreadStates;
    public int DebugVertexIndex { get; init; } = -1;

    public TraceStep CurrentStep => Trace?.StepAt(StepIndex);
}

public sealed record UiState
{
    public ModalKind OpenModal { get; init; } = ModalKind.None;
    public bool MenuOpen { get; init; }
    public bool BonzomaticMode { get; init; }
    public bool ImageCollapsed { get; init; }
}

public sealed record AppState
{
    public EditorState Editor { get; init; } = new();
    public RunState Run { get; init; } = new();
    public DebugState Debug { get; init; } = new();
    public UiState Ui { get; init; } = new();

    public static AppState Initial => new();
}
