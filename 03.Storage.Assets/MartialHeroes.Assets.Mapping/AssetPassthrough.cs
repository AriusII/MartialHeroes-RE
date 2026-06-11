using System.Buffers.Binary;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Pass-through helpers for asset formats that are already modern standard containers.
///
/// The game client uses standard, unmodified file formats for several asset types:
///   - PNG (character and item skin textures) — standard ISO 15948 / RFC 2083.
///     spec: Docs/RE/formats/texture.md §PNG — "No proprietary wrapper. Standard PNG."
///   - BMP (terrain lightmap tiles, toon-shading LUT) — standard Windows DIB.
///     spec: Docs/RE/formats/texture.md §BMP — "Standard Windows BMP. No proprietary wrapper."
///   - OGG Vorbis (.ogg) — standard Ogg Vorbis container (RFC 3533).
///     spec: Docs/RE/formats/sound_tables.md §7.1 — "Standard Ogg Vorbis. Directly decodable."
///   - RIFF/WAVE PCM (.wav) — standard RIFF/WAVE (WAVE_FORMAT_PCM).
///     spec: Docs/RE/formats/sound_tables.md §7.2 — "Standard RIFF/WAVE PCM. Directly playable."
///
/// None of these formats require re-encoding; the raw bytes extracted from the VFS are
/// already valid for direct use by any standards-compliant loader.
///
/// This class detects the format from the byte magic (or extension hint for TGA/BMP where the
/// magic is ambiguous) and returns metadata alongside the raw bytes.
///
/// Design decision: we do NOT re-encode PNG via <see cref="PngConverter"/> because:
///   1. The on-disk PNG is already a valid, correctly-oriented standard PNG.
///   2. Re-encoding would waste CPU and could alter compression ratios without adding value.
///   3. The spec confirms standard D3DX9 format auto-detection is used at runtime,
///      meaning the engine never transforms the PNG bytes either.
///      spec: Docs/RE/formats/texture.md §There is no proprietary texture format: CONFIRMED.
/// </summary>
public static class AssetPassthrough
{
    // -------------------------------------------------------------------------
    // Magic-byte constants (used for format detection)
    // -------------------------------------------------------------------------

    // PNG magic: 8 bytes. spec: Docs/RE/formats/texture.md §PNG §Identification: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> PngMagic =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // BMP magic: first 2 bytes = "BM". spec: Docs/RE/formats/texture.md §BMP §Identification: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> BmpMagic => [0x42, 0x4D];

    // DDS magic: "DDS ". spec: Docs/RE/formats/texture.md §DDS §Identification: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> DdsMagic => [0x44, 0x44, 0x53, 0x20];

    // OGG magic: "OggS". spec: Docs/RE/formats/sound_tables.md §7.1 — magic 0x4F676753: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> OggMagic => [0x4F, 0x67, 0x67, 0x53];

    // RIFF magic: "RIFF". spec: Docs/RE/formats/sound_tables.md §7.2 — magic 0x52494646: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> RiffMagic => [0x52, 0x49, 0x46, 0x46];

    // WAV form type at bytes 8-11: "WAVE". spec: Docs/RE/formats/sound_tables.md §7.2: SAMPLE-VERIFIED.
    private static ReadOnlySpan<byte> WaveType => [0x57, 0x41, 0x56, 0x45];

    // -------------------------------------------------------------------------
    // Image passthrough
    // -------------------------------------------------------------------------

