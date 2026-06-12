using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using System.Text;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based and real-VFS tests for <see cref="UiTexManifestParser"/>.
/// All synthetic fixtures are built as inline strings; no real game bytes are committed.
/// spec: Docs/RE/formats/ui_manifests.md §1 data/ui/UiTex.txt.
/// </summary>
public sealed class UiTexManifestParserTests
{
    // =========================================================================
    // Fixture tests
    // =========================================================================

    [Fact]
    public void ParseText_MinimalDdsBlock_ProducesEntry()
    {
        // spec: Docs/RE/formats/ui_manifests.md §1.2 — UI_TEXTURE { DDS { <id> "<path>" } }: PARSER-CONFIRMED.
        const string txt = """
                           UI_TEXTURE
                           {
                               DDS
                               {
                                   0001 "data/ui/mainwindow.dds"
                               }
                               MSK
                               {
                               }
                           }
                           """;

        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Equal(1, result.DdsEntries.Count);
        Assert.Equal(0, result.MskEntries.Count);
        UiTexEntry? e = result.GetById(1);
        Assert.NotNull(e);
        Assert.Equal(1, e.TexId);
        Assert.Equal("data/ui/mainwindow.dds", e.VfsPath);
        Assert.Equal(UiTexBlockKind.Dds, e.BlockKind);
    }

    [Fact]
    public void ParseText_MultipleEntries_AllDecoded()
    {
        // spec: §1.3 — "tex_id 4-digit zero-padded decimal, then quoted vfs_path": PARSER-CONFIRMED.
        const string txt = """
                           UI_TEXTURE
                           {
                               DDS
                               {
                                   0001 "data/ui/mainwindow.dds"
                                   0002 "data/ui/inventwindow.dds"
                                   0008 "data/ui/skillwindow.dds"
                               }
                               MSK
                               {
                               }
                           }
                           """;

        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Equal(3, result.DdsEntries.Count);
        Assert.Equal("data/ui/mainwindow.dds", result.GetById(1)!.VfsPath);
        Assert.Equal("data/ui/inventwindow.dds", result.GetById(2)!.VfsPath);
        Assert.Equal("data/ui/skillwindow.dds", result.GetById(8)!.VfsPath);
    }

    [Fact]
    public void ParseText_CommentLines_Skipped()
    {
        // spec: §1.7 — "# triggers comment-skip for remainder of line": PARSER-CONFIRMED.
        const string txt = """
                           # This is a header comment
                           UI_TEXTURE
                           {
                               DDS
                               {
                                   # tex_id path
                                   0010 "data/ui/skillpipe.dds"
                               }
                               MSK
                               {
                               }
                           }
                           """;

        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Equal(1, result.DdsEntries.Count);
        Assert.Equal("data/ui/skillpipe.dds", result.GetById(10)!.VfsPath);
    }

    [Fact]
    public void ParseText_MissingClosingQuote_ToleratedEntry0029()
    {
        // Entry 0029 has a missing closing quote in the real file.
        // spec: §1.3 quoting caveat — "missing closing quote: treat rest of line as path value": PARSER-CONFIRMED.
        const string txt = """
                           UI_TEXTURE
                           {
                               DDS
                               {
                                   0029 "data/ui/inactivemember.dds
                               }
                               MSK
                               {
                               }
                           }
                           """;

        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        // The entry must be parsed despite the missing closing quote.
        Assert.Equal(1, result.DdsEntries.Count);
        UiTexEntry? e = result.GetById(29);
        Assert.NotNull(e);
        Assert.Equal("data/ui/inactivemember.dds", e.VfsPath);
    }

    [Fact]
    public void ParseText_UnknownId_ReturnsNull()
    {
        const string txt = "UI_TEXTURE { DDS { 0001 \"data/ui/foo.dds\" } MSK { } }";
        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Null(result.GetById(999));
    }

    [Fact]
    public void ParseText_EmptyMskBlock_YieldsNoMskEntries()
    {
        // spec: §1.6 — MSK block is empty in observed version: PARSER-CONFIRMED keyword.
        const string txt = "UI_TEXTURE { DDS { 0001 \"data/ui/x.dds\" } MSK { } }";
        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Equal(0, result.MskEntries.Count);
    }

    [Fact]
    public void ParseText_TotalCount_IncludesBothBlocks()
    {
        // Count property covers DDS + MSK.
        const string txt = "UI_TEXTURE { DDS { 0001 \"a\" 0002 \"b\" } MSK { } }";
        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseText_ConfirmedEntry0001_MainwindowDds()
    {
        // Confirmed entry from spec §1.4 — tex_id 0001 → data/ui/mainwindow.dds: SAMPLE-VERIFIED.
        const string txt = "UI_TEXTURE { DDS { 0001 \"data/ui/mainwindow.dds\" } MSK { } }";
        UiTexManifest result = UiTexManifestParser.ParseText(txt);

        UiTexEntry? e = result.GetById(1);
        Assert.NotNull(e);
        Assert.Equal("data/ui/mainwindow.dds", e.VfsPath);
    }

    // =========================================================================
    // Real-VFS smoke tests (skipped when clientdata absent)
    // =========================================================================

    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsPath);

    [Fact]
    public void Smoke_UiTexTxt_AtLeast30Entries()
    {
        // spec: §1.4 — "35–38 entries in observed version": SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/ui/uitex.txt")) return; // VFS path normalised to lower-case

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/ui/uitex.txt");
        UiTexManifest result = UiTexManifestParser.Parse(raw);

        Assert.True(result.Count >= 30,
            $"Expected at least 30 UiTex entries, got {result.Count}. " +
            "spec: Docs/RE/formats/ui_manifests.md §1.4 — 35-38 entries: SAMPLE-VERIFIED.");
    }

    [Fact]
    public void Smoke_UiTexTxt_ConfirmedEntry0001_Exists()
    {
        // spec: §1.4 — tex_id 0001 → data/ui/mainwindow.dds: SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/ui/uitex.txt")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/ui/uitex.txt");
        UiTexManifest result = UiTexManifestParser.Parse(raw);

        UiTexEntry? e = result.GetById(1);
        Assert.NotNull(e);
        Assert.Contains("mainwindow", e.VfsPath, StringComparison.OrdinalIgnoreCase);
    }
}