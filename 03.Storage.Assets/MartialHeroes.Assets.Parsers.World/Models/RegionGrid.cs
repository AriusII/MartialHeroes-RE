namespace MartialHeroes.Assets.Parsers.World.Models;

// spec: Docs/RE/formats/region_grid.md

/// <summary>
///     The decoded runtime region/zone-id grid loaded from <c>region&lt;NNN&gt;.bin</c>.
/// </summary>
/// <remarks>
///     <para>
///         This models the RUNTIME <c>region&lt;NNN&gt;.bin</c> layout ONLY. The authoring-side
///         <c>.tol</c> format is editor-only, never loaded by the shipped client, and is intentionally
///         NOT parsed here.
///         spec: Docs/RE/formats/region_grid.md §Two layouts — ".tol (authoring / tool-side — not read by the shipped
///         client)".
///     </para>
///     <para>
///         Binary layout of <c>region&lt;NNN&gt;.bin</c> (all fields little-endian):
///         <code>
///   +0x00            u32le  width      (grid columns, cells along X)
///   +0x04            u32le  height     (grid rows, cells along Z)
///   +0x08            u8[]   cells      (width × height bytes, row-major region IDs)
///   +0x08 + W×H      u32le  originX    (world-space X origin)
///   +0x08 + W×H + 4  u32le  originZ    (world-space Z origin)
///   Total = 16 + width × height bytes
/// </code>
///         spec: Docs/RE/formats/region_grid.md §Layout A — region&lt;NNN&gt;.bin (RUNTIME): HIGH (parser).
///     </para>
///     <para>
///         Cell stride: 256 world units per cell.
///         spec: Docs/RE/formats/region_grid.md §Identification — "Cell-to-world stride: 256 world units per cell":
///         CONFIRMED.
///     </para>
///     <para>
///         World-position → region-id lookup:
///         <code>
///   col      = (worldX - OriginX) / 256
///   row      = (worldZ - OriginZ) / 256
///   index    = col + row × Width
///   regionId = Cells[index]       // u8, range 0..31
/// </code>
///         spec: Docs/RE/formats/region_grid.md §Runtime use — "index = (X − originX) / 256 + (Z − originZ) / 256 ×
///         width": HIGH (parser).
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class RegionGrid
{
    /// <summary>
    ///     Cell stride in world units.
    ///     spec: Docs/RE/formats/region_grid.md §Identification — "Cell-to-world stride: 256 world units per cell": CONFIRMED.
    /// </summary>
    public const int CellWorldSize = 256; // spec: Docs/RE/formats/region_grid.md §Identification

    /// <summary>
    ///     Number of grid columns (cells along the X axis).
    ///     spec: Docs/RE/formats/region_grid.md §Layout A — "width u32le @ 0x00": HIGH (parser).
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    ///     Number of grid rows (cells along the Z axis).
    ///     spec: Docs/RE/formats/region_grid.md §Layout A — "height u32le @ 0x04": HIGH (parser).
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    ///     Flat cell array, row-major (index = col + row × <see cref="Width" />).
    ///     Each byte is a region ID in the range 0..31; 0 = none / no-region.
    ///     Length = <see cref="Width" /> × <see cref="Height" />.
    ///     spec: Docs/RE/formats/region_grid.md §Layout A — "regionIdGrid u8[] @ 0x08, width × height bytes, row-major": HIGH
    ///     (parser).
    ///     spec: Docs/RE/formats/region_grid.md §Grid body layout — "0 = none; 1..31 = region record slot".
    /// </summary>
    public required byte[] Cells { get; init; }

    /// <summary>
    ///     World-space X origin subtracted before the 256-unit cell quantisation.
    ///     spec: Docs/RE/formats/region_grid.md §Layout A — "originX u32le @ 0x08 + W×H": HIGH (parser).
    /// </summary>
    public required int OriginX { get; init; }

    /// <summary>
    ///     World-space Z origin subtracted before the 256-unit cell quantisation.
    ///     spec: Docs/RE/formats/region_grid.md §Layout A — "originZ u32le @ 0x08 + W×H + 4": HIGH (parser).
    /// </summary>
    public required int OriginZ { get; init; }

    // ── World-position lookup helper ──────────────────────────────────────────

    /// <summary>
    ///     Returns the region ID at the given world-space position, or 0 if out of bounds.
    /// </summary>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <returns>Region ID byte (0..31). 0 = none / no-region.</returns>
    /// <remarks>
    ///     Formula: index = (X − originX) / 256 + (Z − originZ) / 256 × width.
    ///     spec: Docs/RE/formats/region_grid.md §Runtime use — world-position → region-id lookup: HIGH (parser).
    /// </remarks>
    public byte GetRegionId(int worldX, int worldZ)
    {
        // spec: Docs/RE/formats/region_grid.md §Runtime use — "Translate the world position by the stored origin": HIGH.
        var col = (worldX - OriginX) / CellWorldSize; // spec: Docs/RE/formats/region_grid.md §Runtime use
        var row = (worldZ - OriginZ) / CellWorldSize; // spec: Docs/RE/formats/region_grid.md §Runtime use

        // Bounds check: yields 0 (none / default) when out of range.
        // spec: Docs/RE/formats/region_grid.md §Runtime use — "Compute the row-major index and bounds-check against width × height".
        if ((uint)col >= (uint)Width || (uint)row >= (uint)Height)
            return 0;

        return Cells
            [col + row * Width]; // spec: Docs/RE/formats/region_grid.md §Grid body layout — "index = col + row × width"
    }
}