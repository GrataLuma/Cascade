using System;
using System.Threading;
using System.Threading.Tasks;
using Demo.Server.Hubs;
using Demo.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Demo.Server.Services
{
    // Idle pair reaper. Každou minutu projde ConnectionRegistry a u párů
    // (Paired) bez aktivity > 5 min provede pair break — oba klienti
    // dostanou PartnerLeft a vrátí se do lobby. Klienti v InLobby /
    // *PairRequest stavech bez aktivity nereapuje (idle browser tab je OK).
    public sealed class SessionTimeoutService : BackgroundService
    {
        private static readonly TimeSpan PairedIdle = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan LobbyIdle = TimeSpan.FromHours(2);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        private readonly ConnectionRegistry _registry;
        private readonly IHubContext<RelayHub> _hub;
        private readonly ILogger<SessionTimeoutService> _logger;

        public SessionTimeoutService(ConnectionRegistry registry, IHubContext<RelayHub> hub, ILogger<SessionTimeoutService> logger)
        {
            _registry = registry;
            _hub = hub;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var stale = _registry.SnapshotIdle(LobbyIdle, PairedIdle);
                    foreach (var entry in stale)
                    {
                        if (entry.Status != ClientStatus.Paired) continue;

                        // Pair break: cancel pair via RejectPair-like reset
                        // (registry exposes nothing dedicated; reuse RemoveClient
                        // is too aggressive — it drops connection entry. Instead
                        // we send PartnerLeft and rely on client to leave/reset.)
                        _logger.LogInformation("Pair idle > {Threshold}: {A} ↔ {B}",
                            PairedIdle, entry.Handle, entry.PartnerConnId);

                        await _hub.Clients.Client(entry.ConnectionId)
                            .SendAsync("PartnerLeft", new PartnerLeft("(timeout)"), stoppingToken);
                        if (entry.PartnerConnId != null)
                        {
                            await _hub.Clients.Client(entry.PartnerConnId)
                                .SendAsync("PartnerLeft", new PartnerLeft("(timeout)"), stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pair idle sweep failed");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (TaskCanceledException) { /* shutdown */ }
            }
        }
    }
}
