using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="DoStanceParser"/> and <see cref="DoStanceTable"/>.
/// All synthetic fixtures are hand-built in-memory from the spec; no real game bytes are committed.
/// spec: Docs/RE/formats/ui_manifests.md §2.7 Per-class stance .do files.
/// </summary>
public sealed class DoStanceParserTests
{
    // ─── layout constants (cited from spec) ──────────────────────────────────

    // Record stride: 116 bytes (0x74).
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "fixed 116-byte records": SAMPLE-VERIFIED.
    private const int Stride = DoStanceRecord.Stride; // 116

    // ─── fixture helpers ─────────────────────────────────────────────────────

    private static void WriteU16LE(byte[] buf, int offset, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), v);

    private static void WriteI16LE(byte[] buf, int offset, short v) =>
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(offset, 2), v);

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    /// <summary>
    /// Writes a single spec-compliant record into <paramref name="buf"/> at
    /// <c>recordIndex × 116</c>.
    /// All field offsets are spec-cited.
    /// </summary>
    private static void WriteRecord(
        byte[] buf,
        int recordIndex,
        uint instanceKey = 0,
        uint groupSubIndex = 0,
        uint slotIndex = 0,
        uint classStanceRef = 1001,
        uint groupId = 19,
        ushort secondaryXVariant = 0,
        short iconSrcX = 0,
        short iconSrcY = 0,
        ushort secondarySpriteX = 0,
        ushort secondarySpriteY = 0)
    {
        int b = recordIndex * Stride;

        // +0x00 u32 instanceKey. spec: §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED.
        WriteU32LE(buf, b + 0x00, instanceKey);

        // +0x04 u32 groupSubIndex. spec: §2.7 SAMPLE-VERIFIED.
        WriteU32LE(buf, b + 0x04, groupSubIndex);

        // +0x08 u32 slotIndex. spec: §2.7 CODE-CONFIRMED.
        WriteU32LE(buf, b + 0x08, slotIndex);

        // +0x0C u32 classStanceRef. spec: §2.7 CODE-CONFIRMED (1001/1002/1003).
        WriteU32LE(buf, b + 0x0C, classStanceRef);

        // +0x10 u32 groupId. spec: §2.7 SAMPLE-VERIFIED.
        WriteU32LE(buf, b + 0x10, groupId);

        // +0x14 u16 (secondary X variant). spec: §2.7 SAMPLE-VERIFIED (pattern); name UNKNOWN.
        WriteU16LE(buf, b + 0x14, secondaryXVariant);

        // +0x18 i16 iconSrcX. spec: §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED.
        WriteI16LE(buf, b + 0x18, iconSrcX);

        // +0x1C i16 iconSrcY. spec: §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED.
        WriteI16LE(buf, b + 0x1C, iconSrcY);

        // +0x20 u16 secondarySpriteX. spec: §2.7 SAMPLE-VERIFIED (pattern); name UNKNOWN.
        WriteU16LE(buf, b + 0x20, secondarySpriteX);

        // +0x24 u16 secondarySpriteY. spec: §2.7 SAMPLE-VERIFIED (pattern); name UNKNOWN.
        WriteU16LE(buf, b + 0x24, secondarySpriteY);

        // +0x28..+0x73: tail 72 bytes — remain zero (fixture default). spec: §2.7 UNKNOWN.
    }

    // ─── layout-constant tests ────────────────────────────────────────────────

    [Fact]
    public void Constants_MatchSpec()
    {
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — "fixed 116-byte records" (0x74): SAMPLE-VERIFIED.
        Assert.Equal(116, DoStanceRecord.Stride);
        Assert.Equal(0x74, DoStanceRecord.Stride);

        // Tail = Stride(116) − TailOffset(0x28=40) = 76 bytes.
        // The spec text says "72 bytes at +0x28" but 116 − 40 = 76 is the mathematically correct tail length.
        // spec: §2.7 — "+0x28..+0x73 unmapped": UNKNOWN.
        Assert.Equal(76, DoStanceRecord.TailByteCount);
        Assert.Equal(DoStanceRecord.Stride - 0x28, DoStanceRecord.TailByteCount);

        // DoStanceTable exposes the same stride constant.
        Assert.Equal(DoStanceRecord.Stride, DoStanceTable.RecordStride);
    }

    // ─── empty buffer tests ──────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyTable()
    {
        // An empty span produces a table with zero records and zero total-count.
        DoStanceTable table = DoStanceParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Equal(0, table.TotalRecordCount);
        Assert.Empty(table.Records);
        Assert.Equal(0, table.TrailingByteCount);
    }

    [Fact]
    public void Parse_MemoryOverload_EmptyBuffer_ReturnsEmptyTable()
    {
        DoStanceTable table = DoStanceParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(0, table.TotalRecordCount);
        Assert.Empty(table.Records);
    }

    // ─── all-zero record skip tests ──────────────────────────────────────────

    [Fact]
    public void Parse_SingleAllZeroRecord_IsSkipped()
    {
        // A single 116-byte all-zero record must be skipped.
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — "skip all-zero records".
        byte[] data = new byte[Stride]; // all zeros

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(1, table.TotalRecordCount); // one record seen
        Assert.Empty(table.Records); // zero non-zero records
    }

    [Fact]
    public void Parse_TwoRecords_FirstAllZero_SecondNonZero_SkipsFirst()
    {
        // Only the non-zero record should appear in Records.
        // spec: §2.7 — "skip all-zero records".
        byte[] data = new byte[2 * Stride];
        // record 0 all-zero (default)
        // record 1 non-zero
        WriteRecord(data, 1,
            instanceKey: 131101011,
            slotIndex: 1,
            classStanceRef: 1001,
            iconSrcX: 23,
            iconSrcY: 0);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(2, table.TotalRecordCount);
        Assert.Single(table.Records);
        Assert.Equal(131101011u, table.Records[0].InstanceKey);
    }

    // ─── single-record round-trip tests ─────────────────────────────────────

    [Fact]
    public void Parse_InstanceKey_RoundTrip()
    {
        // spec: §2.7 — "+0x00 u32 instanceKey": CODE-CONFIRMED + SAMPLE-VERIFIED.
        // Worked example: musajung.do record 0 instanceKey = 131101011.
        // spec: §2.7 Worked examples — "record 0 instanceKey = 131101011".
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 131101011, slotIndex: 0, classStanceRef: 1001);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Single(table.Records);
        Assert.Equal(131101011u, table.Records[0].InstanceKey);
    }

    [Fact]
    public void Parse_SlotIndex_RoundTrip()
    {
        // spec: §2.7 — "+0x08 u32 slotIndex": CODE-CONFIRMED.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 131101021, slotIndex: 5, classStanceRef: 1001);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Single(table.Records);
        Assert.Equal(5u, table.Records[0].SlotIndex);
    }

    [Fact]
    public void Parse_ClassStanceRef_RoundTrip()
    {
        // spec: §2.7 — "+0x0C u32 classStanceRef": CODE-CONFIRMED.
        // musajung = 1001, musasa = 1002, musama = 1003.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, classStanceRef: 1002);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(1002u, table.Records[0].ClassStanceRef);
    }

    [Fact]
    public void Parse_IconSrcX_RoundTrip()
    {
        // spec: §2.7 — "+0x18 i16 iconSrcX": CODE-CONFIRMED + SAMPLE-VERIFIED.
        // Observed: 0, 23, 46, 69 … and non-multiples like 62.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, iconSrcX: 46);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal((short)46, table.Records[0].IconSrcX);
    }

    [Fact]
    public void Parse_IconSrcY_RoundTrip()
    {
        // spec: §2.7 — "+0x1C i16 iconSrcY": CODE-CONFIRMED + SAMPLE-VERIFIED.
        // Record 6 in musajung.do has iconSrcY = 62 (not a multiple of 23).
        // spec: §2.7 Worked examples — "record 6 iconSrcY = 62".
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 6, iconSrcY: 62);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal((short)62, table.Records[0].IconSrcY);
    }

    [Fact]
    public void Parse_GroupId_RoundTrip()
    {
        // spec: §2.7 — "+0x10 u32 groupId": SAMPLE-VERIFIED.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, groupId: 185);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(185u, table.Records[0].GroupId);
    }

    [Fact]
    public void Parse_GroupSubIndex_RoundTrip()
    {
        // spec: §2.7 — "+0x04 u32 groupSubIndex": SAMPLE-VERIFIED.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, groupSubIndex: 2);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(2u, table.Records[0].GroupSubIndex);
    }

    [Fact]
    public void Parse_SecondarySpriteX_RoundTrip()
    {
        // spec: §2.7 — "+0x20 u16 secondarySpriteX": SAMPLE-VERIFIED (pattern); name UNKNOWN.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, secondarySpriteX: 87);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal((ushort)87, table.Records[0].SecondarySpriteX);
    }

    [Fact]
    public void Parse_SecondarySpriteY_RoundTrip()
    {
        // spec: §2.7 — "+0x24 u16 secondarySpriteY": SAMPLE-VERIFIED (pattern); name UNKNOWN.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, secondarySpriteY: 36);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal((ushort)36, table.Records[0].SecondarySpriteY);
    }

    [Fact]
    public void Parse_Tail_AllZeroByDefault()
    {
        // The unmapped tail (+0x28..+0x73 = 76 bytes) must round-trip from the fixture.
        // In this fixture the tail bytes are all zero.
        // spec: §2.7 — "+0x28..+0x73 unmapped": UNKNOWN.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(DoStanceRecord.TailByteCount, table.Records[0].Tail.AsReadOnlySpan().Length);
        Assert.All(table.Records[0].Tail.AsReadOnlySpan().ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Parse_Tail_NonZeroBytes_RoundTrip()
    {
        // A sentinel byte in the tail must survive the round-trip.
        // Tail spans +0x28..+0x73 = 76 bytes (116 - 40 = 76).
        // spec: §2.7 — "+0x28..+0x73 unmapped": UNKNOWN.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0);
        // Write sentinels at tail offset 0 (absolute 0x28) and last byte (absolute 0x73).
        data[0x28] = 0xAB; // tail[0]
        data[0x73] = 0xCD; // tail[75] — last byte of record (0x73 - 0x28 = 0x4B = 75)

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        ReadOnlySpan<byte> tail = table.Records[0].Tail.AsReadOnlySpan();
        Assert.Equal(DoStanceRecord.TailByteCount, tail.Length); // 76
        Assert.Equal(0xAB, tail[0]);
        Assert.Equal(0xCD, tail[75]); // 0x73 - 0x28 = 75
    }

    // ─── multi-record tests ───────────────────────────────────────────────────

    [Fact]
    public void Parse_SixRecords_MatchWorkedExamples()
    {
        // Reproduces the six worked examples from the spec table.
        // spec: Docs/RE/formats/ui_manifests.md §2.7 Worked examples — musajung.do records 0..5.
        // Record | instanceKey  | slotIndex | classStanceRef | iconSrcX | iconSrcY
        //   0    | 131101011    | 0         | 1001           | 0        | 0
        //   1    | 131101021    | 1         | 1001           | 23       | 0
        //   2    | 131101031    | 2         | 1001           | 46       | 0
        //   3    | 131101041    | 3         | 1001           | 69       | 0
        //   4    | 131101051    | 4         | 1001           | 92       | 0
        //   5    | 131101061    | 5         | 1001           | 115      | 0
        byte[] data = new byte[6 * Stride];
        WriteRecord(data, 0, instanceKey: 131101011, slotIndex: 0, classStanceRef: 1001, iconSrcX: 0, iconSrcY: 0);
        WriteRecord(data, 1, instanceKey: 131101021, slotIndex: 1, classStanceRef: 1001, iconSrcX: 23, iconSrcY: 0);
        WriteRecord(data, 2, instanceKey: 131101031, slotIndex: 2, classStanceRef: 1001, iconSrcX: 46, iconSrcY: 0);
        WriteRecord(data, 3, instanceKey: 131101041, slotIndex: 3, classStanceRef: 1001, iconSrcX: 69, iconSrcY: 0);
        WriteRecord(data, 4, instanceKey: 131101051, slotIndex: 4, classStanceRef: 1001, iconSrcX: 92, iconSrcY: 0);
        WriteRecord(data, 5, instanceKey: 131101061, slotIndex: 5, classStanceRef: 1001, iconSrcX: 115, iconSrcY: 0);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(6, table.TotalRecordCount);
        Assert.Equal(6, table.Records.Count);

        // Verify each worked example.
        // spec: §2.7 Worked examples — records 0..5.
        Assert.Equal(131101011u, table.Records[0].InstanceKey);
        Assert.Equal(0u, table.Records[0].SlotIndex);
        Assert.Equal(1001u, table.Records[0].ClassStanceRef);
        Assert.Equal((short)0, table.Records[0].IconSrcX);
        Assert.Equal((short)0, table.Records[0].IconSrcY);

        Assert.Equal(131101021u, table.Records[1].InstanceKey);
        Assert.Equal(1u, table.Records[1].SlotIndex);
        Assert.Equal((short)23, table.Records[1].IconSrcX);

        Assert.Equal(131101031u, table.Records[2].InstanceKey);
        Assert.Equal((short)46, table.Records[2].IconSrcX);

        Assert.Equal(131101041u, table.Records[3].InstanceKey);
        Assert.Equal((short)69, table.Records[3].IconSrcX);

        Assert.Equal(131101051u, table.Records[4].InstanceKey);
        Assert.Equal((short)92, table.Records[4].IconSrcX);

        Assert.Equal(131101061u, table.Records[5].InstanceKey);
        Assert.Equal((short)115, table.Records[5].IconSrcX);
    }

    [Fact]
    public void Parse_Record6_IconSrcY62_NonMultipleOf23()
    {
        // Record 6 in musajung.do has iconSrcY = 62, which is NOT a multiple of 23.
        // This confirms the coordinates are authored data, not a formula.
        // spec: §2.7 Worked examples — "record 6 iconSrcX=0 iconSrcY=62 (not a multiple of 23)".
        // spec: §2.7 — "23-px step in X is common but stored data; iconSrcY=62 at record 6 confirms non-uniform stride".
        byte[] data = new byte[7 * Stride]; // records 0..6
        // records 0..5 with iconSrcX 0..115 and iconSrcY 0 (all non-zero via instanceKey)
        for (int i = 0; i < 6; i++)
            WriteRecord(data, i, instanceKey: (uint)(131101011 + i * 10), slotIndex: (uint)i,
                classStanceRef: 1001, iconSrcX: (short)(i * 23), iconSrcY: 0);
        // record 6 with iconSrcY = 62
        WriteRecord(data, 6, instanceKey: 131101071, slotIndex: 6,
            classStanceRef: 1001, iconSrcX: 0, iconSrcY: 62);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(7, table.TotalRecordCount);
        Assert.Equal(7, table.Records.Count);
        Assert.Equal((short)62, table.Records[6].IconSrcY);
        Assert.Equal((short)0, table.Records[6].IconSrcX);
    }

    // ─── tail count / truncated file tests ──────────────────────────────────

    [Fact]
    public void Parse_TrailingBytes_NotMultipleOfStride_AreIgnored()
    {
        // musama.do has 25,792 bytes = 222 full records + 40 trailing bytes (ignored).
        // spec: §2.7 — "musama.do: 25,792 bytes = 222 records + 40 trailing bytes (ignored)": SAMPLE-VERIFIED.
        // We mimic this with 2 full records + 40 extra bytes.
        int fileSize = 2 * Stride + 40;
        byte[] data = new byte[fileSize];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 0, classStanceRef: 1003);
        WriteRecord(data, 1, instanceKey: 2, slotIndex: 1, classStanceRef: 1003);
        // bytes [2*116 .. 2*116+39] remain zero (trailing fragment).

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(2, table.TotalRecordCount); // 2 complete records
        Assert.Equal(40, table.TrailingByteCount);
        Assert.Equal(2, table.Records.Count);
    }

    [Fact]
    public void Parse_ShortBuffer_LessThanOneRecord_ReturnsEmptyTable()
    {
        // A buffer shorter than one stride (e.g. 80 bytes) contains no complete records.
        byte[] data = new byte[80];
        data[0] = 0x01; // non-zero to ensure it's not all-zero

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(0, table.TotalRecordCount);
        Assert.Equal(80, table.TrailingByteCount);
        Assert.Empty(table.Records);
    }

    // ─── lookup tests ────────────────────────────────────────────────────────

    [Fact]
    public void GetByInstanceKey_ExistingKey_ReturnsRecord()
    {
        // spec: §2.7 — "Map A keyed by instanceKey (+0x00)": CODE-CONFIRMED.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 131101011, slotIndex: 0, classStanceRef: 1001);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        var found = table.GetByInstanceKey(131101011);
        Assert.NotNull(found);
        Assert.Equal(131101011u, found!.InstanceKey);
    }

    [Fact]
    public void GetByInstanceKey_MissingKey_ReturnsNull()
    {
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 131101011, slotIndex: 0);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Null(table.GetByInstanceKey(999999));
    }

    [Fact]
    public void GetBySlotIndex_ExistingKey_ReturnsRecord()
    {
        // spec: §2.7 — "Map B keyed by slotIndex (+0x08)": CODE-CONFIRMED.
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 131101011, slotIndex: 7, classStanceRef: 1001);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        var found = table.GetBySlotIndex(7);
        Assert.NotNull(found);
        Assert.Equal(7u, found!.SlotIndex);
    }

    [Fact]
    public void GetBySlotIndex_MissingKey_ReturnsNull()
    {
        byte[] data = new byte[Stride];
        WriteRecord(data, 0, instanceKey: 1, slotIndex: 3);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Null(table.GetBySlotIndex(99));
    }

    // ─── musajung.do file-size sanity test ──────────────────────────────────

    [Fact]
    public void Parse_MusajungDo_SyntheticSize_301Records()
    {
        // musajung.do is exactly 34,916 bytes = 301 records × 116 bytes, tail 0.
        // spec: §2.7 — "musajung.do: 34,916 bytes = exactly 301 records, tail 0 bytes": SAMPLE-VERIFIED.
        int expectedSize = 301 * Stride;
        Assert.Equal(34916, expectedSize);

        byte[] data = new byte[expectedSize];
        // Populate all 301 records with distinct instanceKeys.
        for (int i = 0; i < 301; i++)
            WriteRecord(data, i, instanceKey: (uint)(100000000 + i), slotIndex: (uint)i, classStanceRef: 1001);

        DoStanceTable table = DoStanceParser.Parse(data.AsSpan());

        Assert.Equal(301, table.TotalRecordCount);
        Assert.Equal(301, table.Records.Count);
        Assert.Equal(0, table.TrailingByteCount);
    }

    // ─── DoStanceTail72 inline-array tests ───────────────────────────────────

    [Fact]
    public void Tail_Length_Is76()
    {
        // The DoStanceTail72 inline-array must expose exactly TailByteCount = 76 bytes.
        // (76 = Stride(116) − TailOffset(0x28=40); spec says "72" but math requires 76.)
        // spec: §2.7 — "+0x28..+0x73 unmapped": UNKNOWN.
        var tail = new DoStanceTail72();
        Assert.Equal(76, tail.AsSpan().Length);
        Assert.Equal(76, tail.AsReadOnlySpan().Length);
    }

    // =========================================================================
    // Real-VFS smoke tests (skipped when clientdata absent)
    // =========================================================================

    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsFilePath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsFilePath);

    [Fact]
    public void Smoke_MusajungDo_ParsesCorrectly()
    {
        // spec: §2.7 — "musajung.do: 34,916 bytes = 301 records, tail 0": SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/script/musajung.do");

        DoStanceTable table = DoStanceParser.Parse(data);

        // File must be exactly 301 records with no tail.
        // spec: §2.7 — "musajung.do: 34,916 bytes = 301 records, tail 0": SAMPLE-VERIFIED.
        Assert.Equal(34916, data.Length);
        Assert.Equal(301, table.TotalRecordCount);
        Assert.Equal(0, table.TrailingByteCount);

        // All records must have classStanceRef = 1001.
        // spec: §2.7 — "classStanceRef 1001 = musajung": CODE-CONFIRMED + SAMPLE-VERIFIED.
        Assert.All(table.Records, r => Assert.Equal(1001u, r.ClassStanceRef));

        // Record 0: instanceKey = 131101011 per worked example.
        // spec: §2.7 Worked examples — "record 0 instanceKey = 131101011": SAMPLE-VERIFIED.
        var r0 = table.GetBySlotIndex(0);
        Assert.NotNull(r0);
        Assert.Equal(131101011u, r0!.InstanceKey);

        // Record 0: iconSrcX = 0, iconSrcY = 0.
        // spec: §2.7 Worked examples — "record 0 iconSrcX=0 iconSrcY=0".
        Assert.Equal((short)0, r0.IconSrcX);
        Assert.Equal((short)0, r0.IconSrcY);

        // Record 1: iconSrcX = 23.
        // spec: §2.7 Worked examples — "record 1 iconSrcX=23 iconSrcY=0".
        var r1 = table.GetBySlotIndex(1);
        Assert.NotNull(r1);
        Assert.Equal((short)23, r1!.IconSrcX);

        // Record 6: iconSrcY = 62 (non-multiple of 23 — confirms data-driven, not formula).
        // spec: §2.7 Worked examples — "record 6 iconSrcX=0 iconSrcY=62".
        var r6 = table.GetBySlotIndex(6);
        Assert.NotNull(r6);
        Assert.Equal((short)62, r6!.IconSrcY);
    }

    [Fact]
    public void Smoke_MusasaDo_ParsesCorrectly()
    {
        // spec: §2.7 — "musasa.do: 34,916 bytes = 301 records": SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/script/musasa.do");

        DoStanceTable table = DoStanceParser.Parse(data);

        Assert.Equal(34916, data.Length);
        Assert.Equal(301, table.TotalRecordCount);
        Assert.Equal(0, table.TrailingByteCount);

        // classStanceRef for musasa must be 1002.
        // spec: §2.7 — "classStanceRef 1002 = musasa": CODE-CONFIRMED.
        Assert.All(table.Records, r => Assert.Equal(1002u, r.ClassStanceRef));
    }

    [Fact]
    public void Smoke_MusamaDo_Has222RecordsAnd40TrailingBytes()
    {
        // spec: §2.7 — "musama.do: 25,792 bytes = 222 records + 40 trailing bytes": SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/script/musama.do");

        DoStanceTable table = DoStanceParser.Parse(data);

        Assert.Equal(25792, data.Length);
        Assert.Equal(222, table.TotalRecordCount);
        Assert.Equal(40, table.TrailingByteCount);

        // classStanceRef for musama is expected to be 1003 but is listed as PLAUSIBLE/UNVERIFIED.
        // spec: §2.7 — "classStanceRef 1003 = musama": CODE-CONFIRMED (1001/1002/1003 for Musa);
        //   §9 item #11c — "classStanceRef for the nine non-Musa files: PLAUSIBLE pattern; UNVERIFIED values".
        // We only assert the structural invariants here; content assertions for non-Musa files are deferred.
        Assert.True(table.Records.Count > 0, "musama.do must contain at least one non-zero record.");
    }
}