using System.Collections.Generic;
using System.Linq;
using GrataCascade.Core;

namespace Demo.Shared
{
    // Wire-format mirrors. Core typy (HASH, MessageToClientA, MessageToClientB)
    // mají interní reference (HASH.parentVector → Vector graf) a public fields
    // místo properties. Mirror přes records + explicitní konverze drží wire
    // serializaci predictable a chrání před netěsnostmi (parentVector NEpůjde
    // přes drát).

    public record WireHash(byte[] Data);

    public record WireMessageToB(List<WireHash> V);

    public record WireMessageToA(byte[] R, List<WireHash> Hashes, byte[] Message, string Tag);

    // SessionId je v 1b interní pair-id, vyplňuje server při relay (klient
    // posílá prázdný / libovolný string, server přepíše před broadcastem).
    public record SessionPayloadAtoB(string SessionId, WireMessageToB Message);

    public record SessionPayloadBtoA(string SessionId, WireMessageToA Message);

    // ── Lobby + pair handshake DTOs (nahrazují 1a SessionJoined / PeerStatus) ──

    // Vrací se z Hub.JoinLobby: vlastní handle + aktuální seznam ostatních
    // klientů ve stavu InLobby (bez vlastního).
    public record LobbyJoined(string OwnHandle, IReadOnlyList<string> Available);

    // Server broadcast všem v InLobby při každé změně lobby (Join, Leave,
    // PairRequest, PairAccept, PairBreak).
    public record LobbyUpdate(IReadOnlyList<string> Available);

    // Target dostane při Hub.RequestPair od initiator. Modal accept/reject.
    public record PairRequestReceived(string FromHandle);

    // Target dostane pokud initiator zruší (Cancel) nebo se odpojí
    // před tím, než target accept/reject.
    public record PairRequestCanceled();

    // Oba klienti dostanou po vzájemném pair accept. Initiator získá
    // Role="A", acceptor "B". A má CreateMessageToB() jako round 1 trigger.
    public record PairAccepted(string PeerHandle, string Role);

    // Initiator dostane pokud target rejectne (manuálně) nebo busy (race).
    public record PairRejected(string TargetHandle, string Reason);

    // Peer (paired) dostane pokud druhý se odpojí nebo Disconnect.
    public record PartnerLeft(string PeerHandle);

    public static class WireConversions
    {
        public static WireMessageToB ToWire(this MessageToClientB core) =>
            new(core.V.Select(h => new WireHash(h.Data)).ToList());

        public static MessageToClientB ToCore(this WireMessageToB wire)
        {
            var msg = new MessageToClientB
            {
                V = wire.V.Select(h => new HASH(h.Data, 0, h.Data.Length)).ToList()
            };
            return msg;
        }

        public static WireMessageToA ToWire(this MessageToClientA core) =>
            new(core.R, core.hashes.Select(h => new WireHash(h.Data)).ToList(), core.message, core.Tag);

        public static MessageToClientA ToCore(this WireMessageToA wire)
        {
            var msg = new MessageToClientA
            {
                R = wire.R,
                hashes = wire.Hashes.Select(h => new HASH(h.Data, 0, h.Data.Length)).ToList(),
                message = wire.Message,
                Tag = wire.Tag
            };
            return msg;
        }
    }
}
