using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.State;

public enum RunStatus { Idle, Running, Cancellable }
public enum RunBackend { Cpu, Gpu }
public enum DebugBottomMode { ThreadStates, PixelColors, Both }
public enum ModalKind { None, Settings, Hotkeys, Textures, Examples, ShaderToyImport, NewFile, LibraryPicker }

public sealed record ShaderImage(byte[] Pixels, int Width, int Height);
public sealed record RunError(string Message, Exception Exception);
public sealed record FrameCapture(float Time, int CanvasW, int CanvasH);
public sealed record ImmediateEntry(string Expression, string Result, bool IsError, string ImageDataUrl);

public sealed record EditorState
{
    public IReadOnlyList<ShaderDocument> Documents { get; init; } = Array.Empty<ShaderDocument>();
    public int ActiveIndex { get; init; }
    public int FontSize { get; init; } = 16;
    public int NextDocumentId { get; init; }
    public Mesh DefaultMesh { get; init; } = Mesh.CreateCube();
    public bool TabsEnabled { get; init; }

    public ShaderDocument ActiveDocument =>
        ActiveIndex >= 0 && ActiveIndex < Documents.Count ? Documents[ActiveIndex] : null;
}

public sealed record RunState
{
    public RunStatus Status { get; init; } = RunStatus.Idle;
    public RunBackend Backend { get; init; } = RunBackend.Cpu;
    public bool GpuPreviewEnabled { get; init; }
    public bool GpuPaused { get; init; }
    public FrameCapture CapturedFrame { get; init; }
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
    public int SelectedFrame { get; init; }
    public int InspectedThread { get; init; }
    public int DebugDocumentId { get; init; } = -1;
    public string DebugCode { get; init; } = "";
    public DebugBottomMode BottomMode { get; init; } = DebugBottomMode.ThreadStates;
    public int DebugVertexIndex { get; init; } = -1;
    public (int X, int Y)? SavedGroupOffset { get; init; }
    public IReadOnlyList<ImmediateEntry> ImmediateHistory { get; init; } = Array.Empty<ImmediateEntry>();

    public TraceStep CurrentStep => Trace?.StepAt(StepIndex);
}

public sealed record UiState
{
    public ModalKind OpenModal { get; init; } = ModalKind.None;
    public bool MenuOpen { get; init; }
    public bool BonzomaticMode { get; init; }
    public bool ImageCollapsed { get; init; }

    // Bumped on each permalink copy. The toast element is keyed on this, so a
    // copy re-creates it and its CSS fade animation replays.
    public int PermalinkToastKey { get; init; }
}

public sealed record AppState
{
    public EditorState Editor { get; init; } = new();
    public RunState Run { get; init; } = new();
    public DebugState Debug { get; init; } = new();
    public UiState Ui { get; init; } = new();

    public static AppState Initial => new();
}
