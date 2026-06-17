using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="TextureListParser"/> and <see cref="TextureListManifest"/>.
/// All synthetic fixtures are built from spec rules; no real game bytes are committed.
/// spec: Docs/RE/formats/ui_manifests.md §10 data/item/texturelist.txt.
/// </summary>
public sealed class TextureListParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static byte[] EncodeCP949(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949).GetBytes(text);
    }

    // ─── empty input tests ────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyText_ReturnsEmptyManifest()
    {
        TextureListManifest manifest = TextureListParser.ParseText(string.Empty);
        Assert.Equal(0, manifest.Count);
    }

    [Fact]
    public void Parse_EmptySpan_ReturnsEmptyManifest()
    {
        TextureListManifest manifest = TextureListParser.Parse(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, manifest.Count);
    }

    [Fact]
    public void Parse_EmptyMemory_ReturnsEmptyManifest()
    {
        TextureListManifest manifest = TextureListParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(0, manifest.Count);
    }

    // ─── single-line tests ────────────────────────────────────────────────────

    [Fact]
    public void ParseText_SingleLine_ExtractsTexIdAndVfsPath()
    {
        // Spec example: "1234someicon.dds" → tex_id=1234, path="data/item/texture/1234someicon.dds"
        // spec: Docs/RE/formats/ui_manifests.md §10.3 example — "1234someicon.dds → tex_id=1234": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("1234someicon.dds\r\n");

        Assert.Equal(1, manifest.Count);
        var entry = manifest.Entries[0];
        Assert.Equal(1234, entry.TexId);
        Assert.Equal("data/item/texture/1234someicon.dds", entry.VfsPath);
    }

    [Fact]
    public void ParseText_CharacterBucket_UsesProvidedTargetDirectory()
    {
        // spec: Docs/RE/formats/texture.md §List files and target directories —
        // data/char/tex512512list.txt resolves under data/char/tex512512/.
        var manifest = TextureListParser.ParseText(
            "419000410.png\r\n",
            "data/char/tex512512/");

        var entry = manifest.GetById(419000410);
        Assert.NotNull(entry);
        Assert.Equal("data/char/tex512512/419000410.png", entry!.VfsPath);
    }

    [Fact]
    public void ParseText_SingleLine_NoTrailingNewline()
    {
        // File may end without a final newline.
        TextureListManifest manifest = TextureListParser.ParseText("0001icon.dds");

        Assert.Equal(1, manifest.Count);
        Assert.Equal(1, manifest.Entries[0].TexId);
    }

    [Fact]
    public void ParseText_LeadingDigitsOnly_AreTexId()
    {
        // Leading decimal digits before the extension dot are the tex_id.
        // spec: §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("0099item_sword.dds\r\n");

        Assert.Equal(1, manifest.Count);
        Assert.Equal(99, manifest.Entries[0].TexId);
    }

    [Fact]
    public void ParseText_NoLeadingDigits_ProducesTexIdZero()
    {
        // A line with no leading digit prefix → atol() returns 0.
        // spec: §10.3 step 4 — "atol stops at first non-digit; returns 0 when no leading digit": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("iconnodigit.dds\r\n");

        Assert.Equal(1, manifest.Count);
        Assert.Equal(0, manifest.Entries[0].TexId);
    }

    [Fact]
    public void ParseText_VfsPath_UsesPrefixAndOriginalLine()
    {
        // The VFS path must be "data/item/texture/" + original filename (including extension).
        // spec: §10.3 step 5 — "data/item/texture/ + name + ext": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("0007sword_big.dds\r\n");

        Assert.Equal("data/item/texture/0007sword_big.dds", manifest.Entries[0].VfsPath);
    }

    [Fact]
    public void ParseText_ExtensionIsPreserved()
    {
        // Extension (.dds, .tga, etc.) must survive round-trip in the VFS path.
        // spec: §10.3 step 3 — "split into name and ext ('.' + remainder)": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("0042item.tga\r\n");

        Assert.Equal("data/item/texture/0042item.tga", manifest.Entries[0].VfsPath);
    }

    // ─── multi-line tests ─────────────────────────────────────────────────────

    [Fact]
    public void ParseText_MultipleLines_AllParsed()
    {
        // Multiple lines → multiple entries.
        // spec: §10.2 — "plain newline-delimited list of filenames": CODE-CONFIRMED.
        string text = "0001sword.dds\r\n0002shield.dds\r\n0003bow.dds\r\n";
        TextureListManifest manifest = TextureListParser.ParseText(text);

        Assert.Equal(3, manifest.Count);
        Assert.Equal(1, manifest.Entries[0].TexId);
        Assert.Equal(2, manifest.Entries[1].TexId);
        Assert.Equal(3, manifest.Entries[2].TexId);
    }

    [Fact]
    public void ParseText_BlankLines_AreSkipped()
    {
        // Blank lines (CRLF-only lines) must not produce entries.
        string text = "0001sword.dds\r\n\r\n0002shield.dds\r\n";
        TextureListManifest manifest = TextureListParser.ParseText(text);

        Assert.Equal(2, manifest.Count);
    }

    [Fact]
    public void ParseText_WhitespaceOnlyLines_AreSkipped()
    {
        string text = "0001sword.dds\r\n   \r\n0002shield.dds\r\n";
        TextureListManifest manifest = TextureListParser.ParseText(text);

        Assert.Equal(2, manifest.Count);
    }

    [Fact]
    public void ParseText_LinesWithNoDot_AreSkipped()
    {
        // A line with no '.' cannot form a valid path — it is skipped.
        // spec: §10.3 step 2 — "locate the last '.' in the line": CODE-CONFIRMED (implied: no dot → skip).
        string text = "0001sword.dds\r\nnodotfile\r\n0003bow.dds\r\n";
        TextureListManifest manifest = TextureListParser.ParseText(text);

        Assert.Equal(2, manifest.Count);
        Assert.Equal(1, manifest.Entries[0].TexId);
        Assert.Equal(3, manifest.Entries[1].TexId);
    }

    // ─── GetById tests ────────────────────────────────────────────────────────

    [Fact]
    public void GetById_ExistingTexId_ReturnsEntry()
    {
        // spec: §10.3 — keyed by tex_id (non-contiguous dictionary): CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText(
            "0001sword.dds\r\n0007shield.dds\r\n");

        var found = manifest.GetById(7);
        Assert.NotNull(found);
        Assert.Equal(7, found!.TexId);
        Assert.Equal("data/item/texture/0007shield.dds", found.VfsPath);
    }

    [Fact]
    public void GetById_MissingTexId_ReturnsNull()
    {
        TextureListManifest manifest = TextureListParser.ParseText("0001sword.dds\r\n");

        Assert.Null(manifest.GetById(999));
    }

    [Fact]
    public void GetById_NonContiguousIds_EachFound()
    {
        // The ID space is non-contiguous; all registered IDs must be found by direct lookup.
        // spec: §10.3 — "tex_id key space is non-contiguous; parser must build a dictionary": CODE-CONFIRMED.
        string text = "0001sword.dds\r\n0050shield.dds\r\n0999bow.dds\r\n";
        TextureListManifest manifest = TextureListParser.ParseText(text);

        Assert.NotNull(manifest.GetById(1));
        Assert.NotNull(manifest.GetById(50));
        Assert.NotNull(manifest.GetById(999));
        Assert.Null(manifest.GetById(2)); // gap
        Assert.Null(manifest.GetById(51)); // gap
    }

    // ─── last-dot semantics tests ─────────────────────────────────────────────

    [Fact]
    public void ParseText_FilenameDotInMiddleOfName_LastDotUsed()
    {
        // The LAST '.' in the line is the extension delimiter.
        // spec: §10.3 step 2 — "locate the LAST '.' in the line": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("0007item.v2.dds\r\n");

        // The extension is ".dds" (last dot), name is "0007item.v2".
        // tex_id = 7 (leading digits of "0007item.v2" before the first non-digit char).
        Assert.Equal(1, manifest.Count);
        Assert.Equal(7, manifest.Entries[0].TexId);
        Assert.Equal("data/item/texture/0007item.v2.dds", manifest.Entries[0].VfsPath);
    }

    // ─── large-integer tex_id tests ──────────────────────────────────────────

    [Fact]
    public void ParseText_LargeTexId_Parsed()
    {
        // The real file likely has IDs up to 1,335 (spec §10.2 says 1,000+ entries).
        // spec: Docs/RE/formats/ui_manifests.md §10.2 — "approximately 1,000+ entries": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("1335bigicon.dds\r\n");

        Assert.Equal(1335, manifest.Entries[0].TexId);
    }

    // ─── CP949 encoding tests ─────────────────────────────────────────────────

    [Fact]
    public void Parse_RawBytes_CP949_Decoded()
    {
        // The file is CP949 encoded; a pure-ASCII filename round-trips unchanged.
        // spec: Docs/RE/formats/ui_manifests.md §Identification — "CP949 encoding": PARSER-CONFIRMED.
        byte[] raw = EncodeCP949("0001sword.dds\r\n");
        TextureListManifest manifest = TextureListParser.Parse(raw.AsSpan());

        Assert.Equal(1, manifest.Count);
        Assert.Equal(1, manifest.Entries[0].TexId);
        Assert.Equal("data/item/texture/0001sword.dds", manifest.Entries[0].VfsPath);
    }

    [Fact]
    public void Parse_Memory_Overload_MatchesSpan_Overload()
    {
        // Both Parse overloads must produce structurally identical results.
        byte[] raw = EncodeCP949("0001sword.dds\r\n0002shield.dds\r\n");

        TextureListManifest fromSpan = TextureListParser.Parse(raw.AsSpan());
        TextureListManifest fromMemory = TextureListParser.Parse(new ReadOnlyMemory<byte>(raw));

        Assert.Equal(fromSpan.Count, fromMemory.Count);
        Assert.Equal(fromSpan.Entries[0].TexId, fromMemory.Entries[0].TexId);
        Assert.Equal(fromSpan.Entries[0].VfsPath, fromMemory.Entries[0].VfsPath);
    }

    // ─── atol() semantics tests ───────────────────────────────────────────────

    [Fact]
    public void ParseText_AtolSemantics_StopsAtFirstNonDigit()
    {
        // atol("0042abc") → 42 (stops at 'a').
        // spec: §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
        TextureListManifest manifest = TextureListParser.ParseText("0042abc_item.dds\r\n");

        Assert.Equal(42, manifest.Entries[0].TexId);
    }

    [Fact]
    public void ParseText_AtolSemantics_AllDigits_WholeNameIsId()
    {
        // When name is all digits (no suffix text), atol returns the whole value.
        TextureListManifest manifest = TextureListParser.ParseText("9999.dds\r\n");

        Assert.Equal(9999, manifest.Entries[0].TexId);
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
    public void Smoke_TextureListTxt_ParsesAtLeast1000Entries()
    {
        // spec: §10.2 — "approximately 1,000+ entries at average 18–20 bytes per line": CODE-CONFIRMED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/item/texturelist.txt");

        TextureListManifest manifest = TextureListParser.Parse(data);

        // File must parse without throwing.
        // We expect at least 1,000 entries (spec says 1,000+ / ~1,335 in the real file).
        Assert.True(manifest.Count >= 1000,
            $"Expected at least 1000 entries, got {manifest.Count}.");
    }

    [Fact]
    public void Smoke_TextureListTxt_AllEntriesHaveDataItemTexturePath()
    {
        // Every VFS path must start with "data/item/texture/".
        // spec: §10.3 step 5 — "data/item/texture/ + name + ext": CODE-CONFIRMED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/item/texturelist.txt");

        TextureListManifest manifest = TextureListParser.Parse(data);

        Assert.All(manifest.Entries, e =>
            Assert.StartsWith("data/item/texture/", e.VfsPath));
    }

    [Fact]
    public void Smoke_TextureListTxt_AllEntriesHaveNonNegativeTexId()
    {
        // tex_id is always non-negative (atol on decimal prefix).
        // spec: §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/item/texturelist.txt");

        TextureListManifest manifest = TextureListParser.Parse(data);

        Assert.All(manifest.Entries, e =>
            Assert.True(e.TexId >= 0, $"tex_id {e.TexId} is negative."));
    }

    [Fact]
    public void Smoke_TextureListTxt_GetById_ReturnsNonNullForFirstEntry()
    {
        // The first parsed entry must be retrievable by its tex_id.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsFilePath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/item/texturelist.txt");

        TextureListManifest manifest = TextureListParser.Parse(data);

        Assert.True(manifest.Count > 0);
        int firstId = manifest.Entries[0].TexId;
        var found = manifest.GetById(firstId);
        Assert.NotNull(found);
        Assert.Equal(firstId, found!.TexId);
    }
}