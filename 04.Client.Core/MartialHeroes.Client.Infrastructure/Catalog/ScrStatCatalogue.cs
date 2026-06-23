using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class ScrStatCatalogue : IStatCatalogueSource
{
    private readonly StatBaseCurve _hpCurve;
    private readonly StatBaseCurve _mpCurve;

    public ScrStatCatalogue(LevelBaseEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _hpCurve = StatBaseCurve.Empty;
        _mpCurve = StatBaseCurve.Empty;
    }

    public StatBaseCurve GetHpBaseCurve()
    {
        return _hpCurve;
    }

    public StatBaseCurve GetMpBaseCurve()
    {
        return _mpCurve;
    }

    public static ScrStatCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var entries = loader.LoadUserLevelScr();
        return new ScrStatCatalogue(entries);
    }
}