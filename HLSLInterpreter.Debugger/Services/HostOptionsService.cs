namespace HLSLInterpreter.Debugger.Services;

// Configuration each host (Web, Desktop) supplies at startup.
public sealed class HostOptionsService
{
    public string InitialCode { get; init; }
    public string InitialName { get; init; }
    public string InitialPath { get; init; }
    public string PermalinkUrl { get; init; }
    public bool TabsEnabled { get; init; }
}
