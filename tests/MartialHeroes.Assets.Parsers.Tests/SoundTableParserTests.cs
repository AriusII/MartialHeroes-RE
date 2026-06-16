using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="SoundTableParser"/> and <see cref="SoundTableData"/>.
/// All synthetic fixtures are built in-memory from the spec; no real game bytes are committed.
/// spec: Docs/RE/formats/sound_tables.md
///
/// Two-witness stride correction (2026-06-15): the on-disk stride is 48 bytes (CONFIRMED).
/// The loader advances 0x30 bytes per record, iterates 256 records, reads 12288 bytes, and leaves
/// a 1024-byte unread trailer. The prior 52-byte reading (2026-06-14) is REFUTED.
/// The per-record tail_unknown field at +0x30 does NOT exist — it belonged to the file-level
/// unread trailer, not to per-record layout.
/// spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — loader stride reconciliation (two-witness).
/// </summary>
public sealed class SoundTableParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    /// <summary>
    /// Builds a synthetic sound-table byte buffer of exactly 13312 bytes (FixedFileSize).
    /// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
    /// spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness)."
    /// </summary>
    private static byte[] BuildNullTable()
    {
        // Null table: every byte zero → every entry has sound_entry_id = 0, weight = 0.0f.
        // Note: in real files the weight at +0x1C is 1.0f even in null records.
        // For fixture purposes, all-zero bytes are sufficient to test structural parsing.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — entry index 0 = null sentinel: CONFIRMED
        // Entire file is 13312 bytes: 256 records × 48 bytes (read by loader) + 1024-byte unread trailer.
        // spec: Docs/RE/formats/sound_tables.md §File layout — stride 48 bytes: CONFIRMED (two-witness, 2026-06-15).
        return new byte[SoundTableData.FixedFileSize];
    }

    /// <summary>
    /// Writes one entry into a sound-table buffer at the given entry index.
    /// spec: Docs/RE/formats/sound_tables.md §Per-record layout (all offsets): CONFIRMED
    /// </summary>
    private static void WriteEntry(
        byte[] buf,
        int entryIndex,
        uint soundEntryId,
        byte[]? hourSchedule = null,
        float weight = 1.0f,
        float posX = 0.0f,
        float posZ = 0.0f,
        float radius = 0.0f)
    {
        // stride is 48 bytes per entry. CONFIRMED (two-witness, 2026-06-15).
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness)."
        int entryBase = entryIndex * SoundTableData.EntryStride; // 48 bytes per entry

        // sound_entry_id u32 LE @ entry+0x00
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
        WriteU32LE(buf, entryBase + 0x00, soundEntryId);

        // hour_schedule u8×24 @ entry+0x04
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        var sched = hourSchedule ?? Enumerable.Repeat((byte)0x01, SoundTableData.HoursPerDay).ToArray();
        sched.AsSpan(0, SoundTableData.HoursPerDay).CopyTo(buf.AsSpan(entryBase + 0x04));

        // weight f32 LE @ entry+0x1C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED type/value; semantic UNVERIFIED
        WriteF32LE(buf, entryBase + 0x1C, weight);

        // pos_x f32 LE @ entry+0x20
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @ +0x20: CONFIRMED (runtime semantic)
        WriteF32LE(buf, entryBase + 0x20, posX);

        // +0x24 (unlabeled_24) — NOT read by the loader; meaning UNRESOLVED.
        // Left as zero in fixtures. The earlier 'pos_y' label is WITHDRAWN.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ; meaning UNRESOLVED.

        // pos_z f32 LE @ entry+0x28
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @ +0x28: CONFIRMED (runtime semantic)
        WriteF32LE(buf, entryBase + 0x28, posZ);

        // radius f32 LE @ entry+0x2C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
        WriteF32LE(buf, entryBase + 0x2C, radius);

        // End of 48-byte record. No tail field exists after +0x2C in the per-record layout.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — record ends at +0x2F (48 bytes total): CONFIRMED.
    }

    // ─── layout-constant tests ─────────────────────────────────────────────────

    [Fact]
    public void Constants_MatchSpec()
    {
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        Assert.Equal(0x3400, SoundTableData.FixedFileSize);
        Assert.Equal(13312, SoundTableData.FixedFileSize);

        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        Assert.Equal(256, SoundTableData.EntryCount);

        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness, 2026-06-15)."
        Assert.Equal(48, SoundTableData.EntryStride);

        // Consistency: 256 × 48 = 12288 = ReadSize
        Assert.Equal(SoundTableData.ReadSize,
            SoundTableData.EntryCount * SoundTableData.EntryStride);

        // Total file = read region + unread trailer
        // spec: Docs/RE/formats/sound_tables.md §File layout — ReadSize + TrailerSize = FixedFileSize: CONFIRMED
        Assert.Equal(SoundTableData.FixedFileSize,
            SoundTableData.ReadSize + SoundTableData.TrailerSize);
        Assert.Equal(12288, SoundTableData.ReadSize);
        Assert.Equal(1024, SoundTableData.TrailerSize);

        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        Assert.Equal(24, SoundTableData.HoursPerDay);
    }

    // ─── CONFIRMED BEHAVIOR: stride-52 reconciliation (2026-06-14) ────────────

    /// <summary>
    /// A synthetic 13312-byte table (256 records × 48 bytes + 1024-byte trailer) with a non-null
    /// record carrying a 9-digit sound_entry_id (910001000) at +0x00 and weight 1.0 (0x3F800000)
    /// at +0x1C must parse to exactly that id and weight.
    ///
    /// spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
    /// spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f
    /// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — 9-digit decimal values: CONFIRMED
    /// </summary>
    [Fact]
    public void Parse_NonNullRecord_9DigitSoundId_And_Weight1f_AtCorrectOffsets()
    {
        // Build 13312-byte fixture; write a non-null record at entry index 1.
        // sound_entry_id = 910001000 (9-digit, 0x363ACF28) @ +0x00
        // weight = 1.0f (0x3F800000) @ +0x1C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — stride 48 bytes: CONFIRMED (two-witness).
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 1, soundEntryId: 910_001_000, weight: 1.0f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        // sound_entry_id at +0x00 must read the 9-digit value, NOT the weight.
        // spec: Docs/RE/formats/sound_tables.md — sound_entry_id u32 @ +0x00: CONFIRMED
        Assert.Equal(910_001_000u, result.Entries[1].SoundEntryId);
        Assert.True(result.Entries[1].IsAssigned);

        // weight at +0x1C must read 1.0f independently from sound_entry_id.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f
        Assert.Equal(1.0f, result.Entries[1].Weight, precision: 6);

        // The two fields are distinct: sound_entry_id != bit-pattern of 1.0f (0x3F800000 = 1065353216).
        Assert.NotEqual(0x3F800000u, result.Entries[1].SoundEntryId);
    }

    /// <summary>
    /// A null/default record (all-zero bytes) must yield sound_entry_id == 0.
    /// This is valid in all table variants: .wlk and .run tables are commonly all-zero.
    /// spec: Docs/RE/formats/sound_tables.md §Record index 0 — null sentinel: CONFIRMED
    /// spec: Docs/RE/formats/sound_tables.md §Semantic mapping — ".wlk/.run contain only null records": SAMPLE-VERIFIED
    /// </summary>
    [Fact]
    public void Parse_NullRecord_AllZeroBytes_SoundEntryIdIsZero()
    {
        // A buffer of all zeros gives sound_entry_id = 0 for every record.
        // spec: Docs/RE/formats/sound_tables.md — sound_entry_id = 0 means empty/unassigned: CONFIRMED
        byte[] data = BuildNullTable(); // all zero

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Wlk);

        // All 256 records must have sound_entry_id = 0.
        Assert.Equal(256, result.Entries.Length);
        foreach (var entry in result.Entries)
            Assert.Equal(0u, entry.SoundEntryId);

        // Entry 0 is the null sentinel.
        // spec: Docs/RE/formats/sound_tables.md §Record index 0 — null sentinel: CONFIRMED
        Assert.False(result.Entries[0].IsAssigned);
    }

    // ─── null-table tests ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullTable_FullFile_EntryCountIs256()
    {
        // A null table (all zeroes, 13312 bytes) must decode to exactly 256 entries.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal(256, result.Entries.Length);
    }

    [Fact]
    public void Parse_NullTable_AllEntriesHaveIdZero()
    {
        // All entries in a null table must have sound_entry_id = 0.
        // spec: Docs/RE/formats/sound_tables.md — sound_entry_id = 0 means empty/unassigned: CONFIRMED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        foreach (var entry in result.Entries)
            Assert.Equal(0u, entry.SoundEntryId);
    }

    [Fact]
    public void Parse_NullTable_Entry0_IsNotAssigned()
    {
        // Entry 0 is always the null sentinel.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Entry index 0 is the null/disabled sentinel": CONFIRMED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.False(result.Entries[0].IsAssigned);
    }

    [Fact]
    public void Parse_NullTable_FullFileSize_Is13312Bytes()
    {
        // The file is 256 × 48 = 12288 bytes read + 1024-byte unread trailer = 13312 bytes total.
        // spec: Docs/RE/formats/sound_tables.md §File layout — stride 48 bytes: CONFIRMED (two-witness, 2026-06-15).
        // spec: Docs/RE/formats/sound_tables.md §File layout — fixed 13312 bytes (0x3400): CONFIRMED.
        byte[] data = BuildNullTable();
        Assert.Equal(SoundTableData.FixedFileSize, data.Length);
        Assert.Equal(13312, data.Length);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Wlk);

        Assert.Equal(256, result.Entries.Length);
    }

    [Fact]
    public void Parse_NullTable_Extension_PreservedOnResult()
    {
        // The extension passed to Parse must be faithfully exposed on the result.
        byte[] data = BuildNullTable();
        var resultBgm = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);
        var resultEff = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(SoundTableExtension.Bgm, resultBgm.Extension);
        Assert.Equal(SoundTableExtension.Eff, resultEff.Extension);
    }

    // ─── single-entry round-trip tests ────────────────────────────────────────

    [Fact]
    public void Parse_SingleEntry_SoundEntryId_RoundTrip()
    {
        // Write a known sound_entry_id at entry index 1 and verify round-trip.
        // spec: Docs/RE/formats/sound_tables.md — sound_entry_id u32 @ +0x00: CONFIRMED
        // Active samples carry 9-digit decimal values e.g. 910022000.
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — 9-digit decimal values: CONFIRMED
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 1, soundEntryId: 910_022_000);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal(910_022_000u, result.Entries[1].SoundEntryId);
        Assert.True(result.Entries[1].IsAssigned);
        Assert.Equal(0u, result.Entries[0].SoundEntryId); // null sentinel untouched
    }

    [Fact]
    public void Parse_SingleEntry_HourSchedule_AllActive_RoundTrip()
    {
        // All-0x01 hour schedule (unconditionally active — matches all 12 real samples).
        // spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04:
        //   "All 12 samples have every byte = 0x01 (unconditionally active)": CONFIRMED
        byte[] data = BuildNullTable();
        var sched = Enumerable.Repeat((byte)0x01, 24).ToArray();
        WriteEntry(data, entryIndex: 5, soundEntryId: 920_100_200, hourSchedule: sched);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(24, result.Entries[5].HourSchedule.Length);
        // HourSchedule24 is an [InlineArray] struct; iterate the span directly — no intermediate array.
        foreach (byte b in result.Entries[5].HourSchedule.AsReadOnlySpan())
            Assert.Equal(0x01, b);
    }

    [Fact]
    public void Parse_SingleEntry_HourSchedule_PartialActive_RoundTrip()
    {
        // Custom hour schedule where only hours 6–18 are active.
        // spec: Docs/RE/formats/sound_tables.md — hour_schedule[h] non-zero → active during hour h: CONFIRMED
        byte[] data = BuildNullTable();
        var sched = new byte[24]; // all zero (inactive)
        for (int h = 6; h < 18; h++)
            sched[h] = 0x01; // active during daytime

        WriteEntry(data, entryIndex: 10, soundEntryId: 910_034_000, hourSchedule: sched);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        // HourSchedule24 is an [InlineArray] struct; use AsReadOnlySpan() to index into it.
        ReadOnlySpan<byte> decoded = result.Entries[10].HourSchedule.AsReadOnlySpan();
        Assert.Equal(24, decoded.Length);
        // hours 0–5 inactive
        for (int h = 0; h < 6; h++)
            Assert.Equal(0, decoded[h]);
        // hours 6–17 active
        for (int h = 6; h < 18; h++)
            Assert.NotEqual(0, decoded[h]);
        // hours 18–23 inactive
        for (int h = 18; h < 24; h++)
            Assert.Equal(0, decoded[h]);
    }

    [Fact]
    public void Parse_SingleEntry_Weight_Is1f_RoundTrip()
    {
        // weight f32 @ +0x1C — confirmed 1.0f in ALL records (including null records) across
        // all ~300 sound tables per the 2026-06-14 reconciliation harness.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 3, soundEntryId: 910_053_002, weight: 1.0f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.Equal(1.0f, result.Entries[3].Weight, precision: 6);
    }

    [Fact]
    public void Parse_SingleEntry_PosX_RoundTrip()
    {
        // pos_x f32 @ +0x20 — world-space X for DirectSound3D.
        // spec: Docs/RE/formats/sound_tables.md — pos_x f32 @ +0x20: CONFIRMED (runtime semantic)
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 7, soundEntryId: 910_033_000, posX: 123.456f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(123.456f, result.Entries[7].PosX, precision: 3);
    }

    [Fact]
    public void Parse_SingleEntry_Unlabeled24_PreservedVerbatim()
    {
        // unlabeled_24 @ +0x24 — 4 bytes NOT read by the loader; preserved verbatim for round-trip.
        // The earlier 'pos_y' label is WITHDRAWN; the loader does not read this offset.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ by loader; meaning UNRESOLVED.
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 2, soundEntryId: 1);
        // Write a known pattern into the unlabeled_24 bytes directly (not via WriteEntry — it's not-read).
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2 * SoundTableData.EntryStride + 0x24, 4), 0xCAFEBABEu);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        // The parser reads and stores the bytes at +0x24 verbatim; the value must round-trip.
        Assert.Equal(0xCAFEBABEu, result.Entries[2].Unlabeled24);
    }

    [Fact]
    public void Parse_SingleEntry_PosZ_RoundTrip()
    {
        // pos_z f32 @ +0x28 — world-space Z for DirectSound3D.
        // spec: Docs/RE/formats/sound_tables.md — pos_z f32 @ +0x28: CONFIRMED (runtime semantic)
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 100, soundEntryId: 2, posZ: -456.789f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(-456.789f, result.Entries[100].PosZ, precision: 3);
    }

    [Fact]
    public void Parse_SingleEntry_Radius_RoundTrip()
    {
        // radius f32 @ +0x2C — audibility radius of the 3D source (EFF only).
        // Previously labelled volume_factor — superseded 2026-06-14.
        // spec: Docs/RE/formats/sound_tables.md — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 255, soundEntryId: 3, radius: 0.85f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(0.85f, result.Entries[255].Radius, precision: 5);
    }

    // ─── full-table tests ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_FullTable_EntryCount_256()
    {
        // A 13312-byte table with all entries populated must decode all 256.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        byte[] data = BuildNullTable();
        // Populate every slot with a unique id.
        for (int i = 0; i < 256; i++)
            WriteEntry(data, i, soundEntryId: (uint)(i + 1)); // id 0 is sentinel, start at 1

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal(256, result.Entries.Length);
        for (int i = 0; i < 256; i++)
            Assert.Equal((uint)(i + 1), result.Entries[i].SoundEntryId);
    }

    [Fact]
    public void Parse_FullTable_LastEntry_Index255_Accessible()
    {
        // Verify the last (256th) entry is accessible and round-trips correctly.
        // Entry 255 starts at byte 255 × 48 = 12240.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness)."
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 255, soundEntryId: 910_053_002, posX: 11.0f, posZ: 22.0f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(910_053_002u, result.Entries[255].SoundEntryId);
        Assert.Equal(11.0f, result.Entries[255].PosX, precision: 5);
        Assert.Equal(22.0f, result.Entries[255].PosZ, precision: 5);
    }

    [Fact]
    public void Parse_Memory_Overload_MatchesSpan_Overload()
    {
        // Both Parse overloads must produce structurally identical results.
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 50, soundEntryId: 920_100_200, weight: 1.0f);

        SoundTableData fromSpan = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);
        SoundTableData fromMemory = SoundTableParser.Parse(
            new ReadOnlyMemory<byte>(data), SoundTableExtension.Bgm);

        Assert.Equal(fromSpan.Entries.Length, fromMemory.Entries.Length);
        Assert.Equal(fromSpan.Entries[50].SoundEntryId, fromMemory.Entries[50].SoundEntryId);
        Assert.Equal(fromSpan.Entries[50].Weight, fromMemory.Entries[50].Weight, precision: 6);
    }

    // ─── truncation / invalid buffer tests ────────────────────────────────────

    [Fact]
    public void Parse_Truncated_BufferShorterThanFixedFileSize_ThrowsInvalidData()
    {
        // A buffer shorter than 13312 bytes must throw.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        byte[] tooShort = new byte[SoundTableData.FixedFileSize - 1]; // 13311
        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(tooShort.AsSpan(), SoundTableExtension.Bgm));
    }

    [Fact]
    public void Parse_EmptyBuffer_ThrowsInvalidData()
    {
        // An empty buffer must throw.
        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(ReadOnlySpan<byte>.Empty, SoundTableExtension.Bgm));
    }

    [Fact]
    public void Parse_BufferLargerThanFixedFileSize_ThrowsInvalidData()
    {
        // A buffer larger than 13312 bytes is structurally invalid.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        byte[] tooLong = new byte[SoundTableData.FixedFileSize + 1];
        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(tooLong.AsSpan(), SoundTableExtension.Bgm));
    }

    /// <summary>
    /// Any buffer length other than exactly 13312 bytes must throw
    /// <see cref="InvalidDataException"/>. The parser enforces the fixed file size.
    ///
    /// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
    /// spec: Docs/RE/formats/sound_tables.md §File layout — ReadSize (12288) + TrailerSize (1024) = FixedFileSize (13312): CONFIRMED
    /// </summary>
    [Theory]
    [InlineData(0)] // empty
    [InlineData(48)] // one 48-byte record — not 13312
    [InlineData(48 * 256)] // 12288 — only the read region, no trailer — rejected
    [InlineData(52)] // one 52-byte record (old stride) — rejected
    [InlineData(52 * 256)] // 13312? No: 52×256=13312 = FixedFileSize — filtered below (valid)
    [InlineData(52 * 255)] // 255 records at old stride — rejected
    [InlineData(52 * 257)] // 257 records at old stride — rejected
    [InlineData(100)] // arbitrary non-13312 length
    [InlineData(13311)] // off-by-one below
    [InlineData(13313)] // off-by-one above
    public void Parse_LengthNotExactly13312_ThrowsInvalidData(int length)
    {
        // The parser enforces exactly 13312 bytes. No other length is valid.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        if (length == SoundTableData.FixedFileSize)
            return; // 13312 is valid; this InlineData case is excluded

        byte[] buf = new byte[length];
        Assert.Throws<InvalidDataException>(() =>
            SoundTableParser.Parse(buf.AsSpan(), SoundTableExtension.Bgm));
    }

    // ─── audio-directory resolution tests ────────────────────────────────────

    [Fact]
    public void AudioDirectory_Bgm_Is_data_sound_2d()
    {
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bgm → data/sound/2d/: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal("data/sound/2d/", result.AudioDirectory);
    }

    [Fact]
    public void AudioDirectory_Eff_Is_data_sound_3d()
    {
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .eff (sound table) → data/sound/3d/: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal("data/sound/3d/", result.AudioDirectory);
    }

    [Fact]
    public void AudioDirectory_Bge_Is_data_sound_2d()
    {
        // CAMPAIGN 11 correction: .bge resolves to data/sound/2d/ (SAMPLE-VERIFIED 2026-06-14).
        // The previous "UNDETERMINED / null" reading is REFUTED — BGE IDs are confirmed under data/sound/2d/.
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bge → data/sound/2d/: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.Equal("data/sound/2d/", result.AudioDirectory);
    }

    [Fact]
    public void AudioDirectory_Wlk_IsNull_Undetermined()
    {
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .wlk → UNDETERMINED: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Wlk);

        Assert.Null(result.AudioDirectory);
    }

    [Fact]
    public void AudioDirectory_Run_IsNull_Undetermined()
    {
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .run → UNDETERMINED: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Run);

        Assert.Null(result.AudioDirectory);
    }

    // ─── ExtensionFromPath helper tests ──────────────────────────────────────

    [Fact]
    public void ExtensionFromPath_BgmFile_ReturnsBgm()
    {
        // spec: Docs/RE/formats/sound_tables.md §Identification — .bgm: CONFIRMED
        var ext = SoundTableParser.ExtensionFromPath("data/map002/soundtable2.bgm");
        Assert.Equal(SoundTableExtension.Bgm, ext);
    }

    [Fact]
    public void ExtensionFromPath_EffMapFile_ReturnsEff()
    {
        // spec: Docs/RE/formats/sound_tables.md §Identification — .eff (sound table variant): CONFIRMED
        var ext = SoundTableParser.ExtensionFromPath("data/map002/soundtable2.eff");
        Assert.Equal(SoundTableExtension.Eff, ext);
    }

    [Fact]
    public void ExtensionFromPath_WlkFile_ReturnsWlk()
    {
        // spec: Docs/RE/formats/sound_tables.md §Identification — .wlk: CONFIRMED
        var ext = SoundTableParser.ExtensionFromPath("data/map003/soundtable3.wlk");
        Assert.Equal(SoundTableExtension.Wlk, ext);
    }

    [Fact]
    public void ExtensionFromPath_RunFile_ReturnsRun()
    {
        // spec: Docs/RE/formats/sound_tables.md §Identification — .run: CONFIRMED
        var ext = SoundTableParser.ExtensionFromPath("data/map004/soundtable4.run");
        Assert.Equal(SoundTableExtension.Run, ext);
    }

    [Fact]
    public void ExtensionFromPath_BgeFile_ReturnsBge()
    {
        // spec: Docs/RE/formats/sound_tables.md §Identification — .bge: CONFIRMED
        var ext = SoundTableParser.ExtensionFromPath("data/map001/soundtable1.bge");
        Assert.Equal(SoundTableExtension.Bge, ext);
    }

    [Fact]
    public void ExtensionFromPath_GeometryEffPath_ThrowsArgumentException()
    {
        // Geometry-shape .eff at data/effect/obj/ must be rejected.
        // spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION
        Assert.Throws<ArgumentException>(() =>
            SoundTableParser.ExtensionFromPath("data/effect/obj/cone.eff"));
    }

    [Fact]
    public void ExtensionFromPath_UnknownExtension_ThrowsArgumentException()
    {
        // An unknown extension must throw.
        Assert.Throws<ArgumentException>(() =>
            SoundTableParser.ExtensionFromPath("data/map001/soundtable1.xyz"));
    }

    [Fact]
    public void ExtensionFromPath_CaseInsensitive()
    {
        // Extension matching is case-insensitive.
        var ext = SoundTableParser.ExtensionFromPath("data/map002/SOUNDTABLE2.BGM");
        Assert.Equal(SoundTableExtension.Bgm, ext);
    }

    // ─── SoundEntryId-to-filename semantics tests ─────────────────────────────

    [Fact]
    public void SoundEntryId_FormattedAsDecimalString_NoZeroPadding()
    {
        // The engine formats sound_entry_id as a plain decimal string for the filename stem.
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics —
        //   "plain decimal integer stem matching sound_entry_id, no zero-padding": CONFIRMED
        // e.g. 910022000 → "910022000.ogg"
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 1, soundEntryId: 910_022_000);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        uint id = result.Entries[1].SoundEntryId;
        string filename = id.ToString() + ".ogg";
        Assert.Equal("910022000.ogg", filename);
    }

    // ─── IsAssigned helper tests ──────────────────────────────────────────────

    [Fact]
    public void IsAssigned_ZeroId_ReturnsFalse()
    {
        // sound_entry_id = 0 → IsAssigned == false.
        // spec: Docs/RE/formats/sound_tables.md — "0 = empty/unassigned slot": CONFIRMED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);
        Assert.False(result.Entries[0].IsAssigned);
    }

    [Fact]
    public void IsAssigned_NonZeroId_ReturnsTrue()
    {
        // sound_entry_id != 0 → IsAssigned == true.
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 10, soundEntryId: 1);
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);
        Assert.True(result.Entries[10].IsAssigned);
    }

    // =========================================================================
    // Real-VFS smoke tests (skipped when clientdata absent)
    // =========================================================================

    /// <summary>
    /// Absolute path to the local clientdata directory (VFS root).
    /// User-supplied, not committed to the repository.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsPath);

    [Fact]
    public void Smoke_Area2_Bgm_ParsesCorrectStructure()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        // Real VFS path pattern: data/map<NNN>/soundtable<NNN>.<ext> with 3-digit zero-padded numbers.
        // spec: Docs/RE/formats/sound_tables.md §Identification — "data/map<id>/soundtable<id>.<ext>": CONFIRMED
        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        // Structural invariants that must always hold regardless of content.
        Assert.Equal(256, result.Entries.Length);
        Assert.Equal(SoundTableExtension.Bgm, result.Extension);
        Assert.Equal("data/sound/2d/", result.AudioDirectory);

        // Entry 0 is always the null sentinel.
        // spec: Docs/RE/formats/sound_tables.md §Record index 0 — null sentinel: CONFIRMED
        Assert.Equal(0u, result.Entries[0].SoundEntryId);
        Assert.False(result.Entries[0].IsAssigned);

        // All HourSchedule arrays must be exactly 24 bytes.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        Assert.All(result.Entries, e => Assert.Equal(24, e.HourSchedule.Length));
    }

    [Fact]
    public void Smoke_Area2_Bgm_Weight_Field_Accessible()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        // Two-witness stride correction (2026-06-15): stride is 48 bytes. The weight field at +0x1C
        // reads independently from sound_entry_id at +0x00 in both stride-48 and the refuted
        // stride-52 readings; the corrected stride does not affect the weight field position.
        // The spec documents weight = 1.0f for active BGM/BGE entries (SAMPLE-VERIFIED), but
        // null records (sound_entry_id = 0) may carry weight = 0.0f if those bytes are zero on disk.
        // This test confirms the field is structurally accessible — not out-of-bounds.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f (active entries)
        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        Assert.Equal(256, result.Entries.Length);
        // Weight is an f32; any finite value is structurally valid.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f
        // The 1.0f value is confirmed for BGM/BGE samples; structural validity (IsFinite) is
        // the invariant asserted here across all 256 records.
        Assert.All(result.Entries, e => Assert.True(float.IsFinite(e.Weight)));
    }

    [Fact]
    public void Smoke_Area2_Bgm_FileSize_Is13312Bytes()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        // The parser must accept the file without throwing.
        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        // Total size on disk must be exactly 13312 bytes.
        // Layout: 256 × 48 bytes (read by loader) + 1024 bytes (unread trailer) = 13312.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        // spec: Docs/RE/formats/sound_tables.md §File layout — stride 48 bytes: CONFIRMED (two-witness, 2026-06-15).
        Assert.Equal(SoundTableData.FixedFileSize, data.Length);
        Assert.Equal(256, result.Entries.Length);
    }

    [Fact]
    public void Smoke_Area2_Bgm_ActiveEntries_ParseWithoutError()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        // Stride 48 bytes (two-witness confirmed, 2026-06-15): parser must decode all 256 entries.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — stride 48 bytes: CONFIRMED (two-witness).
        // The 9-digit ID range (900000000..999999999) is documented for .eff samples; BGM IDs
        // in the real VFS may differ — structural validity (IsFinite, no OOB) is the invariant.
        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        Assert.Equal(256, result.Entries.Length);

        // Structural: every entry's float fields must be finite (no NaN / +Inf / -Inf).
        // Note: unlabeled_24 at +0x24 is a u32 (opaque, not-read by loader); not asserted as float.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ.
        Assert.All(result.Entries, e =>
        {
            Assert.True(float.IsFinite(e.PosX));
            Assert.True(float.IsFinite(e.PosZ));
            Assert.True(float.IsFinite(e.Radius));
        });
    }
}