using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{


    public void Handle(in SmsgKeyExchange packet)
    {
        var payload = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SmsgKeyExchange, byte>(ref Unsafe.AsRef(in packet)),
            Unsafe.SizeOf<SmsgKeyExchange>());
        HandleKeyExchange(payload);
    }

    private void HandleKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (loginDriver is null)
        {
            _unhandled.Record(Opcodes.SmsgKeyExchange, payload.Length);
            return;
        }

        var replyBytes = loginDriver.OnKeyExchange(payload);

        inFlightLatch?.Arm();

        _eventBus.Publish(new LoginHandshakeCompletedEvent(replyBytes));
    }

    public void OnDisconnected()
    {
        worldEntry?.Clear();
        sceneStateMachine?.OnDisconnected();
    }
}