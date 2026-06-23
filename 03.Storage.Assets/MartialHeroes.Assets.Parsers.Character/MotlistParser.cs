using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class MotlistParser
{
    static MotlistParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static MotlistData Parse(ReadOnlyMemory<byte> data)
    {
        var text = Encoding.GetEncoding(949).GetString(data.Span);
        return ParseText(text);
    }

    public static MotlistData ParseText(string text)
    {
        var lines = text.Split('\n');
        var entries = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim('\r', '\n', ' ', '\t');
            if (line.Length == 0) continue;
            if (!line.EndsWith(".mot", StringComparison.OrdinalIgnoreCase)) continue;
            entries.Add(line);
        }

        return new MotlistData([.. entries]);
    }
}