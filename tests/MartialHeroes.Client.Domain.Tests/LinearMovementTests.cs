using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class LinearMovementTests
{
    [Fact]
    public void Step_ReachesTargetExactly_WhenStepExceedsRemainingDistance()
    {
        Vector3Fixed start = Vector3Fixed.FromWholeUnits(0, 0, 0);
        Vector3Fixed target = Vector3Fixed.FromWholeUnits(3, 0, 0);

        // Speed = 100 whole units/sec; over 1000ms the step (100 units) far exceeds 3 units.
        (Vector3Fixed pos, bool arrived) =
            LinearMovement.Step(start, target, 100L * Vector3Fixed.One, deltaMs: 1000);

        Assert.True(arrived);
        Assert.Equal(target, pos);
    }

    [Fact]
    public void Step_DoesNotOvershoot_AndArrivesExactlyOverManyTicks()
    {
        Vector3Fixed start = Vector3Fixed.FromWholeUnits(0, 0, 0);
        Vector3Fixed target = Vector3Fixed.FromWholeUnits(10, 0, 0);

        // 1 whole unit per second. 10 ticks of 1000ms each => arrives exactly at unit 10.
        long speed = 1L * Vector3Fixed.One;
        Vector3Fixed pos = start;
        bool arrived = false;
        for (int i = 0; i < 10; i++)
        {
            (pos, arrived) = LinearMovement.Step(pos, target, speed, 1000);
        }

        Assert.True(arrived);
        Assert.Equal(target, pos);
    }

    [Fact]
    public void Step_ArrivesExactly_OnDiagonalPath()
    {
        Vector3Fixed start = Vector3Fixed.Zero;
        Vector3Fixed target = Vector3Fixed.FromWholeUnits(3, 0, 4); // distance = 5 whole units

        long speed = 1L * Vector3Fixed.One; // 1 unit/sec
        Vector3Fixed pos = start;
        bool arrived = false;

        // 5 one-second ticks should land exactly on the target (length 5).
        for (int i = 0; i < 5; i++)
        {
            (pos, arrived) = LinearMovement.Step(pos, target, speed, 1000);
        }

        Assert.True(arrived);
        Assert.Equal(target, pos);
    }

    [Fact]
    public void Step_ReturnsArrived_WhenAlreadyAtTarget()
    {
        Vector3Fixed p = Vector3Fixed.FromWholeUnits(5, 0, 5);
        (Vector3Fixed pos, bool arrived) = LinearMovement.Step(p, p, 999, 50);

        Assert.True(arrived);
        Assert.Equal(p, pos);
    }

    [Fact]
    public void Step_DoesNotMove_WhenSpeedIsZero()
    {
        Vector3Fixed start = Vector3Fixed.Zero;
        Vector3Fixed target = Vector3Fixed.FromWholeUnits(10, 0, 0);

        (Vector3Fixed pos, bool arrived) = LinearMovement.Step(start, target, 0, 1000);

        Assert.False(arrived);
        Assert.Equal(start, pos);
    }

    [Fact]
    public void Step_MovesPartway_WhenStepShorterThanDistance()
    {
        Vector3Fixed start = Vector3Fixed.Zero;
        Vector3Fixed target = Vector3Fixed.FromWholeUnits(10, 0, 0);

        // 1 unit/sec for 1000ms => exactly 1 unit along +X.
        (Vector3Fixed pos, bool arrived) =
            LinearMovement.Step(start, target, 1L * Vector3Fixed.One, 1000);

        Assert.False(arrived);
        Assert.Equal(Vector3Fixed.FromWholeUnits(1, 0, 0), pos);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(1_000_000, 1000)]
    public void IntegerSqrt_FloorsCorrectly(long input, long expected)
    {
        Assert.Equal(expected, LinearMovement.IntegerSqrt(input));
    }
}
