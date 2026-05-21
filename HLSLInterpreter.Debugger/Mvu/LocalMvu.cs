namespace HLSLInterpreter.Debugger.Mvu;

// A self-contained MVU cell for one component's local view state (panel
// collapse, expanded tree paths, and the like). State that no other component
// reads and no effect depends on lives here rather than in the global model.
// Local view state needs no commands, so the update is a plain function.
public sealed class LocalMvu<TModel, TMsg>
{
    private readonly Func<TMsg, TModel, TModel> _update;
    private readonly Action _onChanged;

    public TModel State { get; private set; }

    public LocalMvu(TModel initial, Func<TMsg, TModel, TModel> update, Action onChanged)
    {
        State = initial;
        _update = update;
        _onChanged = onChanged;
    }

    public void Dispatch(TMsg message)
    {
        State = _update(message, State);
        _onChanged();
    }
}
