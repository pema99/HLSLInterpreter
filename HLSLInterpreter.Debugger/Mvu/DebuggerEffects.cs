using HLSL;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Mvu;

// Builds the application's commands. Every method here returns a Cmd; the
// DebuggerProgram interpreter runs them. The active run's cancellation source is
// the one piece of effect state that must outlive a single command.
public sealed partial class DebuggerEffects
{
    private readonly ShaderExecutor _executor = new();
    private readonly HLSLRunner _runner = new();
    private readonly FileDialogService _fileDialogs;

    public DebuggerEffects(FileDialogService fileDialogs) => _fileDialogs = fileDialogs;

    // Gathers the per-frame inputs a ShaderInvocation needs (canvas size, camera
    // matrices, mouse). Everything model-derived is passed in.
    private async Task<ShaderInvocation> BuildAsync(ShaderConfig config, FrameCapture captured, int debugVertexIndex)
    {
        int wx = Math.Max(1, config.WarpX);
        int wy = Math.Max(1, config.WarpY);
        int canvasW = captured?.CanvasW ?? wx;
        int canvasH = captured?.CanvasH ?? wy;

        float[] view = null;
        float[] projection = null;
        if (config.RenderMode == ShaderRenderMode.VertFrag)
        {
            view = await GpuInterop.View();
            projection = await GpuInterop.Projection(canvasW, canvasH);
        }

        float[] mouse;
        try { mouse = await GpuInterop.Mouse(); }
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

    private static HLSLParserConfig MakeParserConfig(string docPath) =>
        new HLSLParserConfig
        {
            BasePath = docPath != null ? System.IO.Path.GetDirectoryName(docPath) ?? "" : "",
        };
}
