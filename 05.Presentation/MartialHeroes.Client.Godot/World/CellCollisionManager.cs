// World/CellCollisionManager.cs
//
// FIX 13 core — the missing .sod runtime consumer.
//
// SodBlobParser fully decodes SodBlob{Solids[].Segments[]} (slope-intercept wall segments z=m·x+b,
// XConst when AxisFlag==1, with a 2D XZ AABB + endpoints), but until now NOTHING consumed it for
// movement: AssembledCell.Collision held the blob and the movement path never read it. This manager
// is that consumer — it provides horizontal wall block / slide against the .sod wall segments.
//
// Scope (this manager): geometric wall BLOCK + SLIDE in the XZ plane, plus optional .up/.exd overhang
// triangle tests when those buffers are plumbed. It does NOT do zone gating — the Closed-zone (zoneType
// == 2) movement refusal is already implemented and IDA-verified end-to-end via RegionService /
// RegionCatalog (Region_ResolveCombatMode @0x429204), which is OUTSIDE this cluster.
//
// Coordinate convention:
//   .sod geometry is ABSOLUTE world XZ (sod.md §"absolute world XZ", §Coordinate convention). The Godot
//   world-render path negates Z ((x,y,z)→(x,y,-z) — WorldCoordinates.ToGodot). sod.md §Coordinate
//   convention (port note): "A faithful port must solve .sod collision in the SAME convention it renders
//   terrain in (apply the identical Z negation consistently), or walls will not line up." So this manager
//   stores and queries everything in GODOT space (Z already negated at register time) and the public API
//   takes/returns Godot Vector3.
//
// Acceleration structure:
//   sod.md §Read algorithm + §Runtime collision query describe a per-solid 16×16 grid (SOD_GRID_DIM=16)
//   with a sub-quadtree split when a grid cell accumulates many refs (SOD_LEAF_SPLIT_THRESHOLD=16). We
//   build a 16×16 uniform bucket grid over each registered cell's world footprint and insert each wall
//   segment into every grid cell its AABB overlaps. The query maps the sweep AABB onto the grid and tests
//   only the bucketed segments (deduplicated per query). A full recursive sub-quadtree split is an
//   optimisation the uniform grid does not require for the per-cell segment counts seen (the 16×16 grid
//   alone bounds the candidate set); the split threshold is recorded for parity but the uniform grid is
//   the implemented structure. spec: Docs/RE/formats/sod.md §Read algorithm steps 1/5; §Known unknowns.
//
// Collision math (UNVERIFIED — engineering reconstruction):
//   The named Sod_/Collision_/Quadtree_/Move_ slide functions are NOT symbolized in the IDB (CLAUDE.md);
//   sod.md is the authoritative spec. sod.md §Runtime collision query step 4 specifies: 2D-AABB cull →
//   slope-intercept LINE/LINE intersection between the movement segment and the wall segment → two
//   point-in-AABB containment checks to clamp the intersection onto BOTH segments → keep the nearest hit
//   (squared distance to the query origin). That intersection test is spec-pinned and implemented here.
//   The SLIDE RESPONSE vector (what the player does after a block) is NOT byte-confirmed — sod.md only
//   says "block / slide along the wall". We pick the conservative, standard response: stop just short of
//   the wall, then project the REMAINING motion onto the wall tangent (a project-onto-wall slide, not a
//   reflect). This is marked UNVERIFIED; if a debugger trace pins the exact response, update only this
//   file. spec: Docs/RE/formats/sod.md §Runtime collision query (block/slide), §Known unknowns.

