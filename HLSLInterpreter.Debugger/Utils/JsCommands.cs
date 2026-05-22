using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Utils;

// Every [JSInvokable] the app exposes
public sealed class JsCommands
{
    private readonly DebuggerProgram _program;

    public JsCommands(DebuggerProgram program) => _program = program;

    [JSInvokable]
    public void ToggleBreakpoint(int line) => _program.Dispatch(new BreakpointToggled(line));

    [JSInvokable]
    public void LoadObjMesh(string objText) => _program.Dispatch(new ObjMeshLoaded(objText));

    [JSInvokable]
    public void StartDebugAtPixel(int px, int py) =>
        _program.Dispatch(new DebugClicked(DebugTarget.Pixel, px, py));

    [JSInvokable]
    public void StartDebugAtVertex(int vertexIndex) =>
        _program.Dispatch(new DebugClicked(DebugTarget.Vertex, vertexIndex, 0));

    [JSInvokable]
    public void SetInspectedPixel(int px, int py) =>
        _program.Dispatch(new InspectedPixelChanged(px, py));

    [JSInvokable]
    public void OnFileDropped(string name, string content) =>
        _program.Dispatch(new FileOpened(name, "", content));

    [JSInvokable]
    public void CanvasResized(int width, int height) =>
        _program.Dispatch(new CanvasResized(width, height));

    [JSInvokable]
    public HoverInfo GetHoverInfo(string identifier)
    {
        var debug = _program.Model.Debug;
        var config = _program.Model.Editor.ActiveDocument?.Config ?? new ShaderConfig();
        return HLSLValueDisplay.BuildHoverInfo(
            debug.CurrentStep, config.WarpX, config.WarpY,
            debug.InspectedThread, debug.DebugVertexIndex, identifier);
    }
}
