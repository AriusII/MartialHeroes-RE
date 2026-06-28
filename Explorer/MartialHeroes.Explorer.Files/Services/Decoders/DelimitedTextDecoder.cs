using System;
using System.Collections.Generic;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class DelimitedTextDecoder : IFormatDecoder
{
    private const int MaxRows = 100_000;
    private const double TextThreshold = 0.85;

    private readonly SeparatorMode _mode;
    private readonly HexDumpDecoder _hex = new();

    public DelimitedTextDecoder(SeparatorMode mode = SeparatorMode.Auto)
    {
        _mode = mode;
    }

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0)
            return new TextDocument
            {
                Title = node.Name,
                Summary = "empty file (0 bytes)",
                Text = string.Empty
            };

        if (TextEncodings.PrintableRatio(bytes.Span) < TextThreshold)
            return _hex.Decode(node, bytes);

        var text = TextEncodings.Cp949.GetString(bytes.Span);
        var separator = ResolveSeparator(text);

        if (separator == '\0')
            return AsText(node, text);

        var collapse = separator == '\t';
        var recognizeMeta = _mode != SeparatorMode.Comma;

        var raw = new List<string[]>();
        var fields = new List<string>(64);
        var maxColumns = 0;
        var dataLines = 0;
        var comments = 0;
        long? declaredCount = null;
        var sawData = false;

        var pos = 0;
        while (TableTokenizer.NextLine(text, ref pos, out var start, out var end))
        {
            var line = text.AsSpan(start, end - start);
            if (TableTokenizer.IsBlank(line)) continue;

            if (recognizeMeta && TableTokenizer.IsComment(line))
            {
                comments++;
                continue;
            }

            if (recognizeMeta && !sawData && declaredCount is null &&
                TableTokenizer.IsCountLine(line, separator))
            {
                declaredCount = ParseCount(line);
                continue;
            }

            sawData = true;
            dataLines++;

            if (raw.Count >= MaxRows) continue;

            TableTokenizer.SplitFields(line, separator, collapse, fields);
            if (fields.Count > maxColumns) maxColumns = fields.Count;
            raw.Add(fields.ToArray());
        }

        if (maxColumns == 0)
            return AsText(node, text);

        var columns = new List<string>(maxColumns + 1) { "#" };
        for (var i = 0; i < maxColumns; i++)
            columns.Add($"col {i + 1}");

        var rows = new List<TableRow>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var source = raw[i];
            var cells = new string[maxColumns + 1];
            cells[0] = (i + 1).ToString();
            for (var c = 0; c < maxColumns; c++)
                cells[c + 1] = c < source.Length ? source[c] : string.Empty;
            rows.Add(new TableRow { Cells = cells });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = BuildSummary(rows.Count, dataLines, maxColumns, separator, declaredCount, comments),
            Columns = columns,
            Rows = rows
        };
    }

    private char ResolveSeparator(string text)
    {
        return _mode switch
        {
            SeparatorMode.Tab => TableTokenizer.HasSeparator(text, '\t') ? '\t' : '\0',
            SeparatorMode.Comma => TableTokenizer.HasSeparator(text, ',') ? ',' : '\0',
            _ => TableTokenizer.DetectAuto(text)
        };
    }

    private static long? ParseCount(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        return long.TryParse(trimmed, out var value) ? value : null;
    }

    private static string BuildSummary(
        int shown, int dataLines, int columns, char separator, long? declaredCount, int comments)
    {
        var summary = $"{shown:N0} rows × {columns} cols · {TableTokenizer.Name(separator)}-delimited · CP949";

        if (declaredCount is { } declared)
            summary += $" · header count {declared:N0}";

        if (comments > 0)
            summary += $" · {comments:N0} comment line{(comments == 1 ? "" : "s")} skipped";

        if (dataLines > shown)
            summary += $" · capped (showing {shown:N0} of {dataLines:N0})";

        return summary;
    }

    private static TextDocument AsText(VfsFileNode node, string text)
    {
        var lines = 1;
        foreach (var c in text)
            if (c == '\n')
                lines++;

        return new TextDocument
        {
            Title = node.Name,
            Summary = $"{lines:N0} lines · {text.Length:N0} chars · CP949 text",
            Text = text
        };
    }
}
