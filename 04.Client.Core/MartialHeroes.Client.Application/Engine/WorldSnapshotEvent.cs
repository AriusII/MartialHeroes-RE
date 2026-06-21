using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Engine;

/// <summary>
///     An immutable per-actor sample inside a <see cref="WorldSnapshotEvent" />. Carries only value types
///     copied out of the live <see cref="Actor" /> aggregate, so the presentation consumer can interpolate
///     without ever touching mutable Domain state.
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="Position">Authoritative logical position this tick (Q16.16).</param>
/// <param name="MoveTarget">Interpolation destination (Q16.16); Godot lerps Position → MoveTarget.</param>
/// <param name="Yaw">Facing yaw (raw Q16.16).</param>
/// <param name="CurrentHp">Current hit points.</param>
/// <param name="MaxHp">Computed maximum hit points.</param>
/// <param name="CurrentMp">Current mana / ki.</param>
/// <param name="MaxMp">Computed maximum mana / ki.</param>
/// <param name="IsAlive">Alive flag.</param>
public readonly record struct ActorSnapshot(
    ActorKey Key,
    Vector3Fixed Position,
    Vector3Fixed MoveTarget,
    int Yaw,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    bool IsAlive);

/// <summary>
///     Published once per fixed logic tick by the <see cref="GameEngineLoop" />. An immutable snapshot of
///     every live actor's presentation-relevant state, plus the tick index, so the Godot layer can
///     interpolate between successive snapshots.
/// </summary>
/// <remarks>
///     <para>
///         This is the load-bearing artefact of the intentional divergence to a fixed-tick model
///         (spec: Docs/RE/specs/game_loop.md §6): "Godot … interpolates between simulation snapshots produced
///         by the fixed tick." The snapshot is a deep copy of value types — never a reference to a live
///         <see cref="Actor" /> — so the consumer cannot observe torn Domain state.
///     </para>
///     <para>
///         The actor list is an <see cref="ImmutableArray{T}" /> of <see cref="ActorSnapshot" /> structs: one
///         pooled-friendly allocation per tick rather than per-actor garbage.
///     </para>
/// </remarks>
/// <param name="Tick">Monotonic fixed-tick index since the loop started (0-based).</param>
/// <param name="FixedDeltaMs">The fixed simulation delta applied this tick, in milliseconds (post time-scale).</param>
/// <param name="Actors">Immutable per-actor snapshots for this tick.</param>
public sealed record WorldSnapshotEvent(
    long Tick,
    uint FixedDeltaMs,
    ImmutableArray<ActorSnapshot> Actors) : IClientEvent;