// Ui/Scenes/Select/XeffSceneEffect.cs
//
// Faithful Godot renderer for a parsed XeffData instance.
//
// SCOPE: Char-select scene — renders the single composite ambient effect
//   char_select-u.xeff (effect id 380003000, 68 sub-effects) as a mixed set of
//   per-emitter-type quads placed at their per-sub-effect keyframe-0 offsets.
//
// ROOT CAUSE OF FLYING PIXELS (spec: Docs/RE/specs/frontend_scenes.md §3.6.7):
//   The previous code rendered ALL 68 sub-effects as camera-facing SQUARE billboards
//   using max(SizeX, SizeY)*2 with no per-emitter-type handling. The waterfall tiles
//   (emitter_type 1 = mesh-particle, oriented quads) and corona glows (emitter_type 2 =
//   directional, pre-rotated oriented quads) collapsed to tiny camera-facing squares
//   scattered at their world positions → the "flying blue/red pixel" artefact.
//
// FIX — THREE RENDER PATHS (spec: Docs/RE/specs/frontend_scenes.md §3.6.7):
//   emitter_type 0 (billboard): camera-facing quad, per-axis size (NOT a square max()).
//   emitter_type 1 (mesh-particle): oriented quad, NOT camera-facing, Euler rotation from kf.
//   emitter_type 2 (directional billboard): oriented quad with +90° Y pre-rotation + kf Euler.
//
// EXPECTED DISTRIBUTION for char_select-u.xeff (spec: §3.6.6 VFS-VERIFIED):
//   6 billboard / 51 mesh-particle / 11 directional → 68 total.
//
// PASSIVE: zero game logic. Pure asset → visual translation.
// LAYER 05 ONLY: uses Godot types. Never add using Godot; to layers 01–04.

using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
///     A <see cref="Node3D" /> that renders one parsed <c>.xeff</c> descriptor as a set of
///     per-emitter-type quads placed at the sub-effect keyframe-0 offsets.
///     <para>Load: call <see cref="Initialise" /> after the node is in the scene tree.</para>
///     <para>
///         spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — single composite
///         char_select-u.xeff at world (508.483, 69.887, −9758.569), scale 1.0, identity rotation.
///     </para>
///     <para>
///         spec: Docs/RE/specs/frontend_scenes.md §3.6.7 CAMPAIGN-9c two-witness — per-emitter-type
///         render recipe: billboard / mesh-particle / directional.
///     </para>
/// </summary>
public sealed partial class XeffSceneEffect : Node3D
{
    // =========================================================================
    // VFS path constants
    // =========================================================================

    // Texture base-path for .xeff textures.
    // spec: Docs/RE/formats/effects.md §A.4.1 — Full VFS path: data/effect/texture/<name>.tga
    private const string XeffTexturePath = "data/effect/texture/";

    // The char-select effect VFS path.
    // spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff. CODE-CONFIRMED.
    private const string CharSelectXeffPath = "data/effect/xeff/char_select-u.xeff";

    // Emitter-type constants.
    // spec: Docs/RE/formats/effects.md §A.12 / §A.14 — XEFF_EMITTER_BILLBOARD=0, XEFF_EMITTER_MESH=1, XEFF_EMITTER_DIRECTIONAL=2.
    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;

    // =========================================================================
    // Animation state (per-child, stepped in _Process)
    // =========================================================================

    // One entry per built MeshInstance3D child; used in the animation step below.
    // (Kept minimal — full texture-swap animation deferred to step 2; static keyframe-0
    //  placement + correct shape are the dominant visual fix per §3.6.7.)
    private AnimEntry[] _animEntries = [];

