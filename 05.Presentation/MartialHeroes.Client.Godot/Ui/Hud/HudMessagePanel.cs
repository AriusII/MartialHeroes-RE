
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMessagePanel : Control
{

    private const float MsgW = 340f;
    private const float MsgH = 190f;

    private const float BtnW = 113f;
    private const float BtnH = 40f;

    private const int TexInvent = 2;
    private const int TexSkill = 8;

    private const int OkNormX = 302;
    private const int OkNormY = 860;
    private const int OkHoverX = 415;
    private const int OkHoverY = 860;
    private const int YesNormX = 660;
    private const int YesNormY = 984;
    private const int YesHoverX = 187;
    private const int YesHoverY = 956;
    private const int NoNormX = 773;
    private const int NoNormY = 984;
    private const int NoHoverX = 886;
    private const int NoHoverY = 984;

    private static readonly Vector2 BtnLeftPos = new(MsgW / 2f - 120f, MsgH - 60f);
    private static readonly Vector2 BtnRightPos = new(MsgW / 2f + 7f, MsgH - 60f);
    private static readonly Vector2 BtnOkPos = new(MsgW / 2f - 56f, MsgH - 55f);
    private Button? _btnLeft;

    private Button? _btnOk;
    private Button? _btnRight;

    private Label? _label0;
    private Label? _label1;
    private Label? _label2;
    private Action? _onNo;
    private Action? _onYes;


    private bool _open;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudMessagePanel";

        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -MsgW / 2f;
        OffsetTop = -MsgH / 2f;
        OffsetRight = MsgW / 2f;
        OffsetBottom = MsgH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.96f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        _label0 = new Label
        {
            Name = "Label0",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 50f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label0);

        _label1 = new Label
        {
            Name = "Label1",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 80f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label1);

        _label2 = new Label
        {
            Name = "Label2",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 110f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label2);

        _btnOk = BuildButton("BtnOK", "OK", BtnOkPos, TexInvent, atlas);
        _btnOk.Pressed += OnOkPressed;
        AddChild(_btnOk);

        _btnLeft = BuildButton("BtnYes", "Yes", BtnLeftPos, TexSkill, atlas);
        _btnLeft.Pressed += OnYesPressed;
        AddChild(_btnLeft);

        _btnRight = BuildButton("BtnNo", "No", BtnRightPos, TexSkill, atlas);
        _btnRight.Pressed += OnNoPressed;
        AddChild(_btnRight);

        ApplyMode(0);

        GD.Print("[HudMessagePanel] Built — centered 340×190 modal, slot 190. " +
                 "Mode 0=OK notice, Mode 1=Yes/No confirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED.");
    }

    private static Button BuildButton(string name, string fallbackText, Vector2 pos, int texId, HudAtlasLibrary atlas)
    {
        var btn = new Button
        {
            Name = name,
            Text = fallbackText,
            Position = pos,
            Size = new Vector2(BtnW, BtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        if (atlas is not null)
        {
        }

        return btn;
    }


    public void ShowNotice(string text)
    {
        if (_label0 is null) return;

        _label0.Text = text;
        if (_label1 is not null) _label1.Text = string.Empty;
        if (_label2 is not null) _label2.Text = string.Empty;
        _onYes = null;
        _onNo = null;

        ApplyMode(0);
        OpenModal();
    }

    public void ShowConfirm(string text, Action? onYes = null, Action? onNo = null)
    {
        if (_label0 is null) return;

        _label0.Text = text;
        if (_label1 is not null) _label1.Text = string.Empty;
        if (_label2 is not null) _label2.Text = string.Empty;
        _onYes = onYes;
        _onNo = onNo;

        ApplyMode(1);
        OpenModal();
    }


    private void ApplyMode(int mode)
    {
        var modeOk = mode == 0;
        if (_btnOk is not null) _btnOk.Visible = modeOk;
        if (_btnLeft is not null) _btnLeft.Visible = !modeOk;
        if (_btnRight is not null) _btnRight.Visible = !modeOk;
    }


    private void OnOkPressed()
    {
        CloseModal();
    }

    private void OnYesPressed()
    {
        var cb = _onYes;
        CloseModal();
        cb?.Invoke();
    }

    private void OnNoPressed()
    {
        var cb = _onNo;
        CloseModal();
        cb?.Invoke();
    }

    private void OpenModal()
    {
        _open = true;
        Visible = true;
    }

    private void CloseModal()
    {
        _open = false;
        Visible = false;
        _onYes = null;
        _onNo = null;
    }


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            var cb = _onNo;
            CloseModal();
            cb?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }
}