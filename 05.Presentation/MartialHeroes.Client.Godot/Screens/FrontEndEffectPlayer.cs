// Screens/FrontEndEffectPlayer.cs
//
// 2D front-end VFX player driven by parsed XeffData (from the VFS via XeffParser).
//
// SCOPE: front-end screen-space effects only (ServerSelectScreen, CharacterSelectScreen).
//   Login and PIN have no .xeff VFX — confirmed absent from the VFS.
//   spec: Docs/RE/formats/effects.md §A.15 — "Login/PIN scenes have NO .xeff VFX (CONFIRMED ABSENT)".
//
// DESIGN MODEL:
//   Each XeffSubEffect will be rendered as one GPUParticles2D emitter once the .xeff spatial
//   descriptor fields are recovered from IDA (emitter position, size units, velocity
//   world-to-screen mapping). Until then BuildFromXeff logs and renders nothing.
//   Textures are loaded from data/effect/texture/<name>.tga (when VFS is available).
//   When the VFS is absent or parse fails, nothing is rendered (no synthetic stand-in).
//
// SPEC NOTES (unresolved fields):
//   - type_flag (observed 1 or 2): semantics UNRESOLVED — not branched on here.
//   - unknown_constant (value 67): semantics UNRESOLVED — carried through, not used.
//   - field_unknown_a / emitter_type: CONFIRMED values 0/1/2; distinction deferred.
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
/// will instantiate one <see cref="GpuParticles2D"/> per sub-effect once the .xeff spatial
/// descriptor fields (position, size, velocity mapping) are recovered from IDA.
///
/// <para>When the VFS is absent or the parse fails, nothing is rendered — no synthetic stand-in.</para>
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
    /// When null (offline / VFS absent), texture loads are skipped; nothing is rendered.
    /// </summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // No emitter-build constants yet — the .xeff spatial descriptor (position, size units,
    // velocity world-to-screen mapping) is not recovered; constants will be added when
    // BuildSubEffectEmitter is re-implemented from confirmed spec values.
    // Recovery targets: Docs/RE/formats/effects.md §A.4/§A.6/§A.8.

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
            GD.Print($"[FrontEndEffectPlayer] XeffVfsPath='{XeffVfsPath}' not loaded or parsed — " +
                     "rendering nothing.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // XeffData loading
    // ─────────────────────────────────────────────────────────────────────────

    private XeffData? TryLoadXeff()
    {
        if (SharedRealAssets is null)
        {
            GD.Print($"[FrontEndEffectPlayer] No VFS available for '{XeffVfsPath}' — skipping effect.");
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
                     $"sub_effects={data.SubEffectCount}. " +
                     // Header is 8 bytes (CORRECTED 2026-06-14): no file-level type_flag; emitter_type is per sub-effect (A.4.0).
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
                        "Rendering nothing. " +
                        "spec: Docs/RE/formats/effects.md §A.15 — parser caveat for large sub_effect_count.");
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Xeff-driven emitter construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildFromXeff(XeffData data)
    {
        // TODO: the .xeff spatial descriptor fields (emitter position, size units, velocity
        // world-to-screen mapping, spread, gravity) are not yet recovered from IDA.
        // Painting invented/aesthetic-guess particles (sizes 24f, velY −40f, gold hue, ellipse
        // radii, spread 45, ×20 scale) was removed because unconfirmed guesses produce visual
        // noise that diverges from the original.
        // Recovery targets:
        //   spec: Docs/RE/formats/effects.md §A.4 (sub-effect block layout)
        //   spec: Docs/RE/formats/effects.md §A.6 (alpha inversion)
        //   spec: Docs/RE/formats/effects.md §A.8 (velocity Vec3 + size Vec3 — HIGH confidence
        //         but world-to-screen mapping is unconfirmed)
        // When those fields are confirmed, re-implement BuildSubEffectEmitter with spec citations.
        GD.Print($"[FrontEndEffectPlayer] '{XeffVfsPath}' parsed ({data.SubEffectCount} sub-effects) " +
                 "but emitter build is deferred pending §A.4/§A.6/§A.8 spatial descriptor recovery. " +
                 "Rendering nothing.");
    }
}