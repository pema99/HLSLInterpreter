using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Mvu;
using HLSLInterpreter.Debugger.State;
using Microsoft.Extensions.DependencyInjection;

namespace HLSLInterpreter.Debugger.Services;

public static class DebuggerServices
{
    // Registers the MVU runtime, the effect runner, and the interop facades. The
    // hosts add their own PermalinkOptions, InitialCodeOptions,
    // TabbedEditorOptions, and FileDialogService before calling this.
    public static IServiceCollection AddDebuggerServices(this IServiceCollection services)
    {
        services.AddScoped<ShaderExecutor>();
        services.AddScoped<ShaderInvocationBuilder>();
        services.AddScoped<ImageLibrary>();
        services.AddScoped<EditorInterop>();
        services.AddScoped<GpuInterop>();
        services.AddScoped<CanvasInterop>();
        services.AddScoped<BrowserInterop>();
        services.AddScoped<EffectRunner>();
        services.AddScoped(sp => new MvuProgram(
            AppState.Initial, Update.Run, sp.GetRequiredService<EffectRunner>()));
        return services;
    }
}
