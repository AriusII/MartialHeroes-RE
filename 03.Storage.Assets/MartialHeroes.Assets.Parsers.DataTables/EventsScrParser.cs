using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/script/events.scr</c> — the timed game-event definition table.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/events_scr.md §1 events.scr — sample_verified + loader-confirmed.
///     <para>
///         Format: no file header; fixed stride 520 bytes (0x208); record count = file_size / 520.
///         Known sample: 520 × 1848 = 960,960 bytes, exact.
///         spec: Docs/RE/formats/events_scr.md §1.1 — "no header; Record stride: 520 bytes (0x208)": CONFIRMED.
///         spec: Docs/RE/formats/events_scr.md §1.2 — "record count = file_size / 520": CONFIRMED.
///     </para>
///     <para>
///         CONSUMED vs NOT-CONSUMED:
///         The client runtime dereferences ONLY four fields per record (spec §1.6):
///         - event_id u32LE @0x00 (CONSUMED — primary key)
///         - mode_flag u16LE @0x64 (CONSUMED — display/eligibility mode)
///         - rate_array u32LE[50] @0x68 (CONSUMED — ÷1,000,000 = rate fraction)
///         - actor_array u32LE[52] @0x130 (CONSUMED — 9-digit actor IDs)
///         All other fields are present in the 520-byte blob but CONFIRMED NOT-CONSUMED.
///         spec: Docs/RE/formats/events_scr.md §1.6 — "client reads ONLY these four fields": CONFIRMED.
///     </para>
///     <para>
///         Sparsely populated: records whose arrays are entirely zero are empty / unused event slots.
///         spec: Docs/RE/formats/events_scr.md §1.2 — "sparsely populated".
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class EventsScrParser
{
    // Record stride: 520 bytes (0x208). CONFIRMED (sample: 960,960 / 520 = 1,848, exact).
    // spec: Docs/RE/formats/events_scr.md §1.1 — "Record stride: 520 bytes (0x208)": CONFIRMED.
    private const int RecordStride = 520; // 0x208

    // Field offsets within a record.
    // spec: Docs/RE/formats/events_scr.md §1.3 — Record layout.

    // event_id u32LE @ 0x00. CONSUMED (primary key). CONFIRMED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — event_id u32LE @ 0x00: CONFIRMED CONSUMED.
    private const int OffEventId = 0x00;

    // event_type u16LE @ 0x04. NOT-CONSUMED. SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: CONFIRMED not-consumed.
    private const int OffEventType = 0x04;

    // day_count u16LE @ 0x06. NOT-CONSUMED. SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: CONFIRMED not-consumed.
    private const int OffDayCount = 0x06;

    // reserved_a u8[68] @ 0x08. NOT-CONSUMED. Zero in all 1843 standard records.
    // spec: Docs/RE/formats/events_scr.md §1.3 — reserved_a @ 0x08: CONFIRMED not-consumed; NOT decoded.

    // level_min u32LE @ 0x4C. NOT-CONSUMED. SAMPLE-VERIFIED (observed 100 in standard records).
    // spec: Docs/RE/formats/events_scr.md §1.3 — level_min u32LE @ 0x4C: CONFIRMED not-consumed; NOT decoded.

    // level_max u32LE @ 0x50. NOT-CONSUMED. SAMPLE-VERIFIED (observed 1000 in standard records).
    // spec: Docs/RE/formats/events_scr.md §1.3 — level_max u32LE @ 0x50: CONFIRMED not-consumed; NOT decoded.

    // flags 0x54..0x63: day_window_start, day_window_end, pad_58, sub_flag_a/b/c/d, pad_62. NOT-CONSUMED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — flags @0x54..0x63: CONFIRMED not-consumed; NOT decoded.

    // mode_flag u16LE @ 0x64. CONSUMED. CONFIRMED.
    // Earlier drafts mislabeled this region as reserved/padding — it is an actively read field.
    // spec: Docs/RE/formats/events_scr.md §1.3 — mode_flag u16LE @ 0x64: CONFIRMED CONSUMED / PLAUSIBLE semantics.
    private const int OffModeFlag = 0x64;

    // pad_66 u8[2] @ 0x66. NOT-CONSUMED. Always 0.
    // spec: Docs/RE/formats/events_scr.md §1.3 — pad_66 @ 0x66: NOT decoded.

    // rate_array u32LE[50] @ 0x68. CONSUMED. ÷1,000,000 = rate fraction. CONFIRMED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED / HIGH (÷1e6 rate).
    // NOTE: formerly named 'ids_array_a' — CORRECTED to 'rate_array' (values are rates, not IDs).
    private const int OffRateArray = 0x68;
    private const int RateArrayCount = 50; // spec: §1.3 — "Fixed slot of up to 50 u32 entries": CONFIRMED.

    // actor_array u32LE[52] @ 0x130. CONSUMED. 9-digit actor IDs. CONFIRMED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED / HIGH.
    // NOTE: formerly named 'ids_array_b' — CORRECTED to 'actor_array' (values are 9-digit actor IDs).
    private const int OffActorArray = 0x130;
    private const int ActorArrayCount = 52; // spec: §1.3 — "Fixed slot of up to 52 u32 entries": CONFIRMED.

    // record_trailer u8[8] @ 0x200. NOT-CONSUMED.
    // spec: Docs/RE/formats/events_scr.md §1.3 — record_trailer @ 0x200: CONFIRMED not-consumed; NOT decoded.

    /// <summary>
    ///     Parses <c>data/script/events.scr</c> into an array of <see cref="EventsScrRecord" />.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Length must be an exact multiple of 520.</param>
    /// <returns>All decoded records in on-disk order, including empty (all-zero) event slots.</returns>
    /// <exception cref="InvalidDataException">
    ///     Buffer length is not an exact multiple of 520 bytes.
    ///     spec: Docs/RE/formats/events_scr.md §1.1 — "record count = file_size / 520 (exact)": CONFIRMED.
    /// </exception>
    public static EventsScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Validate exact stride divisibility.
        // spec: Docs/RE/formats/events_scr.md §1.1 — "Record stride: 520 bytes; no remainder": CONFIRMED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"events.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/events_scr.md §1.1.");

        var count = span.Length / RecordStride;
        var records = new EventsScrRecord[count];

        // M4: stackalloc scratch buffers hoisted before the loop (CA2014 — must not stackalloc inside a loop).
        // Each iteration clears rateCount/actorCount and overwrites the scratch before calling ToArray().
        // spec: Docs/RE/formats/events_scr.md §1.3 — rate_array[50] / actor_array[52]: CONFIRMED CONSUMED.
        Span<uint> rateBuf = stackalloc uint[RateArrayCount];
        Span<uint> actorBuf = stackalloc uint[ActorArrayCount];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            // CONSUMED FIELD: event_id u32LE @ 0x00. Primary key. CONFIRMED.
            // spec: Docs/RE/formats/events_scr.md §1.3 — event_id u32LE @ 0x00: CONFIRMED CONSUMED.
            var eventId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // NOT-CONSUMED FIELD: event_type u16LE @ 0x04. Exposed for completeness; client does not read.
            // spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: CONFIRMED not-consumed.
            var eventType = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffEventType..]);

            // NOT-CONSUMED FIELD: day_count u16LE @ 0x06. Exposed for completeness; client does not read.
            // spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: CONFIRMED not-consumed.
            var dayCount = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffDayCount..]);

            // [skipped: reserved_a @0x08, level_min @0x4C, level_max @0x50, flags @0x54..0x63]
            // All NOT-CONSUMED — client loader ignores these; carried verbatim in Raw.
            // spec: Docs/RE/formats/events_scr.md §1.3 — confirmed not-consumed.

            // CONSUMED FIELD: mode_flag u16LE @ 0x64. CONFIRMED.
            // One consumer branches on == 1, another on == 0.
            // spec: Docs/RE/formats/events_scr.md §1.3 — mode_flag u16LE @ 0x64: CONFIRMED CONSUMED.
            var modeFlag = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffModeFlag..]);

            // [skipped: pad_66 @0x66 — 2 bytes, always 0, NOT-CONSUMED]
            // spec: Docs/RE/formats/events_scr.md §1.3 — pad_66: NOT decoded.

            // CONSUMED FIELD: rate_array u32LE[50] @ 0x68. CONFIRMED.
            // The client divides each value by 1,000,000 to get a rate fraction (%).
            // Zero-terminated: iteration stops at first zero slot.
            // spec: Docs/RE/formats/events_scr.md §1.3 — rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED.
            // spec: Docs/RE/formats/events_scr.md §1.7 — "÷1,000,000 = rate fraction displayed as %": HIGH.
            // NOTE: formerly 'ids_array_a' — corrected to 'rate_array' per spec update.
            // M4: scratch buffer hoisted before the loop; reset count and fill for this record.
            var rateCount = 0;
            for (var k = 0; k < RateArrayCount; k++)
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(
                    rec[(OffRateArray + k * 4)..]);
                if (v == 0) break;
                rateBuf[rateCount++] = v;
            }

            IReadOnlyList<uint> rateArray = rateCount == 0
                ? Array.Empty<uint>()
                : rateBuf[..rateCount].ToArray();

            // CONSUMED FIELD: actor_array u32LE[52] @ 0x130. CONFIRMED.
            // Values are 9-digit actor IDs (same namespace as items.scr / citems.scr).
            // Zero-terminated: iteration stops at first non-positive/zero slot.
            // spec: Docs/RE/formats/events_scr.md §1.3 — actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED / HIGH.
            // NOTE: formerly 'ids_array_b' — corrected to 'actor_array' per spec update.
            // M4: scratch buffer hoisted before the loop; reset count and fill for this record.
            var actorCount = 0;
            for (var k = 0; k < ActorArrayCount; k++)
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(
                    rec[(OffActorArray + k * 4)..]);
                if (v == 0) break;
                actorBuf[actorCount++] = v;
            }

            IReadOnlyList<uint> actorArray = actorCount == 0
                ? Array.Empty<uint>()
                : actorBuf[..actorCount].ToArray();

            // [skipped: record_trailer @0x200 — 8 bytes, NOT-CONSUMED]
            // spec: Docs/RE/formats/events_scr.md §1.3 — record_trailer @ 0x200: CONFIRMED not-consumed.

            records[i] = new EventsScrRecord
            {
                EventId = eventId,
                EventType = eventType,
                DayCount = dayCount,
                ModeFlag = modeFlag,
                RateArray = rateArray,
                ActorArray = actorArray,
                Raw = data.Slice(recBase, RecordStride)
            };
        }

        return records;
    }
}