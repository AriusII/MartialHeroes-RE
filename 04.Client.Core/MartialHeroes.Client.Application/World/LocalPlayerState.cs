using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     The application-owned holder for the local player's combat / skill subsystems that have no place on
///     the <see cref="Actor" /> aggregate: the 240-slot skill hotbar, the
///     parallel cooldown ("recast") table, the 31-slot buff/status table, the single cast state machine, and
///     the most-recent recomputed combat-stat aggregate.
/// </summary>
/// <remarks>
///     <para>
///         The Domain owns the deterministic <em>rules</em> for each of these (<see cref="CooldownTable" />,
///         <see cref="BuffTable" />, <see cref="SkillCastState" />, <see cref="StatAggregation" />); this holder is
///         pure orchestration plumbing — it groups the live instances so the inbound handlers can mutate them and
///         the <see cref="MartialHeroes.Client.Application.Engine.GameEngineLoop" /> can tick them once per fixed
///         tick. spec: Docs/RE/specs/skills.md §4 (cooldown), §6 (buff table), §2/§5 (cast state).
///     </para>
///     <para>
///         <b>Threading.</b> Like <see cref="ClientWorld" />, this is mutated only by the single network-reader /
///         loop logical owner; it is deliberately lock-free.
///     </para>
/// </remarks>
public sealed class LocalPlayerState
{
    /// <summary>The 240-slot skill hotbar (parallel to the cooldown table). spec: skills.md §4 / structs/skill.md (240).</summary>
    public const int HotbarSlotCount = 240;

    private readonly SkillId[] _hotbar = new SkillId[HotbarSlotCount];
    private readonly short[] _hotbarPoints = new short[HotbarSlotCount];

    /// <summary>The 240-slot cooldown table keyed by hotbar slot index. spec: skills.md §4.</summary>
    public CooldownTable Cooldowns { get; } = new();

    /// <summary>The 31-slot per-actor buff/status table for the local player. spec: skills.md §6.1.</summary>
    public BuffTable Buffs { get; } = new();

    /// <summary>The local player's single cast state machine. spec: skills.md §2 / §5.</summary>
    public SkillCastState CastState { get; set; } = SkillCastState.Idle;

    /// <summary>The most recently recomposed derived combat-stat aggregate. spec: combat.md §1 / §2.</summary>
    public CombatStats Combat { get; set; } = CombatStats.Empty;

    // -------------------------------------------------------------------------
    // FIX 4 — enter-time local-player seed (the 1/9 send site)
    // -------------------------------------------------------------------------
    // The binary's CharSelect-leave handler (SelectWindow_EnterGame @0x546256) seeds three local-player
    // globals IMMEDIATELY AFTER the 1/9 send (Cmsg_EnterGame_Send(&Src) @0x5463ca) and the scene teardown
    // (MainWindow_SceneTeardown @0x5463d8), using the CHOSEN slot index (*(_DWORD*)(this+6120)):
    //   memcpy(::Src/*0x7AC094*/, this+568 + 880*slot, 0x370)                 @0x5463fb (880-byte descriptor)
    //   memcpy(&dword_7AC404, this+4968 + 96*slot, 0x60)                      @0x546418 (96-byte stats)
    //   byte_7AC393     = HIBYTE(NetHandler[220*slot+206])                    @0x546438 (status byte)
    //   g_LocalPlayerLevel = NetHandler[220*slot+207]                         @0x546455 (level)
    // The descriptor/stats memcpys are already mirrored by CharacterSelectionStore.Chosen (the cached
    // 880-byte descriptor + 96-byte stats), so this holder only needs to carry the two ROSTER-sourced
    // scalars (byte_7AC393 status, g_LocalPlayerLevel) that the descriptor decode does NOT reproduce, plus
    // the chosen slot index, so the later 4/1 spawn agrees with the enter-time choice instead of
    // re-deriving level independently. IDA evidence: SelectWindow_EnterGame @0x546256
    // (status @0x546438, level @0x546455; chosen slot = this+6120).

    /// <summary>
    ///     The chosen character slot index seeded at the 1/9 enter send (<c>*(_DWORD*)(this+6120)</c> in
    ///     <c>SelectWindow_EnterGame @0x546256</c>), or <c>-1</c> when no enter has been requested this
    ///     session. spec: SelectWindow_EnterGame @0x546256 (chosen slot = this+6120).
    /// </summary>
    public int ChosenSlotIndex { get; private set; } = -1;

