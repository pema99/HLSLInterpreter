using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HLSLInterpreter.Debugger.Core;
using HLSLInterpreter.Debugger.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<HLSLInterpreter.Debugger.Components.Debugger>("#app");

builder.Services.AddSingleton(new HostOptionsService());
builder.Services.AddSingleton<FileDialogService>();
builder.Services.AddSingleton<ImageLibraryService>();
builder.Services.AddSingleton(sp => new DebuggerProgram(
    new DebuggerExecutionEngine(), sp.GetRequiredService<FileDialogService>()));

await builder.Build().RunAsync();
