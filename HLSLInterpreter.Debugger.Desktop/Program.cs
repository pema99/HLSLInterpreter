using System.IO;
using Microsoft.AspNetCore.Components.Web;
using BlazorDesktop.Hosting;
using HLSLInterpreter.Debugger.Desktop.Components;
using HLSLInterpreter.Debugger.Desktop;
using HLSLInterpreter.Debugger.Mvu;
using HLSLInterpreter.Debugger.Services;

var builder = BlazorDesktopHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

string? initialCode = null;
string? initialName = null;
if (args.Length > 0 && File.Exists(args[0]))
{
    initialCode = File.ReadAllText(args[0]);
    initialName = Path.GetFileName(args[0]);
}
builder.Services.AddSingleton(new DebuggerHostOptions
{
    InitialCode = initialCode,
    InitialName = initialName,
    InitialPath = args.Length > 0 ? args[0] : null,
    PermalinkUrl = "https://pema.dev/hlsl/",
    TabsEnabled = true,
});
builder.Services.AddSingleton<FileDialogService, WpfFileDialogService>();
builder.Services.AddScoped<ImageLibrary>();
builder.Services.AddScoped(sp => new DebuggerProgram(
    DebuggerModel.Initial,
    new DebuggerEffects(sp.GetRequiredService<FileDialogService>())));

builder.Window.UseTitle("HLSL Interpreter");
builder.Window.UseWidth(1600);
builder.Window.UseHeight(900);

if (builder.HostEnvironment.IsDevelopment())
{
    builder.UseDeveloperTools();
}

await builder.Build().RunAsync();
