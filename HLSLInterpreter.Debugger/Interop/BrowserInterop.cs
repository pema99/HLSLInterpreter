using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the host and document helpers in app.js: file IO,
// clipboard, the glsl2hlsl transpiler, image rendering, and DOM layout glue.
public sealed class BrowserInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/app.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public BrowserInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> Module() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask SetDebuggerRef(object reference) =>
        await (await Module()).InvokeVoidAsync("setDebuggerRef", reference);

    public async ValueTask<string> FetchText(string url) =>
        await (await Module()).InvokeAsync<string>("dbgFetchText", url);

    public async ValueTask<PickedImage> FetchImage(string url) =>
        await (await Module()).InvokeAsync<PickedImage>("dbgFetchImage", url);

    public async ValueTask<PickedImage> PickImage() =>
        await (await Module()).InvokeAsync<PickedImage>("dbgPickImage");

    public async ValueTask PickObj() => await (await Module()).InvokeVoidAsync("dbgPickObj");

    public async ValueTask RevokeBlobUrl(string url) =>
        await (await Module()).InvokeVoidAsync("dbgRevokeBlobUrl", url);

    public async ValueTask CopyToClipboard(string text) =>
        await (await Module()).InvokeVoidAsync("copyToClipboard", text);

    public async ValueTask DownloadTextFile(string fileName, string content) =>
        await (await Module()).InvokeVoidAsync("downloadTextFile", fileName, content);

    public async ValueTask<string> TranspileGlsl(string glsl) =>
        await (await Module()).InvokeAsync<string>("glsl2hlslTranspile", glsl);

    public async ValueTask<string> RgbaToDataUrl(byte[] rgba, int width, int height,
        int inspectedX, int inspectedY, double sizeScale) =>
        await (await Module()).InvokeAsync<string>("rgbaToDataUrl", rgba, width, height,
            inspectedX, inspectedY, sizeScale);

    public async ValueTask ScrollImmediateToBottom() =>
        await (await Module()).InvokeVoidAsync("scrollImmediateToBottom");

    public async ValueTask SaveSectionHeights() =>
        await (await Module()).InvokeVoidAsync("saveSectionHeights");

    public async ValueTask RestoreSectionHeights() =>
        await (await Module()).InvokeVoidAsync("restoreSectionHeights");

    public async ValueTask RestoreImageSectionHeight() =>
        await (await Module()).InvokeVoidAsync("restoreImageSectionHeight");

    public async ValueTask InitThreadGridResize(string containerId, int cols, int rows) =>
        await (await Module()).InvokeVoidAsync("initThreadGridResize", containerId, cols, rows);

    public async ValueTask DisposeThreadGridResize() =>
        await (await Module()).InvokeVoidAsync("disposeThreadGridResize");
}
