using System.Collections.Immutable;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Contracts.Events;

/// <summary>
///     Published when an actor enters the world. Immutable snapshot of the freshly-spawned actor's
///     presentation-relevant fields. spec: Docs/RE/opcodes.md (5/3 SmsgCharSpawn);
///     Docs/RE/structs/actor.md (SpawnDescriptor fields).
/// </summary>
/// <param name="Key">Composite actor identity (raw id + sort).</param>
/// <param name="Name">Decoded actor name (NUL-terminated on the wire).</param>
/// <param name="Level">Character level.</param>
/// <param name="Position">Spawn position (Q16.16, world Y forced to 0).</param>
/// <param name="CurrentHp">Current hit points at spawn.</param>
/// <param name="MaxHp">Computed maximum hit points.</param>
/// <param name="ServerClass">Server-assigned class id (martial-arts style).</param>
/// <param name="InternalClass">
///     The descriptor's internal class word {1,2,3,4} (+0x34) — the <c>class</c> argument of the
///     skinned-avatar model-class formula <c>5*(InternalClass + 4*AppearanceVariant) - 24</c>, distinct
///     from <see cref="ServerClass" />. The live skinned-player factory
///     (<c>ActorManager_SpawnActorFromDescriptor @0x423fe9</c>, mode 1 = player → <c>Appearance_ResolveKey
///     @0x422631</c>) drives appearance from this; mob/NPC tags carry the raw field but take a different
///     resolve branch. Optional/defaulted so existing publishers keep compiling; the live 4/4 tag-1 path
///     fills it. spec: Docs/RE/structs/spawn_descriptor.md (+0x34);
///     Docs/RE/specs/assembly_graph.md §2/§4; Appearance_ResolveKey @0x422631.
/// </param>
/// <param name="AppearanceVariant">
///     The descriptor's body / gender appearance variant (+0x2C) — the <c>variant</c> argument of the
///     model-class formula (variant 3 ⇒ invisible). Optional/defaulted. spec:
///     Docs/RE/structs/spawn_descriptor.md (+0x2C); Appearance_ResolveKey @0x422631
///     (<c>if(a4==3) return 0; else return 5*(a3+4*a4)-24</c>).
/// </param>
/// <param name="EquipGids">
///     The six visible-gear part GIDs for the fixed overlay slots <c>{3,4,6,2,11,14}</c> (slot id ==
///     array index), decoded from the descriptor's +0x58 equip table (leading dword of each 16-byte
///     entry). The SAME six-slot decode <see cref="CharacterListSlot.EquipGids" /> and
///     <see cref="LocalPlayerSpawnedEvent.EquipGids" /> carry. Defaults to empty (body-only) for actors
///     that genuinely carry no gear or whose kind lacks the field. spec:
///     Docs/RE/structs/spawn_descriptor.md (+0x58 equip table); Docs/RE/specs/assembly_graph.md §4;
///     ActorVisual_RebindLocalPlayerParts ({3,4,6,2,11,14}).
/// </param>
public sealed record ActorSpawnedEvent(
    ActorKey Key,
    string Name,
    ushort Level,
    Vector3Fixed Position,
    uint CurrentHp,
    uint MaxHp,
    ushort ServerClass,
    ushort InternalClass = 0,
    byte AppearanceVariant = 0,
    ImmutableArray<uint> EquipGids = default) : IClientEvent;

/// <summary>
///     Published when an actor's movement state changes. Immutable snapshot. spec: Docs/RE/opcodes.md
///     (5/13 SmsgActorMovementUpdate).
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="Position">Current position (Q16.16, FromFloat-converted at the handler boundary).</param>
/// <param name="MoveTarget">Interpolation destination (Q16.16).</param>
/// <param name="Yaw">Facing yaw, raw Q16.16.</param>
/// <param name="IsRunning">True when the run flag was set on the wire.</param>
public sealed record ActorMovedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    Vector3Fixed MoveTarget,
    int Yaw,
    bool IsRunning) : IClientEvent;

/// <summary>
///     Published when an actor leaves the world. Immutable snapshot. spec: Docs/RE/opcodes.md
///     (5/0 SmsgCharDespawn).
/// </summary>
/// <param name="Key">Composite actor identity that was removed.</param>
/// <param name="PlayLeaveEffect">Bit0 of the despawn flags: play a "left" SFX + chat line.</param>
public sealed record ActorDespawnedEvent(ActorKey Key, bool PlayLeaveEffect) : IClientEvent;

/// <summary>
///     Published when an actor's current vitals change (5/53). Immutable snapshot. spec:
///     Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="CurrentHp">Updated current hit points.</param>
/// <param name="CurrentMp">Updated current MP / ki (the third vital mirrored to the local player).</param>
/// <param name="CurrentStamina">Updated current stamina.</param>
public sealed record ActorVitalsChangedEvent(
    ActorKey Key,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina) : IClientEvent;

