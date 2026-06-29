using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Social;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public RelationStore Relations { get; } = new();

    public PartyRosterStore Party { get; } = new();

    public GuildRosterStore Guild { get; } = new();

    public void Handle(in SmsgLocalPlayerRelationSlot packet)
    {
        var localKey = _world.LocalActorKey;
        if (localKey is not { } key || key.RawId != unchecked((uint)packet.ActorId)) return;

        Relations.WriteSlot(packet.SlotIndex, packet.Field0, packet.Field1, packet.Field2, packet.Field3);

        _eventBus.Publish(new RelationUpdatedEvent(
            key, packet.SlotIndex, 0, packet.Field0, packet.Field1, packet.Field2, packet.Field3, true));
    }

    public void Handle(in SmsgRemoteActorRelationPair packet)
    {
        var idA = unchecked((uint)packet.ActorIdA);
        var idB = unchecked((uint)packet.ActorIdB);

        if (_world.LocalActorKey is { } local && (local.RawId == idA || local.RawId == idB)) return;

        if (Relations.IsBonded(idA) && Relations.IsBonded(idB)) return;

        var reciprocal = SmsgRemoteActorRelationPair.ReciprocalRelationCode;
        Relations.SetPairState(idA, packet.RelationCode);
        Relations.SetPairState(idB, reciprocal);

        _eventBus.Publish(new RelationUpdatedEvent(
            new ActorKey(idA, EntitySort.PlayerCharacter), 0, packet.RelationCode, unchecked((int)idB), 0, 0, 0,
            false));
        _eventBus.Publish(new RelationUpdatedEvent(
            new ActorKey(idB, EntitySort.PlayerCharacter), 0, reciprocal, unchecked((int)idA), 0, 0, 0, false));
    }
}