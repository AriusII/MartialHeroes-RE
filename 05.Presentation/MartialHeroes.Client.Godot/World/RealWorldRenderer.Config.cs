using Godot;
using MartialHeroes.Assets.Parsers.World;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private static (double AnchorMapX, double AnchorMapZ) ComputeSpawnAnchor(
        RealClientAssets assets,
        int areaId,
        List<(int MapX, int MapZ)> terrainCells,
        int ringRadius = 2)
    {
        long sumX = 0, sumZ = 0;
        foreach (var (x, z) in terrainCells)
        {
            sumX += x;
            sumZ += z;
        }

        var fallbackX = sumX / (double)terrainCells.Count;
        var fallbackZ = sumZ / (double)terrainCells.Count;

        if (areaId == 0)
            return (fallbackX, fallbackZ);

        var tag = AreaTag(areaId);

        var cellCounts = new Dictionary<(int, int), int>(64);

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
            }

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
            }

        if (cellCounts.Count == 0)
            return (fallbackX, fallbackZ);

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

    private static (int MapX, int MapZ, bool FullRing) PickRingCenter(
        List<(int MapX, int MapZ)> cells,
        double anchorMapX,
        double anchorMapZ,
        int ringRadius = 2)
    {
        const double MaxFullRingFallbackDistance = 2.0;

        cells.Sort((a, b) => a.MapX != b.MapX ? a.MapX.CompareTo(b.MapX) : a.MapZ.CompareTo(b.MapZ));

        var present = new HashSet<(int, int)>(cells.Count);
        foreach (var (x, z) in cells) present.Add((x, z));

        var bestFullFound = false;
        var bestFull = cells[cells.Count / 2];
        var bestFullDist = double.MaxValue;

        var bestAny = cells[cells.Count / 2];
        var bestAnyDist = double.MaxValue;

        foreach (var (cx, cz) in cells)
        {
            var ddx = cx - anchorMapX;
            var ddz = cz - anchorMapZ;
            var dist = ddx * ddx + ddz * ddz;

            if (dist < bestAnyDist)
            {
                bestAny = (cx, cz);
                bestAnyDist = dist;
            }

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

        var bestFullChebyshev = bestFullFound
            ? Math.Max(Math.Abs(bestFull.MapX - anchorMapX), Math.Abs(bestFull.MapZ - anchorMapZ))
            : double.MaxValue;

        var useFullRing = bestFullFound && bestFullChebyshev <= MaxFullRingFallbackDistance;
        var chosen = useFullRing ? bestFull : bestAny;

        return (chosen.MapX, chosen.MapZ, useFullRing);
    }

    private static int ReadRingRadiusFromConfig()
    {
        const int DefaultRingRadius = 2;
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
        }

        return DefaultRingRadius;
    }


    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}