using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Per-area runtime consumer of decoded <see cref="SodBlob" /> wall-collision data: provides
///     horizontal wall block / slide (and optional .up/.exd overhang tests) for the local-player
///     movement path. Cells are registered by their <c>(mapX, mapZ)</c> tuple as they stream into the
///     ring and unregistered as they recycle. All geometry is stored in Godot space (Z negated at
///     register time) so queries take and return Godot <see cref="Vector3" />.
///     <para>
///         This manager handles ONLY geometric wall collision. Zone gating (Closed zoneType==2 movement
///         refusal) is delegated to the existing RegionService / RegionCatalog (IDA-verified,
///         out-of-cluster). The actual call into <see cref="TryMoveSlide" /> belongs in the local-player
///         move/reconcile path (GameLoop / ActorRegistry), which is outside this cluster — see the
///         coordination note in the cluster report.
///     </para>
///     spec: Docs/RE/formats/sod.md — wall-segment slope-intercept collision, 16×16 grid, absolute world
///     XZ, Godot Z-negation port note: CONFIRMED (container/line math); slide-response UNVERIFIED.
/// </summary>
internal sealed class CellCollisionManager
{
    /// <summary>
    ///     Per-solid acceleration grid dimension (16×16).
    ///     spec: Docs/RE/formats/sod.md §Read algorithm step 1 — "fixed-size 16×16 grid block";
    ///     §Names — SOD_GRID_DIM = 16: CONFIRMED.
    /// </summary>
    private const int GridDim = 16; // spec: sod.md SOD_GRID_DIM = 16: CONFIRMED

    /// <summary>
    ///     Leaf split threshold (recorded for parity; the uniform 16×16 grid is the implemented structure).
    ///     spec: Docs/RE/formats/sod.md §Read algorithm step 5 — "split threshold = 16";
    ///     §Names — SOD_LEAF_SPLIT_THRESHOLD = 16: CONFIRMED.
    /// </summary>
    private const int LeafSplitThreshold = 16; // spec: sod.md SOD_LEAF_SPLIT_THRESHOLD = 16: CONFIRMED

    /// <summary>
    ///     One terrain cell's world footprint span in world units (also the grid coverage span).
    ///     spec: Docs/RE/formats/sod.md §Linkages — "cell span 1024 units"; terrain.md §1.4: CONFIRMED.
    /// </summary>
    private const float CellSpan = 1024f; // spec: sod.md §Linkages cell span 1024: CONFIRMED

    // Registered cells keyed by (mapX, mapZ). Each holds a 16×16 grid of segment buckets in Godot space.
    private readonly Dictionary<(int MapX, int MapZ), CellCollision> _cells = new();

    /// <summary>
    ///     Registers a cell's collision geometry, building the 16×16 grid over its world footprint.
    ///     Idempotent: re-registering the same key replaces the prior entry. A <see langword="null" />
    ///     <paramref name="sod" /> registers an empty (pass-through) cell — absent <c>.sod</c> is not an
    ///     error (5 areas have a cell without <c>.sod</c>).
    ///     spec: Docs/RE/formats/area_inventory.md §3.1 — missing .sod is not an error.
    ///     spec: Docs/RE/formats/sod.md §Read algorithm — build per-solid 16×16 grid at load: CONFIRMED.
    /// </summary>
    /// <param name="mapX">Biased cell X grid index.</param>
    /// <param name="mapZ">Biased cell Z grid index.</param>
    /// <param name="sod">Decoded wall-collision blob, or null when absent.</param>
    /// <param name="upTriangles">Optional .up overhang triangles (CollisionTriangleList), or null.</param>
    /// <param name="exdTriangles">Optional .exd overhang triangles (CollisionTriangleList), or null.</param>
    public void RegisterCell(
        int mapX, int mapZ, SodBlob? sod,
        CollisionTriangleList? upTriangles = null,
        CollisionTriangleList? exdTriangles = null)
    {
        // Cell world origin (SW corner) in LEGACY space, then Z-negated to Godot for the grid bounds.
        // spec: sod.md §Linkages — cell origin = ((cellX-10000)·1024, (cellZ-10000)·1024): CONFIRMED.
        var legacyOriginX = (mapX - 10000) * CellSpan;
        var legacyOriginZ = (mapZ - 10000) * CellSpan;

        var cell = new CellCollision(legacyOriginX, legacyOriginZ);

        if (sod is not null)
            for (var s = 0; s < sod.Solids.Length; s++)
            {
                var solid = sod.Solids[s];
                for (var q = 0; q < solid.Segments.Length; q++)
                {
                    // Convert each wall segment to Godot space (negate Z) once at register time.
                    // spec: sod.md §Coordinate convention — solve collision in render convention (negate Z).
                    var seg = ToGodotSegment(solid.Segments[q]);
                    cell.Insert(seg);
                }
            }

        // Optional overhang triangles (.up/.exd). Converted to Godot space (negate Z on each vertex).
        // spec: Docs/RE/formats/terrain_layers.md §2.1 — .up/.exd 40-byte triangle (10×f32): CONFIRMED.
        if (upTriangles is not null)
            AppendOverhang(cell, upTriangles);
        if (exdTriangles is not null)
            AppendOverhang(cell, exdTriangles);

        _cells[(mapX, mapZ)] = cell;
    }

