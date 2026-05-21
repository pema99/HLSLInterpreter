using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

public sealed record Point(int X, int Y);

// The full canvas and editor-theme state derived from the model. update computes
// one of these after every message; SyncCanvas hands it to JS in a single push.
public sealed record CanvasProjection(
    int WarpX, int WarpY,
    string RegularMode,
    string DebugMode,
    Point DebugPixel,
    int[] ThreadStates,
    Point CpuClick,
    bool DebugClick,
    string PickMode,
    Mesh PickMesh,
    string Theme)
{
    public static CanvasProjection Compute(AppState s)
    {
        var config = s.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        bool debugging = s.Debug.IsActive;

        string regularMode = s.Run.Backend == RunBackend.Gpu ? "gpu" : "cpu";
        string debugMode = debugging
            ? s.Debug.BottomMode switch
            {
                DebugBottomMode.Both => "both",
                DebugBottomMode.PixelColors => "debug",
                _ => "idle",
            }
            : "idle";

        Point debugPixel = debugging
            ? new Point(s.Debug.InspectedThread % wx, s.Debug.InspectedThread / wx)
            : null;

        int[] threadStates = debugging
            ? (s.Debug.CurrentStep?.GetThreadStatesAt(s.Debug.SelectedFrame)
                ?? Array.Empty<HLSLExecutionState.ThreadState>()).Select(x => (int)x).ToArray()
            : null;

        bool cpuClick = !debugging && s.Run.Backend == RunBackend.Cpu;
        bool vertexPick = !debugging
            && config.RenderMode == ShaderRenderMode.VertFrag
            && config.DebugTarget == DebugTarget.Vertex;
        string theme = s.Ui.BonzomaticMode && !debugging ? "hlsl-bonzomatic" : "hlsl-dark";

        return new CanvasProjection(
            wx, wy, regularMode, debugMode, debugPixel, threadStates,
            cpuClick ? new Point(wx, wy) : null, debugging,
            vertexPick ? "vertex" : "pixel", vertexPick ? config.Mesh : null, theme);
    }
}
