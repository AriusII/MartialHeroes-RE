using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    private const int ActionExitNo = 51;

    private void BuildPinYesNoPanel()
    {
        _pinYesNoPanel = new Control
        {
            Name = "PinYesNoPanel",
            Position = new Vector2(356f, 531f),
            Size = new Vector2(313f, 132f),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };

        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate1.X, LoginLayout.DecoPlate1.Y,
            LoginLayout.DecoPlate1.W, LoginLayout.DecoPlate1.H,
            LoginLayout.DecoPlate1.SrcX, LoginLayout.DecoPlate1.SrcY);

        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate2.X, LoginLayout.DecoPlate2.Y,
            LoginLayout.DecoPlate2.W, LoginLayout.DecoPlate2.H,
            LoginLayout.DecoPlate2.SrcX, LoginLayout.DecoPlate2.SrcY);

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

        var chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

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

        var yes = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            40, 136, 110, 38,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            LoginLayout.ActionAppQuit, fontSlot: 0);
        yes.ActionFired += OnAction;
        var yesCtrl = yes.GetControl();
        if (yesCtrl is not null) panel.AddChild(yesCtrl);

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
        var panel = new Control
        {
            Name = nodeName,
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

        var prompt = _text.GetCaption(promptMsgId);
        var promptLabel = new Label
        {
            Name = "PromptLabel",
            Text = prompt,
            Position = new Vector2(LoginLayout.ConfirmLabelX, LoginLayout.ConfirmLabelY),
            Size = new Vector2(LoginLayout.ConfirmLabelW, LoginLayout.ConfirmLabelH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        promptLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(promptLabel);

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

    private void ShowConnectingPopup()
    {
        if (_quitModal is not null) _quitModal.Visible = true;
    }

    public void NotifyConnectFailed()
    {
        HideQuitModal();
        GD.PrintErr("[LoginWindow] Connect failed → showing error (msg 4028) + returning to server list. " +
                    "spec: frontend_layout_tables.md §2.1a/§4.");
        RaiseServerListError(true);
    }

    public void NotifyConnectSuccess()
    {
        GD.Print("[LoginWindow] Connect succeeded → LoginFlowCompleted (state 41). spec: §2.2/§2.6.");
        RunState(41);
    }
}