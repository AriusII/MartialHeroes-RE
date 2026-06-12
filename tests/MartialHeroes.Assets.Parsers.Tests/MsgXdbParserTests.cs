using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based and real-VFS tests for <see cref="MsgXdbParser"/> and <see cref="UiTexManifestParser"/>
/// and <see cref="SkillIconManifestParser"/>.
///
/// Fixture tests use hand-built in-memory bytes matching the spec; no real game bytes are committed.
/// Real-VFS smoke tests are gated on clientdata presence and skipped when absent.
///
/// spec: Docs/RE/formats/misc_data.md §6 msg.xdb (CODE-CONFIRMED; SAMPLE-UNVERIFIED).
/// spec: Docs/RE/formats/ui_manifests.md §1 UiTex.txt (PARSER-CONFIRMED; SAMPLE-VERIFIED).
/// spec: Docs/RE/formats/ui_manifests.md §2 skillicon.txt (PARSER-CONFIRMED; SAMPLE-VERIFIED).
/// </summary>
public sealed class MsgXdbParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    // Record stride: 4 (id) + 512 (text buffer) = 516 bytes.
    // spec: Docs/RE/formats/misc_data.md §6 — "Total record size: 4 + 512 = 516": CODE-CONFIRMED.
    private const int Stride = 516;

    /// <summary>
    /// Builds a synthetic msg.xdb buffer with the given (id, text) pairs.
    /// Encodes text as CP949. Pads the text buffer to 512 bytes with NULs.
    /// </summary>
    private static byte[] BuildMsgXdb(params (uint Id, string Text)[] records)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var buf = new byte[records.Length * Stride];
        for (int i = 0; i < records.Length; i++)
        {
            int baseOff = i * Stride;
            // id u32LE @ record+0x000. spec: §6 — "id u32LE @ 0x000: CODE-CONFIRMED".
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(baseOff, 4), records[i].Id);

            // text u8[512] CP949 NUL-terminated @ record+0x004. spec: §6 — "text u8[512]: CODE-CONFIRMED".
            byte[] encoded = cp949.GetBytes(records[i].Text);
            int copyLen = Math.Min(encoded.Length, 511); // leave room for NUL
            encoded.AsSpan(0, copyLen).CopyTo(buf.AsSpan(baseOff + 4));
            // rest is already zero (NUL-padding)
        }

        return buf;
    }

    // =========================================================================
    // MsgXdbParser — unit tests
    // =========================================================================

    [Fact]
    public void Parse_SingleRecord_ReturnsCorrectIdAndText()
    {
        // spec: Docs/RE/formats/misc_data.md §6 — record: u32LE id + u8[512] CP949 text: CODE-CONFIRMED.
        byte[] data = BuildMsgXdb((42u, "Hello"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello", result.GetText(42u));
    }

    [Fact]
    public void Parse_MultipleRecords_AllDecoded()
    {
        // Three synthetic records; verify count and individual lookups.
        // spec: §6 — "record_count = file_size / 516": CODE-CONFIRMED.
        byte[] data = BuildMsgXdb(
            (1u, "First"),
            (1000u, "Second"),
            (9999u, "Third"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(3, result.Count);
        Assert.Equal("First", result.GetText(1u));
        Assert.Equal("Second", result.GetText(1000u));
        Assert.Equal("Third", result.GetText(9999u));
    }

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyCatalog()
    {
        // Zero-byte buffer → zero records (0 % 516 == 0 is valid).
        // spec: §6 — "record_count = file_size / 516; any remainder implies malformed file": CODE-CONFIRMED.
        MsgXdbCatalog result = MsgXdbParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Parse_NonMultipleOfStride_ThrowsInvalidData()
    {
        // 515 is not a multiple of 516 → must throw.
        // spec: §6 — "any remainder implies malformed file": CODE-CONFIRMED.
        byte[] bad = new byte[515];
        Assert.Throws<InvalidDataException>(() => MsgXdbParser.Parse(bad.AsSpan()));
    }

    [Fact]
    public void Parse_NulTerminatedText_StripsPaddingBytes()
    {
        // The text buffer is 512 bytes; content before the NUL is the string; the rest is ignored.
        // spec: §6 — "remaining bytes after the NUL are zero padding and must be ignored": CODE-CONFIRMED.
        byte[] data = BuildMsgXdb((7u, "Short"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        // The decoded string must equal "Short" — not "Short\0\0\0…".
        Assert.Equal("Short", result.GetText(7u));
    }

    [Fact]
    public void Parse_UnknownId_ReturnsNull()
    {
        byte[] data = BuildMsgXdb((1u, "text"));
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Null(result.GetText(999u));
    }

    [Fact]
    public void Parse_RecordsProperty_HasCorrectCount()
    {
        byte[] data = BuildMsgXdb((10u, "a"), (20u, "b"));
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(2, result.Records.Count);
    }

    [Fact]
    public void Parse_MemoryOverload_Works()
    {
        // Verify both overloads (Span and Memory) produce identical results.
        byte[] data = BuildMsgXdb((5u, "test"));
        MsgXdbCatalog fromSpan = MsgXdbParser.Parse(data.AsSpan());
        MsgXdbCatalog fromMemory = MsgXdbParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(fromSpan.Count, fromMemory.Count);
        Assert.Equal(fromSpan.GetText(5u), fromMemory.GetText(5u));
    }

    [Fact]
    public void Parse_Stride516_ExactlyTwoRecords_1032Bytes()
    {
        // Confirm that 1032 bytes (= 2 × 516) yields exactly 2 records.
        // spec: §6 — stride 516: CODE-CONFIRMED.
        byte[] data = new byte[1032]; // all zeros → two records with id=0, text=""
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(2, result.Count);
    }

    // ─── VFS smoke tests ─────────────────────────────────────────────────────

    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsPath);

    /// <summary>
    /// Verifies the real msg.xdb:
    /// (a) its size is an exact multiple of 516 (layout conformance),
    /// (b) at least one record is present,
    /// (c) IDs around 9001 exist (CODE-CONFIRMED call-site grouping in spec §6).
    ///
    /// NOTE: this test is the primary SAMPLE-VERIFICATION for msg.xdb.
    /// If the 516-byte layout is incorrect the size-modulo check will FAIL,
    /// which must be reported as a spec discrepancy — do NOT adjust the stride.
    /// spec: Docs/RE/formats/misc_data.md §6 — SAMPLE-UNVERIFIED: file_size % 516 == 0,
    ///       record count, and ID groupings are verified here for the first time.
    /// </summary>
    [Fact]
    public void Smoke_MsgXdb_Layout516_And_KnownIds()
    {
        if (!ClientDataAvailable()) return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);

        // Verify the file exists in the VFS.
        Assert.True(archive.Contains("data/script/msg.xdb"),
            "data/script/msg.xdb not found in VFS — cannot verify layout.");

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/msg.xdb");

        // (a) Size must be an exact multiple of 516.
        // spec: §6 — "file must be an exact multiple of 516": CODE-CONFIRMED.
        // If this assertion fails: the 516-byte stride is wrong for this VFS version.
        // Report as spec discrepancy rather than adjusting.
        Assert.True(
            raw.Length % 516 == 0,
            $"msg.xdb size {raw.Length} is not a multiple of 516 — spec stride mismatch. " +
            "Report to asset-format-doc / spec-author for stride re-evaluation.");

        int expectedCount = raw.Length / 516;
        Assert.True(expectedCount > 0,
            "msg.xdb is empty — expected at least one record.");

        // (b) Parse succeeds.
        MsgXdbCatalog catalog = MsgXdbParser.Parse(raw);
        Assert.Equal(expectedCount, catalog.Count);

        // (c) IDs around 9001 exist.
        // spec: §6 Known ID ranges — "9001 + stateIndex: scene/state name strings: CODE-CONFIRMED".
        // The exact IDs that exist depend on the actual file; we check at least one in [9001..9050].
        bool foundAround9001 = false;
        for (uint id = 9001; id <= 9050; id++)
        {
            if (catalog.GetText(id) is not null)
            {
                foundAround9001 = true;
                break;
            }
        }

        Assert.True(foundAround9001,
            "No IDs in range 9001–9050 found in msg.xdb. " +
            "spec: §6 — '9001 + stateIndex: scene/state name strings: CODE-CONFIRMED'. " +
            "If the range is absent in this VFS version, update the spec.");
    }
}