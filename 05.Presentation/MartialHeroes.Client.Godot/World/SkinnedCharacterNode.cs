// World/SkinnedCharacterNode.cs
//
// Godot glue for the faithful CPU linear-blend-skinning (LBS) path. The non-trivial math lives
// in the engine-free SkinningMath helper; this node only:
//   - holds the precomputed rig (bind world, baked inverse-bind influences, per-bone tracks),
//   - rebuilds the deformed ArrayMesh surface each frame from the sampled .mot pose,
//   - applies the SINGLE project handedness conversion (world Z-negate) to the final output.
//
// Design choice: faithful CPU LBS (rebuild ArrayMesh vertices per frame from sampled bone poses)
// rather than Godot Skeleton3D+Skin. This matches the recovered legacy engine exactly, is trivial
// at a few hundred verts, and is directly checkable against the spec math (the rest-pose
// cancellation invariant is asserted numerically in BuildDiagnostics()).
//
// spec: Docs/RE/specs/skinning.md §1 (CPU LBS, no GPU bone palette), §8(a) (preserve the
//       bind/inverse-bind cancellation), §8(b) (single handedness conversion).
//
// NEVER uses GltfDocument — builds ArrayMesh directly (Bud/Skn MeshBuilder pattern).

using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// A skinned character rendered by per-frame CPU LBS. Owns its own ArrayMesh and updates it
/// from the idle <c>.mot</c> clip each frame.
///
/// spec: Docs/RE/specs/skinning.md (linear-blend skinning, inverse-bind, pose composition).
/// </summary>
public sealed partial class SkinnedCharacterNode : Node3D
{
    // ---- Precomputed rig (immutable after Setup) ----
    private Bone[] _bones = [];
    private int[] _parentIndex = [];
    private bool[] _hasChild = []; // per-bone: parent of ≥1 bone (§6.3 interior-bone lock)
    private float[] _nodeScale = []; // per-bone runtime scale (+84, default 1.0; SPEC GAP source)
    private SkinningMath.VertexInfluences[] _perVertex = [];
    private AnimationTrack?[] _trackByBoneIndex = [];

    // Render topology: flat unindexed corner list. _cornerVertex[c] = unique vertex index.
    private int[] _cornerVertex = [];
    private Vector2[] _uvs = [];

    // Reused per-frame buffers.
    private SkinningMath.BoneTransform[] _world = []; // per bone, animated world transform (native)
    private Vec3[] _deformedPos = []; // per unique vertex, native space
    private Vec3[] _deformedNrm = []; // per unique vertex, native space
    private Vector3[] _outPos = []; // per corner, Godot space
    private Vector3[] _outNrm = []; // per corner, Godot space

    private ArrayMesh? _arrayMesh; // live render mesh (also used for BuildDiagnostics AABB sampling)
    private MeshInstance3D? _meshInstance;
    private Material? _material;

    private float _clipDuration;
    private bool _hasClip;
    private float _time;
    private bool _ready;

    // When true, the node does NOT self-drive from _Process; the owner pumps it via Tick(dt).
    // The player path leaves this false (per-frame _Process). The town's 40 mobs set it true so
    // NpcRenderer can throttle their skinning to ~10 Hz and stagger the ticks across frames.
    // This is a pure scheduling change — the deform math and the rest-pose cancellation invariant
    // are identical either way. spec: Docs/RE/formats/animation.md §Timing — original runs at 10 fps.
    private bool _externalDrive;

    // Interpolation choice for .mot sampling. Smoothed (renormalized alpha) for a modern look.
    // spec: Docs/RE/specs/skinning.md §8(c) — "Smoothed (recommended): renormalize alpha /= 0.1."
    private const bool RenormalizeAlpha = true;

