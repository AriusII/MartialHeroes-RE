using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Explorer.Viewer;

public partial class ViewerRoot
{
    private void SetInspector(string text)
    {
        if (_inspector is not null)
            _inspector.Text = text;
    }

    private string BuildSknInspector(SkinnedMesh mesh, ViewerTextures.Resolved t, SknBuildInfo info)
    {
        var texStr = t.Path ?? "none";
        if (t.Path is null) texStr += $"  ({t.Note})";
        var modeDesc = info.ResolveMode switch
        {
            "id_b-verbatim" => "id_b-verbatim — matched .bnd ActorId == skin IdB",
            "none" => "none — no skeleton matched IdB, showing static rest",
            _ => info.ResolveMode
        };

        var idleName = info.IdlePath.Length > 0 ? Path.GetFileName(info.IdlePath) : "unresolved";
        var idleStr = info.IdleMotionId > 0
            ? $"{info.IdleMotionId} -> {idleName}  ({info.MotionProvenance})"
            : $"none  ({info.MotionProvenance})";
        var clipsStr = info.RegistryTotal > 0
            ? $"{info.ModelClipCount} of {info.RegistryTotal} registry clips"
            : $"{_currentMotionPaths.Length}";

        return $"[b]IdA:[/b] {mesh.IdA}  [b]IdB:[/b] {mesh.IdB}\n" +
               $"[b]Vertices:[/b] {mesh.Positions.Length}  [b]Faces:[/b] {mesh.FaceCount}  [b]Weights:[/b] {mesh.Weights.Length}\n" +
               $"[b]Skeleton:[/b] {info.SkeletonPath}\n" +
               $"[b]Bones:[/b] {info.BoneCount}  [b]Coverage:[/b] {info.Coverage}\n" +
               $"[b]Skeleton resolution:[/b] {modeDesc}\n" +
               $"[b]Skinned:[/b] {info.Skinned}  [b]ActorId:[/b] {info.MatchedActorId}\n" +
               $"[b]Texture:[/b] {texStr}\n" +
               $"[b]Idle:[/b] {idleStr}\n" +
               $"[b]Model clips:[/b] {clipsStr}";
    }

    private static string BuildXobjInspector(StaticMesh mesh)
    {
        return $"[b]Vertices:[/b] {mesh.Positions.Length}  [b]Triangles:[/b] {mesh.Indices.Length / 3}\n" +
               "[b]Texture:[/b] none (StaticMesh carries no texture reference)";
    }

    private static string BuildBudInspector(BudScene scene)
    {
        var total = 0;
        foreach (var obj in scene.Objects) total += obj.Vertices.Length;
        return $"[b]Objects:[/b] {scene.Objects.Length}  [b]Total Vertices:[/b] {total}\n" +
               "[b]Textures:[/b] best-effort via global map000 pool";
    }

    private static string BuildTedInspector(TerrainCell cell)
    {
        return $"[b]Grid:[/b] {TerrainCell.GridSize}x{TerrainCell.GridSize}  [b]Vertices:[/b] {cell.Heights.Length}\n" +
               "[b]Terrain Texture:[/b] none (check console for resolved path)";
    }

    private static string BuildMapInspector(MapAssemblyInfo info)
    {
        var allNotes = info.Notes.Count > 0
            ? string.Join("\n", info.Notes.Select(n => $"  • {n}"))
            : "  (none)";
        var fs = info.FrameBox.Size;
        return $"[b]Map:[/b] {info.MapId}\n" +
               $"[b]Cells:[/b] {info.CellsBuilt} built / {info.CellsFound} found\n" +
               $"[b]Buildings:[/b] {info.BudScenesBuilt} scenes  [b]Objects:[/b] {info.BudObjects}\n" +
               $"[b]Extent:[/b] X={info.WorldExtent.X:F0}  Z={info.WorldExtent.Z:F0}  Y={info.WorldExtent.Y:F0}\n" +
               $"[b]Frame box:[/b] {fs.X:F0} × {fs.Z:F0} × {fs.Y:F0}\n" +
               $"[b]Notes ({info.Notes.Count}):[/b]\n{allNotes}";
    }

    private static string BuildAssemblyInspector(AssemblyInfo info)
    {
        var className = info.InternalClass >= 1 && info.InternalClass <= 4
            ? CharacterAssembler.Classes[info.InternalClass - 1].Name
            : "?";
        var idleName = info.IdlePath.Length > 0 ? Path.GetFileName(info.IdlePath) : "unresolved";
        var idleStr = info.IdleMotionId > 0
            ? $"{info.IdleMotionId} -> {idleName}  ({info.MotionProvenance})"
            : $"none  ({info.MotionProvenance})";

        var slots = info.SlotSummaries.Count > 0
            ? string.Join("\n", info.SlotSummaries.Select(s => $"  • {s}"))
            : "  (none)";

        return $"[b]Class:[/b] {info.InternalClass} {className}  [b]Variant:[/b] {info.Variant}\n" +
               $"[b]model_class_id:[/b] {info.ModelClassId}\n" +
               $"[b]Skeleton:[/b] {info.SkeletonPath}\n" +
               $"[b]Bones:[/b] {info.BoneCount}  [b]ActorId:[/b] {info.MatchedActorId}\n" +
               $"[b]Parts:[/b] {info.PartsResolved} resolved, {info.SkinnedParts} skinned\n" +
               $"[b]Idle:[/b] {idleStr}\n" +
               $"[b]Slots:[/b]\n{slots}";
    }
}