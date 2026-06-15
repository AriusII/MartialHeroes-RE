using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="NpcScrParser"/> (npc.scr — NPC description-text table).
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/config_tables.md §2.17.3 npc.scr: SAMPLE-VERIFIED.
/// </summary>
public sealed class NpcScrParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    // NPC_SCR_RECORD_BYTES = 404 (0x194).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride: 404 bytes (0x194)": SAMPLE-VERIFIED.
    private const int RecordStride = 404; // 0x194

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

    /// <summary>
    /// Builds a single 404-byte npc.scr record.
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — Record layout: SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildRecord(
        uint id = 1u,
        uint idMirror = 1u,
        string para0 = "",
        string para1 = "",
        string para2 = "")
    {
        byte[] buf = new byte[RecordStride];

        // id u32LE @ 0x000. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id u32 @ 0x000: SAMPLE-VERIFIED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x000, 4), id);

        // id_mirror u32LE @ 0x004. Equals id. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id_mirror u32 @ 0x004: SAMPLE-VERIFIED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x004, 4), idMirror);

        // reserved 8 bytes @ 0x008 — zero (already zero from new byte[]). SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "reserved 8 bytes @ 0x008 (zero): SAMPLE-VERIFIED".

        // paragraph_0 CP949 @ 0x014, up to ~36 bytes. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_0 char[] @ 0x014: SAMPLE-VERIFIED".
        // Parser uses a 60-byte conservative buffer.
        if (para0.Length > 0)
            WriteCp949(buf, 0x014, 60, para0);

        // paragraph_1 CP949 @ 0x050. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_1 char[] @ 0x050: SAMPLE-VERIFIED".
        if (para1.Length > 0)
            WriteCp949(buf, 0x050, 64, para1);

        // paragraph_2 CP949 @ 0x090. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_2 char[] @ 0x090: SAMPLE-VERIFIED".
        if (para2.Length > 0)
            WriteCp949(buf, 0x090, 64, para2);

        return buf;
    }

    // =========================================================================
    // 1. Stride validation
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_YieldsEmptyArray()
    {
        // 0 % 404 == 0 — valid empty file.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "record count = file_size / 404".
        NpcScrRecord[] result = NpcScrParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NotMultipleOfStride_ThrowsInvalidDataException()
    {
        // 100 is not divisible by 404.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes (NPC_SCR_RECORD_BYTES)".
        byte[] buf = new byte[100];
        Assert.Throws<InvalidDataException>(() => NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_ExactlyOneRecord_DecodesOne()
    {
        // 404 bytes = exactly one record.
        byte[] buf = BuildRecord(id: 1u);
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(result);
    }

    // =========================================================================
    // 2. Field decoding
    // =========================================================================

    [Fact]
    public void Parse_Id_RoundTrips()
    {
        // id u32LE @ 0x000. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id u32 @ 0x000: SAMPLE-VERIFIED".
        byte[] buf = BuildRecord(id: 123u, idMirror: 123u);
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(123u, result[0].Id);
    }

    [Fact]
    public void Parse_IdMirror_RoundTrips()
    {
        // id_mirror u32LE @ 0x004. Equals id in all records. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id_mirror u32 @ 0x004: SAMPLE-VERIFIED".
        byte[] buf = BuildRecord(id: 42u, idMirror: 42u);
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(42u, result[0].IdMirror);
    }

    [Fact]
    public void Parse_Paragraph0_DecodedFromOffset0x014()
    {
        // paragraph_0 CP949 @ 0x014. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_0 char[] @ 0x014: SAMPLE-VERIFIED".
        byte[] buf = BuildRecord(id: 1u, idMirror: 1u, para0: "NpcInfo");
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("NpcInfo", result[0].Paragraph0);
    }

    [Fact]
    public void Parse_Paragraph1_DecodedFromOffset0x050()
    {
        // paragraph_1 CP949 @ 0x050. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_1 char[] @ 0x050: SAMPLE-VERIFIED".
        byte[] buf = BuildRecord(id: 1u, idMirror: 1u, para1: "Desc1");
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("Desc1", result[0].Paragraph1);
    }

    [Fact]
    public void Parse_Paragraph2_DecodedFromOffset0x090()
    {
        // paragraph_2 CP949 @ 0x090. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_2 char[] @ 0x090: SAMPLE-VERIFIED".
        byte[] buf = BuildRecord(id: 1u, idMirror: 1u, para2: "Desc2");
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("Desc2", result[0].Paragraph2);
    }

    [Fact]
    public void Parse_EmptyParagraphs_YieldEmptyStrings()
    {
        // All paragraph buffers zero-initialized → empty strings.
        byte[] buf = BuildRecord(id: 1u, idMirror: 1u);
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(string.Empty, result[0].Paragraph0);
        Assert.Equal(string.Empty, result[0].Paragraph1);
        Assert.Equal(string.Empty, result[0].Paragraph2);
    }

    // =========================================================================
    // 3. Raw slice
    // =========================================================================

    [Fact]
    public void Parse_RawSlice_HasStrideLength()
    {
        // Raw slice must be exactly 404 bytes (the full record).
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes (0x194)": SAMPLE-VERIFIED.
        byte[] buf = BuildRecord(id: 5u, idMirror: 5u);
        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(404, result[0].Raw.Length);
    }

    // =========================================================================
    // 4. Multi-record walk
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_IndependentDecoding()
    {
        // Two records must decode without cross-record bleed.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — stride 404 bytes.
        byte[] r0 = BuildRecord(id: 1u, idMirror: 1u, para0: "Alpha");
        byte[] r1 = BuildRecord(id: 2u, idMirror: 2u, para0: "Beta");
        byte[] buf = new byte[r0.Length + r1.Length];
        r0.CopyTo(buf, 0);
        r1.CopyTo(buf, r0.Length);

        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal(1u, result[0].Id);
        Assert.Equal("Alpha", result[0].Paragraph0);
        Assert.Equal(2u, result[1].Id);
        Assert.Equal("Beta", result[1].Paragraph0);
    }

    // =========================================================================
    // 5. MSVC 0xCC sentinel — treated as padding (stop before it)
    // =========================================================================

    [Fact]
    public void Parse_Paragraph_WithCcSentinel_TruncatedAtSentinel()
    {
        // 0xCC is the MSVC debug-stack fill used as padding after NUL in some records.
        // The parser must stop reading text AT the first 0xCC byte (before NUL too).
        // spec: Docs/RE/formats/config_tables.md §2.1 — "0xCC MSVC debug-stack sentinel in unused bytes": CONFIRMED.
        byte[] buf = BuildRecord(id: 1u, idMirror: 1u);
        // Write "Hi" + 0xCC at offset 0x014 (paragraph_0 start) manually.
        buf[0x014] = (byte)'H';
        buf[0x015] = (byte)'i';
        buf[0x016] = 0xCC; // MSVC sentinel — stops text here

        NpcScrRecord[] result = NpcScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("Hi", result[0].Paragraph0);
    }
}
