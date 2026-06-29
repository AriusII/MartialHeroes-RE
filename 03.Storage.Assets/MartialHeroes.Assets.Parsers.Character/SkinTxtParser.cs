using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class SkinTxtParser
{
    private const int ColumnsPerRecord = 6;

    private static readonly char[] Separators = ['\t', '\n'];

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

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var tokens = normalized.Split(Separators);
        if (tokens.Length == 0)
            return new SkinTxtCatalog([]);

        var tokenIndex = 0;
        var declaredCount = ParseAtol(tokens[tokenIndex++]);
        var capacity = declaredCount > 0
            ? declaredCount
            : Math.Max(0, (tokens.Length - tokenIndex) / ColumnsPerRecord);

        var entries = new List<SkinTxtEntry>(capacity);
        while (tokenIndex + ColumnsPerRecord <= tokens.Length)
        {
            entries.Add(new SkinTxtEntry(
                ParseAtol(tokens[tokenIndex + 0]),
                ParseAtol(tokens[tokenIndex + 1]),
                ParseAtol(tokens[tokenIndex + 2]),
                ParseAtol(tokens[tokenIndex + 3]),
                ParseAtol(tokens[tokenIndex + 4]),
                ParseAtol(tokens[tokenIndex + 5])));

            tokenIndex += ColumnsPerRecord;
        }

        return new SkinTxtCatalog(entries);
    }

    private static int ParseAtol(string token)
    {
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        return value;
    }
}