using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HLSLInterpreter.Debugger.Web;
using HLSLInterpreter.Debugger;
using HLSLInterpreter.Debugger.Core;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(new PermalinkBaseUrl { Url = null });
builder.Services.AddSingleton(new InitialCodeOverride { Code = null });
builder.Services.AddSingleton(new TabbedEditor { Enabled = false });
builder.Services.AddSingleton<FileDialogService>();
builder.Services.AddScoped<DebuggerAppState>();
builder.Services.AddScoped<RunController>();
builder.Services.AddScoped<DebugController>();

await builder.Build().RunAsync();