/// <summary>
///     Published when an actor's primary stats change (4/29 StatUpdate, applied only when ResultOk == 1).
///     Immutable snapshot of the five echoed absolute stats and remaining points. spec:
///     Docs/RE/packets/4-29_stat_update.yaml.
/// </summary>
/// <param name="Key">Composite actor identity (the local player).</param>
/// <param name="Stat0">Echoed stat[0].</param>
/// <param name="Stat1">Echoed stat[1].</param>
/// <param name="Stat2">Echoed stat[2].</param>
/// <param name="Stat3">Echoed stat[3].</param>
/// <param name="Stat4">Echoed stat[4].</param>
/// <param name="RemainingStatPoints">Remaining allocatable stat points.</param>
public sealed record ActorStatsChangedEvent(
    ActorKey Key,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint RemainingStatPoints) : IClientEvent;

/// <summary>
///     Published when an actor levels up (5/32): new level plus refreshed vitals. Immutable snapshot.
///     spec: Docs/RE/packets/5-32_level_up.yaml.
/// </summary>
/// <param name="Key">Composite actor identity.</param>
/// <param name="NewLevel">The actor's new level.</param>
/// <param name="CurrentHp">Refreshed current HP (low i32 half of the packed HP/MP value).</param>
/// <param name="CurrentMp">Refreshed current MP (high i32 half of the packed HP/MP value).</param>
/// <param name="CurrentStamina">Refreshed current stamina.</param>
/// <param name="RemainingStatPoints">Remaining allocatable stat points (local player).</param>
public sealed record ActorLeveledUpEvent(
    ActorKey Key,
    ushort NewLevel,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    int RemainingStatPoints) : IClientEvent;

/// <summary>
///     Published when the login handshake completes: the 1/4 Auth reply was built and sent in response to
///     the server's 0/0 KeyExchange. Immutable snapshot. spec: Docs/RE/specs/crypto.md §6.
/// </summary>
/// <param name="ReplyByteCount">Length of the built 1/4 reply body (diagnostics).</param>
public sealed record LoginHandshakeCompletedEvent(int ReplyByteCount) : IClientEvent;

// =====================================================================================================
// Equipment / inventory (4/12, 4/22, 4/19)
// =====================================================================================================

/// <summary>
///     Published when an equip / unequip result lands (4/12 SmsgEquipItemResult). Immutable snapshot. On
///     success (<paramref name="Success" />) the presentation refreshes the equipment visual; a destination
///     slot type of 15 additionally rebuilds the title/gear visual. spec: Docs/RE/specs/handlers.md §3 (4/12);
///     Docs/RE/structs/item.md.
/// </summary>
/// <param name="Success">True when the server result byte was 1 (ok). false = error.</param>
/// <param name="FromSlot">Echoed source slot index.</param>
/// <param name="ToSlot">Echoed destination slot type (15 = title/gear visual rebuild).</param>
/// <param name="TitleVisualRebuild">True when <paramref name="ToSlot" /> == 15 (title-slot visual rebuild).</param>
public sealed record EquipResultEvent(
    bool Success,
    byte FromSlot,
    byte ToSlot,
    bool TitleVisualRebuild) : IClientEvent;

/// <summary>
///     Published when an item-slot state ack lands (4/22 SmsgItemSlotStateAck): a slot's state plus its
///     stat/enchant fields. Immutable snapshot. spec: Docs/RE/specs/handlers.md §13 Group B (4/22);
///     Docs/RE/structs/item.md.
/// </summary>
/// <param name="Success">True when the result byte was 1 (ok).</param>
/// <param name="FromSlot">Source slot index.</param>
/// <param name="ToSlot">Destination slot index.</param>
/// <param name="BonusField1">Bonus / stat field 1 (UNVERIFIED meaning; forwarded verbatim).</param>
/// <param name="BonusField2">Bonus / stat field 2 (UNVERIFIED meaning; forwarded verbatim).</param>
/// <param name="BonusField3">Bonus / durability / enchant field (UNVERIFIED meaning; forwarded verbatim).</param>
public sealed record ItemSlotStateEvent(
    bool Success,
    byte FromSlot,
    byte ToSlot,
    int BonusField1,
    int BonusField2,
    int BonusField3) : IClientEvent;

/// <summary>
///     Published when an NPC buy / inventory-acquire ack lands (4/19 SmsgNpcBuyOrAcquireAck). Immutable
///     snapshot. spec: Docs/RE/specs/handlers.md §13 Group B (4/19); Docs/RE/structs/item.md.
/// </summary>
/// <param name="Success">True when the result byte was 1 (ok).</param>
/// <param name="ReasonCode">Error reason code (selects a localized string when not ok).</param>
/// <param name="BagSlotIndex">Destination bag slot the acquired item was applied to.</param>
/// <param name="ItemActorId">The acquired item's actor id (ItemQuadB).</param>
/// <param name="GoldLow">Gold cost / amount (low dword).</param>
public sealed record NpcAcquireResultEvent(
    bool Success,
    byte ReasonCode,
    byte BagSlotIndex,
    int ItemActorId,
    int GoldLow) : IClientEvent;

// =====================================================================================================
// Skill hotbar / points (5/33, 4/41, 4/150)
// =====================================================================================================

