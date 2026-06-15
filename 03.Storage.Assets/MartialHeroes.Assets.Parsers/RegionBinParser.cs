using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

// spec: Docs/RE/formats/region_grid.md

/// <summary>
/// Parser for the RUNTIME <c>region&lt;NNN&gt;.bin</c> per-map region/zone-id grid.
/// </summary>
/// <remarks>
/// <para>
/// Implements ONLY the runtime <c>region&lt;NNN&gt;.bin</c> layout (Layout A in the spec).
/// The authoring-side <c>.tol</c> format (Layout B) is editor-only and is NOT read by the
/// shipped client; it is intentionally NOT implemented here.
/// spec: Docs/RE/formats/region_grid.md §Two layouts — "region.bin is the one the shipped client actually loads".
/// </para>
/// <para>
/// Binary layout (all fields little-endian):
/// <code>
///   +0x00            u32le  width      (columns, cells along X)
///   +0x04            u32le  height     (rows, cells along Z)
///   +0x08            u8[]   cells      (width × height bytes; row-major region IDs 0..31)
///   +0x08 + W×H      i32le  originX    (world-space X origin, SIGNED)
///   +0x08 + W×H + 4  i32le  originZ    (world-space Z origin, SIGNED)
///   Total size = 16 + width × height bytes
/// </code>
/// Origins are signed so maps with negative world extents (origin at negative coordinates)
/// decode correctly.
/// spec: Docs/RE/formats/region_grid.md §Layout A — region&lt;NNN&gt;.bin (RUNTIME): HIGH (parser).
/// spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed / originZ i32 signed": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class RegionBinParser
{
    // Minimum valid file size: 4 (width) + 4 (height) + 0 cells + 4 (originX) + 4 (originZ) = 16.
    // spec: Docs/RE/formats/region_grid.md §Size derivation — "total = 16 + width × height".
    private const int MinFileSize = 16;

    // Width field offset — u32le.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "width u32le @ 0x00": HIGH.
    private const int WidthOffset = 0x00;

    // Height field offset — u32le.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "height u32le @ 0x04": HIGH.
    private const int HeightOffset = 0x04;

    // Cell buffer starts immediately after the two u32 dimension fields.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "regionIdGrid u8[] @ 0x08": HIGH.
    private const int CellsOffset = 0x08;

    // Origins are i32le (signed) — trailing after the cell buffer.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed / originZ i32 signed": CONFIRMED.

    /// <summary>
    /// Parses the raw bytes of a <c>region&lt;NNN&gt;.bin</c> file into a <see cref="RegionGrid"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>The decoded region grid with dimensions, cell bytes, and world origins.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the buffer is too short to hold the declared dimensions, cell array, or trailing
    /// origin fields.
    /// spec: Docs/RE/formats/region_grid.md §Layout A — bounds validation.
    /// </exception>
    public static RegionGrid Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses a span of bytes into a <see cref="RegionGrid"/>.
    /// </summary>
    /// <param name="span">Raw bytes from the VFS.</param>
    /// <returns>The decoded region grid.</returns>
    /// <exception cref="InvalidDataException">Buffer is truncated.</exception>
    public static RegionGrid Parse(ReadOnlySpan<byte> span)
    {
        // Must have at least width + height + originX + originZ (= 16 bytes minimum).
        // spec: Docs/RE/formats/region_grid.md §Size derivation — "total = 16 + width × height": HIGH.
        if (span.Length < MinFileSize)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short " +
                $"(minimum {MinFileSize} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §Layout A.");

        // width u32le @ 0x00.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "width u32le @ 0x00": HIGH.
        uint rawWidth = BinaryPrimitives.ReadUInt32LittleEndian(span[WidthOffset..]);

        // height u32le @ 0x04.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "height u32le @ 0x04": HIGH.
        uint rawHeight = BinaryPrimitives.ReadUInt32LittleEndian(span[HeightOffset..]);

        // Validate that the declared cell buffer fits inside the actual buffer.
        // Total expected = 4 (width) + 4 (height) + (width × height) + 4 (originX) + 4 (originZ).
        // spec: Docs/RE/formats/region_grid.md §Size derivation — "total = 16 + width × height": HIGH.
        long cellCount = (long)rawWidth * rawHeight;
        long expectedTotal = CellsOffset + cellCount + 4L + 4L; // 8 + cells + 8
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $"region.bin parse error: buffer length {span.Length} is too short for a " +
                $"{rawWidth}×{rawHeight} grid ({cellCount} cells, expected total {expectedTotal} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §Layout A.");

        // Copy cells — u8[] region IDs (0..31), row-major.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "regionIdGrid u8[] @ 0x08, width × height bytes": HIGH.
        var cells = new byte[(int)cellCount];
        span.Slice(CellsOffset, (int)cellCount).CopyTo(cells);

        int originXOffset = CellsOffset + (int)cellCount;

        // originX i32le (signed) trailing after the cell buffer.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed @ 0x08 + W×H": CONFIRMED.
        int originX = BinaryPrimitives.ReadInt32LittleEndian(span[originXOffset..]);

        // originZ i32le (signed) trailing 4 bytes after originX.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "originZ i32 signed @ 0x08 + W×H + 4": CONFIRMED.
        int originZ = BinaryPrimitives.ReadInt32LittleEndian(span[(originXOffset + 4)..]);

        return new RegionGrid
        {
            Width = (int)rawWidth,
            Height = (int)rawHeight,
            Cells = cells,
            OriginX = originX,
            OriginZ = originZ,
        };
    }
}