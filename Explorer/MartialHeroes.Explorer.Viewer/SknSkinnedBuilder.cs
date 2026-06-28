using Godot;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public sealed record SknBuildInfo(
    string SkeletonPath,
    int BoneCount,
    string ResolveMode,
    int Coverage,
    uint MatchedActorId,
    bool Skinned,
    int IdleMotionId,
    string IdlePath,
    string MotionProvenance,
    int ModelClipCount,
    int RegistryTotal);

public static class SknSkinnedBuilder
{
    public static Node3D Build(
        MappedVfsArchive archive,
        SkinnedMesh mesh,
        ImageTexture? albedo,
        out ViewerSkinnedNode? node,
        out string[] motionPaths)
    {
        return Build(archive, mesh, albedo, out node, out motionPaths, out _);
    }

    public static Node3D Build(
        MappedVfsArchive archive,
        SkinnedMesh mesh,
        ImageTexture? albedo,
        out ViewerSkinnedNode? node,
        out string[] motionPaths,
        out SknBuildInfo info)
    {
        node = null;
        motionPaths = [];

        var root = new Node3D { Name = $"SkinnedChar_{mesh.Name}" };

        var pivot = new Node3D { Name = "Pivot" };
        root.AddChild(pivot);

        var resolution = CharacterAssetResolver.ResolveSkeleton(archive, mesh);
        var skeleton = resolution.Skeleton;

        info = new SknBuildInfo(resolution.Path, skeleton?.Bones.Length ?? 0, resolution.Mode,
            resolution.Coverage, resolution.MatchedActorId, false, 0, string.Empty, "none", 0, 0);

        GD.Print($"[Viewer] Skeleton resolve for '{mesh.Name}' (id_a={mesh.IdA} id_b={mesh.IdB}): " +
                 $"mode={resolution.Mode} path={resolution.Path} actorId={resolution.MatchedActorId} " +
                 $"bones={skeleton?.Bones.Length ?? 0} coverage={resolution.Coverage}.");

        var useSkinning = skeleton is not null
                          && skeleton.Bones.Length > 0
                          && mesh.Weights.Length > 0;

        if (useSkinning)
            try
            {
                var (lbs, displayedAabb) = TryBuildLbsPart(mesh, skeleton!, null, albedo, mesh.Name);
                if (lbs is not null)
                {
                    pivot.AddChild(lbs);
                    RecentreRoot(root, TransformAabb(pivot.Transform.Basis, displayedAabb));

                    node = lbs;
                    var motion = CharacterAssetResolver.ResolveMotionPaths(archive, skeleton, mesh);
                    motionPaths = motion.Paths;
                    info = info with
                    {
                        Skinned = true,
                        IdleMotionId = motion.IdleMotionId,
                        IdlePath = motion.IdlePath ?? string.Empty,
                        MotionProvenance = motion.Provenance,
                        ModelClipCount = motion.ModelClipCount,
                        RegistryTotal = motion.RegistryTotal
                    };

                    GD.Print($"[Viewer] Skinned build OK: '{mesh.Name}' bones={skeleton!.Bones.Length} " +
                             $"idle={motion.IdleMotionId}->{Path.GetFileName(motion.IdlePath ?? "none")} " +
                             $"prov={motion.Provenance} modelClips={motion.ModelClipCount} " +
                             $"registry={motion.RegistryTotal}");
                    return root;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Viewer] Skinned build failed for '{mesh.Name}': {ex.Message} — " +
                            "falling back to static.");
                node = null;
                foreach (var c in pivot.GetChildren())
                {
                    pivot.RemoveChild(c);
                    c.QueueFree();
                }
            }

        try
        {
            pivot.RotationDegrees = new Vector3(0f, 0f, 90f);
            var (inst, aabb) = BuildStaticPart(mesh, albedo);
            pivot.AddChild(inst);
            RecentreRoot(root, TransformAabb(pivot.Transform.Basis, aabb));
            GD.Print($"[Viewer] Static fallback mesh built for '{mesh.Name}'.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Viewer] Static mesh build failed for '{mesh.Name}': {ex.Message}");
        }

