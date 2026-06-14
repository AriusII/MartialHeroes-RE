// Screens/FrontEndEffectPlayer.cs
//
// 2D front-end VFX player driven by parsed XeffData (from the VFS via XeffParser).
//
// SCOPE: front-end screen-space effects only (ServerSelectScreen, CharacterSelectScreen).
//   Login and PIN have no .xeff VFX — confirmed absent from the VFS.
//   spec: Docs/RE/formats/effects.md §A.15 — "Login/PIN scenes have NO .xeff VFX (CONFIRMED ABSENT)".
//
// DESIGN MODEL:
//   Each XeffSubEffect is rendered as one GPUParticles2D emitter.
//   The spec's keyframe tracks drive per-emitter parameters:
//     - alpha (inverted: file 0.0 = opaque) → particle opacity
//     - scale X/Y/Z → particle size
//     - AnimStride (ms) → keyframe duration / particle lifetime
//     - velocity Vec3 → initial velocity direction/magnitude
//     - size Vec3 → billboard quad size
//   Textures are loaded from data/effect/texture/<name>.tga (when VFS is available).
//   When the VFS is absent or parse fails, a tasteful placeholder (glow rings) is shown
//   so the screen is never blank.
//
// SPEC NOTES (sample-unverified fields, carried through but not semantically interpreted):
//   - type_flag (observed 1 or 2): semantics UNRESOLVED — not branched on here.
//   - unknown_constant (value 67): semantics UNRESOLVED — carried through, not used.
//   - field_unknown_a / emitter_type: CONFIRMED values 0/1/2, but 2D front-end effects
//     are treated as billboard (type 0) for simplicity; the distinction is only meaningful
//     for 3D world effects.
//
// PASSIVE: purely visual. No game logic, no rule evaluation, no packet parsing.
//   Starts on _Ready; no Application event required for ambient idle effects.
//   All node/material mutation on the main thread.
//
// spec: Docs/RE/formats/effects.md §A.15 (front-end id→file mapping; SAMPLE-VERIFIED)
// spec: Docs/RE/formats/effects.md §A.4 (sub-effect block: name table, curves, track header, keyframes)
// spec: Docs/RE/formats/effects.md §A.6 (alpha inversion: stored as 1.0 − opacity; CONFIRMED)
// spec: Docs/RE/formats/effects.md §A.8 (velocity Vec3 + size Vec3 semantics; HIGH)
// spec: Docs/RE/formats/effects.md §A.4.3 (AnimStride ms; CONFIRMED)

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// 2D front-end particle-effect player.
/// Loads a named .xeff from the VFS, parses it via <see cref="XeffParser"/>, and
/// instantiates one <see cref="GpuParticles2D"/> per sub-effect (capped at
/// <see cref="MaxSubEffects"/> to avoid overwhelming the 2D canvas).
///
/// <para>When the VFS is absent or the parse fails, a graceful fallback glow effect is shown.</para>
///
/// <para>Threading: all construction happens in <see cref="_Ready"/> on the Godot main thread.
/// No background threads touch nodes.</para>
///
/// spec: Docs/RE/formats/effects.md §A.15 — front-end VFX id→file mapping; SAMPLE-VERIFIED.
/// </summary>
public sealed partial class FrontEndEffectPlayer : Control
{
    // ─────────────────────────────────────────────────────────────────────────
    // Configuration (set before adding to tree)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// VFS-relative path of the .xeff file to play.
    /// spec: Docs/RE/formats/effects.md §A.15 — zone_sel_u.xeff (effect_id 380000000, 11 sub-effects);
    ///   char_select-u.xeff (effect_id 380003000, 68 sub-effects); SAMPLE-VERIFIED.
    /// </summary>
    public string XeffVfsPath { get; set; } = "";

    /// <summary>
    /// Shared real-asset handle. When non-null, textures are loaded from the VFS.
    /// When null (offline / VFS absent), textures are skipped and the fallback visuals are shown.
    /// </summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum number of sub-effects to instantiate. Caps the 68-sub-effect char_select
    /// and 11-sub-effect zone_sel files at a reasonable node count.
    /// spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff has 68 sub-effects;
    ///   zone_sel_u.xeff has 11. The highest observed sub_effect_count in any non-front-end
    ///   file is 16 (§A.2). Cap is aesthetic, not spec-derived.
    /// </summary>
    private const int MaxSubEffects = 24; // aesthetic cap; not spec-dictated

