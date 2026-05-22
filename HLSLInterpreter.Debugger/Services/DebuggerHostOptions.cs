namespace HLSLInterpreter.Debugger.Services;

// Configuration each host (Web, Desktop) supplies at startup, before calling
// AddDebuggerServices.
public sealed class DebuggerHostOptions
{
    public string InitialCode { get; init; }
    public string InitialName { get; init; }
    public string InitialPath { get; init; }
    public string PermalinkUrl { get; init; }
    public bool TabsEnabled { get; init; }
}
