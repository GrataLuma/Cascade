using Demo.Server.Hubs;
using Demo.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    // Default 32 KB je málo: ClientA.CreateMessageToB exportuje 4096 hashů ×
    // 5 B = 20 KB raw, ale po Base64 + JSON envelope ~85 KB. Briefing §3 odhad
    // (~20 KB/round) byl na raw bytes, ne wire size. 4 MB cap je conservative
    // headroom pro ~50 kol × 85 KB ≈ 4 MB total session traffic.
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddSingleton<HandleGenerator>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddHostedService<SessionTimeoutService>();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapHub<RelayHub>("/hub/relay");
app.MapFallbackToFile("index.html");

app.Run();
