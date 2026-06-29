using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class UserjointParser
{
    private const int ColumnsPerRecord = 5;
    private const int JointIndexBound = 41;

    private static readonly char[] Separators = ['\t', '\n'];

    static UserjointParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static UserjointTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static UserjointTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static UserjointTable ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var tokens = normalized.Split(Separators);
        if (tokens.Length == 0)
            return new UserjointTable([]);

        var tokenIndex = 0;
        var declaredCount = ParseAtol(tokens[tokenIndex++]);
        var capacity = declaredCount > 0
            ? declaredCount
            : Math.Max(0, (tokens.Length - tokenIndex) / ColumnsPerRecord);

        var entries = new List<UserjointEntry>(capacity);
        var read = 0;
        while (tokenIndex + ColumnsPerRecord <= tokens.Length
               && (declaredCount <= 0 || read < declaredCount))
        {
            var jointIndex = ParseAtol(tokens[tokenIndex + 0]);
            if (jointIndex >= JointIndexBound)
                break;

            entries.Add(new UserjointEntry(
                jointIndex,
                ParseAtol(tokens[tokenIndex + 1]),
                ParseAtol(tokens[tokenIndex + 2]),
                ParseAtol(tokens[tokenIndex + 3]),
                ParseAtol(tokens[tokenIndex + 4])));

            tokenIndex += ColumnsPerRecord;
            read++;
        }

        return new UserjointTable(entries);
    }

    private static int ParseAtol(string token)
    {
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        return value;
    }
}