        return root;
    }

    internal static (ViewerSkinnedNode? Node, Aabb DisplayedAabb) TryBuildLbsPart(
        SkinnedMesh mesh, Skeleton skeleton, AnimationClip? clip, ImageTexture? albedo, string label)
    {
        var lbs = new ViewerSkinnedNode { Name = $"Lbs_{label}" };
        lbs.Setup(mesh, skeleton, clip, albedo);

        var diag = lbs.BuildDiagnostics(mesh);
        var restOk = diag.MaxRestDeviation < 1e-3f;
        var sizeLen = diag.RestAabbSize.Length();
        var aabbOk = diag.AabbFinite && sizeLen > 0.001f && sizeLen < 1e4f;
        var pass = restOk && aabbOk;
        var sz = diag.RestAabbSize;

        GD.Print($"[Viewer] Skin diag '{label}': skin={mesh.Positions.Length}v " +
                 $"bones={skeleton.Bones.Length} restDev={diag.MaxRestDeviation:E3} " +
                 $"({(restOk ? "PASS" : "FAIL")}) AABB pos=({diag.RestAabbPos.X:F2}," +
                 $"{diag.RestAabbPos.Y:F2},{diag.RestAabbPos.Z:F2}) " +
                 $"size=({sz.X:F2},{sz.Y:F2},{sz.Z:F2}) finite={diag.AabbFinite} " +
                 $"({(aabbOk ? "PASS" : "FAIL")}) => {(pass ? "SKINNED" : "STATIC-FALLBACK")}.");

        if (pass)
            return (lbs, lbs.GetMeshAabb());

        lbs.QueueFree();
        return (null, default);
    }

    internal static (MeshInstance3D Inst, Aabb Aabb) BuildStaticPart(SkinnedMesh skn, ImageTexture? albedo)
    {
        var faceCount = (int)skn.FaceCount;
        var totalVerts = faceCount * 3;
        var positions = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];

        var corners = skn.Corners;
        var srcPos = skn.Positions;
        var srcNrm = skn.Normals;

        for (var f = 0; f < faceCount; f++)
        {
            var cBase = f * 3;
            int[] order = [cBase + 0, cBase + 2, cBase + 1];
            for (var j = 0; j < 3; j++)
            {
                var corner = corners[order[j]];
                var vi = corner.VertexIndex;
                if (vi >= (uint)srcPos.Length) vi = 0;

                var p = srcPos[vi];
                var n = vi < (uint)srcNrm.Length ? srcNrm[vi] : new Vec3(0f, 1f, 0f);

                var (gx, gy, gz) = WorldCoordinates.SkinToGodot(p.X, p.Y, p.Z);
                var (nx, ny, nz) = WorldCoordinates.SkinToGodot(n.X, n.Y, n.Z);
                positions[cBase + j] = new Vector3(gx, gy, gz);
                normals[cBase + j] = new Vector3(nx, ny, nz).Normalized();
                uvs[cBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var std = new StandardMaterial3D
        {
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
        if (albedo is not null)
            std.AlbedoTexture = albedo;
        else
            std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);

        arrayMesh.SurfaceSetMaterial(0, std);

        var inst = new MeshInstance3D { Name = $"StaticMesh_{skn.Name}", Mesh = arrayMesh };
        return (inst, arrayMesh.GetAabb());
    }

    internal static void RecentreRoot(Node3D root, Aabb aabb)
    {
        if (aabb.Size == Vector3.Zero) return;
        var yShift = -aabb.Position.Y;
        var xShift = -(aabb.Position.X + aabb.Size.X * 0.5f);
        var zShift = -(aabb.Position.Z + aabb.Size.Z * 0.5f);
        root.Position = new Vector3(xShift, yShift, zShift);
    }

    internal static Aabb TransformAabb(Basis basis, Aabb aabb)
    {
        var min = basis * aabb.Position;
        var max = min;
        for (var i = 1; i < 8; i++)
        {
            var corner = new Vector3(
                aabb.Position.X + ((i & 1) != 0 ? aabb.Size.X : 0f),
                aabb.Position.Y + ((i & 2) != 0 ? aabb.Size.Y : 0f),
                aabb.Position.Z + ((i & 4) != 0 ? aabb.Size.Z : 0f));
            var t = basis * corner;
            min = min.Min(t);
            max = max.Max(t);
        }

        return new Aabb(min, max - min);
    }
}