/// <summary>
///     Published when the server overwrites one skill-hotbar slot (5/33 SmsgSkillHotbarSlotSet). Immutable
///     snapshot of the new {slot, skill, points} assignment. spec: Docs/RE/specs/handlers.md §4 (5/33);
///     Docs/RE/structs/skill.md.
/// </summary>
/// <param name="HotbarSlot">Hotbar slot index (0..239).</param>
/// <param name="Skill">The skill id assigned to the slot.</param>
/// <param name="SkillPoints">The skill-point allocation / rank for the slot.</param>
public sealed record SkillHotbarSlotSetEvent(
    byte HotbarSlot,
    SkillId Skill,
    short SkillPoints) : IClientEvent;

/// <summary>
///     Published when a client-initiated hotbar assignment is acked (4/41 SmsgSkillHotbarAssignResult).
///     Immutable snapshot. spec: Docs/RE/specs/handlers.md §13 Group C (4/41); Docs/RE/structs/skill.md.
/// </summary>
/// <param name="Success">True when the gate byte was 1 (apply/ok).</param>
/// <param name="ResultCode">Error reason (1..8 select localized strings) when not ok.</param>
/// <param name="HotbarSlot">Echo of the requested hotbar slot.</param>
/// <param name="Skill">Echo of the requested skill id.</param>
/// <param name="SkillPointPool">Remaining skill points after the assignment.</param>
public sealed record SkillHotbarAssignResultEvent(
    bool Success,
    byte ResultCode,
    int HotbarSlot,
    SkillId Skill,
    uint SkillPointPool) : IClientEvent;

/// <summary>
///     Published when the local player's skill-point pool or level changes (4/150 SmsgSkillPointUpdate).
///     Immutable snapshot. Mode 1 sets the pool (<paramref name="Value" /> = total points); mode 2 is a
///     level-up notice (<paramref name="Value" /> = new level). spec: Docs/RE/specs/handlers.md §13 Group F
///     (4/150); Docs/RE/structs/skill.md.
/// </summary>
/// <param name="Mode">1 = set total skill points; 2 = level-up notice.</param>
/// <param name="Value">Mode 1: new skill-point pool. Mode 2: new character level.</param>
public sealed record SkillPointUpdateEvent(uint Mode, uint Value) : IClientEvent;

// =====================================================================================================
// Buffs / combat stats (5/31, 5/67, combat recompute)
// =====================================================================================================

/// <summary>
///     Published when one buff/status slot is written or cleared on an actor (5/31 SmsgBuffSlotUpdate).
///     Immutable snapshot of the per-actor 12-byte status entry. spec: Docs/RE/specs/handlers.md §4 (5/31);
///     Docs/RE/specs/skills.md §6.1.
/// </summary>
/// <param name="Key">The actor whose buff slot changed.</param>
/// <param name="SlotIndex">The slot index written (per-actor table 0..30).</param>
/// <param name="EffectCode">The status effect code (first dword).</param>
/// <param name="DurationTicks">Remaining duration in ticks (second dword); 0 = cleared.</param>
/// <param name="Param">The slot's param tail (third dword).</param>
public sealed record BuffSlotChangedEvent(
    ActorKey Key,
    int SlotIndex,
    int EffectCode,
    int DurationTicks,
    int Param) : IClientEvent;

/// <summary>
///     Published when an actor's world-entry stat sync lands (5/67 SmsgStatsUpdate). Immutable snapshot of
///     the neutral stat slots plus current XP. The neutral slot numbering is preserved pending a named-stat
///     mapping. spec: Docs/RE/specs/handlers.md §4 (5/67).
/// </summary>
/// <param name="Key">The actor whose stats synced.</param>
/// <param name="Stat0">Neutral stat slot 0.</param>
/// <param name="Stat2">Neutral stat slot 2.</param>
/// <param name="Stat4">Neutral stat slot 4.</param>
/// <param name="Stat5">Neutral stat slot 5.</param>
/// <param name="Stat6">Neutral stat slot 6.</param>
/// <param name="CurrentXp">Current experience (i64 at +16).</param>
public sealed record ActorStatSyncEvent(
    ActorKey Key,
    uint Stat0,
    uint Stat2,
    uint Stat4,
    uint Stat5,
    uint Stat6,
    long CurrentXp) : IClientEvent;

/// <summary>
///     Published when the local player's derived combat-stat aggregate is recomposed (equip / buff / level
///     change). Immutable snapshot of the recomputed <see cref="CombatStats" /> the character sheet mirrors.
///     spec: Docs/RE/specs/combat.md §1 / §2 (the aggregate is fully re-accumulated on input change).
/// </summary>
/// <param name="Key">The local player whose combat stats were recomposed.</param>
/// <param name="Stats">The recomputed derived combat-stat aggregate.</param>
public sealed record CombatStatsRecomputedEvent(ActorKey Key, CombatStats Stats) : IClientEvent;

// =====================================================================================================
// Combat update / chat (4/100, 5/7)
// =====================================================================================================

