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

    private const int CloudRings = 4;

    private const int CloudSegments = 12;

    private const float CloudRingRadiusStep = 750f;

    private const float CloudRingHeightStep = 333.3f;

    private const float CloudSkirtHeight = -200f;

    private const float CloudUvSpan = 0.25f;

    private const int CloudVertexCount = CloudDomeBin.VerticesPerKeyframe;

    private const int StarCount = StarDomeBin.StarsPerKeyframe;

    private const int StarDomeRings = 6;

    private const int StarDomeSegments = 12;

    private const int StarDomeVertexCount = StarDomeRings * StarDomeSegments;

    private const float StarDomeVerticalRatio = 3500f / 4000f;

    private const float StarDomeRadius = DomeRadius * 0.99f;

    private const float StarVisibilityCutoff = 0.1f;

    private const float OrbitScale = 3200f;

    private const float SunBillboardSize = 2048f;

    private const float SunTiltDeg = 45f;

    private static readonly StringName CloudUvScrollParam = "uv_scroll";
    private static readonly StringName CloudTexAParam = "cloud_tex_a";
    private static readonly StringName CloudTexBParam = "cloud_tex_b";
    private static readonly StringName CloudBlendParam = "cloud_blend";

    private CloudCycleRow _activeCycleRow;
    private CloudCycleBin? _cloudCycle;
    private CloudDomeBin? _cloudDome;
    private MeshInstance3D? _cloudDomeMesh1;
    private MeshInstance3D? _cloudDomeMesh2;
    private ArrayMesh? _cloudMesh1;
    private ArrayMesh? _cloudMesh2;
    private Array? _cloud1BaseArrays;
    private Array? _cloud2BaseArrays;
    private int[]? _cloud1ColorIndex;
    private int[]? _cloud2ColorIndex;
    private Color[]? _cloud1ColorBuffer;
    private Color[]? _cloud2ColorBuffer;
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
    private ArrayMesh? _starMesh;
    private MeshInstance3D? _starDomeMesh;
    private Array? _starBaseArrays;
    private int[]? _starColorIndex;
    private Color[]? _starColorBuffer;
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
            BuildStarDome();

        if (cloudDome is not null)
            BuildCloudDome();

        BuildMoonBillboard();

        var starStatus = starDome is not null
            ? $"built({StarDomeVertexCount}-vtx opaque tinted dome, {StarDomeRings}x{StarDomeSegments})"
            : "absent(no-op)";
        var cloudStatus = cloudDome is not null ? "built(textured scroll)" : "absent(no-op)";
        var cycleStatus = cloudCycle is not null
            ? $"speed={_activeCycleRow.Speed} cloud1Id={_activeCycleRow.Cloud1Id0To12H}"
            : "absent";
        GD.Print($"[SkyDome] star={starStatus} cloud={cloudStatus} cloudCycle={cycleStatus} " +
                 $"sun=billboard moon=billboard starRadius={DomeRadius:F0}wu cloud=flat-zenith-cap({CloudVertexCount}v). " +
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

        UpdateStarDome(slot, slotNext, frac);
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

    private void UpdateStarDome(int slot, int slotNext, float frac)
    {
        if (_starMesh is null || _starDome is null || _starDomeMesh is null
            || _starBaseArrays is null || _starColorIndex is null || _starColorBuffer is null) return;

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

        if (brightness < StarVisibilityCutoff)
        {
            _starDomeMesh.Visible = false;
            return;
        }

        _starDomeMesh.Visible = true;

        var colorsA = _starDome.StarColors[Math.Clamp(slot, 0, _starDome.StarColors.Length - 1)];
        var colorsB = _starDome.StarColors[Math.Clamp(slotNext, 0, _starDome.StarColors.Length - 1)];

        for (var v = 0; v < StarDomeVertexCount; v++)
        {
            var i = _starColorIndex[v];
            var ca = colorsA[Math.Clamp(i, 0, colorsA.Length - 1)];
            var cb = colorsB[Math.Clamp(i, 0, colorsB.Length - 1)];
            var r = (ca.R + (cb.R - ca.R) * frac) / 255f * brightness;
            var gv = (ca.G + (cb.G - ca.G) * frac) / 255f * brightness;
            var bv = (ca.B + (cb.B - ca.B) * frac) / 255f * brightness;
            _starColorBuffer[v] = new Color(r, gv, bv, 1f);
        }

        _starBaseArrays[(int)Mesh.ArrayType.Color] = _starColorBuffer;
        _starMesh.ClearSurfaces();
        _starMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _starBaseArrays);
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

        UpdateCloudLayer(_cloudMaterial1, _cloudMesh1, _cloud1BaseArrays, _cloud1ColorIndex,
            _cloud1ColorBuffer, _cloudDome.Layer1Colors, slot, slotNext, frac, offA);
        UpdateCloudLayer(_cloudMaterial2, _cloudMesh2, _cloud2BaseArrays, _cloud2ColorIndex,
            _cloud2ColorBuffer, _cloudDome.Layer2Colors, slot, slotNext, frac, offB);
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
        ArrayMesh? mesh,
        Array? baseArrays,
        int[]? colorIndex,
        Color[]? colorBuffer,
        BgraColor[][] layerColors,
        int slot, int slotNext, float frac,
        float scrollV)
    {
        if (mat is null || mesh is null || baseArrays is null || colorIndex is null || colorBuffer is null) return;

        var a = layerColors[Math.Clamp(slot, 0, layerColors.Length - 1)];
        var b = layerColors[Math.Clamp(slotNext, 0, layerColors.Length - 1)];

        for (var v = 0; v < colorBuffer.Length; v++)
        {
            var gi = colorIndex[v];
            var ca = a[Math.Clamp(gi, 0, a.Length - 1)];
            var cb = b[Math.Clamp(gi, 0, b.Length - 1)];
            var r = (ca.R + (cb.R - ca.R) * frac) / 255f;
            var gv = (ca.G + (cb.G - ca.G) * frac) / 255f;
            var bv = (ca.B + (cb.B - ca.B) * frac) / 255f;
            var av = (ca.A + (cb.A - ca.A) * frac) / 255f;
            colorBuffer[v] = new Color(r, gv, bv, av);
        }

        baseArrays[(int)Mesh.ArrayType.Color] = colorBuffer;
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, baseArrays);

        mat.SetShaderParameter(CloudUvScrollParam, new Vector2(0f, scrollV));
    }

    private void BuildStarDome()
    {
        _starMaterial = BuildStarMaterial();

        const float hRadius = StarDomeRadius;
        const float vRadius = StarDomeRadius * StarDomeVerticalRatio;

        var vertices = new Vector3[StarDomeVertexCount];
        var uvs = new Vector2[StarDomeVertexCount];
        _starColorIndex = new int[StarDomeVertexCount];
        _starColorBuffer = new Color[StarDomeVertexCount];

        for (var ring = 0; ring < StarDomeRings; ring++)
        {
            var elev = ring * 30f * (MathF.PI / 180f);
            var sinE = MathF.Sin(elev);
            var cosE = MathF.Cos(elev);
            for (var seg = 0; seg < StarDomeSegments; seg++)
            {
                var segAngle = seg * 30f * (MathF.PI / 180f);
                var cosS = MathF.Cos(segAngle);
                var sinS = MathF.Sin(segAngle);
                var vi = ring * StarDomeSegments + seg;
                vertices[vi] = new Vector3(hRadius * sinE * cosS, vRadius * cosE, hRadius * sinE * sinS);
                uvs[vi] = new Vector2(cosS * (ring / 6f) + 8f, sinS * (ring / 6f) + 8f);
                _starColorIndex[vi] = vi < StarCount ? vi : vi - StarCount;
                _starColorBuffer[vi] = new Color(0f, 0f, 0f, 1f);
            }
        }

        var indices = new int[(StarDomeRings - 1) * StarDomeSegments * 6];
        var idx = 0;
        for (var ring = 0; ring < StarDomeRings - 1; ring++)
        for (var seg = 0; seg < StarDomeSegments; seg++)
        {
            var a = ring * StarDomeSegments + seg;
            var b = ring * StarDomeSegments + (seg + 1) % StarDomeSegments;
            var c = (ring + 1) * StarDomeSegments + seg;
            var d = (ring + 1) * StarDomeSegments + (seg + 1) % StarDomeSegments;
            indices[idx++] = a;
            indices[idx++] = b;
            indices[idx++] = c;
            indices[idx++] = b;
            indices[idx++] = d;
            indices[idx++] = c;
        }

        _starBaseArrays = new Array();
        _starBaseArrays.Resize((int)Mesh.ArrayType.Max);
        _starBaseArrays[(int)Mesh.ArrayType.Vertex] = vertices;
        _starBaseArrays[(int)Mesh.ArrayType.TexUV] = uvs;
        _starBaseArrays[(int)Mesh.ArrayType.Color] = _starColorBuffer;
        _starBaseArrays[(int)Mesh.ArrayType.Index] = indices;

        _starMesh = new ArrayMesh();
        _starMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _starBaseArrays);

        _starDomeMesh = new MeshInstance3D
        {
            Name = "StarDome",
            Mesh = _starMesh,
            MaterialOverride = _starMaterial,
            ExtraCullMargin = StarDomeRadius,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
        };
        AddChild(_starDomeMesh);

        GD.Print(
            $"[SkyDome] StarDome built: {StarDomeVertexCount}-vertex OPAQUE textured triangle mesh " +
            $"({StarDomeRings} rings x {StarDomeSegments} segments = {(StarDomeRings - 1) * StarDomeSegments * 2} tris, " +
            $"radii {hRadius:F0}:{vRadius:F0} preserving 4000:3500 ratio), fisheye WRAP UVs (u=cos*ring/6+8, v=sin*ring/6+8), " +
            $"per-vertex BGR tint on {StarCount} primary + {StarDomeVertexCount - StarCount} derived copies, " +
            $"global brightness as vertex-colour scale with {StarVisibilityCutoff} hide cutoff. " +
            "spec: Docs/RE/structs/skybox.md §6.1 (was 48 additive point sprites).");
    }

    private void BuildCloudDome()
    {
        _cloudMesh1 = BuildCloudFlatDome(0.997f,
            out _cloud1BaseArrays, out _cloud1ColorIndex, out _cloud1ColorBuffer);
        _cloudMaterial1 = BuildCloudMaterial();
        _cloudDomeMesh1 = new MeshInstance3D
        {
            Name = "CloudDomeInner",
            Mesh = _cloudMesh1,
            MaterialOverride = _cloudMaterial1,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
        };
        AddChild(_cloudDomeMesh1);

        _cloudMesh2 = BuildCloudFlatDome(1.0f,
            out _cloud2BaseArrays, out _cloud2ColorIndex, out _cloud2ColorBuffer);
        _cloudMaterial2 = BuildCloudMaterial();
        _cloudDomeMesh2 = new MeshInstance3D
        {
            Name = "CloudDomeOuter",
            Mesh = _cloudMesh2,
            MaterialOverride = _cloudMaterial2,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1
        };
        AddChild(_cloudDomeMesh2);

        GD.Print(
            $"[SkyDome] cloud domes REBUILT as flat zenith caps: each layer {CloudVertexCount}v = {CloudRings} rings x {CloudSegments} " +
            $"(ring radius=ringIndex*{CloudRingRadiusStep:F0}, height=(3-ringIndex)*{CloudRingHeightStep:F1}) + {CloudSegments}-vtx skirt at h={CloudSkirtHeight:F0}; " +
            $"horizRadius~{(CloudRings - 1) * CloudRingRadiusStep:F0} height~{(CloudRings - 1) * CloudRingHeightStep:F0}; " +
            $"the {CloudVertexCount}-entry BGRA day-tint grid now maps 1:1 (colorIndex[v]=v, was a lossy stack->row/sector->col grid on a UV-sphere). " +
            "Fisheye UV centred (0.5,0.25), only V scrolled; V-scroll + ping-pong + per-vertex tint + colour-key preserved (geometry-independent). " +
            "ENGINEERING NOTE: the fisheye radial UV span (0.25) is not pinned by spec (only the centre is) — chosen so the 4 rings fan out from the centre. " +
            "spec: Docs/RE/structs/skybox.md §7 (60-vtx dome: 4 rings x 12 + 12 skirt) / formats/sky.md §F.");
    }

    private static ArrayMesh BuildCloudFlatDome(float radiusScale,
        out Array baseArrays, out int[] colorIndex, out Color[] colorBuffer)
    {
        const int rings = CloudRings + 1;
        const int segs = CloudSegments;
        const int vertCount = CloudVertexCount;
        const int indexCount = CloudRings * segs * 6;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var indices = new int[indexCount];
        colorIndex = new int[vertCount];
        colorBuffer = new Color[vertCount];

        for (var ring = 0; ring < rings; ring++)
        {
            var dome = ring < CloudRings;
            var radius = (dome ? ring : CloudRings - 1) * CloudRingRadiusStep * radiusScale;
            var height = (dome ? (CloudRings - 1 - ring) * CloudRingHeightStep : CloudSkirtHeight) * radiusScale;
            var norm = dome ? (float)ring / (CloudRings - 1) : 1f;

            for (var seg = 0; seg < segs; seg++)
            {
                var angle = seg * (2f * MathF.PI / segs);
                var cosA = MathF.Cos(angle);
                var sinA = MathF.Sin(angle);

                var vi = ring * segs + seg;
                Vector3 pos = new(radius * cosA, height, radius * sinA);
                vertices[vi] = pos;
                normals[vi] = pos.LengthSquared() > 1e-6f ? -pos.Normalized() : Vector3.Down;
                uvs[vi] = new Vector2(0.5f + cosA * norm * CloudUvSpan, 0.25f + sinA * norm * CloudUvSpan);
                colorIndex[vi] = vi;
                colorBuffer[vi] = new Color(1f, 1f, 1f, 1f);
            }
        }

        var idx = 0;
        for (var ring = 0; ring < CloudRings; ring++)
        for (var seg = 0; seg < segs; seg++)
        {
            var next = (seg + 1) % segs;
            var tl = (ring + 1) * segs + seg;
            var tr = (ring + 1) * segs + next;
            var bl = ring * segs + seg;
            var br = ring * segs + next;
            indices[idx++] = tl;
            indices[idx++] = bl;
            indices[idx++] = tr;
            indices[idx++] = tr;
            indices[idx++] = bl;
            indices[idx++] = br;
        }

        baseArrays = new Array();
        baseArrays.Resize((int)Mesh.ArrayType.Max);
        baseArrays[(int)Mesh.ArrayType.Vertex] = vertices;
        baseArrays[(int)Mesh.ArrayType.Normal] = normals;
        baseArrays[(int)Mesh.ArrayType.TexUV] = uvs;
        baseArrays[(int)Mesh.ArrayType.Color] = colorBuffer;
        baseArrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, baseArrays);
        return mesh;
    }

    private static ShaderMaterial BuildStarMaterial()
    {
        const string ShaderSrc =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled;

            uniform sampler2D star_tex : source_color, repeat_enable, hint_default_white;

            void fragment() {
                vec4 t = texture(star_tex, UV);
                ALBEDO = t.rgb * COLOR.rgb;
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
            uniform vec2 uv_scroll = vec2(0.0, 0.0);

            void fragment() {
                vec2 uv = UV + uv_scroll;
                vec4 ta = texture(cloud_tex_a, uv);
                vec4 tb = texture(cloud_tex_b, uv);
                vec4 t = mix(ta, tb, clamp(cloud_blend, 0.0, 1.0));
                float key = smoothstep(0.0, 0.05, max(t.r, max(t.g, t.b)));
                ALBEDO = t.rgb * COLOR.rgb;
                ALPHA  = t.a * COLOR.a * key;
            }
            """;

        var shader = new Shader { Code = ShaderSrc };
        var mat = new ShaderMaterial { Shader = shader };
        mat.RenderPriority = -127;
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
            render_mode unshaded, fog_disabled, blend_add, depth_draw_never, cull_disabled;

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
            render_mode unshaded, fog_disabled, blend_add, depth_draw_never, cull_disabled;

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

}