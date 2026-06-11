using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// Deterministic straight-line movement on the fixed-point position grid.
/// </summary>
/// <remarks>
/// <para>
/// The legacy client stored and interpolated world positions as IEEE-754 <c>float</c>
/// (spec: Docs/RE/structs/actor.md "Coordinate type"). This deterministic core instead advances
/// the authoritative logical position in <see cref="Vector3Fixed"/> (Q16.16) so the client and a
/// future headless server compute bit-identical trajectories; float is confined to the
/// network/presentation boundary via <see cref="Vector3Fixed.FromFloat"/>. World Y is never moved
/// here because the server never sends Y and forces it to 0 (spec: Docs/RE/structs/actor.md).
/// </para>
/// <para>
/// All arithmetic is integer-only. Speed is expressed in <b>raw Q16.16 units per second</b>; the
/// per-advance step length is <c>speed * deltaMs / 1000</c>. If the remaining distance to the
/// target is less than or equal to the step length, the actor lands exactly on the target with no
/// overshoot.
/// </para>
/// </remarks>
public static class LinearMovement
{
    /// <summary>
    /// Advances <paramref name="current"/> toward <paramref name="target"/> along a straight line.
    /// </summary>
    /// <param name="current">Current position (Q16.16).</param>
    /// <param name="target">Destination position (Q16.16).</param>
    /// <param name="speedRawPerSecond">Movement speed in raw Q16.16 units per second; must be &gt;= 0.</param>
    /// <param name="deltaMs">Elapsed time for this advance, in milliseconds (caller-supplied).</param>
    /// <returns>
    /// The new position and whether the target was reached this advance. The result equals
    /// <paramref name="target"/> exactly when the step would meet or exceed the remaining distance.
    /// </returns>
    public static (Vector3Fixed Position, bool Arrived) Step(
        Vector3Fixed current,
        Vector3Fixed target,
        long speedRawPerSecond,
        uint deltaMs)
    {
        if (speedRawPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedRawPerSecond), "Speed must be non-negative.");
        }

        // Displacement to the target, per-axis, in raw Q16.16 units.
        long dx = (long)target.RawX - current.RawX;
        long dy = (long)target.RawY - current.RawY;
        long dz = (long)target.RawZ - current.RawZ;

        // Squared remaining distance (raw^2). Fits in a 128-bit conceptual range but each axis
        // delta is bounded by ~2^32, so the square is < 2^64 only when components are modest;
        // we widen to unsigned 128-bit emulation via two longs only if needed. In practice map
        // coordinates are far below 2^31 raw, so the products stay within Int64/Int128.
        System.Int128 distanceSquared =
            (System.Int128)dx * dx + (System.Int128)dy * dy + (System.Int128)dz * dz;

        if (distanceSquared == 0)
        {
            return (target, true);
        }

        // Step length in raw units for this advance: speed (raw/s) * deltaMs / 1000.
        long stepLength = (long)(((System.Int128)speedRawPerSecond * deltaMs) / 1000);

        if (stepLength <= 0)
        {
            return (current, false);
        }

        System.Int128 stepSquared = (System.Int128)stepLength * stepLength;

        // If the step reaches or passes the target, snap exactly to it (no overshoot).
        if (stepSquared >= distanceSquared)
        {
            return (target, true);
        }

        // Move stepLength along the unit direction. distance = sqrt(distanceSquared).
        long distance = IntegerSqrt(distanceSquared);
        if (distance == 0)
        {
            return (target, true);
        }

        // newPos = current + delta * (stepLength / distance), computed per axis with rounding
        // toward nearest to keep symmetric behaviour. Intermediate uses Int128 to avoid overflow.
        long nx = current.RawX + ScaleAxis(dx, stepLength, distance);
        long ny = current.RawY + ScaleAxis(dy, stepLength, distance);
        long nz = current.RawZ + ScaleAxis(dz, stepLength, distance);

        var next = new Vector3Fixed((int)nx, (int)ny, (int)nz);
        return (next, false);
    }

    /// <summary>
    /// Computes <c>round(axisDelta * stepLength / distance)</c> using 128-bit intermediates and
    /// round-half-away-from-zero, so the per-axis projection is deterministic and symmetric.
    /// </summary>
    private static long ScaleAxis(long axisDelta, long stepLength, long distance)
    {
        System.Int128 numerator = (System.Int128)axisDelta * stepLength;
        System.Int128 half = distance / 2;
        System.Int128 rounded = numerator >= 0
            ? (numerator + half) / distance
            : (numerator - half) / distance;
        return (long)rounded;
    }

    /// <summary>
    /// Integer square root (floor) of a non-negative 128-bit value, computed with a deterministic
    /// binary method. No floating point.
    /// </summary>
    internal static long IntegerSqrt(System.Int128 value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Cannot take sqrt of a negative value.");
        }

        if (value == 0)
        {
            return 0;
        }

        System.UInt128 v = (System.UInt128)value;
        System.UInt128 result = 0;

        // Highest power-of-four bit not exceeding v.
        System.UInt128 bit = (System.UInt128)1 << 126;
        while (bit > v)
        {
            bit >>= 2;
        }

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
