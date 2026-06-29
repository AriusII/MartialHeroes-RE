using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class SkyDomeNode : Node3D
{
    private const int SlotCount = StarDomeBin.KeyframeCount;

    private const double SlotMs = 1800.0;

    private const double PeriodMs = SlotCount * SlotMs;

    private const double CloudHalfDaySec = 43_200.0;

    private const double CloudQuarterDaySec = 21_600.0;

    private const float DomeRadius = 13_000f;

    private const int DomeSectors = 16;

    private const int CloudDomeStacks = 15;

    private const int StarCount = StarDomeBin.StarsPerKeyframe;

    private const float StarDomeRadius = DomeRadius * 0.99f;

    private const float StarSpriteSize = 240f;

    private const float StarVisibilityCutoff = 0.1f;

    private const float OrbitScale = 3200f;

    private const float SunBillboardSize = 2048f;

    private const float SunTiltDeg = 45f;

    private static readonly StringName CloudTintParam = "tint_color";
    private static readonly StringName CloudOpacityParam = "opacity";
    private static readonly StringName CloudUvScrollParam = "uv_scroll";
    private static readonly StringName CloudTexAParam = "cloud_tex_a";
    private static readonly StringName CloudTexBParam = "cloud_tex_b";
    private static readonly StringName CloudBlendParam = "cloud_blend";

    private CloudCycleRow _activeCycleRow;
    private CloudCycleBin? _cloudCycle;
    private CloudDomeBin? _cloudDome;
    private MeshInstance3D? _cloudDomeMesh1;
    private MeshInstance3D? _cloudDomeMesh2;
    private ShaderMaterial? _cloudMaterial1;
    private ShaderMaterial? _cloudMaterial2;

    private Func<int, ImageTexture?>? _cloudResolver;
    private int _cloudRow;
    private ImageTexture? _cloudTexture;
    private ImageTexture? _layer1Current;
    private long _layer1LastF = long.MinValue;
    private ImageTexture? _layer1Next;
    private ImageTexture? _layer2Current;
    private long _layer2LastF = long.MinValue;
    private ImageTexture? _layer2Next;

    private MeshInstance3D? _moonBillboard;
    private bool _moonVisible = true;

    private float[]? _starBrightness;

    private StarDomeBin? _starDome;
    private ShaderMaterial? _starMaterial;
    private MultiMesh? _starMultiMesh;
    private MultiMeshInstance3D? _starPoints;
    private ImageTexture? _starTexture;
    private MeshInstance3D? _sunBillboard;
    private bool _sunVisible = true;

    private DirectionalLight3D? _trackedDirLight;

    public Vector3 SunGlobalPosition => _sunBillboard is not null ? _sunBillboard.GlobalPosition : Vector3.Zero;

    public bool SunVisible => _sunVisible && _sunBillboard is not null;

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
            BuildStarPoints();

        if (cloudDome is not null)
            BuildCloudDome();

        BuildMoonBillboard();

        var starStatus = starDome is not null ? $"built({StarCount} point sprites)" : "absent(no-op)";
        var cloudStatus = cloudDome is not null ? "built(textured scroll)" : "absent(no-op)";
        var cycleStatus = cloudCycle is not null
            ? $"speed={_activeCycleRow.Speed} cloud1Id={_activeCycleRow.Cloud1Id0To12H}"
            : "absent";
        GD.Print($"[SkyDome] star={starStatus} cloud={cloudStatus} cloudCycle={cycleStatus} " +
                 $"sun=billboard moon=billboard radius={DomeRadius:F0}wu sectors={DomeSectors}. " +
                 "spec: Docs/RE/formats/sky.md D (sun/moon billboard orbit), C (48-slot 1800ms cadence).");
    }

    public void SetBillboardTextures(ImageTexture? sunTexture, ImageTexture? moonTexture)
    {
        if (sunTexture is not null
            && _sunBillboard?.MaterialOverride is ShaderMaterial sunMat)
        {
            sunMat.SetShaderParameter("albedo_tex", sunTexture);
            GD.Print("[SkyDome] sun.dds texture applied to SunBillboard. spec: Docs/RE/formats/sky.md D.5");
        }

        if (moonTexture is not null
            && _moonBillboard?.MaterialOverride is ShaderMaterial moonMat)
        {
            moonMat.SetShaderParameter("albedo_tex", moonTexture);
            GD.Print(
                "[SkyDome] moon{n}.dds texture applied to MoonBillboard (phase fixed at moon0; no day counter in this node). " +
                "spec: Docs/RE/formats/sky.md D.3");
        }
    }

    public void SetBillboardVisibility(bool sun, bool moon)
    {
        _sunVisible = sun;
        _moonVisible = moon;
        if (_sunBillboard is not null) _sunBillboard.Visible = sun;
        if (_moonBillboard is not null) _moonBillboard.Visible = moon;
        GD.Print(
            $"[SkyDome] billboard visibility set sun={sun} moon={moon}. spec: Docs/RE/formats/environment_bins.md 1.1 (SUN/MOON gates).");
    }

    public void SetSkyTextures(ImageTexture? star, ImageTexture? cloud)
    {
        if (star is not null)
        {
            _starTexture = star;
            _starMaterial?.SetShaderParameter("star_tex", star);
            GD.Print(
                "[SkyDome] star.dds texture applied to star point sprites. spec: Docs/RE/formats/sky.md B.1a / 4.3.");
        }

        if (cloud is not null)
        {
            _cloudTexture = cloud;
            _cloudMaterial1?.SetShaderParameter(CloudTexAParam, cloud);
            _cloudMaterial1?.SetShaderParameter(CloudTexBParam, cloud);
            _cloudMaterial2?.SetShaderParameter(CloudTexAParam, cloud);
            _cloudMaterial2?.SetShaderParameter(CloudTexBParam, cloud);
            GD.Print(
                "[SkyDome] cloud fallback texture applied to both layers (overridden per-tick by the ping-pong resolver). spec: Docs/RE/formats/sky.md B.3 / F (UV scroll).");
        }
    }

    public void SetCloudCycle(CloudCycleBin? cycle, Func<int, ImageTexture?>? cloudResolver, int dateBlock)
    {
        _cloudCycle = cycle;
        _cloudResolver = cloudResolver;
        _cloudRow = cycle is not null
            ? (dateBlock % CloudCycleBin.RowCount + CloudCycleBin.RowCount) % CloudCycleBin.RowCount
            : 0;
        _activeCycleRow = cycle?.Rows[_cloudRow] ?? default;
        _layer1LastF = long.MinValue;
        _layer2LastF = long.MinValue;
        _layer1Current = null;
        _layer1Next = null;
        _layer2Current = null;
        _layer2Next = null;
        GD.Print($"[SkyDome] cloud ping-pong wired: cycle={(cycle is not null ? "present" : "absent")} " +
                 $"resolver={(cloudResolver is not null ? "set" : "null")} dateBlock={dateBlock}(row={_cloudRow}; " +
                 "default 0 — in-game date counter not recovered). " +
                 "spec: Docs/RE/formats/sky.md F.2 (cloud_cycle ping-pong) / environment_bins.md 6.");
    }

    public void SetStarBrightnessCurve(float[]? curve)
    {
        _starBrightness = curve;
        GD.Print(curve is not null
            ? $"[SkyDome] star brightness curve set ({curve.Length} slots, light.bin +0x1320). " +
              "spec: Docs/RE/formats/sky.md E.2 (star TEXTUREFACTOR fade)."
            : "[SkyDome] star brightness curve null — caller expected to pass LightBin.DefaultStarBrightnessCurve(). " +
              "spec: Docs/RE/formats/sky.md E.2.");
    }

    public void UpdateDomes(double clockMs, double delta)
    {
        FollowCamera();

        var tWrapped = clockMs % PeriodMs;
        var slot = (int)(tWrapped / SlotMs) % SlotCount;
        var slotNext = (slot + 1) % SlotCount;
        var frac = (float)(tWrapped % SlotMs / SlotMs);

        UpdateStarPoints(slot, slotNext, frac);
        UpdateCloudDomes(slot, slotNext, frac, clockMs);

        UpdateBillboards(clockMs);
    }

    private void FollowCamera()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam is null) return;
        var eye = cam.GlobalPosition;
        Position = new Vector3(eye.X, 0f, eye.Z);
    }

    private void UpdateStarPoints(int slot, int slotNext, float frac)
    {
        if (_starMultiMesh is null || _starDome is null) return;

        var curve = _starBrightness;
        float brightness;
        if (curve is not null && curve.Length >= SlotCount)
        {
            var ba = curve[slot];
            var bb = curve[slotNext];
            brightness = ba + (bb - ba) * frac;
        }
        else
        {
            brightness = 1f;
        }

        var visible = brightness >= StarVisibilityCutoff;
        var alpha = visible ? 1f : 0f;

        var colorsA = _starDome.StarColors[Math.Clamp(slot, 0, _starDome.StarColors.Length - 1)];
        var colorsB = _starDome.StarColors[Math.Clamp(slotNext, 0, _starDome.StarColors.Length - 1)];

        for (var i = 0; i < StarCount; i++)
        {
            var ca = colorsA[Math.Clamp(i, 0, colorsA.Length - 1)];
            var cb = colorsB[Math.Clamp(i, 0, colorsB.Length - 1)];
            var r = (ca.R + (cb.R - ca.R) * frac) / 255f * brightness;
            var gv = (ca.G + (cb.G - ca.G) * frac) / 255f * brightness;
            var bv = (ca.B + (cb.B - ca.B) * frac) / 255f * brightness;
            _starMultiMesh.SetInstanceColor(i, new Color(r, gv, bv, alpha));
        }
    }

    private void UpdateCloudDomes(int slot, int slotNext, float frac, double clockMs)
    {
        if (_cloudDome is null) return;

        var tod = clockMs;
        float speed = _activeCycleRow.Speed;

        var offA = (float)(tod * speed % CloudHalfDaySec * 0.5 / CloudHalfDaySec);
        var offB = offA * 2f;
        if (offB >= 0.5f) offB -= 0.5f;

        UpdateCloudPingPong(tod, speed);

        UpdateCloudLayer(_cloudMaterial1, _cloudDome.Layer1Colors, slot, slotNext, frac, offA);
        UpdateCloudLayer(_cloudMaterial2, _cloudDome.Layer2Colors, slot, slotNext, frac, offB);
    }

    private void UpdateCloudPingPong(double tod, float speed)
    {
        if (_cloudCycle is null || _cloudResolver is null) return;

        var s = (int)speed;

        var blend1 = speed > 0f ? (float)(tod * speed % CloudHalfDaySec * 0.5 / CloudHalfDaySec) : 0f;
        var blend2 = blend1 * 2f;
        if (blend2 >= 0.5f) blend2 -= 0.5f;

        var f1 = (long)Math.Floor(tod / CloudHalfDaySec * speed);
        if (f1 != _layer1LastF)
        {
            var col = 2 * s > f1 + 1 ? (int)((f1 + 1) % 2) : 0;
            var selRow = 2 * s > f1 + 1 ? _cloudRow : (_cloudRow + 1) % CloudCycleBin.RowCount;
            var id = CloudColumn(selRow, 1 + col);
            var tex = _cloudResolver(id);
            if (tex is not null)
            {
                _layer1Current = _layer1Next ?? tex;
                _layer1Next = tex;
                _cloudMaterial1?.SetShaderParameter(CloudTexAParam, _layer1Current);
                _cloudMaterial1?.SetShaderParameter(CloudTexBParam, _layer1Next);
            }

            _layer1LastF = f1;
        }

        _cloudMaterial1?.SetShaderParameter(CloudBlendParam, Math.Clamp(blend1 / 0.5f, 0f, 1f));

        var f2 = (long)Math.Floor(tod / CloudQuarterDaySec * speed);
        if (f2 != _layer2LastF)
        {
            var col = 4 * s > f2 + 1 ? (int)((f2 + 1) % 4) : 0;
            var selRow = 4 * s > f2 + 1 ? _cloudRow : (_cloudRow + 1) % CloudCycleBin.RowCount;
            var id = CloudColumn(selRow, 3 + col);
            var tex = _cloudResolver(id);
            if (tex is not null)
            {
                _layer2Current = _layer2Next ?? tex;
                _layer2Next = tex;
                _cloudMaterial2?.SetShaderParameter(CloudTexAParam, _layer2Current);
                _cloudMaterial2?.SetShaderParameter(CloudTexBParam, _layer2Next);
            }

            _layer2LastF = f2;
        }

        _cloudMaterial2?.SetShaderParameter(CloudBlendParam, Math.Clamp(blend2 / 0.5f, 0f, 1f));
    }

    private int CloudColumn(int row, int col)
    {
        var r = _cloudCycle!.Rows[row];
        return col switch
        {
            0 => r.Speed,
            1 => r.Cloud1Id0To12H,
            2 => r.Cloud1Id12To24H,
            3 => r.Cloud2Id0To6H,
            4 => r.Cloud2Id6To12H,
            5 => r.Cloud2Id12To18H,
            6 => r.Cloud2Id18To24H,
            _ => r.Speed
        };
    }

    private static void UpdateCloudLayer(
        ShaderMaterial? mat,
        BgraColor[][] layerColors,
        int slot, int slotNext, float frac,
        float scrollV)
    {
        if (mat is null) return;

        var a = AverageTint(layerColors[slot]);
        var b = AverageTint(layerColors[slotNext]);
        var tint = a.Lerp(b, frac);

        var opacity = 0.2126f * tint.R + 0.7152f * tint.G + 0.0722f * tint.B;

        mat.SetShaderParameter(CloudTintParam, new Color(tint.R, tint.G, tint.B));
        mat.SetShaderParameter(CloudOpacityParam, opacity);
        mat.SetShaderParameter(CloudUvScrollParam, new Vector2(0f, scrollV));
    }

    private void BuildStarPoints()
    {
        _starMaterial = BuildStarMaterial();

        var quad = BuildStarQuadMesh(StarSpriteSize);

        _starMultiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true
        };
        _starMultiMesh.InstanceCount = StarCount;
        _starMultiMesh.Mesh = quad;

        var goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));
        for (var i = 0; i < StarCount; i++)
        {
            var y = (i + 0.5f) / StarCount;
            var r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
            var theta = i * goldenAngle;
            var x = MathF.Cos(theta) * r;
            var z = MathF.Sin(theta) * r;
            var pos = new Vector3(x, y, z) * StarDomeRadius;
            _starMultiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, pos));
            _starMultiMesh.SetInstanceColor(i, new Color(0f, 0f, 0f, 0f));
        }

        _starPoints = new MultiMeshInstance3D
        {
            Name = "StarPoints",
            Multimesh = _starMultiMesh,
            MaterialOverride = _starMaterial,
            ExtraCullMargin = StarDomeRadius,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
        };
        AddChild(_starPoints);

        GD.Print(
            $"[SkyDome] StarPoints built: {StarCount} textured point sprites (Fibonacci hemisphere placement is aesthetic; dome tessellation not spec-pinned). spec: Docs/RE/formats/sky.md 4.3.");
    }

    private void BuildCloudDome()
    {
        var mesh1 = BuildHemisphereMesh(DomeRadius * 0.97f, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial1 = BuildCloudMaterial();
        var mi1 = new MeshInstance3D
        {
            Name = "CloudDomeInner",
            Mesh = mesh1,
            MaterialOverride = _cloudMaterial1,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
        };
        AddChild(mi1);
        _cloudDomeMesh1 = mi1;

        var mesh2 = BuildHemisphereMesh(DomeRadius, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial2 = BuildCloudMaterial();
        var mi2 = new MeshInstance3D
        {
            Name = "CloudDomeOuter",
            Mesh = mesh2,
            MaterialOverride = _cloudMaterial2,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
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

    private static ArrayMesh BuildStarQuadMesh(float size)
    {
        var half = size / 2f;
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
        return mesh;
    }

    private static ShaderMaterial BuildStarMaterial()
    {
        const string ShaderSrc =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_add, depth_draw_never, cull_disabled;

            uniform sampler2D star_tex : source_color, hint_default_white;

            void vertex() {
                MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
                    INV_VIEW_MATRIX[0],
                    INV_VIEW_MATRIX[1],
                    INV_VIEW_MATRIX[2],
                    MODEL_MATRIX[3]);
            }

            void fragment() {
                vec4 t = texture(star_tex, UV);
                ALBEDO = t.rgb * COLOR.rgb;
                ALPHA  = t.a * COLOR.a;
            }
            """;

        var shader = new Shader { Code = ShaderSrc };
        var mat = new ShaderMaterial { Shader = shader };
        mat.RenderPriority = -128;
        return mat;
    }

    private static ShaderMaterial BuildCloudMaterial()
    {
        const string ShaderSrc =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_front;

            uniform sampler2D cloud_tex_a : source_color, hint_default_white;
            uniform sampler2D cloud_tex_b : source_color, hint_default_white;
            uniform float cloud_blend = 0.0;
            uniform vec4 tint_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
            uniform float opacity = 1.0;
            uniform vec2 uv_scroll = vec2(0.0, 0.0);

            void fragment() {
                vec2 uv = UV + uv_scroll;
                vec4 ta = texture(cloud_tex_a, uv);
                vec4 tb = texture(cloud_tex_b, uv);
                vec4 t = mix(ta, tb, clamp(cloud_blend, 0.0, 1.0));
                float key = smoothstep(0.0, 0.05, max(t.r, max(t.g, t.b)));
                ALBEDO = t.rgb * tint_color.rgb;
                ALPHA  = t.a * opacity * key;
            }
            """;

        var shader = new Shader { Code = ShaderSrc };
        var mat = new ShaderMaterial { Shader = shader };
        mat.RenderPriority = -127;
        mat.SetShaderParameter(CloudTintParam, new Color(1f, 1f, 1f));
        mat.SetShaderParameter(CloudOpacityParam, 0f);
        mat.SetShaderParameter(CloudUvScrollParam, new Vector2(0f, 0f));
        mat.SetShaderParameter(CloudBlendParam, 0f);
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

            void vertex() {
                MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
                    INV_VIEW_MATRIX[0],
                    INV_VIEW_MATRIX[1],
                    INV_VIEW_MATRIX[2],
                    MODEL_MATRIX[3]);
            }

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
            Visible = _sunVisible
        };
        AddChild(mi);
        _sunBillboard = mi;

        GD.Print("[SkyDome] SunBillboard constructed. spec: Docs/RE/formats/sky.md D.5 (sun billboard size 2048.0).");
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

            void vertex() {
                MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
                    INV_VIEW_MATRIX[0],
                    INV_VIEW_MATRIX[1],
                    INV_VIEW_MATRIX[2],
                    MODEL_MATRIX[3]);
            }

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
            Visible = _moonVisible
        };
        AddChild(mi);
        _moonBillboard = mi;

        GD.Print("[SkyDome] MoonBillboard constructed (phase fixed at moon0; no day counter). " +
                 "spec: Docs/RE/formats/sky.md D.2 (flat circle orbit, +/-3200 scale).");
    }

    private void UpdateBillboards(double clockMs)
    {
        var angleRad = clockMs / PeriodMs * (2.0 * Math.PI);

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

    private static Color AverageTint(BgraColor[] colors)
    {
        var rs = 0f;
        var gs = 0f;
        var bs = 0f;
        for (var i = 0; i < colors.Length; i++)
        {
            rs += colors[i].R;
            gs += colors[i].G;
            bs += colors[i].B;
        }

        var inv = 1f / (colors.Length * 255f);
        return new Color(rs * inv, gs * inv, bs * inv);
    }
}