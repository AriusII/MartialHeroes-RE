namespace MartialHeroes.Client.Application.Engine;

public sealed class PeriodicGameClock : IGameClock, IDisposable
{
    private readonly PeriodicTimer _timer;

    public PeriodicGameClock(TimeSpan period)
    {
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period), period, "Tick period must be positive.");

        _timer = new PeriodicTimer(period);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
        return _timer.WaitForNextTickAsync(cancellationToken);
    }
}