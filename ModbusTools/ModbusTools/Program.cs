using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ModbusTools;
using ModbusTools.Browser;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WebSerialService>();
builder.Services.AddScoped<BrowserFileDownloader>();

await builder.Build().RunAsync();
