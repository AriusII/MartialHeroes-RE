using Xunit;
using MartialHeroes.Assets.Parsers;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Tests for <see cref="BgTextureTxtParser"/> — the TAB-separated text companion of the
/// background texture pool (data/map000/texture/bgtexture.txt).
/// spec: Docs/RE/formats/terrain.md §4.2.
/// </summary>
public sealed class BgTextureTxtParserTests
{
    [Fact]
    public void Parses_tab_separated_index_type_path_lines()
    {
        // spec: Docs/RE/formats/terrain.md §4.2 — "<poolIndex>\t<typeFlag>\t<relPath>".
        const string txt = "0\t1\tterrain/a-b-1\n116\t1\tterrain/g3\n200\t2\tbuilding/_castle\n";
        var cat = BgTextureTxtParser.ParseText(txt);

        Assert.Equal(3, cat.Count);
        Assert.Equal("terrain/a-b-1", cat.GetRelPath(0));
        Assert.Equal("terrain/g3", cat.GetRelPath(116)); // the real target-cell ground texture
        Assert.Equal("building/_castle", cat.GetRelPath(200));
    }

    [Fact]
    public void Unknown_index_returns_null()
    {
        var cat = BgTextureTxtParser.ParseText("0\t1\tterrain/a\n");
        Assert.Null(cat.GetRelPath(999));
    }

    [Fact]
    public void Tolerates_crlf_blank_and_malformed_lines()
    {
        // CRLF line endings, a blank line, a too-short line, and a non-numeric index are all skipped.
        const string txt = "0\t1\tterrain/a\r\n\r\njunkline\r\nx\t1\tbad\r\n5\t1\tterrain/b\r\n";
        var cat = BgTextureTxtParser.ParseText(txt);

        Assert.Equal(2, cat.Count);
        Assert.Equal("terrain/a", cat.GetRelPath(0));
        Assert.Equal("terrain/b", cat.GetRelPath(5));
    }
}