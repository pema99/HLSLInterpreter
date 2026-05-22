namespace HLSLInterpreter.Debugger.Core;

// What an update step asks the runtime to do next, interpreted by DebuggerProgram.
public abstract record Cmd
{
    public static readonly Cmd None = new BatchCmd([]);

    public static Cmd OfMsg(Msg message) => new MsgCmd(message);

    public static Cmd Batch(params Cmd[] commands) => new BatchCmd(commands);
    public static Cmd Batch(IEnumerable<Cmd> commands) => new BatchCmd(commands.ToArray());

    public static Cmd OfTask(Func<Task<Msg>> run) => new TaskCmd(_ => run());
    public static Cmd OfTask(Func<Task> run) => new TaskUnitCmd(_ => run());
    public static Cmd OfValueTask(Func<ValueTask> run) => new TaskUnitCmd(_ => run().AsTask());

    public static Cmd OfEffect(Func<Action<Msg>, CancellationToken, Task> run) => new EffectCmd(run);
    public static Cmd OfEffect(Func<Action<Msg>, Task> run) => new EffectCmd((dispatch, _) => run(dispatch));

    internal sealed record MsgCmd(Msg Message) : Cmd;
    internal sealed record BatchCmd(IReadOnlyList<Cmd> Commands) : Cmd;
    internal sealed record TaskCmd(Func<CancellationToken, Task<Msg>> Run) : Cmd;
    internal sealed record TaskUnitCmd(Func<CancellationToken, Task> Run) : Cmd;
    internal sealed record EffectCmd(Func<Action<Msg>, CancellationToken, Task> Run) : Cmd;
}
