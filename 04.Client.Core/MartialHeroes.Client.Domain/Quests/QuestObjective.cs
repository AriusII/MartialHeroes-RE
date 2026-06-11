namespace MartialHeroes.Client.Domain.Quests;

/// <summary>
/// The kind of a quest objective.
/// spec: Docs/RE/specs/quests.md §5.1 (objective lines / counter) / §8.1 (step list).
/// </summary>
/// <remarks>
/// <b>Modeling choice (ours).</b> The internal layout of the per-step quest records is <c>UNVERIFIED</c>
/// (the <c>quests.scr</c> record is mostly undecoded — only the step-list handle at +72 and the title id
/// are proven; reward / objective-target fields are unknown). The objective <em>kinds</em> here
/// (kill / collect / talk) are the common MMORPG objective types and are <b>our</b> modelling vocabulary
/// for injected quest data, not values read from the binary. spec: quests.md §8 / §13 #8.
/// </remarks>
public enum QuestObjectiveKind : byte
{
    /// <summary>Kill a number of a target (mob). spec: quests.md §5.1 (objective counter); kind is our modelling.</summary>
    Kill = 0,

    /// <summary>Collect a number of an item. spec: quests.md §5.1; kind is our modelling.</summary>
    Collect = 1,

    /// <summary>Talk to a target NPC. spec: quests.md §3 (NPC interaction); kind is our modelling.</summary>
    Talk = 2,
}

/// <summary>
/// One quest objective with a target id, a required count and a current progress counter. The dialog
/// surfaces an in-progress objective as a 1-based "current / total" counter.
/// spec: Docs/RE/specs/quests.md §5.1.
/// </summary>
/// <remarks>
/// The objective <em>data</em> (kind, target id, required count) is injected quest data — the Domain
/// does not parse <c>quests.scr</c>. The counter math is pure. spec: quests.md §5.1 / §8.1.
/// </remarks>
public readonly record struct QuestObjective
{
    /// <summary>The objective kind. spec: quests.md §5.1 (our modelling vocabulary).</summary>
    public QuestObjectiveKind Kind { get; init; }

    /// <summary>The target id (mob / item / NPC) the count is against (injected). spec: quests.md §8.1.</summary>
    public uint TargetId { get; init; }

    /// <summary>The required count to satisfy the objective (injected). spec: quests.md §5.1 (total).</summary>
    public int RequiredCount { get; init; }

    /// <summary>The current progress count (0..<see cref="RequiredCount"/>). spec: quests.md §5.1 (current).</summary>
    public int CurrentCount { get; init; }

    /// <summary>True when the current count has reached the required count. spec: quests.md §5.1.</summary>
    public bool IsComplete => CurrentCount >= RequiredCount;

    /// <summary>
    /// Advances the objective for a matching <paramref name="targetId"/> by <paramref name="amount"/>,
    /// saturating at <see cref="RequiredCount"/>. A non-matching target leaves it unchanged.
    /// spec: Docs/RE/specs/quests.md §5.1.
    /// </summary>
    public QuestObjective Advance(uint targetId, int amount = 1)
    {
        if (amount <= 0 || targetId != TargetId || IsComplete)
        {
            return this;
        }

        int next = CurrentCount + amount;
        if (next > RequiredCount)
        {
            next = RequiredCount;
        }

        return this with { CurrentCount = next };
    }
}