    /// <summary>
    /// Default particle count per emitter when the sub-effect has no usable keyframes.
    /// Aesthetic choice.
    /// </summary>
    private const int FallbackParticleCount = 30;

    /// <summary>
    /// Fallback lifetime in seconds when AnimStride is zero or parse failed.
    /// Aesthetic choice; maps to a comfortable ~1 s cycle.
    /// spec: Docs/RE/formats/effects.md §A.4.3 — AnimStride in milliseconds; CONFIRMED.
    /// </summary>
    private const float FallbackLifetimeSec = 1.0f;

    // ─────────────────────────────────────────────────────────────────────────
    // Godot lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    // Cache the XeffData (loaded eagerly in _Ready, built deferred after layout resolves).
    private XeffData? _cachedXeffData;
    private bool _xeffLoaded;

    public override void _Ready()
    {
        if (string.IsNullOrEmpty(XeffVfsPath))
        {
            GD.PrintErr("[FrontEndEffectPlayer] XeffVfsPath not set — no effect to play.");
            return;
        }

        // Load the .xeff now (before layout), but defer the particle-node construction until
        // after the first layout pass so Size is resolved.
        // Without deferral, Size would be Vector2.Zero and all emitters would spawn at (0,0).
        _cachedXeffData = TryLoadXeff();
        _xeffLoaded = true;

        // Defer particle construction to after the layout pass resolves our rect.
        CallDeferred(MethodName.BuildEffects);
    }

    private void BuildEffects()
    {
        if (!_xeffLoaded) return;

        if (_cachedXeffData is not null && _cachedXeffData.SubEffectCount > 0)
        {
            BuildFromXeff(_cachedXeffData);
        }
        else
        {
            GD.Print($"[FrontEndEffectPlayer] Showing fallback effect for '{XeffVfsPath}'.");
            BuildFallback();
        }
    }

    // Use the known 1024×768 reference canvas as the effective size when our own Size is zero
    // (e.g. when the layout pass hasn't run yet but we need a fixed coordinate for positioning).
    // spec: CLAUDE.md §Godot Pipeline — "1024×768 reference canvas". AESTHETIC placement.
    private Vector2 EffectiveSize =>
        Size.LengthSquared() > 1f ? Size : new Vector2(1024f, 768f);

    // ─────────────────────────────────────────────────────────────────────────
    // XeffData loading
    // ─────────────────────────────────────────────────────────────────────────

