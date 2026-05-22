using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Core;

public enum DebugTarget { Pixel, Vertex }
public enum CpuMode { SingleWarp, FullFrame, FullFrameWithMetrics }

// Per-document shader settings, owned by the tab that uses them.
public sealed record ShaderConfig
{
    public ShaderRenderMode RenderMode { get; init; } = ShaderRenderMode.Pixel;
    public string FragmentEntryPoint { get; init; } = "frag";
    public string VertexEntryPoint { get; init; } = "vert";
    public int WarpX { get; init; } = 16;
    public int WarpY { get; init; } = 16;
    public int GroupOffsetX { get; init; }
    public int GroupOffsetY { get; init; }
    public CpuMode CpuMode { get; init; } = CpuMode.SingleWarp;
    public DebugTarget DebugTarget { get; init; } = DebugTarget.Pixel;
    public Mesh Mesh { get; init; } = Mesh.CreateCube();
    public IReadOnlyList<TextureBinding> Textures { get; init; } = Array.Empty<TextureBinding>();
    public IReadOnlyList<SamplerBinding> Samplers { get; init; } = Array.Empty<SamplerBinding>();
}

public sealed record ShaderDocument
{
    public int Id { get; init; }
    public string Name { get; init; } = "new.hlsl";
    public string Path { get; init; }
    public ShaderConfig Config { get; init; } = new();
    public IReadOnlySet<int> Breakpoints { get; init; } = new HashSet<int>();
}

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

    // The output canvas size in device pixels, pushed by viewport.js on resize
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }

    // The interpreter's output image
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
    public bool BonzomaticMode { get; init; }
    public int PermalinkToastKey { get; init; }
}

public sealed record DebuggerModel
{
    public EditorState Editor { get; init; } = new();
    public RunState Run { get; init; } = new();
    public DebugState Debug { get; init; } = new();
    public UiState Ui { get; init; } = new();
}