    // Animated-rotation composition mode. spec: Docs/RE/specs/skinning.md §6.5/§6.6 — the sampled
    // keyframe rotation is composed as a RIGHT (post) multiply on top of the bind-local rotation in
    // the world walk: worldQuat = parentWorld ⊗ bindLocal ⊗ animLocal (so the committed local pose
    // for a tracked bone is bindLocalQ ⊗ sampledRot). It is NOT a literal replacement (localQ = sR).
    //
    // DATA-PROVEN to be the correct mode (char-create-preview campaign). Composing the real VFS idle
    // keyframes against the live parser DLLs and measuring the mean per-vertex displacement of the
    // animated frame-0 deform from the authored rest mesh — an idle's first frame is a calm standing
    // pose, so it must stay NEAR the authored rig — gives, for the §8(d)/§8(e) trios:
    //     rig         DELTA   REPLACEMENT   (model extent)
    //     g1 Warrior  0.47    1.97          (5.0)
    //     g4 Monk     1.17    2.81          (8.8)
    //     g2048 Mob   0.02    3.55          (7.8)
    // DELTA keeps every idle frame-0 close to its authored pose (the mob is essentially identical,
    // 0.02), while REPLACEMENT flings the whole mesh ~half the model extent away — decisively wrong.
    // (The §6.4 "replacement" wording describes the per-pass MIXER accumulator, not the world walk;
    // the keyframes carry full-magnitude values but are composed as deltas on top of bindLocal.)
    //
    // Internal so only this presentation assembly can toggle it; the validated default is delta=true.
    // spec: Docs/RE/specs/skinning.md §6.5/§6.6 (parentWorld ⊗ bindLocal ⊗ animLocal).
    internal static bool AnimAsDelta { get; set; } = true;

