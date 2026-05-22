using Microsoft.AspNetCore.Components;

namespace HLSLInterpreter.Debugger.Core;

// Base for components that render global state. A component selects the single
// slice it needs and re-renders only when that slice changes, so an unrelated
// part of the model changing does not re-run its render. User actions are
// raised with Dispatch.
public abstract class DebuggerComponent<TSlice> : ComponentBase, IDisposable
{
    [Inject] protected DebuggerProgram Program { get; set; } = null!;

    protected TSlice Slice { get; private set; } = default!;

    protected abstract TSlice Select(DebuggerModel model);

    protected void Dispatch(Msg message) => Program.Dispatch(message);

    protected override void OnInitialized()
    {
        Slice = Select(Program.Model);
        Program.Changed += OnProgramChanged;
    }

    private void OnProgramChanged()
    {
        var next = Select(Program.Model);
        if (EqualityComparer<TSlice>.Default.Equals(next, Slice)) return;
        Slice = next;
        InvokeAsync(StateHasChanged);
    }

    public virtual void Dispose() => Program.Changed -= OnProgramChanged;
}
