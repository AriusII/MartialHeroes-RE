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
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Builds a live Godot node from parsed <see cref="SkinnedMesh" />, <see cref="Skeleton" />, and
///     <see cref="AnimationClip" /> data without <c>GltfDocument</c>.
///     Returns a <see cref="Node3D" /> root the orchestrator positions/scales in world space. The
///     root is pre-recentred so feet are near local Y=0 and the body is centred on X=0, Z=0.
/// </summary>
public static class SkinnedCharacterBuilder
{
    /// <summary>
    ///     When <c>false</c>, the skeleton/animation paths are bypassed and the mesh renders as a
    ///     static (unskinned) rest surface. Default <c>true</c> (full skinned + animated CPU LBS).
    ///     Internal so only this presentation assembly can toggle it (no public mutable global state).
    /// </summary>
    internal static bool ForceSkinned { get; set; } = true;

    /// <summary>
    ///     When <c>true</c>, each skinned <see cref="Build" /> prints the mandatory invariant
    ///     diagnostics (max rest deviation, AABB, liveness). Default <c>true</c> so headless runs log them.
    ///     Internal so only this presentation assembly can toggle it (no public mutable global state).
    /// </summary>
    internal static bool PrintDiagnostics { get; set; } = true;

    /// <summary>
    ///     PORT-SIDE up-axis remap for the imported character: a +90° rotation about Z that maps the
    ///     native <c>.skn</c> height axis onto Godot +Y.
    ///     <para>
    ///         ORACLE-MEASURED (asset bytes, the visual oracle — Ground-Truth Doctrine: oracle > spec for
    ///         rendered pixels). The raw <c>g202110001.skn</c> (player class 1, <c>id_b = 1</c>) rest mesh
    ///         parsed through the production <see cref="MartialHeroes.Assets.Parsers.Mesh.SknParser" /> has
    ///         extent ≈ (X 5.02, Y 2.44, Z 1.67) — its HEIGHT runs along native <b>X</b>, not Y. Deformed
    ///         through the §0 LBS pipeline with the CORRECT skeleton (<c>g1.bnd</c>, 84 bones) and a matched
    ///         84-track idle, the displayed frame-0 AABB is tall-along-X (≈ 3.73 × 2.56 × 2.11) — the avatar
    ///         is recumbent. The rest-pose cancellation invariant (§8(a)) PASSES (max dev ≈ 1.5e-6), so this
    ///         is a pure ORIENTATION issue, not a deform-math defect. A +90° rotation about Z brings the
    ///         figure tall-along-Y (head at +Y); −90° lays it head-down. Measured by the headless AABB probe.
    ///     </para>
    ///     <para>
    ///         RECONCILIATION with skinning.md §7/§8(b)/§9. The spec currently states the up axis is "Y-up,
    ///         import = IDENTITY", but that is the engine's WORLD-placement / heading convention (yaw is a
    ///         pure rotation about Y), inferred — NOT the per-vertex height axis of the <c>.skn</c> geometry,
    ///         which §7's banner and §9 still flag as the
    ///         <b>
    ///             native up-axis LABEL = capture/debugger-pending
    ///             (raw numeric up-axis of the asset bytes unread)
    ///         </b>
    ///         . The asset bytes settle that pending label:
    ///         the geometry is authored X-tall, so a faithful port needs the up-axis remap that stands it on
    ///         Y. §8(b)'s "remove the spurious −90°-about-X" guidance assumed the recumbency came from a
    ///         port-ADDED rotation; here the recumbency is intrinsic to the data with the pivot at identity,
    ///         so the correct action is to ADD the measured stand-up remap, not remove one. The "−Z world /
    ///         −X mesh-local" handedness flips still leave the up axis alone (§8(b)); this remap is the
    ///         separate, importer-layer up-axis convention.
    ///     </para>
    ///     Held as a named field so an axis-label refinement (or a per-rig variant) is trivially adjustable.
    ///     spec: Docs/RE/specs/skinning.md §9 (native up-axis LABEL = capture/debugger-pending) / §8(b)
    ///     (up-axis remap is an importer-layer transform; verify tall-along-Y AABB) / §8(a)
    ///     (cancellation preserved — orientation knob is the pivot only, not the deform math).
    /// </summary>
    internal static Vector3 UpAxisRemapDeg { get; set; } = new(0f, 0f, 90f);

    /// <summary>
    ///     Builds a Godot node tree for a skinned character. Never throws — each step is guarded and
    ///     degrades to a visible-but-simpler state on failure. Player-compatible overload.
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
    {
        return Build(mesh, skeleton, clip, albedo, false, 0f,
            out _, null);
    }

