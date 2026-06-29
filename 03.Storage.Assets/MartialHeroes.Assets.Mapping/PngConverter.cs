using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Mapping;

public static class PngConverter
{
    private const uint DdsMagic = 0x20534444u;

    private const uint DdpfFourCC = 0x00000004u;
    private const uint DdpfRgb = 0x00000040u;
    private const uint DdpfAlphaPixels = 0x00000001u;

    private const int DdsHeaderSize = 124;

    private const int DdsDataOffset = 4 + DdsHeaderSize;

    private const byte PngColorTypeRgba = 6;

    private const byte PngFilterNone = 0;

    private static readonly uint FourCcDxt1 = FourCC('D', 'X', 'T', '1');
    private static readonly uint FourCcDxt3 = FourCC('D', 'X', 'T', '3');
    private static readonly uint FourCcDxt5 = FourCC('D', 'X', 'T', '5');


    private static readonly uint[] Crc32Table = BuildCrc32Table();


    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];


    public static void WritePng(TextureDescriptor descriptor, Stream output)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(output);

        switch (descriptor.Format)
        {
            case TextureFormat.Dds:
                WritePngFromDds(descriptor.Payload.Span, output);
                break;

            default:
                throw new NotSupportedException(
                    $"Texture format {descriptor.Format} is not supported by PngConverter. " +
                    "Only DDS (DXT1/DXT3/DXT5/RGBA8) payloads are currently handled.");
        }
    }


    private static void WritePngFromDds(ReadOnlySpan<byte> dds, Stream output)
    {
        if (dds.Length < DdsDataOffset)
            throw new InvalidDataException("DDS payload is too short to contain a valid header.");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(dds);
        if (magic != DdsMagic)
            throw new InvalidDataException("DDS magic bytes missing.");

        var hdr = dds.Slice(4, DdsHeaderSize);
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr[8..]);
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr[12..]);

        var pf = hdr[72..];
        var pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(pf[4..]);
        var fourCC = BinaryPrimitives.ReadUInt32LittleEndian(pf[8..]);
        var rgbBitCount = BinaryPrimitives.ReadUInt32LittleEndian(pf[12..]);

        var pixels = dds[DdsDataOffset..];

        byte[] rgba;
        if ((pfFlags & DdpfFourCC) != 0)
        {
            if (fourCC == FourCcDxt1)
                rgba = DecodeDxt1(pixels, width, height);
            else if (fourCC == FourCcDxt3)
                rgba = DecodeDxt3(pixels, width, height);
            else if (fourCC == FourCcDxt5)
                rgba = DecodeDxt5(pixels, width, height);
            else
                throw new NotSupportedException(
                    $"DDS FourCC 0x{fourCC:X8} is not supported. Only DXT1/DXT3/DXT5 are implemented.");
        }
        else if ((pfFlags & DdpfRgb) != 0 && (rgbBitCount == 32 || rgbBitCount == 24))
        {
            var rMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[16..]);
            var gMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[20..]);
            var bMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[24..]);
            var aMask = (pfFlags & DdpfAlphaPixels) != 0
                ? BinaryPrimitives.ReadUInt32LittleEndian(pf[28..])
                : 0u;
            rgba = DecodeUncompressedRgb(pixels, width, height, (int)(rgbBitCount / 8), rMask, gMask, bMask, aMask);
        }
        else
        {
            throw new NotSupportedException(
                $"DDS pixel format flags 0x{pfFlags:X8} with bit count {rgbBitCount} is not supported.");
        }

        WritePngRgba8(rgba, width, height, output);
    }


    private static byte[] DecodeDxt1(ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var src = 0;

        var palette = new uint[4];

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[src..]);
            var c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 2)..]);
            var selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 4)..]);
            src += 8;

            Rgb565ToRgb8(c0Raw, out var r0, out var g0, out var b0);
            Rgb565ToRgb8(c1Raw, out var r1, out var g1, out var b1);

            palette[0] = Pack(r0, g0, b0, 255);
            palette[1] = Pack(r1, g1, b1, 255);

            if (c0Raw > c1Raw)
            {
                palette[2] = Pack(
                    (byte)((2 * r0 + r1 + 1) / 3),
                    (byte)((2 * g0 + g1 + 1) / 3),
                    (byte)((2 * b0 + b1 + 1) / 3),
                    255);
                palette[3] = Pack(
                    (byte)((r0 + 2 * r1 + 1) / 3),
                    (byte)((g0 + 2 * g1 + 1) / 3),
                    (byte)((b0 + 2 * b1 + 1) / 3),
                    255);
            }
            else
            {
                palette[2] = Pack(
                    (byte)((r0 + r1) / 2),
                    (byte)((g0 + g1) / 2),
                    (byte)((b0 + b1) / 2),
                    255);
                palette[3] = 0u;
            }

            WriteDxtBlock(rgba, bx, by, width, height, palette, selectors, 2);
        }

        return rgba;
    }


    private static byte[] DecodeDxt3(ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var src = 0;

        var alphas = new byte[16];
        var palette = new uint[4];

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            for (var i = 0; i < 8; i++)
            {
                var ab = data[src + i];
                alphas[i * 2] = (byte)((ab & 0x0F) * 17);
                alphas[i * 2 + 1] = (byte)((ab >> 4) * 17);
            }

            var c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 8)..]);
            var c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 10)..]);
            var selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 12)..]);
            src += 16;

            Rgb565ToRgb8(c0Raw, out var r0, out var g0, out var b0);
            Rgb565ToRgb8(c1Raw, out var r1, out var g1, out var b1);

            palette[0] = Pack(r0, g0, b0, 255);
            palette[1] = Pack(r1, g1, b1, 255);
            palette[2] = Pack(
                (byte)((2 * r0 + r1 + 1) / 3),
                (byte)((2 * g0 + g1 + 1) / 3),
                (byte)((2 * b0 + b1 + 1) / 3),
                255);
            palette[3] = Pack(
                (byte)((r0 + 2 * r1 + 1) / 3),
                (byte)((g0 + 2 * g1 + 1) / 3),
                (byte)((b0 + 2 * b1 + 1) / 3),
                255);

            WriteDxtBlockWithAlpha(rgba, bx, by, width, height, palette, selectors, alphas);
        }

        return rgba;
    }


    private static byte[] DecodeDxt5(ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var src = 0;

        var alphaPalette = new byte[8];
        var alphas = new byte[16];
        var palette = new uint[4];

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var a0 = data[src];
            var a1 = data[src + 1];

            alphaPalette[0] = a0;
            alphaPalette[1] = a1;

            if (a0 > a1)
            {
                alphaPalette[2] = (byte)((6 * a0 + 1 * a1 + 3) / 7);
                alphaPalette[3] = (byte)((5 * a0 + 2 * a1 + 3) / 7);
                alphaPalette[4] = (byte)((4 * a0 + 3 * a1 + 3) / 7);
                alphaPalette[5] = (byte)((3 * a0 + 4 * a1 + 3) / 7);
                alphaPalette[6] = (byte)((2 * a0 + 5 * a1 + 3) / 7);
                alphaPalette[7] = (byte)((1 * a0 + 6 * a1 + 3) / 7);
            }
            else
            {
                alphaPalette[2] = (byte)((4 * a0 + 1 * a1 + 2) / 5);
                alphaPalette[3] = (byte)((3 * a0 + 2 * a1 + 2) / 5);
                alphaPalette[4] = (byte)((2 * a0 + 3 * a1 + 2) / 5);
                alphaPalette[5] = (byte)((1 * a0 + 4 * a1 + 2) / 5);
                alphaPalette[6] = 0;
                alphaPalette[7] = 255;
            }

            var alphaBits = data[src + 2]
                            | ((ulong)data[src + 3] << 8)
                            | ((ulong)data[src + 4] << 16)
                            | ((ulong)data[src + 5] << 24)
                            | ((ulong)data[src + 6] << 32)
                            | ((ulong)data[src + 7] << 40);

            for (var i = 0; i < 16; i++)
            {
                var idx = (int)((alphaBits >> (i * 3)) & 0x7);
                alphas[i] = alphaPalette[idx];
            }

            var c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 8)..]);
            var c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 10)..]);
            var selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 12)..]);
            src += 16;

            Rgb565ToRgb8(c0Raw, out var r0, out var g0, out var b0);
            Rgb565ToRgb8(c1Raw, out var r1, out var g1, out var b1);

            palette[0] = Pack(r0, g0, b0, 255);
            palette[1] = Pack(r1, g1, b1, 255);
            palette[2] = Pack(
                (byte)((2 * r0 + r1 + 1) / 3),
                (byte)((2 * g0 + g1 + 1) / 3),
                (byte)((2 * b0 + b1 + 1) / 3),
                255);
            palette[3] = Pack(
                (byte)((r0 + 2 * r1 + 1) / 3),
                (byte)((g0 + 2 * g1 + 1) / 3),
                (byte)((b0 + 2 * b1 + 1) / 3),
                255);

            WriteDxtBlockWithAlpha(rgba, bx, by, width, height, palette, selectors, alphas);
        }

        return rgba;
    }


    private static byte[] DecodeUncompressedRgb(
        ReadOnlySpan<byte> data, int width, int height, int bytesPerPixel,
        uint rMask, uint gMask, uint bMask, uint aMask)
    {
        var rgba = new byte[width * height * 4];
        var texelCount = width * height;

        for (var i = 0; i < texelCount; i++)
        {
            var src = i * bytesPerPixel;
            uint pixel = data[src];
            pixel |= (uint)data[src + 1] << 8;
            pixel |= (uint)data[src + 2] << 16;
            if (bytesPerPixel >= 4)
                pixel |= (uint)data[src + 3] << 24;

            rgba[i * 4 + 0] = ExtractChannel(pixel, rMask);
            rgba[i * 4 + 1] = ExtractChannel(pixel, gMask);
            rgba[i * 4 + 2] = ExtractChannel(pixel, bMask);
            rgba[i * 4 + 3] = aMask != 0 ? ExtractChannel(pixel, aMask) : (byte)255;
        }

        return rgba;
    }

    private static byte ExtractChannel(uint pixel, uint mask)
    {
        if (mask == 0) return 0;
        var bits = pixel & mask;
        var shift = BitOperations.TrailingZeroCount(mask);
        bits >>= shift;
        var maskBits = 32 - BitOperations.LeadingZeroCount(mask >> shift);
        if (maskBits >= 8) return (byte)(bits >> (maskBits - 8));
        return (byte)(bits * 255 / ((1u << maskBits) - 1u));
    }


    private static void WriteDxtBlock(
        byte[] rgba, int bx, int by, int width, int height,
        ReadOnlySpan<uint> palette, uint selectors, int bitsPerSelector)
    {
        var selectorMask = (1u << bitsPerSelector) - 1u;

        for (var row = 0; row < 4; row++)
        {
            var py = by * 4 + row;
            if (py >= height) continue;

            for (var col = 0; col < 4; col++)
            {
                var px = bx * 4 + col;
                if (px >= width) continue;

                var texelIndex = row * 4 + col;
                var shift = texelIndex * bitsPerSelector;
                var palIdx = (int)((selectors >> shift) & selectorMask);

                var packed = palette[palIdx];
                var dst = (py * width + px) * 4;
                rgba[dst + 0] = (byte)(packed & 0xFF);
                rgba[dst + 1] = (byte)((packed >> 8) & 0xFF);
                rgba[dst + 2] = (byte)((packed >> 16) & 0xFF);
                rgba[dst + 3] = (byte)((packed >> 24) & 0xFF);
            }
        }
    }

    private static void WriteDxtBlockWithAlpha(
        byte[] rgba, int bx, int by, int width, int height,
        ReadOnlySpan<uint> palette, uint selectors, ReadOnlySpan<byte> alphas)
    {
        for (var row = 0; row < 4; row++)
        {
            var py = by * 4 + row;
            if (py >= height) continue;

            for (var col = 0; col < 4; col++)
            {
                var px = bx * 4 + col;
                if (px >= width) continue;

                var texelIndex = row * 4 + col;
                var shift = texelIndex * 2;
                var palIdx = (int)((selectors >> shift) & 3u);

                var packed = palette[palIdx];
                var dst = (py * width + px) * 4;
                rgba[dst + 0] = (byte)(packed & 0xFF);
                rgba[dst + 1] = (byte)((packed >> 8) & 0xFF);
                rgba[dst + 2] = (byte)((packed >> 16) & 0xFF);
                rgba[dst + 3] = alphas[texelIndex];
            }
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rgb565ToRgb8(ushort v, out byte r, out byte g, out byte b)
    {
        var ri = (v >> 11) & 0x1F;
        var gi = (v >> 5) & 0x3F;
        var bi = (v >> 0) & 0x1F;
        r = (byte)((ri << 3) | (ri >> 2));
        g = (byte)((gi << 2) | (gi >> 4));
        b = (byte)((bi << 3) | (bi >> 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Pack(byte r, byte g, byte b, byte a)
    {
        return r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FourCC(char a, char b, char c, char d)
    {
        return (byte)a | ((uint)(byte)b << 8) | ((uint)(byte)c << 16) | ((uint)(byte)d << 24);
    }


    public static void WritePngRgba8(byte[] rgba, int width, int height, Stream output)
    {
        output.Write(PngSignature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = PngColorTypeRgba;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdrData);

        var filtered = FilterRgbaImage(rgba, width, height);
        var compressed = ZlibDeflate(filtered);
        WriteChunk(output, "IDAT"u8, compressed);

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static byte[] FilterRgbaImage(byte[] rgba, int width, int height)
    {
        var stride = width * 4;
        var filtered = new byte[height * (stride + 1)];

        for (var row = 0; row < height; row++)
        {
            var outBase = row * (stride + 1);
            filtered[outBase] = PngFilterNone;
            rgba.AsSpan(row * stride, stride).CopyTo(filtered.AsSpan(outBase + 1));
        }

        return filtered;
    }

    private static byte[] ZlibDeflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true))
        {
            zlib.Write(raw);
        }

        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes);
        output.Write(type);
        if (data.Length > 0) output.Write(data);

        var crc = Crc32Update(0xFFFFFFFFu, type);
        crc = Crc32Update(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint[] BuildCrc32Table()
    {
        const uint Poly = 0xEDB88320u;
        var table = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (uint)i;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
            table[i] = crc;
        }

        return table;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];
        return crc;
    }
}