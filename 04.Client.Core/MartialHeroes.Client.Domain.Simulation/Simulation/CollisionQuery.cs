using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public readonly record struct CollisionResult
{
    public bool Blocked { get; init; }

    public Vector3Fixed ClampedPoint { get; init; }

    public static CollisionResult Clear(in Vector3Fixed destination)
    {
        return new CollisionResult { Blocked = false, ClampedPoint = destination };
    }

    public static CollisionResult Hit(in Vector3Fixed hitPoint)
    {
        return new CollisionResult { Blocked = true, ClampedPoint = hitPoint };
    }
}

public interface ITerrainCollision
{
    CollisionResult Sweep(in Vector3Fixed from, in Vector3Fixed to);

    bool IsWalkable(in Vector3Fixed point);
}

public sealed class OpenTerrainCollision : ITerrainCollision
{
    public static readonly OpenTerrainCollision Instance = new();

    public CollisionResult Sweep(in Vector3Fixed from, in Vector3Fixed to)
    {
        return CollisionResult.Clear(in to);
    }

    public bool IsWalkable(in Vector3Fixed point)
    {
        return true;
    }
}