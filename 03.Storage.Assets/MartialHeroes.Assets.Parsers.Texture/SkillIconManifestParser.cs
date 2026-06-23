using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class SkillIconManifestParser
{
    private const string KwSkill = "SKILL";

    static SkillIconManifestParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static SkillIconManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static SkillIconManifest Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static SkillIconManifest ParseText(string text)
    {
        var entries = new List<SkillIconEntry>();

        var tokens = Tokenize(text);
        var pos = 0;

        while (pos < tokens.Count && !tokens[pos].Equals(KwSkill, StringComparison.OrdinalIgnoreCase))
            pos++;
        if (pos >= tokens.Count) return new SkillIconManifest(entries);

        pos++;

        if (pos < tokens.Count && tokens[pos] == "{") pos++;

        while (pos < tokens.Count && tokens[pos] != "}")
        {
            var tok0 = tokens[pos++];
            if (!int.TryParse(tok0, out var skillId))
                continue;

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            if (!int.TryParse(tokens[pos++], out var jobId)) continue;

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            if (!int.TryParse(tokens[pos++], out var kindId)) continue;

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            var rawPath = tokens[pos++];
            var path = ExtractPath(rawPath);

            entries.Add(new SkillIconEntry(skillId, jobId, kindId, path));
        }

        return new SkillIconManifest(entries);
    }


    private static string ExtractPath(string token)
    {
        if (token.StartsWith('"')) token = token[1..];
        if (token.EndsWith('"')) token = token[..^1];
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
}