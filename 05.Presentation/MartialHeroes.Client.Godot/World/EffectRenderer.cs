using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Dev;

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
/// Spawns and tears-down actor-anchored .xeff-driven visual effects in response to cast
/// lifecycle events.  Falls back to a GpuParticles3D placeholder when the VFS is absent or
/// the .xeff cannot be parsed.
///
/// Lifecycle driven by action codes from the network:
///   0xC8 = cast-enable  → PlayCast(actor, effectId)  — starts a looping effect
///   0xC9 = cast-disable → StopCast(actor)             — soft-stops the running effect
///   0xCB = secondary disable → StopCast(actor)        — same teardown
/// spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
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
    // spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_MESH = 1; CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.12 — XEFF_EMITTER_DIRECTIONAL = 2; CONFIRMED.
    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;

    // UV scroll loop period in milliseconds.
    // spec: Docs/RE/formats/effects.md §A.14 — XEFF_UV_SCROLL_PERIOD_MS = 5000; CONFIRMED.
    private const float UvScrollPeriodMs = 5000f;

    // Actor height lift applied to the emitter origin (actor-anchored effects sit at body/feet).
    // spec: Docs/RE/specs/effects.md §15.4 — "effect origin follows the caster's world position"; CODE-CONFIRMED.
    // Aesthetic: 0.9 world units lifts from feet to approximate waist height.
    private const float EmitterHeightOffset = 0.9f;

    // Placeholder particle count and lifetime (used when .xeff is unavailable).
    private const int PlaceholderParticleCount = 40;
    private const float PlaceholderLifetime = 1.0f;

    // ─────────────────────────────────────────────────────────────────────────
    // Parsed sub-effect descriptor (owned by this layer only — not in layer 03)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Presentation view-model for one sub-effect element, populated from
    /// <see cref="XeffSubEffect"/> (layer-03 shared parser output).
    /// Contains only the fields consumed by EffectRenderer at runtime.
    /// Alpha in AlphaKeys is already un-inverted: 0=transparent, 1=opaque.
    /// TotalTime, ScrollU, ScrollV are derived values computed at mapping time.
    /// spec: Docs/RE/formats/effects.md §A.4 — sub-effect block structure; CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.6 — alpha inversion (applied at mapping boundary); CONFIRMED.
    /// </summary>
    internal sealed class SubEffectDesc
    {
        // spec: Docs/RE/formats/effects.md §A.4.0 — element fixed head (24 bytes on disk).
        public uint EmitterType; // 0=billboard, 1=mesh, 2=directional
        public uint ResourceId; // < 10000 CPU mesh; >= 10000 GPU particle
        public uint AnimFlag; // bool: animated path enabled
        public uint TexCount; // number of keyframes (also frames alias)

        // Texture names (resolved to data/effect/texture/<name>.tga).
        // spec: Docs/RE/formats/effects.md §A.4.1 — name table entry_count × 64 bytes; CONFIRMED.
        public string[] TextureNames = [];

        // Alpha curve (already un-inverted: 0=transparent, 1=opaque after parse).
        // spec: Docs/RE/formats/effects.md §A.6 — stored as 1.0 − opacity; CONFIRMED.
        public float[] AlphaKeys = [];

        // Per-keyframe diffuse-RGB tint curve (NOT a scale). Assembled R/G/B-per-key by the
        // layer-03 parser (curve passes 2/3/4 → R/G/B). Sampled linearly per-frame; default 1.0
        // (white) when a curve is empty. Fed into the billboard vertex Color and the material
        // AlbedoColor — this is the warm-brazier / blue-waterfall tint.
        // spec: Docs/RE/specs/effects.md §17.3 — "The colour channel is a per-keyframe diffuse
        //       tint, not a scale"; assembled R/G/B order; defaults to (1,1,1); CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.2 — curve passes 2/3/4 = per-keyframe diffuse R/G/B.
        public float[] DiffuseR = [];
        public float[] DiffuseG = [];
        public float[] DiffuseB = [];

        // Track header.
        // spec: Docs/RE/formats/effects.md §A.4.3 — track header (13 bytes fixed); CONFIRMED.
        public byte AnimLoop; // non-zero = animated
        public uint AnimStride; // ms per keyframe frame
        public uint AnimBaseTime; // ms base offset

        // Derived total time (ms).
        // spec: Docs/RE/formats/effects.md §A.4.3 — total_time = tex_count × anim_stride + anim_base_time; CONFIRMED.
        public uint TotalTime;

        // Keyframes.
        // spec: Docs/RE/formats/effects.md §A.4.4 — keyframe 9-float layout; CONFIRMED.
        public XeffKeyframe[] Keyframes = [];

        // UV-scroll flags from low byte of TexCount.
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
        public bool ScrollU;
        public bool ScrollV;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Live effect state
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// View-state record for one live effect (real .xeff or placeholder fallback).
    /// </summary>
    private sealed class LiveEffect
    {
        // Shared fields (both real and placeholder).
        public uint EffectId;
        public bool Active = true; // cleared by soft-stop
        public Node3D Anchor = null!; // the actor node being followed

        // Real .xeff path (null → using placeholder).
        public SubEffectDesc[]? SubEffects;
        public double ElapsedMs; // running elapsed time in ms

        // Per-sub-effect: one MeshInstance3D per rendered sub-effect.
        // Null entries indicate GPU-particle sub-effects (handled by GpuParticles3D below).
        public MeshInstance3D?[]? MeshInstances;

        // Per-sub-effect loaded textures.
        public ImageTexture?[][]? Textures; // [subEffectIdx][frameIdx]

        // Placeholder fallback (used when SubEffects is null).
        public GpuParticles3D? Particles;

        // GPU-particle placeholders for resource_id >= 10000 sub-effects.
        // spec: Docs/RE/specs/effects.md §17.2 — resource_id >= 10000 → GPU particle; CONFIRMED.
        public GpuParticles3D?[]? GpuParticles;
    }

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

    // Whether the registry build has been attempted (prevents repeated failures).
    private bool _registryBuildAttempted;

    // VFS path for the xeffect manifest.
    // spec: Docs/RE/formats/effects.md §A.9 — "data/effect/xeffect.lst".
    private const string XeffectLstPath = "data/effect/xeffect.lst"; // spec: Docs/RE/formats/effects.md §A.9

    // Bytes per name record in xeffect.lst.
    // spec: Docs/RE/formats/effects.md §A.14 — XEFF_LST_NAME_LEN = 30 (0x1E).
    private const int XeffLstNameLen = 30; // spec: Docs/RE/formats/effects.md §A.14 XEFF_LST_NAME_LEN = 30

    /// <summary>
    /// Builds (or re-uses) the effect_id → vfs-path registry from xeffect.lst.
    /// Reads the manifest: u32 count, then count × char[30] CP949 NUL-padded names.
    /// For each name, opens the corresponding .xeff file and reads its header first u32 as effect_id.
    /// spec: Docs/RE/formats/effects.md §C.2 — registry keyed by header effect_id; CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst format; HIGH confidence.
    /// spec: Docs/RE/formats/effects.md §A.14 — XEFF_LST_NAME_LEN = 30.
    /// Returns null if VFS unavailable or xeffect.lst is absent.
    /// </summary>
    private Dictionary<uint, string>? BuildEffectRegistry(RealClientAssets assets)
    {
        if (_registryBuildAttempted) return _effectRegistry;
        _registryBuildAttempted = true;

        ReadOnlyMemory<byte> lstRaw = assets.GetRaw(XeffectLstPath);
        if (lstRaw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] xeffect.lst not found in VFS ({XeffectLstPath}) — " +
                     "effect registry unavailable; numeric-path fallback will be used.");
            return null;
        }

        ReadOnlySpan<byte> span = lstRaw.Span;
        if (span.Length < 4)
        {
            GD.PrintErr($"[EffectRenderer] xeffect.lst too short ({span.Length} bytes) — skipping registry build.");
            return null;
        }

        // u32 LE count — number of name records.
        // spec: Docs/RE/formats/effects.md §A.9 — "u32 count" at offset 0.
        uint count = System.Runtime.InteropServices.MemoryMarshal.Read<uint>(span[..4]);
        int expectedLen = 4 + (int)count * XeffLstNameLen;
        if (span.Length < expectedLen)
        {
            GD.PrintErr($"[EffectRenderer] xeffect.lst size mismatch: have {span.Length} bytes, " +
                        $"need {expectedLen} for {count} records — truncated manifest.");
            // Proceed with as many records as fit.
            count = (uint)((span.Length - 4) / XeffLstNameLen);
        }

        // CP949 encoding for NUL-padded name records.
        // spec: Docs/RE/formats/effects.md §A.9 — names are ASCII/CP949; all text is CP949.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);

        var registry = new Dictionary<uint, string>((int)count);
        int mapped = 0;
        int skipped = 0;

        for (uint i = 0; i < count; i++)
        {
            int offset = 4 + (int)i * XeffLstNameLen;
            ReadOnlySpan<byte> nameBytes = span.Slice(offset, XeffLstNameLen);

            // Trim NUL padding.
            int nullPos = nameBytes.IndexOf((byte)0);
            if (nullPos == 0)
            {
                skipped++;
                continue;
            }

            ReadOnlySpan<byte> trimmed = nullPos > 0 ? nameBytes[..nullPos] : nameBytes;

            string name;
            try
            {
                name = cp949.GetString(trimmed);
            }
            catch
            {
                skipped++;
                continue;
            }

            // Build the VFS path: "data/effect/xeff/<name>"
            // spec: Docs/RE/formats/effects.md §C.2 — boot loader: "data/effect/xeff/<name>" concat.
            string vfsPath = $"data/effect/xeff/{name}";

            // Read the .xeff header to extract effect_id (first u32).
            // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 at offset 0; CONFIRMED.
            ReadOnlyMemory<byte> xeffRaw = assets.GetRaw(vfsPath);
            if (xeffRaw.IsEmpty)
            {
                skipped++;
                continue;
            }

            ReadOnlySpan<byte> xeffSpan = xeffRaw.Span;
            if (xeffSpan.Length < 4)
            {
                skipped++;
                continue;
            }

            uint effectId = System.Runtime.InteropServices.MemoryMarshal.Read<uint>(xeffSpan[..4]);

            // Reject the anti-magic sentinel (0x46464558 = "XEFF" ASCII).
            // spec: Docs/RE/formats/effects.md §A.2 — XEFF_INVALID_MAGIC = 0x46464558; CONFIRMED.
            if (effectId == 0x46464558u)
            {
                skipped++;
                continue;
            }

            // On duplicate effect_id, keep the first entry (matches likely runtime "first insert wins").
            if (!registry.ContainsKey(effectId))
            {
                registry[effectId] = vfsPath;
                mapped++;
            }
        }

        _effectRegistry = registry;
        GD.Print($"[EffectRenderer] Effect registry built from xeffect.lst: {mapped} effect_ids mapped " +
                 $"({skipped} skipped / duplicates). spec: Docs/RE/formats/effects.md §C.2 / §A.9.");
        return registry;
    }

    // ActorKey → live effect (at most one per actor, as per spec looping UserXEffect).
    // spec: Docs/RE/specs/effects.md §15.4 — one looping UserXEffect per cast; CODE-CONFIRMED.
    private readonly Dictionary<ActorKey, LiveEffect> _live = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Asset access
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// VFS access (null when the real client directory is not present).
    /// Populated in _Ready or Bind().  Disposed with EffectRenderer.
    /// </summary>
    private RealClientAssets? _assets;

    // ─────────────────────────────────────────────────────────────────────────
    // HUD hub subscription
    // ─────────────────────────────────────────────────────────────────────────

    private IHudEventHub? _hub;
    private bool _demoMode;
    private CancellationTokenSource? _cts;

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
    /// Per-frame tick: update all live .xeff effects (keyframe animation, billboard rebuild).
    /// Also drains the CombatTexts channel from the hub.
    /// All Node mutations happen on the main thread here.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — drain channels each frame.
    /// </summary>
    public override void _Process(double delta)
    {
        // Drain CombatTexts channel (non-blocking TryRead loop).
        if (_hub is not null)
        {
            var reader = _hub.CombatTexts;
            while (reader.TryRead(out CombatTextEvent? ev))
                _ = ev; // FUTURE-HOOK: forward to floating combat-text sub-node
        }

        // Advance and rebuild all live .xeff effects.
        double deltaMs = delta * 1000.0;

        // Collect expired keys so we can remove them after iteration.
        List<ActorKey>? toRemove = null;
        foreach (KeyValuePair<ActorKey, LiveEffect> kv in _live)
        {
            LiveEffect live = kv.Value;
            if (!live.Active)
            {
                toRemove ??= new List<ActorKey>(2);
                toRemove.Add(kv.Key);
                continue;
            }

            // Advance elapsed time.
            live.ElapsedMs += deltaMs;

            // Update mesh positions to follow the anchor actor.
            if (live.SubEffects is { } subEffects)
            {
                TickXeffEffect(live, subEffects);
            }
            else if (live.Particles is { } particles && IsInstanceValid(particles))
            {
                // Placeholder: follow anchor.
                if (IsInstanceValid(live.Anchor))
                {
                    particles.GlobalPosition = live.Anchor.GlobalPosition +
                                               new Vector3(0f, EmitterHeightOffset, 0f);
                }
            }
        }

        if (toRemove is not null)
            foreach (ActorKey key in toRemove)
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
    /// Binds this renderer to the application <see cref="IHudEventHub"/>.
    /// Must be called on the Godot main thread.
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
    // Public API — PlayCast / StopCast
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a looping actor-anchored effect for the given cast.
    /// Attempts to load and render the real .xeff; falls back to the placeholder if unavailable.
    ///
    /// Called when action code 0xC8 (cast-enable) is received.
    /// spec: Docs/RE/specs/effects.md §15.3 — 0xC8 = cast-enable; CODE-CONFIRMED.
    /// spec: Docs/RE/specs/effects.md §15.4 — looping UserXEffect, actor-anchored; CODE-CONFIRMED.
    /// </summary>
    /// <param name="actor">The caster's scene node.</param>
    /// <param name="effectId">
    /// cast_effect_id from the skill record (byte offset 1136).
    /// spec: Docs/RE/specs/effects.md §15.2 — cast_effect_id at byte offset 1136; CODE-CONFIRMED.
    /// The numeric .xeff filename = decimal(effectId).
    /// spec: Docs/RE/formats/effects.md §A.2 — filename = decimal(effect_id); SAMPLE-VERIFIED.
    /// </param>
    public void PlayCast(Node3D actor, uint effectId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ActorKey key = ResolveActorKey(actor);
        StopCast(actor); // idempotent restart

        // spec: Docs/RE/specs/effects.md §15.4 — effect origin follows caster's world position; CODE-CONFIRMED.
        Vector3 origin = actor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);

        // Attempt to load and parse the .xeff file.
        SubEffectDesc[]? subEffects = TryLoadXeff(effectId);

        LiveEffect live;
        if (subEffects is { Length: > 0 })
        {
            // Build one MeshInstance3D per sub-effect (billboard/mesh geometry).
            var meshInstances = new MeshInstance3D?[subEffects.Length];
            var gpuParticles = new GpuParticles3D?[subEffects.Length];
            var textures = new ImageTexture?[subEffects.Length][];

            for (int i = 0; i < subEffects.Length; i++)
            {
                SubEffectDesc se = subEffects[i];
                // Coalesce to empty: the downstream mesh builders treat null and empty arrays
                // identically (all gated by `Length > 0`), so this preserves behaviour and keeps
                // the array element type honest (silences CS8601).
                textures[i] = LoadSubEffectTextures(se) ?? System.Array.Empty<ImageTexture?>();

                if (se.ResourceId >= XeffResourceParticleThreshold)
                {
                    // GPU particle element: use placeholder GpuParticles3D.
                    // spec: Docs/RE/specs/effects.md §17.2 — resource_id >= 10000 → GPU particle; CONFIRMED.
                    gpuParticles[i] = SpawnPlaceholderEffect(origin, effectId);
                }
                else
                {
                    // CPU billboard or mesh: build initial ArrayMesh.
                    meshInstances[i] = BuildSubEffectMesh(se, origin, textures[i], elapsedMs: 0);
                    if (meshInstances[i] is not null)
                        AddChild(meshInstances[i]!);
                }
            }

            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = actor,
                SubEffects = subEffects,
                MeshInstances = meshInstances,
                GpuParticles = gpuParticles,
                Textures = textures,
                ElapsedMs = 0,
            };

            GD.Print($"[EffectRenderer] PlayCast: effectId={effectId} actor={key.RawId} " +
                     $"— loaded real .xeff ({subEffects.Length} sub-effects) origin={origin}. " +
                     "spec: Docs/RE/specs/effects.md §15.4 looping UserXEffect; CODE-CONFIRMED.");
        }
        else
        {
            // Fallback: placeholder GpuParticles3D.
            GpuParticles3D particles = SpawnPlaceholderEffect(origin, effectId);
            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = actor,
                Particles = particles,
                ElapsedMs = 0,
            };

            GD.Print($"[EffectRenderer] PlayCast: effectId={effectId} actor={key.RawId} " +
                     $"— .xeff unavailable or parse failed; using placeholder. origin={origin}. " +
                     "spec: Docs/RE/specs/effects.md §15.4 looping UserXEffect; CODE-CONFIRMED.");
        }

        _live[key] = live;
    }

    /// <summary>
    /// Soft-stops the running cast effect for the given actor.
    ///
    /// Called when action code 0xC9 or 0xCB (cast-disable) is received.
    /// spec: Docs/RE/specs/effects.md §15.3 — 0xC9/0xCB = cast-disable; CODE-CONFIRMED.
    /// spec: Docs/RE/specs/effects.md §15.5 — soft-stop: active flag cleared, removed next frame; CODE-CONFIRMED.
    /// </summary>
    public void StopCast(Node3D actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ActorKey key = ResolveActorKey(actor);
        if (!_live.Remove(key, out LiveEffect? live))
            return;

        // Soft-stop: clear active flag and begin teardown.
        // spec: Docs/RE/specs/effects.md §15.5 — "soft-stop: active flag cleared; removed next frame".
        live.Active = false;
        TeardownLiveEffect(live);

        GD.Print($"[EffectRenderer] StopCast: actor={key.RawId} effectId={live.EffectId} soft-stopped. " +
                 "spec: Docs/RE/specs/effects.md §15.5 soft-stop; CODE-CONFIRMED.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-frame .xeff tick
    // ─────────────────────────────────────────────────────────────────────────

    private void TickXeffEffect(LiveEffect live, SubEffectDesc[] subEffects)
    {
        if (!IsInstanceValid(live.Anchor)) return;

        Vector3 anchorPos = live.Anchor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);
        double elapsedMs = live.ElapsedMs;

        for (int i = 0; i < subEffects.Length; i++)
        {
            SubEffectDesc se = subEffects[i];

            // GPU-particle sub-effects follow anchor but don't rebuild geometry.
            if (se.ResourceId >= XeffResourceParticleThreshold)
            {
                GpuParticles3D? gpu = live.GpuParticles?[i];
                if (gpu is not null && IsInstanceValid(gpu))
                    gpu.GlobalPosition = anchorPos;
                continue;
            }

            MeshInstance3D? existing = live.MeshInstances?[i];
            if (existing is null) continue;

            // Rebuild geometry for this sub-effect at the current elapsed time.
            ImageTexture?[]? texRow = live.Textures?[i];
            RebuildSubEffectMesh(existing, se, anchorPos, elapsedMs, texRow);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Geometry builders — billboard / mesh emitters
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an initial <see cref="MeshInstance3D"/> for a sub-effect.
    /// Returns null for GPU-particle sub-effects or empty geometry.
    /// </summary>
    private MeshInstance3D? BuildSubEffectMesh(
        SubEffectDesc se,
        Vector3 origin,
        ImageTexture?[]? textures,
        double elapsedMs)
    {
        var mi = new MeshInstance3D { GlobalPosition = origin };
        RebuildSubEffectMesh(mi, se, origin, elapsedMs, textures);
        return mi;
    }

    /// <summary>
    /// Rebuilds the <see cref="ArrayMesh"/> on <paramref name="mi"/> for the current keyframe
    /// state, computing all geometry in world-space from the anchor origin and sampled channels.
    ///
    /// Billboard geometry built here is camera-facing (Godot billboard basis).
    /// spec: Docs/RE/specs/effects.md §17.2 — billboard quad built in camera billboard basis; CONFIRMED.
    ///
    /// Mesh particles use sub-effect velocity/size to transform vertices.
    /// spec: Docs/RE/specs/effects.md §17.2 — mesh: vertices scaled by sampled size; CONFIRMED.
    /// </summary>
    private static void RebuildSubEffectMesh(
        MeshInstance3D mi,
        SubEffectDesc se,
        Vector3 origin,
        double elapsedMs,
        ImageTexture?[]? textures)
    {
        if (se.Keyframes.Length == 0) return;

        // ── Keyframe sampling ────────────────────────────────────────────────
        // spec: Docs/RE/specs/effects.md §17.3 — piecewise-linear sampling; CONFIRMED.
        // spec: Docs/RE/specs/effects.md §8.2 step 5/6 — frame_index and interpolation; CODE-CONFIRMED.
        uint texCount = se.TexCount;
        if (texCount == 0) return;

        uint stride = se.AnimStride > 0 ? se.AnimStride : 1u;

        // Wrap elapsed into loop period (looping cast-channel effect).
        // spec: Docs/RE/specs/effects.md §8.2 step 3 — phase_ms = elapsed_ms mod total_time; CODE-CONFIRMED.
        double phase = se.TotalTime > 0
            ? elapsedMs % se.TotalTime
            : elapsedMs % (stride * texCount);

        int frameIdx = (int)(phase / stride);
        float frac = (float)((phase % stride) / stride);

        int kfCount = se.Keyframes.Length;
        int kfA = Math.Min(frameIdx, kfCount - 1);
        int kfB = Math.Min(frameIdx + 1, kfCount - 1);

        XeffKeyframe kA = se.Keyframes[kfA];
        XeffKeyframe kB = se.Keyframes[kfB];

        // Linear lerp on all scalar/Vec3 channels.
        // spec: Docs/RE/specs/effects.md §17.3 — linear lerp for velocity/size/alpha; CONFIRMED.
        float vx = kA.VelocityX + (kB.VelocityX - kA.VelocityX) * frac;
        float vy = kA.VelocityY + (kB.VelocityY - kA.VelocityY) * frac;
        float vz = kA.VelocityZ + (kB.VelocityZ - kA.VelocityZ) * frac;

        float sx = kA.SizeX + (kB.SizeX - kA.SizeX) * frac;
        float sy = kA.SizeY + (kB.SizeY - kA.SizeY) * frac;
        float sz = kA.SizeZ + (kB.SizeZ - kA.SizeZ) * frac;

        // Alpha: sample from alpha curve; fallback to 1.0 when curve is empty.
        // Alpha already un-inverted at parse time (in_memory = 1.0 − file_value).
        // spec: Docs/RE/formats/effects.md §A.6 — alpha stored inverted; CONFIRMED.
        float alpha = SampleCurveLinear(se.AlphaKeys, frameIdx, frac);

        // Diffuse tint: sample the per-keyframe R/G/B curve linearly; defaults to (1,1,1) when empty.
        // The curve arrays are already assembled in R/G/B order by the layer-03 parser, so no channel
        // swap is applied here — the on-disk B,G,R,A byte reversal is a pack-site detail of the original
        // binary, not of the sampled in-memory Vec3 (x=R) the parser hands us.
        // spec: Docs/RE/specs/effects.md §17.3 — colour is a per-keyframe diffuse tint (R/G/B), not a
        //       scale; linear lerp; defaults to white; sampled Vec3 is x=R,y=G,z=B; CONFIRMED.
        float diffR = SampleCurveLinear(se.DiffuseR, frameIdx, frac);
        float diffG = SampleCurveLinear(se.DiffuseG, frameIdx, frac);
        float diffB = SampleCurveLinear(se.DiffuseB, frameIdx, frac);

        // Velocity displacement from origin (identity orientation for UserXEffect).
        // spec: Docs/RE/specs/effects.md §8.2 step 8 — world_pos = origin + rotate(quat, velocity) × scale; CODE-CONFIRMED.
        // Cast-channel: looping UserXEffect uses identity orientation.
        // spec: Docs/RE/specs/effects.md §15.4 — "Default transform … no extra anchor offset"; CODE-CONFIRMED.
        //
        // PORT-SIDE Z-NEGATION: the origin is taken from the actor's GlobalPosition (already Godot-space,
        // i.e. Z-negated via WorldCoordinates.ToGodot). The keyframe velocity is parsed in the legacy
        // world convention, so its Z must be negated too — the negation is applied to BOTH the anchor
        // and the sub-effect offset, never one without the other (campaign-9c flying-pixels fix).
        // spec: Docs/RE/specs/effects.md §8.2 step 8 — port negates Z on both anchor AND offset; CONFIRMED.
        var displace = new Vector3(vx, vy, -vz);
        Vector3 particlePos = origin + displace;

        // ── Sprite frame index (stepped — no interpolation) ──────────────────
        // spec: Docs/RE/specs/effects.md §17.3 — sprite frame: stepped, no interpolation; CONFIRMED.
        int spriteFrame = Math.Min(frameIdx, (int)texCount - 1);

        // ── UV scroll ────────────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 scroll U, bit 1 scroll V; MEDIUM.
        float uOff = se.ScrollU ? (float)((elapsedMs % UvScrollPeriodMs) / UvScrollPeriodMs) : 0f;
        float vOff = se.ScrollV ? (float)((elapsedMs % UvScrollPeriodMs) / UvScrollPeriodMs) : 0f;

        // Sampled per-frame diffuse tint fed into both the vertex Color and the material AlbedoColor.
        // spec: Docs/RE/specs/effects.md §17.3 — vertex diffuse RGB from the colour curve; CONFIRMED.
        var tint = new Color(diffR, diffG, diffB, alpha);

        // ── Geometry by emitter type ─────────────────────────────────────────
        ArrayMesh? mesh = se.EmitterType switch
        {
            EmitterBillboard => BuildBillboardQuad(sx, sy, tint, uOff, vOff, preRotate90Y: false),
            EmitterDirectional => BuildBillboardQuad(sx, sy, tint, uOff, vOff, preRotate90Y: true),
            _ => BuildMeshParticle(kA, kB, frac, sx, sy, sz, tint, uOff, vOff),
        };

        if (mesh is null) return;

        mi.Mesh = mesh;
        mi.GlobalPosition = particlePos;

        // Apply texture for the current sprite frame.
        // spec: Docs/RE/formats/effects.md §A.4.1 — texture for frame i = textures[i]; CONFIRMED.
        if (textures is { Length: > 0 })
        {
            int texIdx = Math.Min(spriteFrame, textures.Length - 1);
            if (textures[texIdx] is { } tex)
            {
                StandardMaterial3D mat = BuildEffectMaterial(tex, tint);
                mi.SetSurfaceOverrideMaterial(0, mat);
            }
        }
        else
        {
            // No texture: use unshaded solid colour modulated by the sampled diffuse tint.
            // spec: Docs/RE/specs/effects.md §17.3 — diffuse tint drives AlbedoColor; CONFIRMED.
            var mat = new StandardMaterial3D
            {
                ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = tint,
                Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
                BlendMode = StandardMaterial3D.BlendModeEnum.Add,
                BillboardMode = se.EmitterType <= EmitterDirectional
                    ? StandardMaterial3D.BillboardModeEnum.Enabled
                    : StandardMaterial3D.BillboardModeEnum.Disabled,
            };
            mi.SetSurfaceOverrideMaterial(0, mat);
        }
    }

    /// <summary>
    /// Builds a camera-facing billboard quad from the sampled size channels.
    /// spec: Docs/RE/specs/effects.md §17.2 — billboard: four corners at ±0.5·size_x / ±0.5·size_y; CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.8 — size Vec3 drives billboard half-extents; HIGH.
    /// </summary>
    private static ArrayMesh BuildBillboardQuad(
        float sizeX, float sizeY,
        Color tint,
        float uOff, float vOff,
        bool preRotate90Y)
    {
        // spec: Docs/RE/specs/effects.md §8.2 step 9 emitter_type 0 —
        //   half_width = 0.5 × size_x; half_height = 0.5 × size_y; four corners; CONFIRMED.
        float hw = 0.5f * Math.Abs(sizeX);
        float hh = 0.5f * Math.Abs(sizeY);
        // Minimum visible size (aesthetic, not spec-dictated).
        hw = MathF.Max(hw, 0.05f);
        hh = MathF.Max(hh, 0.05f);

        // Build quad in local XY plane; BillboardMode in the material makes it camera-facing.
        // For directional (type 2) we conceptually apply a 90° Y pre-rotation;
        // in practice we swap X↔Z to approximate the effect without camera-basis math.
        // spec: Docs/RE/specs/effects.md §17.2 emitter_type 1 (oriented-quad) —
        //   "extra fixed 90° rotation about Y applied before the camera-facing transform"; CONFIRMED.
        var (aX, aY, bX, bY, cX, cY, dX, dY) = preRotate90Y
            ? (-hh, hw, hh, hw, hh, -hw, -hh, -hw) // rotated 90°
            : (-hw, hh, hw, hh, hw, -hh, -hw, -hh); // standard

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)ArrayMesh.ArrayType.Max);

        arrays[(int)ArrayMesh.ArrayType.Vertex] = new Vector3[]
        {
            new(aX, aY, 0f),
            new(bX, bY, 0f),
            new(cX, cY, 0f),
            new(dX, dY, 0f),
        };
        arrays[(int)ArrayMesh.ArrayType.TexUV] = new Vector2[]
        {
            new(0f + uOff, 0f + vOff),
            new(1f + uOff, 0f + vOff),
            new(1f + uOff, 1f + vOff),
            new(0f + uOff, 1f + vOff),
        };
        // Per-vertex diffuse tint (R,G,B from the sampled colour curve; A from the alpha curve).
        // spec: Docs/RE/specs/effects.md §17.3 — vertex diffuse = sampled colour curve × alpha; CONFIRMED.
        arrays[(int)ArrayMesh.ArrayType.Color] = new Color[]
        {
            tint,
            tint,
            tint,
            tint,
        };
        // Two triangles (CCW for Godot right-handed).
        arrays[(int)ArrayMesh.ArrayType.Index] = new int[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Builds a mesh-particle quad from the sampled velocity/size/alpha.
    /// For the MVP, produces a simple oriented quad scaled by (sizeX, sizeY).
    /// spec: Docs/RE/specs/effects.md §17.2 emitter_type else (mesh) — scale by size Vec3; CONFIRMED.
    /// </summary>
    private static ArrayMesh BuildMeshParticle(
        XeffKeyframe kA, XeffKeyframe kB, float frac,
        float sx, float sy, float sz,
        Color tint, float uOff, float vOff)
    {
        // Reuse billboard shape scaled by sx/sy; sz drives depth for future 3D mesh.
        // A real implementation would sample the .xobj mesh vertices here.
        // XOBJ-HOOK: load data/effect/xobj/<resource_id>.eff via XeffMiniParser or AssetPassthrough
        //   and transform each vertex by (sx, sy, sz) scale and the sampled orientation quaternion.
        //   spec: Docs/RE/formats/effects.md §A.11 — .xobj ASCII mesh format; CONFIRMED.
        //   spec: Docs/RE/specs/effects.md §17.2 — mesh: per-vertex scale by size Vec3; CONFIRMED.
        return BuildBillboardQuad(sx, sy, tint, uOff, vOff, preRotate90Y: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Material helper
    // ─────────────────────────────────────────────────────────────────────────

    private static StandardMaterial3D BuildEffectMaterial(ImageTexture texture, Color tint)
    {
        // AlbedoColor carries the sampled per-keyframe diffuse tint (R,G,B) and alpha; the texture is
        // modulated by it. Previously hardcoded white, which dropped the .xeff diffuse colour curve.
        // spec: Docs/RE/specs/effects.md §17.3 — diffuse tint drives AlbedoColor (not white); CONFIRMED.
        return new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
            BlendMode = StandardMaterial3D.BlendModeEnum.Add,
            BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Texture loading
    // ─────────────────────────────────────────────────────────────────────────

    private ImageTexture?[]? LoadSubEffectTextures(SubEffectDesc se)
    {
        if (_assets is null || se.TextureNames.Length == 0)
            return null;

        var result = new ImageTexture?[se.TextureNames.Length];
        for (int t = 0; t < se.TextureNames.Length; t++)
        {
            string name = se.TextureNames[t];
            if (string.IsNullOrEmpty(name)) continue;

            // Texture resolution: data/effect/texture/<name>.tga
            // spec: Docs/RE/formats/effects.md §A.4.1 — full path: data/effect/texture/<name>.tga; CONFIRMED.
            string vfsPath = $"data/effect/texture/{name}.tga";
            result[t] = _assets.LoadTexture(vfsPath);

            if (result[t] is null)
            {
                // Some textures may use .dds extension instead; try that as well.
                result[t] = _assets.LoadTexture($"data/effect/texture/{name}.dds");
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // .xeff loading (mini-parser, corrected 8-byte header spec)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to load and parse a .xeff file by raw effect_id.
    ///
    /// Resolution order (per spec):
    ///   1. Registry lookup: _effectRegistry[effectId] → vfs path (built from xeffect.lst at boot).
    ///      spec: Docs/RE/formats/effects.md §C.2 — "runtime ALWAYS resolves through registry keyed
    ///      by raw effect_id; NO numeric-name sprintf in original; CONFIRMED."
    ///      spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst manifest.
    ///   2. Numeric-name fallback: data/effect/xeff/{effectId}.xeff — NOT in original (Option A
    ///      REJECTED per spec §C.2), but kept as a DOCUMENTED last-resort so the demo/dev path
    ///      still works for numeric-named files (984 of 3,584 are numeric and their filename
    ///      coincides with the effect_id by authoring convention).
    ///      spec: Docs/RE/formats/effects.md §C.2 — "Option A REJECTED; no numeric-name path in binary."
    ///   On a registry miss with no numeric-name file, logs and returns null (placeholder fallback).
    /// </summary>
    private SubEffectDesc[]? TryLoadXeff(uint effectId)
    {
        if (_assets is null) return null;

        // 1) Registry resolve (primary — spec §C.2 CONFIRMED).
        string? vfsPath = null;
        if (_effectRegistry is { } reg && reg.TryGetValue(effectId, out string? regPath))
        {
            vfsPath = regPath;
            GD.Print($"[EffectRenderer] Registry hit: effectId={effectId} → {vfsPath}. " +
                     "spec: Docs/RE/formats/effects.md §C.2 registry resolve.");
        }

        // 2) Numeric-name fallback (DOCUMENTED last-resort; NOT in original — spec §C.2 Option A REJECTED).
        if (vfsPath is null)
        {
            string numericPath = $"data/effect/xeff/{effectId}.xeff";
            ReadOnlyMemory<byte> probe = _assets.GetRaw(numericPath);
            if (!probe.IsEmpty)
            {
                vfsPath = numericPath;
                GD.Print($"[EffectRenderer] Registry MISS for effectId={effectId}; numeric fallback hit: {vfsPath}. " +
                         "NOTE: original had no numeric-path fallback (spec §C.2 Option A REJECTED). " +
                         "This path is a dev/demo last-resort only.");
            }
        }

        if (vfsPath is null)
        {
            GD.Print(
                $"[EffectRenderer] effectId={effectId}: not in registry, no numeric file found — using placeholder.");
            return null;
        }

        ReadOnlyMemory<byte> raw = _assets.GetRaw(vfsPath);
        if (raw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] .xeff not found in VFS: {vfsPath} — using placeholder.");
            return null;
        }

        try
        {
            // Use the shared layer-03 parser (corrected 8-byte header).
            // spec: Docs/RE/formats/effects.md §A.2 — XEFF_HEADER_SIZE = 8 (0x08); VERIFIED.
            XeffData data = XeffParser.ParseXeff(raw);
            if (data.SubEffects.Length == 0)
                return [];

            // Map XeffSubEffect → SubEffectDesc (presentation view-model).
            var results = new SubEffectDesc[data.SubEffects.Length];
            for (int i = 0; i < data.SubEffects.Length; i++)
                results[i] = MapSubEffect(data.SubEffects[i]);
            return results;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] .xeff parse failed ({vfsPath}): {ex.Message} — using placeholder.");
            return null;
        }
    }

    /// <summary>
    /// Maps a shared <see cref="XeffSubEffect"/> (layer-03 parse output) into a presentation
    /// <see cref="SubEffectDesc"/> view-model, applying all boundary conversions:
    ///
    ///   ALPHA INVERSION: shared AlphaKeys are RAW file values (0.0=opaque, 1.0=transparent per
    ///   spec §A.6). EffectRenderer renders with 0=transparent/1=opaque convention.
    ///   Boundary: inMemory = 1.0f − fileValue.
    ///   spec: Docs/RE/formats/effects.md §A.6 — alpha inversion; CONFIRMED.
    ///
    ///   TexCount (presentation) == EntryCount (shared model) — the name-table/keyframe count.
    ///   spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14; CONFIRMED.
    ///
    ///   TotalTime derived: EntryCount × AnimStride + AnimBaseTime.
    ///   spec: Docs/RE/formats/effects.md §A.4.3 — total_time = tex_count × anim_stride + anim_base_time; CONFIRMED.
    ///
    ///   ScrollU/V derived from low byte of EntryCount (bit0=U, bit1=V).
    ///   spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
    /// </summary>
    private static SubEffectDesc MapSubEffect(XeffSubEffect se)
    {
        // Alpha inversion: shared model stores file values (0=opaque); presentation expects 1=opaque.
        // spec: Docs/RE/formats/effects.md §A.6 — alpha stored as 1.0−opacity; CONFIRMED.
        float[] alphaKeys = new float[se.AlphaKeys.Length];
        for (int i = 0; i < se.AlphaKeys.Length; i++)
            alphaKeys[i] = 1f - se.AlphaKeys[i]; // un-invert at consumption boundary

        // TexCount == EntryCount (number of keyframes/name-table entries).
        // spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14; CONFIRMED.
        uint texCount = se.EntryCount;

        // TotalTime = tex_count × anim_stride + anim_base_time.
        // spec: Docs/RE/formats/effects.md §A.4.3 — total_time derivation; CONFIRMED.
        uint totalTime = texCount * se.AnimStride + se.AnimBaseTime;

        // ScrollU/V from low byte of EntryCount: bit 0 = U, bit 1 = V.
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
        bool scrollU = (texCount & 1u) != 0;
        bool scrollV = (texCount & 2u) != 0;

        return new SubEffectDesc
        {
            EmitterType = se.EmitterType,
            ResourceId = se.ResourceId,
            AnimFlag = se.AnimFlag,
            TexCount = texCount,
            TextureNames = se.TextureNames,
            AlphaKeys = alphaKeys,
            // Per-keyframe diffuse-RGB tint curve carried straight through (already assembled in
            // R/G/B order by the layer-03 parser). NOT a scale — see §17.3.
            // spec: Docs/RE/formats/effects.md §A.4.2 — pass 2/3/4 = per-keyframe diffuse R/G/B.
            DiffuseR = se.DiffuseR,
            DiffuseG = se.DiffuseG,
            DiffuseB = se.DiffuseB,
            AnimLoop = se.AnimLoop,
            AnimStride = se.AnimStride,
            AnimBaseTime = se.AnimBaseTime,
            TotalTime = totalTime,
            Keyframes = se.Keyframes,
            ScrollU = scrollU,
            ScrollV = scrollV,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Keyframe curve sampling helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Samples a float curve by linear interpolation between adjacent key values.
    /// Returns 1.0 when the curve is empty (no keys → default opaque alpha, default scale 1).
    /// spec: Docs/RE/specs/effects.md §17.3 — linear lerp for scalar channels; CONFIRMED.
    /// </summary>
    private static float SampleCurveLinear(float[] keys, int frameIdx, float frac)
    {
        if (keys.Length == 0) return 1f;
        int a = Math.Min(frameIdx, keys.Length - 1);
        int b = Math.Min(frameIdx + 1, keys.Length - 1);
        return keys[a] + (keys[b] - keys[a]) * frac;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Placeholder fallback (original GpuParticles3D implementation)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns a looping GpuParticles3D placeholder centred at <paramref name="position"/>.
    /// This is the degradation path used when the .xeff is missing or unparseable.
    /// The node is added as a child of this EffectRenderer.
    /// </summary>
    private GpuParticles3D SpawnPlaceholderEffect(
        Vector3 position,
        uint effectId)
    {
        var particles = new GpuParticles3D
        {
            Name = $"CastEffect_{effectId}",
            GlobalPosition = position,
            OneShot = false,
            Emitting = true,
            Amount = PlaceholderParticleCount,
            Lifetime = PlaceholderLifetime,
            Explosiveness = 0f,
            Randomness = 0.3f,
        };

        var particleMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.4f,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 60f,
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 1.5f,
            Gravity = new Vector3(0f, -2f, 0f),
            ScaleMin = 0.08f,
            ScaleMax = 0.12f,
            Color = new Color(1.0f, 0.55f, 0.1f, 1.0f),
        };

        var colorRamp = new Gradient();
        colorRamp.SetColor(0, new Color(1.0f, 0.55f, 0.1f, 1.0f));
        colorRamp.SetOffset(0, 0.0f);
        colorRamp.SetColor(1, new Color(1.0f, 0.4f, 0.0f, 0.0f));
        colorRamp.SetOffset(1, 1.0f);
        var colorTex = new GradientTexture1D { Gradient = colorRamp };
        particleMat.ColorRamp = colorTex;
        particles.ProcessMaterial = particleMat;

        var quadMesh = new QuadMesh { Size = new Vector2(0.1f, 0.1f) };
        var drawMat = new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.55f, 0.1f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.4f, 0.0f),
            EmissionEnergyMultiplier = 2.5f,
            Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
            BlendMode = StandardMaterial3D.BlendModeEnum.Add,
            BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled,
        };
        quadMesh.Material = drawMat;
        particles.DrawPasses = 1;
        particles.SetDrawPassMesh(0, quadMesh);

        AddChild(particles);
        return particles;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Teardown helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void TeardownLiveEffect(LiveEffect live)
    {
        // Tear down real .xeff mesh instances.
        if (live.MeshInstances is not null)
        {
            foreach (MeshInstance3D? mi in live.MeshInstances)
            {
                if (mi is not null && IsInstanceValid(mi))
                    mi.QueueFree();
            }
        }

        // Tear down GPU-particle placeholders for resource_id >= 10000 sub-effects.
        if (live.GpuParticles is not null)
        {
            foreach (GpuParticles3D? gpu in live.GpuParticles)
            {
                if (gpu is not null && IsInstanceValid(gpu))
                {
                    gpu.Emitting = false;
                    var timer = GetTree().CreateTimer(PlaceholderLifetime + 0.1);
                    timer.Timeout += () =>
                    {
                        if (IsInstanceValid(gpu)) gpu.QueueFree();
                    };
                }
            }
        }

        // Tear down placeholder fallback.
        if (live.Particles is not null && IsInstanceValid(live.Particles))
        {
            live.Particles.Emitting = false;
            var timer = GetTree().CreateTimer(PlaceholderLifetime + 0.1);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(live.Particles)) live.Particles.QueueFree();
            };
        }
    }

    private void ClearAllEffects()
    {
        foreach (LiveEffect live in _live.Values)
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
        ulong instanceId = actor.GetInstanceId();
        return new ActorKey((uint)(instanceId & 0xFFFF_FFFF), default);
    }
}