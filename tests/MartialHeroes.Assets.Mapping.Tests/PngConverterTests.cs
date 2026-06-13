using System.Buffers.Binary;
using System.IO.Compression;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

/// <summary>
/// Headless structural tests for <see cref="PngConverter"/>.
/// No GPU, no disk, no Godot dependency.
///
/// DXT1 reference values sourced from the public BC1 algorithm.
/// PNG validation uses manual chunk parsing per ISO/IEC 15948 §5.3.
/// </summary>
public sealed class PngConverterTests
{
    // -------------------------------------------------------------------------
    // PNG signature and IHDR structural tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WritePngRgba8_Output_HasValidPngSignature()
    {
        // PNG spec §5.2 "PNG signature": 8 bytes {89 50 4E 47 0D 0A 1A 0A}.
        byte[] rgba = new byte[4 * 4 * 4]; // 4×4 RGBA8
        using var ms = new MemoryStream();
        PngConverter.WritePngRgba8(rgba, 4, 4, ms);
        byte[] png = ms.ToArray();

        Assert.True(png.Length >= 8, "PNG must be at least 8 bytes.");
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]); // 'P'
        Assert.Equal(0x4E, png[2]); // 'N'
        Assert.Equal(0x47, png[3]); // 'G'
        Assert.Equal(0x0D, png[4]);
        Assert.Equal(0x0A, png[5]);
        Assert.Equal(0x1A, png[6]);
        Assert.Equal(0x0A, png[7]);
    }

    [Fact]
    public void WritePngRgba8_Output_HasValidIhdrChunk()
    {
        // PNG spec §4.1.1 "IHDR Image header".
        // IHDR must be the first chunk, data length = 13, chunk type = ASCII "IHDR".
        int width = 8, height = 4;
        byte[] rgba = new byte[width * height * 4];
        using var ms = new MemoryStream();
        PngConverter.WritePngRgba8(rgba, width, height, ms);
        byte[] png = ms.ToArray();

        // Chunk at offset 8: [length u32BE][type 4B][data][CRC u32BE]
        int chunkOffset = 8;
        uint chunkDataLength = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(chunkOffset));
        byte[] chunkType = png[(chunkOffset + 4)..(chunkOffset + 8)];

        Assert.Equal(13u, chunkDataLength);
        Assert.Equal("IHDR"u8.ToArray(), chunkType);

        // IHDR data starts at chunkOffset + 8.
        int dataStart = chunkOffset + 8;
        uint storedWidth = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataStart));
        uint storedHeight = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataStart + 4));
        byte bitDepth = png[dataStart + 8];
        byte colorType = png[dataStart + 9];
        byte compression = png[dataStart + 10];
        byte filter = png[dataStart + 11];
        byte interlace = png[dataStart + 12];

        Assert.Equal((uint)width, storedWidth);
        Assert.Equal((uint)height, storedHeight);
        Assert.Equal(8, bitDepth); // 8-bit channels
        Assert.Equal(6, colorType); // RGBA (color type 6)
        Assert.Equal(0, compression); // Deflate
        Assert.Equal(0, filter); // filter method 0
        Assert.Equal(0, interlace); // no interlace
    }

    [Fact]
    public void WritePngRgba8_Output_IhdrChunk_CrcIsValid()
    {
        // PNG spec §5.3 "Chunk layout": CRC covers chunk type + chunk data.
        byte[] rgba = new byte[2 * 2 * 4]; // 2×2 RGBA8
        using var ms = new MemoryStream();
        PngConverter.WritePngRgba8(rgba, 2, 2, ms);
        byte[] png = ms.ToArray();

        // IHDR chunk at offset 8; data length = 13; total chunk = 4+4+13+4 = 25 bytes.
        // CRC stored at offset 8 + 4 + 4 + 13 = 29.
        int crcOffset = 8 + 4 + 4 + 13;
        uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(crcOffset));

        // Re-compute CRC over chunk type (4 bytes) + data (13 bytes).
        uint expectedCrc = ComputeCrc32(png.AsSpan(8 + 4, 4 + 13));
        Assert.Equal(expectedCrc, storedCrc);
    }

    [Fact]
    public void WritePngRgba8_Output_HasIendChunk()
    {
        // PNG spec §4.1.3 "IEND Image trailer": must be the last chunk, data length 0.
        byte[] rgba = new byte[1 * 1 * 4];
        using var ms = new MemoryStream();
        PngConverter.WritePngRgba8(rgba, 1, 1, ms);
        byte[] png = ms.ToArray();

        // IEND is a fixed 12-byte chunk at the end: 4 (length=0) + 4 (type) + 0 (data) + 4 (crc).
        Assert.True(png.Length >= 12);
        int iendOffset = png.Length - 12;
        uint iendLength = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(iendOffset));
        string iendType = System.Text.Encoding.ASCII.GetString(png, iendOffset + 4, 4);
        Assert.Equal(0u, iendLength);
        Assert.Equal("IEND", iendType);
    }

    // -------------------------------------------------------------------------
    // DXT1 decode tests using a known synthetic 4×4 block
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal DDS byte array containing a single 4×4 DXT1 block.
    ///
    /// The block uses two known RGB565 endpoints and the 4-colour mode
    /// (c0 > c1 as unsigned 16-bit).
    ///
    /// Endpoint c0 = 0xF800 → RGB888 (255, 0, 0)  = opaque red.
    /// Endpoint c1 = 0x07E0 → RGB888 (0, 255, 0)  = opaque green.
    ///   (c0=0xF800 > c1=0x07E0: 4-colour mode)
    /// Selector byte = 0x00 → all texels in that row select palette[0] = red.
    ///
    /// BC1 spec: c0 R5G6B5: (31,0,0)→(255,0,0); c1 R5G6B5: (0,63,0)→(0,255,0).
    /// </summary>
    private static byte[] MakeDxt1Dds4x4(ushort c0, ushort c1, uint selectors)
    {
        // DDS layout: 4 magic + 124 header + 8 block data = 136 bytes.
        byte[] dds = new byte[136];
        // Magic: "DDS "
        dds[0] = 0x44;
        dds[1] = 0x44;
        dds[2] = 0x53;
        dds[3] = 0x20;

        // DDS_HEADER (124 bytes starting at offset 4)
        // dwSize = 124 at offset 4
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124u);
        // dwFlags — minimal valid flags (DDSD_CAPS|DDSD_HEIGHT|DDSD_WIDTH|DDSD_PIXELFORMAT = 0x1007)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(8), 0x1007u);
        // dwHeight @ +8 (header offset 8 = file offset 12)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), 4u); // height = 4
        // dwWidth @ +12 (file offset 16)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), 4u); // width  = 4

        // DDS_PIXELFORMAT embedded at file offset 0x4C (= DDS_HEADER byte 72).
        // spec: Docs/RE/formats/texture.md §DDS_PIXELFORMAT (embedded at offset 0x4C, 32 bytes)
        //   +0x00 dwSize @ file 0x4C, +0x04 dwFlags @ file 0x50,
        //   +0x08 dwFourCC @ file 0x54, +0x0C dwRGBBitCount @ file 0x58.
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x4C), 32u); // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x50), 4u); // dwFlags = DDPF_FOURCC
        // dwFourCC @ file 0x54: 'DXT1' = 44 58 54 31
        dds[0x54] = (byte)'D';
        dds[0x55] = (byte)'X';
        dds[0x56] = (byte)'T';
        dds[0x57] = (byte)'1';

        // Block data starts at offset 128 (4 magic + 124 header).
        // DXT1 block: c0 (2B LE) + c1 (2B LE) + selectors (4B LE)
        BinaryPrimitives.WriteUInt16LittleEndian(dds.AsSpan(128), c0);
        BinaryPrimitives.WriteUInt16LittleEndian(dds.AsSpan(130), c1);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(132), selectors);

        return dds;
    }

    [Fact]
    public void DecodeDxt1_KnownBlock_AllRedTexels()
    {
        // c0 = 0xF800 (R5G6B5: R=31,G=0,B=0 → RGB888 = 255,0,0)
        // c1 = 0x07E0 (R5G6B5: R=0,G=63,B=0 → RGB888 = 0,255,0)
        // selectors = 0x00000000 → all 16 texels select palette[0] = red (255,0,0,255)
        // 4-colour mode since c0(0xF800=63488) > c1(0x07E0=2016).
        // BC1 spec: RGB565 R5: (31<<3)|(31>>2) = 255; G6: (0<<2)|(0>>4) = 0; B5: same as R5 = 0.
        byte[] dds = MakeDxt1Dds4x4(0xF800, 0x07E0, 0x00000000u);

        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(dds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] png = ms.ToArray();

        // Decode the PNG pixels back and verify every texel is red.
        byte[] pixels = DecodePngPixels(png, out int w, out int h);
        Assert.Equal(4, w);
        Assert.Equal(4, h);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(255, pixels[i * 4 + 0]); // R
            Assert.Equal(0, pixels[i * 4 + 1]); // G
            Assert.Equal(0, pixels[i * 4 + 2]); // B
            Assert.Equal(255, pixels[i * 4 + 3]); // A
        }
    }

    [Fact]
    public void DecodeDxt1_KnownBlock_Palette1Texels()
    {
        // selectors = 0x55555555 → all texels select palette[1] = green (0,255,0,255)
        // c0 = 0xF800 (red), c1 = 0x07E0 (green), 4-colour mode (c0 > c1).
        byte[] dds = MakeDxt1Dds4x4(0xF800, 0x07E0, 0x55555555u);

        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(dds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] pixels = DecodePngPixels(ms.ToArray(), out _, out _);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(0, pixels[i * 4 + 0]); // R
            Assert.Equal(255, pixels[i * 4 + 1]); // G
            Assert.Equal(0, pixels[i * 4 + 2]); // B
            Assert.Equal(255, pixels[i * 4 + 3]); // A
        }
    }

    [Fact]
    public void DecodeDxt1_KnownBlock_4ColourMode_Palette2IsInterpolated()
    {
        // palette[2] in 4-colour mode = (2*c0 + c1) / 3.
        // c0 = (255,0,0), c1 = (0,255,0):
        //   palette[2].R = (2*255 + 0 + 1) / 3 = 511/3 = 170
        //   palette[2].G = (2*0 + 255 + 1) / 3 = 256/3 = 85
        //   BC1 spec §"4-color block".
        // selectors = 0xAAAAAAAA → all texels select palette[2].
        byte[] dds = MakeDxt1Dds4x4(0xF800, 0x07E0, 0xAAAAAAAAu);

        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(dds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] pixels = DecodePngPixels(ms.ToArray(), out _, out _);

        // Check first texel.
        Assert.Equal(170, pixels[0]); // R = (2*255+0+1)/3 = 170
        Assert.Equal(85, pixels[1]); // G = (2*0+255+1)/3 = 85
        Assert.Equal(0, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    // -------------------------------------------------------------------------
    // FourCC offset regression tests
    // spec: Docs/RE/formats/texture.md §DDS_PIXELFORMAT (embedded at offset 0x4C, 32 bytes)
    //   dwFourCC is at DDS_PIXELFORMAT relative offset +0x08 = file offset 0x54.
    //   Bug: previous code read from pf[8] where pf = hdr[76..] → file 0x58 (dwRGBBitCount),
    //   which is 0 for all block-compressed formats and caused every DXT conversion to fail.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal DDS header byte array with the given FourCC written at the
    /// spec-mandated file offset 0x54 (DDS_PIXELFORMAT+0x08).
    /// No pixel data — callers that only test header detection can use a 128-byte buffer.
    /// spec: Docs/RE/formats/texture.md §DDS_PIXELFORMAT (embedded at offset 0x4C, 32 bytes).
    /// </summary>
    private static byte[] MakeMinimalDdsHeader(uint fourCC, uint pfFlags, uint width = 4, uint height = 4)
    {
        // 128-byte header only — no pixel blocks.
        byte[] dds = new byte[128];

        // Magic "DDS " at file 0x00.  spec: Docs/RE/formats/texture.md §DDS_HEADER offset 0x00.
        dds[0] = 0x44;
        dds[1] = 0x44;
        dds[2] = 0x53;
        dds[3] = 0x20;

        // DDS_HEADER fields.  spec: Docs/RE/formats/texture.md §DDS_HEADER layout.
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x04), 124u); // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x08), 0x1007u); // dwFlags
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x0C), height); // dwHeight
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x10), width); // dwWidth

        // DDS_PIXELFORMAT at file offset 0x4C.  spec: Docs/RE/formats/texture.md §DDS_PIXELFORMAT.
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x4C), 32u); // dwSize  @ file 0x4C
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x50), pfFlags); // dwFlags @ file 0x50
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x54), fourCC); // dwFourCC @ file 0x54
        // dwRGBBitCount @ file 0x58 intentionally left 0x00000000 — confirms the converter
        // does NOT read FourCC from 0x58 (the off-by-4 regression location).

        return dds;
    }

    [Fact]
    public void FourCC_ReadAt_FileOffset_0x54_DetectsDxt1()
    {
        // Regression test: the converter must read dwFourCC from file offset 0x54.
        // The byte at 0x58 (dwRGBBitCount) is zero, which was the old (wrong) read location.
        // If the offset is wrong the FourCC will be 0x00000000 and the converter throws
        // NotSupportedException instead of performing DXT1 decode.
        // spec: Docs/RE/formats/texture.md §DDS_PIXELFORMAT +0x08 (file 0x54) = dwFourCC.
        uint dxt1FourCC = (byte)'D' | ((uint)'X' << 8) | ((uint)'T' << 16) | ((uint)'1' << 24);
        byte[] dds = MakeMinimalDdsHeader(dxt1FourCC, pfFlags: 0x00000004u); // DDPF_FOURCC

        // Append one 4×4 DXT1 block (8 bytes) so the decoder has pixel data to read.
        // Block: c0=0xFFFF (white), c1=0x0000 (black), selectors=0x00000000 → all palette[0].
        byte[] fullDds = new byte[128 + 8];
        dds.CopyTo(fullDds, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(fullDds.AsSpan(128), 0xFFFF); // c0 = white
        BinaryPrimitives.WriteUInt16LittleEndian(fullDds.AsSpan(130), 0x0000); // c1 = black
        BinaryPrimitives.WriteUInt32LittleEndian(fullDds.AsSpan(132), 0x00000000u); // all palette[0]

        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(fullDds),
        };

        // Must NOT throw — if FourCC is misread as 0, this would throw NotSupportedException.
        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        Assert.True(ms.Length > 0, "Converter must produce PNG output for a valid DXT1 DDS.");
    }

    [Fact]
    public void FourCC_Zero_WithUncompressedRgba_Detected_Correctly()
    {
        // Uncompressed RGBA32 path: DDPF_RGB | DDPF_ALPHAPIXELS, dwFourCC = 0, dwRGBBitCount = 32.
        // Verifies that the uncompressed branch is still reachable after the offset fix.
        // spec: Docs/RE/formats/texture.md §DDS uncompressed (UNVERIFIED but valid DDS).
        const uint pfFlagsUncompressed = 0x00000041u; // DDPF_RGB(0x40) | DDPF_ALPHAPIXELS(0x01)
        byte[] dds = MakeMinimalDdsHeader(fourCC: 0u, pfFlags: pfFlagsUncompressed);

        // Write dwRGBBitCount = 32 at file 0x58 and standard RGBA masks at 0x5C..0x6B.
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x58), 32u); // dwRGBBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x5C), 0x00FF0000u); // dwRBitMask
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x60), 0x0000FF00u); // dwGBitMask
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x64), 0x000000FFu); // dwBBitMask
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(0x68), 0xFF000000u); // dwABitMask

        // Append 4×4 uncompressed RGBA32 pixel data (64 bytes): all red (R=255,G=0,B=0,A=255).
        // Wire format with above masks: each pixel = 0x00FF0000 (B@byte0=0,G@byte1=0,R@byte2=255,A@byte3=0)
        // Wait — mask 0x00FF0000 extracts bits 16..23 as R.  LE pixel bytes: [B,G,R,A] = [0,0,255,255].
        // In LE u32: 0xFF0000FF (A at bit31..24 = 0xFF, R at bit23..16 = 0x00?).
        // Let's use simple A8R8G8B8 ordering: pixel u32 LE = BGRA bytes.
        // R mask 0x00FF0000 → R from bits 16..23 → byte[2] in LE = R.
        // Write pixel as bytes [B=0x00, G=0x00, R=0xFF, A=0xFF] = u32 LE 0xFF_FF_00_00.
        byte[] fullDds = new byte[128 + 64];
        dds.CopyTo(fullDds, 0);
        for (int i = 0; i < 16; i++)
        {
            int ofs = 128 + i * 4;
            fullDds[ofs + 0] = 0x00; // B channel
            fullDds[ofs + 1] = 0x00; // G channel
            fullDds[ofs + 2] = 0xFF; // R channel (mask 0x00FF0000 picks this byte)
            fullDds[ofs + 3] = 0xFF; // A channel (mask 0xFF000000 picks this byte)
        }

        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(fullDds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] pixels = DecodePngPixels(ms.ToArray(), out int w, out int h);

        Assert.Equal(4, w);
        Assert.Equal(4, h);
        // Every texel should be red with full alpha.
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(255, pixels[i * 4 + 0]); // R
            Assert.Equal(0, pixels[i * 4 + 1]); // G
            Assert.Equal(0, pixels[i * 4 + 2]); // B
            Assert.Equal(255, pixels[i * 4 + 3]); // A
        }
    }

    // -------------------------------------------------------------------------
    // TextureDescriptor dispatch test
    // -------------------------------------------------------------------------

    [Fact]
    public void WritePng_UnknownFormat_ThrowsNotSupportedException()
    {
        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Unknown,
            Payload = new ReadOnlyMemory<byte>(new byte[4]),
        };

        using var ms = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => PngConverter.WritePng(descriptor, ms));
    }

    [Fact]
    public void WritePng_TgaFormat_ThrowsNotSupportedException()
    {
        var descriptor = new TextureDescriptor
        {
            Format = TextureFormat.Tga,
            Payload = new ReadOnlyMemory<byte>(new byte[32]),
        };

        using var ms = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => PngConverter.WritePng(descriptor, ms));
    }

    // -------------------------------------------------------------------------
    // Determinism test
    // -------------------------------------------------------------------------

    [Fact]
    public void WritePngRgba8_Output_IsDeterministic()
    {
        byte[] rgba = Enumerable.Range(0, 4 * 4 * 4)
            .Select(i => (byte)(i * 7 % 256))
            .ToArray();

        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        PngConverter.WritePngRgba8(rgba, 4, 4, ms1);
        PngConverter.WritePngRgba8(rgba, 4, 4, ms2);

        Assert.Equal(ms1.ToArray(), ms2.ToArray());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// CRC-32 (ISO 3309, polynomial 0xEDB88320 reflected).
    /// Used to verify stored chunk CRCs independently from the converter.
    /// PNG spec §5.3.
    /// </summary>
    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        const uint Poly = 0xEDB88320u;
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Minimal PNG decoder: reads IHDR dimensions, then inflates all IDAT data and
    /// un-filters scanlines (filter type 0 only) to produce a raw RGBA8 pixel array.
    /// PNG spec §9 "Filtering", §12 "Compression".
    /// Used to round-trip the PngConverter output in tests.
    /// </summary>
    private static byte[] DecodePngPixels(byte[] png, out int width, out int height)
    {
        // Read IHDR
        uint chunkLen = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8));
        Assert.Equal(13u, chunkLen);
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));

        // Collect all IDAT compressed data.
        var idatData = new List<byte>();
        int pos = 8; // start at first chunk
        while (pos < png.Length - 12)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT")
            {
                idatData.AddRange(png.AsSpan(pos + 8, (int)len).ToArray());
            }

            pos += 4 + 4 + (int)len + 4; // length + type + data + crc
        }

        // Decompress via ZLib.
        byte[] compressed = idatData.ToArray();
        using var inputMs = new MemoryStream(compressed);
        using var zlib = new ZLibStream(inputMs, CompressionMode.Decompress);
        using var outputMs = new MemoryStream();
        zlib.CopyTo(outputMs);
        byte[] filtered = outputMs.ToArray();

        // Un-filter: filter type 0 (None) = strip the filter byte from each scanline.
        int stride = width * 4;
        byte[] pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int srcOffset = row * (stride + 1);
            byte filterByte = filtered[srcOffset];
            Assert.Equal(0, filterByte); // only filter 0 produced by PngConverter
            filtered.AsSpan(srcOffset + 1, stride).CopyTo(pixels.AsSpan(row * stride));
        }

        return pixels;
    }
}