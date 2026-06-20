using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
///     Builds the <see cref="GamePacketHandler.VitalsResolver" /> seam from an
///     <see cref="IStatCatalogueSource" />, so the per-level HP/MP base curves parsed from
///     <c>userlevel.scr</c> feed the Domain vital formula instead of the hard-coded <c>0</c> bases.
///     spec: Docs/RE/formats/config_tables.md §2.4 (userlevel.scr — base stat values per level) /
///     §IMPORTANT (stat curves are client-side and recoverable).
/// </summary>
/// <remarks>
///     <para>
///         This is the wiring point the task calls for: it replaces the resolver's empty curves with the real
///         injected ones when a catalogue is available. The resolver stays an orchestration shim — it builds
///         <see cref="VitalFormulaInputs" /> and calls the Domain <see cref="VitalStats.FromFormula" />; the
///         formula itself lives in Domain.
///     </para>
///     <para>
///         The level threaded into <see cref="VitalFormulaInputs.Level" /> is the spawn's reported level, which
///         the curve uses as its 1-based lookup index. When the catalogue is <see cref="EmptyStatCatalogueSource" />
///         the curves are empty and the result is identical to the previous all-zero-base behaviour.
///     </para>
/// </remarks>
public static class CatalogueVitalsResolver
{
    /// <summary>
    ///     Produces a <see cref="GamePacketHandler.VitalsResolver" /> delegate that looks up the per-level
    ///     HP/MP base curves from <paramref name="catalogue" /> and runs the Domain vital formula.
    ///     Assign the returned delegate to <see cref="GamePacketHandler.VitalsResolver" /> at the
    ///     composition root. spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public static Func<SpawnInfo, VitalStats> Create(IStatCatalogueSource catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        // Snapshot the curves once; they are immutable value lookups over the parsed table.
        var hpCurve = catalogue.GetHpBaseCurve();
        var mpCurve = catalogue.GetMpBaseCurve();

        return info =>
        {
            var inputs = VitalFormulaInputs.Empty with
            {
                // Raw class-id byte indexing the per-class HP table; mapping UNVERIFIED (spec: stats.md).
                ClassId = unchecked((byte)info.ServerClass),
                // Wave-7 unblock: real per-level bases replace the hard-coded 0 when a curve is wired.
                // spec: Docs/RE/formats/config_tables.md §2.4.
                Level = info.Level,
                LevelBaseHpCurve = hpCurve,
                LevelBaseMpCurve = mpCurve
            };

            var formula = VitalStats.FromFormula(in inputs, info.CurrentStamina);

            // Server-authoritative current-value guard: never clamp the reported current below what the
            // server sent (the provisional bases may still under-shoot). spec: stats.md ("server enforces
            // the cap"). This guard relaxes as the full stat block / equipment is decoded.
            return new VitalStats(
                Math.Max(formula.MaxHp, info.CurrentHp),
                Math.Max(formula.MaxMp, info.CurrentMp),
                Math.Max(formula.MaxStamina, info.CurrentStamina));
        };
    }
}