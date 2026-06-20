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
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Client.Presentation.World;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     A skinned character rendered by per-frame CPU LBS. Owns its own ArrayMesh and updates it
///     from the idle <c>.mot</c> clip each frame.
///     spec: Docs/RE/specs/skinning.md (linear-blend skinning, inverse-bind, pose composition).
/// </summary>
public sealed partial class SkinnedCharacterNode : Node3D
{
    // =========================================================================
    // EW4: Standing-idle playback + the 6-case visual-state selector (§10.3.1)
    // =========================================================================

    /// <summary>
    ///     The per-actor client visual-state word that §10.3.1 reports drives a 6-case idle-motion applier
    ///     (a 6-way jump table), each branch routing a standing actor to an <c>actormotion.txt</c> column.
    ///     The branch STRUCTURE is recovered; the concrete value → case → column mapping is
    ///     <c>live-pending (6-D)</c> and must be read off the live debugger — it must NOT be guessed from
    ///     the jump-table shape. We model the 6 cases here so the structure is faithful in code, but every
    ///     case currently resolves to the one slot we can statically resolve: the standing idle.
    ///     spec: Docs/RE/specs/skinning.md §10.3.1 (6-case visual-state idle applier; value mapping
    ///     live-pending), §10.4 (selection mechanism recovered, value → column live-pending).
    /// </summary>
    public enum VisualState
    {
        Standing = 0, // the neutral standing case → known idle slot (motion_ids_a[0] / col15, §8(e)).

        // The remaining five branches of the recovered 6-way idle applier. Which concrete visual-state
        // VALUE lands on which case — and which actormotion column each case selects — is NOT settled
        // statically (§10.3.1). Until a live debugger read confirms the value→clip mapping we do NOT
        // guess a clip for these; SelectVisualStateClip() defaults them to the standing idle.
        VisualState1 = 1, // live-pending (skinning.md §10) — column unconfirmed
        VisualState2 = 2, // live-pending (skinning.md §10) — column unconfirmed
        VisualState3 = 3, // live-pending (skinning.md §10) — column unconfirmed
        VisualState4 = 4, // live-pending (skinning.md §10) — column unconfirmed
        VisualState5 = 5 // live-pending (skinning.md §10) — column unconfirmed
    }

    // Interpolation choice for .mot sampling. Smoothed (renormalized alpha) for a modern look.
    // spec: Docs/RE/specs/skinning.md §8(c) — "Smoothed (recommended): renormalize alpha /= 0.1."
    private const bool RenormalizeAlpha = true;

    private ArrayMesh? _arrayMesh; // live render mesh (also used for BuildDiagnostics AABB sampling)

    // ---- Precomputed rig (immutable after Setup) ----
    private Bone[] _bones = [];

    private float _clipDuration;

    // Render topology: flat unindexed corner list. _cornerVertex[c] = unique vertex index.
    private int[] _cornerVertex = [];
    private Vec3[] _deformedNrm = []; // per unique vertex, native space
    private Vec3[] _deformedPos = []; // per unique vertex, native space

    // When true, the node does NOT self-drive from _Process; the owner pumps it via Tick(dt).
    // The player path leaves this false (per-frame _Process). The town's 40 mobs set it true so
    // NpcRenderer can throttle their skinning to ~10 Hz and stagger the ticks across frames.
    // This is a pure scheduling change — the deform math and the rest-pose cancellation invariant
    // are identical either way. spec: Docs/RE/formats/animation.md §Timing — original runs at 10 fps.
    private bool _externalDrive;
    private bool[] _hasChild = []; // per-bone: parent of ≥1 bone (§6.3 interior-bone lock)
    private bool _hasClip;

