using HLSLInterpreter.Debugger.State;

namespace HLSLInterpreter.Debugger.Mvu;

// The pure update function: computes the next model and the effects to run.
public delegate (AppState State, IReadOnlyList<Cmd> Commands) UpdateFn(AppState model, Msg message);

// The MVU runtime. Holds the single application model and runs the dispatch
// loop. Messages are queued and pumped one at a time, so ordering is defined
// and the loop is never re-entered: a message dispatched from within an effect
// (or from a JS callback) is simply enqueued for the running pump.
public sealed class MvuProgram
{
    private readonly UpdateFn _update;
    private readonly IEffectRunner _effects;
    private readonly Queue<Msg> _queue = new();
    private bool _pumping;

    public AppState Model { get; private set; }

    // Raised after every model swap. Subscribers re-select their own slice and
    // decide for themselves whether the change concerns them.
    public event Action Changed;

    public MvuProgram(AppState initial, UpdateFn update, IEffectRunner effects)
    {
        Model = initial;
        _update = update;
        _effects = effects;
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
                var (model, commands) = _update(Model, message);
                Model = model;
                Changed?.Invoke();
                foreach (var command in commands)
                    await _effects.RunAsync(command, Dispatch);
            }
        }
        finally
        {
            _pumping = false;
        }
    }
}
