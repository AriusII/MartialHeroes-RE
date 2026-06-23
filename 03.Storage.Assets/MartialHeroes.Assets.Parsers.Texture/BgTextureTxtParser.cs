using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class BgTextureTxtParser
{
    public static BgTextureCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static BgTextureCatalog Parse(ReadOnlySpan<byte> span)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return ParseText(Encoding.GetEncoding(949).GetString(span));
    }

    public static BgTextureCatalog ParseText(string text)
    {
        var map = new Dictionary<int, string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Replace("\r", string.Empty);
            if (line.Length == 0) continue;

            var cols = line.Split('\t');
            if (cols.Length < 3) continue;
            if (!int.TryParse(cols[0].Trim(), out var poolIndex)) continue;

            var rel = cols[2].Trim();
            if (rel.Length == 0) continue;

            map[poolIndex] = rel;
        }

        return new BgTextureCatalog(map);
    }
}