    /// <summary>
    ///     Unregisters a cell on ring recycle. No-op if the key is not present.
    ///     spec: Docs/RE/structs/terrain-manager.md §Pool ownership vs ring view — cells recycle on cull.
    /// </summary>
    public void UnregisterCell(int mapX, int mapZ)
    {
        _cells.Remove((mapX, mapZ));
    }

    /// <summary>Removes all registered cells (e.g. on area change).</summary>
    public void Clear()
    {
        _cells.Clear();
    }

    /// <summary>
    ///     Sweeps the desired horizontal motion <paramref name="fromGodot" /> → <paramref name="toGodot" />
    ///     against all registered wall segments and resolves block / slide. Y is preserved from
    ///     <paramref name="toGodot" /> (.sod is XZ-only; ground height is solved separately via .ted).
    ///     <para>
    ///         Returns <see langword="true" /> when a wall was struck (and <paramref name="resolvedGodot" />
    ///         is the blocked-and-slid position), <see langword="false" /> when the motion is clear (and
    ///         <paramref name="resolvedGodot" /> equals <paramref name="toGodot" />).
    ///     </para>
    ///     Math: 2D-AABB cull → slope-intercept line/line intersection → clamp onto both segments via AABB
    ///     → nearest hit → stop short + project remaining motion onto the wall tangent (slide).
    ///     spec: Docs/RE/formats/sod.md §Runtime collision query step 4 — intersection + nearest hit:
    ///     CONFIRMED. Slide response (project-onto-tangent): UNVERIFIED engineering reconstruction.
    /// </summary>
    public bool TryMoveSlide(Vector3 fromGodot, Vector3 toGodot, out Vector3 resolvedGodot)
    {
        resolvedGodot = toGodot;

        var fromXz = new Vector2(fromGodot.X, fromGodot.Z);
        var toXz = new Vector2(toGodot.X, toGodot.Z);
        var motion = toXz - fromXz;
        if (motion.LengthSquared() <= float.Epsilon)
            return false; // no motion → nothing to test.

        // Sweep AABB (the moving segment's bounds) for grid culling.
        var sweepMinX = Math.Min(fromXz.X, toXz.X);
        var sweepMaxX = Math.Max(fromXz.X, toXz.X);
        var sweepMinZ = Math.Min(fromXz.Y, toXz.Y);
        var sweepMaxZ = Math.Max(fromXz.Y, toXz.Y);

        // Find the nearest wall crossing across all registered cells (squared distance to origin).
        // spec: sod.md §Runtime collision query step 1 — nearest-hit set to +∞, keep nearest.
        var nearestT = float.PositiveInfinity; // parametric [0,1] along the motion
        GodotWallSegment? nearestSeg = null;

        foreach (var cell in _cells.Values)
        {
            // Dedup segments shared across grid cells within this query (a segment may span buckets).
            // spec: sod.md §Runtime collision query step 2 — "deduplicated per query".
            foreach (var seg in cell.QueryCandidates(sweepMinX, sweepMinZ, sweepMaxX, sweepMaxZ))
            {
                // 2D-AABB cull: skip segments whose AABB cannot overlap the sweep AABB.
                // spec: sod.md §Runtime collision query step 4 — "2D-AABB cull".
                if (seg.AabbMaxX < sweepMinX || seg.AabbMinX > sweepMaxX ||
                    seg.AabbMaxZ < sweepMinZ || seg.AabbMinZ > sweepMaxZ)
                    continue;

                // Segment/segment intersection: movement segment vs wall segment endpoints.
                // spec: sod.md §Runtime collision query step 4 — line/line intersection clamped onto
                //   both segments via the AABB containment checks. We use the explicit endpoints (P0/P1)
                //   which bound the wall segment, equivalent to the slope-intercept line clamped by AABB.
                if (TrySegmentIntersect(fromXz, toXz, seg.P0, seg.P1, out var t) && t < nearestT)
                {
                    nearestT = t;
                    nearestSeg = seg;
                }
            }
        }

        if (nearestSeg is null)
            return false; // clear path.

        // Block: stop just short of the wall (small epsilon back-off so we never sit exactly on it).
        const float BackOff = 0.01f; // empirical skin width to avoid re-colliding on the same wall
        var hitT = Math.Clamp(nearestT - BackOff / Math.Max(motion.Length(), 1e-4f), 0f, 1f);
        var hitXz = fromXz + motion * hitT;

        // Slide: project the REMAINING motion onto the wall tangent (the wall direction).
        // spec: sod.md §Runtime collision query — "block / slide along the wall". UNVERIFIED response.
        var wallDir = (nearestSeg.Value.P1 - nearestSeg.Value.P0);
        var slidXz = hitXz;
        if (wallDir.LengthSquared() > 1e-8f)
        {
            wallDir = wallDir.Normalized();
            var remaining = toXz - hitXz;
            var projected = wallDir * remaining.Dot(wallDir);
            slidXz = hitXz + projected;

            // Re-test the slide motion once against the same wall so the slide does not re-cross it.
            // (Conservative single pass — a full iterative resolve is out of scope without a byte
            //  reference for the iteration count.) spec: sod.md §Runtime collision query: UNVERIFIED.
            if (TrySegmentIntersect(hitXz, slidXz, nearestSeg.Value.P0, nearestSeg.Value.P1, out var st))
                slidXz = hitXz + (slidXz - hitXz) * Math.Clamp(st - BackOff, 0f, 1f);
        }

        resolvedGodot = new Vector3(slidXz.X, toGodot.Y, slidXz.Y);
        return true;
    }

