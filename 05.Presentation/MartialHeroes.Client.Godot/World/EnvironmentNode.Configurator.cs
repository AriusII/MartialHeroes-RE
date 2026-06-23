using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode
{
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
            var skyColor = a.Lerp(b, frac);

            var lum = 0.2126f * skyColor.R + 0.7152f * skyColor.G + 0.0722f * skyColor.B;
            if (lum >= 0.025f)
            {
                env.BackgroundColor = skyColor;
                return;
            }

            var hazeA = SkyHazeColor(mat.ColorTable[kf]);
            var hazeB = SkyHazeColor(mat.ColorTable[kfNext]);
            var hazeColor = hazeA.Lerp(hazeB, frac);
            var hazeLum = 0.2126f * hazeColor.R + 0.7152f * hazeColor.G + 0.0722f * hazeColor.B;
            if (hazeLum >= 0.025f)
            {
                env.BackgroundColor = new Color(hazeColor.R * 0.6f, hazeColor.G * 0.6f, hazeColor.B * 0.6f);
                return;
            }
        }

        if (_env?.Fog is { } fog)
        {
            var fogColor = LerpFogColor(fog, kf, kfNext, frac);
            var fogLum = 0.2126f * fogColor.R + 0.7152f * fogColor.G + 0.0722f * fogColor.B;
            if (fogLum >= 0.025f)
            {
                env.BackgroundColor = new Color(
                    Math.Min(fogColor.R * 1.3f, 1f),
                    Math.Min(fogColor.G * 1.3f, 1f),
                    Math.Min(fogColor.B * 1.3f, 1f));
                return;
            }
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

        var light = _env?.Light;
        var fogScalar = 0f;
        if (light is { FogDistanceScalars.Length: >= LightBin.KeyframeCount })
        {
            var sA = light.FogDistanceScalars[kf];
            var sB = light.FogDistanceScalars[kfNext];
            fogScalar = sA + (sB - sA) * frac;
        }

        if (fogScalar <= 0f)
        {
            env.FogEnabled = false;
            return;
        }

        env.FogEnabled = true;
        env.FogMode = Environment.FogModeEnum.Depth;

        var fogStart = fog.StartDist * ViewRange;
        var fogEnd = fog.EndDist * ViewRange;
        if (fogEnd <= fogStart || fogEnd <= 0f)
        {
            fogStart = 1.0f / fogScalar;
            fogEnd = fogScalar * 3.0f;
        }

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
        env.AmbientLightColor = Colors.White;
        env.AmbientLightEnergy = OptionBrightFloor;

        CelShadeMaterialFactory.AmbientFloorEnergy = OptionBrightFloor;
    }


    private static void ApplyGlow(Environment env)
    {
        env.GlowEnabled = false;
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
            var a = ColorAOf(light.DirectionalKeyframes[kf]);
            var b = ColorAOf(light.DirectionalKeyframes[kfNext]);
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


    private void SpawnPointLights()
    {
        foreach (var existing in _pointLights)
            if (IsInstanceValid(existing))
                existing.QueueFree();
        _pointLights.Clear();

        var bin = _env?.PointLights;
        if (bin is null || bin.RecordCount == 0)
        {
            GD.Print("[Environment] no point_light bin or empty — no OmniLight3D spawned.");
            return;
        }

        var scale = 1f;

        var spawned = 0;
        for (var i = 0; i < bin.Records.Length; i++)
        {
            var rec = bin.Records[i];

            if (rec.Range <= 0f)
                continue;

            var (gx, gy, gz) = WorldCoordinates.ToGodot(rec.PositionX, rec.PositionY, rec.PositionZ);

            var light = new OmniLight3D
            {
                Name = $"PointLight_{i}",
                Position = new Vector3(gx, gy, gz),
                OmniRange = rec.Range,
                LightColor = new Color(
                    Math.Clamp(rec.ColorDiffuseR, 0f, 1f),
                    Math.Clamp(rec.ColorDiffuseG, 0f, 1f),
                    Math.Clamp(rec.ColorDiffuseB, 0f, 1f)),
                LightEnergy = scale,
                ShadowEnabled = false
            };

            AddChild(light);
            _pointLights.Add(light);
            spawned++;
        }

        GD.Print($"[Environment] point_light: spawned {spawned}/{bin.RecordCount} OmniLight3D nodes " +
                 $"(proximityRadius={bin.ProximityRadius:F3}; skipped {bin.RecordCount - spawned} zero-range).");
    }

    private void ConfigureWeather(RealClientAssets? assets)
    {
        if (_weatherParticles is not null && IsInstanceValid(_weatherParticles))
        {
            _weatherParticles.QueueFree();
            _weatherParticles = null;
        }

        var weather = _env?.Weather;
        if (weather is null)
        {
            GD.Print("[Environment] no weather bin — weather effects disabled.");
            return;
        }

        var nowHour = DateTime.UtcNow.Hour;
        var timeBlock = (int)(ClockMs / (KeyframeMs * 4.8)) % WeatherBin.RowCount;
        var weatherType = weather.GetWeatherType(timeBlock, nowHour % WeatherBin.ColumnsPerRow);
        var intensity = weather.GetIntensity(timeBlock, nowHour % WeatherBin.ColumnsPerRow);

        if (weatherType == 0)
        {
            GD.Print("[Environment] weather bin present but current slot=clear — no particles.");
            return;
        }

        var particles = new GpuParticles3D
        {
            Name = "WeatherParticles",
            Amount = weatherType == 1 ? 2000 : 1200,
            Lifetime = weatherType == 1 ? 2.0 : 4.0,
            VisibilityAabb = new Aabb(new Vector3(-4000f, -100f, -4000f), new Vector3(8000f, 4000f, 8000f)),
            Emitting = true
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(4000f, 100f, 4000f)
        };

        if (weatherType == 1)
        {
            pm.Gravity = new Vector3(0f, -980f * intensity, 0f);
            pm.InitialVelocityMin = 200f * intensity;
            pm.InitialVelocityMax = 400f * intensity;
            pm.Color = new Color(0.7f, 0.85f, 1.0f, 0.7f);
            pm.ScaleMin = 2f;
            pm.ScaleMax = 6f;
        }
        else
        {
            pm.Gravity = new Vector3(0f, -120f * intensity, 0f);
            pm.InitialVelocityMin = 30f * intensity;
            pm.InitialVelocityMax = 80f * intensity;
            pm.Color = new Color(1.0f, 1.0f, 1.0f, 0.85f);
            pm.ScaleMin = 4f;
            pm.ScaleMax = 10f;
        }

        particles.ProcessMaterial = pm;

        AddChild(particles);
        _weatherParticles = particles;

        var typeName = weatherType == 1 ? "rain" : "snow";
        GD.Print($"[Environment] weather: type={typeName} intensity={intensity:F2} " +
                 $"timeBlock={timeBlock} hour={nowHour} — GpuParticles3D spawned.");
    }

    private void ResolveSunDirection()
    {
        float dx = -7f, dy = 7f, dz = 20f;
        if (_env?.Light is { } light)
        {
            dx = light.FallbackDirX;
            dy = light.FallbackDirY;
            dz = light.FallbackDirZ;
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
            GD.Print("[SkyDome] indoor area — domes suppressed.");
            return;
        }

        if (_env.StarDome is null && _env.CloudDome is null)
        {
            GD.Print("[SkyDome] no dome bins available — no sky domes created.");
            return;
        }

        _skyDome = new SkyDomeNode { Name = "SkyDomeNode" };
        AddChild(_skyDome);

        _skyDome.Build(_env.StarDome, _env.CloudDome, _env.CloudCycle, _dirLight);

        if (assets is not null)
        {
            const string SunDdsPath = "data/sky/texture/sun.dds";
            var sunTex = assets.Contains(SunDdsPath) ? assets.LoadTexture(SunDdsPath) : null;
            if (sunTex is null)
                GD.Print("[SkyDome] sun.dds absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.5");

            const int MoonPhase = 0;
            var moonDdsPath = $"data/sky/texture/moon{MoonPhase}.dds";
            var moonTex = assets.Contains(moonDdsPath) ? assets.LoadTexture(moonDdsPath) : null;
            if (moonTex is null)
                GD.Print($"[SkyDome] {moonDdsPath} absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.3");

            _skyDome.SetBillboardTextures(sunTex, moonTex);
        }
        else
        {
            GD.Print("[SkyDome] assets null — billboard textures not loaded; placeholder colours retained. " +
                     "spec: Docs/RE/formats/sky.md §D.5/§D.3");
        }
    }
}