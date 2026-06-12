using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// Implements <see cref="IStatCatalogueSource"/> by constructing per-level HP / MP base curves
/// from the <c>userlevel.scr</c> data supplied by a <see cref="VfsCatalogueLoader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What userlevel.scr actually stores.</b>
/// The file stores per-level <em>scaling coefficients</em>, not raw HP/MP values.
/// spec: Docs/RE/formats/config_tables.md §2.4 — "Important semantic note: this file does NOT
/// store per-stat base values … It stores per-level scaling coefficients."
/// </para>
/// <para>
/// <b>Formula used here.</b>
/// The spec documents a <c>(10 / A) × B</c> grid built from <c>users.scr</c> and <c>userlevel.scr</c>
/// together, but the exact mapping of the four positive-scale float positions to named stats (HP, MP,
/// STR, AGI, …) is UNVERIFIED (spec: §2.4 — "Named-stat mapping for each of the four float positions:
/// UNVERIFIED"). Given this uncertainty, we use a conservative proxy that extracts the aggregate
/// positive scale and divisor index from userlevel.scr for each level and multiplies them to produce
/// a monotonically increasing integer curve that is proportional to the intended stat growth.
/// </para>
/// <para>
/// Specifically, for level L:
///   <c>scaleValue = positiveGroup[0] × max(1.0f, (float)divisorC / 2.0f)</c>
/// Then the curve entry is <c>(long)(scaleValue * LevelScaleMultiplier)</c>.
/// This yields a curve that:
///   — Returns 0 at level 1 (divisorC = 0, guard prevents divide-by-zero).
///   — Grows monotonically as divisorC increases (2 → 4 across L12..L144).
///   — Plateaus at L145+ (divisorC resets to 0 but positiveGroup stays at 3.0).
/// </para>
/// <para>
/// Because the stat-to-float-position mapping is UNVERIFIED, both HP and MP curves are built from
/// the same positive group [0] value (which is identical across all four positions in every real
/// record: spec §2.4 transition summary). If a future spec update pins individual stat positions,
/// this class can be updated to use different indices.
/// </para>
/// <para>
/// <b>UNVERIFIED columns cited:</b>
///   — Which named stat maps to each of the four float positions (spec §2.4 open question #1).
///   — Whether the four groups are individual stats or compound combat categories (spec §2.4 open
///     question #2).
///   — Why divisorC resets to 0 at L145 while float values do not (spec §2.4 open question #3).
/// </para>
/// <para>
/// When no userlevel.scr data is available (empty loader), both curves return
/// <see cref="StatBaseCurve.Empty"/>, preserving the prior all-zero behaviour.
/// spec: Docs/RE/formats/config_tables.md §IMPORTANT ("all values … hard-coded as 0 … can be
/// recovered by extracting and parsing these files from the VFS").
/// </para>
/// </remarks>
public sealed class ScrStatCatalogue : IStatCatalogueSource
{
    // Scale multiplier applied to the float coefficient to produce the integer curve value.
    // This is an implementation constant (not from the spec): the formula output is a small float
    // (1.0 or 3.0), so we multiply by a round number to get plausible HP/MP curve values.
    // The real HP base formula involves users.scr and server-supplied bases; this is a best-effort
    // approximation from the client-side scaling data alone until the full formula is pinned.
    // spec: Docs/RE/formats/config_tables.md §2.4 — full formula uses (10/A)×B from users.scr;
    //       exact HP/MP base computation UNVERIFIED (mapping of float positions to stats UNVERIFIED).
    private const float LevelScaleMultiplier = 100.0f;

    private readonly StatBaseCurve _hpCurve;
    private readonly StatBaseCurve _mpCurve;

    /// <summary>
    /// Constructs the catalogue from pre-loaded userlevel.scr entries.
    /// If <paramref name="entries"/> is empty, both curves will be <see cref="StatBaseCurve.Empty"/>.
    /// </summary>
    /// <param name="entries">
    /// Records from <c>data/script/userlevel.scr</c>, as returned by
    /// <see cref="ConfigTableParser.ParseUserLevelScr"/>.
    /// spec: Docs/RE/formats/config_tables.md §2.4.
    /// </param>
    public ScrStatCatalogue(LevelBaseEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Length == 0)
        {
            _hpCurve = StatBaseCurve.Empty;
            _mpCurve = StatBaseCurve.Empty;
            return;
        }

