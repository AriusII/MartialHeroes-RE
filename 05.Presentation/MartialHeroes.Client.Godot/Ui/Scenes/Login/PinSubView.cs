
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens.Layout;


namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class PinSubView : Control
{
    [Signal]
    public delegate void CancelledEventHandler();


    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);

    private const string AtlasPassword = LoginLayout.AtlasPassword;


    private const int ModalX = LoginLayout.PinModalX;
    private const int ModalY = LoginLayout.PinModalY;
    private const int ModalW = LoginLayout.PinModalW;
    private const int ModalH = LoginLayout.PinModalH;

    private const int TileW = LoginLayout.PinKeypadTileW;
    private const int TileH = LoginLayout.PinKeypadTileH;
    private const int ColSpacing = LoginLayout.PinKeypadColSpacing;
    private const int Col0X = LoginLayout.PinKeypadCol0X;
    private const int Row0Y = LoginLayout.PinKeypadRow0Y;
    private const int Row1Y = LoginLayout.PinKeypadRow1Y;

    private const int DigitNormalV = LoginLayout.PinDigitNormalSrcY;
    private const int DigitHoverV = LoginLayout.PinDigitHoverSrcY;
    private const int DigitPressedV = LoginLayout.PinDigitPressedSrcY;
    private const int DigitColW = LoginLayout.PinDigitColWidth;

    private const int PinMaxLength = LoginLayout.PinMaxLength;


    private const int TagReset = 11;
    private const int TagOk = 12;
    private const int TagCancel = 13;

    private const int PinDisplayX = 81;
    private const int PinDisplayY = 138;
    private const int PinDisplayW = 150;
    private const int PinDisplayH = 22;

    private const int ExitPanelW = 340;
    private const int ExitPanelH = 190;
    private const int ExitPanelSrcX = 318;

    private const int ExitPanelSrcY = 647;

    private const int ExitPanelLocalX = -6;
    private const int ExitPanelLocalY = 116;

    private const int ExitPanelYesAction = 201;
    private const int ExitPanelNoAction = 202;


    private readonly HudAtlasLibrary _atlas;

    private readonly TextureButton?[,] _digitButtons = new TextureButton?[10, 10];

    private Control? _exitPanel;
    private string _pin = "";

    private Label? _pinDisplay;

    private int[] _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];


    public PinSubView(HudAtlasLibrary atlas)
    {
        _atlas = atlas;

        Visible = false;
    }


    public override void _Ready()
    {
        Position = new Vector2(ModalX, ModalY);

        Size = new Vector2(ModalW, ModalH);
        CustomMinimumSize = new Vector2(ModalW, ModalH);

        Scramble();

        BuildModalChrome();

        BuildKeypad();

        _pinDisplay = new Label
        {
            Text = "",
            Position = new Vector2(PinDisplayX, PinDisplayY),
            Size = new Vector2(PinDisplayW, PinDisplayH),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        BuildControlButtons();

        BuildHiddenExitPanel();
    }


    private void BuildModalChrome()
    {
        AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            Position = new Vector2(-ModalX, -ModalY),
            Size = new Vector2(1024, 768),
            MouseFilter = MouseFilterEnum.Stop
        });

        Texture2D? backdrop = _atlas.SliceByPath(
            AtlasPassword,
            0, 0,
            ModalW, ModalH
        );
        if (backdrop is not null)
            AddChild(new TextureRect
            {
                Position = Vector2.Zero,
                Size = new Vector2(ModalW, ModalH),
                Texture = backdrop,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore
            });

    }


    private void BuildKeypad()
    {
        for (var pos = 0; pos < 10; pos++)
        {
            var col = pos % 5;
            var row = pos / 5;

            var x = Col0X + col * ColSpacing;
            var y = row == 0 ? Row0Y : Row1Y;

            for (var face = 0; face < 10; face++)
            {
                var srcU = face * DigitColW;

                Texture2D? normal = _atlas.SliceByPath(AtlasPassword, srcU, DigitNormalV, TileW, TileH);
                Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, srcU, DigitPressedV, TileW, TileH);
                Texture2D? hover = _atlas.SliceByPath(AtlasPassword, srcU, DigitHoverV, TileW, TileH);

                var btn = new TextureButton
                {
                    Position = new Vector2(x, y),
                    Size = new Vector2(TileW, TileH),
                    CustomMinimumSize = new Vector2(TileW, TileH),
                    IgnoreTextureSize = true,
                    StretchMode = TextureButton.StretchModeEnum.Scale,
                    TextureNormal = normal,
                    TextureHover = hover,
                    TexturePressed = pressed,
                    TextureDisabled = normal,
                    Visible = false,
                    MouseFilter = MouseFilterEnum.Ignore
                };

                var actionId = pos * 10 + face;
                btn.Pressed += () => OnDigitFaceAction(actionId);
                AddChild(btn);
                _digitButtons[pos, face] = btn;
            }
        }

        ApplyScramble();
    }

    private void ApplyScramble()
    {
        for (var pos = 0; pos < 10; pos++)
        {
            var shownFace = _scrambled[pos];
            for (var face = 0; face < 10; face++)
            {
                var btn = _digitButtons[pos, face];
                if (btn is null) continue;
                var shown = face == shownFace;
                btn.Visible = shown;
                btn.MouseFilter = shown ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            }
        }
    }

    private void BuildButton(
        int x, int y, int w, int h,
        int nSrcX, int nSrcY,
        int hSrcX, int hSrcY,
        int pSrcX, int pSrcY,
        int tag)
    {
        Texture2D? normal = _atlas.SliceByPath(AtlasPassword, nSrcX, nSrcY, w, h);
        Texture2D? hover = _atlas.SliceByPath(AtlasPassword, hSrcX, hSrcY, w, h);
        Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, pSrcX, pSrcY, w, h);

        var btn = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = normal,
            TextureHover = hover,
            TexturePressed = pressed,
            TextureDisabled = normal
        };

        var capturedTag = tag;
        btn.Pressed += () => OnButtonAction(capturedTag);
        AddChild(btn);
    }


    private void OnDigitPressed(int digit)
    {
        if (_pin.Length >= PinMaxLength) return;
        _pin += digit.ToString();
        UpdatePinDisplay();
        GD.Print($"[PinSubView] Digit {digit} pressed; PIN length now {_pin.Length}.");
    }

    private void OnDigitFaceAction(int actionId)
    {
        var pos = actionId / 10;
        var face = actionId % 10;
        if ((uint)pos >= (uint)_scrambled.Length || _scrambled[pos] != face)
            return;

        OnDigitPressed(face);
    }

    private void OnButtonAction(int tag)
    {
        switch (tag)
        {
            case TagReset:
                _pin = "";
                UpdatePinDisplay();
                Scramble();
                RebuildKeypad();
                GD.Print("[PinSubView] Reset (tag 11): entry wiped + keypad re-scrambled. " +
                         "spec: frontend_layout_tables.md §3.");
                break;

            case TagOk:
                GD.Print($"[PinSubView] OK (tag 12): PinSubmitted(pin_len={_pin.Length}). " +
                         "spec: frontend_layout_tables.md §3.");
                EmitSignal(SignalName.PinSubmitted, _pin);
                _pin = "";
                Scramble();
                RebuildKeypad();
                break;

            case TagCancel:
                GD.Print("[PinSubView] Cancel (tag 13): clear entry + re-scramble + raise ExitPanel. spec: §3.");
                _pin = "";
                UpdatePinDisplay();
                Scramble();
                RebuildKeypad();
                if (_exitPanel is not null) _exitPanel.Visible = true;
                break;
        }
    }


    private void UpdatePinDisplay()
    {
        if (_pinDisplay is null) return;
        _pinDisplay.Text = new string('*', _pin.Length);
    }


    private void Scramble()
    {
        _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        var seed = (int)((long)Math.Floor(Time.GetUnixTimeFromSystem()) & 0x7FFF_FFFF);
        var rng = new Random(seed);

        for (var i = 1; i < 10; i++)
        {
            var j = rng.Next(0, i + 1);
            (_scrambled[i], _scrambled[j]) = (_scrambled[j], _scrambled[i]);
        }
    }

    private void RebuildKeypad()
    {
        ApplyScramble();
        UpdatePinDisplay();
    }

    private void BuildHiddenExitPanel()
    {
        var panel = new Control
        {
            Name = "PinExitPanel",
            Position = new Vector2(ExitPanelLocalX, ExitPanelLocalY),
            Size = new Vector2(ExitPanelW, ExitPanelH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };

        Texture2D? chrome = _atlas.SliceByPath(
            LoginLayout.AtlasInventWindow,
            ExitPanelSrcX, ExitPanelSrcY, ExitPanelW, ExitPanelH);
        if (chrome is not null)
            panel.AddChild(new TextureRect
            {
                Position = Vector2.Zero,
                Size = new Vector2(ExitPanelW, ExitPanelH),
                Texture = chrome,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });

        Texture2D? yesN = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 302, 900, 113, 40);
        Texture2D? yesH = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 415, 900, 113, 40);
        var yesBtn = new TextureButton
        {
            Name = "ExitYes",
            Position = new Vector2(40, 136),
            Size = new Vector2(113, 40),
            CustomMinimumSize = new Vector2(113, 40),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = yesN,
            TextureHover = yesH,
            TexturePressed = yesN
        };
        yesBtn.Pressed += OnExitPanelYes;
        panel.AddChild(yesBtn);

        Texture2D? noN = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 302, 860, 113, 40);
        Texture2D? noH = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 415, 860, 113, 40);
        var noBtn = new TextureButton
        {
            Name = "ExitNo",
            Position = new Vector2(190, 136),
            Size = new Vector2(113, 40),
            CustomMinimumSize = new Vector2(113, 40),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = noN,
            TextureHover = noH,
            TexturePressed = noN
        };
        noBtn.Pressed += OnExitPanelNo;
        panel.AddChild(noBtn);

        AddChild(panel);
        _exitPanel = panel;
    }

    private void OnExitPanelYes()
    {
        if (_exitPanel is not null) _exitPanel.Visible = false;
        _pin = "";
        UpdatePinDisplay();
        Scramble();
        RebuildKeypad();
        GD.Print("[PinSubView] ExitPanel Yes: confirmed cancel; emitting Cancelled. spec: §3.");
        EmitSignal(SignalName.Cancelled);
    }

    private void OnExitPanelNo()
    {
        if (_exitPanel is not null) _exitPanel.Visible = false;
        GD.Print("[PinSubView] ExitPanel No: resume PIN entry. spec: §3.");
    }

    private void BuildControlButtons()
    {
        BuildButton(
            LoginLayout.PinResetX, LoginLayout.PinResetY, LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            TagReset);

        BuildButton(
            LoginLayout.PinOkX, LoginLayout.PinOkY, LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            TagOk);

        BuildButton(
            LoginLayout.PinCancelX, LoginLayout.PinCancelY, LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            TagCancel);
    }
}