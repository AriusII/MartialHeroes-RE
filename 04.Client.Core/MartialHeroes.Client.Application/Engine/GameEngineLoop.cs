using System.Collections.Immutable;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Application.Engine;

/// <summary>
/// The deterministic fixed-tick logic loop. Each tick: (1) drains pending input through the
/// <see cref="InputBus"/>, (2) advances Domain world state by the fixed delta (movement and any
/// per-tick simulation on the live actors), and (3) publishes a <see cref="WorldSnapshotEvent"/> so
/// the Godot layer can interpolate. spec: Docs/RE/specs/game_loop.md §6 (intentional divergence to a
/// fixed-rate, snapshot-interpolated model).
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed cadence.</b> The default is 30&#160;Hz (≈ 33.33&#160;ms per tick). The cadence is
/// configurable but the logical delta is fixed and independent of the presentation frame rate
/// (spec: Docs/RE/specs/game_loop.md §6, "single fixed timestep … decoupled from rendering"). The
/// legacy engine used a variable-cadence per-subscriber ms-threshold scheduler (§3); this is the
/// documented upgrade for determinism and headless testability (§6).
/// </para>
/// <para>
/// <b>Time scale preserved.</b> The legacy optional time-scale (slow-mo / fast-forward, a global float
/// applied to the ms clock) maps onto the fixed-tick model as a multiplier on the fixed delta
/// (spec: game_loop.md §4 / §6, "Time-scale preserved"). <see cref="TimeScale"/> defaults to 1.0
/// (realtime); &lt; 1 is slow-motion, &gt; 1 is fast-forward.
/// </para>
/// <para>
/// <b>No rendering.</b> This loop owns no presentation and no Godot dependency. Rendering happens in
/// Godot at its own frame rate; the only output is the per-tick snapshot (spec: game_loop.md §2 / §6).
/// </para>
/// <para>
/// <b>Determinism / threading.</b> The loop thread is the single logical owner that mutates Domain.
/// Input is funnelled onto this thread via the <see cref="InputBus"/> drain (spec: game_loop.md §6).
/// </para>
/// </remarks>
public sealed class GameEngineLoop
{
    /// <summary>
    /// Default fixed logic cadence: 30&#160;Hz. spec: Docs/RE/specs/game_loop.md §6
    /// ("e.g. 30&#160;Hz via a PeriodicTimer").
    /// </summary>
    public const int DefaultTickRateHz = 30; // spec: Docs/RE/specs/game_loop.md §6

    private readonly ClientWorld _world;
    private readonly IClientEventBus _eventBus;
    private readonly InputBus _inputBus;
    private readonly LocalPlayerState? _localPlayer;

    // The base (un-scaled) fixed delta for one tick, in milliseconds.
    private readonly uint _baseFixedDeltaMs;

    private long _tick;

    // Monotonic logical clock in ms, advanced by the scaled delta each tick. Feeds the deterministic
    // cooldown / cast-state ticks (those take a caller-supplied ms clock, not an ambient one).
    private long _clockMs;

    /// <summary>
    /// Creates a loop at <paramref name="tickRateHz"/> ticks per second (default
    /// <see cref="DefaultTickRateHz"/>). The fixed base delta is <c>1000 / tickRateHz</c> ms.
    /// </summary>
    /// <param name="world">The live actor registry to advance.</param>
    /// <param name="eventBus">The outbound bus the per-tick snapshot is published on.</param>
    /// <param name="inputBus">The input bus drained at the start of each tick.</param>
    /// <param name="tickRateHz">Fixed cadence in Hz; must be &gt; 0. Default 30. spec: game_loop.md §6.</param>
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
        _localPlayer = localPlayer; // optional: cooldown / buff / cast-state ticks

