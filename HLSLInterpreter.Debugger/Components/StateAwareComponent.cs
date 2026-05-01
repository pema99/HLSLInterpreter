using System.ComponentModel;
using HLSLInterpreter.Debugger.Services;
using Microsoft.AspNetCore.Components;

namespace HLSLInterpreter.Debugger.Components;

public abstract class StateAwareComponent : ComponentBase, IDisposable
{
    [Inject] protected DebuggerAppState State { get; set; } = null!;

    protected override void OnInitialized()
    {
        State.PropertyChanged += OnStateChanged;
    }

    public virtual void Dispose()
    {
        State.PropertyChanged -= OnStateChanged;
    }

    protected virtual void OnStateChanged(object sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }
}
