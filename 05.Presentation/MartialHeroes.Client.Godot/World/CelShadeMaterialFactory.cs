// World/CelShadeMaterialFactory.cs
//
// Builds the ShaderMaterial for the cel/toon character pipeline and wires the toon ramp LUT.
//
// SCOPE: skinned characters ONLY.
// spec: Docs/RE/specs/rendering.md §5.2 — "dotoonshading" path is the SKINNED-CHARACTER pipeline.
//   Terrain, buildings, and static world objects draw through the vertex-color-lit opaque bucket
//   (§3.2) and do NOT use this cel material.
//
// TOON RAMP:
//   Primary: loaded from VFS path "data/shader/toonramp.bmp" via RealClientAssets.LoadTexture.
//   spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp, 1-D N·L ramp (~256×1 24 bpp,
//     MEDIUM confidence for exact pixel dimensions), bound on texture stage 1.
//   Fallback: when the VFS is absent or the file is not found, the shader's built-in procedural
//     3-band ramp is used (aesthetic fallback, not spec-dictated).
//
// LIGHTS:
//   The two-light setup mirrors dotoonshading.vsh's two Lambert passes.
//   spec: Docs/RE/formats/shaders.md §C5.4:
//     c4  = TOON light direction; default [-1, 0, 0, 0] (−X axis, HIGH confidence recovered).
//           DISTINCT from the scene DirectionalLight3D — this is the N·L source for the ramp.
//     c5  = [0, 0, -1, 0] (axis / view-Z — used as second fill-light direction here).
//     c6  = [1, 1, 1, 1] (white / material-ambient — main toon light colour).
//     c9  = [0.299, 0.587, 0.114, 1.0] (BT.601 luma — encoded in the shader, HIGH confidence).
//     c10 = [1, 1, 1, 1] (white — applied as the albedo modulate).
//   spec: Docs/RE/formats/shaders.md §C5.4 — c4 default [-1,0,0,0]: HIGH confidence.
//
// spec: Docs/RE/formats/shaders.md §C5 — Campaign 5 Runtime Cel/Glow Shader Set.
// spec: Docs/RE/specs/rendering.md §5.2 — cel skinned-character vertex declaration (stride 32).

using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Static factory that constructs and configures the <see cref="ShaderMaterial" /> for the
///     cel/toon skinned-character pipeline.
///     Call <see cref="Build" /> once per character (pass the resolved albedo texture and the shared
///     pre-loaded ramp). The returned material is set on the character's <c>MeshInstance3D</c> surface.
///     Session-level ramp: call <see cref="InitSession" /> once at world startup (from RealWorldRenderer
///     or GameLoop) to load the toon ramp LUT from the VFS. All subsequent <see cref="Build" /> calls
///     automatically use the cached ramp without needing to pass it explicitly.
/// </summary>
public static class CelShadeMaterialFactory
{
    // The VFS path for the toon ramp LUT.
    // spec: Docs/RE/formats/shaders.md §C5.3 — "data/shader/toonramp.bmp".
    private const string ToonRampVfsPath = "data/shader/toonramp.bmp";

    // Session-level ramp texture. Loaded once in InitSession; shared across all Build calls.
    // Null = procedural fallback in the shader.
    // spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp, 1-D N·L ramp, stage 1.
    private static ImageTexture? _sessionRamp;

    // Toon light direction in Godot world space.
    // spec: Docs/RE/formats/shaders.md §C5.4 — c4 default = [-1, 0, 0, 0]: toon light down the −X axis.
    // This is DISTINCT from the scene DirectionalLight3D (which controls sun shadow casting).
    // The toon light is the N·L source for the BT.601 ramp; its default is −X (confirmed HIGH confidence).
    // The live value is editable at runtime (renderer slot); the default is recovered as −X.
    private static readonly Vector3
        DefaultLightDir = new(-1f, 0f, 0f); // spec: Docs/RE/formats/shaders.md §C5.4 c4 = [-1,0,0,0]

    // Second fill-light direction corresponds to c5 = [0,0,-1,0] (axis/view-Z), negated for fill.
    // Using a soft upward fill so shadowed underbelts read.
    // spec: Docs/RE/formats/shaders.md §C5.4 c5 = [0, 0, -1, 0].
    // Aesthetic choice for the second-light intensity and direction as fill.
    private static readonly Vector3 DefaultLight2Dir = new Vector3(0f, 1f, 0f).Normalized();

    /// <summary>
    ///     Whether cel-shading is enabled for skinned characters.
    ///     Default true. Set to false to revert to the previous StandardMaterial3D path.
    ///     Aesthetic/engineering flag — not spec-dictated.
    /// </summary>
    public static bool CelEnabled { get; set; } = true;

    /// <summary>
    ///     Ambient floor energy, fed from <c>EnvironmentNode.OptionBrightFloor</c>.
    ///     Reproduces the D3DRS_AMBIENT full-white additive constant the original DX9 device applies at
    ///     OPTION_BRIGHT = 100 (device_ambient = full white = 1.0 in float). Because CelShade.gdshader
    ///     runs <c>render_mode unshaded</c> it does NOT pick up WorldEnvironment ambient; this value is
    ///     injected as a shader uniform so shadowed surfaces are lifted instead of going black.
    ///     Default 1.0 — the spec-confirmed default OPTION_BRIGHT = 100 → full-white ambient floor.
    ///     spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default 100 → device_ambient white.
    ///     spec: Docs/RE/specs/environment.md §6.2b — "ambient_light_energy = OPTION_BRIGHT/100 = 1.0."
    /// </summary>
    public static float AmbientFloorEnergy { get; set; } =
        1.0f; // spec: Docs/RE/specs/environment.md §6.2a — default OPTION_BRIGHT=100 → 1.0

