using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{

    public void Handle(in SmsgCharDeath packet)
    {
        var victimKey = new ActorKey(packet.VictimId, ToEntitySort(packet.VictimSort));
        var killerKey = new ActorKey(packet.KillerId, ToEntitySort(packet.KillerSort));

        if (_world.TryGet(victimKey, out var victim))
        {
            victim.SetTarget(0);

            victim.Kill();

            if (localPlayer is not null && _world.LocalActorKey == victimKey)
                localPlayer.Buffs.ClearAll();
        }

        var isPkA = packet.DeathCause == 1;
        var isPkB = packet.DeathCause == 2;
        var isLocalPlayer = _world.LocalActorKey == victimKey;

        _eventBus.Publish(new ActorDiedEvent(
            victimKey, killerKey, packet.DeathCause, isPkA, isPkB, isLocalPlayer));
    }


    public void Handle(in SmsgLocalPlayerStateSync packet)
    {
        var key = new ActorKey(packet.TargetId, ToEntitySort((byte)packet.TargetSort));
        if (_world.LocalActorKey != key || !_world.TryGet(key, out var actor)) return;

        var mode = packet.Mode;

        var position = Vector3Fixed.FromFloat(packet.X, 0f, packet.Z);
        var heading =
            Vector3Fixed.FromFloat(packet.Heading, 0f, 0f).RawX;

        const float teleportThresholdSquared = 40000f;
        var (curX, _, curZ) = actor.Position.ToVector3Float();
        var dx = packet.X - curX;
        var dz = packet.Z - curZ;
        var teleported = dx * dx + dz * dz > teleportThresholdSquared;

        if (teleported)
            actor.SnapTo(position);
        else
            actor.SnapTo(position);

        actor.SetYaw(heading);


        _eventBus.Publish(new LocalPlayerStateSyncedEvent(key, position, heading, mode));
    }
}