using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Tests for <see cref="TextureDetector"/>.
/// All fixtures are synthetic byte arrays; no real texture file is required.
/// spec: Docs/RE/formats/texture.md §Implementation guidance for Assets.Parsers
/// </summary>
public sealed class TextureDetectorTests
{
    // -----------------------------------------------------------------------
    // DDS
    // -----------------------------------------------------------------------

    [Fact]
    public void Detect_DdsMagic_ReturnsDds()
    {
        // spec: Docs/RE/formats/texture.md §Magic / signature:
        //   "DDS files begin with ASCII 'DDS ' (four bytes)". CONFIRMED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance:
        //   "If the first four bytes are 44 44 53 20 (ASCII 'DDS '), treat as DDS."
        byte[] ddsHeader =
            [(byte)'D', (byte)'D', (byte)'S', (byte)' ', 0x7C, 0x00, 0x00, 0x00]; // DDS + minimal DDS_HEADER magic

        TextureDescriptor desc = TextureDetector.Detect(ddsHeader.AsSpan());

        Assert.Equal(TextureFormat.Dds, desc.Format);
    }

    [Fact]
    public void Detect_DdsMagic_PayloadIsFullBuffer()
    {
        // The raw payload must be the complete buffer, not just the header.
        byte[] ddsData = [(byte)'D', (byte)'D', (byte)'S', (byte)' ', 0x01, 0x02, 0x03, 0x04];

        TextureDescriptor desc = TextureDetector.Detect(ddsData.AsSpan());

        Assert.Equal(ddsData, desc.Payload.ToArray());
    }

    [Fact]
    public void Detect_ReadOnlyMemory_DdsMagic_ReturnsDds()
    {
        byte[] ddsData = [(byte)'D', (byte)'D', (byte)'S', (byte)' ', 0xAA, 0xBB];

        TextureDescriptor desc = TextureDetector.Detect(new ReadOnlyMemory<byte>(ddsData));

        Assert.Equal(TextureFormat.Dds, desc.Format);
    }

    // -----------------------------------------------------------------------
    // BMP
    // -----------------------------------------------------------------------

    [Fact]
    public void Detect_BmpMagic_ReturnsBmp()
    {
        // BMP magic: "BM" at offset 0.
        // spec: Docs/RE/formats/texture.md §Likely concrete formats — BMP: LOW confidence. UNVERIFIED.
        byte[] bmpHeader = [(byte)'B', (byte)'M', 0x36, 0x00, 0x00, 0x00]; // BM + minimal BMP header bytes

        TextureDescriptor desc = TextureDetector.Detect(bmpHeader.AsSpan());

        Assert.Equal(TextureFormat.Bmp, desc.Format);
    }

    // -----------------------------------------------------------------------
    // TGA heuristic
    // -----------------------------------------------------------------------

    [Fact]
    public void Detect_TgaHeuristic_TrueColor_ReturnsTga()
    {
        // TGA has no magic. Heuristic: byte[1] = 0 (no color map), byte[2] = 2 (true-color).
        // spec: Docs/RE/formats/texture.md §Likely concrete formats — TGA: MEDIUM confidence. UNVERIFIED.
        // Pad to at least 18 bytes so the length check passes.
        byte[] tgaHeader = new byte[18];
        tgaHeader[0] = 0; // ID length = 0
        tgaHeader[1] = 0; // color map type = 0
        tgaHeader[2] = 2; // image type = 2 (true-color, no compression)

        TextureDescriptor desc = TextureDetector.Detect(tgaHeader.AsSpan());

        Assert.Equal(TextureFormat.Tga, desc.Format);
    }

    [Fact]
    public void Detect_TgaHeuristic_Rle_ReturnsTga()
    {
        // image type = 10 = RLE true-color TGA.
        // spec: Docs/RE/formats/texture.md §Likely concrete formats — TGA: MEDIUM confidence. UNVERIFIED.
        byte[] tgaHeader = new byte[18];
        tgaHeader[1] = 0;
        tgaHeader[2] = 10;

        TextureDescriptor desc = TextureDetector.Detect(tgaHeader.AsSpan());

        Assert.Equal(TextureFormat.Tga, desc.Format);
    }

    // -----------------------------------------------------------------------
    // Unknown
    // -----------------------------------------------------------------------

    [Fact]
    public void Detect_UnknownMagic_ReturnsUnknown()
    {
        // spec: Docs/RE/formats/texture.md §Implementation guidance:
        //   "If an unrecognized format header is encountered, log the first four bytes
        //    and report failure rather than attempting a blind decode."
        byte[] garbage = [0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03];

        TextureDescriptor desc = TextureDetector.Detect(garbage.AsSpan());

        Assert.Equal(TextureFormat.Unknown, desc.Format);
    }

    [Fact]
    public void Detect_EmptyBuffer_ReturnsUnknown()
    {
        TextureDescriptor desc = TextureDetector.Detect(ReadOnlySpan<byte>.Empty);

        Assert.Equal(TextureFormat.Unknown, desc.Format);
    }

    [Fact]
    public void Detect_NeitherJpeg_Spec()
    {
        // spec: Docs/RE/formats/texture.md §JPEG is not an inbound texture format:
        //   "JPEG decoding is never called by this client." CONFIRMED.
        // A JPEG SOI marker (FF D8) should not be positively identified (no JPEG enum value exists).
        byte[] jpegSoi = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]; // JFIF JPEG header

        TextureDescriptor desc = TextureDetector.Detect(jpegSoi.AsSpan());

        // Must not return Dds, Bmp, or Tga. Unknown or another non-JPEG value is correct.
        Assert.NotEqual(TextureFormat.Dds, desc.Format);
        Assert.NotEqual(TextureFormat.Bmp, desc.Format);
        // Note: TextureFormat has no Jpeg value; its absence from the enum is the spec invariant.
        // We verify the payload is still populated correctly.
        Assert.Equal(jpegSoi, desc.Payload.ToArray());
    }
}