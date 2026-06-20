using MartialHeroes.Assets.Parsers.Audio;
using MartialHeroes.Assets.Parsers.Audio.Models;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Parsers.World;

namespace MartialHeroes.Assets.Mapping;

// spec: Docs/RE/specs/assembly_graph.md §4 — AreaComposer contract (Phase 5):
//   cell store=34 (owner/recycle), ring=25 (view), centre=ring slot 12, render layers=9 slots,
//   synchronous open order, sub-assets from .map DATAFILE tokens.
// spec: Docs/RE/structs/terrain-manager.md — pool/ring model, 9 sub-manager slots.
// spec: Docs/RE/formats/area_inventory.md §1A — area→cell fan-out, per-cell open order.
// spec: Docs/RE/formats/terrain.md — .ted TextureIndexGrid, .map TERRAIN/BUILDING TEXTURES.
// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — intTexId IS 0-based pool slot, NO −1.
// spec: Docs/RE/formats/npc_spawns.md — NPC .arr 28-byte records; yaw = π/2 − rawFacing.

/// <summary>
///     Engine-free, deterministic composer that consumes an <see cref="IAreaAssemblySource" /> and
///     emits the neutral <see cref="AssembledArea" /> / <see cref="AssembledCell" /> model.
/// </summary>
/// <remarks>
///     <para>
///         Faithfully models the original terrain-manager/loader pair:
///         <list type="bullet">
///             <item>
///                 A <b>34-slot owner pool</b> (keyed by <c>(mapX, mapZ, areaId)</c>, loaded flag, recycle on
///                 first cleared slot) is the single authoritative owner of live cells.
///                 spec: Docs/RE/structs/terrain-manager.md §"Pool ownership vs ring view" — CONFIRMED.
///                 Pool size: 34.
///                 spec: Docs/RE/structs/terrain-manager.md — pool_count @ TerrainLoader +32, initialised to 34:
///                 CONFIRMED.
///             </item>
///             <item>
///                 A <b>25-slot borrowed-pointer ring</b> (5×5, row-major, slot = 5·row+col, centre = slot 12)
///                 is a spatial VIEW into the pool — it never owns cells.
///                 spec: Docs/RE/structs/terrain-manager.md — ring @ TerrainManager +44, 25 pointers: CONFIRMED.
///                 Ring size: 25. Ring edge: 5. Centre slot: 12.
///                 spec: Docs/RE/structs/terrain-manager.md — "centre_cell = ring slot 12 (+92)": CONFIRMED.
///             </item>
///             <item>
///                 Per-cell fan-out: synchronous <c>.mud</c> → <c>.gad</c> (stub/no-op) → <c>.map</c>;
///                 sub-assets from <c>DATAFILE</c> tokens inside the <c>.map</c> parse (NOT cell-key printf).
///                 spec: Docs/RE/formats/area_inventory.md §1A.4 — CODE-CONFIRMED.
///             </item>
///             <item>
///                 Texture resolution: <c>.ted</c> byte → idx-1 finalize (−1 on .ted byte ONLY; clamp [1,count])
///                 → <c>.map TEXTURES[idx-1].intTexId</c> → <c>BgTextureCatalog[intTexId]</c> (NO further −1) →
///                 <c>data/map000/texture/&lt;rel&gt;.dds</c>.
///                 spec: Docs/RE/specs/assembly_graph.md §1 — "Texture resolution (global under map000)": CONFIRMED.
///             </item>
///         </list>
///     </para>
///     <para>ZERO rendering/engine dependency.</para>
/// </remarks>
public sealed class AreaComposer
{
    // ── Pool / ring constants ─────────────────────────────────────────────────

    /// <summary>
    ///     Number of owner pool slots.
    ///     spec: Docs/RE/structs/terrain-manager.md — pool_count @ TerrainLoader +32, init = 34: CONFIRMED.
    /// </summary>
    public const int PoolSize = 34; // spec: terrain-manager.md — TerrainLoader pool_count = 34: CONFIRMED

    /// <summary>
    ///     Number of ring (borrowed-pointer view) slots.
    ///     spec: Docs/RE/structs/terrain-manager.md — ring @ TerrainManager +44, 25 pointers: CONFIRMED.
    /// </summary>
    public const int RingSize = 25; // spec: terrain-manager.md — TerrainManager ring_slots, 25 entries: CONFIRMED

