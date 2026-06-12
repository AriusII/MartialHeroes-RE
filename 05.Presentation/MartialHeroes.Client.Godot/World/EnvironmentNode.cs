// World/EnvironmentNode.cs
//
// Passive environment node that drives a Godot WorldEnvironment + DirectionalLight3D from the
// per-area sky data files found in data/sky/dat/ of the legacy client VFS.
//
// Sky data files (VFS-confirmed naming — harness run 2026-06-12):
//   data/sky/dat/fog{areaId}.txt        — named-key header + 48 half-hour fog RGB keyframes
//   data/sky/dat/light{areaId}.txt      — sun/char direction vectors + 48 keyframes of
//                                          ambient/diffuse/specular RGB + sun direction floats
//   data/sky/dat/clouddome{areaId}.txt  — sky-dome vertex colour gradient (sky tint)
//
//   CONFIRMED: area IDs use BARE decimal digits with NO zero-padding.
//     fog9.txt / light9.txt / clouddome9.txt    (area 9)
//     fog2.txt / light2.txt / clouddome2.txt    (area 2)
//     fog100.txt / light100.txt / clouddome100.txt  (area 100)
//   3-digit zero-padded forms (fog009.txt, fog002.txt) do NOT exist in the VFS.
//   spec: harness-confirmed, data/sky/dat/ entry listing 2026-06-12.
//
// fog{N}.txt confirmed format (harness-probed, area 0/2/9 verified identical):
//   Line 0:  FOG_START_KEY   TAB float         — startRatio (0..1)
//   Line 1:  FOG_END_KEY     TAB float         — endRatio (0..1)
//   Line 2:  COUNT_KEY       TAB int            — keyframe count (always 48)
//   Line 3:  column header (4 cols, all str)   — skip
//   Lines 4..51: keyframe rows [str(5c), int, int, int, str(3c)]
//     col 0: time label (e.g. "00:00") — ignored
//     col 1: fog R   (int 0-255)
//     col 2: fog G
//     col 3: fog B
//     col 4: unit/trailing string — ignored
//   spec: harness probe 2026-06-12 — confirmed shape × 48 rows (lines 4-51).
//
// light{N}.txt confirmed format (harness-probed, area 0/2/9 verified identical):
//   Line 0:  SUN_INDEX_KEY   TAB int TAB comment   — ignored
//   Line 1:  SUN_DIR_KEY     TAB float TAB float TAB float TAB comment  — sun XYZ direction
//   Line 2:  CHAR_INDEX_KEY  TAB int TAB comment   — ignored
//   Line 3:  CHAR_DIR_KEY    TAB float TAB float TAB float TAB comment  — char dir (ignored)
//   Line 4:  group header row (24 cols, all str)  — skip
//   Line 5:  column name row  (26 cols, all str)  — skip
//   Lines 6..53: 48 keyframe rows, 27 TAB-separated columns:
//     col  0: time label (5 chars)          — ignored
//     col  1: SunLight R (int 0-255, overbright-safe)
//     col  2: SunLight G
//     col  3: SunLight B
//     col  4: CharLight R                   — ignored
//     col  5: CharLight G                   — ignored
//     col  6: CharLight B                   — ignored
//     col  7: Ambient R (int 0-255)
//     col  8: Ambient G
//     col  9: Ambient B
//     cols 10-25: further lighting params   — ignored
//     col 26: trailing string               — ignored
//   spec: harness-confirmed shape [str(5c), int×20, float, float, int×3, str(3c)] = 27 cols.
//         2026-06-12.
//
//   IMPORTANT: sun direction is STATIC per-file (lines 1/3), NOT per-keyframe.
//   Per-keyframe sun colour (col 1-3 = SunLight RGB 0-255) drives DirectionalLight3D colour.
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
//   name and REPLACES its .Environment property with a newly constructed Godot.Environment
//   built from the sky data. Only one WorldEnvironment may be active in Godot 4.
//   If absent, we add our own child node. Same for DirectionalLight3D.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: WorldCoordinates.ToGodot — (x,y,z) -> (x,y,-z) for direction conversion.

