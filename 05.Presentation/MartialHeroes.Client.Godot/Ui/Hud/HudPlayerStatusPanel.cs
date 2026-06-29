using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudPlayerStatusPanel : Control
{
    private const float PanelW = 285f;
    private const float PanelH = 88f;
    private const float GaugeMaxW = 172f;
    private const float GaugeH = 6f;
    private const float HpGaugeY = 18f;
    private const float MpGaugeY = 32f;
    private const float StaminaGaugeY = 46f;
    private const float GaugeOffsetX = 10f;

    private const float CondBarMaxH = 44f;
    private const float CondBarX = 3f;
    private const float CondBarY = 14f;
    private const float CondBarW = 6f;

    private const float PortraitX = 3f;
    private const float PortraitY = 3f;
    private const float PortraitW = 40f;
    private const float PortraitH = 50f;

    private const int HpSrcX = 331;
    private const int HpSrcY = 694;
    private const int MpSrcX = 504;
    private const int MpSrcY = 694;
    private const int StSrcX = 331;
    private const int StSrcY = 712;
    private const int CondSrcX = 598;
    private const int CondSrcY = 736;
    private const int PortraitSrcX = 933;
    private const int PortraitSrcY = 715;

    private const int ManifestKey = 4;
    private Control? _condBar;


    private Control? _hpFill;
    private Label? _hpLabel;
    private Label? _levelLabel;
    private Control? _mpFill;
    private Label? _mpLabel;
    private Control? _staminaFill;
    private Label? _staminaLabel;


    private ChannelReader<HudVitalsEvent>? _vitals;
    private ChannelReader<ExpLevelEvent>? _expLevels;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudPlayerStatusPanel";

        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = PanelW;
        OffsetBottom = PanelH;
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new Panel { Name = "PanelBg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.05f, 0.04f, 0.08f, 0.88f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.35f, 0.30f, 0.50f, 0.80f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        var portrait = new ColorRect
        {
            Name = "PortraitPlaceholder",
            Color = new Color(0.15f, 0.12f, 0.18f, 0.9f),
            Position = new Vector2(PortraitX, PortraitY),
            Size = new Vector2(PortraitW, PortraitH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(portrait);

        var condBg = new ColorRect
        {
            Name = "CondBarBg",
            Color = new Color(0.1f, 0.08f, 0.15f, 0.8f),
            Position = new Vector2(CondBarX, CondBarY),
            Size = new Vector2(CondBarW, CondBarMaxH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(condBg);
        var condFill = new ColorRect
        {
            Name = "CondBarFill",
            Color = new Color(0.55f, 0.20f, 0.80f, 0.9f),
            Position = new Vector2(CondBarX, CondBarY + CondBarMaxH),
            Size = new Vector2(CondBarW, 0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(condFill);
        _condBar = condFill;

        _hpFill = BuildGaugeFill("HpFill",
            GaugeOffsetX, HpGaugeY,
            new Color(0.80f, 0.15f, 0.15f, 0.9f));

        _mpFill = BuildGaugeFill("MpFill",
            GaugeOffsetX, MpGaugeY,
            new Color(0.15f, 0.40f, 0.80f, 0.9f));

        _staminaFill = BuildGaugeFill("StaminaFill",
            GaugeOffsetX, StaminaGaugeY,
            new Color(0.15f, 0.70f, 0.30f, 0.9f));

        _hpLabel = BuildGaugeLabel("HpLabel", GaugeOffsetX + GaugeMaxW + 4f, HpGaugeY);

        _mpLabel = BuildGaugeLabel("MpLabel", GaugeOffsetX + GaugeMaxW + 4f, MpGaugeY);

        _staminaLabel = BuildGaugeLabel("StaminaLabel", GaugeOffsetX + GaugeMaxW + 4f, StaminaGaugeY);

        _levelLabel = new Label
        {
            Name = "LevelLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(GaugeOffsetX, 60f),
            Size = new Vector2(PanelW - GaugeOffsetX - 4f, 14f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        HudFont.ApplyToLabel(_levelLabel, 0);
        AddChild(_levelLabel);

        GD.Print("[HudPlayerStatusPanel] Built — PlayerStatusPanel slot 15 (W=285, H=88, top-left). " +
                 "HP/MP/Stamina fills (172px max) + condition bar (44px max) + portrait placeholder + level label. " +
                 "spec: Docs/RE/scenes/ingame.md §2.1 CODE-CONFIRMED.");
    }


    public void BindHub(IHudEventHub hub)
    {
        _vitals = hub.Vitals;
        _expLevels = hub.ExpLevels;
        GD.Print("[HudPlayerStatusPanel] BindHub: Vitals + ExpLevels channels connected.");
    }


    public override void _Process(double delta)
    {
        if (_vitals is not null)
            while (_vitals.TryRead(out var ev))
            {
                if (ev is null) continue;
                ApplyVitals(ev);
            }

        if (_expLevels is not null)
            while (_expLevels.TryRead(out var exp))
            {
                if (exp is null) continue;
                ApplyExpLevel(exp);
            }
    }

    private void ApplyExpLevel(ExpLevelEvent exp)
    {
        if (_levelLabel is null) return;
        _levelLabel.Text = $"Lv.{exp.Level}";
    }


    private void ApplyVitals(HudVitalsEvent ev)
    {
        SetFillWidth(_hpFill, ev.HpRatio);

        SetFillWidth(_mpFill, ev.MpRatio);

        SetFillWidth(_staminaFill, ev.StaminaRatio);

        if (_hpLabel is not null)
            _hpLabel.Text = $"{ev.CurrentHp}/{ev.MaxHp}";

        if (_mpLabel is not null)
            _mpLabel.Text = $"{ev.CurrentMp}/{ev.MaxMp}";

        if (_staminaLabel is not null)
            _staminaLabel.Text = $"{ev.CurrentStamina}/{ev.MaxStamina}";
    }


    private static void SetFillWidth(Control? fill, float ratio)
    {
        var w = Math.Min(GaugeMaxW, GaugeMaxW * ratio);
        switch (fill)
        {
            case ColorRect cr: cr.Size = new Vector2(w, GaugeH); break;
            case TextureRect tr: tr.Size = new Vector2(w, GaugeH); break;
        }
    }

    private ColorRect BuildGaugeFill(string nodeName, float x, float y, Color fallbackColor)
    {
        var rect = new ColorRect
        {
            Name = nodeName,
            Color = fallbackColor,
            Position = new Vector2(x, y),
            Size = new Vector2(0f, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(rect);
        return rect;
    }

    private Label BuildGaugeLabel(string nodeName, float x, float y)
    {
        var lbl = new Label
        {
            Name = nodeName,
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(x, y),
            Size = new Vector2(PanelW - x - 2f, GaugeH + 4f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        HudFont.ApplyToLabel(lbl, 0);
        AddChild(lbl);
        return lbl;
    }
}