    // The resolved STANDING IDLE clip (motion_ids_a[0] = the actormotion col-15 idle id), supplied to
    // Setup() and held so the node can (re)start looping idle playback on _Ready / when built. The
    // upstream builder resolves this id via the §8(e) chain: data/char/actormotion.txt, the row whose
    // col2 == skin_class (the .skn header id_b), motion_ids_a[0] = column 15, record +0x40 →
    // data/char/mot/g{id}.mot. For the first human class this is the static stand g101100001.mot — a
    // faithful held pose (§10.2), NOT a missing-animation defect. We do NOT resolve any other slot here.
    // spec: Docs/RE/specs/skinning.md §8(e) (idle-clip resolution chain, col2==skin_class → col15 /
    //       motion_ids_a[0] / record +0x40), §10 / §10.2 / §10.5 (the col15 stand idle is static DATA,
    //       render it faithfully; do not synthesize a breathing idle).
    private AnimationClip? _idleClip;

    // True once a visual-state clip has been engaged for looping playback (idle by default). Diagnostic
    // affordance; the actual frame advance is the _Process / Tick → Advance path. Gated on a clip
    // being present so an offline build leaves this false and the node simply stands. spec: §10.5.
    private Material? _material;
    private MeshInstance3D? _meshInstance;
    private float[] _nodeScale = []; // per-bone runtime scale (+84, default 1.0; SPEC GAP source)

    // Rest-pose tracks (all null) reused so the rest path allocates nothing per frame.
    private AnimationTrack?[] _noTracks = [];
    private Vector3[] _outNrm = []; // per corner, Godot space
    private Vector3[] _outPos = []; // per corner, Godot space
    private int[] _parentIndex = [];
    private SkinningMath.VertexInfluences[] _perVertex = [];
    private bool _ready;
    private float _time;
    private AnimationTrack?[] _trackByBoneIndex = [];
    private Vector2[] _uvs = [];

    // Reused per-frame buffers.
    private SkinningMath.BoneTransform[] _world = []; // per bone, animated world transform (native)

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
    ///     True when the standing-idle (or its live-pending default, §10.3.1) is engaged for looping
    ///     playback. False when no idle clip resolved (offline / no VFS) and the node merely stands.
    /// </summary>
    public bool IsIdlePlaying { get; private set; }

    /// <summary>
    ///     Builds the rig from parsed data. Must be called once before the node ticks.
    ///     Performs: hierarchy resolution, bind-world accumulation, influence build, inverse-bind bake,
    ///     per-bone track binding, and the static rest ArrayMesh.
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
        var boneCount = _bones.Length;

        // 1) Hierarchy + bind world. Also build the per-bone has-child flag for the §6.3
        // interior-bone translation lock.
        // spec: Docs/RE/specs/skinning.md §3 / §6.3.
        SkinningMath.ResolveHierarchy(_bones, out _parentIndex, out var idToIndex, out var baseId, out _hasChild);
        var bindWorld = SkinningMath.AccumulateBindWorld(_bones, _parentIndex);

        // Per-bone runtime node scale (+84 field; rotate → scale → translate in the world walk, §6.6).
        // SPEC GAP: the on-disk SOURCE of the per-node scale is not yet pinned in any clean spec, so we
        // default it to 1.0 (no behaviour change; latent until the +84 source is promoted). The world
        // walk multiplies the rotated local animated translation by this before the parent-add.
        // spec-gap: per-node runtime scale (+84) disk source undecoded — default 1.0.
        // spec: Docs/RE/specs/skinning.md §6.6 / §3.4 (+84).
        _nodeScale = new float[boneCount];
        for (var i = 0; i < boneCount; i++) _nodeScale[i] = 1.0f;

        var vertexCount = mesh.Positions.Length;

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
        // The supplied clip IS the resolved standing-idle slot (motion_ids_a[0] / col-15 idle, §8(e)).
        // Retain it so SelectVisualStateClip() can hand it back as the played clip for every recovered
        // visual-state case (only the idle slot is resolvable here; the others are live-pending, §10.3.1).
        // spec: Docs/RE/specs/skinning.md §8(e) (idle = motion_ids_a[0] = col15), §10.3.1.
        _idleClip = clip;

