using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Spawns and tears-down actor-anchored placeholder effects in response to cast lifecycle events.
///
/// MVP SCOPE — this pass does NOT parse or render real .xeff geometry.
/// The visual is a tasteful <see cref="GpuParticles3D"/> sphere-burst centered on the caster's
/// position. The hook where the real .xeff mesh/particle loader will attach is clearly marked
/// below with <c>// XEFF-HOOK:</c> comments.
///
/// Lifecycle driven by action codes from the network:
///   0xC8 = cast-enable  → PlayCast(actor, effectId)  — starts a looping effect
///   0xC9 = cast-disable → StopCast(actor)             — soft-stops the running effect
///   0xCB = secondary disable → StopCast(actor)        — same teardown (same spec site)
/// spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
///
/// Effect resolution chain (for integrators):
///   skill_id → skills.scr record field at byte offset 1136 → cast_effect_id
/// spec: Docs/RE/specs/effects.md §15.1 — byte offset 1136 = cast_effect_id field; CODE-CONFIRMED.
/// spec: Docs/RE/specs/effects.md §15.4 — cast effect is a looping UserXEffect, actor-anchored; CODE-CONFIRMED.
///
/// HUD hub subscription:
///   Subscribes to <see cref="IHudEventHub.CombatTexts"/> to mirror cast-triggered damage numbers,
///   so the EffectRenderer can in future cross-wire floating-text with the right visual.
///   The channel is drained each frame in <see cref="_Process"/> — no background thread touches Nodes.
///
/// PASSIVE: zero game logic, zero formula, zero protocol parsing. Reads only what the Application
/// layer delivers via event channels or direct PlayCast/StopCast calls.
///
/// Threading: <see cref="PlayCast"/> and <see cref="StopCast"/> MUST be called from the Godot main
/// thread (same contract as all other *Node* mutations in this layer). The channel drain also runs
/// on the main thread in _Process.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive rendering.
/// </summary>
public sealed partial class EffectRenderer : Node3D
{
    // ─────────────────────────────────────────────────────────────────────────
    // Per-actor live effect record (VIEW state only — no domain state)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>View-state record for one live placeholder effect.</summary>
    private sealed class LiveEffect
    {
        /// <summary>The Godot particle node representing this effect (child of EffectRenderer).</summary>
        public GpuParticles3D Particles = null!;

        /// <summary>
        /// The effect id as read from skills.scr byte offset 1136.
        /// spec: Docs/RE/specs/effects.md §15.2 — cast_effect_id at byte offset 1136; CODE-CONFIRMED.
        /// </summary>
        public uint EffectId;

        // XEFF-HOOK: when the real .xeff parser is available, add a field here:
        //   public XeffDescriptor? XeffDescriptor;
        // and use it in BuildEffectVisual() to drive real billboard/mesh geometry instead of GpuParticles3D.
        // spec: Docs/RE/formats/effects.md §A.4 — sub-effect block layout (name table, curves, keyframes).
        // spec: Docs/RE/formats/effects.md §A.12 — emitter_type: 0=billboard, 1=mesh-particle, 2=dir-billboard.
        // spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3 + size Vec3 field semantics.
    }

    // ActorKey → live particle effect node (one per actor at most).
    private readonly Dictionary<ActorKey, LiveEffect> _live = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Tunable constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lift applied to the particle emitter origin above the actor's feet, so the aura sits
    /// at body/waist height rather than at ground level.
    /// spec: Docs/RE/specs/effects.md §15.4 — "effect origin follows the caster's world position"
    ///       (actor-anchored at body/feet origin, not a skeleton bone); CODE-CONFIRMED.
    /// </summary>
    private const float EmitterHeightOffset = 0.9f;

    /// <summary>Number of particles per burst cycle for the placeholder visual.</summary>
    private const int PlaceholderParticleCount = 40;

    /// <summary>
    /// Particle lifetime in seconds for the placeholder.
    /// Chosen to give a smooth looping aura feel without being distracting.
    /// </summary>
    private const float PlaceholderLifetime = 1.0f;

