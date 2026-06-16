using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// Implements <see cref="IStatCatalogueSource"/> from the <c>userlevel.scr</c> data supplied by a
/// <see cref="VfsCatalogueLoader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What userlevel.scr actually stores.</b>
/// The file stores per-level <em>scaling coefficients</em>, not raw HP/MP values.
/// spec: Docs/RE/formats/config_tables.md §2.4 — "Important semantic note: this file does NOT
/// store per-stat base values … It stores per-level scaling coefficients."
/// </para>
/// <para>
/// <b>Why the default returns the empty (0-base) curve.</b>
/// The real per-level HP/MP base computation is NOT yet recoverable: the mapping of the four
/// positive-scale float positions to named stats (HP, MP, STR, …) and the actual <c>(10/A)×B</c>
/// → HP/MP formula are UNVERIFIED (spec: Docs/RE/formats/config_tables.md §2.4 open questions #1/#2).
/// Emitting any non-zero magnitude would be a fabricated curve with no spec provenance that
/// numerically diverges from the original's real per-level growth. So the default catalogue returns
/// <see cref="StatBaseCurve.Empty"/> (0 base) — structurally correct, numerically incomplete — which
/// matches the <see cref="VitalFormula"/> contract that leaves the level-base term at its provisional
/// 0 until the curve is pinned.
/// spec: Docs/RE/formats/config_tables.md §2.4 (HP/MP base computation + float-position→stat mapping
///       UNVERIFIED); §IMPORTANT (level-base term provisionally 0 until userlevel.scr is decoded).
/// </para>
/// <para>
/// <b>Provisional proxy (opt-in only).</b> A monotonic placeholder curve can be produced for
/// non-fidelity / debugging purposes by passing <c>useProvisionalCurve: true</c>. It multiplies the
/// confirmed positive-scale coefficient and the divisor-index grid factor by an
/// implementation-chosen scale (<see cref="ProvisionalLevelScaleMultiplier"/>). That scale has <b>no
/// spec basis</b> and the proxy is therefore NEVER the default fidelity path; it must not be wired
/// into the shipping client. When §2.4 open question #1 is answered, both the per-stat float-position
/// selection and this scale must be replaced together with the real formula.
/// spec: Docs/RE/formats/config_tables.md §2.4 (the constant itself has no spec).
/// </para>
/// </remarks>
public sealed class ScrStatCatalogue : IStatCatalogueSource
{
    // Provisional-proxy scale. NO SPEC BASIS — used only when the opt-in provisional curve is
    // explicitly requested (never by default). The real HP/MP base formula is UNVERIFIED; this value
    // is not bound to anything in the binary or any Docs/RE spec.
    // spec: Docs/RE/formats/config_tables.md §2.4 — (10/A)×B from users.scr; HP/MP base computation and
    //       float-position→stat mapping UNVERIFIED (open questions #1/#2). The constant has no spec.
    private const float ProvisionalLevelScaleMultiplier = 100.0f;

    private readonly StatBaseCurve _hpCurve;
    private readonly StatBaseCurve _mpCurve;

    /// <summary>
    /// Constructs the catalogue from pre-loaded userlevel.scr entries.
    /// </summary>
    /// <param name="entries">
    /// Records from <c>data/script/userlevel.scr</c>, as returned by
    /// <see cref="ConfigTableParser.ParseUserLevelScr"/>.
    /// spec: Docs/RE/formats/config_tables.md §2.4.
    /// </param>
    /// <param name="useProvisionalCurve">
    /// When <see langword="false"/> (the default and only fidelity-correct mode) both curves are
    /// <see cref="StatBaseCurve.Empty"/> — the real per-level HP/MP base formula is UNVERIFIED so no
    /// magnitude is invented (spec: config_tables.md §2.4 open questions #1/#2). When
    /// <see langword="true"/> a no-spec-basis monotonic placeholder is built for debugging/tooling only.
    /// </param>
    public ScrStatCatalogue(LevelBaseEntry[] entries, bool useProvisionalCurve = false)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Length == 0 || !useProvisionalCurve)
        {
            // FIDELITY DEFAULT: the real HP/MP base curve is UNVERIFIED — return the empty (0-base)
            // curve rather than a fabricated magnitude. spec: config_tables.md §2.4 (#1/#2 UNVERIFIED).
            _hpCurve = StatBaseCurve.Empty;
            _mpCurve = StatBaseCurve.Empty;
            return;
        }

        // ── OPT-IN PROVISIONAL PROXY (no spec basis; debugging/tooling only) ──────────────────────
        // Build both curves from the same positive-scale coefficient (group[0]).
        // spec: Docs/RE/formats/config_tables.md §2.4 — "Positive-scale group [0]: L1–L35=1.0;
        //       L36–L300=3.0": CONFIRMED (value). Mapping to named stats (STR/HP/MP/…): UNVERIFIED.
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
            ushort divisorC = e.DivisorC;

            // (10/divisorC) × B(=3.0); divide-by-zero guard yields 0 when divisorC=0.
            // spec: Docs/RE/formats/config_tables.md §2.4 — "(10/A)×B formula; B=3.0 from users.scr";
            //       "divisor C = 0 (phases 1 and 5) the grid lookup is skipped via a divide-by-zero guard".
            float gridFactor = divisorC > 0
                ? (10.0f / (float)divisorC) * 3.0f
                : 0.0f;

            long value = (long)(posScale * gridFactor * ProvisionalLevelScaleMultiplier);
            hpValues[i] = value;
            mpValues[i] = value; // proxy: same curve until stat-to-position mapping is confirmed
        }

        _hpCurve = new StatBaseCurve(hpValues);
        _mpCurve = new StatBaseCurve(mpValues);
    }

    /// <summary>
    /// Creates a <see cref="ScrStatCatalogue"/> by loading <c>userlevel.scr</c> from the given
    /// <see cref="VfsCatalogueLoader"/>. Always uses the fidelity-correct default (empty 0-base curve)
    /// because the real per-level HP/MP formula is UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.4 (open questions #1/#2).
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
    /// Default = empty (0-base); real curve UNVERIFIED (float-position→stat mapping open).
    /// </remarks>
    public StatBaseCurve GetHpBaseCurve() => _hpCurve;

    /// <inheritdoc/>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr — MP base curve.
    /// Default = empty (0-base); real curve UNVERIFIED (float-position→stat mapping open).
    /// </remarks>
    public StatBaseCurve GetMpBaseCurve() => _mpCurve;
}