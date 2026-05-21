namespace HLSLInterpreter.Debugger.Services;

// Configuration each host (Web, Desktop) supplies at startup, before calling
// AddDebuggerServices.

public class InitialCodeOptions
{
    public string Code { get; init; }
    public string Name { get; init; }
    public string Path { get; init; }
}

public class PermalinkOptions
{
    public string Url { get; init; }
}

public class TabbedEditorOptions
{
    public bool Enabled { get; init; }
}
