using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Interop;
using HLSLInterpreter.Debugger.State;
using Microsoft.Extensions.DependencyInjection;

namespace HLSLInterpreter.Debugger.Services;

public static class DebuggerServices
{
    // Registers the store, services, and interop facades. The hosts add their
    // own PermalinkOptions, InitialCodeOptions, TabbedEditorOptions, and
    // FileDialogService before calling this.
    public static IServiceCollection AddDebuggerServices(this IServiceCollection services)
    {
        services.AddScoped<AppStore>();
        services.AddScoped<ShaderExecutor>();
        services.AddScoped<ShaderInvocationBuilder>();
        services.AddScoped<ShaderRunService>();
        services.AddScoped<DebugSessionService>();
        services.AddScoped<EditorDocumentService>();
        services.AddScoped<PermalinkService>();
        services.AddScoped<JsProjectionService>();
        services.AddScoped<ImageLibrary>();
        services.AddScoped<IEditorInterop, EditorInterop>();
        services.AddScoped<IGpuInterop, GpuInterop>();
        services.AddScoped<ICanvasInterop, CanvasInterop>();
        services.AddScoped<IBrowserInterop, BrowserInterop>();
        return services;
    }
}
