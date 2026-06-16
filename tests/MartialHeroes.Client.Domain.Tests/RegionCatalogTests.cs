using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Enums;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Fixture-based tests for <see cref="RegionCatalog"/>.
/// All fixtures are built in-memory with synthetic region grids and zone-type tables.
/// No real game asset files are required.
/// spec: Docs/RE/specs/world_systems.md Ch. 16.
/// </summary>
public sealed class RegionCatalogTests
{
    // =========================================================================
    // Helpers — synthetic grid / table builders
    // =========================================================================

    /// <summary>
    /// Builds a flat row-major cell array for a width×height grid where every cell has
    /// <paramref name="regionId"/>.
    /// </summary>
    private static byte[] UniformGrid(uint width, uint height, byte regionId)
    {
        byte[] cells = new byte[(int)(width * height)];
        Array.Fill(cells, regionId);
        return cells;
    }

    /// <summary>
    /// Builds a 32-element raw zone-type array where all entries are <paramref name="defaultType"/>
    /// except those explicitly overridden via <paramref name="overrides"/>.
    /// </summary>
    private static uint[] ZoneTypeArray(uint defaultType = 0, params (int regionId, uint zoneType)[] overrides)
    {
        uint[] table = new uint[32];
        Array.Fill(table, defaultType);
        foreach (var (id, zt) in overrides)
            table[id] = zt;
        return table;
    }

    // =========================================================================
    // Construction guard tests
    // =========================================================================

    [Fact]
    public void Constructor_ThrowsWhen_CellsLengthMismatch()
    {
        // cells.Length ≠ width × height must be rejected.
        Assert.Throws<ArgumentException>(() =>
            new RegionCatalog(
                width: 3, height: 3,
                cells: new byte[8], // should be 9
                originX: 0, originZ: 0,
                rawZoneTypes: ZoneTypeArray()));
    }

    [Fact]
    public void Constructor_ThrowsWhen_ZoneTableNot32Entries()
    {
        // rawZoneTypes must have exactly 32 entries.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed 32 records": CONFIRMED.
        Assert.Throws<ArgumentException>(() =>
            new RegionCatalog(
                width: 1, height: 1,
                cells: new byte[] { 0 },
                originX: 0, originZ: 0,
                rawZoneTypes: new uint[16])); // wrong count
    }

    // =========================================================================
    // ZoneType resolution tests
    // =========================================================================

    [Fact]
    public void Resolve_RegionId1_ReturnsOpenPvp()
    {
        // A cell with region id = 1, zone-type table[1] = 1 (OpenPvp) → must return OpenPvp.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED (OpenPvp).
        byte[] cells = UniformGrid(4, 4, regionId: 1);
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (1, 1u)); // region 1 = OpenPvp
        var catalog = new RegionCatalog(4, 4, cells, originX: 0, originZ: 0, zoneTypes);

        ZoneType result = catalog.Resolve(worldX: 256f, worldZ: 256f); // cell (1,1)