    // ─────────────────────────────────────────────────────────────────────────
    // HUD hub subscription
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Non-null when Bind() has been called with a live hub.</summary>
    private IHudEventHub? _hub;

    /// <summary>True when we are running in DEMO mode (no hub bound).</summary>
    private bool _demoMode;

    /// <summary>Cancellation for the channel-drain task (if we ever spin one up off-thread).</summary>
    private CancellationTokenSource? _cts;

    // ─────────────────────────────────────────────────────────────────────────
    // Godot lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parameterless _Ready: if no hub has been bound yet, shows a small DEMO state —
    /// a single placeholder effect at the world origin — so the node is visually identifiable
    /// when first added to the scene without wiring.
    /// Mirrors the pattern used by <see cref="HUD.InventoryWindow"/> and <see cref="HUD.SkillWindow"/>.
    /// </summary>
    public override void _Ready()
    {
        GD.Print("[EffectRenderer] _Ready.");

        // Deferred demo: wait one frame so the scene tree is fully set up, then check.
        CallDeferred(MethodName.MaybeLaunchDemoEffect);
    }

    /// <summary>Called deferred from _Ready. Spawns a demo effect if no hub was bound.</summary>
    private void MaybeLaunchDemoEffect()
    {
        if (_hub is null && !_demoMode)
        {
            _demoMode = true;
            // Spawn a small demo placeholder at the origin so the renderer is visible
            // when dropped into a scene without wiring.
            SpawnPlaceholderEffect(
                position: GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f),
                effectId: 0,
                demoLabel: "[EffectRenderer DEMO — no hub bound]");

            GD.Print("[EffectRenderer] No hub bound — running in DEMO mode. " +
                     "Call Bind(hub) to subscribe to cast-effect events.");
        }
    }

    /// <summary>
    /// Each frame: drain the CombatTexts channel from the hub (if bound) and forward to the
    /// floating-text system (future wiring point). All Node mutations are on the main thread here.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain channels each frame".
    /// </summary>
    public override void _Process(double delta)
    {
        if (_hub is null) return;

        // Drain CombatTextEvent channel (non-blocking TryRead loop).
        var reader = _hub.CombatTexts;
        while (reader.TryRead(out CombatTextEvent? ev))
        {
            // FUTURE-HOOK: forward ev to a floating combat-text sub-node.
            // GD.Print removed from this hot-path drain loop to keep it allocation-free.
            _ = ev; // suppress unused warning until the FUTURE-HOOK is wired
        }
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API — Bind / Subscribe surface
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Binds this renderer to the application <see cref="IHudEventHub"/>.
    /// The integrator news up a <see cref="HudEventHub"/> once and exposes it as
    /// <see cref="IHudEventHub"/>; this call wires the renderer to that hub.
    ///
    /// Must be called on the Godot main thread, typically from GameLoop._Ready or
    /// the node that owns the EffectRenderer in the scene tree.
    ///
    /// Calling Bind() cancels the DEMO effect if one was started.
    /// </summary>
    /// <param name="hub">The live hub; must not be null.</param>
    public void Bind(IHudEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;

        // Cancel any demo effect.
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
    /// Spawns a looping actor-anchored placeholder effect for the given cast.
    ///
    /// Called by the integrator when action code 0xC8 (cast-enable) is received.
    /// spec: Docs/RE/specs/effects.md §15.3 — 0xC8 = cast-enable; CODE-CONFIRMED.
    /// spec: Docs/RE/specs/effects.md §15.4 — looping UserXEffect, actor-anchored; CODE-CONFIRMED.
    ///
    /// The <paramref name="effectId"/> comes from the skill record at byte offset 1136
    /// (cast_effect_id), resolved before this call by the Application layer.
    /// spec: Docs/RE/specs/effects.md §15.2 — byte offset 1136 = cast_effect_id; CODE-CONFIRMED.
    ///
    /// This method MUST be called on the Godot main thread.
    /// </summary>
    /// <param name="actor">The caster's scene node. Used to anchor the effect at the actor's world position.</param>
    /// <param name="effectId">
    /// The cast_effect_id from the skill record (byte offset 1136).
    /// spec: Docs/RE/specs/effects.md §15.2 — cast_effect_id; CODE-CONFIRMED.
    /// The 9-digit .xeff filename is decimal(effectId).
    /// spec: Docs/RE/formats/effects.md §A.2 — filename = decimal(effect_id); SAMPLE-VERIFIED.
    /// </param>
    public void PlayCast(Node3D actor, uint effectId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ActorKey key = ResolveActorKey(actor);

        // Soft-stop any pre-existing effect for this actor (idempotent restart).
        StopCast(actor);

        // Compute the emitter origin: actor world position + body-height lift.
        // spec: Docs/RE/specs/effects.md §15.4 — effect anchored to caster's world position; CODE-CONFIRMED.
        Vector3 origin = actor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);

        // XEFF-HOOK: here is where the real .xeff loader would be invoked.
        // Pseudocode for the real implementation:
        //
        //   XeffDescriptor? desc = XeffRegistry.TryGet(effectId);   // lazy-load §5.1
        //   if (desc is null) { GD.PrintErr(...); return; }         // abandon on miss (§5.2)
        //   // Build per-element geometry from desc.SubEffects:
        //   //   emitter_type 0 → camera-facing quad (billboard)
        //   //     spec: Docs/RE/formats/effects.md §A.12, emitter_type 0; CONFIRMED.
        //   //   emitter_type 1 → mesh-particle from .xobj
        //   //     spec: Docs/RE/formats/effects.md §A.12, emitter_type 1; CONFIRMED.
        //   //   emitter_type 2 → directional billboard (extra 90° Y pre-rotation)
        //   //     spec: Docs/RE/formats/effects.md §A.12, emitter_type 2; CONFIRMED.
        //   // Use desc.SubEffects[i].AnimStride (ms) as the keyframe cadence.
        //   //   spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride in ms; CONFIRMED.
        //   // Loop flag from XEffect.loop_flag (offset +0x3C in instance):
        //   //   spec: Docs/RE/specs/effects.md §6.1 — loop_flag u8; CODE-CONFIRMED.
        //   // Alpha inversion: stored as 1.0 − opacity; loader corrects on parse.
        //   //   spec: Docs/RE/formats/effects.md §A.6 — alpha inversion; CONFIRMED.
        //   // Scale default = 1.0 at construction; caller_scale = 1.0 for cast-channel.
        //   //   spec: Docs/RE/specs/effects.md §15.4 — "Default transform … scale 1.0"; CODE-CONFIRMED.
        //   // FX1/FX2 terrain layer geometry is a SEPARATE format (terrain_layers.md §Section 1).
        //   //   spec: Docs/RE/formats/effects.md §Terrain FX — NOT xeff; do NOT parse here.

        GpuParticles3D particles = SpawnPlaceholderEffect(origin, effectId);

        var liveEffect = new LiveEffect
        {
            Particles = particles,
            EffectId = effectId,
        };

        _live[key] = liveEffect;

        GD.Print($"[EffectRenderer] PlayCast: actor={key.RawId} effectId={effectId} " +
                 $"origin={origin}. Placeholder GPUParticles3D spawned. " +
                 "spec: Docs/RE/specs/effects.md §15.4 looping actor-anchored UserXEffect; CODE-CONFIRMED.");
    }

    /// <summary>
    /// Soft-stops the running cast effect for the given actor.
    ///
    /// Called by the integrator when action code 0xC9 (cast-disable) or 0xCB (secondary disable)
    /// is received.
    /// spec: Docs/RE/specs/effects.md §15.3 — 0xC9/0xCB = cast-disable; CODE-CONFIRMED.
    /// spec: Docs/RE/specs/effects.md §15.5 — soft-stop: active flag cleared, removed next frame; CODE-CONFIRMED.
    ///
    /// If no effect is running for the actor, this is a no-op (safe to call speculatively).
    /// This method MUST be called on the Godot main thread.
    /// </summary>
    /// <param name="actor">The caster's scene node.</param>
    public void StopCast(Node3D actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ActorKey key = ResolveActorKey(actor);
        if (!_live.Remove(key, out LiveEffect? live))
            return; // No active effect for this actor — silent no-op (spec §15.5 soft-stop).

        // Soft-stop the placeholder: disable emission and let existing particles expire.
        // This mirrors the spec's soft-stop behaviour (active flag cleared; instance removed
        // on the following frame by the tick loop).
        // spec: Docs/RE/specs/effects.md §15.5 — "soft-stop … active flag cleared … removed next frame".
        if (live.Particles.IsInsideTree())
        {
            live.Particles.Emitting = false;
            // QueueFree after one particle lifetime so existing particles can finish.
            // This avoids a hard pop/cut identical to the spec's "no fade-out" teardown.
            // spec: Docs/RE/specs/effects.md §15.5 — "no fade-out … visual is whatever keyframe shows"; CODE-CONFIRMED.
            var timer = GetTree().CreateTimer(PlaceholderLifetime + 0.1);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(live.Particles))
                    live.Particles.QueueFree();
            };
        }

        GD.Print($"[EffectRenderer] StopCast: actor={key.RawId} effectId={live.EffectId} soft-stopped. " +
                 "spec: Docs/RE/specs/effects.md §15.5 soft-stop; CODE-CONFIRMED.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns a looping <see cref="GpuParticles3D"/> placeholder effect centred at
    /// <paramref name="position"/>. The node is added as a child of this EffectRenderer.
    ///
    /// The placeholder uses a sphere-burst emission shape — tasteful, low-cost, and clearly
    /// identifiable as "this is a cast aura". It is looping (Explosiveness=0, OneShot=false).
    ///
    /// XEFF-HOOK — real geometry entry point:
    ///   Replace the <see cref="GpuParticles3D"/> construction below with the real .xeff
    ///   mesh-building path once <see cref="Assets.Parsers.Effects.XeffParser"/> (layer 03)
    ///   is available. The parser produces a descriptor from the file at:
    ///     data/effect/xeff/{effectId}.xeff  (numeric-named files)
    ///   or by registry id-lookup (non-numeric files).
    ///   spec: Docs/RE/formats/effects.md §A.2 — filename = decimal(effect_id); SAMPLE-VERIFIED.
    ///   spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst manifest; HIGH confidence.
    ///   spec: Docs/RE/formats/effects.md §C.2 — lazy-load descriptor registry; CODE-CONFIRMED.
    ///
    ///   Note: NEVER call GltfDocument.AppendFromBuffer — it crashes natively on this project's
    ///   generated GLBs. Build ArrayMesh directly from the .xeff vertex/index data.
    ///   spec: CLAUDE.md §Known Godot Pitfalls — "GltfDocument.AppendFromBuffer crashes natively".
    ///
    ///   FX1/FX2 terrain layer geometry (terrain_layers.md §Section 1) is a SEPARATE format;
    ///   do NOT parse it here.
    /// </summary>
    private GpuParticles3D SpawnPlaceholderEffect(
        Vector3 position,
        uint effectId,
        string? demoLabel = null)
    {
        var particles = new GpuParticles3D
        {
            Name = $"CastEffect_{effectId}",
            GlobalPosition = position,

            // Looping, non-one-shot — mirrors the spec's loop flag for cast-channel effects.
            // spec: Docs/RE/specs/effects.md §6.1 — loop_flag u8 non-zero = looping; CODE-CONFIRMED.
            OneShot = false,
            Emitting = true,

            Amount = PlaceholderParticleCount,
            Lifetime = PlaceholderLifetime,

            // Explosiveness = 0 → continuous trickle (aura feel rather than a burst).
            // A burst (Explosiveness = 1) would be one-shot-like and contradict the looping spec.
            Explosiveness = 0f,
            Randomness = 0.3f,
        };

        // Process material: a glow-orange billboard particle.
        // The exact colour is a placeholder; real .xeff files carry per-element alpha/scale curves.
        // spec: Docs/RE/formats/effects.md §A.4.2 — curve section: alpha, scale X/Y/Z; CONFIRMED.
        var particleMat = new ParticleProcessMaterial
        {
            // Emission from a small sphere at the emitter origin (radius 0.4 wu).
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.4f,

            // Upward initial velocity so particles arc away from the feet.
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 60f,
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 1.5f,

            // Gravity pulls particles back down for an arc shape.
            Gravity = new Vector3(0f, -2f, 0f),

            // Scale over lifetime: start at 0.08, shrink to 0 (fade out).
            ScaleMin = 0.08f,
            ScaleMax = 0.12f,

            // Color over lifetime: orange → transparent.
            // spec: Docs/RE/formats/effects.md §A.6 — alpha stored inverted (1.0 − opacity); CONFIRMED.
            //   Real .xeff alpha uses the inversion convention; here we drive it directly as Godot RGBA.
            Color = new Color(1.0f, 0.55f, 0.1f, 1.0f),
        };

        // Colour-over-lifetime: orange fully opaque → orange transparent.
        // Godot Gradient starts with 2 default points (black at 0, white at 1);
        // we overwrite them in-place via SetColor/SetOffset — no add/remove needed.
        var colorRamp = new Gradient();
        colorRamp.SetColor(0, new Color(1.0f, 0.55f, 0.1f, 1.0f)); // opaque orange at t=0
        colorRamp.SetOffset(0, 0.0f);
        colorRamp.SetColor(1, new Color(1.0f, 0.4f, 0.0f, 0.0f)); // transparent orange at t=1
        colorRamp.SetOffset(1, 1.0f);
        var colorTex = new GradientTexture1D { Gradient = colorRamp };
        particleMat.ColorRamp = colorTex;

        particles.ProcessMaterial = particleMat;

        // Draw pass: a small quad mesh (camera-facing billboard).
        // spec: Docs/RE/formats/effects.md §A.12 — emitter_type 0 = billboard sprite; CONFIRMED.
        //   The real xeff emitter produces camera-facing quads from size_x/size_y half-extents.
        //   spec: Docs/RE/formats/effects.md §A.8 — size Vec3 drives billboard half-extents; HIGH.
        //   This placeholder uses a fixed 0.1×0.1 quad; real .xeff values come from keyframe size.
        var quadMesh = new QuadMesh
        {
            Size = new Vector2(0.1f, 0.1f),
        };

        // Emissive material so the particles glow even without scene lighting.
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

        // Optional: 3D debug label in DEMO mode.
        if (demoLabel is not null)
        {
            var label = new Label3D
            {
                Text = demoLabel,
                Position = new Vector3(0f, 1.2f, 0f),
                FontSize = 14,
                Modulate = new Color(1f, 0.8f, 0.2f),
            };
            particles.AddChild(label);
        }

        AddChild(particles);
        return particles;
    }

    /// <summary>
    /// Resolves an <see cref="ActorKey"/> for a given scene node.
    /// If the node is a <see cref="VisualActor"/>, uses its real key.
    /// Otherwise, falls back to a synthetic key derived from the node's instance id
    /// (safe for non-actor nodes in DEMO/test scenarios).
    /// </summary>
    private static ActorKey ResolveActorKey(Node3D actor)
    {
        if (actor is VisualActor va)
            return va.ActorKey;

        // Fallback: use the Godot instance id as a synthetic raw id.
        // This never happens in production (real casters are VisualActors), but prevents
        // crashes in test / headless scenarios.
        ulong instanceId = actor.GetInstanceId();
        return new ActorKey((uint)(instanceId & 0xFFFF_FFFF), default);
    }

    /// <summary>Stops and removes all live effects (used when the hub changes or on reset).</summary>
    private void ClearAllEffects()
    {
        foreach (LiveEffect live in _live.Values)
        {
            if (IsInstanceValid(live.Particles))
                live.Particles.QueueFree();
        }

        _live.Clear();
    }
}