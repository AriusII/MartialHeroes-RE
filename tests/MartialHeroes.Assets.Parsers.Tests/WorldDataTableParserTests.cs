using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based and optional smoke tests for the Wave-9 World data-table parsers:
/// <see cref="BuffIconPositionParser"/>, <see cref="MapSettingScrParser"/>,
/// <see cref="RegionTableParser"/>, <see cref="NpcScrParser"/>, <see cref="QuestsScrParser"/>.
/// <para>
/// All in-memory fixture tests run offline. Smoke tests are gated and silently skip when the
/// real clientdata VFS is absent.
/// </para>
/// </summary>
public sealed class WorldDataTableParserTests
{
    // ─── binary helpers ────────────────────────────────────────────────────────

    private static void WriteU16LE(byte[] buf, int off, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(off, 2), v);

    private static void WriteU32LE(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteI32LE(byte[] buf, int off, int v) =>
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteF32LE(byte[] buf, int off, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteCp949(byte[] buf, int off, int fieldLen, string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        byte[] encoded = cp949.GetBytes(text);
        int copyLen = Math.Min(encoded.Length, fieldLen - 1); // leave room for NUL
        Array.Copy(encoded, 0, buf, off, copyLen);
        buf[off + copyLen] = 0; // NUL terminator
    }

    // ─── VFS smoke-test gate ───────────────────────────────────────────────────

    private static readonly string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsPath);

    // =========================================================================
    // 1. BuffIconPositionParser
    // =========================================================================
    // spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: CODE-CONFIRMED + SAMPLE-VERIFIED.
    // BUFF_ICON_POS_RECORD_BYTES = 12.
    // SPEC CORRECTION 2026-06-13: atlas_x / atlas_y are i32LE (signed), not u32.

    /// <summary>
    /// Builds a synthetic buff_icon_position.xdb fixture with 2 records.
    /// spec: Docs/RE/formats/misc_data.md §1.3 — "stride 12 bytes": CONFIRMED.
    /// Layout: buff_id u32 @ +0, atlas_x i32 @ +4, atlas_y i32 @ +8.
    /// </summary>
    private static byte[] BuildBuffIconPositionXdb(
        (uint buffId, int atlasX, int atlasY)[] records)
    {
        // stride = 12 bytes per record; no header.
        // spec: Docs/RE/formats/misc_data.md §1.3 — "no header; record count = file_size / 12": CONFIRMED.
        byte[] buf = new byte[records.Length * 12];
        for (int i = 0; i < records.Length; i++)
        {
            int off = i * 12;
            // buff_id u32LE @ +0. CODE-CONFIRMED.
            WriteU32LE(buf, off + 0, records[i].buffId);
            // atlas_x i32LE @ +4. CODE-CONFIRMED (corrected 2026-06-13: signed i32).
            WriteI32LE(buf, off + 4, records[i].atlasX);
            // atlas_y i32LE @ +8. CODE-CONFIRMED (corrected 2026-06-13: signed i32).
            WriteI32LE(buf, off + 8, records[i].atlasY);
        }

        return buf;
    }

    [Fact]
    public void BuffIconPosition_Parse_ReturnsCorrectRecordCount()
    {
        // spec: Docs/RE/formats/misc_data.md §1.3 — "record count = file_size / 12": CONFIRMED.
        byte[] data = BuildBuffIconPositionXdb([
            (1u, 25, 50),
            (81u, 100, 200),
            (1103u, 250, 251),
        ]);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3, table.Records.Count);
    }

    [Fact]
    public void BuffIconPosition_Parse_BuffIdAndSignedCoordinates()
    {
        // spec: Docs/RE/formats/misc_data.md §1.3 — buff_id u32 @ +0: CODE-CONFIRMED.
        // spec: §1.3 — atlas_x i32 @ +4, atlas_y i32 @ +8: CODE-CONFIRMED (signed).
        byte[] data = BuildBuffIconPositionXdb([(42u, 276, -5)]);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(42u, table.Records[0].BuffId);
        Assert.Equal(276, table.Records[0].AtlasX);
        Assert.Equal(-5, table.Records[0].AtlasY);
    }

    [Fact]
    public void BuffIconPosition_Lookup_TryGetById_ReturnsRecord()
    {
        // spec: Docs/RE/formats/misc_data.md §1.3 — "lookup returns (atlas_x, atlas_y) or (0,0) when absent".
        byte[] data = BuildBuffIconPositionXdb([
            (10u, 50, 75),
            (200u, 300, 325),
        ]);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(data));

