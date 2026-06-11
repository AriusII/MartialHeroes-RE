using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for terrain parsers:
/// <see cref="LstManifestParser"/>, <see cref="TedTerrainParser"/>,
/// <see cref="MapDescriptorParser"/>, <see cref="MudBlobParser"/>, <see cref="SodBlobParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/terrain.md
/// </summary>
public sealed class TerrainParserTests
{
    // =========================================================================
    // LstManifestParser tests
    // =========================================================================

    private static byte[] Le4(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    /// <summary>
    /// Builds a synthetic .lst binary buffer.
    /// spec: Docs/RE/formats/terrain.md §1.2 — "count u32le | count × u32le keys": CONFIRMED.
    /// </summary>
    private static byte[] BuildLst(uint[] keys)
    {
        using var ms = new System.IO.MemoryStream();
        // count u32le
        // spec: Docs/RE/formats/terrain.md §1.2 — "count u32le @ offset 0: CONFIRMED".
        ms.Write(Le4((uint)keys.Length));
        // keys u32le[]
        // spec: Docs/RE/formats/terrain.md §1.2 — "keys u32le[] @ offset 4: CONFIRMED".
        foreach (uint k in keys)
            ms.Write(Le4(k));
        return ms.ToArray();
    }

    [Fact]
    public void Lst_Parse_Count_And_Keys()
    {
        // spec: Docs/RE/formats/terrain.md §1.2 — "count u32le, keys u32le[]": CONFIRMED.
        uint key1 = 10000u + 100000u * 10000u; // mapX=10000, mapZ=10000 → key=1000010000
        uint key2 = 10001u + 100000u * 10000u; // mapX=10000, mapZ=10001
        byte[] data = BuildLst([key1, key2]);

        LstManifest manifest = LstManifestParser.Parse(data.AsSpan());

        Assert.Equal(2, manifest.Entries.Length);
        Assert.Equal(key1, manifest.Entries[0].Key);
        Assert.Equal(key2, manifest.Entries[1].Key);
    }

    [Fact]
    public void Lst_Parse_KeyDecomposition_MapX_MapZ()
    {
        // Key formula: key = mapZ + 100000 * mapX.
        // spec: Docs/RE/formats/terrain.md §1.2 — "key = mapZ + 100000 * mapX": CONFIRMED.
        int mapX = 10005;
        int mapZ = 10003;
        uint key = LstManifestParser.ComputeKey(mapX, mapZ);

        byte[] data = BuildLst([key]);
        LstManifest manifest = LstManifestParser.Parse(data.AsSpan());

        Assert.Equal(mapX, manifest.Entries[0].MapX);
        Assert.Equal(mapZ, manifest.Entries[0].MapZ);
    }

    [Fact]
    public void Lst_ComputeKey_RoundTrips()
    {
        // spec: Docs/RE/formats/terrain.md §1.2 — key formula: CONFIRMED.
        int mapX = 10042;
        int mapZ = 10007;
        uint key = LstManifestParser.ComputeKey(mapX, mapZ);

        // Decompose back
        int decodedX = (int)(key / 100000u);
        int decodedZ = (int)(key % 100000u);

        Assert.Equal(mapX, decodedX);
        Assert.Equal(mapZ, decodedZ);
    }

    [Fact]
    public void Lst_Parse_ZeroEntries_EmptyArray()
    {
        byte[] data = BuildLst([]);
        LstManifest manifest = LstManifestParser.Parse(data.AsSpan());
        Assert.Empty(manifest.Entries);
    }

    [Fact]
    public void Lst_Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        byte[] full = BuildLst([12345u, 67890u]);
        byte[] truncated = full[..(full.Length - 2)]; // cut last 2 bytes of second key
        Assert.Throws<InvalidDataException>(() => LstManifestParser.Parse(truncated.AsSpan()));
    }

    // =========================================================================
    // TedTerrainParser tests
    // =========================================================================

