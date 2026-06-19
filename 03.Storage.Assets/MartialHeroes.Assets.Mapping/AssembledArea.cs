using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

// spec: Docs/RE/specs/assembly_graph.md §1 (World-boot chain, per area, then per cell)
// spec: Docs/RE/structs/terrain-manager.md (9 per-cell sub-manager slots, 34-slot pool, 25-slot ring)
// spec: Docs/RE/formats/area_inventory.md §1A (area→cell fan-out, per-cell open order)
// spec: Docs/RE/formats/npc_spawns.md (spawn records, Yaw = π/2 − rawFacing)

/// <summary>
/// Neutral descriptor for one streamed cell in its fully assembled state.
/// Holds the parsed sub-manager slot data exactly as the 9-slot terrain-manager model defines,
/// using existing Parsers model types throughout — NO rendering dependency.
/// </summary>
/// <remarks>
/// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" —
///   slot 0 = ground texture-patch grid (.ted); slot 1 = mass/building object grid (.bud);
///   slots 2..8 = fx1..fx7 overlays (.fx1-.fx7). CONFIRMED.
/// spec: Docs/RE/specs/assembly_graph.md §1 — per-cell sub-assets from .map DATAFILE tokens.
/// spec: Docs/RE/structs/terrain-manager.md §"Key formula" —
///   cell_key = mapZ + 100000 · mapX. CONFIRMED.
/// </remarks>
public sealed class AssembledCell
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// X grid index of this cell.
    /// spec: Docs/RE/structs/terrain-manager.md §Per-cell object — mapX @ +16252: CONFIRMED.
    /// </summary>
    public required int MapX { get; init; }

    /// <summary>
    /// Z grid index of this cell.
    /// spec: Docs/RE/structs/terrain-manager.md §Per-cell object — mapZ @ +16256: CONFIRMED.
    /// </summary>
    public required int MapZ { get; init; }

    /// <summary>
    /// Packed cell key: <c>mapZ + 100000 · mapX</c>.
    /// spec: Docs/RE/structs/terrain-manager.md §"Key formula" —
    ///   "cell_key = mapZ + 100000·mapX. CONFIRMED."
    /// spec: Docs/RE/formats/area_inventory.md §1A.2 — "cell_key = mapZ + 100000 * mapX. CODE-CONFIRMED."
    /// </summary>
    public long CellKey => (long)MapZ + 100000L * MapX; // spec: terrain-manager.md §Key formula

    // ── 9 sub-manager slots ────────────────────────────────────────────────
    // Each slot corresponds exactly to the per-cell sub-manager pointer array at cell +16296..+16328.
    // spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" — CONFIRMED.

    /// <summary>
    /// Slot 0 — Terrain BASE ground texture-patch grid, populated from the <c>.ted</c> blob.
    /// null when the <c>.ted</c> sub-asset is absent or not yet parsed.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 0 — CONFIRMED.
    /// </summary>
    public TerrainCell? Slot0GroundTexGrid { get; init; }

    /// <summary>
    /// Slot 1 — Mass/building-object placement grid, populated from the <c>.bud</c> blob.
    /// null when the <c>.bud</c> is absent (terrain-only cells have no .bud — NOT an error).
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 1 — CONFIRMED.
    /// spec: Docs/RE/formats/area_inventory.md §3.2 — missing .bud is not an error.
    /// </summary>
    public BudScene? Slot1BuildingObjectGrid { get; init; }

    /// <summary>
    /// Slot 2 — FX overlay texture layer 1 (<c>.fx1</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 2 (fx1) — CONFIRMED.
    /// </summary>
    public Fx1Layer? Slot2Fx1 { get; init; }

    /// <summary>
    /// Slot 3 — FX overlay texture layer 2 (<c>.fx2</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 3 (fx2) — CONFIRMED.
    /// </summary>
    public Fx2Layer? Slot3Fx2 { get; init; }

    /// <summary>
    /// Slot 4 — FX overlay texture layer 3 (<c>.fx3</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 4 (fx3) — CONFIRMED.
    /// </summary>
    public Fx3Layer? Slot4Fx3 { get; init; }

    /// <summary>
    /// Slot 5 — FX overlay texture layer 4 (<c>.fx4</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 5 (fx4) — CONFIRMED.
    /// </summary>
    public Fx4Layer? Slot5Fx4 { get; init; }

    /// <summary>
    /// Slot 6 — FX overlay texture layer 5 (<c>.fx5</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 6 (fx5) — CONFIRMED.
    /// </summary>
    public Fx5Layer? Slot6Fx5 { get; init; }

    /// <summary>
    /// Slot 7 — FX overlay texture layer 6 (<c>.fx6</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 7 (fx6) — CONFIRMED.
    /// </summary>
    public Fx6Layer? Slot7Fx6 { get; init; }

    /// <summary>
    /// Slot 8 — FX overlay texture layer 7 (<c>.fx7</c>), or null when absent.
    /// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" slot 8 (fx7) — CONFIRMED.
    /// </summary>
    public Fx7Layer? Slot8Fx7 { get; init; }

    // ── Resolved texture paths (ground patches) ────────────────────────────

    /// <summary>
    /// For each of the 256 ground patches (16×16 grid, row-major), the resolved
    /// <c>data/map000/texture/&lt;rel&gt;.dds</c> VFS path — or <see langword="null"/>
    /// for an empty/kind-0 pool slot or an out-of-range index.
    /// <para>
    /// Resolution chain:
    /// .ted TextureIndexGrid byte → idx-1 finalize (−1 on .ted byte only; clamp [1,count])
    /// → .map TERRAIN TEXTURES[idx-1].intTexId → bgtexture.lst[intTexId] (NO −1; intTexId is
    /// the 0-based pool slot) → data/map000/texture/&lt;rel&gt;.dds.
    /// </para>
    /// Length = 256 (16×16).
    /// spec: Docs/RE/specs/assembly_graph.md §1 — texture resolution (global under map000): CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — idx-1 on .ted byte only: CONFIRMED.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — intTexId IS the 0-based pool slot, NO −1: CONFIRMED.
    /// </summary>
    public string?[]? ResolvedTexturePaths { get; init; }

    // ── Collision geometry ─────────────────────────────────────────────────

    /// <summary>
    /// Decoded collision solid blob for this cell, or <see langword="null"/> when <c>.sod</c>
    /// is absent (5 areas have one cell without .sod — not an error).
    /// spec: Docs/RE/formats/area_inventory.md §3.1 — missing .sod is not an error.
    /// spec: Docs/RE/structs/terrain-manager.md §"What is NOT in the 9-slot array" —
    ///   "Collision is separate. The cell's 2D XZ wall-segment / ray-parity collision geometry
    ///    is the 256-element ray-segment array at cell +516."
    /// </summary>
    public SodBlob? Collision { get; init; }

    // ── Sound grid ─────────────────────────────────────────────────────────

    /// <summary>
    /// Decoded ambient-sound zone grid for this cell, or <see langword="null"/> when
    /// <c>.mud</c> is absent (20 areas carry zero .mud files — not an error, default to silence).
    /// spec: Docs/RE/formats/area_inventory.md §3.4 — absent .mud means silence.
    /// spec: Docs/RE/structs/terrain-manager.md §"What is NOT in the 9-slot array" —
    ///   "Sound-grid is not a member of the 9-slot array."
    /// </summary>
    public MudSoundGrid? SoundGrid { get; init; }

    // ── Height lookup helper ───────────────────────────────────────────────

    /// <summary>
    /// Returns the bilinear-interpolated ground height at the given local (cell-relative) XZ
    /// position, sampling from the <c>.ted</c> heightmap in <see cref="Slot0GroundTexGrid"/>.
    /// Returns <see langword="null"/> when the cell has no <c>.ted</c> data loaded.
    /// </summary>
    /// <param name="localX">X position relative to the cell's south-west corner (0..1023).</param>
    /// <param name="localZ">Z position relative to the cell's south-west corner (0..1023).</param>
    /// <remarks>
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "ground Y is re-sampled from the terrain each frame".
    /// Vertex grid: 65×65, spacing 16 world units.
    /// spec: Docs/RE/formats/terrain.md §5.1 — "65 × 65 vertices, spacing 16 units": CONFIRMED.
    /// </remarks>
    public float? SampleHeight(float localX, float localZ)
    {
        if (Slot0GroundTexGrid is null)
            return null;

        // Vertex grid: 65 × 65, spacing 16 world units per edge.
        // spec: Docs/RE/formats/terrain.md §5.1 — "65 × 65 vertices, spacing 16 units": CONFIRMED.
        const float spacing = 16f; // spec: terrain.md §5.1 — vertex spacing 16 world units: CONFIRMED

        // Clamp to cell boundaries.
        float cx = Math.Clamp(localX, 0f, 64f * spacing);
        float cz = Math.Clamp(localZ, 0f, 64f * spacing);

        float gx = cx / spacing;
        float gz = cz / spacing;

        int ix = (int)gx;
        int iz = (int)gz;

        // Clamp grid indices to [0, 63] so the +1 steps stay in [0, 64].
        ix = Math.Clamp(ix, 0, 63);
        iz = Math.Clamp(iz, 0, 63);

        float fx = gx - ix;
        float fz = gz - iz;

        float[] h = Slot0GroundTexGrid.Heights;
        // Row-major: index = row * 65 + col, where row = Z axis, col = X axis.
        // spec: Docs/RE/formats/terrain.md §5.2 Block 1 — "Heightmap: row-major Z=row, X=col."
        float h00 = h[iz * TerrainCell.GridSize + ix];
        float h10 = h[iz * TerrainCell.GridSize + (ix + 1)];
        float h01 = h[(iz + 1) * TerrainCell.GridSize + ix];
        float h11 = h[(iz + 1) * TerrainCell.GridSize + (ix + 1)];

        // Bilinear interpolation.
        return h00 * (1f - fx) * (1f - fz)
             + h10 * fx * (1f - fz)
             + h01 * (1f - fx) * fz
             + h11 * fx * fz;
    }
}

