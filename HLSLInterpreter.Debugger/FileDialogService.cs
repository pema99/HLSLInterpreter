namespace HLSLInterpreter.Debugger;

public class FileDialogService
{
    private Func<string, string, string, Task>? _fileDropped;

    public Func<string, string, string, Task>? FileDropped
    {
        get => _fileDropped;
        set { _fileDropped = value; if (value != null) OnFileDropCallbackSet(); }
    }

    protected virtual void OnFileDropCallbackSet() { }

    public virtual Task<(string? Path, string? Content)> OpenFile()
        => Task.FromResult<(string?, string?)>((null, null));

    public virtual Task<string?> SaveFile(string content, string? currentPath)
        => Task.FromResult<string?>(null);

    public virtual Task<string?> SaveFileAs(string content)
        => Task.FromResult<string?>(null);
}
