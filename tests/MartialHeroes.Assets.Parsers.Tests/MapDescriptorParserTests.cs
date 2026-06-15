using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="MapDescriptorParser"/>.
/// All fixtures are synthetic ASCII strings; no real VFS is required.
/// spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format)
/// </summary>
public sealed class MapDescriptorParserTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static MapDescriptor Parse(string text)
    {
        // Use the raw bytes → ReadOnlyMemory overload to exercise the full path.
        return MapDescriptorParser.Parse(new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(text)));
    }

    // ── Empty / comment-only tests ────────────────────────────────────────────

    [Fact]
    public void ParseText_EmptyFile_NoSections()
    {
        // An empty .map file must parse with zero sections.
        MapDescriptor desc = MapDescriptorParser.ParseText(string.Empty);
        Assert.Empty(desc.Sections);
    }

    [Fact]
    public void ParseText_CommentsOnly_NoSections()
    {
        // Lines beginning with '#' are comments and are silently ignored.
        // spec: terrain.md §3 — "Lines beginning with '#' are comments": CONFIRMED.
        string text = "# This is a comment\n# Another comment\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);
        Assert.Empty(desc.Sections);
    }

    [Fact]
    public void ParseText_InlineComment_Stripped()
    {
        // Inline comments after '#' on a content line must be stripped.
        // spec: terrain.md §3 — inline comments (after '#' on a line): CONFIRMED.
        string text = "TERRAIN { # open terrain\nDATAFILE map000_00_00.ted # data file\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);
        Assert.Single(desc.Sections);
        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
    }

    // ── Section keyword tests ────────────────────────────────────────────────

    [Theory]
    [InlineData("TERRAIN")]
    [InlineData("BUILDING")]
    [InlineData("FX1")]
    [InlineData("FX7")]
    [InlineData("SOLID")]
    [InlineData("EXTRA_TERRAIN")]
    [InlineData("UP_TERRAIN")]
    public void ParseText_KnownSectionKeyword_Parsed(string keyword)
    {
        // All confirmed section keywords must be parsed.
        // spec: terrain.md §3.1 Sections: CONFIRMED.
        string text = $"{keyword} {{\n}}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Single(desc.Sections);
        Assert.Equal(keyword, desc.Sections[0].Keyword);
    }

    [Fact]
    public void ParseText_UnknownKeyword_Ignored()
    {
        // Keywords not in the known set must be silently ignored (no throw).
        // spec: terrain.md §3 — unknown keyword lines are skipped.
        string text = "UNKNOWN_SECTION {\n}\nTERRAIN {\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Single(desc.Sections);
        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
    }

    // ── DATAFILE directive tests ─────────────────────────────────────────────

    [Fact]
    public void ParseText_DatafileDirective_IsDecoded()
    {
        // DATAFILE <path> must decode the path string.
        // spec: terrain.md §3.2 DATAFILE directive: CONFIRMED.
        string text = "TERRAIN {\nDATAFILE map000_10000_10000.ted\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal("map000_10000_10000.ted", desc.Sections[0].DataFile);
    }

    [Fact]
    public void ParseText_NoDatafileDirective_IsNull()
    {
        // A section without a DATAFILE directive must have DataFile=null.
        string text = "SOLID {\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Null(desc.Sections[0].DataFile);
    }

    // ── TEXTURES block tests ─────────────────────────────────────────────────

    [Fact]
    public void ParseText_TexturesBlock_DecodesEntries()
    {
        // TEXTURES { <flag> <texId> ... } must decode the integer pairs.
        // spec: terrain.md §3.3 TEXTURES directive: CONFIRMED (structure); intFlag semantics UNVERIFIED.
        string text = "TERRAIN {\nTEXTURES {\n0 1001\n1 1002\n}\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(2, desc.Sections[0].Textures.Length);
        Assert.Equal((0, 1001), desc.Sections[0].Textures[0]);
        Assert.Equal((1, 1002), desc.Sections[0].Textures[1]);
    }

    [Fact]
    public void ParseText_TexturesBraceOnSameLine_Parsed()
    {
        // TEXTURES { on the same line must be accepted.
        string text = "BUILDING {\nTEXTURES {\n3 5001\n}\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Single(desc.Sections[0].Textures);
        Assert.Equal((3, 5001), desc.Sections[0].Textures[0]);
    }

    [Fact]
    public void ParseText_EmptyTexturesBlock_YieldsZeroTextures()
    {
        // A TEXTURES block with no entries must yield an empty array.
        string text = "TERRAIN {\nTEXTURES {\n}\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Empty(desc.Sections[0].Textures);
    }

    // ── Geometry directive tests (TERRAIN section) ───────────────────────────

    [Fact]
    public void ParseText_WidthDirective_IsDecoded()
    {
        // WIDTH <integer> (quads per row). Typically 64.
        // spec: terrain.md §3.4 — WIDTH integer: CONFIRMED.
        string text = "TERRAIN {\nWIDTH 64\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(64, desc.Sections[0].Width);
    }

    [Fact]
    public void ParseText_HeightDirective_IsDecoded()
    {
        // HEIGHT <integer> (quads per column). Typically 64.
        // spec: terrain.md §3.4 — HEIGHT integer: CONFIRMED.
        string text = "TERRAIN {\nHEIGHT 64\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(64, desc.Sections[0].Height);
    }

    [Fact]
    public void ParseText_GridDirective_IsDecoded()
    {
        // GRID <integer> (world-unit vertex spacing). Typically 16.
        // spec: terrain.md §3.4 — GRID integer: CONFIRMED.
        string text = "TERRAIN {\nGRID 16\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(16, desc.Sections[0].Grid);
    }

    [Fact]
    public void ParseText_MaxHeightFiled_IsDecoded()
    {
        // MAX_HEIGHTFILED <float> — verbatim dropped-L spelling from original files.
        // spec: terrain.md §3.4 — MAX_HEIGHTFILED float: CONFIRMED.
        // Note: "HEIGHTFILED" is the exact verbatim keyword (dropped 'L') from the data files.
        string text = "TERRAIN {\nMAX_HEIGHTFILED 1024.0\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.NotNull(desc.Sections[0].MaxHeightFiled);
        Assert.Equal(1024f, desc.Sections[0].MaxHeightFiled!.Value, precision: 3);
    }

    [Fact]
    public void ParseText_MinHeightFiled_IsDecoded()
    {
        // MIN_HEIGHTFILED <float> — verbatim dropped-L spelling from original files.
        // spec: terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
        string text = "TERRAIN {\nMIN_HEIGHTFILED -100.5\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.NotNull(desc.Sections[0].MinHeightFiled);
        Assert.Equal(-100.5f, desc.Sections[0].MinHeightFiled!.Value, precision: 3);
    }

    [Fact]
    public void ParseText_OriginDirective_IsDecoded()
    {
        // ORIGIN <float>,<float> — world-space XZ cell origin.
        // spec: terrain.md §3.4 — ORIGIN float,float: CONFIRMED.
        // Format: "0.000,-1024.000" (comma-separated, optional spaces).
        string text = "TERRAIN {\nORIGIN 0.000,-1024.000\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.NotNull(desc.Sections[0].Origin);
        Assert.Equal(0f, desc.Sections[0].Origin!.Value.X, precision: 3);
        Assert.Equal(-1024f, desc.Sections[0].Origin!.Value.Z, precision: 3);
    }

    [Fact]
    public void ParseText_OriginWithSpace_IsDecoded()
    {
        // ORIGIN can have a space after the comma: "0.000, -1024.000".
        string text = "TERRAIN {\nORIGIN 0.000, -1024.000\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(-1024f, desc.Sections[0].Origin!.Value.Z, precision: 3);
    }

    // ── Absent directives → null tests ───────────────────────────────────────

    [Fact]
    public void ParseText_NoGeometryDirectives_AllNull()
    {
        // A section without any geometry directives must have all nullable fields as null.
        string text = "BUILDING {\nDATAFILE building000.bud\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        var s = desc.Sections[0];
        Assert.Null(s.Width);
        Assert.Null(s.Height);
        Assert.Null(s.Grid);
        Assert.Null(s.MaxHeightFiled);
        Assert.Null(s.MinHeightFiled);
        Assert.Null(s.Origin);
    }

    // ── Multi-section tests ──────────────────────────────────────────────────

    [Fact]
    public void ParseText_MultipleSections_InOrder()
    {
        // Multiple sections must be returned in parse (on-disk) order.
        // spec: terrain.md §3.1 — all known section keywords: CONFIRMED.
        string text = "TERRAIN {\nDATAFILE t.ted\n}\nBUILDING {\nDATAFILE b.bud\n}\nSOLID {\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Equal(3, desc.Sections.Length);
        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
        Assert.Equal("BUILDING", desc.Sections[1].Keyword);
        Assert.Equal("SOLID", desc.Sections[2].Keyword);
        Assert.Equal("t.ted", desc.Sections[0].DataFile);
        Assert.Equal("b.bud", desc.Sections[1].DataFile);
    }

    // ── Keyword case-insensitivity ────────────────────────────────────────────

    [Fact]
    public void ParseText_LowercaseKeyword_Parsed()
    {
        // Keywords are stored uppercase but parsed case-insensitively.
        // spec: terrain.md §3 — keyword normalization to uppercase.
        string text = "terrain {\nDATAFILE x.ted\n}\n";
        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Single(desc.Sections);
        Assert.Equal("TERRAIN", desc.Sections[0].Keyword);
    }

    // ── ReadOnlyMemory overload ───────────────────────────────────────────────

    [Fact]
    public void Parse_ReadOnlyMemory_Overload_Works()
    {
        // The ReadOnlyMemory<byte> overload must delegate correctly.
        string text = "SOLID {\n}\n";
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        MapDescriptor desc = MapDescriptorParser.Parse(new ReadOnlyMemory<byte>(bytes));

        Assert.Single(desc.Sections);
        Assert.Equal("SOLID", desc.Sections[0].Keyword);
    }

    // ── FX section enumeration test ──────────────────────────────────────────

    [Fact]
    public void ParseText_AllFxSections_Fx1ToFx7_Parsed()
    {
        // FX1 through FX7 are all confirmed section keywords.
        // spec: terrain.md §3.1 — "FX1–FX7: CONFIRMED".
        var sb = new StringBuilder();
        for (int i = 1; i <= 7; i++)
            sb.AppendLine($"FX{i} {{\n}}");
        MapDescriptor desc = MapDescriptorParser.ParseText(sb.ToString());

        Assert.Equal(7, desc.Sections.Length);
        for (int i = 0; i < 7; i++)
            Assert.Equal($"FX{i + 1}", desc.Sections[i].Keyword);
    }

    // ── Full realistic TERRAIN section ───────────────────────────────────────

    [Fact]
    public void ParseText_RealisticTerrainSection_AllDirectivesDecoded()
    {
        // A realistic full TERRAIN section matching the known .map file structure.
        // spec: terrain.md §3.4 — all directives CONFIRMED.
        // Coordinate: ((10000-10000)*1024, (10000-10000)*1024) = (0, 0) for origin of cell (10000,10000).
        string text =
            "TERRAIN {\n" +
            "WIDTH 64\n" +
            "HEIGHT 64\n" +
            "GRID 16\n" +
            "MAX_HEIGHTFILED 256.0\n" +
            "MIN_HEIGHTFILED 0.0\n" +
            "ORIGIN 0.000,0.000\n" +
            "DATAFILE map000_10000_10000.ted\n" +
            "TEXTURES {\n" +
            "0 1001\n" +
            "}\n" +
            "}\n";

        MapDescriptor desc = MapDescriptorParser.ParseText(text);

        Assert.Single(desc.Sections);
        var s = desc.Sections[0];
        Assert.Equal("TERRAIN", s.Keyword);
        Assert.Equal(64, s.Width);
        Assert.Equal(64, s.Height);
        Assert.Equal(16, s.Grid);
        Assert.Equal(256f, s.MaxHeightFiled!.Value, precision: 3);
        Assert.Equal(0f, s.MinHeightFiled!.Value, precision: 3);
        Assert.Equal(0f, s.Origin!.Value.X, precision: 3);
        Assert.Equal(0f, s.Origin!.Value.Z, precision: 3);
        Assert.Equal("map000_10000_10000.ted", s.DataFile);
        Assert.Single(s.Textures);
        Assert.Equal((0, 1001), s.Textures[0]);
    }
}