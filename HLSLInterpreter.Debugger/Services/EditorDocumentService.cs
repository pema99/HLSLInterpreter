using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Services;

// Owns the open documents and keeps the single Monaco instance in sync with the
// active one. Replaces the tab and file logic in EditorPanel.
public sealed class EditorDocumentService
{
    private readonly AppStore _store;
    private readonly IEditorInterop _editor;
    private readonly FileDialogService _fileDialogs;
    private readonly bool _tabsEnabled;

    private int _nextId;

    // Mesh that newly created documents start with. Set once the monkey .obj
    // has loaded at startup.
    public Mesh DefaultMesh { get; set; } = Mesh.CreateCube();

    public EditorDocumentService(
        AppStore store, IEditorInterop editor, FileDialogService fileDialogs, TabbedEditorOptions tabbedEditor)
    {
        _store = store;
        _editor = editor;
        _fileDialogs = fileDialogs;
        _tabsEnabled = tabbedEditor.Enabled;
        if (_tabsEnabled)
            _fileDialogs.FileDropped = OpenDroppedAsync;
    }

    public ShaderDocument NewDocument(string name, string code, string path = null, ShaderConfig config = null) =>
        new()
        {
            Id = _nextId++,
            Name = name,
            Path = path,
            Code = code,
            Config = config ?? new ShaderConfig { Mesh = DefaultMesh },
        };

    public async Task<string> GetActiveCodeAsync()
    {
        string code = await _editor.GetValue();
        _store.SetActiveCode(code);
        return code;
    }

    public async Task SyncActiveFromEditorAsync() =>
        _store.SetActiveCode(await _editor.GetValue());

    public async Task SwitchToAsync(int index)
    {
        if (index == _store.State.Editor.ActiveIndex) return;
        await SyncActiveFromEditorAsync();
        _store.SetActiveDocument(index);
        await _editor.SetValue(_store.State.Editor.ActiveDocument?.Code ?? "");
    }

    public async Task CloseTabAsync(int index)
    {
        if (_store.State.Editor.Documents.Count <= 1) return;
        bool activeChanges = index == _store.State.Editor.ActiveIndex;
        _store.CloseDocument(index);
        if (activeChanges)
            await _editor.SetValue(_store.State.Editor.ActiveDocument?.Code ?? "");
    }

    public void MoveTab(int from, int desired) => _store.MoveDocument(from, desired);

    public ValueTask<bool> IsTabDropAfterAsync(int tabIndex, double clientX) =>
        _editor.IsTabDropAfter(tabIndex, clientX);

    public async Task OpenFileDialogAsync()
    {
        var (path, content) = await _fileDialogs.OpenFile();
        if (path == null || content == null) return;
        int existing = IndexOfPath(path);
        if (existing >= 0) { await SwitchToAsync(existing); return; }
        await SyncActiveFromEditorAsync();
        _store.AddDocument(NewDocument(System.IO.Path.GetFileName(path), content, path));
        await _editor.SetValue(content);
    }

    public async Task SaveFileAsync()
    {
        string code = await _editor.GetValue();
        string path = await _fileDialogs.SaveFile(code, _store.State.Editor.ActiveDocument?.Path);
        if (path == null) return;
        _store.SetActiveCode(code);
        _store.UpdateActiveDocument(d => d with { Path = path, Name = System.IO.Path.GetFileName(path) });
    }

    public async Task SaveFileAsAsync()
    {
        string code = await _editor.GetValue();
        string path = await _fileDialogs.SaveFileAs(code);
        if (path == null) return;
        _store.SetActiveCode(code);
        _store.UpdateActiveDocument(d => d with { Path = path, Name = System.IO.Path.GetFileName(path) });
    }

    // Opens content from an example, import, or template. With tabs enabled it
    // becomes a new tab, otherwise it replaces the single document.
    public async Task LoadContentAsync(string name, string content)
    {
        if (_tabsEnabled)
        {
            await SyncActiveFromEditorAsync();
            _store.AddDocument(NewDocument(name, content));
        }
        else
        {
            _store.SetActiveCode(content);
            _store.UpdateActiveDocument(d => d with { Name = name });
        }
        await _editor.SetValue(content);
    }

    private async Task OpenDroppedAsync(string name, string content, string path)
    {
        int existing = string.IsNullOrEmpty(path) ? -1 : IndexOfPath(path);
        if (existing >= 0) { await SwitchToAsync(existing); return; }
        await SyncActiveFromEditorAsync();
        _store.AddDocument(NewDocument(name, content, string.IsNullOrEmpty(path) ? null : path));
        await _editor.SetValue(content);
    }

    private int IndexOfPath(string path)
    {
        var documents = _store.State.Editor.Documents;
        for (int i = 0; i < documents.Count; i++)
            if (documents[i].Path == path) return i;
        return -1;
    }
}
