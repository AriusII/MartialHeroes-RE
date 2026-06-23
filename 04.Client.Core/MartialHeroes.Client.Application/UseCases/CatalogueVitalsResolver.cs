using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Application.UseCases;

public static class CatalogueVitalsResolver
{
    public static Func<SpawnInfo, VitalStats> Create(IStatCatalogueSource catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var hpCurve = catalogue.GetHpBaseCurve();
        var mpCurve = catalogue.GetMpBaseCurve();

        return info =>
        {
            var inputs = VitalFormulaInputs.Empty with
            {
                ClassId = unchecked((byte)info.ServerClass),
                Level = info.Level,
                LevelBaseHpCurve = hpCurve,
                LevelBaseMpCurve = mpCurve
            };

            var formula = VitalStats.FromFormula(in inputs, info.CurrentStamina);

            return new VitalStats(
                Math.Max(formula.MaxHp, info.CurrentHp),
                Math.Max(formula.MaxMp, info.CurrentMp),
                Math.Max(formula.MaxStamina, info.CurrentStamina));
        };
    }
}