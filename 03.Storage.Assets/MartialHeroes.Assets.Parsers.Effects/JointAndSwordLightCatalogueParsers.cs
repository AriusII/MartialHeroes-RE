using System.Globalization;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

public static class ItemJointEffectCatalogueParser
{
    public const string VfsPath = "data/effect/itemjointeff.txt";
    private const string FileLabel = "itemjointeff.txt";

    public static JointEffectEntry[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static JointEffectEntry[] Parse(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return [];

        var text = Cp949Encoding.Instance.GetString(span);
        var cursor = new TokenCursor(text);

        var count = TokenParse.ReadCount(ref cursor, FileLabel);
        var entries = new List<JointEffectEntry>(count);

        for (var i = 0; i < count; i++)
        {
            var mapKey = TokenParse.ReadInt(ref cursor, FileLabel, i, "map_key");
            var effectId = TokenParse.ReadInt(ref cursor, FileLabel, i, "effect_id");
            var boneNameMode = TokenParse.ReadInt(ref cursor, FileLabel, i, "bone_name_mode");
            var boneId = TokenParse.ReadInt(ref cursor, FileLabel, i, "bone_id");
            var scale = TokenParse.ReadFloat(ref cursor, FileLabel, i, "scale");
            var rotSource = TokenParse.ReadInt(ref cursor, FileLabel, i, "rot_source");

            if (effectId == 0)
                continue;

            entries.Add(new JointEffectEntry
            {
                MapKey = unchecked((uint)mapKey),
                EffectId = unchecked((uint)effectId),
                BoneNameMode = (byte)boneNameMode,
                BoneId = boneId,
                Scale = scale,
                RotSource = (byte)rotSource
            });
        }

        return entries.ToArray();
    }
}

public static class MobJointEffectCatalogueParser
{
    public const string VfsPath = "data/effect/mobjointeff.txt";
    private const string FileLabel = "mobjointeff.txt";

    public static MobJointEffectEntry[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MobJointEffectEntry[] Parse(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return [];

        var text = Cp949Encoding.Instance.GetString(span);
        var cursor = new TokenCursor(text);

        var count = TokenParse.ReadCount(ref cursor, FileLabel);
        var entries = new List<MobJointEffectEntry>(count);

        for (var i = 0; i < count; i++)
        {
            var classToken = TokenParse.ReadInt(ref cursor, FileLabel, i, "class");
            var offsetToken = TokenParse.ReadInt(ref cursor, FileLabel, i, "offset");
            var effectId = TokenParse.ReadInt(ref cursor, FileLabel, i, "effect_id");
            var boneId = TokenParse.ReadInt(ref cursor, FileLabel, i, "bone_id");
            var scale = TokenParse.ReadFloat(ref cursor, FileLabel, i, "scale");
            var rotSource = TokenParse.ReadInt(ref cursor, FileLabel, i, "rot_source");

            entries.Add(new MobJointEffectEntry
            {
                ClassToken = classToken,
                OffsetToken = offsetToken,
                Effect = new JointEffectEntry
                {
                    MapKey = 0,
                    EffectId = unchecked((uint)effectId),
                    BoneNameMode = 0,
                    BoneId = boneId,
                    Scale = scale,
                    RotSource = (byte)rotSource
                }
            });
        }

        return entries.ToArray();
    }
}

public static class SwordLightDescriptorParser
{
    public const string ItemVfsPath = "data/effect/itemswordlight.txt";
    public const string MobVfsPath = "data/effect/mobswordlight.txt";

    public static SwordLightEntry[] Parse(ReadOnlyMemory<byte> data, string fileLabel)
    {
        return Parse(data.Span, fileLabel);
    }

    public static SwordLightEntry[] Parse(ReadOnlySpan<byte> span, string fileLabel)
    {
        if (span.IsEmpty)
            return [];

        var text = Cp949Encoding.Instance.GetString(span);
        var lines = new LineCursor(text);

        if (!lines.TryNext(out var firstLine))
            throw new InvalidDataException($"{fileLabel} parse error: file is empty.");

        var firstCursor = new TokenCursor(firstLine);
        var count = TokenParse.ReadCount(ref firstCursor, fileLabel);

        var entries = new List<SwordLightEntry>(count);
        var read = 0;
        var tokens = new List<string>(8);

        while (read < count && lines.TryNext(out var line))
        {
            if (line.Trim().IsEmpty)
                continue;

            tokens.Clear();
            var tokenCursor = new TokenCursor(line);
            while (tokenCursor.TryNext(out var token))
                tokens.Add(token.ToString());

            read++;

            if (tokens.Count < 2)
                continue;

            if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
                throw new InvalidDataException(
                    $"{fileLabel} parse error: leading token '{tokens[0]}' at record {read - 1} is not an integer.");

            entries.Add(new SwordLightEntry
            {
                Key = unchecked((uint)key),
                Raw1 = ReadIntAt(tokens, 1),
                R = ReadFloatAt(tokens, 2),
                G = ReadFloatAt(tokens, 3),
                B = ReadFloatAt(tokens, 4),
                HandSelector = ReadIntAt(tokens, 5),
                Raw6 = ReadIntAt(tokens, 6),
                TextureName = tokens[^1]
            });
        }

        return entries.ToArray();
    }

    private static int ReadIntAt(List<string> tokens, int index)
    {
        if ((uint)index >= (uint)tokens.Count)
            return 0;
        return int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static float ReadFloatAt(List<string> tokens, int index)
    {
        if ((uint)index >= (uint)tokens.Count)
            return 0f;
        return float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0f;
    }
}