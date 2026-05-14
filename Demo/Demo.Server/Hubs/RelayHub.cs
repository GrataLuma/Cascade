using System.Threading.Tasks;
using Demo.Server.Services;
using Demo.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Demo.Server.Hubs
{
    // Dumb relay + lobby + pair handshake. Server NEVIDÍ K* ani protocol
    // state — jen drží registry connection→handle, pair state machine,
    // a routuje payload zprávu z A na B (a obráceně) podle PartnerConnId.
    //
    // Pairing flow (1b):
    //   1. OnConnectedAsync → klient automaticky registruje handle
    //   2. JoinLobby() → klient dostane vlastní handle + lobby snapshot
    //   3. RequestPair(target) → target dostane PairRequestReceived
    //   4. target.AcceptPair() → oba dostanou PairAccepted (initiator=A, acceptor=B)
    //      target.RejectPair() → initiator dostane PairRejected
    //   5. SendToB / SendToA — relay payload na peer (route přes registry)
    //   6. OnDisconnectedAsync → cleanup, peer (pokud existuje) dostane
    //      PartnerLeft / PairRequestCanceled / PairRejected dle stavu
    public sealed class RelayHub : Hub
    {
        private readonly ConnectionRegistry _registry;
        private readonly HandleGenerator _handleGen;
        private readonly ILogger<RelayHub> _logger;

        public RelayHub(ConnectionRegistry registry, HandleGenerator handleGen, ILogger<RelayHub> logger)
        {
            _registry = registry;
            _handleGen = handleGen;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var entry = _registry.AddClient(Context.ConnectionId, _handleGen);
            _logger.LogInformation("Client connected: {Handle} ({ConnId})", entry.Handle, Context.ConnectionId);
            await BroadcastLobbyUpdate();
            await base.OnConnectedAsync();
        }

        public Task<LobbyJoined> JoinLobby()
        {
            if (!_registry.TryGetByConnId(Context.ConnectionId, out var self))
                throw new HubException("Connection not registered");

            var available = _registry.AvailableHandles(Context.ConnectionId);
            return Task.FromResult(new LobbyJoined(self.Handle, available));
        }

        public async Task<string> RequestPair(string targetHandle)
        {
            var result = _registry.RequestPair(Context.ConnectionId, targetHandle, out var self, out var target);
            switch (result)
            {
                case PairRequestResult.Sent:
                    _logger.LogInformation("Pair request: {From} → {To}", self.Handle, target.Handle);
                    await Clients.Client(target.ConnectionId)
                        .SendAsync("PairRequestReceived", new PairRequestReceived(self.Handle));
                    await BroadcastLobbyUpdate();
                    return "Sent";
                case PairRequestResult.SelfPair:
                    return "SelfPair";
                case PairRequestResult.TargetNotFound:
                    return "TargetNotFound";
                case PairRequestResult.TargetBusy:
                    return "TargetBusy";
                case PairRequestResult.SelfBusy:
                    return "SelfBusy";
                default:
                    return "Unknown";
            }
        }

        public async Task AcceptPair()
        {
            var result = _registry.AcceptPair(Context.ConnectionId, out var accepter, out var initiator);
            if (result == AcceptPairResult.NotIncoming)
            {
                throw new HubException("No incoming pair request");
            }
            if (result == AcceptPairResult.InitiatorGone)
            {
                // Cleanup accepter's hanging state.
                _registry.RejectPair(Context.ConnectionId, out _, out _);
                throw new HubException("Initiator already left");
            }

            _logger.LogInformation("Pair accepted: {Initiator} (A) ↔ {Acceptor} (B), session {Session}",
                initiator.Handle, accepter.Handle, accepter.PairSessionId);

            // Initiator = role A (drives round 1), acceptor = role B.
            await Clients.Client(initiator.ConnectionId)
                .SendAsync("PairAccepted", new PairAccepted(accepter.Handle, "A"));
            await Clients.Client(accepter.ConnectionId)
                .SendAsync("PairAccepted", new PairAccepted(initiator.Handle, "B"));

            await BroadcastLobbyUpdate();
        }

        public async Task RejectPair()
        {
            if (!_registry.RejectPair(Context.ConnectionId, out var rejecter, out var initiator))
            {
                throw new HubException("No incoming pair request");
            }
            _logger.LogInformation("Pair rejected by {Rejecter}", rejecter.Handle);

            if (initiator != null)
            {
                await Clients.Client(initiator.ConnectionId)
                    .SendAsync("PairRejected", new PairRejected(rejecter.Handle, "Rejected"));
            }
            await BroadcastLobbyUpdate();
        }

        public async Task CancelPairRequest()
        {
            if (!_registry.CancelPairRequest(Context.ConnectionId, out var self, out var target))
            {
                throw new HubException("No outgoing pair request");
            }
            _logger.LogInformation("Pair request canceled by {Self}", self.Handle);

            if (target != null)
            {
                await Clients.Client(target.ConnectionId)
                    .SendAsync("PairRequestCanceled", new PairRequestCanceled());
            }
            await BroadcastLobbyUpdate();
        }

        public async Task SendToB(SessionPayloadAtoB payload)
        {
            if (!_registry.TryGetByConnId(Context.ConnectionId, out var self) ||
                self.Status != ClientStatus.Paired ||
                self.PartnerConnId == null)
            {
                throw new HubException("Not in active pair");
            }
            _registry.Touch(Context.ConnectionId);
            _registry.Touch(self.PartnerConnId);
            // Server přepíše SessionId na pair-id (klient ho nemusí znát).
            var stamped = payload with { SessionId = self.PairSessionId };
            await Clients.Client(self.PartnerConnId).SendAsync("MessageFromA", stamped);
        }

        public async Task SendToA(SessionPayloadBtoA payload)
        {
            if (!_registry.TryGetByConnId(Context.ConnectionId, out var self) ||
                self.Status != ClientStatus.Paired ||
                self.PartnerConnId == null)
            {
                throw new HubException("Not in active pair");
            }
            _registry.Touch(Context.ConnectionId);
            _registry.Touch(self.PartnerConnId);
            var stamped = payload with { SessionId = self.PairSessionId };
            await Clients.Client(self.PartnerConnId).SendAsync("MessageFromB", stamped);
        }

        public override async Task OnDisconnectedAsync(System.Exception exception)
        {
            // Snapshot stav před RemoveClient (RemoveClient resetuje partner).
            ClientStatus? selfStatusBeforeRemove = null;
            if (_registry.TryGetByConnId(Context.ConnectionId, out var snap))
            {
                selfStatusBeforeRemove = snap.Status;
            }

            if (_registry.RemoveClient(Context.ConnectionId, out var removed, out var partner))
            {
                _logger.LogInformation("Client disconnected: {Handle} ({Status}, {ConnId})",
                    removed.Handle, selfStatusBeforeRemove, Context.ConnectionId);

                if (partner != null && selfStatusBeforeRemove.HasValue)
                {
                    switch (selfStatusBeforeRemove.Value)
                    {
                        case ClientStatus.Paired:
                            await Clients.Client(partner.ConnectionId)
                                .SendAsync("PartnerLeft", new PartnerLeft(removed.Handle));
                            break;
                        case ClientStatus.OutgoingPairRequest:
                            // Initiator left before target answered.
                            await Clients.Client(partner.ConnectionId)
                                .SendAsync("PairRequestCanceled", new PairRequestCanceled());
                            break;
                        case ClientStatus.IncomingPairRequest:
                            // Target left before answering.
                            await Clients.Client(partner.ConnectionId)
                                .SendAsync("PairRejected", new PairRejected(removed.Handle, "TargetLeft"));
                            break;
                    }
                }

                await BroadcastLobbyUpdate();
            }
            await base.OnDisconnectedAsync(exception);
        }

        // Pošle LobbyUpdate všem klientům aktuálně v InLobby. Per-recipient
        // available list je individuální (vyloučí self), tak iterujeme.
        private async Task BroadcastLobbyUpdate()
        {
            foreach (var connId in _registry.ConnectionIdsInLobby())
            {
                var available = _registry.AvailableHandles(connId);
                await Clients.Client(connId)
                    .SendAsync("LobbyUpdate", new LobbyUpdate(available));
            }
        }
    }
}
