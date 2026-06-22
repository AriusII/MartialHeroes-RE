// Ui/Scenes/Login/LoginWindow.Modals.cs
//
// Partial: modal/overlay panel construction and show/hide — PIN yes/no, quit-confirm (Confirm-A/B),
//          exit-confirm (ExitPanel), connecting popup.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.2 / §4

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // ExitPanel "no" action (close the quit-confirm). Local id; the binary's "yes" is 101.
    private const int ActionExitNo = 51;
    // -------------------------------------------------------------------------
    // PIN yes/no panel
    // spec: §2.1 "PIN yes/no panel | 0,356,531,313 | init hidden"
    // -------------------------------------------------------------------------

    private void BuildPinYesNoPanel()
    {
        // Connexion/Quitter panel: origin (356,531) size 313×132 — G2 debugger-confirmed 2026 / IDB 263bd994.
        // Supersedes prior (0,356) 531×313 reading which confused the panel dimensions with its position.
        // Children (local coords): strip abs(356,631)=panel+(0,100); Connexion abs(396,613)=panel+(40,82);
        // Quitter abs(520,613)=panel+(164,82). spec: Docs/RE/specs/frontend_layout_tables.md §2.1.
        _pinYesNoPanel = new Control
        {
            Name = "PinYesNoPanel",
            Position = new Vector2(356f, 531f), // spec: frontend_layout_tables.md §2.1 G2-confirmed origin (356,531)
            Size = new Vector2(313f, 132f), // spec: frontend_layout_tables.md §2.1 G2-confirmed size 313×132
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false // always hidden per spec §2.1.
        };

        // Prompt plate A: A1 dst(67,48,178,13) src(0,437). spec §2.1.
        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate1.X, LoginLayout.DecoPlate1.Y,
            LoginLayout.DecoPlate1.W, LoginLayout.DecoPlate1.H,
            LoginLayout.DecoPlate1.SrcX, LoginLayout.DecoPlate1.SrcY);

        // Prompt plate B: A1 dst(0,100,313,32) src(289,437). spec §2.1.
        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate2.X, LoginLayout.DecoPlate2.Y,
            LoginLayout.DecoPlate2.W, LoginLayout.DecoPlate2.H,
            LoginLayout.DecoPlate2.SrcX, LoginLayout.DecoPlate2.SrcY);

        // Yes button: A2 (40,82,110,38) N(520,492) P(635,492). action 111. spec §2.1.
        var yesBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab1.X, LoginLayout.OptionTab1.Y,
            LoginLayout.OptionTab1.W, LoginLayout.OptionTab1.H,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            LoginLayout.ActionOptionTab1, fontSlot: 0);
        yesBtn.ActionFired += OnAction;
        var yesCtrl = yesBtn.GetControl();
        if (yesCtrl is not null) _pinYesNoPanel.AddChild(yesCtrl);

        // No button: A2 (164,82,110,38) N(750,492) P(865,492). action 112. spec §2.1.
        var noBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab2.X, LoginLayout.OptionTab2.Y,
            LoginLayout.OptionTab2.W, LoginLayout.OptionTab2.H,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY,
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY,
            LoginLayout.ActionOptionTab2, fontSlot: 0);
        noBtn.ActionFired += OnAction;
        var noCtrl = noBtn.GetControl();
        if (noCtrl is not null) _pinYesNoPanel.AddChild(noCtrl);

        AddChild(_pinYesNoPanel);
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modals
    // spec: §2.1 "Exit (quit) modal / Error modal | 342,289,340,190 | A3 src(318,647) | init hidden"
    //       "Confirm-A/B panel | 342,289,340,190 | A3 src(318,647) | init hidden"
    // -------------------------------------------------------------------------

    private void BuildQuitModals()
    {
        _quitModal = BuildConfirmModalPanel(
            LoginLayout.MsgQuitConfirm1,
            LoginLayout.QuitConfirmYes1,
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY,
            LoginLayout.ActionQuitConfirmYes1, "QuitModal1");
        AddChild(_quitModal);

        _quitModal2 = BuildConfirmModalPanel(
            LoginLayout.MsgQuitConfirm2,
            LoginLayout.QuitConfirmYes2,
            LoginLayout.QuitConfirmYes2HoverSrcX, LoginLayout.QuitConfirmYes2HoverSrcY,
            LoginLayout.ActionQuitConfirmYes2, "QuitModal2");
        AddChild(_quitModal2);

        _exitConfirm = BuildExitConfirmPanel();
        AddChild(_exitConfirm);
    }

    // Quit-confirm ExitPanel (msg 2007): chrome + prompt + Yes(101 → quit) + No(51 → close).
    // Opened by action 102/112. spec: Docs/RE/specs/frontend_layout_tables.md §2.2.
    private Control BuildExitConfirmPanel()
    {
        var panel = new Control
        {
            Name = "ExitConfirmPanel",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };

        // Shared InventWindow.dds chrome src(318,647,340,190).
        var chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        // Prompt label (msg 2007). Fallback = empty — real client only shows CP949 from msg.xdb.
        var prompt = _text.GetCaption(LoginLayout.MsgExitConfirm);
        var promptLabel = new Label
        {
            Name = "PromptLabel",
            Text = prompt,
            Position = new Vector2(LoginLayout.ModalPromptX, LoginLayout.ModalPromptY),
            Size = new Vector2(LoginLayout.ModalPromptW, LoginLayout.ModalPromptH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        promptLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(promptLabel);

        // Yes button (action 101 → quit) — yes/no plate art (loginwindow.dds 520,492 / 635,492).
        // Positioning: (40,136,110,38). The ExitPanel is distinct from Confirm-A/B (whose OK is at
        // (120,136,113,40)). The ExitPanel Yes/No pair geometry (40/190) is a port choice — the spec
        // (frontend_layout_tables.md §2.1) pins the YES/NO art from the "PIN yes/no panel" children
        // at (40,82)/(164,82) but does not separately pin the ExitPanel Yes/No absolute positions.
        // No capture to verify; marked provisional. spec: frontend_layout_tables.md §2.1 (partial).
        var yes = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            40, 136, 110, 38,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            LoginLayout.ActionAppQuit, fontSlot: 0);
        yes.ActionFired += OnAction;
        var yesCtrl = yes.GetControl();
        if (yesCtrl is not null) panel.AddChild(yesCtrl);

        // No button (action 51 → close) — plate art (loginwindow.dds 750,492 / 865,492).
        // Positioning: (190,136,110,38). Same ExitPanel provisional geometry note as Yes above.
        // spec: frontend_layout_tables.md §2.1 (partial — ExitPanel Yes/No positions not pinned by spec).
        var no = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            190, 136, 110, 38,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY,
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY,
            ActionExitNo, fontSlot: 0);
        no.ActionFired += OnAction;
        var noCtrl = no.GetControl();
        if (noCtrl is not null) panel.AddChild(noCtrl);

        return panel;
    }

    private void ShowExitConfirm()
    {
        if (_exitConfirm is not null) _exitConfirm.Visible = true;
    }

    private void HideExitConfirm()
    {
        if (_exitConfirm is not null) _exitConfirm.Visible = false;
    }

    private Control BuildConfirmModalPanel(
        uint promptMsgId, WidgetRect yesRect,
        int hoverSrcX, int hoverSrcY, int actionId, string nodeName)
    {
        // spec: §2.1 "Confirm-A panel | 342,289,340,190 | A3 src(318,647) | init hidden".
        var panel = new Control
        {
            Name = nodeName,
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };

        // Chrome: InventWindow.dds (A3) src(318,647,340,190) at panel-local (0,0). spec §2.1.
        var chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        // Prompt label: msg.xdb 4023/4024 — panel-local (10,100,330,20), center-aligned.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 "Confirm-A label (msg 4023) | label center | 10 | 100 | 330 | 20"
        // Fallback = empty — real client only shows CP949 from msg.xdb.
        var prompt = _text.GetCaption(promptMsgId);
        var promptLabel = new Label
        {
            Name = "PromptLabel",
            Text = prompt,
            Position = new Vector2(LoginLayout.ConfirmLabelX, LoginLayout.ConfirmLabelY), // spec §2.1 (10,100)
            Size = new Vector2(LoginLayout.ConfirmLabelW, LoginLayout.ConfirmLabelH), // spec §2.1 (330,20)
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        promptLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(promptLabel);

        // OK button: A3 dst(120,136,113,40). spec §2.1 "Confirm-A OK | N302,900/P415,900 | action 113".
        var yes = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasInventWindow,
            yesRect.X, yesRect.Y, yesRect.W, yesRect.H,
            yesRect.SrcX, yesRect.SrcY, hoverSrcX, hoverSrcY,
            actionId, fontSlot: 0);
        yes.ActionFired += OnAction;
        var yesControl = yes.GetControl();
        if (yesControl is not null)
        {
            yesControl.Name = "ConfirmOk";
            panel.AddChild(yesControl);
        }

        return panel;
    }

    private void HideQuitModal()
    {
        if (_quitModal is not null) _quitModal.Visible = false;
        if (_quitModal2 is not null) _quitModal2.Visible = false;
    }

    // Raise Confirm-A (msg 4023) as the "connecting" popup at sub-state 39.
    // Confirm-A is the same panel as the server-list re-fetch popup; "the connecting popup"
    // and "Confirm-A" are the same object.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §4
    //   "Confirm-A is the connecting popup … raised at sub-state 39 (CORRECTION 2026-06-19: 39 not 40)".
    private void ShowConnectingPopup()
    {
        if (_quitModal is not null) _quitModal.Visible = true;
    }

    /// <summary>
    ///     Called by LoginScene when the connect attempt fails.
    ///     Hides the connecting popup, shows the §2.1a countdown error box (msg 4028), and returns to
    ///     the server list (sub-state 34/37 per spec §4).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.1a / §2.2 / §4
    ///     "connect failure → msg 4028 countdown error box → return to server list (state 34)"
    /// </summary>
    public void NotifyConnectFailed()
    {
        HideQuitModal(); // hide the connecting popup. spec: §2.2 / §4.
        // spec: frontend_layout_tables.md §2.1a "fetch result −1 → msg 4028".
        // Re-use existing RaiseServerListError path for the §2.1a countdown box. spec: §2.1a.
        GD.PrintErr("[LoginWindow] Connect failed → showing error (msg 4028) + returning to server list. " +
                    "spec: frontend_layout_tables.md §2.1a/§4.");
        RaiseServerListError(true); // msg 4028 + state 37. spec: §2.1a.
    }

    /// <summary>
    ///     Called by LoginScene when the connect succeeds (char-list arriving → advance to char-select).
    ///     Emits LoginFlowCompleted to advance the scene; the popup dies with the scene teardown.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.2 state 41 / §4
    ///     "SUCCESS = inbound 3/1 char-list tears the scene down → char-select (state 4)"
    /// </summary>
    public void NotifyConnectSuccess()
    {
        // Popup stays visible (it dies with the scene on teardown). spec: §4 "never explicitly closed on success".
        GD.Print("[LoginWindow] Connect succeeded → LoginFlowCompleted (state 41). spec: §2.2/§2.6.");
        RunState(41); // emits LoginFlowCompleted. spec: §2.2.
    }
}