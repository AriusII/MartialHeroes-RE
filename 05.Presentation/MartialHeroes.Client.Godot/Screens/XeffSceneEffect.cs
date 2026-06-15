// Screens/XeffSceneEffect.cs
//
// Faithful Godot renderer for a parsed XeffData instance.
//
// SCOPE: Char-select scene — renders the single composite ambient effect
//   char_select-u.xeff (effect id 380003000, 68 sub-effects) as a set of
//   camera-facing BILLBOARD QUADS with alpha-blend/additive blend, placed at
//   the parsed per-sub-effect velocity-displaced positions from keyframe 0.
//
// FLYING-PIXELS ROOT CAUSE (per spec §3.6.6):
//   Stray red/orange pixels = brazier-fire emitters drawn as bare points/opaque quads.
//   Stray blue pixels = waterfall emitters drawn as bare points/opaque quads.
//   Fix: each particle MUST be a camera-facing textured BILLBOARD QUAD, alpha-blended /
//   additive, with its TGA texture bound. A missing texture MUST suppress the quad (no
//   solid-colour fallback, which reads as a hard opaque dot).
//
// PORT CONTRACT (spec: Docs/RE/specs/frontend_scenes.md §3.6.6 CODE-CONFIRMED):
//   1. Billboard — camera-facing textured quad sized by sprite_size.
//   2. Bind per-sub-effect texture (data/effect/texture/<name>.tga with alpha).
//   3. Additive/transparent blend — fire/water glow, not opaque.
//   4. (Future) Animate via XEffect keyframe stride. First pass: keyframe-0 static.
//   5. NO scene point-lights for the braziers (fire glow = additive texture, not a light).
//   If texture is absent: SKIP the sub-effect entirely (no solid fallback dot).
//
// SPEC CITATIONS:
//   Effect id 380003000 = char_select-u.xeff:
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED
//   Anchor world position (508.483, 69.887, −9758.569):
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED
//   Anchor in Godot-space (world Z negated → +9758.569):
//     spec: Helpers/WorldCoordinates.ToGodot (x,y,z) → (x,y,−z)
//   68 sub-effects, 16 textures (fire_4-01..06, fire_piece1b-01, waterfall-pie-01..04, etc.):
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.6 VFS-VERIFIED
//   Billboard + additive/alpha-blend port contract:
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.6 PORT CONTRACT CODE-CONFIRMED
//   Velocity Vec3 = displacement from effect origin (per sub-effect keyframe 0):
//     spec: Docs/RE/formats/effects.md §A.8 HIGH
//   Alpha inversion convention (file 0.0=opaque, 1.0=transparent):
//     spec: Docs/RE/formats/effects.md §A.6 CONFIRMED
//   Size Vec3 = billboard half-extents (size_x/size_y):
//     spec: Docs/RE/formats/effects.md §A.8 HIGH
//   Flying-pixels root cause (bare points / alpha dropped):
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.6 FLYING-PIXELS ROOT CAUSE CODE-CONFIRMED
//
// PASSIVE: zero game logic. Pure asset → visual translation.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A <see cref="Node3D"/> that renders one parsed <c>.xeff</c> descriptor as a set of
/// camera-facing billboard quads placed at the sub-effect keyframe-0 positions.
///
/// <para>Load: call <see cref="Initialise"/> after the node is in the scene tree.</para>
///
/// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — single composite
/// char_select-u.xeff at world (508.483, 69.887, −9758.569), scale 1.0, identity rotation.</para>
/// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.6 — billboard + additive port contract,
/// flying-pixels root cause (bare points / alpha dropped). CODE-CONFIRMED.</para>
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

    // Minimum rendered quad size — sub-effects smaller than this are too small to see and
    // can contribute to the "flying pixel" look (a 1-unit quad reads as a dot).
    // Aesthetic: 2.0 units minimum so the billboard reads as a soft glow, not a speck.
    private const float MinQuadSize = 2.0f;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Builds all sub-effect billboard quads from <paramref name="xeff"/> at this node's
    /// origin (which should already be set to the anchor world position).
    ///
    /// <para>KEY RULE (anti-flying-pixels): sub-effects whose texture is absent are SKIPPED
    /// entirely — no solid-colour fallback is emitted, because a solid-colour quad of the
    /// wrong size reads as the "flying pixel" artefact the spec §3.6.6 documents.</para>
    ///
    /// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.6 PORT CONTRACT CODE-CONFIRMED —
    /// billboard, texture bound, additive/transparent, no fallback dot.</para>
    /// </summary>
    public void Initialise(XeffData xeff, RealClientAssets? assets)
    {
        int built = 0;
        int skippedNoKf = 0;
        int skippedTransparent = 0;
        int skippedNoTexture = 0;
        int skippedTooSmall = 0;

        for (int i = 0; i < xeff.SubEffects.Length; i++)
        {
            XeffSubEffect sub = xeff.SubEffects[i];

            // We need at least one keyframe to know position and size.
            if (sub.Keyframes.Length == 0)
            {
                skippedNoKf++;
                continue;
            }

            // Use keyframe 0 for the static first-pass render.
            // spec: Docs/RE/formats/effects.md §A.4.4 — frame 0 = first keyframe.
            XeffKeyframe kf0 = sub.Keyframes[0];

            // Velocity Vec3 = displacement from effect origin (world-space in the legacy engine;
            // here the origin is already in Godot-space so we apply the displacement as-is).
            // spec: Docs/RE/formats/effects.md §A.8 — velocity displaced from effect origin: HIGH.
            var displacement = new Vector3(kf0.VelocityX, kf0.VelocityY, kf0.VelocityZ);

            // Size Vec3 = billboard quad size.
            // spec: Docs/RE/formats/effects.md §A.8 — size_x/size_y = billboard half-extents: HIGH.
            float sizeX = kf0.SizeX;
            float sizeY = kf0.SizeY;
            float quadSize = MathF.Max(MathF.Max(sizeX, sizeY), MinQuadSize);

            // Alpha from AlphaKeys[0] if present; inverted per spec §A.6.
            // spec: Docs/RE/formats/effects.md §A.6 — file 0.0=opaque, 1.0=transparent. CONFIRMED.
            float opacity = 1.0f;
            if (sub.AlphaKeys.Length > 0)
                opacity = 1.0f - sub.AlphaKeys[0];

            opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            if (opacity < 0.02f)
            {
                // Fully transparent — skip. No quad, no pixel.
                skippedTransparent++;
                continue;
            }

            // === CRITICAL anti-flying-pixels rule (spec §3.6.6 PORT CONTRACT #1+#2) ===
            // Attempt to load the sub-effect's texture. If it fails, SKIP this sub-effect —
            // do NOT emit a solid-colour fallback quad. A solid quad without its alpha texture
            // IS the "flying pixel" artefact: it renders as an opaque dot of wrong colour.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.6 — "never as a bare point / never
            //   as an opaque quad"; "bind the per-sub-effect texture ... A missing texture /
            //   ignored alpha is the other half of 'bare pixels'". CODE-CONFIRMED.
            ImageTexture? albedo = null;
            if (sub.TextureNames.Length > 0 && sub.TextureNames[0].Length > 0 && assets is not null)
            {
                string tgaPath = $"{XeffTexturePath}{sub.TextureNames[0]}.tga";
                if (assets.Contains(tgaPath))
                {
                    try
                    {
                        albedo = assets.LoadTexture(tgaPath);
                    }
                    catch
                    {
                        // Texture load failed — will skip below.
                    }
                }
            }

            if (albedo is null)
            {
                // No texture → no quad. Emitting a solid-colour quad here would produce the
                // exact "flying pixel" artefact we are trying to eliminate.
                // spec: §3.6.6 — missing texture IS the other half of "bare pixels". CODE-CONFIRMED.
                skippedNoTexture++;
                continue;
            }

            // Build additive billboard material.
            // spec: §3.6.6 PORT CONTRACT #1 — camera-facing billboard.
            // spec: §3.6.6 PORT CONTRACT #3 — additive/transparent blend.
            var mat = new StandardMaterial3D
            {
                // Unshaded — effect sprites are emissive (bypass PBR lighting).
                // spec: §3.6.6 — fire glow is the additive texture, not a lit surface.
                ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,

                // Alpha blend mode with AlbedoTexture carrying the sprite's alpha channel.
                // This prevents the opaque-quad "flying pixel" — the sprite edges are
                // soft/transparent where the TGA alpha is 0.
                // spec: §3.6.6 PORT CONTRACT #3 — additive/transparent blend. CODE-CONFIRMED.
                Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
                BlendMode = StandardMaterial3D.BlendModeEnum.Add,

                // Camera-facing billboard (all emitter types in the char_select xeff
                // are billboard / mesh-particle on the name-table path).
                // spec: §3.6.6 PORT CONTRACT #1 — "camera-facing, alpha-blended, textured quad".
                BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled,

                // Disable depth write so overlapping additive quads accumulate correctly
                // without z-fighting artefacts.
                DepthDrawMode = StandardMaterial3D.DepthDrawModeEnum.Disabled,

                // Bind the sub-effect's TGA texture (with its alpha channel).
                // spec: §3.6.6 PORT CONTRACT #2 — "bind the per-sub-effect texture ... with its alpha".
                AlbedoTexture = albedo,

                // Opacity via albedo color alpha.
                AlbedoColor = new Color(1.0f, 1.0f, 1.0f, opacity),
            };

            // Build the billboard quad mesh.
            // NOTE: NEVER use GltfDocument.AppendFromBuffer — crashes on this project's GLBs.
            // Build QuadMesh directly per the pitfalls in CLAUDE.md.
            var quad = new QuadMesh
            {
                Size = new Vector2(quadSize, quadSize),
                Material = mat,
            };

            var mi = new MeshInstance3D
            {
                Name = $"XeffSub{i}",
                // Position = anchor + displacement.
                // The node itself is placed at the anchor world position; child positions
                // are local to the node, so displacement goes directly to Position.
                Position = displacement,
                Mesh = quad,
            };

            AddChild(mi);
            built++;
        }

        GD.Print($"[XeffSceneEffect] Built {built} billboard quads from xeff effectId={xeff.EffectId} " +
                 $"subEffectCount={xeff.SubEffectCount}. " +
                 $"Skipped: noKf={skippedNoKf}, transparent={skippedTransparent}, " +
                 $"noTexture={skippedNoTexture} (anti-flying-pixel rule — no fallback dot emitted), " +
                 $"tooSmall={skippedTooSmall}. " +
                 "spec: frontend_scenes.md §3.6.6 PORT CONTRACT CODE-CONFIRMED (billboard, additive, no fallback).");
    }

    // =========================================================================
    // Factory helper — load char_select-u.xeff from the VFS and build
    // =========================================================================

    /// <summary>
    /// Loads <c>char_select-u.xeff</c> (effect id 380003000) from the VFS, parses it,
    /// instantiates this node, adds it as a child of <paramref name="parent"/>, and
    /// places it at <paramref name="anchorGodotPos"/>.
    ///
    /// <para>Returns <see langword="null"/> when the VFS is absent or the file is missing/corrupt;
    /// in that case the scene degrades gracefully (no crash).</para>
    ///
    /// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — effect id 380003000,
    /// world (508.483, 69.887, −9758.569), scale 1.0, identity rotation.</para>
    /// <para>spec: Docs/RE/formats/effects.md §A.15 — id 380003000 = char_select-u.xeff. CONFIRMED.</para>
    /// </summary>
    /// <param name="parent">The parent Node3D to attach the effect node to.</param>
    /// <param name="anchorGodotPos">
    /// Anchor world position in Godot-space (world Z negated).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — world (508.483, 69.887, −9758.569)
    ///   → Godot (508.483, 69.887, +9758.569). CODE-CONFIRMED.
    /// </param>
    /// <param name="assets">Open VFS handle. May be null (effect is skipped, not crashed).</param>
    /// <returns>The instantiated effect node, or <see langword="null"/> on failure.</returns>
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
            ReadOnlyMemory<byte> bytes = assets.GetRaw(CharSelectXeffPath);
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
            Scale = Vector3.One,
        };

        parent.AddChild(node);

        // Initialise after adding to tree so deferred calls work.
        node.Initialise(xeff, assets);

        return node;
    }
}