/// <summary>
///     Published when a combat-attack / charge UI state update lands (4/100 SmsgCombatAttackUpdate).
///     Immutable snapshot of the decoded phase/sub-kind/value (the ~176 trailing bytes are opaque and not
///     surfaced). spec: Docs/RE/specs/handlers.md §3 (4/100).
/// </summary>
/// <param name="Phase">Attack / charge phase (3 = start timed charge, 5 = end).</param>
/// <param name="SubKind">Sub-kind selector (0xFF = reset, surfaced as -1).</param>
/// <param name="Value">The charge value stored on phase 3.</param>
/// <param name="ChargeStarted">True on phase 3 (a timed charge began).</param>
/// <param name="ChargeEnded">True on phase 5 (the charge ended).</param>
public sealed record CombatAttackUpdateEvent(
    byte Phase,
    sbyte SubKind,
    uint Value,
    bool ChargeStarted,
    bool ChargeEnded) : IClientEvent;

/// <summary>
///     Published when a server chat broadcast lands (5/7 SmsgChatBroadcast). Immutable snapshot of the
///     sender identity, routing and decoded text. The CP949 sender name and text are decoded at this
///     presentation boundary. spec: Docs/RE/packets/5-7_chat_broadcast.yaml; Docs/RE/specs/handlers.md §17.12.
/// </summary>
/// <param name="SenderKey">The sender actor (sort + id).</param>
/// <param name="SenderName">Decoded sender display name (CP949 -&gt; managed string).</param>
/// <param name="Channel">Chat channel (6/7 = whisper).</param>
/// <param name="ContextId">Context / room / whisper-peer id.</param>
/// <param name="Text">Decoded message text.</param>
public sealed record ChatBroadcastEvent(
    ActorKey SenderKey,
    string SenderName,
    byte Channel,
    uint ContextId,
    string Text) : IClientEvent;

// =====================================================================================================
// Character select (3/1)
// =====================================================================================================

/// <summary>
///     Published when the character-select list lands (3/1 SmsgCharacterList). Immutable snapshot of the
///     per-slot character summaries decoded from the 981-byte per-slot records. spec:
///     Docs/RE/packets/3-1_character_list.yaml; Docs/RE/specs/handlers.md §2 / §17.1.
/// </summary>
/// <param name="ServerId">Server / list context byte.</param>
/// <param name="ChannelId">Channel / context byte.</param>
/// <param name="Characters">The decoded per-slot character summaries (one per set bit in the slot mask).</param>
public sealed record CharacterListEvent(
    byte ServerId,
    byte ChannelId,
    ImmutableArray<CharacterListSlot> Characters) : IClientEvent;

