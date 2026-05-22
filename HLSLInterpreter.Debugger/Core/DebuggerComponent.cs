using Microsoft.AspNetCore.Components;

namespace HLSLInterpreter.Debugger.Core;

// Base for components bound to a slice of the model. A component re-renders only when its own slice changes.
public abstract class DebuggerComponent<TSlice> : ComponentBase, IDisposable
{
    [Inject] protected DebuggerProgram Program { get; set; } = null!;

    protected TSlice Slice { get; private set; } = default!;

    protected abstract TSlice Select(DebuggerModel model);

    protected void Dispatch(Msg message) => Program.Dispatch(message);

    protected override void OnInitialized()
    {
        Slice = Select(Program.Model);
        Program.ModelChanged += OnProgramChanged;
    }

    private void OnProgramChanged()
    {
        var next = Select(Program.Model);
        if (EqualityComparer<TSlice>.Default.Equals(next, Slice)) return;
        Slice = next;
        InvokeAsync(StateHasChanged);
    }

    public virtual void Dispose() => Program.ModelChanged -= OnProgramChanged;
}