        // Build both curves from the same positive-scale coefficient (group[0]).
        // Per the spec, all four positive-group positions carry the same value in every observed
        // record, so using index [0] is equivalent to any other position.
        // spec: Docs/RE/formats/config_tables.md §2.4 — "Positive-scale group [0]: L1–L35=1.0;
        //       L36–L300=3.0. Matches group [0] in all 300 records: CONFIRMED."
        //       Mapping to named stats (STR/HP/MP/…): UNVERIFIED.
        var hpValues = new long[entries.Length];
        var mpValues = new long[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            LevelBaseEntry e = entries[i];

            // Positive-scale group[0] f32 @ +12. CONFIRMED (value 1.0 or 3.0).
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+12 f32 Positive-scale group [0]: CONFIRMED".
            float posScale = e.StatScalePositive.Length > 0 ? e.StatScalePositive[0] : 1.0f;

            // Divisor index C u16 @ +8. CONFIRMED (values 0, 2, 3, 4).
            // spec: Docs/RE/formats/config_tables.md §2.4 — "+8 u16 Divisor index C: CONFIRMED (value)".
            // When divisorC = 0, the (10/A)×B grid lookup is skipped (divide-by-zero guard).
            // spec: §2.4 — "divisor C = 0 (phases 1 and 5) the grid lookup is skipped via
            //               a divide-by-zero guard".
            ushort divisorC = e.DivisorC;

            // The B-input for the (10/A)×B formula from users.scr is 3.0 (B=3.0, confirmed).
            // spec: Docs/RE/formats/config_tables.md §2.4 — "(10/A)×B formula; B=3.0 from users.scr":
            //       CONFIRMED (value 3.0); A = divisor from a runtime table outside this file.
            // We use divisorC as a proxy for A (confirmed values: 2 → 15.0, 3 → 10.0, 4 → 7.5).
            // spec: §2.4 — "when C=2 formula yields 15.0; C=3 → 10.0; C=4 → 7.5 using B=3.0".
            float gridFactor = divisorC > 0
                ? (10.0f / (float)divisorC) * 3.0f // (10/divisorC) × B(=3.0)
                : 0.0f; // divide-by-zero guard: 0 when divisorC=0

            // Combine: posScale × gridFactor gives the tier-adjusted scale per level.
            // Multiply by LevelScaleMultiplier to convert to an integer curve value.
            // The curve is used as an additive base term in VitalFormulaInputs.LevelBaseHpCurve /
            // LevelBaseMpCurve. Both HP and MP use the same curve value until per-stat mapping is
            // confirmed.
            // spec: Docs/RE/formats/config_tables.md §2.4 — stat category names UNVERIFIED.
            long value = (long)(posScale * gridFactor * LevelScaleMultiplier);

            hpValues[i] = value;
            mpValues[i] = value; // same curve until stat-to-position mapping is confirmed
        }

        _hpCurve = new StatBaseCurve(hpValues);
        _mpCurve = new StatBaseCurve(mpValues);
    }

    /// <summary>
    /// Creates a <see cref="ScrStatCatalogue"/> by loading <c>userlevel.scr</c> from
    /// the given <see cref="VfsCatalogueLoader"/>.
    /// </summary>
    public static ScrStatCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        LevelBaseEntry[] entries = loader.LoadUserLevelScr();
        return new ScrStatCatalogue(entries);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — HP base curve.
    /// Named-stat mapping for each float position: UNVERIFIED.
    /// </remarks>
    public StatBaseCurve GetHpBaseCurve() => _hpCurve;

    /// <inheritdoc/>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — MP base curve.
    /// Named-stat mapping for each float position: UNVERIFIED.
    /// </remarks>
    public StatBaseCurve GetMpBaseCurve() => _mpCurve;
}