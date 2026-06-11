namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The numeric result code returned by the cast pipeline (0..21). <c>0</c> = success (the use-skill
/// request would be sent); every non-zero value is the code of the first gate that blocked.
/// spec: Docs/RE/specs/skills.md §2 ("returns a numeric result code 0..21; 0 = success") / §2.3.
/// </summary>
/// <remarks>
/// <para>
/// Only the codes the §2.1 ordered gate chain and the §2.2 range/LoS step emit are named here; the
/// localized-string-id mapping (§2.3) lives in the UI layer, not the Domain. Code <c>8</c>
/// (<see cref="MoveCloser"/>) intentionally produces no toast — it triggers an approach instead.
/// spec: skills.md §2.3.
/// </para>
/// <para>
/// The spec recommends lifting this enum into <c>Shared.Kernel</c>; until a Kernel home exists it is
/// modelled here so the cast state machine is self-contained and additive. spec: skills.md §2.3 note.
/// </para>
/// </remarks>
public enum SkillCastResult
{
    /// <summary>Success — the request passes every gate. spec: skills.md §2 / §2.1 step 19.</summary>
    Ok = 0,

    /// <summary>Billing / rank gate blocked (rank-cap notice). spec: skills.md §2.1 gate 2 (code 1).</summary>
    BillingOrRank = 1,

    /// <summary>Alive gate blocked (local player not alive/can-act). spec: skills.md §2.1 gate 7 (code 2).</summary>
    NotAlive = 2,

    /// <summary>Current-target hostile-state gate blocked. spec: skills.md §2.1 gate 9 (code 3).</summary>
    TargetHostileState = 3,

    /// <summary>Mounted gate, or unresolved skill, or self-cast path. spec: skills.md §2.1 gates 4/10 (code 4).</summary>
    MountedOrUnresolved = 4,

    /// <summary>Self-cast eligibility gate blocked. spec: skills.md §2.1 gate 12 (code 5).</summary>
    SelfCastIneligible = 5,

    /// <summary>MP affordability gate blocked (available MP &lt; 100 × mp_cost_factor). spec: skills.md §2.1 gate 14 (code 6).</summary>
    NotEnoughMp = 6,

    /// <summary>Code 7 (string id 44014; gate not individually enumerated in §2.1). spec: skills.md §2.3 (code 7).</summary>
    Reason7 = 7,

    /// <summary>Out of range — no toast; the client issues a move toward the aim point. spec: skills.md §2.2 (code 8).</summary>
    MoveCloser = 8,

    /// <summary>Terrain / line-of-sight blocked on the aim point. spec: skills.md §2.2 (code 9).</summary>
    LineOfSightBlocked = 9,

    /// <summary>Target-state test failed (target not valid). spec: skills.md §2.2 (code 10).</summary>
    InvalidTarget = 10,

    /// <summary>Cast-window timing gate blocked (out of warm-up / valid window). spec: skills.md §2.1 gate 17 (code 11).</summary>
    CastWindowTiming = 11,

    /// <summary>Neither the ally nor the enemy target array was populated. spec: skills.md §2.1 gate 18 (code 12).</summary>
    NoTargets = 12,

    /// <summary>Busy / already-casting gate blocked. spec: skills.md §2.1 gate 3 (code 13).</summary>
    AlreadyCasting = 13,

    /// <summary>Code 14 (string id 44024). spec: skills.md §2.3 (code 14).</summary>
    Reason14 = 14,

    /// <summary>Code 15 (string id 44025). spec: skills.md §2.3 (code 15).</summary>
    Reason15 = 15,

    /// <summary>Map / zone-mode gate blocked (scene mode forbids casting). spec: skills.md §2.1 gate 5 (code 16).</summary>
    MapModeForbidden = 16,

    /// <summary>Party / relation gate blocked (a referenced actor is not allied). spec: skills.md §2.1 gate 1 (code 17).</summary>
    PartyRelation = 17,

    /// <summary>Weapon / stance requirement not satisfied. spec: skills.md §2.1 gate 11 (code 18).</summary>
    WeaponRequirement = 18,

    /// <summary>Stun / silence gate blocked. spec: skills.md §2.1 gate 6 (code 19).</summary>
    StunnedOrSilenced = 19,

    /// <summary>Generic action-lock flag set. spec: skills.md §2.1 gate 8 (code 20).</summary>
    ActionLocked = 20,

    /// <summary>Still cooling down (recast gate). spec: skills.md §2.1 gate 13 / §4 (cooldown not ready).</summary>
    /// <remarks>
    /// <b>Modeling choice (ours).</b> The spec's §2.1 gate 13 falls through to the MP check rather
    /// than emitting a distinct numeric code, but a dedicated "on cooldown" outcome is useful to a
    /// re-implementation; we assign it code 21 (within the documented 0..21 range) and document it as
    /// our own modelling addition. spec: skills.md §2 ("result code 0..21"), §2.1 gate 13.
    /// </remarks>
    OnCooldown = 21,
}
