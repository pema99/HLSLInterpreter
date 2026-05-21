using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.Mvu;
using Microsoft.Extensions.DependencyInjection;

namespace HLSLInterpreter.Debugger.Services;

public static class DebuggerServices
{
    // Registers the MVU runtime, the effects, and the interop facades. The hosts
    // add their own PermalinkOptions, InitialCodeOptions, TabbedEditorOptions,
    // and FileDialogService before calling this.
    public static IServiceCollection AddDebuggerServices(this IServiceCollection services)
    {
        services.AddScoped<ShaderExecutor>();
        services.AddScoped<ShaderInvocationBuilder>();
        services.AddScoped<ImageLibrary>();
        services.AddScoped<EditorInterop>();
        services.AddScoped<GpuInterop>();
        services.AddScoped<CanvasInterop>();
        services.AddScoped<BrowserInterop>();
        services.AddScoped<DebuggerEffects>();
        services.AddScoped(sp =>
            new DebuggerProgram(DebuggerModel.Initial, sp.GetRequiredService<DebuggerEffects>()));
        return services;
    }
}
