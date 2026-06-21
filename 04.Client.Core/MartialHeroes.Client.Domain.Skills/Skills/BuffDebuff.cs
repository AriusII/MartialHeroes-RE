namespace MartialHeroes.Client.Domain.Skills.Skills;

/// <summary>
///     One status (buff / debuff) slot: the 12-byte status entry plus its parallel strength field. A slot
///     is "active" when its <see cref="DurationTicks" /> is greater than 0.
///     spec: Docs/RE/specs/skills.md §6.1 / §6.2 / §6.3.
/// </summary>
/// <remarks>
///     <para>
///         The on-wire entry is <c>[u32 effect_code][u32 value/duration][u32 param]</c> (§6.1); the second
///         dword is the remaining-duration counter that the §6.3 tick decrements by 1 per tick. The parallel
///         secondary table carries the buff's <see cref="Magnitude" /> (the %-gated strength, e.g. the
///         <c>&lt; 100</c> gate on effect code 64). spec: skills.md §6.1, §6.2 (secondary magnitude), §6.3.
///     </para>
///     <para>
///         <b>Stacking model:</b> each effect code occupies one fixed slot index; re-applying overwrites that
///         slot (refresh, not additive — there is no stack counter). Duration is the only per-slot timer.
///         spec: skills.md §6.3 ("Stacking model: refresh-by-slot").
///     </para>
/// </remarks>
public readonly record struct BuffDebuff
{
    /// <summary>
    ///     The %-gate threshold for effect code 64 (flag set only when magnitude &lt; 100). spec: skills.md §6.2 (64 /
    ///     0x40).
    /// </summary>
    public const int PercentGateThreshold = 100;

    /// <summary>The effect code (first dword). spec: skills.md §6.1 / §6.2.</summary>
    public int EffectCode { get; init; }

    /// <summary>
    ///     Remaining duration in ticks (second dword). Active while &gt; 0; decrements by 1 per tick. spec: skills.md
    ///     §6.1 / §6.3.
    /// </summary>
    public int DurationTicks { get; init; }

    /// <summary>The slot's param tail (third dword). spec: skills.md §6.1.</summary>
    public int Param { get; init; }

    /// <summary>
    ///     The buff's strength / percent from the parallel secondary table (the %-gated magnitude). spec: skills.md §6.1
    ///     / §6.2.
    /// </summary>
    public ushort Magnitude { get; init; }

    /// <summary>An empty (cleared) slot.</summary>
    public static BuffDebuff Empty => default;

    /// <summary>
    ///     True when the slot is active (duration &gt; 0). spec: skills.md §6.2 ("active when its value/duration field is
    ///     &gt; 0").
    /// </summary>
    public bool IsActive => DurationTicks > 0;

    /// <summary>True when the slot holds no effect (cleared). spec: skills.md §6.1 (clear zeroes the effect-code dword).</summary>
    public bool IsEmpty => EffectCode == 0 && DurationTicks <= 0;

    /// <summary>
    ///     True when this is the §6.2 effect code 64 %-gated flag and its magnitude is below 100 (the flag
    ///     is set only when active AND magnitude &lt; 100). spec: Docs/RE/specs/skills.md §6.2 (64 / 0x40).
    /// </summary>
    public bool IsPercentGatedFlagSet =>
        EffectCode == (int)BuffEffectCode.PercentGatedFlag
        && IsActive
        && Magnitude < PercentGateThreshold;

    /// <summary>
    ///     Decrements the duration by one tick (§6.3). While the counter is &gt; 1 it decrements; reaching 0
    ///     expires the slot (the applicator re-runs with the active flag now 0). Negative durations clamp
    ///     to 0. spec: Docs/RE/specs/skills.md §6.3.
    /// </summary>
    /// <returns>
    ///     The slot after one tick and whether it expired <em>this</em> tick (transitioned to inactive).
    /// </returns>
    public (BuffDebuff Next, bool Expired) TickOnce()
    {
        // Negative durations clamp to 0. spec: skills.md §6.3 ("Negative durations clamp to 0").
        if (DurationTicks <= 0) return (DurationTicks < 0 ? this with { DurationTicks = 0 } : this, false);

        var next = DurationTicks - 1;
        var expired = next == 0;
        return (this with { DurationTicks = next }, expired);
    }
}