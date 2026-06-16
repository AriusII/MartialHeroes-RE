using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/cursor/curse.txt</c> and <c>data/cursor/cursechat.txt</c> —
/// the CP949 chat-filter substitution tables.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/text_tables.md §3.1 chat-filter tables — HIGH.
/// <para>
/// Format: TAB-delimited, two columns per data row, CRLF line endings, CP949 encoding.
/// Header: a <c>;</c>-prefixed comment preamble (revision / identification comments),
/// followed by headerless data rows.
/// spec: Docs/RE/formats/text_tables.md §3.1 — "Delimiter: TAB; CRLF; ';'-prefixed comment preamble": HIGH.
/// </para>
/// <para>
/// Column layout (0-based):
/// <list type="bullet">
/// <item><term>col0</term><description>bad word to filter (CP949). HIGH.</description></item>
/// <item><term>col1</term><description>clean replacement to substitute (CP949). HIGH.</description></item>
/// </list>
/// spec: Docs/RE/formats/text_tables.md §3.1 column table.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class ChatFilterParser
{
    // Delimiter: TAB. CONFIRMED.
    // spec: Docs/RE/formats/text_tables.md §3.1 — "Delimiter: TAB": HIGH.
    private const char Delimiter = '\t';

    // Comment prefix: ';'. CONFIRMED.
    // spec: Docs/RE/formats/text_tables.md §3.1 — "';'-prefixed comment preamble": HIGH.
    private const char CommentPrefix = ';';

    // Expected column count per data row: 2. CONFIRMED.
    // spec: Docs/RE/formats/text_tables.md §3.1 — "col0: bad word, col1: replacement": HIGH.
    private const int ExpectedColumns = 2;

    /// <summary>
    /// Parses a raw CP949 chat-filter table (either <c>curse.txt</c> or <c>cursechat.txt</c>)
    /// into an array of <see cref="ChatFilterEntry"/> records.
    /// </summary>
    /// <param name="data">
    /// Raw file bytes from the VFS (CP949 / EUC-KR encoding, CRLF line endings).
    /// </param>
    /// <returns>
    /// Array of decoded filter entries (comment and blank lines skipped).
    /// </returns>
    /// <remarks>
    /// spec: Docs/RE/formats/text_tables.md §3.1 — schema: HIGH (no IDA cross-check).
    /// </remarks>
    public static ChatFilterEntry[] Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static ChatFilterEntry[] Parse(ReadOnlySpan<byte> span)
    {
        // Decode as CP949.
        // spec: Docs/RE/formats/text_tables.md §3.1 — "Encoding: CP949": HIGH.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/formats/text_tables.md — CP949.
        string text = cp949.GetString(span);

        return ParseText(text);
    }

    /// <summary>
    /// Overload accepting pre-decoded text (for testing with known strings).
    /// </summary>
    public static ChatFilterEntry[] ParseText(string text)
    {
        var entries = new List<ChatFilterEntry>();
        string[] lines = text.Split('\n');

        foreach (string rawLine in lines)
        {
            // Strip CR for CRLF line endings.
            // spec: Docs/RE/formats/text_tables.md §3.1 — "CRLF line endings": HIGH.
            string line = rawLine.TrimEnd('\r');

            // Skip blank lines.
            if (line.Length == 0) continue;

            // Skip comment lines (';'-prefixed preamble).
            // spec: Docs/RE/formats/text_tables.md §3.1 — "';'-prefixed comment preamble": HIGH.
            if (line[0] == CommentPrefix) continue;

            // TAB-split into exactly 2 columns.
            // spec: Docs/RE/formats/text_tables.md §3.1 — col0: bad word, col1: replacement: HIGH.
            string[] cols = line.Split(Delimiter);
            if (cols.Length < ExpectedColumns) continue;

            entries.Add(new ChatFilterEntry
            {
                BadWord = cols[0], // col0 — bad word to filter — HIGH
                Replacement = cols[1], // col1 — clean replacement — HIGH
            });
        }

        return entries.ToArray();
    }
}