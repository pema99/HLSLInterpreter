using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// The pure update function. It maps (model, message) to the next model plus the
// side effects to run. It never performs an effect itself: anything impure is
// described as a Cmd and handed to the effect runner. A canvas sync is appended
// after every message so the JS canvas state always tracks the model.
public static class Update
{
    private static readonly IReadOnlyList<Cmd> None = Array.Empty<Cmd>();

    public static (AppState State, IReadOnlyList<Cmd> Commands) Run(AppState model, Msg message)
    {
        AppState next;
        IReadOnlyList<Cmd> commands;
        switch (message)
        {
            case AppStarted x:
            {
                string code = string.IsNullOrEmpty(x.FallbackCode) ? DefaultShader.Code : x.FallbackCode;
                var codeParam = PermalinkCodec.GetQueryParam(x.Url, "c");
                if (!string.IsNullOrEmpty(codeParam))
                {
                    try { code = PermalinkCodec.DecompressCode(codeParam); }
                    catch { }
                }
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
                    Code = code,
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
                commands = None;
                break;
            }

            case CanvasReady:
                next = model;
                commands = None;
                break;

            case DefaultMeshLoaded x:
                next = WithActiveConfig(
                    model with { Editor = model.Editor with { DefaultMesh = x.Mesh } },
                    c => c with { Mesh = x.Mesh });
                commands = None;
                break;

            case RunRequested:
                next = model;
                commands = model.Run.Status != RunStatus.Idle
                    ? None
                    : Cmds(new FetchEditorText(code => new RunWithCode(code)));
                break;

            case RunCancelRequested:
                next = model;
                commands = Cmds(new CancelRun());
                break;

            case RunWithCode x:
                (next, commands) = StartRunWithCode(SyncActiveCode(model, x.Code), x.Code);
                break;

            case RunBecameCancellable:
                next = model with { Run = model.Run with { Status = RunStatus.Cancellable } };
                commands = None;
                break;

            case RunFinished x:
                next = model with
                {
                    Run = model.Run with
                    {
                        Status = RunStatus.Idle,
                        Output = x.Output ?? "",
                        Error = x.Error,
                        Image = x.Image,
                        Metrics = x.Metrics,
                    }
                };
                commands = None;
                break;

            case GpuPauseToggled:
                next = model with { Run = model.Run with { GpuPaused = !model.Run.GpuPaused } };
                commands = Cmds(new SetGpuPaused(!model.Run.GpuPaused));
                break;

            case GpuTimeRestartRequested:
                next = model;
                commands = Cmds(new RestartGpuTime());
                break;

            case GpuPreviewToggled x:
                next = model with { Run = model.Run with { GpuPreviewEnabled = x.Enabled } };
                commands = Cmds(new FetchEditorText(code => new RunWithCode(code)));
                break;

            case ViewModeChanged x:
                next = model with { Run = model.Run with { ViewMode = x.Mode } };
                commands = Cmds(new RenderViewMode(x.Mode, model.Run.Metrics, model.Run.Image));
                break;

            case ImageCollapseToggled:
                next = model with { Ui = model.Ui with { ImageCollapsed = !model.Ui.ImageCollapsed } };
                commands = None;
                break;

            case DebugRequested:
            {
                var config = ActiveConfig(model);
                if (config.RenderMode == ShaderRenderMode.VertFrag
                    && config.DebugTarget == DebugTarget.Vertex
                    && model.Debug.DebugVertexIndex < 0)
                {
                    var cap = model.Run.CapturedFrame;
                    (next, commands) = DebugAtVertex(model, 0,
                        cap?.Time ?? 0f,
                        cap?.CanvasW ?? Math.Max(1, config.WarpX),
                        cap?.CanvasH ?? Math.Max(1, config.WarpY));
                    break;
                }
                next = model;
                commands = model.Editor.ActiveDocument == null
                    ? None
                    : Cmds(new FetchEditorText(code => new DebugWithCode(code)));
                break;
            }

            case DebugWithCode x:
            {
                next = SyncActiveCode(model, x.Code);
                var doc = next.Editor.ActiveDocument;
                if (doc == null) { commands = None; break; }
                var captured = next.Run.CapturedFrame;
                bool snapshot = next.Run.GpuPreviewEnabled && captured == null;
                next = next with { Run = BeginRunReset(next.Run, keepCaptured: true) };
                commands = Cmds(new RecordTrace(
                    x.Code, doc.Config, captured, snapshot, next.Debug.DebugVertexIndex, doc.Id, doc.Path));
                break;
            }

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
                commands = Cmds(new FetchEditorText(code => new DebugWithCode(code)));
                break;
            }

            case DebugAtVertexRequested x:
                (next, commands) = DebugAtVertex(model, x.VertexIndex, x.Time, x.CanvasW, x.CanvasH);
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
                    commands = None;
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
                commands = Cmds(new SetEditorReadOnly(true), HighlightCmd(next));
                break;
            }

