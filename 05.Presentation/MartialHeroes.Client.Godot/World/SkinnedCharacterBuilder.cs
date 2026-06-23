using Godot;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public static class SkinnedCharacterBuilder
{
    internal static bool ForceSkinned { get; set; } = true;

    internal static bool PrintDiagnostics { get; set; } = true;

    internal static Vector3 UpAxisRemapDeg { get; set; } = new(0f, 0f, 90f);

    public static Node3D Build(
        SkinnedMesh mesh,
        Skeleton? skeleton,
        AnimationClip? clip,
        ImageTexture? albedo = null)
    {
        return Build(mesh, skeleton, clip, albedo, false, 0f,
            out _, null);
    }

    public static Node3D Build(
        SkinnedMesh mesh,
        Skeleton? skeleton,
        AnimationClip? clip,
        ImageTexture? albedo,
        bool externalDrive,
        float startPhaseSeconds,
        out SkinnedCharacterNode? lbsNode,
        string? debugLabel)
    {
        lbsNode = null;
        var root = new Node3D { Name = $"SkinnedChar_{mesh.Name}" };

        var useSkinning = ForceSkinned
                          && skeleton is not null
                          && skeleton.Bones.Length > 0
                          && mesh.Weights.Length > 0;

        var pivot = new Node3D { Name = "Pivot", RotationDegrees = UpAxisRemapDeg };
        root.AddChild(pivot);

        if (useSkinning)
            try
            {
                var lbs = new SkinnedCharacterNode { Name = "Lbs" };
                lbs.Setup(mesh, skeleton!, clip, albedo, externalDrive, startPhaseSeconds);

                if (PrintDiagnostics)
                {
                    var d = lbs.BuildDiagnostics(mesh);
                    LogDiagnostics(mesh, skeleton!, clip, d);
                }

                var displayedAabb = lbs.GetMeshAabb();

                if (PrintDiagnostics)
                {
                    var after = TransformAabb(pivot.Transform.Basis, displayedAabb);
                    Vector3 b = displayedAabb.Size, a = after.Size;
                    GD.Print(
                        $"[Skinning] '{mesh.Name}' UPRIGHT remap {UpAxisRemapDeg}: " +
                        $"BEFORE size=({b.X:F2},{b.Y:F2},{b.Z:F2}) tall={TallAxis(b)} -> " +
                        $"AFTER size=({a.X:F2},{a.Y:F2},{a.Z:F2}) tall={TallAxis(a)}.");
                }

                pivot.AddChild(lbs);
                RecentreRoot(root, TransformAabb(pivot.Transform.Basis, displayedAabb));
                lbsNode = lbs;
                return root;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Skinning] CPU LBS build failed for '{mesh.Name}': {ex.Message} — " +
                            "falling back to static rest pose.");
                lbsNode = null;
                foreach (var c in pivot.GetChildren())
                {
                    pivot.RemoveChild(c);
                    c.QueueFree();
                }
            }

        try
        {
            var (inst, aabb) = BuildStaticMesh(mesh, albedo);
            pivot.AddChild(inst);
            RecentreRoot(root, TransformAabb(pivot.Transform.Basis, aabb));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Skinning] Static mesh build failed for '{mesh.Name}': {ex.Message}");
        }

        return root;
    }

    public static Node3D BuildWithEquipment(
        SkinnedMesh mesh,
        Skeleton? skeleton,
        AnimationClip? clip,
        ImageTexture? albedo,
        bool externalDrive,
        float startPhaseSeconds,
        IReadOnlyList<EquipmentVisualPart> parts,
        out SkinnedCharacterNode? lbsNode,
        string? debugLabel)
    {
        var root = Build(mesh, skeleton, clip, albedo, externalDrive, startPhaseSeconds,
            out lbsNode, debugLabel);

        if (lbsNode is null || parts.Count == 0)
            return root;

        lbsNode.ClearOverlayParts();
        lbsNode.ClearWeapons();

        foreach (var part in parts)
            if (part.IsHandWeapon)
                try
                {
                    lbsNode.AttachHandWeapon(part.Mesh, part.Albedo, part.BoneId, part.VisualScale,
                        part.IsOffHand);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[Skinning] Weapon attach failed (slot {part.Slot}, " +
                                $"'{part.Mesh.Name}'): {ex.Message}");
                }
            else
                try
                {
                    lbsNode.AttachDeformPart(part.Mesh, part.Albedo, $"slot{part.Slot}:{part.Mesh.Name}");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[Skinning] Overlay deform part attach failed (slot {part.Slot}, " +
                                $"'{part.Mesh.Name}'): {ex.Message}");
                }

        return root;
    }

    public static (MeshInstance3D Inst, Aabb Aabb) BuildStaticRigidMesh(
        SkinnedMesh mesh, ImageTexture? albedo, string nodeName)
    {
        var (inst, aabb) = BuildStaticMesh(mesh, albedo);
        inst.Name = nodeName;
        return (inst, aabb);
    }


    private static (MeshInstance3D Inst, Aabb Aabb) BuildStaticMesh(SkinnedMesh skn, ImageTexture? albedo)
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

        Material mat;
        if (CelShadeMaterialFactory.CelEnabled)
        {
            try
            {
                mat = CelShadeMaterialFactory.Build(albedo);
            }
            catch
            {
                var std = new StandardMaterial3D
                {
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                };
                if (albedo is not null) std.AlbedoTexture = albedo;
                else std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);
                mat = std;
            }
        }
        else
        {
            var std = new StandardMaterial3D
            {
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            if (albedo is not null) std.AlbedoTexture = albedo;
            else std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);
            mat = std;
        }

        arrayMesh.SurfaceSetMaterial(0, mat);

        var inst = new MeshInstance3D { Name = $"StaticMesh_{skn.Name}", Mesh = arrayMesh };
        return (inst, arrayMesh.GetAabb());
    }


    private static string TallAxis(Vector3 s)
    {
        return s.Y >= s.X && s.Y >= s.Z ? "Y" : s.X >= s.Z ? "X" : "Z";
    }

    private static Aabb TransformAabb(Basis basis, Aabb aabb)
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

    private static void RecentreRoot(Node3D root, Aabb aabb)
    {
        if (aabb.Size == Vector3.Zero) return;
        var yShift = -aabb.Position.Y;
        var xShift = -(aabb.Position.X + aabb.Size.X * 0.5f);
        var zShift = -(aabb.Position.Z + aabb.Size.Z * 0.5f);
        root.Position = new Vector3(xShift, yShift, zShift);
    }


    private static void LogDiagnostics(
        SkinnedMesh mesh,
        Skeleton skeleton,
        AnimationClip? clip,
        SkinnedCharacterNode.SkinDiagnostics d)
    {
        var restOk = d.MaxRestDeviation < 1e-3f;
        var aabbOk = d.AabbFinite && d.RestAabbSize.Length() > 0.001f && d.RestAabbSize.Length() < 1e4f;
        var liveOk = clip is null || clip.FrameCount == 0 || d.LivenessDelta > 1e-4f;

        var sz = d.RestAabbSize;
        var pos = d.RestAabbPos;

        GD.Print(
            $"[Skinning] '{mesh.Name}' skin={mesh.Positions.Length}v bones={skeleton.Bones.Length} " +
            $"clip={(clip is null ? "none" : $"{clip.Tracks.Length}trk/{clip.FrameCount}f")} | " +
            $"INV1 restDev={d.MaxRestDeviation:E3} ({(restOk ? "PASS" : "FAIL")}) | " +
            $"INV3 AABB pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) size=({sz.X:F2},{sz.Y:F2},{sz.Z:F2}) " +
            $"finite={d.AabbFinite} ({(aabbOk ? "PASS" : "FAIL")}) | " +
            $"INV2 liveDelta={d.LivenessDelta:F4} @v{d.LivenessVertex} " +
            $"t{d.LivenessT0:F2}->{d.LivenessT1:F2} ({(liveOk ? "PASS" : "FAIL")})");
    }

    public readonly record struct EquipmentVisualPart(
        int Slot,
        SkinnedMesh Mesh,
        ImageTexture? Albedo,
        bool IsHandWeapon,
        bool IsOffHand,
        int BoneId,
        float VisualScale);
}