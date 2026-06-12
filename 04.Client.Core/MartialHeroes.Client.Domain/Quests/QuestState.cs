namespace MartialHeroes.Client.Domain.Quests;

/// <summary>
/// The lifecycle phase of a single quest.
/// spec: Docs/RE/specs/quests.md §4.2 (accept / proceed / give-up) / §7 (completion verdict) / §11.
/// </summary>
public enum QuestPhase : byte
{
    /// <summary>Not accepted; available to accept. spec: quests.md §4.2 (accept).</summary>
    Available = 0,

    /// <summary>Accepted and in progress (objectives advancing). spec: quests.md §4.2 (proceed) / §5.1.</summary>
    InProgress = 1,

    /// <summary>Objectives done; the completion verdict was granted. spec: quests.md §7.2 (grant).</summary>
    Completed = 2,

    /// <summary>Abandoned (give-up) — the local active-quest state was cleared. spec: quests.md §4.2 (give-up) / §4.3.</summary>
    GivenUp = 3,

    /// <summary>Completion denied / failed by the verdict. spec: quests.md §7.2 (deny).</summary>
    Failed = 4,
}

/// <summary>
/// The C2S quest-action sub-action selector (2/28 body +0).
/// spec: Docs/RE/specs/quests.md §4.2.
/// </summary>
public enum QuestSubAction : byte
{
    /// <summary>Accept the offered quest. spec: quests.md §4.2 (value 2).</summary>
    Accept = 2,

    /// <summary>Proceed / continue an in-progress quest dialog. spec: quests.md §4.2 (value 3).</summary>
    Proceed = 3,

    /// <summary>Give up / abandon the active quest. spec: quests.md §4.2 (value 4).</summary>
    GiveUp = 4,
}

/// <summary>
/// The completion verdict reward state (5/73 +12).
/// spec: Docs/RE/specs/quests.md §7.2.
/// </summary>
public enum QuestRewardState : byte
{
    /// <summary>Grant — open the result panel, positive completion sound. spec: quests.md §7.2 (value 1).</summary>
    Grant = 1,

    /// <summary>Deny / fail — negative completion sound. spec: quests.md §7.2 (value 2).</summary>
    Deny = 2,
}

/// <summary>
/// The deterministic per-quest state machine: <c>available → in-progress → completed</c>, with give-up
/// and fail branches, the §4.3 accept gate, and objective-driven progression. Pure and engine-free.
/// spec: Docs/RE/specs/quests.md §4 / §5.1 / §7.
/// </summary>
/// <remarks>
/// <para>
/// Immutable value type; every transition returns a new state. Each (phase, action) is total —
/// out-of-phase actions are rejected (state unchanged). Quest <em>data</em> (id, objectives) is
/// injected; the Domain does not parse <c>quests.scr</c>. spec: quests.md §8 / §11.
/// </para>
/// <para>
/// <b>Accept gate threshold 26.</b> On accept the client compares a runtime status field against 26 and
/// blocks below it. Whether the gated quantity is character level or a cash/VIP status is
/// <c>UNVERIFIED</c> — only the literal threshold 26 is proven; the gated value is supplied by the
/// caller. spec: quests.md §4.3 / §10 / §13 #10.
/// </para>
/// </remarks>
public readonly record struct QuestState
{
    /// <summary>The accept gate threshold: accept is blocked when the gated status value is below this. spec: quests.md §4.3 / §10 (26).</summary>
    public const int AcceptGateThreshold = 26;

    /// <summary>The active quest id (0 when none). spec: quests.md §4.3 (active quest id).</summary>
    public uint QuestId { get; init; }

    /// <summary>The current quest phase. spec: quests.md §4.2 / §7.</summary>
    public QuestPhase Phase { get; init; }

    /// <summary>True when the quest is accepted and progressing. spec: quests.md §5.1.</summary>
    public bool IsInProgress => Phase == QuestPhase.InProgress;

    /// <summary>True when the quest reached the granted completion verdict. spec: quests.md §7.2.</summary>
    public bool IsCompleted => Phase == QuestPhase.Completed;

    /// <summary>
    /// True when the §4.3 accept gate passes for <paramref name="gatedStatusValue"/> (≥ 26).
    /// spec: Docs/RE/specs/quests.md §4.3 / §10.
    /// </summary>
    public static bool AcceptGatePasses(int gatedStatusValue) => gatedStatusValue >= AcceptGateThreshold;

    /// <summary>
    /// Accepts an available quest (sub_action 2) when the §4.3 gate passes: <c>available → in-progress</c>.
    /// Rejected if not available or the gate fails. spec: Docs/RE/specs/quests.md §4.2/§4.3.
    /// </summary>
    /// <returns>The next state and whether the accept was applied (and would send 2/28).</returns>
    public (QuestState Next, bool Accepted) Accept(uint questId, int gatedStatusValue)
    {
        if (Phase != QuestPhase.Available || questId == 0 || !AcceptGatePasses(gatedStatusValue))
        {
            return (this, false);
        }

        return (new QuestState { QuestId = questId, Phase = QuestPhase.InProgress }, true);
    }

    /// <summary>
    /// Marks the quest completed by a granted verdict (5/73 reward_state 1): <c>in-progress → completed</c>.
    /// Rejected unless in progress. spec: Docs/RE/specs/quests.md §7.2 (grant).
    /// </summary>
    public QuestState Complete() => Phase == QuestPhase.InProgress ? this with { Phase = QuestPhase.Completed } : this;

    /// <summary>
    /// Marks the quest failed by a deny verdict (5/73 reward_state 2): <c>in-progress → failed</c>.
    /// Rejected unless in progress. spec: Docs/RE/specs/quests.md §7.2 (deny).
    /// </summary>
    public QuestState Fail() => Phase == QuestPhase.InProgress ? this with { Phase = QuestPhase.Failed } : this;

    /// <summary>
    /// Applies a 5/73 completion verdict, mapping <see cref="QuestRewardState.Grant"/> to
    /// <see cref="Complete"/> and <see cref="QuestRewardState.Deny"/> to <see cref="Fail"/>. The handler
    /// acts only when the apply flag is set (the caller passes <paramref name="apply"/>).
    /// spec: Docs/RE/specs/quests.md §7.1/§7.2 (apply == 1).
    /// </summary>
    public QuestState ApplyVerdict(bool apply, QuestRewardState rewardState)
    {
        if (!apply)
        {
            return this; // spec: quests.md §7.1 (handler acts only when apply == 1).
        }

        return rewardState switch
        {
            QuestRewardState.Grant => Complete(),
            QuestRewardState.Deny => Fail(),
            _ => this,
        };
    }

    /// <summary>
    /// Gives up / abandons an in-progress quest (sub_action 4) and clears the local active-quest state.
    /// Rejected unless in progress. spec: Docs/RE/specs/quests.md §4.2/§4.3 ("clear local active-quest state").
    /// </summary>
    public QuestState GiveUp() =>
        Phase == QuestPhase.InProgress ? new QuestState { QuestId = 0, Phase = QuestPhase.GivenUp } : this;
}