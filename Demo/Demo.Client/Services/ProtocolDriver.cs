using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Demo.Shared;
using GrataCascade.Core;
using Microsoft.AspNetCore.SignalR.Client;

namespace Demo.Client.Services
{
    public enum DriverPhase
    {
        Disconnected,
        Connecting,
        InLobby,            // 1b: připojen, vidí ostatní, idle
        OutgoingRequest,    // čekám na odpověď peera, kterého jsem oslovil
        IncomingRequest,    // peer mě chce zpárovat, čekám na můj accept/reject
        Running,
        Converged,
        Failed
    }

    // Per-tab driver. Drží lobby + pair handshake state machine + protokol
    // pokud je v páru. UI binduje na public properties + OnStateChanged event.
    public sealed class ProtocolDriver : IAsyncDisposable
    {
        public const int MaxIterations = 500;

        private readonly HubConnection _hub;
        private readonly LanguageService _lang;
        private ClientA _clientA;
        private ClientB _clientB;

        // Lobby state
        public string OwnHandle { get; private set; }
        public IReadOnlyList<string> LobbyClients { get; private set; } = Array.Empty<string>();
        public string PendingIncomingFrom { get; private set; }
        public string PendingOutgoingTo { get; private set; }
        public string PeerHandle { get; private set; }

        // Connection / phase
        public bool IsConnected { get; private set; }
        public DriverPhase Phase { get; private set; } = DriverPhase.Disconnected;

        // Last error: ukládá se jako key + args; lokalizace happens render-time
        // přes LanguageService, takže CS/EN přepínání po erroru funguje.
        private string _lastErrorKey;
        private object[] _lastErrorArgs = Array.Empty<object>();
        public string LastError =>
            _lastErrorKey == null ? null : LocalizeError(_lastErrorKey, _lastErrorArgs);

        // Protocol state
        public string Role { get; private set; }       // "A" | "B" | null
        public int Round { get; private set; }
        public int PeerLastSeenRound { get; private set; }
        public string KStarHex { get; private set; }
        public string KStarSafetyNumber { get; private set; }

        // Live diagnostiky (Core public fields, copy at hot path).
        public int FillRejections { get; private set; }
        public int FillSafetyTriggers { get; private set; }
        public int? RoundsSkippedByF4Filter { get; private set; }   // B-only

        public event Action OnStateChanged;

        public ProtocolDriver(string hubUrl, LanguageService lang)
        {
            _lang = lang;

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hub.On<LobbyUpdate>("LobbyUpdate", OnLobbyUpdate);
            _hub.On<PairRequestReceived>("PairRequestReceived", OnPairRequestReceived);
            _hub.On<PairRequestCanceled>("PairRequestCanceled", OnPairRequestCanceled);
            _hub.On<PairAccepted>("PairAccepted", OnPairAccepted);
            _hub.On<PairRejected>("PairRejected", OnPairRejected);
            _hub.On<PartnerLeft>("PartnerLeft", OnPartnerLeft);
            _hub.On<SessionPayloadAtoB>("MessageFromA", OnMessageFromA);
            _hub.On<SessionPayloadBtoA>("MessageFromB", OnMessageFromB);

            _hub.Closed += async ex =>
            {
                IsConnected = false;
                if (ex != null)
                {
                    SetError("ConnectionClosed", ex.Message);
                    SetPhase(DriverPhase.Failed);
                }
                else if (Phase != DriverPhase.Disconnected)
                {
                    SetPhase(DriverPhase.Disconnected);
                }
                Notify();
                await Task.CompletedTask;
            };
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            SetPhase(DriverPhase.Connecting);
            try
            {
                await _hub.StartAsync(ct);
                IsConnected = true;
                var joined = await _hub.InvokeAsync<LobbyJoined>("JoinLobby", ct);
                OwnHandle = joined.OwnHandle;
                LobbyClients = joined.Available;
                SetPhase(DriverPhase.InLobby);
                Notify();
            }
            catch (Exception ex)
            {
                SetError("ConnectFailed", ex.Message);
                SetPhase(DriverPhase.Failed);
                Notify();
            }
        }