    /// <summary>
    ///     Builds a Godot node tree for a skinned character, returning the inner
    ///     <see cref="SkinnedCharacterNode" /> (when one was created) so a throttling owner can pump it
    ///     via <see cref="SkinnedCharacterNode.Tick" />.
    ///     Orientation is produced by the SINGLE handedness conversion (the world Z-negate) applied
    ///     inside the skinning math output — there is NO per-rig "stand-up" reorientation. The original
    ///     brings native bone space and rest-mesh space to screen with one conversion and no extra
    ///     tallest-axis rotation; any apparent lying-down/standing is the rig's authored rest/idle, which
    ///     the faithful pipeline reproduces. spec: Docs/RE/specs/skinning.md §7 / §8(b) / §9.
    /// </summary>
    /// <param name="externalDrive">
    ///     When true, the LBS node does not self-tick from <c>_Process</c>; the owner drives it via
    ///     <see cref="SkinnedCharacterNode.Tick" /> (used by NpcRenderer's ~10 Hz staggered scheduler).
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

        var useSkinning = ForceSkinned
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
            try
            {
                var lbs = new SkinnedCharacterNode { Name = "Lbs" };
                lbs.Setup(mesh, skeleton!, clip, albedo, externalDrive, startPhaseSeconds);

                if (PrintDiagnostics)
                {
                    var d = lbs.BuildDiagnostics(mesh);
                    LogDiagnostics(mesh, skeleton!, clip, d);
                }

                // Recentre from the REST (bind-pose) AABB — the pose the mesh is actually displayed in
                // at spawn (Setup leaves the surface in rest; the idle only advances once _Process runs)
                // and the STABLE reference the idle oscillates AROUND (the delta-composed idle keeps every
                // frame near the bind pose — §6.5/§6.6, the validated AnimAsDelta default). The earlier
                // choice of the ANIMATED frame-0 AABB diverged from the displayed rest extent for rigs
                // whose idle frame-0 pulls the silhouette in (measured: a209110001 rest X-extent 7.25 vs
                // animated-frame-0 3.22), recentring against a pose the mesh is NOT sitting in and floating
                // the feet ~9 units off the platform. The bind pose is the canonical standing reference, so
                // recentring feet to local Y=0 from the rest AABB is both what's on screen AND stable across
                // the idle cycle (no wandering floor). The single Z-negate inside the skinning output stays
                // the only handedness conversion; the pivot remap is the port-side display reorientation
                // (§7/§9), shared by player + actors — and the player's bind feet land on the ground, the
                // idle gently oscillating around that contact (behaviour-preserving for the in-world render).
                // spec: Docs/RE/specs/skinning.md §8(a) (the orientation/recentre knob is the pivot/root
                //       transform only, NOT the deform math; bind pose is the cancellation reference) /
                //       §6.5/§6.6 (delta idle stays near bind) / §7 / §9.
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
                foreach (var c in pivot.GetChildren())
                {
                    pivot.RemoveChild(c);
                    c.QueueFree();
                }
            }

