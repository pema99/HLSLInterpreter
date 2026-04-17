using Microsoft.AspNetCore.Components.Web;
using BlazorDesktop.Hosting;
using HLSLInterpreter.Debugger.Desktop.Components;

var builder = BlazorDesktopHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Window.UseTitle("HLSL Interpreter");
builder.Window.UseWidth(1600);
builder.Window.UseHeight(900);

if (builder.HostEnvironment.IsDevelopment())
{
    builder.UseDeveloperTools();
}

await builder.Build().RunAsync();
