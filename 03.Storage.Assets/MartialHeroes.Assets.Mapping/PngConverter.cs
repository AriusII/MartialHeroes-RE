using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Converts a <see cref="TextureDescriptor"/> (whose payload is a DDS or raw RGBA8 buffer)
/// to a PNG byte stream.
///
/// Supported DDS surface formats (detected from the DDPF flags / fourCC in the DDS header):
///   DXT1 (BC1) — fully implemented (opaque and 1-bit alpha).
///   DXT3 (BC2) — partially implemented: alpha block decoded, colour block shared with DXT1 path.
///   DXT5 (BC3) — partially implemented: alpha block interpolated, colour block shared with DXT1 path.
///   Uncompressed RGBA8 (DDPF_RGBA, 32-bit) — pass-through decode.
///
/// DDS header layout sourced from the public DirectDraw Surface (DDS) file specification
/// (Microsoft Learn "DDS File Layout" / DXGI SDK docs), which is an open published format.
/// No proprietary game-specific knowledge is used here.
///
/// PNG output:
///   Hand-rolled minimal PNG (IHDR + IDAT + IEND).
///   IDAT payload is zlib-wrapped Deflate via <see cref="ZLibStream"/> (BCL).
///   CRC32 computed inline (ISO 3309 / ITU-T V.42 polynomial 0xEDB88320).
///   Color type 6 (RGBA, 8-bit channels).
///   No ancillary chunks (no gAMA, no tEXt, etc.) to keep output minimal and deterministic.
///   PNG spec: ISO/IEC 15948:2004 / libpng-manual §4 File Structure.
/// </summary>
public static class PngConverter
{
    // -------------------------------------------------------------------------
    // DDS header constants
    // Source: public Microsoft DDS documentation (not game-specific).
    // -------------------------------------------------------------------------

    /// <summary>DDS magic bytes: ASCII "DDS " = 0x20534444 LE. Public DDS spec §DDS_HEADER.</summary>
    private const uint DdsMagic = 0x20534444u;

    // DDS pixel format flags (DDPF_*). Public DDS spec §DDPIXELFORMAT.dwFlags.
    private const uint DdpfFourCC = 0x00000004u;
    private const uint DdpfRgb = 0x00000040u;
    private const uint DdpfAlphaPixels = 0x00000001u;

    // Common FourCC codes. Public DDS spec §DDPIXELFORMAT.dwFourCC.
    private static readonly uint FourCcDxt1 = FourCC('D', 'X', 'T', '1');
    private static readonly uint FourCcDxt3 = FourCC('D', 'X', 'T', '3');
    private static readonly uint FourCcDxt5 = FourCC('D', 'X', 'T', '5');

    // DDS_HEADER size = 124 bytes. Public DDS spec §DDS_HEADER.dwSize.
    private const int DdsHeaderSize = 124;

    // DDS file starts with 4-byte magic, then 124-byte header.
    private const int DdsDataOffset = 4 + DdsHeaderSize; // 128

    // -------------------------------------------------------------------------
    // PNG constants — ISO/IEC 15948 §4
    // -------------------------------------------------------------------------

    // PNG signature: 8 bytes. PNG spec §5.2 "PNG signature".
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Color type 6 = RGBA (4 channels, 8 bits each). PNG spec §4.1.2 "Color types".
    private const byte PngColorTypeRgba = 6;

    // Filter type 0 = None. PNG spec §9.2 "Filter types for filter method 0".
    private const byte PngFilterNone = 0;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Decodes the texture described by <paramref name="descriptor"/> and encodes it as PNG,
    /// writing the result to <paramref name="output"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown for unrecognized source formats (e.g. TGA, BMP, unknown DDS surface format).
    /// </exception>
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

    // -------------------------------------------------------------------------
    // DDS decode entry
    // -------------------------------------------------------------------------

