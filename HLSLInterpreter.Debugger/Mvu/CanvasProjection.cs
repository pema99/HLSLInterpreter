using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// The full canvas and editor-theme state derived from the model. update computes
// one of these after every message and hands it to the effect runner, which
// pushes only the fields that changed since the last sync.
public sealed record CanvasProjection(
    int WarpX, int WarpY,
    string RegularMode,
    string DebugMode,
    (int X, int Y)? DebugPixel,
    int[] ThreadStates,
    (int X, int Y)? CpuClick,
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

        (int, int)? debugPixel = debugging
            ? (s.Debug.InspectedThread % wx, s.Debug.InspectedThread / wx)
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
            cpuClick ? (wx, wy) : null, debugging,
            vertexPick ? "vertex" : "pixel", vertexPick ? config.Mesh : null, theme);
    }
}