    /// <summary>
    /// Builds the rig from parsed data. Must be called once before the node ticks.
    /// Performs: hierarchy resolution, bind-world accumulation, influence build, inverse-bind bake,
    /// per-bone track binding, and the static rest ArrayMesh.
    /// </summary>
    public void Setup(
        SkinnedMesh mesh,
        Skeleton skeleton,
        AnimationClip? clip,
        ImageTexture? albedo,
        bool externalDrive = false,
        float startPhaseSeconds = 0f)
    {
        _externalDrive = externalDrive;
        _bones = skeleton.Bones;
        int boneCount = _bones.Length;

        // 1) Hierarchy + bind world. Also build the per-bone has-child flag for the §6.3
        // interior-bone translation lock.
        // spec: Docs/RE/specs/skinning.md §3 / §6.3.
        SkinningMath.ResolveHierarchy(_bones, out _parentIndex, out int[] idToIndex, out int baseId, out _hasChild);
        SkinningMath.BoneTransform[] bindWorld = SkinningMath.AccumulateBindWorld(_bones, _parentIndex);

        // Per-bone runtime node scale (+84 field; rotate → scale → translate in the world walk, §6.6).
        // SPEC GAP: the on-disk SOURCE of the per-node scale is not yet pinned in any clean spec, so we
        // default it to 1.0 (no behaviour change; latent until the +84 source is promoted). The world
        // walk multiplies the rotated local animated translation by this before the parent-add.
        // spec-gap: per-node runtime scale (+84) disk source undecoded — default 1.0.
        // spec: Docs/RE/specs/skinning.md §6.6 / §3.4 (+84).
        _nodeScale = new float[boneCount];
        for (int i = 0; i < boneCount; i++) _nodeScale[i] = 1.0f;

        int vertexCount = mesh.Positions.Length;

        // 2) Influences (grouped, ID-resolved, normalized) + inverse-bind bake.
        // spec: Docs/RE/specs/skinning.md §4, §5.
        _perVertex = SkinningMath.BuildInfluences(mesh.Weights, vertexCount, idToIndex, baseId, boneCount);
        SkinningMath.BakeInverseBind(_perVertex, mesh.Positions, mesh.Normals, bindWorld);

        // 3) Per-bone track binding (by bone ID → array index).
        //
        // DEFENSIVE GUARD (rig/clip identity): a track whose bone_id is NOT a bone of this skeleton
        // is SKIPPED, never clamped/redirected. Clamping a stray bone_id into range would still
        // drive the WRONG bone (the precise way a wrong-rig clip shatters the mesh once it rotates
        // bones off bind). Unmatched bone_id is a NON-FATAL skip. With the rig + clip now resolved
        // from the same id_b (CharCreatePreview3D / CharPreview3D), the matched clip's track count
        // equals the rig's bone count, so this guard should drop nothing for a correct trio — it is
        // a belt-and-braces safety net against a future mismatch.
        // spec: Docs/RE/specs/skinning.md §8(e) item 4 — "SKIP (do not clamp) any clip track whose
        //       bone_id falls outside [base_id, base_id + bone_count)".
        // spec: Docs/RE/formats/animation.md §Bone-track linkage — bone_id matches Bone.SelfId.
        _trackByBoneIndex = new AnimationTrack?[boneCount];
        if (clip is not null && clip.FrameCount > 0)
        {
            int boundTracks = 0;
            int skippedTracks = 0;
            foreach (AnimationTrack tr in clip.Tracks)
            {
                // Resolve bone_id → array slot ONLY by the skeleton's own id→index map. A bone_id
                // absent from the map names a joint that does not exist on this rig → SKIP it.
                // (No "off = bone_id − base_id" salvage: that is exactly the clamp-into-range the
                // spec forbids, since the offset could land on an unrelated bone of the wrong rig.)
                int bid = tr.BoneId & 0xFF;
                int bIdx = (bid >= 0 && bid < 256) ? idToIndex[bid] : -1;

                if (bIdx >= 0 && bIdx < boneCount)
                {
                    _trackByBoneIndex[bIdx] = tr;
                    boundTracks++;
                }
                else
                {
                    skippedTracks++;
                }
            }

            if (skippedTracks > 0)
            {
                GD.PrintErr($"[Skinning] '{mesh.Name}': SKIPPED {skippedTracks} clip track(s) whose " +
                            $"bone_id is not a bone of this {boneCount}-bone rig (base_id={baseId}); " +
                            $"bound {boundTracks}. spec: skinning.md §8(e) item 4 — skip, do not clamp.");
            }

            // Duration = frame_count × 0.1 (10 fps).
            // spec: Docs/RE/formats/animation.md §Timing. CONFIRMED.
            _clipDuration = clip.FrameCount * SkinningMath.MotSecondsPerFrame;
            _hasClip = _clipDuration > 0f;
        }

        // 4) Render topology: flat unindexed corner list with CW→CCW winding swap.
        // spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap [0,2,1] for Godot CCW.
        int faceCount = (int)mesh.FaceCount;
        int cornerCount = faceCount * 3;
        _cornerVertex = new int[cornerCount];
        _uvs = new Vector2[cornerCount];
        SknCorner[] corners = mesh.Corners;
        for (int f = 0; f < faceCount; f++)
        {
            int cBase = f * 3;
            // CCW: emit source corners 0,2,1 into output slots 0,1,2.
            int[] order = [cBase + 0, cBase + 2, cBase + 1];
            for (int j = 0; j < 3; j++)
            {
                SknCorner corner = corners[order[j]];
                uint vi = corner.VertexIndex;
                if (vi >= (uint)vertexCount) vi = 0;
                _cornerVertex[cBase + j] = (int)vi;
                _uvs[cBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        // 5) Reusable buffers.
        _world = new SkinningMath.BoneTransform[boneCount];
        _noTracks = new AnimationTrack?[boneCount];
        _deformedPos = new Vec3[vertexCount];
        _deformedNrm = new Vec3[vertexCount];
        _outPos = new Vector3[cornerCount];
        _outNrm = new Vector3[cornerCount];

        // 6) Material — apply cel/toon ShaderMaterial (CelShadeMaterialFactory) when CelEnabled;
        // fall back to StandardMaterial3D for debugging or when the shader is unavailable.
        // spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
        // spec: Docs/RE/formats/shaders.md §C5 — Runtime Cel/Glow Shader Set, Campaign 5.
        // spec: CLAUDE.md asset chain — skin.txt col5 texId → data/char/tex{dir}/{texId}.png.
        if (CelShadeMaterialFactory.CelEnabled)
        {
            try
            {
                _material = CelShadeMaterialFactory.Build(albedo);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Skinning] CelShadeMaterialFactory.Build failed for '{mesh.Name}': {ex.Message} " +
                            "— falling back to StandardMaterial3D.");
                _material = null;
            }
        }

        if (_material is null)
        {
            // Fallback: StandardMaterial3D (non-cel, flat PBR).
            var stdMat = new StandardMaterial3D
            {
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            if (albedo is not null)
                stdMat.AlbedoTexture = albedo;
            else
                stdMat.AlbedoColor = new Color(0.85f, 0.75f, 0.65f, 1f);
            _material = stdMat;
        }

        // 7) Build the live render mesh using ArrayMesh (updated per-frame via ClearSurfaces +
        // AddSurfaceFromArrays). The MeshInstance3D owns the ArrayMesh directly so
        // SetSurfaceOverrideMaterial is valid after the first DeformAndUpload call adds surface 0.
        // Note: _immMesh was previously referenced here but never populated with vertices — the
        // DeformAndUpload path always wrote to _arrayMesh. The MeshInstance3D.Mesh is now _arrayMesh.
        _arrayMesh = new ArrayMesh();
        _meshInstance = new MeshInstance3D { Name = "LbsMesh", Mesh = _arrayMesh };
        AddChild(_meshInstance);
        DeformAndUpload(0f, restPose: true); // fills surface 0 on _arrayMesh first
        // Set the material AFTER DeformAndUpload so surface 0 exists when SetSurfaceOverrideMaterial runs.
        if (_material is not null)
            _meshInstance.SetSurfaceOverrideMaterial(0, _material);

        // Randomized clip start phase so a town of mobs sharing one idle clip does not animate in
        // lockstep. Wrapped into [0, duration). No-op for the player (default 0). The visible mesh
        // is left in the rest pose here; the first Tick/_Process advances it from this phase.
        if (_hasClip && _clipDuration > 0f && startPhaseSeconds != 0f)
            _time = startPhaseSeconds % _clipDuration;

        // DIAGNOSTIC (Fix 8): prove the idle clip actually loaded with >1 frame. A pixel-identical
        // pair of liveness screenshots is ambiguous between "clip never loaded" and "probe captured
        // the same frame"; this line disambiguates by logging the clip's frame count + duration.
        // spec: Docs/RE/formats/animation.md §Timing — 10 fps, duration = frame_count × 0.1.
        uint frameCount = clip?.FrameCount ?? 0u;
        GD.Print($"[Skinning] Setup '{mesh.Name}': hasClip={_hasClip} clipFrameCount={frameCount} " +
                 $"clipDuration={_clipDuration:F2}s externalDrive={externalDrive}.");

        _ready = true;
    }

    /// <summary>Current ArrayMesh AABB (rest pose after Setup), for recentring.</summary>
    public Aabb GetMeshAabb() => _arrayMesh?.GetAabb() ?? new Aabb();

    /// <summary>
    /// AABB of the DISPLAYED animated pose at clip frame 0 (Godot space), or the rest AABB when
    /// there is no clip. Used by the builder to derive the stand-up pivot and recentre from the pose
    /// that is actually rendered — not the raw bind pose, which can have a different tallest axis.
    ///
    /// Why this matters: a rig authored lying along X (X-tallest at rest) whose idle stands it
    /// upright on Y (Y-tallest animated) would be double-rotated if the pivot were derived from the
    /// rest pose. Deriving from the animated frame-0 pose makes the pivot reflect what is on screen.
    /// For the g1 World player (X-tallest in BOTH rest and animation) this returns the same tallest
    /// axis as the rest AABB, so the World rendering is provably unchanged.
    /// spec: Docs/RE/specs/skinning.md §6 (the displayed pose is the sampled idle, not the bind pose);
    ///       §8(b) (single handedness conversion, applied at output here too).
    /// </summary>
    public Aabb GetDisplayedFrame0Aabb()
    {
        if (_arrayMesh is null) return new Aabb();
        if (!_hasClip)
            return _arrayMesh.GetAabb(); // no clip → rest is the displayed pose

        // Render frame 0 of the idle, read its AABB, then restore the rest pose so the visible mesh
        // and _time are left exactly as Setup left them (the first Tick/_Process re-advances).
        DeformAndUpload(0f, restPose: false);
        Aabb animated = _arrayMesh.GetAabb();
        DeformAndUpload(0f, restPose: true);
        return animated;
    }

    public override void _Process(double delta)
    {
        // Externally-driven nodes (the town mobs) are pumped by NpcRenderer at ~10 Hz instead, so
        // the engine's per-frame _Process must not also advance them (that would double-tick and
        // defeat the throttle). The player path leaves _externalDrive false and self-drives here.
        if (_externalDrive) return;
        Advance((float)delta);
    }

    /// <summary>
    /// Advances the idle clip by <paramref name="dtSeconds"/> and re-uploads the deformed surface.
    /// Used by the throttled owner (NpcRenderer) for externally-driven nodes. The accumulated dt
    /// makes ~10 Hz updates visually equivalent to per-frame ones: the clip time still advances by
    /// real elapsed seconds, only the resample cadence is coarser (matching the original 10 fps).
    /// spec: Docs/RE/formats/animation.md §Timing — fixed 10 fps clip rate.
    /// </summary>
    public void Tick(float dtSeconds)
    {
        if (!_externalDrive) return; // self-driven nodes ignore external pumps
        Advance(dtSeconds);
    }

    /// <summary>Shared clip-advance + upload. spec: animation.md §Wrap and loop behaviour (modulo).</summary>
    private void Advance(float dtSeconds)
    {
        if (!_ready || !_hasClip || _arrayMesh is null) return;

        _time += dtSeconds;
        if (_clipDuration > 0f && _time >= _clipDuration)
            _time %= _clipDuration; // CycleLayer loop. spec: animation.md §Wrap and loop behaviour.

        DeformAndUpload(_time, restPose: false);
    }

    /// <summary>
    /// Runs the full per-frame pipeline at clip time <paramref name="t"/> and uploads the result
    /// to the single ArrayMesh surface. When <paramref name="restPose"/> is true the animated pose
    /// is forced to the bind pose (used for the initial build and the rest-cancellation diagnostic).
    /// </summary>
    private void DeformAndUpload(float t, bool restPose)
    {
        if (_arrayMesh is null || _meshInstance is null) return;

        ComputeWorldPoses(t, restPose);

        // LBS deform every unique vertex in native space.
        for (int v = 0; v < _deformedPos.Length; v++)
        {
            (_deformedPos[v], _deformedNrm[v]) = SkinningMath.DeformVertex(_perVertex[v], _world);
        }

        // Expand to corners and apply the single handedness conversion at the output.
        // spec: Docs/RE/specs/skinning.md §8(b) — single conversion (world Z-negate) at output.
        for (int c = 0; c < _cornerVertex.Length; c++)
        {
            int vi = _cornerVertex[c];
            Vec3 p = _deformedPos[vi];
            Vec3 n = _deformedNrm[vi];
            var (gx, gy, gz) = WorldCoordinates.SkinToGodot(p.X, p.Y, p.Z);
            var (nx, ny, nz) = WorldCoordinates.SkinToGodot(n.X, n.Y, n.Z);
            _outPos[c] = new Vector3(gx, gy, gz);
            _outNrm[c] = new Vector3(nx, ny, nz).Normalized();
        }

        // Reuse the persistent _arrayMesh: ClearSurfaces then re-add the deformed surface.
        // The material is held as a SetSurfaceOverrideMaterial on the MeshInstance3D (not on the
        // ArrayMesh surface), so it survives ClearSurfaces without any per-frame re-assignment.
        //
        // The per-upload Godot.Collections.Array allocation is an ENGINE API CONSTRAINT — Godot's
        // AddSurfaceFromArrays accepts only a Variant-typed Array; keep the reused CPU buffers above.
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _outPos;
        arrays[(int)Mesh.ArrayType.Normal] = _outNrm;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;

        _arrayMesh!.ClearSurfaces();
        _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        // Re-apply the material to surface 0 after ClearSurfaces wipes it.
        // SetSurfaceOverrideMaterial on MeshInstance3D is NOT called per-frame (it was set once in
        // Setup after the first DeformAndUpload, and it persists across ClearSurfaces).
        _arrayMesh.SurfaceSetMaterial(0, _material);
    }

    // Rest-pose tracks (all null) reused so the rest path allocates nothing per frame.
    private AnimationTrack?[] _noTracks = [];

    /// <summary>Computes per-bone animated world transforms into the reused <c>_world</c> buffer.</summary>
    private void ComputeWorldPoses(float t, bool restPose)
    {
        AnimationTrack?[] tracks = restPose ? _noTracks : _trackByBoneIndex;
        SkinningMath.ComputeAnimatedWorld(
            _bones, _parentIndex, tracks, t, RenormalizeAlpha, _world, AnimAsDelta,
            _hasChild, _nodeScale);
    }

    // =========================================================================
    // Diagnostics (headless verification of the mandatory invariants)
    // =========================================================================

    /// <summary>
    /// Computes the rest-pose cancellation deviation, the rest AABB, and a liveness sample, and
    /// returns them for the orchestrator to print. Run after <see cref="Setup"/>.
    ///
    /// Invariant 1 (REST CANCELLATION): with the bind pose, the deformed vertices must equal the
    /// rest model vertices (max deviation ~0, &lt; 1e-3). spec: skinning.md §0 / §8(a).
    /// Invariant 2 (LIVENESS): a tracked vertex must move between two clip times.
    /// Invariant 3 (AABB): finite, human-sized.
    /// </summary>
    public SkinDiagnostics BuildDiagnostics(SkinnedMesh mesh)
    {
        var d = new SkinDiagnostics();

        // ---- Invariant 1: rest cancellation (native space, against model-space rest verts) ----
        ComputeWorldPoses(0f, restPose: true);
        float maxDev = 0f;
        for (int v = 0; v < _deformedPos.Length; v++)
        {
            (Vec3 p, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
            Vec3 r = mesh.Positions[v];
            float dx = p.X - r.X, dy = p.Y - r.Y, dz = p.Z - r.Z;
            float dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dev > maxDev) maxDev = dev;
        }

        d.MaxRestDeviation = maxDev;

        // ---- Invariant 3: rest-pose AABB in Godot space ----
        DeformAndUpload(0f, restPose: true);
        if (_arrayMesh is not null)
        {
            Aabb aabb = _arrayMesh.GetAabb();
            d.RestAabbPos = aabb.Position;
            d.RestAabbSize = aabb.Size;
            d.AabbFinite = IsFinite(aabb.Position) && IsFinite(aabb.Size);
        }

        // ---- Invariant 2: liveness (the MOST-MOVING vertex across the clip) ----
        //
        // The earlier probe locked onto ONE tracked vertex (FindTrackedVertex returns the first
        // vertex whose dominant bone is tracked, falling back to v0). For a SHORT human idle clip
        // (e.g. a 3-frame stand whose duration is 0.3s) that single vertex was frequently dominated
        // by the ROOT — which an idle barely translates/rotates — so it read liveDelta≈0 and FAILED
        // INV2 even though the limb verts DO move. The 36f/121f mob clips happened to pick a moving
        // vertex and passed. The probe, not the clip, was the discriminator. We now scan EVERY vertex
        // over SEVERAL sample times spanning the clip and report the GLOBAL MAXIMUM displacement from
        // the frame-0 pose, plus the vertex that achieved it. This disambiguates "genuinely near-static
        // idle" (every vertex's max ≈ 0) from "the probe sat on a still vertex" (some vertex moves).
        // The deform math is unchanged — this only changes which vertex the diagnostic inspects — so
        // the working mob/world multi-frame behaviour is provably untouched (same DeformVertex calls).
        // spec: Docs/RE/specs/skinning.md §6 (the displayed pose is the sampled idle) /
        //       formats/animation.md §Timing (10 fps; a 3-frame idle spans 0.3s).
        if (_hasClip && _clipDuration > 0f && _deformedPos.Length > 0)
        {
            // Reference pose at frame 0.
            ComputeWorldPoses(0f, restPose: false);
            int vc = _deformedPos.Length;
            var p0 = new Vec3[vc];
            for (int v = 0; v < vc; v++)
                (p0[v], _) = SkinningMath.DeformVertex(_perVertex[v], _world);

            // Sample times spanning the clip (skip 0; include the last interior frame). For a 3-frame
            // 0.3s idle this hits t≈0.075/0.15/0.225/0.29 — enough to catch a subtle idle's peak.
            float[] sampleTimes = LivenessSampleTimes(_clipDuration);

            float bestDelta = 0f;
            int bestVi = 0;
            float bestT = 0f;
            Vector3 bestP0 = Vector3.Zero, bestP1 = Vector3.Zero;
            foreach (float t in sampleTimes)
            {
                ComputeWorldPoses(t, restPose: false);
                for (int v = 0; v < vc; v++)
                {
                    (Vec3 pt, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
                    float dx = pt.X - p0[v].X, dy = pt.Y - p0[v].Y, dz = pt.Z - p0[v].Z;
                    float dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dev > bestDelta)
                    {
                        bestDelta = dev;
                        bestVi = v;
                        bestT = t;
                        bestP0 = new Vector3(p0[v].X, p0[v].Y, p0[v].Z);
                        bestP1 = new Vector3(pt.X, pt.Y, pt.Z);
                    }
                }
            }

            d.LivenessVertex = bestVi;
            d.LivenessT0 = 0f;
            d.LivenessT1 = bestT;
            d.LivenessP0 = bestP0;
            d.LivenessP1 = bestP1;
            d.LivenessDelta = bestDelta;
        }

        // Restore the visible mesh to the rest pose; _Process will drive it once in the tree.
        DeformAndUpload(0f, restPose: true);
        _time = 0f;
        return d;
    }

    /// <summary>
    /// The clip times the liveness probe samples (excludes t=0, the reference). Uses up to four
    /// interior samples spread across the clip so a SHORT idle's subtle peak motion is captured;
    /// the last sample is just inside the final frame. spec: animation.md §Timing (10 fps).
    /// </summary>
    private static float[] LivenessSampleTimes(float clipDuration)
    {
        float last = MathF.Max(clipDuration - 0.01f, 0f);
        // Quarter/half/three-quarter plus the last interior frame.
        return
        [
            clipDuration * 0.25f,
            clipDuration * 0.5f,
            clipDuration * 0.75f,
            last,
        ];
    }

    private static bool IsFinite(Vector3 v)
        => !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
             || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));

    /// <summary>Diagnostic results for the mandatory skinning invariants.</summary>
    public sealed class SkinDiagnostics
    {
        public float MaxRestDeviation;
        public Vector3 RestAabbPos;
        public Vector3 RestAabbSize;
        public bool AabbFinite;
        public int LivenessVertex = -1;
        public float LivenessT0;
        public float LivenessT1;
        public Vector3 LivenessP0;
        public Vector3 LivenessP1;
        public float LivenessDelta;
    }
}