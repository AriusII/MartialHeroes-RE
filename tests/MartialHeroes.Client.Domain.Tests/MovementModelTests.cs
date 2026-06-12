using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class MovementModelTests
{
    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(12, MovementModel.MoveStepClampUnits);
        Assert.Equal(144L, MovementModel.MoveStepClampSquaredUnits);
        Assert.Equal(2, MovementModel.MoveDeadZoneUnits);
        Assert.Equal(4L, MovementModel.MoveDeadZoneSquaredUnits);
    }

    [Fact]
    public void ResolveClickTarget_WithinDeadZone_NoCommit()
    {
        var current = Vector3Fixed.Zero;
        // 1.5 units away → squared 2.25 ≤ dead-zone 4 → no commit.
        var target = Vector3Fixed.FromFloat(1.5f, 0f, 0f);

        var (dest, commit) = MovementModel.ResolveClickTarget(current, target);

        Assert.False(commit);
        Assert.Equal(current, dest);
    }

    [Fact]
    public void ResolveClickTarget_WithinReach_TargetsClickPoint_YForcedZero()
    {
        var current = Vector3Fixed.Zero;
        // 5 units away on X → squared 25 (between 4 and 144) → target directly. Y forced 0.
        var target = Vector3Fixed.FromFloat(5f, 99f, 0f);

        var (dest, commit) = MovementModel.ResolveClickTarget(current, target);

        Assert.True(commit);
        var (x, y, z) = dest.ToVector3Float();
        Assert.Equal(5f, x, 3);
        Assert.Equal(0f, y, 3); // Y forced to 0.
        Assert.Equal(0f, z, 3);
    }

    [Fact]
    public void ResolveClickTarget_FarAway_ClampsToTwelveUnits()
    {
        var current = Vector3Fixed.Zero;
        // 100 units away on X → far → clamp to a 12-unit step along +X.
        var target = Vector3Fixed.FromFloat(100f, 0f, 0f);

        var (dest, commit) = MovementModel.ResolveClickTarget(current, target);

        Assert.True(commit);
        var (x, _, _) = dest.ToVector3Float();
        Assert.Equal(12f, x, 1); // clamped step length = 12.
    }

    [Fact]
    public void IntegrateTick_ClearTerrain_AdvancesTowardDestination()
    {
        var current = Vector3Fixed.Zero;
        var dest = Vector3Fixed.FromWholeUnits(10, 0, 0);
        long speed = 4L * Vector3Fixed.One; // 4 units/sec

        var (pos, arrived) = MovementModel.IntegrateTick(
            current, dest, speed, deltaMs: 1000, OpenTerrainCollision.Instance);

        Assert.False(arrived);
        var (x, _, _) = pos.ToVector3Float();
        Assert.Equal(4f, x, 1); // advanced 4 units in 1 s.
    }

    [Fact]
    public void IntegrateTick_BlockedTerrain_SnapsToClampedPoint()
    {
        var current = Vector3Fixed.Zero;
        var dest = Vector3Fixed.FromWholeUnits(10, 0, 0);
        var wall = Vector3Fixed.FromWholeUnits(2, 0, 0);
        var collision = new BlockingCollision(wall);

        var (pos, arrived) = MovementModel.IntegrateTick(
            current, dest, 100L * Vector3Fixed.One, deltaMs: 1000, collision);

        Assert.False(arrived);
        Assert.Equal(wall, pos); // wall-slide / stop.
    }

    [Fact]
    public void IntegrateTick_AlreadyAtDestination_Arrived()
    {
        var p = Vector3Fixed.FromWholeUnits(3, 0, 3);
        var (pos, arrived) = MovementModel.IntegrateTick(p, p, 1, 100, OpenTerrainCollision.Instance);

        Assert.True(arrived);
        Assert.Equal(p, pos);
    }

    [Fact]
    public void OpenTerrainCollision_NeverBlocks_AlwaysWalkable()
    {
        var c = OpenTerrainCollision.Instance;
        Assert.False(c.Sweep(Vector3Fixed.Zero, Vector3Fixed.FromWholeUnits(99, 0, 99)).Blocked);
        Assert.True(c.IsWalkable(Vector3Fixed.Zero));
    }

    private sealed class BlockingCollision(Vector3Fixed clamp) : ITerrainCollision
    {
        public CollisionResult Sweep(in Vector3Fixed from, in Vector3Fixed to) => CollisionResult.Hit(clamp);
        public bool IsWalkable(in Vector3Fixed point) => false;
    }
}