    /// <summary>
    ///     One axis of the 5×5 ring grid.
    ///     spec: Docs/RE/structs/terrain-manager.md — ring is a 5×5 spatial window: CONFIRMED.
    /// </summary>
    public const int RingEdge = 5; // spec: terrain-manager.md — ring is 5×5: CONFIRMED

    /// <summary>
    ///     Index of the live centre cell within the ring (0-based, row-major 5×5, centre = row 2 col 2).
    ///     spec: Docs/RE/structs/terrain-manager.md — "center_cell = ring slot 12 (+92)": CONFIRMED.
    /// </summary>
    public const int CenterSlot = 12; // spec: terrain-manager.md — ring slot 12 is live centre: CONFIRMED

    // ── Owner pool (34 entries) ───────────────────────────────────────────────

    private readonly PoolEntry[] _pool = CreatePool(); // spec: terrain-manager.md — pool_count = 34: CONFIRMED

    // ── Ring (25 borrowed-pointer indices into the pool) ──────────────────────
    // Ring slot → pool index; -1 means the ring slot is empty.
    // Row-major: ring[5·row + col] → pool index.
    // spec: Docs/RE/structs/terrain-manager.md — ring stored row-major (slot = 5·row+col): CONFIRMED.

    private readonly int[] _ring = Enumerable.Repeat(-1, RingSize).ToArray();

    // ── Centre tracking ───────────────────────────────────────────────────────

    private int _centerMapX;
    private int _centerMapZ;
    private bool _ringInitialized;

    // ── Ring centre property ──────────────────────────────────────────────────

    /// <summary>
    ///     The live centre cell (ring slot 12), or <see langword="null" /> when no cell is centred.
    ///     spec: Docs/RE/structs/terrain-manager.md — "center_cell = ring slot 12 (+92)": CONFIRMED.
    /// </summary>
    public AssembledCell? CenterCell
    {
        get
        {
            var poolIdx = _ring[CenterSlot]; // spec: terrain-manager.md — centre = ring slot 12: CONFIRMED
            return poolIdx >= 0 ? _pool[poolIdx].Cell : null;
        }
    }

    private static PoolEntry[] CreatePool()
    {
        var p = new PoolEntry[PoolSize]; // spec: terrain-manager.md — TerrainLoader pool_count = 34: CONFIRMED
        for (var i = 0; i < PoolSize; i++) p[i] = new PoolEntry();
        return p;
    }

    // ── ComposeArea ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Composes the full area from the given source: reads the cell-key membership set, loads all
    ///     per-area binaries and spawns, and assembles every cell in the membership set.
    /// </summary>
    /// <param name="source">The VFS-backed (or synthetic) assembly source.</param>
    /// <returns>The fully assembled area model.</returns>
    /// <remarks>
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — "Phase A — area load (the area orchestrator)":
    ///     area id → d&lt;NNN&gt;.lst cell-key set → area binaries → spawns. CONFIRMED.
    ///     spec: Docs/RE/formats/area_inventory.md §1A.3 — 4 per-area binaries opened before any cell streams.
    /// </remarks>
    public AssembledArea ComposeArea(IAreaAssemblySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var cellKeys = source.AreaCellKeys;

        // Build NPC spawns from npc<NNN>.arr.
        // spec: Docs/RE/formats/area_inventory.md §1A.3 — npc<NNN>.arr opened as per-area binary #4. CODE-CONFIRMED.
        // spec: Docs/RE/specs/assembly_graph.md §1 — npc.arr supplies position/facing/static metadata.
        var spawns = BuildSpawns(source);

        // Assemble each cell in the membership set.
        var cells = new Dictionary<(int MapX, int MapZ), AssembledCell>(cellKeys.Count);
        foreach (var (mapX, mapZ) in cellKeys)
        {
            var cell = ComposeCell(source, mapX, mapZ);
            cells[(mapX, mapZ)] = cell;
        }

        return new AssembledArea
        {
            AreaId = source.AreaId,
            CellKeys = cellKeys.ToList(),
            Spawns = spawns,
            Cells = cells
        };
    }

