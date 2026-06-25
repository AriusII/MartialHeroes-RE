using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    private void BuildCurtainPanels()
    {
        var top = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BackgroundPanel.X, LoginLayout.BackgroundPanel.Y,
            LoginLayout.BackgroundPanel.W, LoginLayout.BackgroundPanel.H,
            LoginLayout.BackgroundPanel.SrcX, LoginLayout.BackgroundPanel.SrcY);
        if (top is not null)
        {
            top.Name = "CurtainTop";
            _curtainTop = top;
            AddChild(top);
        }

        var bot = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            0, CurtainBotBaseY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY);
        if (bot is not null)
        {
            bot.Name = "CurtainBot";
            _curtainBot = bot;
            AddChild(bot);
        }
    }


    private void BuildBackgroundLayer()
    {
        _backgroundLayer = new Control
        {
            Name = "BackgroundLayer",
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _backgroundLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var backdrop = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.X, LoginLayout.MainPanel.Y,
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY);
        if (backdrop is not null) _backgroundLayer.AddChild(backdrop);

        AddChild(_backgroundLayer);
    }


    private void BuildBannerFrame()
    {
        _bannerFrame = new Control
        {
            Name = "BannerFrame",
            Position = new Vector2(LoginLayout.ServerListbox.X, LoginLayout.ServerListbox.Y),
            Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };

        var frameArt = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            0, 0,
            LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H,
            LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY);
        if (frameArt is not null)
        {
            frameArt.Name = "BannerFrameArt";
            _bannerFrame.AddChild(frameArt);
        }

        var logo = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.ListboxHeader.X, LoginLayout.ListboxHeader.Y,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H,
            LoginLayout.ListboxHeader.SrcX, LoginLayout.ListboxHeader.SrcY);
        if (logo is not null)
        {
            logo.Name = "BannerLogo";
            _bannerFrame.AddChild(logo);
        }

        AddChild(_bannerFrame);
    }


    private void BuildNoticePanel()
    {
        _noticePanel = new Control
        {
            Name = "NoticePanel",
            Position = new Vector2(LoginLayout.ServerListbox.X, LoginLayout.ServerListbox.Y),
            Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };


        AddNoticeButton(LoginLayout.ScrollUpArrow, LoginLayout.ActionScrollUp);
        AddNoticeButton(LoginLayout.ScrollDownArrow, LoginLayout.ActionScrollDown);
        AddNoticeButton(LoginLayout.ScrollThumb, LoginLayout.ActionScrollThumb);

        for (var id = LoginLayout.MsgLabelFirst; id <= LoginLayout.MsgLabelLast; id++)
        {
            var caption = _text.GetCaption((int)id);
            if (caption.Length == 0) continue;
            var k = (int)(id - LoginLayout.MsgLabelFirst);
            var label = new Label
            {
                Name = $"NoticeLabel{k}",
                Text = caption,
                Position = new Vector2(LoginLayout.NoticeLabelLocalX,
                    LoginLayout.NoticeLabelStartY + k * LoginLayout.NoticeLabelStrideY),
                Size = new Vector2(LoginLayout.NoticeLabelW, LoginLayout.NoticeLabelH),
                AutowrapMode = TextServer.AutowrapMode.Off,
                MouseFilter = MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            _noticePanel.AddChild(label);
        }

        AddChild(_noticePanel);
    }

    private void AddNoticeButton(WidgetRect rect, int actionId)
    {
        var button = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            rect.X, rect.Y, rect.W, rect.H,
            rect.SrcX, rect.SrcY, rect.SrcX, rect.SrcY,
            actionId, fontSlot: 0);
        button.ActionFired += OnAction;
        var control = button.GetControl();
        if (control is not null) _noticePanel?.AddChild(control);
    }


    private void BuildServerListRoot()
    {
        _serverListRoot = new Control
        {
            Name = "ServerListRoot",
            Position = Vector2.Zero,
            Size = new Vector2(1024f, 768f),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };


        AddChild(_serverListRoot);
    }

    private void BuildPinKeypadRoot()
    {
        _pinKeypadRoot = new Control
        {
            Name = "PinKeypadRoot",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };
        _pinKeypadRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_pinKeypadRoot);
    }


    private void BuildFormGroup()
    {
        _formGroup = new Control
        {
            Name = "FormGroup",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };
        _formGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_formGroup);

        var formPanel = new Control
        {
            Name = "FormPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass
        };
        _formGroup.AddChild(formPanel);
        _formPanel = formPanel;

        var decoPlate = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H,
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY);
        if (decoPlate is not null)
        {
            decoPlate.Name = "FormDecoPlate";
            decoPlate.Visible = false;
            decoPlate.Position = new Vector2(LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y);
            formPanel.AddChild(decoPlate);
            _formDecoPlate = decoPlate;
        }

        var serverBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
            LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
            LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY,
            LoginLayout.ConfirmHoverSrcX, LoginLayout.ConfirmHoverSrcY,
            LoginLayout.ActionConfirm, fontSlot: 0);
        serverBtn.ActionFired += OnAction;
        var serverCtrl = serverBtn.GetControl();
        if (serverCtrl is not null)
        {
            serverCtrl.Name = "ServerSubmitButton";
            formPanel.AddChild(serverCtrl);
        }

        _serverListStripDeco = AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitDecoPlate.X, LoginLayout.QuitDecoPlate.Y,
            LoginLayout.QuitDecoPlate.W, LoginLayout.QuitDecoPlate.H,
            LoginLayout.QuitDecoPlate.SrcX, LoginLayout.QuitDecoPlate.SrcY);

        var quitBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY,
            LoginLayout.QuitHoverSrcX, LoginLayout.QuitHoverSrcY,
            105, fontSlot: 0);
        quitBtn.ActionFired += OnAction;
        var quitCtrl = quitBtn.GetControl();
        if (quitCtrl is not null)
        {
            quitCtrl.Name = "HelpQuitButton";
            _serverListStrip = quitCtrl;
            formPanel.AddChild(quitCtrl);
        }
    }


    private void BuildCredentialGroup()
    {
        _credentialGroup = new Control
        {
            Name = "CredentialGroup",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false
        };
        _credentialGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_credentialGroup);

        var credPanel = new Control
        {
            Name = "CredPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass
        };
        _credentialGroup.AddChild(credPanel);
        _credPanel = credPanel;

        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H,
            LoginLayout.AccountLabelArt.SrcX, LoginLayout.AccountLabelArt.SrcY);

        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H,
            LoginLayout.PasswordLabelArt.SrcX, LoginLayout.PasswordLabelArt.SrcY);

        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H,
            LoginLayout.SmallDecorPlate.SrcX, LoginLayout.SmallDecorPlate.SrcY);

        _idBox = new MaskedTextField(
            _atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.AccountBox.H,
            LoginLayout.AccountBox.SrcX, LoginLayout.AccountBox.SrcY,
            false,
            LoginLayout
                .IdTextboxKeystrokeCap);
        _idBox.Name = "IdTextbox";
        _idBox.TextSubmitted += OnEnterKey;
        credPanel.AddChild(_idBox);

        _pwBox = new MaskedTextField(
            _atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.PasswordBox.H,
            LoginLayout.PasswordBox.SrcX, LoginLayout.PasswordBox.SrcY,
            true,
            LoginLayout
                .PwTextboxKeystrokeCap);
        _pwBox.Name = "PwTextbox";
        _pwBox.TextSubmitted += OnEnterKey;
        credPanel.AddChild(_pwBox);

        var okBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY,
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY,
            LoginLayout.ActionOk, fontSlot: 0);
        okBtn.ActionFired += OnAction;
        var okCtrl = okBtn.GetControl();
        if (okCtrl is not null)
        {
            okCtrl.Name = "OkButton";
            credPanel.AddChild(okCtrl);
        }

        _saveIdCheck = HudWidgetFactory.MakeCheckbox(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.SaveIdCheck.X, LoginLayout.SaveIdCheck.Y,
            LoginLayout.SaveIdCheck.W, LoginLayout.SaveIdCheck.H,
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY,
            LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY,
            LoginLayout.ActionSaveId);
        if (_saveIdCheck is HudCheckbox chk)
        {
            chk.Toggled += OnSaveIdToggled;
            chk.IsChecked = _saveIdChecked;
        }

        var chkCtrl = _saveIdCheck?.GetControl();
        if (chkCtrl is not null)
        {
            chkCtrl.Name = "SaveIdCheckbox";
            credPanel.AddChild(chkCtrl);
        }

        if (_savedId.Length > 0 && _idBox is not null)
            _idBox.Text = _savedId;
    }

    private TextureRect? AddRect(Control parent, string atlas, int x, int y, int w, int h, int srcX, int srcY)
    {
        var r = HudWidgetFactory.MakeAtlasRect(_atlas, atlas, x, y, w, h, srcX, srcY);
        if (r is not null) parent.AddChild(r);
        return r;
    }
}