namespace MartialHeroes.Client.Domain.Simulation.Simulation;

/// <summary>
///     The universal "10001" deferred timed-EVENT queue — a deterministic, engine-free reproduction of
///     the legacy effect-manager's sorted, fire-time-keyed event scheduler. It is the generic "do this
///     scene/connection action when the deadline arrives" mechanism (a connection-state transition, a
///     character action-result, a login-curtain / logout delay), NOT an effect-spawn list.
/// </summary>
/// <remarks>
///     <para>
///         <b>Sorted by fire-time, drained by a TWO-PASS FULL-TREE SWEEP.</b> Each frame the drain fires
///         EVERY entry whose <c>fire_time &lt; now_ms</c> — it does NOT stop at the first future entry —
///         then removes the fired entries (CYCLE 11 RESOLVED). This is the load-bearing fidelity point:
///         the original is a full-tree sweep, not an early-out at the earliest future deadline.
///         spec: Docs/RE/specs/effect-scheduling.md §5A.3 / §8 (10001 drain = two-pass full-tree sweep).
///     </para>
///     <para>
///         <b>Arm primitive: <c>fire_time = now_ms + delay</c>.</b> The now-ms is sampled at the enqueue
///         site and the caller's delay is added (the same <c>now + delay</c> arm the effect baseline uses).
///         Each record is six words: <c>fire_time</c>, <c>event_id</c>, and four payload words.
///         spec: Docs/RE/specs/effect-scheduling.md §5A.1 / §5A.2.
///     </para>
///     <para>
///         <b>Deterministic + headless.</b> This type owns no clock: the host passes <c>now_ms</c> into
///         <see cref="Enqueue" /> and <see cref="Drain" />. The same queue runs on a future server.
///     </para>
/// </remarks>
public sealed class TimedEventQueue
{
    /// <summary>
    ///     The universal deferred scene/connection-state trigger event id. The bare 10001 trigger carries
    ///     zero payload words; observed delays include 5000 ms and 10000 ms.
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A.1 (event_id 10001 = generic deferred scene/connection trigger).
    /// </summary>
    public const uint SceneConnectionEventId = 10001; // spec: effect-scheduling.md §5A.1

    /// <summary>The number of payload words carried per record (the four after fire_time + event_id). spec: §5A.1.</summary>
    public const int PayloadWordCount = 4; // spec: effect-scheduling.md §5A.1 (6-word record: fire_time, event_id, +4)

    // Ordered by fire_time (the sort key), reproducing the legacy sorted tree. A List kept sorted on
    // insert is the engine-free equivalent of the red-black tree; the drain visits ALL past-due nodes
    // (full-tree sweep), so the only ordering property the drain relies on is "earliest first" for a
    // stable fire order. spec: Docs/RE/specs/effect-scheduling.md §5A (sorted tree keyed by fire-time).
    private readonly List<TimedEventRecord> _records = [];

    /// <summary>The number of armed (not-yet-fired) events in the queue.</summary>
    public int Count => _records.Count;

    /// <summary>
    ///     Arms one deferred event: builds a six-word record and inserts it into the queue ordered by
    ///     <c>fire_time = nowMs + delayMs</c>. spec: Docs/RE/specs/effect-scheduling.md §5A.1 / §5A.2.
    /// </summary>
    /// <param name="nowMs">The frame's single now-ms sample (captured at the enqueue site). spec: §1.</param>
    /// <param name="delayMs">The caller's deferral delay added to now-ms. spec: §5A.2.</param>
    /// <param name="eventId">The deferred event selector (<see cref="SceneConnectionEventId" /> for the bare trigger).</param>
    /// <param name="payload">Up to four payload words (zero for the bare 10001 trigger). spec: §5A.1.</param>
    public void Enqueue(long nowMs, long delayMs, uint eventId, ReadOnlySpan<uint> payload)
    {
        var p0 = payload.Length > 0 ? payload[0] : 0u;
        var p1 = payload.Length > 1 ? payload[1] : 0u;
        var p2 = payload.Length > 2 ? payload[2] : 0u;
        var p3 = payload.Length > 3 ? payload[3] : 0u;

        var fireTime = nowMs + delayMs; // the arm: now + delay. spec: effect-scheduling.md §5A.2.
        var record = new TimedEventRecord(fireTime, eventId, p0, p1, p2, p3);

        // Insert keeping the list ordered by fire_time (earliest first), reproducing the sorted tree's
        // ordering. spec: Docs/RE/specs/effect-scheduling.md §5A (ordered by fire-time).
        var index = LowerBound(fireTime);
        _records.Insert(index, record);
    }

