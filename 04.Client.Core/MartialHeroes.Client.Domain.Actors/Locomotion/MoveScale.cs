namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public readonly struct MoveScale : IEquatable<MoveScale>
{
    public const int FractionBits = 16;

    public const int Unit = 1 << FractionBits;

    public static readonly MoveScale Default = FromRaw(Unit + (Unit >> 1));

    public static readonly MoveScale FastCatchUp = FromRaw(3 * Unit);

    public static readonly MoveScale RemotePlayerFinish = FromRaw(4 * Unit + (Unit >> 1));

    public static readonly MoveScale RemoteNonPlayerFinish = FromRaw(2 * Unit + (Unit >> 1));

    public readonly int Raw;

    private MoveScale(int raw)
    {
        Raw = raw;
    }

    public static MoveScale FromRaw(int raw)
    {
        if (raw < 0)
            throw new ArgumentOutOfRangeException(nameof(raw), "Move scale must be non-negative.");

        return new MoveScale(raw);
    }

    public static MoveScale FromUnits(int wholeUnits, int sixteenthsUnit)
    {
        return FromRaw(wholeUnits * Unit + sixteenthsUnit * (Unit >> 4));
    }

    public bool Equals(MoveScale other)
    {
        return Raw == other.Raw;
    }

    public override bool Equals(object? obj)
    {
        return obj is MoveScale other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Raw;
    }

    public static bool operator ==(MoveScale left, MoveScale right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MoveScale left, MoveScale right)
    {
        return !left.Equals(right);
    }
}