    /// <summary>
    /// Builds a minimal synthetic .ted buffer with known values in each block.
    /// spec: Docs/RE/formats/terrain.md §5.2 — five sequential blocks, total 46987 bytes: CONFIRMED.
    /// </summary>
    private static byte[] BuildTed(
        float heightValue = 123.456f,
        byte normalR = 200, byte normalG = 128, byte normalB = 64,
        byte lookupByte = 0xAB,
        byte directionByte = 0xCD,
        byte diffuseR = 255, byte diffuseG = 0, byte diffuseB = 128, byte diffuseA = 255)
    {
        // Total: 46987 bytes.
        // spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46987 bytes (0xB78B)": CONFIRMED.
        int vertexCount = TerrainCell.VertexCount; // 4225
        var buffer = new byte[16900 + 12675 + 256 + 256 + 16900]; // = 46987

        // Block 1 — Heightmap: 4225 × f32 LE @ offset 0.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 1 — offset 0, size 16900: CONFIRMED.
        for (int i = 0; i < vertexCount; i++)
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i * 4, 4), heightValue);

        // Block 2 — Normals: 4225 × RGB u8×3 @ offset 16900.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 2 — offset 16900, size 12675: CONFIRMED.
        int normOffset = 16900;
        for (int i = 0; i < vertexCount; i++)
        {
            buffer[normOffset + i * 3 + 0] = normalR;
            buffer[normOffset + i * 3 + 1] = normalG;
            buffer[normOffset + i * 3 + 2] = normalB;
        }

        // Block 3 — Lookup: 256 bytes @ offset 29575.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 3 — offset 29575, size 256: CONFIRMED.
        int lookupOffset = 29575;
        buffer[lookupOffset] = lookupByte;

        // Block 4 — Direction: 256 bytes @ offset 29831.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 4 — offset 29831, size 256: CONFIRMED.
        int dirOffset = 29831;
        buffer[dirOffset] = directionByte;

        // Block 5 — Diffuse RGBA: 4225 × u8×4 @ offset 30087.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — offset 30087, size 16900: CONFIRMED.
        int diffuseOffset = 30087;
        for (int i = 0; i < vertexCount; i++)
        {
            buffer[diffuseOffset + i * 4 + 0] = diffuseR;
            buffer[diffuseOffset + i * 4 + 1] = diffuseG;
            buffer[diffuseOffset + i * 4 + 2] = diffuseB;
            buffer[diffuseOffset + i * 4 + 3] = diffuseA;
        }

        return buffer;
    }

    [Fact]
    public void Ted_Parse_TotalFileSize_Is46987()
    {
        // Verify the fixture matches the spec total size.
        // spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46987 bytes (0xB78B)": CONFIRMED.
        byte[] data = BuildTed();
        Assert.Equal(46987, data.Length);
    }

    [Fact]
    public void Ted_Parse_HeightmapCount_Is4225()
    {
        // spec: Docs/RE/formats/terrain.md §5.1 — "65×65 = 4225 vertices": CONFIRMED.
        byte[] data = BuildTed(heightValue: 7.5f);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        Assert.Equal(4225, cell.Heights.Length);
    }

    [Fact]
    public void Ted_Parse_HeightValues_RoundTrip()
    {
        // spec: Docs/RE/formats/terrain.md §5.2 Block 1 — "f32le, 65×65": CONFIRMED.
        const float expected = 512.25f;
        byte[] data = BuildTed(heightValue: expected);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        // Check first and last vertex.
        Assert.Equal(expected, cell.Heights[0], precision: 4);
        Assert.Equal(expected, cell.Heights[4224], precision: 4);
    }

    [Fact]
    public void Ted_Parse_NormalCount_Is12675Bytes()
    {
        // spec: Docs/RE/formats/terrain.md §5.2 Block 2 — "u8×3 (RGB), 65×65 = 12675 bytes": CONFIRMED.
        byte[] data = BuildTed(normalR: 10, normalG: 20, normalB: 30);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        Assert.Equal(12675, cell.Normals.Length);
        Assert.Equal((byte)10, cell.Normals[0]); // R of vertex 0
        Assert.Equal((byte)20, cell.Normals[1]); // G of vertex 0
        Assert.Equal((byte)30, cell.Normals[2]); // B of vertex 0
    }

    [Fact]
    public void Ted_Parse_LookupTable_Is256Bytes()
    {
        // spec: Docs/RE/formats/terrain.md §5.2 Block 3 — "u8, 256 entries": CONFIRMED.
        byte[] data = BuildTed(lookupByte: 0xAB);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        Assert.Equal(256, cell.LookupTable.Length);
        Assert.Equal((byte)0xAB, cell.LookupTable[0]);
    }

    [Fact]
    public void Ted_Parse_DirectionMap_Is256Bytes()
    {
        // spec: Docs/RE/formats/terrain.md §5.2 Block 4 — "u8, 256 entries": CONFIRMED.
        byte[] data = BuildTed(directionByte: 0xCD);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        Assert.Equal(256, cell.DirectionMap.Length);
        Assert.Equal((byte)0xCD, cell.DirectionMap[0]);
    }

    [Fact]
    public void Ted_Parse_DiffuseColour_Is16900Bytes()
    {
        // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — "u8×4 (RGBA), 65×65 = 16900 bytes": CONFIRMED.
        byte[] data = BuildTed(diffuseR: 255, diffuseG: 0, diffuseB: 128, diffuseA: 200);
        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        Assert.Equal(16900, cell.DiffuseColours.Length);
        Assert.Equal((byte)255, cell.DiffuseColours[0]); // R of vertex 0
        Assert.Equal((byte)0, cell.DiffuseColours[1]);   // G of vertex 0
        Assert.Equal((byte)128, cell.DiffuseColours[2]); // B of vertex 0
        Assert.Equal((byte)200, cell.DiffuseColours[3]); // A of vertex 0
    }

    [Fact]
    public void Ted_Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        byte[] full = BuildTed();
        byte[] truncated = full[..(full.Length - 100)];
        Assert.Throws<InvalidDataException>(() => TedTerrainParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Ted_Parse_BlockOffsets_AreCorrect()
    {
        // Verify block boundaries via distinct sentinel values in each block.
        // spec: Docs/RE/formats/terrain.md §5.2 — block offsets CONFIRMED.
        byte[] data = BuildTed(
            heightValue: 1.0f,
            normalR: 11, normalG: 22, normalB: 33,
            lookupByte: 0x44,
            directionByte: 0x55,
            diffuseR: 66, diffuseG: 77, diffuseB: 88, diffuseA: 99);

        TerrainCell cell = TedTerrainParser.Parse(data.AsSpan());

        // Block 1 sample
        Assert.Equal(1.0f, cell.Heights[0], precision: 5);
        // Block 2 sample
        Assert.Equal((byte)11, cell.Normals[0]);
        // Block 3 sample
        Assert.Equal((byte)0x44, cell.LookupTable[0]);
        // Block 4 sample
        Assert.Equal((byte)0x55, cell.DirectionMap[0]);
        // Block 5 sample
        Assert.Equal((byte)66, cell.DiffuseColours[0]);
    }

    // =========================================================================
    // MapDescriptorParser tests
    // =========================================================================

    [Fact]
    public void Map_Parse_Sections_Keywords()
    {
        // spec: Docs/RE/formats/terrain.md §3.1 Sections — all keywords CONFIRMED.
        string mapText = """
            TERRAIN {
            }
            SOLID {
            }
            """;

        MapDescriptor desc = MapDescriptorParser.ParseText(mapText);

        Assert.Equal(2, desc.Sections.Length);
        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
        Assert.Equal("SOLID", desc.Sections[1].Keyword);
    }

    [Fact]
    public void Map_Parse_DataFile_Directive()
    {
        // spec: Docs/RE/formats/terrain.md §3.2 DATAFILE directive: CONFIRMED.
        string mapText = """
            TERRAIN {
                DATAFILE data/map001/dat/d001x10000z10000.ted
            }
            """;

        MapDescriptor desc = MapDescriptorParser.ParseText(mapText);

        Assert.Single(desc.Sections);
        Assert.Equal("data/map001/dat/d001x10000z10000.ted", desc.Sections[0].DataFile);
    }

    [Fact]
    public void Map_Parse_Textures_Directive()
    {
        // spec: Docs/RE/formats/terrain.md §3.3 TEXTURES directive — (intFlag, intTexId) pairs: CONFIRMED.
        // intFlag semantics: UNVERIFIED.
        string mapText = """
            TERRAIN {
                TEXTURES {
                    0 5
                    1 10
                    2 15
                }
            }
            """;

        MapDescriptor desc = MapDescriptorParser.ParseText(mapText);

        Assert.Single(desc.Sections);
        (int Flag, int TexId)[] textures = desc.Sections[0].Textures;
        Assert.Equal(3, textures.Length);
        Assert.Equal((0, 5), textures[0]);
        Assert.Equal((1, 10), textures[1]);
        Assert.Equal((2, 15), textures[2]);
    }

    [Fact]
    public void Map_Parse_Comments_Ignored()
    {
        // spec: Docs/RE/formats/terrain.md §3 — "Lines beginning with '#' are comments": CONFIRMED.
        string mapText = """
            # This is a comment
            TERRAIN {
                # Another comment
                DATAFILE data/test.ted
            }
            """;

        MapDescriptor desc = MapDescriptorParser.ParseText(mapText);

        Assert.Single(desc.Sections);
        Assert.Equal("data/test.ted", desc.Sections[0].DataFile);
    }

    [Fact]
    public void Map_Parse_MultipleSections_DataFileAndTextures()
    {
        // spec: Docs/RE/formats/terrain.md §3.1 — multiple sections: CONFIRMED.
        string mapText = """
            TERRAIN {
                DATAFILE primary.ted
                TEXTURES {
                    0 1
                }
            }
            EXTRA_TERRAIN {
                DATAFILE water.ted
            }
            BUILDING {
            }
            """;

        MapDescriptor desc = MapDescriptorParser.ParseText(mapText);

        Assert.Equal(3, desc.Sections.Length);

        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
        Assert.Equal("primary.ted", desc.Sections[0].DataFile);
        Assert.Single(desc.Sections[0].Textures);

        Assert.Equal("EXTRA_TERRAIN", desc.Sections[1].Keyword);
        Assert.Equal("water.ted", desc.Sections[1].DataFile);

        Assert.Equal("BUILDING", desc.Sections[2].Keyword);
        Assert.Null(desc.Sections[2].DataFile);
        Assert.Empty(desc.Sections[2].Textures);
    }

    [Fact]
    public void Map_Parse_FxSections_AllSevenSlots()
    {
        // spec: Docs/RE/formats/terrain.md §3.1 Sections — "FX1 … FX7 (7 named slots)": CONFIRMED.
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= 7; i++)
            sb.AppendLine($"FX{i} {{ }}");

        MapDescriptor desc = MapDescriptorParser.ParseText(sb.ToString());

        Assert.Equal(7, desc.Sections.Length);
        for (int i = 0; i < 7; i++)
            Assert.Equal($"FX{i + 1}", desc.Sections[i].Keyword);
    }

    [Fact]
    public void Map_Parse_EmptyFile_NoSections()
    {
        MapDescriptor desc = MapDescriptorParser.ParseText("");
        Assert.Empty(desc.Sections);
    }

    // =========================================================================
    // MudBlobParser tests
    // =========================================================================

    [Fact]
    public void Mud_Parse_FixedSize_32768()
    {
        // spec: Docs/RE/formats/terrain.md §6 — "32768 bytes (0x8000)": CONFIRMED.
        byte[] data = new byte[MudBlob.FixedSize]; // 32768 zeroes
        MudBlob mud = MudBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(MudBlob.FixedSize, mud.RawData.Length);
    }

    [Fact]
    public void Mud_Parse_WrongSize_ThrowsInvalidData()
    {
        // spec: Docs/RE/formats/terrain.md §6 — fixed size check.
        byte[] tooSmall = new byte[MudBlob.FixedSize - 1];
        Assert.Throws<InvalidDataException>(() => MudBlobParser.Parse(new ReadOnlyMemory<byte>(tooSmall)));

        byte[] tooLarge = new byte[MudBlob.FixedSize + 1];
        Assert.Throws<InvalidDataException>(() => MudBlobParser.Parse(new ReadOnlyMemory<byte>(tooLarge)));
    }

    [Fact]
    public void Mud_Parse_RawDataPreserved()
    {
        // Internal layout UNVERIFIED; verify the raw bytes are preserved as-is.
        // spec: Docs/RE/formats/terrain.md §6 — "internal structure UNVERIFIED".
        byte[] data = new byte[MudBlob.FixedSize];
        data[0] = 0xDE;
        data[MudBlob.FixedSize - 1] = 0xAD;

        MudBlob mud = MudBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal((byte)0xDE, mud.RawData.Span[0]);
        Assert.Equal((byte)0xAD, mud.RawData.Span[MudBlob.FixedSize - 1]);
    }

    // =========================================================================
    // SodBlobParser tests
    // =========================================================================

    /// <summary>
    /// Builds a synthetic .sod buffer.
    /// spec: Docs/RE/formats/terrain.md §8 — solidCount u32le | solidCount × 108-byte records | per-record tri data.
    /// </summary>
    private static byte[] BuildSod(
        (uint triCount, byte[] record108, byte[] triData)[] solids)
    {
        using var ms = new System.IO.MemoryStream();

        // solidCount u32le @ offset 0.
        // spec: Docs/RE/formats/terrain.md §8.1 — "solidCount u32le @ offset 0: CONFIRMED".
        ms.Write(Le4((uint)solids.Length));

        // solidCount × 108-byte records.
        // spec: Docs/RE/formats/terrain.md §8.2 — "stride 108 bytes CONFIRMED".
        foreach (var (_, record108, _) in solids)
        {
            if (record108.Length != 108)
                throw new ArgumentException("SolidRecord must be exactly 108 bytes.");
            ms.Write(record108);
        }

        // Per-solid triangle data.
        // spec: Docs/RE/formats/terrain.md §8.3 — "triCount u32le | triCount × 48-byte triangles CONFIRMED (stride)".
        foreach (var (triCount, _, triData) in solids)
        {
            ms.Write(Le4(triCount));
            ms.Write(triData); // length must be triCount × 48
        }

        return ms.ToArray();
    }

    [Fact]
    public void Sod_Parse_SolidCount()
    {
        // spec: Docs/RE/formats/terrain.md §8.1 — "solidCount u32le: CONFIRMED".
        var rec = new byte[108];
        byte[] data = BuildSod([(triCount: 0u, record108: rec, triData: [])]);

        SodBlob sod = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(1u, sod.SolidCount);
    }

    [Fact]
    public void Sod_Parse_SolidRecordStride_Is108()
    {
        // spec: Docs/RE/formats/terrain.md §8.2 — "108 bytes (0x6C): CONFIRMED (stride)".
        var rec0 = new byte[108];
        rec0[0] = 0xAA; // sentinel
        var rec1 = new byte[108];
        rec1[0] = 0xBB;

        byte[] data = BuildSod([
            (0u, rec0, []),
            (0u, rec1, []),
        ]);

        SodBlob sod = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(2u, sod.SolidCount);
        Assert.Equal((byte)0xAA, sod.RawSolidRecords[0].Span[0]);
        Assert.Equal((byte)0xBB, sod.RawSolidRecords[1].Span[0]);
        Assert.Equal(108, sod.RawSolidRecords[0].Length);
    }

    [Fact]
    public void Sod_Parse_TriangleCounts()
    {
        // spec: Docs/RE/formats/terrain.md §8.3 — "triCount u32le: CONFIRMED".
        var rec = new byte[108];
        // 2 triangles × 48 bytes each = 96 bytes
        byte[] triData = new byte[2 * 48];
        byte[] data = BuildSod([(triCount: 2u, record108: rec, triData: triData)]);

        SodBlob sod = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(2u, sod.TriangleCounts[0]);
        Assert.Equal(2 * 48, sod.RawTriangleData[0].Length);
    }

    [Fact]
    public void Sod_Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        var rec = new byte[108];
        byte[] data = BuildSod([(0u, rec, [])]);
        byte[] truncated = data[..(data.Length - 5)];

        Assert.Throws<InvalidDataException>(() => SodBlobParser.Parse(new ReadOnlyMemory<byte>(truncated)));
    }

    [Fact]
    public void Sod_Parse_EmptySolids()
    {
        byte[] data = BuildSod([]);
        SodBlob sod = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));
        Assert.Equal(0u, sod.SolidCount);
        Assert.Empty(sod.RawSolidRecords);
    }
}
