using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 5/10 — character / actor death push
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/10 — actor death push. Resolves the victim and killer by their composite (id, sort) keys, runs
    ///     the COMMON death arm on the victim (clear locked battle target if it matches, zero combat
    ///     resources, play the death motion, clear the buff array), then switches on
    ///     <see cref="SmsgCharDeath.DeathCause" /> (0 normal / 1 PK-A / 2 PK-B / 3 special-no-modal). PK
    ///     effects (causes 1, 2) anchor on the VICTIM pair, not the killer. Publishes
    ///     <see cref="ActorDiedEvent" />; layer 05 plays the death motion/effect and opens the respawn modal
    ///     off it. Respawn-modal mode (1 vs 3), effect/sound/message ids and death-penalty magnitudes are
    ///     NON-blocking value residuals and are NOT modelled here. spec: Docs/RE/packets/5-10_combat_death.yaml;
    ///     Docs/RE/structs/actor.md (death-state fields +0x58C/+0x590/+0x5C8; battle-controller lock item 12).
    /// </summary>
    public void Handle(in SmsgCharDeath packet)
    {
        var victimKey = new ActorKey(packet.VictimId, ToEntitySort(packet.VictimSort));
        var killerKey = new ActorKey(packet.KillerId, ToEntitySort(packet.KillerSort));

        // COMMON ARM (every DeathCause): apply the victim-side death state when we know the victim actor.
        if (_world.TryGet(victimKey, out var victim))
        {
            // Per actor.md item 12 the locked battle target lives on the battle-controller SINGLETON
            // (BC+9 id / BC+40 sort), NOT the actor. There is NO battle-controller abstraction in
            // Application, so we clear the victim actor's UI target id (SetTarget(0)) instead of
            // fabricating a battle-controller type. spec: Docs/RE/structs/actor.md item 12 (battle-controller
            // lock clear — debugger-pending, no BC abstraction in Application; CYCLE 12 deferred).
            victim.SetTarget(0);

            // Zero the victim's combat resources + play the death motion. The Domain Actor's Kill() zeroes HP,
            // clears the in-combat flag, sets lifecycle to Dead (LifecycleState value 8 = Dead/knockdown per
            // actor.md +0x58C; alive -> 0) and parks the move target. There is no separate combat-resource /
            // death-timestamp setter on the Domain Actor; the death timestamp is a NON-blocking residual.
            // spec: Docs/RE/structs/actor.md (+0x58C lifecycle Dead = 8; alive = 0); 5-10_combat_death.yaml
            // (death timestamp — debugger-pending, CYCLE 12 deferred).
            victim.Kill();

            // Clear the victim's 30-slot buff array. The Domain Actor exposes no buff table (buffs are mirrored
            // on LocalPlayerState.Buffs only); for the local player we clear that mirror, otherwise the per-actor
            // buff array clear is a NON-blocking residual (no Domain Actor buff-clear API).
            // spec: Docs/RE/structs/actor.md (30-slot buff array clear on death — no Domain Actor buff-clear API;
            // CYCLE 12 deferred).
            if (_localPlayer is not null && _world.LocalActorKey == victimKey)
                _localPlayer.Buffs.ClearAll();
        }

        // DEATHCAUSE SWITCH: 0 normal / 1 PK-A / 2 PK-B / 3 special-no-modal. PK effects (1, 2) ANCHOR ON THE
        // VICTIM pair (victim sort + id), NOT the killer. spec: 5-10_combat_death.yaml (DeathCause).
        // Respawn-modal mode (1 vs 3), effect/sound/message ids, death-penalty magnitudes are NON-blocking
        // value residuals — NOT modelled. spec: 5-10_combat_death.yaml (debugger-pending, CYCLE 12 deferred).
        var isPkA = packet.DeathCause == 1; // spec: 5-10_combat_death.yaml (DeathCause 1 = PK type A)
        var isPkB = packet.DeathCause == 2; // spec: 5-10_combat_death.yaml (DeathCause 2 = PK type B)
        var isLocalPlayer = _world.LocalActorKey == victimKey;

        _eventBus.Publish(new ActorDiedEvent(
            victimKey, killerKey, packet.DeathCause, isPkA, isPkB, isLocalPlayer));
    }

    // -------------------------------------------------------------------------
    // 4/13 — local player state sync (Mode @ wire 33)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/13 — local player state synchronization. Gated to the local player ((TargetSort, TargetId) must
    ///     match the local actor). Reads X/Z (Y forced 0), the heading, and the sync-mode byte (which sits at
    ///     wire offset 33, NOT 32). When the squared location delta exceeds 40000 (&gt; 200 units) the actor is
    ///     snapped/teleported; otherwise its position is updated. Mode 1 is a BattleController sync (stance
    ///     indicators), mode 5 is a no-state-write. Publishes <see cref="LocalPlayerStateSyncedEvent" />.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.
    /// </summary>
    public void Handle(in SmsgLocalPlayerStateSync packet)
    {
        // Gate: only process for the local player. spec: 4-13_local_player_state_sync.yaml (TargetSort/TargetId gate).
        var key = new ActorKey(packet.TargetId, ToEntitySort((byte)packet.TargetSort));
        if (_world.LocalActorKey != key || !_world.TryGet(key, out var actor)) return;

        // spec: 4-13_local_player_state_sync.yaml (Mode @ wire 33, NOT 32; bytes 24..32 reserved gap).
        var mode = packet.Mode;

        // Float -> fixed at the boundary; world Y forced 0. spec: 4-13 (X @+0x10, Z @+0x14; Y ignored).
        var position = Vector3Fixed.FromFloat(packet.X, 0f, packet.Z);
        var heading =
            Vector3Fixed.FromFloat(packet.Heading, 0f, 0f).RawX; // reuse Q16.16 conversion for the heading scalar

        // Squared location delta computed in float at this boundary (no deterministic-path use): a delta
        // whose square exceeds 40000 (i.e. > 200 units) is a teleport — snap and reset visual anims; else
        // update the position. spec: 4-13_local_player_state_sync.yaml (40000 = (>200)^2 teleport threshold).
        const float teleportThresholdSquared = 40000f; // spec: 4-13 (>200 units => snap/teleport)
        var (curX, _, curZ) = actor.Position.ToVector3Float();
        var dx = packet.X - curX;
        var dz = packet.Z - curZ;
        var teleported = dx * dx + dz * dz > teleportThresholdSquared;

        if (teleported)
            actor.SnapTo(position); // teleport: snap + (layer 05) reset visual anims off the event. spec: 4-13.
        else
            actor.SnapTo(position); // in-range sync: set position from the authoritative sample. spec: 4-13.

        actor.SetYaw(heading);

        // Mode 1 = BattleController sync (stance / battle indicators). There is no battle/stance setter on the
        // Domain Actor; the stance update is carried on the event for layer 05 (no Domain stance API).
        // spec: 4-13_local_player_state_sync.yaml (Mode 1 = BattleController sync — stance setter debugger-pending,
        // CYCLE 12 deferred).

        _eventBus.Publish(new LocalPlayerStateSyncedEvent(key, position, heading, mode));
    }
}