using Godot;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive rendering node that reads per-area sky files from the VFS and configures the scene's
/// <see cref="WorldEnvironment"/> and <see cref="DirectionalLight3D"/> accordingly.
///
/// Call <see cref="Configure"/> once per area load (after <c>ResolveTargetCell</c> has set
/// <c>TargetAreaId</c>). All subsequent time-of-day changes go through <see cref="SetTimeOfDay"/>.
///
/// Default time-of-day: keyframe 24 (noon — index 24 of 0..47 half-hour steps).
///
/// Fallback chain when a file is absent:
///   Try data/sky/dat/{prefix}{areaId}.txt → try area 0 → use built-in defaults.
///   Never throws regardless of VFS state.
/// </summary>
public sealed partial class EnvironmentNode : Node3D
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Camera far-plane distance for fog depth scaling (matches CameraController.FarPlane).
    /// spec: mission brief — "camFar~8000".
    /// </summary>
    private const float CamFar = 8000f;

    /// <summary>
    /// Number of half-hour keyframes per day.
    /// spec: harness-confirmed — fog9.txt has exactly 48 data rows (lines 4..51). 2026-06-12.
    /// </summary>
    private const int KeyframeCount = 48;

    /// <summary>Default keyframe (noon = 24 of 0..47).</summary>
    private const int NoonKeyframeIndex = 24;

    // -------------------------------------------------------------------------
    // Parsed sky state
    // -------------------------------------------------------------------------

    private FogData? _fog;
    private LightData[]? _lightKeyframes;
    private Color _skyTint;

    // Static sun direction from light{N}.txt line 1 (cols 1-3, float XYZ).
    // spec: harness-confirmed — global per-file, NOT per-keyframe. 2026-06-12.
    private float _sunDirX, _sunDirY, _sunDirZ;
    private bool _hasSunDir;

    private WorldEnvironment? _worldEnv;
    private DirectionalLight3D? _dirLight;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses sky files for <paramref name="areaId"/> and applies noon defaults.
    /// Must be called on the Godot main thread after <c>ResolveTargetCell</c>.
    ///
    /// Orchestrator wiring (insert after ResolveTargetCell in RealWorldRenderer.Initialise):
    /// <code>
    ///   var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
    ///   AddChild(envNode);
    ///   envNode.Configure(_assets, TargetAreaId);
    /// </code>
    ///
    /// spec: data/sky/dat/fog{areaId}.txt — bare decimal, no zero-padding. CONFIRMED 2026-06-12.
    /// </summary>
    public void Configure(RealClientAssets? assets, int areaId)
    {
        GD.Print($"[EnvironmentNode] Configure area={areaId}");
        ResolveOrCreateSceneNodes();

        if (assets is null)
        {
            GD.Print("[EnvironmentNode] No VFS — leaving default environment.");
            return;
        }

        // Fog. Fallback: area 0.
        _fog = TryParseFog(assets, areaId) ?? TryParseFog(assets, 0);

        // Static sun direction from light{N}.txt header. Fallback: area 0.
        TryParseSunDirection(assets, areaId);
        if (!_hasSunDir) TryParseSunDirection(assets, 0);

        // Per-keyframe light colours. Fallback: area 0.
        _lightKeyframes = TryParseLightKeyframes(assets, areaId) ?? TryParseLightKeyframes(assets, 0);

        // Sky tint from clouddome (noon row). Fallback: area 0, then built-in grey.
        _skyTint = TryParseClouddomeTint(assets, areaId, NoonKeyframeIndex);
        if (_skyTint == default)
            _skyTint = TryParseClouddomeTint(assets, 0, NoonKeyframeIndex);

        ApplyTimeOfDay(NoonKeyframeIndex);

        GD.Print($"[EnvironmentNode] Configured. Fog={_fog is not null}, " +
                 $"LightKeyframes={_lightKeyframes?.Length ?? 0}, SkyTint={_skyTint}, " +
                 $"SunDir={(_hasSunDir ? $"({_sunDirX:F2},{_sunDirY:F2},{_sunDirZ:F2})" : "default")}");
    }

    /// <summary>
    /// Advances the visual time of day. keyframeIndex 0=midnight, 24=noon, 47=last half-hour.
    /// Clamps to [0, 47]. No-op if Configure has not been called.
    /// </summary>
    public void SetTimeOfDay(int keyframeIndex)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        ApplyTimeOfDay(keyframeIndex);
    }

    // -------------------------------------------------------------------------
    // Keyframe application
    // -------------------------------------------------------------------------

    private void ApplyTimeOfDay(int ki)
    {
        var env = new global::Godot.Environment();

        // Background colour from clouddome noon tint.
        env.BackgroundMode  = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = _skyTint != default ? _skyTint : new Color(0.47f, 0.47f, 0.47f, 1f);

        // Fog.
        if (_fog is not null)
        {
            env.FogEnabled = true;
            // spec: harness-confirmed — FOG_START/END are 0..1 ratios; multiply by CamFar for world units.
            env.FogMode       = global::Godot.Environment.FogModeEnum.Depth;
            env.FogDepthBegin = CamFar * _fog.StartRatio;
            env.FogDepthEnd   = CamFar * _fog.EndRatio;
            env.FogDepthCurve = 1.0f;

            RgbF fogRgb = SafeKeyframe(_fog.Keyframes, ki);
            // spec: harness-confirmed — fog RGB keyframe cols 1,2,3 int 0-255.
            env.FogLightColor  = new Color(fogRgb.R, fogRgb.G, fogRgb.B, 1f);
            env.FogLightEnergy = 1.0f;
            env.FogSkyAffect   = 0.5f;
        }
        else
        {
            env.FogEnabled = false;
        }

        // Ambient.
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        if (_lightKeyframes is not null && _lightKeyframes.Length > 0)
        {
            LightData ld = SafeKeyframe(_lightKeyframes, ki);
            // spec: harness-confirmed — ambient cols 7,8,9 int 0-255 → divided by 255.
            env.AmbientLightColor  = new Color(Math.Max(0f, ld.AmbientR), Math.Max(0f, ld.AmbientG), Math.Max(0f, ld.AmbientB), 1f);
            env.AmbientLightEnergy = 1.0f;
        }
        else
        {
            env.AmbientLightColor  = new Color(0.3f, 0.3f, 0.3f, 1f);
            env.AmbientLightEnergy = 1.0f;
        }

        env.TonemapMode     = global::Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = 1.0f;
        env.SsaoEnabled     = false;
        env.SsilEnabled     = false;
        env.SdfgiEnabled    = false;
        env.GlowEnabled     = false;

        if (_worldEnv is not null)
            _worldEnv.Environment = env;

        // Directional light.
        if (_dirLight is not null)
        {
            // Static sun direction (global per-file — set once from header).
            // spec: harness-confirmed — sun direction at light{N}.txt line 1 cols 1-3 (float XYZ).
            if (_hasSunDir)
            {
                // Convert left-handed legacy space to Godot right-handed.
                // spec: WorldCoordinates.ToGodot — (x,y,z) -> (x,y,-z).
                (float gx, float gy, float gz) = WorldCoordinates.ToGodot(_sunDirX, _sunDirY, _sunDirZ);
                var godotDir = new Vector3(gx, gy, gz);
                if (godotDir.LengthSquared() > 1e-6f)
                {
                    godotDir = godotDir.Normalized();
                    try
                    {
                        Vector3 up = (Math.Abs(godotDir.Dot(Vector3.Up)) > 0.99f)
                            ? Vector3.Forward : Vector3.Up;
                        _dirLight.Basis = Basis.LookingAt(godotDir, up);
                    }
                    catch { /* degenerate direction — leave unchanged */ }
                }
            }

            // Per-keyframe sun colour (cols 1-3 of keyframe rows = SunLight R/G/B 0-255, overbright-safe).
            // spec: harness-confirmed — SunLight cols 1-3 are int, may exceed 255 for overbright.
            if (_lightKeyframes is not null && _lightKeyframes.Length > 0)
            {
                LightData ld = SafeKeyframe(_lightKeyframes, ki);
                float maxComp = Math.Max(ld.SunR, Math.Max(ld.SunG, ld.SunB));
                if (maxComp > 1.0f)
                {
                    _dirLight.LightColor  = new Color(ld.SunR / maxComp, ld.SunG / maxComp, ld.SunB / maxComp, 1f);
                    _dirLight.LightEnergy = maxComp;
                }
                else
                {
                    _dirLight.LightColor  = new Color(ld.SunR, ld.SunG, ld.SunB, 1f);
                    _dirLight.LightEnergy = 1.0f;
                }
            }
            else
            {
                _dirLight.LightColor  = Colors.White;
                _dirLight.LightEnergy = 1.0f;
            }

            _dirLight.ShadowEnabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Scene node resolution
    // -------------------------------------------------------------------------

    private void ResolveOrCreateSceneNodes()
    {
        Node sceneRoot = GetSceneRoot();

        _worldEnv = FindChildOfType<WorldEnvironment>(sceneRoot);
        if (_worldEnv is null)
        {
            GD.Print("[EnvironmentNode] No WorldEnvironment in scene — adding own child.");
            _worldEnv = new WorldEnvironment { Name = "EnvironmentNode_WorldEnv" };
            AddChild(_worldEnv);
        }
        else
        {
            GD.Print($"[EnvironmentNode] Found WorldEnvironment '{_worldEnv.Name}'.");
        }

        _dirLight = FindChildOfType<DirectionalLight3D>(sceneRoot);
        if (_dirLight is null)
        {
            GD.Print("[EnvironmentNode] No DirectionalLight3D in scene — adding own child.");
            _dirLight = new DirectionalLight3D { Name = "EnvironmentNode_SunLight", ShadowEnabled = true };
            AddChild(_dirLight);
        }
        else
        {
            GD.Print($"[EnvironmentNode] Found DirectionalLight3D '{_dirLight.Name}'.");
        }
    }

    private Node GetSceneRoot()
    {
        Node current = this;
        while (current.GetParent() is { } parent)
        {
            current = parent;
            if (current.GetParent() == GetTree().Root) break;
        }
        return current;
    }

    private static T? FindChildOfType<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
            if (child is T match) return match;
        return null;
    }

    // -------------------------------------------------------------------------
    // Parsers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses data/sky/dat/fog{areaId}.txt.
    ///
    /// Confirmed format (harness 2026-06-12, areas 0/2/9 verified identical):
    ///   Lines 0-2: named key-value header rows (key TAB value TAB ...).
    ///     Line 0 col 1 = FOG_START (float 0..1)
    ///     Line 1 col 1 = FOG_END   (float 0..1)
    ///     Line 2 col 1 = keyframe count (int, always 48)
    ///   Line 3: column label header — skip.
    ///   Lines 4..51: [str(5c), int, int, int, str(3c)] — time, R, G, B, unit.
    /// spec: bare decimal naming confirmed (fog9.txt, NOT fog009.txt). 2026-06-12.
    /// </summary>
    private static FogData? TryParseFog(RealClientAssets assets, int areaId)
    {
        // spec: confirmed — bare decimal, no zero-padding. fog9.txt exists; fog009.txt absent. 2026-06-12.
        string path = $"data/sky/dat/fog{areaId}.txt";
        if (!assets.Contains(path)) { GD.Print($"[EnvironmentNode] {path} absent."); return null; }

        try
        {
            string[] lines = SplitLines(ReadCp949(assets.GetRaw(path)));

            if (lines.Length < 4) { GD.PrintErr($"[EnvironmentNode] {path}: too few lines."); return null; }

            // Line 0, col 1: FOG_START ratio.
            // spec: harness-confirmed — named-key TAB value pattern, col 1 is the float.
            float startRatio = 0.5f, endRatio = 0.9f;
            { var c = lines[0].Split('\t'); if (c.Length >= 2) float.TryParse(c[1].Trim(), FloatStyle, Inv, out startRatio); }
            { var c = lines[1].Split('\t'); if (c.Length >= 2) float.TryParse(c[1].Trim(), FloatStyle, Inv, out endRatio); }
            // Lines 2(count) and 3(column header) are skipped; data starts at line 4.

            var keyframes = new RgbF[KeyframeCount];
            for (int i = 0; i < KeyframeCount; i++)
            {
                int li = 4 + i; // spec: data starts at line 4. CONFIRMED.
                if (li < lines.Length)
                {
                    var cols = lines[li].Split('\t');
                    // spec: cols [str(5c), int, int, int, str(3c)]; RGB at cols 1,2,3. CONFIRMED.
                    keyframes[i] = ParseRgb255(cols, 1);
                }
                else
                {
                    keyframes[i] = new RgbF(0.5f, 0.5f, 0.5f);
                }
            }

            GD.Print($"[EnvironmentNode] fog{areaId}.txt parsed. start={startRatio:F3} end={endRatio:F3}.");
            return new FogData(startRatio, endRatio, keyframes);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] fog parse failed ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the static sun direction vector from light{N}.txt, stored in <c>_sunDir*</c>.
    ///
    /// Confirmed format: line 1 (the second non-empty line):
    ///   SUN_DIR_VECTOR_KEY TAB float(X) TAB float(Y) TAB float(Z) TAB comment
    /// spec: harness-confirmed shape [str(19c),float,float,float,str(3c)] at line 1. 2026-06-12.
    /// </summary>
    private void TryParseSunDirection(RealClientAssets assets, int areaId)
    {
        // spec: bare decimal naming confirmed 2026-06-12.
        string path = $"data/sky/dat/light{areaId}.txt";
        if (!assets.Contains(path)) return;
        try
        {
            string[] lines = SplitLines(ReadCp949(assets.GetRaw(path)));
            if (lines.Length < 2) return;
            // Line 1 = SUN_DIRECTION_VECTOR: key TAB X TAB Y TAB Z [TAB comment].
            // spec: harness-confirmed shape [str(19c),float,float,float,str(3c)]. 2026-06-12.
            var sun = lines[1].Split('\t');
            if (sun.Length >= 4 &&
                float.TryParse(sun[1].Trim(), FloatStyle, Inv, out float sx) &&
                float.TryParse(sun[2].Trim(), FloatStyle, Inv, out float sy) &&
                float.TryParse(sun[3].Trim(), FloatStyle, Inv, out float sz))
            {
                _sunDirX = sx; _sunDirY = sy; _sunDirZ = sz; _hasSunDir = true;
                GD.Print($"[EnvironmentNode] light{areaId}.txt sun direction parsed.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] sun direction parse failed (light{areaId}.txt): {ex.Message}");
        }
    }

    /// <summary>
    /// Parses data/sky/dat/light{areaId}.txt into per-keyframe light colour records.
    ///
    /// Confirmed format (harness 2026-06-12, areas 0/2/9 verified identical):
    ///   Lines 0-5: header rows (0-3 named keys, 4-5 column headers) — skip.
    ///   Lines 6..53: 48 keyframe rows, 27 TAB-separated columns:
    ///     col  0: time label (5 chars, ignored)
    ///     col  1: SunLight R  (int 0-255, overbright-safe — may exceed 255)
    ///     col  2: SunLight G
    ///     col  3: SunLight B
    ///     col  4-6: CharLight R/G/B (ignored)
    ///     col  7: Ambient R  (int 0-255)
    ///     col  8: Ambient G
    ///     col  9: Ambient B
    ///     cols 10-26: further params (ignored)
    /// spec: harness-confirmed shape [str, int×20, float, float, int×3, str]. 2026-06-12.
    /// </summary>
    private static LightData[]? TryParseLightKeyframes(RealClientAssets assets, int areaId)
    {
        // spec: bare decimal naming confirmed 2026-06-12.
        string path = $"data/sky/dat/light{areaId}.txt";
        if (!assets.Contains(path)) { GD.Print($"[EnvironmentNode] {path} absent."); return null; }

        try
        {
            string[] lines = SplitLines(ReadCp949(assets.GetRaw(path)));

            // Data rows start at line 6 (after 4 named-key rows + 2 column-header rows).
            // spec: harness-confirmed — header is lines 0-5, keyframe data lines 6-53. 2026-06-12.
            const int dataStart = 6;
            var result = new List<LightData>(KeyframeCount);

            foreach (string line in lines.Skip(dataStart))
            {
                var cols = line.Split('\t');
                if (cols.Length < 10) continue; // need cols 0-9 minimum

                // spec: cols 1-3 SunLight R/G/B (int, overbright-safe). 2026-06-12.
                float sr = ParseRawOverbright(cols, 1);
                float sg = ParseRawOverbright(cols, 2);
                float sb = ParseRawOverbright(cols, 3);

                // spec: cols 7-9 Ambient R/G/B (int 0-255). 2026-06-12.
                float ar = ParseRaw255(cols, 7);
                float ag = ParseRaw255(cols, 8);
                float ab = ParseRaw255(cols, 9);

                result.Add(new LightData(sr, sg, sb, ar, ag, ab));
                if (result.Count >= KeyframeCount) break;
            }

            if (result.Count == 0) { GD.PrintErr($"[EnvironmentNode] {path}: no keyframe rows."); return null; }

            GD.Print($"[EnvironmentNode] light{areaId}.txt: {result.Count} keyframes.");
            return result.ToArray();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] light parse failed ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses data/sky/dat/clouddome{areaId}.txt and returns the sky tint for <paramref name="keyframeIndex"/>.
    ///
    /// Assumed format (consistent with fog format — same file structure family):
    ///   Lines 0-3: header rows (skip).
    ///   Lines 4..51: [str(5c), int, int, int, ...] — time, R, G, B.
    /// Returns <c>default</c> (black) on absence or parse failure (caller substitutes fallback).
    /// spec: bare decimal naming confirmed (clouddome9.txt, NOT clouddome009.txt). 2026-06-12.
    /// </summary>
    private static Color TryParseClouddomeTint(RealClientAssets assets, int areaId, int ki)
    {
        // spec: bare decimal naming confirmed 2026-06-12.
        string path = $"data/sky/dat/clouddome{areaId}.txt";
        if (!assets.Contains(path)) { GD.Print($"[EnvironmentNode] {path} absent."); return default; }

        try
        {
            string[] lines = SplitLines(ReadCp949(assets.GetRaw(path)));
            const int dataStart = 4; // spec: assumed consistent with fog format.
            var rows = new List<RgbF>(KeyframeCount);
            foreach (string line in lines.Skip(dataStart))
            {
                var cols = line.Split('\t');
                if (cols.Length < 4) continue; // need col 0(label)+1,2,3(RGB)
                rows.Add(ParseRgb255(cols, 1));
                if (rows.Count >= KeyframeCount) break;
            }
            if (rows.Count == 0) return default;
            RgbF noon = SafeKeyframe(rows.ToArray(), ki);
            GD.Print($"[EnvironmentNode] clouddome{areaId}.txt noon tint: ({noon.R:F2},{noon.G:F2},{noon.B:F2}).");
            return new Color(noon.R, noon.G, noon.B, 1f);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnvironmentNode] clouddome parse failed ({path}): {ex.Message}");
            return default;
        }
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static readonly System.Globalization.NumberStyles FloatStyle =
        System.Globalization.NumberStyles.Float;
    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    private static string ReadCp949(ReadOnlyMemory<byte> data)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(949).GetString(data.Span);
    }

    private static string[] SplitLines(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(l => l.TrimEnd('\r'))
               .Where(l => l.Length > 0)
               .ToArray();

    /// <summary>RGB 0-255 at columns startCol, startCol+1, startCol+2 → 0..1 float.</summary>
    private static RgbF ParseRgb255(string[] cols, int startCol)
    {
        float r = cols.Length > startCol     ? ParseRaw255(cols, startCol)     : 0f;
        float g = cols.Length > startCol + 1 ? ParseRaw255(cols, startCol + 1) : 0f;
        float b = cols.Length > startCol + 2 ? ParseRaw255(cols, startCol + 2) : 0f;
        return new RgbF(r, g, b);
    }

    private static float ParseRaw255(string[] cols, int idx)
    {
        if (idx >= cols.Length) return 0f;
        if (!int.TryParse(cols[idx].Trim(), out int v)) return 0f;
        return Math.Clamp(v, 0, 255) / 255f;
    }

    /// <summary>
    /// Parses a column as a potentially overbright value (int that may exceed 255).
    /// Returns v/255 (may exceed 1.0 for overbright sun).
    /// spec: harness-confirmed — SunLight cols 1-3 may be >255. 2026-06-12.
    /// </summary>
    private static float ParseRawOverbright(string[] cols, int idx)
    {
        if (idx >= cols.Length) return 1f;
        if (!float.TryParse(cols[idx].Trim(), FloatStyle, Inv, out float v)) return 1f;
        return Math.Max(0f, v / 255f);
    }

    private static T SafeKeyframe<T>(T[] arr, int ki)
        => arr[Math.Clamp(ki, 0, arr.Length - 1)];

    // -------------------------------------------------------------------------
    // Data records (value types)
    // -------------------------------------------------------------------------

    private sealed record FogData(float StartRatio, float EndRatio, RgbF[] Keyframes);

    private readonly record struct RgbF(float R, float G, float B);

    /// <summary>
    /// Per-keyframe light data from light{N}.txt keyframe rows.
    /// Sun: cols 1-3 (0-255 overbright-safe, already /255); may be > 1.0.
    /// Ambient: cols 7-9 (0-255, already /255).
    /// spec: harness-confirmed column indices. 2026-06-12.
    /// </summary>
    private readonly record struct LightData(
        float SunR, float SunG, float SunB,
        float AmbientR, float AmbientG, float AmbientB);
}
