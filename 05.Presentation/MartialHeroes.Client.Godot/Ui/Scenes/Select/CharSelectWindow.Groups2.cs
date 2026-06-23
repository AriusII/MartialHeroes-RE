using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private void BuildGroup7PerSlotControls()
    {
        _perSlotControls = NewContainer("PerSlotControls", 8, 46, 244, 360, false);

        AddButtonAction(_perSlotControls, AtlasLoginWindow, 48, 109, 25, 18,
            483, 551, 483, 551, 483, 551, ActFaceMinus);
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 73, 109, 25, 18,
            483, 576, 483, 576, 483, 576, ActFacePlus);
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 48, 153, 25, 18,
            483, 551, 483, 551, 483, 551, ActSpinner25);
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 73, 153, 25, 18,
            483, 576, 483, 576, 483, 576, ActSpinner26);

        int[] rowY = [191, 215, 239, 263, 287];
        for (var r = 0; r < 5; r++)
            AddButtonAction(_perSlotControls, AtlasLoginWindow, 154, rowY[r], 24, 16,
                500, 548, 500, 548, 500, 548, ActColA0 + r);

        for (var r = 0; r < 5; r++)
            AddButtonAction(_perSlotControls, AtlasLoginWindow, 178, rowY[r], 24, 16,
                524, 572, 524, 572, 524, 572, ActColB0 + r);

        AddButtonPlain(_perSlotControls, AtlasLoginWindow, 42, 325, 59, 20, 354, 413);
        AddButtonPlain(_perSlotControls, AtlasLoginWindow, 112, 325, 59, 20, 472, 531);

        _perSlotControls.Visible = false;
    }


    private void BuildGroup8InnerSubPanel()
    {
        const int sx = 8;
        const int sy = 46;
        _innerSubPanel = NewContainer("InnerSubPanel", sx, sy, 244, 440, false);

        AddPanel(_innerSubPanel, AtlasLoginWindow, 0, 0, 244, 187, 0, 0, false);
        AddImage(_innerSubPanel, AtlasLoginWindow, 0, 0, 215, 147, 556, 542);
        AddImage(_innerSubPanel, AtlasLoginWindow, 215, 0, 29, 22, 556, 729);
        AddImage(_innerSubPanel, AtlasLoginWindow, 0, 147, 29, 40, 556, 689);

        int[] tabY = [30, 30, 30, 30];
        int[] tabSrcY = [770, 815, 860, 905];
        int[] tabX = [0, 46, 92, 138];
        for (var i = 0; i < 4; i++)
            AddButtonAction(_innerSubPanel, AtlasLoginWindow, tabX[i], tabY[i], 45, 19,
                590, tabSrcY[i], 770, tabSrcY[i], 590, tabSrcY[i], ActClass0 + i);

        _createFaceLabelWidget = AddLabel(_innerSubPanel, 8, 72, 24, 12,
            _createFaceIndex.ToString(), LblWhite);
        AddLabel(_innerSubPanel, 8, 86, 12, 12, string.Empty, LblWhite);
        AddLabel(_innerSubPanel, 8, 100, 12, 12, string.Empty, LblWhite);

        AddButtonPlain(_innerSubPanel, AtlasLoginWindow, 8, 110, 59, 20, 354, 413);
        AddButtonPlain(_innerSubPanel, AtlasLoginWindow, 72, 110, 59, 20, 472, 531);

        _createDescLine0Widget = AddLabel(_innerSubPanel, 8, 148, 215, 274,
            string.Empty, LblWhite, 0, true);
        _createDescLine1Widget = AddLabel(_innerSubPanel, 8, 148, 12, 12, string.Empty, LblWhite);

        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 388, 27, 18,
            500, 674, 500, 674, 500, 674, ActAppSpin66);
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 40, 388, 27, 18,
            500, 701, 500, 701, 500, 701, ActAppSpin67);

        _createDescLine2Widget = AddLabel(_innerSubPanel, 8, 410, 190, 14,
            string.Empty, LblWhite, 0, true);

        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 576, 27, 18,
            500, 674, 500, 674, 500, 674, ActAppSpin68);
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 40, 576, 27, 18,
            500, 701, 500, 701, 500, 701, ActAppSpin69);

        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 600, 80, 60,
            500, 820, 500, 820, 500, 820, ActNav70);
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 92, 600, 80, 60,
            500, 820, 500, 820, 500, 820, ActNav71);
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 664, 30, 30,
            500, 600, 500, 600, 500, 600, ActToggle72);
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 42, 664, 30, 30,
            500, 600, 500, 600, 500, 600, ActToggle73);

        _innerSubPanel.Visible = false;
    }


    private void BuildGroup9CloseAndChrome()
    {
        var close = AddButtonPlain(this, AtlasTradeKeep, 971, 610, 23, 23, 0, 0);
        if (close.GetControl() is { } closeCtrl)
            closeCtrl.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                    OnAction(ActCornerClose);
            };

        _noticeSmall = NewContainer("NoticeSmall", 430, 100, 176, 42, true);
        AddPanel(_noticeSmall, AtlasInventWindow, 0, 0, 176, 42, 132, 295, true);
        _noticeSmall.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                OnAction(ActNoticePanel);
        };
        _wActionBindings++;
        _noticeSmall.Visible = false;

        AddTextbox(_noticeSmall, 54, 60, 140, 12, 0, 0);

        AddLabel(_noticeSmall, 8, 8, 160, 12, string.Empty, LblWarm, 4);
    }


    private void BuildGroup10CreateNameModal()
    {
        _modalCreateName = NewModalContainer("ModalCreateName");
        AddPanel(_modalCreateName, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        _nameModalTitleWidget = AddLabel(_modalCreateName, 70, 70, 200, 12, string.Empty, LblWarm);
        if (_nameModalTitle is not null) _nameModalTitle.HorizontalAlignment = HorizontalAlignment.Center;
        AddButtonAction(_modalCreateName, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateConfirm);
        AddButtonAction(_modalCreateName, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateCancel);
        _nameToastWidget = AddLabel(_modalCreateName, 30, 100, 280, 12, string.Empty,
            new Color(1.0f, 0.35f, 0.20f));
        AddLabel(_modalCreateName, 30, 100, 12, 12, string.Empty, LblWhite);

        _nameEntry = new LineEdit
        {
            Name = "CreateNameEntry",
            Position = new Vector2(40, 50),
            Size = new Vector2(260, 18)
        };
        _nameEntry.AddThemeFontSizeOverride("font_size", 13);
        _modalCreateName.AddChild(_nameEntry);

        _modalCreateName.Visible = false;
    }


    private void BuildGroup11RenameModal()
    {
        _modalRename = NewModalContainer("ModalRename");
        AddPanel(_modalRename, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        AddButtonAction(_modalRename, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActRenameOk);
        AddButtonAction(_modalRename, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActRenameCancel);
        _renameModalTitleWidget =
            AddLabel(_modalRename, 25, 20, 290, 12, string.Empty, LblWarm);
        AddLabel(_modalRename, 25, 77, 12, 12, string.Empty, LblWhite);
        AddImage(_modalRename, AtlasInventWindow, 55, 75, 274, 18, 419, 1003);
        AddTextbox(_modalRename, 60, 80, 274, 18, 16, 0);
        _renameEntry = new LineEdit
        {
            Name = "RenameNameEntry",
            Position = new Vector2(60, 80),
            Size = new Vector2(274, 18)
        };
        _renameEntry.AddThemeFontSizeOverride("font_size", 13);
        _modalRename.AddChild(_renameEntry);
        _renameToastWidget = AddLabel(_modalRename, 20, 110, 300, 20, string.Empty,
            new Color(1.0f, 0.35f, 0.20f), 2);

        _modalRename.Visible = false;
    }


    private void ApplyOpenVisibility()
    {
        if (_perSlotControls is not null) _perSlotControls.Visible = false;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = false;
        if (_rightDetail is not null) _rightDetail.Visible = true;
        if (_topStrip is not null) _topStrip.Visible = true;
        if (_bottomRow is not null) _bottomRow.Visible = true;
    }
}