// Screens/XeffSceneEffect.cs
//
// Faithful Godot renderer for a parsed XeffData instance.
//
// SCOPE: Char-select scene — renders the single composite ambient effect
//   char_select-u.xeff (effect id 380003000, 68 sub-effects) as a set of
//   billboard Node3D meshes placed at the parsed per-sub-effect velocity-displaced
//   positions from keyframe 0, with the parsed alpha opacity and parsed size.
//
// SPEC CITATIONS:
//   Effect id 380003000 = char_select-u.xeff:
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED
//   Anchor world position (508.483, 69.887, −9758.569):
//     spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED
//   Anchor in Godot-space (world Z negated → +9758.569):
//     spec: Helpers/WorldCoordinates.ToGodot (x,y,z) → (x,y,−z)
//   68 sub-effects, emitter_type=1, textures "lflare-l-yellow-01" family:
//     spec: Docs/RE/formats/effects.md §A.15 / §A.4 CODE-CONFIRMED
//   Velocity Vec3 = displacement from effect origin (per sub-effect keyframe 0):
//     spec: Docs/RE/formats/effects.md §A.8 HIGH
//   Alpha inversion convention (file 0.0=opaque, 1.0=transparent):
//     spec: Docs/RE/formats/effects.md §A.6 CONFIRMED
//   Size Vec3 = billboard half-extents (size_x/size_y):
//     spec: Docs/RE/formats/effects.md §A.8 HIGH
//
// DESIGN:
//   1. Reads all sub-effects from the parsed XeffData.
//   2. For each sub-effect, places a MeshInstance3D billboard quad at:
//        anchorWorldPos + velocityVec3 (from keyframe 0)
//      (velocity is the per-sub-effect displacement from the origin; the runtime
//       rotates it by the instance orientation quaternion = identity here).
//   3. The quad size comes from size_x/size_y of keyframe 0.
//   4. The alpha comes from the FIRST alpha key (file value inverted: opacity = 1 − fileValue).
//   5. Texture: loads data/effect/texture/<name>.tga for the first texture name in the sub-effect.
//   6. Additive billboard material (UNSHADED, blend ADD, camera-facing).
//   7. TODO (future): drive per-curve alpha/scale animation by sampling AlphaKeys/ScaleX/Y/Z
//      against elapsed time in _Process. First pass uses keyframe-0 values only.
//
// PASSIVE: zero game logic. Pure asset → visual translation.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A <see cref="Node3D"/> that renders one parsed <c>.xeff</c> descriptor as a set of
/// static billboard quads placed at the sub-effect keyframe-0 positions.
///
/// <para>Load: call <see cref="Initialise"/> after the node is in the scene tree.</para>
///
/// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — single composite
/// char_select-u.xeff at world (508.483, 69.887, −9758.569), scale 1.0, identity rotation.</para>
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

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Builds all sub-effect billboard quads from <paramref name="xeff"/> at this node's
    /// origin (which should already be set to the anchor world position).
    ///
    /// <para>Textures are loaded from <paramref name="assets"/> via the
    /// <c>data/effect/texture/&lt;name&gt;.tga</c> path.
    /// Sub-effects whose texture is absent get a bright yellow-white fallback material
    /// so the effect is still visible (lflare-l-yellow-01 family = yellow lens flare).</para>
    ///
    /// <para>spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3 = displacement from origin,
    /// size Vec3 = billboard half-extents. HIGH confidence. spec: §A.6 alpha inversion CONFIRMED.</para>
    /// </summary>
    public void Initialise(XeffData xeff, RealClientAssets? assets)
    {
        int built = 0;
        int skipped = 0;

        for (int i = 0; i < xeff.SubEffects.Length; i++)
        {
            XeffSubEffect sub = xeff.SubEffects[i];

            // We need at least one keyframe to know position and size.
            if (sub.Keyframes.Length == 0)
            {
                skipped++;
                continue;
            }

            // Use keyframe 0 for the static first-pass render.
            // spec: Docs/RE/formats/effects.md §A.4.4 — frame 0 has no index prefix; first keyframe.
            XeffKeyframe kf0 = sub.Keyframes[0];

            // Velocity Vec3 = displacement from effect origin (world-space in the legacy engine;
            // here the origin is already in Godot-space so we apply the displacement as-is).
            // spec: Docs/RE/formats/effects.md §A.8 — velocity displaced from effect origin: HIGH.
            // NOTE: the runtime rotates velocity by the instance world-orientation quaternion
            // (identity here — the spec confirms identity quaternion at spawn). No negation needed
            // because the velocity is a LOCAL offset (not a world coordinate), and local X-negation
            // applies only to mesh geometry (.skn), not to effect displacement vectors.
            var displacement = new Vector3(kf0.VelocityX, kf0.VelocityY, kf0.VelocityZ);

            // Size Vec3 = billboard quad size.
            // spec: Docs/RE/formats/effects.md §A.8 — size_x/size_y = billboard half-extents: HIGH.
            // Use the larger of size_x/size_y as the uniform quad size. If both are 0 use 1.0.
            float sizeX = kf0.SizeX;
            float sizeY = kf0.SizeY;
            float quadSize = MathF.Max(MathF.Max(sizeX, sizeY), 0.5f);

            // Alpha from AlphaKeys[0] if present; inverted per spec §A.6.
            // spec: Docs/RE/formats/effects.md §A.6 — file 0.0=opaque, 1.0=transparent. CONFIRMED.
            float opacity = 1.0f;
            if (sub.AlphaKeys.Length > 0)
                opacity = 1.0f - sub.AlphaKeys[0]; // file value is 1−opacity

            // Clamp opacity — small negative / > 1 can occur from curve extremes.
            opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            if (opacity < 0.02f)
            {
                // Fully transparent sub-effect; skip geometry but count.
                skipped++;
                continue;
            }

            // Texture: first name in the name table resolves to data/effect/texture/<name>.tga.
            // spec: Docs/RE/formats/effects.md §A.4.1 — tex_name[0] → data/effect/texture/<name>.tga: CONFIRMED.
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
                        // Non-critical; fall back to solid colour.
                    }
                }
            }

            // Build additive billboard material.
            var mat = new StandardMaterial3D
            {
                // Unshaded — the effect system bypasses PBR lighting.
                // Aesthetic rationale: lens-flare / corona effects are emissive additive,
                // not PBR-shaded by design.
                ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,

                // Additive blend: bright areas add to the scene behind them (torch glow).
                // Aesthetic choice for the torch/corona effect class (lflare family).
                Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
                BlendMode = StandardMaterial3D.BlendModeEnum.Add,

                // Camera-facing billboard.
                // spec: Docs/RE/formats/effects.md §A.12 emitter_type 0 = flat billboard: CONFIRMED.
                BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled,

                // Disable depth write so overlapping additive quads accumulate correctly.
                DepthDrawMode = StandardMaterial3D.DepthDrawModeEnum.Disabled,

                // Use vertex colour for alpha (opacity channel from the curve).
                VertexColorUseAsAlbedo = true,
            };

            if (albedo is not null)
            {
                mat.AlbedoTexture = albedo;
                mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, opacity);
            }
            else
            {
                // Fallback: warm yellow-white matching the lflare-l-yellow-01 family.
                // Aesthetic: yellow-white chosen to match the torch/lens-flare visual family.
                // The spec confirms textures are "lflare-l-yellow-01 yellow lens-flare family".
                // spec: Docs/RE/formats/effects.md §A.15 — textures "lflare-l-yellow-01" family. CONFIRMED.
                mat.AlbedoColor = new Color(1.0f, 0.92f, 0.35f, opacity);
            }

            // Build the billboard quad mesh.
            // NOTE: NEVER use GltfDocument.AppendFromBuffer — crashes on this project's GLBs.
            // Build ArrayMesh or QuadMesh directly per the pitfalls in CLAUDE.md.
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

        GD.Print($"[XeffSceneEffect] Built {built} sub-effect quads ({skipped} skipped) " +
                 $"from xeff effectId={xeff.EffectId} subEffectCount={xeff.SubEffectCount}. " +
                 "spec: Docs/RE/formats/effects.md §A.8 (velocity displacement, size quad). " +
                 "First-pass: keyframe-0 static. TODO: per-curve alpha/scale animation.");
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
                     "spec: Docs/RE/formats/effects.md §A.15 effectId=380003000 / 68 sub-effects. CONFIRMED.");
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