        Assert.Equal(ZoneType.OpenPvp, result);
    }

    [Fact]
    public void Resolve_RegionId2_ReturnsClosed()
    {
        // A cell with region id = 2, zone-type table[2] = 2 (Closed) → must return Closed.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 2: CONFIRMED (Closed).
        byte[] cells = UniformGrid(2, 2, regionId: 2);
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (2, 2u));
        var catalog = new RegionCatalog(2, 2, cells, originX: 0, originZ: 0, zoneTypes);

        ZoneType result = catalog.Resolve(worldX: 100f, worldZ: 100f);

        Assert.Equal(ZoneType.Closed, result);
    }

    [Fact]
    public void Resolve_RegionId0_ReturnsSafe()
    {
        // Region id 0, zone-type table[0] = 0 (Safe) → must return Safe.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 0: PLAUSIBLE (Safe).
        byte[] cells = UniformGrid(2, 2, regionId: 0);
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0); // all Safe
        var catalog = new RegionCatalog(2, 2, cells, originX: 0, originZ: 0, zoneTypes);

        ZoneType result = catalog.Resolve(worldX: 50f, worldZ: 50f);

        Assert.Equal(ZoneType.Safe, result);
    }

    [Fact]
    public void Resolve_OutOfBounds_ReturnsSafeViaRegionId0()
    {
        // Position outside grid → region id 0 → ZoneType based on table[0].
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "yields region id 0": CONFIRMED.
        byte[] cells = UniformGrid(2, 2, regionId: 1);
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (0, 0u)); // region 0 = Safe
        var catalog = new RegionCatalog(2, 2, cells, originX: 0, originZ: 0, zoneTypes);

        // worldX=9999 is far outside a 2×2 grid of 256-unit cells (ends at 512).
        ZoneType result = catalog.Resolve(worldX: 9999f, worldZ: 9999f);

        Assert.Equal(ZoneType.Safe, result);
    }

    [Fact]
    public void Resolve_WithOriginOffset_CellMathCorrect()
    {
        // Grid has originX=1000, originZ=2000. A position at (1256, 2256) → col=1, row=1.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "col = (worldX - originX) / 256": CONFIRMED.
        // 3×3 grid, cell (1,1) = region id 7; table[7] = 2 (Closed).
        byte[] cells = new byte[9]; // all 0 except index 4 = cell(1,1)
        cells[4] = 7;
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (7, 2u));
        var catalog = new RegionCatalog(3, 3, cells, originX: 1000, originZ: 2000, zoneTypes);

        // col = (1256 - 1000) / 256 = 256/256 = 1
        // row = (2256 - 2000) / 256 = 256/256 = 1
        // index = 1 + 1*3 = 4 → cells[4] = 7 → table[7] = 2 → Closed
        ZoneType result = catalog.Resolve(worldX: 1256f, worldZ: 2256f);

        Assert.Equal(ZoneType.Closed, result);
    }

    [Fact]
    public void Resolve_RawZoneType3_ReturnsSafe()
    {
        // A raw zone-type value ≥ 3 falls through to ZoneType.Safe — the enum is CONFIRMED-COMPLETE
        // at three values and 3..31 behave as the default (safe) case.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — values 3..31 = default (safe): CONFIRMED-COMPLETE.
        byte[] cells = UniformGrid(1, 1, regionId: 5);
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (5, 3u)); // region 5 = raw 3
        var catalog = new RegionCatalog(1, 1, cells, originX: 0, originZ: 0, zoneTypes);

        ZoneType result = catalog.Resolve(worldX: 100f, worldZ: 100f);

        Assert.Equal(ZoneType.Safe, result);
    }

    // =========================================================================
    // ZoneTypeForRegion tests
    // =========================================================================

    [Fact]
    public void ZoneTypeForRegion_KnownId_ReturnsCorrectType()
    {
        // Direct lookup by region id without a world position.
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (3, 1u), (10, 2u));
        var catalog = new RegionCatalog(1, 1, new byte[] { 0 }, originX: 0, originZ: 0, zoneTypes);

        Assert.Equal(ZoneType.OpenPvp, catalog.ZoneTypeForRegion(3));
        Assert.Equal(ZoneType.Closed, catalog.ZoneTypeForRegion(10));
    }

    [Fact]
    public void ZoneTypeForRegion_NegativeId_ReturnsOpenPvp()
    {
        // Region id < 0 has no record → defaults to OpenPvp (the combat arbiter fallback).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "missing record treated as type 1": CONFIRMED.
        uint[] zoneTypes = ZoneTypeArray();
        var catalog = new RegionCatalog(1, 1, new byte[] { 0 }, originX: 0, originZ: 0, zoneTypes);

        Assert.Equal(ZoneType.OpenPvp, catalog.ZoneTypeForRegion(-1));
    }

    [Fact]
    public void ZoneTypeForRegion_IdAbove31_ReturnsOpenPvp()
    {
        // Region id ≥ 32 has no record → defaults to OpenPvp.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — "missing record treated as type 1": CONFIRMED.
        uint[] zoneTypes = ZoneTypeArray();
        var catalog = new RegionCatalog(1, 1, new byte[] { 0 }, originX: 0, originZ: 0, zoneTypes);

        Assert.Equal(ZoneType.OpenPvp, catalog.ZoneTypeForRegion(32));
        Assert.Equal(ZoneType.OpenPvp, catalog.ZoneTypeForRegion(255));
    }

    // =========================================================================
    // Edge: mixed region ids in the same grid
    // =========================================================================

    [Fact]
    public void Resolve_MixedGrid_CorrectCellPicked()
    {
        // 2×1 grid: cell(0,0) = region 0 (Safe), cell(1,0) = region 1 (OpenPvp).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — index = col + row * width: CONFIRMED.
        byte[] cells = { 0, 1 }; // 2 cells: col=0 → id 0, col=1 → id 1
        uint[] zoneTypes = ZoneTypeArray(defaultType: 0, (1, 1u));
        var catalog = new RegionCatalog(2, 1, cells, originX: 0, originZ: 0, zoneTypes);

        // col = 0 → cell[0] = 0 → Safe
        Assert.Equal(ZoneType.Safe, catalog.Resolve(worldX: 0f, worldZ: 0f));
        // col = 1 → cell[1] = 1 → OpenPvp
        Assert.Equal(ZoneType.OpenPvp, catalog.Resolve(worldX: 256f, worldZ: 0f));
    }
}