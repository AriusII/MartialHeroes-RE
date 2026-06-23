using System.Globalization;
using System.Text;
using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public enum CelCharState
{
    Default = 0,
    Choice = 1,
    Hit = 2,
    Alpha = 3,
    Hidden = 4,
    Poison = 5,
    Type = 6,
    Anger = 7,
    Auto = 8
}

public static class CelShadeMaterialFactory
{
    private const string ToonRampVfsPath = "data/shader/toonramp.bmp";
    private const string DisplayLuaVfsPath = "data/script/display.lua";

    private static readonly (Vector3 Multi, Vector4 Add)[] _stateTable = BuildShippedDefaults();

    private static ImageTexture? _sessionRamp;

    private static readonly Vector3 DefaultLightDir = new(-1f, 0f, 0f);

    public static bool CelEnabled { get; set; } = true;

    public static float AmbientFloorEnergy { get; set; } = 1.0f;

    public static void InitSession(RealClientAssets? assets)
    {
        _sessionRamp = LoadToonRamp(assets);
        LoadDisplayLua(assets);
    }

    public static ImageTexture? LoadToonRamp(RealClientAssets? assets)
    {
        if (assets is null) return null;
        var tex = assets.LoadTexture(ToonRampVfsPath);
        if (tex is null)
            GD.Print("[CelShade] toonramp.bmp not found in VFS — using procedural fallback ramp.");
        else
            GD.Print($"[CelShade] toonramp.bmp loaded: {tex.GetWidth()}×{tex.GetHeight()}.");
        return tex;
    }

    private static void LoadDisplayLua(RealClientAssets? assets)
    {
        if (assets is null) return;

        var raw = assets.GetRaw(DisplayLuaVfsPath);
        if (raw.IsEmpty)
        {
            GD.Print("[CelShade] display.lua not found in VFS — using shipped defaults for DISPLAY_CHAR_BRIGHT_*.");
            return;
        }

        try
        {
            ParseDisplayLua(raw.Span);
            GD.Print("[CelShade] display.lua parsed — per-state DISPLAY_CHAR_BRIGHT_* table loaded.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CelShade] display.lua parse failed: {ex.Message} — keeping shipped defaults.");
        }
    }

    private static void ParseDisplayLua(ReadOnlySpan<byte> raw)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(949).GetString(raw);

        var kv = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("--", StringComparison.Ordinal)) continue;
            if (line.StartsWith("if ", StringComparison.Ordinal) ||
                line.StartsWith("elseif ", StringComparison.Ordinal) ||
                line.StartsWith("else", StringComparison.Ordinal) ||
                line.StartsWith("end", StringComparison.Ordinal)) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var valRaw = line[(eq + 1)..].Trim();

            var commentIdx = valRaw.IndexOf("--", StringComparison.Ordinal);
            if (commentIdx >= 0) valRaw = valRaw[..commentIdx].Trim();

            if (float.TryParse(valRaw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var val))
                kv[key] = val;
        }

        var stateNames = new[] { "DEFAULT", "CHOICE", "HIT", "ALPHA", "HIDDEN", "POISON", "TYPE", "ANGER", "AUTO" };

        for (var i = 0; i < stateNames.Length; i++)
        {
            var s = stateNames[i];
            var mr = Get(kv, $"DISPLAY_CHAR_BRIGHT_MULTI_R_{s}", _stateTable[i].Multi.X);
            var mg = Get(kv, $"DISPLAY_CHAR_BRIGHT_MULTI_G_{s}", _stateTable[i].Multi.Y);
            var mb = Get(kv, $"DISPLAY_CHAR_BRIGHT_MULTI_B_{s}", _stateTable[i].Multi.Z);
            var ar = Get(kv, $"DISPLAY_CHAR_BRIGHT_ADD_R_{s}", _stateTable[i].Add.X);
            var ag = Get(kv, $"DISPLAY_CHAR_BRIGHT_ADD_G_{s}", _stateTable[i].Add.Y);
            var ab = Get(kv, $"DISPLAY_CHAR_BRIGHT_ADD_B_{s}", _stateTable[i].Add.Z);
            var aw = Get(kv, $"DISPLAY_CHAR_BRIGHT_ALPHA_{s}", _stateTable[i].Add.W);

            _stateTable[i] = (new Vector3(mr, mg, mb), new Vector4(ar, ag, ab, aw));
        }
    }

    private static float Get(Dictionary<string, float> kv, string key, float fallback)
    {
        return kv.TryGetValue(key, out var v) ? v : fallback;
    }

    private static (Vector3, Vector4)[] BuildShippedDefaults()
    {
        var one = new Vector3(1f, 1f, 1f);
        var zero4 = new Vector4(0f, 0f, 0f, 1f);
        var table = new (Vector3, Vector4)[9];
        for (var i = 0; i < 9; i++) table[i] = (one, zero4);
        return table;
    }

    public static ShaderMaterial Build(ImageTexture? albedo, CelCharState state = CelCharState.Default,
        bool stealth = false)
    {
        return Build(albedo, _sessionRamp, state, stealth);
    }

    public static ShaderMaterial Build(ImageTexture? albedo, ImageTexture? toonRamp,
        CelCharState state = CelCharState.Default, bool stealth = false)
    {
        var shader = GD.Load<Shader>("res://World/CelShade.gdshader");
        var mat = new ShaderMaterial { Shader = shader };

        if (albedo is not null)
        {
            mat.SetShaderParameter("albedo_texture", albedo);
            mat.SetShaderParameter("use_albedo_texture", true);
            mat.SetShaderParameter("albedo_color", new Color(1f, 1f, 1f));
        }
        else
        {
            mat.SetShaderParameter("use_albedo_texture", false);
            mat.SetShaderParameter("albedo_color", new Color(0.85f, 0.75f, 0.65f));
        }

        if (toonRamp is not null)
        {
            mat.SetShaderParameter("toon_ramp", toonRamp);
            mat.SetShaderParameter("use_toon_ramp", true);
        }
        else
        {
            mat.SetShaderParameter("use_toon_ramp", false);
        }

        mat.SetShaderParameter("cel_enabled", CelEnabled);
        mat.SetShaderParameter("ambient_floor_energy", AmbientFloorEnergy);
        mat.SetShaderParameter("ambient_floor_color", new Color(1f, 1f, 1f));

        mat.SetShaderParameter("light_dir", DefaultLightDir);
        mat.SetShaderParameter("light_color", new Color(1f, 1f, 1f));

        ApplyState(mat, state, stealth);

        return mat;
    }

    public static void ApplyState(ShaderMaterial mat, CelCharState state, bool stealth = false)
    {
        var idx = (int)state;
        if ((uint)idx >= (uint)_stateTable.Length) idx = 0;

        var (multi, add) = _stateTable[idx];
        mat.SetShaderParameter("state_multi", multi);
        mat.SetShaderParameter("state_add", new Color(add.X, add.Y, add.Z, add.W));
        mat.SetShaderParameter("stealth", stealth);
    }

    public static ShaderMaterial Build(ImageTexture? albedo)
    {
        return Build(albedo, _sessionRamp);
    }
}