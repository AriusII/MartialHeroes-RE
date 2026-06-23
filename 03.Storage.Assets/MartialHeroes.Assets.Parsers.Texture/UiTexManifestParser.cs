using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class UiTexManifestParser
{
    private const string KwUiTexture = "UI_TEXTURE";

    private const string KwDds = "DDS";

    private const string KwMsk = "MSK";

    static UiTexManifestParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static UiTexManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static UiTexManifest Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static UiTexManifest ParseText(string text)
    {
        var ddsEntries = new List<UiTexEntry>();
        var mskEntries = new List<UiTexEntry>();

        var tokens = Tokenize(text);
        var pos = 0;

        SkipUntilToken(tokens, KwUiTexture, ref pos);
        if (pos >= tokens.Count) return new UiTexManifest(ddsEntries, mskEntries);

        pos++;
        if (pos < tokens.Count && tokens[pos] == "{") pos++;

        while (pos < tokens.Count && tokens[pos] != "}")
        {
            var subBlock = tokens[pos++];

            if (pos >= tokens.Count || tokens[pos] != "{")
                continue;

            pos++;

            var target = subBlock.Equals(KwDds, StringComparison.OrdinalIgnoreCase)
                ? ddsEntries
                : subBlock.Equals(KwMsk, StringComparison.OrdinalIgnoreCase)
                    ? mskEntries
                    : [];

            while (pos < tokens.Count && tokens[pos] != "}")
            {
                var idToken = tokens[pos++];

                if (!int.TryParse(idToken, out var texId))
                    continue;

                if (pos >= tokens.Count) break;

                var pathToken = tokens[pos++];
                var path = ExtractPath(pathToken);

                var kind = subBlock.Equals(KwDds, StringComparison.OrdinalIgnoreCase)
                    ? UiTexBlockKind.Dds
                    : UiTexBlockKind.Msk;

                target.Add(new UiTexEntry(texId, path, kind));
            }

            if (pos < tokens.Count && tokens[pos] == "}") pos++;
        }

        return new UiTexManifest(ddsEntries, mskEntries);
    }


    private static string ExtractPath(string token)
    {
        if (token.StartsWith('"'))
            token = token[1..];
        if (token.EndsWith('"'))
            token = token[..^1];
        return token;
    }

    private static List<string> Tokenize(string text)
    {
        var result = new List<string>(256);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            var i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                if (line[i] == '#') break;

                if (line[i] == '"')
                {
                    var start = i;
                    i++;
                    while (i < line.Length && line[i] != '"') i++;
                    if (i < line.Length) i++;
                    result.Add(line[start..i]);
                }
                else
                {
                    var start = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '#') i++;
                    result.Add(line[start..i]);
                }
            }
        }

        return result;
    }

    private static void SkipUntilToken(List<string> tokens, string keyword, ref int pos)
    {
        while (pos < tokens.Count && !tokens[pos].Equals(keyword, StringComparison.OrdinalIgnoreCase))
            pos++;
    }
}