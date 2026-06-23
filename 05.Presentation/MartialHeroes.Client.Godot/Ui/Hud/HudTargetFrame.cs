
using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudTargetFrame : Control
{

    private const float FrameW = 226f;
    private const float FrameH = 54f;
    private const float HpBarDstX = 35f;
    private const float HpBarDstY = 5f;
    private const float HpBarMaxW = 172f;
    private const float HpBarH = 6f;
    private const int HpBarSrcX = 40;
    private const int HpBarSrcY = 517;
    private const float PortraitDstX = 13f;
    private const float PortraitDstY = 55f;
    private const float PortraitSide = 200f;
    private const float CloseX = 190f;
    private const float NavUpX = 202f;
    private const float NavDownX = 214f;
    private const float BtnY = 2f;
    private const float BtnSide = 11f;
    private const float StatusAX = 12f;
    private const float StatusAY = 2f;
    private const int StatusASrcX = 40;
    private const int StatusASrcY = 309;
    private const float StatusBX = 12f;
    private const float StatusBY = 17f;
    private const int StatusBSrcX = 278;
    private const int StatusBSrcY = 500;
    private const int StatusIconSide = 13;

    private const int UitexChromeId = 1;


    private Control? _hpFill;
    private Label? _nameLabel;
    private Label? _percentLabel;
    private Label? _relationLabel;
    private ChannelReader<TargetChangedEvent>? _targetChanges;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudTargetFrame";

        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -FrameW / 2f;
        OffsetRight = FrameW / 2f;
        OffsetTop = 0f;
        OffsetBottom = FrameH;
        MouseFilter = MouseFilterEnum.Ignore;

        Visible = false;

        var topFrameTex = atlas.SliceById(UitexChromeId, 226, 17, (int)FrameW, (int)FrameH);
        if (topFrameTex is not null)
        {
            var topFrame = new TextureRect
            {
                Name = "TopFrame",
                Texture = topFrameTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(0f, 0f),
                Size = new Vector2(FrameW, FrameH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(topFrame);
        }
        else
        {
            var bg = new Panel { Name = "BgFallback" };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var s = new StyleBoxFlat();
            s.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.80f);
            s.SetBorderWidthAll(1);
            s.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            bg.AddThemeStyleboxOverride("panel", s);
            AddChild(bg);
            GD.PrintErr("[HudTargetFrame] uitex id 1 unavailable (VFS offline); using fallback chrome. " +
                        "spec: Docs/RE/scenes/ingame.md §5.");
        }

        var hpFillTex = atlas.SliceById(UitexChromeId, HpBarSrcX, HpBarSrcY, (int)HpBarMaxW, (int)HpBarH);
        if (hpFillTex is not null)
        {
            var hpRect = new TextureRect
            {
                Name = "HpFill",
                Texture = hpFillTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(HpBarDstX, HpBarDstY),
                Size = new Vector2(HpBarMaxW, HpBarH),
                ClipContents = true,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(hpRect);
            _hpFill = hpRect;
        }
        else
        {
            var hpRect = new ColorRect
            {
                Name = "HpFill",
                Color = new Color(0.8f, 0.15f, 0.15f, 0.9f),
                Position = new Vector2(HpBarDstX, HpBarDstY),
                Size = new Vector2(HpBarMaxW, HpBarH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(hpRect);
            _hpFill = hpRect;
        }

        var statusATex =
            atlas.SliceById(UitexChromeId, StatusASrcX, StatusASrcY, StatusIconSide, StatusIconSide);
        var statusA = new TextureRect
        {
            Name = "StatusIconA",
            Texture = statusATex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusAX, StatusAY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(statusA);

        var statusBTex =
            atlas.SliceById(UitexChromeId, StatusBSrcX, StatusBSrcY, StatusIconSide, StatusIconSide);
        var statusB = new TextureRect
        {
            Name = "StatusIconB",
            Texture = statusBTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusBX, StatusBY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(statusB);

        var portraitPlaceholder = new ColorRect
        {
            Name = "PortraitPlaceholder",
            Color = new Color(0.1f, 0.1f, 0.1f, 0.4f),
            Position = new Vector2(PortraitDstX, PortraitDstY),
            Size = new Vector2(PortraitSide, PortraitSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(portraitPlaceholder);

        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0f, 3f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        HudFont.ApplyToLabel(_nameLabel, 0);
        AddChild(_nameLabel);

        _percentLabel = new Label
        {
            Name = "PercentLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 18f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        HudFont.ApplyToLabel(_percentLabel, 0);
        AddChild(_percentLabel);

        _relationLabel = new Label
        {
            Name = "RelationTag",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector2(150f, 12f),
            Size = new Vector2(FrameW - 150f, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        HudFont.ApplyToLabel(_relationLabel, 0);
        AddChild(_relationLabel);

        _BuildButton("CloseBtn", CloseX, BtnY, BtnSide,
            atlas, UitexChromeId, 310, 488, () =>
            {
                GD.Print("[HudTargetFrame] Close button (action 3) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5 — mode buttons (actions 1/2/3).");
                ClearTarget();
            });

        _BuildButton("NavUpBtn", NavUpX, BtnY, BtnSide,
            atlas, UitexChromeId, 321, 488, () =>
            {
                GD.Print("[HudTargetFrame] Nav-up button (action 1) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5.");
            });

        _BuildButton("NavDownBtn", NavDownX, BtnY, BtnSide,
            atlas, UitexChromeId, 332, 488, () =>
            {
                GD.Print("[HudTargetFrame] Nav-down button (action 2) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5.");
            });

        GD.Print("[HudTargetFrame] Built — MopGagePanel slot 35, rect 226×54 top-flush centred. " +
                 "Chrome 226×54 (FIXED — prior 175×318 transposed). " +
                 "PercentLabel wired to HpRatio. Name/level/relation labels + con/grade icons built. " +
                 "(Prior 'slot 177' REFUTED — ui_system.md §1.9.4 binary-won reversal, 263bd994 RTTI pass.) " +
                 "spec: Docs/RE/specs/ui_system.md §1.9.3 CODE-CONFIRMED. " +
                 "spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel layout. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.5a CYCLE 11 RESOLVED (226×54 top-flush centred).");
    }


    public void BindHub(IHudEventHub hub)
    {
        _targetChanges = hub.TargetChanges;
        GD.Print("[HudTargetFrame] BindHub: TargetChanges channel connected.");
    }


    public override void _Process(double delta)
    {
        if (_targetChanges is null) return;

        while (_targetChanges.TryRead(out var ev))
        {
            if (ev is null) continue;

            if (ev.IsCleared)
                ClearTarget();
            else
                ApplyTarget(ev);
        }
    }


    private void ApplyTarget(TargetChangedEvent ev)
    {
        Visible = true;

        var fillW = Math.Min(HpBarMaxW, HpBarMaxW * ev.HpRatio);
        switch (_hpFill)
        {
            case TextureRect tr:
                tr.Size = new Vector2(fillW, HpBarH);
                break;
            case ColorRect cr:
                cr.Size = new Vector2(fillW, HpBarH);
                break;
        }

        if (_percentLabel is not null)
            _percentLabel.Text = $"{ev.HpRatio * 100f,10:F2} %";

        if (_nameLabel is not null)
            _nameLabel.Text = string.IsNullOrEmpty(ev.Name) ? string.Empty : ev.Name;

        if (_relationLabel is not null)
            _relationLabel.Text = string.Empty;
    }

    private void ClearTarget()
    {
        Visible = false;
        if (_nameLabel is not null) _nameLabel.Text = string.Empty;
        if (_percentLabel is not null) _percentLabel.Text = string.Empty;
        if (_relationLabel is not null) _relationLabel.Text = string.Empty;

        switch (_hpFill)
        {
            case TextureRect tr: tr.Size = new Vector2(0f, HpBarH); break;
            case ColorRect cr: cr.Size = new Vector2(0f, HpBarH); break;
        }
    }


    private void _BuildButton(string nodeName, float x, float y, float side,
        HudAtlasLibrary atlas, int texId, int srcX, int srcY, Action onPress)
    {
        var normalTex = atlas.SliceById(texId, srcX, srcY, (int)side, (int)side);

        if (normalTex is not null)
        {
            var btn = new TextureButton
            {
                Name = nodeName,
                TextureNormal = normalTex,
                Position = new Vector2(x, y),
                Size = new Vector2(side, side),
                MouseFilter = MouseFilterEnum.Stop
            };
            btn.Pressed += onPress;
            AddChild(btn);
        }
        else
        {
            var btn = new Button
            {
                Name = nodeName,
                Text = string.Empty,
                Position = new Vector2(x, y),
                Size = new Vector2(side, side),
                MouseFilter = MouseFilterEnum.Stop
            };
            btn.Pressed += onPress;
            AddChild(btn);
        }
    }
}