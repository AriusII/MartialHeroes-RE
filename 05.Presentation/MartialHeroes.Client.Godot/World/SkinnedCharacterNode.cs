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

    private ArrayMesh? _arrayMesh;
    private MeshInstance3D? _meshInstance;
    private StandardMaterial3D? _material;

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
    // keyframe rotation is a RIGHT (post) multiply DELTA on top of the bind-local rotation
    // (parentWorld ⊗ bindLocal ⊗ animLocal), NOT a literal replacement of the bind-local rotation.
    //
    // VALIDATED EMPIRICALLY (the fix for the exploded char-select preview): the canonical g1 player
    // rig (g202110001.skn + g1.bnd + idle g111100010.mot) renders an INTACT mesh under the delta
    // form and shatters into flying triangle shards under the replacement form. Decisively, under
    // the delta form the rig FROZEN at idle frame 0 is pixel-identical to the bind-pose rest mesh —
    // proving the §6.4 wording ("replacement") describes the per-pass accumulator, while the WORLD
    // walk (§6.5/§6.6) composes the sampled rotation as a delta on top of bind-local. The replacement
    // reading drops the bindLocal factor and explodes any rig whose frame-0 keyframes are small
    // deltas rather than full local rotations.
    //
    // Internal so only this presentation assembly can toggle it; the validated default is delta=true.
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

        // 1) Hierarchy + bind world.
        // spec: Docs/RE/specs/skinning.md §3.
        SkinningMath.ResolveHierarchy(_bones, out _parentIndex, out int[] idToIndex, out int baseId);
        SkinningMath.BoneTransform[] bindWorld = SkinningMath.AccumulateBindWorld(_bones, _parentIndex);

        int vertexCount = mesh.Positions.Length;

        // 2) Influences (grouped, ID-resolved, normalized) + inverse-bind bake.
        // spec: Docs/RE/specs/skinning.md §4, §5.
        _perVertex = SkinningMath.BuildInfluences(mesh.Weights, vertexCount, idToIndex, baseId, boneCount);
        SkinningMath.BakeInverseBind(_perVertex, mesh.Positions, mesh.Normals, bindWorld);

        // 3) Per-bone track binding (by bone ID → array index).
        // spec: Docs/RE/formats/animation.md §Bone-track linkage — bone_id matches Bone.SelfId.
        _trackByBoneIndex = new AnimationTrack?[boneCount];
        if (clip is not null && clip.FrameCount > 0)
        {
            foreach (AnimationTrack tr in clip.Tracks)
            {
                int bid = tr.BoneId & 0xFF;
                int bIdx = (bid >= 0 && bid < 256) ? idToIndex[bid] : -1;
                if (bIdx < 0)
                {
                    int off = tr.BoneId - baseId;
                    bIdx = (off >= 0 && off < boneCount) ? off : -1;
                }

                if (bIdx >= 0 && bIdx < boneCount) _trackByBoneIndex[bIdx] = tr;
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

        // 6) Material.
        _material = new StandardMaterial3D
        {
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, // double-sided safety for thin geometry
        };
        if (albedo is not null) _material.AlbedoTexture = albedo;
        else _material.AlbedoColor = new Color(0.85f, 0.75f, 0.65f, 1f);

        // 7) Build the initial (rest-pose) mesh so the node is visible even before the first tick.
        _arrayMesh = new ArrayMesh();
        DeformAndUpload(0f, restPose: true);

        _meshInstance = new MeshInstance3D { Name = "LbsMesh", Mesh = _arrayMesh };
        AddChild(_meshInstance);

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
        if (_arrayMesh is null) return;

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

        // The per-upload Godot.Collections.Array allocation is an ENGINE API CONSTRAINT, not an
        // oversight: ArrayMesh.AddSurfaceFromArrays takes a Godot.Collections.Array of length
        // ArrayType.Max, which must be freshly built each upload (it is consumed by the native call
        // and cannot be safely reused/pooled across frames). The expensive per-vertex work above
        // already runs in reused buffers; do NOT "optimize" this array away. spec: Godot ArrayMesh API.
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _outPos;
        arrays[(int)Mesh.ArrayType.Normal] = _outNrm;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;

        _arrayMesh.ClearSurfaces();
        _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (_material is not null) _arrayMesh.SurfaceSetMaterial(0, _material);
    }

    // Rest-pose tracks (all null) reused so the rest path allocates nothing per frame.
    private AnimationTrack?[] _noTracks = [];

    /// <summary>Computes per-bone animated world transforms into the reused <c>_world</c> buffer.</summary>
    private void ComputeWorldPoses(float t, bool restPose)
    {
        AnimationTrack?[] tracks = restPose ? _noTracks : _trackByBoneIndex;
        SkinningMath.ComputeAnimatedWorld(
            _bones, _parentIndex, tracks, t, RenormalizeAlpha, _world, AnimAsDelta);
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

        // ---- Invariant 2: liveness (same tracked vertex at two distinct clip times) ----
        if (_hasClip && _clipDuration > 0f)
        {
            int sampleVi = FindTrackedVertex();
            d.LivenessVertex = sampleVi;
            if (sampleVi >= 0)
            {
                float t0 = 0f;
                float t1 = Math.Min(_clipDuration * 0.5f, _clipDuration - 0.01f);

                ComputeWorldPoses(t0, restPose: false);
                (Vec3 a, _) = SkinningMath.DeformVertex(_perVertex[sampleVi], _world);
                ComputeWorldPoses(t1, restPose: false);
                (Vec3 b, _) = SkinningMath.DeformVertex(_perVertex[sampleVi], _world);

                d.LivenessT0 = t0;
                d.LivenessT1 = t1;
                d.LivenessP0 = new Vector3(a.X, a.Y, a.Z);
                d.LivenessP1 = new Vector3(b.X, b.Y, b.Z);
                d.LivenessDelta = (d.LivenessP1 - d.LivenessP0).Length();
            }
        }

        // Restore the visible mesh to the rest pose; _Process will drive it once in the tree.
        DeformAndUpload(0f, restPose: true);
        _time = 0f;
        return d;
    }

    /// <summary>Finds a vertex whose dominant bone is animated by a track (for the liveness probe).</summary>
    private int FindTrackedVertex()
    {
        for (int v = 0; v < _perVertex.Length; v++)
        {
            Influence_Best(v, out int bone);
            if (bone >= 0 && bone < _trackByBoneIndex.Length && _trackByBoneIndex[bone] is not null)
                return v;
        }

        return _perVertex.Length > 0 ? 0 : -1;
    }

    private void Influence_Best(int v, out int boneIndex)
    {
        boneIndex = -1;
        float best = -1f;
        SkinningMath.Influence[] items = _perVertex[v].Items;
        for (int k = 0; k < items.Length; k++)
        {
            if (items[k].Weight > best)
            {
                best = items[k].Weight;
                boneIndex = items[k].BoneIndex;
            }
        }
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