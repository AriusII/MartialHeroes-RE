using System.Text.RegularExpressions;
using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Explorer.Viewer;

public sealed record MapAssemblyInfo(
    string MapId,
    int CellsFound,
    int CellsBuilt,
    int BudScenesBuilt,
    int BudObjects,
    Vector3 WorldExtent,
    Aabb FrameBox,
    Vector3 RootOffset,
    IReadOnlyList<(Vector3 CellOriginGodot, TerrainCell Cell)> TerrainCells,
    IReadOnlyList<string> Notes);

public static partial class MapAssembler
{
    private const int CellWorld = 1024;
    private const int CellOriginBias = 10000;

    [GeneratedRegex(@"x(\d+)z(\d+)\.ted$", RegexOptions.IgnoreCase)]
    private static partial Regex CellCoordRegex();

    public static IReadOnlyList<string> DiscoverMapIds(MappedVfsArchive archive)
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirRegex = MapDirRegex();
        foreach (var entry in archive.GetEntries())
        {
            if (!entry.Name.EndsWith(".ted", StringComparison.OrdinalIgnoreCase)) continue;
            var m = dirRegex.Match(entry.Name);
            if (m.Success) ids.Add(m.Groups[1].Value);
        }

        return [.. ids];
    }

    public static Node3D Build(MappedVfsArchive archive, BgTextureCatalog? bgCatalog, string mapId,
        out MapAssemblyInfo info)
    {
        var notes = new List<string>();
        var root = new Node3D { Name = $"Map_{mapId}" };

        var prefix = $"data/map{mapId}/";
        var cellPaths = new List<string>();
        foreach (var entry in archive.GetEntries())
        {
            var name = entry.Name.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(".ted", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".ted.post", StringComparison.OrdinalIgnoreCase)) continue;
            cellPaths.Add(name);
        }

        cellPaths.Sort(StringComparer.OrdinalIgnoreCase);

        var cellsBuilt = 0;
        var budScenes = 0;
        var budObjects = 0;
        var fxLayers = 0;
        var wallSegments = 0;
        var hasTerrainBox = false;
        var terrainBox = new Aabb();
        var terrainCells = new List<(Vector3 CellOriginGodot, TerrainCell Cell)>();

        foreach (var tedPath in cellPaths)
        {
            var m = CellCoordRegex().Match(tedPath);
            if (!m.Success)
            {
                notes.Add($"skip (no x/z in name): {Path.GetFileName(tedPath)}");
                continue;
            }

            var mapX = int.Parse(m.Groups[1].Value);
            var mapZ = int.Parse(m.Groups[2].Value);
            var originGodot = new Vector3(
                (mapX - CellOriginBias) * CellWorld,
                0f,
                -((mapZ - CellOriginBias) * CellWorld));

            TerrainCell cell;
            try
            {
                cell = TedTerrainParser.Parse(archive.GetFileContent(tedPath));
                terrainCells.Add((originGodot, cell));
                var terrainNode = TedTerrainBuilder.BuildWorld(cell, archive, bgCatalog, tedPath, originGodot);
                root.AddChild(terrainNode);
                cellsBuilt++;

                if (terrainNode.GetChildCount() > 0 && terrainNode.GetChild(0) is MeshInstance3D tmesh
                                                    && tmesh.Mesh is not null)
                {
                    var worldBox = terrainNode.Transform * tmesh.Mesh.GetAabb();
                    if (!hasTerrainBox)
                    {
                        terrainBox = worldBox;
                        hasTerrainBox = true;
                    }
                    else
                    {
                        terrainBox = terrainBox.Merge(worldBox);
                    }
                }
            }
            catch (Exception ex)
            {
                notes.Add($"terrain fail {Path.GetFileName(tedPath)}: {ex.Message}");
                continue;
            }

            TryBuildOverlays(archive, bgCatalog, tedPath, cell, mapX, mapZ, root, notes,
                ref fxLayers, ref wallSegments);

            var budPath = tedPath[..^4] + ".bud";
            if (!archive.Contains(budPath)) continue;
            try
            {
                var scene = TerrainSceneParser.Parse(archive.GetFileContent(budPath));
                var budNode = BudSceneBuilder.BuildWorld(scene, archive, bgCatalog, budPath);
                budNode.Name = $"Bud_x{mapX}z{mapZ}";
                root.AddChild(budNode);
                budScenes++;
                budObjects += scene.Objects.Length;
            }
            catch (Exception ex)
            {
                notes.Add($"bud fail {Path.GetFileName(budPath)}: {ex.Message}");
            }
        }

        if (fxLayers > 0 || wallSegments > 0)
            notes.Add($"overlays: {fxLayers} FX layer(s), {wallSegments} collision wall segment(s)");

        var (frameBox, rootOffset) = GroundAndCentre(root, hasTerrainBox ? terrainBox : null);
        var extent = frameBox.Size;

        info = new MapAssemblyInfo(mapId, cellPaths.Count, cellsBuilt, budScenes, budObjects, extent, frameBox,
            rootOffset, terrainCells, notes);
        GD.Print($"[Map] {mapId}: cells {cellsBuilt}/{cellPaths.Count}, bud scenes {budScenes} " +
                 $"({budObjects} objects), extent X={extent.X:F0} Y={extent.Y:F0} Z={extent.Z:F0}.");
        return root;
    }

    public static float? SampleRawGroundHeight(
        IReadOnlyList<(Vector3 CellOriginGodot, TerrainCell Cell)> cells,
        Vector3 rootOffset,
        float viewportX, float viewportZ)
    {
        var rawX = viewportX - rootOffset.X;
        var rawZ = viewportZ - rootOffset.Z;
        var boundary = (TerrainCell.GridSize - 1) * TerrainCell.GridSpacing;
        foreach (var (origin, cell) in cells)
        {
            var localX = rawX - origin.X;
            var localZ = origin.Z - rawZ;
            if (localX < 0f || localX > boundary || localZ < 0f || localZ > boundary)
                continue;
            return cell.SampleGroundHeight(localX, localZ);
        }

        return null;
    }

    private static void TryBuildOverlays(MappedVfsArchive archive, BgTextureCatalog? bgCatalog,
        string tedPath, TerrainCell cell, int mapX, int mapZ, Node3D root, List<string> notes,
        ref int fxLayers, ref int wallSegments)
    {
        var mapPath = FindSiblingMap(archive, tedPath);
        if (mapPath is null) return;

        MapDescriptor descriptor;
        try
        {
            descriptor = MapDescriptorParser.Parse(archive.GetFileContent(mapPath));
        }
        catch (Exception ex)
        {
            notes.Add($"overlay map fail {Path.GetFileName(mapPath)}: {ex.Message}");
            return;
        }

        try
        {
            var fxNode = MapOverlayBuilder.BuildFxLayers(archive, bgCatalog, descriptor, out var built);
            if (fxNode is not null)
            {
                fxNode.Name = $"Fx_x{mapX}z{mapZ}";
                root.AddChild(fxNode);
                fxLayers += built;
            }
        }
        catch (Exception ex)
        {
            notes.Add($"fx overlay fail x{mapX}z{mapZ}: {ex.Message}");
        }

        var minY = cell.Heights.Length > 0 ? cell.Heights[0] : 0f;
        var maxY = minY;
        foreach (var h in cell.Heights)
        {
            if (h < minY) minY = h;
            if (h > maxY) maxY = h;
        }

        try
        {
            var wallNode = MapOverlayBuilder.BuildCollisionWalls(archive, descriptor, minY, maxY + 128f,
                out var segments);
            if (wallNode is not null)
            {
                wallNode.Name = $"Walls_x{mapX}z{mapZ}";
                root.AddChild(wallNode);
                wallSegments += segments;
            }
        }
        catch (Exception ex)
        {
            notes.Add($"wall overlay fail x{mapX}z{mapZ}: {ex.Message}");
        }
    }

    private static string? FindSiblingMap(MappedVfsArchive archive, string tedVfsPath)
    {
        var normalised = tedVfsPath.Replace('\\', '/');
        var changeExt = Path.ChangeExtension(normalised, ".map").Replace('\\', '/');
        return archive.Contains(changeExt) ? changeExt : null;
    }

    private static (Aabb FrameBox, Vector3 Offset) GroundAndCentre(Node3D root, Aabb? terrainBox)
    {
        if (terrainBox is not { } box || box.Size == Vector3.Zero)
            return (new Aabb(), Vector3.Zero);

        var centreX = box.Position.X + box.Size.X * 0.5f;
        var centreZ = box.Position.Z + box.Size.Z * 0.5f;
        var offset = new Vector3(-centreX, -box.Position.Y, -centreZ);
        root.Position = offset;
        return (new Aabb(box.Position + offset, box.Size), offset);
    }

    [GeneratedRegex(@"^data/map(\d+)/", RegexOptions.IgnoreCase)]
    private static partial Regex MapDirRegex();
}