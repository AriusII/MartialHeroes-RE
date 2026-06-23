namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public sealed class TimedEventQueue
{
    private readonly List<TimedEventRecord> _records = [];

    public int Drain(long nowMs, Action<TimedEventRecord> fire)
    {
        ArgumentNullException.ThrowIfNull(fire);
        if (_records.Count == 0) return 0;

        var fired = 0;
        for (var i = 0; i < _records.Count; i++)
        {
            if (_records[i].FireTime >= nowMs) continue;
            fire(_records[i]);
            fired++;
        }

        if (fired == 0) return 0;

        _records.RemoveAll(r => r.FireTime < nowMs);
        return fired;
    }

    public int FlushOnSceneTransition()
    {
        var discarded = _records.Count;
        _records.Clear();
        return discarded;
    }
}

public readonly record struct TimedEventRecord(
    long FireTime,
    uint EventId,
    uint Payload0,
    uint Payload1,
    uint Payload2,
    uint Payload3);