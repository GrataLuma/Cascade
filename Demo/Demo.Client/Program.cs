using System.Net.Http;
using Demo.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Demo.Client.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<ConfigurationProbe>();
builder.Services.AddScoped<LanguageService>();

// Per-tab driver. Hub URL relative na app base — server hostí WASM i Hub
// na stejném originu (Demo.Server.Program.cs MapHub("/hub/relay")).
// LanguageService injektován pro render-time lokalizaci LastError textů.
builder.Services.AddScoped(sp =>
{
    var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
    var hubUri = new Uri(baseUri, "hub/relay");
    var lang = sp.GetRequiredService<LanguageService>();
    return new ProtocolDriver(hubUri.ToString(), lang);
});

await builder.Build().RunAsync();
