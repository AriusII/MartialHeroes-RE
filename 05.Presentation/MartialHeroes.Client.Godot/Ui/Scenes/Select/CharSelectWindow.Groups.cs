
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private static readonly Color LblWhite = new(0.92f, 0.92f, 0.95f);
    private static readonly Color LblWarm = new(0.95f, 0.86f, 0.55f);


    private void BuildGroup1RosterFrame()
    {
        const int frameX = 8;
        const int frameY = 560;
        _topStrip = NewContainer("RosterFrame", frameX, frameY, 577, 187, false);

        AddPanel(_topStrip, AtlasLoginWindow, 0, 0, 577, 58, 0, 0, false);
        AddPanel(_topStrip, AtlasLoginWindow, 0, 0, 244, 187, 0, 0, false);

        AddImage(_topStrip, AtlasLoginWindow, 0, 12, 200, 46, 608, 793);
        AddImage(_topStrip, AtlasLoginWindow, 200, 0, 176, 58, 608, 735);
        AddImage(_topStrip, AtlasLoginWindow, 376, 12, 201, 46, 608, 689);

        AddButtonAction(_topStrip, AtlasLoginWindow, 67, 17, 113, 40,
            483, 883, 483, 883, 483, 883, ActCmdCreateNew);
        AddButtonAction(_topStrip, AtlasLoginWindow, 232, 7, 113, 40,
            483, 923, 483, 923, 483, 923, ActCmdEnterGame);
        AddButtonAction(_topStrip, AtlasLoginWindow, 393, 17, 113, 40,
            483, 963, 483, 963, 483, 963, ActCmdCreateAlt);

        AddImage(_topStrip, AtlasLoginWindow, 0, 0, 215, 147, 556, 542);
        AddImage(_topStrip, AtlasLoginWindow, 215, 0, 29, 22, 556, 729);
        AddImage(_topStrip, AtlasLoginWindow, 0, 147, 29, 40, 556, 689);

        AddImage(_topStrip, AtlasLoginWindow, 20, 33, 34, 18, 771, 542);
        AddImage(_topStrip, AtlasLoginWindow, 20, 57, 34, 18, 771, 560);
        AddImage(_topStrip, AtlasLoginWindow, 20, 81, 34, 18, 771, 578);

        AddImage(_topStrip, AtlasLoginWindow, 50, 33, 157, 18, 140, 980);
        AddImage(_topStrip, AtlasLoginWindow, 50, 57, 157, 18, 140, 980);
        AddImage(_topStrip, AtlasLoginWindow, 50, 81, 157, 18, 140, 980);

        _infoNameWidget = AddLabel(_topStrip, 60, 37, 70, 12, string.Empty, LblWhite);
        _infoLevelWidget = AddLabel(_topStrip, 60, 61, 70, 12, string.Empty, LblWhite);
        _infoPositionWidget = AddLabel(_topStrip, 60, 85, 70, 12, string.Empty, LblWhite);

        _charCountCaptionWidget = AddLabel(this, 0, 12, (int)RefW, 28, string.Empty, LblWarm, 2);
        if (_charCountCaption is not null)
            _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
    }


    private void BuildGroup2CreateClassModal()
    {
        _modalCreateClass = NewModalContainer("ModalCreateClass");
        AddPanel(_modalCreateClass, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        AddLabel(_modalCreateClass, 35, 60, 12, 12, string.Empty, LblWhite);
        AddLabel(_modalCreateClass, 10, 100, 12, 12, string.Empty, LblWhite);
        AddButtonAction(_modalCreateClass, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateClassOk);
        AddButtonAction(_modalCreateClass, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateClassCancel);
        _modalCreateClass.Visible = false;
    }


    private void BuildGroup3NoticeModal()
    {
        _modalNotice = NewModalContainer("ModalNotice");
        AddPanel(_modalNotice, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        _noticeBodyWidget = AddLabel(_modalNotice, 0, 0, 340, 190, string.Empty, LblWhite, 0, true);
        if (_noticeBodyWidget.GetControl() is Label nb) nb.HorizontalAlignment = HorizontalAlignment.Center;
        AddButtonAction(_modalNotice, AtlasInventWindow, 230, 150, 113, 40,
            415, 860, 415, 860, 415, 900, ActNoticeOk);
        _modalNotice.Visible = false;
    }


    private void BuildGroup4DeleteConfirmModal()
    {
        _modalDeleteCfm = NewModalContainer("ModalDeleteConfirm");
        AddPanel(_modalDeleteCfm, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        _deleteTitleWidget = AddLabel(_modalDeleteCfm, 24, 60, 292, 12, string.Empty, LblWarm);
        _deleteBodyWidget = AddLabel(_modalDeleteCfm, 20, 100, 300, 12, string.Empty, LblWhite);
        AddButtonAction(_modalDeleteCfm, AtlasInventWindow, 120, 133, 113, 40,
            415, 860, 415, 860, 415, 900, ActDeleteConfirm);
        _modalDeleteCfm.Visible = false;
    }


    private void BuildGroup5BottomRow()
    {
        var parent = _topStrip ?? this;
        _bottomRow = NewContainer("RosterBottomRow", 8, 712, 577, 20, false);

        var overlay = AddButtonAction(_bottomRow, AtlasLoginWindow, 20, 0, 95, 20,
            500, 1004, 500, 1004, 500, 1004, ActConditionalOverlay);
        _overlayAction61 = overlay.GetControl();

        AddButtonAction(_bottomRow, AtlasLoginWindow, 130, 0, 59, 20,
            59, 1004, 59, 1004, 59, 1004, ActBottom4);
        AddButtonAction(_bottomRow, AtlasLoginWindow, 42, 0, 59, 20,
            118, 1004, 118, 1004, 118, 1004, ActBottom5);
        AddButtonAction(_bottomRow, AtlasLoginWindow, 112, 0, 59, 20,
            295, 1004, 295, 1004, 295, 1004, ActBottom6);

        _ = parent;
    }


    private void BuildGroup6RightDetail()
    {
        const int rx = 772;
        const int ry = 60;
        _rightDetail = NewContainer("RightDetail", rx, ry, 244, 474, false);

        AddPanel(_rightDetail, AtlasLoginWindow, 0, 0, 244, 474, 0, 0, false);
        AddImage(_rightDetail, AtlasLoginWindow, 0, 0, 215, 93, 809, 543);
        AddImage(_rightDetail, AtlasMainWindow, 0, 93, 215, 49, 0, 730);
        AddImage(_rightDetail, AtlasLoginWindow, 0, 142, 215, 210, 809, 768);
        AddImage(_rightDetail, AtlasLoginWindow, 215, 0, 29, 22, 556, 729);
        AddImage(_rightDetail, AtlasLoginWindow, 0, 352, 29, 40, 556, 689);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 33, 34, 18, 771, 596);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 57, 34, 18, 771, 542);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 109, 34, 18, 771, 614);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 73, 34, 18, 771, 632);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 190, 34, 18, 771, 650);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 214, 34, 18, 771, 668);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 238, 34, 18, 297, 980);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 262, 34, 18, 331, 980);
        AddImage(_rightDetail, AtlasLoginWindow, 12, 286, 34, 18, 365, 980);

        int[] barY = [33, 57, 190, 214, 238, 262, 286];
        foreach (var by in barY)
            AddImage(_rightDetail, AtlasLoginWindow, 46, by, 157, 18, 140, 980);

        AddLabel(_rightDetail, 51, 193, 35, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 51, 217, 35, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 51, 241, 35, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 51, 265, 35, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 51, 289, 35, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 118, 155, 26, 12, string.Empty, LblWhite);
        AddLabel(_rightDetail, 51, 36, 26, 12, string.Empty, LblWarm);
    }
}