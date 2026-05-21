using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// A Cmd is a description of a side effect: update returns it, the effect runner
// carries it out. A command that produces a result dispatches a follow-up
// message when it completes.
public abstract record Cmd;

// ---- Editor ----
public sealed record FetchEditorText(Func<string, Msg> Then) : Cmd;
public sealed record SetEditorText(string Code) : Cmd;
public sealed record SetEditorFontSize(int Size) : Cmd;
public sealed record SetEditorReadOnly(bool ReadOnly) : Cmd;
public sealed record HighlightEditorLine(int Line) : Cmd;
public sealed record SetEditorBreakpoints(IReadOnlyList<int> Lines) : Cmd;

// ---- Run ----
public sealed record RunCpu(string Code, ShaderConfig Config, string DocPath) : Cmd;
public sealed record RunGpu(string Code, ShaderConfig Config, float InitialTime, bool Paused, string DocPath) : Cmd;
public sealed record CancelRun : Cmd;
public sealed record RenderViewMode(DebugViewMode Mode, ExecutionMetrics Metrics, ShaderImage Image) : Cmd;
public sealed record SetGpuPaused(bool Paused) : Cmd;
public sealed record RestartGpuTime : Cmd;

// ---- Debug ----
public sealed record RecordTrace(
    string Code, ShaderConfig Config, FrameCapture Captured, bool SnapshotGpu,
    int DebugVertexIndex, int DocumentId, string DocPath) : Cmd;
public sealed record EvaluateImmediate(
    string Expression, string DebugCode, int StepIndex, ShaderConfig Config,
    FrameCapture Captured, int InspectedThread, int DebugVertexIndex, string DocPath) : Cmd;

// ---- Files ----
public sealed record OpenFileDialog : Cmd;
public sealed record SaveFileDialog(string Code, string CurrentPath, bool AsNew) : Cmd;
public sealed record DownloadFile(string FileName, string Content) : Cmd;
public sealed record PickObjFile : Cmd;
public sealed record CopyToClipboard(string Text) : Cmd;

// ---- Canvas ----
public sealed record SyncCanvas(CanvasProjection Projection) : Cmd;

// ---- Generic ----
public sealed record DelayThenDispatch(int DelayMs, Msg Message) : Cmd;
