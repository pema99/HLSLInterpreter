namespace HLSLInterpreter.Debugger.Mvu;

// Editor and file-dialog effects.
public sealed partial class EffectRunner
{
    private async Task FetchTextEffect(FetchEditorText c, Action<Msg> dispatch)
    {
        string code;
        try { code = await _editor.GetValue(); }
        catch { code = ""; }
        dispatch(c.Then(code));
    }

    private async Task OpenFileDialogEffect(Action<Msg> dispatch)
    {
        var (path, content) = await _fileDialogs.OpenFile();
        dispatch(new FileOpened(path, content));
    }

    private async Task SaveFileDialogEffect(SaveFileDialog c, Action<Msg> dispatch)
    {
        string path = c.AsNew
            ? await _fileDialogs.SaveFileAs(c.Code)
            : await _fileDialogs.SaveFile(c.Code, c.CurrentPath);
        if (path != null) dispatch(new FileSaved(path));
    }
}
