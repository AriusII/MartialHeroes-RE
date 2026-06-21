// Ui/Scenes/Login/LoginWindow.Input.cs
//
// Partial: input handling — _UnhandledKeyInput, OnAction, OnEnterKey, RunValidation,
//          RestartServerFetch, validation-error panel (§2.1a), error countdown.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.1a

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // -------------------------------------------------------------------------
    // Validation-error message box (§2.1a)
    // Geometry: A3 (InventWindow.dds) panel dst(342,289,340,190) src(318,647).
    // Centered message label dst panel+(0,89) 340×20 (action 670).
    // OK button: dst panel+(125,151) = abs (467,440) 90×25, A3 N(417,943)/H(507,943), action 671.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1a
    // -------------------------------------------------------------------------

    private const int ErrorMsgAction = 670;
    private const int ErrorOkAction = 671;

    // Panel-relative coords for the message label (center-aligned).
    // spec: §2.1a "centered message label: dst panel+(0,89) 340×20"
    private const int ErrorMsgRelX = 0;
    private const int ErrorMsgRelY = 89;
    private const int ErrorMsgW = 340;
    private const int ErrorMsgH = 20;

    // OK button panel-relative and absolute coords.
    // spec: §2.1a "dst panel+(125,151) = abs(467,440) 90×25; A3 N(417,943)/H(507,943)"
    private const int ErrorOkRelX = 125;
    private const int ErrorOkRelY = 151;
    private const int ErrorOkW = 90;
    private const int ErrorOkH = 25;
    private const int ErrorOkNormSrcX = 417; // spec §2.1a N(417,943)
    private const int ErrorOkNormSrcY = 943;
    private const int ErrorOkHoverSrcX = 507; // spec §2.1a H(507,943)
    private const int ErrorOkHoverSrcY = 943;

    // Caption: msg 101 ("확인") then " - N". spec §2.1a "caption = '<msg 101> - <N>'".
    private const uint MsgOkCaption = 101; // spec §2.1a

    // Start budget: 3000 ms → N=3. spec §2.1a "Start N=3. The Show call passes a 3000 ms budget."
    private const double ErrorBudgetStartMs = 3000.0;
    // -------------------------------------------------------------------------
    // Key input
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2
    // -------------------------------------------------------------------------

    // Window-level key handler. Covers Enter at ALL states (MaskedTextField fires TextSubmitted
    // AND AcceptEvent on Enter, so _UnhandledKeyInput will only see Enter when neither field is
    // focused — state 4). Also handles TAB to toggle ID/PW focus.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 "ENTER(10) → state6→OK, state4→5"
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 "TAB(9) toggles ID/PW focus"
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
                // TAB: toggle focus between ID and PW boxes.
                // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 "TAB(9) toggles ID/PW focus"
                if (_idBox?.HasFocus() == true)
                    _pwBox?.GrabFocus();
                else
                    _idBox?.GrabFocus();
                AcceptEvent();
            }
        }
    }

    // -------------------------------------------------------------------------
    // Action handler
    // spec: §2.2 "OnEvent action map"
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        // Any action while curtain is opening snaps it to done. spec: ui_system.md §7.7.
        if (_flowSubState == 2) SnapCurtainOpen();

        // Action map re-confirmed vs binary (LoginWindow_OnEvent) 2026-06-18.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.2.
        switch (actionId)
        {
            case LoginLayout.ActionAppQuit: // 101 — app quit (fired by the ExitPanel "yes").
                EmitSignal(SignalName.QuitRequested);
                break;

            case LoginLayout.ActionOk: // 103 — OK / Login button
                // spec: §2.2 "103 OK/login requires flowSubState==6, runs the game.ver gate → 29".
                if (_flowSubState == 6) RunState(29);
                break;

            case LoginLayout.ActionConfirm: // 102 — open quit-confirm ExitPanel (NOT server-list).
            case LoginLayout.ActionOptionTab2: // 112 — same target as 102 (open ExitPanel).
                ShowExitConfirm();
                break;

            case 105: // server-list re-fetch — throttled to 1/10 s, skipped while a fetch is in flight
                // (sub-state 35). spec §2.2.
                if (_flowSubState != 35
                    && Time.GetTicksMsec() >= _lastServerFetchMs + ServerFetchThrottleMs)
                {
                    _lastServerFetchMs = Time.GetTicksMsec();
                    RestartServerFetch();
                }

                break;

            case LoginLayout.ActionSaveId: // 104 — save-ID toggle (handled by checkbox).
                break;

            case LoginLayout.ActionOptionTab1: // 111 — advance (→ 5). spec §2.2.
                RunState(5);
                break;

            case LoginLayout.ActionQuitConfirmYes1
                : // 113 — Cancel connecting popup OR re-fetch popup → restart fetch (→34).
            case LoginLayout.ActionQuitConfirmYes2: // 114 — re-fetch popup OK → restart fetch (→34).
                // When state is 38/39 (connecting in progress), action 113 is the Cancel button of the
                // connecting popup: abort the join worker and return to the server list (state 34).
                // spec: frontend_layout_tables.md §2.2 "113/114 = hide confirm popup, restart fetch → 34";
                //        §4 "clicking [Cancel] aborts the join and returns to the server list (sub-state 34)".
                HideQuitModal();
                EmitSignal(SignalName.ConnectCancelled); // let LoginScene cancel the in-flight connect.
                RestartServerFetch();
                break;

            case ActionExitNo: // 51 — ExitPanel "no": close the quit-confirm.
                HideExitConfirm();
                break;
        }
        // Pager (115..124) and plate (400/401) actions handled inside ServerSelectSubView.
    }

    // 105 / 113 / 114: throttled restart of the server-list fetch (re-enter sub-state 34).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 (105 = restart fetch, ~10 s throttle).
    private void RestartServerFetch()
    {
        DoEnsureServerSelect();
        RunState(34); // ApplyVisibility(34..37) keeps _serverListRoot shown — the sole gate. spec: frontend_layout_tables.md §2.2.
    }

    private void OnEnterKey()
    {
        // spec: §2.2 "ENTER (10) at the rest (6) runs the OK path → 29" (state 4 is a transient auto-settle step).
        if (_flowSubState == 6) RunState(29);
    }

    // -------------------------------------------------------------------------
    // Validation (state 29)
    // spec: §2.2 "29 validate: ID length ≥4 … PW length ≠0 … both OK → 31"
    // -------------------------------------------------------------------------

    private void RunValidation()
    {
        var account = _idBox?.Text ?? "";
        var password = _pwBox?.Text ?? "";

        // ID length ≥ 4 (else msg 4025). spec §2.4 / §2.1a.
        // spec: frontend_layout_tables.md §2.1a "ID empty OR ID length<4 → msg 4025"
        if (account.Length < LoginLayout.MinIdLength)
        {
            GD.PrintErr(
                $"[LoginWindow] ID too short (len={account.Length}). Raising ErrorPanel msg 4025. spec: §2.1a/§2.4.");
            RunState(6); // reset to idle before showing error. spec §2.1a "first reset to sub-state 6"
            ShowErrorPanel((int)LoginLayout.MsgErrShortId); // spec §2.1a
            return;
        }

        // PW length ≠ 0 (else msg 4026). spec §2.4 / §2.1a.
        // spec: frontend_layout_tables.md §2.1a "PW empty → msg 4026"
        if (password.Length < LoginLayout.MinPwLength)
        {
            GD.PrintErr("[LoginWindow] PW empty. Raising ErrorPanel msg 4026. spec: §2.1a/§2.4.");
            RunState(6); // reset to idle before showing error. spec §2.1a
            ShowErrorPanel((int)LoginLayout.MsgErrEmptyPassword); // spec §2.1a
            return;
        }

        // Persist save-ID if checked. spec §2.5.
        if (_saveIdChecked) PersistSaveId(account);

        // Emit credential accepted. spec: login_flow.md §4.2.
        GD.Print($"[LoginWindow] LoginAccepted (account='{account}'). spec: login_flow.md §4.2.");
        EmitSignal(SignalName.LoginAccepted, account, password);

        // → state 31 (PIN raise). spec §2.2.
        RunState(31);
    }

    private void BuildErrorPanel()
    {
        // Panel chrome: A3 (InventWindow.dds) dst(342,289,340,190) src(318,647). spec §2.1a.
        // Same geometry as the other login modals (Exit / Confirm-A/B). init hidden.
        var panel = new Control
        {
            Name = "ErrorPanel",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false, // init hidden; shown by ShowErrorPanel only. spec §2.1a.
            MouseFilter = MouseFilterEnum.Pass
        };

        // Atlas chrome frame (same A3 src rect as other modals). spec §2.1a.
        var chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        // Error message label (action 670): center-aligned. spec §2.1a.
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

        // OK (countdown) button: A3 dst panel+(125,151) 90×25 N(417,943) H(507,943). spec §2.1a.
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
            TexturePressed = okNorm, // PRESSED = NORMAL per §0.12 convention
            TextureDisabled = okNorm
        };
        okBtn.Pressed += OnErrorOkPressed;
        panel.AddChild(okBtn);
        _errorOkBtnNode = okBtn;

        // Caption label overlaid on the button. spec §2.1a "caption = '<msg 101> - <N>'".
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

    // Show the error panel with the given msg.xdb id and start the 3 s countdown.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1a
    private void ShowErrorPanel(int msgId)
    {
        if (_errorPanel is null) return;

        // Set error message text. spec §2.1a "text from msg.xdb, action 670".
        if (_errorMsgLabel is not null)
            _errorMsgLabel.Text = _text.GetCaption(msgId);

        // Start countdown: N=3, budget=3000 ms. spec §2.1a "Start N=3; 3000 ms budget".
        _errorN = 3;
        _errorBudgetMs = ErrorBudgetStartMs;
        _errorLastDecrementMs = Time.GetTicksMsec();
        RebuildErrorOkCaption();

        _errorPanel.Visible = true;
        GD.Print($"[LoginWindow] ErrorPanel shown (msgId={msgId}, N=3). spec: §2.1a.");
    }

    // Rebuild the OK button caption: "<msg 101> - <N>". spec §2.1a.
    private void RebuildErrorOkCaption()
    {
        if (_errorOkBtnLabel is null) return;
        var okText = _text.GetCaption((int)MsgOkCaption, "확인"); // spec §2.1a "msg 101 = 확인"
        _errorOkBtnLabel.Text = $"{okText} - {_errorN}";
    }

    // Hide the error panel (early dismiss via OK, or auto-close at N=0). spec §2.1a.
    private void HideErrorPanel()
    {
        if (_errorPanel is not null) _errorPanel.Visible = false;
        GD.Print("[LoginWindow] ErrorPanel hidden. spec: §2.1a.");
    }

    private void OnErrorOkPressed()
    {
        // Early dismiss on OK click (action 671). spec §2.1a "clicking OK (671) hides immediately".
        HideErrorPanel();
    }

    // Per-frame tick for the validation-error countdown button.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1a
    //   "Tick source = per-frame ms wall-clock delta, throttled ≥1000 ms per decrement"
    private void TickErrorCountdown()
    {
        var nowMs = Time.GetTicksMsec();
        // spec §2.1a: "throttled to at most one decrement per 1000 ms"
        if (nowMs >= _errorLastDecrementMs + 1000u && _errorBudgetMs > 0)
        {
            _errorBudgetMs -= 1000.0;
            _errorN = (int)(_errorBudgetMs / 1000.0);
            _errorLastDecrementMs = nowMs;
            RebuildErrorOkCaption();
        }

        // Auto-close when budget expires. spec §2.1a "auto-close: hide at N=0".
        if (_errorBudgetMs <= 0 && _errorPanel?.Visible == true)
            HideErrorPanel();
    }
}