        public async Task RequestPairAsync(string targetHandle)
        {
            if (Phase != DriverPhase.InLobby) return;
            try
            {
                var result = await _hub.InvokeAsync<string>("RequestPair", targetHandle);
                switch (result)
                {
                    case "Sent":
                        PendingOutgoingTo = targetHandle;
                        SetPhase(DriverPhase.OutgoingRequest);
                        break;
                    case "TargetBusy":
                        SetError("TargetBusy", targetHandle);
                        break;
                    case "TargetNotFound":
                        SetError("TargetOffline", targetHandle);
                        break;
                    case "SelfPair":
                        SetError("SelfPair");
                        break;
                    case "SelfBusy":
                        SetError("AlreadyHasOutgoing");
                        break;
                    default:
                        SetError("PairRequestFailed", result);
                        break;
                }
                Notify();
            }
            catch (Exception ex)
            {
                SetError("PairRequestFailed", ex.Message);
                Notify();
            }
        }

        public async Task AcceptPairAsync()
        {
            if (Phase != DriverPhase.IncomingRequest) return;
            try
            {
                await _hub.SendAsync("AcceptPair");
                // Phase change přijde server-side eventem PairAccepted.
            }
            catch (Exception ex)
            {
                SetError("AcceptPairFailed", ex.Message);
                Notify();
            }
        }

        public async Task RejectPairAsync()
        {
            if (Phase != DriverPhase.IncomingRequest) return;
            try
            {
                await _hub.SendAsync("RejectPair");
                PendingIncomingFrom = null;
                SetPhase(DriverPhase.InLobby);
                Notify();
            }
            catch (Exception ex)
            {
                SetError("RejectPairFailed", ex.Message);
                Notify();
            }
        }

        public async Task CancelPairRequestAsync()
        {
            if (Phase != DriverPhase.OutgoingRequest) return;
            try
            {
                await _hub.SendAsync("CancelPairRequest");
                PendingOutgoingTo = null;
                SetPhase(DriverPhase.InLobby);
                Notify();
            }
            catch (Exception ex)
            {
                SetError("CancelPairFailed", ex.Message);
                Notify();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_hub.State == HubConnectionState.Disconnected) return;
            try
            {
                await _hub.StopAsync();
            }
            catch
            {
                // graceful — _hub.Closed callback dorovná state
            }
            IsConnected = false;
            SetPhase(DriverPhase.Disconnected);
            Notify();
        }

        private Task OnLobbyUpdate(LobbyUpdate update)
        {
            LobbyClients = update.Available;
            Notify();
            return Task.CompletedTask;
        }

        private Task OnPairRequestReceived(PairRequestReceived req)
        {
            // Pokud jsem zrovna v outgoing request (race), server-side first-wins
            // už odmítl mou žádost přes PairRejected. Sem dorazí jen pokud jsem
            // v InLobby.
            if (Phase != DriverPhase.InLobby) return Task.CompletedTask;
            PendingIncomingFrom = req.FromHandle;
            SetPhase(DriverPhase.IncomingRequest);
            Notify();
            return Task.CompletedTask;
        }

        private Task OnPairRequestCanceled(PairRequestCanceled _)
        {
            // Initiator zrušil request — uklidit incoming state. DTO je
            // empty record, vždy adresuje aktuálního PendingIncomingFrom.
            PendingIncomingFrom = null;
            if (Phase == DriverPhase.IncomingRequest)
            {
                SetPhase(DriverPhase.InLobby);
            }
            Notify();
            return Task.CompletedTask;
        }

        private async Task OnPairAccepted(PairAccepted accepted)
        {
            PeerHandle = accepted.PeerHandle;
            Role = accepted.Role;
            PendingOutgoingTo = null;
            PendingIncomingFrom = null;
            ResetProtocolState();

            if (Role == "A")
            {
                _clientA = new ClientA();
                await StartRoundAAsync();
            }
            else
            {
                _clientB = new ClientB();
                SetPhase(DriverPhase.Running);
                Notify();
            }
        }

