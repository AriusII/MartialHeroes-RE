using System.Buffers.Binary;
using System.IO.Compression;
using MartialHeroes.Assets.Mapping;
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
        uint storedWidth  = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataStart));
        uint storedHeight = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataStart + 4));
        byte bitDepth     = png[dataStart + 8];
        byte colorType    = png[dataStart + 9];
        byte compression  = png[dataStart + 10];
        byte filter       = png[dataStart + 11];
        byte interlace    = png[dataStart + 12];

        Assert.Equal((uint)width,  storedWidth);
        Assert.Equal((uint)height, storedHeight);
        Assert.Equal(8,    bitDepth);       // 8-bit channels
        Assert.Equal(6,    colorType);      // RGBA (color type 6)
        Assert.Equal(0,    compression);    // Deflate
        Assert.Equal(0,    filter);         // filter method 0
        Assert.Equal(0,    interlace);      // no interlace
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
        dds[0] = 0x44; dds[1] = 0x44; dds[2] = 0x53; dds[3] = 0x20;

        // DDS_HEADER (124 bytes starting at offset 4)
        // dwSize = 124 at offset 4
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124u);
        // dwFlags — minimal valid flags (DDSD_CAPS|DDSD_HEIGHT|DDSD_WIDTH|DDSD_PIXELFORMAT = 0x1007)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(8), 0x1007u);
        // dwHeight @ +8 (header offset 8 = file offset 12)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), 4u); // height = 4
        // dwWidth @ +12 (file offset 16)
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), 4u); // width  = 4

        // DDPIXELFORMAT starts at header offset 76 = file offset 80.
        // pfSize @ 80
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 32u);
        // pfFlags @ 84: DDPF_FOURCC = 4
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(84), 4u);
        // pfFourCC @ 88: 'DXT1'
        dds[88] = (byte)'D'; dds[89] = (byte)'X'; dds[90] = (byte)'T'; dds[91] = (byte)'1';

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
            Format  = TextureFormat.Dds,
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
            Assert.Equal(  0, pixels[i * 4 + 1]); // G
            Assert.Equal(  0, pixels[i * 4 + 2]); // B
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
            Format  = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(dds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] pixels = DecodePngPixels(ms.ToArray(), out _, out _);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(  0, pixels[i * 4 + 0]); // R
            Assert.Equal(255, pixels[i * 4 + 1]); // G
            Assert.Equal(  0, pixels[i * 4 + 2]); // B
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
            Format  = TextureFormat.Dds,
            Payload = new ReadOnlyMemory<byte>(dds),
        };

        using var ms = new MemoryStream();
        PngConverter.WritePng(descriptor, ms);
        byte[] pixels = DecodePngPixels(ms.ToArray(), out _, out _);

        // Check first texel.
        Assert.Equal(170, pixels[0]); // R = (2*255+0+1)/3 = 170
        Assert.Equal( 85, pixels[1]); // G = (2*0+255+1)/3 = 85
        Assert.Equal(  0, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    // -------------------------------------------------------------------------
    // TextureDescriptor dispatch test
    // -------------------------------------------------------------------------

    [Fact]
    public void WritePng_UnknownFormat_ThrowsNotSupportedException()
    {
        var descriptor = new TextureDescriptor
        {
            Format  = TextureFormat.Unknown,
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
            Format  = TextureFormat.Tga,
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
        width  = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));

        // Collect all IDAT compressed data.
        var idatData = new List<byte>();
        int pos = 8; // start at first chunk
        while (pos < png.Length - 12)
        {
            uint len  = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT")
            {
                idatData.AddRange(png.AsSpan(pos + 8, (int)len).ToArray());
            }
            pos += 4 + 4 + (int)len + 4; // length + type + data + crc
        }

        // Decompress via ZLib.
        byte[] compressed = idatData.ToArray();
        using var inputMs  = new MemoryStream(compressed);
        using var zlib     = new ZLibStream(inputMs, CompressionMode.Decompress);
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
