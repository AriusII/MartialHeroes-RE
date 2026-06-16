using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class GamePacketHandlerTests
{
    private static (InboundFrameDispatcher dispatcher, ClientWorld world,
        ClientEventBus bus, ClientStateMachine fsm, CountingUnhandledOpcodeSink unhandled) NewHarness()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded); // lossless for assertions
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.Loading);
        var unhandled = new CountingUnhandledOpcodeSink();
        var handler = new GamePacketHandler(world, bus, fsm, unhandled);
        var dispatcher = new InboundFrameDispatcher(handler);
        return (dispatcher, world, bus, fsm, unhandled);
    }

    private static List<IClientEvent> Drain(ClientEventBus bus)
    {
        var events = new List<IClientEvent>();
        while (bus.Reader.TryRead(out IClientEvent? e))
        {
            events.Add(e);
        }

        return events;
    }

    [Fact]
    public void CharSpawn_creates_actor_and_publishes_spawn_event()
    {
        var (dispatcher, world, bus, _, _) = NewHarness();

        byte[] frame = SyntheticFrames.CharSpawn(
            sort: 1, actorId: 42, name: "Wuxia", level: 7,
            currentHp: 250, currentMp: 100, currentStamina: 80,
            worldX: 12.5f, worldZ: -3.25f, serverClass: 9);

        Assert.True(dispatcher.RouteNow(frame));

        var key = new ActorKey(42, EntitySort.PlayerCharacter);
        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(7, actor.Level);
        Assert.Equal(250u, actor.CurrentHp);
        // Float -> Vector3Fixed boundary conversion; world Y forced to 0.
        Assert.Equal(Vector3Fixed.FromFloat(12.5f, 0f, -3.25f), actor.Position);

        var spawned = Assert.IsType<ActorSpawnedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(key, spawned.Key);
        Assert.Equal("Wuxia", spawned.Name);
        Assert.Equal((ushort)7, spawned.Level);
        Assert.Equal(250u, spawned.CurrentHp);
        Assert.Equal((ushort)9, spawned.ServerClass);
        Assert.Equal(Vector3Fixed.FromFloat(12.5f, 0f, -3.25f), spawned.Position);
    }

    [Fact]
    public void MovementUpdate_converts_float_to_fixed_and_publishes_moved_event()
    {
        var (dispatcher, world, bus, _, _) = NewHarness();

        // Spawn first so the actor exists.
        dispatcher.RouteNow(SyntheticFrames.CharSpawn(
            1, 7, "Wuxia", 1, 100, 0, 0, 0f, 0f, 0));
        Drain(bus);

        byte[] move = SyntheticFrames.MovementUpdate(
            sort: 1, actorId: 7, yaw: 0f,
            posX: 5.0f, posZ: 6.0f, destX: 9.0f, destZ: 10.0f, runFlag: 1);

        Assert.True(dispatcher.RouteNow(move));

        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        Assert.True(world.TryGet(key, out Actor actor));
        // Position equals the FromFloat-converted wire coords (the load-bearing boundary assertion).
        Assert.Equal(Vector3Fixed.FromFloat(5.0f, 0f, 6.0f), actor.Position);
        Assert.Equal(Vector3Fixed.FromFloat(9.0f, 0f, 10.0f), actor.MoveTarget);
        Assert.Equal(LifecycleState.Running, actor.Lifecycle);

        var moved = Assert.IsType<ActorMovedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(key, moved.Key);
        Assert.Equal(Vector3Fixed.FromFloat(5.0f, 0f, 6.0f), moved.Position);
        Assert.Equal(Vector3Fixed.FromFloat(9.0f, 0f, 10.0f), moved.MoveTarget);
        Assert.True(moved.IsRunning);
    }

    [Fact]
    public void MovementUpdate_for_unknown_actor_registers_placeholder()
    {
        var (dispatcher, world, bus, _, _) = NewHarness();

        byte[] move = SyntheticFrames.MovementUpdate(
            sort: 2, actorId: 99, yaw: 0f, posX: 1f, posZ: 2f, destX: 1f, destZ: 2f);

        dispatcher.RouteNow(move);

        Assert.True(world.TryGet(new ActorKey(99, EntitySort.Monster), out Actor actor));
        Assert.Equal(Vector3Fixed.FromFloat(1f, 0f, 2f), actor.Position);
        Assert.IsType<ActorMovedEvent>(Assert.Single(Drain(bus)));
    }

    [Fact]
    public void Despawn_removes_actor_and_publishes_event_with_flag()
    {
        var (dispatcher, world, bus, _, _) = NewHarness();

        dispatcher.RouteNow(SyntheticFrames.CharSpawn(1, 7, "Wuxia", 1, 100, 0, 0, 0f, 0f, 0));
        Drain(bus);

        Assert.True(dispatcher.RouteNow(SyntheticFrames.Despawn(sort: 1, actorId: 7, flags: 0x01)));

        Assert.False(world.TryGet(new ActorKey(7, EntitySort.PlayerCharacter), out _));
        var despawned = Assert.IsType<ActorDespawnedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(new ActorKey(7, EntitySort.PlayerCharacter), despawned.Key);
        Assert.True(despawned.PlayLeaveEffect);
    }

    [Fact]
    public void EnterGameAck_drives_state_machine_to_world()
    {
        var (dispatcher, _, bus, fsm, _) = NewHarness(); // starts in Loading

        Assert.True(dispatcher.RouteNow(SyntheticFrames.EnterGameAck()));

        Assert.Equal(ClientState.World, fsm.Current);
        var changed = Assert.IsType<ClientStateChangedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(ClientState.Loading, changed.Previous);
        Assert.Equal(ClientState.World, changed.Current);
    }

    [Fact]
    public void Unhandled_opcode_is_counted_not_thrown()
    {
        var (dispatcher, _, _, _, unhandled) = NewHarness();

        // 1/16 SmsgSrvBillingDeactivated has confirmed routing but no specced struct/handler.
        byte[] frame = new byte[FrameWithOpcode(1, 16)];
        WriteHeader(frame, 1, 16);

        Assert.False(dispatcher.RouteNow(frame));
        Assert.Equal(1, unhandled.Count);
    }

    // -----------------------------------------------------------------------------------------------
    // 5/9 ExpGain + 5/11 RankXpGain routing into the Domain ProgressionState.
    // spec: Docs/RE/specs/progression.md §3 / §3.1 / §4 / §11.
    // -----------------------------------------------------------------------------------------------

    private static (InboundFrameDispatcher dispatcher, ClientWorld world, GamePacketHandler handler)
        NewProgressionHarness(
            System.Collections.Generic.IReadOnlyList<long>? divisors = null,
            System.Collections.Generic.IReadOnlyList<long>? caps = null,
            System.Action<int>? levelTableError = null,
            System.Func<long>? bonusRate = null)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.Loading);
        var unhandled = new CountingUnhandledOpcodeSink();
        var handler = new GamePacketHandler(world, bus, fsm, unhandled)
        {
            RankXpDivisorTable = divisors,
            RankXpCapTable = caps,
            LevelTableErrorSink = levelTableError,
            XpBonusRatePercentResolver = bonusRate,
        };
        var dispatcher = new InboundFrameDispatcher(handler);
        return (dispatcher, world, handler);
    }

    // NOTE: 5/9 and 5/11 route through GamePacketHandler.OnUnhandled (they have no typed PacketRouter
    // seam), so PacketRouter.Route / RouteNow returns false for them by design (same as any unhandled
    // opcode). The load-bearing assertion is the resulting ProgressionState, not the route return value.

    [Fact]
    public void ExpGain_5_9_adds_amount_to_both_accumulators()
    {
        var (dispatcher, _, handler) = NewProgressionHarness();

        // spec: progression.md §3 / §3.4 — the i64 amount adds to current-XP AND lifetime-XP.
        dispatcher.RouteNow(SyntheticFrames.ExpGain(sort: 1, actorId: 7, amount: 1500));
        Assert.Equal(1500L, handler.Progression.Experience.CurrentXp);
        Assert.Equal(1500L, handler.Progression.Experience.LifetimeXp);

        dispatcher.RouteNow(SyntheticFrames.ExpGain(sort: 1, actorId: 7, amount: 500));
        Assert.Equal(2000L, handler.Progression.Experience.CurrentXp);
        Assert.Equal(2000L, handler.Progression.Experience.LifetimeXp);
    }

    [Fact]
    public void ExpGain_5_9_bonus_split_is_display_only_full_amount_still_accumulates()
    {
        // spec: progression.md §3.1 — when source-mode low byte == 2 the floating text splits base/bonus,
        // but the FULL amount accumulates (the split changes nothing about the accumulator).
        var (dispatcher, _, handler) = NewProgressionHarness(bonusRate: () => 100L);

        dispatcher.RouteNow(
            SyntheticFrames.ExpGain(sort: 1, actorId: 7, amount: 200, sourceSort: 2));

        Assert.Equal(200L, handler.Progression.Experience.CurrentXp);
        Assert.Equal(200L, handler.Progression.Experience.LifetimeXp);
    }

    [Fact]
    public void RankXp_5_11_mode2_adds_directly_no_level_math()
    {
        var (dispatcher, _, handler) = NewProgressionHarness();

        // spec: progression.md §4 / §4.1 — mode 2 adds the amount straight to the rank accumulator.
        dispatcher.RouteNow(
            SyntheticFrames.RankXpGain(actorId: 7, sort: 1, amount: 42, mode: 2));

        Assert.Equal(42L, handler.Progression.RankXp.RankAccumulator);
        Assert.Equal(0L, handler.Progression.RankXp.WithinRank);
    }

    [Fact]
    public void RankXp_5_11_nonMode2_runs_per_level_table_routine()
    {
        // spec: progression.md §4 — rank_acc += (remainder+amount)/divisor[level]; within = % divisor.
        // Local-player level 3 → divisor 10; amount 25 → +2 ranks, remainder 5.
        var divisors = new long[] { 0, 0, 0, 10, 10 }; // indexed by level cache.
        var (dispatcher, world, handler) = NewProgressionHarness(divisors: divisors);

        // A local actor at level 3 supplies the table index.
        dispatcher.RouteNow(SyntheticFrames.CharSpawn(1, 7, "Wuxia", 3, 100, 0, 0, 0f, 0f, 0));
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        dispatcher.RouteNow(
            SyntheticFrames.RankXpGain(actorId: 7, sort: 1, amount: 25, mode: 0));

        Assert.Equal(2L, handler.Progression.RankXp.RankAccumulator);
        Assert.Equal(5L, handler.Progression.RankXp.WithinRank);
    }

    [Fact]
    public void RankXp_5_11_zero_divisor_fires_leveltable_error_and_leaves_state_unchanged()
    {
        // spec: progression.md §4 — a 0 divisor for the active level is the "leveltable error".
        int erroredLevel = -1;
        var divisors = new long[] { 0, 0, 0, 0 }; // divisor for level 3 is 0.
        var (dispatcher, world, handler) =
            NewProgressionHarness(divisors: divisors, levelTableError: lvl => erroredLevel = lvl);

        dispatcher.RouteNow(SyntheticFrames.CharSpawn(1, 7, "Wuxia", 3, 100, 0, 0, 0f, 0f, 0));
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        dispatcher.RouteNow(
            SyntheticFrames.RankXpGain(actorId: 7, sort: 1, amount: 25, mode: 0));

        Assert.Equal(3, erroredLevel); // diagnostic fired for the level-3 index.
        Assert.Equal(0L, handler.Progression.RankXp.RankAccumulator); // state untouched.
        Assert.Equal(0L, handler.Progression.RankXp.WithinRank);
    }

    private static int FrameWithOpcode(ushort major, ushort minor) => 8; // header only, empty payload

    private static void WriteHeader(byte[] frame, ushort major, ushort minor)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)frame.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), major);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), minor);
    }
}