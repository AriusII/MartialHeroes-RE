namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// The decoded region grid loaded from <c>region&lt;area&gt;.bin</c>.
/// </summary>
/// <remarks>
/// <para>
/// The grid quantizes the world into 256-unit cells. Each cell holds a region id (0..31) that
/// indexes the parallel zone-type table (<c>regiontable&lt;area&gt;.bin</c>).
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.
/// </para>
/// <para>
/// World-position → region id lookup:
/// <code>
///   col       = (worldX - OriginX) / 256
///   row       = (worldZ - OriginZ) / 256
///   index     = col + row * Width
///   region_id = Cells[index]          // unsigned byte, 0..31
/// </code>
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "World position to region id": CONFIRMED.
/// </para>
/// <para>
/// If the grid is not loaded, or <c>index &gt;= Width × Height</c>, the lookup yields region id
/// 0 (treated as safe / default).
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "yields region id 0": CONFIRMED.
/// </para>
/// </remarks>
public sealed class RegionGridData
{
    /// <summary>
    /// Number of columns in the grid.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid width u32 @ offset 0": CONFIRMED.
    /// </summary>
    public required uint Width { get; init; }

    /// <summary>
    /// Number of rows in the grid.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid height u32 @ offset 4": CONFIRMED.
    /// </summary>
    public required uint Height { get; init; }

    /// <summary>
    /// Flat cell array — one byte per cell, row-major order. Length = <see cref="Width"/> × <see cref="Height"/>.
    /// Each byte is a region id in the range 0..31.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "grid buffer: width × height bytes, 1 byte per cell": CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> Cells { get; init; }

    /// <summary>
    /// World-space X origin subtracted before the 256-unit quantisation.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "world-X origin u32 @ offset 8 + width*height": CONFIRMED.
    /// </summary>
    public required uint OriginX { get; init; }

    /// <summary>
    /// World-space Z origin subtracted before the 256-unit quantisation.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "world-Z origin u32 @ offset 12 + width*height": CONFIRMED.
    /// </summary>
    public required uint OriginZ { get; init; }
}