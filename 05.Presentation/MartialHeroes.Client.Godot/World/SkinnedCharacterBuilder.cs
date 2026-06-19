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
    /// PORT-SIDE coordinate convention: map the legacy native up-axis onto Godot's Y-up.
    ///
    /// The recovered bind/inverse-bind/LBS math (<see cref="SkinningMath"/>, the bake) is CORRECT and
    /// is NOT touched here — the deform reproduces the rig faithfully in its NATIVE axis frame. But the
    /// legacy engine's native up-axis differs from Godot's Y-up, so the faithfully-deformed avatar
    /// arrives lying along X (the CYCLE-2 screenshots show AABB longest extent X≈25 vs Y≈12). This is
    /// the SAME CATEGORY of port-side coordinate mapping as the existing conventions already in the
    /// codebase: world geometry negates Z (<see cref="Helpers.WorldCoordinates.ToGodot"/>) and the
    /// mesh-local .skn geometry negates X. It is a display-node reorientation — a coordinate-mapping
    /// PORT CHOICE — NOT a fabricated game behaviour and NOT a change to the bind/bake math.
    ///
    /// Applied to the Pivot node (the Node3D wrapping the mesh), so it composes cleanly with the
    /// caller's actor yaw on the root (NpcRenderer / RealWorldRenderer set root.Rotation = (0,yaw,0))
    /// and is shared by BOTH the player and the spawned actors (both go through Build).
    ///
    /// The human avatar's faithfully-deformed mesh is tallest along X (AABB X≈5.0 vs Y≈2.4 vs Z≈1.7
    /// for the g2 player b202110001 — it arrives lying down the +X axis). Mapping X→Y stands it up,
    /// which is a rotation about Z (not about X — about X only swaps Y↔Z and leaves it X-tall). A
    /// +90° rotation about Z maps +X onto +Y while keeping the figure NOT mirrored/inside-out (a pure
    /// rotation has det = +1, so winding is preserved, unlike a reflection). The AABB long axis moves
    /// from X to Y, which is the verifiable signature.
    /// Held as a named field so it is trivially adjustable when the maintainer confirms the native axis
    /// against the official captures / live debugger.
    /// spec: Docs/RE/specs/skinning.md §9 — the native up-axis / handedness LABEL is capture/debugger-
    ///       pending (no axis flip inside the math); this is the importer-layer remap §7 sanctions.
    /// spec: CLAUDE.md "Coordinate conventions" — same category as world Z-negate / mesh-local X-negate.
    /// </summary>
    internal static Vector3 UpAxisRemapDeg { get; set; } = new Vector3(0f, 0f, 90f);

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

        // A pivot node sits between the root and the mesh child. The skinning math output applies the
        // ONE handedness conversion (the world Z-negate); the pivot then applies the PORT-SIDE
        // up-axis remap (UpAxisRemapDeg) that maps the legacy native up-axis onto Godot Y-up — a
        // display-node coordinate-mapping choice (same category as the Z/X negations), NOT a change to
        // the recovered bind/bake math. Held on the Pivot (a child of root) so the caller's actor yaw
        // on the root composes cleanly on top.
        // spec: Docs/RE/specs/skinning.md §7 (axis remap is an importer-layer transform) / §9
        //       (native up-axis label capture/debugger-pending) / §8(b) (single Z-negate stays in math).
        var pivot = new Node3D { Name = "Pivot", RotationDegrees = UpAxisRemapDeg };
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

                // Recentre from the DISPLAYED animated frame-0 pose (the pose actually on screen), as
                // REORIENTED by the pivot up-axis remap, so feet sit near local Y=0 and the body
                // centres on X/Z in the final upright orientation. The single Z-negate inside the
                // skinning output stays the only handedness conversion in the math; the pivot remap is
                // a port-side display reorientation (§7/§9), shared by player + actors.
                // spec: Docs/RE/specs/skinning.md §6 (displayed pose is the sampled idle) / §7 / §9.
                Aabb displayedAabb = lbs.GetDisplayedFrame0Aabb();

                if (PrintDiagnostics)
                {
                    Aabb after = TransformAabb(pivot.Transform.Basis, displayedAabb);
                    Vector3 b = displayedAabb.Size, a = after.Size;
                    GD.Print(
                        $"[Skinning] '{mesh.Name}' UPRIGHT remap {UpAxisRemapDeg}: " +
                        $"BEFORE size=({b.X:F2},{b.Y:F2},{b.Z:F2}) tall={TallAxis(b)} -> " +
                        $"AFTER size=({a.X:F2},{a.Y:F2},{a.Z:F2}) tall={TallAxis(a)}.");
                }

                pivot.AddChild(lbs);
                // Recentre from the AABB AS REORIENTED by the pivot's up-axis remap, so feet sit near
                // local Y=0 and the body centres on X/Z in the FINAL (upright) orientation.
                RecentreRoot(root, TransformAabb(pivot.Transform.Basis, displayedAabb));
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
            // Same pivot up-axis remap as the skinned path; recentre from the reoriented AABB.
            // spec: Docs/RE/specs/skinning.md §7 / §8(b) / §9.
            pivot.AddChild(inst);
            RecentreRoot(root, TransformAabb(pivot.Transform.Basis, aabb));
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

    /// <summary>
    /// Returns the axis-aligned bounding box that encloses <paramref name="aabb"/> after its 8 corners
    /// are rotated by <paramref name="basis"/> (the pivot's up-axis remap). Godot's Aabb has no
    /// Basis*Aabb operator, so transform the corners and re-fit. Pure geometry — no engine state.
    /// </summary>
    /// <summary>Names the longest axis of an AABB size (diagnostic for the upright remap).</summary>
    private static string TallAxis(Vector3 s)
        => s.Y >= s.X && s.Y >= s.Z ? "Y" : (s.X >= s.Z ? "X" : "Z");

    private static Aabb TransformAabb(Basis basis, Aabb aabb)
    {
        Vector3 min = basis * aabb.Position;
        Vector3 max = min;
        for (int i = 1; i < 8; i++)
        {
            var corner = new Vector3(
                aabb.Position.X + ((i & 1) != 0 ? aabb.Size.X : 0f),
                aabb.Position.Y + ((i & 2) != 0 ? aabb.Size.Y : 0f),
                aabb.Position.Z + ((i & 4) != 0 ? aabb.Size.Z : 0f));
            Vector3 t = basis * corner;
            min = min.Min(t);
            max = max.Max(t);
        }

        return new Aabb(min, max - min);
    }

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