using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Application.UseCases;

public interface IStatCatalogueSource
{
    StatBaseCurve GetHpBaseCurve();

    StatBaseCurve GetMpBaseCurve();
}

public sealed class EmptyStatCatalogueSource : IStatCatalogueSource
{
    public static readonly EmptyStatCatalogueSource Instance = new();

    private EmptyStatCatalogueSource()
    {
    }

    public StatBaseCurve GetHpBaseCurve()
    {
        return StatBaseCurve.Empty;
    }

    public StatBaseCurve GetMpBaseCurve()
    {
        return StatBaseCurve.Empty;
    }
}