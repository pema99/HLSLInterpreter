using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the remaining host and document helpers in app.js: file IO,
// clipboard, the glsl2hlsl transpiler, and DOM layout glue.
public interface IBrowserInterop
{
    ValueTask SetDebuggerRef(object reference);
    ValueTask<string> FetchText(string url);
    ValueTask<PickedImage> FetchImage(string url);
    ValueTask<PickedImage> PickImage();
    ValueTask PickObj();
    ValueTask RevokeBlobUrl(string url);
    ValueTask CopyToClipboard(string text);
    ValueTask DownloadTextFile(string fileName, string content);
    ValueTask<string> TranspileGlsl(string glsl);
    ValueTask ScrollImmediateToBottom();
    ValueTask SaveSectionHeights();
    ValueTask RestoreSectionHeights();
    ValueTask RestoreImageSectionHeight();
    ValueTask InitThreadGridResize(string containerId, int cols, int rows);
    ValueTask DisposeThreadGridResize();
}

public sealed class BrowserInterop : IBrowserInterop
{
    private readonly IJSRuntime _js;

    public BrowserInterop(IJSRuntime js) => _js = js;

    public ValueTask SetDebuggerRef(object reference) =>
        _js.InvokeVoidAsync("setDebuggerRef", reference);

    public ValueTask<string> FetchText(string url) => _js.InvokeAsync<string>("dbgFetchText", url);

    public ValueTask<PickedImage> FetchImage(string url) =>
        _js.InvokeAsync<PickedImage>("dbgFetchImage", url);

    public ValueTask<PickedImage> PickImage() => _js.InvokeAsync<PickedImage>("dbgPickImage");

    public ValueTask PickObj() => _js.InvokeVoidAsync("dbgPickObj");

    public ValueTask RevokeBlobUrl(string url) => _js.InvokeVoidAsync("dbgRevokeBlobUrl", url);

    public ValueTask CopyToClipboard(string text) => _js.InvokeVoidAsync("copyToClipboard", text);

    public ValueTask DownloadTextFile(string fileName, string content) =>
        _js.InvokeVoidAsync("downloadTextFile", fileName, content);

    public ValueTask<string> TranspileGlsl(string glsl) =>
        _js.InvokeAsync<string>("glsl2hlslTranspile", glsl);

    public ValueTask ScrollImmediateToBottom() => _js.InvokeVoidAsync("scrollImmediateToBottom");

    public ValueTask SaveSectionHeights() => _js.InvokeVoidAsync("saveSectionHeights");

    public ValueTask RestoreSectionHeights() => _js.InvokeVoidAsync("restoreSectionHeights");

    public ValueTask RestoreImageSectionHeight() => _js.InvokeVoidAsync("restoreImageSectionHeight");

    public ValueTask InitThreadGridResize(string containerId, int cols, int rows) =>
        _js.InvokeVoidAsync("initThreadGridResize", containerId, cols, rows);

    public ValueTask DisposeThreadGridResize() => _js.InvokeVoidAsync("disposeThreadGridResize");
}
