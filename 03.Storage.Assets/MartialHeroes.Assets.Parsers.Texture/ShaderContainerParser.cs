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

        var remaining = text.AsSpan();
        var lineEnd = FindLineEnd(remaining);
        if (lineEnd < 0)
            lineEnd = remaining.Length;

        var firstLine = remaining[..lineEnd].TrimEnd('\r');

        ShaderType shaderType;
        if (firstLine.StartsWith(VertexToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            shaderType = ShaderType.Vertex;
        else if (firstLine.StartsWith(PixelToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            shaderType = ShaderType.Pixel;
        else
            throw new InvalidDataException(
                $"Shader parse error: version line '{firstLine.ToString()}' does not begin with 'vs' or 'ps'. " +
                "spec: Docs/RE/formats/shaders.md §Version Declaration Line.");

        return new ShaderSource
        {
            ShaderType = shaderType,
            SourceText = text,
            RawBytes = rawBytes
        };
    }

    private static int FindLineEnd(ReadOnlySpan<char> text)
    {
        var lf = text.IndexOf('\n');
        return lf < 0 ? -1 : lf + 1;
    }
}