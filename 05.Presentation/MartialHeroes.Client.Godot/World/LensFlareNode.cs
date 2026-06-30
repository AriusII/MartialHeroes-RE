using System.Globalization;
using System.Text;
using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class LensFlareNode : CanvasLayer
{
    private const string ConfigPath = "data/sky/lensflare.txt";
    private Sprite2D[] _ghosts = Array.Empty<Sprite2D>();
    private float _invIntensityBorder;

    private SkyDomeNode? _skyDome;
    private FlareSpot[] _spots = Array.Empty<FlareSpot>();

    public bool Configure(RealClientAssets? assets, SkyDomeNode skyDome)
    {
        _skyDome = skyDome;

        if (assets is null || !assets.Contains(ConfigPath))
        {
            GD.Print("[LensFlare] data/sky/lensflare.txt absent — flare skipped (no fabricated flare). " +
                     "spec: Docs/RE/formats/sky.md D.4.3.");
            return false;
        }

        var raw = assets.GetRaw(ConfigPath);
        if (raw.IsEmpty)
        {
            GD.Print("[LensFlare] lensflare.txt empty — flare skipped. spec: Docs/RE/formats/sky.md D.4.3.");
            return false;
        }

        var text = Encoding.GetEncoding(949).GetString(raw.Span);
        if (!ParseConfig(text))
        {
            GD.Print("[LensFlare] lensflare.txt parsed 0 spots — flare skipped. spec: Docs/RE/formats/sky.md D.4.3.");
            return false;
        }

        BuildGhosts(assets);
        Layer = 64;

        GD.Print($"[LensFlare] enabled: {_spots.Length} ghost sprites, invIntensityBorder={_invIntensityBorder:F4} " +
                 "(brightness=1-(1/INTENSITY_BORDER)*screenDist; sun projected via Godot Camera3D.UnprojectPosition). " +
                 "Gates: map_option LensFlareEnable + sun above horizon (Y>=0) + sun in front of camera + screen-edge brightness. " +
                 "Terrain-occlusion gate (D.4.4) NOT wired — no terrain height/raycast API exposed to this node. " +
                 "spec: Docs/RE/formats/sky.md D.4.1/D.4.2/D.4.4.");
        return true;
    }

    private bool ParseConfig(string text)
    {
        var spots = new List<FlareSpot>();
        var current = new FlareSpot { Color = Colors.White, Radius = 32f };
        var inSpot = false;
        var intensityBorder = 0f;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var toks = line.Split(new[] { ' ', '\t', ',', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) continue;

            var key = toks[0].ToUpperInvariant();
            switch (key)
            {
                case "INTENSITY_BORDER":
                    if (toks.Length >= 2)
                        float.TryParse(toks[1], NumberStyles.Float, CultureInfo.InvariantCulture, out intensityBorder);
                    break;
                case "SPOT":
                    if (toks.Length >= 3 && toks[2].Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
                    {
                        inSpot = true;
                        current = new FlareSpot { Color = Colors.White, Radius = 32f };
                    }
                    else if (toks.Length >= 2 && toks[1].Equals("END", StringComparison.OrdinalIgnoreCase))
                    {
                        if (inSpot) spots.Add(current);
                        inSpot = false;
                    }

                    break;
                case "TEXTURE_ID":
                    if (inSpot && toks.Length >= 2 && int.TryParse(toks[1], out var tid))
                        current.TextureId = tid;
                    break;
                case "RADIUS":
                    if (inSpot && toks.Length >= 2 &&
                        float.TryParse(toks[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rad))
                        current.Radius = rad;
                    break;
                case "POSITION":
                    if (inSpot && toks.Length >= 2 &&
                        float.TryParse(toks[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pos))
                        current.Position = pos;
                    break;
                case "COLOR":
                    if (inSpot && toks.Length >= 5 &&
                        byte.TryParse(toks[1], out var cr) && byte.TryParse(toks[2], out var cg) &&
                        byte.TryParse(toks[3], out var cb) && byte.TryParse(toks[4], out var ca))
                        current.Color = new Color(cr / 255f, cg / 255f, cb / 255f, ca / 255f);
                    break;
            }
        }

        _spots = spots.ToArray();
        _invIntensityBorder = intensityBorder > 0f ? 1f / intensityBorder : 0f;
        return _spots.Length > 0;
    }

    private void BuildGhosts(RealClientAssets assets)
    {
        var ghosts = new List<Sprite2D>();
        var texCache = new Dictionary<int, Texture2D?>();

        for (var i = 0; i < _spots.Length; i++)
        {
            var spot = _spots[i];
            if (!texCache.TryGetValue(spot.TextureId, out var tex))
            {
                var path = $"data/sky/texture/lensflare{spot.TextureId}.dds";
                tex = assets.Contains(path) ? assets.LoadTexture(path) : null;
                texCache[spot.TextureId] = tex;
                if (tex is null)
                    GD.Print(
                        $"[LensFlare] {path} absent — spot {i} will not draw. spec: Docs/RE/formats/sky.md D.4.3.");
            }

            var sprite = new Sprite2D
            {
                Name = $"FlareGhost_{i}",
                Texture = tex,
                Centered = true,
                Visible = false,
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
            };
            AddChild(sprite);
            ghosts.Add(sprite);
        }

        _ghosts = ghosts.ToArray();
    }

    public override void _Process(double delta)
    {
        if (_ghosts.Length == 0 || _skyDome is null)
        {
            HideAll();
            return;
        }

        if (!_skyDome.SunVisible)
        {
            HideAll();
            return;
        }

        var cam = GetViewport()?.GetCamera3D();
        if (cam is null)
        {
            HideAll();
            return;
        }

        var sun = _skyDome.SunGlobalPosition;

        if (sun.Y < 0f)
        {
            HideAll();
            return;
        }

        if (cam.IsPositionBehind(sun))
        {
            HideAll();
            return;
        }

        var anchor = cam.UnprojectPosition(sun);
        var rectSize = GetViewport().GetVisibleRect().Size;
        var center = rectSize * 0.5f;

        var clamped = new Vector2(
            Math.Clamp(anchor.X, 0f, rectSize.X),
            Math.Clamp(anchor.Y, 0f, rectSize.Y));
        var edgeDistance = anchor.DistanceTo(clamped);
        var brightness = _invIntensityBorder > 0f ? 1f - _invIntensityBorder * edgeDistance : 1f;
        if (brightness <= 0f)
        {
            HideAll();
            return;
        }

        brightness = Math.Clamp(brightness, 0f, 1f);
        var dir = anchor - center;
        var viewportWidth = center.X * 2f;

        for (var i = 0; i < _ghosts.Length; i++)
        {
            var g = _ghosts[i];
            var spot = _spots[i];
            if (g.Texture is null)
            {
                g.Visible = false;
                continue;
            }

            g.Position = center + dir * spot.Position;

            var texSize = g.Texture.GetSize();
            var maxDim = Math.Max(texSize.X, texSize.Y);
            var scale = maxDim > 0f ? spot.Radius * viewportWidth * 2f / maxDim : 1f;
            g.Scale = new Vector2(scale, scale);
            g.Modulate = new Color(spot.Color.R, spot.Color.G, spot.Color.B, spot.Color.A * brightness);
            g.Visible = true;
        }
    }

    private void HideAll()
    {
        for (var i = 0; i < _ghosts.Length; i++)
            _ghosts[i].Visible = false;
    }

    private struct FlareSpot
    {
        public int TextureId;
        public float Radius;
        public float Position;
        public Color Color;
    }
}