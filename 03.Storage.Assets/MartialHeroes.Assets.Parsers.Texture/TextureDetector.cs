using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Detects the format of a raw texture asset by inspecting its header magic bytes.
///     Does NOT decode pixels or decompress DXT blocks — that is <c>Assets.Mapping</c>'s
///     responsibility.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/texture.md §There is no proprietary texture format
///     spec: Docs/RE/formats/texture.md §Implementation guidance for Assets.Parsers
///     <para>
///         There is no proprietary texture header in this client.  All textures are standard formats
///         delegated to D3DX9.  This detector identifies the format so that <c>Assets.Mapping</c> can
///         choose the correct decoder.  Pixel data is not touched here.
///     </para>
/// </remarks>
public static class TextureDetector
{
    // Minimum header bytes needed to identify any supported format.
    private const int MinHeaderBytes = 4;

    // DDS magic: ASCII "DDS " = 0x44 0x44 0x53 0x20
    // spec: Docs/RE/formats/texture.md §Magic / signature:
    //   "DDS files begin with ASCII 'DDS ' (four bytes)". CONFIRMED.
    // spec: Docs/RE/formats/texture.md §Implementation guidance:
    //   "If the first four bytes of the raw buffer are 44 44 53 20 (ASCII 'DDS '), treat as DDS."
    private static ReadOnlySpan<byte> DdsMagic => "DDS "u8;

    // BMP magic: ASCII "BM" = 0x42 0x4D
    // spec: Docs/RE/formats/texture.md §Likely concrete formats — BMP: LOW confidence. UNVERIFIED.
    private static ReadOnlySpan<byte> BmpMagic => "BM"u8;

    /// <summary>
    ///     Detects the format of a texture asset from its raw bytes and returns a
    ///     <see cref="TextureDescriptor" /> carrying the format kind and the raw payload.
    /// </summary>
    /// <param name="data">
    ///     Raw bytes of the texture file as delivered by the VFS.
    ///     The payload in the returned descriptor refers to these same bytes (no copy).
    /// </param>
    /// <returns>
    ///     A <see cref="TextureDescriptor" /> with the detected <see cref="TextureFormat" /> and
    ///     the original raw payload.  If the format cannot be identified,
    ///     <see cref="TextureFormat.Unknown" /> is returned.
    /// </returns>
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

    /// <summary>
    ///     Overload accepting a <see cref="ReadOnlySpan{byte}" /> directly (e.g. for testing
    ///     without a heap-allocated buffer).  The returned descriptor's
    ///     <see cref="TextureDescriptor.Payload" /> will wrap a copy of the bytes.
    /// </summary>
    public static TextureDescriptor Detect(ReadOnlySpan<byte> data)
    {
        var header = data.Length >= MinHeaderBytes
            ? data[..MinHeaderBytes]
            : data;

        var format = DetectFormat(header, data.Length);

        return new TextureDescriptor
        {
            Format = format,
            Payload = data.ToArray() // copy required to produce a ReadOnlyMemory<byte>
        };
    }

    // -------------------------------------------------------------------------

    private static TextureFormat DetectFormat(ReadOnlySpan<byte> header, int totalLength)
    {
        if (totalLength < 1)
            return TextureFormat.Unknown;

        // DDS check (highest-confidence format for this era).
        // spec: Docs/RE/formats/texture.md §Implementation guidance:
        //   "Attempt DDS decoding first (highest-confidence format for this era)." HIGH confidence.
        if (header.Length >= DdsMagic.Length && header[..DdsMagic.Length].SequenceEqual(DdsMagic))
            return TextureFormat.Dds;

        // BMP check: magic "BM" at offset 0.
        // spec: Docs/RE/formats/texture.md §Likely concrete formats — BMP: LOW confidence. UNVERIFIED.
        if (header.Length >= BmpMagic.Length && header[..BmpMagic.Length].SequenceEqual(BmpMagic))
            return TextureFormat.Bmp;

        // TGA has no fixed magic.  A heuristic is used: the 18-byte TGA header has
        // byte[2] (image type) typically in range 1-3 or 9-11 for common TGA variants,
        // and byte[16] (bits per pixel) in {8, 16, 24, 32}.
        // This is a low-confidence heuristic only; it is applied as a last resort.
        // spec: Docs/RE/formats/texture.md §Likely concrete formats — TGA: MEDIUM confidence. UNVERIFIED.
        //
        // The heuristic is conservative: only flag as TGA when the byte pattern is clearly
        // incompatible with DDS/BMP yet plausibly TGA.  We check byte[2] (color map type 0 or 1)
        // and whether the rest of the initial bytes look like binary data rather than text.
        if (LooksTga(header, totalLength))
            return TextureFormat.Tga;

        // spec: Docs/RE/formats/texture.md §Implementation guidance:
        //   "If an unrecognized format header is encountered, log the first four bytes and
        //    report failure rather than attempting a blind decode."
        return TextureFormat.Unknown;
    }

    /// <summary>
    ///     Heuristic TGA identification. TGA has no magic; this is best-effort.
    ///     Returns <see langword="true" /> only when the byte pattern is clearly TGA-like.
    ///     UNVERIFIED: no texture sample from this archive has been inspected.
    ///     spec: Docs/RE/formats/texture.md §Likely concrete formats — TGA: MEDIUM confidence.
    /// </summary>
    private static bool LooksTga(ReadOnlySpan<byte> header, int totalLength)
    {
        // We need at least 3 bytes to apply the heuristic.
        if (header.Length < 3 || totalLength < 18)
            return false;

        // Byte[0]: ID length (0–255; typically 0).
        // Byte[1]: color map type (0 or 1 for standard TGA).
        // Byte[2]: image type (1=color-mapped, 2=true-color, 3=grayscale, 9-11 are RLE variants).
        var colorMapType = header[1];
        var imageType = header[2];

        if (colorMapType > 1)
            return false; // invalid color map type for TGA

        return imageType is 1 or 2 or 3 or 9 or 10 or 11;
    }
}