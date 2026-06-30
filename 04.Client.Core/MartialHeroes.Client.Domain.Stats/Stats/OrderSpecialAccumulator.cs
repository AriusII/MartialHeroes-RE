namespace MartialHeroes.Client.Domain.Stats.Stats;

public struct OrderSpecialAccumulator
{
    public const int DiscriminatorAll = 5;
    private const double ContributionScale = 100.0;

    private double _bucket0;
    private double _bucket1;
    private double _bucket2;
    private double _bucket3;

    public readonly double Bucket0 => _bucket0;
    public readonly double Bucket1 => _bucket1;
    public readonly double Bucket2 => _bucket2;
    public readonly double Bucket3 => _bucket3;

    public void Add(int discriminator, int rawValue)
    {
        var contribution = rawValue / ContributionScale;

        switch (discriminator)
        {
            case 0:
                _bucket0 += contribution;
                break;
            case 1:
                _bucket1 += contribution;
                break;
            case 2:
                _bucket2 += contribution;
                break;
            case 3:
                _bucket3 += contribution;
                break;
            case DiscriminatorAll:
                _bucket0 += contribution;
                _bucket1 += contribution;
                _bucket2 += contribution;
                _bucket3 += contribution;
                break;
        }
    }
}