            case DebugExitRequested:
                next = ExitDebugCore(model);
                commands = Cmds(
                    new SetEditorReadOnly(false),
                    new HighlightEditorLine(0),
                    new FetchEditorText(code => new RunWithCode(code)));
                break;

            case StepRequested x:
            {
                var trace = model.Debug.Trace;
                if (trace == null) { next = model; commands = None; break; }
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
                            cmds.Add(new SetEditorText(docs[i].Code));
                            next = model with { Editor = model.Editor with { ActiveIndex = i } };
                            break;
                        }
                    }
                }

                int from = next.Debug.StepIndex;
                var breakpoints = next.Debug.Breakpoints;
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
                commands = cmds;
                break;
            }

            case BreakpointToggled x:
            {
                var breakpoints = new HashSet<int>(model.Debug.Breakpoints);
                if (!breakpoints.Add(x.Line)) breakpoints.Remove(x.Line);
                next = model with { Debug = model.Debug with { Breakpoints = breakpoints } };
                commands = Cmds(new SetEditorBreakpoints(breakpoints.ToArray()));
                break;
            }

            case SelectedFrameChanged x:
                next = model with { Debug = model.Debug with { SelectedFrame = Math.Max(0, x.Frame) } };
                commands = None;
                break;

            case InspectedThreadChanged x:
                next = WithInspectedThread(model, x.Thread);
                commands = None;
                break;

            case InspectedPixelChanged x:
            {
                var config = ActiveConfig(model);
                next = config.WarpX <= 0 ? model : WithInspectedThread(model, x.Py * config.WarpX + x.Px);
                commands = None;
                break;
            }

            case BottomModeChanged x:
                next = model with { Debug = model.Debug with { BottomMode = x.Mode } };
                commands = None;
                break;

            case ImmediateEvalRequested x:
            {
                var debug = model.Debug;
                next = model;
                commands = debug.Trace == null || debug.StepIndex < 0 || string.IsNullOrEmpty(debug.DebugCode)
                    ? None
                    : Cmds(new EvaluateImmediate(
                        x.Expression, debug.DebugCode, debug.StepIndex, ActiveConfig(model),
                        model.Run.CapturedFrame, debug.InspectedThread, debug.DebugVertexIndex, ActiveDocPath(model)));
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
                commands = None;
                break;

            case TabSwitchRequested x:
                next = model;
                commands = Cmds(new FetchEditorText(code => new TabSwitched(code, x.Index)));
                break;

            case TabSwitched x:
                next = SyncActiveCode(model, x.CurrentCode);
                if (x.Index < 0 || x.Index >= next.Editor.Documents.Count || x.Index == next.Editor.ActiveIndex)
                {
                    commands = None;
                    break;
                }
                next = next with { Editor = next.Editor with { ActiveIndex = x.Index } };
                commands = Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next));
                break;

            case TabCloseRequested x:
            {
                var docs = model.Editor.Documents;
                if (docs.Count <= 1 || x.Index < 0 || x.Index >= docs.Count)
                {
                    next = model;
                    commands = None;
                    break;
                }
                var cmds = new List<Cmd>();
                next = model;
                if (next.Debug.IsActive && docs[x.Index].Id == next.Debug.DebugDocumentId)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(new SetEditorReadOnly(false));
                    cmds.Add(new HighlightEditorLine(0));
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
                    cmds.Add(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""));
                commands = cmds;
                break;
            }

            case TabMoveRequested x:
            {
                var editor = model.Editor;
                if (x.From < 0 || x.From >= editor.Documents.Count
                    || x.Desired == x.From || x.Desired == x.From + 1)
                {
                    next = model;
                    commands = None;
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
                commands = None;
                break;
            }

            case ObjPickRequested:
                next = model;
                commands = Cmds(new PickObjFile());
                break;

            case ObjMeshLoaded x:
            {
                if (string.IsNullOrWhiteSpace(x.ObjText))
                {
                    next = model;
                    commands = None;
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
                    commands = None;
                    break;
                }
                next = WithActiveConfig(model, c => c with { Mesh = mesh });
                commands = Cmds(new FetchEditorText(code => new RunWithCode(code)));
                break;
            }

            case OpenFileRequested:
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                commands = Cmds(new OpenFileDialog());
                break;

            case FileOpened x:
                next = model;
                commands = Cmds(new FetchEditorText(code => new FileOpenedWithCode(code, x.Path, x.Content)));
                break;

            case FileOpenedWithCode x:
                next = SyncActiveCode(model, x.CurrentCode);
                if (x.Path == null || x.Content == null) { commands = None; break; }
                int openExisting = IndexOfPath(next, x.Path);
                if (openExisting >= 0)
                {
                    if (openExisting == next.Editor.ActiveIndex) { commands = None; break; }
                    next = next with { Editor = next.Editor with { ActiveIndex = openExisting } };
                    commands = Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next));
                    break;
                }
                next = AddDoc(next, System.IO.Path.GetFileName(x.Path), x.Content, x.Path);
                commands = Cmds(new SetEditorText(x.Content));
                break;

            case FileDropped x:
                next = model;
                commands = Cmds(new FetchEditorText(
                    code => new FileDroppedWithCode(code, x.Name, x.Content, x.Path)));
                break;

            case FileDroppedWithCode x:
                next = SyncActiveCode(model, x.CurrentCode);
                int dropExisting = string.IsNullOrEmpty(x.Path) ? -1 : IndexOfPath(next, x.Path);
                if (dropExisting >= 0)
                {
                    if (dropExisting == next.Editor.ActiveIndex) { commands = None; break; }
                    next = next with { Editor = next.Editor with { ActiveIndex = dropExisting } };
                    commands = Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next));
                    break;
                }
                next = AddDoc(next, x.Name, x.Content, string.IsNullOrEmpty(x.Path) ? null : x.Path);
                commands = Cmds(new SetEditorText(x.Content));
                break;

            case SaveFileRequested x:
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                commands = Cmds(new FetchEditorText(code => new SaveFileWithCode(code, x.AsNew)));
                break;

            case SaveFileWithCode x:
                next = SyncActiveCode(model, x.Code);
                commands = Cmds(new SaveFileDialog(x.Code, ActiveDocPath(model), x.AsNew));
                break;

            case FileSaved x:
                next = WithActiveDoc(model, d => d with
                {
                    Path = x.Path,
                    Name = System.IO.Path.GetFileName(x.Path),
                });
                commands = None;
                break;

            case DownloadRequested:
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                commands = Cmds(new FetchEditorText(code => new DownloadWithCode(code)));
                break;

            case DownloadWithCode x:
            {
                next = SyncActiveCode(model, x.Code);
                var doc = next.Editor.ActiveDocument;
                string fileName = string.IsNullOrWhiteSpace(doc?.Name) ? "shader.hlsl" : doc.Name;
                commands = Cmds(new DownloadFile(fileName, x.Code));
                break;
            }

            case NewFileRequested x:
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                commands = Cmds(new FetchEditorText(code => new ContentLoaded(
                    code, x.Name, x.Content, x.Mode, x.FragEntry, x.VertEntry, null, null, false)));
                break;

            case ExampleLoaded x:
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                commands = Cmds(new FetchEditorText(code => new ContentLoaded(
                    code, x.Name, x.Code, x.Mode, x.FragEntry, x.VertEntry, x.Textures, x.Samplers, true)));
                break;

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
                commands = Cmds(new FetchEditorText(code => new ContentLoaded(
                    code, "shadertoy.hlsl", x.Hlsl, ShaderRenderMode.Pixel, "frag", null,
                    textures, Array.Empty<SamplerBinding>(), true)));
                break;
            }

            case ContentLoaded x:
            {
                next = SyncActiveCode(model, x.CurrentCode);
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(new SetEditorReadOnly(false));
                    cmds.Add(new HighlightEditorLine(0));
                }
                next = next.Editor.TabsEnabled || next.Editor.ActiveDocument == null
                    ? AddDoc(next, x.Name, x.Content)
                    : WithActiveDoc(next, d => d with { Name = x.Name, Code = x.Content });
                if (x.Mode.HasValue) next = WithActiveConfig(next, c => c with { RenderMode = x.Mode.Value });
                if (!string.IsNullOrWhiteSpace(x.FragEntry))
                    next = WithActiveConfig(next, c => c with { FragmentEntryPoint = x.FragEntry });
                if (!string.IsNullOrWhiteSpace(x.VertEntry))
                    next = WithActiveConfig(next, c => c with { VertexEntryPoint = x.VertEntry });
                if (x.Textures != null) next = WithActiveConfig(next, c => c with { Textures = x.Textures });
                if (x.Samplers != null) next = WithActiveConfig(next, c => c with { Samplers = x.Samplers });

                string content = next.Editor.ActiveDocument?.Code ?? "";
                cmds.Add(new SetEditorText(content));
                if (x.Run)
                {
                    var (ranModel, runCmds) = StartRunWithCode(next, content);
                    next = ranModel;
                    cmds.AddRange(runCmds);
                }
                commands = cmds;
                break;
            }

            case RenderModeChanged x:
                next = WithActiveConfig(model, c => c with { RenderMode = x.Mode });
                commands = None;
                break;

            case FragmentEntryChanged x:
                next = WithActiveConfig(model, c => c with { FragmentEntryPoint = x.Entry });
                commands = None;
                break;

            case VertexEntryChanged x:
                next = WithActiveConfig(model, c => c with { VertexEntryPoint = x.Entry });
                commands = None;
                break;

            case GroupOffsetChanged x:
                next = WithActiveConfig(model, c => c with { GroupOffsetX = x.X, GroupOffsetY = x.Y });
                commands = None;
                break;

            case WarpSizeChanged x:
                next = WithActiveConfig(model, c => c with
                {
                    WarpX = Math.Max(1, x.X),
                    WarpY = Math.Max(1, x.Y),
                });
                next = WithInspectedThread(next, next.Debug.InspectedThread);
                commands = None;
                break;

            case CpuModeChanged x:
                next = WithActiveConfig(model, c => c with { CpuMode = x.Mode });
                commands = None;
                break;

            case DebugTargetChanged x:
                next = WithActiveConfig(model, c => c with { DebugTarget = x.Target });
                commands = None;
                break;

            case FontSizeChanged x:
                next = model with { Editor = model.Editor with { FontSize = x.Size } };
                commands = Cmds(new SetEditorFontSize(x.Size));
                break;

            case TexturesSaved x:
                next = WithActiveConfig(
                    model with { Ui = model.Ui with { OpenModal = ModalKind.None } },
                    c => c with { Textures = x.Textures, Samplers = x.Samplers });
                commands = Cmds(new FetchEditorText(code => new RunWithCode(code)));
                break;

            case ModalRequested x:
                next = model with { Ui = model.Ui with { OpenModal = x.Kind, MenuOpen = false } };
                commands = None;
                break;

            case ModalDismissed:
                next = model with { Ui = model.Ui with { OpenModal = ModalKind.None } };
                commands = None;
                break;

            case MenuToggled:
                next = model with { Ui = model.Ui with { MenuOpen = !model.Ui.MenuOpen } };
                commands = None;
                break;

            case MenuClosed:
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                commands = None;
                break;

            case BonzomaticToggled:
            {
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                var cmds = new List<Cmd>();
                if (next.Debug.IsActive)
                {
                    next = ExitDebugCore(next);
                    cmds.Add(new SetEditorReadOnly(false));
                    cmds.Add(new HighlightEditorLine(0));
                }
                bool enabled = !next.Ui.BonzomaticMode;
                next = next with { Ui = next.Ui with { BonzomaticMode = enabled } };
                if (enabled && !next.Run.GpuPreviewEnabled)
                {
                    next = next with { Run = next.Run with { GpuPreviewEnabled = true } };
                    cmds.Add(new FetchEditorText(code => new RunWithCode(code)));
                }
                commands = cmds;
                break;
            }

            case PermalinkCopyRequested x:
                next = model with { Ui = model.Ui with { MenuOpen = false } };
                commands = Cmds(new FetchEditorText(code => new PermalinkCopyWithCode(code, x.BaseUrl)));
                break;

            case PermalinkCopyWithCode x:
            {
                var config = ActiveConfig(model);
                var settings = new PermalinkSettings(
                    config.FragmentEntryPoint, config.WarpX, config.WarpY,
                    config.GroupOffsetX, config.GroupOffsetY, model.Run.GpuPreviewEnabled,
                    config.RenderMode, config.VertexEntryPoint, config.CpuMode);
                string url = PermalinkCodec.BuildUrl(x.BaseUrl, x.Code, settings);
                next = SyncActiveCode(model, x.Code) with { Ui = model.Ui with { PermalinkToastVisible = true } };
                commands = Cmds(
                    new CopyToClipboard(url),
                    new DelayThenDispatch(1500, new PermalinkToastDismissed()));
                break;
            }

            case PermalinkToastDismissed:
                next = model with { Ui = model.Ui with { PermalinkToastVisible = false } };
                commands = None;
                break;

            default:
                next = model;
                commands = None;
                break;
        }

        var all = new List<Cmd>(commands) { new SyncCanvas(CanvasProjection.Compute(next)) };
        return (next, all);
    }

    // ---- Shared helpers ----

    private static (AppState, IReadOnlyList<Cmd>) StartRunWithCode(AppState m, string code)
    {
        var config = ActiveConfig(m);
        float initialTime = m.Run.CapturedFrame?.Time ?? 0f;
        var next = m with
        {
            Run = BeginRunReset(m.Run, keepCaptured: false),
            Ui = m.Ui with { ImageCollapsed = false },
        };
        if (m.Run.GpuPreviewEnabled)
        {
            next = next with { Run = next.Run with { Backend = RunBackend.Gpu } };
            return (next, Cmds(new RunGpu(code, config, initialTime, next.Run.GpuPaused, ActiveDocPath(m))));
        }
        return (next, Cmds(new RunCpu(code, config, ActiveDocPath(m))));
    }

    private static RunState BeginRunReset(RunState r, bool keepCaptured) => r with
    {
        Status = RunStatus.Running,
        Backend = RunBackend.Cpu,
        Error = null,
        Output = "",
        Image = null,
        Metrics = null,
        CapturedFrame = keepCaptured ? r.CapturedFrame : null,
        ViewMode = DebugViewMode.Color,
    };

    private static (AppState, IReadOnlyList<Cmd>) DebugAtVertex(
        AppState m, int vertexIndex, float time, int canvasW, int canvasH)
    {
        var config = ActiveConfig(m);
        int warpSize = Math.Max(1, config.WarpX * config.WarpY);
        var next = WithInspectedThread(m, vertexIndex % warpSize);
        next = next with { Debug = next.Debug with { DebugVertexIndex = vertexIndex } };
        next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(time, canvasW, canvasH) } };
        if (next.Debug.BottomMode != DebugBottomMode.ThreadStates)
            next = next with { Debug = next.Debug with { BottomMode = DebugBottomMode.ThreadStates } };
        return (next, Cmds(new FetchEditorText(code => new DebugWithCode(code))));
    }

    private static AppState ExitDebugCore(AppState m)
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

    private static AppState AddDoc(AppState m, string name, string content, string path = null)
    {
        var doc = new ShaderDocument
        {
            Id = m.Editor.NextDocumentId,
            Name = name,
            Path = path,
            Code = content,
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

    private static int IndexOfPath(AppState m, string path)
    {
        var docs = m.Editor.Documents;
        for (int i = 0; i < docs.Count; i++)
            if (docs[i].Path == path) return i;
        return -1;
    }

    private static ShaderConfig ActiveConfig(AppState m) =>
        m.Editor.ActiveDocument?.Config ?? new ShaderConfig();

    private static string ActiveDocPath(AppState m) => m.Editor.ActiveDocument?.Path;

    private static AppState WithActiveConfig(AppState m, Func<ShaderConfig, ShaderConfig> update)
    {
        var doc = m.Editor.ActiveDocument;
        if (doc == null) return m;
        var documents = m.Editor.Documents.ToArray();
        documents[m.Editor.ActiveIndex] = doc with { Config = update(doc.Config) };
        return m with { Editor = m.Editor with { Documents = documents } };
    }

    private static AppState WithActiveDoc(AppState m, Func<ShaderDocument, ShaderDocument> update)
    {
        var doc = m.Editor.ActiveDocument;
        if (doc == null) return m;
        var documents = m.Editor.Documents.ToArray();
        documents[m.Editor.ActiveIndex] = update(doc);
        return m with { Editor = m.Editor with { Documents = documents } };
    }

    private static AppState SyncActiveCode(AppState m, string code) =>
        WithActiveDoc(m, d => d with { Code = code ?? "" });

    private static AppState WithInspectedThread(AppState m, int thread)
    {
        var config = m.Editor.ActiveDocument?.Config;
        int max = config == null ? 0 : Math.Max(0, config.WarpX * config.WarpY - 1);
        return m with { Debug = m.Debug with { InspectedThread = Math.Clamp(thread, 0, max) } };
    }

    private static Cmd HighlightCmd(AppState m)
    {
        if (!m.Debug.IsActive) return new HighlightEditorLine(0);
        bool onDoc = m.Editor.ActiveDocument?.Id == m.Debug.DebugDocumentId;
        int line = onDoc && m.Debug.Trace != null ? m.Debug.Trace.LineAt(m.Debug.StepIndex) : 0;
        return new HighlightEditorLine(line);
    }

    private static IReadOnlyList<Cmd> Cmds(params Cmd[] commands) => commands;
}
