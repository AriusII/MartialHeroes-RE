namespace MartialHeroes.Assets.Parsers.Texture.Models;

/// <summary>
///     Detected format of a texture asset whose raw bytes were inspected by <see cref="TextureDetector" />.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/texture.md §There is no proprietary texture format
/// </remarks>
public enum TextureFormat
{
    /// <summary>
    ///     Format could not be identified from the header magic bytes.
    ///     spec: Docs/RE/formats/texture.md §Implementation guidance — "log the first four bytes and report failure".
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     DirectDraw Surface. Magic bytes <c>44 44 53 20</c> (ASCII <c>DDS </c>) at offset 0.
    ///     spec: Docs/RE/formats/texture.md §Magic / signature: "DDS files begin with ASCII 'DDS ' (four bytes)". CONFIRMED.
    /// </summary>
    Dds = 1,

    /// <summary>
    ///     Truevision TGA. No fixed magic — identified heuristically.
    ///     spec: Docs/RE/formats/texture.md §Likely concrete formats — TGA: MEDIUM confidence.
    ///     UNVERIFIED: no texture sample has been inspected.
    /// </summary>
    Tga = 2,

    /// <summary>
    ///     Windows Bitmap. Identified by magic bytes <c>42 4D</c> (ASCII <c>BM</c>).
    ///     spec: Docs/RE/formats/texture.md §Likely concrete formats — BMP: LOW confidence.
    ///     UNVERIFIED: no texture sample has been inspected.
    /// </summary>
    Bmp = 3
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