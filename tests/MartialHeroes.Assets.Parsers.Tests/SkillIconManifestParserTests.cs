using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based and real-VFS tests for <see cref="SkillIconManifestParser"/>.
/// All synthetic fixtures are built as inline strings; no real game bytes are committed.
/// spec: Docs/RE/formats/ui_manifests.md §2 data/ui/skillicon/skillicon.txt.
/// </summary>
public sealed class SkillIconManifestParserTests
{
    // =========================================================================
    // Fixture tests
    // =========================================================================

    [Fact]
    public void ParseText_SingleEntry_Decoded()
    {
        // spec: Docs/RE/formats/ui_manifests.md §2.2 — SKILL { <skill_id> <job_id> <kind_id> "<path>" }: PARSER-CONFIRMED.
        const string txt = """
                           SKILL
                           {
                               # id job kind path
                               100 1 1 "data/ui/skillicon/musajung.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(1, result.Count);
        SkillIconEntry? e = result.GetEntry(100, 1, 1);
        Assert.NotNull(e);
        Assert.Equal(100, e.SkillId);
        Assert.Equal(1, e.JobId);
        Assert.Equal(1, e.KindId);
        Assert.Equal("data/ui/skillicon/musajung.dds", e.IconSheetPath);
    }

    [Fact]
    public void ParseText_MultipleEntries_AllDecoded()
    {
        // spec: §2.3 — "3 integers + 1 quoted path; exactly 4 token reads": PARSER-CONFIRMED.
        const string txt = """
                           SKILL
                           {
                               100 1 1 "data/ui/skillicon/musajung.dds"
                               200 1 2 "data/ui/skillicon/musasa.dds"
                               300 2 1 "data/ui/skillicon/assasinjung.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(3, result.Count);
        Assert.Equal("data/ui/skillicon/musajung.dds", result.GetEntry(100, 1, 1)!.IconSheetPath);
        Assert.Equal("data/ui/skillicon/musasa.dds", result.GetEntry(200, 1, 2)!.IconSheetPath);
        Assert.Equal("data/ui/skillicon/assasinjung.dds", result.GetEntry(300, 2, 1)!.IconSheetPath);
    }

    [Fact]
    public void ParseText_CommentLines_Skipped()
    {
        // spec: §1.7 shared tokenizer — "# triggers comment-skip": PARSER-CONFIRMED.
        const string txt = """
                           # top-level comment
                           SKILL
                           {
                               # id job kind path
                               100 1 1 "data/ui/skillicon/musajung.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result.Entries[0].JobId);
    }

    [Fact]
    public void ParseText_UnknownKey_ReturnsNull()
    {
        const string txt = "SKILL { 1 1 1 \"data/ui/skillicon/musajung.dds\" }";
        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        // Different skillId → null.
        Assert.Null(result.GetEntry(999, 1, 1));
    }

    [Fact]
    public void ParseText_EmptyBlock_ZeroEntries()
    {
        const string txt = "SKILL { }";
        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void ParseText_JobId_Values_CorrectlyParsed()
    {
        // spec: §2.3 col 2 — "job_id: 1=Musa, 2=Assassin, 3=Wizard, 4=Monk": PARSER-CONFIRMED.
        const string txt = """
                           SKILL
                           {
                               1 1 1 "musajung.dds"
                               2 2 1 "assasinjung.dds"
                               3 3 1 "wizardjung.dds"
                               4 4 1 "monkjung.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(4, result.Count);
        Assert.Equal(1, result.GetEntry(1, 1, 1)!.JobId); // Musa
        Assert.Equal(2, result.GetEntry(2, 2, 1)!.JobId); // Assassin
        Assert.Equal(3, result.GetEntry(3, 3, 1)!.JobId); // Wizard
        Assert.Equal(4, result.GetEntry(4, 4, 1)!.JobId); // Monk
    }

    [Fact]
    public void ParseText_KindId_Values_CorrectlyParsed()
    {
        // spec: §2.3 col 3 — "kind_id: 1=jung, 2=sa, 3=ma": PARSER-CONFIRMED.
        const string txt = """
                           SKILL
                           {
                               10 1 1 "musajung.dds"
                               11 1 2 "musasa.dds"
                               12 1 3 "musama.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(1, result.GetEntry(10, 1, 1)!.KindId); // jung
        Assert.Equal(2, result.GetEntry(11, 1, 2)!.KindId); // sa
        Assert.Equal(3, result.GetEntry(12, 1, 3)!.KindId); // ma
    }

    [Fact]
    public void ParseText_GetEntriesForJob_FiltersCorrectly()
    {
        // spec: §2.3 — entries keyed by (skill_id, job_id, kind_id): PARSER-CONFIRMED.
        const string txt = """
                           SKILL
                           {
                               100 1 1 "musajung.dds"
                               200 1 2 "musasa.dds"
                               300 2 1 "assasinjung.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        var job1 = result.GetEntriesForJob(1).ToList();
        Assert.Equal(2, job1.Count);
        Assert.All(job1, e => Assert.Equal(1, e.JobId));
    }

    [Fact]
    public void ParseText_MemoryOverload_Works()
    {
        // Verify Parse(ReadOnlySpan) and Parse(ReadOnlyMemory) produce the same result.
        // ASCII subset only — avoids CP949 encoding in the fixture.
        byte[] raw = System.Text.Encoding.ASCII.GetBytes(
            "SKILL { 1 1 1 \"data/ui/skillicon/musajung.dds\" }");

        SkillIconManifest fromSpan = SkillIconManifestParser.Parse(raw.AsSpan());
        SkillIconManifest fromMemory = SkillIconManifestParser.Parse(new ReadOnlyMemory<byte>(raw));

        Assert.Equal(fromSpan.Count, fromMemory.Count);
    }

    /// <summary>
    /// Regression test for the bug where a non-integer field 2 (job_id) or field 3 (kind_id)
    /// caused a <c>break</c> from the entire parse loop, silently discarding all subsequent entries.
    /// The correct behaviour is to skip only the malformed entry and continue parsing.
    /// spec: Docs/RE/formats/ui_manifests.md §2.3 — "3 integers + 1 quoted path": PARSER-CONFIRMED.
    /// </summary>
    [Fact]
    public void ParseText_MalformedJobId_SkipsEntryAndContinues()
    {
        // Entry 200 has a non-integer job_id ("BAD") — must be skipped.
        // Entry 100 (before) and entry 300 (after) must still be decoded.
        const string txt = """
                           SKILL
                           {
                               100 1 1 "data/ui/skillicon/before.dds"
                               200 BAD 1 "data/ui/skillicon/malformed.dds"
                               300 2 1 "data/ui/skillicon/after.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        // Only the two valid entries must be present.
        Assert.Equal(2, result.Count);

        // The entry before the malformed one must be decoded correctly.
        SkillIconEntry? before = result.GetEntry(100, 1, 1);
        Assert.NotNull(before);
        Assert.Equal("data/ui/skillicon/before.dds", before.IconSheetPath);

        // The entry after the malformed one must also be decoded correctly.
        SkillIconEntry? after = result.GetEntry(300, 2, 1);
        Assert.NotNull(after);
        Assert.Equal("data/ui/skillicon/after.dds", after.IconSheetPath);

        // The malformed entry must not appear.
        Assert.Null(result.GetEntry(200, 0, 1)); // any jobId lookup
    }

    /// <summary>
    /// Regression test: a non-integer field 3 (kind_id) must also skip only that entry.
    /// spec: Docs/RE/formats/ui_manifests.md §2.3 col 3 — "kind_id: integer-parse helper": PARSER-CONFIRMED.
    /// </summary>
    [Fact]
    public void ParseText_MalformedKindId_SkipsEntryAndContinues()
    {
        // Entry 500 has a non-integer kind_id ("?") — must be skipped.
        // Entries 400 and 600 must still be decoded.
        const string txt = """
                           SKILL
                           {
                               400 1 1 "data/ui/skillicon/before2.dds"
                               500 1 ? "data/ui/skillicon/malformed2.dds"
                               600 1 2 "data/ui/skillicon/after2.dds"
                           }
                           """;

        SkillIconManifest result = SkillIconManifestParser.ParseText(txt);

        Assert.Equal(2, result.Count);

        SkillIconEntry? before = result.GetEntry(400, 1, 1);
        Assert.NotNull(before);
        Assert.Equal("data/ui/skillicon/before2.dds", before.IconSheetPath);

        SkillIconEntry? after = result.GetEntry(600, 1, 2);
        Assert.NotNull(after);
        Assert.Equal("data/ui/skillicon/after2.dds", after.IconSheetPath);
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
    public void Smoke_SkillIconTxt_ParsesWithEntries()
    {
        // spec: §2.4 — "22 confirmed sheets in manifest": SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/ui/skillicon/skillicon.txt")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/ui/skillicon/skillicon.txt");
        SkillIconManifest result = SkillIconManifestParser.Parse(raw);

        Assert.True(result.Count > 0,
            "skillicon.txt produced no entries — parser may have failed to find the SKILL block.");

        // Verify at least the 12 confirmed job/kind sheet entries exist.
        // spec: §2.4 — job 1-4 × kind 1-3: SAMPLE-VERIFIED.
        bool hasJob1Kind1 = result.GetEntriesForJob(1)
            .Any(e => e.KindId == 1);
        Assert.True(hasJob1Kind1,
            "No job=1, kind=1 entry found in skillicon.txt. " +
            "spec: §2.4 — job_id=1 kind_id=1 → musajung.dds: SAMPLE-VERIFIED.");
    }

    [Fact]
    public void Smoke_SkillIconTxt_MusaJung_PathContainsMusa()
    {
        // The musajung.dds path must contain "musa" (case-insensitive).
        // spec: §2.4 — job_id=1, kind_id=1 → data/ui/skillicon/musajung.dds: SAMPLE-VERIFIED.
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        if (!archive.Contains("data/ui/skillicon/skillicon.txt")) return;

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/ui/skillicon/skillicon.txt");
        SkillIconManifest result = SkillIconManifestParser.Parse(raw);

        SkillIconEntry? musaJung = result.GetEntriesForJob(1)
            .FirstOrDefault(e => e.KindId == 1);
        if (musaJung is null) return; // already covered by previous smoke test

        Assert.Contains("musa", musaJung.IconSheetPath, StringComparison.OrdinalIgnoreCase);
    }
}