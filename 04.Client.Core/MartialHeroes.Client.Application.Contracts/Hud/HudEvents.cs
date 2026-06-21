using System.Collections.Immutable;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.Contracts.Hud;

// =====================================================================================================
// HUD event DTOs — immutable snapshots the World-Scene HUD binds to, fed by the live packet handlers
// noted per type. Each carries only value types / already-
// decoded managed strings so the render-thread consumer can never observe torn Domain state.
// =====================================================================================================

/// <summary>
///     One scrollback chat line, ready for the chat-log window to append. Immutable snapshot.
/// </summary>
/// <remarks>
///     The chat log is a single append sink fed by both local echo and the inbound 5/7 broadcast handler;
///     every line is a <c>(text, channel, colour)</c> triple keyed off the integer channel code that
///     selects the C2S route, the log colour, and the overhead-bubble slot. The CP949 text is decoded at
///     the producer boundary (the 5/7 handler) into a managed string — never carried as
///     raw bytes across this seam, matching <see cref="MartialHeroes.Client.Application.Events.ChatBroadcastEvent" />.
///     spec: Docs/RE/specs/chat.md §3 (channel → colour table), §6.2 (the 36-byte line record:
///     text + ARGB colour + channel), §6.3 (the append sink's (text, channel, colour) contract).
/// </remarks>
/// <param name="ChannelCode">
///     Integer chat channel code: 0 say, 1 shout, 2 party, 3 guild, 6 event, 7 special, 9 whisper,
///     15 alliance. spec: Docs/RE/specs/chat.md §3 (channel code table).
/// </param>
/// <param name="Text">Already-decoded (CP949 → managed) line text.</param>
/// <param name="ColorArgb">
///     32-bit ARGB log colour (<c>0xAARRGGBB</c>) chosen per channel by the producer.
///     spec: Docs/RE/specs/chat.md §3 (e.g. say <c>0xFFFFFFFF</c>, party <c>0xFF00FFFF</c>).
/// </param>
/// <param name="SenderName">
///     Already-decoded sender display name, or <see langword="null" /> for a system / local line.
/// </param>
public sealed record ChatLineEvent(
    int ChannelCode,
    string Text,
    uint ColorArgb,
    string? SenderName = null) : IHudEvent
{
    /// <summary>Say / normal channel code. spec: Docs/RE/specs/chat.md §3.</summary>
    public const int ChannelSay = 0; // spec: Docs/RE/specs/chat.md §3 (channel code table)

    /// <summary>Whisper channel code (log-only, no overhead bubble). spec: Docs/RE/specs/chat.md §3.</summary>
    public const int ChannelWhisper = 9; // spec: Docs/RE/specs/chat.md §3 (channel code table)

    /// <summary>Default say-line ARGB (white). spec: Docs/RE/specs/chat.md §3 (say row).</summary>
    public const uint SayColorArgb = 0xFFFFFFFFu; // spec: Docs/RE/specs/chat.md §3 (say = white)
}

/// <summary>
///     One active buff/state slot in the 30-slot HUD buff bar. <see cref="BuffId" /> == 0 marks an empty
///     slot the bar leaves hidden. <see cref="RemainingTicks" /> is a provisional countdown candidate.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §1.6 (30 icon slots; the 12-byte active-buff record:
///     <c>buff_id</c> u16 at +0, a duration candidate u32 at +4; <c>buff_id == 0</c> = empty slot);
///     Docs/RE/packets/4-102_buff_state.yaml (30 records of 12 bytes from payload +116).
///     The duration unit (ms vs s) is CAPTURE-UNVERIFIED, so the remaining time is nullable and the HUD
///     must verify it against a capture before drawing an on-icon countdown.
/// </remarks>
/// <param name="BuffId">Buff/state catalogue id; 0 = empty slot. spec: Docs/RE/formats/misc_data.md §1.6.</param>
/// <param name="RemainingTicks">
///     Provisional remaining-time candidate (unit unconfirmed), or <see langword="null" /> when the
///     producer has no duration. spec: Docs/RE/formats/misc_data.md §1.6 (duration field, PLAUSIBLE).
/// </param>
public readonly record struct BuffSlot(ushort BuffId, uint? RemainingTicks)
{
    /// <summary>Empty-slot sentinel: a record with this id is skipped by the bar. spec: Docs/RE/formats/misc_data.md §1.6.</summary>
    public const ushort EmptyBuffId = 0; // spec: Docs/RE/formats/misc_data.md §1.6 (buff_id == 0 = empty slot)

    /// <summary>True when this slot is empty (hidden by the HUD). spec: Docs/RE/formats/misc_data.md §1.6.</summary>
    public bool IsEmpty => BuffId == EmptyBuffId;
}

