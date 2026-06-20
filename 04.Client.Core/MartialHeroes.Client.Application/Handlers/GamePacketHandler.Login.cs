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