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
    // PNG signature is 8 bytes — the longest magic in the set.
    // spec: Docs/RE/formats/texture.md §Format: PNG — "Magic (bytes 0–7)". SAMPLE-VERIFIED.
    private const int MinHeaderBytes = 8;

    // DDS magic: ASCII "DDS " = 0x44 0x44 0x53 0x20
    // spec: Docs/RE/formats/texture.md §Format: DDS — "Magic (bytes 0–3): 44 44 53 20 (ASCII DDS , space included)". SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/texture.md §Implementation guidance for Assets.Parsers — step 2.
    private static ReadOnlySpan<byte> DdsMagic => "DDS "u8;

    // PNG magic: 8 bytes per ISO 15948 / RFC 2083.
    // spec: Docs/RE/formats/texture.md §Format: PNG — "Magic (bytes 0–7): 89 50 4E 47 0D 0A 1A 0A". SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> PngMagic => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // BMP magic: ASCII "BM" = 0x42 0x4D
    // spec: Docs/RE/formats/texture.md §Format: BMP — "Magic (bytes 0–1): 42 4D (ASCII BM)". SAMPLE-VERIFIED.
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

        // DDS check: magic "DDS " (4 bytes).
        // spec: Docs/RE/formats/texture.md §Format: DDS — "Magic (bytes 0–3): 44 44 53 20". SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance — step 2: "DDS: bytes 0–3 = 44 44 53 20".
        if (header.Length >= DdsMagic.Length && header[..DdsMagic.Length].SequenceEqual(DdsMagic))
            return TextureFormat.Dds;

        // PNG check: 8-byte signature.
        // spec: Docs/RE/formats/texture.md §Format: PNG — "Magic (bytes 0–7): 89 50 4E 47 0D 0A 1A 0A". SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance — step 2: "PNG: bytes 0–7 = 89 50 4E 47 0D 0A 1A 0A".
        // Note: PNG is the format for character skin textures in data/char/tex*/ — this check is load-bearing.
        if (header.Length >= PngMagic.Length && header[..PngMagic.Length].SequenceEqual(PngMagic))
            return TextureFormat.Png;

        // BMP check: magic "BM" at offset 0.
        // spec: Docs/RE/formats/texture.md §Format: BMP — "Magic (bytes 0–1): 42 4D". SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance — step 2: "BMP: bytes 0–1 = 42 4D".
        if (header.Length >= BmpMagic.Length && header[..BmpMagic.Length].SequenceEqual(BmpMagic))
            return TextureFormat.Bmp;

        // TGA has no fixed magic. Identify by extension or decoder heuristic (last resort).
        // spec: Docs/RE/formats/texture.md §Format: TGA — "TGA has no fixed magic bytes;
        //   identify by extension or D3DX9 auto-detection heuristics". SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance — step 2:
        //   "TGA: no magic; use file extension. If extension is .tga, treat as TGA regardless
        //    of first-byte value."
        // This heuristic applies only when the caller has no extension information.
        // The caller should prefer extension-based dispatch over this heuristic when possible.
        if (LooksTga(header, totalLength))
            return TextureFormat.Tga;

        // spec: Docs/RE/formats/texture.md §Implementation guidance:
        //   "If an unrecognized format header is encountered, log the first eight bytes and
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