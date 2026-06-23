using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class SkinTxtParser
{
    private const int ColumnsPerRecord = 6;

    static SkinTxtParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static SkinTxtCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static SkinTxtCatalog Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

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