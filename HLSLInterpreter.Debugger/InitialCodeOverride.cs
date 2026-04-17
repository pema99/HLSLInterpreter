namespace HLSLInterpreter.Debugger;

/// <summary>
/// Optional override for the initial code loaded into the editor.
/// When null, the default example code (or permalink) is used instead.
/// </summary>
public class InitialCodeOverride
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
}
