
using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public static class CelShadeMaterialFactory
{
    private const string ToonRampVfsPath = "data/shader/toonramp.bmp";

    private static ImageTexture? _sessionRamp;

    private static readonly Vector3
        DefaultLightDir = new(-1f, 0f, 0f);

    private static readonly Vector3 DefaultLight2Dir = new Vector3(0f, 1f, 0f).Normalized();

    public static bool CelEnabled { get; set; } = true;

    public static float AmbientFloorEnergy { get; set; } =
        1.0f;

    public static void InitSession(RealClientAssets? assets)
    {
        _sessionRamp = LoadToonRamp(assets);
    }

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

    public static ShaderMaterial Build(ImageTexture? albedo)
    {
        return Build(albedo, _sessionRamp);
    }

    public static ShaderMaterial Build(ImageTexture? albedo, ImageTexture? toonRamp)
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
        mat.SetShaderParameter("ambient_floor_color",
            new Color(1f, 1f, 1f));

        mat.SetShaderParameter("light_dir", DefaultLightDir);
        mat.SetShaderParameter("light_color", new Color(1f, 1f, 1f));

        mat.SetShaderParameter("light2_dir", DefaultLight2Dir);
        mat.SetShaderParameter("light2_color", new Color(0.5f, 0.5f, 0.6f));

        return mat;
    }
}