    /// <summary>
    ///     Tests whether a vertical position is under an <c>.up</c>/<c>.exd</c> overhang triangle at the
    ///     given Godot XZ, returning the overhang's plane height (Godot Y) when found. Returns
    ///     <see langword="false" /> when no overhang covers the point. Overhang triangles are only present
    ///     when the caller plumbed .up/.exd buffers into <see cref="RegisterCell" /> (deferred by default).
    ///     spec: Docs/RE/formats/terrain_layers.md §2.1 — .up/.exd 40-byte triangle, plane_height @ +0x24:
    ///     CONFIRMED (flat-triangle case).
    /// </summary>
    public bool OverhangTest(Vector3 atGodot, out float overhangGodotY)
    {
        overhangGodotY = 0f;
        var px = atGodot.X;
        var pz = atGodot.Z;

        var found = false;
        var best = float.NegativeInfinity;
        foreach (var cell in _cells.Values)
            foreach (var tri in cell.Overhangs)
                if (PointInTriangleXz(px, pz, tri.A, tri.B, tri.C))
                {
                    // Use the triangle's plane height (already Godot Y). Keep the highest covering plane.
                    if (tri.PlaneGodotY > best)
                    {
                        best = tri.PlaneGodotY;
                        found = true;
                    }
                }

        if (found)
            overhangGodotY = best;
        return found;
    }

