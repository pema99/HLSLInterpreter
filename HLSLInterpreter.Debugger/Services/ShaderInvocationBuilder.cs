using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Services;

// Gathers the per-frame inputs a ShaderInvocation needs (canvas size, camera
// matrices, mouse) from the store and the GPU interop.
public sealed class ShaderInvocationBuilder
{
    private readonly AppStore _store;
    private readonly IGpuInterop _gpu;

    public ShaderInvocationBuilder(AppStore store, IGpuInterop gpu)
    {
        _store = store;
        _gpu = gpu;
    }

    public async Task<ShaderInvocation> BuildAsync()
    {
        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        var captured = _store.State.Run.GpuCaptured;
        int canvasW = captured?.CanvasW ?? wx;
        int canvasH = captured?.CanvasH ?? wy;

        float[] view = null;
        float[] projection = null;
        if (config.RenderMode == ShaderRenderMode.VertFrag)
        {
            view = await _gpu.View();
            projection = await _gpu.Projection(canvasW, canvasH);
        }

        float[] mouse;
        try { mouse = await _gpu.Mouse(); }
        catch { mouse = new float[] { 0f, 0f, 0f, 0f }; }

        return new ShaderInvocation(
            Mode: config.RenderMode,
            FragmentEntryPoint: config.FragmentEntryPoint,
            VertexEntryPoint: config.VertexEntryPoint,
            Mesh: config.Mesh,
            WarpX: wx,
            WarpY: wy,
            GroupOffsetX: config.GroupOffsetX,
            GroupOffsetY: config.GroupOffsetY,
            CanvasW: canvasW,
            CanvasH: canvasH,
            Time: captured?.Time ?? 0f,
            View: view,
            Projection: projection,
            Mouse: mouse,
            DebugVertexIndex: _store.State.Debug.DebugVertexIndex,
            Textures: config.Textures,
            Samplers: config.Samplers);
    }
}