/// <summary>
///     A full refresh of the 30-slot HUD buff bar. Immutable snapshot. The bar clears and hides all 30
///     slots, then shows only the non-empty ones — buff stacking is fully server-driven, with no
///     client-side expiry between refreshes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §1.6 (the 30-slot buff bar, the per-refresh reset, server-driven
///     slot assignment); Docs/RE/packets/4-102_buff_state.yaml (one 4/102 message rebuilds the whole bar).
///     Future producer: the 4/102 <c>SmsgSkillWindowStateUpdate</c> handler.
/// </remarks>
/// <param name="Slots">
///     Exactly <see cref="SlotCount" /> slots in wire order; empty slots carry
///     <see cref="BuffSlot.EmptyBuffId" />.
/// </param>
public sealed record BuffStateEvent(ImmutableArray<BuffSlot> Slots) : IHudEvent
{
    /// <summary>
    ///     The HUD buff bar has exactly 30 icon slots. spec: Docs/RE/formats/misc_data.md §1.6;
    ///     Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    public const int SlotCount = 30; // spec: Docs/RE/formats/misc_data.md §1.6 (30 icon slots)

    /// <summary>
    ///     Builds a <see cref="BuffStateEvent" />, validating the <see cref="SlotCount" /> invariant the
    ///     4/102 wire layout guarantees.
    /// </summary>
    public static BuffStateEvent FromSlots(ImmutableArray<BuffSlot> slots)
    {
        if (slots.Length != SlotCount)
            throw new ArgumentException(
                $"Buff state must carry exactly {SlotCount} slots (got {slots.Length}).", nameof(slots));

        return new BuffStateEvent(slots);
    }
}

/// <summary>
///     The local player's current vitals (HP / MP / stamina) changed. Immutable snapshot driving the
///     right-edge gauge panel. Published by the <c>5/53 SmsgActorVitalsAndPairState</c> handler whenever
///     the incoming actor key matches the local player, and by the <c>5/32 SmsgLevelUp</c> handler when
///     that message also refreshes vitals.
/// </summary>
/// <remarks>
///     The server sends absolute current values; the client clamps them against its locally-computed
///     maxima (from the stat pipeline in <c>Client.Domain</c>). The gauge panel reads the pre-clamped
///     current values and the Domain-resolved maxima via this snapshot — it never reads live Domain
///     objects across the channel seam.
///     spec: Docs/RE/specs/combat.md §12.2 (5/53 is the canonical HP-bar source — absolute current HP);
///     Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentHp@0x10 / CurrentMp@0x14 /
///     Stamina@0x18 are the three local-player vitals mirrored to client globals and clamped to the
///     client-computed maxima).
/// </remarks>
/// <param name="CurrentHp">
///     Clamped current hit points (server-authoritative absolute, floored at 0). spec:
///     Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentHp@0x10).
/// </param>
/// <param name="MaxHp">
///     Client-computed maximum HP (from the stat pipeline). spec: Docs/RE/specs/combat.md §12.2 (HP-bar
///     reads current vs. locally-computed max).
/// </param>
/// <param name="CurrentMp">
///     Clamped current MP / ki. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml
///     (CurrentMp@0x14).
/// </param>
/// <param name="MaxMp">Client-computed maximum MP.</param>
/// <param name="CurrentStamina">
///     Clamped current stamina. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml
///     (Stamina@0x18).
/// </param>
/// <param name="MaxStamina">
///     Client-computed maximum stamina. spec: Docs/RE/specs/combat.md §1 (MaxStamina field in the
///     derived-stat aggregate).
/// </param>
public sealed record HudVitalsEvent(
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    uint CurrentStamina,
    uint MaxStamina) : IHudEvent
{
    /// <summary>
    ///     The "no vitals / uninitialized" sentinel — all zeroes. Published on world exit or before the
    ///     first 5/53 arrives. spec: Docs/RE/specs/combat.md §12.2 (HP reaching 0 = death condition;
    ///     gauge must handle a zero max gracefully).
    /// </summary>
    public static HudVitalsEvent None { get; } = new(0u, 0u, 0u, 0u, 0u, 0u);

    /// <summary>
    ///     HP fill fraction in <c>[0, 1]</c>. Returns 0 when <see cref="MaxHp" /> is 0 (unknown / dead)
    ///     so the gauge never divides by zero.
    ///     spec: Docs/RE/specs/combat.md §12.2 (HP-bar reads the absolute current HP against the
    ///     locally-computed maximum).
    /// </summary>
    public float HpRatio =>
        MaxHp == 0u ? 0f : Math.Clamp((float)CurrentHp / MaxHp, 0f, 1f);

    /// <summary>
    ///     MP fill fraction in <c>[0, 1]</c>. Returns 0 when <see cref="MaxMp" /> is 0.
    ///     spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentMp@0x14).
    /// </summary>
    public float MpRatio =>
        MaxMp == 0u ? 0f : Math.Clamp((float)CurrentMp / MaxMp, 0f, 1f);

    /// <summary>
    ///     Stamina fill fraction in <c>[0, 1]</c>. Returns 0 when <see cref="MaxStamina" /> is 0.
    ///     spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (Stamina@0x18).
    /// </summary>
    public float StaminaRatio =>
        MaxStamina == 0u ? 0f : Math.Clamp((float)CurrentStamina / MaxStamina, 0f, 1f);

    /// <summary>True when this snapshot is the "no vitals" sentinel (all fields are zero).</summary>
    public bool IsEmpty =>
        CurrentHp == 0u && MaxHp == 0u && CurrentMp == 0u && MaxMp == 0u
        && CurrentStamina == 0u && MaxStamina == 0u;
}