    private static void WritePngFromDds(ReadOnlySpan<byte> dds, Stream output)
    {
        if (dds.Length < DdsDataOffset)
            throw new InvalidDataException("DDS payload is too short to contain a valid header.");

        // Verify magic. Public DDS spec §DDS_HEADER.
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(dds);
        if (magic != DdsMagic)
            throw new InvalidDataException("DDS magic bytes missing.");

        // DDS_HEADER starts at offset 4.
        // dwSize @ +0, dwFlags @ +4, dwHeight @ +8, dwWidth @ +12
        // Public DDS spec §DDS_HEADER field offsets.
        ReadOnlySpan<byte> hdr = dds.Slice(4, DdsHeaderSize);
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr[8..]);
        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr[12..]);

        // DDPIXELFORMAT starts at offset 76 within DDS_HEADER.
        // Public DDS spec §DDS_HEADER: ddspf @ dwSize+72 (0-based field offset 76 inside header).
        ReadOnlySpan<byte> pf = hdr[76..]; // DDPIXELFORMAT (32 bytes)
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(pf[4..]); // dwFlags @ +4
        uint fourCC = BinaryPrimitives.ReadUInt32LittleEndian(pf[8..]); // dwFourCC @ +8
        uint rgbBitCount = BinaryPrimitives.ReadUInt32LittleEndian(pf[12..]); // dwRGBBitCount @ +12

        ReadOnlySpan<byte> pixels = dds[DdsDataOffset..];

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
        else if ((pfFlags & DdpfRgb) != 0 && rgbBitCount == 32 && (pfFlags & DdpfAlphaPixels) != 0)
        {
            // Uncompressed RGBA8: read masks to determine channel layout.
            // Masks: dwRBitMask @ pf+16, dwGBitMask @ pf+20, dwBBitMask @ pf+24, dwABitMask @ pf+28.
            uint rMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[16..]);
            uint gMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[20..]);
            uint bMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[24..]);
            uint aMask = BinaryPrimitives.ReadUInt32LittleEndian(pf[28..]);
            rgba = DecodeUncompressedRgba32(pixels, width, height, rMask, gMask, bMask, aMask);
        }
        else
        {
            throw new NotSupportedException(
                $"DDS pixel format flags 0x{pfFlags:X8} with bit count {rgbBitCount} is not supported.");
        }

        WritePngRgba8(rgba, width, height, output);
    }

    // -------------------------------------------------------------------------
    // DXT1 (BC1) decoder
    // Spec: public "BC1 Block Compression" algorithm (OpenGL EXT_texture_compression_s3tc,
    //       S3TC licence is expired / broadly public).
    // Each 4×4 block is encoded in 8 bytes: 2×u16 colour endpoints + 4 bytes of 2-bit selectors.
    // -------------------------------------------------------------------------

    private static byte[] DecodeDxt1(ReadOnlySpan<byte> data, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int src = 0;

        // palette and alphas allocated once outside the block loop — CA2014 fix.
        uint[] palette = new uint[4];

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                // 8 bytes per DXT1 block.
                ushort c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[src..]);
                ushort c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 2)..]);
                uint selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 4)..]);
                src += 8;

                // Unpack RGB565 → R8G8B8.
                Rgb565ToRgb8(c0Raw, out byte r0, out byte g0, out byte b0);
                Rgb565ToRgb8(c1Raw, out byte r1, out byte g1, out byte b1);

                // Build colour palette.
                // BC1 spec: if c0 > c1 (as unsigned 16-bit), 4-colour mode; else 3-colour + transparent.
                palette[0] = Pack(r0, g0, b0, 255);
                palette[1] = Pack(r1, g1, b1, 255);

                if (c0Raw > c1Raw)
                {
                    // 4-colour mode (opaque). BC1 spec §"4-color block".
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
                    // 3-colour + transparent. BC1 spec §"3-color block".
                    palette[2] = Pack(
                        (byte)((r0 + r1) / 2),
                        (byte)((g0 + g1) / 2),
                        (byte)((b0 + b1) / 2),
                        255);
                    palette[3] = 0u; // transparent black
                }

                // Write 4×4 texels to output.
                WriteDxtBlock(rgba, bx, by, width, height, palette, selectors, bitsPerSelector: 2);
            }
        }

        return rgba;
    }

    // -------------------------------------------------------------------------
    // DXT3 (BC2) decoder
    // Spec: public "BC2 Block Compression" (S3TC / Microsoft DX compression docs).
    // 16 bytes per block: 8 bytes explicit 4-bit alpha + 8 bytes BC1 colour block.
    // -------------------------------------------------------------------------

    private static byte[] DecodeDxt3(ReadOnlySpan<byte> data, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int src = 0;

        // Allocate per-block working arrays once outside the loop — CA2014 fix.
        byte[] alphas = new byte[16];
        uint[] palette = new uint[4];

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                // First 8 bytes: 4-bit alpha for each of the 16 texels (row-major, low nibble first).
                // BC2 spec: each byte holds two 4-bit alpha values; expand to 8-bit by mult×17.
                for (int i = 0; i < 8; i++)
                {
                    byte ab = data[src + i];
                    alphas[i * 2] = (byte)((ab & 0x0F) * 17); // 0x0F → 0xFF, linear scale
                    alphas[i * 2 + 1] = (byte)((ab >> 4) * 17);
                }

                // Next 8 bytes: BC1 colour block.
                ushort c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 8)..]);
                ushort c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 10)..]);
                uint selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 12)..]);
                src += 16;

                Rgb565ToRgb8(c0Raw, out byte r0, out byte g0, out byte b0);
                Rgb565ToRgb8(c1Raw, out byte r1, out byte g1, out byte b1);

                // BC2 always uses 4-colour mode for the colour block regardless of c0/c1 order.
                // BC2 spec §"Colour information".
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

                // Write pixels with explicit alpha override.
                WriteDxtBlockWithAlpha(rgba, bx, by, width, height, palette, selectors, alphas);
            }
        }

        return rgba;
    }

    // -------------------------------------------------------------------------
    // DXT5 (BC3) decoder
    // Spec: public "BC3 Block Compression" (S3TC / Microsoft DX compression docs).
    // 16 bytes per block: 8 bytes interpolated alpha + 8 bytes BC1 colour block.
    // -------------------------------------------------------------------------

    private static byte[] DecodeDxt5(ReadOnlySpan<byte> data, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int src = 0;

        // Allocate per-block working arrays once outside the loop — CA2014 fix.
        byte[] alphaPalette = new byte[8];
        byte[] alphas = new byte[16];
        uint[] palette = new uint[4];

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                // BC3 alpha block: 2 reference values + 48 bits (6 bytes) of 3-bit selectors.
                byte a0 = data[src];
                byte a1 = data[src + 1];

                // Build 8-entry alpha palette. BC3 spec §"Alpha information".
                alphaPalette[0] = a0;
                alphaPalette[1] = a1;

                if (a0 > a1)
                {
                    // 8-alpha mode
                    alphaPalette[2] = (byte)((6 * a0 + 1 * a1 + 3) / 7);
                    alphaPalette[3] = (byte)((5 * a0 + 2 * a1 + 3) / 7);
                    alphaPalette[4] = (byte)((4 * a0 + 3 * a1 + 3) / 7);
                    alphaPalette[5] = (byte)((3 * a0 + 4 * a1 + 3) / 7);
                    alphaPalette[6] = (byte)((2 * a0 + 5 * a1 + 3) / 7);
                    alphaPalette[7] = (byte)((1 * a0 + 6 * a1 + 3) / 7);
                }
                else
                {
                    // 6-alpha + 0/255 endpoints mode
                    alphaPalette[2] = (byte)((4 * a0 + 1 * a1 + 2) / 5);
                    alphaPalette[3] = (byte)((3 * a0 + 2 * a1 + 2) / 5);
                    alphaPalette[4] = (byte)((2 * a0 + 3 * a1 + 2) / 5);
                    alphaPalette[5] = (byte)((1 * a0 + 4 * a1 + 2) / 5);
                    alphaPalette[6] = 0;
                    alphaPalette[7] = 255;
                }

                // 6 bytes of 3-bit alpha selectors (48 bits, 16 × 3-bit).
                // Pack into a ulong for easy bit extraction.
                ulong alphaBits = (ulong)data[src + 2]
                                  | ((ulong)data[src + 3] << 8)
                                  | ((ulong)data[src + 4] << 16)
                                  | ((ulong)data[src + 5] << 24)
                                  | ((ulong)data[src + 6] << 32)
                                  | ((ulong)data[src + 7] << 40);

                for (int i = 0; i < 16; i++)
                {
                    int idx = (int)((alphaBits >> (i * 3)) & 0x7);
                    alphas[i] = alphaPalette[idx];
                }

                // BC1 colour block at src+8.
                ushort c0Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 8)..]);
                ushort c1Raw = BinaryPrimitives.ReadUInt16LittleEndian(data[(src + 10)..]);
                uint selectors = BinaryPrimitives.ReadUInt32LittleEndian(data[(src + 12)..]);
                src += 16;

                Rgb565ToRgb8(c0Raw, out byte r0, out byte g0, out byte b0);
                Rgb565ToRgb8(c1Raw, out byte r1, out byte g1, out byte b1);

                // BC3 colour block also always uses 4-colour mode.
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
        }

        return rgba;
    }

    // -------------------------------------------------------------------------
    // Uncompressed RGBA32 decode
    // -------------------------------------------------------------------------

    private static byte[] DecodeUncompressedRgba32(
        ReadOnlySpan<byte> data, int width, int height,
        uint rMask, uint gMask, uint bMask, uint aMask)
    {
        byte[] rgba = new byte[width * height * 4];
        int texelCount = width * height;

        for (int i = 0; i < texelCount; i++)
        {
            uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
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
        uint bits = pixel & mask;
        // Shift right to align to LSB.
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        bits >>= shift;
        // Scale to 8-bit.
        int maskBits = 32 - System.Numerics.BitOperations.LeadingZeroCount(mask >> shift);
        if (maskBits >= 8) return (byte)(bits >> (maskBits - 8));
        // Replicate MSBs for correct 8-bit range.
        return (byte)((bits * 255) / ((1u << maskBits) - 1u));
    }

    // -------------------------------------------------------------------------
    // DXT block write helpers
    // -------------------------------------------------------------------------

    private static void WriteDxtBlock(
        byte[] rgba, int bx, int by, int width, int height,
        ReadOnlySpan<uint> palette, uint selectors, int bitsPerSelector)
    {
        uint selectorMask = (1u << bitsPerSelector) - 1u;

        for (int row = 0; row < 4; row++)
        {
            int py = by * 4 + row;
            if (py >= height) continue;

            for (int col = 0; col < 4; col++)
            {
                int px = bx * 4 + col;
                if (px >= width) continue;

                int texelIndex = row * 4 + col;
                int shift = texelIndex * bitsPerSelector;
                int palIdx = (int)((selectors >> shift) & selectorMask);

                uint packed = palette[palIdx];
                int dst = (py * width + px) * 4;
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
        for (int row = 0; row < 4; row++)
        {
            int py = by * 4 + row;
            if (py >= height) continue;

            for (int col = 0; col < 4; col++)
            {
                int px = bx * 4 + col;
                if (px >= width) continue;

                int texelIndex = row * 4 + col;
                int shift = texelIndex * 2;
                int palIdx = (int)((selectors >> shift) & 3u);

                uint packed = palette[palIdx];
                int dst = (py * width + px) * 4;
                rgba[dst + 0] = (byte)(packed & 0xFF);
                rgba[dst + 1] = (byte)((packed >> 8) & 0xFF);
                rgba[dst + 2] = (byte)((packed >> 16) & 0xFF);
                rgba[dst + 3] = alphas[texelIndex]; // override alpha from block
            }
        }
    }

    // -------------------------------------------------------------------------
    // RGB565 → RGB888 expansion
    // BC1 colour endpoint format. Public S3TC spec.
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rgb565ToRgb8(ushort v, out byte r, out byte g, out byte b)
    {
        // R: bits 15..11 (5 bits), G: bits 10..5 (6 bits), B: bits 4..0 (5 bits).
        // Scale: 5-bit → 8-bit by (x << 3) | (x >> 2);  6-bit → 8-bit by (x << 2) | (x >> 4).
        int ri = (v >> 11) & 0x1F;
        int gi = (v >> 5) & 0x3F;
        int bi = (v >> 0) & 0x1F;
        r = (byte)((ri << 3) | (ri >> 2));
        g = (byte)((gi << 2) | (gi >> 4));
        b = (byte)((bi << 3) | (bi >> 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Pack(byte r, byte g, byte b, byte a) =>
        r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FourCC(char a, char b, char c, char d) =>
        (byte)a | ((uint)(byte)b << 8) | ((uint)(byte)c << 16) | ((uint)(byte)d << 24);

    // -------------------------------------------------------------------------
    // PNG encoder — hand-rolled minimal implementation
    // PNG spec: ISO/IEC 15948:2004.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encodes an RGBA8 pixel array as a minimal PNG and writes it to <paramref name="output"/>.
    /// Color type 6 (RGBA), bit depth 8, no interlacing.
    /// PNG spec §4.1 "Critical chunks", §9 "Filtering", §12 "Compression".
    /// </summary>
    public static void WritePngRgba8(byte[] rgba, int width, int height, Stream output)
    {
        // Signature
        output.Write(PngSignature);

        // IHDR chunk (13 bytes of data)
        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8; // bit depth = 8
        ihdrData[9] = PngColorTypeRgba; // color type 6 = RGBA
        ihdrData[10] = 0; // compression method 0 (Deflate)
        ihdrData[11] = 0; // filter method 0
        ihdrData[12] = 0; // interlace method 0 (none)
        WriteChunk(output, "IHDR"u8, ihdrData);

        // IDAT chunk — filter then compress
        byte[] filtered = FilterRgbaImage(rgba, width, height);
        byte[] compressed = ZlibDeflate(filtered);
        WriteChunk(output, "IDAT"u8, compressed);

        // IEND chunk (no data)
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Applies PNG filter type 0 (None) to each scanline.
    /// Each scanline is prefixed with a filter byte (0x00).
    /// PNG spec §9.2 "Filter types for filter method 0".
    /// </summary>
    private static byte[] FilterRgbaImage(byte[] rgba, int width, int height)
    {
        int stride = width * 4;
        byte[] filtered = new byte[height * (stride + 1)]; // +1 per row for filter byte

        for (int row = 0; row < height; row++)
        {
            int outBase = row * (stride + 1);
            filtered[outBase] = PngFilterNone;
            rgba.AsSpan(row * stride, stride).CopyTo(filtered.AsSpan(outBase + 1));
        }

        return filtered;
    }

    /// <summary>
    /// Wraps <paramref name="raw"/> in a zlib container (CMF + FLG header, Deflate data, Adler-32 checksum)
    /// using <see cref="ZLibStream"/> from BCL (System.IO.Compression).
    /// PNG spec §12: IDAT uses the zlib compression format (RFC 1950 wrapper, Deflate RFC 1951).
    /// </summary>
    private static byte[] ZlibDeflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes one PNG chunk: [length u32BE][type 4B][data][CRC u32BE].
    /// CRC covers the type bytes + data bytes.
    /// PNG spec §5.3 "Chunk layout".
    /// </summary>
    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes);
        output.Write(type);
        if (data.Length > 0) output.Write(data);

        // CRC32 of type + data. PNG spec §5.3 "CRC algorithm".
        uint crc = Crc32Update(0xFFFFFFFFu, type);
        crc = Crc32Update(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    // -------------------------------------------------------------------------
    // CRC-32 — ISO 3309 / ITU-T V.42, polynomial 0xEDB88320 (reflected).
    // PNG spec §5.3 cites ISO 3309. The table approach is the standard implementation.
    // -------------------------------------------------------------------------

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        const uint Poly = 0xEDB88320u;
        var table = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint crc = (uint)i;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
            table[i] = crc;
        }

        return table;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];
        return crc;
    }
}