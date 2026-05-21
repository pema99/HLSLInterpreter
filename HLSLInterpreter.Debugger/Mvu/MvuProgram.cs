using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// The pure update function: computes the next model and the command to run.
public delegate (AppState State, Cmd Command) UpdateFn(AppState model, Msg message);

// The MVU runtime. Holds the single application model, runs the dispatch loop,
// and interprets commands. Messages are queued and pumped one at a time, so
// ordering is defined and the loop is never re-entered: a message dispatched
// from within an effect (or a JS callback) is simply enqueued for the running
// pump. This type knows nothing app-specific: the command vocabulary is generic.
public sealed class MvuProgram : IDisposable
{
    private readonly UpdateFn _update;
    private readonly Queue<Msg> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;

    public AppState Model { get; private set; }

    // Raised after every model swap. Subscribers re-select their own slice and
    // decide for themselves whether the change concerns them.
    public event Action Changed;

    public MvuProgram(AppState initial, UpdateFn update)
    {
        Model = initial;
        _update = update;
    }

    public void Dispatch(Msg message)
    {
        _queue.Enqueue(message);
        if (!_pumping) _ = Pump();
    }

    private async Task Pump()
    {
        _pumping = true;
        try
        {
            while (_queue.Count > 0)
            {
                var message = _queue.Dequeue();
                var (model, command) = _update(Model, message);
                Model = model;
                Changed?.Invoke();
                await Execute(command);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    // The command interpreter. A fixed, exhaustive switch over the generic Cmd
    // vocabulary. An effect must never break the pump, so failures are swallowed.
    private async Task Execute(Cmd command)
    {
        try
        {
            switch (command)
            {
                case Cmd.BatchCmd b:
                    foreach (var c in b.Commands) await Execute(c);
                    break;
                case Cmd.MsgCmd m:
                    Dispatch(m.Message);
                    break;
                case Cmd.TaskCmd t:
                    Dispatch(await t.Run(_cts.Token));
                    break;
                case Cmd.TaskUnitCmd t:
                    await t.Run(_cts.Token);
                    break;
                case Cmd.EffectCmd e:
                    await e.Run(Dispatch, _cts.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // An effect must never break the dispatch pump.
        }
    }

    public void Dispose() => _cts.Cancel();
}
