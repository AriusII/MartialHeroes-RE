namespace MartialHeroes.Assets.Parsers.Texture.Models;

public enum TextureFormat
{
    Unknown = 0,

    Dds = 1,

    Tga = 2,

    Bmp = 3,

    Png = 4
}

public sealed class TextureDescriptor
{
    public required TextureFormat Format { get; init; }

    public required ReadOnlyMemory<byte> Payload { get; init; }
}