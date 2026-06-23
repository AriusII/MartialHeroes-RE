
using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Client.Godot.World;

public sealed class CellCollisionManager
{
    private const int GridDim = 16;

    private const int LeafSplitThreshold = 16;

    private const float CellSpan = 1024f;

    private readonly Dictionary<(int MapX, int MapZ), CellCollision> _cells = new();

    public void RegisterCell(
        int mapX, int mapZ, SodBlob? sod,
        CollisionTriangleList? upTriangles = null,
        CollisionTriangleList? exdTriangles = null)
    {
        var legacyOriginX = (mapX - 10000) * CellSpan;
        var legacyOriginZ = (mapZ - 10000) * CellSpan;

        var cell = new CellCollision(legacyOriginX, legacyOriginZ);

        if (sod is not null)
            for (var s = 0; s < sod.Solids.Length; s++)
            {
                var solid = sod.Solids[s];
                for (var q = 0; q < solid.Segments.Length; q++)
                {
                    var seg = ToGodotSegment(solid.Segments[q]);
                    cell.Insert(seg);
                }
            }

        if (upTriangles is not null)
            AppendOverhang(cell, upTriangles);
        if (exdTriangles is not null)
            AppendOverhang(cell, exdTriangles);

        _cells[(mapX, mapZ)] = cell;
    }

    public void UnregisterCell(int mapX, int mapZ)
    {
        _cells.Remove((mapX, mapZ));
    }

    public void Clear()
    {
        _cells.Clear();
    }

    public bool TryMoveSlide(Vector3 fromGodot, Vector3 toGodot, out Vector3 resolvedGodot)
    {
        resolvedGodot = toGodot;

        var fromXz = new Vector2(fromGodot.X, fromGodot.Z);
        var toXz = new Vector2(toGodot.X, toGodot.Z);
        var motion = toXz - fromXz;
        if (motion.LengthSquared() <= float.Epsilon)
            return false;

        var sweepMinX = Math.Min(fromXz.X, toXz.X);
        var sweepMaxX = Math.Max(fromXz.X, toXz.X);
        var sweepMinZ = Math.Min(fromXz.Y, toXz.Y);
        var sweepMaxZ = Math.Max(fromXz.Y, toXz.Y);

        var nearestT = float.PositiveInfinity;
        GodotWallSegment? nearestSeg = null;

        foreach (var cell in _cells.Values)
        foreach (var seg in cell.QueryCandidates(sweepMinX, sweepMinZ, sweepMaxX, sweepMaxZ))
        {
            if (seg.AabbMaxX < sweepMinX || seg.AabbMinX > sweepMaxX ||
                seg.AabbMaxZ < sweepMinZ || seg.AabbMinZ > sweepMaxZ)
                continue;

            if (TrySegmentIntersect(fromXz, toXz, seg.P0, seg.P1, out var t) && t < nearestT)
            {
                nearestT = t;
                nearestSeg = seg;
            }
        }

        if (nearestSeg is null)
            return false;

        const float BackOff = 0.01f;
        var hitT = Math.Clamp(nearestT - BackOff / Math.Max(motion.Length(), 1e-4f), 0f, 1f);
        var hitXz = fromXz + motion * hitT;

        var wallDir = nearestSeg.Value.P1 - nearestSeg.Value.P0;
        var slidXz = hitXz;
        if (wallDir.LengthSquared() > 1e-8f)
        {
            wallDir = wallDir.Normalized();
            var remaining = toXz - hitXz;
            var projected = wallDir * remaining.Dot(wallDir);
            slidXz = hitXz + projected;

            if (TrySegmentIntersect(hitXz, slidXz, nearestSeg.Value.P0, nearestSeg.Value.P1, out var st))
                slidXz = hitXz + (slidXz - hitXz) * Math.Clamp(st - BackOff, 0f, 1f);
        }

        resolvedGodot = new Vector3(slidXz.X, toGodot.Y, slidXz.Y);
        return true;
    }

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
                if (tri.PlaneGodotY > best)
                {
                    best = tri.PlaneGodotY;
                    found = true;
                }

        if (found)
            overhangGodotY = best;
        return found;
    }


    private static GodotWallSegment ToGodotSegment(WallSegment w)
    {
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
            var a = new Vector2(t.V1X, -t.V1Z);
            var b = new Vector2(t.V2X, -t.V2Z);
            var c = new Vector2(t.V3X, -t.V3Z);
            cell.Overhangs.Add(new GodotOverhangTri { A = a, B = b, C = c, PlaneGodotY = t.PlaneHeight });
        }
    }

    private static bool TrySegmentIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float t)
    {
        t = 0f;
        var r = a1 - a0;
        var s = b1 - b0;
        var denom = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denom) < 1e-8f)
            return false;

        var qp = b0 - a0;
        var tNum = qp.X * s.Y - qp.Y * s.X;
        var uNum = qp.X * r.Y - qp.Y * r.X;
        var tt = tNum / denom;
        var uu = uNum / denom;

        if (tt < 0f || tt > 1f || uu < 0f || uu > 1f)
            return false;

        t = tt;
        return true;
    }

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


    private sealed class CellCollision
    {
        private readonly List<int>[] _buckets;

        private readonly float _gridMinX;
        private readonly float _gridMinZ;
        private readonly List<GodotWallSegment> _segments = new();

        public readonly List<GodotOverhangTri> Overhangs = new();

        public CellCollision(float legacyOriginX, float legacyOriginZ)
        {
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

            var (c0x, c0z) = CellOf(seg.AabbMinX, seg.AabbMinZ);
            var (c1x, c1z) = CellOf(seg.AabbMaxX, seg.AabbMaxZ);
            for (var cz = c0z; cz <= c1z; cz++)
            for (var cx = c0x; cx <= c1x; cx++)
                _buckets[cz * GridDim + cx].Add(index);
        }

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

        private (int X, int Z) CellOf(float x, float z)
        {
            var cellSize = CellSpan / GridDim;
            var cx = (int)Math.Floor((x - _gridMinX) / cellSize);
            var cz = (int)Math.Floor((z - _gridMinZ) / cellSize);
            cx = Math.Clamp(cx, 0, GridDim - 1);
            cz = Math.Clamp(cz, 0, GridDim - 1);
            return (cx, cz);
        }
    }

    private readonly struct GodotWallSegment
    {
        public float AabbMinX { get; init; }
        public float AabbMinZ { get; init; }
        public float AabbMaxX { get; init; }
        public float AabbMaxZ { get; init; }
        public Vector2 P0 { get; init; }
        public Vector2 P1 { get; init; }
    }

    private readonly struct GodotOverhangTri
    {
        public Vector2 A { get; init; }
        public Vector2 B { get; init; }
        public Vector2 C { get; init; }
        public float PlaneGodotY { get; init; }
    }
}