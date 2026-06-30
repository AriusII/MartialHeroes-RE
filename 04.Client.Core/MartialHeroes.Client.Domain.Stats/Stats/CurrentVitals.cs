namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct CurrentVitals(long Hp, int Mp, int Stamina)
{
    public static readonly CurrentVitals Zero = new(0L, 0, 0);

    public static CurrentVitals FromWire(long hp, int mp, int stamina)
    {
        return new CurrentVitals(hp, mp < 0 ? 0 : mp, stamina < 0 ? 0 : stamina);
    }

    public bool IsDepleted => Hp <= 0;
}