    /// <summary>
    ///     Initialise the session: loads the toon ramp LUT from the VFS and caches it.
    ///     Call once at world startup before any Build call. Idempotent — safe to call again.
    ///     spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp.
    /// </summary>
    public static void InitSession(RealClientAssets? assets)
    {
        _sessionRamp = LoadToonRamp(assets);
    }

    /// <summary>
    ///     Loads the toon ramp LUT from the VFS. Returns null if the VFS is absent or the file is
    ///     not found — the shader falls back to its procedural 3-band ramp.
    ///     Call this ONCE per session and share the result across all character materials.
    ///     spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp, 1-D N·L ramp, stage 1.
    /// </summary>
    public static ImageTexture? LoadToonRamp(RealClientAssets? assets)
    {
        if (assets is null) return null;
        var tex = assets.LoadTexture(ToonRampVfsPath);
        if (tex is null)
            GD.Print("[CelShade] toonramp.bmp not found in VFS — shader will use procedural fallback ramp.");
        else
            GD.Print($"[CelShade] toonramp.bmp loaded: {tex.GetWidth()}×{tex.GetHeight()}.");
        return tex;
    }

    /// <summary>
    ///     Builds a <see cref="ShaderMaterial" /> for a single skinned character using the session ramp.
    ///     Parameters:
    ///     <paramref name="albedo" /> — the character's skin texture (from skin.txt chain); null = warm fallback.
    ///     SCOPE: returned material is intended only for a skinned character's MeshInstance3D surface.
    ///     spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
    /// </summary>
    public static ShaderMaterial Build(ImageTexture? albedo)
    {
        return Build(albedo, _sessionRamp);
    }

    /// <summary>
    ///     Builds a <see cref="ShaderMaterial" /> for a single skinned character.
    ///     Parameters:
    ///     <paramref name="albedo" /> — the character's skin texture (from skin.txt chain); null = warm fallback.
    ///     <paramref name="toonRamp" /> — the shared preloaded ramp from <see cref="LoadToonRamp" />;
    ///     null = procedural fallback.
    ///     SCOPE: returned material is intended only for a skinned character's MeshInstance3D surface.
    ///     spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
    /// </summary>
    public static ShaderMaterial Build(ImageTexture? albedo, ImageTexture? toonRamp)
    {
        // Load the CelShade.gdshader from the Godot resource path.
        // The .gdshader is part of the project (res://World/CelShade.gdshader).
        var shader = GD.Load<Shader>("res://World/CelShade.gdshader");

        var mat = new ShaderMaterial { Shader = shader };

        // ---- Albedo ----
        if (albedo is not null)
        {
            mat.SetShaderParameter("albedo_texture", albedo);
            mat.SetShaderParameter("use_albedo_texture", true);
            mat.SetShaderParameter("albedo_color", new Color(1f, 1f, 1f));
        }
        else
        {
            // Warm skin-tone fallback — aesthetic, not spec-dictated.
            mat.SetShaderParameter("use_albedo_texture", false);
            mat.SetShaderParameter("albedo_color", new Color(0.85f, 0.75f, 0.65f));
        }

        // ---- Toon ramp LUT ----
        // spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp 1-D N·L ramp, stage 1.
        if (toonRamp is not null)
        {
            mat.SetShaderParameter("toon_ramp", toonRamp);
            mat.SetShaderParameter("use_toon_ramp", true);
        }
        else
        {
            mat.SetShaderParameter("use_toon_ramp", false);
        }

        // ---- Post-process gate (cel_enabled) ----
        // When CelEnabled is false the post-process offscreen path is off; the shader falls back to
        // plain diffuse (no toon ramp). Wire the factory flag through to the shader uniform.
        // spec: Docs/RE/specs/rendering.md §5.1a — cel/dotoonshading coupled to the post-process flag.
        mat.SetShaderParameter("cel_enabled", CelEnabled); // spec: rendering.md §5.1a

        // ---- Ambient floor ----
        // Replicates the D3DRS_AMBIENT full-white additive constant at OPTION_BRIGHT = 100.
        // CelShade.gdshader runs render_mode unshaded (no WorldEnvironment ambient pickup); this
        // explicit uniform lifts dark surfaces so the cel scene reads at the same overall brightness
        // as the original DX9 device at default brightness.
        // spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default 100 → device_ambient white.
        // spec: Docs/RE/specs/environment.md §6.2b — ambient_floor = OPTION_BRIGHT/100 = 1.0.
        mat.SetShaderParameter("ambient_floor_energy", AmbientFloorEnergy); // spec: environment.md §6.2a
        mat.SetShaderParameter("ambient_floor_color",
            new Color(1f, 1f, 1f)); // achromatic white; spec: environment.md §6.2a

        // ---- Light directions and colours ----
        // Main light: c4 runtime direction, c6 = white.
        // spec: Docs/RE/formats/shaders.md §C5.4 c4/c6.
        mat.SetShaderParameter("light_dir", DefaultLightDir);
        mat.SetShaderParameter("light_color", new Color(1f, 1f, 1f)); // c6 = [1,1,1,1]

        // Fill light: derived from c5 = [0,0,-1,0] axis; soft warm-blue fill (aesthetic).
        // spec: Docs/RE/formats/shaders.md §C5.4 c5 = [0, 0, -1, 0].
        mat.SetShaderParameter("light2_dir", DefaultLight2Dir);
        mat.SetShaderParameter("light2_color", new Color(0.5f, 0.5f, 0.6f));

        return mat;
    }
}