using System.Text;
using MartialHeroes.Client.Infrastructure.Exceptions;

namespace MartialHeroes.Client.Infrastructure.Macros;

/// <summary>
///     Parses <c>.mhm</c> (Martial Heroes Macro) files into
///     <see cref="MacroDefinition" /> objects.
///     <para>
///         Format spec: <c>Docs/RE/formats/macro_file.md</c>.
///         This is the project's own format — no legacy reverse-engineering involved.
///     </para>
/// </summary>
public sealed class MacroFileParser : IMacroFileParser
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MacroDefinition>> ParseFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Cannot read macro file '{filePath}'.", ex);
        }

        return ParseContent(content);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Parsing rules are documented in <c>Docs/RE/formats/macro_file.md</c>.
    ///     No magic constants or offsets are derived from the legacy binary.
    /// </remarks>
    public IReadOnlyList<MacroDefinition> ParseContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Strip UTF-8 BOM if present.
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 1
        if (content.StartsWith('﻿'))
            content = content[1..];

        var result = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
        // tracks first-appearance order; the dict always holds the last definition for each name
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 7
        var order = new List<string>();

        string? currentName = null;
        string? currentKey = null;
        var currentCmds = new List<string>();

        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();

            // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 3 & 4
            if (line.Length == 0 || line[0] == '#')
                continue;

            // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 5
            if (line[0] == '[')
            {
                // Flush the previous macro block before starting the new one.
                if (currentName is not null)
                    FlushMacro(result, order, currentName, currentKey, currentCmds);

                ParseHeader(line, out currentName, out currentKey);
                currentCmds = new List<string>();
                continue;
            }

            // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 6
            if (currentName is not null)
                currentCmds.Add(line);

            // Lines before any header are silently ignored.
        }

        // Flush the last block.
        if (currentName is not null)
            FlushMacro(result, order, currentName, currentKey, currentCmds);

        // Return macros in first-appearance order; block content comes from the last definition.
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 7
        // Contract: position = first appearance of a given name; value = last definition for that name.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var macros = new List<MacroDefinition>(result.Count);
        foreach (var name in order)
            if (seen.Add(name) && result.TryGetValue(name, out var m))
                macros.Add(m);

        return macros;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void FlushMacro(
        Dictionary<string, MacroDefinition> dict,
        List<string> order,
        string name,
        string? triggerKey,
        List<string> cmds)
    {
        // spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 7 (last-definition wins)
        if (!dict.ContainsKey(name))
            order.Add(name);

        dict[name] = new MacroDefinition(name, triggerKey, cmds.AsReadOnly());
    }

    /// <summary>
    ///     Parses a header line of the form <c>[MacroName] OptionalKey</c>.
    ///     spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 5
    /// </summary>
    private static void ParseHeader(string line, out string name, out string? triggerKey)
    {
        var closeIndex = line.IndexOf(']');
        if (closeIndex <= 1)
        {
            // Malformed header — treat name as everything between '[' and end-of-line.
            name = line[1..].Trim();
            triggerKey = null;
            return;
        }

        name = line[1..closeIndex].Trim();

        var afterBracket = line[(closeIndex + 1)..].Trim();
        triggerKey = afterBracket.Length > 0 ? afterBracket : null;
    }

    /// <summary>
    ///     Yields individual lines from <paramref name="content" /> honouring both
    ///     CR+LF and LF line endings.
    ///     spec: Docs/RE/formats/macro_file.md §"Parsing Rules" rule 2
    /// </summary>
    private static IEnumerable<string> EnumerateLines(string content)
    {
        var start = 0;
        for (var i = 0; i < content.Length; i++)
            if (content[i] == '\n')
            {
                var end = i;
                if (end > start && content[end - 1] == '\r') end--;
                yield return content[start..end];
                start = i + 1;
            }

        // Emit the last line if content does not end with '\n'.
        if (start < content.Length)
            yield return content[start..];
    }
}