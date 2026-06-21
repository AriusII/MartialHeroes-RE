using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

/// <summary>
///     Parser for <c>data/char/skin.txt</c>, the character appearance catalogue.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/text_tables.md §skin.txt — leading count token followed by
///     six integer tokens per record.
///     spec: Docs/RE/specs/skinning.md §3.5.3 — column 4 is the mesh gid and column 5 is the texture id.
///     spec: Docs/RE/formats/texture.md §The skin chain — <c>.skn</c> <c>IdA</c> joins to
///     <c>skin.txt</c> col4, yielding col5 <c>tex_id</c>.
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class SkinTxtParser
{
    private const int ColumnsPerRecord = 6;

    static SkinTxtParser()
    {
        // spec: Docs/RE/formats/text_tables.md §1A — count-prefixed text tables use CP949.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static SkinTxtCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>Parses raw CP949 bytes from <c>data/char/skin.txt</c>.</summary>
    public static SkinTxtCatalog Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    /// <summary>Parses pre-decoded text for tests and diagnostics.</summary>
    public static SkinTxtCatalog ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return new SkinTxtCatalog([]);

        var tokenIndex = 0;
        var capacity = int.TryParse(tokens[tokenIndex++], out var declaredCount) && declaredCount > 0
            ? declaredCount
            : Math.Max(0, (tokens.Length - tokenIndex) / ColumnsPerRecord);

        var entries = new List<SkinTxtEntry>(capacity);
        while (tokenIndex + ColumnsPerRecord <= tokens.Length)
        {
            // spec: Docs/RE/formats/text_tables.md §skin.txt — 6 integer tokens per record.
            if (!TryParseInt(tokens[tokenIndex + 0], out var col0)
                || !TryParseInt(tokens[tokenIndex + 1], out var col1)
                || !TryParseInt(tokens[tokenIndex + 2], out var col2)
                || !TryParseInt(tokens[tokenIndex + 3], out var col3)
                || !TryParseInt(tokens[tokenIndex + 4], out var col4)
                || !TryParseInt(tokens[tokenIndex + 5], out var col5))
            {
                tokenIndex += ColumnsPerRecord;
                continue;
            }

            entries.Add(new SkinTxtEntry(
                col0,
                col1,
                col2,
                col3,
                col4,
                col5));

            tokenIndex += ColumnsPerRecord;
        }

        return new SkinTxtCatalog(entries);
    }

    private static bool TryParseInt(string token, out int value)
    {
        return int.TryParse(token, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out value);
    }
}