    // ── ComposeCell ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Composes one cell via the synchronous per-cell fan-out:
    ///     <c>.mud</c> → <c>.gad</c> (stub/no-op) → <c>.map</c>, then sub-assets from DATAFILE tokens.
    /// </summary>
    /// <param name="source">The assembly source (VFS-backed or synthetic).</param>
    /// <param name="mapX">X grid index of the cell.</param>
    /// <param name="mapZ">Z grid index of the cell.</param>
    /// <returns>The assembled cell, with all available slots populated.</returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/area_inventory.md §1A.4 — synchronous per-cell open order:
    ///     .mud → .gad (stub) → .map; sub-assets from DATAFILE tokens in section order. CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/area_inventory.md §1A.5 — d&lt;NNN&gt;x..z.. printf patterns are a
    ///     dev exporter, NOT the loader; sub-asset filenames are DATA-DRIVEN (DATAFILE tokens). CODE-CONFIRMED.
    ///     Missing .bud/.sod/.mud are NOT errors.
    ///     spec: Docs/RE/formats/area_inventory.md §3.1/§3.2/§3.4 — CONFIRMED.
    /// </remarks>
    public AssembledCell ComposeCell(IAreaAssemblySource source, int mapX, int mapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        // ── Step 1: .mud (ambient sound grid) ────────────────────────────────
        // Opened first in the per-cell loader.
        // spec: Docs/RE/formats/area_inventory.md §1A.4 — .mud opened FIRST: CODE-CONFIRMED.
        MudSoundGrid? soundGrid = null;
        if (source.TryGetCellFile(mapX, mapZ, ".mud", out var mudBytes))
            try
            {
                soundGrid = MudSoundGridParser.Parse(mudBytes);
            }
            catch (InvalidDataException)
            {
                // Corrupted .mud — treat as absent (no sound); not a fatal error.
            }
        // Missing .mud → silence. 20 areas carry zero .mud files — not an error.
        // spec: Docs/RE/formats/area_inventory.md §3.4 — absent .mud: default to silence.

        // ── Step 2: .gad (stub — no-op) ──────────────────────────────────────
        // The .gad call always succeeds without opening a file in this build (dead scaffolding).
        // spec: Docs/RE/formats/area_inventory.md §1A.4 — .gad is a STUB in this build:
        //   "the call always succeeds without ever opening a file (dead / disabled scaffolding)."
        //   CODE-CONFIRMED. No .gad files exist in the census. Treat as a no-op.

        // ── Step 3: .map (text descriptor → DATAFILE sub-assets) ─────────────
        // Opened last; all sub-assets come from DATAFILE tokens within its parse.
        // spec: Docs/RE/formats/area_inventory.md §1A.4 — .map opened LAST; sub-assets from DATAFILE tokens: CODE-CONFIRMED.
        if (!source.TryGetCellFile(mapX, mapZ, ".map", out var mapBytes))
            // No .map → cell is empty (no sub-assets possible).
            return new AssembledCell
            {
                MapX = mapX,
                MapZ = mapZ,
                SoundGrid = soundGrid
            };

        var mapDesc = MapDescriptorParser.Parse(mapBytes);

        // ── Step 4: sub-assets from DATAFILE tokens in section order ──────────
        // spec: Docs/RE/formats/area_inventory.md §1A.4 — "sub-asset filenames are DATA-DRIVEN — read as
        //   literal DATAFILE tokens from the .map text — NOT derived from a cell-key→filename rule."
        //   CODE-CONFIRMED.
        TerrainCell? slot0Ted = null;
        BudScene? slot1Bud = null;
        Fx1Layer? slot2Fx1 = null;
        Fx2Layer? slot3Fx2 = null;
        Fx3Layer? slot4Fx3 = null;
        Fx4Layer? slot5Fx4 = null;
        Fx5Layer? slot6Fx5 = null;
        Fx6Layer? slot7Fx6 = null;
        Fx7Layer? slot8Fx7 = null;
        SodBlob? collision = null;
        (int Flag, int TexId)[]? terrainTextures = null;

        foreach (var section in mapDesc.Sections)
        {
            var key = section.Keyword; // Already upper-cased by the parser.

            // Capture TERRAIN section textures for the resolution chain.
            if (key == "TERRAIN" && section.Textures.Length > 0)
                terrainTextures = section.Textures;

            // Route DATAFILE token to the right slot.
            if (section.DataFile is null)
                continue;

            // Each DATAFILE token names a VFS logical path of the sub-asset blob to open.
            // spec: Docs/RE/formats/area_inventory.md §1A.4 — "the parser opens + decodes that blob
            //   via the VFS open router as it scans the section." CODE-CONFIRMED.
            if (!source.TryGetCellFileByName(section.DataFile, out var subBytes))
                continue; // Missing sub-asset blob → treat as absent slot (not an error).

            // Dispatch by section keyword → slot assignment.
            // spec: Docs/RE/specs/assembly_graph.md §1 — slot mapping table:
            //   slot 0 ← .ted; slot 1 ← .bud; slots 2..8 ← .fx1..fx7; .sod → collision.
            try
            {
                switch (key)
                {
                    case "TERRAIN":
                        // Slot 0: ground texture-patch grid (.ted TextureIndexGrid).
                        // spec: Docs/RE/structs/terrain-manager.md slot 0 — ".ted TextureIndexGrid": CONFIRMED.
                        slot0Ted = TedTerrainParser.Parse(subBytes);
                        break;

                    case "BUILDING":
                        // Slot 1: building / object placement grid (.bud).
                        // spec: Docs/RE/structs/terrain-manager.md slot 1 — "mass/building-object placement grid (.bud)": CONFIRMED.
                        // Missing .bud is NOT an error.
                        // spec: Docs/RE/formats/area_inventory.md §3.2 — absent .bud: treat as terrain-only.
                        slot1Bud = TerrainSceneParser.Parse(subBytes);
                        break;

                    case "SOLID":
                        // Collision (.sod) — NOT one of the 9 render slots; held separately.
                        // spec: Docs/RE/structs/terrain-manager.md §"What is NOT in the 9-slot array" —
                        //   "Collision is separate: 256-element ray-segment array at cell +516."
                        collision = SodBlobParser.Parse(subBytes);
                        break;

                    case "FX1":
                        // Slot 2: fx1 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 2 — "FX overlay texture layer 1 (fx1)": CONFIRMED.
                        slot2Fx1 = TerrainLayerParsers.ParseFx1(subBytes);
                        break;

                    case "FX2":
                        // Slot 3: fx2 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 3 — "FX overlay texture layer 2 (fx2)": CONFIRMED.
                        slot3Fx2 = TerrainLayerParsers.ParseFx2(subBytes);
                        break;

                    case "FX3":
                        // Slot 4: fx3 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 4 — "FX overlay texture layer 3 (fx3)": CONFIRMED.
                        slot4Fx3 = TerrainLayerParsers.ParseFx3(subBytes);
                        break;

                    case "FX4":
                        // Slot 5: fx4 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 5 — "FX overlay texture layer 4 (fx4)": CONFIRMED.
                        slot5Fx4 = TerrainLayerParsers.ParseFx4(subBytes);
                        break;

                    case "FX5":
                        // Slot 6: fx5 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 6 — "FX overlay texture layer 5 (fx5)": CONFIRMED.
                        slot6Fx5 = TerrainLayerParsers.ParseFx5(subBytes);
                        break;

                    case "FX6":
                        // Slot 7: fx6 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 7 — "FX overlay texture layer 6 (fx6)": CONFIRMED.
                        slot7Fx6 = TerrainLayerParsers.ParseFx6(subBytes);
                        break;

                    case "FX7":
                        // Slot 8: fx7 overlay.
                        // spec: Docs/RE/structs/terrain-manager.md slot 8 — "FX overlay texture layer 7 (fx7)": CONFIRMED.
                        slot8Fx7 = TerrainLayerParsers.ParseFx7(subBytes);
                        break;

                    // "UP_TERRAIN" and "EXTRA_TERRAIN" sections carry .up / .exd blobs — not in the 9-slot array
                    // but valid DATAFILE consumers; the AreaComposer ignores them here (they are outside
                    // the AssembledCell model scope for this wave).
                    // spec: Docs/RE/structs/terrain-manager.md §"Trailing companion grids (NOT part of the 9)".
                }
            }
            catch (InvalidDataException)
            {
                // Corrupted sub-asset blob — treat as absent for that slot (not a fatal error).
                // The composer is deterministic: a bad blob leaves the corresponding slot null.
            }
        }

        // ── Step 5: resolve per-patch texture paths ───────────────────────────
        // .ted TextureIndexGrid byte → idx-1 finalize (−1 ONLY on the .ted byte; clamp [1,count])
        // → .map TERRAIN TEXTURES[idx-1].intTexId → bgtexture.lst[intTexId] (NO −1) → path.
        // spec: Docs/RE/specs/assembly_graph.md §1 — "Texture resolution (global under map000)": CONFIRMED.
        // spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — idx-1 on .ted byte ONLY: CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — intTexId IS 0-based pool slot, NO −1: CONFIRMED.
        string?[]? resolvedPaths = null;
        if (slot0Ted is not null && terrainTextures is not null && terrainTextures.Length > 0)
            resolvedPaths = ResolveTexturePaths(
                slot0Ted.TextureIndexGrid,
                terrainTextures,
                source.TerrainTextureCatalog);

        return new AssembledCell
        {
            MapX = mapX,
            MapZ = mapZ,
            Slot0GroundTexGrid = slot0Ted,
            Slot1BuildingObjectGrid = slot1Bud,
            Slot2Fx1 = slot2Fx1,
            Slot3Fx2 = slot3Fx2,
            Slot4Fx3 = slot4Fx3,
            Slot5Fx4 = slot5Fx4,
            Slot6Fx5 = slot6Fx5,
            Slot7Fx6 = slot7Fx6,
            Slot8Fx7 = slot8Fx7,
            Collision = collision,
            SoundGrid = soundGrid,
            ResolvedTexturePaths = resolvedPaths
        };
    }