    /// <summary>
    ///     The per-frame drain: a TWO-PASS FULL-TREE SWEEP. Pass 1 visits EVERY record and fires every one
    ///     whose <c>fire_time &lt; nowMs</c> by invoking <paramref name="fire" /> (it does NOT stop at the
    ///     first future entry); pass 2 removes the fired records. Returns the number fired.
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A.3 / §8 (two-pass full-tree sweep; no early stop).
    /// </summary>
    /// <param name="nowMs">The frame's single now-ms sample. spec: effect-scheduling.md §1.</param>
    /// <param name="fire">The dispatch callback invoked for each past-due record (in fire-time order).</param>
    public int Drain(long nowMs, Action<TimedEventRecord> fire)
    {
        ArgumentNullException.ThrowIfNull(fire);
        if (_records.Count == 0) return 0;

        // PASS 1 — walk EVERY node and FIRE every entry with fire_time < now_ms. Do NOT stop at the first
        // future entry (the original is a full-tree sweep, not an early-out). spec: §5A.3 pass 1.
        var fired = 0;
        for (var i = 0; i < _records.Count; i++)
        {
            if (_records[i].FireTime >= nowMs) continue; // future entry — keep scanning (no early stop).
            fire(_records[i]);
            fired++;
        }

        if (fired == 0) return 0;

        // PASS 2 — remove the fired (past-due) nodes. spec: Docs/RE/specs/effect-scheduling.md §5A.3 pass 2.
        _records.RemoveAll(r => r.FireTime < nowMs);
        return fired;
    }

    /// <summary>
    ///     Flushes the timed-event queue on a scene transition. The scene-transition reset is a
    ///     timed-event-queue FLUSH (NOT a full effect reset) — it discards all pending deferred events so a
    ///     stale deferred trigger never fires into the next scene. Returns the number discarded.
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A.3; Docs/RE/specs/effects.md (scene-transition reset
    ///     = timed-event-queue flush, NOT a full effect reset).
    /// </summary>
    public int FlushOnSceneTransition()
    {
        var discarded = _records.Count;
        _records.Clear();
        return discarded;
    }

    // First index whose fire_time is >= the given key (a binary lower-bound on the sorted list), so an
    // insert at that index keeps the list ordered. spec: effect-scheduling.md §5A (ordered by fire-time).
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

/// <summary>
///     One six-word timed-event record: the <c>FireTime</c> sort key, the <c>EventId</c> selector, and four
///     payload words. Immutable value type. spec: Docs/RE/specs/effect-scheduling.md §5A.1.
/// </summary>
/// <param name="FireTime">The armed deadline (<c>delay + now_ms</c>); the sort key. spec: §5A.1.</param>
/// <param name="EventId">The deferred event selector (10001 = scene/connection trigger). spec: §5A.1.</param>
/// <param name="Payload0">Payload word 0 (zero for the bare 10001 trigger). spec: §5A.1.</param>
/// <param name="Payload1">Payload word 1. spec: §5A.1.</param>
/// <param name="Payload2">Payload word 2. spec: §5A.1.</param>
/// <param name="Payload3">Payload word 3. spec: §5A.1.</param>
public readonly record struct TimedEventRecord(
    long FireTime,
    uint EventId,
    uint Payload0,
    uint Payload1,
    uint Payload2,
    uint Payload3);