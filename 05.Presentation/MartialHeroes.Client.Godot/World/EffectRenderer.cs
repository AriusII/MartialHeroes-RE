using Godot;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;

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
// • If the VFS is absent, the .xeff file is missing, or parsing fails, we fall back to the
//   original GpuParticles3D placeholder so the node is never visually silent.
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
// MVP strategy: resolve the file directly as data/effect/xeff/{effectId}.xeff (decimal name).
//   spec: Docs/RE/formats/effects.md §A.2 — "numeric-named files: value matches decimal filename"; SAMPLE-VERIFIED.
// For non-numeric names the full xeffect.lst manifest lookup is required.
//   MANIFEST-HOOK: labelled below — load xeffect.lst and build an id→name map at bind time.
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
// GPU-particle (resource_id >= 10000): bridged to GpuParticles3D placeholder only.
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
    private bool _demoMode;

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
            GD.Print("[EffectRenderer] VFS unavailable — will use placeholder fallback.");
        }

        CallDeferred(MethodName.MaybeLaunchDemoEffect);
    }

    private void MaybeLaunchDemoEffect()
    {
        // No hub bound yet: the original client shows NO synthetic stand-in here, so we render nothing
        // (faithfully empty) and merely log that Bind(hub) is pending. The former synthetic orange
        // sphere-burst + invented "[EffectRenderer DEMO …]" English Label3D was presentation noise that
        // could ship if the hub was late-bound — removed per the CAMPAIGN-9 no-invented-data doctrine.
        // spec: CLAUDE.md — "NO invented English text, NO fake/demo data … NO procedural placeholders."
        if (_hub is null && !_demoMode)
        {
            _demoMode = true;
            GD.Print("[EffectRenderer] No hub bound yet — idle until Bind(hub) subscribes to cast-effect events.");
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

            // Update mesh positions to follow the anchor actor.
            if (live.SubEffects is { } subEffects) TickXeffEffect(live, subEffects);
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

        if (_demoMode)
        {
            _demoMode = false;
            ClearAllEffects();
        }

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

        // GPU-particle placeholders for resource_id >= 10000 sub-effects.
        // spec: Docs/RE/specs/effects.md §17.2 — resource_id >= 10000 → GPU particle; CONFIRMED.
        public GpuParticles3D?[]? GpuParticles;

        // Per-sub-effect: one MeshInstance3D per rendered sub-effect.
        // Null entries indicate GPU-particle sub-effects (handled by GpuParticles3D below).
        public MeshInstance3D?[]? MeshInstances;

        // Real .xeff path (null → using placeholder).
        public SubEffectDesc[]? SubEffects;

        // Per-sub-effect loaded textures.
        public ImageTexture?[][]? Textures; // [subEffectIdx][frameIdx]
    }
}