using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HLSLInterpreter.Debugger.Web;
using HLSLInterpreter.Debugger.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(new PermalinkOptions { Url = null });
builder.Services.AddSingleton(new InitialCodeOptions { Code = null });
builder.Services.AddSingleton(new TabbedEditorOptions { Enabled = false });
builder.Services.AddSingleton<FileDialogService>();
builder.Services.AddScoped<DebuggerAppState>();
builder.Services.AddScoped<RunController>();
builder.Services.AddScoped<DebugController>();
builder.Services.AddScoped<ImageLibrary>();

await builder.Build().RunAsync();