    /// <summary>
    /// Detects the image format of <paramref name="rawBytes"/> and returns the bytes
    /// unchanged alongside the extracted dimensions and format.
    /// Supported: PNG, BMP. DDS is detected but NOT decoded (the caller should use
    /// <see cref="PngConverter"/> for DDS → PNG conversion).
    /// </summary>
    /// <param name="rawBytes">
    /// Raw bytes from the VFS (PNG, BMP, or DDS).
    /// </param>
    /// <param name="extensionHint">
    /// Optional file extension hint (e.g. ".tga") for formats with no fixed magic.
    /// </param>
    /// <returns>
    /// An <see cref="ImagePassthroughResult"/> describing the format and dimensions.
    /// The <see cref="ImagePassthroughResult.Bytes"/> span aliases <paramref name="rawBytes"/>;
    /// no copy is made.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown for unrecognised formats when no extension hint resolves the ambiguity.
    /// </exception>
    public static ImagePassthroughResult PassthroughImage(
        ReadOnlyMemory<byte> rawBytes,
        string? extensionHint = null)
    {
        ReadOnlySpan<byte> span = rawBytes.Span;

        if (span.Length >= 8 && span[..8].SequenceEqual(PngMagic))
        {
            (int w, int h) = ReadPngDimensions(span);
            return new ImagePassthroughResult(
                Bytes: rawBytes,
                Format: ImageFormat.Png,
                Width: w,
                Height: h);
        }

        if (span.Length >= 2 && span[..2].SequenceEqual(BmpMagic))
        {
            (int w, int h) = ReadBmpDimensions(span);
            return new ImagePassthroughResult(
                Bytes: rawBytes,
                Format: ImageFormat.Bmp,
                Width: w,
                Height: h);
        }

        if (span.Length >= 4 && span[..4].SequenceEqual(DdsMagic))
        {
            (int w, int h) = ReadDdsDimensions(span);
            return new ImagePassthroughResult(
                Bytes: rawBytes,
                Format: ImageFormat.Dds,
                Width: w,
                Height: h);
        }

        // TGA has no fixed magic — use extension hint.
        // spec: Docs/RE/formats/texture.md §TGA — "No fixed magic; identify by extension": SAMPLE-VERIFIED.
        if (extensionHint is not null &&
            extensionHint.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            (int w, int h) = ReadTgaDimensions(span);
            return new ImagePassthroughResult(
                Bytes: rawBytes,
                Format: ImageFormat.Tga,
                Width: w,
                Height: h);
        }

        throw new NotSupportedException(
            $"Unrecognised image format (first 8 bytes: {FormatHex(span)}, hint: {extensionHint}).");
    }

    // -------------------------------------------------------------------------
    // Audio passthrough
    // -------------------------------------------------------------------------

    /// <summary>
    /// Detects the audio format of <paramref name="rawBytes"/> (OGG Vorbis or RIFF/WAVE)
    /// and returns the bytes unchanged alongside format metadata.
    /// </summary>
    /// <param name="rawBytes">Raw audio bytes from the VFS.</param>
    /// <returns>An <see cref="AudioPassthroughResult"/> describing the format.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown for unrecognised audio formats.
    /// </exception>
    public static AudioPassthroughResult PassthroughAudio(ReadOnlyMemory<byte> rawBytes)
    {
        ReadOnlySpan<byte> span = rawBytes.Span;

        if (span.Length >= 4 && span[..4].SequenceEqual(OggMagic))
        {
            // spec: Docs/RE/formats/sound_tables.md §7.1 OGG — magic "OggS": SAMPLE-VERIFIED.
            return new AudioPassthroughResult(Bytes: rawBytes, Format: AudioFormat.OggVorbis);
        }

        if (span.Length >= 12 && span[..4].SequenceEqual(RiffMagic) && span[8..12].SequenceEqual(WaveType))
        {
            // spec: Docs/RE/formats/sound_tables.md §7.2 WAV — magic "RIFF"+"WAVE": SAMPLE-VERIFIED.
            return new AudioPassthroughResult(Bytes: rawBytes, Format: AudioFormat.RiffWave);
        }

        throw new NotSupportedException(
            $"Unrecognised audio format (first 12 bytes: {FormatHex(span)}).");
    }

    // -------------------------------------------------------------------------
    // Dimension extraction helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads PNG width and height from the IHDR chunk at fixed offsets.
    /// spec: Docs/RE/formats/texture.md §PNG §IHDR — width @ abs+16 (u32 BE), height @ abs+20 (u32 BE): SAMPLE-VERIFIED.
    /// </summary>
    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 24) return (0, 0);
        // IHDR data starts at offset 16 (8 sig + 4 length + 4 type).
        // Width at +16 (4 bytes BE), Height at +20 (4 bytes BE).
        int w = (int)BinaryPrimitives.ReadUInt32BigEndian(span[16..]);
        int h = (int)BinaryPrimitives.ReadUInt32BigEndian(span[20..]);
        return (w, h);
    }

    /// <summary>
    /// Reads BMP width and height from the BITMAPINFOHEADER.
    /// spec: Docs/RE/formats/texture.md §BMP §BITMAPINFOHEADER —
    ///   Width i32 @ 0x12, Height i32 @ 0x16 (absolute file offsets): SAMPLE-VERIFIED.
    /// Width=0x12 (18), Height=0x16 (22).
    /// Note: Height is the absolute value of the stored signed integer.
    /// </summary>
    private static (int Width, int Height) ReadBmpDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x1A) return (0, 0);
        int w = BinaryPrimitives.ReadInt32LittleEndian(span[0x12..]);
        int h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(span[0x16..]));
        return (w, h);
    }

    /// <summary>
    /// Reads DDS width and height from the DDS_HEADER.
    /// spec: Docs/RE/formats/texture.md §DDS — dwHeight @ 0x0C, dwWidth @ 0x10 (LE u32): SAMPLE-VERIFIED.
    /// </summary>
    private static (int Width, int Height) ReadDdsDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x14) return (0, 0);
        // DDS_HEADER starts at offset 4 (after 4-byte magic).
        // dwHeight at file offset 0x0C (= 4+8), dwWidth at 0x10 (= 4+12).
        int h = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);
        int w = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);
        return (w, h);
    }

    /// <summary>
    /// Reads TGA width and height from the 18-byte header.
    /// spec: Docs/RE/formats/texture.md §TGA §Header — width u16 LE @ 0x0C, height u16 LE @ 0x0E: SAMPLE-VERIFIED.
    /// </summary>
    private static (int Width, int Height) ReadTgaDimensions(ReadOnlySpan<byte> span)
    {
        if (span.Length < 0x10) return (0, 0);
        int w = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0C..]);
        int h = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0E..]);
        return (w, h);
    }

    // -------------------------------------------------------------------------
    // Diagnostic helper
    // -------------------------------------------------------------------------

    private static string FormatHex(ReadOnlySpan<byte> span)
    {
        int count = Math.Min(12, span.Length);
        var sb = new System.Text.StringBuilder(count * 3);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(span[i].ToString("X2"));
        }

        return sb.ToString();
    }
}

