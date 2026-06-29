using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode
{
    private LensFlareNode? _lensFlare;

    private System.Collections.Generic.Dictionary<int, ImageTexture?>? _cloudTexCache;
    private Func<int, ImageTexture?>? _cloudResolver;
    private int _dateBlock;

    private void ApplyKeyframe(int kf, float frac)
    {
        var kfNext = (kf + 1) % KeyframeCount;

        var env = _environment;
        if (env is null) return;

        ApplyBackground(env, kf, kfNext, frac);

        ApplyFog(env, kf, kfNext, frac);

        ApplyAmbient(env, kf, kfNext, frac);

        ApplyDirectional(kf, kfNext, frac);

        _appliedKeyframe = kf;
    }

    private void ApplyStaticPostSettings()
    {
        var env = _environment;
        if (env is null) return;

        env.TonemapMode = Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f;

        env.SsaoEnabled = false;
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;

        ApplyGlow(env);
    }

    private void ApplyBackground(Environment env, int kf, int kfNext, float frac)
    {
        env.BackgroundMode = Environment.BGMode.Color;

        var mat = _env?.Material;
        if (mat is not null && mat.ColorTable.Length == MaterialBin.KeyframeCount)
        {
            var a = MaterialSkyColor(mat.ColorTable[kf]);
            var b = MaterialSkyColor(mat.ColorTable[kfNext]);
            env.BackgroundColor = a.Lerp(b, frac);
            return;
        }

        if (!_bgFallbackLogged)
        {
            GD.Print(
                "[Environment] material sky LUT absent — background using engineering stand-in (0.45,0.55,0.70); NOT a spec value. " +
                "spec: Docs/RE/specs/environment.md §6.1 (material ambient_sky_color [29..31]).");
            _bgFallbackLogged = true;
        }

        env.BackgroundColor = new Color(0.45f, 0.55f, 0.70f);
    }

    private void ApplyFog(Environment env, int kf, int kfNext, float frac)
    {
        var fog = _env?.Fog;
        if (fog is null)
        {
            env.FogEnabled = false;
            return;
        }

        if (_env?.MapOption is { IndoorFlag: 1 })
        {
            env.FogEnabled = false;
            return;
        }

        env.FogEnabled = true;
        env.FogMode = Environment.FogModeEnum.Depth;

        var fogStart = fog.StartDist * _streamRadius;
        var fogEnd = fog.EndDist * _streamRadius;
        if (fogEnd <= fogStart) fogEnd = fogStart + 1f;

        env.FogDepthBegin = fogStart;
        env.FogDepthEnd = fogEnd;
        env.FogDepthCurve = 1.0f;

        env.FogLightColor = LerpFogColor(fog, kf, kfNext, frac);
        env.FogLightEnergy = 1.0f;
        env.FogSkyAffect = 0.0f;
    }

    private void ApplyAmbient(Environment env, int kf, int kfNext, float frac)
    {
        env.AmbientLightSource = Environment.AmbientSource.Color;

        var floor = OptionBrightFloor;
        var baseCol = AmbientBaseColor(kf, kfNext, frac);

        env.AmbientLightColor = new Color(
            Math.Min(1f, baseCol.R + floor),
            Math.Min(1f, baseCol.G + floor),
            Math.Min(1f, baseCol.B + floor));
        env.AmbientLightEnergy = 1.0f;
    }

    private Color AmbientBaseColor(int kf, int kfNext, float frac)
    {
        var e = _env?.Light?.DeviceAmbientKeyframes;
        if (e is null || e.Length < LightBin.KeyframeCount) return new Color(0f, 0f, 0f);

        var a = BgraToColor(e[Math.Clamp(kf, 0, e.Length - 1)]);
        var b = BgraToColor(e[Math.Clamp(kfNext, 0, e.Length - 1)]);
        return a.Lerp(b, frac);
    }

    private void ApplyGlow(Environment env)
    {
        env.GlowEnabled = true;
        env.GlowHdrThreshold = 0.0f;
        env.GlowBlendMode = Environment.GlowBlendModeEnum.Additive;

        for (var i = 0; i < 7; i++) env.SetGlowLevel(i, 0f);
        env.SetGlowLevel(3, 1.0f);

        env.GlowIntensity = _glowGlowWeight;
        env.GlowStrength = 1.0f;
        env.GlowBloom = 0.0f;

        GD.Print(
            $"[Environment] glow ENABLED additive single-level: intensity(c1 DISPLAY_GLOW_BRIGHT_MULTI)={_glowGlowWeight:F3} " +
            $"displayBaseBrightMulti(c0)={_displayBaseBrightMulti:F3} hdrThreshold=0. " +
            "spec: Docs/RE/specs/post_processing.md §8 / environment.md §9.2.");
    }

    private void ApplyDirectional(int kf, int kfNext, float frac)
    {
        if (_dirLight is null) return;

        var sunEnabled = _env?.MapOption is not { SunEnable: 0 };
        if (!sunEnabled)
        {
            _dirLight.Visible = false;
            return;
        }

        _dirLight.Visible = true;

        if (_skyDome is null && !_fallbackDirApplied
                             && _hasSunDir && _sunDirGodot.LengthSquared() > 1e-6f && !_sunDirGodot.IsZeroApprox())
            try
            {
                var dir = _sunDirGodot.Normalized();
                var up = Math.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
                _dirLight.Basis = Basis.LookingAt(dir, up);
                _fallbackDirApplied = true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[EnvironmentNode] degenerate sun dir: {ex.Message}");
            }

        var light = _env?.Light;
        if (light is not null && light.DirectionalKeyframes.Length == KeyframeCount)
        {
            var a = ColorBOf(light.DirectionalKeyframes[kf]);
            var b = ColorBOf(light.DirectionalKeyframes[kfNext]);
            var sun = a.Lerp(b, frac);
            _dirLight.LightColor = sun;
            _dirLight.LightEnergy = 1.0f;
        }
        else
        {
            _dirLight.LightColor = Colors.White;
            _dirLight.LightEnergy = 1.0f;
        }

        _dirLight.ShadowEnabled = true;
    }


    private void InitPointLightPool()
    {
        if (_plPoolInit) return;
        for (var i = 0; i < PointLightSlots; i++)
        {
            var l = new OmniLight3D
            {
                Name = $"PointLight_{i}",
                Visible = false,
                ShadowEnabled = false,
                OmniRange = 1f
            };
            AddChild(l);
            _plPool[i] = l;
        }

        _plPoolInit = true;
    }

    private void LoadPointLightData()
    {
        var bin = _env?.PointLights;
        if (bin is null || bin.RecordCount == 0 || bin.Records.Length == 0)
        {
            _plRecords = null;
            _plGodotPos = null;
            GD.Print("[Environment] point_light: no records — runtime pool idle. " +
                     "spec: Docs/RE/formats/environment_bins.md §13.");
            return;
        }

        _plRecords = bin.Records;
        _plProximityRadius = bin.ProximityRadius > 0f ? bin.ProximityRadius : 1024f;
        _plGodotPos = new Vector3[bin.Records.Length];
        for (var i = 0; i < bin.Records.Length; i++)
        {
            var r = bin.Records[i];
            var (gx, gy, gz) = WorldCoordinates.ToGodot(r.PositionX, r.PositionY, r.PositionZ);
            _plGodotPos[i] = new Vector3(gx, gy, gz);
        }

        _plCachedFocus = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        GD.Print($"[Environment] point_light: {bin.Records.Length} records, proximityRadius={_plProximityRadius:F1}; " +
                 "runtime 5-nearest model (colour +0x0C diffuse, range verbatim, skip +0x34, flicker type_flag==1). " +
                 "spec: Docs/RE/formats/environment_bins.md §13 / §3.4.");
    }

    private void UpdatePointLights(int kf, int kfNext, float frac, float delta)
    {
        if (!_plPoolInit) return;

        var light = _env?.Light;
        var master = 0f;
        if (light is { PointLightMasterIntensity.Length: KeyframeCount })
        {
            var a = light.PointLightMasterIntensity[kf];
            var b = light.PointLightMasterIntensity[kfNext];
            master = a + (b - a) * frac;
        }

        _plMasterIntensity = master;

        if (master < 0.1f || _plRecords is null || _plRecords.Length == 0 || _plGodotPos is null)
        {
            for (var i = 0; i < PointLightSlots; i++)
                if (_plPool[i] is { } l)
                    l.Visible = false;
            _plActiveCount = 0;
            return;
        }

        _plFlickerRamp += delta * _plFlickerDir * 1.6f;
        if (_plFlickerRamp >= 1f)
        {
            _plFlickerRamp = 1f;
            _plFlickerDir = -1;
        }
        else if (_plFlickerRamp <= 0f)
        {
            _plFlickerRamp = 0f;
            _plFlickerDir = 1;
        }

        var focus = PointLightFocusGodot;
        if ((focus - _plCachedFocus).LengthSquared() > 256f || _plActiveCount == 0)
        {
            SelectNearestPointLights(focus);
            _plCachedFocus = focus;
        }

        for (var slot = 0; slot < PointLightSlots; slot++)
        {
            var l = _plPool[slot];
            if (l is null) continue;

            if (slot >= _plActiveCount)
            {
                l.Visible = false;
                continue;
            }

            var ri = _plSelected[slot];
            var rec = _plRecords[ri];
            l.Position = _plGodotPos[ri];
            l.LightColor = new Color(
                Math.Clamp(rec.ColorBR, 0f, 1f),
                Math.Clamp(rec.ColorBG, 0f, 1f),
                Math.Clamp(rec.ColorBB, 0f, 1f));
            l.LightEnergy = master;

            var s = rec.Range;
            if (rec.TypeFlag == 1) s -= _plFlickerRamp * 0.3f;
            l.OmniRange = Math.Max(0.01f, s);
            l.Visible = true;
        }
    }

    private void SelectNearestPointLights(Vector3 focus)
    {
        _plActiveCount = 0;
        if (_plRecords is null || _plGodotPos is null) return;

        var radSq = _plProximityRadius * _plProximityRadius;
        for (var i = 0; i < PointLightSlots; i++)
        {
            _plSelected[i] = -1;
            _plSelectedDistSq[i] = float.MaxValue;
        }

        for (var i = 0; i < _plRecords.Length; i++)
        {
            var rec = _plRecords[i];
            if (rec.Range <= 0f) continue;
            if (rec.RawU32At0x34 != 0u) continue;

            var p = _plGodotPos[i];
            var dx = p.X - focus.X;
            var dz = p.Z - focus.Z;
            var dsq = dx * dx + dz * dz;
            if (dsq > radSq) continue;

            for (var slot = 0; slot < PointLightSlots; slot++)
                if (dsq < _plSelectedDistSq[slot])
                {
                    for (var j = PointLightSlots - 1; j > slot; j--)
                    {
                        _plSelectedDistSq[j] = _plSelectedDistSq[j - 1];
                        _plSelected[j] = _plSelected[j - 1];
                    }

                    _plSelectedDistSq[slot] = dsq;
                    _plSelected[slot] = i;
                    break;
                }
        }

        for (var i = 0; i < PointLightSlots; i++)
            if (_plSelected[i] >= 0)
                _plActiveCount++;
    }


    private void BuildSynthFogColors()
    {
        _synthFogColors = null;

        var fog = _env?.Fog;
        if (fog is null) return;
        if (fog.DataLoadFlag != 0u) return;

        var mat = _env?.Material;
        if (mat is null || mat.ColorTable.Length != MaterialBin.KeyframeCount)
        {
            GD.Print("[Environment] fog DataLoadFlag=0 but material LUT absent — synth fog unavailable; " +
                     "fog colour falls back to verbatim fog table. spec: Docs/RE/formats/environment_bins.md §2.4.");
            return;
        }

        var synth = new Color[MaterialBin.KeyframeCount];
        for (var k = 0; k < synth.Length; k++)
        {
            var row = mat.ColorTable[k];
            var high = MaterialSkyColor(row);
            var low = SkyHazeColor(row);
            synth[k] = new Color(
                Math.Clamp(high.R * 0.75f + low.R * 0.25f, 0f, 1f),
                Math.Clamp(high.G * 0.75f + low.G * 0.25f, 0f, 1f),
                Math.Clamp(high.B * 0.75f + low.B * 0.25f, 0f, 1f));
        }

        _synthFogColors = synth;
        GD.Print("[Environment] fog DataLoadFlag=0 -> synthesised 48 fog colours via 0.75*sky_ambient + 0.25*sky_haze " +
                 "(recovered 3:1 weights; exact §2.4 byte-band synth needs the raw sky byte LUT not exposed by MaterialBin — " +
                 "engineering approximation). spec: Docs/RE/formats/environment_bins.md §2.4.");
    }


    private void ResetWeatherState()
    {
        if (_weatherParticles is not null && IsInstanceValid(_weatherParticles))
        {
            _weatherParticles.QueueFree();
            _weatherParticles = null;
        }

        _weatherLastCode = -1;

        GD.Print(_env?.Weather is null
            ? "[Environment] no weather bin — weather disabled. spec: Docs/RE/formats/environment_bins.md §7."
            : "[Environment] weather bin loaded — client-local day-clock driven (row=timeBlock%10, col=hour). " +
              "spec: Docs/RE/formats/environment_bins.md §7.3.");
    }

    private void TickWeather()
    {
        var weather = _env?.Weather;
        if (weather is null) return;

        var row = _weatherDayCount % WeatherBin.RowCount;
        var col = Math.Clamp((int)(ClockMs / 3600.0), 0, WeatherBin.ColumnsPerRow - 1);
        var code = weather.GetCode(row, col);
        if (code == _weatherLastCode) return;

        _weatherLastCode = code;
        var type = code / 10;
        var intensity = code % 10 * 0.1f;
        ApplyWeather(type, intensity);
    }

    private void ApplyWeather(int type, float intensity)
    {
        if (_weatherParticles is not null && IsInstanceValid(_weatherParticles))
        {
            _weatherParticles.QueueFree();
            _weatherParticles = null;
        }

        if (type == 0 || intensity <= 0f)
        {
            GD.Print("[Environment] weather: clear (no precipitation).");
            return;
        }

        var isSnow = type == 2;
        var texPath = isSnow ? "data/sky/texture/snow.dds" : "data/sky/texture/rains.dds";

        _weatherParticles = BuildWeatherParticles(isSnow, intensity, texPath);
        if (_weatherParticles is not null) AddChild(_weatherParticles);

        GD.Print($"[Environment] weather: {(isSnow ? "SNOW" : "RAIN")} intensity={intensity:F1} tex={texPath}. " +
                 "spec: Docs/RE/formats/environment_bins.md §7.2/§7.4.");
    }

    private GpuParticles3D BuildWeatherParticles(bool isSnow, float intensity, string texPath)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(WeatherFieldExtent, 1f, WeatherFieldExtent),
            Direction = new Vector3(0f, -1f, 0f),
            Spread = isSnow ? 25f : 4f,
            Gravity = new Vector3(0f, isSnow ? -40f : -900f, 0f),
            InitialVelocityMin = isSnow ? 20f : 600f,
            InitialVelocityMax = isSnow ? 40f : 800f
        };

        var quad = new QuadMesh { Size = new Vector2(isSnow ? 12f : 6f, isSnow ? 12f : 40f) };

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = false
        };

        var tex = _assets is not null && _assets.Contains(texPath) ? _assets.LoadTexture(texPath) : null;
        if (tex is not null) mat.AlbedoTexture = tex;
        quad.Material = mat;

        var focus = PointLightFocusGodot;
        return new GpuParticles3D
        {
            Name = "WeatherParticles",
            Amount = Math.Max(8, (int)(intensity * WeatherMaxParticles)),
            Lifetime = isSnow ? 6f : 1.2f,
            ProcessMaterial = process,
            DrawPass1 = quad,
            Position = new Vector3(focus.X, focus.Y + WeatherSpawnHeight, focus.Z),
            LocalCoords = false,
            Emitting = true,
            Visible = true
        };
    }

    private void FollowWeather()
    {
        if (_weatherParticles is null || !IsInstanceValid(_weatherParticles)) return;
        var f = PointLightFocusGodot;
        _weatherParticles.Position = new Vector3(f.X, f.Y + WeatherSpawnHeight, f.Z);
    }


    private void ReadOptionBright()
    {
        OptionBrightFloor = 1.0f;
        try
        {
            var dir = ClientPathResolver.ResolveClientDir();
            if (dir is null) return;

            var path = System.IO.Path.Combine(dir, "DoOption.ini");
            if (!System.IO.File.Exists(path)) return;

            var inSection = false;
            foreach (var rawLine in System.IO.File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals("[DO_OPTION]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line[..eq].Trim();
                if (!k.Equals("OPTION_BRIGHT", StringComparison.OrdinalIgnoreCase)) continue;

                if (int.TryParse(line[(eq + 1)..].Trim(), out var pct))
                {
                    pct = Math.Clamp(pct, 1, 100);
                    OptionBrightFloor = pct / 100.0f;
                    GD.Print($"[Environment] OPTION_BRIGHT={pct} -> ambient floor {OptionBrightFloor:F2}. " +
                             "spec: Docs/RE/specs/environment.md §6.2a.");
                }

                return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Environment] DoOption.ini read failed: {ex.Message}");
        }
    }

    private void ReadDisplayLuaScalars(RealClientAssets? assets)
    {
        _glowGlowWeight = 0.3f;
        _displayBaseBrightMulti = 1.05f;

        if (assets is null) return;

        const string LuaPath = "data/script/display.lua";
        if (!assets.Contains(LuaPath))
        {
            GD.Print("[Environment] display.lua absent — glow weight default c1=0.300 displayBaseBright=1.050. " +
                     "spec: Docs/RE/specs/post_processing.md §8.");
            return;
        }

        try
        {
            var raw = assets.GetRaw(LuaPath);
            if (raw.IsEmpty) return;

            var text = System.Text.Encoding.GetEncoding(949).GetString(raw.Span);
            var glow = ParseLuaScalar(text, "DISPLAY_GLOW_BRIGHT_MULTI");
            var baseMul = ParseLuaScalar(text, "DISPLAY_BASE_BRIGHT_MULTI");
            if (glow is { } g) _glowGlowWeight = g;
            if (baseMul is { } bm) _displayBaseBrightMulti = bm;

            GD.Print($"[Environment] display.lua scalars (CP949): c1(DISPLAY_GLOW_BRIGHT_MULTI)={_glowGlowWeight:F3} " +
                     $"c0(DISPLAY_BASE_BRIGHT_MULTI)={_displayBaseBrightMulti:F3}. spec: Docs/RE/specs/post_processing.md §8.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Environment] display.lua parse failed: {ex.Message}");
        }
    }

    private static float? ParseLuaScalar(string text, string key)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!line.StartsWith(key, StringComparison.Ordinal)) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var rhs = line[(eq + 1)..].Trim();
            var c = rhs.IndexOf("--", StringComparison.Ordinal);
            if (c >= 0) rhs = rhs[..c].Trim();
            rhs = rhs.TrimEnd(';').Trim();

            if (float.TryParse(rhs, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
        }

        return null;
    }


    private void ResolveSunDirection()
    {
        float dx = -7f, dy = 7f, dz = 20f;
        if (_env?.Light is { } light)
        {
            dx = light.SunDirectionX;
            dy = light.SunDirectionY;
            dz = light.SunDirectionZ;
            if (dx == 0f && dy == 0f && dz == 0f)
            {
                dx = -7f;
                dy = 7f;
                dz = 20f;
            }
        }

        var (gx, gy, gz) = WorldCoordinates.ToGodot(dx, dy, dz);
        _sunDirGodot = new Vector3(gx, gy, gz);
        _hasSunDir = _sunDirGodot.LengthSquared() > 1e-6f;
    }


    private void BuildSkyDomes(RealClientAssets? assets)
    {
        if (_env is null) return;

        if (_env.MapOption is { IndoorFlag: 1 })
        {
            GD.Print("[SkyDome] indoor area — domes + sun/moon suppressed.");
            return;
        }

        _skyDome = new SkyDomeNode { Name = "SkyDomeNode" };
        AddChild(_skyDome);

        _skyDome.Build(_env.StarDome, _env.CloudDome, _env.CloudCycle, _dirLight);

        _skyDome.SetStarBrightnessCurve(_env.Light?.StarBrightnessCurve ?? LightBin.DefaultStarBrightnessCurve());

        if (assets is not null)
        {
            const string SunDdsPath = "data/sky/texture/sun.dds";
            var sunTex = assets.Contains(SunDdsPath) ? assets.LoadTexture(SunDdsPath) : null;
            if (sunTex is null)
                GD.Print("[SkyDome] sun.dds absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.5.");

            var moonPhase = _dateBlock % 30 / 2;
            var moonDdsPath = $"data/sky/texture/moon{moonPhase}.dds";
            var moonTex = assets.Contains(moonDdsPath) ? assets.LoadTexture(moonDdsPath) : null;
            if (moonTex is null)
                GD.Print($"[SkyDome] {moonDdsPath} absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.3.");
            GD.Print($"[SkyDome] moon phase index={moonPhase} = floor((dateBlock {_dateBlock} mod 30)/2); " +
                     "dateBlock default 0 — in-game date counter not recovered, so phase fixed at moon0. " +
                     "spec: Docs/RE/formats/sky.md §D.3.");

            _skyDome.SetBillboardTextures(sunTex, moonTex);

            const string StarDdsPath = "data/sky/texture/star.dds";
            var starTex = assets.Contains(StarDdsPath) ? assets.LoadTexture(StarDdsPath) : null;
            if (starTex is null)
                GD.Print("[SkyDome] star.dds absent from VFS — star point sprites retain placeholder white. " +
                         "spec: Docs/RE/formats/sky.md §B.");

            _skyDome.SetSkyTextures(starTex, null);

            _cloudTexCache ??= new System.Collections.Generic.Dictionary<int, ImageTexture?>();
            _cloudResolver ??= ResolveCloudTexture;
            _skyDome.SetCloudCycle(_env.CloudCycle, _cloudResolver, _dateBlock);
            GD.Print("[SkyDome] cloud textures now resolved per-tick via cloud_cycle ping-pong (cloud{id}.dds, " +
                     "id from cloud_cycle columns; colorkey 0xFF000000 black->transparent in cloud shader). " +
                     "spec: Docs/RE/formats/sky.md §F.2 / environment_bins.md §6.");
        }
        else
        {
            GD.Print("[SkyDome] assets null — billboard textures not loaded; placeholder colours retained. " +
                     "spec: Docs/RE/formats/sky.md §D.5/§D.3.");
        }

        var sunEnable = _env.MapOption is not { SunEnable: 0 };
        var moonEnable = _env.MapOption is not { MoonEnable: 0 };
        _skyDome.SetBillboardVisibility(sunEnable, moonEnable);

        GD.Print($"[SkyDome] billboard gating: sun={sunEnable} moon={moonEnable} " +
                 "(map_option Sun/Moon flags, independent of dome bins). " +
                 "spec: Docs/RE/formats/environment_bins.md §1.1 (SUN 0x14 / MOON 0x18).");

        BuildLensFlare(assets);
    }

    private ImageTexture? ResolveCloudTexture(int id)
    {
        _cloudTexCache ??= new System.Collections.Generic.Dictionary<int, ImageTexture?>();
        if (_cloudTexCache.TryGetValue(id, out var cached)) return cached;

        ImageTexture? tex = null;
        if (_assets is not null)
        {
            var path = $"data/sky/texture/cloud{id}.dds";
            tex = _assets.Contains(path) ? _assets.LoadTexture(path) : null;
            if (tex is null)
                GD.Print($"[SkyDome] cloud{id}.dds absent — ping-pong layer keeps previous texture. " +
                         "spec: Docs/RE/formats/sky.md §F.2.");
        }

        _cloudTexCache[id] = tex;
        return tex;
    }

    private void BuildLensFlare(RealClientAssets? assets)
    {
        if (_skyDome is null) return;

        if (_env?.MapOption is not { LensFlareEnable: not 0 })
        {
            var flag = _env?.MapOption is { } mo ? mo.LensFlareEnable.ToString() : "no map_option";
            GD.Print($"[SkyDome] lens flare disabled (map_option LensFlareEnable={flag}). " +
                     "spec: Docs/RE/formats/sky.md §D.4.4 / environment_bins.md §1.1 (LENSFLARE 0x08).");
            return;
        }

        var lens = new LensFlareNode { Name = "LensFlareNode" };
        AddChild(lens);
        if (lens.Configure(assets, _skyDome))
        {
            _lensFlare = lens;
            GD.Print("[SkyDome] lens flare created (map_option LensFlareEnable set). " +
                     "spec: Docs/RE/formats/sky.md §D.4.4.");
        }
        else
        {
            lens.QueueFree();
            GD.Print("[SkyDome] lens flare requested but config absent/empty — not shown. " +
                     "spec: Docs/RE/formats/sky.md §D.4.3.");
        }
    }
}
