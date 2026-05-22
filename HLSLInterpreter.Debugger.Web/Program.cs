using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HLSLInterpreter.Debugger.Mvu;
using HLSLInterpreter.Debugger.Services;
using HLSLInterpreter.Debugger.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(new HostOptionsService());
builder.Services.AddSingleton<FileDialogService>();
builder.Services.AddSingleton<ImageLibraryService>();
builder.Services.AddSingleton(sp => new DebuggerProgram(
    DebuggerModel.Initial,
    new DebuggerEffects(sp.GetRequiredService<FileDialogService>())));

await builder.Build().RunAsync();