        private Task OnPairRejected(PairRejected rejected)
        {
            PendingOutgoingTo = null;
            switch (rejected.Reason)
            {
                case "Rejected":
                    SetError("RejectedByPeer", rejected.TargetHandle);
                    break;
                case "TargetLeft":
                    SetError("TargetLeftBeforeAccept", rejected.TargetHandle);
                    break;
                default:
                    SetError("RejectedUnknown", rejected.Reason);
                    break;
            }
            if (Phase == DriverPhase.OutgoingRequest)
            {
                SetPhase(DriverPhase.InLobby);
            }
            Notify();
            return Task.CompletedTask;
        }

        private Task OnPartnerLeft(PartnerLeft left)
        {
            SetError("PartnerLeftMidProtocol", left.PeerHandle);
            SetPhase(DriverPhase.Failed);
            Notify();
            return Task.CompletedTask;
        }

        // ── Protocol logic (per-pair, post-PairAccepted) ───────────────────

        private async Task StartRoundAAsync()
        {
            Round = 1;
            SetPhase(DriverPhase.Running);
            try
            {
                var coreMsg = _clientA.CreateMessageToB();
                CopyDiagnosticsFromA();
                await _hub.SendAsync("SendToB", new SessionPayloadAtoB(string.Empty, coreMsg.ToWire()));
            }
            catch (Exception ex)
            {
                SetError("RoundOneSendFailed", ex.Message);
                SetPhase(DriverPhase.Failed);
            }
            Notify();
        }

        private async Task OnMessageFromA(SessionPayloadAtoB payload)
        {
            if (Role != "B") return;
            try
            {
                Round++;
                PeerLastSeenRound = Round;  // peer reached this round to send.
                SetPhase(DriverPhase.Running);
                var coreMsgB = payload.Message.ToCore();
                var coreMsgA = _clientB.ProcessMessageFromA(coreMsgB, Round);
                CopyDiagnosticsFromB();
                await _hub.SendAsync("SendToA", new SessionPayloadBtoA(string.Empty, coreMsgA.ToWire()));

                if (coreMsgA.Tag != null && coreMsgA.Tag.Contains("Final"))
                {
                    var k = _clientB.GetPassword();
                    KStarHex = ToHex(k);
                    KStarSafetyNumber = ComputeSafetyNumber(k);
                    SetPhase(DriverPhase.Converged);
                }
                Notify();
            }
            catch (Exception ex)
            {
                SetError("BSideError", ex.Message);
                SetPhase(DriverPhase.Failed);
                Notify();
            }
        }

