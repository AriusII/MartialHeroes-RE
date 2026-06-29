using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgActorVisualSlotSet packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(unchecked((byte)packet.ActorSort)));

        _eventBus.Publish(new ActorEquipVisualChangedEvent(
            key, packet.SlotIndex, packet.ItemId, packet.ItemUpgrade, false));
    }

    public void Handle(in SmsgActorVisualSlotClear packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(unchecked((byte)packet.ActorSort)));

        _eventBus.Publish(new ActorEquipVisualChangedEvent(
            key, packet.SlotIndex, 0u, 0u, true));
    }

    public void Handle(in SmsgActorVisualFlagsSet packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(unchecked((byte)packet.ActorSort)));

        _eventBus.Publish(new ActorVisualFlagsChangedEvent(key, packet.VisualFlags));
    }

    public void Handle(in SmsgActorStateEvent packet)
    {
        var key = new ActorKey(packet.TargetId, ToEntitySort(unchecked((byte)packet.TargetSort)));

        _eventBus.Publish(new ActorStateChangedEvent(
            key, ActorStateKind.Generic, 0u, 0u, packet.ActorId));
    }

    public void Handle(in SmsgActorTimedStateUpdate packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(unchecked((byte)packet.Sort)));

        _eventBus.Publish(new ActorStateChangedEvent(
            key, ActorStateKind.Timed, packet.StateByte, packet.TimedValue, 0u));
    }

    public void Handle(in SmsgActorCombatFlagUpdate packet)
    {
        var key = ResolveActorKey(packet.ActorId);

        _eventBus.Publish(new ActorStateChangedEvent(
            key, ActorStateKind.Combat, unchecked((uint)packet.CombatFlag), 0u, 0u));
    }
}
