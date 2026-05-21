namespace HLSLInterpreter.Debugger.Mvu;

// Carries out commands. This is the one impure component in the architecture.
// An async effect dispatches a follow-up Msg when it completes. Implementations
// must not throw: a failed effect is reported by dispatching an error message,
// so the dispatch pump cannot be broken by an effect.
public interface IEffectRunner
{
    Task RunAsync(Cmd command, Action<Msg> dispatch);
}

// Phase 1 skeleton. The command handlers, and the tools they use (ShaderExecutor,
// the interop facades, ImageLibrary, ...), are wired up in phase 3.
public sealed class EffectRunner : IEffectRunner
{
    public Task RunAsync(Cmd command, Action<Msg> dispatch) => Task.CompletedTask;
}
