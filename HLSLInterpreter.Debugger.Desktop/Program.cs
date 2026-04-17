using System.IO;
using Microsoft.AspNetCore.Components.Web;
using BlazorDesktop.Hosting;
using HLSLInterpreter.Debugger.Desktop.Components;
using HLSLInterpreter.Debugger.Desktop;
using HLSLInterpreter.Debugger;

var builder = BlazorDesktopHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(new PermalinkBaseUrl { Url = "https://pema.dev/hlsl/" });

string? initialCode = null;
string? initialName = null;
if (args.Length > 0 && File.Exists(args[0]))
{
    initialCode = File.ReadAllText(args[0]);
    initialName = Path.GetFileName(args[0]);
}
builder.Services.AddSingleton(new InitialCodeOverride { Code = initialCode, Name = initialName, Path = args.Length > 0 ? args[0] : null });
builder.Services.AddSingleton(new TabbedEditor { Enabled = true });
builder.Services.AddSingleton<FileDialogService, WpfFileDialogService>();

builder.Window.UseTitle("HLSL Interpreter");
builder.Window.UseWidth(1600);
builder.Window.UseHeight(900);

if (builder.HostEnvironment.IsDevelopment())
{
    builder.UseDeveloperTools();
}

await builder.Build().RunAsync();
