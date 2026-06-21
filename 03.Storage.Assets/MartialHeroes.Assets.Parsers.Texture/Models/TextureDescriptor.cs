namespace MartialHeroes.Assets.Parsers.Texture.Models;

/// <summary>
///     Detected format of a texture asset whose raw bytes were inspected by <see cref="TextureDetector" />.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/texture.md §There is no proprietary texture format
///     spec: Docs/RE/formats/texture.md §Implementation guidance for Assets.Parsers — step 2
///     All four raster containers (DDS, TGA, BMP, PNG) are used by this client.
///     The loader delegates to D3DX9 which auto-detects from header bytes, not extension.
/// </remarks>
public enum TextureFormat
{
    /// <summary>
    ///     Format could not be identified from the header magic bytes.
    ///     spec: Docs/RE/formats/texture.md §Implementation guidance — "log the first eight bytes and report failure".
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     DirectDraw Surface. Magic bytes <c>44 44 53 20</c> (ASCII <c>DDS </c>) at offset 0.
    ///     spec: Docs/RE/formats/texture.md §Format: DDS — "Magic (bytes 0–3): 44 44 53 20". SAMPLE-VERIFIED.
    /// </summary>
    Dds = 1,

    /// <summary>
    ///     Truevision TGA. No fixed magic — identified by extension or decoder heuristic.
    ///     spec: Docs/RE/formats/texture.md §Format: TGA — "TGA has no fixed magic bytes". SAMPLE-VERIFIED.
    /// </summary>
    Tga = 2,

    /// <summary>
    ///     Windows Bitmap. Identified by magic bytes <c>42 4D</c> (ASCII <c>BM</c>).
    ///     spec: Docs/RE/formats/texture.md §Format: BMP — "Magic (bytes 0–1): 42 4D". SAMPLE-VERIFIED.
    /// </summary>
    Bmp = 3,

    /// <summary>
    ///     Portable Network Graphics. Identified by the 8-byte PNG signature.
    ///     Magic: <c>89 50 4E 47 0D 0A 1A 0A</c>.
    ///     Used for character and item skin textures in <c>data/char/tex*/</c> and <c>data/item/texture/</c>.
    ///     spec: Docs/RE/formats/texture.md §Format: PNG — "Magic (bytes 0–7): 89 50 4E 47 0D 0A 1A 0A". SAMPLE-VERIFIED.
    /// </summary>
    Png = 4
}

/// <summary>
///     Result of the texture format detector: the identified format kind and the raw payload bytes.
///     Pixel decoding is NOT performed here; that is <c>Assets.Mapping</c>'s responsibility.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/texture.md §Implementation guidance for Assets.Parsers
/// </remarks>
public sealed class TextureDescriptor
{
    /// <summary>Detected texture format.</summary>
    public required TextureFormat Format { get; init; }

    /// <summary>
    ///     Raw bytes of the texture asset as extracted from the VFS (no pixel decoding).
    ///     Backed by the VFS memory-mapped view; lifetime is tied to the open archive.
    /// </summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}