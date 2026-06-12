// World/EnvironmentNode.cs
//
// Passive environment node that drives a Godot WorldEnvironment + DirectionalLight3D from the
// per-area sky data files found in data/sky/dat/ of the legacy client VFS.
//
// Sky data files (spec: Mission D brief — confirmed AREA-keyed files):
//   data/sky/dat/fog{area}.txt        — fog start/end ratios + 48 half-hour fog RGB keyframes
//   data/sky/dat/light{area}.txt      — sun direction, character light direction, 48 keyframes of
//                                        ambient/diffuse/specular RGB + StarDome brightness
//   data/sky/dat/clouddome{area}.txt  — sky-dome vertex colour gradient (used as sky tint)
//
// All files are CP949 TAB-separated plain text. This node parses them inline — no new parser class.
//
// PASSIVE: zero game-rule authority. This node only translates sky data into Godot visuals.
//
// Threading: all Godot node mutation happens on the Godot main thread (called from
// RealWorldRenderer.Initialise which runs on the main thread via GameLoop._Ready).
//
// WorldEnvironment strategy:
//   World.tscn already contains a "WorldEnvironment" child of the scene root with a static
//   Environment sub-resource. EnvironmentNode FINDS that existing WorldEnvironment node by
//   name (walking up to the scene root) and REPLACES its .Environment property with a newly
//   constructed global::Godot.Environment built from the sky data. This is the correct approach because:
//     (a) Godot 4 allows only ONE active WorldEnvironment; two in the tree compete unpredictably.
//     (b) Replacing the .Environment property leaves the node wired (camera viewport etc.) intact.
//     (c) If the WorldEnvironment node is absent (scene modified), we add our own child instead.
//   The existing static DirectionalLight3D in the scene is found the same way and updated in-place.
//   If absent, a new DirectionalLight3D is added as a child of EnvironmentNode.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: WorldCoordinates.ToGodot — (x,y,z) -> (x,y,-z) for direction conversion.
// spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition (AreaTag helper).