/// <summary>
///     One character-select slot summary, decoded from a 3/1 per-slot 981-byte record (its embedded
///     880-byte SpawnDescriptor). spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/structs/spawn_descriptor.md.
/// </summary>
/// <param name="SlotIndex">The slot index (bit position in the mask).</param>
/// <param name="Name">Decoded character name.</param>
/// <param name="Level">Character level.</param>
/// <param name="ServerClass">Server-assigned class id.</param>
/// <param name="CurrentHp">Current HP from the descriptor.</param>
/// <param name="PosX">
///     The character's last in-world X coordinate, shown on the slot info-row line 3 (formatted
///     <c>"%d , %d"</c> over the two position floats). Decoded from the embedded SpawnDescriptor.
///     Note: the axis pairing (X/Z vs X/Y) is itself debugger-pending in the spec. spec:
///     Docs/RE/specs/frontend_scenes.md §3.2 (info-row line 3 = world POSITION); Docs/RE/structs/actor.md (world_x).
/// </param>
/// <param name="PosZ">
///     The character's last in-world Z coordinate, paired with <see cref="PosX" /> on the slot info-row
///     line 3. World Y is forced to 0, so the second float is the Z component. spec:
///     Docs/RE/specs/frontend_scenes.md §3.2; Docs/RE/structs/actor.md (world_z).
/// </param>
/// <param name="InternalClass">
///     The descriptor's internal class word {1,2,3,4} — THE skeleton driver and the <c>class</c> argument
///     of the model-class formula <c>5*(InternalClass + 4*AppearanceVariant) - 24</c>. Distinct from
///     <see cref="ServerClass" /> (which is kept). The layer-05 preview row drives its rendered appearance
///     from this, NOT from <see cref="ServerClass" />. spec: Docs/RE/packets/3-1_character_list.yaml (+0x34).
/// </param>
/// <param name="AppearanceVariant">
///     The descriptor's body / gender variant — the <c>variant</c> argument of the model-class formula.
///     spec: Docs/RE/packets/3-1_character_list.yaml (+0x2C).
/// </param>
/// <param name="FaceA">
///     The descriptor's faceA (face index / render-visibility byte; nonzero ⇒ slot occupied). Surfaced as
///     the spec-pinned u16. spec: Docs/RE/specs/frontend_scenes.md §3.2 (+0x2E, u16, CODE-CONFIRMED).
/// </param>
/// <param name="EquipGids">
///     The six visible-gear part gids for overlay slots {3,4,6,2,11,14}, decoded from the descriptor's
///     +0x58 equip table (leading dword of each 16-byte entry; slot id == entry index). Defaults to empty.
///     spec: Docs/RE/packets/3-1_character_list.yaml (+0x58); Docs/RE/specs/frontend_scenes.md §3.3.7.
/// </param>
/// <param name="SlotFlag">
///     The server-supplied per-slot OCCUPIED / SELECTABLE flag (3/1 sub-block 3, record +0x3D0). This is the
///     byte the select window copies into its <c>+0x148C + slot</c> field — the
///     <b>occupied / selectable</b> flag that gates the <c>1/7</c> select/manage click and (together with the
///     client-side lock flag at the window's <c>+0x1548</c>, which is NOT a wire field) decides
///     selectable-for-enter. Surfaced so layer 05 has the server flag end-to-end without re-reading the raw
///     record. spec: Docs/RE/packets/3-1_character_list.yaml (sub-block 3, record +0x3D0);
///     Docs/RE/specs/frontend_scenes.md §3.4 (+0x148C = server-supplied per-slot flag).
/// </param>
/// <param name="BillingFlags">
///     The per-slot FLAGS WORD (3/1 sub-block 4, record +0x3D1, LE u32). Only bit 0 (mask 0x1) is
///     client-consumed: the character's billing / premium state, copied verbatim into the in-game player
///     panel at Enter Game. NOT a timestamp (the rename/delete-cooldown reading is REFUTED by the binary).
///     Surfaced whole so layer 05 can read bit 0 (and capture any server-set high bits). spec:
///     Docs/RE/packets/3-1_character_list.yaml (sub-block 4 — RESOLVED FlagsWord, bit 0 = billing/premium).
/// </param>
/// <param name="LockFlag">
///     The per-slot CLIENT-SIDE <b>lock / creating</b> state byte — the byte the select window holds at its
///     <c>+0x1548 + slot</c> field. Distinct from <see cref="SlotFlag" /> (the server-supplied
///     <c>+0x148C</c> occupied/selectable flag): <c>+0x1548</c> is NOT a 3/1 wire field — the original
///     populates it from the live roster (the slot-record writer sets it to <b>0</b> when a character is
///     populated; create / delete / reset paths mutate it). It drives the yaw-π facing AND blocks enter,
///     and gates the conditional <b>action-61</b> overlay button (<b>nonzero ⇒ hide</b> action 61 + flip
///     the sibling triad; <b>zero ⇒ show</b>). <b>SENSIBLE DEFAULT (FLAGGED):</b> because there is no wire
///     source on the 3/1 record, every decoded occupied slot is surfaced with <c>LockFlag = 0</c>
///     (unlocked / selectable / action-61 shown) — the faithful default the binary writes for a populated
///     slot. The exact byte semantic (occupied vs locked vs deletion-pending) is <b>OPEN@DEBUGGER</b>;
///     layer 05 may overwrite it from its own create/delete UI state. spec:
///     Docs/RE/specs/frontend_scenes.md §3.4 / §11.5c (+0x1548 lock/creating flag, action-61 gate);
///     §3.4 ("slot-record write sets it to 0 when a character is populated").
/// </param>
public readonly record struct CharacterListSlot(
    int SlotIndex,
    string Name,
    ushort Level,
    ushort ServerClass,
    uint CurrentHp,
    float PosX = 0f,
    float PosZ = 0f,
    ushort InternalClass = 0,
    byte AppearanceVariant = 0,
    ushort FaceA = 0,
    ImmutableArray<uint> EquipGids = default,
    byte SlotFlag = 0,
    uint BillingFlags = 0,
    byte LockFlag = 0)
{
    /// <summary>
    ///     True when this slot's billing / premium bit (bit 0 of <see cref="BillingFlags" />) is set — the
    ///     same bit Enter Game copies into the in-game player panel. spec:
    ///     Docs/RE/packets/3-1_character_list.yaml (sub-block 4, bit 0 = billing/premium).
    /// </summary>
    public bool IsBillingPremium => (BillingFlags & 0x1u) != 0;

    /// <summary>
    ///     True when the conditional <b>action-61</b> overlay button is shown for this slot — i.e. the
    ///     <c>+0x1548</c> <see cref="LockFlag" /> is <b>zero</b> (the original shows action 61 iff the byte
    ///     is 0, hides it when nonzero). With the flagged sensible default (<see cref="LockFlag" /> = 0 for
    ///     every decoded occupied slot) this is true unless layer 05 overrides the lock state. spec:
    ///     Docs/RE/specs/frontend_scenes.md §11.5c ("shown iff +0x1548+slot byte is 0").
    /// </summary>
    public bool IsActionConditionalVisible => LockFlag == 0;

    /// <summary>
    ///     True when this slot is <b>selectable-for-enter</b>: the client-side <c>+0x1548</c> lock is clear
    ///     AND the server-supplied <c>+0x148C</c> occupied flag (<see cref="SlotFlag" />) is set. spec:
    ///     Docs/RE/specs/frontend_scenes.md §3.4 ("selectable-for-enter = lock clear AND occupied").
    /// </summary>
    public bool IsSelectableForEnter => LockFlag == 0 && SlotFlag != 0;
}

// =====================================================================================================
// Enter-game spawn (3/7) and character-creation routing
// =====================================================================================================

