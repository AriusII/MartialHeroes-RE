using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ItemsCsvParser
{
    public static ItemCsvRow[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ItemCsvRow[] Parse(ReadOnlySpan<byte> span)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var text = cp949.GetString(span);

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

        static ushort GetS(string[] t, int i)
        {
            return i < t.Length ? ParseUShort(t[i]) : (ushort)0;
        }

        static byte GetB(string[] t, int i)
        {
            return i < t.Length ? ParseByte(t[i]) : (byte)0;
        }

        static float GetF(string[] t, int i)
        {
            return i < t.Length ? ParseFloat(t[i]) : 0f;
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
            LinkedItemId = GetU(tail, 0),
            BaseRefId = GetU(tail, 1),
            SecondaryRefId = GetU(tail, 2),
            ItemSubtype = GetU(tail, 3),

            BonusFlagA = GetB(tail, 4),
            BonusFlagB = GetB(tail, 5),
            EnhancementSize = GetB(tail, 7),
            SellPrice = GetU(tail, 13),
            NpcPurchaseable = GetB(tail, 14),
            Enabled = GetB(tail, 15),

            MaxStack = GetS(tail, 16),
            ItemTierRank = GetS(tail, 19),
            MaxDurability = GetS(tail, 20),

            ReqStr = GetS(tail, 21),
            ReqCon = GetS(tail, 22),
            ReqAgi = GetS(tail, 23),
            ReqInt = GetS(tail, 24),
            ReqChi = GetS(tail, 25),

            ClassYi = GetB(tail, 26),
            ClassYe = GetB(tail, 27),
            ClassIn = GetB(tail, 28),
            ClassJi = GetB(tail, 29),

            EnchantLevel = GetB(tail, 44),
            GemPower = GetB(tail, 45),

            BonusAtk = GetU(tail, 61),
            BonusHp = GetU(tail, 62),
            BonusExtAtk = GetU(tail, 65),

            AttackSpeed = GetF(tail, 72),
            DodgeRate = GetF(tail, 75),

            BonusChi = GetU(tail, 81),
            WeaponStatA = GetU(tail, 82),
            WeaponStatB = GetU(tail, 83),
            MinAttack = GetU(tail, 84),
            MaxAttack = GetU(tail, 87),
            BonusDefenseA = GetU(tail, 90),
            PhysDefense = GetU(tail, 91),
            ArmorDefense = GetU(tail, 93),

            ModelSetId = GetS(tail, 114),
            ModelType = GetB(tail, 115),

            DurationMinutes = GetU(tail, 109),
            ExpireMode = GetB(tail, 110),
            ConsumableValue = GetU(tail, 116),
            IsConsumable = GetB(tail, 117),
            GemCategory = GetB(tail, 124),
            EquippableFlag = GetB(tail, 125),
            HasEffect = GetB(tail, 126),
            EffectType = GetB(tail, 127),
            EffectStrength = GetS(tail, 128),

            RawColumns = rawColumns
        };
    }


    private static uint ParseUInt(string s)
    {
        return uint.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static ushort ParseUShort(string s)
    {
        return ushort.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;
    }

    private static byte ParseByte(string s)
    {
        return byte.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
    }

    private static float ParseFloat(string s)
    {
        return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }
}