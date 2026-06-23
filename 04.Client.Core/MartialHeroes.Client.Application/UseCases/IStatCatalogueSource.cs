using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Application.UseCases;

public interface IStatCatalogueSource
{
    StatBaseCurve GetHpBaseCurve();

    StatBaseCurve GetMpBaseCurve();
}