        private async Task OnMessageFromB(SessionPayloadBtoA payload)
        {
            if (Role != "A") return;
            try
            {
                var coreMsgA = payload.Message.ToCore();
                _clientA.ProcessMessageFromB(coreMsgA, Round);
                CopyDiagnosticsFromA();
                PeerLastSeenRound = Round;  // peer responded for current round.

                if (coreMsgA.Tag != null && coreMsgA.Tag.Contains("Final"))
                {
                    var k = _clientA.GetPassword();
                    KStarHex = ToHex(k);
                    KStarSafetyNumber = ComputeSafetyNumber(k);
                    SetPhase(DriverPhase.Converged);
                    Notify();
                    return;
                }

                Round++;
                if (Round > MaxIterations)
                {
                    SetError("MaxIterationsReached", MaxIterations);
                    SetPhase(DriverPhase.Failed);
                    Notify();
                    return;
                }

                var nextMsgB = _clientA.CreateMessageToB();
                CopyDiagnosticsFromA();
                await _hub.SendAsync("SendToB", new SessionPayloadAtoB(string.Empty, nextMsgB.ToWire()));
                Notify();
            }
            catch (Exception ex)
            {
                SetError("ASideError", ex.Message);
                SetPhase(DriverPhase.Failed);
                Notify();
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void CopyDiagnosticsFromA()
        {
            if (_clientA == null) return;
            FillRejections = _clientA.FillRejections;
            FillSafetyTriggers = _clientA.FillSafetyTriggers;
        }

        private void CopyDiagnosticsFromB()
        {
            if (_clientB == null) return;
            FillRejections = _clientB.FillRejections;
            FillSafetyTriggers = _clientB.FillSafetyTriggers;
            RoundsSkippedByF4Filter = _clientB.RoundsSkippedByF4Filter;
        }

        private void ResetProtocolState()
        {
            _clientA = null;
            _clientB = null;
            Round = 0;
            PeerLastSeenRound = 0;
            KStarHex = null;
            KStarSafetyNumber = null;
            FillRejections = 0;
            FillSafetyTriggers = 0;
            RoundsSkippedByF4Filter = null;
        }

        // Safety Number: SHA256(K*)[28..32] formát XXXX-XXXX (last 4 bytes
        // hex, group 4-4 pro eyeball compare). Briefing §3.
        private static string ComputeSafetyNumber(byte[] kStar)
        {
            if (kStar == null) return null;
            var digest = SHA256.HashData(kStar);
            var hex = Convert.ToHexString(digest, digest.Length - 4, 4).ToUpperInvariant();
            return hex.Substring(0, 4) + "-" + hex.Substring(4, 4);
        }

        private void SetPhase(DriverPhase phase)
        {
            Phase = phase;
        }

        private void Notify() => OnStateChanged?.Invoke();

        // ── Error storage + localization ────────────────────────────────────

        private void SetError(string key, params object[] args)
        {
            _lastErrorKey = key;
            _lastErrorArgs = args ?? Array.Empty<object>();
        }

        private string LocalizeError(string key, object[] args)
        {
            // Single switch řeší oba jazyky. args[i] interpolation kde potřeba.
            // Pokud Lang.IsCs == false, použít EN; jinak CS.
            bool cs = _lang?.IsCs ?? true;
            return key switch
            {
                "ConnectionClosed"       => cs ? $"Spojení uzavřeno: {args[0]}"
                                              : $"Connection closed: {args[0]}",
                "ConnectFailed"          => cs ? $"Nepodařilo se připojit k serveru: {args[0]}"
                                              : $"Failed to connect to server: {args[0]}",
                "TargetBusy"             => cs ? $"Peer '{args[0]}' je momentálně zaneprázdněný."
                                              : $"Peer '{args[0]}' is currently busy.",
                "TargetOffline"          => cs ? $"Peer '{args[0]}' už není online."
                                              : $"Peer '{args[0]}' is no longer online.",
                "SelfPair"               => cs ? "Sám sebe zpárovat nemůžeš."
                                              : "You cannot pair with yourself.",
                "AlreadyHasOutgoing"     => cs ? "Máš jiný pair request — zruš ho nejdřív."
                                              : "You already have a pending pair request — cancel it first.",
                "PairRequestFailed"      => cs ? $"Pair request selhal: {args[0]}"
                                              : $"Pair request failed: {args[0]}",
                "AcceptPairFailed"       => cs ? $"Přijetí páru selhalo: {args[0]}"
                                              : $"Accept pair failed: {args[0]}",
                "RejectPairFailed"       => cs ? $"Odmítnutí páru selhalo: {args[0]}"
                                              : $"Reject pair failed: {args[0]}",
                "CancelPairFailed"       => cs ? $"Zrušení páru selhalo: {args[0]}"
                                              : $"Cancel pair failed: {args[0]}",
                "RejectedByPeer"         => cs ? $"{args[0]} odmítl spojení."
                                              : $"{args[0]} rejected the request.",
                "TargetLeftBeforeAccept" => cs ? $"{args[0]} se odpojil dřív, než stihl odpovědět."
                                              : $"{args[0]} disconnected before responding.",
                "RejectedUnknown"        => cs ? $"Pair zamítnut: {args[0]}"
                                              : $"Pair rejected: {args[0]}",
                "PartnerLeftMidProtocol" => cs ? $"Peer ({args[0]}) se odpojil uprostřed protokolu."
                                              : $"Peer ({args[0]}) disconnected mid-protocol.",
                "RoundOneSendFailed"     => cs ? $"Odeslání 1. kola selhalo: {args[0]}"
                                              : $"Round 1 send failed: {args[0]}",
                "BSideError"             => cs ? $"Chyba strany B: {args[0]}"
                                              : $"B side: {args[0]}",
                "MaxIterationsReached"   => cs ? $"MaxIterations ({args[0]}) dosaženo bez Final"
                                              : $"MaxIterations ({args[0]}) reached without Final",
                "ASideError"             => cs ? $"Chyba strany A: {args[0]}"
                                              : $"A side: {args[0]}",
                _ => key   // fallback — should not happen
            };
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null) return null;
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _hub.DisposeAsync();
        }
    }
}
