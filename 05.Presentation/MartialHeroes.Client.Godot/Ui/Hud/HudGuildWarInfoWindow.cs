using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudGuildWarInfoWindow : Control
{
    private const float PanelW = 618f;
    private const float PanelH = 309f;

    private const float TitleBarH = 36f;
    private const float BodyY = 36f;
    private const float BodyH = 273f;

    private const float CloseBtnX = 584f;
    private const float CloseBtnY = 11f;
    private const float CloseBtnSize = 11f;
    private const float OkBtnX = 253f;
    private const float OkBtnY = 235f;
    private const float OkBtnW = 94f;
    private const float OkBtnH = 27f;

    private const int MaxRows = 10;

    private const float RowPitch = 34f;
    private const float RowBaseY = 49f;

    private const float LeftIconX = 62f;
    private const float LeftNameX = 95f;
    private const float LeftValueX = 211f;

    private const float RightIconX = 326f;
    private const float RightNameX = 359f;
    private const float RightValueX = 475f;

    private const float IconSz = 23f;
    private const float NameW = 102f;
    private const float NameH = 23f;
    private const float ValueW = 78f;
    private const float ValueH = 23f;
    private readonly Label[] _nameLabels = new Label[MaxRows];
    private readonly Label[] _valueLabels = new Label[MaxRows];


    private bool _open;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudGuildWarInfoWindow";

        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = -PanelH / 2f;
        OffsetRight = PanelW / 2f;
        OffsetBottom = PanelH / 2f;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var titleBar = new Panel { Name = "TitleBar" };
        titleBar.Position = Vector2.Zero;
        titleBar.Size = new Vector2(PanelW, TitleBarH);
        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.1f, 0.08f, 0.06f, 0.95f);
        titleBar.AddThemeStyleboxOverride("panel", titleStyle);
        AddChild(titleBar);

        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Guild War Info",
            Position = new Vector2(10f, 8f),
            Size = new Vector2(300f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(CloseBtnX, CloseBtnY),
            Size = new Vector2(CloseBtnSize, CloseBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += OnClose;
        AddChild(closeBtn);

        var bodyPanel = new Panel { Name = "BodyPanel" };
        bodyPanel.Position = new Vector2(0f, BodyY);
        bodyPanel.Size = new Vector2(PanelW, BodyH);
        var bodyStyle = new StyleBoxFlat();
        bodyStyle.BgColor = new Color(0.08f, 0.07f, 0.10f, 0.93f);
        bodyPanel.AddThemeStyleboxOverride("panel", bodyStyle);
        AddChild(bodyPanel);

        for (var r = 0; r < MaxRows; r++)
        {
            var isRight = r % 2 != 0;
            var rowY = BodyY + RowBaseY + RowPitch * (r / 2);

            var iconX = isRight ? RightIconX : LeftIconX;
            var nameX = isRight ? RightNameX : LeftNameX;
            var valueX = isRight ? RightValueX : LeftValueX;

            var capturedR = r;
            var iconBtn = new Button
            {
                Name = $"IconBtn{r}",
                Text = "□",
                Position = new Vector2(iconX, rowY),
                Size = new Vector2(IconSz, IconSz),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            iconBtn.Pressed += () => OnRowIcon(capturedR);
            AddChild(iconBtn);

            var nameBtn = new Button
            {
                Name = $"NameBtn{r}",
                Text = "",
                Position = new Vector2(nameX, rowY),
                Size = new Vector2(NameW, NameH),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop
            };
            nameBtn.Pressed += () => OnRowName(capturedR + 20);
            AddChild(nameBtn);

            var nameLbl = new Label
            {
                Name = $"NameLbl{r}",
                Text = "",
                Position = new Vector2(nameX + 2f, rowY + 3f),
                Size = new Vector2(NameW - 4f, NameH - 6f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(nameLbl);
            _nameLabels[r] = nameLbl;

            var valueBtn = new Button
            {
                Name = $"ValueBtn{r}",
                Text = "",
                Position = new Vector2(valueX, rowY),
                Size = new Vector2(ValueW, ValueH),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            valueBtn.Pressed += () => OnRowValue(capturedR + 10);
            AddChild(valueBtn);

            var valueLbl = new Label
            {
                Name = $"ValueLbl{r}",
                Text = "",
                Position = new Vector2(valueX + 2f, rowY + 3f),
                Size = new Vector2(ValueW - 4f, ValueH - 6f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(valueLbl);
            _valueLabels[r] = valueLbl;
        }

        var okBtn = new Button
        {
            Name = "OkBtn",
            Text = "OK",
            Position = new Vector2(OkBtnX, OkBtnY),
            Size = new Vector2(OkBtnW, OkBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        okBtn.Pressed += OnClose;
        AddChild(okBtn);

        GD.Print("[HudGuildWarInfoWindow] Built — GuildWarInfoPanel slot 224 (key 'j'). " +
                 "10-row two-wide list (icon 0..9, value 10..19, name 20..29). " +
                 "Close (31) + OK (30). Read-only: NO C2S. " +
                 "Atlas: literal data/ui/moonpa.dds (VFS-pending). " +
                 "Populate: TODO(capture): S2C 5/73 (344B guild-war info block). " +
                 "spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        GD.Print($"[HudGuildWarInfoWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED.");
    }


    private void OnRowIcon(int row)
    {
        GD.Print($"[HudGuildWarInfoWindow] Row icon {row} (action {row}) — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnRowName(int action)
    {
        GD.Print($"[HudGuildWarInfoWindow] Row name action {action} — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnRowValue(int action)
    {
        GD.Print($"[HudGuildWarInfoWindow] Row value action {action} — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnClose()
    {
        Toggle(false);
        GD.Print("[HudGuildWarInfoWindow] Closed (actions 30/31). " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}