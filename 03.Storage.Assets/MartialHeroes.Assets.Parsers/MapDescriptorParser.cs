using MartialHeroes.Assets.Parsers.Models;
using System.Text;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.map</c> plain-text ASCII scene descriptor files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format)
/// <para>
/// Grammar:
///   - Lines beginning with '#' are comments and are ignored.
///   - Section blocks are opened with the section keyword followed by '{' and closed with '}'.
///   - Within a section, two directives are understood: DATAFILE and TEXTURES.
/// </para>
/// <para>
/// Section keywords: TERRAIN, EXTRA_TERRAIN, UP_TERRAIN, BUILDING, FX1–FX7, SOLID.
/// All CONFIRMED as present in the client string table.
/// spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class MapDescriptorParser
{
    // Known section keywords, CONFIRMED by client string table.
    // spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "TERRAIN", "EXTRA_TERRAIN", "UP_TERRAIN", "BUILDING",
        "FX1", "FX2", "FX3", "FX4", "FX5", "FX6", "FX7",
        "SOLID"
    };

    /// <summary>
    /// Parses the raw ASCII bytes of a <c>.map</c> file into a <see cref="MapDescriptor"/>.
    /// </summary>
    /// <param name="data">Raw file content (plain ASCII text) from the VFS.</param>
    /// <returns>Decoded map descriptor.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on malformed input (unclosed brace, malformed TEXTURES entry, etc.).
    /// </exception>
    public static MapDescriptor Parse(ReadOnlyMemory<byte> data) =>
        ParseText(Encoding.ASCII.GetString(data.Span));

    /// <summary>
    /// Parses a string representation of a <c>.map</c> file.
    /// </summary>
    public static MapDescriptor ParseText(string text)
    {
        // Tokenise to lines; strip comments and blank lines.
        string[] lines = text.Split('\n');
        var sections = new List<MapSection>();

        int lineIndex = 0;

        while (lineIndex < lines.Length)
        {
            string rawLine = lines[lineIndex].Trim();
            lineIndex++;

            // Skip comments and blank lines.
            // spec: Docs/RE/formats/terrain.md §3 — "Lines beginning with '#' are comments": CONFIRMED.
            if (rawLine.Length == 0 || rawLine.StartsWith('#'))
                continue;

            // Strip inline comments.
            int commentPos = rawLine.IndexOf('#');
            if (commentPos >= 0)
                rawLine = rawLine[..commentPos].TrimEnd();
            if (rawLine.Length == 0)
                continue;

            // Check for a section-opening line: "<KEYWORD> {" or "<KEYWORD>" followed by "{" on next token.
            // The parser accepts the opening brace on the same line or as the next non-blank token.
            string[] tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            string keyword = tokens[0].ToUpperInvariant();

            // Only process known section keywords.
            // spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
            if (!KnownSections.Contains(keyword))
                continue; // Unknown keyword — skip silently; section semantics only documented for known list.

            // Find the opening brace (may be on same line after keyword, or on its own next line).
            // Also detect if the closing brace appears on the same line as the opening brace
            // (e.g. "FX1 { }" — empty section, inline).
            bool foundBrace = false;
            bool closedOnSameLine = false;
            int openBraceTokenIndex = -1;
            for (int ti = 1; ti < tokens.Length; ti++)
            {
                if (tokens[ti] == "{")
                {
                    foundBrace = true;
                    openBraceTokenIndex = ti;
                }
                else if (foundBrace && tokens[ti] == "}")
                {
                    // Closing brace appears after opening brace on the same line.
                    closedOnSameLine = true;
                    break;
                }
            }

            if (!foundBrace)
            {
                // Brace must appear as next non-blank non-comment token.
                while (lineIndex < lines.Length)
                {
                    string next = lines[lineIndex].Trim();
                    lineIndex++;
                    if (next.Length == 0 || next.StartsWith('#'))
                        continue;
                    if (next.StartsWith('{'))
                    {
                        foundBrace = true;
                        // Check if the closing brace is also on this line (e.g. "{ }").
                        string[] nextTokens = next.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (nextTokens.Length > 1 && nextTokens[1] == "}")
                            closedOnSameLine = true;
                        break;
                    }

                    // Unexpected token before '{'.
                    throw new InvalidDataException(
                        $".map parse error: expected '{{' after section keyword '{keyword}', " +
                        $"got '{next}'.");
                }
            }

            if (!foundBrace)
                throw new InvalidDataException(
                    $".map parse error: section '{keyword}' opened but no '{{' found before end of file.");

            // Parse section body until '}' is found.
            // If the closing brace appeared on the same line as the opening brace, skip body loop.
            string? dataFile = null;
            var textures = new List<(int Flag, int TexId)>();

            while (!closedOnSameLine && lineIndex < lines.Length)
            {
                string bodyLine = lines[lineIndex].Trim();
                lineIndex++;

                // Strip inline comments.
                int bcp = bodyLine.IndexOf('#');
                if (bcp >= 0) bodyLine = bodyLine[..bcp].TrimEnd();

                if (bodyLine.Length == 0) continue;

                if (bodyLine == "}")
                    break; // End of this section.

                string[] bt = bodyLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (bt.Length == 0) continue;

                string directive = bt[0].ToUpperInvariant();

                if (directive == "DATAFILE")
                {
                    // DATAFILE <path>
                    // spec: Docs/RE/formats/terrain.md §3.2 DATAFILE directive: CONFIRMED.
                    if (bt.Length < 2)
                        throw new InvalidDataException(
                            $".map parse error: DATAFILE directive in section '{keyword}' has no path argument.");
                    dataFile = bt[1];
                }
                else if (directive == "TEXTURES")
                {
                    // TEXTURES { <intFlag> <intTexId> ... }
                    // spec: Docs/RE/formats/terrain.md §3.3 TEXTURES directive: CONFIRMED (structure).
                    // intFlag semantics: UNVERIFIED.
                    bool texBraceOnSameLine = bt.Length > 1 && bt[1] == "{";
                    if (!texBraceOnSameLine)
                    {
                        // Find opening brace.
                        bool texBraceFound = false;
                        while (lineIndex < lines.Length)
                        {
                            string tl = lines[lineIndex].Trim();
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

                    // Read texture pairs until closing brace.
                    while (lineIndex < lines.Length)
                    {
                        string tl = lines[lineIndex].Trim();
                        lineIndex++;
                        int tcp = tl.IndexOf('#');
                        if (tcp >= 0) tl = tl[..tcp].TrimEnd();
                        if (tl.Length == 0) continue;
                        if (tl == "}") break;

                        string[] tp = tl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (tp.Length < 2)
                            throw new InvalidDataException(
                                $".map parse error: TEXTURES entry in section '{keyword}' " +
                                $"must have two integers, got '{tl}'.");

                        if (!int.TryParse(tp[0], out int flag) || !int.TryParse(tp[1], out int texId))
                            throw new InvalidDataException(
                                $".map parse error: TEXTURES entry in section '{keyword}' " +
                                $"could not parse integers from '{tl}'.");

                        textures.Add((flag, texId));
                    }
                }
                // Other directives within a section body are ignored (no other directives documented).
            }

            sections.Add(new MapSection
            {
                Keyword = keyword,
                DataFile = dataFile,
                Textures = textures.ToArray(),
            });
        }

        return new MapDescriptor { Sections = sections.ToArray() };
    }
}