using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class TexListParser
{
    private const string Extension = ".png";

    static TexListParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static CharFilenameManifest Parse(ReadOnlyMemory<byte> data)
    {
        var text = Encoding.GetEncoding(949).GetString(data.Span);
        return ParseText(text);
    }

    public static CharFilenameManifest ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n');
        var entries = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim('\r', '\n', ' ', '\t');
            if (line.Length == 0) continue;
            if (!line.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
            entries.Add(line);
        }

        return new CharFilenameManifest([.. entries]);
    }
}