    // ── Pool acquire / recycle ────────────────────────────────────────────────

    /// <summary>
    ///     Acquires a free pool slot for the given (mapX, mapZ, areaId) triple.
    ///     The allocator uses the first pool slot whose <c>loaded</c> flag is false (recycle-first-unloaded).
    ///     If no free slot is available, returns -1.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view —
    ///     "Cells are allocated, key-matched, loaded, recycled and freed there.
    ///     The slot allocator picks the first cell whose loaded_flag is clear." CONFIRMED.
    /// </remarks>
    public int AcquirePoolSlot(int mapX, int mapZ, int areaId)
    {
        // First check if already loaded in the pool (cache hit).
        for (var i = 0; i < PoolSize; i++)
            if (_pool[i].Loaded
                && _pool[i].MapX == mapX
                && _pool[i].MapZ == mapZ
                && _pool[i].AreaId == areaId)
                return i; // Cache hit — cell already resident.

        // Cache miss: acquire the first free (unloaded) slot.
        // spec: Docs/RE/structs/terrain-manager.md — recycle predicate: "first cell whose loaded_flag is clear."
        for (var i = 0; i < PoolSize; i++)
            if (!_pool[i].Loaded)
            {
                _pool[i].MapX = mapX;
                _pool[i].MapZ = mapZ;
                _pool[i].AreaId = areaId;
                return i;
            }

        return -1; // Pool full — caller must evict or wait.
    }

