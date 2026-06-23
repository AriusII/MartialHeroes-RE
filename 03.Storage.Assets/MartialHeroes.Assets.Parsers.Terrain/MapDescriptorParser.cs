using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class MapDescriptorParser
{
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "TERRAIN", "EXTRA_TERRAIN", "UP_TERRAIN", "BUILDING",
        "FX1", "FX2", "FX3", "FX4", "FX5", "FX6", "FX7",
        "SOLID"
    };

    public static MapDescriptor Parse(ReadOnlyMemory<byte> data)
    {
        return ParseText(Encoding.ASCII.GetString(data.Span));
    }

    public static MapDescriptor ParseText(string text)
    {
        var lines = text.Split('\n');
        var sections = new List<MapSection>();

        var lineIndex = 0;

        while (lineIndex < lines.Length)
        {
            var rawLine = lines[lineIndex].Trim();
            lineIndex++;

            if (rawLine.Length == 0 || rawLine.StartsWith('#'))
                continue;

            var commentPos = rawLine.IndexOf('#');
            if (commentPos >= 0)
                rawLine = rawLine[..commentPos].TrimEnd();
            if (rawLine.Length == 0)
                continue;

            var tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            var keyword = tokens[0].ToUpperInvariant();

            if (!KnownSections.Contains(keyword))
                continue;

            var foundBrace = false;
            var closedOnSameLine = false;
            var openBraceTokenIndex = -1;
            for (var ti = 1; ti < tokens.Length; ti++)
                if (tokens[ti] == "{")
                {
                    foundBrace = true;
                    openBraceTokenIndex = ti;
                }
                else if (foundBrace && tokens[ti] == "}")
                {
                    closedOnSameLine = true;
                    break;
                }

            if (!foundBrace)
                while (lineIndex < lines.Length)
                {
                    var next = lines[lineIndex].Trim();
                    lineIndex++;
                    if (next.Length == 0 || next.StartsWith('#'))
                        continue;
                    if (next.StartsWith('{'))
                    {
                        foundBrace = true;
                        var nextTokens = next.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (nextTokens.Length > 1 && nextTokens[1] == "}")
                            closedOnSameLine = true;
                        break;
                    }

                    throw new InvalidDataException(
                        $".map parse error: expected '{{' after section keyword '{keyword}', " +
                        $"got '{next}'.");
                }

            if (!foundBrace)
                throw new InvalidDataException(
                    $".map parse error: section '{keyword}' opened but no '{{' found before end of file.");

            string? dataFile = null;
            var textures = new List<(int Flag, int TexId)>();

            int? sectionWidth = null;
            int? sectionHeight = null;
            int? sectionGrid = null;
            float? sectionMaxHeightFiled = null;
            float? sectionMinHeightFiled = null;
            (float X, float Z)? sectionOrigin = null;

            while (!closedOnSameLine && lineIndex < lines.Length)
            {
                var bodyLine = lines[lineIndex].Trim();
                lineIndex++;

                var bcp = bodyLine.IndexOf('#');
                if (bcp >= 0) bodyLine = bodyLine[..bcp].TrimEnd();

                if (bodyLine.Length == 0) continue;

                if (bodyLine == "}")
                    break;

                var bt = bodyLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (bt.Length == 0) continue;

                var directive = bt[0].ToUpperInvariant();

                if (directive == "DATAFILE")
                {
                    if (bt.Length < 2)
                        throw new InvalidDataException(
                            $".map parse error: DATAFILE directive in section '{keyword}' has no path argument.");
                    dataFile = bt[1];
                }
                else if (directive == "WIDTH")
                {
                    if (bt.Length < 2 || !int.TryParse(bt[1], out var w))
                        throw new InvalidDataException(
                            $".map parse error: WIDTH directive in section '{keyword}' requires an integer argument.");
                    sectionWidth = w;
                }
                else if (directive == "HEIGHT")
                {
                    if (bt.Length < 2 || !int.TryParse(bt[1], out var h))
                        throw new InvalidDataException(
                            $".map parse error: HEIGHT directive in section '{keyword}' requires an integer argument.");
                    sectionHeight = h;
                }
                else if (directive == "GRID")
                {
                    if (bt.Length < 2 || !int.TryParse(bt[1], out var g))
                        throw new InvalidDataException(
                            $".map parse error: GRID directive in section '{keyword}' requires an integer argument.");
                    sectionGrid = g;
                }
                else if (directive == "MAX_HEIGHTFILED")
                {
                    if (bt.Length < 2 || !float.TryParse(bt[1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var maxH))
                        throw new InvalidDataException(
                            $".map parse error: MAX_HEIGHTFILED directive in section '{keyword}' requires a float argument.");
                    sectionMaxHeightFiled = maxH;
                }
                else if (directive == "MIN_HEIGHTFILED")
                {
                    if (bt.Length < 2 || !float.TryParse(bt[1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var minH))
                        throw new InvalidDataException(
                            $".map parse error: MIN_HEIGHTFILED directive in section '{keyword}' requires a float argument.");
                    sectionMinHeightFiled = minH;
                }
                else if (directive == "ORIGIN")
                {
                    var originStr = string.Join("", bt[1..]).Replace(" ", "");
                    var originParts = originStr.Split(',');
                    if (originParts.Length < 2
                        || !float.TryParse(originParts[0], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var ox)
                        || !float.TryParse(originParts[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var oz))
                        throw new InvalidDataException(
                            $".map parse error: ORIGIN directive in section '{keyword}' requires two comma-separated floats.");
                    sectionOrigin = (ox, oz);
                }
                else if (directive == "TEXTURES")
                {
                    var texBraceOnSameLine = bt.Length > 1 && bt[1] == "{";
                    if (!texBraceOnSameLine)
                    {
                        var texBraceFound = false;
                        while (lineIndex < lines.Length)
                        {
                            var tl = lines[lineIndex].Trim();
                            lineIndex++;
                            if (tl.Length == 0 || tl.StartsWith('#')) continue;
                            if (tl.StartsWith('{'))
                            {
                                texBraceFound = true;
                                break;
                            }

                            throw new InvalidDataException(
                                $".map parse error: expected '{{' after TEXTURES in section '{keyword}'.");
                        }

                        if (!texBraceFound)
                            throw new InvalidDataException(
                                $".map parse error: TEXTURES block in section '{keyword}' has no opening brace.");
                    }

                    while (lineIndex < lines.Length)
                    {
                        var tl = lines[lineIndex].Trim();
                        lineIndex++;
                        var tcp = tl.IndexOf('#');
                        if (tcp >= 0) tl = tl[..tcp].TrimEnd();
                        if (tl.Length == 0) continue;
                        if (tl == "}") break;

                        var tp = tl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (tp.Length < 2)
                            throw new InvalidDataException(
                                $".map parse error: TEXTURES entry in section '{keyword}' " +
                                $"must have two integers, got '{tl}'.");

                        if (!int.TryParse(tp[0], out var flag) || !int.TryParse(tp[1], out var texId))
                            throw new InvalidDataException(
                                $".map parse error: TEXTURES entry in section '{keyword}' " +
                                $"could not parse integers from '{tl}'.");

                        if (flag <= 0) break;

                        textures.Add((flag, texId));
                    }
                }
            }

            sections.Add(new MapSection
            {
                Keyword = keyword,
                DataFile = dataFile,
                Textures = textures.ToArray(),
                Width = sectionWidth,
                Height = sectionHeight,
                Grid = sectionGrid,
                MaxHeightFiled = sectionMaxHeightFiled,
                MinHeightFiled = sectionMinHeightFiled,
                Origin = sectionOrigin
            });
        }

        return new MapDescriptor { Sections = sections.ToArray() };
    }
}