/// <summary>
///     One floating combat number to spawn above a target actor. Immutable snapshot. The HUD selects the
///     number's motion curve and colour family from <see cref="Kind" /> and <see cref="IsCrit" />.
/// </summary>
/// <remarks>
///     The client receives combat outcomes from the server (it does not roll damage); a "damage applier"
///     chooses an animation <see cref="Kind" /> in <c>0..7</c> that selects both the motion and the colour
///     of the per-digit billboard. spec: Docs/RE/specs/combat.md §12.3 (the 0..7 kind → motion/colour
///     table; the crit flag), §12.1 (per-target hit records carry the visible damage). Future producer:
///     the 5/52 <c>SmsgActorSkillAction</c> handler. (The spec discusses 5/52 for hit records; the task's
///     "5/52 combat" producer is the documented follow-up.)
/// </remarks>
/// <param name="TargetKey">The actor the number floats above (sort + id).</param>
/// <param name="Value">The visible damage / heal magnitude.</param>
/// <param name="Kind">
///     Animation kind in <see cref="MinKind" />..<see cref="MaxKind" /> (0..7). spec:
///     Docs/RE/specs/combat.md §12.3.
/// </param>
/// <param name="IsCrit">True for a critical hit (crit-style size / colour). spec: Docs/RE/specs/combat.md §12.3.</param>
/// <param name="SkillId">
///     The casting skill / entity key (5/52 header +0x0C), so the producer can pass the action's skill
///     through without re-deriving it. Defaults to 0 for legacy / non-5/52 producers.
///     spec: Docs/RE/packets/5-52_actor_skill_action.yaml (SkillId @0x0C).
/// </param>
/// <param name="RawDamageCandidateA">
///     CAPTURE-PENDING raw 64-bit damage candidate read from 5/52 target record +0x10/+0x14
///     (handlers.md §17.11 reading). NOT decoded into <see cref="Value" /> — the offset/polarity is
///     ambiguous, so both candidates are carried raw for a capture to disambiguate.
///     spec: Docs/RE/specs/handlers.md §17.11 / Docs/RE/packets/5-52_actor_skill_action.yaml.
/// </param>
/// <param name="RawDamageCandidateB">
///     CAPTURE-PENDING raw 64-bit damage candidate read from 5/52 target record +0x14/+0x18
///     (the 5-52.yaml reading). See <see cref="RawDamageCandidateA" />.
///     spec: Docs/RE/packets/5-52_actor_skill_action.yaml (§RECORD OFFSET CORRECTION).
/// </param>
public sealed record CombatTextEvent(
    ActorKey TargetKey,
    int Value,
    byte Kind,
    bool IsCrit,
    uint SkillId = 0u,
    long RawDamageCandidateA = 0L,
    long RawDamageCandidateB = 0L) : IHudEvent
{
    /// <summary>Lowest valid animation kind. spec: Docs/RE/specs/combat.md §12.3 (kind range 0..7).</summary>
    public const byte MinKind = 0; // spec: Docs/RE/specs/combat.md §12.3 (animation kind 0..7)

    /// <summary>Highest valid animation kind. spec: Docs/RE/specs/combat.md §12.3 (kind range 0..7).</summary>
    public const byte MaxKind = 7; // spec: Docs/RE/specs/combat.md §12.3 (animation kind 0..7)
}

