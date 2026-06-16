using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>region&lt;area&gt;.bin</c> — the per-area 256-unit region-id grid.
/// </summary>
/// <remarks>
/// <para>
/// File layout (all fields little-endian):
/// <code>
///   [0..3]   u32le  grid width  (columns)
///   [4..7]   u32le  grid height (rows)
///   [8 .. 8+width*height-1]  u8[width*height]  cell bytes (region ids 0..31)
///   [next 4] u32le  world-X origin
///   [next 4] u32le  world-Z origin
/// </code>
/// Total size = 4 + 4 + (width × height) + 4 + 4 bytes.
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — file layout: CONFIRMED.
/// </para>
/// <para>
/// Each cell byte is an unsigned region id in the range 0..31. The id indexes the companion
/// <c>regiontable&lt;area&gt;.bin</c> zone-type table (see <see cref="RegionZoneTableParser"/>).
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "1 byte per cell = region id (0..31)": CONFIRMED.
/// </para>
/// <para>
/// Cell size = 256 world units per axis (a quarter of a 1024-unit terrain cell).
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "Cell size = 256 world units": CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class RegionGridParser
{
    // ── offsets / sizes ──────────────────────────────────────────────────────

    // grid width u32le @ offset 0.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid width 4-byte unsigned integer": CONFIRMED.
    private const int WidthOffset = 0;

    // grid height u32le @ offset 4.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid height 4-byte unsigned integer": CONFIRMED.
    private const int HeightOffset = 4;

    // grid buffer starts at offset 8; length = width × height bytes.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid buffer width × height bytes, 1 byte per cell": CONFIRMED.
    private const int CellsOffset = 8;

    // World-X origin i32le (signed) @ CellsOffset + (width × height).
    // World-Z origin i32le (signed) @ CellsOffset + (width × height) + 4.
    // Signed so maps with negative world extents decode correctly.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed @ 0x08 + W×H": CONFIRMED.
    private const int OriginSize = 4; // each origin field = 4 bytes

    // Minimum valid file size: 4 (width) + 4 (height) + 0 (cells) + 4 (originX) + 4 (originZ).
    private const int MinFileSize = 16; // width=0, height=0 edge case

    // ── public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>region&lt;area&gt;.bin</c> file into a <see cref="RegionGridData"/> record.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS (zero-copy slice).</param>
    /// <returns>The decoded region grid with dimensions, cell bytes, and world origin.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer is too short to contain the header, the declared cell buffer, or the two origin fields.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.
    /// </remarks>
    public static RegionGridData Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // Must contain at least the two 4-byte dimension fields + two 4-byte origin fields.
        if (span.Length < MinFileSize)
            throw new InvalidDataException(
                $"region*.bin parse error: buffer length {span.Length} is too short " +
                $"(minimum {MinFileSize} bytes — 2 dimension fields + 2 origin fields). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.");

        // grid width u32le @ 0.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid width u32": CONFIRMED.
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(span[WidthOffset..]);

        // grid height u32le @ 4.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid height u32": CONFIRMED.
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(span[HeightOffset..]);

        // Validate that the declared cell buffer fits inside the actual buffer.
        // Total expected length = 4 + 4 + (width × height) + 4 + 4.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "width x height bytes" cell buffer: CONFIRMED.
        long cellCount = (long)width * height;
        long expectedTotal = CellsOffset + cellCount + OriginSize + OriginSize;
        if (span.Length < expectedTotal)
            throw new InvalidDataException(
                $"region*.bin parse error: buffer length {span.Length} is too short for a " +
                $"{width}×{height} grid ({cellCount} cells) + origin fields " +
                $"(expected {expectedTotal} bytes). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.");

        // Slice the cell bytes — zero-copy from the original memory.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid buffer … each byte = region id (0..31)": CONFIRMED.
        ReadOnlyMemory<byte> cells = data.Slice(CellsOffset, (int)cellCount);

        int originXOffset = CellsOffset + (int)cellCount;

        // world-X origin i32le (signed) — subtracted before the 256-unit cell quantisation.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed": CONFIRMED.
        int originX = BinaryPrimitives.ReadInt32LittleEndian(span[originXOffset..]);

        // world-Z origin i32le (signed) — subtracted before the 256-unit cell quantisation.
        // spec: Docs/RE/formats/region_grid.md §Layout A — "originZ i32 signed": CONFIRMED.
        int originZ = BinaryPrimitives.ReadInt32LittleEndian(span[(originXOffset + OriginSize)..]);

        return new RegionGridData
        {
            Width = width,
            Height = height,
            Cells = cells,
            OriginX = originX,
            OriginZ = originZ,
        };
    }
}