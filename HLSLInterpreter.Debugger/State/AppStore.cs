namespace HLSLInterpreter.Debugger.State;

// The single source of truth. Every mutation goes through Update, which swaps in
// a new immutable AppState and notifies subscribers with the old and new values.
public sealed class AppStore
{
    public AppState State { get; private set; } = AppState.Initial;

    public event Action<AppState, AppState> Changed;

    public void Update(Func<AppState, AppState> reducer)
    {
        var next = reducer(State);
        if (ReferenceEquals(next, State)) return;
        var previous = State;
        State = next;
        Changed?.Invoke(previous, next);
    }
}
