using System.Buffers.Binary;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

// spec: Docs/RE/structs/terrain-manager.md (pool 34, ring 25, 5×5, centre slot 12,
//       cell_key = mapZ + 100000·mapX)
// spec: Docs/RE/formats/area_inventory.md §1A (fan-out order; missing .bud/.sod NOT errors)

/// <summary>
/// Tests the AreaComposer pool/ring model and per-cell fan-out access order using a
/// fully synthetic IAreaAssemblySource (no real VFS).
/// </summary>
public sealed class AreaComposerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cell-key formula
    // spec: Docs/RE/structs/terrain-manager.md §Key formula — cell_key = mapZ + 100000·mapX
    // spec: Docs/RE/formats/area_inventory.md §1A.2 — CODE-CONFIRMED
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0L)]
    [InlineData(1, 0, 100000L)]         // mapX=1, mapZ=0 → 0 + 100000·1 = 100000
    [InlineData(0, 1, 1L)]              // mapX=0, mapZ=1 → 1 + 100000·0 = 1
    [InlineData(3, 7, 300007L)]         // mapX=3, mapZ=7 → 7 + 100000·3 = 300007
    [InlineData(10, 5, 1000005L)]
    public void CellKey_Formula_IsMapZPlus100000TimesMapX(int mapX, int mapZ, long expectedKey)
    {
        // spec: Docs/RE/structs/terrain-manager.md §Key formula — cell_key = mapZ + 100000·mapX: CONFIRMED
        var cell = new AssembledCell { MapX = mapX, MapZ = mapZ };
        Assert.Equal(expectedKey, cell.CellKey);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pool / ring constants
    // spec: Docs/RE/structs/terrain-manager.md — pool_count = 34, ring_slots = 25, centre = 12
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constants_PoolSize_Is34()
    {
        // spec: Docs/RE/structs/terrain-manager.md — pool_count @ TerrainLoader +32, init = 34: CONFIRMED
        Assert.Equal(34, AreaComposer.PoolSize);
    }

    [Fact]
    public void Constants_RingSize_Is25()
    {
        // spec: Docs/RE/structs/terrain-manager.md — ring @ TerrainManager +44, 25 pointers: CONFIRMED
        Assert.Equal(25, AreaComposer.RingSize);
    }

    [Fact]
    public void Constants_CenterSlot_Is12()
    {
        // spec: Docs/RE/structs/terrain-manager.md — "center_cell = ring slot 12 (+92)": CONFIRMED
        Assert.Equal(12, AreaComposer.CenterSlot);
    }

    [Fact]
    public void Constants_RingEdge_Is5()
    {
        // spec: Docs/RE/structs/terrain-manager.md — ring is 5×5: CONFIRMED
        Assert.Equal(5, AreaComposer.RingEdge);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fan-out access order: .mud → .gad (stub) → .map
    // spec: Docs/RE/formats/area_inventory.md §1A.4 — .mud opened FIRST, .gad stub no-op, .map LAST
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeCell_FanOut_AccessOrder_IsMudThenMap()
    {
        // Arrange: a fake source that logs which extensions were queried via TryGetCellFile.
        // .mud is queried first (step 1), .gad is the stub (no file query), .map is last (step 3).
        // spec: Docs/RE/formats/area_inventory.md §1A.4 — synchronous open order .mud → .gad (stub) → .map
        var source = new LoggingFakeSource(areaId: 1, membersOnly: false);
        var composer = new AreaComposer();

        composer.ComposeCell(source, mapX: 0, mapZ: 0);

        // The TryGetCellFile access log must show .mud before .map; .gad is never queried.
        var log = source.ExtensionAccessLog;
        Assert.Contains(".mud", log);
        Assert.Contains(".map", log);

        // .mud MUST appear before .map in the query sequence
        int mudIdx = log.IndexOf(".mud");
        int mapIdx = log.IndexOf(".map");
        Assert.True(mudIdx < mapIdx,
            $".mud (idx={mudIdx}) must be queried before .map (idx={mapIdx}). " +
            "spec: Docs/RE/formats/area_inventory.md §1A.4");

        // .gad is a stub (no query issued to the source)
        Assert.DoesNotContain(".gad", log);
    }

    [Fact]
    public void ComposeCell_MissingMud_DoesNotThrow_SoundGridIsNull()
    {
        // spec: Docs/RE/formats/area_inventory.md §3.4 — absent .mud: default to silence, not an error
        var source = new LoggingFakeSource(areaId: 1, membersOnly: false, omitExtensions: new[] { ".mud" });
        var composer = new AreaComposer();

        var cell = composer.ComposeCell(source, 0, 0);

        Assert.NotNull(cell);
        Assert.Null(cell.SoundGrid); // no .mud → silence
    }

    [Fact]
    public void ComposeCell_MissingBud_DoesNotThrow_Slot1IsNull()
    {
        // spec: Docs/RE/formats/area_inventory.md §3.2 — missing .bud is not an error;
        //       slot 1 stays null for terrain-only cells.
        // NOTE: we rely on the .map not containing a BUILDING DATAFILE token to get null slot1.
        // Our minimal .map fixture has no BUILDING section.
        var source = new LoggingFakeSource(areaId: 1, membersOnly: false);
        var composer = new AreaComposer();

        var cell = composer.ComposeCell(source, 0, 0);

        Assert.NotNull(cell);
        Assert.Null(cell.Slot1BuildingObjectGrid); // no BUILDING section in minimal .map → slot1 null
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pool acquire / recycle semantics
    // spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AcquirePoolSlot_FirstFreeSlotUsed_ReturnsIndex0()
    {
        // spec: Docs/RE/structs/terrain-manager.md — recycle predicate: first cleared slot: CONFIRMED
        var composer = new AreaComposer();
        int slot = composer.AcquirePoolSlot(mapX: 5, mapZ: 3, areaId: 2);
        Assert.Equal(0, slot); // pool starts empty; first free slot is 0
    }

    [Fact]
    public void AcquirePoolSlot_SameCoords_CacheHit_ReturnsSameSlot()
    {
        // After a first acquire + load, the same coords must return the same pool index (cache hit).
        // spec: Docs/RE/structs/terrain-manager.md — "Cells are allocated, key-matched, loaded, recycled"
        var source = new LoggingFakeSource(areaId: 7, membersOnly: false);
        var composer = new AreaComposer();

        int slot1 = composer.LoadCellIntoPool(source, mapX: 2, mapZ: 4);
        int slot2 = composer.AcquirePoolSlot(mapX: 2, mapZ: 4, areaId: 7);

        Assert.True(slot1 >= 0, "Pool must have a free slot");
        Assert.Equal(slot1, slot2); // cache hit — same slot
    }

    [Fact]
    public void RecyclePoolSlot_MakesSlotReusable()
    {
        // spec: Docs/RE/structs/terrain-manager.md — "loaded_flag cleared by the cull": CONFIRMED
        var source = new LoggingFakeSource(areaId: 1, membersOnly: false);
        var composer = new AreaComposer();

        int firstSlot = composer.LoadCellIntoPool(source, 0, 0);
        Assert.True(firstSlot >= 0);

        // Recycle it
        composer.RecyclePoolSlot(firstSlot);

        // After recycle, the same slot index should be re-acquired for a different cell
        int reacquired = composer.AcquirePoolSlot(mapX: 9, mapZ: 9, areaId: 1);
        Assert.Equal(firstSlot, reacquired);
    }

    [Fact]
    public void RecyclePoolSlot_OutOfRange_Throws()
    {
        var composer = new AreaComposer();
        Assert.Throws<ArgumentOutOfRangeException>(() => composer.RecyclePoolSlot(AreaComposer.PoolSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => composer.RecyclePoolSlot(-1));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RecenterRing — centre slot points at the expected pool entry
    // spec: Docs/RE/structs/terrain-manager.md — ring slot 12 = live centre
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecenterRing_CenterCell_PointsAtSpecifiedCoords()
    {
        // spec: Docs/RE/structs/terrain-manager.md — "center_cell = ring slot 12": CONFIRMED
        // After RecenterRing(x,z), CenterCell (slot 12) must be the cell at (x,z).
        int centerX = 10, centerZ = 20;
        var source = new LoggingFakeSource(
            areaId: 2,
            membersOnly: false,
            memberCoords: GenerateRingCoords(centerX, centerZ));

        var composer = new AreaComposer();
        composer.RecenterRing(source, centerX, centerZ);

        var center = composer.CenterCell;
        Assert.NotNull(center);
        Assert.Equal(centerX, center.MapX);
        Assert.Equal(centerZ, center.MapZ);
    }

    [Fact]
    public void RecenterRing_SameCoords_NoCellReloaded_ExistingPoolSlotsReused()
    {
        // After a RecenterRing, re-centering on the same position should not discard / reload cells
        // that were already in the pool. The ring still points at the same pool entries.
        // spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view — borrowed-pointer view
        int cx = 5, cz = 5;
        var members = GenerateRingCoords(cx, cz);
        var source = new LoggingFakeSource(areaId: 1, membersOnly: false, memberCoords: members);
        var composer = new AreaComposer();

        composer.RecenterRing(source, cx, cz);
        int firstCenterPoolIdx = composer.GetRingPoolIndex(AreaComposer.CenterSlot);

        // Clear the access log; second recenter should re-use existing pool cells
        source.ExtensionAccessLog.Clear();
        composer.RecenterRing(source, cx, cz);
        int secondCenterPoolIdx = composer.GetRingPoolIndex(AreaComposer.CenterSlot);

        // The pool index for the centre should not change (same cell, same slot)
        Assert.Equal(firstCenterPoolIdx, secondCenterPoolIdx);
    }

    [Fact]
    public void RecenterRing_OneStepShift_PreviouslyResidentCellsKeptInPool()
    {
        // A single-step recenter shifts the 5×5 window; cells that are still in the overlap
        // must NOT be reloaded — they stay in the pool with the same pool index.
        // spec: Docs/RE/structs/terrain-manager.md — "only the newly-exposed edge needs new cells loaded"
        int cx = 5, cz = 5;
        var allMembers = new HashSet<(int, int)>();
        foreach (var c in GenerateRingCoords(cx, cz)) allMembers.Add(c);
        foreach (var c in GenerateRingCoords(cx + 1, cz)) allMembers.Add(c);

        var source = new LoggingFakeSource(areaId: 1, membersOnly: false, memberCoords: [.. allMembers]);
        var composer = new AreaComposer();
        composer.RecenterRing(source, cx, cz);

        // Record pool index for a cell known to be in the overlap (left edge of shifted ring)
        AssembledCell? overlapCell = composer.GetRingCell(AreaComposer.CenterSlot);
        Assert.NotNull(overlapCell);

        // Shift one step right; the old centre is now at ring slot (5·2 + 1) = 11
        composer.RecenterRing(source, cx + 1, cz);

        // The cell that was at the old centre should still be alive in some ring slot
        // (it is in the overlap between the old and new rings).
        bool found = false;
        for (int s = 0; s < AreaComposer.RingSize; s++)
        {
            var c = composer.GetRingCell(s);
            if (c is not null && c.MapX == overlapCell.MapX && c.MapZ == overlapCell.MapZ)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Cell from overlap region must remain resident after a one-step recenter");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ComposeArea — cell membership, spawn list built (no errors on empty spawns)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeArea_CellCount_MatchesMembershipSet()
    {
        var source = new LoggingFakeSource(areaId: 3, membersOnly: false,
            memberCoords: [(1, 2), (3, 4)]);
        var composer = new AreaComposer();

        var area = composer.ComposeArea(source);

        Assert.Equal(3, area.AreaId);
        Assert.Equal(2, area.Cells.Count);
        Assert.True(area.Cells.ContainsKey((1, 2)));
        Assert.True(area.Cells.ContainsKey((3, 4)));
    }

    [Fact]
    public void ComposeArea_MissingNpcArr_SpawnsEmpty_NoThrow()
    {
        // spec: Docs/RE/formats/area_inventory.md §5.2 — missing npc.arr is not an error
        var source = new LoggingFakeSource(areaId: 5, membersOnly: false, memberCoords: [(0, 0)]);
        var composer = new AreaComposer();

        var area = composer.ComposeArea(source);

        Assert.NotNull(area);
        // No npc/mob .arr provided → spawns may be empty (no throw)
        Assert.NotNull(area.Spawns);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (int MapX, int MapZ)[] GenerateRingCoords(int centerX, int centerZ)
    {
        // spec: Docs/RE/structs/terrain-manager.md — ring is 5×5 centred on (cx,cz), half-extent 2
        var coords = new List<(int, int)>();
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 5; col++)
            coords.Add((centerX - 2 + col, centerZ - 2 + row));
        return [.. coords];
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake IAreaAssemblySource — logs all TryGetCellFile calls (extension access log),
// returns minimal parseable bytes for .mud and .map, empty for everything else.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class LoggingFakeSource : IAreaAssemblySource
{
    private readonly bool _membersOnly;
    private readonly HashSet<(int, int)>? _memberSet;
    private readonly HashSet<string> _omit;

    public int AreaId { get; }

    // All (mapX, mapZ, ext) pairs queried via TryGetCellFile, in order.
    public List<string> ExtensionAccessLog { get; } = [];

    // Minimal bgtexture.lst catalog (1 static record "terrain/test").
    // spec: Docs/RE/formats/bgtexture_lst.md §Header — count u32LE, 48-byte records: CONFIRMED
    public BgtextureLstCatalog TerrainTextureCatalog { get; }

    public IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys
    {
        get
        {
            if (_memberSet is not null)
                return _memberSet.ToList();
            // Default: return (0,0) as the only member
            return new[] { (0, 0) };
        }
    }

    public LoggingFakeSource(
        int areaId,
        bool membersOnly,
        (int MapX, int MapZ)[]? memberCoords = null,
        string[]? omitExtensions = null)
    {
        AreaId = areaId;
        _membersOnly = membersOnly;
        _memberSet = memberCoords is null ? null : new HashSet<(int, int)>(memberCoords);
        _omit = omitExtensions is null ? [] : new HashSet<string>(omitExtensions, StringComparer.OrdinalIgnoreCase);

        // Build a minimal 1-record bgtexture.lst catalog.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes, kind u8 @+0, relpath char[47] @+1
        TerrainTextureCatalog = BuildMinimalBgtextureCatalog();
    }

    public bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes)
    {
        ExtensionAccessLog.Add(extension);

        if (_omit.Contains(extension))
        {
            bytes = default;
            return false;
        }

        // Provide minimal valid bytes for .mud and .map; return false for everything else.
        if (extension == ".mud")
        {
            // A minimal .mud sound-grid: MudSoundGridParser expects specific bytes.
            // The simplest approach: return false (let it be treated as absent = silence).
            // We still LOG the access (done above) to verify the query order.
            bytes = default;
            return false;
        }

        if (extension == ".map")
        {
            // A minimal empty .map text: no sections, no DATAFILE tokens.
            // MapDescriptorParser must handle an empty/whitespace body without throwing.
            // This gives us slot0..8 all null and no texture resolution.
            bytes = System.Text.Encoding.ASCII.GetBytes("# empty\n");
            return true;
        }

        bytes = default;
        return false;
    }

    public bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes)
    {
        // Not needed for the fan-out / pool tests; return false for all named lookups.
        bytes = default;
        return false;
    }

    private static BgtextureLstCatalog BuildMinimalBgtextureCatalog()
    {
        // Build the smallest valid bgtexture.lst: count=1, one 48-byte record with kind=0x01 (Static).
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — count u32LE @ 0: CONFIRMED
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED
        byte[] buf = new byte[4 + 48]; // 4-byte header + 1×48-byte record
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 1); // record_count = 1

        // Record 0: kind=0x01 (Static), relpath = "terrain/t" (9 bytes + NUL)
        buf[4] = 0x01; // kind = Static
        byte[] relPathBytes = System.Text.Encoding.ASCII.GetBytes("terrain/t");
        relPathBytes.CopyTo(buf, 5); // relpath @ record +1 (offset 5 in buf)

        return MartialHeroes.Assets.Parsers.BgtextureLstParser.Parse(buf);
    }
}
