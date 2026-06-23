using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    private const int ErrorMsgAction = 670;
    private const int ErrorOkAction = 671;

    private const int ErrorMsgRelX = 0;
    private const int ErrorMsgRelY = 89;
    private const int ErrorMsgW = 340;
    private const int ErrorMsgH = 20;

    private const int ErrorOkRelX = 125;
    private const int ErrorOkRelY = 151;
    private const int ErrorOkW = 90;
    private const int ErrorOkH = 25;
    private const int ErrorOkNormSrcX = 417;
    private const int ErrorOkNormSrcY = 943;
    private const int ErrorOkHoverSrcX = 507;
    private const int ErrorOkHoverSrcY = 943;

    private const uint MsgOkCaption = 101;

    private const double ErrorBudgetStartMs = 3000.0;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            {
                OnEnterKey();
                AcceptEvent();
            }
            else if (key.Keycode == Key.Tab && _credentialGroup?.Visible == true)
            {
                if (_idBox?.HasFocus() == true)
                    _pwBox?.GrabFocus();
                else
                    _idBox?.GrabFocus();
                AcceptEvent();
            }
        }
    }


    private void OnAction(int actionId)
    {
        if (_flowSubState == 2) SnapCurtainOpen();

        switch (actionId)
        {
            case LoginLayout.ActionAppQuit:
                EmitSignal(SignalName.QuitRequested);
                break;

            case LoginLayout.ActionOk:
                if (_flowSubState == 6) RunState(29);
                break;

            case LoginLayout.ActionConfirm:
            case LoginLayout.ActionOptionTab2:
                ShowExitConfirm();
                break;

            case 105:
                if (_flowSubState != 35
                    && Time.GetTicksMsec() >= _lastServerFetchMs + ServerFetchThrottleMs)
                {
                    _lastServerFetchMs = Time.GetTicksMsec();
                    RestartServerFetch();
                }

                break;

            case LoginLayout.ActionSaveId:
                break;

            case LoginLayout.ActionOptionTab1:
                RunState(5);
                break;

            case LoginLayout.ActionQuitConfirmYes1
                :
            case LoginLayout.ActionQuitConfirmYes2:
                HideQuitModal();
                EmitSignal(SignalName.ConnectCancelled);
                RestartServerFetch();
                break;

            case ActionExitNo:
                HideExitConfirm();
                break;
        }
    }

    private void RestartServerFetch()
    {
        DoEnsureServerSelect();
        RunState(34);
    }

    private void OnEnterKey()
    {
        if (_flowSubState == 6) RunState(29);
    }


    private void RunValidation()
    {
        var account = _idBox?.Text ?? "";
        var password = _pwBox?.Text ?? "";

        if (account.Length < LoginLayout.MinIdLength)
        {
            GD.PrintErr(
                $"[LoginWindow] ID too short (len={account.Length}). Raising ErrorPanel msg 4025. spec: §2.1a/§2.4.");
            RunState(6);
            ShowErrorPanel((int)LoginLayout.MsgErrShortId);
            return;
        }

        if (password.Length < LoginLayout.MinPwLength)
        {
            GD.PrintErr("[LoginWindow] PW empty. Raising ErrorPanel msg 4026. spec: §2.1a/§2.4.");
            RunState(6);
            ShowErrorPanel((int)LoginLayout.MsgErrEmptyPassword);
            return;
        }

        if (_saveIdChecked) PersistSaveId(account);

        GD.Print($"[LoginWindow] LoginAccepted (account='{account}'). spec: login_flow.md §4.2.");
        EmitSignal(SignalName.LoginAccepted, account, password);

        RunState(31);
    }

    private void BuildErrorPanel()
    {
        var panel = new Control
        {
            Name = "ErrorPanel",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };

        var chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        var msgLabel = new Label
        {
            Name = "ErrorMsgLabel",
            Position = new Vector2(ErrorMsgRelX, ErrorMsgRelY),
            Size = new Vector2(ErrorMsgW, ErrorMsgH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        msgLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(msgLabel);
        _errorMsgLabel = msgLabel;

        Texture2D? okNorm = _atlas.SliceByPath(LoginLayout.AtlasInventWindow,
            ErrorOkNormSrcX, ErrorOkNormSrcY, ErrorOkW, ErrorOkH);
        Texture2D? okHover = _atlas.SliceByPath(LoginLayout.AtlasInventWindow,
            ErrorOkHoverSrcX, ErrorOkHoverSrcY, ErrorOkW, ErrorOkH);

        var okBtn = new TextureButton
        {
            Name = "ErrorOkButton",
            Position = new Vector2(ErrorOkRelX, ErrorOkRelY),
            Size = new Vector2(ErrorOkW, ErrorOkH),
            CustomMinimumSize = new Vector2(ErrorOkW, ErrorOkH),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = okNorm,
            TextureHover = okHover,
            TexturePressed = okNorm,
            TextureDisabled = okNorm
        };
        okBtn.Pressed += OnErrorOkPressed;
        panel.AddChild(okBtn);
        _errorOkBtnNode = okBtn;

        var okLabel = new Label
        {
            Name = "ErrorOkBtnLabel",
            Position = new Vector2(ErrorOkRelX, ErrorOkRelY),
            Size = new Vector2(ErrorOkW, ErrorOkH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        okLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(okLabel);
        _errorOkBtnLabel = okLabel;

        AddChild(panel);
        _errorPanel = panel;
    }

    private void ShowErrorPanel(int msgId)
    {
        if (_errorPanel is null) return;

        if (_errorMsgLabel is not null)
            _errorMsgLabel.Text = _text.GetCaption(msgId);

        _errorN = 3;
        _errorBudgetMs = ErrorBudgetStartMs;
        _errorLastDecrementMs = Time.GetTicksMsec();
        RebuildErrorOkCaption();

        _errorPanel.Visible = true;
        GD.Print($"[LoginWindow] ErrorPanel shown (msgId={msgId}, N=3). spec: §2.1a.");
    }

    private void RebuildErrorOkCaption()
    {
        if (_errorOkBtnLabel is null) return;
        var okText = _text.GetCaption((int)MsgOkCaption, "확인");
        _errorOkBtnLabel.Text = $"{okText} - {_errorN}";
    }

    private void HideErrorPanel()
    {
        if (_errorPanel is not null) _errorPanel.Visible = false;
        GD.Print("[LoginWindow] ErrorPanel hidden. spec: §2.1a.");
    }

    private void OnErrorOkPressed()
    {
        HideErrorPanel();
    }

    private void TickErrorCountdown()
    {
        var nowMs = Time.GetTicksMsec();
        if (nowMs >= _errorLastDecrementMs + 1000u && _errorBudgetMs > 0)
        {
            _errorBudgetMs -= 1000.0;
            _errorN = (int)(_errorBudgetMs / 1000.0);
            _errorLastDecrementMs = nowMs;
            RebuildErrorOkCaption();
        }

        if (_errorBudgetMs <= 0 && _errorPanel?.Visible == true)
            HideErrorPanel();
    }
}