/// <summary>
///     Published when the local player materializes into the world. The state-5 core path is the 4/1
///     GameStateTick world-entry seed, sourced from the slot descriptor cached at select time; the older
///     3/14 bridge still emits the same surface for keep-green compatibility. spec: Docs/RE/opcodes.md
///     (4/1 row); Docs/RE/specs/client_runtime.md §9.1.
/// </summary>
/// <param name="Key">The local player's composite actor identity (raw id + sort).</param>
/// <param name="SlotIndex">The character slot that spawned (echoed by the 3/14 result).</param>
/// <param name="Name">Decoded character name (from the cached descriptor).</param>
/// <param name="Level">Character level.</param>
/// <param name="Position">Spawn position (Q16.16, world Y forced to 0).</param>
/// <param name="CurrentHp">Current hit points at spawn.</param>
/// <param name="MaxHp">Resolved maximum hit points.</param>
/// <param name="ServerClass">Server-assigned class id (martial-arts style).</param>
/// <param name="EquipGids">
///     The local player's six visible-gear part GIDs for the fixed overlay slots
///     <c>{3,4,6,2,11,14}</c> (slot id == array index), decoded from the cached select descriptor's
///     +0x58 equip table — the SAME six-slot decode <see cref="CharacterListSlot.EquipGids" /> carries.
///     A GID of <c>0</c> is an empty slot. Defaults to empty (body-only) when no descriptor / no gear.
///     <para>
///         This is the equip-overlay INPUT the local-player render path feeds through
///         <c>EquipOverlayResolver</c> (the in-world rebuild resolver, which owns the threshold-1000 slot
///         set, the categoryBase / scale-10000 / weapon base-1000 GID math, and the 64-bit catalogue key).
///         The core carries only the decoded GIDs and never duplicates that GID/key64 math: the
///         IO-bearing catalogue lookup (GID → skin map, the data-driven categoryBase rows loaded at
///         startup) lives with the resolver's caller, so the resolution runs ONCE, in its proper home,
///         off these inputs. The weapon hand-bone-id (slot 14) stays the deferred default 0
///         (debugger-pending — never fabricated here).
///     </para>
///     spec: Docs/RE/structs/spawn_descriptor.md (+0x58 equip table);
///     Docs/RE/packets/3-1_character_list.yaml (APPEARANCE DRIVER overlays {3,4,6,2,11,14});
///     Docs/RE/specs/equipment_visuals.md §1.1 / §3 (threshold-1000 slot set, GID/key64 math) / §5
///     (weapon hand-bone-id 0 deferred).
/// </param>
public sealed record LocalPlayerSpawnedEvent(
    ActorKey Key,
    int SlotIndex,
    string Name,
    ushort Level,
    Vector3Fixed Position,
    uint CurrentHp,
    uint MaxHp,
    ushort ServerClass,
    ImmutableArray<uint> EquipGids = default) : IClientEvent;

/// <summary>
///     Published when the 4/1 world-entry tick bootstraps or repositions the local player. Carries only
///     engine-free values for the presentation world scene. spec: Docs/RE/specs/client_runtime.md
///     §9.1/§9.4 (spawn X/Z at +0x2374/+0x2378; Y forced to 0; scenario code at +0x00C).
/// </summary>
/// <param name="Key">The local player's composite actor identity.</param>
/// <param name="Position">World-entry position (Q16.16, with Y forced to 0).</param>
/// <param name="AreaId">
///     Absolute area index from the 4/1 body (offset 12) — its 3-digit decimal directory selects the
///     on-disk area. spec: Docs/RE/packets/4-1_game_state_tick.yaml
/// </param>
public sealed record InGameWorldBootstrappedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    int AreaId) : IClientEvent;

/// <summary>
///     Published when the 4/4 area-snapshot tag loop delivers a tag-4 ground-item record. Immutable
///     engine-free snapshot for the world-item spawner. The float XZ is converted to
///     <see cref="Vector3Fixed" /> at the network/application boundary (world Y forced to 0). spec:
///     Docs/RE/specs/handlers.md §4/4 (tag-4); Docs/RE/packets/4-4_ground_item_tag4.yaml.
/// </summary>
/// <param name="EntityKey">Ground-item entity key (the pickup / remove target — wire +0x00).</param>
/// <param name="TemplateId">Item template id resolving the per-template 3D model (wire +0x04).</param>
/// <param name="Position">World position (Q16.16, Y forced to 0; wire WorldX +0x10 / WorldZ +0x14).</param>
public sealed record GroundItemSpawnedEvent(
    uint EntityKey,
    uint TemplateId,
    Vector3Fixed Position) : IClientEvent;

/// <summary>
///     Published when the 4/4 area-snapshot tag loop delivers a tag-6 guild-name overlay record. The
///     overlay attaches a guild name to an already-spawned actor. Immutable engine-free snapshot. spec:
///     Docs/RE/specs/handlers.md §4/4 (tag-6, 36-byte record).
/// </summary>
/// <param name="EntityId">Composite-key actor lookup id (wire +0x00).</param>
/// <param name="GuildName">Decoded CP949 guild name (NUL-terminated cell at wire +0x05).</param>
public sealed record GuildOverlayEvent(
    uint EntityId,
    string GuildName) : IClientEvent;

