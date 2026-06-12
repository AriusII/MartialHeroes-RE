using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Tests the combat-stat recompute shim (equip change -> stats change) and the engine-loop integration of
/// the per-tick cooldown / buff / cast-state advances.
/// </summary>
public sealed class CombatRecomputeAndLoopTickTests
{
    // -------------------------------------------------------------------------
    // Combat-stat recompute
    // -------------------------------------------------------------------------

    [Fact]
    public void Recompute_aggregates_server_base_plus_equipment_into_primary_stats()
    {
        var bases = new PrimaryStatServerBases(Str: 10, Dex: 5, Agi: 3, Con: 8, Int: 2);

        // No equipment -> stats equal the server bases.
        CombatStats before = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: ReadOnlySpan<BuffStatGrant>.Empty,
            wornEquipment: ReadOnlySpan<SlottedEquipmentContribution>.Empty,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty);

        Assert.Equal(10, before.Str);
        Assert.Equal(8, before.Vital); // CON -> Vital
        Assert.Equal(2, before.Inte);

        // Equip a STR +20 item in slot 0 -> STR rises by 20.
        var worn = new[]
        {
            new SlottedEquipmentContribution(0, new EquipmentContribution(StatKey.Str, 20)),
        };
        CombatStats after = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: ReadOnlySpan<BuffStatGrant>.Empty,
            wornEquipment: worn,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty);

        Assert.Equal(30, after.Str); // 10 base + 20 equip
        Assert.NotEqual(before.Str, after.Str); // equip change -> stats change
    }

    [Fact]
    public void Recompute_applies_slot8_skip_so_excluded_equipment_does_not_count()
    {
        var bases = new PrimaryStatServerBases(Str: 10, Dex: 0, Agi: 0, Con: 0, Int: 0);

        // A STR +50 grant in the excluded stat slot (8) must be dropped. spec: combat.md §2.1.
        var worn = new[]
        {
            new SlottedEquipmentContribution(EquipSlots.StatExcludedSlot, new EquipmentContribution(StatKey.Str, 50)),
        };
        CombatStats stats = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: ReadOnlySpan<BuffStatGrant>.Empty,
            wornEquipment: worn,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty);

        Assert.Equal(10, stats.Str); // slot-8 grant skipped
    }

    [Fact]
    public void Recompute_active_buff_contributes_but_expired_buff_does_not()
    {
        var bases = new PrimaryStatServerBases(Str: 10, Dex: 0, Agi: 0, Con: 0, Int: 0);

        var activeBuff = new BuffDebuff { EffectCode = 1, DurationTicks = 100 };
        var expiredBuff = new BuffDebuff { EffectCode = 1, DurationTicks = 0 };

        CombatStats withActive = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: new[] { new BuffStatGrant(activeBuff, StatKey.Str, 7) },
            wornEquipment: ReadOnlySpan<SlottedEquipmentContribution>.Empty,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty);

        CombatStats withExpired = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: new[] { new BuffStatGrant(expiredBuff, StatKey.Str, 7) },
            wornEquipment: ReadOnlySpan<SlottedEquipmentContribution>.Empty,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty);

        Assert.Equal(17, withActive.Str); // 10 + 7 active buff
        Assert.Equal(10, withExpired.Str); // expired buff contributes nothing
    }

    [Fact]
    public void Recompute_composes_attack_and_hit_ratings()
    {
        var bases = new PrimaryStatServerBases(Str: 100, Dex: 100, Agi: 100, Con: 100, Int: 100);

        CombatStats stats = CombatStatsRecomputer.Recompute(
            in bases,
            buffGrants: ReadOnlySpan<BuffStatGrant>.Empty,
            wornEquipment: ReadOnlySpan<SlottedEquipmentContribution>.Empty,
            setPieces: ReadOnlySpan<SetPieceContribution>.Empty,
            modifierSlots: ReadOnlySpan<ModifierSlotContribution>.Empty,
            ratingTerms: new CombatRatingTerms { LevelTerm = 20 });

        // Both ratings come straight from CombatFormula; just assert they were composed (non-zero).
        Assert.True(stats.AttackRating > 0);
        Assert.True(stats.HitRating > 0);
    }

    // -------------------------------------------------------------------------
    // Engine-loop tick integration
    // -------------------------------------------------------------------------

    private static GameEngineLoop NewLoop(out LocalPlayerState local, out ClientEventBus bus)
    {
        bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var inputBus = new InputBus();
        local = new LocalPlayerState();
        // 1000 Hz -> 1 ms per tick, so the ms clock advances exactly with the tick count.
        return new GameEngineLoop(world, bus, inputBus, tickRateHz: 1000, localPlayer: local);
    }

    [Fact]
    public void Loop_ticks_cooldown_table_to_expiry()
    {
        GameEngineLoop loop = NewLoop(out LocalPlayerState local, out _);

        // Arm a 3 ms cooldown at t=0. spec: skills.md §4.
        var skill = new SkillId(1);
        local.SetHotbarSlot(0, skill, points: 1, cooldownDurationMs: 3);
        local.Cooldowns.Arm(skill, now: 0);
        Assert.False(local.Cooldowns.CheckReady(skill, now: 0));

        // Step 4 ms (4 ticks at 1000 Hz) -> the cooldown expired.
        for (int i = 0; i < 4; i++)
        {
            loop.StepOnce();
        }

        Assert.True(local.Cooldowns[0].IsReady);
    }

    [Fact]
    public void Loop_ticks_buff_table_duration_down()
    {
        GameEngineLoop loop = NewLoop(out LocalPlayerState local, out _);

        // Apply a 2-tick buff. spec: skills.md §6.3 (single decrement per tick).
        local.Buffs.Apply(slotIndex: 0, effectCode: 47, durationTicks: 2, param: 0, magnitude: 0);
        Assert.True(local.Buffs[0].IsActive);

        loop.StepOnce(); // 2 -> 1
        Assert.True(local.Buffs[0].IsActive);

        loop.StepOnce(); // 1 -> 0 (expired)
        Assert.False(local.Buffs[0].IsActive);
    }

    [Fact]
    public void Loop_returns_cast_state_to_idle_after_cooldown_phase()
    {
        GameEngineLoop loop = NewLoop(out LocalPlayerState local, out _);

        // Put the cast state directly into a 2 ms cooldown phase. spec: skills.md §4 / §5.2.
        local.CastState = new SkillCastState
        {
            Phase = SkillCastPhase.Cooldown,
            ActiveSkill = new SkillId(1),
            CooldownEndMs = 2,
        };

        loop.StepOnce(); // t = 1 ms, still cooling
        Assert.Equal(SkillCastPhase.Cooldown, local.CastState.Phase);

        loop.StepOnce(); // t = 2 ms, cooldown end reached -> idle
        Assert.Equal(SkillCastPhase.Idle, local.CastState.Phase);
    }

    [Fact]
    public void Loop_without_local_player_state_still_ticks_world()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var inputBus = new InputBus();
        // No LocalPlayerState wired -> the skill-subsystem tick is a clean no-op.
        var loop = new GameEngineLoop(world, bus, inputBus, tickRateHz: 30);

        WorldSnapshotEvent snapshot = loop.StepOnce();

        Assert.Equal(0, snapshot.Tick);
        Assert.Equal(1, loop.TickCount);
    }
}