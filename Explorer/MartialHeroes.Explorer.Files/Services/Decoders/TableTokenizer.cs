using System;
using System.Collections.Generic;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public enum SeparatorMode
{
    Tab,
    Comma,
    Auto
}

public static class TableTokenizer
{
    private const int SampleLines = 128;

    public static bool NextLine(string text, ref int pos, out int start, out int end)
    {
        var len = text.Length;
        if (pos >= len)
        {
            start = end = len;
            return false;
        }

        start = pos;
        var lf = text.IndexOf('\n', pos);
        if (lf < 0)
        {
            end = len;
            pos = len;
        }
        else
        {
            end = lf;
            pos = lf + 1;
        }

        if (end > start && text[end - 1] == '\r')
            end--;

        return true;
    }

    public static bool IsBlank(ReadOnlySpan<char> line)
    {
        foreach (var c in line)
            if (c != ' ' && c != '\t' && c != '\r')
                return false;
        return true;
    }

    public static bool IsComment(ReadOnlySpan<char> line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;

        var rest = line[i..];
        if (rest.Length == 0) return false;
        if (rest.Length >= 2 && rest[0] == '/' && rest[1] == '/') return true;
        return rest[0] == ';' || rest[0] == '#';
    }

    public static bool IsCountLine(ReadOnlySpan<char> line, char separator)
    {
        var digits = false;
        foreach (var c in line)
        {
            if (c == separator) return false;
            if (c is >= '0' and <= '9')
            {
                digits = true;
                continue;
            }

            if (c == ' ' || c == '\t' || c == '\r') continue;
            return false;
        }

        return digits;
    }

    public static void SplitFields(ReadOnlySpan<char> line, char separator, bool collapse, List<string> into)
    {
        into.Clear();
        var n = line.Length;

        if (collapse)
        {
            var i = 0;
            while (i < n)
            {
                while (i < n && line[i] == separator) i++;
                if (i >= n) break;
                var start = i;
                while (i < n && line[i] != separator) i++;
                into.Add(line[start..i].ToString());
            }

            return;
        }

        var from = 0;
        for (var i = 0; i <= n; i++)
            if (i == n || line[i] == separator)
            {
                into.Add(line[from..i].ToString());
                from = i + 1;
            }
    }

    public static char DetectAuto(string text)
    {
        var tab = ScoreSeparator(text, '\t');
        var comma = ScoreSeparator(text, ',');

        if (tab == 0 && comma == 0) return '\0';
        return tab >= comma ? '\t' : ',';
    }

    public static bool HasSeparator(string text, char separator)
    {
        return ScoreSeparator(text, separator) > 0;
    }

    private static int ScoreSeparator(string text, char separator)
    {
        var counts = new List<int>();
        var pos = 0;
        var sampled = 0;

        while (sampled < SampleLines && NextLine(text, ref pos, out var start, out var end))
        {
            var line = text.AsSpan(start, end - start);
            if (IsBlank(line) || IsComment(line)) continue;

            counts.Add(Count(line, separator));
            sampled++;
        }

        if (counts.Count == 0) return 0;

        var mode = ModeNonZero(counts);
        if (mode <= 0) return 0;

        var consistent = 0;
        foreach (var count in counts)
            if (count == mode)
                consistent++;

        return mode * 1000 + consistent;
    }

    private static int Count(ReadOnlySpan<char> line, char c)
    {
        var count = 0;
        foreach (var ch in line)
            if (ch == c)
                count++;
        return count;
    }

    private static int ModeNonZero(List<int> counts)
    {
        var freq = new Dictionary<int, int>();
        foreach (var value in counts)
        {
            if (value == 0) continue;
            freq[value] = freq.GetValueOrDefault(value) + 1;
        }

        var mode = 0;
        var modeFreq = 0;
        foreach (var pair in freq)
            if (pair.Value > modeFreq)
            {
                modeFreq = pair.Value;
                mode = pair.Key;
            }

        return mode;
    }

    public static string Name(char separator)
    {
        return separator switch
        {
            '\t' => "tab",
            ',' => "comma",
            ';' => "semicolon",
            '|' => "pipe",
            _ => separator.ToString()
        };
    }
}
