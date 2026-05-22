using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Services;

namespace HLSLInterpreter.Debugger.Mvu;

// Editor, file-dialog, and browser effects.
public sealed partial class DebuggerEffects
{
    public Cmd FetchEditorText(Func<string, Msg> then) =>
        Cmd.OfTask(async () => then(await GetEditorText()));

    private async Task<string> GetEditorText()
    {
        try { return await EditorInterop.GetValue(); }
        catch { return ""; }
    }

    // One Monaco model per document: the model owns the text and undo history.
    public Cmd CreateModel(int docId, string content) =>
        Cmd.OfTask(() => EditorInterop.CreateModel(docId, content).AsTask());

    public Cmd ShowModel(int docId) =>
        Cmd.OfTask(() => EditorInterop.ShowModel(docId).AsTask());

    public Cmd SetModelContent(int docId, string content) =>
        Cmd.OfTask(() => EditorInterop.SetModelContent(docId, content).AsTask());

    public Cmd DisposeModel(int docId) =>
        Cmd.OfTask(() => EditorInterop.DisposeModel(docId).AsTask());

    public Cmd SetEditorFontSize(int size) =>
        Cmd.OfTask(() => EditorInterop.SetFontSize(size).AsTask());

    public Cmd SetEditorReadOnly(bool readOnly) =>
        Cmd.OfTask(() => EditorInterop.SetReadOnly(readOnly).AsTask());

    public Cmd SetTheme(string theme) =>
        Cmd.OfTask(() => EditorInterop.SetTheme(theme).AsTask());

    public Cmd HighlightLine(int line) =>
        Cmd.OfTask(() => EditorInterop.HighlightLine(line).AsTask());

    public Cmd SetBreakpoints(int docId, IReadOnlyList<int> lines) =>
        Cmd.OfTask(() => EditorInterop.SetBreakpoints(docId, lines).AsTask());

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
        Cmd.OfTask(() => BrowserInterop.DownloadTextFile(fileName, content).AsTask());

    public Cmd PickObjFile() =>
        Cmd.OfTask(() => BrowserInterop.PickObj().AsTask());

    public Cmd CopyToClipboard(string text) =>
        Cmd.OfTask(() => BrowserInterop.CopyToClipboard(text).AsTask());
}
