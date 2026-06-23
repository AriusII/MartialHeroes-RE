using System.Runtime.CompilerServices;

namespace MartialHeroes.Shared.Kernel.Numerics;

public readonly struct Vector3Fixed : IEquatable<Vector3Fixed>
{
    public const int One = 1 << 16;

    public static readonly Vector3Fixed Zero = new(0, 0, 0);


    public readonly int RawX;

    public readonly int RawY;

    public readonly int RawZ;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3Fixed(int rawX, int rawY, int rawZ)
    {
        RawX = rawX;
        RawY = rawY;
        RawZ = rawZ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed FromWholeUnits(int x, int y, int z)
    {
        return new Vector3Fixed(x * One, y * One, z * One);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed FromFloat(float x, float y, float z)
    {
        return new Vector3Fixed((int)(x * One), (int)(y * One), (int)(z * One));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (float X, float Y, float Z) ToVector3Float()
    {
        return ((float)RawX / One, (float)RawY / One, (float)RawZ / One);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator +(Vector3Fixed a, Vector3Fixed b)
    {
        return new Vector3Fixed(a.RawX + b.RawX, a.RawY + b.RawY, a.RawZ + b.RawZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator -(Vector3Fixed a, Vector3Fixed b)
    {
        return new Vector3Fixed(a.RawX - b.RawX, a.RawY - b.RawY, a.RawZ - b.RawZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator -(Vector3Fixed v)
    {
        return new Vector3Fixed(-v.RawX, -v.RawY, -v.RawZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator *(Vector3Fixed v, int scalar)
    {
        return new Vector3Fixed(
            (int)(((long)v.RawX * scalar) >> 16),
            (int)(((long)v.RawY * scalar) >> 16),
            (int)(((long)v.RawZ * scalar) >> 16));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Fixed operator *(int scalar, Vector3Fixed v)
    {
        return v * scalar;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector3Fixed other)
    {
        return RawX == other.RawX && RawY == other.RawY && RawZ == other.RawZ;
    }

    public override bool Equals(object? obj)
    {
        return obj is Vector3Fixed other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RawX, RawY, RawZ);
    }

    public static bool operator ==(Vector3Fixed left, Vector3Fixed right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Vector3Fixed left, Vector3Fixed right)
    {
        return !left.Equals(right);
    }


    public override string ToString()
    {
        var (x, y, z) = ToVector3Float();
        return $"({x:F4}, {y:F4}, {z:F4})";
    }
}