    /// <summary>
    ///     Marks a pool slot as unloaded (recycles it for reuse).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view —
    ///     "loaded_flag cleared by the cull." CONFIRMED.
    /// </remarks>
    public void RecyclePoolSlot(int poolIndex)
    {
        if ((uint)poolIndex >= PoolSize)
            throw new ArgumentOutOfRangeException(nameof(poolIndex),
                $"Pool index {poolIndex} is out of range [0, {PoolSize - 1}]. " +
                "spec: Docs/RE/structs/terrain-manager.md — pool_slots[34]: CONFIRMED.");

        _pool[poolIndex].Loaded = false;
        _pool[poolIndex].Cell = null;
    }

    /// <summary>
    ///     Loads a cell into a pool slot: composes it, marks it loaded, and returns the pool index.
    ///     Returns -1 if no free pool slot is available.
    /// </summary>
    public int LoadCellIntoPool(IAreaAssemblySource source, int mapX, int mapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        var slot = AcquirePoolSlot(mapX, mapZ, source.AreaId);
        if (slot < 0)
            return -1;

        // If not already loaded by the cache hit, compose and store.
        if (!_pool[slot].Loaded)
        {
            _pool[slot].Cell = ComposeCell(source, mapX, mapZ);
            _pool[slot].Loaded = true;
        }

        return slot;
    }

