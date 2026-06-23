using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Application.Engine;

public sealed class GameEngineLoop
{
    public const int DefaultTickRateHz = 30;

    private readonly IClientEventBus _eventBus;
    private readonly InputBus _inputBus;
    private readonly LocalPlayerState? _localPlayer;

    private readonly ClientWorld _world;

    private long _clockMs;

    public GameEngineLoop(
        ClientWorld world,
        IClientEventBus eventBus,
        InputBus inputBus,
        int tickRateHz = DefaultTickRateHz,
        LocalPlayerState? localPlayer = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _inputBus = inputBus ?? throw new ArgumentNullException(nameof(inputBus));
        _localPlayer = localPlayer;

        if (tickRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickRateHz), tickRateHz, "Tick rate must be > 0.");

        TickRateHz = tickRateHz;
        BaseFixedDeltaMs = (uint)(1000 / tickRateHz);
        if (BaseFixedDeltaMs == 0) BaseFixedDeltaMs = 1;
    }

    public int TickRateHz { get; }

    public uint BaseFixedDeltaMs { get; }

    public long TickCount { get; private set; }

    public double TimeScale { get; set; } = 1.0;

    public PeriodicGameClock CreateRealtimeClock()
    {
        return new PeriodicGameClock(TimeSpan.FromSeconds(1.0 / TickRateHz));
    }

    public async Task RunAsync(IGameClock clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);

        try
        {
            while (await clock.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) StepOnce();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public WorldSnapshotEvent StepOnce()
    {
        var deltaMs = ScaledDeltaMs();

        _inputBus.DrainAndDispatch();

        AdvanceWorld(deltaMs);

        AdvanceLocalPlayerSystems(deltaMs);

        var snapshot = BuildSnapshot(deltaMs);
        _eventBus.Publish(snapshot);

        TickCount++;
        return snapshot;
    }

    private void AdvanceLocalPlayerSystems(uint deltaMs)
    {
        if (_localPlayer is null) return;

        _clockMs += deltaMs;

        _localPlayer.Cooldowns.TickAll(_clockMs);

        _localPlayer.Buffs.Tick();

        _localPlayer.CastState = _localPlayer.CastState.Tick(_clockMs);
    }

    private uint ScaledDeltaMs()
    {
        var scale = TimeScale;
        if (double.IsNaN(scale) ||
            scale <= 0.0) return 0u;

        var scaled = BaseFixedDeltaMs * scale;
        if (scaled >= uint.MaxValue) return uint.MaxValue;

        return (uint)scaled;
    }

    private void AdvanceWorld(uint deltaMs)
    {
        if (deltaMs == 0) return;

        foreach (var actor in _world.ActorValues)
            actor.AdvanceMovement(deltaMs);
    }

    private WorldSnapshotEvent BuildSnapshot(uint deltaMs)
    {
        var count = _world.Count;
        if (count == 0) return new WorldSnapshotEvent(TickCount, deltaMs, ImmutableArray<ActorSnapshot>.Empty);

        var arr = new ActorSnapshot[count];
        var i = 0;
        foreach (var actor in _world.ActorValues)
            arr[i++] = new ActorSnapshot(
                actor.Key,
                actor.Position,
                actor.MoveTarget,
                actor.Yaw,
                actor.CurrentHp,
                actor.MaxHp,
                actor.CurrentMp,
                actor.MaxMp,
                actor.IsAlive);

        return new WorldSnapshotEvent(
            TickCount,
            deltaMs,
            ImmutableCollectionsMarshal.AsImmutableArray(arr));
    }
}