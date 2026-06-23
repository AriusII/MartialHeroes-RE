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

    public static readonly uint[] CategoryBase = [0, 0, 10000, 1000];


    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes)
    {
        return Parse(fileBytes.Span, CategoryBase);
    }

    public static Dictionary<int, ActormotionEntry> ParseAsLookup(ReadOnlyMemory<byte> fileBytes)
    {
        var catalogue = Parse(fileBytes);
        var dict = new Dictionary<int, ActormotionEntry>(catalogue.Count);
        foreach (var entry in catalogue.AllEntries)
            dict.TryAdd(entry.ActorClassId, entry);
        return dict;
    }

    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes, ReadOnlySpan<uint> baseTable)
    {
        return Parse(fileBytes.Span, baseTable);
    }

    public static ActormotionCatalogue Parse(ReadOnlySpan<byte> fileBytes, ReadOnlySpan<uint> baseTable)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var text = cp949.GetString(fileBytes);
        return ParseText(text, baseTable);
    }

    public static ActormotionCatalogue ParseText(string text)
    {
        return ParseText(text, CategoryBase);
    }

    public static ActormotionCatalogue ParseText(string text, ReadOnlySpan<uint> baseTable)
    {
        var lines = text.Split('\n');

        var capacity = lines.Length;
        if (lines.Length > 0 && int.TryParse(lines[0].Trim('\r').Trim(), out var declaredCount))
            capacity = declaredCount;

        var byKey = new Dictionary<uint, ActormotionEntry>(capacity);

        for (var lineIdx = 1; lineIdx < lines.Length; lineIdx++)
        {
            var raw = lines[lineIdx];
            if (raw.Length == 0) continue;

            var trimmed = raw.AsSpan().TrimEnd('\r');
            if (trimmed.IsEmpty) continue;

            var cols = raw.TrimEnd('\r').Split('\t');

            if (cols.Length < TotalColumns) continue;

            if (!int.TryParse(cols[ColCategory].Trim(), out var col0)) continue;
            if (!int.TryParse(cols[ColIntraOffset].Trim(), out var col1)) continue;

            uint baseContrib = 0;
            int baseIdx =
                (byte)(col0 + 1);
            if (baseIdx < baseTable.Length)
                baseContrib = baseTable[baseIdx];

            var motionKey = (uint)(col1 + (int)baseContrib);


            int.TryParse(cols[ColIntA].Trim(), out var intA);
            float.TryParse(cols[ColRateSrcX].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var rateSrcX);
            int.TryParse(cols[ColDivisorX].Trim(), out var divisorX);
            float.TryParse(cols[ColRateSrcY].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var rateSrcY);
            int.TryParse(cols[ColIntB].Trim(), out var intB);
            float.TryParse(cols[ColFloatC].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatC);
            float.TryParse(cols[ColFloatD].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatD);
            float.TryParse(cols[ColFloatE].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatE);
            float.TryParse(cols[ColFloatF].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatF);
            float.TryParse(cols[ColFloatG].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatG);
            float.TryParse(cols[ColFloatH].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatH);
            float.TryParse(cols[ColFloatI].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var floatI);
            int.TryParse(cols[ColDivisorY].Trim(), out var divisorY);

            if (divisorX == 0) divisorX = 1;
            if (divisorY == 0) divisorY = 1;

            var rateX = FpsBase * rateSrcX / divisorX;
            var rateY = FpsBase * rateSrcY / divisorY;

            var dirArray1 = new int[MotionIdArrayCount];
            var dirArray2 = new int[MotionIdArrayCount];
            for (var d = 0; d < MotionIdArrayCount; d++)
            {
                int.TryParse(cols[ColMotionIdsAStart + d].Trim(), out dirArray1[d]);
                int.TryParse(cols[ColMotionIdsBStart + d].Trim(), out dirArray2[d]);
            }

            var entry = new ActormotionEntry
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
                DirArray1 = dirArray1,
                DirArray2 = dirArray2
            };

            byKey.TryAdd(motionKey, entry);
        }

        return new ActormotionCatalogue(byKey);
    }
}