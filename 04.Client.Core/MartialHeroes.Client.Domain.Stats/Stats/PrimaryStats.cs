namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct PrimaryStats(int Str, int Dex, int Agi, int Con, int Int)
{
    public static readonly PrimaryStats Zero = new(0, 0, 0, 0, 0);
}