        _trackByBoneIndex = new AnimationTrack?[boneCount];
        if (clip is not null && clip.FrameCount > 0)
        {
            var boundTracks = 0;
            var skippedTracks = 0;
            foreach (var tr in clip.Tracks)
            {
                // Resolve bone_id → array slot ONLY by the skeleton's own id→index map. A bone_id
                // absent from the map names a joint that does not exist on this rig → SKIP it.
                // (No "off = bone_id − base_id" salvage: that is exactly the clamp-into-range the
                // spec forbids, since the offset could land on an unrelated bone of the wrong rig.)
                var bid = tr.BoneId & 0xFF;
                var bIdx = bid >= 0 && bid < 256 ? idToIndex[bid] : -1;

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
                GD.PrintErr($"[Skinning] '{mesh.Name}': SKIPPED {skippedTracks} clip track(s) whose " +
                            $"bone_id is not a bone of this {boneCount}-bone rig (base_id={baseId}); " +
                            $"bound {boundTracks}. spec: skinning.md §8(e) item 4 — skip, do not clamp.");

            // Duration = frame_count × 0.1 (10 fps).
            // spec: Docs/RE/formats/animation.md §Timing. CONFIRMED.
            _clipDuration = clip.FrameCount * SkinningMath.MotSecondsPerFrame;
            _hasClip = _clipDuration > 0f;
        }

        // 4) Render topology: flat unindexed corner list with CW→CCW winding swap.
        // spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap [0,2,1] for Godot CCW.
        var faceCount = (int)mesh.FaceCount;
        var cornerCount = faceCount * 3;
        _cornerVertex = new int[cornerCount];
        _uvs = new Vector2[cornerCount];
        var corners = mesh.Corners;
        for (var f = 0; f < faceCount; f++)
        {
            var cBase = f * 3;
            // CCW: emit source corners 0,2,1 into output slots 0,1,2.
            int[] order = [cBase + 0, cBase + 2, cBase + 1];
            for (var j = 0; j < 3; j++)
            {
                var corner = corners[order[j]];
                var vi = corner.VertexIndex;
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

        if (_material is null)
        {
            // Fallback: StandardMaterial3D (non-cel, flat PBR).
            var stdMat = new StandardMaterial3D
            {
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };
            if (albedo is not null)
                stdMat.AlbedoTexture = albedo;
            else
                stdMat.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);
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
        DeformAndUpload(0f, true); // fills surface 0 on _arrayMesh first
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
        var frameCount = clip?.FrameCount ?? 0u;
        GD.Print($"[Skinning] Setup '{mesh.Name}': hasClip={_hasClip} clipFrameCount={frameCount} " +
                 $"clipDuration={_clipDuration:F2}s externalDrive={externalDrive}.");

        _ready = true;

        // EW4: begin LOOPING STANDING-IDLE playback as soon as the skinned character is built. The
        // standing visual state routes to the known idle slot (motion_ids_a[0] / col-15 idle, §8(e));
        // the other 5 recovered visual-state cases are live-pending (§10.3.1) and default to this same
        // idle clip — see SelectVisualStateClip(). Playback is gated on the clip being present, so an
        // offline / no-VFS build with no resolvable idle still stands silently (no crash).
        // spec: Docs/RE/specs/skinning.md §10.5 (drive the active clip's clock with real per-frame dt
        //       and loop at clip end; the static look comes from the DATA, not from failing to advance t).
        PlayStandingIdle();
    }

    /// <summary>
    ///     Maps a recovered visual-state case to the clip to play. ONLY the <see cref="VisualState.Standing" />
    ///     case has a statically-resolvable clip (the col-15 standing idle = <c>motion_ids_a[0]</c>, §8(e));
    ///     the other five cases are <c>live-pending (6-D)</c> (§10.3.1) and intentionally default to the same
    ///     idle clip rather than guessing a clip the live engine has not been confirmed to select. This node
    ///     only ever holds the resolved idle clip, so the default is the faithful conservative choice.
    ///     spec: Docs/RE/specs/skinning.md §8(e) (idle = motion_ids_a[0] = col15), §10.3.1 / §10.4
    ///     (value → column mapping live-pending — do not guess).
    /// </summary>
    private AnimationClip? SelectVisualStateClip(VisualState state)
    {
        return state switch
        {
            // KNOWN: neutral standing → the col-15 standing idle (motion_ids_a[0]). spec: skinning.md §8(e).
            VisualState.Standing => _idleClip,

            // live-pending (skinning.md §10) — the value → column mapping for these five cases is not
            // statically settled (§10.3.1); default to the standing idle until a live debugger read pins it.
            VisualState.VisualState1 => _idleClip, // live-pending (skinning.md §10)
            VisualState.VisualState2 => _idleClip, // live-pending (skinning.md §10)
            VisualState.VisualState3 => _idleClip, // live-pending (skinning.md §10)
            VisualState.VisualState4 => _idleClip, // live-pending (skinning.md §10)
            VisualState.VisualState5 => _idleClip, // live-pending (skinning.md §10)

            _ => _idleClip // any other value → faithful default (standing idle). spec: skinning.md §10.5.
        };
    }

    /// <summary>
    ///     Starts looping playback of the STANDING-IDLE clip (the default state; §10.5). Resolves the clip
    ///     through the 6-case visual-state selector for the <see cref="VisualState.Standing" /> case and
    ///     engages it; the per-frame <see cref="Advance" /> path (driven by <c>_Process</c> for the player,
    ///     or by the throttled owner via <see cref="Tick" />) advances the clock with real elapsed time and
    ///     wraps at clip end (CycleLayer loop). Gated on a clip being present — with no resolvable idle
    ///     (offline / no VFS) the node simply stands in the rest pose (no crash, no playback).
    ///     STRICTLY PASSIVE: which clip plays arrives as the visual state; this method only translates it
    ///     into AnimationPlayer-style playback. No game-rule authority, no clip synthesis.
    ///     spec: Docs/RE/specs/skinning.md §10.5 (render the col15 idle faithfully, advance with real dt and
    ///     loop at clip end — the static look comes from the data), §8(e) (idle slot resolution).
    /// </summary>
    public void PlayStandingIdle()
    {
        PlayVisualState(VisualState.Standing);
    }

    /// <summary>
    ///     Selects the clip for <paramref name="state" /> (the standing idle, or its live-pending default per
    ///     §10.3.1) and begins looping playback. No-op (the node just stands) when no clip resolves.
    ///     Note: the clip is the single resolved idle held since Setup, so engaging it is a matter of
    ///     confirming the playback gate — the per-frame <see cref="Advance" /> clock (real dt + modulo wrap,
    ///     §10.5) does the looping. The phase is left as Setup initialized it (0, or the per-actor stagger
    ///     from <c>startPhaseSeconds</c> so a town of mobs sharing one idle does not animate in lockstep);
    ///     this method does NOT clobber that stagger.
    ///     spec: Docs/RE/specs/skinning.md §10.3.1 / §10.5.
    /// </summary>
    public void PlayVisualState(VisualState state)
    {
        var selected = SelectVisualStateClip(state);

        // Playback gate: only drive the clock when a clip is actually present. CycleLayer looping is
        // handled in Advance() (modulo wrap at _clipDuration). With no resolvable idle (offline / no
        // VFS) the node stands in the rest pose, faithfully (§10.2/§10.5) — no crash, no playback.
        if (selected is null || !_hasClip || _clipDuration <= 0f)
            return;

        // The idle is now engaged: _Process (player) / Tick (throttled mobs) advances _time from its
        // current phase each frame and DeformAndUpload re-samples the clip, looping at clip end. We do
        // NOT reset _time here so Setup's per-actor stagger phase is preserved.
        IsIdlePlaying = true;
        GD.Print($"[Skinning] Idle playback engaged (state={state}, looping, " +
                 $"duration={_clipDuration:F2}s). spec: skinning.md §10.5 (advance real dt + loop).");
    }

    /// <summary>Current ArrayMesh AABB (rest pose after Setup), for recentring.</summary>
    public Aabb GetMeshAabb()
    {
        return _arrayMesh?.GetAabb() ?? new Aabb();
    }

    /// <summary>
    ///     AABB of the DISPLAYED animated pose at clip frame 0 (Godot space), or the rest AABB when
    ///     there is no clip. Used by the builder to derive the stand-up pivot and recentre from the pose
    ///     that is actually rendered — not the raw bind pose, which can have a different tallest axis.
    ///     Why this matters: a rig authored lying along X (X-tallest at rest) whose idle stands it
    ///     upright on Y (Y-tallest animated) would be double-rotated if the pivot were derived from the
    ///     rest pose. Deriving from the animated frame-0 pose makes the pivot reflect what is on screen.
    ///     For the g1 World player (X-tallest in BOTH rest and animation) this returns the same tallest
    ///     axis as the rest AABB, so the World rendering is provably unchanged.
    ///     spec: Docs/RE/specs/skinning.md §6 (the displayed pose is the sampled idle, not the bind pose);
    ///     §8(b) (single handedness conversion, applied at output here too).
    /// </summary>
    public Aabb GetDisplayedFrame0Aabb()
    {
        if (_arrayMesh is null) return new Aabb();
        if (!_hasClip)
            return _arrayMesh.GetAabb(); // no clip → rest is the displayed pose

        // Render frame 0 of the idle, read its AABB, then restore the rest pose so the visible mesh
        // and _time are left exactly as Setup left them (the first Tick/_Process re-advances).
        DeformAndUpload(0f, false);
        var animated = _arrayMesh.GetAabb();
        DeformAndUpload(0f, true);
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
    ///     Advances the idle clip by <paramref name="dtSeconds" /> and re-uploads the deformed surface.
    ///     Used by the throttled owner (NpcRenderer) for externally-driven nodes. The accumulated dt
    ///     makes ~10 Hz updates visually equivalent to per-frame ones: the clip time still advances by
    ///     real elapsed seconds, only the resample cadence is coarser (matching the original 10 fps).
    ///     spec: Docs/RE/formats/animation.md §Timing — fixed 10 fps clip rate.
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

        DeformAndUpload(_time, false);
    }

    /// <summary>
    ///     Runs the full per-frame pipeline at clip time <paramref name="t" /> and uploads the result
    ///     to the single ArrayMesh surface. When <paramref name="restPose" /> is true the animated pose
    ///     is forced to the bind pose (used for the initial build and the rest-cancellation diagnostic).
    /// </summary>
    private void DeformAndUpload(float t, bool restPose)
    {
        if (_arrayMesh is null || _meshInstance is null) return;

        ComputeWorldPoses(t, restPose);

        // LBS deform every unique vertex in native space.
        for (var v = 0; v < _deformedPos.Length; v++)
            (_deformedPos[v], _deformedNrm[v]) = SkinningMath.DeformVertex(_perVertex[v], _world);

        // Expand to corners and apply the single handedness conversion at the output.
        // spec: Docs/RE/specs/skinning.md §8(b) — single conversion (world Z-negate) at output.
        for (var c = 0; c < _cornerVertex.Length; c++)
        {
            var vi = _cornerVertex[c];
            var p = _deformedPos[vi];
            var n = _deformedNrm[vi];
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
        var arrays = new Array();
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

    /// <summary>Computes per-bone animated world transforms into the reused <c>_world</c> buffer.</summary>
    private void ComputeWorldPoses(float t, bool restPose)
    {
        var tracks = restPose ? _noTracks : _trackByBoneIndex;
        SkinningMath.ComputeAnimatedWorld(
            _bones, _parentIndex, tracks, t, RenormalizeAlpha, _world, AnimAsDelta,
            _hasChild, _nodeScale);
    }

    // =========================================================================
    // Diagnostics (headless verification of the mandatory invariants)
    // =========================================================================

    /// <summary>
    ///     Computes the rest-pose cancellation deviation, the rest AABB, and a liveness sample, and
    ///     returns them for the orchestrator to print. Run after <see cref="Setup" />.
    ///     Invariant 1 (REST CANCELLATION): with the bind pose, the deformed vertices must equal the
    ///     rest model vertices (max deviation ~0, &lt; 1e-3). spec: skinning.md §0 / §8(a).
    ///     Invariant 2 (LIVENESS): a tracked vertex must move between two clip times.
    ///     Invariant 3 (AABB): finite, human-sized.
    /// </summary>
    public SkinDiagnostics BuildDiagnostics(SkinnedMesh mesh)
    {
        var d = new SkinDiagnostics();

        // ---- Invariant 1: rest cancellation (native space, against model-space rest verts) ----
        ComputeWorldPoses(0f, true);
        var maxDev = 0f;
        for (var v = 0; v < _deformedPos.Length; v++)
        {
            var (p, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
            var r = mesh.Positions[v];
            float dx = p.X - r.X, dy = p.Y - r.Y, dz = p.Z - r.Z;
            var dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dev > maxDev) maxDev = dev;
        }

        d.MaxRestDeviation = maxDev;

        // ---- Invariant 3: rest-pose AABB in Godot space ----
        DeformAndUpload(0f, true);
        if (_arrayMesh is not null)
        {
            var aabb = _arrayMesh.GetAabb();
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
            ComputeWorldPoses(0f, false);
            var vc = _deformedPos.Length;
            var p0 = new Vec3[vc];
            for (var v = 0; v < vc; v++)
                (p0[v], _) = SkinningMath.DeformVertex(_perVertex[v], _world);

            // Sample times spanning the clip (skip 0; include the last interior frame). For a 3-frame
            // 0.3s idle this hits t≈0.075/0.15/0.225/0.29 — enough to catch a subtle idle's peak.
            var sampleTimes = LivenessSampleTimes(_clipDuration);

            var bestDelta = 0f;
            var bestVi = 0;
            var bestT = 0f;
            Vector3 bestP0 = Vector3.Zero, bestP1 = Vector3.Zero;
            foreach (var t in sampleTimes)
            {
                ComputeWorldPoses(t, false);
                for (var v = 0; v < vc; v++)
                {
                    var (pt, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
                    float dx = pt.X - p0[v].X, dy = pt.Y - p0[v].Y, dz = pt.Z - p0[v].Z;
                    var dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
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
        DeformAndUpload(0f, true);
        _time = 0f;
        return d;
    }

    /// <summary>
    ///     The clip times the liveness probe samples (excludes t=0, the reference). Uses up to four
    ///     interior samples spread across the clip so a SHORT idle's subtle peak motion is captured;
    ///     the last sample is just inside the final frame. spec: animation.md §Timing (10 fps).
    /// </summary>
    private static float[] LivenessSampleTimes(float clipDuration)
    {
        var last = MathF.Max(clipDuration - 0.01f, 0f);
        // Quarter/half/three-quarter plus the last interior frame.
        return
        [
            clipDuration * 0.25f,
            clipDuration * 0.5f,
            clipDuration * 0.75f,
            last
        ];
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                 || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
    }

    /// <summary>Diagnostic results for the mandatory skinning invariants.</summary>
    public sealed class SkinDiagnostics
    {
        public bool AabbFinite;
        public float LivenessDelta;
        public Vector3 LivenessP0;
        public Vector3 LivenessP1;
        public float LivenessT0;
        public float LivenessT1;
        public int LivenessVertex = -1;
        public float MaxRestDeviation;
        public Vector3 RestAabbPos;
        public Vector3 RestAabbSize;
    }
}