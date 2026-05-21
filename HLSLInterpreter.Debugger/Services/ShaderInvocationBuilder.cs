using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Services;

// Gathers the per-frame inputs a ShaderInvocation needs (canvas size, camera
// matrices, mouse) from the GPU interop. The MVU effect runner passes the
// model-derived inputs explicitly; the legacy store-based overloads are kept
// until the old services are removed.
public sealed class ShaderInvocationBuilder
{
    private readonly AppStore _store;
    private readonly IGpuInterop _gpu;

    public ShaderInvocationBuilder(AppStore store, IGpuInterop gpu)
    {
        _store = store;
        _gpu = gpu;
    }

    public async Task<ShaderInvocation> BuildAsync(ShaderConfig config, FrameCapture captured, int debugVertexIndex)
    {
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
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
            DebugVertexIndex: debugVertexIndex,
            Textures: config.Textures,
            Samplers: config.Samplers);
    }

    public static HLSLParserConfig MakeParserConfig(string docPath) =>
        new HLSLParserConfig
        {
            BasePath = docPath != null ? System.IO.Path.GetDirectoryName(docPath) ?? "" : "",
        };

    public Task<ShaderInvocation> BuildAsync()
    {
        var config = _store.State.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        return BuildAsync(config, _store.State.Run.CapturedFrame, _store.State.Debug.DebugVertexIndex);
    }

    public HLSLParserConfig MakeParserConfig() =>
        MakeParserConfig(_store.State.Editor.ActiveDocument?.Path);
}