/// <summary>
/// Neutral descriptor for one fully assembled area — the product of loading <c>d&lt;NNN&gt;.lst</c>,
/// the 4 per-area binaries, the spawns, and the per-cell assemblies.
/// </summary>
/// <remarks>
/// spec: Docs/RE/specs/assembly_graph.md §1 — "Phase A — area load (the area orchestrator)":
///   area id → d&lt;NNN&gt;.lst cell-key set → area binaries (map.bin, regiontable.bin, region.bin,
///   npc.arr) → sound tables → sky/environment. CONFIRMED.
/// spec: Docs/RE/formats/area_inventory.md §1A — area → cell fan-out. CODE-CONFIRMED.
/// </remarks>
public sealed class AssembledArea
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Area identifier (0..300 range, 60 registered areas).
    /// spec: Docs/RE/formats/area_inventory.md §1.1 — area id space: CONFIRMED.
    /// </summary>
    public required int AreaId { get; init; }

    // ── Cell membership ───────────────────────────────────────────────────

    /// <summary>
    /// The full (mapX, mapZ) membership set loaded from <c>d&lt;NNN&gt;.lst</c>.
    /// A cell may only be streamed if its key (<c>mapZ + 100000·mapX</c>) is in this set.
    /// spec: Docs/RE/formats/area_inventory.md §1A.1 — cell-key membership set: CODE-CONFIRMED.
    /// spec: Docs/RE/formats/area_inventory.md §1A.2 — "cell_key = mapZ + 100000 * mapX": CODE-CONFIRMED.
    /// </summary>
    public required IReadOnlyList<(int MapX, int MapZ)> CellKeys { get; init; }

    // ── Area-level scalars ─────────────────────────────────────────────────

    /// <summary>
    /// Map-option / region value for this area (area-level scalar on TerrainManager +464).
    /// null when not loaded.
    /// spec: Docs/RE/structs/terrain-manager.md §TerrainManager — map_option_value @ +464: CONFIRMED.
    /// </summary>
    public int? MapOptionValue { get; init; }

    /// <summary>
    /// Region value for this area (area-level scalar on TerrainManager +468).
    /// null when not loaded.
    /// spec: Docs/RE/structs/terrain-manager.md §TerrainManager — region_value @ +468: CONFIRMED.
    /// </summary>
    public int? RegionValue { get; init; }

    // ── Spawn descriptors (offline path) ─────────────────────────────────

    /// <summary>
    /// Neutral spawn descriptors built from <c>npc&lt;NNN&gt;.arr</c> and, for the offline port,
    /// from <c>mob&lt;NNN&gt;.arr</c> as well. Ground Y is NOT baked — re-sample per frame via
    /// <see cref="AssembledCell.SampleHeight"/>.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "Port-side note: offline port synthesises actors
    ///   from .arr + the visual catalogue."
    /// spec: Docs/RE/formats/npc_spawns.md §Runtime role — "Ground-Y is re-sampled from terrain
    ///   every frame; not once at spawn." CONFIRMED.
    /// </summary>
    public required IReadOnlyList<SpawnDescriptor> Spawns { get; init; }

    // ── Per-cell assemblies ───────────────────────────────────────────────

    /// <summary>
    /// Assembled cells, keyed by <c>(MapX, MapZ)</c>.
    /// Only cells that were explicitly composed are present here; the streaming ring/pool manages
    /// the live set at runtime.
    /// spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view — CONFIRMED.
    /// </summary>
    public required IReadOnlyDictionary<(int MapX, int MapZ), AssembledCell> Cells { get; init; }
}

