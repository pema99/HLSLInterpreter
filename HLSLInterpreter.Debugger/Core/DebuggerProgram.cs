using HLSL;
using HLSLInterpreter.Debugger.Execution;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Core;

// The debugger's MVU runtime: it holds the single application model, runs the
// dispatch loop, applies the update logic (in DebuggerProgram.Update.cs), and
// interprets the resulting commands. Messages are queued and pumped one at a
// time, so ordering is defined and the loop is never re-entered: a message
// dispatched from within an effect (or a JS callback) is simply enqueued for
// the running pump.
public sealed class DebuggerProgram : IDisposable
{
    private readonly DebuggerEffects _effects;
    private readonly Queue<Msg> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;

    public DebuggerModel Model { get; private set; }

    // Raised after every model swap. Subscribers re-select their own slice and
    // decide for themselves whether the change concerns them.
    public event Action Changed;

    public DebuggerProgram(DebuggerModel initial, DebuggerEffects effects)
    {
        Model = initial;
        _effects = effects;
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
                Changed?.Invoke();
                await Execute(command);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    // The command interpreter. A fixed, exhaustive switch over the generic Cmd
    // vocabulary. An effect must never break the pump, so failures are swallowed.
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
        catch
        {
            // An effect must never break the dispatch pump.
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
                command = _effects.SetMeshData(x.Mesh);
                break;

            case RunRequested:
                next = model;
                command = model.Run.Status != RunStatus.Idle
                    ? Cmd.None
                    : _effects.FetchEditorText(code => new RunWithCode(code));
                break;

            case RunCancelRequested:
                next = model;
                command = _effects.CancelRun();
                break;

            case RunWithCode x:
                (next, command) = StartRunWithCode(model, x.Code);
                break;

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
                command = _effects.SetGpuPaused(!model.Run.GpuPaused);
                break;

            case GpuTimeRestartRequested:
                next = model;
                command = _effects.RestartGpuTime();
                break;

            case GpuPreviewToggled x:
                next = model with { Run = model.Run with { GpuPreviewEnabled = x.Enabled } };
                command = _effects.FetchEditorText(code => new RunWithCode(code));
                break;

            case ViewModeChanged x:
                next = model with { Run = model.Run with { ViewMode = x.Mode } };
                command = _effects.RenderViewMode(x.Mode, model.Run.Metrics, model.Run.Image);
                break;

            case DebugRequested:
            {
                var config = ActiveConfig(model);
                if (config.RenderMode == ShaderRenderMode.VertFrag
                    && config.DebugTarget == DebugTarget.Vertex
                    && model.Debug.DebugVertexIndex < 0)
                {
                    var cap = model.Run.CapturedFrame;
                    next = model;
                    command = Cmd.OfMsg(new DebugAtVertexRequested(0,
                        cap?.Time ?? 0f,
                        cap?.CanvasW ?? Math.Max(1, config.WarpX),
                        cap?.CanvasH ?? Math.Max(1, config.WarpY)));
                    break;
                }
                next = model;
                command = model.Editor.ActiveDocument == null
                    ? Cmd.None
                    : _effects.FetchEditorText(code => new DebugWithCode(code));
                break;
            }

            case DebugWithCode x:
            {
                var doc = model.Editor.ActiveDocument;
                if (doc == null) { next = model; command = Cmd.None; break; }
                var captured = model.Run.CapturedFrame;
                bool snapshot = model.Run.GpuPreviewEnabled && captured == null;
                next = model with { Run = BeginRunReset(model.Run, keepCaptured: true) };
                command = _effects.RecordTrace(
                    x.Code, doc.Config, captured, snapshot, next.Debug.DebugVertexIndex, doc.Id, doc.Path);
                break;
            }

            case DebugPixelClicked x:
                next = model;
                command = DebugClickCmd(model, (t, w, h) => new DebugAtPixelRequested(x.Px, x.Py, t, w, h));
                break;

            case DebugVertexClicked x:
                next = model;
                command = DebugClickCmd(model, (t, w, h) => new DebugAtVertexRequested(x.VertexIndex, t, w, h));
                break;

            case DebugAtPixelRequested x:
            {
                var config = ActiveConfig(model);
                int wx = Math.Max(1, config.WarpX);
                int wy = Math.Max(1, config.WarpY);
                next = model with
                {
                    Debug = model.Debug with
                    {
                        SavedGroupOffset = (config.GroupOffsetX, config.GroupOffsetY),
                    }
                };
                next = WithActiveConfig(next, c => c with { GroupOffsetX = x.Px / wx, GroupOffsetY = x.Py / wy });
                next = WithInspectedThread(next, (x.Py % wy) * wx + (x.Px % wx));
                next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(x.Time, x.CanvasW, x.CanvasH) } };
                command = _effects.FetchEditorText(code => new DebugWithCode(code));
                break;
            }

