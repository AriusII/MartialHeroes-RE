using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="ChatFilterParser"/>.
/// The chatfilter files (curse.txt / cursechat.txt) are ABSENT from the real VFS, so all tests
/// operate on SYNTHETIC bytes only — no real client file is needed or expected.
/// spec: Docs/RE/formats/text_tables.md §3.1 chat-filter tables — HIGH.
/// </summary>
public sealed class ChatFilterParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    /// <summary>
    /// Encodes a string as CP949 bytes for feeding to <see cref="ChatFilterParser.Parse(ReadOnlyMemory{byte})"/>.
    /// spec: Docs/RE/formats/text_tables.md §3.1 — "Encoding: CP949": HIGH.
    /// </summary>
    private static ReadOnlyMemory<byte> AsCp949(string text) =>
        new ReadOnlyMemory<byte>(Cp949().GetBytes(text));

    // =========================================================================
    // 1. ParseText (string overload) — baseline behaviour
    // =========================================================================

    [Fact]
    public void ParseText_EmptyString_YieldsNoEntries()
    {
        // An empty file has no data rows.
        // spec: Docs/RE/formats/text_tables.md §3.1 — comment lines and blank lines are skipped.
        ChatFilterEntry[] result = ChatFilterParser.ParseText("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseText_OnlyCommentLines_YieldsNoEntries()
    {
        // Comment lines (';'-prefixed) are skipped; no data rows.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "';'-prefixed comment preamble": HIGH.
        string text = ";version 1.0\r\n;author tools\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseText_OneDataRow_DecodesCorrectly()
    {
        // One TAB-delimited row: col0 = bad word, col1 = replacement.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "col0: bad word, col1: replacement": HIGH.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "Delimiter: TAB": HIGH.
        string text = "badword\tclean\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Single(result);
        Assert.Equal("badword", result[0].BadWord);
        Assert.Equal("clean", result[0].Replacement);
    }

    [Fact]
    public void ParseText_MultipleDataRows_AllDecoded()
    {
        // Multiple rows: each row yields one entry in order.
        // spec: Docs/RE/formats/text_tables.md §3.1 — schema HIGH.
        string text = "wordA\trepA\r\nwordB\trepB\r\nwordC\trepC\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Equal(3, result.Length);
        Assert.Equal("wordA", result[0].BadWord);
        Assert.Equal("repA", result[0].Replacement);
        Assert.Equal("wordC", result[2].BadWord);
        Assert.Equal("repC", result[2].Replacement);
    }

    [Fact]
    public void ParseText_CommentPreambleThenData_PreambleSkipped()
    {
        // Preamble comments are skipped; data rows follow.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "header: ';'-prefixed comment preamble … then data rows": HIGH.
        string text = ";comment\r\nfoo\tbar\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Single(result);
        Assert.Equal("foo", result[0].BadWord);
        Assert.Equal("bar", result[0].Replacement);
    }

    [Fact]
    public void ParseText_BlankLinesSkipped()
    {
        // Blank lines between rows must not produce empty entries.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "blank lines are skipped".
        string text = "\r\nfoo\tbar\r\n\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Single(result);
        Assert.Equal("foo", result[0].BadWord);
    }

    [Fact]
    public void ParseText_RowWithFewerThanTwoColumns_Skipped()
    {
        // Rows with < 2 TAB-separated columns are skipped gracefully.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "two columns per data row": HIGH.
        string text = "onlyone\r\nfoo\tbar\r\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Single(result);
        Assert.Equal("foo", result[0].BadWord);
    }

    // =========================================================================
    // 2. Byte overload — CP949 round-trip
    // =========================================================================

    [Fact]
    public void Parse_Bytes_AsciiContent_RoundTrips()
    {
        // ASCII content round-trips through CP949 (ASCII is a subset of CP949).
        // spec: Docs/RE/formats/text_tables.md §3.1 — "Encoding: CP949": HIGH.
        ReadOnlyMemory<byte> data = AsCp949("alpha\tbeta\r\n");
        ChatFilterEntry[] result = ChatFilterParser.Parse(data);

        Assert.Single(result);
        Assert.Equal("alpha", result[0].BadWord);
        Assert.Equal("beta", result[0].Replacement);
    }

    [Fact]
    public void Parse_Bytes_EmptyBuffer_YieldsNoEntries()
    {
        // Empty byte buffer produces no entries.
        ChatFilterEntry[] result = ChatFilterParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_Bytes_CommentOnlyBuffer_YieldsNoEntries()
    {
        // A comment-only CP949 buffer yields no data entries.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "';'-prefixed comment preamble": HIGH.
        ReadOnlyMemory<byte> data = AsCp949(";no data here\r\n");
        ChatFilterEntry[] result = ChatFilterParser.Parse(data);
        Assert.Empty(result);
    }

    // =========================================================================
    // 3. LF-only line endings (graceful)
    // =========================================================================

    [Fact]
    public void ParseText_LfOnlyLineEndings_DataDecoded()
    {
        // CRLF is canonical but LF-only must also work (graceful strip of '\r').
        // spec: Docs/RE/formats/text_tables.md §3.1 — "CRLF line endings": HIGH (canonical); LF accepted.
        string text = "bad\tgood\n";
        ChatFilterEntry[] result = ChatFilterParser.ParseText(text);

        Assert.Single(result);
        Assert.Equal("bad", result[0].BadWord);
        Assert.Equal("good", result[0].Replacement);
    }
}
