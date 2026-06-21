using Godot;

namespace MartialHeroes.Client.Godot.World;

// EffectRenderer — partial: per-frame .xeff tick + keyframe sampling + mesh rebuild.
// spec: Docs/RE/specs/effects.md §17.3 — piecewise-linear keyframe sampling; CONFIRMED.
public sealed partial class EffectRenderer
{
    // ─────────────────────────────────────────────────────────────────────────
    // Per-frame .xeff tick
    // ─────────────────────────────────────────────────────────────────────────

    private void TickXeffEffect(LiveEffect live, SubEffectDesc[] subEffects, double deltaMs = 0)
    {
        if (!IsInstanceValid(live.Anchor)) return;

        var anchorPos = live.Anchor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);
        var elapsedMs = live.ElapsedMs;

        for (var i = 0; i < subEffects.Length; i++)
        {
            var se = subEffects[i];

            // GPU-particle sub-effects: tick the simulation node and follow anchor.
            // spec: Docs/RE/formats/effects.md §E.2.2 — Euler integration per sim step: CODE-CONFIRMED.
            if (se.ResourceId >= XeffResourceParticleThreshold)
            {
                // Tick the real Euler-integration sim node (if present).
                var sim = live.SimNodes?[i];
                if (sim is not null && IsInstanceValid(sim))
                {
                    sim.GlobalPosition = anchorPos;
                    sim.Tick(deltaMs / 1000.0); // deltaMs is the per-frame ms; Tick expects seconds
                }
                else
                {
                    // Fallback: legacy GpuParticles3D (always null now; keep for compat).
                    var gpu = live.GpuParticles?[i];
                    if (gpu is not null && IsInstanceValid(gpu))
                        gpu.GlobalPosition = anchorPos;
                }

                continue;
            }

            var existing = live.MeshInstances?[i];
            if (existing is null) continue;

            // Rebuild geometry for this sub-effect at the current elapsed time.
            var texRow = live.Textures?[i];
            RebuildSubEffectMesh(existing, se, anchorPos, elapsedMs, texRow);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mesh rebuild — driven by keyframe state each frame
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Rebuilds the <see cref="ArrayMesh" /> on <paramref name="mi" /> for the current keyframe
    ///     state, computing all geometry in world-space from the anchor origin and sampled channels.
    ///     Billboard geometry built here is camera-facing (Godot billboard basis).
    ///     spec: Docs/RE/specs/effects.md §17.2 — billboard quad built in camera billboard basis; CONFIRMED.
    ///     Mesh particles use sub-effect velocity/size to transform vertices.
    ///     spec: Docs/RE/specs/effects.md §17.2 — mesh: vertices scaled by sampled size; CONFIRMED.
    ///     PERFORMANCE NOTE: this method allocates a new <see cref="ArrayMesh" /> and
    ///     <see cref="StandardMaterial3D" /> on every call frame. For high-frequency effects with
    ///     many sub-effects this is a per-frame GC pressure source. A future optimisation should
    ///     cache the material keyed by (spriteFrame, tint) and the mesh keyed by (size, origin) when
    ///     neither has changed since the last frame — this is deferred pending real-world profiling.
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
        var texCount = se.TexCount;
        if (texCount == 0) return;

        var stride = se.AnimStride > 0 ? se.AnimStride : 1u;

        // Wrap elapsed into loop period (looping cast-channel effect).
        // spec: Docs/RE/specs/effects.md §8.2 step 3 — phase_ms = elapsed_ms mod total_time; CODE-CONFIRMED.
        var phase = se.TotalTime > 0
            ? elapsedMs % se.TotalTime
            : elapsedMs % (stride * texCount);

        var frameIdx = (int)(phase / stride);
        var frac = (float)(phase % stride / stride);

        var kfCount = se.Keyframes.Length;
        var kfA = Math.Min(frameIdx, kfCount - 1);
        var kfB = Math.Min(frameIdx + 1, kfCount - 1);

        var kA = se.Keyframes[kfA];
        var kB = se.Keyframes[kfB];

        // Linear lerp on all scalar/Vec3 channels.
        // spec: Docs/RE/specs/effects.md §17.3 — linear lerp for velocity/size/alpha; CONFIRMED.
        var vx = kA.VelocityX + (kB.VelocityX - kA.VelocityX) * frac;
        var vy = kA.VelocityY + (kB.VelocityY - kA.VelocityY) * frac;
        var vz = kA.VelocityZ + (kB.VelocityZ - kA.VelocityZ) * frac;

        var sx = kA.SizeX + (kB.SizeX - kA.SizeX) * frac;
        var sy = kA.SizeY + (kB.SizeY - kA.SizeY) * frac;
        var sz = kA.SizeZ + (kB.SizeZ - kA.SizeZ) * frac;

        // Alpha: sample from alpha curve; fallback to 1.0 when curve is empty.
        // Alpha already un-inverted at parse time (in_memory = 1.0 − file_value).
        // spec: Docs/RE/formats/effects.md §A.6 — alpha stored inverted; CONFIRMED.
        var alpha = SampleCurveLinear(se.AlphaKeys, frameIdx, frac);

        // Diffuse tint: sample the per-keyframe R/G/B curve linearly; defaults to (1,1,1) when empty.
        // The curve arrays are already assembled in R/G/B order by the layer-03 parser, so no channel
        // swap is applied here — the on-disk B,G,R,A byte reversal is a pack-site detail of the original
        // binary, not of the sampled in-memory Vec3 (x=R) the parser hands us.
        // spec: Docs/RE/specs/effects.md §17.3 — colour is a per-keyframe diffuse tint (R/G/B), not a
        //       scale; linear lerp; defaults to white; sampled Vec3 is x=R,y=G,z=B; CONFIRMED.
        var diffR = SampleCurveLinear(se.DiffuseR, frameIdx, frac);
        var diffG = SampleCurveLinear(se.DiffuseG, frameIdx, frac);
        var diffB = SampleCurveLinear(se.DiffuseB, frameIdx, frac);

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
        var particlePos = origin + displace;

        // ── Sprite frame index (stepped — no interpolation) ──────────────────
        // spec: Docs/RE/specs/effects.md §17.3 — sprite frame: stepped, no interpolation; CONFIRMED.
        var spriteFrame = Math.Min(frameIdx, (int)texCount - 1);

        // ── UV scroll ────────────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 scroll U, bit 1 scroll V; MEDIUM.
        var uOff = se.ScrollU ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;
        var vOff = se.ScrollV ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;

        // Sampled per-frame diffuse tint fed into both the vertex Color and the material AlbedoColor.
        // spec: Docs/RE/specs/effects.md §17.3 — vertex diffuse RGB from the colour curve; CONFIRMED.
        var tint = new Color(diffR, diffG, diffB, alpha);

        // ── Geometry by emitter type ─────────────────────────────────────────
        var mesh = se.EmitterType switch
        {
            EmitterBillboard => BuildBillboardQuad(sx, sy, tint, uOff, vOff, false),
            EmitterDirectional => BuildBillboardQuad(sx, sy, tint, uOff, vOff, true),
            _ => BuildMeshParticle(kA, kB, frac, sx, sy, sz, tint, uOff, vOff)
        };

        if (mesh is null) return;

        mi.Mesh = mesh;
        mi.GlobalPosition = particlePos;

        // Apply texture for the current sprite frame.
        // spec: Docs/RE/formats/effects.md §A.4.1 — texture for frame i = textures[i]; CONFIRMED.
        if (textures is { Length: > 0 })
        {
            var texIdx = Math.Min(spriteFrame, textures.Length - 1);
            if (textures[texIdx] is { } tex)
            {
                var mat = BuildEffectMaterial(tex, tint);
                mi.SetSurfaceOverrideMaterial(0, mat);
            }
        }
        else
        {
            // No texture: use unshaded solid colour modulated by the sampled diffuse tint.
            // spec: Docs/RE/specs/effects.md §17.3 — diffuse tint drives AlbedoColor; CONFIRMED.
            // Blend mode: default is alpha (SRCALPHA/INVSRCALPHA); additive is an override for the
            // additive particle sub-bucket. Per-drawable blend byte is a SPEC GAP (effects.md §14.8)
            // — default to Mix (alpha) until the field is recovered.
            // spec: Docs/RE/specs/rendering.md §3.3/§4.2 — FX sub-buckets default SRCALPHA/INVSRCALPHA;
            //   additive SRCALPHA/ONE is an override, NOT the universal default.
            // spec-gap: per-sub-effect blend-state field location in .xeff (effects.md §14.8).
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = tint,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix, // spec: rendering.md §4.2 — default alpha
                BillboardMode = se.EmitterType <= EmitterDirectional
                    ? BaseMaterial3D.BillboardModeEnum.Enabled
                    : BaseMaterial3D.BillboardModeEnum.Disabled
            };
            mi.SetSurfaceOverrideMaterial(0, mat);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Keyframe curve sampling helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Samples a float curve by linear interpolation between adjacent key values.
    ///     Returns 1.0 when the curve is empty (no keys → default opaque alpha, default scale 1).
    ///     spec: Docs/RE/specs/effects.md §17.3 — linear lerp for scalar channels; CONFIRMED.
    /// </summary>
    private static float SampleCurveLinear(float[] keys, int frameIdx, float frac)
    {
        if (keys.Length == 0) return 1f;
        var a = Math.Min(frameIdx, keys.Length - 1);
        var b = Math.Min(frameIdx + 1, keys.Length - 1);
        return keys[a] + (keys[b] - keys[a]) * frac;
    }
}