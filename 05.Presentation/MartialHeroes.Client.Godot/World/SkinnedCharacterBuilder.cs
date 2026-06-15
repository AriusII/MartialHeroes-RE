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
    /// The stand-up pivot is derived PER-RIG from the rest AABB's tallest axis (a standing humanoid
    /// is tallest on Y after the pure Z-negate); the g1 player's +90°-about-Z pivot is no longer
    /// hardcoded — it falls out of this heuristic for that rig and is computed independently for
    /// every mob rig. spec: Docs/RE/specs/skinning.md §8(b)/§9 — exact up-axis is empirical per rig.
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

        // A rigid "stand-up" pivot sits between the root and the mesh child. The skinning math and
        // the single handedness conversion stay PURE (Z-negate only, spec §7/§8(b)); the pivot is a
        // rigid isometry applied AFTER all skinning, so it cannot break the skin↔skeleton
        // consistency or the rest-pose cancellation. It only re-orients the whole actor so its
        // authored "up" axis points along Godot +Y. The basis is derived PER RIG below from the
        // rest AABB tallest axis; we start at identity and replace it once the AABB is known.
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

                // Derive the stand-up basis from THIS rig's rest AABB (not g1's hardcoded value).
                Aabb restAabb = lbs.GetMeshAabb();
                pivot.Basis = DeriveStandUpBasis(restAabb, debugLabel ?? mesh.Name);

                pivot.AddChild(lbs);
                RecentreRoot(root, new Transform3D(pivot.Basis, Vector3.Zero) * restAabb);
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
            // Derive the stand-up basis from the static rest AABB too, so a fallback mob still
            // stands upright (same tallest-axis heuristic as the skinned path).
            pivot.Basis = DeriveStandUpBasis(aabb, debugLabel ?? mesh.Name);
            pivot.AddChild(inst);
            RecentreRoot(root, new Transform3D(pivot.Basis, Vector3.Zero) * aabb);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Skinning] Static mesh build failed for '{mesh.Name}': {ex.Message}");
        }

        return root;
    }

    // -------------------------------------------------------------------------
    // Stand-up orientation — derived PER RIG from the rest AABB's tallest axis
    // -------------------------------------------------------------------------
    //
    // The single handedness conversion (world Z-negate) brings the rig into Godot space with skin
    // and skeleton perfectly consistent. spec: Docs/RE/specs/skinning.md §8(b).
    //
    // VERIFIED (CAMPAIGN 9 L6) on the canonical §8(d) specimen trios: after the pure Z-negate BOTH
    // the g1 player rig (rest AABB Y=7.20 > X=5.02 > Z=1.75) and the g2048 mob rig (Y=7.84 > X=7.38 >
    // Z=1.49) are already TALLEST ON Y, so this heuristic returns IDENTITY for both and the rigs stand
    // upright with no extra rotation — the Z-negate alone is sufficient (a windowed screenshot of the
    // trio shows two coherent, upright humanoids, NOT an exploded star of triangles). The earlier note
    // that g1 was X-tallest (~5.0 X vs ~2.4 Y) reflected an OLD pre-unification convention and no longer
    // holds under the single Z-negate; the +90°-about-Z branch is therefore now a DORMANT defensive
    // fallback, not the live path for any recovered rig.
    //
    // The branch is retained only as a safety net for a hypothetical malformed rig authored with its
    // head-to-foot axis off +Y: when X is the tallest axis it maps local +X → +Y (+90° about Z); when
    // Y is tallest (the verified humanoid case) the basis is identity; when Z is tallest a −90°-about-X
    // maps local +Z → +Y. Behaviour is unchanged from the prior implementation, so the FROZEN World
    // scene cannot regress.
    //
    // The pivot is a rigid isometry applied AFTER all skinning, so it can never break the
    // skin↔skeleton consistency or the rest-pose cancellation.
    //
    // spec: Docs/RE/specs/skinning.md §8(b)/§9 — the exact remap is to be VALIDATED against a real
    //       bone/rig rather than assumed; here it is derived empirically per rig from the rest AABB.
    private static Basis DeriveStandUpBasis(Aabb restAabb, string debugLabel)
    {
        Vector3 s = restAabb.Size;
        // Pick the tallest axis: 0 = X, 1 = Y, 2 = Z.
        int tallest = 0;
        float best = s.X;
        if (s.Y > best)
        {
            best = s.Y;
            tallest = 1;
        }

        if (s.Z > best)
        {
            tallest = 2;
        }

        Basis basis = tallest switch
        {
            // X tallest → rotate local +X onto +Y: +90° about Z (g1 player rig case).
            0 => new Basis(new Vector3(0, 0, 1), Mathf.Pi / 2f),
            // Y tallest → already upright: identity.
            1 => Basis.Identity,
            // Z tallest → rotate local +Z onto +Y: −90° about X.
            _ => new Basis(new Vector3(1, 0, 0), -Mathf.Pi / 2f),
        };

        if (PrintDiagnostics)
        {
            string axisName = tallest == 0 ? "X(+90°Z)" : tallest == 1 ? "Y(identity)" : "Z(−90°X)";
            GD.Print($"[Skinning] pivot '{debugLabel}': rest AABB size=" +
                     $"({s.X:F2},{s.Y:F2},{s.Z:F2}) → tallest axis {axisName} → stand upright.");
        }

        return basis;
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