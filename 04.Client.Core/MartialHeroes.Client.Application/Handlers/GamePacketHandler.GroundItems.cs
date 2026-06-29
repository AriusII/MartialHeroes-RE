using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgGroundItemSpawn packet)
    {
        var position = Vector3Fixed.FromFloat(packet.PosX, 0f, packet.PosZ);

        _eventBus.Publish(new GroundItemSpawnedEvent(
            packet.SourceId, unchecked((uint)packet.TemplateId), position));
    }

    public void Handle(in SmsgGroundItemRemove packet)
    {
        _eventBus.Publish(new GroundItemRemovedEvent(
            packet.TrackedId, packet.PickerId, packet.NotifyFlag != 0));
    }
}