/// <summary>
///     Published when the 4/4 area-snapshot tag loop delivers a tag-9 title / relation overlay record.
///     The overlay attaches a title and a relation marker to an already-spawned actor. The
///     <paramref name="RelationState" /> and <paramref name="OverlaySubCode" /> are forwarded RAW: their
///     value meanings are live-pending (world_entry.md §4 / handlers.md §4/4). Immutable engine-free
///     snapshot. spec: Docs/RE/specs/handlers.md §4/4 (tag-9, 24-byte record).
/// </summary>
/// <param name="EntityId">Actor lookup id (wire +0x00).</param>
/// <param name="RelationState">Raw relation-state byte (wire +0x04) — value meaning live-pending.</param>
/// <param name="OverlaySubCode">Raw overlay sub-code byte (wire +0x05) — value meaning live-pending.</param>
/// <param name="TitleName">Decoded CP949 title name (17-byte cell at wire +0x06).</param>
public sealed record TitleOverlayEvent(
    uint EntityId,
    byte RelationState,
    byte OverlaySubCode,
    string TitleName) : IClientEvent;

/// <summary>
///     Published once the 4/4 area-snapshot tag loop drains (the zero terminator or a short read ends
///     it) — the area is "populated". Carries the area-centre recenter coordinates from the snapshot
///     header and the count of actor records spawned this snapshot, for the render side. Immutable
///     engine-free snapshot. spec: Docs/RE/specs/handlers.md §4/4.
/// </summary>
/// <param name="AreaCentreX">Area-centre X from the 17-byte header (wire +0x0D, f32).</param>
/// <param name="AreaCentreZ">Area-centre Z from the 17-byte header (wire +0x09, f32).</param>
/// <param name="SpawnedActorCount">Number of tag-1/2/3 actor records spawned this snapshot.</param>
public sealed record AreaPopulatedEvent(
    float AreaCentreX,
    float AreaCentreZ,
    int SpawnedActorCount) : IClientEvent;

/// <summary>
///     Published when the enter-game spawn fails (3/14 SmsgCharSpawnResponse, Result == 0). The presentation
///     shows a timed failure message and may return to the character-select screen. Immutable snapshot.
///     spec: Docs/RE/specs/login_flow.md §5.3 (Result 0 = failure, timed message shown).
/// </summary>
/// <param name="SlotIndex">The character slot the failed spawn targeted (echoed by the 3/7 result).</param>
public sealed record LocalPlayerSpawnFailedEvent(byte SlotIndex) : IClientEvent;

/// <summary>
///     Published when the player confirms an <b>empty</b> character slot (descriptor name == "@BLANK@"):
///     the flow routes to character creation instead of sending the 1/9 enter-game request. Immutable
///     snapshot. spec: Docs/RE/specs/login_flow.md §3.3 / §3.5 (blank slot routes to creation).
/// </summary>
/// <param name="SlotIndex">The empty slot index the player picked (0..4).</param>
public sealed record CreateCharacterRequestedEvent(int SlotIndex) : IClientEvent;

// =====================================================================================================
// 4/1 world-entry interior snapshot (roster / scene-entity / hotbar)
// =====================================================================================================

/// <summary>
///     One WorldEntryTableA roster member, decoded from a 16-byte record in the 4/1 world-entry tick.
///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A 16-byte record); Docs/RE/specs/world_systems.md §13.3.
/// </summary>
/// <param name="ActorId">Actor id (record +4; nonzero = occupied slot).</param>
/// <param name="KeepGuard">Keep-guard dword (record +8); doubles as the displayed roster member number.</param>
/// <param name="Aux">Auxiliary dword (record +12; value meaning capture-pending).</param>
public readonly record struct RosterMember(uint ActorId, uint KeepGuard, uint Aux);

/// <summary>
///     Published after the 4/1 world-entry bootstrap with the decoded WorldEntryTableA roster (the
///     non-empty stale-slot-sweep members). Immutable engine-free snapshot. spec:
///     Docs/RE/packets/4-1_game_state_tick.yaml (Table A note); Docs/RE/specs/world_systems.md §13.3.
/// </summary>
/// <param name="Members">The non-empty roster members (ActorId != 0) from the 120-record sweep.</param>
public sealed record RosterSnapshotEvent(ImmutableArray<RosterMember> Members) : IClientEvent;

/// <summary>
///     One WorldEntryTableB category entry (8 bytes): a category selector and its value. spec:
///     Docs/RE/packets/4-1_game_state_tick.yaml (Table B category entry); Docs/RE/specs/world_systems.md §13.3.
/// </summary>
/// <param name="Category">Category selector dword (entry +0).</param>
/// <param name="Value">Category value (entry +4; value meaning capture-pending).</param>
public readonly record struct SceneCategoryEntry(uint Category, int Value);

/// <summary>
///     Published after the 4/1 world-entry bootstrap with the decoded WorldEntryTableB scene-entity state:
///     the non-empty 16-byte actor-slot records plus the 21 category entries. Immutable engine-free
///     snapshot. spec: Docs/RE/specs/world_systems.md §13.3; Docs/RE/packets/4-1_game_state_tick.yaml (Table B note).
/// </summary>
/// <param name="ActorSlots">The non-empty (ActorId != 0) actor-slot records (same shape as the roster).</param>
/// <param name="Categories">The 21 category entries that follow the actor-slot subregion (after the 20-byte gap).</param>
public sealed record SceneEntitySnapshotEvent(
    ImmutableArray<RosterMember> ActorSlots,
    ImmutableArray<SceneCategoryEntry> Categories) : IClientEvent;

