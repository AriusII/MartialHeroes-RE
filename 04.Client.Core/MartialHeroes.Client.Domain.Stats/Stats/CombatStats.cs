namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct CombatStats
{
    public int Str { get; init; }

    public int Dex { get; init; }

    public int Vital { get; init; }

    public int Inte { get; init; }

    public int Agil { get; init; }

    public short MaxStamina { get; init; }

    public int CriticalValue { get; init; }

    public int MinDamage { get; init; }

    public int MaxDamage { get; init; }

    public int Damage { get; init; }

    public int Defence { get; init; }

    public int HuntDamageRate0 { get; init; }

    public int HuntDamageRate1 { get; init; }

    public int PvpDamageRate0 { get; init; }

    public int PvpDamageRate1 { get; init; }

    public int MaxLife { get; init; }

    public int MaxEnergy { get; init; }


    public float MaxLifeRate { get; init; }

    public float MaxEnergyRate { get; init; }

    public float CriticalRate { get; init; }

    public float HitRate { get; init; }

    public float DefenceRate { get; init; }

    public float CriticalHit { get; init; }

    public float OrderSpecial0 { get; init; }

    public float OrderSpecial1 { get; init; }

    public float OrderSpecial2 { get; init; }

    public float OrderSpecial3 { get; init; }

    public float Range { get; init; }


    public int AttackRating { get; init; }

    public int HitRating { get; init; }

    public static CombatStats Empty => default;
}