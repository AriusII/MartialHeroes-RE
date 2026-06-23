namespace MartialHeroes.Assets.Parsers.Texture.Models;

public enum ShaderType
{
    Vertex,

    Pixel
}

public sealed class ShaderSource
{
    public required ShaderType ShaderType { get; init; }

    public required string SourceText { get; init; }

    public required ReadOnlyMemory<byte> RawBytes { get; init; }
}