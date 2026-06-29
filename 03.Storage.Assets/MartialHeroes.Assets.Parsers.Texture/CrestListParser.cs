using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class CrestListParser
{
    private const string PoolPathPrefix = "data/ui/guildicon/pool/";

    static CrestListParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static CrestListManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static CrestListManifest Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, PoolPathPrefix);
    }

    public static CrestListManifest Parse(ReadOnlySpan<byte> span, string poolPathPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(poolPathPrefix);
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, poolPathPrefix);
    }

    public static CrestListManifest ParseText(string text, string poolPathPrefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(poolPathPrefix);

        var entries = new List<CrestListEntry>(Math.Max(16, text.Length / 24));

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var vfsPath = poolPathPrefix + line;
            entries.Add(new CrestListEntry(line, vfsPath));
        }

        return new CrestListManifest(entries.ToArray());
    }
}