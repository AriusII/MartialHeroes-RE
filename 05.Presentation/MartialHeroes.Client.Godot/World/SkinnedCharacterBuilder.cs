// World/SkinnedCharacterBuilder.cs
//
// Builds a fully animated Godot 4 node tree (Skeleton3D + skinned ArrayMesh + AnimationPlayer)
// from parsed Assets.Parsers models WITHOUT using GltfDocument.
//
// Architecture:
//   Root (Node3D)
//     ├─ Skeleton3D          — bone hierarchy, rest transforms, owns the skinned mesh
//     │    └─ MeshInstance3D  — ArrayMesh with Bones/Weights arrays + Skin, child of Skeleton3D
//     └─ AnimationPlayer     — one animation library entry from the .mot clip (or absent if no clip)
//
// ============================================================================
// UP-AXIS AND COORDINATE CONVENTIONS
// ============================================================================
//
// The legacy .skn / .bnd character assets are stored with X as the UP axis.
// This is evidenced by the orchestrator-observed AABB of the Musa character:
//   ~5.0 along what the current build maps to Godot X (from raw pos_x)
//   ~2.44 along what the current build maps to Godot Y (from raw pos_y)
//   ~1.67 along what the current build maps to Godot Z (from raw pos_z)
// A standing 84-bone humanoid should have its tallest dimension as Godot Y (up).
// The largest dimension (5.0) is mapped from raw pos_x — therefore raw X is the
// character height axis.  (empirical: AABB width=5.0 >> height=2.44 when using
// the old -X,Y,Z mapping, confirming raw X is the character up-axis.)
//
// The correct basis change for character mesh-local geometry is therefore:
//
//   godot.X = -legacy.Y    (legacy Y becomes Godot right, negated for handedness)
//   godot.Y =  legacy.X    (legacy X = character up → Godot Y = up)
//   godot.Z = -legacy.Z    (legacy Z negated: same direction as WorldCoordinates.ToGodot)
//
// Matrix M (columns = where legacy basis vectors land in Godot space):
//
//       legacy_x  legacy_y  legacy_z
// gx [     0       -1          0   ]
// gy [     1        0          0   ]
// gz [     0        0         -1   ]
//
// det(M) = -1  → orientation-reversing (LH → RH).  This is correct.
//
// This same mapping applies to:
//   - Vertex positions and normals (geometry)
//   - Bone rest-pose translations (bone local-space → Godot local-space)
//   - Bone rest-pose rotation quaternions (see §Quaternion formula)
//   - Animation keyframe translations and rotations
//
// spec: Docs/RE/formats/mesh.md §Vertex record — "pos_x stored second on disk at
//       sub-offset 12; normal_x stored first on disk at sub-offset 0."  CONFIRMED.
// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_trans_x/y/z @ +8.
// empirical: orchestrator AABB width=5.0 identifies raw X as the character height axis.
//
// ============================================================================
// QUATERNION FORMULA (from the M basis change above)
// ============================================================================
//
// For a unit quaternion q = (qx i + qy j + qz k + qw), the physical rotation
// transforms under basis change M as:  q' = M * q * M^{-1}  (conjugation by M).
//
// Since det(M) = -1, M is an improper rotation.  For the vector part, M acts as:
//   M * (qx, qy, qz) = (-qy, qx, -qz)
// For det=-1 we also negate the vector part of the result (chirality flip):
//   q'_vec = -((-qy, qx, -qz)) = (qy, -qx, qz)  ← WRONG direction
//
// Instead, derive directly from M applied to the rotation axis:
//   The new rotation axis is M * (qx, qy, qz) = (-qy, qx, -qz).
//   The scalar w is unchanged (it is cos(θ/2) and θ is invariant).
//
// Verification:
//   - 90° around legacy X (= Godot Y):  q=(sin,0,0,cos) → q'=(0,sin,0,cos)  ✓
//   - 90° around legacy Y (→ Godot -X): q=(0,sin,0,cos) → q'=(-sin,0,0,cos) ✓
//   - 90° around legacy Z (→ Godot -Z): q=(0,0,sin,cos) → q'=(0,0,-sin,cos) ✓
//
// Therefore:  q_godot = Quaternion(-q.Y, q.X, -q.Z, q.W).Normalized()
//
// spec: Docs/RE/formats/mesh.md §Quaternion component order —
//       "XYZW order: X at +20, Y at +24, Z at +28, W at +32."  CONFIRMED.
// spec: Docs/RE/formats/animation.md §Keyframe record —
//       "component order: XYZ translation, then XYZW quaternion."  CONFIRMED.
//
// ============================================================================
// WINDING ORDER
// ============================================================================
//
// On-disk .skn corner order is D3D9 CW.  Emit corners as [0, 2, 1] per triangle.
// spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap for Godot CCW.
// spec: SknMeshBuilder — same [0, 2, 1] swap applied.
//
// ============================================================================
// SKINNING / BONE WEIGHT BINDING
// ============================================================================
//
// .skn weight records carry:
//   vertex_index : zero-based index into the vertex array
//   bone_index   : zero-based index into the .bnd bone ARRAY (not the SelfId space)
//   weight       : influence weight
//
// spec: Docs/RE/formats/mesh.md §Weight record — "bone_index: zero-based index into
//       the associated bind-pose bone array."  CONFIRMED.
//
// Since BuildSkeleton adds bones in on-disk order (bone[0] → Godot index 0,
// bone[1] → Godot index 1, …), the Godot bone index for a weight record is
// simply (int)wr.BoneIndex directly.  Using the SelfId lookup table for weights
// is incorrect (SelfId values may not be sequential array indices).
//
// Animation tracks use a DIFFERENT linkage:
//   track.BoneId = low byte of track_descriptor → must match Bone.SelfId low byte
// spec: Docs/RE/formats/animation.md §Bone-track linkage — "bone_id matches Bone.SelfId".
// Therefore animation tracks still use the SelfId → GodotIndex lookup map.
//
// ============================================================================
// RECENTRE
// ============================================================================
//
// After building the ArrayMesh, the mesh AABB is computed and the root Node3D
// is translated so that:
//   - The minimum Y of the mesh AABB sits at world Y=0 (feet on the ground plane).
//   - The mesh is centred on X=0 and Z=0.
// This is a presentation convenience; the orchestrator can override the position.

