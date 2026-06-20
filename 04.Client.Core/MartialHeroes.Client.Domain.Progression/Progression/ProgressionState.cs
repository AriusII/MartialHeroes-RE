namespace MartialHeroes.Client.Domain.Progression.Progression;

/// <summary>
///     The local player's progression aggregate — the experience accumulators (<see cref="ExperienceModel" />)
///     and the separate rank / honor XP channel (<see cref="RankXpModel" />). Pure, deterministic, engine-free;
///     every mutation returns a new state. This is the Domain authority the <c>5/9 ExpGain</c> and
///     <c>5/11 RankXpGain</c> handlers route into. spec: Docs/RE/specs/progression.md §3 / §4 / §11.
/// </summary>
/// <remarks>
///     All server-authored magnitudes (the XP / rank-XP percentage-bonus rates and the per-level rank-XP
///     divisor / cap tables) are DATA injected by the caller — they are not client constants
///     (spec §12 Q6). The Domain holds and advances state; it never invents those numbers.
/// </remarks>
public readonly record struct ProgressionState
{
    /// <summary>The character-XP accumulators (current-XP + lifetime-XP). spec: progression.md §3.</summary>
    public ExperienceModel Experience { get; init; }

    /// <summary>The rank / honor XP channel state. spec: progression.md §4.</summary>
    public RankXpModel RankXp { get; init; }

    /// <summary>
    ///     Applies a <c>5/9 ExpGain</c>: adds the full <paramref name="amount" /> to both XP accumulators.
    ///     spec: Docs/RE/specs/progression.md §3 / §3.4.
    /// </summary>
    public ProgressionState AddExperience(long amount)
    {
        return this with { Experience = Experience.AddExperience(amount) };
    }

    /// <summary>
    ///     Applies a <c>5/11 RankXpGain</c>: routes the gain through the rank-XP model (mode 2 = direct add;
    ///     else the per-level table routine, capped at 25). The per-level <paramref name="divisorTable" /> and
    ///     <paramref name="capTable" /> are server/config DATA supplied by the caller.
    ///     spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    /// <exception cref="LevelTableException">
    ///     When the divisor for <paramref name="levelCache" /> is 0. spec: progression.md
    ///     §4.
    /// </exception>
    public ProgressionState AddRankXp(
        long amount,
        byte mode,
        int levelCache,
        IReadOnlyList<long>? divisorTable,
        IReadOnlyList<long>? capTable)
    {
        return this with { RankXp = RankXp.ApplyRankGain(amount, mode, levelCache, divisorTable, capTable) };
    }
}