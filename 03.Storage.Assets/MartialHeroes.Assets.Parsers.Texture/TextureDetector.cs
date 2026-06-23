using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class TextureDetector
{
    private const int MinHeaderBytes = 8;

    private static ReadOnlySpan<byte> DdsMagic => "DDS "u8;

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> BmpMagic => "BM"u8;

    public static TextureDescriptor Detect(ReadOnlyMemory<byte> data)
    {
        var header = data.Length >= MinHeaderBytes
            ? data.Span[..MinHeaderBytes]
            : data.Span;

        var format = DetectFormat(header, data.Length);

        return new TextureDescriptor
        {
            Format = format,
            Payload = data
        };
    }

    private static TextureFormat DetectFormat(ReadOnlySpan<byte> header, int totalLength)
    {
        if (totalLength < 1)
            return TextureFormat.Unknown;

        if (header.Length >= DdsMagic.Length && header[..DdsMagic.Length].SequenceEqual(DdsMagic))
            return TextureFormat.Dds;

        if (header.Length >= PngMagic.Length && header[..PngMagic.Length].SequenceEqual(PngMagic))
            return TextureFormat.Png;

        if (header.Length >= BmpMagic.Length && header[..BmpMagic.Length].SequenceEqual(BmpMagic))
            return TextureFormat.Bmp;

        if (LooksTga(header, totalLength))
            return TextureFormat.Tga;

        return TextureFormat.Unknown;
    }

    private static bool LooksTga(ReadOnlySpan<byte> header, int totalLength)
    {
        if (header.Length < 3 || totalLength < 18)
            return false;

        var colorMapType = header[1];
        var imageType = header[2];

        if (colorMapType > 1)
            return false;

        return imageType is 1 or 2 or 3 or 9 or 10 or 11;
    }
}