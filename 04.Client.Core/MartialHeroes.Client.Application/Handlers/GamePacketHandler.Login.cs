using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 0/0 — login key exchange -> 1/4 Auth reply
    // -------------------------------------------------------------------------

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