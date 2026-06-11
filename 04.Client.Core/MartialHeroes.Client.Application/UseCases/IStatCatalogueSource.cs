using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Port supplying the Domain with the per-level stat base curves parsed from the client-side
/// catalogues (<c>userlevel.scr</c> and friends). The concrete adapter lives at the composition root
/// (Godot / Infrastructure) and parses the <c>.scr</c> via Assets.Parsers; this layer must not
/// reference Assets.* (DAG), so the dependency is inverted through this interface. spec:
/// Docs/RE/formats/config_tables.md §IMPORTANT (stat curves are client-side) / §2.4 (userlevel.scr).
/// </summary>
/// <remarks>
/// <para>
/// The curves replace the hard-coded <c>0</c> bases the Domain was forced to use before the catalogue
/// data was available (spec: config_tables.md §IMPORTANT — "all values … hard-coded as 0 … can be
/// recovered"). They are looked up by character level and fed into <see cref="VitalFormulaInputs"/>'s
/// <see cref="VitalFormulaInputs.LevelBaseHpCurve"/> / <see cref="VitalFormulaInputs.LevelBaseMpCurve"/>.
/// </para>
/// <para>
/// When no catalogue is wired (no adapter / no sample data), an implementation returns
/// <see cref="StatBaseCurve.Empty"/>, preserving the prior all-zero behaviour. The exact byte layout
/// of the stat block (offset +2, 58 bytes) is still UNVERIFIED (spec: config_tables.md §2.4 / Known
/// unknowns #2); the Domain never parses it — Application receives the already-built curve.
/// </para>
/// </remarks>
public interface IStatCatalogueSource
{
    /// <summary>
    /// The per-level HP base curve (from <c>userlevel.scr</c>). Returns
    /// <see cref="StatBaseCurve.Empty"/> when no catalogue is available. spec:
    /// Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    StatBaseCurve GetHpBaseCurve();

    /// <summary>
    /// The per-level MP base curve (from <c>userlevel.scr</c>). Returns
    /// <see cref="StatBaseCurve.Empty"/> when no catalogue is available. spec:
    /// Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    StatBaseCurve GetMpBaseCurve();
}

/// <summary>
/// A null-object <see cref="IStatCatalogueSource"/> that always yields the empty (all-zero) curves.
/// The default when no catalogue adapter is wired; preserves the prior hard-coded behaviour. spec:
/// Docs/RE/formats/config_tables.md §2.4 (empty curve = previous 0 bases).
/// </summary>
public sealed class EmptyStatCatalogueSource : IStatCatalogueSource
{
    /// <summary>The shared singleton instance.</summary>
    public static readonly EmptyStatCatalogueSource Instance = new();

    private EmptyStatCatalogueSource()
    {
    }

    /// <inheritdoc />
    public StatBaseCurve GetHpBaseCurve() => StatBaseCurve.Empty;

    /// <inheritdoc />
    public StatBaseCurve GetMpBaseCurve() => StatBaseCurve.Empty;
}