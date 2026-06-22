using Godot;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

// =============================================================================
// EffectRenderer — real .xeff-driven visual effect renderer
// =============================================================================
//
// CAMPAIGN 5 rewrite: replaces the placeholder GpuParticles3D sphere-burst with real
// per-keyframe billboard/mesh geometry driven directly from parsed .xeff descriptors.
//
// Architecture overview
// ─────────────────────
// • On PlayCast(actor, effectId), this node tries to load and parse the .xeff file from
//   the VFS using RealClientAssets (if available).  On parse success it creates a
//   LiveXeffEffect that ticks per-frame in _Process.
// • On StopCast(actor), the live effect is soft-stopped (active flag cleared) and removed
//   on the following frame — matching the original runtime semantics.
//   spec: Docs/RE/specs/effects.md §15.5 — soft-stop; CODE-CONFIRMED.
// • If the VFS is absent, the .xeff file is missing, or parsing fails, no effect is rendered
//   (no-placeholder doctrine; see PlayCast implementation).
//
// Parser note
// ────────────────────────────────────────
// XeffParser.cs (layer 03, Assets.Parsers) implements the corrected 8-byte header spec:
// effect_id u32 + sub_effect_count u32; block 0 starts at offset 0x08.
//   spec: Docs/RE/formats/effects.md §A.2 — header 8 bytes (CORRECTED 2026-06-14); VERIFIED.
//   spec: Docs/RE/formats/effects.md §A.14 — XEFF_HEADER_SIZE = 8.
// EffectRenderer calls XeffParser.ParseXeff() and maps XeffData/XeffSubEffect/XeffKeyframe
// into SubEffectDesc (a thin presentation view-model).  The former XeffMiniParser private
// class has been removed — XeffParser is the single byte-level .xeff parsing point.
//
// Effect resolution chain (§F hook)
// ──────────────────────────────────
// The spec documents three link tables in data/effect/:
//   xeffect.lst      — name→file manifest (§A.9 / §A.14: XEFF_LST_NAME_LEN = 30)
//   totalmugong.txt  — skill cast-channel sound overlay (§3/§13)
//   itemjointeff.txt — item joint-effect binding (§3/§9.3)
//   mobjointeff.txt  — mob joint-effect binding (§3/§9.3)
//   spec: Docs/RE/specs/effects.md §3 — boot sequence; CODE-CONFIRMED.
//   spec: Docs/RE/formats/effects.md §A.9 / §F — xeffect.lst + binding tables.
// Resolution strategy: the runtime resolves a .xeff ONLY through the xeffect.lst registry keyed by raw
// effect_id (built at bind time). The original has NO numeric-name sprintf path (spec §C.2 Option A
// REJECTED), so a registry miss renders nothing — no direct {effectId}.xeff probe.
//   spec: Docs/RE/formats/effects.md §C.2 — "registry is the sole resolver; no numeric-name path in binary".
//
// Emitter types rendered
// ──────────────────────
// type 0 — Billboard (camera-facing quad); half-extents from keyframe size_x/size_y.
//   spec: Docs/RE/specs/effects.md §17.2 — billboard; CONFIRMED.
//   spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_BILLBOARD = 0; CONFIRMED.
// type 1 — Mesh-particle; per-vertex transform from the sub-effect velocity/size.
//   spec: Docs/RE/specs/effects.md §17.2 — mesh-particle; CONFIRMED.
//   spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_MESH = 1; CONFIRMED.
// type 2 — Directional billboard; same as type 0 plus 90° Y pre-rotation.
//   spec: Docs/RE/specs/effects.md §17.2 — oriented-quad; CONFIRMED.
//   spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_DIRECTIONAL = 2; CONFIRMED.
// GPU-particle (resource_id >= 10000): driven by GpuParticleSimNode (stepwise Euler integration).
//   spec: Docs/RE/specs/effects.md §17.2 — resource_id >= 10000 → GPU particle; CONFIRMED.
//   spec: Docs/RE/formats/effects.md §A.14 — XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000; CONFIRMED.
//
// Keyframe sampling
// ─────────────────
//   i = floor(elapsed_ms / anim_stride_ms)         — frame index
//   frac = (elapsed_ms mod anim_stride_ms) / anim_stride_ms  — interpolation factor
//   alpha = lerp(alpha[i], alpha[i+1], frac)       — linear
//   size  = lerp(size[i],  size[i+1],  frac)       — linear (Vec3)
//   (rotation slerp is approximated as nlerp for MVP; exact slerp needs Quaternion.Slerp)
//   spec: Docs/RE/specs/effects.md §17.3 — piecewise-linear with slerp rotation; CONFIRMED.
//   spec: Docs/RE/specs/effects.md §8.2 step 6 — keyframe interpolation; CODE-CONFIRMED.
//
// Alpha inversion
// ───────────────
// On-disk: file 0.0 = fully opaque, file 1.0 = fully transparent.
// At load time the mini-parser applies: in_memory = 1.0 − file_value.
//   spec: Docs/RE/formats/effects.md §A.6 — alpha inversion; CONFIRMED.
//
// Threading
// ─────────
// All Node mutations are on the Godot main thread (_Process / CallDeferred).
// VFS loads happen synchronously in PlayCast (main thread, one-off on cast start).
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/effects.md §15 — skill-cast effect chain.

