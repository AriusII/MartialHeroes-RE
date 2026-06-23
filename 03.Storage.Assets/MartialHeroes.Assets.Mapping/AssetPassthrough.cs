using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Mapping;

public static class AssetPassthrough
{
    private static ReadOnlySpan<byte> PngMagic =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> BmpMagic => [0x42, 0x4D];

    private static ReadOnlySpan<byte> DdsMagic => [0x44, 0x44, 0x53, 0x20];

    private static ReadOnlySpan<byte> OggMagic => [0x4F, 0x67, 0x67, 0x53];

    private static ReadOnlySpan<byte> RiffMagic => [0x52, 0x49, 0x46, 0x46];

    private static ReadOnlySpan<byte> WaveType => [0x57, 0x41, 0x56, 0x45];


    public static ImagePassthroughResult PassthroughImage(
        ReadOnlyMemory<byte> rawBytes,
        string? extensionHint = null)
    {
        var span = rawBytes.Span;

        if (span.Length >= 8 && span[..8].SequenceEqual(PngMagic))
        {
            var (w, h) = ReadPngDimensions(span);
            return new ImagePassthroughResult(
                rawBytes,
                ImageFormat.Png,
                w,
                h);
        }

        if (span.Length >= 2 && span[..2].SequenceEqual(BmpMagic))
        {
            var (w, h) = ReadBmpDimensions(span);
            return new ImagePassthroughResult(
                rawBytes,
                ImageFormat.Bmp,
                w,
                h);
        }

        if (span.Length >= 4 && span[..4].SequenceEqual(DdsMagic))
        {
            var (w, h) = ReadDdsDimensions(span);
            return new ImagePassthroughResult(
                rawBytes,
                ImageFormat.Dds,
                w,
                h);
        }

        if (extensionHint is not null &&
            extensionHint.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var (w, h) = ReadTgaDimensions(span);
            return new ImagePassthroughResult(
                rawBytes,
                ImageFormat.Tga,
                w,
                h);
        }

        throw new NotSupportedException(
            $"Unrecognised image format (first 8 bytes: {FormatHex(span)}, hint: {extensionHint}).");
    }


    public static AudioPassthroughResult PassthroughAudio(ReadOnlyMemory<byte> rawBytes)
    {
        var span = rawBytes.Span;

        if (span.Length >= 4 && span[..4].SequenceEqual(OggMagic))
            return new AudioPassthroughResult(rawBytes, AudioFormat.OggVorbis);

        if (span.Length >= 12 && span[..4].SequenceEqual(RiffMagic) && span[8..12].SequenceEqual(WaveType))
            return new AudioPassthroughResult(rawBytes, AudioFormat.RiffWave);

        throw new NotSupportedException(
            $"Unrecognised audio format (first 12 bytes: {FormatHex(span)}).");
    }


    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 24) return (0, 0);
        var w = (int)BinaryPrimitives.ReadUInt32BigEndian(span[16..]);
        var h = (int)BinaryPrimitives.ReadUInt32BigEndian(span[20..]);
        return (w, h);
    }

    private static (int Width, int Height) ReadBmpDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x1A) return (0, 0);
        var w = BinaryPrimitives.ReadInt32LittleEndian(span[0x12..]);
        var h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(span[0x16..]));
        return (w, h);
    }

    private static (int Width, int Height) ReadDdsDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x14) return (0, 0);
        var h = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);
        var w = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);
        return (w, h);
    }

    private static (int Width, int Height) ReadTgaDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x10) return (0, 0);
        int w = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0C..]);
        int h = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0E..]);
        return (w, h);
    }


    private static string FormatHex(ReadOnlySpan<byte> span)
    {
        var count = Math.Min(12, span.Length);
        var sb = new StringBuilder(count * 3);
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(span[i].ToString("X2"));
        }

        return sb.ToString();
    }
}

public enum ImageFormat
{
    Png,

    Bmp,

    Dds,

    Tga
}

public enum AudioFormat
{
    OggVorbis,

    RiffWave
}

public sealed record ImagePassthroughResult(
    ReadOnlyMemory<byte> Bytes,
    ImageFormat Format,
    int Width,
    int Height);

public sealed record AudioPassthroughResult(
    ReadOnlyMemory<byte> Bytes,
    AudioFormat Format);