    // ── Ring / RecenterRing ───────────────────────────────────────────────────

    /// <summary>
    ///     Re-centres the 5×5 ring around the new (centerMapX, centerMapZ) player cell, loading any
    ///     newly-exposed edge cells on demand and recycling those that fall out of the ring.
    /// </summary>
    /// <param name="source">The assembly source to load newly-exposed cells from.</param>
    /// <param name="centerMapX">New centre X grid index.</param>
    /// <param name="centerMapZ">New centre Z grid index.</param>
    /// <remarks>
    ///     spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view —
    ///     "The 25-slot manager ring is a borrowed-pointer 5×5 spatial VIEW. A single-step recenter
    ///     shifts the whole 5×5 window by one cell; only the newly-exposed edge needs new cells loaded."
    ///     CONFIRMED.
    ///     Ring slot formula: slot = 5·row + col.
    ///     spec: Docs/RE/structs/terrain-manager.md — "ring stored row-major (slot = 5·row+col)": CONFIRMED.
    /// </remarks>
    public void RecenterRing(IAreaAssemblySource source, int centerMapX, int centerMapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Build the new 5×5 ring of (mapX, mapZ) pairs centred on (centerMapX, centerMapZ).
        // The ring half-extent: RingEdge/2 = 2 cells to each side.
        // spec: Docs/RE/structs/terrain-manager.md — "5×5 ring around centre cell": CONFIRMED.
        var halfEdge = RingEdge / 2; // = 2 (integer division)

        var newRingCoords = new (int MapX, int MapZ)[RingSize];
        for (var row = 0; row < RingEdge; row++)
        for (var col = 0; col < RingEdge; col++)
        {
            var slot = RingEdge * row + col; // spec: terrain-manager.md — slot = 5·row+col: CONFIRMED
            newRingCoords[slot] = (centerMapX - halfEdge + col, centerMapZ - halfEdge + row);
        }

        // Determine which old ring slots are no longer needed; recycle their pool entries
        // if they are not in the new ring.
        if (_ringInitialized)
            for (var i = 0; i < RingSize; i++)
            {
                var oldPoolIdx = _ring[i];
                if (oldPoolIdx < 0)
                    continue;

                var oldEntry = _pool[oldPoolIdx];
                var stillNeeded = false;
                for (var j = 0; j < RingSize; j++)
                    if (newRingCoords[j].MapX == oldEntry.MapX
                        && newRingCoords[j].MapZ == oldEntry.MapZ)
                    {
                        stillNeeded = true;
                        break;
                    }

                if (!stillNeeded)
                    RecyclePoolSlot(oldPoolIdx);
            }

        // Point each ring slot to the corresponding pool entry, loading as needed.
        for (var i = 0; i < RingSize; i++)
        {
            var (mx, mz) = newRingCoords[i];

            // Only load cells that are in the area's membership set.
            // spec: Docs/RE/formats/area_inventory.md §1A.1 — "a cell may only be loaded if its
            //   key is in [the membership] set." CODE-CONFIRMED.
            var isMember = false;
            foreach (var (kx, kz) in source.AreaCellKeys)
                if (kx == mx && kz == mz)
                {
                    isMember = true;
                    break;
                }

            if (!isMember)
            {
                _ring[i] = -1;
                continue;
            }

            var poolIdx = LoadCellIntoPool(source, mx, mz);
            _ring[i] = poolIdx; // -1 if pool is full; ring slot stays empty.
        }

        _centerMapX = centerMapX;
        _centerMapZ = centerMapZ;
        _ringInitialized = true;
    }

    // ── Ring inspection ───────────────────────────────────────────────────────

