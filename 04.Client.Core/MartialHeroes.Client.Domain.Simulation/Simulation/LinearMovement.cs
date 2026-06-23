using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public static class LinearMovement
{
    public static (Vector3Fixed Position, bool Arrived) Step(
        Vector3Fixed current,
        Vector3Fixed target,
        long speedRawPerSecond,
        uint deltaMs)
    {
        if (speedRawPerSecond < 0)
            throw new ArgumentOutOfRangeException(
                nameof(speedRawPerSecond), "Speed must be non-negative.");

        var dx = (long)target.RawX - current.RawX;
        var dy = (long)target.RawY - current.RawY;
        var dz = (long)target.RawZ - current.RawZ;

        var distanceSquared =
            (Int128)dx * dx + (Int128)dy * dy + (Int128)dz * dz;

        if (distanceSquared == 0) return (target, true);

        var stepLength = (long)((Int128)speedRawPerSecond * deltaMs / 1000);

        if (stepLength <= 0) return (current, false);

        var stepSquared = (Int128)stepLength * stepLength;

        if (stepSquared >= distanceSquared) return (target, true);

        var distance = IntegerSqrt(distanceSquared);
        if (distance == 0) return (target, true);

        var nx = current.RawX + ScaleAxis(dx, stepLength, distance);
        var ny = current.RawY + ScaleAxis(dy, stepLength, distance);
        var nz = current.RawZ + ScaleAxis(dz, stepLength, distance);

        var next = new Vector3Fixed((int)nx, (int)ny, (int)nz);
        return (next, false);
    }

    private static long ScaleAxis(long axisDelta, long stepLength, long distance)
    {
        var numerator = (Int128)axisDelta * stepLength;
        Int128 half = distance / 2;
        var rounded = numerator >= 0
            ? (numerator + half) / distance
            : (numerator - half) / distance;
        return (long)rounded;
    }

    internal static long IntegerSqrt(Int128 value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Cannot take sqrt of a negative value.");

        if (value == 0) return 0;

        var v = (UInt128)value;
        UInt128 result = 0;

        var bit = (UInt128)1 << 126;
        while (bit > v) bit >>= 2;

        while (bit != 0)
        {
            if (v >= result + bit)
            {
                v -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return (long)result;
    }
}