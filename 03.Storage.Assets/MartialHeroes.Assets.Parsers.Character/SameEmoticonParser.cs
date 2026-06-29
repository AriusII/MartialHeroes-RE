using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class SameEmoticonParser
{
    private const int ColumnsPerRecord = 2;

    static SameEmoticonParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static SameEmoticonTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static SameEmoticonTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static SameEmoticonTable ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var entries = new List<SameEmoticonEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;

            var cols = line.Split('\t', ColumnsPerRecord);
            if (cols.Length < ColumnsPerRecord)
                continue;

            entries.Add(new SameEmoticonEntry(ParseAtol(cols[0]), cols[1]));
        }

        return new SameEmoticonTable(entries);
    }

    private static int ParseAtol(string token)
    {
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        return value;
    }
}