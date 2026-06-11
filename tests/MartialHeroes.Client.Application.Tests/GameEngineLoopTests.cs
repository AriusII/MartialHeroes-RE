using System.Collections.Immutable;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class GameEngineLoopTests
{
    /// <summary>A manual-step clock: yields N ticks then stops, with no wall-clock waiting.</summary>
    private sealed class ManualGameClock(int ticks) : IGameClock
    {
        private int _remaining = ticks;

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return ValueTask.FromResult(false);
            }

            _remaining--;
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>Counts and records every queued input dispatch (world handler that never consumes).</summary>
    private sealed class RecordingHandler : IInputHandler
    {
        public List<InputEvent> Seen { get; } = new();

        public bool TryHandle(in InputEvent e)
        {
            Seen.Add(e);
            return false;
        }
    }

    private static (GameEngineLoop loop, ClientWorld world, ClientEventBus bus, InputBus input, RecordingHandler handler
        )
        NewHarness(int tickRateHz = GameEngineLoop.DefaultTickRateHz)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var handler = new RecordingHandler();
        var input = new InputBus(handler);
        var loop = new GameEngineLoop(world, bus, input, tickRateHz);
        return (loop, world, bus, input, handler);
    }

    private static List<WorldSnapshotEvent> DrainSnapshots(ClientEventBus bus)
    {
        var snaps = new List<WorldSnapshotEvent>();
        while (bus.Reader.TryRead(out IClientEvent? e))
        {
            if (e is WorldSnapshotEvent snap)
            {
                snaps.Add(snap);
            }
        }

        return snaps;
    }

    [Fact]
    public async Task N_clock_ticks_publish_N_snapshots()
    {
        var (loop, _, bus, _, _) = NewHarness();
        var clock = new ManualGameClock(5);

        await loop.RunAsync(clock);

        List<WorldSnapshotEvent> snaps = DrainSnapshots(bus);
        Assert.Equal(5, snaps.Count);
        Assert.Equal(5, loop.TickCount);
        // Tick index is monotonic and 0-based.
        for (int i = 0; i < snaps.Count; i++)
        {
            Assert.Equal((long)i, snaps[i].Tick);
        }
    }

    [Fact]
    public void StepOnce_publishes_one_snapshot_per_call()
    {
        var (loop, _, bus, _, _) = NewHarness();

        WorldSnapshotEvent s0 = loop.StepOnce();
        WorldSnapshotEvent s1 = loop.StepOnce();

        Assert.Equal(0, s0.Tick);
        Assert.Equal(1, s1.Tick);
        Assert.Equal(2, DrainSnapshots(bus).Count);
    }

    [Fact]
    public void Pending_inputs_are_drained_each_tick()
    {
        var (loop, _, _, input, handler) = NewHarness();

        input.Enqueue(new InputEvent(InputType.MouseMove, 10, 20, 0, 0));
        input.Enqueue(new InputEvent(InputType.MouseButtonDown, 10, 20, MouseButton.Left, 0));
        Assert.Equal(2, input.PendingCount);

        loop.StepOnce();

        // Both queued events were drained and dispatched through the chain this tick.
        Assert.Equal(0, input.PendingCount);
        Assert.Equal(2, handler.Seen.Count);

        // A second tick with nothing queued dispatches nothing further.
        loop.StepOnce();
        Assert.Equal(2, handler.Seen.Count);
    }

    [Fact]
    public void Default_cadence_is_30hz_with_33ms_base_delta()
    {
        var (loop, _, _, _, _) = NewHarness();
        Assert.Equal(30, loop.TickRateHz);
        Assert.Equal(33u, loop.BaseFixedDeltaMs); // 1000 / 30 = 33 (integer). spec: game_loop.md §6
    }

    [Fact]
    public void Time_scale_multiplies_the_fixed_delta()
    {
        var (loop, _, _, _, _) = NewHarness(tickRateHz: 10); // base delta = 100 ms

        loop.TimeScale = 1.0;
        Assert.Equal(100u, loop.StepOnce().FixedDeltaMs);

        loop.TimeScale = 2.0; // fast-forward
        Assert.Equal(200u, loop.StepOnce().FixedDeltaMs);

        loop.TimeScale = 0.5; // slow-motion
        Assert.Equal(50u, loop.StepOnce().FixedDeltaMs);
    }

    [Fact]
    public void World_advances_actor_movement_by_the_fixed_delta()
    {
        var (loop, world, bus, _, _) = NewHarness(tickRateHz: 10); // 100 ms / tick

        var key = new ActorKey(1, EntitySort.PlayerCharacter);
        // Speed = 1 unit/s in raw Q16.16; over 100 ms it advances ~0.1 units toward the target.
        var actor = new Actor(
            key,
            level: 1,
            vitals: VitalStats.Zero,
            currentHp: 0,
            currentMp: 0,
            currentStamina: 0,
            position: Vector3Fixed.FromFloat(0f, 0f, 0f),
            moveSpeedRawPerSecond: 100L * Vector3Fixed.One);
        actor.SetMoveTarget(Vector3Fixed.FromFloat(1000f, 0f, 0f));
        world.Add(actor);

        Vector3Fixed before = actor.Position;
        WorldSnapshotEvent snap = loop.StepOnce();
        Vector3Fixed after = actor.Position;

        Assert.NotEqual(before, after); // it moved
        Assert.True(after.RawX > before.RawX); // toward +X target

        // The snapshot reflects the post-tick position, not a live reference.
        ActorSnapshot sample = Assert.Single(snap.Actors);
        Assert.Equal(after, sample.Position);
        Assert.Equal(key, sample.Key);
    }

    [Fact]
    public void Empty_world_still_publishes_a_snapshot()
    {
        var (loop, _, _, _, _) = NewHarness();
        WorldSnapshotEvent snap = loop.StepOnce();
        Assert.Equal(ImmutableArray<ActorSnapshot>.Empty, snap.Actors);
        // Empty path returns the shared Empty singleton — no per-tick backing-array alloc.
        Assert.True(snap.Actors.IsEmpty);
    }

    [Fact]
    public void Snapshot_actor_count_matches_world_for_multiple_actors()
    {
        var (loop, world, _, _, _) = NewHarness();

        for (uint id = 1; id <= 3; id++)
        {
            world.Add(new Actor(
                new ActorKey(id, EntitySort.PlayerCharacter),
                level: 1,
                vitals: VitalStats.Zero,
                currentHp: 0,
                currentMp: 0,
                currentStamina: 0,
                position: Vector3Fixed.FromFloat(0f, 0f, 0f),
                moveSpeedRawPerSecond: 0L));
        }

        WorldSnapshotEvent snap = loop.StepOnce();

        // The zero-copy backing array is sized to the world and fully populated (no holes/extra slots).
        Assert.Equal(world.Count, snap.Actors.Length);
        Assert.Equal(3, snap.Actors.Length);
        Assert.Equal(3, snap.Actors.Select(a => a.Key).Distinct().Count());
    }
}