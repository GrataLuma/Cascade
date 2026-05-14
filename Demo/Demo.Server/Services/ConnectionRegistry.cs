using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Demo.Server.Services
{
    public enum ClientStatus
    {
        InLobby,
        OutgoingPairRequest,
        IncomingPairRequest,
        Paired
    }

    public enum PairRequestResult
    {
        Sent,
        SelfPair,
        TargetNotFound,
        TargetBusy,
        SelfBusy
    }

    public enum AcceptPairResult
    {
        NotIncoming,
        InitiatorGone,
        Accepted
    }

    // Per-client record. Hub vlastní mutaci přes ConnectionRegistry — žádné
    // přímé property assignment z venku.
    public sealed class ClientEntry
    {
        public string ConnectionId { get; init; }
        public string Handle { get; init; }
        public ClientStatus Status { get; internal set; } = ClientStatus.InLobby;
        public string PartnerConnId { get; internal set; }
        public DateTime LastActivity { get; internal set; } = DateTime.UtcNow;

        // Při pair handshake je sessionId derivovaný (stable per-pair). Server
        // ho generuje v AcceptPair a oba členové páru ho dostanou skrze
        // PairAccepted DTO. Klient v 1b sessionId nepoužívá pro routing
        // (server route per ConnectionId).
        public string PairSessionId { get; internal set; }
    }

    // In-memory registry pro lobby + pair state machine. Klíčový posun
    // proti 1a SessionRegistry: zde má každý připojený klient record (ne jen
    // ti v páru), a stavový automat pokrývá pair handshake.
    //
    // Thread-safety: ConcurrentDictionary pro hot read; všechny multi-step
    // mutace (RequestPair, AcceptPair, ...) seriality ovládá `_pairLock` —
    // pair lifecycle se mění zřídka, jednoduchý global lock je dostačující.
    public sealed class ConnectionRegistry
    {
        private readonly ConcurrentDictionary<string, ClientEntry> _byConnId = new();
        private readonly ConcurrentDictionary<string, string> _handleToConnId =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _pairLock = new();

        public ClientEntry AddClient(string connectionId, HandleGenerator gen)
        {
            // Snapshot taken handles pod lockem (handle table je live).
            // Při simultánní AddClient může mezi snapshot a TryAdd vzniknout
            // collision; v takovém případě retry.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var taken = new HashSet<string>(_handleToConnId.Keys, StringComparer.OrdinalIgnoreCase);
                var handle = gen.Generate(taken);
                var entry = new ClientEntry
                {
                    ConnectionId = connectionId,
                    Handle = handle
                };
                if (_byConnId.TryAdd(connectionId, entry) &&
                    _handleToConnId.TryAdd(handle, connectionId))
                {
                    return entry;
                }
                // Cleanup partial insert on retry.
                _byConnId.TryRemove(connectionId, out _);
            }
            throw new InvalidOperationException("Failed to assign unique handle after retries");
        }

        public bool TryGetByConnId(string connectionId, out ClientEntry entry) =>
            _byConnId.TryGetValue(connectionId, out entry);

        public bool TryGetByHandle(string handle, out ClientEntry entry)
        {
            if (_handleToConnId.TryGetValue(handle, out var connId))
            {
                return _byConnId.TryGetValue(connId, out entry);
            }
            entry = null;
            return false;
        }

        // Snapshot handles ve stavu InLobby, vyloučí self.
        public IReadOnlyList<string> AvailableHandles(string excludingConnId)
        {
            return _byConnId.Values
                .Where(e => e.Status == ClientStatus.InLobby && e.ConnectionId != excludingConnId)
                .Select(e => e.Handle)
                .OrderBy(h => h, StringComparer.Ordinal)
                .ToList();
        }

        public PairRequestResult RequestPair(string fromConnId, string targetHandle, out ClientEntry self, out ClientEntry target)
        {
            self = null;
            target = null;
            lock (_pairLock)
            {
                if (!_byConnId.TryGetValue(fromConnId, out self))
                    return PairRequestResult.TargetNotFound;
                if (!_handleToConnId.TryGetValue(targetHandle, out var targetConnId))
                    return PairRequestResult.TargetNotFound;
                if (!_byConnId.TryGetValue(targetConnId, out target))
                    return PairRequestResult.TargetNotFound;
                if (string.Equals(fromConnId, targetConnId, StringComparison.Ordinal))
                    return PairRequestResult.SelfPair;
                if (self.Status != ClientStatus.InLobby)
                    return PairRequestResult.SelfBusy;
                if (target.Status != ClientStatus.InLobby)
                    return PairRequestResult.TargetBusy;

                self.Status = ClientStatus.OutgoingPairRequest;
                self.PartnerConnId = targetConnId;
                self.LastActivity = DateTime.UtcNow;

                target.Status = ClientStatus.IncomingPairRequest;
                target.PartnerConnId = fromConnId;
                target.LastActivity = DateTime.UtcNow;

                return PairRequestResult.Sent;
            }
        }

        public AcceptPairResult AcceptPair(string accepterConnId, out ClientEntry accepter, out ClientEntry initiator)
        {
            accepter = null;
            initiator = null;
            lock (_pairLock)
            {
                if (!_byConnId.TryGetValue(accepterConnId, out accepter))
                    return AcceptPairResult.NotIncoming;
                if (accepter.Status != ClientStatus.IncomingPairRequest)
                    return AcceptPairResult.NotIncoming;
                if (accepter.PartnerConnId == null ||
                    !_byConnId.TryGetValue(accepter.PartnerConnId, out initiator))
                    return AcceptPairResult.InitiatorGone;
                if (initiator.Status != ClientStatus.OutgoingPairRequest)
                    return AcceptPairResult.InitiatorGone;

                var sessionId = Guid.NewGuid().ToString("N");
                accepter.Status = ClientStatus.Paired;
                accepter.PairSessionId = sessionId;
                accepter.LastActivity = DateTime.UtcNow;

                initiator.Status = ClientStatus.Paired;
                initiator.PairSessionId = sessionId;
                initiator.LastActivity = DateTime.UtcNow;

                return AcceptPairResult.Accepted;
            }
        }

        // Reject (target → initiator). Vrátí oba do InLobby a zveřejní je
        // znovu pro lobby update.
        public bool RejectPair(string rejecterConnId, out ClientEntry rejecter, out ClientEntry initiator)
        {
            rejecter = null;
            initiator = null;
            lock (_pairLock)
            {
                if (!_byConnId.TryGetValue(rejecterConnId, out rejecter)) return false;
                if (rejecter.Status != ClientStatus.IncomingPairRequest) return false;
                if (rejecter.PartnerConnId != null)
                    _byConnId.TryGetValue(rejecter.PartnerConnId, out initiator);

                ResetToLobby(rejecter);
                if (initiator != null) ResetToLobby(initiator);
                return true;
            }
        }

        // Cancel (initiator → target). Vrátí oba do InLobby.
        public bool CancelPairRequest(string fromConnId, out ClientEntry self, out ClientEntry target)
        {
            self = null;
            target = null;
            lock (_pairLock)
            {
                if (!_byConnId.TryGetValue(fromConnId, out self)) return false;
                if (self.Status != ClientStatus.OutgoingPairRequest) return false;
                if (self.PartnerConnId != null)
                    _byConnId.TryGetValue(self.PartnerConnId, out target);

                ResetToLobby(self);
                if (target != null) ResetToLobby(target);
                return true;
            }
        }

        // Disconnect cleanup: vrátí ClientEntry pokud existoval; partner
        // (pokud existoval pair request nebo Paired) je vrácený přes out.
        //
        // Reset partnera do InLobby JE-LI to bezpečné:
        //   - OutgoingPairRequest / IncomingPairRequest → reset (handshake
        //     nedokončený, partner může ihned pairovat znovu)
        //   - Paired → NE-reset (zombie Paired). Důvod: klient v Paired byl
        //     v aktivním protokolu, jeho driver dostane PartnerLeft event
        //     a přejde do Failed UI stavu. Server-side reset do InLobby by
        //     vytvořil mismatch (server: InLobby, klient: Failed) a partner's
        //     handle by se objevil jako "volný peer" v lobby ostatních
        //     klientů. Ti by na něj poslali pair request, jenž klient v
        //     Failed ignoruje → phantom OutgoingRequest věčně waiting.
        //     Zombie Paired se vyčistí, až partner sám provede Reset /
        //     Disconnect / zavře tab → jeho vlastní OnDisconnectedAsync.
        public bool RemoveClient(string connectionId, out ClientEntry removed, out ClientEntry partner)
        {
            removed = null;
            partner = null;
            lock (_pairLock)
            {
                if (!_byConnId.TryRemove(connectionId, out removed)) return false;
                _handleToConnId.TryRemove(removed.Handle, out _);

                if (removed.PartnerConnId != null &&
                    _byConnId.TryGetValue(removed.PartnerConnId, out partner))
                {
                    if (partner.Status is ClientStatus.OutgoingPairRequest
                                       or ClientStatus.IncomingPairRequest)
                    {
                        ResetToLobby(partner);
                    }
                    // Paired partner zůstává Paired (zombie) až do vlastního cleanupu.
                }
                return true;
            }
        }

        public void Touch(string connectionId)
        {
            if (_byConnId.TryGetValue(connectionId, out var e))
            {
                e.LastActivity = DateTime.UtcNow;
            }
        }

        // Snapshot pro idle reaper.
        public IReadOnlyList<ClientEntry> SnapshotIdle(TimeSpan lobbyIdle, TimeSpan pairedIdle)
        {
            var now = DateTime.UtcNow;
            var stale = new List<ClientEntry>();
            foreach (var e in _byConnId.Values)
            {
                var threshold = e.Status == ClientStatus.Paired ? pairedIdle : lobbyIdle;
                if (now - e.LastActivity > threshold) stale.Add(e);
            }
            return stale;
        }

        public IReadOnlyList<string> ConnectionIdsInLobby() =>
            _byConnId.Values
                .Where(e => e.Status == ClientStatus.InLobby)
                .Select(e => e.ConnectionId)
                .ToList();

        // Caller must hold _pairLock.
        private static void ResetToLobby(ClientEntry e)
        {
            e.Status = ClientStatus.InLobby;
            e.PartnerConnId = null;
            e.PairSessionId = null;
            e.LastActivity = DateTime.UtcNow;
        }
    }
}
