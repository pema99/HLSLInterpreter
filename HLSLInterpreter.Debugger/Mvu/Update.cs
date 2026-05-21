using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;
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
        var (next, commands) = Reduce(model, message);
        var all = new List<Cmd>(commands) { new SyncCanvas(CanvasProjection.Compute(next)) };
        return (next, all);
    }

    private static (AppState, IReadOnlyList<Cmd>) Reduce(AppState m, Msg message) => message switch
    {
        AppStarted x => Started(m, x),
        CanvasReady => (m, None),
        DefaultMeshLoaded x => (WithActiveConfig(
            m with { Editor = m.Editor with { DefaultMesh = x.Mesh } }, c => c with { Mesh = x.Mesh }), None),

        RunRequested => m.Run.Status != RunStatus.Idle
            ? (m, None)
            : (m, Cmds(new FetchEditorText(code => new RunWithCode(code)))),
        RunCancelRequested => (m, Cmds(new CancelRun())),
        RunWithCode x => StartRunWithCode(SyncActiveCode(m, x.Code), x.Code),
        RunBecameCancellable => (m with { Run = m.Run with { Status = RunStatus.Cancellable } }, None),
        RunFinished x => (m with { Run = m.Run with
        {
            Status = RunStatus.Idle,
            Output = x.Output ?? "",
            Error = x.Error,
            Image = x.Image,
            Metrics = x.Metrics,
        } }, None),
        GpuPauseToggled => (m with { Run = m.Run with { GpuPaused = !m.Run.GpuPaused } },
            Cmds(new SetGpuPaused(!m.Run.GpuPaused))),
        GpuTimeRestartRequested => (m, Cmds(new RestartGpuTime())),
        GpuPreviewToggled x => (m with { Run = m.Run with { GpuPreviewEnabled = x.Enabled } },
            Cmds(new FetchEditorText(code => new RunWithCode(code)))),
        ViewModeChanged x => (m with { Run = m.Run with { ViewMode = x.Mode } },
            Cmds(new RenderViewMode(x.Mode, m.Run.Metrics, m.Run.Image))),
        ImageCollapseToggled => (m with { Ui = m.Ui with { ImageCollapsed = !m.Ui.ImageCollapsed } }, None),

        DebugRequested => DebugRequest(m),
        DebugWithCode x => RecordTraceFlow(SyncActiveCode(m, x.Code), x.Code),
        DebugAtPixelRequested x => DebugAtPixel(m, x),
        DebugAtVertexRequested x => DebugAtVertex(m, x.VertexIndex, x.Time, x.CanvasW, x.CanvasH),
        DebugTraceRecorded x => TraceRecorded(m, x),
        DebugExitRequested => ExitDebug(m),
        StepRequested x => Step(m, x.Kind),
        BreakpointToggled x => ToggleBreakpoint(m, x.Line),
        SelectedFrameChanged x => (m with { Debug = m.Debug with { SelectedFrame = Math.Max(0, x.Frame) } }, None),
        InspectedThreadChanged x => (WithInspectedThread(m, x.Thread), None),
        InspectedPixelChanged x => InspectedPixel(m, x),
        BottomModeChanged x => (m with { Debug = m.Debug with { BottomMode = x.Mode } }, None),
        ImmediateEvalRequested x => ImmediateEval(m, x.Expression),
        ImmediateEvalFinished x => (m with { Debug = m.Debug with
        {
            ImmediateHistory = m.Debug.ImmediateHistory.Append(x.Entry).ToArray(),
        } }, None),

        TabSwitchRequested x => (m, Cmds(new FetchEditorText(code => new TabSwitched(code, x.Index)))),
        TabSwitched x => TabSwitch(m, x.CurrentCode, x.Index),
        TabCloseRequested x => TabClose(m, x.Index),
        TabMoveRequested x => (MoveDoc(m, x.From, x.Desired), None),
        ObjPickRequested => (m, Cmds(new PickObjFile())),
        ObjMeshLoaded x => ObjMesh(m, x.ObjText),
        OpenFileRequested => (m, Cmds(new OpenFileDialog())),
        FileOpened x => (m, Cmds(new FetchEditorText(code => new FileOpenedWithCode(code, x.Path, x.Content)))),
        FileOpenedWithCode x => FileOpen(m, x),
        FileDropped x => (m, Cmds(new FetchEditorText(
            code => new FileDroppedWithCode(code, x.Name, x.Content, x.Path)))),
        FileDroppedWithCode x => FileDrop(m, x),
        SaveFileRequested x => (m, Cmds(new FetchEditorText(code => new SaveFileWithCode(code, x.AsNew)))),
        SaveFileWithCode x => (SyncActiveCode(m, x.Code),
            Cmds(new SaveFileDialog(x.Code, ActiveDocPath(m), x.AsNew))),
        FileSaved x => (WithActiveDoc(m, d => d with
        {
            Path = x.Path,
            Name = System.IO.Path.GetFileName(x.Path),
        }), None),
        DownloadRequested => (m with { Ui = m.Ui with { MenuOpen = false } },
            Cmds(new FetchEditorText(code => new DownloadWithCode(code)))),
        DownloadWithCode x => Download(m, x.Code),

        NewFileRequested x => (m with { Ui = m.Ui with { OpenModal = ModalKind.None } },
            Cmds(new FetchEditorText(code => new ContentLoaded(
                code, x.Name, x.Content, x.Mode, x.FragEntry, x.VertEntry, null, null, false)))),
        ExampleLoaded x => (m with { Ui = m.Ui with { OpenModal = ModalKind.None } },
            Cmds(new FetchEditorText(code => new ContentLoaded(
                code, x.Name, x.Code, x.Mode, x.FragEntry, x.VertEntry, x.Textures, x.Samplers, true)))),
        ShaderToyImported x => ShaderToy(m, x.Hlsl),
        ContentLoaded x => LoadContentMsg(m, x),

        RenderModeChanged x => (WithActiveConfig(m, c => c with { RenderMode = x.Mode }), None),
        FragmentEntryChanged x => (WithActiveConfig(m, c => c with { FragmentEntryPoint = x.Entry }), None),
        VertexEntryChanged x => (WithActiveConfig(m, c => c with { VertexEntryPoint = x.Entry }), None),
        GroupOffsetChanged x => (WithActiveConfig(m, c => c with
        {
            GroupOffsetX = x.X,
            GroupOffsetY = x.Y,
        }), None),
        WarpSizeChanged x => (WithClampedInspectedThread(WithActiveConfig(m, c => c with
        {
            WarpX = Math.Max(1, x.X),
            WarpY = Math.Max(1, x.Y),
        })), None),
        CpuModeChanged x => (WithActiveConfig(m, c => c with { CpuMode = x.Mode }), None),
        DebugTargetChanged x => (WithActiveConfig(m, c => c with { DebugTarget = x.Target }), None),
        FontSizeChanged x => (m with { Editor = m.Editor with { FontSize = x.Size } },
            Cmds(new SetEditorFontSize(x.Size))),
        TexturesSaved x => (WithActiveConfig(
            m with { Ui = m.Ui with { OpenModal = ModalKind.None } },
            c => c with { Textures = x.Textures, Samplers = x.Samplers }),
            Cmds(new FetchEditorText(code => new RunWithCode(code)))),

        ModalRequested x => (m with { Ui = m.Ui with { OpenModal = x.Kind, MenuOpen = false } }, None),
        ModalDismissed => (m with { Ui = m.Ui with { OpenModal = ModalKind.None } }, None),
        MenuToggled => (m with { Ui = m.Ui with { MenuOpen = !m.Ui.MenuOpen } }, None),
        MenuClosed => (m with { Ui = m.Ui with { MenuOpen = false } }, None),
        BonzomaticToggled => Bonzomatic(m),
        PermalinkCopyRequested x => (m with { Ui = m.Ui with { MenuOpen = false } },
            Cmds(new FetchEditorText(code => new PermalinkCopyWithCode(code, x.BaseUrl)))),
        PermalinkCopyWithCode x => CopyPermalink(m, x.Code, x.BaseUrl),
        PermalinkToastDismissed => (m with { Ui = m.Ui with { PermalinkToastVisible = false } }, None),

        _ => (m, None),
    };

    // ---- Lifecycle ----

    private static (AppState, IReadOnlyList<Cmd>) Started(AppState m, AppStarted x)
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
        var next = m with
        {
            Editor = m.Editor with
            {
                Documents = new[] { doc },
                ActiveIndex = 0,
                NextDocumentId = 1,
                TabsEnabled = x.TabsEnabled,
            },
            Run = m.Run with { GpuPreviewEnabled = applied.GpuPreviewEnabled },
        };
        return (next, Cmds(new LoadDefaultMesh()));
    }

    // ---- Run ----

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

    // ---- Debug ----

    private static (AppState, IReadOnlyList<Cmd>) DebugRequest(AppState m)
    {
        var config = ActiveConfig(m);
        if (config.RenderMode == ShaderRenderMode.VertFrag
            && config.DebugTarget == DebugTarget.Vertex
            && m.Debug.DebugVertexIndex < 0)
        {
            var cap = m.Run.CapturedFrame;
            return DebugAtVertex(m, 0,
                cap?.Time ?? 0f,
                cap?.CanvasW ?? Math.Max(1, config.WarpX),
                cap?.CanvasH ?? Math.Max(1, config.WarpY));
        }
        if (m.Editor.ActiveDocument == null) return (m, None);
        return (m, Cmds(new FetchEditorText(code => new DebugWithCode(code))));
    }

    private static (AppState, IReadOnlyList<Cmd>) DebugAtPixel(AppState m, DebugAtPixelRequested x)
    {
        var config = ActiveConfig(m);
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        var next = m with { Debug = m.Debug with
        {
            SavedGroupOffset = (config.GroupOffsetX, config.GroupOffsetY),
        } };
        next = WithActiveConfig(next, c => c with { GroupOffsetX = x.Px / wx, GroupOffsetY = x.Py / wy });
        next = WithInspectedThread(next, (x.Py % wy) * wx + (x.Px % wx));
        next = next with { Run = next.Run with { CapturedFrame = new FrameCapture(x.Time, x.CanvasW, x.CanvasH) } };
        return (next, Cmds(new FetchEditorText(code => new DebugWithCode(code))));
    }

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

    private static (AppState, IReadOnlyList<Cmd>) RecordTraceFlow(AppState m, string code)
    {
        var doc = m.Editor.ActiveDocument;
        if (doc == null) return (m, None);
        var captured = m.Run.CapturedFrame;
        bool snapshot = m.Run.GpuPreviewEnabled && captured == null;
        var next = m with { Run = BeginRunReset(m.Run, keepCaptured: true) };
        return (next, Cmds(new RecordTrace(
            code, doc.Config, captured, snapshot, m.Debug.DebugVertexIndex, doc.Id, doc.Path)));
    }

    private static (AppState, IReadOnlyList<Cmd>) TraceRecorded(AppState m, DebugTraceRecorded x)
    {
        var trace = x.Trace;
        var run = m.Run with
        {
            Status = RunStatus.Idle,
            Output = trace.Output ?? "",
            CapturedFrame = x.Captured,
        };

        bool testFailure = trace.Exception is HLSLRunner.TestFailException;
        bool canDebug = !trace.HasError || (testFailure && trace.Steps.Count > 0);
        if (!canDebug)
            return (m with { Run = run with { Error = new RunError(trace.ErrorMessage, trace.Exception) } }, None);

        if (trace.HasError)
            run = run with { Error = new RunError(trace.ErrorMessage, trace.Exception) };
        else if (x.Image != null)
            run = run with { Image = x.Image };

        int stepIndex = trace.HasError ? TraceNavigator.End(trace) : 0;
        var debug = m.Debug with
        {
            IsActive = true,
            Trace = trace,
            StepIndex = stepIndex,
            DebugDocumentId = x.DocumentId,
            DebugCode = x.Code,
            SelectedFrame = 0,
            ImmediateHistory = Array.Empty<ImmediateEntry>(),
        };
        var next = m with { Run = run, Debug = debug };
        return (next, Cmds(new SetEditorReadOnly(true), HighlightCmd(next)));
    }

    private static (AppState, IReadOnlyList<Cmd>) ExitDebug(AppState m)
    {
        var next = ExitDebugCore(m);
        return (next, Cmds(
            new SetEditorReadOnly(false),
            new HighlightEditorLine(0),
            new FetchEditorText(code => new RunWithCode(code))));
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

    private static (AppState, IReadOnlyList<Cmd>) Step(AppState m, StepKind kind)
    {
        var trace = m.Debug.Trace;
        if (trace == null) return (m, None);
        var cmds = new List<Cmd>();
        var next = EnsureDebugDocActive(m, cmds);
        int index = Navigate(trace, next.Debug.StepIndex, kind, next.Debug.Breakpoints);
        next = next with { Debug = next.Debug with { StepIndex = index, SelectedFrame = 0 } };
        cmds.Add(HighlightCmd(next));
        return (next, cmds);
    }

    private static AppState EnsureDebugDocActive(AppState m, List<Cmd> cmds)
    {
        int debugId = m.Debug.DebugDocumentId;
        if (debugId < 0 || m.Editor.ActiveDocument?.Id == debugId) return m;
        var docs = m.Editor.Documents;
        for (int i = 0; i < docs.Count; i++)
        {
            if (docs[i].Id == debugId)
            {
                cmds.Add(new SetEditorText(docs[i].Code));
                return m with { Editor = m.Editor with { ActiveIndex = i } };
            }
        }
        return m;
    }

    private static int Navigate(ExecutionTrace trace, int index, StepKind kind, IReadOnlySet<int> breakpoints) =>
        kind switch
        {
            StepKind.In => TraceNavigator.Forward(trace, index),
            StepKind.Over => TraceNavigator.Over(trace, index),
            StepKind.Out => TraceNavigator.Out(trace, index),
            StepKind.InBack => TraceNavigator.Back(trace, index),
            StepKind.OverBack => TraceNavigator.OverBack(trace, index),
            StepKind.OutBack => TraceNavigator.OutBack(trace, index),
            StepKind.Continue => TraceNavigator.ToBreakpoint(trace, index, breakpoints),
            StepKind.ContinueBack => TraceNavigator.ToBreakpointBack(trace, index, breakpoints),
            _ => index,
        };

    private static (AppState, IReadOnlyList<Cmd>) ToggleBreakpoint(AppState m, int line)
    {
        var breakpoints = new HashSet<int>(m.Debug.Breakpoints);
        if (!breakpoints.Add(line)) breakpoints.Remove(line);
        var next = m with { Debug = m.Debug with { Breakpoints = breakpoints } };
        return (next, Cmds(new SetEditorBreakpoints(breakpoints.ToArray())));
    }

    private static (AppState, IReadOnlyList<Cmd>) InspectedPixel(AppState m, InspectedPixelChanged x)
    {
        var config = ActiveConfig(m);
        if (config.WarpX <= 0) return (m, None);
        return (WithInspectedThread(m, x.Py * config.WarpX + x.Px), None);
    }

    private static (AppState, IReadOnlyList<Cmd>) ImmediateEval(AppState m, string expression)
    {
        var debug = m.Debug;
        if (debug.Trace == null || debug.StepIndex < 0 || string.IsNullOrEmpty(debug.DebugCode))
            return (m, None);
        return (m, Cmds(new EvaluateImmediate(
            expression, debug.DebugCode, debug.StepIndex, ActiveConfig(m),
            m.Run.CapturedFrame, debug.InspectedThread, ActiveDocPath(m))));
    }

    // ---- Editor and documents ----

    private static (AppState, IReadOnlyList<Cmd>) TabSwitch(AppState m, string currentCode, int index)
    {
        var next = SyncActiveCode(m, currentCode);
        if (index < 0 || index >= next.Editor.Documents.Count || index == next.Editor.ActiveIndex)
            return (next, None);
        next = next with { Editor = next.Editor with { ActiveIndex = index } };
        return (next, Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next)));
    }

    private static (AppState, IReadOnlyList<Cmd>) TabClose(AppState m, int index)
    {
        var docs = m.Editor.Documents;
        if (docs.Count <= 1 || index < 0 || index >= docs.Count) return (m, None);
        var cmds = new List<Cmd>();
        var next = m;
        if (next.Debug.IsActive && docs[index].Id == next.Debug.DebugDocumentId)
        {
            next = ExitDebugCore(next);
            cmds.Add(new SetEditorReadOnly(false));
            cmds.Add(new HighlightEditorLine(0));
        }
        bool activeChanges = index == next.Editor.ActiveIndex;
        next = CloseDoc(next, index);
        if (activeChanges)
            cmds.Add(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""));
        return (next, cmds);
    }

    private static AppState CloseDoc(AppState m, int index)
    {
        var editor = m.Editor;
        var documents = editor.Documents.ToList();
        documents.RemoveAt(index);
        int active = editor.ActiveIndex;
        if (index < active || active >= documents.Count) active--;
        return m with { Editor = editor with
        {
            Documents = documents,
            ActiveIndex = Math.Clamp(active, 0, documents.Count - 1),
        } };
    }

    private static AppState MoveDoc(AppState m, int from, int desired)
    {
        var editor = m.Editor;
        if (from < 0 || from >= editor.Documents.Count) return m;
        if (desired == from || desired == from + 1) return m;
        var documents = editor.Documents.ToList();
        var active = documents[editor.ActiveIndex];
        var moving = documents[from];
        documents.RemoveAt(from);
        documents.Insert(desired > from ? desired - 1 : desired, moving);
        return m with { Editor = editor with
        {
            Documents = documents,
            ActiveIndex = documents.IndexOf(active),
        } };
    }

    private static (AppState, IReadOnlyList<Cmd>) ObjMesh(AppState m, string objText)
    {
        if (string.IsNullOrWhiteSpace(objText)) return (m, None);
        Mesh mesh;
        try { mesh = Mesh.ParseObj(objText); }
        catch (Exception ex)
        {
            return (m with { Run = m.Run with
            {
                Error = new RunError("Failed to load OBJ: " + ex.Message, ex),
            } }, None);
        }
        var next = WithActiveConfig(m, c => c with { Mesh = mesh });
        return (next, Cmds(new FetchEditorText(code => new RunWithCode(code))));
    }

    private static (AppState, IReadOnlyList<Cmd>) FileOpen(AppState m, FileOpenedWithCode x)
    {
        var next = SyncActiveCode(m, x.CurrentCode);
        if (x.Path == null || x.Content == null) return (next, None);
        int existing = IndexOfPath(next, x.Path);
        if (existing >= 0)
        {
            if (existing == next.Editor.ActiveIndex) return (next, None);
            next = next with { Editor = next.Editor with { ActiveIndex = existing } };
            return (next, Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next)));
        }
        next = AddDoc(next, System.IO.Path.GetFileName(x.Path), x.Content, x.Path);
        return (next, Cmds(new SetEditorText(x.Content)));
    }

    private static (AppState, IReadOnlyList<Cmd>) FileDrop(AppState m, FileDroppedWithCode x)
    {
        var next = SyncActiveCode(m, x.CurrentCode);
        int existing = string.IsNullOrEmpty(x.Path) ? -1 : IndexOfPath(next, x.Path);
        if (existing >= 0)
        {
            if (existing == next.Editor.ActiveIndex) return (next, None);
            next = next with { Editor = next.Editor with { ActiveIndex = existing } };
            return (next, Cmds(new SetEditorText(next.Editor.ActiveDocument?.Code ?? ""), HighlightCmd(next)));
        }
        next = AddDoc(next, x.Name, x.Content, string.IsNullOrEmpty(x.Path) ? null : x.Path);
        return (next, Cmds(new SetEditorText(x.Content)));
    }

    private static int IndexOfPath(AppState m, string path)
    {
        var docs = m.Editor.Documents;
        for (int i = 0; i < docs.Count; i++)
            if (docs[i].Path == path) return i;
        return -1;
    }

    private static (AppState, IReadOnlyList<Cmd>) Download(AppState m, string code)
    {
        var next = SyncActiveCode(m, code);
        var doc = next.Editor.ActiveDocument;
        string fileName = string.IsNullOrWhiteSpace(doc?.Name) ? "shader.hlsl" : doc.Name;
        return (next, Cmds(new DownloadFile(fileName, code)));
    }

    // ---- Content loading ----

    private static (AppState, IReadOnlyList<Cmd>) ShaderToy(AppState m, string hlsl)
    {
        var textures = new List<TextureBinding>();
        for (int i = 0; i < 4; i++)
        {
            string name = $"iChannel{i}";
            if (System.Text.RegularExpressions.Regex.IsMatch(hlsl, $@"\b{name}\b"))
                textures.Add(new TextureBinding { Name = name });
        }
        return (m with { Ui = m.Ui with { OpenModal = ModalKind.None } },
            Cmds(new FetchEditorText(code => new ContentLoaded(
                code, "shadertoy.hlsl", hlsl, ShaderRenderMode.Pixel, "frag", null,
                textures, Array.Empty<SamplerBinding>(), true))));
    }

    private static (AppState, IReadOnlyList<Cmd>) LoadContentMsg(AppState m, ContentLoaded x)
    {
        var next = SyncActiveCode(m, x.CurrentCode);
        var cmds = new List<Cmd>();
        if (next.Debug.IsActive)
        {
            next = ExitDebugCore(next);
            cmds.Add(new SetEditorReadOnly(false));
            cmds.Add(new HighlightEditorLine(0));
        }
        next = LoadContent(next, x.Name, x.Content);
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
        return (next, cmds);
    }

    private static AppState LoadContent(AppState m, string name, string content)
    {
        if (m.Editor.TabsEnabled || m.Editor.ActiveDocument == null)
            return AddDoc(m, name, content);
        return WithActiveDoc(m, d => d with { Name = name, Code = content });
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
        return m with { Editor = m.Editor with
        {
            Documents = documents,
            ActiveIndex = documents.Length - 1,
            NextDocumentId = m.Editor.NextDocumentId + 1,
        } };
    }

    // ---- UI ----

    private static (AppState, IReadOnlyList<Cmd>) Bonzomatic(AppState m)
    {
        var next = m with { Ui = m.Ui with { MenuOpen = false } };
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
        return (next, cmds);
    }

    private static (AppState, IReadOnlyList<Cmd>) CopyPermalink(AppState m, string code, string baseUrl)
    {
        var config = ActiveConfig(m);
        var settings = new PermalinkSettings(
            config.FragmentEntryPoint, config.WarpX, config.WarpY,
            config.GroupOffsetX, config.GroupOffsetY, m.Run.GpuPreviewEnabled,
            config.RenderMode, config.VertexEntryPoint, config.CpuMode);
        string url = PermalinkCodec.BuildUrl(baseUrl, code, settings);
        var next = SyncActiveCode(m, code) with { Ui = m.Ui with { PermalinkToastVisible = true } };
        return (next, Cmds(
            new CopyToClipboard(url),
            new DelayThenDispatch(1500, new PermalinkToastDismissed())));
    }

    // ---- Helpers ----

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

    private static AppState WithClampedInspectedThread(AppState m) =>
        WithInspectedThread(m, m.Debug.InspectedThread);

    private static Cmd HighlightCmd(AppState m)
    {
        if (!m.Debug.IsActive) return new HighlightEditorLine(0);
        bool onDoc = m.Editor.ActiveDocument?.Id == m.Debug.DebugDocumentId;
        int line = onDoc && m.Debug.Trace != null ? m.Debug.Trace.LineAt(m.Debug.StepIndex) : 0;
        return new HighlightEditorLine(line);
    }

    private static IReadOnlyList<Cmd> Cmds(params Cmd[] commands) => commands;
}