// -------------------------------------------------------------------------
// Result types
// -------------------------------------------------------------------------

/// <summary>
/// Enumerates the detected image formats supported by <see cref="AssetPassthrough"/>.
/// </summary>
public enum ImageFormat
{
    /// <summary>
    /// Standard PNG (ISO 15948 / RFC 2083).
    /// spec: Docs/RE/formats/texture.md §PNG: SAMPLE-VERIFIED.
    /// </summary>
    Png,

    /// <summary>
    /// Standard Windows BMP (DIB v3, BI_RGB).
    /// spec: Docs/RE/formats/texture.md §BMP: SAMPLE-VERIFIED.
    /// </summary>
    Bmp,

    /// <summary>
    /// DDS (DirectDraw Surface) — DXT1/DXT3/DXT5.
    /// spec: Docs/RE/formats/texture.md §DDS: SAMPLE-VERIFIED (DXT1); DXT5 CONFIRMED-from-routine.
    /// Note: DDS is NOT a modern standard container; use <see cref="PngConverter"/> to convert.
    /// </summary>
    Dds,

    /// <summary>
    /// TGA (Truevision TARGA) uncompressed 32bpp BGRA.
    /// spec: Docs/RE/formats/texture.md §TGA: SAMPLE-VERIFIED.
    /// Note: TGA is NOT the preferred modern format; use <see cref="PngConverter"/> to convert.
    /// </summary>
    Tga,
}

/// <summary>
/// Enumerates the detected audio formats supported by <see cref="AssetPassthrough"/>.
/// </summary>
public enum AudioFormat
{
    /// <summary>
    /// Ogg Vorbis container.
    /// spec: Docs/RE/formats/sound_tables.md §7.1: SAMPLE-VERIFIED (3 samples, 22050 Hz mono).
    /// </summary>
    OggVorbis,

    /// <summary>
    /// RIFF/WAVE PCM (WAVE_FORMAT_PCM, audio_format=1).
    /// spec: Docs/RE/formats/sound_tables.md §7.2: SAMPLE-VERIFIED (1 sample, 22050 Hz mono 16-bit).
    /// </summary>
    RiffWave,
}

/// <summary>
/// Result of a successful image passthrough detection.
/// The <see cref="Bytes"/> span aliases the original buffer — no copy is made.
/// </summary>
/// <param name="Bytes">Raw bytes of the image file (alias of the input).</param>
/// <param name="Format">Detected image format.</param>
/// <param name="Width">Image width in pixels (0 if undetermined).</param>
/// <param name="Height">Image height in pixels (0 if undetermined).</param>
public sealed record ImagePassthroughResult(
    ReadOnlyMemory<byte> Bytes,
    ImageFormat Format,
    int Width,
    int Height);

/// <summary>
/// Result of a successful audio passthrough detection.
/// The <see cref="Bytes"/> span aliases the original buffer — no copy is made.
/// </summary>
/// <param name="Bytes">Raw bytes of the audio file (alias of the input).</param>
/// <param name="Format">Detected audio format.</param>
public sealed record AudioPassthroughResult(
    ReadOnlyMemory<byte> Bytes,
    AudioFormat Format);