using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class TextureListParser
{
    private const string VfsPathPrefix = "data/item/texture/";

    static TextureListParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TextureListManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static TextureListManifest Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, VfsPathPrefix);
    }

    public static TextureListManifest Parse(ReadOnlySpan<byte> span, string vfsPathPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(vfsPathPrefix);
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, vfsPathPrefix);
    }

    public static TextureListManifest ParseText(string text, string vfsPathPrefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(vfsPathPrefix);

        var entries = new List<TextureListEntry>(Math.Max(16, text.Length / 20));

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
                continue;

            var dotPos = line.LastIndexOf('.');
            if (dotPos < 0)
                continue;

            var nameSpan = line.AsSpan(0, dotPos);
            var ext = line[dotPos..];

            var texId = ParseLeadingDigits(nameSpan);

            var vfsPath = vfsPathPrefix + line;

            entries.Add(new TextureListEntry(texId, vfsPath));
        }

        return new TextureListManifest(entries);
    }


    private static int ParseLeadingDigits(ReadOnlySpan<char> name)
    {
        var value = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is < '0' or > '9') break;
            value = value * 10 + (c - '0');
        }

        return value;
    }
}