/// <summary>
/// Neutral offline-port spawn descriptor — position and facing, NO baked Y.
/// Built from <c>npc&lt;NNN&gt;.arr</c> (NPC kind, 28-byte records) or synthesised from
/// <c>mob&lt;NNN&gt;.arr</c> for the offline port.
/// </summary>
/// <remarks>
/// spec: Docs/RE/specs/assembly_graph.md §1 — spawns (server-driven; offline-port substitution):
///   "the offline port synthesises actors from .arr + the visual catalogue."
/// spec: Docs/RE/formats/npc_spawns.md §Runtime role — "Ground-Y is re-sampled from terrain
///   every frame": CONFIRMED. Y is NOT stored in this descriptor.
/// spec: Docs/RE/formats/npc_spawns.md §Record layout — facing: "runtime applies π/2 − value":
///   sample-verified. This descriptor stores the ALREADY-APPLIED Yaw (= π/2 − rawFacing).
/// </remarks>
public readonly record struct SpawnDescriptor
{
    /// <summary>
    /// World-space X coordinate (horizontal plane).
    /// spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4: CONFIRMED.
    /// </summary>
    public float WorldX { get; init; }

    /// <summary>
    /// World-space Z coordinate (horizontal plane).
    /// Y is absent — it is re-sampled from terrain per frame.
    /// spec: Docs/RE/formats/npc_spawns.md — world_z f32 @ +8: CONFIRMED.
    /// spec: Docs/RE/formats/npc_spawns.md §Runtime role — "Ground-Y is re-sampled from terrain every frame."
    /// </summary>
    public float WorldZ { get; init; }

    /// <summary>
    /// On-screen facing angle in radians, after the runtime transform <c>π/2 − rawFacing</c>.
    /// spec: Docs/RE/formats/npc_spawns.md §Record layout facing — "runtime applies π/2 − value":
    ///   sample-verified. The raw on-disk value is subtracted from a quarter-turn (NOT added).
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    /// Visual id: the <c>mob_id</c> / NPC template identifier used to look up the actor's
    /// visual chain (skin / skeleton / motion) through the boot-loaded character-table catalogue.
    /// spec: Docs/RE/formats/npc_spawns.md — mob_id u16 @ +0: CONFIRMED (primary key for template lookup).
    /// </summary>
    public int VisualId { get; init; }

    /// <summary>
    /// True when this descriptor was synthesised from <c>npc&lt;NNN&gt;.arr</c> (NPC kind);
    /// false when synthesised from <c>mob&lt;NNN&gt;.arr</c> (mob kind, offline port only).
    /// spec: Docs/RE/specs/assembly_graph.md §1 port-side note — offline substitution: mob or npc .arr.
    /// </summary>
    public bool IsNpc { get; init; }
}
