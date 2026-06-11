using System.Runtime.CompilerServices;

namespace MartialHeroes.Shared.Kernel.Numerics;

/// <summary>
/// A deterministic, fixed-point 3-D vector using Q16.16 format.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q16.16 format:</b> each component is a signed 32-bit integer where the upper 16 bits
/// represent the integer part and the lower 16 bits represent the fractional part.
/// <c>One = 1 &lt;&lt; 16 = 65536</c>, so the value <c>1.5</c> in real units is stored as
/// <c>98304</c> (<c>1 * 65536 + 32768</c>).
/// </para>
/// <para>
/// <b>Why fixed-point:</b> floating-point arithmetic is non-deterministic across platforms and
/// compiler optimisations. A headless server and the Godot client must compute identical
/// trajectories for hit-detection and state rollback. All trajectory/position arithmetic stays
/// inside this type; the only permitted float conversions are <see cref="FromFloat"/> and
/// <see cref="ToVector3Float"/> — both clearly marked and called only at the presentation
/// boundary (Godot layer) or when ingesting float-typed assets.
/// </para>
/// </remarks>
public readonly struct Vector3Fixed : IEquatable<Vector3Fixed>
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// The scale factor for Q16.16: 1 real unit = 65 536 raw units.
    /// All fixed-point arithmetic uses this constant as the denominator.
    /// </summary>
    public const int One = 1 << 16; // 65536

    /// <summary>The zero vector (origin).</summary>
    public static readonly Vector3Fixed Zero = new(0, 0, 0);

    // -------------------------------------------------------------------------
    // Components (raw Q16.16 integers)
    // -------------------------------------------------------------------------

    /// <summary>Raw Q16.16 X component.</summary>
    public readonly int RawX;

    /// <summary>Raw Q16.16 Y component.</summary>
    public readonly int RawY;

    /// <summary>Raw Q16.16 Z component.</summary>
    public readonly int RawZ;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Constructs a vector from three raw Q16.16 integers.
    /// </summary>
    /// <param name="rawX">Raw Q16.16 X component.</param>
    /// <param name="rawY">Raw Q16.16 Y component.</param>
    /// <param name="rawZ">Raw Q16.16 Z component.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3Fixed(int rawX, int rawY, int rawZ)
    {
        RawX = rawX;
        RawY = rawY;
        RawZ = rawZ;
    }

    /// <summary>
    /// Constructs a vector from three whole-unit integers (no fractional part).
    /// Equivalent to <c>new Vector3Fixed(x * One, y * One, z * One)</c>.
    /// </summary>
    /// <param name="x">Whole-unit X value.</param>
    /// <param name="y">Whole-unit Y value.</param>
    /// <param name="z">Whole-unit Z value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed FromWholeUnits(int x, int y, int z) =>
        new(x * One, y * One, z * One);

    // -------------------------------------------------------------------------
    // Presentation-boundary float bridge
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts three <see cref="float"/> values into a <see cref="Vector3Fixed"/>.
    /// </summary>
    /// <remarks>
    /// <b>Presentation boundary only.</b> This method introduces floating-point rounding and
    /// must never be called on the deterministic game-logic path. Acceptable uses: ingesting
    /// float-typed asset coordinates, converting Godot <c>Vector3</c> inputs at the boundary
    /// layer before handing to game logic.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed FromFloat(float x, float y, float z) =>
        new((int)(x * One), (int)(y * One), (int)(z * One));

    /// <summary>
    /// Converts this fixed-point vector to a tuple of <see cref="float"/> values.
    /// </summary>
    /// <remarks>
    /// <b>Presentation boundary only.</b> Only call this when handing a position to the
    /// Godot rendering layer. Do not use the returned values in any deterministic calculation.
    /// </remarks>
    /// <returns>A tuple <c>(X, Y, Z)</c> in real floating-point units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (float X, float Y, float Z) ToVector3Float() =>
        ((float)RawX / One, (float)RawY / One, (float)RawZ / One);

    // -------------------------------------------------------------------------
    // Arithmetic operators (deterministic — no float inside)
    // -------------------------------------------------------------------------

    /// <summary>Adds two fixed-point vectors component-wise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator +(Vector3Fixed a, Vector3Fixed b) =>
        new(a.RawX + b.RawX, a.RawY + b.RawY, a.RawZ + b.RawZ);

    /// <summary>Subtracts one fixed-point vector from another component-wise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator -(Vector3Fixed a, Vector3Fixed b) =>
        new(a.RawX - b.RawX, a.RawY - b.RawY, a.RawZ - b.RawZ);

    /// <summary>Negates a fixed-point vector component-wise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator -(Vector3Fixed v) =>
        new(-v.RawX, -v.RawY, -v.RawZ);

    /// <summary>
    /// Multiplies two fixed-point scalars using Q16.16 semantics:
    /// <c>(a * b) &gt;&gt; 16</c> to keep the result in Q16.16 range.
    /// </summary>
    /// <remarks>
    /// Uses <c>long</c> for the intermediate product to avoid overflow before the right-shift.
    /// This is the correct way to multiply two Q16.16 values; the result is also Q16.16.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator *(Vector3Fixed v, int scalar)
    {
        // scalar is a raw Q16.16 value; use long intermediate to prevent overflow.
        return new(
            (int)(((long)v.RawX * scalar) >> 16),
            (int)(((long)v.RawY * scalar) >> 16),
            (int)(((long)v.RawZ * scalar) >> 16));
    }

    /// <inheritdoc cref="operator *(Vector3Fixed, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator *(int scalar, Vector3Fixed v) => v * scalar;

    // -------------------------------------------------------------------------
    // Equality
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector3Fixed other) =>
        RawX == other.RawX && RawY == other.RawY && RawZ == other.RawZ;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector3Fixed other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RawX, RawY, RawZ);

    /// <summary>Compares two vectors for equality.</summary>
    public static bool operator ==(Vector3Fixed left, Vector3Fixed right) => left.Equals(right);

    /// <summary>Compares two vectors for inequality.</summary>
    public static bool operator !=(Vector3Fixed left, Vector3Fixed right) => !left.Equals(right);

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public override string ToString()
    {
        var (x, y, z) = ToVector3Float();
        return $"({x:F4}, {y:F4}, {z:F4})";
    }
}