        // ---- Static path (no skeleton, ForceSkinned off, or LBS failure) ----
        try
        {
            var (inst, aabb) = BuildStaticMesh(mesh, albedo);
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

    /// <summary>
    ///     Builds the base skinned character (
    ///     <see
    ///         cref="Build(SkinnedMesh, Skeleton?, AnimationClip?, ImageTexture?, bool, float, out SkinnedCharacterNode?, string?)" />
    ///     )
    ///     and then attaches the resolved equipment <paramref name="parts" />: weapon parts (slot 14) are
    ///     RIGIDLY attached to a hand bone of the shared skeleton (equipment_visuals.md §5), while
    ///     non-weapon parts would be skinned-deform under the same skeleton (§4 — see the note below).
    ///     ADDED as a NEW overload (the existing <c>Build</c> signatures are unchanged) so callers that do
    ///     not yet supply equipment are unaffected.
    ///     <para>
    ///         WEAPON path: fully wired here — each weapon part is built as a static rigid mesh and bound
    ///         to the inner <see cref="SkinnedCharacterNode" /> via
    ///         <see cref="SkinnedCharacterNode.AttachHandWeapon" />, which re-places it from the hand
    ///         bone's animated world pose every frame and applies the <c>Visual+100</c> scalar scale.
    ///     </para>
    ///     <para>
    ///         NON-WEAPON path: STATUS — not yet rendered here. Per equipment_visuals.md §4 the head/face/
    ///         body parts are skinned-deform parts sharing the ONE skeleton (no socket); reproducing them
    ///         in this CPU-LBS node requires deforming several meshes against the shared <c>_world</c> pose
    ///         (a multi-surface extension of <see cref="SkinnedCharacterNode" />). That extension is
    ///         reported as the remaining work; non-weapon parts are skipped here (the body mesh passed as
    ///         <paramref name="mesh" /> still renders). spec: equipment_visuals.md §4 / §9 item 1–2.
    ///     </para>
    ///     spec: Docs/RE/specs/equipment_visuals.md §1 (compose, don't swap) / §4 / §5 / §9.
    /// </summary>
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
            return root; // static fallback (no skeleton) or no equipment — body only.

        // PART-BUILD ORDER / NO DOUBLE-ATTACH (§3.6.2 "1:1 port guidance (b)"): the recovered lineup
        // build runs two passes with a FULL skin-list teardown between them, so only the final pass's
        // parts survive. We build that final set ONCE on a freshly-created node (Build above made a new
        // SkinnedCharacterNode with empty overlay/weapon lists), then append the body's overlays + the
        // weapon in slot order — no part is attached twice and nothing is overwritten. Clear first so a
        // re-used node (defensive) also starts empty, exactly reproducing the teardown semantics.
        // spec: Docs/RE/specs/skinning.md §3.6.2 (PASS-2 tears PASS-1 down; build the PASS-2 set directly).
        lbsNode.ClearOverlayParts();
        lbsNode.ClearWeapons();

        foreach (var part in parts)
            if (part.IsHandWeapon)
                // WEAPON (slot 14): rigid single-bone attach. spec: equipment_visuals.md §5.
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
                // NON-WEAPON overlay {4,6,2,11}: skinned-deform under the SHARED skeleton (§3.5.1 / §4) —
                // now reproduced as an additional ArrayMesh surface on the same SkinnedCharacterNode (one
                // skeleton, one deform pass, no second base mesh, no double-attach). The part is baked
                // against the shared bind world so its §0 cancellation holds; it animates with the idle.
                // spec: Docs/RE/specs/skinning.md §3.5.1 / §3.6.2 / equipment_visuals.md §4.
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

    /// <summary>
    ///     Builds a static rigid rest-pose <see cref="MeshInstance3D" /> for a bone-attached part (e.g.
    ///     a slot-14 weapon). Uses the SAME single handedness conversion + winding as the body's static
    ///     path; the caller (<see cref="SkinnedCharacterNode.AttachHandWeapon" />) drives the node's
    ///     transform from a hand bone each frame, so the mesh itself stays in its rest/grip geometry.
    ///     spec: Docs/RE/specs/equipment_visuals.md §5 (weapon = rigid single-bone attach, not skinned).
    ///     spec: Docs/RE/specs/skinning.md §8(b) — single handedness conversion.
    /// </summary>
    public static (MeshInstance3D Inst, Aabb Aabb) BuildStaticRigidMesh(
        SkinnedMesh mesh, ImageTexture? albedo, string nodeName)
    {
        var (inst, aabb) = BuildStaticMesh(mesh, albedo);
        inst.Name = nodeName;
        return (inst, aabb);
    }

    // -------------------------------------------------------------------------
    // Static rest-pose ArrayMesh (uses the SAME unified handedness conversion)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds a flat unindexed static rest-pose <see cref="ArrayMesh" />. Positions and normals use
    ///     the single handedness conversion (world Z-negate), identical to the skinned path's output.
    ///     spec: Docs/RE/specs/skinning.md §8(b) — one conversion, applied uniformly.
    ///     spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap [0,2,1] for Godot CCW.
    /// </summary>
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
            int[] order = [cBase + 0, cBase + 2, cBase + 1]; // CW→CCW
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

    // -------------------------------------------------------------------------
    // Recentre
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the axis-aligned bounding box that encloses <paramref name="aabb" /> after its 8 corners
    ///     are rotated by <paramref name="basis" /> (the pivot's up-axis remap). Godot's Aabb has no
    ///     Basis*Aabb operator, so transform the corners and re-fit. Pure geometry — no engine state.
    /// </summary>
    /// <summary>Names the longest axis of an AABB size (diagnostic for the upright remap).</summary>
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

    // -------------------------------------------------------------------------
    // Diagnostics logging (one concise [Skinning] summary block)
    // -------------------------------------------------------------------------

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

    /// <summary>
    ///     A resolved equipment part ready to render: the weapon slot (14) is rigid-attached to a hand
    ///     bone; non-weapon parts (head/face/body slots {2,3,4,6,11}) are skinned-deform under the shared
    ///     skeleton. The caller (a VFS-aware renderer) resolves the part's mesh + texture via the
    ///     appearance chain and hands the loaded resources here.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3 (GID derivation) / §4 / §5 / §9.
    /// </summary>
    /// <param name="Slot">Visual part-slot id (14 = weapon; {2,3,4,6,11} = non-weapon).</param>
    /// <param name="Mesh">The resolved part <c>.skn</c>.</param>
    /// <param name="Albedo">Optional resolved part texture.</param>
    /// <param name="IsHandWeapon">True for the weapon slot (rigid hand-bone attach).</param>
    /// <param name="IsOffHand">True for the off-hand node of a dual / two-piece weapon (§5.1).</param>
    /// <param name="BoneId">Hand bone-id for a weapon (default <see cref="SkinnedCharacterNode.DefaultHandBoneId" />).</param>
    /// <param name="VisualScale">The <c>Visual+100</c> scalar scale for a weapon's grip (default 1.0).</param>
    public readonly record struct EquipmentVisualPart(
        int Slot,
        SkinnedMesh Mesh,
        ImageTexture? Albedo,
        bool IsHandWeapon,
        bool IsOffHand,
        int BoneId,
        float VisualScale);
}