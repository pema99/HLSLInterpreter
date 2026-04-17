namespace HLSLInterpreter.Debugger;

/// <summary>
/// Optional override for the base URL used when generating permalinks.
/// When null, the current page URL is used instead.
/// </summary>
public class PermalinkBaseUrl
{
    public string? Url { get; init; }
}
