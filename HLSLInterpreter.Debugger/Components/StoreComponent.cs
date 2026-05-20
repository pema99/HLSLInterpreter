using HLSLInterpreter.Debugger.State;
using Microsoft.AspNetCore.Components;

namespace HLSLInterpreter.Debugger.Components;

// Base component that re-renders only when its selected slice changes. Each
// subclass picks one slice via Select, so a change to an unrelated slice does
// not invalidate it. Select must return a slice record, not a scalar.
public abstract class StoreComponent<TSlice> : ComponentBase, IDisposable
{
    [Inject] protected AppStore Store { get; set; }

    protected TSlice Slice { get; private set; }

    protected abstract TSlice Select(AppState state);

    protected override void OnInitialized()
    {
        Slice = Select(Store.State);
        Store.Changed += OnStoreChanged;
    }

    private void OnStoreChanged(AppState previous, AppState next)
    {
        var selected = Select(next);
        if (EqualityComparer<TSlice>.Default.Equals(selected, Slice)) return;
        Slice = selected;
        InvokeAsync(StateHasChanged);
    }

    public virtual void Dispose() => Store.Changed -= OnStoreChanged;
}
