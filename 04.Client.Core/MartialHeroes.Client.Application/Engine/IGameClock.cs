namespace MartialHeroes.Client.Application.Engine;

/// <summary>
/// A clock abstraction over the fixed-tick cadence so the <see cref="GameEngineLoop"/> can be driven
/// either by a real <see cref="System.Threading.PeriodicTimer"/> or, in tests, by a manual stepper
/// that yields one tick at a time without waiting on wall-clock time.
/// </summary>
/// <remarks>
/// <para>
/// The legacy engine sampled a monotonic millisecond clock once per loop iteration and serviced a
/// per-subscriber threshold scheduler (spec: Docs/RE/specs/game_loop.md §3-§4). The deterministic
/// .NET core adopts the documented intentional divergence — a single fixed-rate logic tick decoupled
/// from rendering (spec: Docs/RE/specs/game_loop.md §6). This interface is the seam that lets the
/// fixed tick be produced by either a wall-clock timer or a deterministic test stepper.
/// </para>
/// <para>
/// Each <see cref="WaitForNextTickAsync"/> completion represents exactly one fixed logical step. The
/// fixed delta is owned by the loop, not the clock; the clock only paces the steps.
/// </para>
/// </remarks>
public interface IGameClock
{
    /// <summary>
    /// Waits for the next fixed tick boundary. Returns <see langword="true"/> when a tick is due and
    /// <see langword="false"/> when the clock has been stopped/disposed and no more ticks will come
    /// (mirrors <see cref="System.Threading.PeriodicTimer.WaitForNextTickAsync"/>).
    /// </summary>
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default);
}