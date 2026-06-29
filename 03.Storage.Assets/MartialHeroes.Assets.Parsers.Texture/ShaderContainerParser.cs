using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class ShaderContainerParser
{
    private const string VertexToken = "vs";
    private const string PixelToken = "ps";

    public static ShaderSource Parse(ReadOnlyMemory<byte> data)
    {
        return ParseCore(data.Span, data);
    }

    private static ShaderSource ParseCore(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> rawBytes)
    {
        var text = Encoding.Latin1.GetString(span);

        var cursor = text.AsSpan();
        var versionLine = ReadOnlySpan<char>.Empty;
        var found = false;

        while (cursor.Length > 0)
        {
            var lineEnd = FindLineEnd(cursor);
            ReadOnlySpan<char> line;
            if (lineEnd < 0)
            {
                line = cursor;
                cursor = ReadOnlySpan<char>.Empty;
            }
            else
            {
                line = cursor[..lineEnd];
                cursor = cursor[lineEnd..];
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed[0] == ';')
                continue;

            versionLine = trimmed;
            found = true;
            break;
        }

        if (!found)
            throw new InvalidDataException(
                "Shader parse error: no version line found before end of file.");

        ShaderType shaderType;
        if (versionLine.StartsWith(VertexToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            shaderType = ShaderType.Vertex;
        else if (versionLine.StartsWith(PixelToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            shaderType = ShaderType.Pixel;
        else
            throw new InvalidDataException(
                $"Shader parse error: version line '{versionLine.ToString()}' does not begin with 'vs' or 'ps'.");

        var shaderModel = ParseVersion(versionLine);

        return new ShaderSource
        {
            ShaderType = shaderType,
            ShaderModel = shaderModel,
            SourceText = text,
            RawBytes = rawBytes
        };
    }

    private static ShaderModelVersion ParseVersion(ReadOnlySpan<char> versionLine)
    {
        var i = VertexToken.Length;

        if (i >= versionLine.Length || versionLine[i] != '.')
            throw new InvalidDataException(
                $"Shader parse error: version line '{versionLine.ToString()}' is missing the '.major.minor' suffix.");

        i++;
        var major = ReadDigits(versionLine, ref i);

        if (i >= versionLine.Length || versionLine[i] != '.')
            throw new InvalidDataException(
                $"Shader parse error: version line '{versionLine.ToString()}' is missing the '.minor' field.");

        i++;
        var minor = ReadDigits(versionLine, ref i);

        return new ShaderModelVersion(major, minor);
    }

    private static int ReadDigits(ReadOnlySpan<char> text, ref int i)
    {
        var start = i;
        var value = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9')
        {
            value = value * 10 + (text[i] - '0');
            i++;
        }

        if (i == start)
            throw new InvalidDataException(
                $"Shader parse error: version line '{text.ToString()}' has a non-numeric version component.");

        return value;
    }

    private static int FindLineEnd(ReadOnlySpan<char> text)
    {
        var lf = text.IndexOf('\n');
        return lf < 0 ? -1 : lf + 1;
    }
}