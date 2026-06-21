namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     The recovered per-class HP growth multiplier table, indexed by the local player's class-id byte.
///     spec: Docs/RE/structs/stats.md ("Per-class HP growth table").
/// </summary>
/// <remarks>
///     <para>
///         The table has 5 entries (indices 0–4), implying at most 4 in-game classes plus a sentinel:
///         <c>[0.0, 0.3, 0.2, 0.15, 0.1]</c> (spec: stats.md). Index 0 is a sentinel ("no class assigned")
///         that yields a 0.0 multiplier; it is an out-of-range guard, not a real class.
///     </para>
///     <para>
///         <b>UNVERIFIED (spec).</b> Whether the wire class-id byte maps directly onto
///         <see cref="Shared.Kernel.Enums.CharacterClass" /> or is offset by one is unconfirmed; the spec
///         asks to confirm against a capture before binding class ids to the table. We therefore index the
///         table by a raw <c>byte</c> class id rather than the domain enum, and out-of-range ids fall back
///         to the sentinel 0.0 multiplier rather than throwing.
///     </para>
/// </remarks>
public static class ClassHpTable
{
    /// <summary>
    ///     The raw growth multipliers by class id. spec: Docs/RE/structs/stats.md
    ///     (CLASS_HP_TABLE = [0.0, 0.3, 0.2, 0.15, 0.1]).
    /// </summary>
    private static readonly double[] Multipliers = [0.0, 0.3, 0.2, 0.15, 0.1];

    /// <summary>Number of entries in the table (sentinel + classes).</summary>
    public static int Length => Multipliers.Length;

    /// <summary>
    ///     Returns the HP growth multiplier for <paramref name="classId" />. Out-of-range ids return the
    ///     sentinel <c>0.0</c> (spec: class id 0 is a guard; ids beyond the table are treated likewise).
    /// </summary>
    public static double MultiplierFor(byte classId)
    {
        return classId < Multipliers.Length ? Multipliers[classId] : 0.0;
    }
}