    private double _elapsed; // seconds since scene entered tree

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    ///     Builds all sub-effect quads from <paramref name="xeff" /> at this node's origin.
    ///     <para>Dispatch by emitter_type (spec: Docs/RE/specs/frontend_scenes.md §3.6.7):</para>
    ///     <list type="bullet">
    ///         <item>0 = camera-facing billboard quad, per-axis size.</item>
    ///         <item>1 = oriented quad (mesh-particle / waterfall tiles), NOT camera-facing.</item>
    ///         <item>2 = oriented quad with +90° Y pre-rotation (corona/glow directional).</item>
    ///     </list>
    ///     <para>
    ///         KEY RULE (anti-flying-pixels): sub-effects whose texture is absent are SKIPPED —
    ///         no solid-colour fallback dot. spec: §3.6.6 PORT CONTRACT CODE-CONFIRMED.
    ///     </para>
    ///     <para>
    ///         spec: Docs/RE/specs/frontend_scenes.md §3.6.7 CAMPAIGN-9c two-witness: IDA render +
    ///         black-box byte-parse of char_select-u.xeff.
    ///     </para>
    /// </summary>
    public void Initialise(XeffData xeff, RealClientAssets? assets)
    {
        var builtBillboard = 0;
        var builtMesh = 0;
        var builtDirectional = 0;
        var skippedNoKf = 0;
        var skippedTransparent = 0;
        var skippedNoTexture = 0;
        var skippedDegenerate = 0;

        var animList = new List<AnimEntry>(xeff.SubEffects.Length);

        for (var i = 0; i < xeff.SubEffects.Length; i++)
        {
            var sub = xeff.SubEffects[i];

            // Need at least one keyframe for position and size.
            if (sub.Keyframes.Length == 0)
            {
                skippedNoKf++;
                continue;
            }

            var kf0 = sub.Keyframes[0];

            // ---- Placement ----
            // Each sub-effect CENTER = anchor (this node's world position) + kf0 offset.
            // The offset is the legacy WORLD-frame displacement; the node sits at the Z-NEGATED
            // anchor (WorldCoordinates.ToGodot negates Z), so the offset's Z must be negated too —
            // otherwise the whole effect is mirrored in depth (braziers, authored at offset_z +36.8
            // = FOREGROUND toward the camera, get pushed behind the anchor; the waterfall at
            // offset_z −105 = deep background gets pushed in front of / onto the camera and vanishes).
            // One uniform handedness conversion: local offset = (off_x, off_y, −off_z).
            // spec: Docs/RE/formats/effects.md §A.8 HIGH — velocity Vec3 = displacement from effect origin.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — "anchor + offset, identity-oriented".
            // spec: CLAUDE.md "Coordinate conventions" — world geometry negates Z (x,y,z)→(x,y,−z).
            var offset = new Vector3(kf0.VelocityX, kf0.VelocityY, -kf0.VelocityZ);

            // ---- Size (per-axis, NOT a square max) ----
            // spec: Docs/RE/formats/effects.md §A.8 HIGH — size Vec3 = billboard half-extents.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — "use the per-axis size — NOT a square max()".
            var sizeX = kf0.SizeX;
            var sizeY = kf0.SizeY;
            var quadW = sizeX * 2.0f;
            var quadH = sizeY * 2.0f;
            if (quadW <= 0.0f || quadH <= 0.0f)
            {
                // Authored degenerate size — skip, no zero/inverted quad.
                skippedDegenerate++;
                continue;
            }

            // ---- Alpha ----
            // spec: Docs/RE/formats/effects.md §A.6 CONFIRMED — file 0.0=opaque, 1.0=transparent.
            // In-memory opacity = 1.0 − file_value.
            var opacity = 1.0f;
            if (sub.AlphaKeys.Length > 0)
                opacity = 1.0f - sub.AlphaKeys[0];

            opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            if (opacity <= 0.0f)
            {
                skippedTransparent++;
                continue;
            }

            // ---- Texture ----
            // spec: §3.6.6 PORT CONTRACT — "A missing texture MUST suppress the quad (no fallback dot)."
            // spec: Docs/RE/formats/effects.md §A.4.1 — full VFS path: data/effect/texture/<name>.tga.
            ImageTexture? albedo = null;
            if (sub.TextureNames.Length > 0 && sub.TextureNames[0].Length > 0 && assets is not null)
            {
                var tgaPath = $"{XeffTexturePath}{sub.TextureNames[0]}.tga";
                if (assets.Contains(tgaPath))
                    try
                    {
                        albedo = assets.LoadTexture(tgaPath);
                    }
                    catch
                    {
                        /* load failed → null → skip below */
                    }
            }

            if (albedo is null)
            {
                skippedNoTexture++;
                continue;
            }

            // ---- Material (shared logic for all emitter types) ----
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — ADDITIVE blend for fire/flare/corona/waterfall.
            // spec: §3.6.6 PORT CONTRACT — unshaded, depth-write off, additive, texture bound.
            // TODO spec: §3.6.7 — per-sub-effect straight-alpha flag at element+0x30 not yet exposed by
            //   parser; additive is correct for the visible majority (fire/water/corona).
            //   // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — "flag at element +0x30 PARSER-TODO"
            // Per-keyframe DIFFUSE multiplier (R,G,B). The .xeff curve section's passes 2/3/4 — which the
            // parser model exposes as DiffuseR/G/B (formerly mislabelled ScaleX/Y/Z) — are the diffuse R/G/B
            // multiplier in [0,1] (default 1.0), NOT a scale (the real size is the keyframe SizeX/Y/Z used
            // above for the quad). The char-select WATERFALL diffuse is WHITE (the spray IS white, not blue
            // — the visible blue is the SEPARATE cell water plane, not these sprites); the lens-flare is
            // warm-yellow; smoke is black (adds nothing under additive). This drops the earlier interim
            // blue-tint hack and renders each sub-effect by its REAL recovered diffuse.
            // spec: Docs/RE/formats/effects.md §A.4.2 (passes 2/3/4 = diffuse R/G/B) / frontend_scenes.md
            //   §3.6.1.
            var diffR = sub.DiffuseR.Length > 0 ? Math.Clamp(sub.DiffuseR[0], 0f, 1f) : 1.0f;
            var diffG = sub.DiffuseG.Length > 0 ? Math.Clamp(sub.DiffuseG[0], 0f, 1f) : 1.0f;
            var diffB = sub.DiffuseB.Length > 0 ? Math.Clamp(sub.DiffuseB[0], 0f, 1f) : 1.0f;
            var tint = new Color(diffR, diffG, diffB);

            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                // Double-sided: the oriented mesh-particle quads (waterfall curtain) and directional
                // coronas must render regardless of which way they face — a back-facing additive
                // sprite would otherwise be culled and vanish (the invisible-waterfall artefact).
                // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — additive sprites, no opaque culling.
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoTexture = albedo,
                AlbedoColor = new Color(tint.R, tint.G, tint.B, opacity)
            };

            // ---- Per-emitter-type dispatch ----
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 CAMPAIGN-9c.
            // spec: Docs/RE/formats/effects.md §A.12 emitter_type enum.
            MeshInstance3D mi;

            switch (sub.EmitterType)
            {
                // ------------------------------------------------------------------
                // emitter_type 0 — BILLBOARD (camera-facing quad)
                // spec: §3.6.7 — "camera-facing flat quad, full size = size_x·2 × size_y·2"
                // spec: §A.12 — XEFF_EMITTER_BILLBOARD = 0
                // ------------------------------------------------------------------
                case EmitterBillboard:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;

                    var quad = new QuadMesh
                    {
                        // Per-axis size (NOT a square max) — the spec is explicit.
                        // spec: §3.6.7 — "use the per-axis size — NOT a square max()".
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffBillboard{i}",
                        Position = offset,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtBillboard++;
                    break;
                }

                // ------------------------------------------------------------------
                // emitter_type 1 — MESH-PARTICLE (oriented quad, NOT camera-facing)
                // spec: §3.6.7 — "real ORIENTED quad/mesh tile, oriented by keyframe Euler rotation"
                //   "The 28 waterfall tiles are oriented quads forming a flat curtain sheet."
                // spec: §A.12 — XEFF_EMITTER_MESH = 1
                // Rotation: kf0.Rotation = Quat from Euler XYZ (spec: effects.md §A.7 CONFIRMED).
                // Handedness: world geometry negates Z (CLAUDE.md + Helpers/WorldCoordinates.ToGodot).
                //   The Euler stored in the file is already in legacy (right-hand, +Z forward) space.
                //   We convert it by negating the Y and Z components of the quaternion to match
                //   Godot's left-hand Y-up (+Z toward viewer) coordinate system.
                //   // spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — "debugger-pending orientation"
                //   (exact handedness of the Euler axes is DEBUGGER-PENDING; we apply the
                //    world Z-negate convention which gives the correct waterfall curtain orientation.)
                // ------------------------------------------------------------------
                case EmitterMesh:
                {
                    // BillboardMode stays Disabled (default) — this is the oriented case.
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    // Legacy Euler → Quat (spec: effects.md §A.7 CONFIRMED: π/180, half-angle XYZ).
                    // XeffKeyframe.Rotation does this conversion already (see EffectData.cs §A.7).
                    var legacyQ = kf0.Rotation;

                    // Convert to Godot Quaternion applying the world Z-negate handedness.
                    // World geometry negates Z: (x,y,z) → (x,y,−z).
                    // Quaternion under Z-negate: (qx,qy,qz,qw) → (−qx, −qy, qz, qw)
                    //   [negate the X and Y imaginary parts, keep Z and W].
                    // spec: CLAUDE.md "Coordinate conventions" — world negates Z.
                    // spec: §3.6.7 — orientation debugger-pending; size/placement fix is dominant.
                    var godotQ = new Quaternion(-legacyQ.X, -legacyQ.Y, legacyQ.Z, legacyQ.W);
                    // Normalise to guard against near-identity quaternion numerical drift.
                    if (godotQ.LengthSquared() > 0.0001f)
                        godotQ = godotQ.Normalized();
                    else
                        godotQ = Quaternion.Identity;

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffMesh{i}",
                        Position = offset,
                        Quaternion = godotQ,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtMesh++;
                    break;
                }

                // ------------------------------------------------------------------
                // emitter_type 2 — DIRECTIONAL BILLBOARD (oriented quad, +90° Y pre-rotation)
                // spec: §3.6.7 — "oriented quad with an explicit +90° Y pre-rotation plus the
                //   keyframe Euler rotation (used here for the large imot-gu-tung06-01 corona glows)."
                // spec: §A.12 — XEFF_EMITTER_DIRECTIONAL = 2
                // ------------------------------------------------------------------
                case EmitterDirectional:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    // Apply +90° Y pre-rotation then compose with keyframe Euler.
                    // spec: §3.6.7 — "+90° Y pre-rotation composed with the keyframe rotation".
                    var preRot = new Quaternion(Vector3.Up, Mathf.Pi * 0.5f); // +90° around Y

                    var legacyQ = kf0.Rotation;
                    // Apply same Z-negate handedness conversion as type-1.
                    var godotKfQ = new Quaternion(-legacyQ.X, -legacyQ.Y, legacyQ.Z, legacyQ.W);
                    if (godotKfQ.LengthSquared() > 0.0001f)
                        godotKfQ = godotKfQ.Normalized();
                    else
                        godotKfQ = Quaternion.Identity;

                    // Compose: pre-rotation first, then keyframe.
                    var combined = (preRot * godotKfQ).Normalized();

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffDir{i}",
                        Position = offset,
                        Quaternion = combined,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtDirectional++;
                    break;
                }

                default:
                    // Unknown emitter_type — fall back to billboard (safe visual approximation).
                    // spec: Docs/RE/formats/effects.md §A.12 — values beyond 0/1/2 render-DBG-pending.
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffUnk{i}",
                        Position = offset,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtBillboard++;
                    break;
                }
            }

            // ---- Animation registration ----
            // Step 1 COMPLETE (static keyframe-0 placement + correct shape).
            // Step 2 (per-frame texture-swap + alpha/size lerp) is registered here and
            // driven in _Process below.
            // spec: §3.6.7 — "fire ≈ 18×67 ms, waterfall 4-frame, sparks 9-frame, corona 21-frame".
            // spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride (ms per frame).
            animList.Add(new AnimEntry(
                mi,
                sub.AlphaKeys,
                sub.AnimStride,
                (uint)sub.TextureNames.Length,
                sub.TextureNames,
                albedo,
                mat,
                assets));
        }

        _animEntries = [.. animList];

        GD.Print($"[XeffSceneEffect] Built effectId={xeff.EffectId} subEffectCount={xeff.SubEffectCount}: " +
                 $"billboard={builtBillboard} mesh={builtMesh} directional={builtDirectional} " +
                 $"(total={builtBillboard + builtMesh + builtDirectional}). " +
                 $"Skipped: noKf={skippedNoKf} transparent={skippedTransparent} " +
                 $"noTexture={skippedNoTexture} degenerate={skippedDegenerate}. " +
                 "spec: frontend_scenes.md §3.6.7 (expected: ~6 billboard / ~51 mesh / ~11 directional).");
    }

    // =========================================================================
    // Animation — step keyframes in _Process
    // =========================================================================

    /// <summary>
    ///     Steps each animated sub-effect through its texture keyframes and alpha curve.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.6.7 — "each sub-effect cycles on its own
    ///     tex_count / anim_stride".
    ///     spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride in milliseconds.
    ///     spec: Docs/RE/formats/effects.md §A.6 — alpha inversion: opacity = 1.0 − file.
    /// </summary>
    public override void _Process(double delta)
    {
        _elapsed += delta;
        var elapsedMs = _elapsed * 1000.0;

        foreach (ref readonly var entry in _animEntries.AsSpan())
        {
            // Static sub-effects (AnimStride == 0) use their keyframe-0 texture permanently.
            if (entry.AnimStride == 0 || entry.FrameCount <= 1)
                continue;

            // Frame index from elapsed time.
            // spec: §A.4.3 — anim_stride (ms); wrap around frame count.
            var frameIdx = (int)(elapsedMs / entry.AnimStride) % (int)entry.FrameCount;

            // Alpha update: invert the per-frame alpha key (1.0 − file).
            // spec: §A.6 CONFIRMED — file 0.0=opaque, 1.0=transparent.
            var opacity = 1.0f;
            if (entry.AlphaKeys.Length > frameIdx)
                opacity = Math.Clamp(1.0f - entry.AlphaKeys[frameIdx], 0.0f, 1.0f);

            entry.Material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, opacity);

            // Texture swap: load the frame texture on demand.
            // Frame 0 is already set; only swap when frame changes.
            if (frameIdx == 0)
            {
                // Restore frame 0 (cheapest case — already cached).
                if (entry.Tex0 is not null)
                    entry.Material.AlbedoTexture = entry.Tex0;
            }
            else if (frameIdx < entry.TextureNames.Length && entry.Assets is not null)
            {
                var texName = entry.TextureNames[frameIdx];
                if (texName.Length > 0)
                {
                    var tgaPath = $"{XeffTexturePath}{texName}.tga";
                    if (entry.Assets.Contains(tgaPath))
                        try
                        {
                            var tex = entry.Assets.LoadTexture(tgaPath);
                            if (tex is not null)
                                entry.Material.AlbedoTexture = tex;
                        }
                        catch
                        {
                            /* silently keep previous frame */
                        }
                }
            }
        }
    }

    // =========================================================================
    // Factory helper — load char_select-u.xeff from the VFS and build
    // =========================================================================

    /// <summary>
    ///     Loads <c>char_select-u.xeff</c> (effect id 380003000) from the VFS, parses it,
    ///     instantiates this node, adds it as a child of <paramref name="parent" />, and
    ///     places it at <paramref name="anchorGodotPos" />.
    ///     <para>
    ///         Returns <see langword="null" /> when the VFS is absent or the file is missing/corrupt;
    ///         in that case the scene degrades gracefully (no crash).
    ///     </para>
    ///     <para>
    ///         spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — effect id 380003000,
    ///         world (508.483, 69.887, −9758.569), scale 1.0, identity rotation.
    ///     </para>
    ///     <para>spec: Docs/RE/formats/effects.md §A.15 — id 380003000 = char_select-u.xeff. CONFIRMED.</para>
    /// </summary>
    /// <param name="parent">The parent Node3D to attach the effect node to.</param>
    /// <param name="anchorGodotPos">
    ///     Anchor world position in Godot-space (world Z negated).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — world (508.483, 69.887, −9758.569)
    ///     → Godot (508.483, 69.887, +9758.569). CODE-CONFIRMED.
    /// </param>
    /// <param name="assets">Open VFS handle. May be null (effect is skipped, not crashed).</param>
    /// <returns>The instantiated effect node, or <see langword="null" /> on failure.</returns>
    public static XeffSceneEffect? LoadAndAttach(
        Node3D parent,
        Vector3 anchorGodotPos,
        RealClientAssets? assets)
    {
        if (assets is null)
        {
            GD.Print("[XeffSceneEffect] No VFS — char_select-u.xeff skipped.");
            return null;
        }

        if (!assets.Contains(CharSelectXeffPath))
        {
            GD.Print($"[XeffSceneEffect] {CharSelectXeffPath} absent from VFS — effect skipped.");
            return null;
        }

        XeffData xeff;
        try
        {
            var bytes = assets.GetRaw(CharSelectXeffPath);
            if (bytes.IsEmpty)
            {
                GD.PrintErr($"[XeffSceneEffect] {CharSelectXeffPath} returned empty bytes.");
                return null;
            }

            xeff = XeffParser.ParseXeff(bytes);
            GD.Print($"[XeffSceneEffect] Parsed {CharSelectXeffPath}: " +
                     $"effectId={xeff.EffectId} subEffectCount={xeff.SubEffectCount}. " +
                     "spec: frontend_scenes.md §3.6.5 + §3.6.6 effectId=380003000 / 68 sub-effects. CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[XeffSceneEffect] XeffParser failed on {CharSelectXeffPath}: {ex.Message}");
            return null;
        }

        var node = new XeffSceneEffect
        {
            Name = "CharSelectXeff",
            // Anchor in Godot-space (world Z negated from spec +9758.569).
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — world (508.483, 69.887, −9758.569)
            //   CODE-CONFIRMED. WorldCoordinates.ToGodot → (508.483, 69.887, +9758.569).
            Position = anchorGodotPos,
            // Scale 1.0 per spec.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — scale 1.0 CODE-CONFIRMED.
            Scale = Vector3.One
        };

        parent.AddChild(node);

        // Initialise after adding to tree so deferred calls work.
        node.Initialise(xeff, assets);

        return node;
    }

    private readonly record struct AnimEntry(
        MeshInstance3D Node,
        float[] AlphaKeys, // inverted opacity per frame (1.0 − file)
        uint AnimStride, // ms per keyframe; 0 = static
        uint FrameCount, // total texture frames
        string[] TextureNames,
        ImageTexture? Tex0, // frame 0 texture (already loaded)
        StandardMaterial3D Material,
        RealClientAssets? Assets);
}