            case DebugAtVertexRequested x:
                (next, command) = DebugAtVertex(model, x.VertexIndex, x.Time, x.CanvasW, x.CanvasH);
                break;

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
                command = Cmd.Batch(_effects.SetEditorReadOnly(true), HighlightCmd(next), ThemeCmd(next));
                break;
            }

            case DebugExitRequested:
                next = ExitDebugCore(model);
                command = Cmd.Batch(
                    _effects.SetEditorReadOnly(false),
                    _effects.HighlightLine(0),
                    ThemeCmd(next),
                    _effects.FetchEditorText(code => new RunWithCode(code)));
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
                            cmds.Add(_effects.ShowModel(docs[i].Id));
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
                command = _effects.SetBreakpoints(doc.Id, breakpoints.ToArray());
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
                var config = ActiveConfig(model);
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
                    : _effects.EvaluateImmediate(
                        x.Expression, debug.DebugCode, debug.StepIndex, ActiveConfig(model),
                        model.Run.CapturedFrame, debug.InspectedThread, debug.DebugVertexIndex, ActiveDocPath(model));
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
                command = Cmd.Batch(_effects.ShowModel(next.Editor.ActiveDocument.Id), HighlightCmd(next));
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
                var cmds = new List<Cmd> { _effects.DisposeModel(docs[x.Index].Id) };
                next = model;
                if (next.Debug.IsActive && docs[x.Index].Id == next.Debug.DebugDocumentId)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(_effects.SetEditorReadOnly(false));
                    cmds.Add(_effects.HighlightLine(0));
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
                    cmds.Add(_effects.ShowModel(next.Editor.ActiveDocument.Id));
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
                command = _effects.PickObjFile();
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
                    _effects.SetMeshData(mesh),
                    _effects.FetchEditorText(code => new RunWithCode(code)));
                break;
            }

            case OpenFileRequested:
                next = model;
                command = _effects.OpenFileDialog();
                break;

            case FileOpened x:
            {
                if (x.Path == null || x.Content == null) { next = model; command = Cmd.None; break; }
                int existing = IndexOfPath(model, x.Path);
                if (existing >= 0)
                {
                    if (existing == model.Editor.ActiveIndex) { next = model; command = Cmd.None; break; }
                    next = model with { Editor = model.Editor with { ActiveIndex = existing } };
                    command = Cmd.Batch(_effects.ShowModel(next.Editor.ActiveDocument.Id), HighlightCmd(next));
                    break;
                }
                next = AddDoc(model, System.IO.Path.GetFileName(x.Path), x.Path);
                int newId = next.Editor.ActiveDocument.Id;
                command = Cmd.Batch(_effects.CreateModel(newId, x.Content), _effects.ShowModel(newId));
                break;
            }

            case FileDropped x:
            {
                int existing = string.IsNullOrEmpty(x.Path) ? -1 : IndexOfPath(model, x.Path);
                if (existing >= 0)
                {
                    if (existing == model.Editor.ActiveIndex) { next = model; command = Cmd.None; break; }
                    next = model with { Editor = model.Editor with { ActiveIndex = existing } };
                    command = Cmd.Batch(_effects.ShowModel(next.Editor.ActiveDocument.Id), HighlightCmd(next));
                    break;
                }
                next = AddDoc(model, x.Name, string.IsNullOrEmpty(x.Path) ? null : x.Path);
                int newId = next.Editor.ActiveDocument.Id;
                command = Cmd.Batch(_effects.CreateModel(newId, x.Content), _effects.ShowModel(newId));
                break;
            }

            case SaveFileRequested x:
                next = model;
                command = _effects.FetchEditorText(code => new SaveFileWithCode(code, x.AsNew));
                break;

            case SaveFileWithCode x:
                next = model;
                command = _effects.SaveFileDialog(x.Code, ActiveDocPath(model), x.AsNew);
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
                command = _effects.FetchEditorText(code => new DownloadWithCode(code));
                break;

            case DownloadWithCode x:
            {
                next = model;
                var doc = model.Editor.ActiveDocument;
                string fileName = string.IsNullOrWhiteSpace(doc?.Name) ? "shader.hlsl" : doc.Name;
                command = _effects.DownloadFile(fileName, x.Code);
                break;
            }

            case NewFileRequested x:
            {
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(_effects.SetEditorReadOnly(false));
                    cmds.Add(_effects.HighlightLine(0));
                }
                Cmd loadCmd;
                (next, loadCmd) = LoadContent(next, x.Name, x.Content);
                cmds.Add(loadCmd);
                if (x.Mode.HasValue) next = WithActiveConfig(next, c => c with { RenderMode = x.Mode.Value });
                if (!string.IsNullOrWhiteSpace(x.FragEntry))
                    next = WithActiveConfig(next, c => c with { FragmentEntryPoint = x.FragEntry });
                if (!string.IsNullOrWhiteSpace(x.VertEntry))
                    next = WithActiveConfig(next, c => c with { VertexEntryPoint = x.VertEntry });
                command = Cmd.Batch(cmds);
                break;
            }

            case ExampleLoaded x:
            {
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(_effects.SetEditorReadOnly(false));
                    cmds.Add(_effects.HighlightLine(0));
                }
                Cmd loadCmd;
                (next, loadCmd) = LoadContent(next, x.Name, x.Code);
                cmds.Add(loadCmd);
                if (x.Mode.HasValue) next = WithActiveConfig(next, c => c with { RenderMode = x.Mode.Value });
                if (!string.IsNullOrWhiteSpace(x.FragEntry))
                    next = WithActiveConfig(next, c => c with { FragmentEntryPoint = x.FragEntry });
                if (!string.IsNullOrWhiteSpace(x.VertEntry))
                    next = WithActiveConfig(next, c => c with { VertexEntryPoint = x.VertEntry });
                if (x.Textures != null) next = WithActiveConfig(next, c => c with { Textures = x.Textures });
                if (x.Samplers != null) next = WithActiveConfig(next, c => c with { Samplers = x.Samplers });
                cmds.Add(Cmd.OfMsg(new RunWithCode(x.Code)));
                command = Cmd.Batch(cmds);
                break;
            }

            case ShaderToyImported x:
            {
                var textures = new List<TextureBinding>();
                for (int i = 0; i < 4; i++)
                {
                    string name = $"iChannel{i}";
                    if (System.Text.RegularExpressions.Regex.IsMatch(x.Hlsl, $@"\b{name}\b"))
                        textures.Add(new TextureBinding { Name = name });
                }
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(_effects.SetEditorReadOnly(false));
                    cmds.Add(_effects.HighlightLine(0));
                }
                Cmd loadCmd;
                (next, loadCmd) = LoadContent(next, "shadertoy.hlsl", x.Hlsl);
                cmds.Add(loadCmd);
                next = WithActiveConfig(next, c => c with
                {
                    RenderMode = ShaderRenderMode.Pixel,
                    FragmentEntryPoint = "frag",
                    Textures = textures,
                    Samplers = Array.Empty<SamplerBinding>(),
                });
                cmds.Add(Cmd.OfMsg(new RunWithCode(x.Hlsl)));
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
                command = _effects.SetEditorFontSize(x.Size);
                break;

            case TexturesSaved x:
                next = WithActiveConfig(
                    model with { Ui = model.Ui with { OpenModal = ModalKind.None } },
                    c => c with { Textures = x.Textures, Samplers = x.Samplers });
                command = _effects.FetchEditorText(code => new RunWithCode(code));
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
                    cmds.Add(_effects.SetEditorReadOnly(false));
                    cmds.Add(_effects.HighlightLine(0));
                }
                bool enabled = !next.Ui.BonzomaticMode;
                next = next with { Ui = next.Ui with { BonzomaticMode = enabled } };
                if (enabled && !next.Run.GpuPreviewEnabled)
                {
                    next = next with { Run = next.Run with { GpuPreviewEnabled = true } };
                    cmds.Add(_effects.FetchEditorText(code => new RunWithCode(code)));
                }
                cmds.Add(ThemeCmd(next));
                command = Cmd.Batch(cmds);
                break;
            }

            case PermalinkCopyRequested x:
                next = model;
                command = _effects.FetchEditorText(code => new PermalinkCopyWithCode(code, x.BaseUrl));
                break;

            case PermalinkCopyWithCode x:
            {
                var config = ActiveConfig(model);
                var settings = new PermalinkSettings(
                    config.FragmentEntryPoint, config.WarpX, config.WarpY,
                    config.GroupOffsetX, config.GroupOffsetY, model.Run.GpuPreviewEnabled,
                    config.RenderMode, config.VertexEntryPoint, config.CpuMode);
                string url = PermalinkCodec.BuildUrl(x.BaseUrl, x.Code, settings);
                next = model with { Ui = model.Ui with { PermalinkToastKey = model.Ui.PermalinkToastKey + 1 } };
                command = _effects.CopyToClipboard(url);
                break;
            }

            default:
                next = model;
                command = Cmd.None;
                break;
        }

        return (next, command);
    }

    // ---- Shared helpers ----

    private (DebuggerModel, Cmd) StartRunWithCode(DebuggerModel m, string code)
    {
        var config = ActiveConfig(m);
        float initialTime = m.Run.CapturedFrame?.Time ?? 0f;
        var next = m with
        {
            Run = BeginRunReset(m.Run, keepCaptured: false),
        };
        if (m.Run.GpuPreviewEnabled)
        {
            next = next with { Run = next.Run with { Backend = RunBackend.Gpu } };
            return (next, _effects.RunGpu(code, config, initialTime, next.Run.GpuPaused, ActiveDocPath(m)));
        }
        return (next, _effects.RunCpu(code, config, ActiveDocPath(m)));
    }

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

    private (DebuggerModel, Cmd) DebugAtVertex(
        DebuggerModel m, int vertexIndex, float time, int canvasW, int canvasH)
    {
        var config = ActiveConfig(m);
        int warpSize = Math.Max(1, config.WarpX * config.WarpY);
        var next = WithInspectedThread(m, vertexIndex % warpSize);
        next = next with { Debug = next.Debug with { DebugVertexIndex = vertexIndex } };
        next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(time, canvasW, canvasH) } };
        if (next.Debug.BottomMode != DebugBottomMode.ThreadStates)
            next = next with { Debug = next.Debug with { BottomMode = DebugBottomMode.ThreadStates } };
        return (next, _effects.FetchEditorText(code => new DebugWithCode(code)));
    }

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

    // Loads content into a document: a new tab when tabs are on, otherwise the
    // single document is reused. Returns the command that gives the document its
    // Monaco model (or replaces the model's content).
    private (DebuggerModel, Cmd) LoadContent(DebuggerModel m, string name, string content)
    {
        if (m.Editor.TabsEnabled || m.Editor.ActiveDocument == null)
        {
            var added = AddDoc(m, name);
            int id = added.Editor.ActiveDocument.Id;
            return (added, Cmd.Batch(_effects.CreateModel(id, content), _effects.ShowModel(id)));
        }
        var renamed = WithActiveDoc(m, d => d with { Name = name });
        return (renamed, _effects.SetModelContent(renamed.Editor.ActiveDocument.Id, content));
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

    private static int IndexOfPath(DebuggerModel m, string path)
    {
        var docs = m.Editor.Documents;
        for (int i = 0; i < docs.Count; i++)
            if (docs[i].Path == path) return i;
        return -1;
    }

    private static ShaderConfig ActiveConfig(DebuggerModel m) =>
        m.Editor.ActiveDocument?.Config ?? new ShaderConfig();

    private static string ActiveDocPath(DebuggerModel m) => m.Editor.ActiveDocument?.Path;

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

    // A canvas click resolves to a debug request. GPU mode must snapshot the
    // live frame (an effect); CPU mode reads the image size from the model, so
    // it is a plain message.
    private Cmd DebugClickCmd(DebuggerModel m, Func<float, int, int, Msg> make)
    {
        if (m.Run.Backend == RunBackend.Gpu)
            return _effects.SnapshotGpuFrame(make);
        return m.Run.Image is { } img
            ? Cmd.OfMsg(make(0, img.Width, img.Height))
            : Cmd.None;
    }

    private Cmd HighlightCmd(DebuggerModel m)
    {
        if (!m.Debug.IsActive) return _effects.HighlightLine(0);
        bool onDoc = m.Editor.ActiveDocument?.Id == m.Debug.DebugDocumentId;
        int line = onDoc && m.Debug.Trace != null ? m.Debug.Trace.LineAt(m.Debug.StepIndex) : 0;
        return _effects.HighlightLine(line);
    }

    private Cmd ThemeCmd(DebuggerModel m) =>
        _effects.SetTheme(m.Ui.BonzomaticMode && !m.Debug.IsActive ? "hlsl-bonzomatic" : "hlsl-dark");
}