/// <summary>
///     One populated 4/1 HotbarSlots entry (8 bytes): the slot index, its catalogue EntryKey and quantity.
///     There is NO inline type byte — skill-vs-item is data-driven (category value 5 = skill via the skill
///     catalogue), which is NOT resolved here, so the EntryKey is carried RAW (category-pending). spec:
///     Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).
/// </summary>
/// <param name="SlotIndex">Hotbar slot index (0..239).</param>
/// <param name="EntryKey">Catalogue entry key (slot +0; nonzero = occupied). Skill-vs-item is category-pending.</param>
/// <param name="Count">Quantity / charge (slot +4).</param>
public readonly record struct HotbarSlotEntry(int SlotIndex, uint EntryKey, ushort Count);

/// <summary>
///     Published after the 4/1 world-entry bootstrap with the decoded HotbarSlots (the non-empty slots).
///     The skill-vs-item classification is category-pending (no skill-category resolver is injected into
///     Application), so the raw EntryKey + Count are carried. Immutable engine-free snapshot. spec:
///     Docs/RE/specs/world_entry.md (hotbar init); Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).
/// </summary>
/// <param name="Slots">The non-empty (EntryKey != 0) hotbar slots.</param>
public sealed record HotbarInitializedEvent(ImmutableArray<HotbarSlotEntry> Slots) : IClientEvent;

// =====================================================================================================
// 4/4 actor visual refresh (KindByte == 5)
// =====================================================================================================

/// <summary>
///     Published when a 4/4 area-entity-snapshot tag-1/2/3 actor record has KindByte == 5 (visual-only
///     refresh) AND the actor already exists: a lightweight weapon/joint visual refresh, NOT a respawn (the
///     actor is NOT removed/rebuilt). Layer 05 refreshes the weapon/joint visual off this event. Immutable
///     engine-free snapshot. spec: Docs/RE/structs/actor.md (4/4 record: KindByte == 5 visual-only refresh).
/// </summary>
/// <param name="Key">The already-spawned actor's composite identity (id + tag-byte sort).</param>
/// <param name="RelationVisual">Raw RelationVisual byte (record +0x05) — value meaning capture-pending.</param>
public sealed record ActorVisualRefreshedEvent(ActorKey Key, byte RelationVisual) : IClientEvent;

// =====================================================================================================
// 5/10 death / 4/13 local-player state sync
// =====================================================================================================

/// <summary>
///     Published when an actor dies (5/10 SmsgCharDeath). Layer 05 plays the death motion/effect and opens
///     the respawn modal off this event. PK effects (causes 1, 2) anchor on the VICTIM pair. The
///     respawn-modal mode (1 vs 3), effect/sound/message ids and death-penalty magnitudes are NON-blocking
///     value residuals and are not modelled. Immutable engine-free snapshot. spec:
///     Docs/RE/packets/5-10_combat_death.yaml; Docs/RE/structs/actor.md (death-state fields).
/// </summary>
/// <param name="VictimKey">The dying actor's composite identity (VictimId + VictimSort).</param>
/// <param name="KillerKey">The killer's composite identity (KillerId + KillerSort).</param>
/// <param name="DeathCause">0 = normal; 1 = PK type A; 2 = PK type B; 3 = special / no-modal.</param>
/// <param name="IsPkA">True when <paramref name="DeathCause" /> == 1 (PK type A; anchors on the victim).</param>
/// <param name="IsPkB">True when <paramref name="DeathCause" /> == 2 (PK type B; anchors on the victim).</param>
/// <param name="IsLocalPlayer">True when the victim is the local player (opens the respawn modal).</param>
public sealed record ActorDiedEvent(
    ActorKey VictimKey,
    ActorKey KillerKey,
    int DeathCause,
    bool IsPkA,
    bool IsPkB,
    bool IsLocalPlayer) : IClientEvent;

/// <summary>
///     Published when the local player's state is synced (4/13 SmsgLocalPlayerStateSync). Carries the synced
///     position (Q16.16, Y forced 0), the heading, and the sync-mode byte (wire offset 33). Mode 1 =
///     BattleController sync (stance indicators); mode 5 = no state write. A &gt; 200-unit delta is a
///     teleport (the actor was snapped). Immutable engine-free snapshot. spec:
///     Docs/RE/packets/4-13_local_player_state_sync.yaml.
/// </summary>
/// <param name="Key">The local player's composite identity.</param>
/// <param name="Position">Synced position (Q16.16, Y forced 0; X @+0x10 / Z @+0x14).</param>
/// <param name="Heading">Heading / yaw as a raw Q16.16 scalar (wire +0x0C).</param>
/// <param name="Mode">Sync-mode byte (wire offset 33): 0 = set-position; 1 = BattleController sync; 5 = no write.</param>
public sealed record LocalPlayerStateSyncedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    int Heading,
    byte Mode) : IClientEvent;