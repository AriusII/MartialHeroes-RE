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
// Coordinate conventions (D3D9 left-handed → Godot right-handed):
//   .skn / .bnd data is in MESH-LOCAL space (not world-space absolute). The handedness flip
//   for mesh-local geometry is NEGATE X — matching SknMeshBuilder and the comment in
//   WorldCoordinates.cs: "the mesh uses its per-vertex convention" (negate X).
//   spec: Helpers/WorldCoordinates.cs — "mesh-local geometry uses negate X".
//   spec: Docs/RE/formats/mesh.md §Vertex record — "pos_x stored second on disk at sub-offset 12".
//   spec: Docs/RE/formats/mesh.md §Quaternion component order — "XYZW: X at +20, Y +24, Z +28, W +32".
//
//   The same negate-X rule applies to:
//     - Vertex positions and normals (geometry)
//     - Bone rest-pose translations (bone local space → Godot local space)
//     - Bone rest-pose rotations (quaternion handedness: negate X and Z components of the quaternion)
//     - Animation keyframe translations and rotations (same as bone rest)
//
// Quaternion handedness conversion (right-handed ↔ left-handed, negate X axis):
//   In a right-hand basis with X negated, the equivalent rotation is obtained by negating
//   the quaternion X and Z components (and keeping Y and W unchanged).
//   This is the standard D3D-to-OpenGL quaternion flip for a Z-forward to Z-backward or
//   X-mirror basis change.  Concretely: q_godot = Quaternion(-qx, qy, -qz, qw).
//   spec: Docs/RE/formats/mesh.md §Quaternion component order — XYZW on disk maps to XYZW in memory.
//
// Winding order:
//   On-disk .skn corner order is D3D9 CW. We emit corners as [0, 2, 1] per triangle for CCW.
//   spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap for Godot CCW.
//   spec: SknMeshBuilder — same [0, 2, 1] swap applied.
//
// Skinning:
//   .skn weight records are NOT one-per-vertex; each record says "vertex N is influenced by
//   bone B with weight W". Multiple records can share the same vertex_index. We accumulate up
//   to 4 influences per vertex, normalise the weights, and then pack them into the flat
//   unindexed (per-rendered-vertex) arrays Godot expects.
//   spec: Docs/RE/formats/mesh.md §Weight record — 12 bytes, "vertex_index, bone_index, weight".
//   spec: Docs/RE/formats/mesh.md §Weight / skin table — "weight_count == vertex_count in
//         item-skin samples (single-bone); character skins expected to have multiple per vertex".
//
// Animation:
//   Fixed 10 fps. Duration = frame_count × 0.1 s.
//   spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
//   Track bone linkage: track.BoneId matches Bone.SelfId (low byte).
//   spec: Docs/RE/formats/animation.md §Bone-track linkage.
//   Each keyframe is placed at time = keyframeIndex × 0.1 s (no re-normalization of alpha).
//   Loop: set via Animation.LoopMode.Linear (wraps at clip end).
//   spec: Docs/RE/formats/animation.md §Wrap and loop behaviour — "no loop flag on disk; wrap
//         is a runtime property." Godot loop mode is the runtime equivalent of CycleLayer.

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
/// All mesh geometry is built around its own local origin (no world offset baked in).
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
    // spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
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
    ///   world space by the orchestrator.
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
        int[] boneIdToGodotIndex; // maps Bone.SelfId (low byte) → Godot bone index

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
        try
        {
            MeshInstance3D? meshInst = BuildSkinnedMesh(mesh, godotSkeleton, boneIdToGodotIndex, albedo);
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
    /// Rest transforms are converted from legacy left-handed (negate X) to Godot right-handed.
    ///
    /// spec: Docs/RE/formats/mesh.md §Bone array — 36 bytes per bone, XYZW quaternion.
    /// spec: Docs/RE/formats/mesh.md §Root bone sentinel — self_id==0 and parent_id==0.
    /// </summary>
    /// <returns>
    ///   A tuple of (Skeleton3D, boneIdToGodotIndex).
    ///   boneIdToGodotIndex is a 256-element array where index = Bone.SelfId low byte,
    ///   value = Godot bone index (or 0 if that bone ID has no entry).
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

        // First pass: add all bones and build the id→godotIndex map.
        // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — self_id @ +0, low byte is ID.
        for (int i = 0; i < count; i++)
        {
            byte selfIdByte = (byte)(bones[i].SelfId & 0xFF);
            string boneName = $"bone_{selfIdByte}";
            int godotIdx = godotSkel.GetBoneCount();
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
                // Look up the Godot index of the parent bone.
                int parentGodotIdx = idMap[parentIdByte];
                // Avoid self-parenting if the parent resolved to itself (e.g. all-zero idMap).
                if (parentGodotIdx != godotIdx)
                {
                    godotSkel.SetBoneParent(godotIdx, parentGodotIdx);
                }
            }

            // Rest transform: convert from legacy left-handed (negate X) to Godot right-handed.
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_trans_x/y/z @ +8.
            // spec: WorldCoordinates.cs — "mesh-local geometry uses negate X".
            Vec3 lt = bone.Translation;
            var restOrigin = new Vector3(-lt.X, lt.Y, lt.Z);

            // Quaternion handedness conversion for negate-X basis:
            //   q_godot = Quaternion(-qx, qy, -qz, qw)
            // spec: Docs/RE/formats/mesh.md §Quaternion component order — XYZW on disk.
            Quat lr = bone.Rotation;
            var restQuat = new Quaternion(-lr.X, lr.Y, -lr.Z, lr.W).Normalized();

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
    /// spec: Docs/RE/formats/mesh.md §Vertex record — negate X handedness flip.
    /// </summary>
    private static MeshInstance3D? BuildSkinnedMesh(
        SkinnedMesh skn,
        Skeleton3D godotSkeleton,
        int[] boneIdToGodotIndex,
        ImageTexture? albedo)
    {
        int faceCount = (int)skn.FaceCount;
        if (faceCount == 0 || skn.Positions.Length == 0)
        {
            GD.Print($"[SkinnedCharacterBuilder] Mesh '{skn.Name}' has no geometry — skipping.");
            return null;
        }

        // --- Accumulate per-geometry-vertex bone influences ---
        // Each geometry vertex can have multiple weight records pointing at it.
        // We take up to MaxInfluences (4) per vertex, then normalise.
        // spec: Docs/RE/formats/mesh.md §Weight record — vertex_index, bone_index, weight.
        int geoVertCount = skn.Positions.Length;
        AccumulateWeights(skn.Weights, geoVertCount, boneIdToGodotIndex,
            out int[] perGeoVtxBones, // [geoVertCount × MaxInfluences] Godot bone indices
            out float[] perGeoVtxWts); // [geoVertCount × MaxInfluences] normalised weights

        // --- Build flat rendered-vertex arrays ---
        int totalVerts = faceCount * 3;
        var positions = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];

        // Godot's Bones array is int[] (4 ints per vertex).
        // Godot's Weights array is float[] (4 floats per vertex).
        // Both are indexed identically.
        // spec: Godot 4 ArrayMesh — Bones/Weights arrays must be 4 × vertexCount.
        var bones = new int[totalVerts * MaxInfluences];
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

                // Clamp corrupt indices to 0; log once per face.
                if (vi >= (uint)geoVertCount)
                {
                    GD.PrintErr($"[SkinnedCharacterBuilder] Face {f} corner {j}: " +
                                $"VertexIndex {vi} out of range (posCount={geoVertCount}) — using 0.");
                    vi = 0;
                }

                Vec3 p = srcPos[vi];
                Vec3 n = (vi < (uint)srcNrm.Length) ? srcNrm[vi] : new Vec3(0f, 1f, 0f);

                // Handedness flip: negate X (mesh-local convention).
                // spec: Docs/RE/formats/mesh.md §Vertex record — pos_x @ sub-offset 12.
                // spec: WorldCoordinates.cs — "mesh-local geometry uses negate X".
                positions[vBase + j] = new Vector3(-p.X, p.Y, p.Z);

                // Normal: negate X.
                // spec: Docs/RE/formats/mesh.md §Vertex record — normal_x @ sub-offset 0.
                normals[vBase + j] = new Vector3(-n.X, n.Y, n.Z).Normalized();

                // UV: already v-flipped by the parser (1.0f - uv_v_on_disk).
                // spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v".
                uvs[vBase + j] = new Vector2(corner.UvU, corner.UvV);

                // Copy bone influences from the per-geometry-vertex accumulator into the flat array.
                int dstBase = (vBase + j) * MaxInfluences;
                int srcBase = (int)vi * MaxInfluences;
                for (int inf = 0; inf < MaxInfluences; inf++)
                {
                    bones[dstBase + inf] = perGeoVtxBones[srcBase + inf];
                    weights[dstBase + inf] = perGeoVtxWts[srcBase + inf];
                }
            }
        }

        // --- Assemble ArrayMesh ---
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Bones] = bones;
        arrays[(int)Mesh.ArrayType.Weights] = weights;
        // No index array — flat unindexed layout mirrors SknMeshBuilder.

        var arrayMesh = new ArrayMesh();

        // Add surface with default flags (4-bone skinning).
        // Do NOT pass FlagUse8BoneWeights — that would require 8 entries per vertex.
        // The standard 4-influence path uses the default (no special compress flag).
        // spec: Godot 4 ArrayMesh.AddSurfaceFromArrays — default flags = 4-bone skinning.
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // --- Material ---
        var mat = new StandardMaterial3D();
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        // Double-sided is safe for now — character skins may have thin geometry.
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
        // spec: Godot 4 Skeleton3D.CreateSkinFromRestTransforms() documentation.
        Skin skin = godotSkeleton.CreateSkinFromRestTransforms();

        // Build the NodePath that points from the MeshInstance3D (which will be a child of
        // Skeleton3D) to the Skeleton3D itself.  As a direct child, the path is "..".
        var meshInst = new MeshInstance3D
        {
            Name = $"SkinnedMesh_{skn.Name}",
            Mesh = arrayMesh,
            Skin = skin,
            // The Skeleton property is a NodePath relative to the MeshInstance3D.
            // As a child of Skeleton3D the path to the parent is "..".
            // spec: Godot 4 MeshInstance3D.Skeleton documentation.
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
    /// spec: Docs/RE/formats/mesh.md §Weight / skin table — "post-load invariant: engine
    ///       normalizes weights per vertex so the per-vertex sum equals 1.0."
    /// spec: Docs/RE/formats/mesh.md §Weight record — "records where weight &lt; 0.01 are skipped."
    /// </summary>
    private static void AccumulateWeights(
        SknWeight[] weightRecords,
        int geoVertCount,
        int[] boneIdToGodotIndex,
        out int[] outBones,
        out float[] outWeights)
    {
        // Working storage: each vertex holds up to MaxInfluences (boneIdx, weight) pairs.
        // We use parallel arrays sized [geoVertCount × MaxInfluences].
        outBones = new int[geoVertCount * MaxInfluences];
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

            // Map the on-disk bone_index (which is a .bnd SelfId low-byte value) to the
            // Godot bone index via the id→godotIndex lookup table.
            // spec: Docs/RE/formats/mesh.md §Weight record — bone_index "zero-based index into
            //       the associated bind-pose bone array."  Matches Bone.SelfId low byte.
            byte boneId = (byte)(wr.BoneIndex & 0xFF);
            int godotBoneIdx = boneIdToGodotIndex[boneId];

            int current = influenceCounts[vi];
            int baseIdx = vi * MaxInfluences;

            if (current < MaxInfluences)
            {
                // Slot available — write directly.
                outBones[baseIdx + current] = godotBoneIdx;
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
                    if (w < minW)
                    {
                        minW = w;
                        minSlot = s;
                    }
                }

                if (wr.Weight > minW)
                {
                    outBones[baseIdx + minSlot] = godotBoneIdx;
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
                // This is the single-bone item-skin default path.
                outBones[baseIdx] = 0;
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
    /// <para>Track naming: <c>SkeletonNodeName/bone_N</c> where N is the Bone.SelfId low byte,
    /// expressed as a <see cref="NodePath"/> relative to the AnimationPlayer's root
    /// (i.e. relative to the parent <see cref="Node3D"/> root that also owns the Skeleton3D).</para>
    ///
    /// <para>Keyframe timing: key index K is placed at time K × 0.1 s (10 fps fixed).</para>
    ///
    /// <para>Loop mode: <see cref="Animation.LoopModeEnum.Linear"/> — identical to the runtime
    /// CycleLayer wrap behaviour described in the spec.</para>
    ///
    /// spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
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
        // spec: Docs/RE/formats/animation.md §Timing — "duration = frame_count × 0.1". CONFIRMED.
        double durationSecs = clip.FrameCount * (double)SecondsPerFrame;

        var anim = new Animation();
        anim.Length = (float)durationSecs;
        anim.LoopMode = Animation.LoopModeEnum.Linear;
        // spec: Docs/RE/formats/animation.md §Wrap and loop — CycleLayer is "looping; replays
        //       continuously". Godot Linear loop is the equivalent.

        // Build a reverse lookup: SelfId low byte → bone name (for track path construction).
        // If no skeleton is supplied, every track bone_id maps to "root" (bone 0).
        var boneIdToName = BuildBoneNameMap(skeleton);

        foreach (AnimationTrack track in clip.Tracks)
        {
            // spec: Docs/RE/formats/animation.md §Per-track record — bone_id = low byte of track_descriptor.
            byte boneId = track.BoneId;
            string boneName = boneIdToName.TryGetValue(boneId, out string? n) ? n : $"bone_{boneId}";

            if (track.Keyframes.Length == 0)
                continue;

            // Track path: from the AnimationPlayer's root to the Skeleton3D's bone.
            // AnimationPlayer is a child of the root Node3D; Skeleton3D is also a child.
            // So the path is "SkeletonNodeName:boneName".
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

                // Translation: negate X for legacy→Godot handedness.
                // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_trans_x: CONFIRMED.
                // spec: WorldCoordinates.cs — "mesh-local geometry uses negate X".
                Vec3 tr = kf.Translation;
                var godotPos = new Vector3(-tr.X, tr.Y, tr.Z);

                // Rotation quaternion: q_godot = Quaternion(-qx, qy, -qz, qw) for negate-X basis.
                // spec: Docs/RE/formats/mesh.md §Quaternion component order — XYZW on disk.
                Quat rq = kf.Rotation;
                var godotQuat = new Quaternion(-rq.X, rq.Y, -rq.Z, rq.W).Normalized();

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
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a dictionary from Bone.SelfId low byte → Godot bone name (e.g. "bone_0").
    /// Used to construct animation track paths.
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