using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.Mvu;

// Editor, file-dialog, and browser effects.
public sealed partial class Effects
{
    public Cmd FetchEditorText(Func<string, Msg> then) =>
        Cmd.OfTask(async () => then(await GetEditorText()));

    private async Task<string> GetEditorText()
    {
        try { return await _editor.GetValue(); }
        catch { return ""; }
    }

    // One Monaco model per document: the model owns the text and undo history.
    public Cmd CreateModel(int docId, string content) =>
        Cmd.OfTask(() => _editor.CreateModel(docId, content).AsTask());

    public Cmd ShowModel(int docId) =>
        Cmd.OfTask(() => _editor.ShowModel(docId).AsTask());

    public Cmd SetModelContent(int docId, string content) =>
        Cmd.OfTask(() => _editor.SetModelContent(docId, content).AsTask());

    public Cmd DisposeModel(int docId) =>
        Cmd.OfTask(() => _editor.DisposeModel(docId).AsTask());

    public Cmd SetEditorFontSize(int size) =>
        Cmd.OfTask(() => _editor.SetFontSize(size).AsTask());

    public Cmd SetEditorReadOnly(bool readOnly) =>
        Cmd.OfTask(() => _editor.SetReadOnly(readOnly).AsTask());

    public Cmd HighlightLine(int line) =>
        Cmd.OfTask(() => _editor.HighlightLine(line).AsTask());

    public Cmd SetBreakpoints(IReadOnlyList<int> lines) =>
        Cmd.OfTask(() => _editor.SetBreakpoints(lines).AsTask());

    public Cmd OpenFileDialog() =>
        Cmd.OfTask(async () =>
        {
            var (path, content) = await _fileDialogs.OpenFile();
            return (Msg)new FileOpened(path, content);
        });

    public Cmd SaveFileDialog(string code, string currentPath, bool asNew) =>
        Cmd.OfEffect(async dispatch =>
        {
            string path = asNew
                ? await _fileDialogs.SaveFileAs(code)
                : await _fileDialogs.SaveFile(code, currentPath);
            if (path != null) dispatch(new FileSaved(path));
        });

    public Cmd DownloadFile(string fileName, string content) =>
        Cmd.OfTask(() => _browser.DownloadTextFile(fileName, content).AsTask());

    public Cmd PickObjFile() =>
        Cmd.OfTask(() => _browser.PickObj().AsTask());

    public Cmd CopyToClipboard(string text) =>
        Cmd.OfTask(() => _browser.CopyToClipboard(text).AsTask());
}