    /// <summary>
    ///     The local player's level seeded at the 1/9 enter send. The binary reads this from the NetHandler
    ///     roster (<c>g_LocalPlayerLevel = NetHandler[220*slot+207]</c> @0x546455) — a ROSTER-sourced field.
    ///     The C# port caches the chosen DESCRIPTOR (not the NetHandler roster row), so this is seeded from
    ///     the descriptor's <c>+0x3A</c> level instead (the only available authority); the roster row is the
    ///     true binary source. spec: SelectWindow_EnterGame @0x546455 (g_LocalPlayerLevel = NetHandler[..207]);
    ///     Docs/RE/structs/spawn_descriptor.md (+0x3A level).
    /// </summary>
    public ushort Level { get; private set; }

    /// <summary>
    ///     The local player's status byte seeded at the 1/9 enter send. The binary sources this ONLY from the
    ///     NetHandler roster (<c>byte_7AC393 = HIBYTE(NetHandler[220*slot+206])</c> @0x546438) — there is NO
    ///     descriptor carrier for it, and the C# port does not retain the NetHandler roster row. It is
    ///     therefore seeded as the supplied value (default <c>0</c> + a logged gap when no roster status is
    ///     threaded through), never fabricated. spec: SelectWindow_EnterGame @0x546438
    ///     (byte_7AC393 = HIBYTE(NetHandler[220*slot+206]) — roster-sourced, no descriptor carrier).
    /// </summary>
    public byte StatusByte { get; private set; }

    /// <summary>
    ///     <see langword="true" /> once <see cref="SeedEnterChoice" /> has run for this session's enter (the
    ///     1/9 emit). Lets the 4/1 spawn path detect that an enter-time seed is present.
    /// </summary>
    public bool HasEnterSeed { get; private set; }

    /// <summary>
    ///     FIX 4 — seeds the enter-time local-player choice at the 1/9 send site. Mirrors the binary's
    ///     post-1/9 global seeding in <c>SelectWindow_EnterGame @0x546256</c>: the chosen slot index, the
    ///     level, and the roster status byte. The descriptor/stats memcpys (0x7AC094 / 0x7AC404) are already
    ///     mirrored by the cached descriptor in <see cref="MartialHeroes.Client.Application.UseCases.CharacterSelectionStore.Chosen" />,
    ///     so this only carries the two roster scalars + the slot. The <paramref name="statusByte" /> has no
    ///     descriptor carrier (binary reads <c>HIBYTE(NetHandler[220*slot+206])</c> @0x546438) — pass the
    ///     real roster value when available, else <c>0</c> (never fabricated; the caller logs the gap).
    ///     spec: SelectWindow_EnterGame @0x546256 (@0x546438 status, @0x546455 level, this+6120 slot).
    /// </summary>
    /// <param name="slotIndex">The chosen slot index (the 1/9 SlotIndex; binary <c>this+6120</c>).</param>
    /// <param name="level">The chosen character level (descriptor +0x3A; binary reads NetHandler[..207]).</param>
    /// <param name="statusByte">The roster status byte (binary HIBYTE(NetHandler[..206]); 0 when unavailable).</param>
    public void SeedEnterChoice(int slotIndex, ushort level, byte statusByte)
    {
        ChosenSlotIndex = slotIndex;
        Level = level;
        StatusByte = statusByte;
        HasEnterSeed = true;
    }

    /// <summary>The skill currently occupying hotbar <paramref name="slot" /> (<see cref="SkillId.None" /> when empty).</summary>
    public SkillId HotbarSkill(int slot)
    {
        return _hotbar[slot];
    }

    /// <summary>The skill-point allocation for hotbar <paramref name="slot" />.</summary>
    public short HotbarPoints(int slot)
    {
        return _hotbarPoints[slot];
    }

    /// <summary>
    ///     Writes the skill + points + cooldown duration into hotbar <paramref name="slot" /> and mirrors the
    ///     skill id and duration into the parallel cooldown table (leaving the slot ready). Mirrors the 5/33
    ///     authoritative server overwrite + the §4 duration-table rebuild. spec: skills.md §4 / structs/skill.md (5/33).
    /// </summary>
    public void SetHotbarSlot(int slot, SkillId skill, short points, int cooldownDurationMs)
    {
        if ((uint)slot >= (uint)_hotbar.Length) throw new ArgumentOutOfRangeException(nameof(slot));

        _hotbar[slot] = skill;
        _hotbarPoints[slot] = points;
        Cooldowns.SetSlot(slot, skill, cooldownDurationMs);
    }
}