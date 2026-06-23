
using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class SkyDomeNode : Node3D
{

    private const int DomeKfCount = StarDomeBin.KeyframeCount;

    private const double DomeKfMs = 7200.0;

    private const double PeriodMs = 86_400.0;


    private const float DomeRadius = 13_000f;

    private const int DomeSectors = 16;

    private const int CloudDomeStacks = 15;

    private const int StarDomeStacks = 12;


    private const float OrbitScale = 3200f;

    private const float SunBillboardSize = 2048f;

    private const float SunTiltDeg = 45f;





    private const float StarFullNightKf = 8f;
    private const float StarFadeOutKf = 16f;
    private const float StarFadeInKf = 36f;
    private const float StarFullNightKf2 = 44f;


    private const float CloudUvRatePerSpeedUnit = 0.003f;

    private CloudCycleRow _activeCycleRow;
    private CloudCycleBin? _cloudCycle;
    private CloudDomeBin? _cloudDome;
    private MeshInstance3D? _cloudDomeMesh1;
    private MeshInstance3D? _cloudDomeMesh2;
    private ShaderMaterial? _cloudMaterial1;
    private ShaderMaterial? _cloudMaterial2;

    private MeshInstance3D? _moonBillboard;


    private StarDomeBin? _starDome;

    private MeshInstance3D? _starDomeMesh;

    private ShaderMaterial? _starMaterial;


    private MeshInstance3D? _sunBillboard;

    private DirectionalLight3D? _trackedDirLight;




    public void Build(StarDomeBin? starDome, CloudDomeBin? cloudDome, CloudCycleBin? cloudCycle,
        DirectionalLight3D? dirLight = null)
    {
        _starDome = starDome;
        _cloudDome = cloudDome;
        _cloudCycle = cloudCycle;
        _trackedDirLight = dirLight;

        _activeCycleRow = cloudCycle?.Rows[0] ?? default;

        BuildSunBillboard();

        if (starDome is not null)
            BuildStarDome();

        if (cloudDome is not null)
            BuildCloudDome();

        BuildMoonBillboard();

        var starStatus = starDome is not null ? "built" : "absent(no-op)";
        var cloudStatus = cloudDome is not null ? "built" : "absent(no-op)";
        var cycleStatus = cloudCycle is not null
            ? $"speed={_activeCycleRow.Speed} cloud1Id={_activeCycleRow.Cloud1Id0To12H}"
            : "absent";
        GD.Print($"[SkyDome] star={starStatus} cloud={cloudStatus} cloudCycle={cycleStatus} " +
                 $"sun=billboard moon=billboard radius={DomeRadius:F0}wu sectors={DomeSectors}. " +
                 "spec: Docs/RE/formats/sky.md §D (sun/moon billboard orbit).");
    }

    public void SetBillboardTextures(ImageTexture? sunTexture, ImageTexture? moonTexture)
    {
        if (sunTexture is not null
            && _sunBillboard?.MaterialOverride is ShaderMaterial sunMat)
        {
            sunMat.SetShaderParameter("albedo_tex", sunTexture);
            GD.Print("[SkyDome] sun.dds texture applied to SunBillboard. spec: Docs/RE/formats/sky.md §D.5");
        }

        if (moonTexture is not null
            && _moonBillboard?.MaterialOverride is ShaderMaterial moonMat)
        {
            moonMat.SetShaderParameter("albedo_tex", moonTexture);
            GD.Print("[SkyDome] moon{n}.dds texture applied to MoonBillboard (phase ORACLE-PENDING: default moon0). " +
                     "spec: Docs/RE/formats/sky.md §D.3");
        }
    }

    public void UpdateDomes(double clockMs, double delta)
    {
        var tWrapped = clockMs % PeriodMs;
        var domeKf = (int)(tWrapped / DomeKfMs) % DomeKfCount;
        var domeKfNext = (domeKf + 1) % DomeKfCount;
        var domeFrac = (float)(tWrapped % DomeKfMs / DomeKfMs);

        var mainKfFloat = (float)(tWrapped / 1800.0);

        UpdateStarDome(domeKf, domeKfNext, domeFrac, mainKfFloat);
        UpdateCloudDomes(domeKf, domeKfNext, domeFrac, mainKfFloat, (float)delta);

        UpdateBillboards(clockMs);
    }


    private void UpdateStarDome(int kf, int kfNext, float frac, float mainKf)
    {
        if (_starDomeMesh is null || _starMaterial is null || _starDome is null) return;

        var tintA = BgraToColor(_starDome.StarColors[kf][0]);
        var tintB = BgraToColor(_starDome.StarColors[kfNext][0]);
        var tint = tintA.Lerp(tintB, frac);

        var alpha = StarAlpha(mainKf);

        _starMaterial.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));

        _starDomeMesh.Visible = alpha > 0.01f;
    }

    private static float StarAlpha(float kf)
    {
        if (kf < StarFullNightKf) return 1f;
        if (kf < StarFadeOutKf) return 1f - (kf - StarFullNightKf) / (StarFadeOutKf - StarFullNightKf);
        if (kf < StarFadeInKf) return 0f;
        if (kf < StarFullNightKf2) return (kf - StarFadeInKf) / (StarFullNightKf2 - StarFadeInKf);
        return 1f;
    }


    private void UpdateCloudDomes(int kf, int kfNext, float frac, float mainKf, float delta)
    {
        if (_cloudDome is null) return;

        var cloudAlpha = 1f - StarAlpha(mainKf);


        UpdateCloudLayer(_cloudDomeMesh1, _cloudMaterial1, _cloudDome.Layer1Colors, kf, kfNext, frac, cloudAlpha);
        UpdateCloudLayer(_cloudDomeMesh2, _cloudMaterial2, _cloudDome.Layer2Colors, kf, kfNext, frac,
            cloudAlpha * 0.6f);
    }

    private void UpdateCloudLayer(
        MeshInstance3D? mesh,
        ShaderMaterial? mat,
        BgraColor[][] layerColors,
        int kf, int kfNext, float frac,
        float alpha)
    {
        if (mesh is null || mat is null) return;

        var tintA = BgraToColor(layerColors[kf][0]);
        var tintB = BgraToColor(layerColors[kfNext][0]);
        var tint = tintA.Lerp(tintB, frac);

        var tintLum = 0.2126f * tint.R + 0.7152f * tint.G + 0.0722f * tint.B;
        if (tintLum < 0.015f)
        {
            mesh.Visible = false;
            return;
        }

        mat.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));

        mesh.Visible = alpha > 0.01f;
    }


    private void BuildStarDome()
    {
        var mesh = BuildHemisphereMesh(DomeRadius, StarDomeStacks, DomeSectors, true);

        _starMaterial = BuildDomeMaterial(false);
        var mi = new MeshInstance3D
        {
            Name = "StarDome",
            Mesh = mesh,
            MaterialOverride = _starMaterial,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        mi.SetLayerMaskValue(1, true);
        mi.Layers = 1;
        mi.Visible = false;
        AddChild(mi);
        _starDomeMesh = mi;
    }

    private void BuildCloudDome()
    {

        var mesh1 = BuildHemisphereMesh(DomeRadius * 0.97f, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial1 = BuildDomeMaterial(true);
        var mi1 = new MeshInstance3D
        {
            Name = "CloudDomeInner",
            Mesh = mesh1,
            MaterialOverride = _cloudMaterial1,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = false
        };
        AddChild(mi1);
        _cloudDomeMesh1 = mi1;

        var mesh2 = BuildHemisphereMesh(DomeRadius, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial2 = BuildDomeMaterial(true);
        var mi2 = new MeshInstance3D
        {
            Name = "CloudDomeOuter",
            Mesh = mesh2,
            MaterialOverride = _cloudMaterial2,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = false
        };
        AddChild(mi2);
        _cloudDomeMesh2 = mi2;
    }

    private static ArrayMesh BuildHemisphereMesh(float radius, int stacks, int sectors, bool inverted)
    {

        var stride = sectors + 1;
        var vertCount = (stacks + 1) * stride;
        var indexCount = stacks * sectors * 6;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var indices = new int[indexCount];

        for (var stack = 0; stack <= stacks; stack++)
        {
            var theta = MathF.PI / 2f + stack * (MathF.PI / 2f) / stacks;
            var sinTheta = MathF.Sin(theta);
            var cosTheta = MathF.Cos(theta);

            for (var sec = 0; sec <= sectors; sec++)
            {
                var phi = sec * (2f * MathF.PI) / sectors;
                var sinPhi = MathF.Sin(phi);
                var cosPhi = MathF.Cos(phi);

                var x = radius * sinTheta * cosPhi;
                var y = radius * -cosTheta;
                var z = radius * sinTheta * sinPhi;

                Vector3 pos = new(x, y, z);
                var outward = pos.Normalized();
                var vi = stack * stride + sec;
                vertices[vi] = pos;
                normals[vi] = inverted ? -outward : outward;
                uvs[vi] = new Vector2((float)sec / sectors, (float)stack / stacks);
            }
        }

        var idx = 0;
        for (var stack = 0; stack < stacks; stack++)
        for (var sec = 0; sec < sectors; sec++)
        {
            var tl = stack * stride + sec;
            var tr = tl + 1;
            var bl = tl + stride;
            var br = bl + 1;

            if (inverted)
            {
                indices[idx++] = tl;
                indices[idx++] = bl;
                indices[idx++] = tr;
                indices[idx++] = tr;
                indices[idx++] = bl;
                indices[idx++] = br;
            }
            else
            {
                indices[idx++] = tl;
                indices[idx++] = tr;
                indices[idx++] = bl;
                indices[idx++] = tr;
                indices[idx++] = br;
                indices[idx++] = bl;
            }
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }


    private static ShaderMaterial BuildDomeMaterial(bool isCloud)
    {
        const string ShaderSrc =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_front;

            uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

            void fragment() {
                ALBEDO = albedo_color.rgb;
                ALPHA  = albedo_color.a;
            }
            """;

        var shader = new Shader();
        shader.Code = ShaderSrc;

        var mat = new ShaderMaterial();
        mat.Shader = shader;
        mat.RenderPriority = -128;
        mat.SetShaderParameter("albedo_color", new Color(1f, 1f, 1f));
        return mat;
    }


    private void BuildSunBillboard()
    {
        var half = SunBillboardSize / 2f;
        Vector3[] verts =
        [
            new(-half, -half, 0f),
            new(half, -half, 0f),
            new(half, half, 0f),
            new(-half, half, 0f)
        ];
        Vector2[] uvs =
        [
            new(0f, 1f),
            new(1f, 1f),
            new(1f, 0f),
            new(0f, 0f)
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        const string BillboardShader =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_disabled;

            uniform vec4 albedo_color : source_color = vec4(1.0, 0.95, 0.7, 1.0);
            uniform sampler2D albedo_tex : source_color, hint_default_white;

            void fragment() {
                vec4 tex = texture(albedo_tex, UV);
                ALBEDO = albedo_color.rgb * tex.rgb;
                ALPHA  = albedo_color.a * tex.a;
            }
            """;

        var shader = new Shader { Code = BillboardShader };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("albedo_color", new Color(1f, 0.95f, 0.7f));
        mat.RenderPriority = -64;

        var mi = new MeshInstance3D
        {
            Name = "SunBillboard",
            Mesh = mesh,
            MaterialOverride = mat,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = true
        };
        AddChild(mi);
        _sunBillboard = mi;

        GD.Print("[SkyDome] SunBillboard constructed. spec: Docs/RE/formats/sky.md §D.5 (sun billboard size 2048.0).");
    }

    private void BuildMoonBillboard()
    {
        const float MoonSize = SunBillboardSize / 2f;
        var half = MoonSize / 2f;

        Vector3[] verts =
        [
            new(-half, -half, 0f),
            new(half, -half, 0f),
            new(half, half, 0f),
            new(-half, half, 0f)
        ];
        Vector2[] uvs =
        [
            new(0f, 1f),
            new(1f, 1f),
            new(1f, 0f),
            new(0f, 0f)
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        const string BillboardShader =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_disabled;

            uniform vec4 albedo_color : source_color = vec4(0.85, 0.9, 1.0, 1.0);
            uniform sampler2D albedo_tex : source_color, hint_default_white;

            void fragment() {
                vec4 tex = texture(albedo_tex, UV);
                ALBEDO = albedo_color.rgb * tex.rgb;
                ALPHA  = albedo_color.a * tex.a;
            }
            """;

        var shader = new Shader { Code = BillboardShader };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("albedo_color", new Color(0.85f, 0.9f, 1f));
        mat.RenderPriority = -64;

        var mi = new MeshInstance3D
        {
            Name = "MoonBillboard",
            Mesh = mesh,
            MaterialOverride = mat,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = true
        };
        AddChild(mi);
        _moonBillboard = mi;

        GD.Print("[SkyDome] MoonBillboard constructed (phase texture oracle-pending). " +
                 "spec: Docs/RE/formats/sky.md §D.2 (flat circle orbit, ±3200 scale).");
    }


    private void UpdateBillboards(double clockMs)
    {
        var secondsOfDay = clockMs / 1000.0 % 86400.0;
        var angleRad = secondsOfDay * (2.0 * Math.PI) / 86400.0;

        var sinA = Math.Sin(angleRad);
        var cosA = Math.Cos(angleRad);

        var cos45 = Math.Cos(SunTiltDeg * (Math.PI / 180.0));
        var sin45 = Math.Sin(SunTiltDeg * (Math.PI / 180.0));

        var sunXLegacy = sinA * -OrbitScale;
        var sunYLegacy = cosA * -OrbitScale * cos45;
        var sunZLegacy = sunYLegacy * sin45;

        var (sgx, sgy, sgz) = WorldCoordinates.ToGodot((float)sunXLegacy, (float)sunYLegacy, (float)sunZLegacy);
        var sunPos = new Vector3(sgx, sgy, sgz);

        var moonXLegacy = sinA * OrbitScale;
        var moonYLegacy = cosA * OrbitScale;

        var (mgx, mgy, mgz) = WorldCoordinates.ToGodot((float)moonXLegacy, (float)moonYLegacy, 0f);
        var moonPos = new Vector3(mgx, mgy, mgz);

        if (_sunBillboard is not null)
            _sunBillboard.Position = sunPos;

        if (_moonBillboard is not null)
            _moonBillboard.Position = moonPos;

        if (_trackedDirLight is not null && sunPos.LengthSquared() > 1e-6f)
        {
            var lightDir = -sunPos.Normalized();
            var up = Vector3.Up;
            if (Math.Abs(lightDir.Dot(up)) > 0.999f)
                up = Vector3.Right;
            var right = lightDir.Cross(up).Normalized();
            var newUp = right.Cross(lightDir).Normalized();
            _trackedDirLight.Basis = new Basis(right, newUp, -lightDir);
        }
    }


    private static Color BgraToColor(BgraColor c)
    {
        return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
    }
}