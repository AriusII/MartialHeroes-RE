// World/SkinnedCharacterBuilder.cs
//
// Builds a Godot node tree for a skinned character WITHOUT using GltfDocument.
//
// Two paths, selected at Build time:
//   - SKINNED (skeleton present + ForceSkinned): a faithful CPU linear-blend-skinning node
//     (SkinnedCharacterNode) that rebuilds its ArrayMesh each frame from the sampled idle .mot,
//     reproducing the recovered legacy pipeline exactly. spec: Docs/RE/specs/skinning.md.
//   - STATIC (skeleton null or ForceSkinned == false): a single static ArrayMesh in the rest pose.
//
// BOTH paths apply the SAME single handedness conversion (the world Z-negate) so the static and
// skinned renderings of the same character are oriented identically. There is NO ad-hoc per-asset
// X-flip for skinned characters any more — the spec mandates one uniform conversion applied to
// bones + vertices + keyframes (here: applied once to the final deformed/rest position+normal).
// spec: Docs/RE/specs/skinning.md §8(b); §7 (no axis flip inside the skinning math).
//
// NEVER uses GltfDocument.AppendFromBuffer (it crashes natively on this project's GLBs).

using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Builds a live Godot node from parsed <see cref="SkinnedMesh"/>, <see cref="Skeleton"/>, and
/// <see cref="AnimationClip"/> data without <c>GltfDocument</c>.
///
/// Returns a <see cref="Node3D"/> root the orchestrator positions/scales in world space. The
/// root is pre-recentred so feet are near local Y=0 and the body is centred on X=0, Z=0.
/// </summary>
public static class SkinnedCharacterBuilder
{
    /// <summary>
    /// When <c>false</c>, the skeleton/animation paths are bypassed and the mesh renders as a
    /// static (unskinned) rest surface. Default <c>true</c> (full skinned + animated CPU LBS).
    /// Internal so only this presentation assembly can toggle it (no public mutable global state).
    /// </summary>
    internal static bool ForceSkinned { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, each skinned <see cref="Build"/> prints the mandatory invariant
    /// diagnostics (max rest deviation, AABB, liveness). Default <c>true</c> so headless runs log them.
    /// Internal so only this presentation assembly can toggle it (no public mutable global state).
    /// </summary>
    internal static bool PrintDiagnostics { get; set; } = true;

    /// <summary>
    /// Builds a Godot node tree for a skinned character. Never throws — each step is guarded and
    /// degrades to a visible-but-simpler state on failure. Player-compatible overload.
    /// </summary>
    /// <param name="mesh">Parsed .skn skinned mesh. Must not be null.</param>
    /// <param name="skeleton">Parsed .bnd skeleton, or null (→ static pose).</param>
    /// <param name="clip">Parsed .mot idle clip, or null (→ rest pose, no animation).</param>
    /// <param name="albedo">Optional albedo texture; null → neutral material.</param>
    public static Node3D Build(
        SkinnedMesh mesh,
        Skeleton? skeleton,
        AnimationClip? clip,
        ImageTexture? albedo = null)
        => Build(mesh, skeleton, clip, albedo, externalDrive: false, startPhaseSeconds: 0f,
            out _, debugLabel: null);

    /// <summary>
    /// Builds a Godot node tree for a skinned character, returning the inner
    /// <see cref="SkinnedCharacterNode"/> (when one was created) so a throttling owner can pump it
    /// via <see cref="SkinnedCharacterNode.Tick"/>.
    ///
    /// Orientation is produced by the SINGLE handedness conversion (the world Z-negate) applied
    /// inside the skinning math output — there is NO per-rig "stand-up" reorientation. The original
    /// brings native bone space and rest-mesh space to screen with one conversion and no extra
    /// tallest-axis rotation; any apparent lying-down/standing is the rig's authored rest/idle, which
    /// the faithful pipeline reproduces. spec: Docs/RE/specs/skinning.md §7 / §8(b) / §9.
    /// </summary>
    /// <param name="externalDrive">
    /// When true, the LBS node does not self-tick from <c>_Process</c>; the owner drives it via
    /// <see cref="SkinnedCharacterNode.Tick"/> (used by NpcRenderer's ~10 Hz staggered scheduler).
    /// </param>
    /// <param name="startPhaseSeconds">Initial clip phase offset so actors don't move in lockstep.</param>
    /// <param name="lbsNode">Out: the inner LBS node, or null if the static path was taken.</param>
    /// <param name="debugLabel">Optional label printed with the per-rig pivot decision.</param>
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

        bool useSkinning = ForceSkinned
                           && skeleton is not null
                           && skeleton.Bones.Length > 0
                           && mesh.Weights.Length > 0;

        // A pivot node sits between the root and the mesh child. Its basis is IDENTITY: the original
        // applies exactly ONE handedness conversion (the world Z-negate, done inside the skinning
        // math output) and NO per-rig reorientation. The earlier tallest-axis "stand-up" rotation was
        // a fabricated correction with no IDA counterpart and has been removed (§7/§8(b)).
        // spec: Docs/RE/specs/skinning.md §7 (no axis flip inside skinning) / §8(b) (single Z-negate).
        var pivot = new Node3D { Name = "Pivot" };
        root.AddChild(pivot);

        if (useSkinning)
        {
            try
            {
                var lbs = new SkinnedCharacterNode { Name = "Lbs" };
                lbs.Setup(mesh, skeleton!, clip, albedo, externalDrive, startPhaseSeconds);

                if (PrintDiagnostics)
                {
                    SkinnedCharacterNode.SkinDiagnostics d = lbs.BuildDiagnostics(mesh);
                    LogDiagnostics(mesh, skeleton!, clip, d);
                }

                // Recentre from the DISPLAYED animated frame-0 pose (the pose actually on screen),
                // NOT the raw bind/rest pose, so feet sit near local Y=0 and the body centres on X/Z.
                // The pivot basis stays IDENTITY — no per-rig stand-up rotation (§7/§8(b)); the single
                // Z-negate inside the skinning output is the only handedness conversion. If a rig then
                // appears mis-oriented, the cause is the §8(b)/§9 quaternion remap or a missing actor
                // yaw — to be confirmed by the screenshot oracle, NOT a guessed pivot angle.
                // spec: Docs/RE/specs/skinning.md §6 (displayed pose is the sampled idle) / §7 / §8(b).
                Aabb displayedAabb = lbs.GetDisplayedFrame0Aabb();

                pivot.AddChild(lbs);
                RecentreRoot(root, displayedAabb);
                lbsNode = lbs;
                return root;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Skinning] CPU LBS build failed for '{mesh.Name}': {ex.Message} — " +
                            "falling back to static rest pose.");
                lbsNode = null;
                foreach (Node c in pivot.GetChildren())
                {
                    pivot.RemoveChild(c);
                    c.QueueFree();
                }
            }
        }

        // ---- Static path (no skeleton, ForceSkinned off, or LBS failure) ----
        try
        {
            (MeshInstance3D inst, Aabb aabb) = BuildStaticMesh(mesh, albedo);
            // Identity pivot (no stand-up rotation) — same single Z-negate convention as the skinned
            // path. spec: Docs/RE/specs/skinning.md §7 / §8(b).
            pivot.AddChild(inst);
            RecentreRoot(root, aabb);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Skinning] Static mesh build failed for '{mesh.Name}': {ex.Message}");
        }

        return root;
    }

    // -------------------------------------------------------------------------
    // Static rest-pose ArrayMesh (uses the SAME unified handedness conversion)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a flat unindexed static rest-pose <see cref="ArrayMesh"/>. Positions and normals use
    /// the single handedness conversion (world Z-negate), identical to the skinned path's output.
    /// spec: Docs/RE/specs/skinning.md §8(b) — one conversion, applied uniformly.
    /// spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap [0,2,1] for Godot CCW.
    /// </summary>
    private static (MeshInstance3D Inst, Aabb Aabb) BuildStaticMesh(SkinnedMesh skn, ImageTexture? albedo)
    {
        int faceCount = (int)skn.FaceCount;
        int totalVerts = faceCount * 3;
        var positions = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];

        SknCorner[] corners = skn.Corners;
        Vec3[] srcPos = skn.Positions;
        Vec3[] srcNrm = skn.Normals;

        for (int f = 0; f < faceCount; f++)
        {
            int cBase = f * 3;
            int[] order = [cBase + 0, cBase + 2, cBase + 1]; // CW→CCW
            for (int j = 0; j < 3; j++)
            {
                SknCorner corner = corners[order[j]];
                uint vi = corner.VertexIndex;
                if (vi >= (uint)srcPos.Length) vi = 0;

                Vec3 p = srcPos[vi];
                Vec3 n = vi < (uint)srcNrm.Length ? srcNrm[vi] : new Vec3(0f, 1f, 0f);

                var (gx, gy, gz) = WorldCoordinates.SkinToGodot(p.X, p.Y, p.Z);
                var (nx, ny, nz) = WorldCoordinates.SkinToGodot(n.X, n.Y, n.Z);
                positions[cBase + j] = new Vector3(gx, gy, gz);
                normals[cBase + j] = new Vector3(nx, ny, nz).Normalized();
                uvs[cBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // Cel material for static path — same scope as the skinned path (skinned char only).
        // spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
        Material mat;
        if (CelShadeMaterialFactory.CelEnabled)
        {
            try
            {
                mat = CelShadeMaterialFactory.Build(albedo);
            }
            catch
            {
                // Fallback to PBR if shader resource unavailable.
                var std = new StandardMaterial3D
                {
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                if (albedo is not null) std.AlbedoTexture = albedo;
                else std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f, 1f);
                mat = std;
            }
        }
        else
        {
            var std = new StandardMaterial3D
            {
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            if (albedo is not null) std.AlbedoTexture = albedo;
            else std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f, 1f);
            mat = std;
        }

        arrayMesh.SurfaceSetMaterial(0, mat);

        var inst = new MeshInstance3D { Name = $"StaticMesh_{skn.Name}", Mesh = arrayMesh };
        return (inst, arrayMesh.GetAabb());
    }

    // -------------------------------------------------------------------------
    // Recentre
    // -------------------------------------------------------------------------

    private static void RecentreRoot(Node3D root, Aabb aabb)
    {
        if (aabb.Size == Vector3.Zero) return;
        float yShift = -aabb.Position.Y;
        float xShift = -(aabb.Position.X + aabb.Size.X * 0.5f);
        float zShift = -(aabb.Position.Z + aabb.Size.Z * 0.5f);
        root.Position = new Vector3(xShift, yShift, zShift);
    }

    // -------------------------------------------------------------------------
    // Diagnostics logging (one concise [Skinning] summary block)
    // -------------------------------------------------------------------------

    private static void LogDiagnostics(
        SkinnedMesh mesh,
        Skeleton skeleton,
        AnimationClip? clip,
        SkinnedCharacterNode.SkinDiagnostics d)
    {
        bool restOk = d.MaxRestDeviation < 1e-3f;
        bool aabbOk = d.AabbFinite && d.RestAabbSize.Length() > 0.001f && d.RestAabbSize.Length() < 1e4f;
        bool liveOk = clip is null || clip.FrameCount == 0 || d.LivenessDelta > 1e-4f;

        Vector3 sz = d.RestAabbSize;
        Vector3 pos = d.RestAabbPos;

        GD.Print(
            $"[Skinning] '{mesh.Name}' skin={mesh.Positions.Length}v bones={skeleton.Bones.Length} " +
            $"clip={(clip is null ? "none" : $"{clip.Tracks.Length}trk/{clip.FrameCount}f")} | " +
            $"INV1 restDev={d.MaxRestDeviation:E3} ({(restOk ? "PASS" : "FAIL")}) | " +
            $"INV3 AABB pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) size=({sz.X:F2},{sz.Y:F2},{sz.Z:F2}) " +
            $"finite={d.AabbFinite} ({(aabbOk ? "PASS" : "FAIL")}) | " +
            $"INV2 liveDelta={d.LivenessDelta:F4} @v{d.LivenessVertex} " +
            $"t{d.LivenessT0:F2}->{d.LivenessT1:F2} ({(liveOk ? "PASS" : "FAIL")})");
    }
}