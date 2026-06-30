using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public readonly record struct LocomotionStepInput(
    Vector3Fixed Position,
    Vector3Fixed SegmentStart,
    Vector3Fixed MoveTarget,
    long SpeedRawPerSecond,
    MoveScale Scale,
    uint DeltaMs);

public readonly record struct LocomotionStepResult(
    Vector3Fixed Position,
    bool Arrived);

public static class LocomotionStep
{
    public static LocomotionStepResult Advance(in LocomotionStepInput input)
    {
        if (input.SpeedRawPerSecond < 0)
            throw new ArgumentOutOfRangeException(
                nameof(input), "Speed must be non-negative.");

        var dx = (long)input.MoveTarget.RawX - input.Position.RawX;
        var dz = (long)input.MoveTarget.RawZ - input.Position.RawZ;

        var lengthSquared = (Int128)dx * dx + (Int128)dz * dz;
        if (lengthSquared == 0)
            return new LocomotionStepResult(input.MoveTarget, true);

        var stepLength = ComputeStepLength(input.SpeedRawPerSecond, input.DeltaMs, input.Scale);

        Vector3Fixed next;
        if (stepLength <= 0)
        {
            next = input.Position;
        }
        else
        {
            var length = IntegerSqrt(lengthSquared);
            if (length == 0)
                return new LocomotionStepResult(input.MoveTarget, true);

            var nx = input.Position.RawX + ScaleAxis(dx, stepLength, length);
            var nz = input.Position.RawZ + ScaleAxis(dz, stepLength, length);
            next = new Vector3Fixed((int)nx, input.Position.RawY, (int)nz);
        }

        if (HasArrived(next, input.SegmentStart, input.MoveTarget))
            return new LocomotionStepResult(input.MoveTarget, true);

        return new LocomotionStepResult(next, false);
    }

    public static long ComputeStepLength(long speedRawPerSecond, uint deltaMs, MoveScale scale)
    {
        if (speedRawPerSecond <= 0 || deltaMs == 0) return 0;

        var numerator = (Int128)speedRawPerSecond * deltaMs * scale.Raw;
        var denominator = (Int128)1000 * MoveScale.Default.Raw;
        return (long)(numerator / denominator);
    }

    private static bool HasArrived(Vector3Fixed position, Vector3Fixed segmentStart, Vector3Fixed target)
    {
        if (PlanarDistance.SquaredXz(position, target) < LocomotionThresholds.ArrivalSquared)
            return true;

        var travelled = PlanarDistance.SquaredXz(position, segmentStart);
        var segment = PlanarDistance.SquaredXz(target, segmentStart);
        return travelled >= segment;
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

    private static long IntegerSqrt(Int128 value)
    {
        if (value <= 0) return 0;

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
