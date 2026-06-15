using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// Pure, deterministic catalogue that resolves a world position (X, Z) to a <see cref="ZoneType"/>
/// by combining the per-area 256-unit region-id grid and the parallel zone-type table.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is <b>engine-free</b> and holds no rendering dependencies. It is loaded once when
/// the active map area is set and queried on every combat-arbiter, movement-gate, and target-
/// validity check.
/// </para>
/// <para>
/// <b>Resolution chain (world XZ → ZoneType):</b>
/// <code>
///   col       = (worldX − originX) / 256         // integer division
///   row       = (worldZ − originZ) / 256
///   index     = col + row × width
///   regionId  = gridCells[index]                 // 0..31
///   zoneType  = zoneTypeTable[regionId]
/// </code>
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 (grid lookup) and §16.3 (enum).
/// </para>
/// <para>
/// <b>Out-of-bounds / missing-record rules:</b>
/// <list type="bullet">
///   <item>If the grid is not loaded or the index is out of bounds → region id 0 →
///     <see cref="ZoneType.Safe"/>.<br/>
///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "yields region id 0": CONFIRMED.</item>
///   <item>If region id ≥ 32 (no record in the table) → defaults to <see cref="ZoneType.OpenPvp"/>
///     (1), matching the combat arbiter's documented fallback.<br/>
///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "missing record treated as type 1": CONFIRMED.</item>
///   <item>If the raw zone-type value is 0, 1, or 2 → maps to the corresponding
///     <see cref="ZoneType"/> member. Values ≥ 3 map to <see cref="ZoneType.Unknown"/>.<br/>
///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — values 0 (PLAUSIBLE), 1 (CONFIRMED), 2 (CONFIRMED), 3+ (UNVERIFIED).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RegionCatalog
{
    // Cell size in world units.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "Cell size = 256 world units per axis": CONFIRMED.
    private const int CellSize = 256;

    // Maximum valid region id (inclusive). Region table has exactly 32 records (0..31).
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed 32 records, indexed directly by region id (0..31)": CONFIRMED.
    private const int MaxRegionId = 31;

    // ── state ─────────────────────────────────────────────────────────────────

    private readonly uint _width;

    private readonly uint _height;

    // Origins are SIGNED (i32): region<area>.bin stores originX/originZ as i32le so maps with
    // negative world extents address correctly.
    // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed / originZ i32 signed": CONFIRMED.
    private readonly int _originX;
    private readonly int _originZ;
    private readonly byte[] _cells; // length = _width × _height
    private readonly ZoneType[] _zoneTypes; // length = 32, indexed by region id

    // ── construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="RegionCatalog"/> from the decoded grid and zone-type table.
    /// </summary>
    /// <param name="width">Grid column count (from <c>region&lt;area&gt;.bin</c> header).</param>
    /// <param name="height">Grid row count (from <c>region&lt;area&gt;.bin</c> header).</param>
    /// <param name="cells">
    /// Flat cell bytes, length must equal <paramref name="width"/> × <paramref name="height"/>.
    /// Each byte is a region id 0..31.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "1 byte per cell = region id (0..31)": CONFIRMED.
    /// </param>
    /// <param name="originX">
    /// World-X origin (SIGNED i32) subtracted before the 256-unit quantise.
    /// spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed": CONFIRMED.
    /// </param>
    /// <param name="originZ">
    /// World-Z origin (SIGNED i32) subtracted before the 256-unit quantise.
    /// spec: Docs/RE/formats/region_grid.md §Layout A — "originZ i32 signed": CONFIRMED.
    /// </param>
    /// <param name="rawZoneTypes">
    /// Array of exactly 32 raw u32 zone-type values, one per region id (0..31), as read from
    /// <c>regiontable&lt;area&gt;.bin</c> at record offset +40.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "zone type u32 @ +40": CONFIRMED (encoding).
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="cells"/> length ≠ <paramref name="width"/> × <paramref name="height"/>,
    /// or <paramref name="rawZoneTypes"/> length ≠ 32.
    /// </exception>
    public RegionCatalog(
        uint width,
        uint height,
        ReadOnlySpan<byte> cells,
        int originX,
        int originZ,
        ReadOnlySpan<uint> rawZoneTypes)
    {
        long expectedCells = (long)width * height;
        if (cells.Length != expectedCells)
            throw new ArgumentException(
                $"RegionCatalog: cells length {cells.Length} ≠ width × height ({width} × {height} = {expectedCells}). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.");

        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed 32 records": CONFIRMED.
        if (rawZoneTypes.Length != 32)
            throw new ArgumentException(
                $"RegionCatalog: rawZoneTypes length {rawZoneTypes.Length} ≠ 32 (the fixed record count). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.");

        _width = width;
        _height = height;
        _originX = originX;
        _originZ = originZ;
        _cells = cells.ToArray();

        // Convert raw u32 values to ZoneType enum.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — enum values 0/1/2 (3+ = Unknown).
        _zoneTypes = new ZoneType[32];
        for (int i = 0; i < 32; i++)
            _zoneTypes[i] = ToZoneType(rawZoneTypes[i]);
    }

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a world position (X, Z) to a <see cref="ZoneType"/>.
    /// </summary>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <returns>
    /// The zone type for the 256-unit cell that contains <paramref name="worldX"/>,
    /// <paramref name="worldZ"/>. Returns <see cref="ZoneType.Safe"/> if the position is outside
    /// the loaded grid; returns <see cref="ZoneType.OpenPvp"/> if region id ≥ 32.
    /// </returns>
    /// <remarks>
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 (grid lookup) and §16.3 (enum).
    /// </remarks>
    public ZoneType Resolve(float worldX, float worldZ)
    {
        // Integer cell column and row — integer division after subtracting the world origin.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "col = (worldX - originX) / 256; row = (worldZ - originZ) / 256": CONFIRMED.
        int col = (int)((worldX - _originX) / CellSize);
        int row = (int)((worldZ - _originZ) / CellSize);

        // Bounds check: out-of-bounds → region id 0.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "yields region id 0": CONFIRMED.
        if (col < 0 || row < 0 || (uint)col >= _width || (uint)row >= _height)
            return _zoneTypes[0]; // region id 0

        int index = col + row * (int)_width;

        // Additional check (safety for degenerate grids).
        if ((uint)index >= (uint)_cells.Length)
            return _zoneTypes[0]; // region id 0

        // Fetch region id (unsigned byte 0..31).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "region_id = gridBuffer[index]; unsigned byte, 0..31": CONFIRMED.
        int regionId = _cells[index];

        // If region id ≥ 32 there is no record → default to OpenPvp (1).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "missing record treated as type 1": CONFIRMED.
        if (regionId > MaxRegionId)
            return ZoneType.OpenPvp;

        return _zoneTypes[regionId];
    }

    /// <summary>
    /// Looks up the <see cref="ZoneType"/> for a given region id directly (without a world position).
    /// Region id ≥ 32 returns <see cref="ZoneType.OpenPvp"/> (the combat-arbiter default).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "missing record treated as type 1": CONFIRMED.
    /// </summary>
    public ZoneType ZoneTypeForRegion(int regionId)
    {
        if (regionId < 0 || regionId > MaxRegionId)
            return ZoneType.OpenPvp; // spec: §16.3 — missing record = type 1: CONFIRMED.
        return _zoneTypes[regionId];
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a raw u32 zone-type value to a <see cref="ZoneType"/> enum member.
    /// Values ≥ 3 map to <see cref="ZoneType.Unknown"/> (UNVERIFIED territory).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
    /// </summary>
    private static ZoneType ToZoneType(uint raw) => raw switch
    {
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 0: PLAUSIBLE (Safe).
        0 => ZoneType.Safe,
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED (OpenPvp).
        1 => ZoneType.OpenPvp,
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 2: CONFIRMED (Closed).
        2 => ZoneType.Closed,
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — values 3+: UNVERIFIED.
        _ => ZoneType.Unknown,
    };
}