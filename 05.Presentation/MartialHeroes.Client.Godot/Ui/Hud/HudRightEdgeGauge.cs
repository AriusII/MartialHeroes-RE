using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudRightEdgeGauge : Control
{
    private const float OffsetFromRight = 135f;
    private const float GaugeW = 140f;
    private const float GaugeH = 35f;
    private const float HpY = 200f;
    private const float MpY = 250f;

    private const string GaugeDdsPath = "data/ui/chunrihojung.dds";


    private ProgressBar _hpBar = null!;
    private Label _hpLabel = null!;


    private IHudEventHub? _hub;
    private ProgressBar _mpBar = null!;
    private Label _mpLabel = null!;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudRightEdgeGauge";
        MouseFilter = MouseFilterEnum.Ignore;

        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -OffsetFromRight;
        OffsetRight = -OffsetFromRight + GaugeW;
        OffsetTop = HpY;
        OffsetBottom = MpY + GaugeH;

        var gaugeTex = atlas.GetByPath(GaugeDdsPath);
        if (gaugeTex is null)
            GD.PrintErr("[HudRightEdgeGauge] chunrihojung.dds unavailable — gauge chrome absent (VFS offline). " +
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.6.");

        _hpBar = BuildGaugeStrip(gaugeTex, 0f,
            new Color(0.9f, 0.1f, 0.1f, 0.85f));

        _mpBar = BuildGaugeStrip(gaugeTex, MpY - HpY,
            new Color(0.1f, 0.3f, 0.9f, 0.85f));

        _hpLabel = new Label
        {
            Name = "HpLabel",
            Text = "HP —/—",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _hpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _hpLabel.OffsetTop = 0f;
        _hpLabel.OffsetBottom = GaugeH;
        HudFont.ApplyToLabel(_hpLabel, 4);
        AddChild(_hpLabel);

        _mpLabel = new Label
        {
            Name = "MpLabel",
            Text = "MP —/—",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _mpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _mpLabel.OffsetTop = MpY - HpY;
        _mpLabel.OffsetBottom = MpY - HpY + GaugeH;
        HudFont.ApplyToLabel(_mpLabel, 4);
        AddChild(_mpLabel);

        GD.Print("[HudRightEdgeGauge] Built — two 140×35 strips at screen_width−135, Y=200/250. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.");
    }

    private ProgressBar BuildGaugeStrip(Texture2D? chromeTex, float localY, Color fillColor)
    {
        var container = new Control
        {
            Name = localY < 1f ? "HpStrip" : "MpStrip",
            Position = new Vector2(0f, localY),
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(container);

        if (chromeTex is not null)
        {
            var chromeSrcY = (int)(localY / GaugeH) * (int)GaugeH;
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = new AtlasTexture
                {
                    Atlas = chromeTex,
                    Region = new Rect2(0, chromeSrcY, (int)GaugeW, (int)GaugeH),
                    FilterClip = true
                },
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            container.AddChild(chrome);
        }

        var bar = new ProgressBar
        {
            Name = "Bar",
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            MouseFilter = MouseFilterEnum.Ignore,
            ShowPercentage = false
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var fillStyle = new StyleBoxFlat { BgColor = fillColor };
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        var bgStyle = new StyleBoxEmpty();
        bar.AddThemeStyleboxOverride("background", bgStyle);
        container.AddChild(bar);

        return bar;
    }


    public void BindHub(IHudEventHub hub)
    {
        _hub = hub;
        GD.Print("[HudRightEdgeGauge] BindHub: VitalsGauge channel wired (dedicated fan-out so the " +
                 "PlayerStatusPanel Vitals reader no longer steals events — SingleReader bounded channels). " +
                 "spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.");
    }

    public override void _Process(double delta)
    {
        if (_hub is null) return;

        HudVitalsEvent? latest = null;
        while (_hub.VitalsGauge.TryRead(out var v))
            latest = v;

        if (latest is null) return;

        if (latest.MaxHp > 0)
        {
            _hpBar.MaxValue = latest.MaxHp;
            _hpBar.Value = latest.CurrentHp;
        }

        _hpLabel.Text = $"HP {latest.CurrentHp}/{latest.MaxHp}";

        if (latest.MaxMp > 0)
        {
            _mpBar.MaxValue = latest.MaxMp;
            _mpBar.Value = latest.CurrentMp;
        }

        _mpLabel.Text = $"MP {latest.CurrentMp}/{latest.MaxMp}";
    }
}