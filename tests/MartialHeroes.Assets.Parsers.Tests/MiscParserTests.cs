using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="MiscParser"/>: ParseMobInfoMi, ParseTol, ParseDescriptSc.
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/misc_data.md §2–§5.
/// </summary>
public sealed class MiscParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    private static void WriteCp949(byte[] buf, int off, int fieldLen, string text)
    {
        byte[] enc = Cp949().GetBytes(text);
        int copyLen = Math.Min(enc.Length, fieldLen - 1);
        enc.AsSpan(0, copyLen).CopyTo(buf.AsSpan(off));
        buf[off + copyLen] = 0x00;
    }

    // =========================================================================
    // ParseMobInfoMi — 4-byte header + 28-byte records
    // spec: Docs/RE/formats/misc_data.md §2 mobinfo.mi: sample_verified.
    // =========================================================================

    private const int MobInfoHeaderSize = 4;       // spec: §2 — count u32 @ 0
    private const int MobInfoRecordStride = 28;    // spec: §2 — stride 28 bytes: HIGH.

    /// <summary>
    /// Builds a mobinfo.mi buffer with the given records (each: 7 × u32le).
    /// spec: Docs/RE/formats/misc_data.md §2 — "4 + count × 28 bytes": HIGH.
    /// </summary>
    private static byte[] BuildMobInfoBuffer(params (uint MobClassId, uint NameStrId, uint AltNameStrId,
        uint IconIndex, uint Portrait1, uint Portrait2, uint Portrait3)[] records)
    {
        byte[] buf = new byte[MobInfoHeaderSize + records.Length * MobInfoRecordStride];

        // count u32le @ 0. HIGH.
        // spec: Docs/RE/formats/misc_data.md §2 — "count u32 @ 0: HIGH".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)records.Length);

        for (int i = 0; i < records.Length; i++)
        {
            int off = MobInfoHeaderSize + i * MobInfoRecordStride;
            var r = records[i];
            // 7 × u32le: all fields. spec: §2 — "7 × u32le: HIGH (layout)".
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 0, 4), r.MobClassId);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 4, 4), r.NameStrId);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 8, 4), r.AltNameStrId);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 12, 4), r.IconIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 16, 4), r.Portrait1);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 20, 4), r.Portrait2);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off + 24, 4), r.Portrait3);
        }

        return buf;
    }

    [Fact]
    public void ParseMobInfoMi_TooShortForHeader_Throws()
    {
        // Buffer shorter than 4 bytes cannot have the header.
        // spec: Docs/RE/formats/misc_data.md §2 — "4-byte header": HIGH.
        byte[] buf = new byte[3];
        Assert.Throws<InvalidDataException>(() => MiscParser.ParseMobInfoMi(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseMobInfoMi_ZeroRecords_YieldsEmptyArray()
    {
        // count = 0 → no record body needed.
        // spec: Docs/RE/formats/misc_data.md §2 — count u32 @ 0: HIGH.
        byte[] buf = new byte[4]; // header only; count = 0
        MobInfoRecord[] result = MiscParser.ParseMobInfoMi(new ReadOnlyMemory<byte>(buf));
        Assert.Empty(result);
    }

    [Fact]
    public void ParseMobInfoMi_TruncatedRecords_Throws()
    {
        // Header says 1 record but buffer is only 8 bytes (< 4 + 28).
        // spec: Docs/RE/formats/misc_data.md §2 — "4 + count × 28 == file size": HIGH.
        byte[] buf = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), 1u); // count = 1
        Assert.Throws<InvalidDataException>(() => MiscParser.ParseMobInfoMi(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseMobInfoMi_OneRecord_AllFieldsDecoded()
    {
        // All 7 fields must be decoded correctly from a single record.
        // spec: Docs/RE/formats/misc_data.md §2 — "7 × u32le: HIGH (layout)".
        byte[] buf = BuildMobInfoBuffer((55u, 1001u, 1002u, 77u, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu));
        MobInfoRecord[] result = MiscParser.ParseMobInfoMi(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(55u, result[0].MobClassId);
        Assert.Equal(1001u, result[0].NameStrId);
        Assert.Equal(1002u, result[0].AltNameStrId);
        Assert.Equal(77u, result[0].IconIndex);
        Assert.Equal(0xFFFFFFFFu, result[0].PortraitRes1);
    }

    [Fact]
    public void ParseMobInfoMi_TwoRecords_IndependentDecoding()
    {
        // Two records decode independently without cross-record bleed.
        // spec: Docs/RE/formats/misc_data.md §2 — stride 28 bytes: HIGH.
        byte[] buf = BuildMobInfoBuffer(
            (10u, 100u, 101u, 55u, 0u, 0u, 0u),
            (20u, 200u, 201u, 88u, 0u, 0u, 0u));
        MobInfoRecord[] result = MiscParser.ParseMobInfoMi(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal(10u, result[0].MobClassId);
        Assert.Equal(55u, result[0].IconIndex);
        Assert.Equal(20u, result[1].MobClassId);
        Assert.Equal(88u, result[1].IconIndex);
    }

    // =========================================================================
    // ParseTol — 16-byte header + W×H tile bytes
    // spec: Docs/RE/formats/misc_data.md §3 .tol: sample_verified.
    // =========================================================================

    private const int TolHeaderSize = 16; // spec: §3 — "16-byte header (4 × u32le): HIGH".

    private static byte[] BuildTolBuffer(
        uint originX = 0u, uint originY = 0u,
        uint width = 4u, uint height = 4u,
        byte tileValue = 0)
    {
        // spec: Docs/RE/formats/misc_data.md §3 — "16 + width × height": HIGH.
        byte[] buf = new byte[TolHeaderSize + (int)(width * height)];

        // Header: 4 × u32le. spec: §3 — "world_origin_x @ 0: PARTIAL; world_origin_y @ 4: PARTIAL; width_tiles @ 8: HIGH; height_tiles @ 12: HIGH".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), originX);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), originY);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), width);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), height);

        // tile_grid u8[W×H] @ 16. spec: §3 — "tile_grid u8[W×H] @ 16, row-major: HIGH".
        for (int i = 0; i < (int)(width * height); i++)
            buf[TolHeaderSize + i] = tileValue;

        return buf;
    }

    [Fact]
    public void ParseTol_TooShortForHeader_Throws()
    {
        // Buffer shorter than 16 bytes cannot hold the header.
        // spec: Docs/RE/formats/misc_data.md §3 — "16-byte header": HIGH.
        byte[] buf = new byte[10];
        Assert.Throws<InvalidDataException>(() => MiscParser.ParseTol(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseTol_TruncatedTileGrid_Throws()
    {
        // Header says 4×4 = 16 tiles but buffer is only 20 bytes (< 16 + 16).
        // spec: Docs/RE/formats/misc_data.md §3 — "16 + W×H == file size": HIGH.
        byte[] buf = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), 4u);  // width
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), 4u); // height
        Assert.Throws<InvalidDataException>(() => MiscParser.ParseTol(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseTol_ValidBuffer_HeaderFieldsDecoded()
    {
        // All header fields must round-trip.
        // spec: Docs/RE/formats/misc_data.md §3 — "width_tiles @ 8: HIGH; height_tiles @ 12: HIGH".
        byte[] buf = BuildTolBuffer(originX: 256u, originY: 512u, width: 4u, height: 4u);
        TolMapData result = MiscParser.ParseTol(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(256u, result.WorldOriginX);
        Assert.Equal(512u, result.WorldOriginY);
        Assert.Equal(4u, result.WidthTiles);
        Assert.Equal(4u, result.HeightTiles);
    }

    [Fact]
    public void ParseTol_TileGrid_LengthIsWidthTimesHeight()
    {
        // TileGrid length must equal W × H.
        // spec: Docs/RE/formats/misc_data.md §3 — "tile_grid u8[W×H] @ 16": HIGH.
        byte[] buf = BuildTolBuffer(width: 8u, height: 8u);
        TolMapData result = MiscParser.ParseTol(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(64, result.TileGrid.Length); // 8 × 8 = 64
    }

    [Fact]
    public void ParseTol_AllBlockedTileGrid_AllOnes()
    {
        // Tile value 1 = blocked (as-written).
        // spec: Docs/RE/formats/misc_data.md §3 — "tile values: 0 = walkable, 1 = blocked": HIGH.
        byte[] buf = BuildTolBuffer(width: 2u, height: 2u, tileValue: 1);
        TolMapData result = MiscParser.ParseTol(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(4, result.TileGrid.Length);
        Assert.All(result.TileGrid.ToArray(), b => Assert.Equal(1, b));
    }

    // =========================================================================
    // ParseDescriptSc — stride 68 bytes
    // spec: Docs/RE/formats/misc_data.md §5 discript.sc: sample_verified true.
    // =========================================================================

    private const int DescriptorStride = 68; // spec: §5 — "stride 68 bytes (0x44): HIGH".

    private static byte[] BuildDescriptorRecord(
        uint descriptorId = 1u,
        uint category = 3u,
        string displayName = "Panel",
        string keyboardShortcut = "P")
    {
        byte[] buf = new byte[DescriptorStride];

        // descriptor_id u32le @ +0. HIGH.
        // spec: Docs/RE/formats/misc_data.md §5 — "descriptor_id u32 @ 0: HIGH".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), descriptorId);

        // category u32le @ +4. HIGH.
        // spec: Docs/RE/formats/misc_data.md §5 — "category u32 @ 4: HIGH".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), category);

        // display_name char[30] CP949 @ +8. HIGH.
        // spec: Docs/RE/formats/misc_data.md §5 — "display_name char[30] CP949 @ +8: HIGH".
        WriteCp949(buf, 8, 30, displayName);

        // keyboard_shortcut char[3] ASCII @ +38. HIGH.
        // spec: Docs/RE/formats/misc_data.md §5 — "keyboard_shortcut char[3] @ +38: HIGH".
        byte[] sc = System.Text.Encoding.ASCII.GetBytes(keyboardShortcut);
        int scLen = Math.Min(sc.Length, 2);
        sc.AsSpan(0, scLen).CopyTo(buf.AsSpan(38));
        buf[38 + scLen] = 0x00;

        return buf;
    }

    [Fact]
    public void ParseDescriptSc_NotMultipleOfStride_Throws()
    {
        // 50 is not divisible by 68.
        // spec: Docs/RE/formats/misc_data.md §5 — "stride 68 bytes (0x44)": HIGH.
        byte[] buf = new byte[50];
        Assert.Throws<InvalidDataException>(() => MiscParser.ParseDescriptSc(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseDescriptSc_Empty_YieldsNoRecords()
    {
        // 0 % 68 == 0 — valid empty file.
        // spec: Docs/RE/formats/misc_data.md §5 — record count = file_size / 68.
        DescriptorRecord[] result = MiscParser.ParseDescriptSc(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseDescriptSc_OneRecord_AllFieldsDecoded()
    {
        // All confirmed fields must round-trip.
        // spec: Docs/RE/formats/misc_data.md §5 — all fields HIGH.
        byte[] buf = BuildDescriptorRecord(
            descriptorId: 7u, category: 102u,
            displayName: "Skills", keyboardShortcut: "K");
        DescriptorRecord[] result = MiscParser.ParseDescriptSc(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(7u, result[0].DescriptorId);
        Assert.Equal(102u, result[0].Category);
        Assert.Equal("Skills", result[0].DisplayName);
        Assert.Equal("K", result[0].KeyboardShortcut);
    }

    [Fact]
    public void ParseDescriptSc_ReservedSlice_HasExpectedLength()
    {
        // Reserved 27 bytes at +41.
        // spec: Docs/RE/formats/misc_data.md §5 — "reserved u8[27] @ +41: LOW".
        byte[] buf = BuildDescriptorRecord();
        DescriptorRecord[] result = MiscParser.ParseDescriptSc(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(27, result[0].Reserved.Length);
    }

    [Fact]
    public void ParseDescriptSc_TwoRecords_IndependentDecoding()
    {
        // Two records must not bleed into each other.
        // spec: Docs/RE/formats/misc_data.md §5 — stride 68 bytes.
        byte[] r0 = BuildDescriptorRecord(descriptorId: 1u, category: 3u, displayName: "Alpha");
        byte[] r1 = BuildDescriptorRecord(descriptorId: 2u, category: 102u, displayName: "Beta");
        byte[] buf = new byte[r0.Length + r1.Length];
        r0.CopyTo(buf, 0);
        r1.CopyTo(buf, r0.Length);

        DescriptorRecord[] result = MiscParser.ParseDescriptSc(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal("Alpha", result[0].DisplayName);
        Assert.Equal("Beta", result[1].DisplayName);
    }
}
