namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     An injectable per-level base-value curve, looked up by character level. This is the channel by
///     which the EXP / level-base stat tables (parsed elsewhere from <c>userlevel.scr</c>) reach the
///     vital formula, replacing the previously hard-coded <c>0</c> placeholders.
///     spec: Docs/RE/formats/config_tables.md §2.4 (userlevel.scr — base stat values per level).
/// </summary>
/// <remarks>
///     <para>
///         <b>Wave-7 unblock.</b> The level-base and server-base terms in <see cref="VitalFormula" /> were
///         forced to <c>0</c> because the curve data lives client-side in <c>data/script/userlevel.scr</c>
///         and was not yet available (spec: Docs/RE/formats/config_tables.md §IMPORTANT — Architecture
///         unlock for Client.Domain). This struct provides the injection point: Assets.Parsers reads the
///         <c>.scr</c> and Application builds a curve, which is then passed into the formula inputs. The
///         Domain does NOT parse and does NOT embed any curve values — the byte layout of the stat block
///         (offset +2, 58 bytes) is still UNVERIFIED (spec: config_tables.md §2.4 / Known unknowns #2).
///     </para>
///     <para>
///         <b>Modeling choice (ours).</b> The curve is a flat, 1-based level lookup: index 1 holds the base
///         for level 1, etc. A level below 1 or above the highest tabulated level clamps to the nearest
///         end (the legacy loader validates a strictly sequential 1..N level index, so the table is dense).
///         An empty or default curve returns <c>0</c> for every level, preserving the previous behaviour so
///         existing call sites and tests are unaffected.
///     </para>
/// </remarks>
public readonly struct StatBaseCurve
{
    private readonly IReadOnlyList<long>? _baseByLevel;

    /// <summary>
    ///     Constructs a curve from a 1-based, dense list of per-level base values where
    ///     <c>baseByLevel[0]</c> is the base for level 1, <c>baseByLevel[1]</c> for level 2, and so on.
    ///     A <c>null</c> or empty list produces the identity (all-zero) curve.
    /// </summary>
    /// <param name="baseByLevel">Per-level base values, ordered from level 1 upward.</param>
    public StatBaseCurve(IReadOnlyList<long>? baseByLevel)
    {
        _baseByLevel = baseByLevel is { Count: > 0 } ? baseByLevel : null;
    }

    /// <summary>
    ///     The all-zero curve. Looking up any level returns <c>0</c>, exactly reproducing the previous
    ///     hard-coded behaviour. Used as the default when no <c>userlevel.scr</c> curve is injected.
    /// </summary>
    public static StatBaseCurve Empty => default;

    /// <summary>True when no curve data is present (every lookup yields 0).</summary>
    public bool IsEmpty => _baseByLevel is null;

    /// <summary>The number of tabulated levels (0 when empty).</summary>
    public int Count => _baseByLevel?.Count ?? 0;

    /// <summary>
    ///     Returns the base value for <paramref name="level" /> (1-based). Levels at or below 0 clamp to
    ///     the first entry; levels above the table clamp to the last entry; an empty curve returns 0.
    ///     Pure and deterministic. spec: Docs/RE/formats/config_tables.md §2.4.
    /// </summary>
    public long BaseForLevel(int level)
    {
        var table = _baseByLevel;
        if (table is null) return 0L;

        // 1-based level -> 0-based index, clamped into [0, Count - 1].
        var index = level - 1;
        if (index < 0)
            index = 0;
        else if (index >= table.Count) index = table.Count - 1;

        return table[index];
    }
}