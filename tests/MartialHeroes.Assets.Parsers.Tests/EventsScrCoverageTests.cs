using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Additional coverage tests for <see cref="EventsScrParser"/> (events.scr).
/// Complements the regression tests in VfsDeepIISpecCorrectionTests.cs by covering:
/// stride validation, not-consumed field decoding, multi-record walk, raw-slice length,
/// and actor_array up to 52 slots.
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/events_scr.md §1.
/// </summary>
public sealed class EventsScrCoverageTests
{
    // Record stride: 520 bytes (0x208). CONFIRMED.
    // spec: Docs/RE/formats/events_scr.md §1.1 — "Record stride: 520 bytes (0x208)": CONFIRMED.
    private const int RecordStride = 520; // 0x208

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static byte[] BuildRecord(
        uint eventId = 10551u,
        ushort eventType = 1,
        ushort dayCount = 7,
        ushort modeFlag = 1,
        uint[]? rateArray = null,
        uint[]? actorArray = null)
    {
        byte[] buf = new byte[RecordStride];

        // CONSUMED: event_id u32LE @ 0x00. CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_id u32LE @ 0x00: CONFIRMED CONSUMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), eventId);

        // NOT-CONSUMED: event_type u16LE @ 0x04. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: CONFIRMED not-consumed.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x04, 2), eventType);

        // NOT-CONSUMED: day_count u16LE @ 0x06. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: CONFIRMED not-consumed.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x06, 2), dayCount);

        // CONSUMED: mode_flag u16LE @ 0x64. CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — mode_flag u16LE @ 0x64: CONFIRMED CONSUMED.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x64, 2), modeFlag);

        // CONSUMED: rate_array u32LE[50] @ 0x68. CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — rate_array u32LE[50] @ 0x68: CONFIRMED CONSUMED.
        if (rateArray != null)
            for (int k = 0; k < Math.Min(rateArray.Length, 50); k++)
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x68 + k * 4, 4), rateArray[k]);

        // CONSUMED: actor_array u32LE[52] @ 0x130. CONFIRMED.
        // spec: Docs/RE/formats/events_scr.md §1.3 — actor_array u32LE[52] @ 0x130: CONFIRMED CONSUMED.
        if (actorArray != null)
            for (int k = 0; k < Math.Min(actorArray.Length, 52); k++)
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x130 + k * 4, 4), actorArray[k]);

        return buf;
    }

    // =========================================================================
    // 1. Stride validation
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_YieldsEmptyArray()
    {
        // 0 % 520 == 0 — valid empty file.
        // spec: Docs/RE/formats/events_scr.md §1.1 — "record count = file_size / 520 (exact)": CONFIRMED.
        EventsScrRecord[] result = EventsScrParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NotMultipleOfStride_ThrowsInvalidDataException()
    {
        // 100 is not divisible by 520.
        // spec: Docs/RE/formats/events_scr.md §1.1 — "file_size % 520 == 0": CONFIRMED.
        byte[] buf = new byte[100];
        Assert.Throws<InvalidDataException>(() => EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_ExactlyOneRecord_DecodesSingleEntry()
    {
        // Exactly 520 bytes = one record.
        // spec: Docs/RE/formats/events_scr.md §1.1 — stride 520 bytes: CONFIRMED.
        byte[] buf = BuildRecord(eventId: 10551u);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(result);
    }

    // =========================================================================
    // 2. Not-consumed fields (exposed for completeness; client does not read them)
    // =========================================================================

    [Fact]
    public void Parse_EventType_NotConsumedField_DecodedFromOffset0x04()
    {
        // event_type u16LE @ 0x04. NOT-CONSUMED but exposed on the model.
        // spec: Docs/RE/formats/events_scr.md §1.3 — event_type u16LE @ 0x04: CONFIRMED not-consumed.
        byte[] buf = BuildRecord(eventType: 1);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal((ushort)1, result[0].EventType);
    }

    [Fact]
    public void Parse_DayCount_NotConsumedField_DecodedFromOffset0x06()
    {
        // day_count u16LE @ 0x06. NOT-CONSUMED but exposed on the model.
        // spec: Docs/RE/formats/events_scr.md §1.3 — day_count u16LE @ 0x06: CONFIRMED not-consumed.
        byte[] buf = BuildRecord(dayCount: 7);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal((ushort)7, result[0].DayCount);
    }

    // =========================================================================
    // 3. Raw slice
    // =========================================================================

    [Fact]
    public void Parse_RawSlice_HasStrideLength()
    {
        // Raw slice must be exactly 520 bytes (one full record).
        // spec: Docs/RE/formats/events_scr.md §1.3 — "stride 520 bytes": CONFIRMED.
        byte[] buf = BuildRecord(eventId: 12000u);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(520, result[0].Raw.Length);
    }

    // =========================================================================
    // 4. Multi-record walk
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_IndependentDecoding()
    {
        // Two consecutive 520-byte records must decode without cross-record bleed.
        // spec: Docs/RE/formats/events_scr.md §1.1 — stride 520 bytes: CONFIRMED.
        byte[] r0 = BuildRecord(eventId: 10551u, modeFlag: 0);
        byte[] r1 = BuildRecord(eventId: 20001u, modeFlag: 1);
        byte[] buf = new byte[r0.Length + r1.Length];
        r0.CopyTo(buf, 0);
        r1.CopyTo(buf, r0.Length);

        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal(10551u, result[0].EventId);
        Assert.Equal((ushort)0, result[0].ModeFlag);
        Assert.Equal(20001u, result[1].EventId);
        Assert.Equal((ushort)1, result[1].ModeFlag);
    }

    // =========================================================================
    // 5. RateArray max-slot boundary (50 slots)
    // =========================================================================

    [Fact]
    public void Parse_RateArray_FullSlot50_AllDecoded()
    {
        // rate_array holds up to 50 entries; a full-slot array is zero-terminated by convention
        // but the parser must read exactly the non-zero run.
        // spec: Docs/RE/formats/events_scr.md §1.3 — "Fixed slot of up to 50 u32 entries": CONFIRMED.
        var rates = new uint[50];
        for (int i = 0; i < 50; i++) rates[i] = (uint)(i + 1) * 10000u;

        byte[] buf = BuildRecord(rateArray: rates);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(50, result[0].RateArray.Count);
        Assert.Equal(10000u, result[0].RateArray[0]);
        Assert.Equal(500000u, result[0].RateArray[49]); // 50 × 10000
    }

    // =========================================================================
    // 6. ActorArray max-slot boundary (52 slots)
    // =========================================================================

    [Fact]
    public void Parse_ActorArray_FullSlot52_AllDecoded()
    {
        // actor_array holds up to 52 entries.
        // spec: Docs/RE/formats/events_scr.md §1.3 — "Fixed slot of up to 52 u32 entries": CONFIRMED.
        var actors = new uint[52];
        for (int i = 0; i < 52; i++) actors[i] = 210000000u + (uint)i;

        byte[] buf = BuildRecord(actorArray: actors);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(52, result[0].ActorArray.Count);
        Assert.Equal(210000000u, result[0].ActorArray[0]);
        Assert.Equal(210000051u, result[0].ActorArray[51]);
    }

    // =========================================================================
    // 7. Sparse record (event_id present, arrays empty)
    // =========================================================================

    [Fact]
    public void Parse_SparseRecord_EventIdSet_ArraysEmpty()
    {
        // Sparsely populated: event_id is non-zero but arrays are all-zero.
        // spec: Docs/RE/formats/events_scr.md §1.2 — "sparsely populated".
        byte[] buf = BuildRecord(eventId: 99999u, rateArray: null, actorArray: null);
        EventsScrRecord[] result = EventsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(99999u, result[0].EventId);
        Assert.Empty(result[0].RateArray);
        Assert.Empty(result[0].ActorArray);
    }
}