using Godot;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive rendering node that reads per-area sky files from the VFS and configures the scene's
/// <see cref="WorldEnvironment"/> and <see cref="DirectionalLight3D"/> accordingly.
///
/// Call <see cref="Configure"/> once per area load (after <see cref="RealWorldRenderer.ResolveTargetCell"/>
/// has resolved <c>TargetAreaId</c>). All subsequent visual overrides go through
/// <see cref="SetTimeOfDay"/>.
///
/// Default time-of-day: keyframe index 24 (noon, 12:00 local sun-time, half of 48 half-hour steps).
///
/// If any sky file is absent, the node leaves a sensible Godot default environment in place and
/// logs a diagnostic. It never throws.
/// </summary>
public sealed partial class EnvironmentNode : Node3D
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// The camera far-plane distance assumed for fog depth scaling.
    /// Matches the Camera3D far value used by CameraController.
    /// spec: mission brief — "camFar~8000".
    /// </summary>
    private const float CamFar = 8000f;

    /// <summary>
    /// Number of half-hour keyframes per day in the sky data files.
    /// spec: mission brief — "48 half-hour keyframes".
    /// </summary>
    private const int KeyframeCount = 48;

    /// <summary>
    /// Default time-of-day keyframe index (noon = index 24 of 0..47).
    /// spec: mission brief — "use the noon row, index ~24 of 48".
    /// </summary>
    private const int NoonKeyframeIndex = 24;

    // -------------------------------------------------------------------------
    // Parsed sky data (populated by Configure, consumed by ApplyTimeOfDay)
    // -------------------------------------------------------------------------

    private FogData? _fog;
    private LightData[]? _lightKeyframes;  // [KeyframeCount]
    private Color _skyTint;                // from clouddome noon row

    // Godot nodes we drive (resolved in Configure)
    private WorldEnvironment? _worldEnv;
    private DirectionalLight3D? _dirLight;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the sky files for the given area from the VFS and applies the noon time-of-day.
    ///
    /// Must be called on the Godot main thread. Safe to call multiple times (re-configures for
    /// a new area). If <paramref name="assets"/> is null or files are absent, the node leaves
    /// the scene's existing environment/light as-is and logs a diagnostic.
    ///
    /// Recommended call site in RealWorldRenderer.Initialise:
    /// <code>
    ///   var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
    ///   AddChild(envNode);
    ///   envNode.Configure(_assets, TargetAreaId);
    /// </code>
    /// Called AFTER ResolveTargetCell() sets TargetAreaId.
    ///
    /// spec: data/sky/dat/fog{area}.txt, light{area}.txt, clouddome{area}.txt.
    /// </summary>
    /// <param name="assets">The VFS reader (may be null — defensive).</param>
    /// <param name="areaId">
    /// The area identifier (same as RealWorldRenderer.TargetAreaId).
    /// spec: Docs/RE/formats/terrain.md §1.1 — area id 0..N.
    /// </param>
    public void Configure(RealClientAssets? assets, int areaId)
    {
        GD.Print($"[EnvironmentNode] Configure area={areaId}");

        // Resolve (or create) the WorldEnvironment and DirectionalLight3D targets.
        ResolveOrCreateSceneNodes();

        if (assets is null)
        {
            GD.Print("[EnvironmentNode] No VFS — leaving default environment.");
            return;
        }

        // Parse fog data.
        _fog = TryParseFog(assets, areaId);

        // Parse per-keyframe light data.
        _lightKeyframes = TryParseLightKeyframes(assets, areaId);

        // Parse sky tint from clouddome (noon row).
        _skyTint = TryParseClouddomeTint(assets, areaId, NoonKeyframeIndex);

        // Apply noon as the default time-of-day.
        ApplyTimeOfDay(NoonKeyframeIndex);

        GD.Print($"[EnvironmentNode] Configured. Fog={_fog is not null}, " +
                 $"LightKeyframes={_lightKeyframes?.Length ?? 0}, SkyTint={_skyTint}");
    }

    /// <summary>
    /// Overrides the displayed time of day. Keyframe 0 = midnight, 24 = noon, 47 = last half-hour.
    /// Clamps to [0, KeyframeCount-1]. No-op if Configure has not been called successfully.
    /// </summary>
    /// <param name="keyframeIndex">Half-hour keyframe index (0..47).</param>
    public void SetTimeOfDay(int keyframeIndex)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        ApplyTimeOfDay(keyframeIndex);
    }

    // -------------------------------------------------------------------------
    // Core application logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies a single keyframe to the WorldEnvironment and DirectionalLight3D.
    /// Called from <see cref="Configure"/> (noon) and <see cref="SetTimeOfDay"/>.
    /// Main-thread only.
    /// </summary>
    private void ApplyTimeOfDay(int ki)
    {
        var env = new global::Godot.Environment();

        // --- Background / sky tint ---
        // Use a flat colour background tinted by the clouddome noon colour.
        // spec: mission brief — "BackgroundMode=Color or Sky tinted by the clouddome/noon colour".
        // We choose Color mode because we have no procedural sky driven by the real data;
        // the clouddome is the closest equivalent flat tint.
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = _skyTint != default ? _skyTint : new Color(0.47f, 0.47f, 0.47f, 1f);
        // spec: mission brief — daytime clouddome ~(120,120,120)/255 ≈ (0.47, 0.47, 0.47).

        // --- Fog ---
        if (_fog is not null)
        {
            env.FogEnabled = true;
            // spec: mission brief — "FogMode depth; FogDepthBegin=camFar*FOG_START; FogDepthEnd=camFar*FOG_END".
            env.FogMode = global::Godot.Environment.FogModeEnum.Depth;
            env.FogDepthBegin = CamFar * _fog.StartRatio;
            env.FogDepthEnd = CamFar * _fog.EndRatio;
            env.FogDepthCurve = 1.0f; // linear falloff

            // Per-keyframe fog colour.
            RgbF fogRgb = SafeKeyframe(_fog.Keyframes, ki);
            // spec: mission brief — "FogLightColor = fogRGB/255". Night ~(19,20,29), noon ~(155,101,57).
            env.FogLightColor = new Color(fogRgb.R, fogRgb.G, fogRgb.B, 1f);
            env.FogLightEnergy = 1.0f;
            env.FogSkyAffect = 0.5f; // moderate sky tinting
        }
        else
        {
            env.FogEnabled = false;
        }

        // --- Ambient light ---
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        if (_lightKeyframes is not null && _lightKeyframes.Length > 0)
        {
            LightData ld = SafeKeyframe(_lightKeyframes, ki);
            // spec: mission brief — "AmbientLightColor = ambientRGB/255 clamped >= 0; AmbientLightEnergy ~1".
            float ar = Math.Max(0f, ld.AmbientR);
            float ag = Math.Max(0f, ld.AmbientG);
            float ab = Math.Max(0f, ld.AmbientB);
            env.AmbientLightColor = new Color(ar, ag, ab, 1f);
            env.AmbientLightEnergy = 1.0f;
        }
        else
        {
            // Sensible default: dim grey ambient.
            env.AmbientLightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            env.AmbientLightEnergy = 1.0f;
        }

        // Tonemapping carried over from the static scene resource (neutral Filmic).
        env.TonemapMode = global::Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = 1.0f;

        // No GI / SSAO / SSIL / SDFGI (expensive; not needed for a preview renderer).
        env.SsaoEnabled = false;
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;
        env.GlowEnabled = false;

        // Push into the WorldEnvironment node (replaces the static sub-resource).
        if (_worldEnv is not null)
        {
            _worldEnv.Environment = env;
        }

        // --- Directional light ---
        if (_dirLight is not null && _lightKeyframes is not null && _lightKeyframes.Length > 0)
        {
            LightData ld = SafeKeyframe(_lightKeyframes, ki);

            // Convert legacy left-handed sun direction to Godot right-handed space.
            // spec: WorldCoordinates.ToGodot — (x,y,z) -> (x,y,-z).
            (float gx, float gy, float gz) = WorldCoordinates.ToGodot(ld.SunDirX, ld.SunDirY, ld.SunDirZ);

            var godotDir = new Vector3(gx, gy, gz);
            if (godotDir.LengthSquared() > 1e-6f)
            {
                godotDir = godotDir.Normalized();
                // A DirectionalLight3D emits along its local -Z axis. We build a basis
                // where the local -Z aligns with godotDir, then extract its Euler angles.
                // spec: Godot 4 DirectionalLight3D — light direction = -Transform.Basis.Z.
                // Approach: use Basis.LookingAt(godotDir, up=Vector3.Up).
                // LookingAt targets the -Z forward, so we negate godotDir to make -Z point in godotDir.
                // Actually: Basis.LookingAt(target) sets -Z toward target.
                // We WANT -Z = godotDir, so call LookingAt(godotDir) directly.
                try
                {
                    // LookingAt(target, up): the resulting -Z faces toward target.
                    // Guard against degenerate (nearly-vertical) directions.
                    Vector3 up = (Math.Abs(godotDir.Dot(Vector3.Up)) > 0.99f)
                        ? Vector3.Forward
                        : Vector3.Up;
                    _dirLight.Basis = Basis.LookingAt(godotDir, up);
                }
                catch
                {
                    // If LookingAt fails (degenerate input), leave the transform unchanged.
                }
            }

            // Diffuse colour. spec: mission brief — "LightColor = diffuseRGB/255 (allow >1 for overbright)".
            // Diffuse values > 255 in the legacy data are overbright; we divide by 255 and allow
            // values > 1.0 — Godot's HDR pipeline handles this correctly.
            _dirLight.LightColor = new Color(ld.DiffuseR, ld.DiffuseG, ld.DiffuseB, 1f);
            // Normalise energy: if all channels <= 1 use energy 1, else push extra into LightEnergy.
            // Simple approach: energy = max channel if any component > 1 (overbright scaling).
            float maxComp = Math.Max(ld.DiffuseR, Math.Max(ld.DiffuseG, ld.DiffuseB));
            if (maxComp > 1.0f)
            {
                _dirLight.LightColor = new Color(
                    ld.DiffuseR / maxComp,
                    ld.DiffuseG / maxComp,
                    ld.DiffuseB / maxComp,
                    1f);
                _dirLight.LightEnergy = maxComp;
            }
            else
            {
                _dirLight.LightEnergy = 1.0f;
            }

            _dirLight.ShadowEnabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Scene node resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the existing "WorldEnvironment" and "DirectionalLight3D" nodes in the scene root
    /// (siblings of RealWorldRenderer or children of the World node), or creates new ones as
    /// children of this node when absent.
    ///
    /// Strategy: walk up to this node's owner (the scene root), then search its direct children.
    /// This matches the structure in World.tscn where both nodes are direct children of "World".
    /// </summary>
    private void ResolveOrCreateSceneNodes()
    {
        // Walk up to find the scene root (Node named "World" or the first ancestor with Owner == null).
        Node sceneRoot = GetSceneRoot();

        // Search for WorldEnvironment.
        _worldEnv = FindChildOfType<WorldEnvironment>(sceneRoot);
        if (_worldEnv is null)
        {
            GD.Print("[EnvironmentNode] No WorldEnvironment found in scene — adding own child.");
            _worldEnv = new WorldEnvironment { Name = "EnvironmentNode_WorldEnv" };
            AddChild(_worldEnv);
        }
        else
        {
            GD.Print($"[EnvironmentNode] Found WorldEnvironment '{_worldEnv.Name}' — will replace .Environment.");
        }

        // Search for DirectionalLight3D.
        _dirLight = FindChildOfType<DirectionalLight3D>(sceneRoot);
        if (_dirLight is null)
        {
            GD.Print("[EnvironmentNode] No DirectionalLight3D found in scene — adding own child.");
            _dirLight = new DirectionalLight3D { Name = "EnvironmentNode_SunLight" };
            _dirLight.ShadowEnabled = true;
            AddChild(_dirLight);
        }
        else
        {
            GD.Print($"[EnvironmentNode] Found DirectionalLight3D '{_dirLight.Name}' — will update in-place.");
        }
    }

    private Node GetSceneRoot()
    {
        // Walk up until we find the node whose parent is the Godot scene root or has no parent.
        Node current = this;
        while (current.GetParent() is { } parent)
        {
            current = parent;
            // Stop when we reach the direct child of GetTree().Root (Viewport).
            if (current.GetParent() == GetTree().Root)
                break;
        }
        return current;
    }

    private static T? FindChildOfType<T>(Node root) where T : Node
    {
        // Search direct children first (matches World.tscn layout).
        foreach (Node child in root.GetChildren())
        {
            if (child is T match) return match;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // File parsers (inline — no new parser class)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses data/sky/dat/fog{area}.txt.
    ///
    /// Format (CP949, TAB-separated):
    ///   Line 0: FOG_START [tab] FOG_END     (float 0..1 ratios of view distance)
    ///   Lines 1..48: R [tab] G [tab] B       (fog colour for each half-hour keyframe, 0-255 integers)
    ///
    /// spec: mission brief — "fog{area}.txt: FOG_START (0..1), FOG_END, then 48 half-hour keyframes".
    /// spec: data/sky/dat/fog{area}.txt — CP949 TAB-separated.
    /// </summary>
    private static FogData? TryParseFog(RealClientAssets assets, int areaId)
    {
        string path = SkyPath("fog", areaId);
        if (!assets.Contains(path))
        {
            GD.Print($"[EnvironmentNode] {path} absent — fog disabled.");
            return null;
        }

        try
        {
            string text = ReadCp949(assets.GetRaw(path));
            string[] lines = SplitLines(text);

            if (lines.Length < 1)
            {
                GD.PrintErr($"[EnvironmentNode] {path}: empty file.");
                return null;
            }

            // Line 0: FOG_START [tab] FOG_END
            string[] header = lines[0].Split('\t');
            float startRatio = 0.5f; // spec: mission brief example
            float endRatio = 0.9f;
            if (header.Length >= 1) float.TryParse(header[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out startRatio);
            if (header.Length >= 2) float.TryParse(header[1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out endRatio);

            // Lines 1..48: R G B keyframes
            var keyframes = new RgbF[KeyframeCount];
            for (int i = 0; i < KeyframeCount; i++)
            {
                int li = i + 1;
                if (li < lines.Length)
                {
                    string[] cols = lines[li].Split('\t');
                    // spec: mission brief — "fog RGB (0-255); Night~(19,20,29), noon~(155,101,57)".
                    keyframes[i] = ParseRgb255(cols, 0);
                }
                else
                {
                    // Pad with a neutral grey if the file is shorter than 48 keyframes.
                    keyframes[i] = new RgbF(0.5f, 0.5f, 0.5f);
                }
            }

            GD.Print($"[EnvironmentNode] fog{areaId}.txt parsed: start={startRatio:F2} end={endRatio:F2}.");
            return new FogData(startRatio, endRatio, keyframes);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] fog parse failed ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses data/sky/dat/light{area}.txt into per-keyframe light records.
    ///
    /// Format (CP949, TAB-separated, confirmed from mission brief):
    ///   The file contains a sun light direction, a character light direction, and per-time
    ///   ambient/diffuse/specular RGB (0-255, diffuse may exceed 255 = overbright) + StarDome brightness.
    ///
    ///   Assumed columnar layout (PROVISIONAL — adjust when confirmed by spec-author):
    ///   Each row = one half-hour keyframe:
    ///     Col 0:  Sun dir X (float)
    ///     Col 1:  Sun dir Y (float)
    ///     Col 2:  Sun dir Z (float)
    ///     Col 3:  Char dir X (float, unused)
    ///     Col 4:  Char dir Y (float, unused)
    ///     Col 5:  Char dir Z (float, unused)
    ///     Col 6:  Ambient R  (0-255)
    ///     Col 7:  Ambient G
    ///     Col 8:  Ambient B
    ///     Col 9:  Diffuse R  (0-255, may be overbright >255)
    ///     Col 10: Diffuse G
    ///     Col 11: Diffuse B
    ///     Col 12: Specular R (0-255, unused in this node)
    ///     Col 13: Specular G
    ///     Col 14: Specular B
    ///     Col 15: StarDome brightness (float, unused)
    ///
    /// spec: mission brief — "light{area}.txt: Sun direction, Character direction, per-time
    ///        ambient/diffuse/specular RGB (0-255, diffuse may exceed 255), StarDome brightness".
    /// spec: data/sky/dat/light{area}.txt — CP949 TAB-separated.
    ///
    /// CAUTION: If the actual column layout differs, only this method needs updating.
    /// </summary>
    private static LightData[]? TryParseLightKeyframes(RealClientAssets assets, int areaId)
    {
        string path = SkyPath("light", areaId);
        if (!assets.Contains(path))
        {
            GD.Print($"[EnvironmentNode] {path} absent — using default directional light.");
            return null;
        }

        try
        {
            string text = ReadCp949(assets.GetRaw(path));
            string[] lines = SplitLines(text);

            var result = new List<LightData>(KeyframeCount);
            foreach (string line in lines)
            {
                string[] cols = line.Split('\t');
                if (cols.Length < 9) continue; // skip header/empty rows

                // Sun direction (cols 0..2).
                float sx = ParseFloat(cols, 0);
                float sy = ParseFloat(cols, 1);
                float sz = ParseFloat(cols, 2);

                // Ambient (cols 6..8). spec: mission brief — "ambient RGB (0-255)".
                float ar = ParseRaw255(cols, 6);
                float ag = ParseRaw255(cols, 7);
                float ab = ParseRaw255(cols, 8);

                // Diffuse (cols 9..11). spec: mission brief — "diffuse may exceed 255 = overbright".
                float dr = cols.Length > 9  ? ParseRawOverbright(cols, 9)  : 1f;
                float dg = cols.Length > 10 ? ParseRawOverbright(cols, 10) : 1f;
                float db = cols.Length > 11 ? ParseRawOverbright(cols, 11) : 1f;

                result.Add(new LightData(sx, sy, sz, ar, ag, ab, dr, dg, db));

                if (result.Count >= KeyframeCount) break;
            }

            if (result.Count == 0)
            {
                GD.PrintErr($"[EnvironmentNode] {path}: no valid keyframe rows found.");
                return null;
            }

            GD.Print($"[EnvironmentNode] light{areaId}.txt parsed: {result.Count} keyframes.");
            return result.ToArray();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] light parse failed ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses data/sky/dat/clouddome{area}.txt and returns the sky tint colour for the given keyframe.
    ///
    /// Format (CP949, TAB-separated):
    ///   Each row = one half-hour keyframe, columns = R G B (0-255) for the sky-dome vertex colour.
    ///   spec: mission brief — "a sky-dome vertex colour gradient; daytime ~(120,120,120)".
    /// spec: data/sky/dat/clouddome{area}.txt — CP949 TAB-separated.
    /// </summary>
    private static Color TryParseClouddomeTint(RealClientAssets assets, int areaId, int keyframeIndex)
    {
        // spec: mission brief — daytime ~(120,120,120) i.e. ~(0.47, 0.47, 0.47).
        var fallback = new Color(0.47f, 0.47f, 0.47f, 1f);

        string path = SkyPath("clouddome", areaId);
        if (!assets.Contains(path))
        {
            GD.Print($"[EnvironmentNode] {path} absent — using default sky tint.");
            return fallback;
        }

        try
        {
            string text = ReadCp949(assets.GetRaw(path));
            string[] lines = SplitLines(text);

            // Collect valid rows.
            var rows = new List<RgbF>(KeyframeCount);
            foreach (string line in lines)
            {
                string[] cols = line.Split('\t');
                if (cols.Length < 3) continue;
                rows.Add(ParseRgb255(cols, 0));
                if (rows.Count >= KeyframeCount) break;
            }

            if (rows.Count == 0) return fallback;

            RgbF noon = SafeKeyframe(rows.ToArray(), keyframeIndex);
            GD.Print($"[EnvironmentNode] clouddome{areaId}.txt noon tint: ({noon.R:F2},{noon.G:F2},{noon.B:F2}).");
            return new Color(noon.R, noon.G, noon.B, 1f);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] clouddome parse failed ({path}): {ex.Message}");
            return fallback;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the VFS path for a sky data file.
    /// spec: mission brief — "data/sky/dat/fog{area}.txt", "data/sky/dat/light{area}.txt", etc.
    /// Area id is used directly (e.g. area 0 -> "fog0.txt", area 1 -> "fog1.txt").
    /// </summary>
    private static string SkyPath(string prefix, int areaId)
        => $"data/sky/dat/{prefix}{areaId}.txt";

    /// <summary>
    /// Decodes a VFS byte slice as CP949 text.
    /// spec: CLAUDE.md — "Text is CP949".
    /// </summary>
    private static string ReadCp949(ReadOnlyMemory<byte> data)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(949).GetString(data.Span);
    }

    /// <summary>Splits text into non-empty trimmed lines, stripping CR.</summary>
    private static string[] SplitLines(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(l => l.TrimEnd('\r'))
               .Where(l => l.Length > 0)
               .ToArray();

    /// <summary>
    /// Parses three consecutive columns at <paramref name="startCol"/> as R G B (0-255 → 0..1).
    /// spec: mission brief — RGB values are 0-255 integers.
    /// </summary>
    private static RgbF ParseRgb255(string[] cols, int startCol)
    {
        float r = cols.Length > startCol     ? ParseRaw255(cols, startCol)     : 0f;
        float g = cols.Length > startCol + 1 ? ParseRaw255(cols, startCol + 1) : 0f;
        float b = cols.Length > startCol + 2 ? ParseRaw255(cols, startCol + 2) : 0f;
        return new RgbF(r, g, b);
    }

    /// <summary>Parses a single column as a 0-255 byte → 0..1 float. Returns 0 on failure.</summary>
    private static float ParseRaw255(string[] cols, int idx)
    {
        if (!int.TryParse(cols[idx].Trim(), out int v)) return 0f;
        return Math.Clamp(v, 0, 255) / 255f;
    }

    /// <summary>
    /// Parses a single column as a potentially-overbright value (may exceed 255).
    /// Returns the value / 255 even when > 1.0.
    /// spec: mission brief — "diffuse may exceed 255 = overbright".
    /// </summary>
    private static float ParseRawOverbright(string[] cols, int idx)
    {
        if (!float.TryParse(cols[idx].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v)) return 1f;
        return Math.Max(0f, v / 255f);
    }

    /// <summary>Parses a single column as a float. Returns 0 on failure.</summary>
    private static float ParseFloat(string[] cols, int idx)
    {
        if (idx >= cols.Length) return 0f;
        float.TryParse(cols[idx].Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v);
        return v;
    }

    /// <summary>
    /// Returns the element at <paramref name="ki"/> clamped to the array bounds.
    /// Avoids an IndexOutOfRange when the file has fewer rows than expected.
    /// </summary>
    private static T SafeKeyframe<T>(T[] arr, int ki)
        => arr[Math.Clamp(ki, 0, arr.Length - 1)];

    // -------------------------------------------------------------------------
    // Data records (value types — no heap allocation per-frame)
    // -------------------------------------------------------------------------

    private sealed record FogData(float StartRatio, float EndRatio, RgbF[] Keyframes);

    private readonly record struct RgbF(float R, float G, float B);

    /// <summary>
    /// Per-keyframe light data.
    /// Ambient and diffuse are already divided by 255 (diffuse may be > 1.0 = overbright).
    /// Sun direction is in legacy left-handed world space (not yet converted to Godot).
    /// </summary>
    private readonly record struct LightData(
        float SunDirX, float SunDirY, float SunDirZ,
        float AmbientR, float AmbientG, float AmbientB,
        float DiffuseR, float DiffuseG, float DiffuseB);
}
