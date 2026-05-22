using HLSL;
using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Services;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Core;

// The main event loop.
public sealed class DebuggerProgram : IDisposable
{
    private readonly DebuggerExecutionEngine _engine;
    private readonly FileDialogService _fileDialogs;
    private readonly Queue<Msg> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;

    public DebuggerModel Model { get; private set; }

    // Raised after every model swap, so subscribers can re-check their slice.
    public event Action ModelChanged;

    public DebuggerProgram(DebuggerExecutionEngine engine, FileDialogService fileDialogs)
    {
        Model = new();
        _engine = engine;
        _fileDialogs = fileDialogs;
    }

    public void Dispatch(Msg message)
    {
        _queue.Enqueue(message);
        if (!_pumping) _ = Pump();
    }

    private async Task Pump()
    {
        _pumping = true;
        try
        {
            while (_queue.Count > 0)
            {
                var message = _queue.Dequeue();
                var (model, command) = Update(Model, message);
                Model = model;
                ModelChanged?.Invoke();
                await Execute(command);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task Execute(Cmd command)
    {
        try
        {
            switch (command)
            {
                case Cmd.BatchCmd b:
                    foreach (var c in b.Commands) await Execute(c);
                    break;
                case Cmd.MsgCmd m:
                    Dispatch(m.Message);
                    break;
                case Cmd.TaskCmd t:
                    Dispatch(await t.Run(_cts.Token));
                    break;
                case Cmd.TaskUnitCmd t:
                    await t.Run(_cts.Token);
                    break;
                case Cmd.EffectCmd e:
                    await e.Run(Dispatch, _cts.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // An effect must never break the event loop.
            Console.WriteLine("[pump] effect threw: " + ex);
        }
    }

    public void Dispose() => _cts.Cancel();

    private (DebuggerModel State, Cmd Command) Update(DebuggerModel model, Msg message)
    {
        DebuggerModel next;
        Cmd command;
        switch (message)
        {
            case AppStarted x:
            {
                var defaults = new ShaderConfig();
                var current = new PermalinkSettings(
                    defaults.FragmentEntryPoint, defaults.WarpX, defaults.WarpY,
                    defaults.GroupOffsetX, defaults.GroupOffsetY, false,
                    defaults.RenderMode, defaults.VertexEntryPoint, defaults.CpuMode);
                var applied = PermalinkCodec.ApplyToSettings(x.Url, current);
                var config = defaults with
                {
                    FragmentEntryPoint = applied.EntryPoint,
                    VertexEntryPoint = applied.VertexEntryPoint,
                    WarpX = applied.WarpX,
                    WarpY = applied.WarpY,
                    GroupOffsetX = applied.GroupOffsetX,
                    GroupOffsetY = applied.GroupOffsetY,
                    RenderMode = applied.ShaderRenderMode,
                    CpuMode = applied.CpuMode,
                };
                var doc = new ShaderDocument
                {
                    Id = 0,
                    Name = string.IsNullOrEmpty(x.FallbackName) ? "new.hlsl" : x.FallbackName,
                    Path = x.FallbackPath,
                    Config = config,
                };
                next = model with
                {
                    Editor = model.Editor with
                    {
                        Documents = new[] { doc },
                        ActiveIndex = 0,
                        NextDocumentId = 1,
                        TabsEnabled = x.TabsEnabled,
                    },
                    Run = model.Run with { GpuPreviewEnabled = applied.GpuPreviewEnabled },
                };
                command = Cmd.None;
                break;
            }

            case DefaultMeshLoaded x:
                next = WithActiveConfig(
                    model with { Editor = model.Editor with { DefaultMesh = x.Mesh } },
                    c => c with { Mesh = x.Mesh });
                command = Cmd.OfValueTask(() => CanvasInterop.SetMeshData(x.Mesh?.Positions, x.Mesh?.Indices));
                break;

            case RunRequested:
                next = model;
                command = model.Run.Status != RunStatus.Idle
                    ? Cmd.None
                    : FetchEditorText(code => new RunStarted(code));
                break;

            case RunCancelRequested:
                next = model;
                command = _engine.CancelRun();
                break;

            case RunStarted x:
            {
                var config = model.Editor.ActiveDocument.Config;
                float initialTime = model.Run.CapturedFrame?.Time ?? 0f;
                next = model with { Run = BeginRunReset(model.Run, keepCaptured: false) };
                if (model.Run.GpuPreviewEnabled)
                {
                    next = next with { Run = next.Run with { Backend = RunBackend.Gpu } };
                    command = _engine.RunGpu(x.Code, config, initialTime, next.Run.GpuPaused, model.Editor.ActiveDocument.Path);
                }
                else
                {
                    command = _engine.RunCpu(
                        x.Code, config, model.Editor.ActiveDocument.Path, model.Run.CanvasWidth, model.Run.CanvasHeight);
                }
                break;
            }

            case RunBecameCancellable:
                next = model with { Run = model.Run with { Status = RunStatus.Cancellable } };
                command = Cmd.None;
                break;

            case RunFinished x:
                next = model with
                {
                    Run = model.Run with
                    {
                        Status = RunStatus.Idle,
                        Output = x.Output ?? "",
                        Error = x.Error,
                        Image = x.Image ?? model.Run.Image,
                        Metrics = x.Metrics,
                    }
                };
                command = Cmd.None;
                break;

            case GpuPauseToggled:
                next = model with { Run = model.Run with { GpuPaused = !model.Run.GpuPaused } };
                command = _engine.SetGpuPaused(!model.Run.GpuPaused);
                break;

            case GpuTimeRestartRequested:
                next = model;
                command = Cmd.OfValueTask(() => GpuInterop.Restart());
                break;

            case GpuPreviewToggled x:
                next = model with { Run = model.Run with { GpuPreviewEnabled = x.Enabled } };
                command = FetchEditorText(code => new RunStarted(code));
                break;

            case ViewModeChanged x:
                next = model with { Run = model.Run with { ViewMode = x.Mode } };
                command = _engine.RenderViewMode(x.Mode, model.Run.Metrics, model.Run.Image);
                break;

            case CanvasResized x:
                next = model with { Run = model.Run with { CanvasWidth = x.Width, CanvasHeight = x.Height } };
                command = Cmd.None;
                break;

            case DebugRequested:
            {
                var config = model.Editor.ActiveDocument.Config;
                if (config.RenderMode == ShaderRenderMode.VertFrag
                    && config.DebugTarget == DebugTarget.Vertex
                    && model.Debug.DebugVertexIndex < 0)
                {
                    var cap = model.Run.CapturedFrame;
                    next = model;
                    command = Cmd.OfMsg(new DebugAtRequested(DebugTarget.Vertex, 0, 0,
                        cap?.Time ?? 0f,
                        cap?.CanvasW ?? Math.Max(1, config.WarpX),
                        cap?.CanvasH ?? Math.Max(1, config.WarpY)));
                    break;
                }
                next = model;
                command = model.Editor.ActiveDocument == null
                    ? Cmd.None
                    : FetchEditorText(code => new DebugStarted(code));
                break;
            }

            case DebugStarted x:
            {
                var doc = model.Editor.ActiveDocument;
                if (doc == null) { next = model; command = Cmd.None; break; }
                var captured = model.Run.CapturedFrame;
                bool snapshot = model.Run.GpuPreviewEnabled && captured == null;
                next = model with { Run = BeginRunReset(model.Run, keepCaptured: true) };
                command = _engine.RecordTrace(
                    x.Code, doc.Config, captured, snapshot, next.Debug.DebugVertexIndex, doc.Id, doc.Path);
                break;
            }

            case DebugClicked x:
            {
                next = model;
                if (model.Run.Backend == RunBackend.Gpu)
                    command = _engine.SnapshotGpuFrame(
                        (t, w, h) => new DebugAtRequested(x.Target, x.X, x.Y, t, w, h));
                else
                    command = model.Run.Image is { } img
                        ? Cmd.OfMsg(new DebugAtRequested(x.Target, x.X, x.Y, 0, img.Width, img.Height))
                        : Cmd.None;
                break;
            }

            case DebugAtRequested x:
            {
                if (x.Target == DebugTarget.Vertex)
                {
                    var vConfig = model.Editor.ActiveDocument.Config;
                    int warpSize = Math.Max(1, vConfig.WarpX * vConfig.WarpY);
                    next = WithInspectedThread(model, x.X % warpSize);
                    next = next with { Debug = next.Debug with { DebugVertexIndex = x.X } };
                    next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(x.Time, x.CanvasW, x.CanvasH) } };
                    if (next.Debug.BottomMode != DebugBottomMode.ThreadStates)
                        next = next with { Debug = next.Debug with { BottomMode = DebugBottomMode.ThreadStates } };
                    command = FetchEditorText(code => new DebugStarted(code));
                    break;
                }
                var config = model.Editor.ActiveDocument.Config;
                int wx = Math.Max(1, config.WarpX);
                int wy = Math.Max(1, config.WarpY);
                next = model with
                {
                    Debug = model.Debug with
                    {
                        SavedGroupOffset = (config.GroupOffsetX, config.GroupOffsetY),
                    }
                };
                next = WithActiveConfig(next, c => c with { GroupOffsetX = x.X / wx, GroupOffsetY = x.Y / wy });
                next = WithInspectedThread(next, (x.Y % wy) * wx + (x.X % wx));
                next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(x.Time, x.CanvasW, x.CanvasH) } };
                command = FetchEditorText(code => new DebugStarted(code));
                break;
            }

            case DebugTraceRecorded x:
            {
                var trace = x.Trace;
                var run = model.Run with
                {
                    Status = RunStatus.Idle,
                    Output = trace.Output ?? "",
                    CapturedFrame = x.Captured,
                };
                bool testFailure = trace.Exception is HLSLRunner.TestFailException;
                bool canDebug = !trace.HasError || (testFailure && trace.Steps.Count > 0);
                if (!canDebug)
                {
                    next = model with { Run = run with { Error = new RunError(trace.ErrorMessage, trace.Exception) } };
                    command = Cmd.None;
                    break;
                }
                if (trace.HasError)
                    run = run with { Error = new RunError(trace.ErrorMessage, trace.Exception) };
                else if (x.Image != null)
                    run = run with { Image = x.Image };
                int stepIndex = trace.HasError ? TraceNavigator.End(trace) : 0;
                var debug = model.Debug with
                {
                    IsActive = true,
                    Trace = trace,
                    StepIndex = stepIndex,
                    DebugDocumentId = x.DocumentId,
                    DebugCode = x.Code,
                    SelectedFrame = 0,
                    ImmediateHistory = Array.Empty<ImmediateEntry>(),
                };
                next = model with { Run = run, Debug = debug };
                command = Cmd.Batch(
                    Cmd.OfValueTask(() => EditorInterop.SetReadOnly(true)), HighlightCmd(next), ThemeCmd(next));
                break;
            }

            case DebugExitRequested:
                next = ExitDebugCore(model);
                command = Cmd.Batch(
                    Cmd.OfValueTask(() => EditorInterop.SetReadOnly(false)),
                    Cmd.OfValueTask(() => EditorInterop.HighlightLine(0)),
                    ThemeCmd(next),
                    FetchEditorText(code => new RunStarted(code)));
                break;

            case StepRequested x:
            {
                var trace = model.Debug.Trace;
                if (trace == null) { next = model; command = Cmd.None; break; }
                var cmds = new List<Cmd>();

                // Stepping always brings the debugged document's tab to the front.
                next = model;
                int debugId = model.Debug.DebugDocumentId;
                if (debugId >= 0 && model.Editor.ActiveDocument?.Id != debugId)
                {
                    var docs = model.Editor.Documents;
                    for (int i = 0; i < docs.Count; i++)
                    {
                        if (docs[i].Id == debugId)
                        {
                            cmds.Add(Cmd.OfValueTask(() => EditorInterop.ShowModel(docs[i].Id)));
                            next = model with { Editor = model.Editor with { ActiveIndex = i } };
                            break;
                        }
                    }
                }

                int from = next.Debug.StepIndex;
                var debugDoc = model.Editor.Documents.FirstOrDefault(d => d.Id == model.Debug.DebugDocumentId);
                IReadOnlySet<int> breakpoints = debugDoc?.Breakpoints ?? new HashSet<int>();
                int index = x.Kind switch
                {
                    StepKind.In => TraceNavigator.Forward(trace, from),
                    StepKind.Over => TraceNavigator.Over(trace, from),
                    StepKind.Out => TraceNavigator.Out(trace, from),
                    StepKind.InBack => TraceNavigator.Back(trace, from),
                    StepKind.OverBack => TraceNavigator.OverBack(trace, from),
                    StepKind.OutBack => TraceNavigator.OutBack(trace, from),
                    StepKind.Continue => TraceNavigator.ToBreakpoint(trace, from, breakpoints),
                    StepKind.ContinueBack => TraceNavigator.ToBreakpointBack(trace, from, breakpoints),
                    _ => from,
                };
                next = next with { Debug = next.Debug with { StepIndex = index, SelectedFrame = 0 } };
                cmds.Add(HighlightCmd(next));
                command = Cmd.Batch(cmds);
                break;
            }

            case BreakpointToggled x:
            {
                var doc = model.Editor.ActiveDocument;
                if (doc == null) { next = model; command = Cmd.None; break; }
                var breakpoints = new HashSet<int>(doc.Breakpoints);
                if (!breakpoints.Add(x.Line)) breakpoints.Remove(x.Line);
                next = WithActiveDoc(model, d => d with { Breakpoints = breakpoints });
                command = Cmd.OfValueTask(() => EditorInterop.SetBreakpoints(doc.Id, breakpoints.ToArray()));
                break;
            }

            case SelectedFrameChanged x:
                next = model with { Debug = model.Debug with { SelectedFrame = Math.Max(0, x.Frame) } };
                command = Cmd.None;
                break;

            case InspectedThreadChanged x:
                next = WithInspectedThread(model, x.Thread);
                command = Cmd.None;
                break;

            case InspectedPixelChanged x:
            {
                var config = model.Editor.ActiveDocument.Config;
                next = config.WarpX <= 0 ? model : WithInspectedThread(model, x.Py * config.WarpX + x.Px);
                command = Cmd.None;
                break;
            }

            case BottomModeChanged x:
                next = model with { Debug = model.Debug with { BottomMode = x.Mode } };
                command = Cmd.None;
                break;

            case ImmediateEvalRequested x:
            {
                var debug = model.Debug;
                next = model;
                command = debug.Trace == null || debug.StepIndex < 0 || string.IsNullOrEmpty(debug.DebugCode)
                    ? Cmd.None
                    : _engine.EvaluateImmediate(
                        x.Expression, debug.DebugCode, debug.StepIndex, model.Editor.ActiveDocument.Config,
                        model.Run.CapturedFrame, debug.InspectedThread, debug.DebugVertexIndex, model.Editor.ActiveDocument.Path);
                break;
            }

            case ImmediateEvalFinished x:
                next = model with
                {
                    Debug = model.Debug with
                    {
                        ImmediateHistory = model.Debug.ImmediateHistory.Append(x.Entry).ToArray(),
                    }
                };
                command = Cmd.None;
                break;

            case TabSwitchRequested x:
                if (x.Index < 0 || x.Index >= model.Editor.Documents.Count
                    || x.Index == model.Editor.ActiveIndex)
                {
                    next = model;
                    command = Cmd.None;
                    break;
                }
                next = model with { Editor = model.Editor with { ActiveIndex = x.Index } };
                command = Cmd.Batch(Cmd.OfValueTask(() => EditorInterop.ShowModel(next.Editor.ActiveDocument.Id)), HighlightCmd(next));
                break;

            case TabCloseRequested x:
            {
                var docs = model.Editor.Documents;
                if (docs.Count <= 1 || x.Index < 0 || x.Index >= docs.Count)
                {
                    next = model;
                    command = Cmd.None;
                    break;
                }
                var cmds = new List<Cmd> { Cmd.OfValueTask(() => EditorInterop.DisposeModel(docs[x.Index].Id)) };
                next = model;
                if (next.Debug.IsActive && docs[x.Index].Id == next.Debug.DebugDocumentId)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.SetReadOnly(false)));
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.HighlightLine(0)));
                }
                bool activeChanges = x.Index == next.Editor.ActiveIndex;
                var documents = next.Editor.Documents.ToList();
                documents.RemoveAt(x.Index);
                int active = next.Editor.ActiveIndex;
                if (x.Index < active || active >= documents.Count) active--;
                next = next with
                {
                    Editor = next.Editor with
                    {
                        Documents = documents,
                        ActiveIndex = Math.Clamp(active, 0, documents.Count - 1),
                    }
                };
                if (activeChanges)
                {
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.ShowModel(next.Editor.ActiveDocument.Id)));
                    cmds.Add(HighlightCmd(next));
                }
                command = Cmd.Batch(cmds);
                break;
            }

            case TabMoveRequested x:
            {
                var editor = model.Editor;
                if (x.From < 0 || x.From >= editor.Documents.Count
                    || x.Desired == x.From || x.Desired == x.From + 1)
                {
                    next = model;
                    command = Cmd.None;
                    break;
                }
                var documents = editor.Documents.ToList();
                var active = documents[editor.ActiveIndex];
                var moving = documents[x.From];
                documents.RemoveAt(x.From);
                documents.Insert(x.Desired > x.From ? x.Desired - 1 : x.Desired, moving);
                next = model with
                {
                    Editor = editor with
                    {
                        Documents = documents,
                        ActiveIndex = documents.IndexOf(active),
                    }
                };
                command = Cmd.None;
                break;
            }

            case ObjPickRequested:
                next = model;
                command = Cmd.OfValueTask(() => BrowserInterop.PickObj());
                break;

            case ObjMeshLoaded x:
            {
                if (string.IsNullOrWhiteSpace(x.ObjText))
                {
                    next = model;
                    command = Cmd.None;
                    break;
                }
                Mesh mesh;
                try { mesh = Mesh.ParseObj(x.ObjText); }
                catch (Exception ex)
                {
                    next = model with
                    {
                        Run = model.Run with { Error = new RunError("Failed to load OBJ: " + ex.Message, ex) }
                    };
                    command = Cmd.None;
                    break;
                }
                next = WithActiveConfig(model, c => c with { Mesh = mesh });
                command = Cmd.Batch(
                    Cmd.OfValueTask(() => CanvasInterop.SetMeshData(mesh?.Positions, mesh?.Indices)),
                    FetchEditorText(code => new RunStarted(code)));
                break;
            }

            case OpenFileRequested:
                next = model;
                command = Cmd.OfTask(async () =>
                {
                    var (path, content) = await _fileDialogs.OpenFile();
                    return (Msg)new FileOpened(System.IO.Path.GetFileName(path), path, content);
                });
                break;

            case FileOpened x:
            {
                if (x.Content == null) { next = model; command = Cmd.None; break; }
                int existing = -1;
                if (!string.IsNullOrEmpty(x.Path))
                {
                    var docs = model.Editor.Documents;
                    for (int i = 0; i < docs.Count; i++)
                        if (docs[i].Path == x.Path) { existing = i; break; }
                }
                if (existing >= 0)
                {
                    if (existing == model.Editor.ActiveIndex) { next = model; command = Cmd.None; break; }
                    next = model with { Editor = model.Editor with { ActiveIndex = existing } };
                    command = Cmd.Batch(Cmd.OfValueTask(() => EditorInterop.ShowModel(next.Editor.ActiveDocument.Id)), HighlightCmd(next));
                    break;
                }
                next = AddDoc(model, x.Name, string.IsNullOrEmpty(x.Path) ? null : x.Path);
                int newId = next.Editor.ActiveDocument.Id;
                command = Cmd.Batch(
                    Cmd.OfValueTask(() => EditorInterop.CreateModel(newId, x.Content)),
                    Cmd.OfValueTask(() => EditorInterop.ShowModel(newId)));
                break;
            }

            case SaveFileRequested x:
                next = model;
                command = FetchEditorText(code => new SaveFileStarted(code, x.AsNew));
                break;

            case SaveFileStarted x:
                next = model;
                command = Cmd.OfEffect(async dispatch =>
                {
                    string path = x.AsNew
                        ? await _fileDialogs.SaveFileAs(x.Code)
                        : await _fileDialogs.SaveFile(x.Code, model.Editor.ActiveDocument.Path);
                    if (path != null) dispatch(new FileSaved(path));
                });
                break;

            case FileSaved x:
                next = WithActiveDoc(model, d => d with
                {
                    Path = x.Path,
                    Name = System.IO.Path.GetFileName(x.Path),
                });
                command = Cmd.None;
                break;

            case DownloadRequested:
                next = model;
                command = FetchEditorText(code => new DownloadStarted(code));
                break;

            case DownloadStarted x:
            {
                next = model;
                var doc = model.Editor.ActiveDocument;
                string fileName = string.IsNullOrWhiteSpace(doc?.Name) ? "shader.hlsl" : doc.Name;
                command = Cmd.OfValueTask(() => BrowserInterop.DownloadTextFile(fileName, x.Code));
                break;
            }

            case DocumentLoaded x:
            {
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.SetReadOnly(false)));
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.HighlightLine(0)));
                }
                // New tab when tabs are on, otherwise reuse the single document.
                if (next.Editor.TabsEnabled || next.Editor.ActiveDocument == null)
                {
                    next = AddDoc(next, x.Name);
                    int id = next.Editor.ActiveDocument.Id;
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.CreateModel(id, x.Code)));
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.ShowModel(id)));
                }
                else
                {
                    next = WithActiveDoc(next, d => d with { Name = x.Name });
                    int id = next.Editor.ActiveDocument.Id;
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.SetModelContent(id, x.Code)));
                }
                if (x.Mode.HasValue) next = WithActiveConfig(next, c => c with { RenderMode = x.Mode.Value });
                if (!string.IsNullOrWhiteSpace(x.FragEntry))
                    next = WithActiveConfig(next, c => c with { FragmentEntryPoint = x.FragEntry });
                if (!string.IsNullOrWhiteSpace(x.VertEntry))
                    next = WithActiveConfig(next, c => c with { VertexEntryPoint = x.VertEntry });
                if (x.Textures != null) next = WithActiveConfig(next, c => c with { Textures = x.Textures });
                if (x.Samplers != null) next = WithActiveConfig(next, c => c with { Samplers = x.Samplers });
                if (x.Run) cmds.Add(Cmd.OfMsg(new RunStarted(x.Code)));
                command = Cmd.Batch(cmds);
                break;
            }

            case RenderModeChanged x:
                next = WithActiveConfig(model, c => c with { RenderMode = x.Mode });
                command = Cmd.None;
                break;

            case FragmentEntryChanged x:
                next = WithActiveConfig(model, c => c with { FragmentEntryPoint = x.Entry });
                command = Cmd.None;
                break;

            case VertexEntryChanged x:
                next = WithActiveConfig(model, c => c with { VertexEntryPoint = x.Entry });
                command = Cmd.None;
                break;

            case GroupOffsetChanged x:
                next = WithActiveConfig(model, c => c with { GroupOffsetX = x.X, GroupOffsetY = x.Y });
                command = Cmd.None;
                break;

            case WarpSizeChanged x:
                next = WithActiveConfig(model, c => c with
                {
                    WarpX = Math.Max(1, x.X),
                    WarpY = Math.Max(1, x.Y),
                });
                next = WithInspectedThread(next, next.Debug.InspectedThread);
                command = Cmd.None;
                break;

            case CpuModeChanged x:
                next = WithActiveConfig(model, c => c with { CpuMode = x.Mode });
                command = Cmd.None;
                break;

            case DebugTargetChanged x:
                next = WithActiveConfig(model, c => c with { DebugTarget = x.Target });
                command = Cmd.None;
                break;

            case FontSizeChanged x:
                next = model with { Editor = model.Editor with { FontSize = x.Size } };
                command = Cmd.OfValueTask(() => EditorInterop.SetFontSize(x.Size));
                break;

            case TexturesSaved x:
                next = WithActiveConfig(
                    model with { Ui = model.Ui with { OpenModal = ModalKind.None } },
                    c => c with { Textures = x.Textures, Samplers = x.Samplers });
                command = FetchEditorText(code => new RunStarted(code));
                break;

            case ModalRequested x:
                next = model with { Ui = model.Ui with { OpenModal = x.Kind } };
                command = Cmd.None;
                break;

            case ModalDismissed:
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                command = Cmd.None;
                break;

            case BonzomaticToggled:
            {
                next = model;
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.SetReadOnly(false)));
                    cmds.Add(Cmd.OfValueTask(() => EditorInterop.HighlightLine(0)));
                }
                bool enabled = !next.Ui.BonzomaticMode;
                next = next with { Ui = next.Ui with { BonzomaticMode = enabled } };
                if (enabled && !next.Run.GpuPreviewEnabled)
                {
                    next = next with { Run = next.Run with { GpuPreviewEnabled = true } };
                    cmds.Add(FetchEditorText(code => new RunStarted(code)));
                }
                cmds.Add(ThemeCmd(next));
                command = Cmd.Batch(cmds);
                break;
            }

            case PermalinkCopyRequested x:
                next = model;
                command = FetchEditorText(code => new PermalinkCopyStarted(code, x.BaseUrl));
                break;

            case PermalinkCopyStarted x:
            {
                var config = model.Editor.ActiveDocument.Config;
                var settings = new PermalinkSettings(
                    config.FragmentEntryPoint, config.WarpX, config.WarpY,
                    config.GroupOffsetX, config.GroupOffsetY, model.Run.GpuPreviewEnabled,
                    config.RenderMode, config.VertexEntryPoint, config.CpuMode);
                string url = PermalinkCodec.BuildUrl(x.BaseUrl, x.Code, settings);
                next = model with { Ui = model.Ui with { PermalinkToastKey = model.Ui.PermalinkToastKey + 1 } };
                command = Cmd.OfValueTask(() => BrowserInterop.CopyToClipboard(url));
                break;
            }

            default:
                next = model;
                command = Cmd.None;
                break;
        }

        return (next, command);
    }

    // Helpers
    private static RunState BeginRunReset(RunState r, bool keepCaptured) => r with
    {
        Status = RunStatus.Running,
        Backend = RunBackend.Cpu,
        Error = null,
        Output = "",
        Metrics = null,
        CapturedFrame = keepCaptured ? r.CapturedFrame : null,
        ViewMode = DebugViewMode.Color,
    };

    private static DebuggerModel ExitDebugCore(DebuggerModel m)
    {
        var saved = m.Debug.SavedGroupOffset;
        var next = m with
        {
            Debug = m.Debug with
            {
                IsActive = false,
                Trace = null,
                StepIndex = 0,
                DebugDocumentId = -1,
                DebugVertexIndex = -1,
                SelectedFrame = 0,
                ImmediateHistory = Array.Empty<ImmediateEntry>(),
                SavedGroupOffset = null,
            },
            Run = m.Run with { Backend = RunBackend.Cpu },
        };
        if (saved is { } offset)
            next = WithActiveConfig(next, c => c with { GroupOffsetX = offset.X, GroupOffsetY = offset.Y });
        return next;
    }

    private static DebuggerModel AddDoc(DebuggerModel m, string name, string path = null)
    {
        var doc = new ShaderDocument
        {
            Id = m.Editor.NextDocumentId,
            Name = name,
            Path = path,
            Config = new ShaderConfig { Mesh = m.Editor.DefaultMesh },
        };
        var documents = m.Editor.Documents.Append(doc).ToArray();
        return m with
        {
            Editor = m.Editor with
            {
                Documents = documents,
                ActiveIndex = documents.Length - 1,
                NextDocumentId = m.Editor.NextDocumentId + 1,
            }
        };
    }

    private static DebuggerModel WithActiveConfig(DebuggerModel m, Func<ShaderConfig, ShaderConfig> update)
    {
        var doc = m.Editor.ActiveDocument;
        if (doc == null) return m;
        var documents = m.Editor.Documents.ToArray();
        documents[m.Editor.ActiveIndex] = doc with { Config = update(doc.Config) };
        return m with { Editor = m.Editor with { Documents = documents } };
    }

    private static DebuggerModel WithActiveDoc(DebuggerModel m, Func<ShaderDocument, ShaderDocument> update)
    {
        var doc = m.Editor.ActiveDocument;
        if (doc == null) return m;
        var documents = m.Editor.Documents.ToArray();
        documents[m.Editor.ActiveIndex] = update(doc);
        return m with { Editor = m.Editor with { Documents = documents } };
    }

    private static DebuggerModel WithInspectedThread(DebuggerModel m, int thread)
    {
        var config = m.Editor.ActiveDocument?.Config;
        int max = config == null ? 0 : Math.Max(0, config.WarpX * config.WarpY - 1);
        return m with { Debug = m.Debug with { InspectedThread = Math.Clamp(thread, 0, max) } };
    }

    private Cmd HighlightCmd(DebuggerModel m)
    {
        if (!m.Debug.IsActive) return Cmd.OfValueTask(() => EditorInterop.HighlightLine(0));
        bool onDoc = m.Editor.ActiveDocument?.Id == m.Debug.DebugDocumentId;
        int line = onDoc && m.Debug.Trace != null ? m.Debug.Trace.LineAt(m.Debug.StepIndex) : 0;
        return Cmd.OfValueTask(() => EditorInterop.HighlightLine(line));
    }

    private Cmd ThemeCmd(DebuggerModel m) =>
        Cmd.OfValueTask(() => EditorInterop.SetTheme(
            m.Ui.BonzomaticMode && !m.Debug.IsActive ? "hlsl-bonzomatic" : "hlsl-dark"));

    private Cmd FetchEditorText(Func<string, Msg> then) =>
        Cmd.OfTask(async () => then(await EditorInterop.GetValue()));
}