using Godot;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Builds a live Godot node from parsed <see cref="SkinnedMesh"/>, <see cref="Skeleton"/>,
/// and <see cref="AnimationClip"/> data without using <c>GltfDocument</c>.
///
/// Returns a <see cref="Node3D"/> root whose children are:
/// <list type="bullet">
///   <item>A <see cref="Skeleton3D"/> (with the <see cref="MeshInstance3D"/> as its child).</item>
///   <item>An <see cref="AnimationPlayer"/> (if <paramref name="clip"/> is non-null).</item>
/// </list>
///
/// The orchestrator should position and scale the returned root in world space.
/// The root's position is pre-adjusted so that feet are near local Y=0 and the
/// body is centred on X=0, Z=0.
/// </summary>
public static class SkinnedCharacterBuilder
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    // Maximum bone influences stored per vertex in the Godot surface arrays.
    // Godot 4 requires either 4 or 8 per vertex; 4 is the standard.
    // spec: Godot 4 ArrayMesh documentation — Bones/Weights arrays must be multiples of 4.
    private const int MaxInfluences = 4;

    // Fixed animation frame rate for .mot clips.
    // spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps."  CONFIRMED.
    private const float MotFrameRate = 10.0f;

    // Seconds per frame at the fixed .mot rate (0.1 s/frame = 10 fps).
    private const float SecondsPerFrame = 1.0f / MotFrameRate;

    // Minimum weight threshold below which a weight record is skipped.
    // Matches the parser's observed skip threshold.
    // spec: Docs/RE/formats/mesh.md §Weight record — "records where weight < 0.01 are skipped".
    private const float WeightSkipThreshold = 0.01f;

    // Name assigned to the Skeleton3D node (used in AnimationPlayer track paths).
    private const string SkeletonNodeName = "Skeleton3D";

    // Name assigned to the AnimationLibrary entry.
    private const string AnimLibraryName = "default";

    // Name assigned to the single animation within the library.
    private const string AnimationName = "clip";

    // -------------------------------------------------------------------------
    // Coordinate helpers (character-local space)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a legacy character-local position (X-up, left-handed) to Godot (Y-up, right-handed).
    ///
    /// Mapping:  godot.X = -legacy.Y
    ///           godot.Y =  legacy.X    (raw X is the character height axis)
    ///           godot.Z = -legacy.Z
    ///
    /// Matrix determinant = -1 → LH→RH handedness flip.
    ///
    /// empirical: orchestrator AABB 5.0-wide axis identified as raw pos_x → character up.
    /// spec: Docs/RE/formats/mesh.md §Vertex record — pos_x @ sub-offset 12; normal_x @ sub-offset 0.
    /// </summary>
    private static Vector3 ToGodotCharacter(Vec3 v)
        => new Vector3(-v.Y, v.X, -v.Z);

    /// <summary>
    /// Converts a legacy character-local unit normal to Godot and re-normalizes.
    /// Same axis mapping as <see cref="ToGodotCharacter"/>.
    /// </summary>
    private static Vector3 ToGodotNormal(Vec3 n)
        => new Vector3(-n.Y, n.X, -n.Z).Normalized();

    /// <summary>
    /// Converts a legacy character-local quaternion (XYZW, X-up left-handed) to Godot (Y-up right-handed).
    ///
    /// Formula derived by applying M to the rotation axis:
    ///   q_godot = Quaternion(-q.Y, q.X, -q.Z, q.W).Normalized()
    ///
    /// Verification:
    ///   90° around legacy X (= Godot Y): (sin,0,0,cos) → (0,sin,0,cos)  ✓
    ///   90° around legacy Y (→ Godot -X): (0,sin,0,cos) → (-sin,0,0,cos) ✓
    ///   90° around legacy Z (→ Godot -Z): (0,0,sin,cos) → (0,0,-sin,cos) ✓
    ///
    /// spec: Docs/RE/formats/mesh.md §Quaternion component order — XYZW on disk, W last.  CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §Keyframe record — XYZW quaternion.  CONFIRMED.
    /// </summary>
    private static Quaternion ToGodotQuat(Quat q)
        => new Quaternion(-q.Y, q.X, -q.Z, q.W).Normalized();

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a Godot node tree for a skinned character.
    ///
    /// <para>Steps performed:</para>
    /// <list type="number">
    ///   <item>Create a <see cref="Skeleton3D"/> from <paramref name="skeleton"/> (or a single
    ///         fallback root bone if <paramref name="skeleton"/> is null).</item>
    ///   <item>Build a skinned <see cref="ArrayMesh"/> from <paramref name="mesh"/> including
    ///         per-vertex bone indices and weights; attach it as a <see cref="MeshInstance3D"/>
    ///         child of the Skeleton3D with a <see cref="Skin"/> and skeleton path set.</item>
    ///   <item>If <paramref name="clip"/> is non-null, build an <see cref="AnimationPlayer"/> with
    ///         position/rotation tracks targeting each animated bone; set it to loop and auto-play.
    ///         If <paramref name="clip"/> is null the mesh renders in the static rest pose.</item>
    ///   <item>Recentre the root node so the mesh AABB min-Y is at 0 and the body is centred on X/Z.</item>
    /// </list>
    ///
    /// All steps are individually guarded: a failure in any step is logged and the node is still
    /// returned in a degraded-but-visible state.
    ///
    /// spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh.
    /// spec: Docs/RE/formats/mesh.md §Format: .bnd — binary bind-pose skeleton.
    /// spec: Docs/RE/formats/animation.md §Header layout.
    /// </summary>
    /// <param name="mesh">Parsed .skn skinned mesh. Must not be null.</param>
    /// <param name="skeleton">
    ///   Parsed .bnd skeleton, or null. If null a single root bone is synthesised so the
    ///   mesh still renders with no skinning.
    /// </param>
    /// <param name="clip">
    ///   Parsed .mot animation clip, or null. If null the character renders in its rest pose.
    /// </param>
    /// <param name="albedo">
    ///   Optional albedo texture. If null a neutral grey material is applied.
    /// </param>
    /// <returns>
    ///   A <see cref="Node3D"/> root ready to be added to the scene tree and positioned in
    ///   world space by the orchestrator.  Feet are near local Y=0; body centred on X/Z.
    /// </returns>
    public static Node3D Build(
        SkinnedMesh mesh,
        Skeleton? skeleton,
        AnimationClip? clip,
        ImageTexture? albedo = null)
    {
        var root = new Node3D { Name = $"SkinnedChar_{mesh.Name}" };

        // Step 1 — build Skeleton3D.
        Skeleton3D godotSkeleton;
        int[] boneIdToGodotIndex; // maps Bone.SelfId (low byte) → Godot bone index (for anim tracks)

        try
        {
            (godotSkeleton, boneIdToGodotIndex) = BuildSkeleton(skeleton);
            godotSkeleton.Name = SkeletonNodeName;
            root.AddChild(godotSkeleton);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkinnedCharacterBuilder] BuildSkeleton failed for '{mesh.Name}': {ex.Message}");
            // Synthesise a minimal skeleton so we can still render the mesh.
            godotSkeleton = new Skeleton3D { Name = SkeletonNodeName };
            godotSkeleton.AddBone("root");
            godotSkeleton.SetBoneRest(0, Transform3D.Identity);
            boneIdToGodotIndex = new int[256];
            // All bone IDs map to bone 0.
            root.AddChild(godotSkeleton);
        }

        // Step 2 — build skinned ArrayMesh and MeshInstance3D.
        ArrayMesh? builtArrayMesh = null;
        try
        {
            MeshInstance3D? meshInst = BuildSkinnedMesh(
                mesh, godotSkeleton, boneIdToGodotIndex, albedo, out builtArrayMesh);
            if (meshInst is not null)
            {
                // The MeshInstance3D must be a CHILD of the Skeleton3D so that Godot applies
                // skeletal deformation through the Skeleton3D.SkinReference pipeline.
                godotSkeleton.AddChild(meshInst);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkinnedCharacterBuilder] BuildSkinnedMesh failed for '{mesh.Name}': {ex.Message}");
            // Degraded: character is invisible but the node tree is valid.
        }

        // Step 3 — build AnimationPlayer (optional).
        if (clip is not null)
        {
            try
            {
                AnimationPlayer? player = BuildAnimationPlayer(clip, skeleton, boneIdToGodotIndex, godotSkeleton);
                if (player is not null)
                {
                    root.AddChild(player);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SkinnedCharacterBuilder] BuildAnimationPlayer failed for '{mesh.Name}': {ex.Message}");
                // Degraded: character stays in rest pose.
            }
        }

        // Step 4 — recentre: shift root so feet are at Y=0, body centred on X/Z.
        if (builtArrayMesh is not null)
        {
            try
            {
                RecentreRoot(root, builtArrayMesh);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SkinnedCharacterBuilder] Recentre failed for '{mesh.Name}': {ex.Message}");
                // Non-fatal: character is visible but may be offset.
            }
        }

        return root;
    }

    // -------------------------------------------------------------------------
    // Step 1 — Skeleton3D construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="Skeleton3D"/> from a parsed <see cref="Skeleton"/>.
    ///
    /// Bones are added in on-disk order. Parent indices are resolved by matching
    /// <c>Bone.ParentId</c> (low byte) to earlier entries' <c>Bone.SelfId</c> (low byte),
    /// consistent with the .bnd spec sentinel rule.
    ///
    /// Rest transforms are converted from legacy X-up left-handed to Godot Y-up right-handed
    /// using <see cref="ToGodotCharacter"/> (translation) and <see cref="ToGodotQuat"/> (rotation).
    ///
    /// spec: Docs/RE/formats/mesh.md §Bone array — 36 bytes per bone, XYZW quaternion.
    /// spec: Docs/RE/formats/mesh.md §Root bone sentinel — self_id==0 and parent_id==0.
    /// </summary>
    /// <returns>
    ///   A tuple of (Skeleton3D, boneIdToGodotIndex).
    ///   boneIdToGodotIndex is a 256-element array where index = Bone.SelfId low byte,
    ///   value = Godot bone index.  Used for animation-track → bone lookups.
    ///   NOTE: do NOT use this map for weight-record bone_index lookups; those are
    ///   array-position indices (see AccumulateWeights).
    /// </returns>
    private static (Skeleton3D Skel, int[] IdMap) BuildSkeleton(Skeleton? skeleton)
    {
        var godotSkel = new Skeleton3D();

        // 256-element lookup: boneId (0..255) → Godot bone index.
        // Initialise to 0 so unmapped bone IDs fall back to the root bone.
        var idMap = new int[256];

        if (skeleton is null || skeleton.Bones.Length == 0)
        {
            // Synthesise a single root bone at the identity rest transform.
            godotSkel.AddBone("root");
            godotSkel.SetBoneRest(0, Transform3D.Identity);
            // All entries in idMap already point to bone 0.
            GD.Print("[SkinnedCharacterBuilder] No skeleton supplied — synthesised single root bone.");
            return (godotSkel, idMap);
        }

        Bone[] bones = skeleton.Bones;
        int count = bones.Length;

        // First pass: add all bones and build the SelfId→godotIndex map.
        // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — self_id @ +0, low byte is ID.
        for (int i = 0; i < count; i++)
        {
            byte selfIdByte = (byte)(bones[i].SelfId & 0xFF);
            string boneName = $"bone_{selfIdByte}";
            int godotIdx = godotSkel.GetBoneCount(); // == i since we add in order
            godotSkel.AddBone(boneName);
            idMap[selfIdByte] = godotIdx;
        }

        // Second pass: set parent relationships and rest transforms.
        for (int i = 0; i < count; i++)
        {
            Bone bone = bones[i];
            int godotIdx = i; // bones are added in on-disk order, so godot index == i

            byte selfIdByte = (byte)(bone.SelfId & 0xFF);
            byte parentIdByte = (byte)(bone.ParentId & 0xFF);

            // spec: Docs/RE/formats/mesh.md §Root bone sentinel:
            //   "root bone: self_id == 0 and parent_id == 0 (both low bytes zero)."
            bool isRoot = bone.IsRoot;

            if (!isRoot)
            {
                // Look up the Godot index of the parent bone via the SelfId map.
                int parentGodotIdx = idMap[parentIdByte];
                // Avoid self-parenting if the parent resolved to the same index.
                if (parentGodotIdx != godotIdx)
                {
                    godotSkel.SetBoneParent(godotIdx, parentGodotIdx);
                }
            }

            // Rest translation: convert from legacy X-up left-handed to Godot Y-up right-handed.
            // godot.X = -legacy.Y,  godot.Y = legacy.X,  godot.Z = -legacy.Z
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_trans_x/y/z @ +8.
            // empirical: raw pos_x is the character height axis (up).
            Vector3 restOrigin = ToGodotCharacter(bone.Translation);

            // Rest rotation quaternion: q_godot = Quaternion(-q.Y, q.X, -q.Z, q.W)
            // spec: Docs/RE/formats/mesh.md §Quaternion component order — XYZW on disk.
            Quaternion restQuat = ToGodotQuat(bone.Rotation);

            var restBasis = new Basis(restQuat);
            var restTransform = new Transform3D(restBasis, restOrigin);

            godotSkel.SetBoneRest(godotIdx, restTransform);
        }

        // Reset to rest pose so the mesh starts in the correct bind pose.
        godotSkel.ResetBonePoses();

        GD.Print($"[SkinnedCharacterBuilder] Skeleton3D built: {count} bones from .bnd.");
        return (godotSkel, idMap);
    }

    // -------------------------------------------------------------------------
    // Step 2 — Skinned ArrayMesh + MeshInstance3D
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a skinned <see cref="ArrayMesh"/> from the parsed <see cref="SkinnedMesh"/>.
    ///
    /// <para>The mesh is constructed as a flat (unindexed) triangle list, one rendered vertex
    /// per triangle corner.  This avoids re-indexing while supporting per-corner UVs.</para>
    ///
    /// <para>Bone influence arrays (<see cref="Mesh.ArrayType.Bones"/> and
    /// <see cref="Mesh.ArrayType.Weights"/>) are built from the .skn weight table by
    /// accumulating influences per geometry-vertex index and then distributing them into the
    /// flat rendered-vertex layout.</para>
    ///
    /// spec: Docs/RE/formats/mesh.md §Weight / skin table.
    /// spec: Docs/RE/formats/mesh.md §Face table — CW winding swap [0,2,1] per triangle.
    /// spec: Docs/RE/formats/mesh.md §Vertex record — negate-X/Z, swap X↔Y for character up-axis.
    /// </summary>
    private static MeshInstance3D? BuildSkinnedMesh(
        SkinnedMesh skn,
        Skeleton3D godotSkeleton,
        int[] boneIdToGodotIndex,
        ImageTexture? albedo,
        out ArrayMesh? outArrayMesh)
    {
        outArrayMesh = null;

        int faceCount = (int)skn.FaceCount;
        if (faceCount == 0 || skn.Positions.Length == 0)
        {
            GD.Print($"[SkinnedCharacterBuilder] Mesh '{skn.Name}' has no geometry — skipping.");
            return null;
        }

        int boneCount = godotSkeleton.GetBoneCount();

        // --- Accumulate per-geometry-vertex bone influences ---
        // Each geometry vertex can have multiple weight records pointing at it.
        // We take up to MaxInfluences (4) per vertex, then normalise.
        //
        // IMPORTANT: bone_index in weight records is a zero-based array index into the
        // .bnd bone array, NOT the SelfId.  Since Godot bones are added in on-disk order,
        // Godot bone index == .bnd array index.  Pass boneCount for bounds-clamping.
        //
        // spec: Docs/RE/formats/mesh.md §Weight record — "bone_index: zero-based index into
        //       the associated bind-pose bone array."  CONFIRMED.
        int geoVertCount = skn.Positions.Length;
        AccumulateWeights(skn.Weights, geoVertCount, boneCount,
            out int[] perGeoVtxBones,   // [geoVertCount × MaxInfluences] Godot bone indices
            out float[] perGeoVtxWts);  // [geoVertCount × MaxInfluences] normalised weights

        // --- Build flat rendered-vertex arrays ---
        int totalVerts = faceCount * 3;
        var positions = new Vector3[totalVerts];
        var normals   = new Vector3[totalVerts];
        var uvs       = new Vector2[totalVerts];

        // Godot's Bones array is int[] (4 ints per vertex).
        // Godot's Weights array is float[] (4 floats per vertex).
        // Both are indexed identically.
        // spec: Godot 4 ArrayMesh — Bones/Weights arrays must be 4 × vertexCount.
        var bones   = new int[totalVerts * MaxInfluences];
        var weights = new float[totalVerts * MaxInfluences];

        SknCorner[] corners = skn.Corners;
        Vec3[] srcPos = skn.Positions;
        Vec3[] srcNrm = skn.Normals;

        for (int f = 0; f < faceCount; f++)
        {
            // CW→CCW winding swap: emit corners 0, 2, 1 per triangle.
            // spec: Docs/RE/formats/mesh.md §Face table — CW winding, swap for Godot CCW.
            // spec: SknMeshBuilder — same [0, 2, 1] swap.
            int cBase = f * 3;
            int vBase = f * 3;

            // CW→CCW: corner access order [0, 2, 1] maps to rendered-vertex slots [0, 1, 2].
            int[] swap = [cBase + 0, cBase + 2, cBase + 1];

            for (int j = 0; j < 3; j++)
            {
                SknCorner corner = corners[swap[j]];
                uint vi = corner.VertexIndex;

                // Clamp corrupt indices to 0.
                if (vi >= (uint)geoVertCount)
                {
                    GD.PrintErr($"[SkinnedCharacterBuilder] Face {f} corner {j}: " +
                                $"VertexIndex {vi} out of range (posCount={geoVertCount}) — using 0.");
                    vi = 0;
                }

                Vec3 p = srcPos[vi];
                Vec3 n = (vi < (uint)srcNrm.Length) ? srcNrm[vi] : new Vec3(0f, 1f, 0f);

                // Coordinate conversion: legacy X-up LH → Godot Y-up RH.
                // godot.X = -legacy.Y,  godot.Y = legacy.X,  godot.Z = -legacy.Z
                // empirical: raw pos_x is character height (up).
                // spec: Docs/RE/formats/mesh.md §Vertex record — pos_x @ sub-offset 12.
                positions[vBase + j] = ToGodotCharacter(p);

                // Normal: same axis mapping.
                // spec: Docs/RE/formats/mesh.md §Vertex record — normal_x @ sub-offset 0.
                normals[vBase + j] = ToGodotNormal(n);

                // UV: already v-flipped by the parser (1.0f - uv_v_on_disk).
                // spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v".
                uvs[vBase + j] = new Vector2(corner.UvU, corner.UvV);

                // Copy bone influences from the per-geometry-vertex accumulator into the flat array.
                int dstBase = (vBase + j) * MaxInfluences;
                int srcBase = (int)vi * MaxInfluences;
                for (int inf = 0; inf < MaxInfluences; inf++)
                {
                    bones[dstBase + inf]   = perGeoVtxBones[srcBase + inf];
                    weights[dstBase + inf] = perGeoVtxWts[srcBase + inf];
                }
            }
        }

        // --- Assemble ArrayMesh ---
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]  = positions;
        arrays[(int)Mesh.ArrayType.Normal]  = normals;
        arrays[(int)Mesh.ArrayType.TexUV]   = uvs;
        arrays[(int)Mesh.ArrayType.Bones]   = bones;
        arrays[(int)Mesh.ArrayType.Weights] = weights;
        // No index array — flat unindexed layout mirrors SknMeshBuilder.

        var arrayMesh = new ArrayMesh();

        // Add surface with default flags (4-bone skinning).
        // Do NOT pass FlagUse8BoneWeights — that would require 8 entries per vertex.
        // spec: Godot 4 ArrayMesh.AddSurfaceFromArrays — default flags = 4-bone skinning.
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        outArrayMesh = arrayMesh;

        // --- Material ---
        var mat = new StandardMaterial3D();
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        // Double-sided for thin geometry safety.
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        if (albedo is not null)
        {
            mat.AlbedoTexture = albedo;
        }
        else
        {
            // Neutral warm skin tone so the silhouette is recognisable without a texture.
            mat.AlbedoColor = new Color(0.85f, 0.75f, 0.65f, 1f);
        }

        arrayMesh.SurfaceSetMaterial(0, mat);

        // --- Create Skin from the Skeleton3D rest transforms ---
        // CreateSkinFromRestTransforms() builds a Skin whose per-bone bind poses are the
        // inverse model-space rest transforms — exactly what GPU skinning needs.
        // The Skeleton3D is already added to the root node at this point, and bone rests
        // have been set, so the model-space accumulation is correct.
        // spec: Godot 4 Skeleton3D.CreateSkinFromRestTransforms() documentation.
        Skin skin = godotSkeleton.CreateSkinFromRestTransforms();

        // Build the MeshInstance3D.  It will be added as a child of Skeleton3D, so the
        // path from MeshInstance3D to Skeleton3D is "..".
        // spec: Godot 4 MeshInstance3D.Skeleton documentation.
        var meshInst = new MeshInstance3D
        {
            Name     = $"SkinnedMesh_{skn.Name}",
            Mesh     = arrayMesh,
            Skin     = skin,
            Skeleton = new NodePath(".."),
        };

        GD.Print($"[SkinnedCharacterBuilder] Skinned ArrayMesh built: '{skn.Name}' " +
                 $"{faceCount} faces, {geoVertCount} geo-verts, {totalVerts} rendered-verts.");

        return meshInst;
    }

    // -------------------------------------------------------------------------
    // Weight accumulation helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Accumulates per-geometry-vertex bone influences from the .skn weight table.
    ///
    /// Up to <see cref="MaxInfluences"/> (4) influences are retained per vertex (largest by
    /// weight). Weights are normalised so their per-vertex sum equals 1.0. Any vertex with
    /// zero total weight receives full influence on bone 0 (root bone fallback).
    ///
    /// IMPORTANT: <paramref name="weightRecords"/>'s <c>BoneIndex</c> is a zero-based array index
    /// into the .bnd bone array — NOT a SelfId.  Since Godot bones are added in on-disk order,
    /// the Godot bone index equals the .bnd array index directly.
    ///
    /// spec: Docs/RE/formats/mesh.md §Weight / skin table — "post-load invariant: engine
    ///       normalizes weights per vertex so the per-vertex sum equals 1.0."
    /// spec: Docs/RE/formats/mesh.md §Weight record — "bone_index: zero-based index into
    ///       the associated bind-pose bone array."  CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §Weight record — "records where weight &lt; 0.01 are skipped."
    /// </summary>
    private static void AccumulateWeights(
        SknWeight[] weightRecords,
        int geoVertCount,
        int godotBoneCount,
        out int[] outBones,
        out float[] outWeights)
    {
        // Working storage: each vertex holds up to MaxInfluences (boneIdx, weight) pairs.
        // We use parallel arrays sized [geoVertCount × MaxInfluences].
        outBones   = new int[geoVertCount * MaxInfluences];
        outWeights = new float[geoVertCount * MaxInfluences];

        // Track how many influences we have so far per vertex (0..MaxInfluences).
        var influenceCounts = new int[geoVertCount];

        foreach (SknWeight wr in weightRecords)
        {
            // spec: Docs/RE/formats/mesh.md §Weight record — skip threshold weight < 0.01.
            if (wr.Weight < WeightSkipThreshold)
                continue;

            int vi = (int)wr.VertexIndex;
            if (vi < 0 || vi >= geoVertCount)
            {
                GD.PrintErr($"[SkinnedCharacterBuilder] Weight record: vertex_index {vi} out of range " +
                            $"({geoVertCount} verts) — skipping.");
                continue;
            }

            // bone_index in the weight record is a zero-based array position in the .bnd bone array.
            // Since Godot bones are added in on-disk order, Godot bone index == bone array index.
            // spec: Docs/RE/formats/mesh.md §Weight record — "zero-based index into the bone array."
            int godotBoneIdx = (int)(wr.BoneIndex);
            if (godotBoneIdx < 0 || godotBoneIdx >= godotBoneCount)
            {
                // Out-of-range bone index — clamp to root bone 0 as a safe fallback.
                GD.PrintErr($"[SkinnedCharacterBuilder] Weight record: bone_index {godotBoneIdx} out of range " +
                            $"(boneCount={godotBoneCount}) — clamping to 0.");
                godotBoneIdx = 0;
            }

            int current = influenceCounts[vi];
            int baseIdx = vi * MaxInfluences;

            if (current < MaxInfluences)
            {
                // Slot available — write directly.
                outBones[baseIdx + current]   = godotBoneIdx;
                outWeights[baseIdx + current] = wr.Weight;
                influenceCounts[vi] = current + 1;
            }
            else
            {
                // All 4 slots full — replace the lowest-weight slot if this one is heavier.
                // This preserves the most significant influences for vertices with > 4 weights.
                int minSlot = 0;
                float minW = outWeights[baseIdx];
                for (int s = 1; s < MaxInfluences; s++)
                {
                    float w = outWeights[baseIdx + s];
                    if (w < minW) { minW = w; minSlot = s; }
                }

                if (wr.Weight > minW)
                {
                    outBones[baseIdx + minSlot]   = godotBoneIdx;
                    outWeights[baseIdx + minSlot] = wr.Weight;
                }
            }
        }

        // Normalise weights and fill uninfluenced vertices with a fallback to bone 0.
        for (int vi = 0; vi < geoVertCount; vi++)
        {
            int baseIdx = vi * MaxInfluences;

            float total = 0f;
            for (int s = 0; s < MaxInfluences; s++)
                total += outWeights[baseIdx + s];

            if (total < WeightSkipThreshold)
            {
                // No usable influences — bind entirely to bone 0 (root).
                outBones[baseIdx]   = 0;
                outWeights[baseIdx] = 1f;
                // Remaining slots are already 0 / 0f from array initialisation.
            }
            else
            {
                // Normalise so the sum is 1.0.
                float invTotal = 1f / total;
                for (int s = 0; s < MaxInfluences; s++)
                    outWeights[baseIdx + s] *= invTotal;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Step 3 — AnimationPlayer construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="AnimationPlayer"/> with one looping <see cref="Animation"/> whose
    /// tracks drive the skeleton bones in Godot-space.
    ///
    /// <para>Track naming: <c>SkeletonNodeName:bone_N</c> where N is the Bone.SelfId low byte.
    /// AnimationPlayer is a sibling of Skeleton3D under the root Node3D, so the track path
    /// is relative to the root.</para>
    ///
    /// <para>Keyframe timing: key index K is placed at time K × 0.1 s (10 fps fixed).</para>
    ///
    /// <para>Loop mode: <see cref="Animation.LoopModeEnum.Linear"/> — identical to the runtime
    /// CycleLayer wrap behaviour described in the spec.</para>
    ///
    /// <para>Translations and rotations are converted to Godot-space using
    /// <see cref="ToGodotCharacter"/> and <see cref="ToGodotQuat"/> respectively.</para>
    ///
    /// spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps."  CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §Bone-track linkage — bone_id matches Bone.SelfId low byte.
    /// spec: Docs/RE/formats/animation.md §Wrap and loop behaviour — no on-disk flag; wrap is runtime.
    /// </summary>
    private static AnimationPlayer? BuildAnimationPlayer(
        AnimationClip clip,
        Skeleton? skeleton,
        int[] boneIdToGodotIndex,
        Skeleton3D godotSkeleton)
    {
        if (clip.FrameCount == 0 || clip.Tracks.Length == 0)
        {
            GD.Print(
                $"[SkinnedCharacterBuilder] Clip '{clip.Name}' is a stub (frameCount=0 or no tracks) — skipping AnimationPlayer.");
            return null;
        }

        // Clip duration in seconds.
        // spec: Docs/RE/formats/animation.md §Timing — "duration = frame_count × 0.1".  CONFIRMED.
        double durationSecs = clip.FrameCount * (double)SecondsPerFrame;

        var anim = new Animation();
        anim.Length    = (float)durationSecs;
        anim.LoopMode  = Animation.LoopModeEnum.Linear;
        // spec: Docs/RE/formats/animation.md §Wrap and loop — CycleLayer is "looping; replays
        //       continuously".  Godot Linear loop is the equivalent.

        // Build a reverse lookup: SelfId low byte → bone name (for track path construction).
        // Animation tracks use SelfId linkage (NOT array index).
        // spec: Docs/RE/formats/animation.md §Bone-track linkage — "bone_id matches Bone.SelfId".
        var boneIdToName = BuildBoneNameMap(skeleton);

        foreach (AnimationTrack track in clip.Tracks)
        {
            // spec: Docs/RE/formats/animation.md §Per-track record — bone_id = low byte of track_descriptor.
            byte boneId   = track.BoneId;
            string boneName = boneIdToName.TryGetValue(boneId, out string? n) ? n : $"bone_{boneId}";

            if (track.Keyframes.Length == 0)
                continue;

            // Track path: from the AnimationPlayer's root (the Node3D root) to the Skeleton3D bone.
            // AnimationPlayer is a child of the root Node3D; Skeleton3D is also a child of that root.
            // Path format: "SkeletonNodeName:boneName"
            // spec: Godot 4 AnimationPlayer track naming convention for Skeleton3D bones.
            string trackPath = $"{SkeletonNodeName}:{boneName}";

            // Position3D track
            int posTrackIdx = anim.AddTrack(Animation.TrackType.Position3D);
            anim.TrackSetPath(posTrackIdx, new NodePath(trackPath));
            anim.TrackSetInterpolationType(posTrackIdx, Animation.InterpolationType.Linear);

            // Rotation3D track
            int rotTrackIdx = anim.AddTrack(Animation.TrackType.Rotation3D);
            anim.TrackSetPath(rotTrackIdx, new NodePath(trackPath));
            anim.TrackSetInterpolationType(rotTrackIdx, Animation.InterpolationType.Linear);

            for (int ki = 0; ki < track.Keyframes.Length; ki++)
            {
                // Keyframe time: index × 1/10 s.
                // spec: Docs/RE/formats/animation.md §Timing — "sample index = floor(t × 10.0)".
                float t = ki * SecondsPerFrame;

                Keyframe kf = track.Keyframes[ki];

                // Translation: apply the same X-up LH → Y-up RH mapping as vertices.
                // godot.X = -legacy.Y,  godot.Y = legacy.X,  godot.Z = -legacy.Z
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x/y/z @ sub-offset 0.
                // empirical: raw X is character height axis.
                Vector3 godotPos = ToGodotCharacter(kf.Translation);

                // Rotation quaternion: q_godot = Quaternion(-q.Y, q.X, -q.Z, q.W)
                // spec: Docs/RE/formats/animation.md §Keyframe record — XYZW quaternion.
                Quaternion godotQuat = ToGodotQuat(kf.Rotation);

                anim.PositionTrackInsertKey(posTrackIdx, t, godotPos);
                anim.RotationTrackInsertKey(rotTrackIdx, t, godotQuat);
            }
        }

        // Wrap the animation in a library and add it to the player.
        var lib = new AnimationLibrary();
        lib.AddAnimation(AnimationName, anim);

        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        player.AddAnimationLibrary(AnimLibraryName, lib);

        // Auto-play the clip.  Godot convention for named library entries:
        // "libraryName/animationName" — so the auto-play key is "default/clip".
        player.Autoplay = $"{AnimLibraryName}/{AnimationName}";

        GD.Print($"[SkinnedCharacterBuilder] AnimationPlayer built: clip '{clip.Name}' " +
                 $"({clip.Tracks.Length} tracks, {clip.FrameCount} frames, {durationSecs:F2} s, looping).");

        return player;
    }

    // -------------------------------------------------------------------------
    // Step 4 — Recentre
    // -------------------------------------------------------------------------

    /// <summary>
    /// Translates the root <see cref="Node3D"/> so that the mesh AABB minimum Y is at 0
    /// (feet on the ground) and the mesh is centred on X=0, Z=0.
    ///
    /// The AABB is computed from the <see cref="ArrayMesh"/> directly (without requiring a
    /// scene-tree traversal), so this works before the root is added to the scene.
    ///
    /// The translation is applied to the root node position.  The orchestrator may freely
    /// override or further adjust this position when placing the character in world space.
    /// </summary>
    private static void RecentreRoot(Node3D root, ArrayMesh arrayMesh)
    {
        Aabb aabb = arrayMesh.GetAabb();

        if (aabb.Size == Vector3.Zero)
        {
            GD.Print("[SkinnedCharacterBuilder] Recentre: degenerate AABB — skipping.");
            return;
        }

        // Shift so that the lowest Y vertex is at Y=0 (feet on ground plane).
        float yShift = -aabb.Position.Y;

        // Centre on X and Z.
        float xShift = -(aabb.Position.X + aabb.Size.X * 0.5f);
        float zShift = -(aabb.Position.Z + aabb.Size.Z * 0.5f);

        root.Position = new Vector3(xShift, yShift, zShift);

        GD.Print($"[SkinnedCharacterBuilder] Recentre: AABB pos=({aabb.Position.X:F3},{aabb.Position.Y:F3},{aabb.Position.Z:F3}) " +
                 $"size=({aabb.Size.X:F3},{aabb.Size.Y:F3},{aabb.Size.Z:F3}) " +
                 $"→ root shift=({xShift:F3},{yShift:F3},{zShift:F3}).");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a dictionary from Bone.SelfId low byte → Godot bone name (e.g. "bone_0").
    /// Used to construct animation track paths.
    /// Animation tracks use SelfId linkage — NOT bone array index.
    /// spec: Docs/RE/formats/animation.md §Bone-track linkage — "bone_id matches Bone.SelfId".
    /// </summary>
    private static Dictionary<byte, string> BuildBoneNameMap(Skeleton? skeleton)
    {
        var map = new Dictionary<byte, string>();

        if (skeleton is null)
        {
            map[0] = "root";
            return map;
        }

        foreach (Bone bone in skeleton.Bones)
        {
            byte selfIdByte = (byte)(bone.SelfId & 0xFF);
            // Name must match what was assigned in BuildSkeleton.
            map[selfIdByte] = $"bone_{selfIdByte}";
        }

        return map;
    }
}
