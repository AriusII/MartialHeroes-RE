using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
///     Implements <see cref="IStatCatalogueSource" /> from the <c>userlevel.scr</c> data supplied by a
///     <see cref="VfsCatalogueLoader" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>What userlevel.scr actually stores.</b>
///         The file stores per-level <em>scaling coefficients</em>, not raw HP/MP values.
///         spec: Docs/RE/formats/config_tables.md §2.4 — "Important semantic note: this file does NOT
///         store per-stat base values … It stores per-level scaling coefficients."
///     </para>
///     <para>
///         <b>Why the default returns the empty (0-base) curve.</b>
///         The real per-level HP/MP base computation is NOT yet recoverable: the mapping of the four
///         positive-scale float positions to named stats (HP, MP, STR, …) and the actual <c>(10/A)×B</c>
///         → HP/MP formula are UNVERIFIED (spec: Docs/RE/formats/config_tables.md §2.4 open questions #1/#2).
///         Emitting any non-zero magnitude would be a fabricated curve with no spec provenance that
///         numerically diverges from the original's real per-level growth. So the default catalogue returns
///         <see cref="StatBaseCurve.Empty" /> (0 base) — structurally correct, numerically incomplete — which
///         matches the <see cref="VitalFormula" /> contract that leaves the level-base term at its provisional
///         0 until the curve is pinned.
///         spec: Docs/RE/formats/config_tables.md §2.4 (HP/MP base computation + float-position→stat mapping
///         UNVERIFIED); §IMPORTANT (level-base term provisionally 0 until userlevel.scr is decoded).
///     </para>
/// </remarks>
public sealed class ScrStatCatalogue : IStatCatalogueSource
{
    private readonly StatBaseCurve _hpCurve;
    private readonly StatBaseCurve _mpCurve;

    /// <summary>
    ///     Constructs the catalogue from pre-loaded userlevel.scr entries.
    ///     <para>
    ///         Both curves are <see cref="StatBaseCurve.Empty" /> (0 base): the real per-level HP/MP base
    ///         formula is UNVERIFIED (the float-position→stat mapping and the (10/A)×B → HP/MP derivation are
    ///         open), so no magnitude is invented. Rendering 0 is faithful-empty until the curve is pinned.
    ///         spec: Docs/RE/formats/config_tables.md §2.4 (open questions #1/#2 UNVERIFIED).
    ///     </para>
    /// </summary>
    /// <param name="entries">
    ///     Records from <c>data/script/userlevel.scr</c>, as returned by
    ///     <see cref="ConfigTableParser.ParseUserLevelScr" />.
    ///     spec: Docs/RE/formats/config_tables.md §2.4.
    /// </param>
    public ScrStatCatalogue(LevelBaseEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // FIDELITY: the real HP/MP base curve is UNVERIFIED — return the empty (0-base) curve rather
        // than a fabricated magnitude. spec: config_tables.md §2.4 (open questions #1/#2 UNVERIFIED).
        _hpCurve = StatBaseCurve.Empty;
        _mpCurve = StatBaseCurve.Empty;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — HP base curve.
    ///     Default = empty (0-base); real curve UNVERIFIED (float-position→stat mapping open).
    /// </remarks>
    public StatBaseCurve GetHpBaseCurve()
    {
        return _hpCurve;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — MP base curve.
    ///     Default = empty (0-base); real curve UNVERIFIED (float-position→stat mapping open).
    /// </remarks>
    public StatBaseCurve GetMpBaseCurve()
    {
        return _mpCurve;
    }

    /// <summary>
    ///     Creates a <see cref="ScrStatCatalogue" /> by loading <c>userlevel.scr</c> from the given
    ///     <see cref="VfsCatalogueLoader" />. Always uses the fidelity-correct default (empty 0-base curve)
    ///     because the real per-level HP/MP formula is UNVERIFIED.
    ///     spec: Docs/RE/formats/config_tables.md §2.4 (open questions #1/#2).
    /// </summary>
    public static ScrStatCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var entries = loader.LoadUserLevelScr();
        return new ScrStatCatalogue(entries);
    }
}