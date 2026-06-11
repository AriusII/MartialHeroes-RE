namespace MartialHeroes.Client.Application.Engine;

/// <summary>
/// Production <see cref="IGameClock"/> backed by a <see cref="PeriodicTimer"/> ticking at the fixed
/// logic cadence. spec: Docs/RE/specs/game_loop.md §6 ("Fixed-rate logic tick … via a PeriodicTimer").
/// </summary>
/// <remarks>
/// The cadence is supplied by the loop (default 30&#160;Hz ≈ 33.33&#160;ms); the timer paces the
/// steps but the fixed delta itself is owned by the <see cref="GameEngineLoop"/>. Rendering is not
/// driven here — Godot owns presentation at its own frame rate and interpolates between the snapshots
/// this loop publishes (spec: game_loop.md §6, "Render decoupled from logic").
/// </remarks>
public sealed class PeriodicGameClock : IGameClock, IDisposable
{
    private readonly PeriodicTimer _timer;

    /// <summary>Creates a clock that fires every <paramref name="period"/>.</summary>
    public PeriodicGameClock(TimeSpan period)
    {
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Tick period must be positive.");
        }

        _timer = new PeriodicTimer(period);
    }

    /// <inheritdoc />
    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default) =>
        _timer.WaitForNextTickAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _timer.Dispose();
}