using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

public static class ActormotionParser
{
    private const int ColCategory = 0;
    private const int ColIntraOffset = 1;
    private const int ColIntA = 2;
    private const int ColRateSrcX = 3;
    private const int ColDivisorX = 4;
    private const int ColRateSrcY = 5;
    private const int ColDivisorY = 6;
    private const int ColIntB = 7;
    private const int ColFloatC = 8;
    private const int ColFloatD = 9;
    private const int ColFloatE = 10;
    private const int ColFloatF = 11;
    private const int ColFloatG = 12;
    private const int ColFloatH = 13;
    private const int ColFloatI = 14;
    private const int ColMotionIdsAStart = 15;
    private const int ColMotionIdsBStart = 24;
    private const int TotalColumns = 33;

    private const int MotionIdArrayCount = 9;

    private const float FpsBase = 15.0f;

    static ActormotionParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes)
    {
        return Parse(fileBytes.Span, default);
    }

    public static Dictionary<int, ActormotionEntry> ParseAsLookup(ReadOnlyMemory<byte> fileBytes)
    {
        var catalogue = Parse(fileBytes);
        var dict = new Dictionary<int, ActormotionEntry>(catalogue.Count);
        foreach (var entry in catalogue.AllEntries)
            dict.TryAdd(entry.Col1RawOffset, entry);
        return dict;
    }

    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes, ReadOnlySpan<uint> baseTable)
    {
        return Parse(fileBytes.Span, baseTable);
    }

    public static ActormotionCatalogue Parse(ReadOnlySpan<byte> fileBytes, ReadOnlySpan<uint> baseTable)
    {
        var cp949 = Encoding.GetEncoding(949);
        var text = cp949.GetString(fileBytes);
        return ParseText(text, baseTable);
    }

    public static ActormotionCatalogue ParseText(string text)
    {
        return ParseText(text, default);
    }

    public static ActormotionCatalogue ParseText(string text, ReadOnlySpan<uint> baseTable)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n');

        var capacity = lines.Length;
        if (lines.Length > 0 && int.TryParse(lines[0].Trim('\r').Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var declaredCount) && declaredCount > 0)
            capacity = declaredCount;

        var entries = new List<ActormotionEntry>(capacity);
        var baseResolved = baseTable.Length > 0;

        for (var lineIdx = 1; lineIdx < lines.Length; lineIdx++)
        {
            var trimmed = lines[lineIdx].AsSpan().TrimEnd('\r');
            if (trimmed.IsEmpty) continue;

            var cols = lines[lineIdx].TrimEnd('\r').Split('\t');
            if (cols.Length < TotalColumns) continue;

            var col0 = ParseInt(cols[ColCategory]);
            var col1 = ParseInt(cols[ColIntraOffset]);

            uint baseContrib = 0;
            var baseIdx = (byte)(col0 + 1);
            if (baseIdx < baseTable.Length)
                baseContrib = baseTable[baseIdx];

            var motionKey = (uint)(col1 + (int)baseContrib);

            var intA = ParseInt(cols[ColIntA]);
            var rateSrcX = ParseFloat(cols[ColRateSrcX]);
            var divisorX = ParseInt(cols[ColDivisorX]);
            var rateSrcY = ParseFloat(cols[ColRateSrcY]);
            var divisorY = ParseInt(cols[ColDivisorY]);
            var intB = ParseInt(cols[ColIntB]);
            var floatC = ParseFloat(cols[ColFloatC]);
            var floatD = ParseFloat(cols[ColFloatD]);
            var floatE = ParseFloat(cols[ColFloatE]);
            var floatF = ParseFloat(cols[ColFloatF]);
            var floatG = ParseFloat(cols[ColFloatG]);
            var floatH = ParseFloat(cols[ColFloatH]);
            var floatI = ParseFloat(cols[ColFloatI]);

            if (divisorX == 0) divisorX = 1;
            if (divisorY == 0) divisorY = 1;

            var rateX = FpsBase * rateSrcX / divisorX;
            var rateY = FpsBase * rateSrcY / divisorY;

            var motionIdsA = new int[MotionIdArrayCount];
            var motionIdsB = new int[MotionIdArrayCount];
            for (var d = 0; d < MotionIdArrayCount; d++)
            {
                motionIdsA[d] = ParseInt(cols[ColMotionIdsAStart + d]);
                motionIdsB[d] = ParseInt(cols[ColMotionIdsBStart + d]);
            }

            entries.Add(new ActormotionEntry
            {
                MotionKey = motionKey,
                Col0Category = col0,
                Col1RawOffset = col1,
                IntA = intA,
                RateSrcX = rateSrcX,
                RateSrcY = rateSrcY,
                IntB = intB,
                FloatC = floatC,
                FloatD = floatD,
                FloatE = floatE,
                FloatF = floatF,
                FloatG = floatG,
                DivisorX = divisorX,
                DivisorY = divisorY,
                RateX = rateX,
                RateY = rateY,
                FloatH = floatH,
                FloatI = floatI,
                MotionIdsA = motionIdsA,
                MotionIdsB = motionIdsB
            });
        }

        return new ActormotionCatalogue(entries, baseResolved);
    }

    private static int ParseInt(string token)
    {
        int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        return value;
    }

    private static float ParseFloat(string token)
    {
        float.TryParse(token.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        return value;
    }
}