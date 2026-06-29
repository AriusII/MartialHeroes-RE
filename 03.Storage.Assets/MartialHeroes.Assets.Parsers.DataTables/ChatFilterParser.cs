using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ChatFilterParser
{
    private const char Delimiter = '\t';

    private const char CommentPrefix = ';';

    private const int ExpectedColumns = 2;

    private static readonly Encoding Cp949;

    static ChatFilterParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static ChatFilterEntry[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ChatFilterEntry[] Parse(ReadOnlySpan<byte> span)
    {
        var text = Cp949.GetString(span);

        return ParseText(text);
    }

    public static ChatFilterEntry[] ParseText(string text)
    {
        var entries = new List<ChatFilterEntry>();
        var lines = text.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0) continue;

            if (line[0] == CommentPrefix) continue;

            var cols = line.Split(Delimiter);
            if (cols.Length < ExpectedColumns) continue;

            entries.Add(new ChatFilterEntry
            {
                BadWord = cols[0],
                Replacement = cols[1]
            });
        }

        return entries.ToArray();
    }
}