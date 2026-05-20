using HLSL;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Core;

public readonly record struct StatementEvent(HLSLSyntaxNode Node, HLSLRunner Runner, int OutputLength);

public sealed class ExecutionOptions
{
    public static readonly ExecutionOptions None = new();

    public Action<StatementEvent> BeforeStatement { get; init; }
    public Action<StatementEvent> AfterStatement { get; init; }

    // Attach the hooks before the program is loaded so global initializers are
    // observed. The trace recorder needs this, plain runs and metrics do not.
    public bool ObserveProgramLoad { get; init; }

    // Redirect Console.Out for this run. Disabled for tiled full-frame tiles,
    // where one outer redirect spans all tiles instead.
    public bool CaptureConsole { get; init; } = true;
}
