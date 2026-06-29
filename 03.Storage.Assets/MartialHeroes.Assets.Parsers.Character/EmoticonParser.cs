using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class EmoticonParser
{
    private const int ColumnsPerRecord = 12;
    private const int AnimIdStart = 4;
    private const int AnimIdCount = 8;

    private static readonly char[] Separators = ['\t', '\n'];

    static EmoticonParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static EmoticonTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static EmoticonTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static EmoticonTable ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var tokens = normalized.Split(Separators);
        if (tokens.Length == 0)
            return new EmoticonTable([]);

        var tokenIndex = 0;
        var declaredCount = ParseAtol(tokens[tokenIndex++]);
        var capacity = declaredCount > 0
            ? declaredCount
            : Math.Max(0, (tokens.Length - tokenIndex) / ColumnsPerRecord);

        var entries = new List<EmoticonEntry>(capacity);
        var read = 0;
        while (tokenIndex + ColumnsPerRecord <= tokens.Length
               && (declaredCount <= 0 || read < declaredCount))
        {
            var emoteId = ParseAtol(tokens[tokenIndex + 0]);
            var emoteName = tokens[tokenIndex + 1];
            var enterState = ParseAtol(tokens[tokenIndex + 2]);
            var nextState = ParseAtol(tokens[tokenIndex + 3]);

            var animIds = new int[AnimIdCount];
            for (var k = 0; k < AnimIdCount; k++)
                animIds[k] = ParseAtol(tokens[tokenIndex + AnimIdStart + k]);

            entries.Add(new EmoticonEntry(emoteId, emoteName, enterState, nextState, animIds));

            tokenIndex += ColumnsPerRecord;
            read++;
        }

        return new EmoticonTable(entries);
    }

    private static int ParseAtol(string token)
    {
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        return value;
    }
}