    // ─── Geometry helpers ───────────────────────────────────────────────────────

    /// <summary>
    ///     Converts a decoded <see cref="WallSegment" /> (legacy world XZ) to a Godot-space segment by
    ///     negating Z on the AABB and endpoints. spec: sod.md §Coordinate convention — negate Z.
    /// </summary>
    private static GodotWallSegment ToGodotSegment(WallSegment w)
    {
        // Negate Z on both endpoints and the AABB Z bounds. Negation flips min/max ordering on Z, so
        // recompute min/max after negation. spec: WorldCoordinates.ToGodot — (x,y,z)→(x,y,-z): CONFIRMED.
        var p0 = new Vector2(w.P0X, -w.P0Z);
        var p1 = new Vector2(w.P1X, -w.P1Z);
        var minZ = Math.Min(-w.AabbMinZ, -w.AabbMaxZ);
        var maxZ = Math.Max(-w.AabbMinZ, -w.AabbMaxZ);
        return new GodotWallSegment
        {
            AabbMinX = w.AabbMinX,
            AabbMaxX = w.AabbMaxX,
            AabbMinZ = minZ,
            AabbMaxZ = maxZ,
            P0 = p0,
            P1 = p1
        };
    }

    private static void AppendOverhang(CellCollision cell, CollisionTriangleList list)
    {
        foreach (var t in list.Triangles)
        {
            // Negate Z on each vertex (legacy → Godot). plane_height equals vertex Y in flat samples, so
            // it is a Y value (unaffected by Z negation). spec: terrain_layers.md §2.1: CONFIRMED.
            var a = new Vector2(t.V1X, -t.V1Z);
            var b = new Vector2(t.V2X, -t.V2Z);
            var c = new Vector2(t.V3X, -t.V3Z);
            cell.Overhangs.Add(new GodotOverhangTri { A = a, B = b, C = c, PlaneGodotY = t.PlaneHeight });
        }
    }