        BuffIconPositionRecord? rec = table.TryGetById(200u);
        Assert.NotNull(rec);
        Assert.Equal(300, rec!.AtlasX);
        Assert.Equal(325, rec.AtlasY);
    }

    [Fact]
    public void BuffIconPosition_Lookup_MissingId_ReturnsNull()
    {
        byte[] data = BuildBuffIconPositionXdb([(1u, 0, 0)]);
        BuffIconPositionTable table = BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Null(table.TryGetById(999u));
    }

    [Fact]
    public void BuffIconPosition_Parse_InvalidStride_Throws()
    {
        // spec: Docs/RE/formats/misc_data.md §1.3 — "file size must be exact multiple of 12": CONFIRMED.
        byte[] bad = new byte[13]; // not a multiple of 12
        Assert.Throws<InvalidDataException>(() =>
            BuffIconPositionParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Smoke_BuffIconPosition_ParsesFromVfs()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/script/buff_icon_position.xdb")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/buff_icon_position.xdb");

        // spec: Docs/RE/formats/misc_data.md §1.3 — "1608 bytes = 134 records in known sample": CONFIRMED.
        Assert.True(raw.Length % 12 == 0,
            $"buff_icon_position.xdb size {raw.Length} is not a multiple of 12.");

        BuffIconPositionTable table = BuffIconPositionParser.Parse(raw);
        Assert.True(table.Records.Count > 0, "buff_icon_position.xdb should have at least 1 record.");

        // buff_id 1 should be present in the real file (range 1–1103 observed).
        // spec: Docs/RE/formats/misc_data.md §1.3 — "range 1–1103 observed": CODE-CONFIRMED.
        // Accept either present or absent (VFS version may differ); but table must parse cleanly.
    }

    // =========================================================================
    // 2. MapSettingScrParser
    // =========================================================================
    // spec: Docs/RE/formats/misc_data.md §7.1 mapsetting.scr: SAMPLE-VERIFIED.
    // MAPSETTING_RECORD_BYTES = 84 (0x54). 52 zones expected.

    /// <summary>
    /// Builds a synthetic mapsetting.scr fixture.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — "stride 84 bytes (0x54)": SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildMapSettingScr(
        (int zoneId, string zoneName, int minX, int minZ, int maxX, int maxZ,
            int flagsA, int flagsB, float fogDensity,
            int unk44, int unk48, int unk4C, int unk50)[] records)
    {
        // stride = 84 bytes per record; no header.
        // spec: Docs/RE/formats/misc_data.md §7.1 — "flat array of fixed 84-byte records; no header": SAMPLE-VERIFIED.
        byte[] buf = new byte[records.Length * 84];
        foreach (var (rec, i) in records.Select((r, idx) => (r, idx)))
        {
            int off = i * 84;
            // zone_id i32LE @ 0x00. SAMPLE-VERIFIED.
            WriteI32LE(buf, off + 0x00, rec.zoneId);
            // zone_name char[36] CP949 @ 0x04. SAMPLE-VERIFIED.
            WriteCp949(buf, off + 0x04, 36, rec.zoneName);
            // world_min_x i32LE @ 0x28. PLAUSIBLE.
            WriteI32LE(buf, off + 0x28, rec.minX);
            // world_min_z i32LE @ 0x2C. PLAUSIBLE.
            WriteI32LE(buf, off + 0x2C, rec.minZ);
            // world_max_x i32LE @ 0x30. PLAUSIBLE.
            WriteI32LE(buf, off + 0x30, rec.maxX);
            // world_max_z i32LE @ 0x34. PLAUSIBLE.
            WriteI32LE(buf, off + 0x34, rec.maxZ);
            // flags_a i32LE @ 0x38. UNKNOWN.
            WriteI32LE(buf, off + 0x38, rec.flagsA);
            // flags_b i32LE @ 0x3C. UNKNOWN.
            WriteI32LE(buf, off + 0x3C, rec.flagsB);
            // fog_density f32LE @ 0x40. PLAUSIBLE.
            WriteF32LE(buf, off + 0x40, rec.fogDensity);
            // unknown_0x44 i32LE @ 0x44. UNKNOWN.
            WriteI32LE(buf, off + 0x44, rec.unk44);
            // unknown_0x48 i32LE @ 0x48. UNKNOWN.
            WriteI32LE(buf, off + 0x48, rec.unk48);
            // unknown_0x4C i32LE @ 0x4C. UNKNOWN.
            WriteI32LE(buf, off + 0x4C, rec.unk4C);
            // unknown_0x50 i32LE @ 0x50. UNKNOWN.
            WriteI32LE(buf, off + 0x50, rec.unk50);
        }

        return buf;
    }

    [Fact]
    public void MapSettingScr_Parse_ReturnsCorrectRecordCount()
    {
        // spec: Docs/RE/formats/misc_data.md §7.1 — "file_size / 84 = record count": SAMPLE-VERIFIED.
        byte[] data = BuildMapSettingScr([
            (1, "하왕관", -10240, -7168, 5120, 10240, 0x012C0001, 1, 1.70f, 1, 0, 0x64000007, 0),
            (2, "염무진", -5120, -5120, 5120, 5120, 0x012C0001, 1, 1.70f, 0, 0, 0x64000002, 0),
        ]);
        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(2, zones.Length);
    }

    [Fact]
    public void MapSettingScr_Parse_ZoneIdAndName()
    {
        // spec: Docs/RE/formats/misc_data.md §7.1 — "zone_id i32 @ 0x00, zone_name char[36] CP949 @ 0x04": SAMPLE-VERIFIED.
        byte[] data = BuildMapSettingScr([
            (42, "사해주", -1000, -1000, 1000, 1000, 0x012C0001, 1, 1.30f, 0, 0, 0x64000001, 0),
        ]);
        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(42, zones[0].ZoneId);
        Assert.Equal("사해주", zones[0].ZoneName);
    }

    [Fact]
    public void MapSettingScr_Parse_BoundingBoxAndFog()
    {
        // spec: Docs/RE/formats/misc_data.md §7.1 — world_min_x/z @ 0x28/0x2C, world_max_x/z @ 0x30/0x34: PLAUSIBLE.
        // spec: §7.1 — fog_density f32 @ 0x40: PLAUSIBLE.
        byte[] data = BuildMapSettingScr([
            (1, "TestZone", -10240, -7168, 5120, 10240, 0x012C0001, 1, 1.50f, 0, 0, 0x64000001, 0),
        ]);
        MapZoneRecord[] zones = MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(-10240, zones[0].WorldMinX);
        Assert.Equal(-7168, zones[0].WorldMinZ);
        Assert.Equal(5120, zones[0].WorldMaxX);
        Assert.Equal(10240, zones[0].WorldMaxZ);
        Assert.Equal(1.50f, zones[0].FogDensity, precision: 3);
    }

    [Fact]
    public void MapSettingScr_Parse_InvalidStride_Throws()
    {
        byte[] bad = new byte[85]; // not a multiple of 84
        Assert.Throws<InvalidDataException>(() =>
            MapSettingScrParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Smoke_MapSettingScr_ParsesFromVfs()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/script/mapsetting.scr")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/mapsetting.scr");

        // spec: Docs/RE/formats/misc_data.md §7.1 — "4 368 bytes = 52 records": SAMPLE-VERIFIED.
        Assert.True(raw.Length % 84 == 0,
            $"mapsetting.scr size {raw.Length} is not a multiple of 84.");

        MapZoneRecord[] zones = MapSettingScrParser.Parse(raw);

        // spec: Docs/RE/formats/misc_data.md §7.1 — "52 zones expected": SAMPLE-VERIFIED.
        Assert.Equal(52, zones.Length);

        // All zone names should decode without throwing.
        Assert.All(zones, z => Assert.NotNull(z.ZoneName));

        // First zone should have a non-zero id.
        Assert.NotEqual(0, zones[0].ZoneId);

        // Fog density should be in a physically meaningful range (spec: 1.30–1.70 observed).
        // spec: §7.1 — "fog_density: 1.30 (interior), 1.50 (rare), 1.70 (outdoor)": PLAUSIBLE.
        Assert.All(zones, z =>
            Assert.True(z.FogDensity >= 0.5f && z.FogDensity <= 5.0f,
                $"Zone {z.ZoneId} fog_density={z.FogDensity} out of expected range."));
    }

    // =========================================================================
    // 3. RegionTableParser
    // =========================================================================
    // spec: Docs/RE/formats/misc_data.md §7.2 regiontableNNN.bin: SAMPLE-VERIFIED.
    // REGIONTABLE_RECORD_BYTES = 32 (0x20). 52 records per area expected.

    /// <summary>
    /// Builds a synthetic regiontableNNN.bin fixture.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — "stride 32 bytes (0x20)": SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildRegionTableBin(
        (float cx, float cz, string name)[] records)
    {
        // stride = 32 bytes per record; no header.
        // spec: Docs/RE/formats/misc_data.md §7.2 — "flat array of fixed 32-byte records; no header": SAMPLE-VERIFIED.
        byte[] buf = new byte[records.Length * 32];
        foreach (var (rec, i) in records.Select((r, idx) => (r, idx)))
        {
            int off = i * 32;
            // center_x f32LE @ 0x00. PLAUSIBLE.
            WriteF32LE(buf, off + 0x00, rec.cx);
            // center_z f32LE @ 0x04. PLAUSIBLE.
            WriteF32LE(buf, off + 0x04, rec.cz);
            // unknown_0x08 u8[8] — zero (as observed). UNKNOWN.
            // (no write needed; buf is pre-zeroed)
            // sub_zone_name char[16] CP949 @ 0x10. PLAUSIBLE.
            WriteCp949(buf, off + 0x10, 16, rec.name);
        }

        return buf;
    }

    [Fact]
    public void RegionTable_Parse_ReturnsCorrectRecordCount()
    {
        // spec: Docs/RE/formats/misc_data.md §7.2 — "record count = file_size / 32": SAMPLE-VERIFIED.
        byte[] data = BuildRegionTableBin([
            (100f, -200f, "폐어촌"),
            (300f, 400f, "구룡부"),
        ]);
        RegionTableRecord[] recs = RegionTableParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(2, recs.Length);
    }

    [Fact]
    public void RegionTable_Parse_CoordinatesAndName()
    {
        // spec: Docs/RE/formats/misc_data.md §7.2 — center_x f32 @ 0x00, center_z f32 @ 0x04: PLAUSIBLE.
        // spec: §7.2 — sub_zone_name char[16] CP949 @ 0x10: PLAUSIBLE.
        byte[] data = BuildRegionTableBin([(12345.5f, -9876.25f, "무암촌")]);
        RegionTableRecord[] recs = RegionTableParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(12345.5f, recs[0].CenterX, precision: 3);
        Assert.Equal(-9876.25f, recs[0].CenterZ, precision: 3);
        Assert.Equal("무암촌", recs[0].SubZoneName);
    }

    [Fact]
    public void RegionTable_Parse_Unknown08_IsEightBytes()
    {
        // spec: Docs/RE/formats/misc_data.md §7.2 — "unknown_0x08 u8[8] @ 0x08: UNKNOWN".
        byte[] data = BuildRegionTableBin([(0f, 0f, "Test")]);
        RegionTableRecord[] recs = RegionTableParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(8, recs[0].Unknown0x08.Length);
    }

    [Fact]
    public void RegionTable_Parse_InvalidStride_Throws()
    {
        byte[] bad = new byte[33]; // not a multiple of 32
        Assert.Throws<InvalidDataException>(() =>
            RegionTableParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Smoke_RegionTable_Area2_ParsesFromVfs()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        const string vfsPath = "data/map002/regiontable002.bin";
        if (!archive.Contains(vfsPath)) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent(vfsPath);

        // spec: Docs/RE/formats/misc_data.md §7.2 — "1 664 bytes = 52 records per area": SAMPLE-VERIFIED.
        Assert.True(raw.Length % 32 == 0,
            $"regiontable002.bin size {raw.Length} is not a multiple of 32.");

        RegionTableRecord[] recs = RegionTableParser.Parse(raw);

        Assert.Equal(52, recs.Length);

        // All names should decode without throwing.
        Assert.All(recs, r => Assert.NotNull(r.SubZoneName));

        // Unknown08 should always be 8 bytes.
        Assert.All(recs, r => Assert.Equal(8, r.Unknown0x08.Length));
    }

    // =========================================================================
    // 4. NpcScrParser
    // =========================================================================
    // spec: Docs/RE/formats/config_tables.md §2.17.3 npc.scr: SAMPLE-VERIFIED.
    // NPC_SCR_RECORD_BYTES = 404 (0x194). 2510 records expected.

    /// <summary>
    /// Builds a synthetic npc.scr fixture (one record = 404 bytes).
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes (0x194)": SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildNpcScrOneRecord(
        uint id, string para0, string para1 = "", string para2 = "")
    {
        // stride = 404 bytes per record; no header.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "no header, no record-count prefix": CONFIRMED.
        byte[] buf = new byte[404];
        // id u32LE @ 0x000. SAMPLE-VERIFIED.
        WriteU32LE(buf, 0x000, id);
        // id_mirror u32LE @ 0x004. SAMPLE-VERIFIED.
        WriteU32LE(buf, 0x004, id);
        // reserved 8 bytes @ 0x008 — zero (pre-zeroed). SAMPLE-VERIFIED.
        // paragraph_0 CP949 @ 0x014 (≤60 bytes, ends before 0x050). SAMPLE-VERIFIED.
        WriteCp949(buf, 0x014, 60, para0);
        // paragraph_1 CP949 @ 0x050 (≤64 bytes, ends before 0x090). SAMPLE-VERIFIED.
        WriteCp949(buf, 0x050, 64, para1);
        // paragraph_2 CP949 @ 0x090 (≤64 bytes, ends before 0x0D0). SAMPLE-VERIFIED.
        WriteCp949(buf, 0x090, 64, para2);
        // sub-section markers at 0x0D0/0x110/0x150 (value 48 = 0x30). SAMPLE-VERIFIED.
        WriteU32LE(buf, 0x0D0, 48u); // spec: §2.17.3 — "sub-section marker (48) @ 0x0D0: SAMPLE-VERIFIED".
        WriteU32LE(buf, 0x110, 48u); // spec: §2.17.3 — "sub-section marker (48) @ 0x110: SAMPLE-VERIFIED".
        WriteU32LE(buf, 0x150, 48u); // spec: §2.17.3 — "sub-section marker (48) @ 0x150: SAMPLE-VERIFIED".
        return buf;
    }

    [Fact]
    public void NpcScr_Parse_RecordCountFromStride()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "record count = file_size / 404": SAMPLE-VERIFIED.
        byte[] twoRecs = new byte[404 * 2];
        // Write two sequential ids.
        WriteU32LE(twoRecs, 0x000, 1u);
        WriteU32LE(twoRecs, 0x004, 1u);
        WriteU32LE(twoRecs, 404 + 0x000, 2u);
        WriteU32LE(twoRecs, 404 + 0x004, 2u);

        NpcScrRecord[] recs = NpcScrParser.Parse(new ReadOnlyMemory<byte>(twoRecs));
        Assert.Equal(2, recs.Length);
    }

    [Fact]
    public void NpcScr_Parse_IdAndIdMirror()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id u32 @ 0x000, id_mirror u32 @ 0x004": SAMPLE-VERIFIED.
        byte[] data = BuildNpcScrOneRecord(42u, "무사");
        NpcScrRecord[] recs = NpcScrParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(42u, recs[0].Id);
        Assert.Equal(42u, recs[0].IdMirror);
    }

    [Fact]
    public void NpcScr_Parse_Paragraphs()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — paragraph_0 @ 0x014, paragraph_1 @ 0x050, paragraph_2 @ 0x090.
        byte[] data = BuildNpcScrOneRecord(1u, "단락 0", "단락 1", "단락 2");
        NpcScrRecord[] recs = NpcScrParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal("단락 0", recs[0].Paragraph0);
        Assert.Equal("단락 1", recs[0].Paragraph1);
        Assert.Equal("단락 2", recs[0].Paragraph2);
    }

    [Fact]
    public void NpcScr_Parse_RawBodyIsCorrectLength()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes": SAMPLE-VERIFIED.
        byte[] data = BuildNpcScrOneRecord(1u, "test");
        NpcScrRecord[] recs = NpcScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(404, recs[0].Raw.Length);
    }

    [Fact]
    public void NpcScr_Parse_InvalidStride_Throws()
    {
        byte[] bad = new byte[405]; // not a multiple of 404
        Assert.Throws<InvalidDataException>(() =>
            NpcScrParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Smoke_NpcScr_ParsesFromVfs()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/script/npc.scr")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/npc.scr");

        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "2510 records": SAMPLE-VERIFIED.
        Assert.True(raw.Length % 404 == 0,
            $"npc.scr size {raw.Length} is not a multiple of 404 (NPC_SCR_RECORD_BYTES).");

        NpcScrRecord[] recs = NpcScrParser.Parse(raw);

        Assert.Equal(2510, recs.Length);

        // First record id should be 1 (sequential 1..2510).
        // spec: §2.17.3 — "Sequential 1..2510": SAMPLE-VERIFIED.
        Assert.Equal(1u, recs[0].Id);
        Assert.Equal(recs[0].Id, recs[0].IdMirror);

        // All records should decode without throwing.
        Assert.All(recs, r =>
        {
            Assert.NotNull(r.Paragraph0);
            Assert.NotNull(r.Paragraph1);
            Assert.NotNull(r.Paragraph2);
        });
    }

    // =========================================================================
    // 5. QuestsScrParser
    // =========================================================================
    // spec: Docs/RE/formats/config_tables.md §2.17.1 quests.scr: SAMPLE-VERIFIED.
    // QUESTS_SCR_RECORD_BYTES = 3720 (0xE88). 488 slots; 122 occupied.

    /// <summary>
    /// Builds a synthetic quests.scr fixture.
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes (0xE88), sparse": SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildQuestsScrTwoSlots(
        ushort slot0Id, string slot0Name,
        ushort slot1Id, string slot1Name)
    {
        // Two 3720-byte slots; no header.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "no header, no record-count prefix": CONFIRMED.
        byte[] buf = new byte[2 * 3720];

        // Slot 0 — quest_id u16LE @ 0x000. SAMPLE-VERIFIED.
        WriteU16LE(buf, 0x000, slot0Id);
        // quest_name CP949 @ 0x002. SAMPLE-VERIFIED.
        if (slot0Id != 0)
            WriteCp949(buf, 0x002, 62, slot0Name);

        // Slot 1.
        int off = 3720;
        WriteU16LE(buf, off + 0x000, slot1Id);
        if (slot1Id != 0)
            WriteCp949(buf, off + 0x002, 62, slot1Name);

        return buf;
    }

    [Fact]
    public void QuestsScr_Parse_OccupiedSlotOnlyReturned()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "slot is empty when quest_id == 0": SAMPLE-VERIFIED.
        byte[] data = BuildQuestsScrTwoSlots(
            7, "퀘스트 A",
            0, ""); // slot 1 is empty

        QuestScrRecord[] recs = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Single(recs); // only 1 occupied slot
        Assert.Equal((ushort)7, recs[0].QuestId);
    }

    [Fact]
    public void QuestsScr_Parse_QuestName()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_name char[] @ 0x002: SAMPLE-VERIFIED".
        byte[] data = BuildQuestsScrTwoSlots(
            42, "강의 수호자",
            0, "");

        QuestScrRecord[] recs = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal("강의 수호자", recs[0].QuestName);
    }

    [Fact]
    public void QuestsScr_Parse_BothSlotsOccupied()
    {
        byte[] data = BuildQuestsScrTwoSlots(1, "퀘A", 2, "퀘B");
        QuestScrRecord[] recs = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(2, recs.Length);
        Assert.Equal((ushort)1, recs[0].QuestId);
        Assert.Equal((ushort)2, recs[1].QuestId);
    }

    [Fact]
    public void QuestsScr_Parse_RawBodyIsCorrectLength()
    {
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes": SAMPLE-VERIFIED.
        byte[] data = BuildQuestsScrTwoSlots(1, "Quest", 0, "");
        QuestScrRecord[] recs = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3720, recs[0].Raw.Length);
    }

    [Fact]
    public void QuestsScr_Parse_InvalidStride_Throws()
    {
        byte[] bad = new byte[3721]; // not a multiple of 3720
        Assert.Throws<InvalidDataException>(() =>
            QuestsScrParser.Parse(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Smoke_QuestsScr_ParsesFromVfs()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/script/quests.scr")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/quests.scr");

        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "488 slots × 3720 bytes = 1,815,360 bytes": SAMPLE-VERIFIED.
        Assert.True(raw.Length % 3720 == 0,
            $"quests.scr size {raw.Length} is not a multiple of 3720 (QUESTS_SCR_RECORD_BYTES).");

        int totalSlots = raw.Length / 3720;
        Assert.Equal(488, totalSlots);

        QuestScrRecord[] recs = QuestsScrParser.Parse(raw);

        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "122 occupied of 488": SAMPLE-VERIFIED.
        Assert.Equal(122, recs.Length);

        // All occupied slots should have quest_id in range 1..617.
        // spec: §2.17.1 — "quest_id range 1..617": SAMPLE-VERIFIED.
        Assert.All(recs, r =>
        {
            Assert.True(r.QuestId >= 1 && r.QuestId <= 617,
                $"quest_id {r.QuestId} out of expected range 1..617.");
            Assert.NotNull(r.QuestName);
        });
    }
}