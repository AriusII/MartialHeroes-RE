namespace MartialHeroes.Client.Domain.Progression.Progression;

/// <summary>
///     The local player's experience accumulators — the two 64-bit running totals the <c>5/9 ExpGain</c>
///     channel advances. Pure, deterministic, engine-free value type; every mutation returns a new state.
///     spec: Docs/RE/specs/progression.md §3 (current-XP + lifetime-XP 64-bit accumulators, add-with-carry)
///     / §3.1 (the server XP percentage-bonus display split) / §11.
/// </summary>
/// <remarks>
///     <para>
///         The XP <em>amount</em> on the wire is a signed 64-bit value (<c>5/9</c> payload +16, spec §3.4);
///         it is added to BOTH the current-XP accumulator (the XP bar) and a separate lifetime-XP running
///         total. The legacy client performs a 64-bit add-with-carry; in managed code a single 64-bit add
///         is the faithful equivalent (the carry is implicit in the 64-bit register width).
///     </para>
///     <para>
///         <b>Server-authored magnitudes stay DATA.</b> The XP percentage-bonus <c>rate</c> used by the §3.1
///         display split is a server-set global — it is NOT a client constant (spec §12 Q6). It is supplied
///         by the caller; the Domain never invents it.
///     </para>
/// </remarks>
public readonly record struct ExperienceModel
{
    /// <summary>The current-XP accumulator (drives the XP bar). spec: progression.md §3.</summary>
    public long CurrentXp { get; init; }

    /// <summary>The lifetime-XP running total. spec: progression.md §3.</summary>
    public long LifetimeXp { get; init; }

    /// <summary>
    ///     Adds an experience <paramref name="amount" /> to BOTH the current-XP and lifetime-XP accumulators
    ///     (64-bit add-with-carry), per <c>5/9 ExpGain</c>. The full <paramref name="amount" /> is what is
    ///     added — the §3.1 base/bonus split is a display transform only and does not change what accumulates.
    ///     spec: Docs/RE/specs/progression.md §3 / §3.4.
    /// </summary>
    public ExperienceModel AddExperience(long amount)
    {
        return this with { CurrentXp = unchecked(CurrentXp + amount), LifetimeXp = unchecked(LifetimeXp + amount) };
    }

    /// <summary>
    ///     Overwrites the current-XP accumulator from an authoritative resync (<c>5/67 StatsUpdate</c> primes
    ///     the XP bar). Lifetime XP is untouched. spec: Docs/RE/specs/progression.md §6.
    /// </summary>
    public ExperienceModel ResyncCurrentXp(long currentXp)
    {
        return this with { CurrentXp = currentXp };
    }

    /// <summary>
    ///     The §3.1 server XP percentage-bonus display split: <c>shown_base = 100·amount/(rate+100)</c> and
    ///     <c>bonus = amount − shown_base</c>. This is a pure DISPLAY transformation — the floating text shows
    ///     <c>"&lt;base&gt; + &lt;bonus&gt;"</c>, while the full <paramref name="amount" /> still accumulates
    ///     (see <see cref="AddExperience" />). Active only when the wire source-mode byte equals 2 (the caller
    ///     gates that); the <paramref name="ratePercent" /> is server-authored DATA (spec §12 Q6), injected.
    ///     spec: Docs/RE/specs/progression.md §3.1.
    /// </summary>
    /// <param name="amount">The full XP amount (the value that accumulates).</param>
    /// <param name="ratePercent">
    ///     The server-set XP bonus rate, as a percentage. Negative rates are clamped
    ///     away so the denominator <c>(rate + 100)</c> stays positive; a non-positive denominator yields the
    ///     full amount as the base with no bonus.
    /// </param>
    public static (long ShownBase, long Bonus) SplitBonus(long amount, long ratePercent)
    {
        // spec: progression.md §3.1 — shown_base = 100 * amount / (rate + 100); bonus = amount - shown_base.
        var denominator = ratePercent + 100L;
        if (denominator <= 0L) return (amount, 0L);

        var shownBase = checked(100L * amount) / denominator;
        return (shownBase, amount - shownBase);
    }
}