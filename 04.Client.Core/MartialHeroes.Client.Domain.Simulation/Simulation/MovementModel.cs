using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public static class MovementModel
{
    public const int MoveStepClampUnits = 12;

    public const long MoveStepClampSquaredUnits = (long)MoveStepClampUnits * MoveStepClampUnits;

    public const int MoveDeadZoneUnits = 2;

    public const long MoveDeadZoneSquaredUnits = (long)MoveDeadZoneUnits * MoveDeadZoneUnits;

    public static (Vector3Fixed Destination, bool Commit) ResolveClickTarget(
        in Vector3Fixed current,
        in Vector3Fixed rawTarget)
    {
        var dx = (long)rawTarget.RawX - current.RawX;
        var dz = (long)rawTarget.RawZ - current.RawZ;

        var oneSquared = (long)Vector3Fixed.One * Vector3Fixed.One;
        var rawSquared = (Int128)dx * dx + (Int128)dz * dz;

        var deadZoneRaw = (Int128)MoveDeadZoneSquaredUnits * oneSquared;
        if (rawSquared <= deadZoneRaw) return (current, false);

        var clampRaw = (Int128)MoveStepClampSquaredUnits * oneSquared;
        if (rawSquared <= clampRaw)
            return (new Vector3Fixed(rawTarget.RawX, 0, rawTarget.RawZ), true);

        var distance = LinearMovement.IntegerSqrt(rawSquared);
        var stepRaw = (long)MoveStepClampUnits * Vector3Fixed.One;
        var nx = current.RawX + ScaleAxis(dx, stepRaw, distance);
        var nz = current.RawZ + ScaleAxis(dz, stepRaw, distance);
        return (new Vector3Fixed((int)nx, 0, (int)nz), true);
    }

    public static (Vector3Fixed Position, bool Arrived) IntegrateTick(
        in Vector3Fixed current,
        in Vector3Fixed destination,
        long speedRawPerSecond,
        uint deltaMs,
        ITerrainCollision collision)
    {
        ArgumentNullException.ThrowIfNull(collision);

        if (current == destination) return (destination, true);

        var (candidate, arrived) =
            LinearMovement.Step(current, destination, speedRawPerSecond, deltaMs);

        var result = collision.Sweep(in current, in candidate);
        if (result.Blocked)
            return (result.ClampedPoint, false);

        return (candidate, arrived);
    }

    private static long ScaleAxis(long axisDelta, long stepLength, long distance)
    {
        if (distance == 0) return 0;

        var numerator = (Int128)axisDelta * stepLength;
        Int128 half = distance / 2;
        var rounded = numerator >= 0
            ? (numerator + half) / distance
            : (numerator - half) / distance;
        return (long)rounded;
    }
}