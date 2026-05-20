using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Services;

// Subscribes to the store and pushes canvas and editor state to JS whenever the
// relevant values change. Replaces the per-render JS sync block in the old page:
// instead of re-pushing everything every render, it diffs and pushes only what
// changed, driven by state changes rather than the Blazor render loop.
public sealed class JsProjectionService : IDisposable
{
    private readonly AppStore _store;
    private readonly ICanvasInterop _canvas;
    private readonly IEditorInterop _editor;

    private (int, int)? _warp;
    private string _regularMode;
    private string _debugMode;
    private string _pickMode;
    private string _theme;
    private (int, int)? _debugPixel;
    private bool? _cpuClick;
    private bool? _debugClick;
    private int[] _threadStates;
    private Mesh _pushedMesh;

    public JsProjectionService(AppStore store, ICanvasInterop canvas, IEditorInterop editor)
    {
        _store = store;
        _canvas = canvas;
        _editor = editor;
    }

    public void Start() => _store.Changed += OnChanged;

    public void Dispose() => _store.Changed -= OnChanged;

    private async void OnChanged(AppState previous, AppState next)
    {
        try { await ProjectAsync(next); }
        catch { }
    }

    public Task ProjectAsync() => ProjectAsync(_store.State);

    private async Task ProjectAsync(AppState s)
    {
        var config = s.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        bool debugging = s.Debug.IsActive;

        if (_warp != (wx, wy))
        {
            _warp = (wx, wy);
            await _canvas.SetWarp(wx, wy);
        }

        string regularMode = s.Run.Backend == RunBackend.Gpu ? "gpu" : "cpu";
        if (_regularMode != regularMode)
        {
            _regularMode = regularMode;
            await _canvas.SetRegularMode(regularMode);
        }

        string debugMode = debugging
            ? s.Debug.BottomMode switch
            {
                DebugBottomMode.Both => "both",
                DebugBottomMode.PixelColors => "debug",
                _ => "idle",
            }
            : "idle";
        if (_debugMode != debugMode)
        {
            _debugMode = debugMode;
            await _canvas.SetDebugMode(debugMode);
        }

        (int, int)? debugPixel = debugging
            ? (s.Debug.InspectedThread % wx, s.Debug.InspectedThread / wx)
            : null;
        if (_debugPixel != debugPixel)
        {
            _debugPixel = debugPixel;
            await _canvas.SetDebugPixel(debugPixel?.Item1, debugPixel?.Item2);
        }

        int[] threadStates = debugging
            ? (s.Debug.CurrentStep?.GetThreadStatesAt(s.Debug.SelectedFrame)
                ?? Array.Empty<HLSLExecutionState.ThreadState>()).Select(x => (int)x).ToArray()
            : null;
        if (!ThreadStatesEqual(_threadStates, threadStates))
        {
            _threadStates = threadStates;
            await _canvas.SetThreadStates(threadStates);
        }

        bool cpuClick = !debugging && s.Run.Backend == RunBackend.Cpu;
        if (_cpuClick != cpuClick)
        {
            _cpuClick = cpuClick;
            await _canvas.SetCpuClickHandler(cpuClick ? wx : null, cpuClick ? wy : null);
        }

        if (_debugClick != debugging)
        {
            _debugClick = debugging;
            await _canvas.SetDebugClickHandler(debugging);
        }

        bool vertexPick = !debugging
            && config.RenderMode == ShaderRenderMode.VertFrag
            && config.DebugTarget == DebugTarget.Vertex;
        string pickMode = vertexPick ? "vertex" : "pixel";
        if (_pickMode != pickMode)
        {
            _pickMode = pickMode;
            await _canvas.SetPickMode(pickMode);
        }

        if (vertexPick && !ReferenceEquals(_pushedMesh, config.Mesh))
        {
            _pushedMesh = config.Mesh;
            await _canvas.SetMeshData(config.Mesh.Positions, config.Mesh.Indices);
        }
        else if (!vertexPick && _pushedMesh != null)
        {
            _pushedMesh = null;
            await _canvas.SetMeshData(null, null);
        }

        string theme = s.Ui.BonzomaticMode && !debugging ? "hlsl-bonzomatic" : "hlsl-dark";
        if (_theme != theme)
        {
            _theme = theme;
            await _editor.SetTheme(theme);
        }
    }

    private static bool ThreadStatesEqual(int[] a, int[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
