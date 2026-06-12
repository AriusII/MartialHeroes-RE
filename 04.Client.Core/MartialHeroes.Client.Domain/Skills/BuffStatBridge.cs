using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// Bridges active status entries into <see cref="BuffContribution"/> rows for
/// <see cref="StatAggregation"/>: a buff that carries a stat key contributes its magnitude to that
/// stat while active. spec: Docs/RE/specs/combat.md §2 / §2.2 (buffs are stat contributions) and
/// Docs/RE/specs/skills.md §6 (the buff/aura table is the source of the per-stat buff terms).
/// </summary>
/// <remarks>
/// <para>
/// The combat aggregation reads its per-stat and shared all-stats buff terms from the buff/aura table
/// (combat.md §2.2 keys 70..74, 93). This bridge produces those <see cref="BuffContribution"/> rows
/// from a buff source so a caller can feed them straight into
/// <see cref="StatAggregation.Aggregate"/> / <see cref="StatAggregation.AggregatePrimaryStats"/>.
/// </para>
/// <para>
/// <b>Modeling note (ours).</b> The §6 status table keys slots by effect code, while the combat
/// aggregation keys buffs by <see cref="StatKey"/>. The mapping from a status slot to a stat key is
/// data the server/skills.scr supplies (open question 3 — the icon-only code→meaning table is not in
/// the client). So this bridge takes the (stat key, magnitude) pairing as input rather than inventing
/// a code→stat map: callers pair an active buff slot with the stat it grants. The bridge only enforces
/// the "active while duration &gt; 0" rule. spec: skills.md §6.2 / combat.md §2.2.
/// </para>
/// </remarks>
public static class BuffStatBridge
{
    /// <summary>
    /// Produces a <see cref="BuffContribution"/> from an active buff: the buff contributes
    /// <paramref name="value"/> to <paramref name="statKey"/>. An inactive (expired/cleared) buff
    /// contributes nothing. spec: combat.md §2.2 / skills.md §6.3.
    /// </summary>
    /// <returns>The contribution, or <c>null</c> when the buff is not active.</returns>
    public static BuffContribution? ToContribution(in BuffDebuff buff, StatKey statKey, int value) =>
        buff.IsActive ? new BuffContribution(statKey, value) : null;

    /// <summary>
    /// Maps each active (slot, stat-key) pairing in <paramref name="source"/> into the
    /// <paramref name="destination"/> contribution span, returning how many were written. Inactive
    /// pairings are skipped. The caller owns the destination buffer (no allocation here).
    /// spec: combat.md §2.2 / skills.md §6.3.
    /// </summary>
    /// <param name="source">Active-buff candidates: each pairs a buff with the stat it grants.</param>
    /// <param name="destination">Caller-owned output span sized to hold up to <paramref name="source"/>'s length.</param>
    /// <returns>The number of contributions written to <paramref name="destination"/>.</returns>
    public static int BuildContributions(
        ReadOnlySpan<BuffStatGrant> source,
        Span<BuffContribution> destination)
    {
        int written = 0;
        for (int i = 0; i < source.Length; i++)
        {
            BuffStatGrant grant = source[i];
            if (!grant.Buff.IsActive)
            {
                continue;
            }

            if (written >= destination.Length)
            {
                throw new ArgumentException(
                    "Destination span is too small for the active contributions.", nameof(destination));
            }

            destination[written++] = new BuffContribution(grant.StatKey, grant.Value);
        }

        return written;
    }
}

/// <summary>
/// Pairs a status slot with the stat key + value it grants, so <see cref="BuffStatBridge"/> can emit a
/// <see cref="BuffContribution"/> while the buff is active. The stat-key binding is data supplied by
/// the caller (skills.scr / server), not invented here. spec: skills.md §6.2 / combat.md §2.2.
/// </summary>
public readonly record struct BuffStatGrant(BuffDebuff Buff, StatKey StatKey, int Value);