    /// <summary>
    ///     Returns the pool index pointed to by ring slot <paramref name="ringSlot" />, or -1 when
    ///     the ring slot is empty.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/structs/terrain-manager.md — ring holds borrowed pointers into the pool: CONFIRMED.
    /// </remarks>
    public int GetRingPoolIndex(int ringSlot)
    {
        if ((uint)ringSlot >= RingSize)
            throw new ArgumentOutOfRangeException(nameof(ringSlot),
                $"Ring slot {ringSlot} is out of range [0, {RingSize - 1}]. " +
                "spec: Docs/RE/structs/terrain-manager.md — ring_slots[25]: CONFIRMED.");
        return _ring[ringSlot];
    }

    /// <summary>
    ///     Returns the assembled cell currently pointed to by ring slot <paramref name="ringSlot" />,
    ///     or <see langword="null" /> when the slot is empty.
    /// </summary>
    public AssembledCell? GetRingCell(int ringSlot)
    {
        var poolIdx = GetRingPoolIndex(ringSlot);
        return poolIdx >= 0 ? _pool[poolIdx].Cell : null;
    }

    // ── Texture resolution ────────────────────────────────────────────────────

    /// <summary>
    ///     Resolves the 256-entry per-patch texture path array from the <c>.ted</c> TextureIndexGrid and
    ///     the TERRAIN section's <c>TEXTURES</c> list.
    /// </summary>
    /// <param name="textureIndexGrid">
    ///     The 256-byte (16×16) raw TextureIndexGrid from the <c>.ted</c> file.
    ///     Each byte is the 1-based index into the per-cell TEXTURES list BEFORE the −1 finalize.
    ///     spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — "idx-1 decrement is the FINALIZE consumer's job":
    ///     CONFIRMED. The parser stores the raw byte; we apply the −1 here.
    /// </param>
    /// <param name="terrainTextures">
    ///     The <c>TEXTURES</c> entries from the TERRAIN section of the <c>.map</c> file.
    ///     Each entry is <c>(intFlag, intTexId)</c>; <c>intTexId</c> is used DIRECTLY as the
    ///     0-based <c>bgtexture.lst</c> pool slot (NO −1).
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — "intTexId IS the 0-based record index,
    ///     used DIRECTLY — NO -1": CONFIRMED.
    /// </param>
    /// <param name="catalog">
    ///     The global background-texture catalogue (built from <c>data/map000/texture/bgtexture.lst</c>).
    ///     Textures are global under <c>map000</c> for ALL areas.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — "textures are global under map000 for every area": CONFIRMED.
    /// </param>
    /// <returns>
    ///     A 256-element array of VFS paths (<c>data/map000/texture/&lt;rel&gt;.dds</c>), or
    ///     <see langword="null" /> entries for empty / out-of-range slots.
    /// </returns>
    private static string?[] ResolveTexturePaths(
        byte[] textureIndexGrid,
        (int Flag, int TexId)[] terrainTextures,
        BgtextureLstCatalog catalog)
    {
        // Build a BgTextureCatalog wrapper from the parsed catalog for path resolution.
        var bgCatalog = BgTextureCatalog.FromLst(catalog);

        var count = terrainTextures.Length;
        var paths = new string?[textureIndexGrid.Length];

        for (var i = 0; i < textureIndexGrid.Length; i++)
        {
            var rawByte = textureIndexGrid[i];

            // Apply the idx-1 finalize: clamp to [1, count], then subtract 1 to get
            // a 0-based index into the per-cell TEXTURES list.
            // Spec rule: BOTH < 1 AND > count → floor to 1 (NOT clamp-to-count; no sentinel).
            // spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 —
            //   "clamped to [1, count], then indexed as perCellTexList[byte - 1]": CONFIRMED.
            // spec: Docs/RE/formats/terrain.md §5.6 —
            //   "a byte < 1 or > count floors to slot 1" (both out-of-range → 1): CONFIRMED.
            // spec: Docs/RE/formats/terrain.md §5.9 —
            //   "a byte < 1 or > count floors to slot 1; NOT a no-texture sentinel": CONFIRMED.
            // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
            //   "both <1 and >count → 1": CONFIRMED.
            if (rawByte < 1 || rawByte > count)
                rawByte = 1; // spec: terrain.md §5.6/§5.9 — BOTH under AND over floor to 1: CONFIRMED

            // 0-based list index after −1 finalize.
            var listIdx = rawByte - 1; // spec: terrain.md §CORRECTED CYCLE 1 — idx-1 finalize: CONFIRMED

            // Bounds guard: if the per-cell TEXTURES list is empty or listIdx is out of range,
            // leave the slot null.
            if (listIdx < 0 || listIdx >= terrainTextures.Length)
            {
                paths[i] = null;
                continue;
            }

            // intTexId from the TEXTURES entry IS the 0-based bgtexture.lst pool slot; NO further −1.
            // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
            //   "intTexId IS the 0-based record index, used DIRECTLY — NO -1": CONFIRMED.
            var intTexId = terrainTextures[listIdx].TexId;

            // Resolve full VFS path: data/map000/texture/<rel>.dds
            // spec: Docs/RE/specs/assembly_graph.md §1 — "textures are global under map000": CONFIRMED.
            paths[i] = bgCatalog.ResolveTexturePath(intTexId);
        }

        return paths;
    }

