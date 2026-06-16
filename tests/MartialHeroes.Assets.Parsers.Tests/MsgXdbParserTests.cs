using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based and real-VFS tests for <see cref="MsgXdbParser"/> / <see cref="MsgXdbCatalog"/>.
///
/// Fixture tests use hand-built in-memory bytes that exactly match the spec layout — no real
/// game bytes are committed (the client's <c>msg.xdb</c> payload is user-supplied, gitignored).
/// Real-VFS smoke tests are gated on client-data presence and silently skipped when absent.
///
/// spec: Docs/RE/formats/msg_xdb.md — flat headerless 516-byte records (i32 LE caption_id + char[512] CP949).
/// </summary>
public sealed class MsgXdbParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    // Record stride: 4 (caption_id) + 512 (text buffer) = 516 bytes (0x204).
    // spec: Docs/RE/formats/msg_xdb.md §File layout — "Record stride: 516 bytes (0x204)". CONFIRMED.
    private const int Stride = 516;

    /// <summary>
    /// Builds a synthetic <c>msg.xdb</c> payload with the given (captionId, text) pairs.
    /// Encodes text as CP949 and pads the 512-byte text buffer with NUL bytes.
    /// </summary>
    private static byte[] BuildMsgXdb(params (int CaptionId, string Text)[] records)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);

        byte[] buf = new byte[records.Length * Stride];
        for (int i = 0; i < records.Length; i++)
        {
            int baseOff = i * Stride;

            // caption_id i32 LE @ record+0x000.
            // spec: Docs/RE/formats/msg_xdb.md §Record layout — "caption_id i32 LE @ +0x000". CONFIRMED.
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(baseOff, 4), records[i].CaptionId);

            // text char[512] CP949 NUL-terminated @ record+0x004.
            // spec: Docs/RE/formats/msg_xdb.md §Record layout — "text char[512] CP949 @ +0x004". CONFIRMED.
            byte[] encoded = cp949.GetBytes(records[i].Text);
            int copyLen = Math.Min(encoded.Length, 511); // leave room for NUL
            encoded.AsSpan(0, copyLen).CopyTo(buf.AsSpan(baseOff + 4));
            // remaining bytes are already NUL (zero-initialised)
        }

        return buf;
    }

    // =========================================================================
    // Stride and record-count formula
    // =========================================================================

    [Fact]
    public void Parse_Stride516_TwoRecords_Exactly1032Bytes()
    {
        // 2 × 516 = 1032 → exactly 2 records.
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "record_count = file_size / 516". CONFIRMED.
        byte[] data = new byte[1032]; // all-zero → two records with captionId=0, text=""
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Parse_NonMultipleOfStride_ThrowsInvalidData()
    {
        // 515 bytes is not a multiple of 516 → must throw.
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "any remainder implies a malformed file". CONFIRMED.
        byte[] bad = new byte[515];
        Assert.Throws<InvalidDataException>(() => MsgXdbParser.Parse(bad.AsSpan()));
    }

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyCatalog()
    {
        // 0 bytes → 0 records (0 % 516 == 0, valid empty table).
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "record_count = file_size / 516". CONFIRMED.
        MsgXdbCatalog result = MsgXdbParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Equal(0, result.Count);
    }

    // =========================================================================
    // i32 caption_id parse
    // =========================================================================

    [Fact]
    public void Parse_SingleRecord_ReturnsCorrectCaptionIdAndText()
    {
        // spec: Docs/RE/formats/msg_xdb.md §Record layout — caption_id i32 LE @ +0x000. CONFIRMED.
        byte[] data = BuildMsgXdb((42, "Hello"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(1, result.Count);
        Assert.Equal("Hello", result.GetText(42));
    }

    [Fact]
    public void Parse_MultipleRecords_AllDecoded()
    {
        // Three synthetic records; verify count and individual lookups.
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "record_count = file_size / 516". CONFIRMED.
        byte[] data = BuildMsgXdb(
            (1, "First"),
            (1000, "Second"),
            (9999, "Third"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(3, result.Count);
        Assert.Equal("First", result.GetText(1));
        Assert.Equal("Second", result.GetText(1000));
        Assert.Equal("Third", result.GetText(9999));
    }

    [Fact]
    public void Parse_RecordsProperty_HasCorrectCount()
    {
        byte[] data = BuildMsgXdb((10, "a"), (20, "b"));
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal(2, result.Records.Count);
    }

    // =========================================================================
    // CP949 decode + NUL trim
    // =========================================================================

    [Fact]
    public void Parse_NulTerminatedText_StripsPaddingBytes()
    {
        // Text buffer is 512 bytes; content before the NUL is the string; padding is ignored.
        // spec: Docs/RE/formats/msg_xdb.md §Record layout —
        //   "trailing bytes after the terminator are unused (padding)". CONFIRMED.
        byte[] data = BuildMsgXdb((7, "Short"));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal("Short", result.GetText(7));
    }

    [Fact]
    public void Parse_EmptyString_RecordTextIsEmpty()
    {
        // An all-zero text buffer is a valid empty string.
        // spec: Docs/RE/formats/msg_xdb.md §Record layout — "An empty string is valid". CONFIRMED.
        byte[] data = BuildMsgXdb((4022, ""));

        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Equal("", result.GetText(4022));
    }

    // =========================================================================
    // Lookup — hit / miss / TryGet
    // =========================================================================

    [Fact]
    public void GetText_UnknownId_ReturnsNull()
    {
        // spec: Docs/RE/formats/msg_xdb.md §Lookup model — "Caption IDs are sparse". CONFIRMED.
        byte[] data = BuildMsgXdb((1, "text"));
        MsgXdbCatalog result = MsgXdbParser.Parse(data.AsSpan());

        Assert.Null(result.GetText(999));
    }

    [Fact]
    public void TryGet_KnownId_ReturnsTrueAndText()
    {
        // TryGet should surface the same text as GetText for a known id.
        byte[] data = BuildMsgXdb((4023, "Connecting to server..."));

        MsgXdbCatalog catalog = MsgXdbParser.Parse(data.AsSpan());

        bool found = catalog.TryGet(4023, out string? text);
        Assert.True(found);
        Assert.Equal("Connecting to server...", text);
    }

    [Fact]
    public void TryGet_UnknownId_ReturnsFalseAndNull()
    {
        // spec: Docs/RE/formats/msg_xdb.md §Lookup model — caption IDs are sparse. CONFIRMED.
        byte[] data = BuildMsgXdb((1, "text"));
        MsgXdbCatalog catalog = MsgXdbParser.Parse(data.AsSpan());

        bool found = catalog.TryGet(99999, out string? text);
        Assert.False(found);
        Assert.Null(text);
    }

    // =========================================================================
    // Both overloads (Span + Memory)
    // =========================================================================

    [Fact]
    public void Parse_MemoryOverload_IdenticalToSpanOverload()
    {
        // Verify both Parse(ReadOnlySpan<byte>) and Parse(ReadOnlyMemory<byte>) agree.
        byte[] data = BuildMsgXdb((5, "test"));
        MsgXdbCatalog fromSpan = MsgXdbParser.Parse(data.AsSpan());
        MsgXdbCatalog fromMemory = MsgXdbParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(fromSpan.Count, fromMemory.Count);
        Assert.Equal(fromSpan.GetText(5), fromMemory.GetText(5));
    }

    // =========================================================================
    // Front-end caption IDs (from spec §Front-end caption-ID index)
    // =========================================================================

    [Fact]
    public void Parse_FrontEndCaptionIds_Resolvable()
    {
        // Verify the catalogue resolves the IDs referenced by the layer-05 front-end.
        // The text values here are synthetic ASCII stand-ins — not the real CP949 strings.
        // All 21 EULA lines (4001–4021) plus the trailing blank (4022) are included.
        // spec: Docs/RE/formats/msg_xdb.md §Front-end caption-ID index. CONFIRMED.

        // Build EULA block records (4001–4022) dynamically.
        var eulaRecords = Enumerable.Range(4001, 21) // 4001..4021
            .Select(id => (id, $"EULA line {id - 4000}"))
            .Append((4022, "")) // trailing blank
            .ToArray();

        // Combine with the non-EULA front-end IDs.
        (int, string)[] nonEula =
        [
            (2206, "Wellness advisory"),
            (4023, "Connecting to server..."),
            (4024, "Could not connect."),
            (14001, "Please enter your name"),
            (14002, "Delete character?"),
            (46001, "Enter new name"),
            (46002, "Name"),
            (48001, "Teleport to main town?"),
            (48003, "Use when stuck"),
            (48004, "Once per 30 minutes"),
            (48005, "No nearby save point"),
            (63030, "Complete tutorial first"),
        ];

        byte[] data = BuildMsgXdb([.. eulaRecords, .. nonEula]);
        MsgXdbCatalog catalog = MsgXdbParser.Parse(data.AsSpan());

        // Login / server-list scene
        Assert.NotNull(catalog.GetText(4023)); // connecting dialog
        Assert.NotNull(catalog.GetText(4024)); // failure dialog
        Assert.Equal("", catalog.GetText(4022)); // trailing blank EULA line

        // EULA block — all 21 lines must resolve.
        // spec: Docs/RE/formats/msg_xdb.md §Front-end caption-ID index — "4001–4021 EULA". CONFIRMED.
        for (int id = 4001; id <= 4021; id++)
            Assert.NotNull(catalog.GetText(id));

        // Character-select scene
        Assert.NotNull(catalog.GetText(2206));
        Assert.NotNull(catalog.GetText(14001));
        Assert.NotNull(catalog.GetText(14002));
        Assert.NotNull(catalog.GetText(46001));
        Assert.NotNull(catalog.GetText(46002));
        Assert.NotNull(catalog.GetText(48001));
        Assert.NotNull(catalog.GetText(48003));
        Assert.NotNull(catalog.GetText(48004));
        Assert.NotNull(catalog.GetText(48005));
        Assert.NotNull(catalog.GetText(63030));
    }

    // =========================================================================
    // Real-VFS smoke test (skipped when client data absent)
    // =========================================================================

    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() => File.Exists(InfPath) && File.Exists(VfsPath);

    /// <summary>
    /// Smoke test against the real <c>data/script/msg.xdb</c> from the user-supplied VFS.
    /// Verifies: (a) exact multiple of 516-byte stride, (b) at least one record, (c) the
    /// front-end caption IDs documented in the spec resolve successfully.
    ///
    /// Silently skipped when client data is absent.
    /// spec: Docs/RE/formats/msg_xdb.md — SAMPLE-VERIFIED (stride, record count ~2644).
    /// </summary>
    [Fact]
    public void Smoke_RealVfs_MsgXdb_LayoutAndFrontEndIds()
    {
        if (!ClientDataAvailable()) return;

        using MappedVfsArchive archive = MappedVfsArchive.Open(InfPath, VfsPath);
        Assert.True(archive.Contains("data/script/msg.xdb"),
            "data/script/msg.xdb not found in the VFS — cannot verify layout.");

        ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/msg.xdb");

        // (a) File size must be an exact multiple of 516.
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "record_count = file_size / 516". CONFIRMED.
        Assert.True(raw.Length % 516 == 0,
            $"msg.xdb size {raw.Length} is not a multiple of 516 — spec stride mismatch. " +
            "Report to asset-spec-author for re-evaluation before adjusting the stride constant.");

        // (b) At least one record present.
        Assert.True(raw.Length > 0, "msg.xdb is empty — expected ~2644 records.");

        // (c) Parse succeeds and count matches formula.
        MsgXdbCatalog catalog = MsgXdbParser.Parse(raw);
        Assert.Equal(raw.Length / 516, catalog.Count);

        // (d) Front-end caption IDs documented in the spec are present.
        // spec: Docs/RE/formats/msg_xdb.md §Front-end caption-ID index. CONFIRMED.
        int[] frontEndIds = [4023, 4024, 2206, 14001, 14002, 46001, 46002, 48001, 48003, 48004, 48005, 63030];
        foreach (int id in frontEndIds)
        {
            Assert.True(catalog.TryGet(id, out _),
                $"Front-end caption ID {id} not found in msg.xdb. " +
                "spec: Docs/RE/formats/msg_xdb.md §Front-end caption-ID index.");
        }

        // EULA block 4001–4021 must be present.
        for (int id = 4001; id <= 4021; id++)
        {
            Assert.True(catalog.TryGet(id, out _),
                $"EULA caption ID {id} not found in msg.xdb. " +
                "spec: Docs/RE/formats/msg_xdb.md §Front-end caption-ID index.");
        }
    }
}