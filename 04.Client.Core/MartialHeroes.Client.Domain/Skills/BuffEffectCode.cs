namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The recovered status effect-code values that change gameplay or visual state (the first dword of a
/// 12-byte status slot). Codes outside this set fall through to icon-only display.
/// spec: Docs/RE/specs/skills.md §6.2 (effect-code applicator table).
/// </summary>
/// <remarks>
/// Only the codes named in §6.2 are enumerated; the full code→meaning table for the icon-only codes
/// (poison, slow, stun, heal-over-time magnitudes) lives in <c>skills.scr</c> data, not the client
/// logic (open question 3). An unknown code is valid and simply displays an icon. spec: skills.md §6.2.
/// All mappings below are <c>LIKELY</c> confidence per §6.2.
/// </remarks>
public enum BuffEffectCode
{
    /// <summary>Enter stance / motion state (transform-in). spec: skills.md §6.2 (43 / 0x2B).</summary>
    EnterStance = 43,

    /// <summary>A motion state. spec: skills.md §6.2 (44 / 0x2C).</summary>
    MotionState44 = 44,

    /// <summary>Toggle a local control flag. spec: skills.md §6.2 (45 / 0x2D).</summary>
    ToggleLocalControl = 45,

    /// <summary>Model / appearance swap (petrify / transform). spec: skills.md §6.2 (46 / 0x2E).</summary>
    AppearanceSwap = 46,

    /// <summary>Movement / control restriction (root / snare). spec: skills.md §6.2 (47 / 0x2F).</summary>
    RootSnare = 47,

    /// <summary>Dispel / cleanse: clears effect-code 43, 46, 47 slots; resets stance. spec: skills.md §6.2 (48 / 0x30).</summary>
    Dispel = 48,

    /// <summary>Appearance / poison transform (variants). spec: skills.md §6.2 (50 / 0x32).</summary>
    PoisonTransform = 50,

    /// <summary>Sets an AoE-active actor state. spec: skills.md §6.2 (57 / 0x39).</summary>
    AoeActiveState = 57,

    /// <summary>Sets a flag only when active AND the secondary magnitude &lt; 100 (a %-gated flag). spec: skills.md §6.2 (64 / 0x40).</summary>
    PercentGatedFlag = 64,

    /// <summary>A motion state. spec: skills.md §6.2 (131 / 0x83).</summary>
    MotionState131 = 131,
}