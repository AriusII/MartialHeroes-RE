namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public sealed class TimedEventQueue
{
    public const uint SceneConnectionEventId = 10001;

    public const int PayloadWordCount = 4;

    private readonly List<TimedEventRecord> _records = [];

    public int Count => _records.Count;

    public void Enqueue(long nowMs, long delayMs, uint eventId, ReadOnlySpan<uint> payload)
    {
        var p0 = payload.Length > 0 ? payload[0] : 0u;
        var p1 = payload.Length > 1 ? payload[1] : 0u;
        var p2 = payload.Length > 2 ? payload[2] : 0u;
        var p3 = payload.Length > 3 ? payload[3] : 0u;

        var fireTime = nowMs + delayMs;
        var record = new TimedEventRecord(fireTime, eventId, p0, p1, p2, p3);

        var index = LowerBound(fireTime);
        _records.Insert(index, record);
    }

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

    private int LowerBound(long fireTime)
    {
        var lo = 0;
        var hi = _records.Count;
        while (lo < hi)
        {
            var mid = (int)(((uint)lo + (uint)hi) >> 1);
            if (_records[mid].FireTime < fireTime) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}

public readonly record struct TimedEventRecord(
    long FireTime,
    uint EventId,
    uint Payload0,
    uint Payload1,
    uint Payload2,
    uint Payload3);