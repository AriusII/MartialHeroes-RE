using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class LensFlareConfigParser
{
    private const string KwSpotCount = "SPOT_COUNT";
    private const string KwTextureCount = "TEXTURE_COUNT";
    private const string KwIntensityBorder = "INTENSITY_BORDER";
    private const string KwSpot = "SPOT";
    private const string KwBegin = "BEGIN";
    private const string KwEnd = "END";
    private const string KwTextureId = "TEXTURE_ID";
    private const string KwRadius = "RADIUS";
    private const string KwPosition = "POSITION";
    private const string KwColor = "COLOR";

    private static readonly char[] Separators = [' ', '\t', '\r', ','];

    static LensFlareConfigParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static LensFlareConfig Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static LensFlareConfig Parse(ReadOnlySpan<byte> span)
    {
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    public static LensFlareConfig ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var spots = new List<LensFlareSpot>();
        var spotCount = 0;
        var textureCount = 0;
        var intensityBorder = 0f;

        var inSpot = false;
        var sTexId = 0;
        var sRadius = 0f;
        var sPosition = 0f;
        byte sColorR = 0;
        byte sColorG = 0;
        byte sColorB = 0;
        byte sColorA = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var tokens = rawLine.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            var key = tokens[0];

            if (key.Equals(KwSpot, StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length >= 2 && tokens[^1].Equals(KwBegin, StringComparison.OrdinalIgnoreCase))
                {
                    inSpot = true;
                    sTexId = 0;
                    sRadius = 0f;
                    sPosition = 0f;
                    sColorR = 0;
                    sColorG = 0;
                    sColorB = 0;
                    sColorA = 0;
                }
                else if (tokens.Length >= 2 && tokens[1].Equals(KwEnd, StringComparison.OrdinalIgnoreCase))
                {
                    if (inSpot)
                        spots.Add(new LensFlareSpot(sTexId, sRadius, sPosition, sColorR, sColorG, sColorB, sColorA));
                    inSpot = false;
                }

                continue;
            }

            if (key.Equals(KwSpotCount, StringComparison.OrdinalIgnoreCase))
            {
                spotCount = ReadInt(tokens, 1);
            }
            else if (key.Equals(KwTextureCount, StringComparison.OrdinalIgnoreCase))
            {
                textureCount = ReadInt(tokens, 1);
            }
            else if (key.Equals(KwIntensityBorder, StringComparison.OrdinalIgnoreCase))
            {
                intensityBorder = ReadFloat(tokens, 1);
            }
            else if (key.Equals(KwTextureId, StringComparison.OrdinalIgnoreCase))
            {
                sTexId = ReadInt(tokens, 1);
            }
            else if (key.Equals(KwRadius, StringComparison.OrdinalIgnoreCase))
            {
                sRadius = ReadFloat(tokens, 1);
            }
            else if (key.Equals(KwPosition, StringComparison.OrdinalIgnoreCase))
            {
                sPosition = ReadFloat(tokens, 1);
            }
            else if (key.Equals(KwColor, StringComparison.OrdinalIgnoreCase))
            {
                sColorR = ReadByte(tokens, 1);
                sColorG = ReadByte(tokens, 2);
                sColorB = ReadByte(tokens, 3);
                sColorA = ReadByte(tokens, 4);
            }
        }

        if (inSpot)
            spots.Add(new LensFlareSpot(sTexId, sRadius, sPosition, sColorR, sColorG, sColorB, sColorA));

        return new LensFlareConfig(spotCount, textureCount, intensityBorder, spots.ToArray());
    }

    private static int ReadInt(string[] tokens, int index)
    {
        return index < tokens.Length &&
               int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static float ReadFloat(string[] tokens, int index)
    {
        return index < tokens.Length &&
               float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0f;
    }

    private static byte ReadByte(string[] tokens, int index)
    {
        if (index >= tokens.Length || !int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var value))
            return 0;

        if (value < 0)
            return 0;
        if (value > 255)
            return 255;

        return (byte)value;
    }
}