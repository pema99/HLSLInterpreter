using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Core;

// Every event the app can produce is a Msg.
public abstract record Msg;

public enum StepKind { In, Over, Out, InBack, OverBack, OutBack, Continue, ContinueBack }

// ---- Lifecycle ----
public sealed record AppStarted(
    string Url, string FallbackName, string FallbackPath, bool TabsEnabled) : Msg;
public sealed record DefaultMeshLoaded(Mesh Mesh) : Msg;

// ---- Run ----
public sealed record RunRequested : Msg;
public sealed record RunCancelRequested : Msg;
public sealed record RunStarted(string Code) : Msg;
public sealed record RunBecameCancellable : Msg;
public sealed record RunFinished(string Output, RunError Error, ShaderImage Image, ExecutionMetrics Metrics) : Msg;
public sealed record GpuPauseToggled : Msg;
public sealed record GpuTimeRestartRequested : Msg;
public sealed record GpuPreviewToggled(bool Enabled) : Msg;
public sealed record ViewModeChanged(DebugViewMode Mode) : Msg;
public sealed record CanvasResized(int Width, int Height) : Msg;

// ---- Debug ----
public sealed record DebugRequested : Msg;
public sealed record DebugStarted(string Code) : Msg;
public sealed record DebugClicked(DebugTarget Target, int X, int Y) : Msg;
public sealed record DebugAtRequested(DebugTarget Target, int X, int Y, float Time, int CanvasW, int CanvasH) : Msg;
public sealed record DebugTraceRecorded(
    ExecutionTrace Trace, string Code, int DocumentId, FrameCapture Captured, ShaderImage Image) : Msg;
public sealed record DebugExitRequested : Msg;
public sealed record StepRequested(StepKind Kind) : Msg;
public sealed record BreakpointToggled(int Line) : Msg;
public sealed record SelectedFrameChanged(int Frame) : Msg;
public sealed record InspectedThreadChanged(int Thread) : Msg;
public sealed record InspectedPixelChanged(int Px, int Py) : Msg;
public sealed record BottomModeChanged(DebugBottomMode Mode) : Msg;
public sealed record ImmediateEvalRequested(string Expression) : Msg;
public sealed record ImmediateEvalFinished(ImmediateEntry Entry) : Msg;

// ---- Editor and documents ----
public sealed record TabSwitchRequested(int Index) : Msg;
public sealed record TabCloseRequested(int Index) : Msg;
public sealed record TabMoveRequested(int From, int Desired) : Msg;
public sealed record ObjPickRequested : Msg;
public sealed record ObjMeshLoaded(string ObjText) : Msg;
public sealed record OpenFileRequested : Msg;
public sealed record FileOpened(string Name, string Path, string Content) : Msg;
public sealed record SaveFileRequested(bool AsNew) : Msg;
public sealed record SaveFileStarted(string Code, bool AsNew) : Msg;
public sealed record FileSaved(string Path) : Msg;
public sealed record DownloadRequested : Msg;
public sealed record DownloadStarted(string Code) : Msg;
public sealed record DocumentLoaded(
    string Name, string Code, ShaderRenderMode? Mode, string FragEntry, string VertEntry,
    IReadOnlyList<TextureBinding> Textures, IReadOnlyList<SamplerBinding> Samplers, bool Run) : Msg;

// ---- Config ----
public sealed record RenderModeChanged(ShaderRenderMode Mode) : Msg;
public sealed record FragmentEntryChanged(string Entry) : Msg;
public sealed record VertexEntryChanged(string Entry) : Msg;
public sealed record GroupOffsetChanged(int X, int Y) : Msg;
public sealed record WarpSizeChanged(int X, int Y) : Msg;
public sealed record CpuModeChanged(CpuMode Mode) : Msg;
public sealed record DebugTargetChanged(DebugTarget Target) : Msg;
public sealed record FontSizeChanged(int Size) : Msg;
public sealed record TexturesSaved(
    IReadOnlyList<TextureBinding> Textures, IReadOnlyList<SamplerBinding> Samplers) : Msg;

// ---- UI ----
public sealed record ModalRequested(ModalKind Kind) : Msg;
public sealed record ModalDismissed : Msg;
public sealed record BonzomaticToggled : Msg;
public sealed record PermalinkCopyRequested(string BaseUrl) : Msg;
public sealed record PermalinkCopyStarted(string Code, string BaseUrl) : Msg;
