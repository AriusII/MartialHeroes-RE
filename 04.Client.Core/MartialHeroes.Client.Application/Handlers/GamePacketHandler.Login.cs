using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // LOGIN wire path on the GAME connection (the (major:minor) opcode surface).
    //
    // SCOPE NOTE — the SERVER LIST is NOT handled here. The server-list query and the
    // channel-endpoint query ride a SEPARATE, synchronous lobby surface (port 10000 /
    // 10000+serverId) that does NOT use the 8-byte (major:minor) opcode header and carries
    // no byte cipher. Its parse lives entirely in Network.Transport.Pipelines/LobbyClient
    // (server-list -> LobbyServerRecordWire/LobbyServerRecord; channel-endpoint -> the game
    // host:port). GamePacketHandler only sees the GAME connection that BEGINS after the lobby
    // hands over the endpoint: the 0/0 key exchange below drives the reactive 1/4 credential.
    // Do NOT add a 3/x "server list" opcode here — there is none.
    // spec: Docs/RE/packets/lobby.yaml (LOGIN-SERVER vs GAME-SERVER SPLIT — lobby is a separate
    // wire surface, NOT a (major:minor) opcode); Docs/RE/specs/login_flow.md §2.
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // 0/0 — login key exchange -> 1/4 Auth reply
    // -------------------------------------------------------------------------

    /// <summary>
    ///     0/0 — typed typed IPacketHandler seam. Reinterprets the 62-byte struct as a raw span so the
    ///     existing payload-based handler can walk the RSA blob without copying. The struct is reinterpreted
    ///     in place over the frame buffer by the PacketRouter — no allocation, no copy.
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml; Docs/RE/specs/crypto.md §6.
    /// </summary>
    public void Handle(in SmsgKeyExchange packet) // spec: Docs/RE/opcodes.md row 0/0; IPacketHandler seam
    {
        // Reconstruct the raw byte span from the Pack=1 struct reinterpreted in the frame buffer.
        // Unsafe.SizeOf<SmsgKeyExchange>() == SmsgKeyExchange.WireSize == 62. spec: packets/0-0_key_exchange.yaml.
        var payload = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SmsgKeyExchange, byte>(ref Unsafe.AsRef(in packet)),
            Unsafe.SizeOf<SmsgKeyExchange>()); // spec: Docs/RE/packets/0-0_key_exchange.yaml (size: 62)
        HandleKeyExchange(payload);
    }

    /// <summary>
    ///     0/0 — server KeyExchange. Delegates to the injected login driver, which parses the payload,
    ///     builds the 1/4 reply from the staged credential, and sends it. spec: Docs/RE/specs/crypto.md §6.
    /// </summary>
    private void HandleKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (_loginDriver is null)
        {
            _unhandled.Record(Opcodes.SmsgKeyExchange, payload.Length);
            return;
        }

        var replyBytes = _loginDriver.OnKeyExchange(payload);

        // The 0/0 → 1/4 reactive secure-auth branch arms the single char-management in-flight latch on
        // send (the keepalive timer suppresses the 20s idle heartbeat while the login reply is outstanding).
        // It is cleared by the first char-mgmt result the server pushes next (3/1 success roster, etc.).
        // spec: Docs/RE/specs/net_contracts.md §1.3 (SET by … the (0,0)→1/4 inbound secure-auth branch).
        _inFlightLatch?.Arm();

        _eventBus.Publish(new LoginHandshakeCompletedEvent(replyBytes));
    }

    /// <summary>
    ///     Transport/session disconnect notification — drives the scene machine (load-time → 7/2, else 7/8).
    ///     spec: Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public void OnDisconnected()
    {
        // Reset the durable 4/1 world-entry record so a later InGame _Ready cannot cold-start a stale
        // area after a disconnect / back-to-select. spec: Docs/RE/specs/world_exit.md (world-leave reset).
        _worldEntry?.Clear();
        _sceneStateMachine?.OnDisconnected();
    }
}