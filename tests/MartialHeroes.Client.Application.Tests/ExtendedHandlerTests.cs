using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Drives the newly-wired S2C packets (4/12, 4/22, 4/19, 5/33, 4/41, 4/150, 5/31, 5/67, 4/100, 5/7, 3/1)
/// through the dispatcher into <see cref="GamePacketHandler"/>, asserting Domain / LocalPlayerState
/// mutation, the stat recompute, and the published events.
/// </summary>
public sealed class ExtendedHandlerTests
{
    private static List<IClientEvent> Drain(ClientEventBus bus)
    {
        var events = new List<IClientEvent>();
        while (bus.Reader.TryRead(out IClientEvent? e))
        {
            events.Add(e);
        }

        return events;
    }

    private static (GamePacketHandler Handler, InboundFrameDispatcher Dispatcher, ClientEventBus Bus, ClientWorld World, LocalPlayerState Local)
        NewHarness(
            Func<CombatStats, CombatStats>? recompute = null,
            Func<SkillId, int>? cooldownResolver = null,
            ClientState initial = ClientState.World)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, initial);
        var local = new LocalPlayerState();
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink(), localPlayer: local)
        {
            CombatStatsRecompute = recompute,
            CooldownDurationResolver = cooldownResolver,
        };
        var dispatcher = new InboundFrameDispatcher(handler);
        return (handler, dispatcher, bus, world, local);
    }

    [Fact]
    public void EquipResult_4_12_success_recomputes_stats_and_publishes()
    {
        bool recomputed = false;
        var (_, dispatcher, bus, world, _) = NewHarness(
            recompute: s => { recomputed = true; return s with { AttackRating = 123 }; });
        var key = new ActorKey(9, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        dispatcher.RouteNow(SyntheticFrames.EquipResult(result: 1, fromSlot: 2, toSlot: 15));

        Assert.True(recomputed);
        List<IClientEvent> events = Drain(bus);
        var equip = Assert.Single(events.OfType<EquipResultEvent>());
        Assert.True(equip.Success);
        Assert.Equal((byte)2, equip.FromSlot);
        Assert.True(equip.TitleVisualRebuild); // toSlot == 15
        var recompose = Assert.Single(events.OfType<CombatStatsRecomputedEvent>());
        Assert.Equal(123, recompose.Stats.AttackRating);
    }

    [Fact]
    public void EquipResult_4_12_failure_does_not_recompute()
    {
        bool recomputed = false;
        var (_, dispatcher, bus, world, _) = NewHarness(recompute: s => { recomputed = true; return s; });
        var key = new ActorKey(9, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        dispatcher.RouteNow(SyntheticFrames.EquipResult(result: 0, fromSlot: 2, toSlot: 3));

        Assert.False(recomputed);
        var equip = Assert.Single(Drain(bus).OfType<EquipResultEvent>());
        Assert.False(equip.Success);
        Assert.False(equip.TitleVisualRebuild);
    }

    [Fact]
    public void ItemSlotState_4_22_publishes_bonus_fields()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.ItemSlotState(
            result: 1, fromSlot: 3, toSlot: 7, bonus1: 11, bonus2: 22, bonus3: 33));

        var ev = Assert.Single(Drain(bus).OfType<ItemSlotStateEvent>());
        Assert.True(ev.Success);
        Assert.Equal((byte)3, ev.FromSlot);
        Assert.Equal((byte)7, ev.ToSlot);
        Assert.Equal(11, ev.BonusField1);
        Assert.Equal(33, ev.BonusField3);
    }

    [Fact]
    public void NpcAcquire_4_19_publishes_outcome()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.NpcAcquire(
            result: 1, reason: 0, bagSlot: 5, itemActorId: 7777, goldLo: 1500));

        var ev = Assert.Single(Drain(bus).OfType<NpcAcquireResultEvent>());
        Assert.True(ev.Success);
        Assert.Equal((byte)5, ev.BagSlotIndex);
        Assert.Equal(7777, ev.ItemActorId);
        Assert.Equal(1500, ev.GoldLow);
    }

    [Fact]
    public void HotbarSlotSet_5_33_writes_hotbar_and_arms_cooldown_duration()
    {
        var (_, dispatcher, bus, _, local) = NewHarness(cooldownResolver: _ => 5000);

        dispatcher.RouteNow(SyntheticFrames.HotbarSlotSet(
            sort: 1, actorId: 1, hotbarSlot: 4, skillId: 4242, points: 3));

        // Hotbar mutated.
        Assert.Equal(new SkillId(4242), local.HotbarSkill(4));
        Assert.Equal((short)3, local.HotbarPoints(4));
        // Cooldown slot now mirrors the skill + duration (ready until armed). spec: skills.md §4.
        Assert.Equal(new SkillId(4242), local.Cooldowns[4].Skill);
        Assert.Equal(5000, local.Cooldowns[4].DurationMs);

        var ev = Assert.Single(Drain(bus).OfType<SkillHotbarSlotSetEvent>());
        Assert.Equal((byte)4, ev.HotbarSlot);
        Assert.Equal(new SkillId(4242), ev.Skill);
        Assert.Equal((short)3, ev.SkillPoints);
    }

    [Fact]
    public void HotbarAssignResult_4_41_maps_gate_to_success()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.HotbarAssignResult(
            gate: 1, resultCode: 0, hotbarSlotEcho: 6, skillIdEcho: 99, pool: 12));

        var ev = Assert.Single(Drain(bus).OfType<SkillHotbarAssignResultEvent>());
        Assert.True(ev.Success);
        Assert.Equal(6, ev.HotbarSlot);
        Assert.Equal(new SkillId(99), ev.Skill);
        Assert.Equal(12u, ev.SkillPointPool);
    }

    [Fact]
    public void SkillPointUpdate_4_150_mode2_updates_level_and_recomputes()
    {
        bool recomputed = false;
        var (_, dispatcher, bus, world, _) = NewHarness(recompute: s => { recomputed = true; return s; });
        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        dispatcher.RouteNow(SyntheticFrames.SkillPointUpdate(mode: 2, value: 24)); // level-up notice -> level 24

        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(24, actor.Level);
        Assert.True(recomputed);
        var ev = Assert.Single(Drain(bus).OfType<SkillPointUpdateEvent>());
        Assert.Equal(2u, ev.Mode);
        Assert.Equal(24u, ev.Value);
    }

    [Fact]
    public void SkillPointUpdate_4_150_mode1_sets_pool_without_level_change()
    {
        var (_, dispatcher, bus, world, _) = NewHarness();
        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 5, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        dispatcher.RouteNow(SyntheticFrames.SkillPointUpdate(mode: 1, value: 99));

        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(5, actor.Level); // unchanged on mode 1
        var ev = Assert.Single(Drain(bus).OfType<SkillPointUpdateEvent>());
        Assert.Equal(1u, ev.Mode);
        Assert.Equal(99u, ev.Value);
    }

    [Fact]
    public void BuffSlotUpdate_5_31_local_player_applies_to_buff_table_and_recomputes()
    {
        bool recomputed = false;
        var (_, dispatcher, bus, world, local) = NewHarness(recompute: s => { recomputed = true; return s; });
        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        dispatcher.RouteNow(SyntheticFrames.BuffSlotUpdate(
            sort: 1, actorId: 7, slot: 3, effectCode: 47, duration: 100, extra: 9));

        // Local buff table slot 3 now active. spec: skills.md §6.1.
        Assert.Equal(47, local.Buffs[3].EffectCode);
        Assert.Equal(100, local.Buffs[3].DurationTicks);
        Assert.True(local.Buffs[3].IsActive);
        Assert.True(recomputed);

        var ev = Assert.Single(Drain(bus).OfType<BuffSlotChangedEvent>());
        Assert.Equal(3, ev.SlotIndex);
        Assert.Equal(47, ev.EffectCode);
        Assert.Equal(100, ev.DurationTicks);
    }

    [Fact]
    public void BuffSlotUpdate_5_31_remote_actor_does_not_touch_local_buff_table()
    {
        var (_, dispatcher, bus, world, local) = NewHarness();
        var localKey = new ActorKey(7, EntitySort.PlayerCharacter);
        world.LocalActorKey = localKey;

        // A buff for a different (remote) actor.
        dispatcher.RouteNow(SyntheticFrames.BuffSlotUpdate(
            sort: 1, actorId: 99, slot: 3, effectCode: 47, duration: 100, extra: 0));

        Assert.False(local.Buffs[3].IsActive); // local table untouched
        var ev = Assert.Single(Drain(bus).OfType<BuffSlotChangedEvent>());
        Assert.Equal(new ActorKey(99, EntitySort.PlayerCharacter), ev.Key);
    }

    [Fact]
    public void StatsUpdate_5_67_publishes_neutral_slots_and_xp()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.StatsUpdate(
            sort: 1, actorId: 7, stat0: 100, stat2: 200, currentXp: 123456789L, stat6: 6, stat4: 4, stat5: 5));

        var ev = Assert.Single(Drain(bus).OfType<ActorStatSyncEvent>());
        Assert.Equal(100u, ev.Stat0);
        Assert.Equal(200u, ev.Stat2);
        Assert.Equal(4u, ev.Stat4);
        Assert.Equal(5u, ev.Stat5);
        Assert.Equal(6u, ev.Stat6);
        Assert.Equal(123456789L, ev.CurrentXp);
    }

    [Fact]
    public void CombatAttackUpdate_4_100_decodes_phase_and_value()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.CombatAttackUpdate(phase: 3, subKind: -1, value: 4096));

        var ev = Assert.Single(Drain(bus).OfType<CombatAttackUpdateEvent>());
        Assert.Equal((byte)3, ev.Phase);
        Assert.Equal((sbyte)-1, ev.SubKind);
        Assert.Equal(4096u, ev.Value);
        Assert.True(ev.ChargeStarted);
        Assert.False(ev.ChargeEnded);
    }

    [Fact]
    public void ChatBroadcast_5_7_decodes_sender_and_text()
    {
        var (_, dispatcher, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.ChatBroadcast(
            senderSort: 1, senderId: 55, channel: 6, contextId: 99, senderName: "Master", text: "hello world"));

        var ev = Assert.Single(Drain(bus).OfType<ChatBroadcastEvent>());
        Assert.Equal("Master", ev.SenderName);
        Assert.Equal("hello world", ev.Text);
        Assert.Equal((byte)6, ev.Channel);
        Assert.Equal(99u, ev.ContextId);
        Assert.Equal(new ActorKey(55, EntitySort.PlayerCharacter), ev.SenderKey);
    }

    [Fact]
    public void CharacterList_3_1_decodes_per_slot_records_and_switches_select_screen()
    {
        var (_, dispatcher, bus, _, _) = NewHarness(initial: ClientState.Login);

        dispatcher.RouteNow(SyntheticFrames.CharacterList(
            serverId: 1, channelId: 2,
            (Slot: 0, Name: "Alpha", Level: (ushort)10, Hp: 300u, Class: (ushort)3),
            (Slot: 2, Name: "Beta", Level: (ushort)20, Hp: 600u, Class: (ushort)5)));

        List<IClientEvent> events = Drain(bus);
        var list = Assert.Single(events.OfType<CharacterListEvent>());
        Assert.Equal((byte)1, list.ServerId);
        Assert.Equal(2, list.Characters.Length);
        Assert.Equal("Alpha", list.Characters[0].Name);
        Assert.Equal(0, list.Characters[0].SlotIndex);
        Assert.Equal("Beta", list.Characters[1].Name);
        Assert.Equal(2, list.Characters[1].SlotIndex);
        Assert.Equal((ushort)20, list.Characters[1].Level);

        // FSM moved Login -> CharacterSelection. spec: opcodes.md (3/1 switches to select screen).
        var fsmEvent = Assert.Single(events.OfType<ClientStateChangedEvent>());
        Assert.Equal(ClientState.CharacterSelection, fsmEvent.Current);
    }
}
