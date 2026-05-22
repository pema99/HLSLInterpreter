using HLSLInterpreter.Debugger.Execution;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Utils;

public static class JsInterop
{
    public static IJSRuntime Js { get; set; }
}

// Monaco editor (editor.js).
public static class EditorInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/editor.js";
    private static IJSObjectReference _module;
    private static async ValueTask<IJSObjectReference> Module() =>
        _module ??= await JsInterop.Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public static async ValueTask Init(string containerId, int docId, string initialCode) =>
        await (await Module()).InvokeVoidAsync("initMonaco", containerId, docId, initialCode);

    public static async ValueTask<string> GetValue() =>
        await (await Module()).InvokeAsync<string>("getMonacoValue");

    public static async ValueTask CreateModel(int id, string content) =>
        await (await Module()).InvokeVoidAsync("createModel", id, content);

    public static async ValueTask ShowModel(int id) =>
        await (await Module()).InvokeVoidAsync("showModel", id);

    public static async ValueTask SetModelContent(int id, string content) =>
        await (await Module()).InvokeVoidAsync("setModelContent", id, content);

    public static async ValueTask DisposeModel(int id) =>
        await (await Module()).InvokeVoidAsync("disposeModel", id);

    public static async ValueTask SetTheme(string theme) =>
        await (await Module()).InvokeVoidAsync("setMonacoTheme", theme);

    public static async ValueTask SetFontSize(int size) =>
        await (await Module()).InvokeVoidAsync("setMonacoFontSize", size);

    public static async ValueTask SetReadOnly(bool readOnly) =>
        await (await Module()).InvokeVoidAsync("setMonacoReadOnly", readOnly);

    public static async ValueTask HighlightLine(int line) =>
        await (await Module()).InvokeVoidAsync("highlightDebugLine", line);

    public static async ValueTask SetBreakpoints(int docId, IReadOnlyList<int> lines) =>
        await (await Module()).InvokeVoidAsync("setBreakpoints", docId, lines);

    public static async ValueTask<bool> IsTabDropAfter(int tabIndex, double clientX) =>
        await (await Module()).InvokeAsync<bool>("dbgIsTabDropAfter", tabIndex, clientX);
}

// Host helpers in host.js
public static class BrowserInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/host.js";
    private static IJSObjectReference _module;
    private static async ValueTask<IJSObjectReference> Module() =>
        _module ??= await JsInterop.Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public static async ValueTask SetDebuggerRef(object reference) =>
        await (await Module()).InvokeVoidAsync("setDebuggerRef", reference);

    public static async ValueTask<string> FetchText(string url) =>
        await (await Module()).InvokeAsync<string>("dbgFetchText", url);

    public static async ValueTask<PickedImage> FetchImage(string url) =>
        await (await Module()).InvokeAsync<PickedImage>("dbgFetchImage", url);

    public static async ValueTask<PickedImage> PickImage() =>
        await (await Module()).InvokeAsync<PickedImage>("dbgPickImage");

    public static async ValueTask PickObj() => await (await Module()).InvokeVoidAsync("dbgPickObj");

    public static async ValueTask RevokeBlobUrl(string url) =>
        await (await Module()).InvokeVoidAsync("dbgRevokeBlobUrl", url);

    public static async ValueTask CopyToClipboard(string text) =>
        await (await Module()).InvokeVoidAsync("copyToClipboard", text);

    public static async ValueTask DownloadTextFile(string fileName, string content) =>
        await (await Module()).InvokeVoidAsync("downloadTextFile", fileName, content);

    public static async ValueTask<string> TranspileGlsl(string glsl) =>
        await (await Module()).InvokeAsync<string>("glsl2hlslTranspile", glsl);

    public static async ValueTask<string> RgbaToDataUrl(byte[] rgba, int width, int height,
        int inspectedX, int inspectedY, double sizeScale) =>
        await (await Module()).InvokeAsync<string>("rgbaToDataUrl", rgba, width, height,
            inspectedX, inspectedY, sizeScale);

    public static async ValueTask ScrollImmediateToBottom() =>
        await (await Module()).InvokeVoidAsync("scrollImmediateToBottom");

    public static async ValueTask InitThreadGridResize(string containerId, int cols, int rows) =>
        await (await Module()).InvokeVoidAsync("initThreadGridResize", containerId, cols, rows);

    public static async ValueTask DisposeThreadGridResize() =>
        await (await Module()).InvokeVoidAsync("disposeThreadGridResize");
}

public sealed record GpuRenderRequest(
    string CanvasId,
    string Source,
    string FragmentEntryPoint,
    int WarpX,
    int WarpY,
    string Mode,
    string VertexEntryPoint,
    IReadOnlyList<VertexInput> VertexInputs,
    float[] MeshVertices,
    uint[] MeshIndices,
    float Time,
    IReadOnlyList<TextureBinding> Textures,
    IReadOnlyList<SamplerBinding> Samplers);

// WebGPU preview loop (gpu.js).
public static class GpuInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/gpu.js";
    private static IJSObjectReference _module;
    private static async ValueTask<IJSObjectReference> Module() =>
        _module ??= await JsInterop.Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public static async ValueTask<bool> IsAvailable()
    {
        try { return await (await Module()).InvokeAsync<bool>("gpuIsAvailable"); }
        catch { return false; }
    }

    public static async ValueTask Render(GpuRenderRequest r) =>
        await (await Module()).InvokeVoidAsync("gpuRender", r.CanvasId, r.Source, r.FragmentEntryPoint,
            r.WarpX, r.WarpY, r.Mode, r.VertexEntryPoint, r.VertexInputs,
            r.MeshVertices, r.MeshIndices, r.Time, r.Textures, r.Samplers);

    public static async ValueTask Stop() => await (await Module()).InvokeVoidAsync("gpuStop");
    public static async ValueTask Pause() => await (await Module()).InvokeVoidAsync("gpuPause");
    public static async ValueTask Resume() => await (await Module()).InvokeVoidAsync("gpuResume");
    public static async ValueTask Restart() => await (await Module()).InvokeVoidAsync("gpuRestart");

    public static async ValueTask<float[]> Snapshot() => await (await Module()).InvokeAsync<float[]>("gpuSnapshot");
    public static async ValueTask<float[]> View() => await (await Module()).InvokeAsync<float[]>("gpuView");

    public static async ValueTask<float[]> Projection(int canvasW, int canvasH) =>
        await (await Module()).InvokeAsync<float[]>("gpuProjection", canvasW, canvasH);

    public static async ValueTask<float[]> Mouse() => await (await Module()).InvokeAsync<float[]>("gpuMouse");
}

// Color output canvas (viewport.js).
public static class CanvasInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/viewport.js";
    private static IJSObjectReference _module;
    private static async ValueTask<IJSObjectReference> Module() =>
        _module ??= await JsInterop.Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public static async ValueTask SetPixels(byte[] pixels, int width, int height) =>
        await (await Module()).InvokeVoidAsync("setPixels", pixels, width, height);

    public static async ValueTask SetPixelsRect(byte[] pixels, int x, int y, int width, int height) =>
        await (await Module()).InvokeVoidAsync("setPixelsRect", pixels, x, y, width, height);

    public static async ValueTask AllocPixels(int width, int height) =>
        await (await Module()).InvokeVoidAsync("allocPixels", width, height);

    public static async ValueTask ApplyState(string containerId, object projection) =>
        await (await Module()).InvokeVoidAsync("applyCanvasState", containerId, projection);

    public static async ValueTask SetMeshData(float[] positions, uint[] indices) =>
        await (await Module()).InvokeVoidAsync("setMeshData", positions, indices);
}
