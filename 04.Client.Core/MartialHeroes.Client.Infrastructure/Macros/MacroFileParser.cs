using System.Text;
using MartialHeroes.Client.Infrastructure.Exceptions;

namespace MartialHeroes.Client.Infrastructure.Macros;

public sealed class MacroFileParser : IMacroFileParser
{
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

    public IReadOnlyList<MacroDefinition> ParseContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.StartsWith('﻿'))
            content = content[1..];

        var result = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
        var order = new List<string>();

        string? currentName = null;
        string? currentKey = null;
        var currentCmds = new List<string>();

        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                if (currentName is not null)
                    FlushMacro(result, order, currentName, currentKey, currentCmds);

                ParseHeader(line, out currentName, out currentKey);
                currentCmds = new List<string>();
                continue;
            }

            if (currentName is not null)
                currentCmds.Add(line);

        }

        if (currentName is not null)
            FlushMacro(result, order, currentName, currentKey, currentCmds);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var macros = new List<MacroDefinition>(result.Count);
        foreach (var name in order)
            if (seen.Add(name) && result.TryGetValue(name, out var m))
                macros.Add(m);

        return macros;
    }


    private static void FlushMacro(
        Dictionary<string, MacroDefinition> dict,
        List<string> order,
        string name,
        string? triggerKey,
        List<string> cmds)
    {
        if (!dict.ContainsKey(name))
            order.Add(name);

        dict[name] = new MacroDefinition(name, triggerKey, cmds.AsReadOnly());
    }

    private static void ParseHeader(string line, out string name, out string? triggerKey)
    {
        var closeIndex = line.IndexOf(']');
        if (closeIndex <= 1)
        {
            name = line[1..].Trim();
            triggerKey = null;
            return;
        }

        name = line[1..closeIndex].Trim();

        var afterBracket = line[(closeIndex + 1)..].Trim();
        triggerKey = afterBracket.Length > 0 ? afterBracket : null;
    }

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

        if (start < content.Length)
            yield return content[start..];
    }
}