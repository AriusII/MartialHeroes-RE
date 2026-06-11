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
