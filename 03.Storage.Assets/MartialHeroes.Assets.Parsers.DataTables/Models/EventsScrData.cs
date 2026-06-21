namespace MartialHeroes.Assets.Parsers.DataTables.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  events.scr — Timed game-event definition table
//  spec: Docs/RE/formats/events_scr.md §1 events.scr — sample_verified.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/script/events.scr</c>. Stride: 520 bytes (0x208).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/events_scr.md §1 events.scr — sample_verified + loader-confirmed.
///     No file header; record count = file_size / 520 (must be exact).
///     Known sample: 520 × 1848 = 960,960 bytes, exact.
///     <para>
///         CONSUMED vs NOT-CONSUMED:
///         The client's runtime loader for events.scr dereferences ONLY four fields:
///         - <see cref="EventId" /> (primary key at load) — CONFIRMED CONSUMED
///         - <see cref="ModeFlag" /> (display/eligibility mode) — CONFIRMED CONSUMED
///         - <see cref="RateArray" /> (÷1,000,000 = rate fraction) — CONFIRMED CONSUMED
///         - <see cref="ActorArray" /> (9-digit actor IDs) — CONFIRMED CONSUMED
///         All other fields in the 520-byte record are present in the blob but CONFIRMED NOT-CONSUMED.
///         They are carried verbatim through <see cref="Raw" />.
///         spec: Docs/RE/formats/events_scr.md §1.6 — "client reads ONLY four fields": CONFIRMED.
///     </para>
/// </remarks>
public sealed class EventsScrRecord
{
    // ── CONSUMED fields (the only four the client runtime dereferences) ────────

    /// <summary>
    ///     Event identifier; non-zero unique key. Observed range 10551–31704.
    ///     Used as the map key at load time and the primary lookup argument for every consumer.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — event_id u32LE @ 0x00: CONFIRMED CONSUMED (primary key).
    /// </summary>
    public required uint EventId { get; init; }

    /// <summary>
    ///     Live display/eligibility mode flag; one consumer branches on == 1, another on == 0.
    ///     Observed 1 in 1233/1848 records, 0 otherwise.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — mode_flag u16LE @ 0x64: CONFIRMED CONSUMED.
    ///     spec: Docs/RE/formats/events_scr.md §1.6 — "CONSUMED; earlier drafts mislabeled as reserved/padding."
    /// </summary>
    public required ushort ModeFlag { get; init; }

    /// <summary>
    ///     Fixed slot of up to 50 u32 entries (0x68–0x12F), zero-terminated.
    ///     The client divides each entry by 1,000,000 and displays the result as a fractional rate (%).
    ///     Positionally paired with <see cref="ActorArray" /> (slot N rate ↔ slot N actor).
    ///     NOT an ID array — semantics and value range are disjoint from <see cref="ActorArray" />.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED / HIGH (÷1e6 rate role).
    ///     spec: Docs/RE/formats/events_scr.md §1.7 — "÷1,000,000 = rate fraction displayed as %": HIGH.
    /// </summary>
    public required IReadOnlyList<uint> RateArray { get; init; }

    /// <summary>
    ///     Fixed slot of up to 52 u32 entries (0x130–0x1FF), zero-terminated.
    ///     The client iterates these and resolves each as an actor ID. Values use the 9-digit ID
    ///     namespace (e.g. 213010002, 215010101), shared with items.scr / citems.scr.
    ///     Positionally paired with <see cref="RateArray" />.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED / HIGH.
    /// </summary>
    public required IReadOnlyList<uint> ActorArray { get; init; }

    // ── NOT-CONSUMED fields (present in blob, never read by client; exposed for completeness) ──

    /// <summary>
    ///     Event type code; observed value 1 in all standard records.
    ///     NOT-CONSUMED: not dereferenced by the client at load or query time.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: SAMPLE-VERIFIED / CONFIRMED not-consumed.
    /// </summary>
    public required ushort EventType { get; init; }

    /// <summary>
    ///     Number of active days per cycle (observed 7 = weekly).
    ///     NOT-CONSUMED: not dereferenced by the client at load or query time.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: SAMPLE-VERIFIED / CONFIRMED not-consumed.
    /// </summary>
    public required ushort DayCount { get; init; }

    /// <summary>
    ///     Full 520-byte raw record bytes (zero-copy slice of the original buffer).
    ///     Contains the NOT-CONSUMED flag fields (reserved_a@0x08, flags@0x54..0x63, level_min/max,
    ///     pad_66, record_trailer@0x200) in their original positions.
    ///     spec: Docs/RE/formats/events_scr.md §1.3 — stride 520 bytes: CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}