    /// <summary>
    ///     Segment/segment intersection in 2D (XZ). On hit, <paramref name="t" /> is the parametric
    ///     position [0,1] of the intersection along the moving segment a0→a1. Returns false for parallel
    ///     or non-overlapping segments. spec: sod.md §Runtime collision query step 4 — line/line clamped
    ///     onto both segments.
    /// </summary>
    private static bool TrySegmentIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float t)
    {
        t = 0f;
        var r = a1 - a0;
        var s = b1 - b0;
        var denom = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denom) < 1e-8f)
            return false; // parallel / collinear — treat as no crossing (conservative).

        var qp = b0 - a0;
        var tNum = qp.X * s.Y - qp.Y * s.X;
        var uNum = qp.X * r.Y - qp.Y * r.X;
        var tt = tNum / denom; // along the moving segment
        var uu = uNum / denom; // along the wall segment

        if (tt < 0f || tt > 1f || uu < 0f || uu > 1f)
            return false; // intersection lies outside one of the segments (AABB-clamp equivalent).

        t = tt;
        return true;
    }

    /// <summary>2D point-in-triangle test (XZ) via barycentric sign. Used for overhang coverage.</summary>
    private static bool PointInTriangleXz(float px, float pz, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Sign(px, pz, a, b);
        var d2 = Sign(px, pz, b, c);
        var d3 = Sign(px, pz, c, a);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);

        static float Sign(float px, float pz, Vector2 u, Vector2 v)
        {
            return (px - v.X) * (u.Y - v.Y) - (u.X - v.X) * (pz - v.Y);
        }
    }

    // ─── Per-cell storage ───────────────────────────────────────────────────────

    /// <summary>
    ///     One registered cell's collision: a 16×16 grid of wall-segment buckets over the cell footprint,
    ///     plus optional overhang triangles. All geometry in Godot space.
    ///     spec: Docs/RE/formats/sod.md §Read algorithm — 16×16 grid seeded from cell world bounds.
    /// </summary>
    private sealed class CellCollision
    {
        private readonly List<int>[] _buckets; // GridDim×GridDim buckets of indices into _segments
        private readonly List<GodotWallSegment> _segments = new();

        // Grid origin in GODOT space (negate Z on the legacy SW-corner Z). The footprint spans CellSpan.
        private readonly float _gridMinX;
        private readonly float _gridMinZ;

        public readonly List<GodotOverhangTri> Overhangs = new();

        public CellCollision(float legacyOriginX, float legacyOriginZ)
        {
            // Godot Z of the cell's two Z corners (negate flips ordering); take the min for the grid origin.
            // spec: sod.md §Coordinate convention — negate Z; §Linkages — cell origin & span 1024.
            var gz0 = -legacyOriginZ;
            var gz1 = -(legacyOriginZ + CellSpan);
            _gridMinX = legacyOriginX;
            _gridMinZ = Math.Min(gz0, gz1);

            _buckets = new List<int>[GridDim * GridDim];
            for (var i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<int>();
        }

        public void Insert(GodotWallSegment seg)
        {
            var index = _segments.Count;
            _segments.Add(seg);

            // Insert into every grid cell the segment AABB overlaps.
            // spec: sod.md §Read algorithm step 5 — partition segments into the 16×16 grid.
            var (c0x, c0z) = CellOf(seg.AabbMinX, seg.AabbMinZ);
            var (c1x, c1z) = CellOf(seg.AabbMaxX, seg.AabbMaxZ);
            for (var cz = c0z; cz <= c1z; cz++)
                for (var cx = c0x; cx <= c1x; cx++)
                    _buckets[cz * GridDim + cx].Add(index);
        }

        /// <summary>
        ///     Yields candidate segments whose buckets overlap the query AABB, deduplicated per query.
        ///     spec: sod.md §Runtime collision query step 2 — dedup per query (frame counter analogue).
        /// </summary>
        public IEnumerable<GodotWallSegment> QueryCandidates(float minX, float minZ, float maxX, float maxZ)
        {
            var (c0x, c0z) = CellOf(minX, minZ);
            var (c1x, c1z) = CellOf(maxX, maxZ);

            var seen = new HashSet<int>();
            for (var cz = c0z; cz <= c1z; cz++)
                for (var cx = c0x; cx <= c1x; cx++)
                    foreach (var idx in _buckets[cz * GridDim + cx])
                        if (seen.Add(idx))
                            yield return _segments[idx];
        }

        // Maps a Godot XZ point to a clamped [0,GridDim-1] grid cell over the cell footprint.
        private (int X, int Z) CellOf(float x, float z)
        {
            var cellSize = CellSpan / GridDim; // 1024 / 16 = 64 wu per grid cell
            var cx = (int)Math.Floor((x - _gridMinX) / cellSize);
            var cz = (int)Math.Floor((z - _gridMinZ) / cellSize);
            cx = Math.Clamp(cx, 0, GridDim - 1);
            cz = Math.Clamp(cz, 0, GridDim - 1);
            return (cx, cz);
        }
    }

    /// <summary>One wall segment in Godot space (Z already negated): 2D AABB + endpoints.</summary>
    private readonly struct GodotWallSegment
    {
        public float AabbMinX { get; init; }
        public float AabbMinZ { get; init; }
        public float AabbMaxX { get; init; }
        public float AabbMaxZ { get; init; }
        public Vector2 P0 { get; init; }
        public Vector2 P1 { get; init; }
    }

    /// <summary>One overhang triangle in Godot space (XZ corners + plane Y).</summary>
    private readonly struct GodotOverhangTri
    {
        public Vector2 A { get; init; }
        public Vector2 B { get; init; }
        public Vector2 C { get; init; }
        public float PlaneGodotY { get; init; }
    }
}
