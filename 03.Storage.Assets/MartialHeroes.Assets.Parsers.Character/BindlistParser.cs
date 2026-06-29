using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class BindlistParser
{
    static BindlistParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static BindlistData Parse(ReadOnlyMemory<byte> data)
    {
        var cp949 = Encoding.GetEncoding(949);
        var raw = cp949.GetString(data.Span);

        var lines = raw.Split('\n');

        var entries = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim('\r');
            if (line.Length == 0)
                continue;

            entries.Add(line);
        }

        return new BindlistData([.. entries]);
    }
}