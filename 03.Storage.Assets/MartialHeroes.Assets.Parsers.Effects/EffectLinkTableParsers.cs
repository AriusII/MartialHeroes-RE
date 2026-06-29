using System.Globalization;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

internal static class TokenParse
{
    public static int ReadInt(ref TokenCursor cursor, string fileLabel, int record, string field)
    {
        if (!cursor.TryNext(out var token))
            throw new InvalidDataException(
                $"{fileLabel} parse error: ran out of tokens at record {record} reading '{field}'.");
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $"{fileLabel} parse error: token '{token.ToString()}' at record {record} field '{field}' is not an integer.");
        return value;
    }

    public static float ReadFloat(ref TokenCursor cursor, string fileLabel, int record, string field)
    {
        if (!cursor.TryNext(out var token))
            throw new InvalidDataException(
                $"{fileLabel} parse error: ran out of tokens at record {record} reading '{field}'.");
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $"{fileLabel} parse error: token '{token.ToString()}' at record {record} field '{field}' is not a float.");
        return value;
    }

    public static int ReadCount(ref TokenCursor cursor, string fileLabel)
    {
        if (!cursor.TryNext(out var token) ||
            !int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count < 0)
            throw new InvalidDataException(
                $"{fileLabel} parse error: missing or invalid leading record count.");
        return count;
    }
}

public static class ItemJointEffParser
{
    private const string FileLabel = "itemjointeff.txt";

    public static ItemJointEffTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ItemJointEffTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Cp949Encoding.Instance.GetString(span);
        var cursor = new TokenCursor(text);

        var count = TokenParse.ReadCount(ref cursor, FileLabel);
        var records = new List<ItemJointEffRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var groupKey = TokenParse.ReadInt(ref cursor, FileLabel, i, "group_key");
            var effectId = TokenParse.ReadInt(ref cursor, FileLabel, i, "effect_id");
            var field2 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field2");
            var field3 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field3");
            var field4 = TokenParse.ReadFloat(ref cursor, FileLabel, i, "field4");
            var field5 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field5");

            if (effectId == 0)
                continue;

            records.Add(new ItemJointEffRecord
            {
                GroupKey = groupKey,
                EffectId = effectId,
                Field2 = field2,
                Field3 = field3,
                Field4 = field4,
                Field5 = (byte)field5
            });
        }

        return new ItemJointEffTable(records.ToArray());
    }
}

public static class MobJointEffParser
{
    private const string FileLabel = "mobjointeff.txt";

    public static MobJointEffTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MobJointEffTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Cp949Encoding.Instance.GetString(span);
        var cursor = new TokenCursor(text);

        var count = TokenParse.ReadCount(ref cursor, FileLabel);
        var records = new List<MobJointEffRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var classToken = TokenParse.ReadInt(ref cursor, FileLabel, i, "class");
            var offsetToken = TokenParse.ReadInt(ref cursor, FileLabel, i, "offset");
            var field2 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field2");
            var field3 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field3");
            var field4 = TokenParse.ReadFloat(ref cursor, FileLabel, i, "field4");
            var field5 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field5");

            records.Add(new MobJointEffRecord
            {
                ClassToken = classToken,
                OffsetToken = offsetToken,
                Field2 = field2,
                Field3 = field3,
                Field4 = field4,
                Field5 = (byte)field5
            });
        }

        return new MobJointEffTable(records.ToArray());
    }
}

public static class TotalMugongParser
{
    private const string FileLabel = "totalmugong.txt";

    public static TotalMugongTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static TotalMugongTable Parse(ReadOnlySpan<byte> span)
    {
        var text = Cp949Encoding.Instance.GetString(span);
        var cursor = new TokenCursor(text);

        var count = TokenParse.ReadCount(ref cursor, FileLabel);
        var records = new List<TotalMugongRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var field1 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field1");
            var field2 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field2");
            var field3 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field3");
            var field4 = TokenParse.ReadInt(ref cursor, FileLabel, i, "field4");

            records.Add(new TotalMugongRecord
            {
                Field1 = field1,
                Field2 = field2,
                Field3 = field3,
                Field4 = field4
            });
        }

        return new TotalMugongTable(records.ToArray());
    }
}

public static class SwordLightTableParser
{
    public static SwordLightTable Parse(ReadOnlyMemory<byte> data, string fileLabel)
    {
        return Parse(data.Span, fileLabel);
    }

    public static SwordLightTable Parse(ReadOnlySpan<byte> span, string fileLabel)
    {
        var text = Cp949Encoding.Instance.GetString(span);
        var lines = new LineCursor(text);

        if (!lines.TryNext(out var firstLine))
            throw new InvalidDataException($"{fileLabel} parse error: file is empty.");

        var firstCursor = new TokenCursor(firstLine);
        var count = TokenParse.ReadCount(ref firstCursor, fileLabel);

        var records = new List<SwordLightRecord>(count);
        var read = 0;

        while (read < count && lines.TryNext(out var line))
        {
            if (line.Trim().IsEmpty)
                continue;

            var tokenCursor = new TokenCursor(line);
            var tokens = new List<string>();
            while (tokenCursor.TryNext(out var token))
                tokens.Add(token.ToString());

            read++;

            if (tokens.Count < 2)
                continue;

            if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException(
                    $"{fileLabel} parse error: leading token '{tokens[0]}' at record {read - 1} is not an integer.");

            records.Add(new SwordLightRecord
            {
                Id = id,
                TextureName = tokens[^1],
                Tokens = tokens.ToArray()
            });
        }

        return new SwordLightTable(records.ToArray());
    }
}