        if (tickRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickRateHz), tickRateHz, "Tick rate must be > 0.");
        }

        TickRateHz = tickRateHz;
        // Fixed timestep in ms. 30 Hz -> 33 ms (integer); the snapshot carries the post-scale delta.
        _baseFixedDeltaMs = (uint)(1000 / tickRateHz);
        if (_baseFixedDeltaMs == 0)
        {
            _baseFixedDeltaMs = 1; // guard absurdly high rates so a tick still advances time.
        }
    }

    /// <summary>The configured fixed cadence in Hz.</summary>
    public int TickRateHz { get; }

    /// <summary>The base (un-scaled) fixed delta for one tick, in milliseconds.</summary>
    public uint BaseFixedDeltaMs => _baseFixedDeltaMs;

    /// <summary>Number of fixed ticks executed so far (0 before the first step).</summary>
    public long TickCount => _tick;

    /// <summary>
    /// Engine-wide time-scale multiplier on the fixed delta. <c>1.0</c> = realtime, <c>&lt; 1</c> =
    /// slow-motion, <c>&gt; 1</c> = fast-forward. spec: Docs/RE/specs/game_loop.md §4 / §6
    /// ("Time-scale preserved"). Must be &gt;= 0.
    /// </summary>
    public double TimeScale { get; set; } = 1.0;

    /// <summary>
    /// Returns a default <see cref="PeriodicGameClock"/> matching this loop's cadence — a convenience
    /// for the composition root. The clock paces the steps; the fixed delta stays owned by the loop.
    /// </summary>
    public PeriodicGameClock CreateRealtimeClock() =>
        new(TimeSpan.FromSeconds(1.0 / TickRateHz));

    /// <summary>
    /// Runs the loop until <paramref name="cancellationToken"/> is cancelled or the
    /// <paramref name="clock"/> stops. Each clock tick executes exactly one <see cref="StepOnce"/>.
    /// spec: Docs/RE/specs/game_loop.md §6.
    /// </summary>
    public async Task RunAsync(IGameClock clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clock);

        try
        {
            while (await clock.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                StepOnce();
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown — cancellation is the normal stop signal.
        }
    }

    /// <summary>
    /// Executes one fixed tick synchronously: drain input, advance world, publish snapshot. Exposed
    /// for deterministic, wall-clock-free testing (drive N steps and assert N snapshots). spec:
    /// Docs/RE/specs/game_loop.md §6.
    /// </summary>
    /// <returns>The snapshot published this tick.</returns>
    public WorldSnapshotEvent StepOnce()
    {
        // Apply the time-scale to the fixed delta. spec: game_loop.md §4 / §6 (time-scale multiplier).
        uint deltaMs = ScaledDeltaMs();

        // (1) Drain pending input onto this (the single owner) thread. spec: game_loop.md §6.
        _inputBus.DrainAndDispatch();

        // (2) Advance Domain world state by the fixed delta. The actual movement/timeline math lives
        //     in the Domain Simulation classes invoked by Actor.AdvanceMovement. No game-rule math
        //     here — orchestration only.
        AdvanceWorld(deltaMs);

        // (2b) Advance the local player's skill subsystems (cooldowns, buff expiry, cast state) on this
        //      same logical owner. The Domain owns each deterministic tick. spec: skills.md §4 / §6.3 / §2.
        AdvanceLocalPlayerSystems(deltaMs);

        // (3) Publish an immutable snapshot for Godot to interpolate. spec: game_loop.md §6.
        WorldSnapshotEvent snapshot = BuildSnapshot(deltaMs);
        _eventBus.Publish(snapshot);

        _tick++;
        return snapshot;
    }

    /// <summary>
    /// Advances the local player's deterministic skill subsystems by one fixed tick: the cooldown table
    /// (to the monotonic ms clock), the buff/status table (one §6.3 decrement), and the cast state machine
    /// (cooldown-phase expiry). No-op when no <see cref="LocalPlayerState"/> is wired. Each tick is a
    /// Domain call; no game-rule math here. spec: Docs/RE/specs/skills.md §4 / §6.3 / §2 / §5.2.
    /// </summary>
    private void AdvanceLocalPlayerSystems(uint deltaMs)
    {
        if (_localPlayer is null)
        {
            return;
        }

        // Advance the monotonic logical clock; the cooldown / cast ticks take a caller-supplied ms value.
        _clockMs += deltaMs;

        // Tick all cooldown slots to the current clock (expired slots disarm). spec: skills.md §4 (tick-all).
        _localPlayer.Cooldowns.TickAll(_clockMs);

        // One §6.3 buff tick: each active status slot decrements its duration by 1; reaching 0 expires it.
        // spec: skills.md §6.3 (single decrement per tick).
        _localPlayer.Buffs.Tick();

        // Advance the cast state machine: a cooldown phase whose end has elapsed returns to idle. The cast
        // window itself is server-confirmed (§5.2), not auto-completed here. spec: skills.md §4 / §5.2.
        _localPlayer.CastState = _localPlayer.CastState.Tick(_clockMs);
    }

    /// <summary>The fixed delta after applying <see cref="TimeScale"/>, clamped to &gt;= 0.</summary>
    private uint ScaledDeltaMs()
    {
        double scale = TimeScale;
        if (double.IsNaN(scale) || scale <= 0.0)
        {
            return 0u; // paused / invalid scale advances no time but still ticks the loop.
        }

        double scaled = _baseFixedDeltaMs * scale;
        if (scaled >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)scaled;
    }

    private void AdvanceWorld(uint deltaMs)
    {
        if (deltaMs == 0)
        {
            return; // paused: no simulation advance, snapshot still emitted for a steady stream.
        }

        // Iterate the concrete ValueCollection so the struct enumerator is used directly (no boxing
        // per tick). spec: Docs/RE/specs/game_loop.md §6 (per-tick path stays zero-alloc).
        foreach (Actor actor in _world.ActorValues)
        {
            // Movement interpolation toward the move target; Domain owns the math (LinearMovement).
            actor.AdvanceMovement(deltaMs);
        }
    }

    private WorldSnapshotEvent BuildSnapshot(uint deltaMs)
    {
        int count = _world.Count;
        if (count == 0)
        {
            return new WorldSnapshotEvent(_tick, deltaMs, ImmutableArray<ActorSnapshot>.Empty);
        }

        // Fill a backing array directly and wrap it zero-copy as an ImmutableArray — avoids the extra
        // builder object the CreateBuilder/MoveToImmutable path allocates per tick. The array is never
        // handed out by reference, so the ImmutableArray remains the single owner (no mutation aliasing).
        ActorSnapshot[] arr = new ActorSnapshot[count];
        int i = 0;
        // Concrete ValueCollection -> struct enumerator, no boxing on the per-tick path.
        foreach (Actor actor in _world.ActorValues)
        {
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
        }

        return new WorldSnapshotEvent(
            _tick,
            deltaMs,
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(arr));
    }
}