    // ── Spawn building ────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds the neutral NPC spawn descriptor list from <c>npc&lt;NNN&gt;.arr</c>.
    ///     Ground Y is NOT baked — re-sample per frame.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/area_inventory.md §1A.3 — npc&lt;NNN&gt;.arr is per-area binary #4,
    ///     opened before any cell streams. CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/npc_spawns.md §Runtime role — "Ground-Y is re-sampled from terrain
    ///     every frame; not once at spawn." CONFIRMED.
    ///     spec: Docs/RE/formats/npc_spawns.md §Record layout facing — "runtime applies π/2 − value":
    ///     sample-verified. Applied yaw = π/2 − rawFacing.
    ///     NPC .arr: 28-byte records, record_count = floor(file_size / 28).
    ///     spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
    ///     mob&lt;NNN&gt;.arr is a content-census signal only — there is NO runtime mob.arr loader
    ///     in the shipped client. Live mob actors arrive via the server area-entity snapshot (packet 4/4).
    ///     spec: Docs/RE/formats/area_inventory.md §5.1 — "no runtime mob.arr loader in the client."
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats with NO client loader — mob.arr: CONFIRMED.
    /// </remarks>
    private static IReadOnlyList<SpawnDescriptor> BuildSpawns(IAreaAssemblySource source)
    {
        var spawns = new List<SpawnDescriptor>();
        var areaId = source.AreaId;

        // NPC spawns from npc<NNN>.arr — 28-byte records.
        // spec: Docs/RE/formats/area_inventory.md §1A.3 — npc<NNN>.arr opened as per-area binary #4. CODE-CONFIRMED.
        var npcArrPath = $"data/map{areaId:D3}/npc{areaId:D3}.arr";
        if (source.TryGetCellFileByName(npcArrPath, out var npcBytes))
        {
            var npcArr = NpcSpawnParser.Parse(npcBytes);
            foreach (var rec in npcArr.Records)
            {
                // Yaw = π/2 − rawFacing.
                // spec: Docs/RE/formats/npc_spawns.md §Record layout facing —
                //   "runtime applies π/2 − value (NOT +π/2)": sample-verified.
                var yaw = MathF.PI / 2f - rec.Facing; // spec: npc_spawns.md §Record layout facing: sample-verified

                spawns.Add(new SpawnDescriptor
                {
                    WorldX = rec.WorldX,
                    WorldZ = rec.WorldZ,
                    Yaw = yaw,
                    VisualId = rec.MobId,
                    IsNpc = true
                });
            }
        }
        // Missing npc.arr → zero NPC spawns; not an error (areas 11 and 14 have no npc.arr).
        // spec: Docs/RE/formats/area_inventory.md §5.2 — missing npc.arr is not an error.

        return spawns;
    }

    // ── Pool entry ────────────────────────────────────────────────────────────

    private sealed class PoolEntry
    {
        public int MapX { get; set; }
        public int MapZ { get; set; }
        public int AreaId { get; set; }

        /// <summary>
        ///     Whether this pool slot is currently occupied (loaded).
        ///     spec: Docs/RE/structs/terrain-manager.md — loaded_flag @ cell +24708: CONFIRMED.
        ///     Recycle predicate: allocator picks the first slot where loaded == false.
        /// </summary>
        public bool Loaded { get; set; }

        public AssembledCell? Cell { get; set; }
    }
}