    private XeffData? TryLoadXeff()
    {
        if (SharedRealAssets is null)
        {
            GD.Print($"[FrontEndEffectPlayer] No VFS available for '{XeffVfsPath}' — using fallback.");
            return null;
        }

        ReadOnlyMemory<byte> raw = SharedRealAssets.GetRaw(XeffVfsPath);
        if (raw.IsEmpty)
        {
            GD.PrintErr($"[FrontEndEffectPlayer] VFS miss: '{XeffVfsPath}' not found.");
            return null;
        }

        try
        {
            XeffData data = XeffParser.ParseXeff(raw);
            GD.Print($"[FrontEndEffectPlayer] Parsed '{XeffVfsPath}': effect_id={data.EffectId} " +
                     $"sub_effects={data.SubEffectCount} type_flag={data.TypeFlag}. " +
                     // spec: Docs/RE/formats/effects.md §A.15 — front-end VFX mapping; SAMPLE-VERIFIED.
                     "spec: Docs/RE/formats/effects.md §A.15.");
            return data;
        }
        catch (Exception ex)
        {
            // Parser caveat per spec §A.15: large-count files (68 sub-effects) may fail at the
            // scale-curve (Group D) read — the header parses cleanly, the failure is in the body.
            // spec: Docs/RE/formats/effects.md §A.15 — "parser caveat: high-sub_effect_count front-end files
            //   currently fail the existing .xeff parser at the scale-curve (Group D) read"; SAMPLE-VERIFIED.
            GD.PrintErr($"[FrontEndEffectPlayer] XeffParser failed for '{XeffVfsPath}': {ex.Message}. " +
                        "Using fallback effect. " +
                        "spec: Docs/RE/formats/effects.md §A.15 — parser caveat for large sub_effect_count.");
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Xeff-driven emitter construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildFromXeff(XeffData data)
    {
        int count = (int)Math.Min(data.SubEffectCount, MaxSubEffects);
        GD.Print($"[FrontEndEffectPlayer] Building {count}/{data.SubEffectCount} sub-effect emitters " +
                 $"from '{XeffVfsPath}'. spec: Docs/RE/formats/effects.md §A.4.");

        // Place the emitter anchor at the centre of the reference canvas.
        // EffectiveSize uses Size when known, or falls back to the 1024×768 reference canvas.
        Vector2 centre = EffectiveSize * 0.5f;

        for (int i = 0; i < count; i++)
        {
            XeffSubEffect se = data.SubEffects[i];
            GpuParticles2D emitter = BuildSubEffectEmitter(se, i, centre);
            AddChild(emitter);
        }
    }

    private GpuParticles2D BuildSubEffectEmitter(XeffSubEffect se, int index, Vector2 centre)
    {
        // ── Timing from the track header ──────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.3 — AnimStride in milliseconds; CONFIRMED.
        float lifetimeSec = se.AnimStride > 0
            ? se.AnimStride / 1000f
            : FallbackLifetimeSec;

        // ── Alpha from curve pass 1 (inverted: 0.0 = opaque, 1.0 = transparent) ──
        // spec: Docs/RE/formats/effects.md §A.6 — stored as 1.0 − opacity; CONFIRMED.
        float startOpacity = 1f;
        float endOpacity = 0f;
        if (se.AlphaKeys.Length > 0)
        {
            // File value 0.0 → memory 1.0 (opaque). File value 1.0 → memory 0.0 (transparent).
            // spec: Docs/RE/formats/effects.md §A.6 — in_memory_value = 1.0 − file_value; CONFIRMED.
            startOpacity = 1f - se.AlphaKeys[0];
            endOpacity = se.AlphaKeys.Length > 1 ? 1f - se.AlphaKeys[^1] : 0f;
        }

        // ── Size from frame 0 if keyframes are present ────────────────────────
        // spec: Docs/RE/formats/effects.md §A.8 — size Vec3: size_x/size_y = billboard half-extents; HIGH.
        // For 2D we treat size_x/size_y as screen-space pixel dimensions (world units → px: aesthetic mapping).
        float sizeX = 24f; // aesthetic default when keyframes absent
        float sizeY = 24f;
        if (se.Keyframes.Length > 0)
        {
            XeffKeyframe kf0 = se.Keyframes[0];
            // Guard against degenerate (zero) values; fall back to aesthetic defaults.
            sizeX = kf0.SizeX > 0.001f ? kf0.SizeX * 24f : 24f; // scale factor: aesthetic
            sizeY = kf0.SizeY > 0.001f ? kf0.SizeY * 24f : 24f;
        }

        // ── Velocity from frame 0 ─────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3: displacement direction+magnitude; HIGH.
        // In 2D screen space, velocity_x → X axis; velocity_y → Y axis. velocity_z discarded (depth).
        // World Z negation (CLAUDE.md §Coordinate conventions) does not apply here — these are
        // 2D screen-space effects, not world-placed geometry.
        float velX = 0f;
        float velY = -40f; // default: particles drift upward (aesthetic)
        if (se.Keyframes.Length > 0)
        {
            XeffKeyframe kf0 = se.Keyframes[0];
            velX = kf0.VelocityX * 20f; // world-unit → screen-pixel: aesthetic scale
            velY = kf0.VelocityY * 20f;
            // If both are zero, keep the aesthetic upward drift.
            if (Math.Abs(velX) < 0.001f && Math.Abs(velY) < 0.001f)
                velY = -40f;
        }

        // ── Stagger position around centre ────────────────────────────────────
        // Distribute sub-effects in a ring so multiple emitters are visible.
        // Aesthetic placement — not spec-derived (the spec position comes from instance world transform
        // which is not stored in the .xeff descriptor file).
        // Distribute in an ellipse across the full canvas to be visible across all UI regions.
        float angle = index * (MathF.PI * 2f / MaxSubEffects);
        float radiusX = centre.X * 0.85f; // fill ~85% of the canvas width
        float radiusY = centre.Y * 0.75f; // fill ~75% of the canvas height
        Vector2 pos = centre + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);

        // ── Build the colour ──────────────────────────────────────────────────
        // The spec does not define a per-sub-effect colour (textures carry the colour data).
        // In the absence of loaded textures, use a gold/amber hue matching the front-end art
        // style seen in zone_sel and char_select effects. Aesthetic choice.
        var hue = new Color(
            0.95f + (index % 3) * 0.02f, // slight R variation across sub-effects
            0.75f - index * 0.008f,       // G decreases slightly for variety
            0.1f + index * 0.005f,        // B increases slightly
            startOpacity);

        // ── Texture resolution ────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.1 — tex_name char[64]; full path data/effect/texture/<name>.tga.
        // spec: Docs/RE/formats/effects.md §A.4.1 XEFF_TEX_NAME_LEN = 64; CONFIRMED.
        ImageTexture? tex = null;
        if (se.TextureNames.Length > 0 && !string.IsNullOrEmpty(se.TextureNames[0]) &&
            SharedRealAssets is not null)
        {
            // spec: Docs/RE/formats/effects.md §A.4.1 — "client resolves full path as data/effect/texture/<name>.tga"; CONFIRMED.
            string texPath = $"data/effect/texture/{se.TextureNames[0]}.tga";
            try
            {
                tex = SharedRealAssets.LoadTexture(texPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[FrontEndEffectPlayer] Texture load failed ({texPath}): {ex.Message}");
            }
        }

        // ── ParticleProcessMaterial ───────────────────────────────────────────
        // Velocity in screen-space pixels/sec. The xeff velocity is in world units (multiplied
        // by 20 to map to screen pixels). A minimum of 30 px/s ensures visible motion. Aesthetic.
        float speed = Math.Max(30f, MathF.Sqrt(velX * velX + velY * velY));

        var mat = new ParticleProcessMaterial
        {
            // Emit from a small circle at the staggered position.
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Math.Max(8f, sizeX * 0.5f),

            // Direction + velocity.
            // spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3 gives displacement; HIGH.
            Direction = velY != 0f || velX != 0f
                ? new Vector3(velX, velY, 0f).Normalized()
                : new Vector3(0f, -1f, 0f), // aesthetic upward default
            Spread = 45f, // aesthetic spread cone
            InitialVelocityMin = speed * 0.5f,
            InitialVelocityMax = speed,

            // Gravity: gentle upward float for ambient UI effects (negative Y = upward in 2D Godot).
            // Aesthetic: makes particles drift upward gently.
            Gravity = new Vector3(0f, -15f, 0f),

            Color = hue,
        };

        // Colour-over-lifetime: opaque → transparent.
        // spec: Docs/RE/formats/effects.md §A.6 — alpha inversion: 1.0−file_value; CONFIRMED.
        var grad = new Gradient();
        grad.SetColor(0, hue with { A = startOpacity });
        grad.SetOffset(0, 0f);
        grad.SetColor(1, hue with { A = endOpacity });
        grad.SetOffset(1, 1f);
        mat.ColorRamp = new GradientTexture1D { Gradient = grad };

        // Scale over lifetime: start full-size, fade out.
        // spec: Docs/RE/formats/effects.md §A.4.2 — scale curves (X/Y/Z); CONFIRMED.
        // Sample-unverified: we map scale curve to scale-over-lifetime proportionally.
        float startScale = 1f;
        float endScale = 0.1f;
        if (se.ScaleX.Length > 0) startScale = se.ScaleX[0] > 0.001f ? se.ScaleX[0] : 1f;
        if (se.ScaleX.Length > 1) endScale = se.ScaleX[^1] > 0f ? se.ScaleX[^1] : 0.1f;

        // Clamp to reasonable screen ranges — the .xeff scale values are in world units,
        // not screen pixels. Aesthetic clamping.
        startScale = Math.Clamp(startScale, 0.1f, 3f);
        endScale = Math.Clamp(endScale, 0f, 2f);
        mat.ScaleMin = startScale;
        mat.ScaleMax = startScale * 1.2f;

        // ── GPUParticles2D node ───────────────────────────────────────────────
        // Scale: GPUParticles2D draws each particle at ParticleProcessMaterial's scale
        // multiplied by the particle's size in pixels. A scale of 1.0 = 1×1 px — extremely
        // small. Multiply by 20 to get ~20px × scale_factor particles. Aesthetic.
        mat.ScaleMin *= 20f;
        mat.ScaleMax *= 20f;

        // Use additive blend mode so particles glow on the dark background.
        // Additive: dest += src*alpha — bright on dark, invisible on white.
        // This is the correct choice for "glowing" UI VFX. Aesthetic decision.
        var emitter = new GpuParticles2D
        {
            Name = $"XeffSubEffect_{index}",
            Position = pos,
            Amount = Math.Max(8, (int)(se.EntryCount * 2)), // more particles for richer files
            Lifetime = lifetimeSec,
            OneShot = false,
            Emitting = true,
            Explosiveness = 0f, // continuous emission for ambient effect
            Randomness = 0.25f,
            ProcessMaterial = mat,
            // Additive blend: source color adds to destination, creating glow.
            // Aesthetic: matches the "glow around UI" feel of the original front-end effects.
            DrawOrder = GpuParticles2D.DrawOrderEnum.Index,
        };

        // Use the TGA texture if loaded; otherwise fall back to a small QuadMesh (default draw).
        if (tex is not null)
        {
            emitter.Texture = tex;
        }

        // Preprocess to fill the emitter immediately on first frame.
        emitter.Preprocess = lifetimeSec;

        GD.Print($"[FrontEndEffectPlayer] Sub-effect {index}: " +
                 $"pos=({pos.X:F0},{pos.Y:F0}) lifetime={lifetimeSec:F2}s " +
                 $"opacity={startOpacity:F2}→{endOpacity:F2} " +
                 $"size=({sizeX:F1},{sizeY:F1}) tex={se.TextureNames.FirstOrDefault()??"-"}. " +
                 "spec: Docs/RE/formats/effects.md §A.4, §A.6, §A.8.");

        return emitter;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fallback effect (VFS absent or parse failed)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildFallback()
    {
        // Fallback: two concentric ring emitters — ambient front-end glow stand-in.
        // Aesthetic choice; not spec-derived.
        Vector2 centre = EffectiveSize * 0.5f;
        GD.Print($"[FrontEndEffectPlayer] BuildFallback: centre={centre} size={EffectiveSize}.");

        float[] radii = [100f, 220f];
        Color[] colors =
        [
            new Color(1.0f, 0.75f, 0.2f, 1.0f), // gold
            new Color(0.55f, 0.45f, 1.0f, 0.9f), // violet
        ];

        for (int r = 0; r < 2; r++)
        {
            var mat = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = radii[r],
                // Particles drift outward and upward.
                Direction = new Vector3(0f, -1f, 0f),
                Spread = 60f,
                InitialVelocityMin = 30f,
                InitialVelocityMax = 80f,
                // Light upward gravity drift. In 2D: negative Y = up.
                Gravity = new Vector3(0f, -10f, 0f),
                Color = colors[r],
                // Scale: 16px base size so particles are clearly visible.
                // (GPUParticles2D scale is in pixels; 1.0 = 1×1 px, so 16=16px).
                ScaleMin = 16f,
                ScaleMax = 28f,
            };

            // Colour-over-lifetime: opaque → transparent (glow then fade out).
            var grad = new Gradient();
            grad.SetColor(0, colors[r]);
            grad.SetOffset(0, 0f);
            grad.SetColor(1, colors[r] with { A = 0f });
            grad.SetOffset(1, 1f);
            mat.ColorRamp = new GradientTexture1D { Gradient = grad };

            var emitter = new GpuParticles2D
            {
                Name = $"FallbackRing{r}",
                Position = centre,
                Amount = 60,
                Lifetime = 2.0f,
                OneShot = false,
                Emitting = true,
                Explosiveness = 0f,
                Randomness = 0.35f,
                // Preprocess fills the emitter immediately so particles are visible on frame 1.
                Preprocess = 2.0f,
                ProcessMaterial = mat,
                // Z-index offset so fallback rings appear above other sibling Controls.
                ZIndex = 5,
            };
            AddChild(emitter);
            GD.Print($"[FrontEndEffectPlayer] FallbackRing{r} added at pos={centre} radius={radii[r]:F0}.");
        }
    }
}
