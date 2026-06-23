using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudPetPanel : Control
{
    private const float WinX = 80f;
    private const float WinY = 200f;
    private const float WinW = 228f;
    private const float WinH = 337f;

    private const float TitleH = 16f;

    private const float CornerBtnSize = 11f;

    private const float NameLblY = 16f;
    private const float NameLblH = 15f;

    private const float GaugeX = 15f;
    private const float Gauge0Y = 77f;
    private const float Gauge1Y = 107f;
    private const float GaugeW = 196f;
    private const float GaugeH = 11f;

    private const float CmdBtnW = 91f;
    private const float CmdBtnH = 25f;

    private const int MsgHelpBtn = 16002;
    private ProgressBar? _gauge0;
    private ProgressBar? _gauge1;
    private Label? _levelLabel;


    private Label? _nameLabel;


    private bool _open;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudPetPanel";

        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = WinX;
        OffsetTop = WinY;
        OffsetRight = WinX + WinW;
        OffsetBottom = WinY + WinH;

        Visible = false;
        _open = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.05f, 0.08f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.25f, 0.50f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var titleBar = new Panel { Name = "TitleBar" };
        titleBar.AnchorLeft = 0f;
        titleBar.AnchorTop = 0f;
        titleBar.AnchorRight = 0f;
        titleBar.AnchorBottom = 0f;
        titleBar.OffsetLeft = 0f;
        titleBar.OffsetTop = 0f;
        titleBar.OffsetRight = WinW;
        titleBar.OffsetBottom = TitleH;
        var tbStyle = new StyleBoxFlat();
        tbStyle.BgColor = new Color(0.35f, 0.15f, 0.35f, 0.97f);
        titleBar.AddThemeStyleboxOverride("panel", tbStyle);
        AddChild(titleBar);

        BuildCornerButton("MinimizeBtn", 5f, 3f, 0);
        BuildCornerButton("HelpBtn", 116f, 3f, 2);
        BuildCornerButton("CloseBtn_Title", 211f, 3f, 8);

        _nameLabel = new Label
        {
            Name = "PartnerName",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, NameLblY),
            Size = new Vector2(WinW, NameLblH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_nameLabel);

        _levelLabel = new Label
        {
            Name = "PartnerLevel",
            Text = string.Empty,
            Position = new Vector2(10f, 59f),
            Size = new Vector2(78f, 15f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_levelLabel);

        _gauge0 = new ProgressBar
        {
            Name = "Gauge0",
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            Position = new Vector2(GaugeX, Gauge0Y),
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_gauge0);

        _gauge1 = new ProgressBar
        {
            Name = "Gauge1",
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            Position = new Vector2(GaugeX, Gauge1Y),
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_gauge1);

        BuildCommandButton("CmdA", 15f, 157f, 3);
        BuildCommandButton("CmdB", 124f, 157f, 4);
        BuildCommandButton("CmdC", 15f, 215f, 5);
        BuildCommandButton("CmdD", 124f, 215f, 6);

        var infoBtn = new Button
        {
            Name = "InfoBtn",
            Text = text?.GetCaption(MsgHelpBtn, "[정보]") ?? "[정보]",
            Position = new Vector2(110f, 253f),
            Size = new Vector2(110f, 39f),
            MouseFilter = MouseFilterEnum.Stop
        };
        infoBtn.Pressed += () => OnAction(7);
        AddChild(infoBtn);

        GD.Print("[HudPetPanel] Built — player-couple/pair-relation window slot 194 (§8.26). " +
                 "Fixed (80,200) 228×337. Partner name/level, 2 gauges, 4 cmd buttons, info button. " +
                 "Auto-shown by S2C 5/53 SmsgActorPairRelation (TODO world-campaign). " +
                 "NOT a hotkey window (§8.26.4). Command sends = TODO(world-campaign). " +
                 "spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.");
    }


    private void BuildCornerButton(string nodeName, float x, float y, int action)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = action == 8 ? "×" : action == 0 ? "−" : "?",
            Position = new Vector2(x, y),
            Size = new Vector2(CornerBtnSize, CornerBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        var captured = action;
        btn.Pressed += () => OnAction(captured);
        AddChild(btn);
    }

    private void BuildCommandButton(string nodeName, float x, float y, int action)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = string.Empty,
            Position = new Vector2(x, y),
            Size = new Vector2(CmdBtnW, CmdBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        var captured = action;
        btn.Pressed += () => OnAction(captured);
        AddChild(btn);
    }


    public void ShowPartner(string partnerName, int partnerLevel, float gauge0Value, float gauge1Value)
    {
        if (_nameLabel != null) _nameLabel.Text = partnerName;
        if (_levelLabel != null) _levelLabel.Text = $"Lv. {partnerLevel}";
        if (_gauge0 != null) _gauge0.Value = gauge0Value;
        if (_gauge1 != null) _gauge1.Value = gauge1Value;

        _open = true;
        Visible = true;

        GD.Print($"[HudPetPanel] ShowPartner: name=\"{partnerName}\" lv={partnerLevel} " +
                 $"g0={gauge0Value} g1={gauge1Value}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.");
    }

    public void ClearPartner()
    {
        if (_nameLabel != null) _nameLabel.Text = string.Empty;
        if (_levelLabel != null) _levelLabel.Text = string.Empty;
        if (_gauge0 != null) _gauge0.Value = 0.0;
        if (_gauge1 != null) _gauge1.Value = 0.0;

        _open = false;
        Visible = false;

        GD.Print("[HudPetPanel] ClearPartner — hidden. " +
                 "spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.");
    }


    private void OnAction(int actionId)
    {
        switch (actionId)
        {
            case 0:
                GD.Print("[HudPetPanel] action 0 — minimize (role MED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 2:
                GD.Print("[HudPetPanel] action 2 — help (msg 16002). " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 3:
            case 4:
            case 5:
            case 6:
                GD.Print($"[HudPetPanel] action {actionId} — command button {actionId - 2}. " +
                         "TODO(world-campaign): generic select/send helper. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 7:
                GD.Print("[HudPetPanel] action 7 — info/details button. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 8:
                ClearPartner();
                GD.Print("[HudPetPanel] action 8 — close. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            default:
                GD.Print($"[HudPetPanel] action {actionId} — unhandled. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
        }
    }
}