/// <summary>
///     Spawns and tears-down actor-anchored .xeff-driven visual effects in response to cast
///     lifecycle events.  When the VFS is absent or the .xeff cannot be parsed the effect is
///     silent (no synthetic placeholder — the no-placeholder doctrine; spec: effects.md §17.2).
///     Lifecycle driven by action codes from the network:
///     0xC8 = cast-enable  → PlayCast(actor, effectId)  — starts a looping effect
///     0xC9 = cast-disable → StopCast(actor)             — soft-stops the running effect
///     0xCB = secondary disable → StopCast(actor)        — same teardown
///     spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
/// </summary>
public sealed partial class EffectRenderer : Node3D
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants from spec
    // ─────────────────────────────────────────────────────────────────────────

    // Resource id threshold: below this → CPU mesh; at or above → GPU particle.
    // spec: Docs/RE/formats/effects.md §A.14 — XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000; CONFIRMED.
    private const uint XeffResourceParticleThreshold = 10000;

    // emitter_type enum values.
    // spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_BILLBOARD = 0; CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_MESH = 1; CONFIRMED (type 1 not yet rendered; no separate const needed).
    // spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_DIRECTIONAL = 2; CONFIRMED.
    private const uint EmitterBillboard = 0;
    private const uint EmitterDirectional = 2;

    // UV scroll loop period in milliseconds.
    // spec: Docs/RE/formats/effects.md §A.14 — XEFF_UV_SCROLL_PERIOD_MS = 5000; CONFIRMED.
    private const float UvScrollPeriodMs = 5000f;

    // Actor height lift applied to the emitter origin (actor-anchored effects sit at body/feet).
    // spec: Docs/RE/specs/effects.md §15.4 — "effect origin follows the caster's world position"; CODE-CONFIRMED.
    // Aesthetic: 0.9 world units lifts from feet to approximate waist height.
    private const float EmitterHeightOffset = 0.9f;

    // VFS path for the xeffect manifest.
    // spec: Docs/RE/formats/effects.md §A.9 — "data/effect/xeffect.lst".
    private const string XeffectLstPath = "data/effect/xeffect.lst"; // spec: Docs/RE/formats/effects.md §A.9

    // Bytes per name record in xeffect.lst.
    // spec: Docs/RE/formats/effects.md §A.14 — XEFF_LST_NAME_LEN = 30 (0x1E).
    private const int XeffLstNameLen = 30; // spec: Docs/RE/formats/effects.md §A.14 XEFF_LST_NAME_LEN = 30

    // VFS path for the GPU-particle emitter descriptor table.
    // spec: Docs/RE/formats/effects.md §E.1 — "data/effect/particle/particleEmitter.eff": CONFIRMED.
    // VFS-lowercased to "particleemitter.eff" by the VFS layer.
    private const string ParticleEmitterEffPath = "data/effect/particle/particleemitter.eff";

    // ActorKey → live effect (at most one per actor, as per spec looping UserXEffect).
    // spec: Docs/RE/specs/effects.md §15.4 — one looping UserXEffect per cast; CODE-CONFIRMED.
    private readonly Dictionary<ActorKey, LiveEffect> _live = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Asset access
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     VFS access (null when the real client directory is not present).
    ///     Populated in _Ready or Bind().  Disposed with EffectRenderer.
    /// </summary>
    private RealClientAssets? _assets;

    private CancellationTokenSource? _cts;

    // ─────────────────────────────────────────────────────────────────────────
    // Effect registry — effect_id → vfs path (keyed by header first u32)
    // ─────────────────────────────────────────────────────────────────────────

    // Registry populated lazily on first TryLoadXeff call (or eagerly in _Ready when VFS available).
    // Keys are raw effect_id values (first u32 of .xeff header, NOT filenames).
    // Values are VFS paths: "data/effect/xeff/<name>" (NUL-trimmed, CP949 name from xeffect.lst).
    // spec: Docs/RE/formats/effects.md §C.2 — runtime registry keyed by RAW effect_id; CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst = u32 count + count × char[30] CP949 name.
    // spec: Docs/RE/specs/effects.md §15.1 — resolve cast_effect_id through registry; CODE-CONFIRMED.
    private Dictionary<uint, string>? _effectRegistry;

    // ─────────────────────────────────────────────────────────────────────────
    // HUD hub subscription
    // ─────────────────────────────────────────────────────────────────────────

    private IHudEventHub? _hub;

    // ─────────────────────────────────────────────────────────────────────────
    // GPU-particle emitter descriptor table (particleEmitter.eff)
    // ─────────────────────────────────────────────────────────────────────────

    // Loaded lazily on first GPU-particle sub-effect spawn. Null = not yet loaded or VFS absent.
    // spec: Docs/RE/formats/effects.md §E.1 — data/effect/particle/particleEmitter.eff: CONFIRMED.
    private ParticleEmitterTable? _particleEmitterTable;

    // Whether the table load has been attempted (prevents repeated VFS reads on miss).
    private bool _particleEmitterTableAttempted;

    // Whether the registry build has been attempted (prevents repeated failures).
    private bool _registryBuildAttempted;

    // ─────────────────────────────────────────────────────────────────────────
    // Godot lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[EffectRenderer] _Ready.");

        // Open VFS access if real client assets are available.
        _assets = RealClientAssets.TryOpen();
        if (_assets is not null)
        {
            GD.Print("[EffectRenderer] VFS available — real .xeff loading enabled.");
            // Build the effect registry eagerly at boot from xeffect.lst.
            // spec: Docs/RE/formats/effects.md §C.2 — registry keyed by RAW effect_id; CONFIRMED.
            // spec: Docs/RE/specs/effects.md §3 step 3 — manifest loaded at boot; CODE-CONFIRMED.
            BuildEffectRegistry(_assets);
        }
        else
        {
            GD.Print("[EffectRenderer] VFS unavailable — effects disabled; renders nothing.");
        }
    }

    /// <summary>
    ///     Per-frame tick: update all live .xeff effects (keyframe animation, billboard rebuild).
    ///     Also drains the CombatTexts channel from the hub.
    ///     All Node mutations happen on the main thread here.
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — drain channels each frame.
    /// </summary>
    public override void _Process(double delta)
    {
        // Drain CombatTexts channel (non-blocking TryRead loop).
        if (_hub is not null)
        {
            var reader = _hub.CombatTexts;
            while (reader.TryRead(out var ev))
                _ = ev; // FUTURE-HOOK: forward to floating combat-text sub-node
        }

        // Advance and rebuild all live .xeff effects.
        var deltaMs = delta * 1000.0;

        // Collect expired keys so we can remove them after iteration.
        List<ActorKey>? toRemove = null;
        foreach (var kv in _live)
        {
            var live = kv.Value;
            if (!live.Active)
            {
                toRemove ??= new List<ActorKey>(2);
                toRemove.Add(kv.Key);
                continue;
            }

            // Advance elapsed time.
            live.ElapsedMs += deltaMs;

            // Update mesh positions to follow the anchor actor; advance GPU particle sims.
            if (live.SubEffects is { } subEffects) TickXeffEffect(live, subEffects, deltaMs);
        }

        if (toRemove is not null)
            foreach (var key in toRemove)
                _live.Remove(key);
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _assets?.Dispose();
        _assets = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API — Bind
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Binds this renderer to the application <see cref="IHudEventHub" />.
    ///     Must be called on the Godot main thread.
    /// </summary>
    public void Bind(IHudEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;

        GD.Print("[EffectRenderer] Hub bound. Subscribed to CombatTexts channel.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Teardown helpers (used by Caster partial and lifecycle)
    // ─────────────────────────────────────────────────────────────────────────

    private void ClearAllEffects()
    {
        foreach (var live in _live.Values)
            TeardownLiveEffect(live);
        _live.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Actor key resolution
    // ─────────────────────────────────────────────────────────────────────────

    private static ActorKey ResolveActorKey(Node3D actor)
    {
        if (actor is VisualActor va)
            return va.ActorKey;

        // Fallback: use the Godot instance id as a synthetic raw id.
        var instanceId = actor.GetInstanceId();
        return new ActorKey((uint)(instanceId & 0xFFFF_FFFF), default);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parsed sub-effect descriptor (owned by this layer only — not in layer 03)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Presentation view-model for one sub-effect element, populated from
    ///     <see cref="XeffSubEffect" /> (layer-03 shared parser output).
    ///     Contains only the fields consumed by EffectRenderer at runtime.
    ///     Alpha in AlphaKeys is already un-inverted: 0=transparent, 1=opaque.
    ///     TotalTime, ScrollU, ScrollV are derived values computed at mapping time.
    ///     spec: Docs/RE/formats/effects.md §A.4 — sub-effect block structure; CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §A.6 — alpha inversion (applied at mapping boundary); CONFIRMED.
    /// </summary>
    internal sealed class SubEffectDesc
    {
        // Alpha curve (already un-inverted: 0=transparent, 1=opaque after parse).
        // spec: Docs/RE/formats/effects.md §A.6 — stored as 1.0 − opacity; CONFIRMED.
        public float[] AlphaKeys = [];
        public uint AnimBaseTime; // ms base offset
        public uint AnimFlag; // bool: animated path enabled

        // Track header.
        // spec: Docs/RE/formats/effects.md §A.4.3 — track header (13 bytes fixed); CONFIRMED.
        public byte AnimLoop; // non-zero = animated
        public uint AnimStride; // ms per keyframe frame
        public float[] DiffuseB = [];
        public float[] DiffuseG = [];

        // Per-keyframe diffuse-RGB tint curve (NOT a scale). Assembled R/G/B-per-key by the
        // layer-03 parser (curve passes 2/3/4 → R/G/B). Sampled linearly per-frame; default 1.0
        // (white) when a curve is empty. Fed into the billboard vertex Color and the material
        // AlbedoColor — this is the warm-brazier / blue-waterfall tint.
        // spec: Docs/RE/specs/effects.md §17.3 — "The colour channel is a per-keyframe diffuse
        //       tint, not a scale"; assembled R/G/B order; defaults to (1,1,1); CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.2 — curve passes 2/3/4 = per-keyframe diffuse R/G/B.
        public float[] DiffuseR = [];

        // spec: Docs/RE/formats/effects.md §A.4.0 — element fixed head (24 bytes on disk).
        public uint EmitterType; // 0=billboard, 1=mesh, 2=directional

        // Keyframes.
        // spec: Docs/RE/formats/effects.md §A.4.4 — keyframe 9-float layout; CONFIRMED.
        public XeffKeyframe[] Keyframes = [];
        public uint ResourceId; // < 10000 CPU mesh; >= 10000 GPU particle

        // UV-scroll flags from low byte of TexCount.
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
        public bool ScrollU;
        public bool ScrollV;
        public uint TexCount; // number of keyframes (also frames alias)

        // Texture names (resolved to data/effect/texture/<name>.tga).
        // spec: Docs/RE/formats/effects.md §A.4.1 — name table entry_count × 64 bytes; CONFIRMED.
        public string[] TextureNames = [];

        // Derived total time (ms).
        // spec: Docs/RE/formats/effects.md §A.4.3 — total_time = tex_count × anim_stride + anim_base_time; CONFIRMED.
        public uint TotalTime;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Live effect state
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     View-state record for one live effect (real .xeff or placeholder fallback).
    /// </summary>
    private sealed class LiveEffect
    {
        public bool Active = true; // cleared by soft-stop

        public Node3D Anchor = null!; // the actor node being followed

        // Shared fields (both real and placeholder).
        public uint EffectId;
        public double ElapsedMs; // running elapsed time in ms

        // Legacy GpuParticles3D slot — superseded by GpuParticleSimNode; kept for null-safe
        // teardown compat in EmitterRenderer and KeyframeAnimator (always null, never assigned).
        // spec: Docs/RE/specs/effects.md §17.2 — GPU particle now via GpuParticleSimNode; CONFIRMED.
#pragma warning disable CS0649 // field always null; intentional (compat guard, never assigned)
        public GpuParticles3D?[]? GpuParticles;
#pragma warning restore CS0649

        // Per-sub-effect: one MeshInstance3D per rendered sub-effect.
        // Null entries indicate GPU-particle sub-effects (handled by GpuParticleSimNode below).
        public MeshInstance3D?[]? MeshInstances;

        // GPU particle simulation nodes (one per GPU-particle sub-effect).
        // Indexed parallel to SubEffects/MeshInstances; non-null only for resource_id >= 10000.
        public GpuParticleSimNode?[]? SimNodes;

        // Real .xeff path (null → using placeholder).
        public SubEffectDesc[]? SubEffects;

        // Per-sub-effect loaded textures.
        public ImageTexture?[][]? Textures; // [subEffectIdx][frameIdx]
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPU particle simulation node (Euler integration per §E.2.2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Godot Node3D that simulates one <see cref="ParticleEmitterEntry" /> entry via stepwise
    ///     Euler integration (spec §E.2.2 / §E.2.4). Each particle's state is an in-memory float
    ///     array; per fixed sim step (~67 ms per §E.2.2) position/velocity/size/colour are advanced
    ///     then written to per-particle <see cref="MeshInstance3D" /> nodes in the scene tree.
    ///     Designed to be a child of the <see cref="EffectRenderer" /> node; parented before
    ///     <see cref="_Ready" /> is called on the renderer.
    ///     Threading contract: all mutation on the Godot main thread via parent's _Process.
    ///     spec: Docs/RE/formats/effects.md §E.2.2 — per-particle Euler integration: CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §E.2.4 — global brightness alpha scale: CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/effects.md §11 — sim step ~67 ms.
    /// </summary>
    internal sealed partial class GpuParticleSimNode : Node3D
    {
        // Fixed simulation step in seconds (~67 ms = 15 Hz sim tick matching the original).
        // spec: Docs/RE/specs/effects.md §11 — GPU particle sim step ~67 ms: CODE-CONFIRMED.
        private const double SimStepSec = 0.067;

        // Global brightness alpha scale floor.
        // spec: Docs/RE/formats/effects.md §E.2.4 — factor = 0.05 + 0.95 × (brightness/100): CODE-CONFIRMED.
        // Default brightness = 100 → factor = 1.0 (no dimming at max brightness).
        private const float BrightnessAlphaFloor = 0.05f;
        private readonly float[] _colA;
        private readonly float[] _colB;
        private readonly float[] _colG;
        private readonly float[] _colR;
        private readonly int[] _delayTick; // counts down from spawn_delay; particle active when 0

        private readonly ParticleEmitterEntry _entry;
        private readonly int[] _lifeTick; // counts down from lifetime; respawns at 0
        private readonly MeshInstance3D[] _meshes; // one billboard quad per particle

        // Per-particle mutable state arrays (indexed 0..numParticles-1).
        private readonly float[] _posX;
        private readonly float[] _posY;
        private readonly float[] _posZ;
        private readonly float[] _size;
        private readonly ImageTexture? _texture;
        private readonly float[] _velX;
        private readonly float[] _velY;
        private readonly float[] _velZ;

        private double _accumSec; // elapsed time accumulator for fixed-step integration

        internal GpuParticleSimNode(ParticleEmitterEntry entry, ImageTexture? texture)
        {
            _entry = entry;
            _texture = texture;

            var n = (int)entry.NumFrames;
            _posX = new float[n];
            _posY = new float[n];
            _posZ = new float[n];
            _velX = new float[n];
            _velY = new float[n];
            _velZ = new float[n];
            _size = new float[n];
            _colR = new float[n];
            _colG = new float[n];
            _colB = new float[n];
            _colA = new float[n];
            _lifeTick = new int[n];
            _delayTick = new int[n];
            _meshes = new MeshInstance3D[n];

            // Initialise each particle from its sub-record (spawn state).
            // spec: Docs/RE/formats/effects.md §E.2.2 — at spawn copy size_init, RGBA, spawn_pos, velocity: CODE-CONFIRMED.
            for (var i = 0; i < n; i++)
                SpawnParticle(i);
        }

        public override void _Ready()
        {
            // Build one billboard-quad MeshInstance3D per particle and add as children.
            for (var i = 0; i < _meshes.Length; i++)
            {
                var mi = BuildParticleMesh(i);
                _meshes[i] = mi;
                AddChild(mi);
            }
        }

        /// <summary>
        ///     Advances the simulation by <paramref name="deltaSec" /> seconds, draining the
        ///     fixed-step accumulator and updating all particle MeshInstance3D positions/colours.
        ///     Called from EffectRenderer._Process on the Godot main thread.
        /// </summary>
        public void Tick(double deltaSec)
        {
            _accumSec += deltaSec;
            while (_accumSec >= SimStepSec)
            {
                _accumSec -= SimStepSec;
                StepAll((float)SimStepSec);
            }

            // Update mesh transforms / materials from current particle state (per-frame visual sync).
            UpdateMeshes();
        }

        // ── Per-particle spawn init ────────────────────────────────────────────

        private void SpawnParticle(int i)
        {
            var sr = _entry.SubRecords[i];
            // spec: Docs/RE/formats/effects.md §E.2.2 — spawn state from sub-record: CODE-CONFIRMED.
            _posX[i] = sr.SpawnPosX;
            _posY[i] = sr.SpawnPosY;
            _posZ[i] = sr.SpawnPosZ;
            _velX[i] = sr.VelocityX;
            _velY[i] = sr.VelocityY;
            _velZ[i] = sr.VelocityZ;
            _size[i] = sr.SizeInit;
            _colR[i] = sr.ColorR;
            _colG[i] = sr.ColorG;
            _colB[i] = sr.ColorB;
            _colA[i] = sr.ColorA;
            // life_bonus added once at init.
            // spec: Docs/RE/formats/effects.md §E.2.2 — life += life_bonus at init: CODE-CONFIRMED.
            _lifeTick[i] = sr.Lifetime + sr.LifeBonus;
            _delayTick[i] = sr.SpawnDelay;
        }

        // ── Fixed-step Euler integration (§E.2.2) ─────────────────────────────

        private void StepAll(float dt)
        {
            for (var i = 0; i < _entry.NumFrames; i++)
            {
                // Count down delay; particle is dormant while delay > 0.
                if (_delayTick[i] > 0)
                {
                    _delayTick[i]--;
                    continue;
                }

                // Count down lifetime; respawn on expiry.
                if (_lifeTick[i] <= 0)
                {
                    SpawnParticle(i);
                    continue;
                }

                _lifeTick[i]--;

                var sr = _entry.SubRecords[i];

                // Velocity damping (applied before position update when non-zero).
                // spec: Docs/RE/formats/effects.md §E.2.2 — if velocity_damp ≠ 0: velocity *= damp: CODE-CONFIRMED.
                if (sr.VelocityDamp != 0f)
                {
                    _velX[i] *= sr.VelocityDamp;
                    _velY[i] *= sr.VelocityDamp;
                    _velZ[i] *= sr.VelocityDamp;
                }

                // Position integration: pos += vel × dt.
                _posX[i] += _velX[i] * dt;
                _posY[i] += _velY[i] * dt;
                _posZ[i] += _velZ[i] * dt;

                // Size rate: size += size_rate × dt.
                _size[i] += sr.SizeRate * dt;
                if (_size[i] < 0f) _size[i] = 0f;

                // Colour rate integration: channel += rate × dt (signed i16 rates).
                _colR[i] = Math.Clamp(_colR[i] + sr.ColorRRate * dt, 0f, 255f);
                _colG[i] = Math.Clamp(_colG[i] + sr.ColorGRate * dt, 0f, 255f);
                _colB[i] = Math.Clamp(_colB[i] + sr.ColorBRate * dt, 0f, 255f);

                // Alpha rate + global brightness scale.
                // spec: Docs/RE/formats/effects.md §E.2.4 — alpha scaled by brightness_factor each step: CODE-CONFIRMED.
                var alpha = _colA[i] + sr.ColorARate * dt;
                // Apply global brightness alpha scale (brightness=100 → factor=1.0).
                // Aesthetic: use factor=1.0 (max brightness) — no user brightness option exposed yet.
                // factor = 0.05 + 0.95 × (brightness/100). At 100 → 1.0 exactly.
                var brightnessFactor =
                    BrightnessAlphaFloor + (1f - BrightnessAlphaFloor) * 1.0f; // aesthetic: brightness=100
                alpha *= brightnessFactor;
                _colA[i] = Math.Clamp(alpha, 0f, 255f);
            }
        }

        // ── Visual update (post-step) ─────────────────────────────────────────

        private void UpdateMeshes()
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                var mi = _meshes[i];
                if (!IsInstanceValid(mi)) continue;

                var dormant = _delayTick[i] > 0 || _lifeTick[i] <= 0;
                mi.Visible = !dormant;
                if (dormant) continue;

                // World position = emitter origin (this node's position) + spawn offset integrated so far.
                // The spawn_pos is in emitter-local space; add to parent's world position.
                mi.Position = new Vector3(_posX[i], _posY[i], _posZ[i]);

                // Size drives the billboard quad scale (sprite_size_x/y from entry header).
                // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x/y: size of the sprite quad: HIGH.
                var sizeScale = _size[i] / 65535f; // normalise from u16 range to ~[0,1] for scale
                var qw = _entry.SpriteSizeX * sizeScale;
                var qh = _entry.SpriteSizeY * sizeScale;
                mi.Scale = new Vector3(qw > 0f ? qw : 1f, qh > 0f ? qh : 1f, 1f);

                // Per-particle colour update via material override.
                if (mi.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
                    mat.AlbedoColor = new Color(
                        _colR[i] / 255f,
                        _colG[i] / 255f,
                        _colB[i] / 255f,
                        _colA[i] / 255f);
            }
        }

        // ── Mesh builder ─────────────────────────────────────────────────────

        private MeshInstance3D BuildParticleMesh(int i)
        {
            var sr = _entry.SubRecords[i];

            // Initial sprite size from entry header × initial size_init (normalised from u16).
            // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x/y drive the sprite quad: HIGH.
            var sizeScale = sr.SizeInit > 0 ? sr.SizeInit / 65535f : 1f / 65535f;
            var hw = _entry.SpriteSizeX * sizeScale * 0.5f;
            var hh = _entry.SpriteSizeY * sizeScale * 0.5f;
            hw = MathF.Max(hw, 0.01f);
            hh = MathF.Max(hh, 0.01f);

            var arrays = new Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
            {
                new(-hw, hh, 0f),
                new(hw, hh, 0f),
                new(hw, -hh, 0f),
                new(-hw, -hh, 0f)
            };
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
            {
                new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
            };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var initColor = new Color(
                sr.ColorR / 255f,
                sr.ColorG / 255f,
                sr.ColorB / 255f,
                sr.ColorA / 255f);

            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = initColor,
                AlbedoTexture = _texture,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };

            var mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.SetSurfaceOverrideMaterial(0, mat);
            // Initial local position = spawn_pos offset.
            mi.Position = new Vector3(sr.SpawnPosX, sr.SpawnPosY, sr.SpawnPosZ);
            mi.Visible = sr.SpawnDelay == 0; // dormant if delay > 0
            return mi;
        }
    }
}