/// <summary>
///     The current-target tooltip/billboard changed. Immutable snapshot driving the target name + level
///     label and the overhead HP/MP minibar. A <see cref="ActorKey.RawId" /> of
///     <see cref="ActorKey.UnassignedRawId" /> in <see cref="TargetKey" /> (or a null event published by the
///     producer) means "no target / target cleared".
/// </summary>
/// <remarks>
///     The global hovered/selected target drives the target-info tooltip / overhead name+level billboard;
///     the tooltip reads the target actor's name (CP949, decoded at the producer boundary) and level.
///     spec: Docs/RE/specs/combat.md §10.1 (the global hovered/selected target drives the tooltip /
///     overhead name+level billboard), §12.2 (the HP-bar reads the actor's absolute current HP — the
///     ratio source). Future producer: the target-selection / 5/53 vitals path.
/// </remarks>
/// <param name="TargetKey">The selected actor (sort + id).</param>
/// <param name="Name">Already-decoded target display name.</param>
/// <param name="HpRatio">Current HP as a fraction of max in <c>[0, 1]</c>. spec: Docs/RE/specs/combat.md §12.2.</param>
/// <param name="MpRatio">Current MP / ki as a fraction of max in <c>[0, 1]</c>. spec: Docs/RE/specs/combat.md §12.2.</param>
public sealed record TargetChangedEvent(
    ActorKey TargetKey,
    string Name,
    float HpRatio,
    float MpRatio) : IHudEvent
{
    /// <summary>
    ///     The "no target" event: an unassigned key, blank name, zeroed bars. spec: Docs/RE/specs/combat.md §10.1
    ///     (selection cleared on world re-entry).
    /// </summary>
    public static TargetChangedEvent None { get; } =
        new(new ActorKey(ActorKey.UnassignedRawId, default), string.Empty, 0f, 0f);

    /// <summary>True when this snapshot represents "no current target". spec: Docs/RE/specs/combat.md §10.1.</summary>
    public bool IsCleared => TargetKey.RawId == ActorKey.UnassignedRawId;
}

/// <summary>
///     The player's current zone type changed. Immutable snapshot driving the HUD zone indicator.
///     Published by <c>RegionService</c> whenever the 256-unit region cell under the local player
///     switches to a different <see cref="MartialHeroes.Shared.Kernel.Enums.ZoneType" />.
///     Only published when the zone CHANGES — identical consecutive positions do not re-fire.
///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum (Safe/OpenPvp/Closed).
/// </summary>
/// <param name="Zone">
///     The new zone type.
///     <see cref="MartialHeroes.Shared.Kernel.Enums.ZoneType.Safe" /> (the §16.3 default) is emitted
///     when region data is absent (VFS offline or files missing).
///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
/// </param>
public sealed record ZoneChangedEvent(
    ZoneType Zone) : IHudEvent;

/// <summary>
///     The experience bar / level changed. Immutable snapshot driving the HUD XP gage. <see cref="Ratio" />
///     is the filled fraction the streaming bar widget renders.
/// </summary>
/// <remarks>
///     5/9 <c>ExpGain</c> adds 64-bit experience to the local player and refreshes the XP bar; 5/32
///     <c>LevelUp</c> writes the new level. The within-level progress is the player's current XP measured
///     against the XP required for the current level. spec: Docs/RE/specs/progression.md §3 (5/9 ExpGain
///     adds 64-bit XP, refreshes the bar), §5 (5/32 LevelUp writes the new level), §12 Q3 (the streaming
///     on-HUD exp-bar gage is a separate widget — this event is its bound input). Future producers: the
///     5/9 and 5/32 handlers.
/// </remarks>
/// <param name="CurrentXp">
///     Current experience within the level (64-bit accumulator). spec: Docs/RE/specs/progression.md
///     §3.
/// </param>
/// <param name="XpForLevel">
///     Experience required to fill the current level (0 ⇒ unknown / max level). spec:
///     Docs/RE/specs/progression.md §12 Q3.
/// </param>
/// <param name="Level">Current character level. spec: Docs/RE/specs/progression.md §5.</param>
public sealed record ExpLevelEvent(
    long CurrentXp,
    long XpForLevel,
    ushort Level) : IHudEvent
{
    /// <summary>
    ///     The XP bar's filled fraction in <c>[0, 1]</c>. Returns 0 when <see cref="XpForLevel" /> is
    ///     non-positive (unknown / max level) so the widget never divides by zero.
    ///     spec: Docs/RE/specs/progression.md §3 (the XP bar fill).
    /// </summary>
    public float Ratio =>
        XpForLevel <= 0 ? 0f : Math.Clamp((float)((double)CurrentXp / XpForLevel), 0f, 1f);
}