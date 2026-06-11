using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Events;

/// <summary>
/// Published when an actor enters the world. Immutable snapshot of the freshly-spawned actor's
/// presentation-relevant fields. spec: Docs/RE/opcodes.md (5/3 SmsgCharSpawn);
/// Docs/RE/structs/actor.md (SpawnDescriptor fields).
/// </summary>
/// <param name="Key">Composite actor identity (raw id + sort).</param>
/// <param name="Name">Decoded actor name (NUL-terminated on the wire).</param>
/// <param name="Level">Character level.</param>
/// <param name="Position">Spawn position (Q16.16, world Y forced to 0).</param>
/// <param name="CurrentHp">Current hit points at spawn.</param>
/// <param name="MaxHp">Computed maximum hit points.</param>
/// <param name="ServerClass">Server-assigned class id (martial-arts style).</param>
public sealed record ActorSpawnedEvent(
    ActorKey Key,
    string Name,
    ushort Level,
    Vector3Fixed Position,
    uint CurrentHp,
    uint MaxHp,
    ushort ServerClass) : IClientEvent;

/// <summary>
/// Published when an actor's movement state changes. Immutable snapshot. spec: Docs/RE/opcodes.md
/// (5/13 SmsgActorMovementUpdate).
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="Position">Current position (Q16.16, FromFloat-converted at the handler boundary).</param>
/// <param name="MoveTarget">Interpolation destination (Q16.16).</param>
/// <param name="Yaw">Facing yaw, raw Q16.16.</param>
/// <param name="IsRunning">True when the run flag was set on the wire.</param>
public sealed record ActorMovedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    Vector3Fixed MoveTarget,
    int Yaw,
    bool IsRunning) : IClientEvent;

/// <summary>
/// Published when an actor leaves the world. Immutable snapshot. spec: Docs/RE/opcodes.md
/// (5/0 SmsgCharDespawn).
/// </summary>
/// <param name="Key">Composite actor identity that was removed.</param>
/// <param name="PlayLeaveEffect">Bit0 of the despawn flags: play a "left" SFX + chat line.</param>
public sealed record ActorDespawnedEvent(ActorKey Key, bool PlayLeaveEffect) : IClientEvent;

/// <summary>
/// Published whenever the client lifecycle state changes. Immutable snapshot. spec:
/// Docs/RE/opcodes.md (3/5 drives Loading -> World).
/// </summary>
/// <param name="Previous">The state the client left.</param>
/// <param name="Current">The state the client entered.</param>
public sealed record ClientStateChangedEvent(ClientState Previous, ClientState Current) : IClientEvent;

/// <summary>
/// Published when an actor's current vitals change (5/53). Immutable snapshot. spec:
/// Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="CurrentHp">Updated current hit points.</param>
/// <param name="CurrentMp">Updated current MP / ki (the third vital mirrored to the local player).</param>
/// <param name="CurrentStamina">Updated current stamina.</param>
public sealed record ActorVitalsChangedEvent(
    ActorKey Key,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina) : IClientEvent;

/// <summary>
/// Published when an actor's primary stats change (4/29 StatUpdate, applied only when ResultOk == 1).
/// Immutable snapshot of the five echoed absolute stats and remaining points. spec:
/// Docs/RE/packets/4-29_stat_update.yaml.
/// </summary>
/// <param name="Key">Composite actor identity (the local player).</param>
/// <param name="Stat0">Echoed stat[0].</param>
/// <param name="Stat1">Echoed stat[1].</param>
/// <param name="Stat2">Echoed stat[2].</param>
/// <param name="Stat3">Echoed stat[3].</param>
/// <param name="Stat4">Echoed stat[4].</param>
/// <param name="RemainingStatPoints">Remaining allocatable stat points.</param>
public sealed record ActorStatsChangedEvent(
    ActorKey Key,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint RemainingStatPoints) : IClientEvent;

/// <summary>
/// Published when an actor levels up (5/32): new level plus refreshed vitals. Immutable snapshot.
/// spec: Docs/RE/packets/5-32_level_up.yaml.
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="NewLevel">The actor's new level.</param>
/// <param name="CurrentHp">Refreshed current HP (low i32 half of the packed HP/MP value).</param>
/// <param name="CurrentMp">Refreshed current MP (high i32 half of the packed HP/MP value).</param>
/// <param name="CurrentStamina">Refreshed current stamina.</param>
/// <param name="RemainingStatPoints">Remaining allocatable stat points (local player).</param>
public sealed record ActorLeveledUpEvent(
    ActorKey Key,
    ushort NewLevel,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    int RemainingStatPoints) : IClientEvent;

/// <summary>
/// Published when the login handshake completes: the 1/4 Auth reply was built and sent in response to
/// the server's 0/0 KeyExchange. Immutable snapshot. spec: Docs/RE/specs/crypto.md §6.
/// </summary>
/// <param name="ReplyByteCount">Length of the built 1/4 reply body (diagnostics).</param>
public sealed record LoginHandshakeCompletedEvent(int ReplyByteCount) : IClientEvent;