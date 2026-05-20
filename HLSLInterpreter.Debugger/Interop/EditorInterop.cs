using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the Monaco editor functions in app.js.
public interface IEditorInterop
{
    ValueTask Init(string containerId, string initialCode, object editorRef);
    ValueTask<string> GetValue();
    ValueTask SetValue(string value);
    ValueTask SetTheme(string theme);
    ValueTask SetFontSize(int size);
    ValueTask SetReadOnly(bool readOnly);
    ValueTask HighlightLine(int line);
    ValueTask SetBreakpoints(IReadOnlyList<int> lines);
    ValueTask<bool> IsTabDropAfter(int tabIndex, double clientX);
}

public sealed class EditorInterop : IEditorInterop
{
    private readonly IJSRuntime _js;

    public EditorInterop(IJSRuntime js) => _js = js;

    public ValueTask Init(string containerId, string initialCode, object editorRef) =>
        _js.InvokeVoidAsync("initMonaco", containerId, initialCode, editorRef);

    public ValueTask<string> GetValue() => _js.InvokeAsync<string>("getMonacoValue");

    public ValueTask SetValue(string value) => _js.InvokeVoidAsync("setMonacoValue", value);

    public ValueTask SetTheme(string theme) => _js.InvokeVoidAsync("setMonacoTheme", theme);

    public ValueTask SetFontSize(int size) => _js.InvokeVoidAsync("setMonacoFontSize", size);

    public ValueTask SetReadOnly(bool readOnly) => _js.InvokeVoidAsync("setMonacoReadOnly", readOnly);

    public ValueTask HighlightLine(int line) => _js.InvokeVoidAsync("highlightDebugLine", line);

    public ValueTask SetBreakpoints(IReadOnlyList<int> lines) =>
        _js.InvokeVoidAsync("setBreakpoints", lines);

    public ValueTask<bool> IsTabDropAfter(int tabIndex, double clientX) =>
        _js.InvokeAsync<bool>("dbgIsTabDropAfter", tabIndex, clientX);
}
