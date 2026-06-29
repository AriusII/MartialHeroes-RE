using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ItemsCsvParser
{
    private static readonly Encoding Cp949;

    static ItemsCsvParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static ItemCsvRow[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ItemCsvRow[] Parse(ReadOnlySpan<byte> span)
    {
        var text = Cp949.GetString(span);

        return ParseText(text);
    }

    public static ItemCsvRow[] ParseText(string text)
    {
        var rows = new List<ItemCsvRow>();

        var start = 0;
        var len = text.Length;
        while (start < len)
        {
            var lf = text.IndexOf('\n', start);
            var lineEnd = lf >= 0 ? lf : len;
            var lineSpan = text.AsSpan(start, lineEnd - start);

            if (lineSpan.Length > 0 && lineSpan[^1] == '\r')
                lineSpan = lineSpan[..^1];

            if (lineSpan.Length > 0)
            {
                var line = new string(lineSpan);
                var row = ParseLine(line);
                if (row is not null)
                    rows.Add(row);
            }

            start = lf >= 0 ? lf + 1 : len;
        }

        return rows.ToArray();
    }

    private static ItemCsvRow? ParseLine(string line)
    {
        var tokens = line.Split(',');

        var idTokenIndex = -1;
        for (var t = 0; t < tokens.Length; t++)
            if (IsNumericToken(tokens[t].Trim()))
            {
                idTokenIndex = t;
                break;
            }

        if (idTokenIndex < 0)
            return null;

        var itemName = string.Join(",", tokens, 0, idTokenIndex);

        var itemId = ParseUInt(tokens[idTokenIndex]);

        var descStart = idTokenIndex + 1;
        var numericTailStart = -1;
        for (var t = descStart; t < tokens.Length; t++)
            if (IsNumericToken(tokens[t].Trim()))
            {
                numericTailStart = t;
                break;
            }

        string itemDesc;
        string[] numericTokens;

        if (numericTailStart < 0)
        {
            itemDesc = string.Join(",", tokens, descStart, tokens.Length - descStart);
            numericTokens = [];
        }
        else
        {
            itemDesc = string.Join(",", tokens, descStart, numericTailStart - descStart);

            var tailCount = tokens.Length - numericTailStart;
            numericTokens = new string[tailCount];
            Array.Copy(tokens, numericTailStart, numericTokens, 0, tailCount);
        }

        return BuildRow(itemName, itemId, itemDesc, numericTokens);
    }

    private static bool IsNumericToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        var start = 0;
        var hasLeadingMinus = token[0] == '-';
        if (hasLeadingMinus)
        {
            if (token.Length == 1) return false;
            start = 1;
        }

        var hasPeriod = false;
        for (var i = start; i < token.Length; i++)
        {
            var c = token[i];
            if (c == '.')
            {
                if (hasPeriod) return false;
                hasPeriod = true;
            }
            else if (c < '0' || c > '9')
            {
                return false;
            }
        }

        if (hasLeadingMinus && !hasPeriod)
            return false;

        return true;
    }

    private static ItemCsvRow BuildRow(string name, uint itemId, string desc, string[] tail)
    {
        static uint GetU(string[] t, int i)
        {
            return i < t.Length ? ParseUInt(t[i]) : 0u;
        }

        var rawColumns = new string[3 + tail.Length];
        rawColumns[0] = name;
        rawColumns[1] = itemId.ToString(CultureInfo.InvariantCulture);
        rawColumns[2] = desc;
        tail.CopyTo(rawColumns, 3);

        return new ItemCsvRow
        {
            NameCp949 = name,
            ItemId = itemId,
            DescriptionCp949 = desc,
            BaseItemId = GetU(tail, 1),
            SecondaryTypeId = GetU(tail, 2),
            Col6Flag = GetU(tail, 3),
            RawColumns = rawColumns
        };
    }


    private static uint ParseUInt(string s)
    {
        return uint.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}