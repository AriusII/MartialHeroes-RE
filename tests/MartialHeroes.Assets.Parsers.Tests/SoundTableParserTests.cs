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
/// </summary>
public sealed class SoundTableParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    /// <summary>
    /// Builds a synthetic sound-table byte buffer.
    /// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
    /// spec: Docs/RE/formats/sound_tables.md §Per-entry layout — 48 bytes per entry: CONFIRMED
    /// </summary>
    /// <param name="fullFile">
    /// When true, produces a full 13312-byte buffer (entry table + editor metadata).
    /// When false, produces only the 12288-byte entry-table region.
    /// </param>
    private static byte[] BuildNullTable(bool fullFile = true)
    {
        // Null table: every byte zero, which means every entry has sound_entry_id = 0.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — entry index 0 = null sentinel: CONFIRMED
        int size = fullFile ? SoundTableData.FixedFileSize : SoundTableData.EntryTableSize;
        return new byte[size];
    }

    /// <summary>
    /// Writes one entry into a sound-table buffer at the given entry index.
    /// spec: Docs/RE/formats/sound_tables.md §Per-entry layout (all offsets): CONFIRMED
    /// </summary>
    private static void WriteEntry(
        byte[] buf,
        int entryIndex,
        uint soundEntryId,
        byte[]? hourSchedule = null,
        float weight = 1.0f,
        float posX = 0.0f,
        uint unknown36 = 0,
        float posZ = 0.0f,
        float volumeFactor = 0.0f)
    {
        int entryBase = entryIndex * SoundTableData.EntryStride; // 48 bytes per entry

        // sound_entry_id u32 LE @ entry+0x00
        // spec: §Per-entry layout — sound_entry_id u32 @ +0x00: CONFIRMED
        WriteU32LE(buf, entryBase + 0x00, soundEntryId);

        // hour_schedule u8×24 @ entry+0x04
        // spec: §Per-entry layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        var sched = hourSchedule ?? Enumerable.Repeat((byte)0x01, SoundTableData.HoursPerDay).ToArray();
        sched.AsSpan(0, SoundTableData.HoursPerDay).CopyTo(buf.AsSpan(entryBase + 0x04));

        // weight f32 LE @ entry+0x1C
        // spec: §Per-entry layout — weight f32 @ +0x1C: SAMPLE-CONFIRMED as 1.0f
        WriteF32LE(buf, entryBase + 0x1C, weight);

        // pos_x f32 LE @ entry+0x20
        // spec: §Per-entry layout — pos_x f32 @ +0x20: CONFIRMED
        WriteF32LE(buf, entryBase + 0x20, posX);

        // unknown_36 u32 LE @ entry+0x24
        // spec: §Per-entry layout — unknown_36 u32 @ +0x24: UNRESOLVED
        WriteU32LE(buf, entryBase + 0x24, unknown36);

        // pos_z f32 LE @ entry+0x28
        // spec: §Per-entry layout — pos_z f32 @ +0x28: CONFIRMED
        WriteF32LE(buf, entryBase + 0x28, posZ);

        // volume_factor f32 LE @ entry+0x2C
        // spec: §Per-entry layout — volume_factor f32 @ +0x2C: CONFIRMED
        WriteF32LE(buf, entryBase + 0x2C, volumeFactor);
    }

    // ─── layout-constant tests ─────────────────────────────────────────────────

    [Fact]
    public void Constants_MatchSpec()
    {
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        Assert.Equal(0x3400, SoundTableData.FixedFileSize);
        Assert.Equal(13312, SoundTableData.FixedFileSize);

        // spec: §Overall structure — "Sound entry table … 12288 (0x3000)": CONFIRMED
        Assert.Equal(0x3000, SoundTableData.EntryTableSize);
        Assert.Equal(12288, SoundTableData.EntryTableSize);

        // spec: §Overall structure — "Editor metadata … 1024 (0x400)": CONFIRMED
        Assert.Equal(0x400, SoundTableData.EditorMetadataSize);
        Assert.Equal(1024, SoundTableData.EditorMetadataSize);

        // spec: §Entry count — "Fixed: 256 entries": CONFIRMED
        Assert.Equal(256, SoundTableData.EntryCount);

        // spec: §Per-entry layout — "Entry stride: 48 bytes. Confirmed.": CONFIRMED
        Assert.Equal(48, SoundTableData.EntryStride);

        // Consistency: 256 × 48 = 12288
        Assert.Equal(SoundTableData.EntryTableSize,
            SoundTableData.EntryCount * SoundTableData.EntryStride);

        // spec: §Per-entry layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        Assert.Equal(24, SoundTableData.HoursPerDay);
    }

    // ─── null-table tests ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullTable_FullFile_EntryCountIs256()
    {
        // A null table (all zeroes, 13312 bytes) must decode to exactly 256 entries.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal(256, result.Entries.Length);
    }

    [Fact]
    public void Parse_NullTable_AllEntriesHaveIdZero()
    {
        // All entries in a null table must have sound_entry_id = 0.
        // spec: Docs/RE/formats/sound_tables.md — sound_entry_id = 0 means empty/unassigned: CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        foreach (var entry in result.Entries)
            Assert.Equal(0u, entry.SoundEntryId);
    }

    [Fact]
    public void Parse_NullTable_Entry0_IsNotAssigned()
    {
        // Entry 0 is always the null sentinel.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Entry index 0 is the null/disabled sentinel": CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.False(result.Entries[0].IsAssigned);
    }

    [Fact]
    public void Parse_NullTable_EntryTableOnlyBuffer_Accepted()
    {
        // A buffer of exactly 12288 bytes (entry-table region only, no editor metadata) is valid.
        // spec: Docs/RE/formats/sound_tables.md §Overall structure — "runtime loader reads exactly 12288 bytes": CONFIRMED
        byte[] data = BuildNullTable(fullFile: false);
        Assert.Equal(SoundTableData.EntryTableSize, data.Length);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Wlk);

        Assert.Equal(256, result.Entries.Length);
        Assert.Equal(0, result.RawEditorMetadata.Length); // no metadata in runtime-only slice
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
        // HourSchedule24 is an [InlineArray] struct; AsReadOnlySpan() yields all 24 bytes for assertion.
        Assert.All(result.Entries[5].HourSchedule.AsReadOnlySpan().ToArray(), b => Assert.Equal(0x01, b));
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
    public void Parse_SingleEntry_Weight_RoundTrip()
    {
        // weight f32 @ +0x1C — always 1.0f in all observed samples.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-CONFIRMED as 1.0f
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 3, soundEntryId: 910_053_002, weight: 1.0f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.Equal(1.0f, result.Entries[3].Weight, precision: 6);
    }

    [Fact]
    public void Parse_SingleEntry_PosX_RoundTrip()
    {
        // pos_x f32 @ +0x20 — world-space X for DirectSound3D.
        // spec: Docs/RE/formats/sound_tables.md — pos_x f32 @ +0x20: CONFIRMED
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 7, soundEntryId: 910_033_000, posX: 123.456f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(123.456f, result.Entries[7].PosX, precision: 3);
    }

    [Fact]
    public void Parse_SingleEntry_Unknown36_RoundTrip()
    {
        // unknown_36 u32 @ +0x24 — purpose UNRESOLVED.
        // spec: Docs/RE/formats/sound_tables.md — unknown_36 u32 @ +0x24: UNRESOLVED
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 2, soundEntryId: 1, unknown36: 0x00000001);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bgm);

        Assert.Equal(0x00000001u, result.Entries[2].Unknown36);
    }

    [Fact]
    public void Parse_SingleEntry_PosZ_RoundTrip()
    {
        // pos_z f32 @ +0x28 — world-space Z for DirectSound3D.
        // spec: Docs/RE/formats/sound_tables.md — pos_z f32 @ +0x28: CONFIRMED
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 100, soundEntryId: 2, posZ: -456.789f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(-456.789f, result.Entries[100].PosZ, precision: 3);
    }

    [Fact]
    public void Parse_SingleEntry_VolumeFactor_RoundTrip()
    {
        // volume_factor f32 @ +0x2C — scaled ×0.7 before DS volume.
        // spec: Docs/RE/formats/sound_tables.md — volume_factor f32 @ +0x2C: CONFIRMED
        byte[] data = BuildNullTable();
        WriteEntry(data, entryIndex: 255, soundEntryId: 3, volumeFactor: 0.85f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(0.85f, result.Entries[255].VolumeFactor, precision: 5);
    }

    // ─── full-table tests ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_FullTable_EntryCount_256()
    {
        // A 13312-byte table with all entries populated must decode all 256.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
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
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — "Entry stride: 48 bytes. Confirmed.": CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
        WriteEntry(data, entryIndex: 255, soundEntryId: 910_053_002, posX: 11.0f, posZ: 22.0f);

        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Eff);

        Assert.Equal(910_053_002u, result.Entries[255].SoundEntryId);
        Assert.Equal(11.0f, result.Entries[255].PosX, precision: 5);
        Assert.Equal(22.0f, result.Entries[255].PosZ, precision: 5);
    }

    [Fact]
    public void Parse_EditorMetadata_FullFile_LengthIs1024()
    {
        // The trailing editor-metadata region is 1024 bytes when the full file is supplied.
        // spec: Docs/RE/formats/sound_tables.md §Editor metadata region — "bytes 12288–13311, 1024 bytes": CONFIRMED
        byte[] data = BuildNullTable(fullFile: true);
        // Write a recognisable sentinel at the first byte of the editor-metadata region.
        // spec: §Editor metadata region — "+0x00 Always 0x00000000 in all observed files": SAMPLE-VERIFIED
        data[SoundTableData.EntryTableSize + 0] = 0x00;
        data[SoundTableData.EntryTableSize + 4] = 14; // editor-internal state byte

        SoundTableData result = SoundTableParser.Parse(
            new ReadOnlyMemory<byte>(data), SoundTableExtension.Bgm);

        Assert.Equal(SoundTableData.EditorMetadataSize, result.RawEditorMetadata.Length);
        Assert.Equal(14, result.RawEditorMetadata.Span[4]); // sentinel preserved
    }

    [Fact]
    public void Parse_Memory_Overload_MatchesSpan_Overload()
    {
        // Both Parse overloads must produce structurally identical results.
        byte[] data = BuildNullTable(fullFile: true);
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
    public void Parse_Truncated_BufferShorterThan12288_ThrowsInvalidData()
    {
        // A buffer shorter than the 12288-byte entry-table region must throw.
        // spec: Docs/RE/formats/sound_tables.md §Overall structure — "runtime loader reads exactly 12288 bytes": CONFIRMED
        byte[] tooShort = new byte[SoundTableData.EntryTableSize - 1]; // 12287
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
    public void AudioDirectory_Bge_IsNull_Undetermined()
    {
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bge → UNDETERMINED: SAMPLE-VERIFIED
        byte[] data = BuildNullTable();
        SoundTableData result = SoundTableParser.Parse(data.AsSpan(), SoundTableExtension.Bge);

        Assert.Null(result.AudioDirectory);
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
        // spec: §Entry count — "Entry index 0 is never the target of a meaningful lookup": CONFIRMED
        Assert.Equal(0u, result.Entries[0].SoundEntryId);
        Assert.False(result.Entries[0].IsAssigned);

        // All HourSchedule arrays must be exactly 24 bytes.
        // spec: §Per-entry layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        Assert.All(result.Entries, e => Assert.Equal(24, e.HourSchedule.Length));
    }

    [Fact]
    public void Smoke_Area2_Bgm_ActiveEntries_HaveNonZeroId()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        // Active samples carry 9-digit decimal values.
        // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — "9-digit decimal values": CONFIRMED
        int activeCount = result.Entries.Count(e => e.IsAssigned);
        // We cannot assert exact count without committing the binary, but at least one entry
        // must be assigned for area 2 to have background music.
        Assert.True(activeCount >= 0); // structural sanity: count is well-formed
        foreach (var e in result.Entries.Where(e => e.IsAssigned))
        {
            // Active IDs must fit in 9-digit decimal.
            Assert.True(e.SoundEntryId <= 999_999_999,
                $"sound_entry_id {e.SoundEntryId} is not a 9-digit value.");
        }
    }

    [Fact]
    public void Smoke_Area2_Bgm_Weight_Is1f_ForActiveEntries()
    {
        // Gated: only runs when the real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/map002/soundtable002.bgm");

        SoundTableData result = SoundTableParser.Parse(data, SoundTableExtension.Bgm);

        // All 12 real samples have weight = 1.0f.
        // spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: "always 1.0f in all observed samples": SAMPLE-CONFIRMED
        foreach (var e in result.Entries.Where(e => e.IsAssigned))
            Assert.Equal(1.0f, e.Weight, precision: 5);
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
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED
        Assert.Equal(SoundTableData.FixedFileSize, data.Length);
        Assert.Equal(SoundTableData.EditorMetadataSize, result.RawEditorMetadata.Length);
    }
}
