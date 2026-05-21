using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the Monaco editor exports in app.js.
public sealed class EditorInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/app.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public EditorInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> Module() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask Init(string containerId, string initialCode, object editorRef) =>
        await (await Module()).InvokeVoidAsync("initMonaco", containerId, initialCode, editorRef);

    public async ValueTask<string> GetValue() =>
        await (await Module()).InvokeAsync<string>("getMonacoValue");

    public async ValueTask SetValue(string value) =>
        await (await Module()).InvokeVoidAsync("setMonacoValue", value);

    public async ValueTask SetTheme(string theme) =>
        await (await Module()).InvokeVoidAsync("setMonacoTheme", theme);

    public async ValueTask SetFontSize(int size) =>
        await (await Module()).InvokeVoidAsync("setMonacoFontSize", size);

    public async ValueTask SetReadOnly(bool readOnly) =>
        await (await Module()).InvokeVoidAsync("setMonacoReadOnly", readOnly);

    public async ValueTask HighlightLine(int line) =>
        await (await Module()).InvokeVoidAsync("highlightDebugLine", line);

    public async ValueTask SetBreakpoints(IReadOnlyList<int> lines) =>
        await (await Module()).InvokeVoidAsync("setBreakpoints", lines);

    public async ValueTask<bool> IsTabDropAfter(int tabIndex, double clientX) =>
        await (await Module()).InvokeAsync<bool>("dbgIsTabDropAfter", tabIndex, clientX);
}
