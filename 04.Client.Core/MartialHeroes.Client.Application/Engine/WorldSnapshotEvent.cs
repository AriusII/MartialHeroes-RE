using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Engine;

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

public sealed record WorldSnapshotEvent(
    long Tick,
    uint FixedDeltaMs,
    ImmutableArray<ActorSnapshot> Actors) : IClientEvent;