// World/RealWorldRenderer.Config.cs
//
// Config readers, target cell discovery, spawn-anchor computation, ring-center selection.
// Part of the RealWorldRenderer partial class split.

using Godot;
using MartialHeroes.Assets.Parsers.World;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    // -------------------------------------------------------------------------
    // Target cell discovery
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Resolves <see cref="TargetAreaId" />, <see cref="TargetMapX" /> and <see cref="TargetMapZ" />
    ///     by enumerating real VFS entries instead of using a hard-coded coordinate.
    ///     Resolution order:
    ///     1. Read "area=" key from client_dir.cfg (defaults to 0).
    ///     2. Enumerate .ted entries in the VFS for that area via
    ///     <see cref="RealClientAssets.EnumerateTerrainCells" />.
    ///     3. If at least one cell is found for the requested area, read the spawn files (mob/npc
    ///     .arr) to compute a spawn-weighted anchor cell, then call <see cref="PickRingCenter" />
    ///     with that anchor so the ring centers on where the game content actually is.
    ///     Falls back to terrain centroid when no spawn data is available.
    ///     4. If the requested area has NO cells, try areas 0..20 in order and pick the first
    ///     area+cell pair that exists.
    ///     5. If no cells are found in any area, fall back to the configured defaults and log a
    ///     warning — streaming will silently produce empty sectors but won't crash.
    ///     spec: Docs/RE/formats/terrain.md §1.3 — per-cell path pattern. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition. CONFIRMED.
    ///     spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8: CONFIRMED.
    /// </summary>
    private void ResolveTargetCell()
    {
        if (Assets is null) return;

        // Read area= from config. Default 0. Silently ignore missing key.
        var configArea = ReadAreaFromConfig();
        GD.Print($"[RealWorldRenderer] Config area={configArea} (from client_dir.cfg or default).");

        // Read ring_radius= from config. Default 2 (5×5 ring, High quality).
        // spec: Docs/RE/formats/terrain.md §12.2 — High quality = radius 2 (5×5). CONFIRMED.
        var ringRadius = ReadRingRadiusFromConfig();
        GD.Print($"[RealWorldRenderer] Ring radius={ringRadius} ({2 * ringRadius + 1}×{2 * ringRadius + 1} ring) " +
                 $"(from client_dir.cfg ring_radius= or default 2).");

        // Try to get cells for the configured area first.
        var cells = Assets.EnumerateTerrainCells(configArea);
        if (cells.Count > 0)
        {
            // Compute a spawn-weighted anchor from mob/npc .arr files so the ring centers on
            // game content (the walled town, NPC clusters) rather than the terrain centroid.
            // spec: Docs/RE/formats/terrain.md §1.4 — cell key formula. CONFIRMED.
            // spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8. CONFIRMED.
            // The spawn-anchor density is always computed with the smallest ring (radius=1 / 3×3
            // neighbourhood) to find the tightest content cluster — not the streaming ring radius.
            // A 5×5 neighbourhood would smear the density peak outward and miss tight clusters.
            // The found anchor is then passed to PickRingCenter which selects a streaming center
            // covering the anchor with the actual ringRadius.
            // spec: Docs/RE/formats/terrain.md §12.2 — density anchor at radius 1; stream at radius 2. CONFIRMED.
            const int DensityRadius = 1; // always 3×3 neighbourhood for anchor detection.
            var (anchorX, anchorZ) = ComputeSpawnAnchor(Assets, configArea, cells, DensityRadius);
            var (mx, mz, fullRing) = PickRingCenter(cells, anchorX, anchorZ, ringRadius);
            TargetAreaId = configArea;
            TargetMapX = mx;
            TargetMapZ = mz;
            GD.Print($"[RealWorldRenderer] Area {configArea}: {cells.Count} cells found — " +
                     $"selected ({TargetMapX},{TargetMapZ}) " +
                     $"(anchor=({anchorX:F1},{anchorZ:F1}), " +
                     $"full {2 * ringRadius + 1}×{2 * ringRadius + 1} ring={fullRing}).");
            return;
        }

        GD.Print($"[RealWorldRenderer] Area {configArea} has no .ted cells — scanning areas 0..20.");

        // Auto-select: try areas 0 through 20 and take the first that has cells.
        for (var area = 0; area <= 20; area++)
        {
            if (area == configArea) continue; // already tried
            var areaCells = Assets.EnumerateTerrainCells(area);
            if (areaCells.Count > 0)
            {
                const int DensityRadiusAuto = 1; // same fixed radius for auto-select.
                var (anchorX, anchorZ) = ComputeSpawnAnchor(Assets, area, areaCells, DensityRadiusAuto);
                var (mx, mz, fullRing) = PickRingCenter(areaCells, anchorX, anchorZ, ringRadius);
                TargetAreaId = area;
                TargetMapX = mx;
                TargetMapZ = mz;
                GD.Print($"[RealWorldRenderer] Auto-selected area {area}: {areaCells.Count} cells — " +
                         $"cell ({TargetMapX},{TargetMapZ}) " +
                         $"(full {2 * ringRadius + 1}×{2 * ringRadius + 1} ring={fullRing}).");
                return;
            }
        }

        // No cells found anywhere — keep configured defaults but warn clearly.
        GD.PrintErr($"[RealWorldRenderer] WARNING: no .ted cells found in any area 0..20. " +
                    $"Keeping defaults ({TargetMapX},{TargetMapZ}) — streaming will produce empty sectors.");
    }

    /// <summary>
    ///     Computes a spawn-density anchor by reading the area's <c>mob{tag}.arr</c> and
    ///     <c>npc{tag}.arr</c> files and finding the cell that maximises the number of spawn
    ///     records that fall within a neighbourhood of <paramref name="ringRadius" /> cells
    ///     (matching the streaming ring size).
    ///     Using the density-peak cell (rather than the simple centroid) handles the common case
    ///     where the game content (NPC clusters, the walled town) is concentrated in one corner
    ///     of the area's spawn grid.  The centroid can be pulled toward a sparse but large
    ///     peripheral region and land far from the actual player-visible cluster.
    ///     Falls back to the terrain centroid when no spawn data is available.
    ///     Cell key formula (matches <see cref="TerrainNode.TryGetGroundHeight" />):
    ///     mapX = floor(worldX / 1024) + 10000
    ///     mapZ = floor(worldZ / 1024) + 10000
    ///     spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ring radius 2). CONFIRMED.
    ///     spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8. CONFIRMED.
    ///     spec: MISSION B — mob{tag}.arr world_x f32@4, world_z f32@8. CONFIRMED.
    /// </summary>
    private static (double AnchorMapX, double AnchorMapZ) ComputeSpawnAnchor(
        RealClientAssets assets,
        int areaId,
        List<(int MapX, int MapZ)> terrainCells,
        int ringRadius = 2)
    {
        // Fallback: terrain centroid.
        long sumX = 0, sumZ = 0;
        foreach (var (x, z) in terrainCells)
        {
            sumX += x;
            sumZ += z;
        }

        var fallbackX = sumX / (double)terrainCells.Count;
        var fallbackZ = sumZ / (double)terrainCells.Count;

        if (areaId == 0)
            // Area 0 has no spawn data.
            // spec: Docs/RE/formats/npc_spawns.md §Anomaly: map 000 — 0 records: CONFIRMED.
            return (fallbackX, fallbackZ);

        var tag = AreaTag(areaId);

        // Accumulate per-cell spawn count in a dictionary.
        // Key: (cellMapX, cellMapZ). Value: number of spawn records in that cell.
        // spec: terrain.md §1.4 — cellMapX = floor(worldX/1024)+10000. CONFIRMED.
        var cellCounts = new Dictionary<(int, int), int>(64);

        // ── mob{tag}.arr ──────────────────────────────────────────────────────
        // 20-byte records; world_x f32@4, world_z f32@8.
        // spec: MISSION B — mob record layout; world_x @4, world_z @8. CONFIRMED.
        var mobPath = $"data/map{tag}/mob{tag}.arr";
        if (assets.Contains(mobPath))
            try
            {
                var raw = assets.GetRaw(mobPath);
                var mobRecords = MobSpawnParser.Parse(raw);
                foreach (var rec in mobRecords)
                {
                    var cx = (int)Math.Floor(rec.WorldX / 1024.0) + 10000;
                    var cz = (int)Math.Floor(rec.WorldZ / 1024.0) + 10000;
                    cellCounts.TryGetValue((cx, cz), out var existing);
                    cellCounts[(cx, cz)] = existing + 1;
                }
            }
            catch
            {
                // Parse failure: ignore, use what we have.
            }

        // ── npc{tag}.arr ──────────────────────────────────────────────────────
        // 28-byte records; world_x f32@4, world_z f32@8.
        // spec: Docs/RE/formats/npc_spawns.md — world_x @4, world_z @8. CONFIRMED.
        var npcPath = $"data/map{tag}/npc{tag}.arr";
        if (assets.Contains(npcPath))
            try
            {
                var raw = assets.GetRaw(npcPath);
                var npcArray = NpcSpawnParser.Parse(raw);
                foreach (var rec in npcArray.Records)
                {
                    if (rec.MobId == 0) continue;
                    var cx = (int)Math.Floor(rec.WorldX / 1024.0) + 10000;
                    var cz = (int)Math.Floor(rec.WorldZ / 1024.0) + 10000;
                    cellCounts.TryGetValue((cx, cz), out var existing);
                    cellCounts[(cx, cz)] = existing + 1;
                }
            }
            catch
            {
                // Parse failure: ignore, use what we have.
            }

        if (cellCounts.Count == 0)
            // No usable spawn data — fall back to terrain centroid.
            return (fallbackX, fallbackZ);

        // Find the cell whose (2r+1)×(2r+1) neighbourhood (the streaming ring at ringRadius)
        // covers the most spawns. This is an O(spawnerCells × (2r+1)²) pass — small for typical areas.
        // spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ringRadius=2). CONFIRMED.
        var bestNeighbourCount = -1;
        (int BestCX, int BestCZ) bestDensityCell = (0, 0);
        foreach (var (cx, cz) in cellCounts.Keys)
        {
            var neighbourhood = 0;
            for (var dz = -ringRadius; dz <= ringRadius; dz++)
            for (var dx = -ringRadius; dx <= ringRadius; dx++)
            {
                cellCounts.TryGetValue((cx + dx, cz + dz), out var n);
                neighbourhood += n;
            }

            if (neighbourhood > bestNeighbourCount)
            {
                bestNeighbourCount = neighbourhood;
                bestDensityCell = (cx, cz);
            }
        }

        var totalCount = cellCounts.Values.Sum();
        GD.Print($"[RealWorldRenderer] Spawn density anchor for area {areaId}: " +
                 $"({bestDensityCell.BestCX},{bestDensityCell.BestCZ}) " +
                 $"neighbourhood={bestNeighbourCount}/{totalCount} spawns " +
                 $"(terrain centroid was ({fallbackX:F2},{fallbackZ:F2})).");
        return (bestDensityCell.BestCX, bestDensityCell.BestCZ);
    }

    /// <summary>
    ///     Picks the cell to centre the streaming ring on, given every <c>.ted</c> cell present in an
    ///     area.
    ///     Strategy (two-pass):
    ///     Pass 1 — full-ring preference: find the complete-ring candidate (all neighbours at
    ///     Chebyshev radius <paramref name="ringRadius" /> present) nearest to the anchor.
    ///     A full ring guarantees all (2r+1)² sectors render without holes.
    ///     Pass 2 — fallback to any cell: if no full-ring cell exists, OR if the nearest full-ring
    ///     cell is more than <c>MaxFullRingFallbackDistance</c> cells away from the anchor (meaning
    ///     the NPC/spawn cluster lives outside all complete-ring areas), pick the available cell that
    ///     is simply nearest to the anchor, even if its ring is incomplete.
    ///     The fallback matters when spawn data is dense in a region where the terrain edge cells
    ///     don't have enough neighbours to form a complete ring (e.g. the walled town is near the
    ///     edge of the map grid).  In that case it is better to centre the stream on the actual
    ///     content and accept a few missing border sectors than to centre it on a geometrically-
    ///     perfect but content-empty region far away.
    ///     spec: Docs/RE/formats/terrain.md §12.3 — eviction: absent keys yield empty loads, not crashes.
    ///     The anchor point is the spawn-weighted centroid (from <see cref="ComputeSpawnAnchor" />)
    ///     so the ring centers on where the game content actually is.
    ///     spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ringRadius=2). CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §1.3 (per-cell path). CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu. CONFIRMED.
    /// </summary>
    /// <param name="cells">All cell coordinates available for the area (may be unsorted).</param>
    /// <param name="anchorMapX">Target mapX to stay near (e.g. spawn centroid cell X).</param>
    /// <param name="anchorMapZ">Target mapZ to stay near (e.g. spawn centroid cell Z).</param>
    /// <param name="ringRadius">
    ///     Chebyshev radius of the streaming ring (1 = 3×3, 2 = 5×5).
    ///     spec: Docs/RE/formats/terrain.md §12.2 — High quality → radius 2. CONFIRMED.
    /// </param>
    /// <returns>The chosen centre cell and whether its full ring exists.</returns>
    private static (int MapX, int MapZ, bool FullRing) PickRingCenter(
        List<(int MapX, int MapZ)> cells,
        double anchorMapX,
        double anchorMapZ,
        int ringRadius = 2)
    {
        // When the nearest full-ring cell exceeds this Chebyshev distance from the anchor,
        // we prefer a partial-ring cell that is actually near the content.
        // A value of 2 means: "the full-ring center is more than 2 cells away from the NPC
        // cluster — prefer proximity to content over a perfect ring".
        // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu per cell. CONFIRMED.
        const double MaxFullRingFallbackDistance = 2.0;

        // Deterministic order so a tie resolves the same way every run.
        cells.Sort((a, b) => a.MapX != b.MapX ? a.MapX.CompareTo(b.MapX) : a.MapZ.CompareTo(b.MapZ));

        var present = new HashSet<(int, int)>(cells.Count);
        foreach (var (x, z) in cells) present.Add((x, z));

        // ── Pass 1: nearest full-ring candidate ───────────────────────────────
        var bestFullFound = false;
        var bestFull = cells[cells.Count / 2];
        var bestFullDist = double.MaxValue;

        // ── Pass 2: nearest any-cell candidate ───────────────────────────────
        var bestAny = cells[cells.Count / 2];
        var bestAnyDist = double.MaxValue;

        foreach (var (cx, cz) in cells)
        {
            var ddx = cx - anchorMapX;
            var ddz = cz - anchorMapZ;
            var dist = ddx * ddx + ddz * ddz; // squared distance in cell units

            // Any-cell pass: always track the nearest cell regardless of ring completeness.
            if (dist < bestAnyDist)
            {
                bestAny = (cx, cz);
                bestAnyDist = dist;
            }

            // Full-ring pass: a full ring requires all (2r+1)² cells at Chebyshev radius r.
            // For r=2 (5×5) that is 25 cells including the centre itself.
            // spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (r=2). CONFIRMED.
            var full = true;
            for (var dz = -ringRadius; dz <= ringRadius && full; dz++)
            for (var dx = -ringRadius; dx <= ringRadius; dx++)
                if (!present.Contains((cx + dx, cz + dz)))
                {
                    full = false;
                    break;
                }

            if (!full) continue;

            if (!bestFullFound || dist < bestFullDist)
            {
                bestFullFound = true;
                bestFull = (cx, cz);
                bestFullDist = dist;
            }
        }

        // ── Decision: use full-ring if it is close enough to the anchor ───────
        // If the best full-ring center is within MaxFullRingFallbackDistance cells of the anchor,
        // it is likely covering the content region too — use it for the perfect terrain ring.
        // If it is farther away, the content (NPCs/spawns) lives outside the complete-ring area;
        // use the nearest available cell so the streaming ring at least overlaps the content.
        // spec: Docs/RE/formats/terrain.md §12.3 — absent cells load empty without crash: CONFIRMED.
        var bestFullChebyshev = bestFullFound
            ? Math.Max(Math.Abs(bestFull.MapX - anchorMapX), Math.Abs(bestFull.MapZ - anchorMapZ))
            : double.MaxValue;

        var useFullRing = bestFullFound && bestFullChebyshev <= MaxFullRingFallbackDistance;
        var chosen = useFullRing ? bestFull : bestAny;

        return (chosen.MapX, chosen.MapZ, useFullRing);
    }

    /// <summary>
    ///     Reads the "area=" integer key from client_dir.cfg.
    ///     Returns 0 (the default) when the key is absent or unparseable.
    /// </summary>
    private static int ReadAreaFromConfig()
    {
        try
        {
            // Reuse ClientPathResolver's internal config reader by re-opening the same file.
            // Duplicate the minimal read logic here to keep the coupling narrow.
            // Fully-qualify to avoid ambiguity with the MartialHeroes namespace. spec: Godot API.
            var absPath = ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return 0;

            foreach (var rawLine in File.ReadLines(absPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();
                if (k.Equals("area", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(v, out var parsed))
                    return parsed;
            }
        }
        catch
        {
            // Any I/O error → default 0.
        }

        return 0;
    }

    /// <summary>
    ///     Reads the "ring_radius=" integer key from client_dir.cfg.
    ///     Valid values: 1 (3×3 ring, Medium quality) or 2 (5×5 ring, High quality).
    ///     Returns 2 (the high-quality default) when the key is absent, out-of-range, or unparseable.
    ///     spec: Docs/RE/formats/terrain.md §12.2 — High quality = ring radius 2 (5×5). CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §12.2 — Medium/Low quality = ring radius 1 (3×3). CONFIRMED.
    /// </summary>
    private static int ReadRingRadiusFromConfig()
    {
        const int DefaultRingRadius = 2; // spec: terrain.md §12.2 — High quality = radius 2 (5×5). CONFIRMED.
        try
        {
            var absPath = ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return DefaultRingRadius;

            foreach (var rawLine in File.ReadLines(absPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();
                if (k.Equals("ring_radius", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(v, out var parsed) &&
                    parsed >= 1 && parsed <= 2)
                    return parsed;
            }
        }
        catch
        {
            // Any I/O error → default radius 2.
        }

        return DefaultRingRadius;
    }

    // -------------------------------------------------------------------------
    // Path helpers
    // -------------------------------------------------------------------------

    private static string AreaTag(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition. CONFIRMED.
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}