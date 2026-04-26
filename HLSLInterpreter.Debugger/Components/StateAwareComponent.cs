using System.ComponentModel;
using HLSLInterpreter.Debugger.Core;
using Microsoft.AspNetCore.Components;

namespace HLSLInterpreter.Debugger.Components;

/// <summary>
/// Base class for components that read from <see cref="DebuggerAppState"/> and need to
/// re-render when any of its properties change.
/// </summary>
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

    protected virtual void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }
}
