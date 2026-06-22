// Ui/Scenes/Select/CharSelectWindow.Groups.cs
//
// The 124-widget inventory, group by group. Every dst/src rect and every action id is
// transcribed from the frontend_scenes.md §11.5h ordered widget table (CODE-CONFIRMED
// immediates). 'Reg' fields are placed at a documented sensible default and tagged
// // OPEN@DEBUGGER — never invented geometry.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.5h. CODE-CONFIRMED.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // Group-local label colours (front-end labels are zero-tint white in the binary; we use
    // readable near-white for chrome, warm for captions — render-only choice, no spec value).
    private static readonly Color LblWhite = new(0.92f, 0.92f, 0.95f);
    private static readonly Color LblWarm = new(0.95f, 0.86f, 0.55f);

    // =====================================================================================
    // GROUP 1 — bi#1–20 : roster frame + top command-button strip (loginwindow atlas).
    // 1 top header bar Panel, 1 left roster Panel 244×187, header art, slot-info backing,
    // info icons/value bars/labels, and the THREE always-shown command buttons (act 1/2/3).
    // spec §11.5h Group 1. dst/src CODE-CONFIRMED (Reg = layout-computed, tagged).
    // =====================================================================================

    private void BuildGroup1RosterFrame()
    {
        // The roster frame lives in the bottom band so the 3D backdrop reads above it.
        // OPEN@DEBUGGER: the §11.5h Reg origin of the strip/panels; placed at the §11.5a
        // bottom-band layout (left roster panel near the lower-left). spec §11.5h Group 1.
        const int frameX = 8; // OPEN@DEBUGGER spec-flag (Reg dst origin)
        const int frameY = 560; // OPEN@DEBUGGER spec-flag (Reg dst origin)
        _topStrip = NewContainer("RosterFrame", frameX, frameY, 577, 187, false);

        // bi#1 Panel (Reg,Reg,577,58) login — top header bar (opaque=0).
        AddPanel(_topStrip, AtlasLoginWindow, 0, 0, 577, 58, 0, 0, false); // OPEN@DEBUGGER src (Reg,Reg)
        // bi#2 Panel (0,0,244,187) src(0,0) login — left roster panel 244×187 (DISTINCT from bi#82).
        AddPanel(_topStrip, AtlasLoginWindow, 0, 0, 244, 187, 0, 0, false);

        // bi#3–5 header art L/C/R.
        AddImage(_topStrip, AtlasLoginWindow, 0, 12, 200, 46, 608, 793); // bi#3
        AddImage(_topStrip, AtlasLoginWindow, 200, 0, 176, 58, 608, 735); // bi#4
        AddImage(_topStrip, AtlasLoginWindow, 376, 12, 201, 46, 608, 689); // bi#5

        // bi#6–8 command buttons — act 1 / 2 / 3 (always shown). Three atlas origins:
        // NORMAL/HOVER are layout-computed (OPEN@DEBUGGER); PRESSED is the §11.5h src.
        AddButtonAction(_topStrip, AtlasLoginWindow, 67, 17, 113, 40,
            483, 883, 483, 883, 483, 883, ActCmdCreateNew); // bi#6 act 1
        AddButtonAction(_topStrip, AtlasLoginWindow, 232, 7, 113, 40,
            483, 923, 483, 923, 483, 923, ActCmdEnterGame); // bi#7 act 2
        AddButtonAction(_topStrip, AtlasLoginWindow, 393, 17, 113, 40,
            483, 963, 483, 963, 483, 963, ActCmdCreateAlt); // bi#8 act 3

        // bi#9–11 slot-info backing + corner art.
        AddImage(_topStrip, AtlasLoginWindow, 0, 0, 215, 147, 556, 542); // bi#9
        AddImage(_topStrip, AtlasLoginWindow, 215, 0, 29, 22, 556, 729); // bi#10
        AddImage(_topStrip, AtlasLoginWindow, 0, 147, 29, 40, 556, 689); // bi#11

        // bi#12–14 info icon rows.
        AddImage(_topStrip, AtlasLoginWindow, 20, 33, 34, 18, 771, 542); // bi#12
        AddImage(_topStrip, AtlasLoginWindow, 20, 57, 34, 18, 771, 560); // bi#13
        AddImage(_topStrip, AtlasLoginWindow, 20, 81, 34, 18, 771, 578); // bi#14

        // bi#15–17 value bars (placeholder strip src 140,980).
        AddImage(_topStrip, AtlasLoginWindow, 50, 33, 157, 18, 140, 980); // bi#15
        AddImage(_topStrip, AtlasLoginWindow, 50, 57, 157, 18, 140, 980); // bi#16
        AddImage(_topStrip, AtlasLoginWindow, 50, 81, 157, 18, 140, 980); // bi#17

        // bi#18–20 info labels — name / level / position (runtime-substituted, §3.2).
        _infoNameWidget = AddLabel(_topStrip, 60, 37, 70, 12, string.Empty, LblWhite); // bi#18
        _infoLevelWidget = AddLabel(_topStrip, 60, 61, 70, 12, string.Empty, LblWhite); // bi#19
        _infoPositionWidget = AddLabel(_topStrip, 60, 85, 70, 12, string.Empty, LblWhite); // bi#20

        // The char-count caption (msg 2209) is the terminal "slot-count label" of §11.5h;
        // it is a top-of-screen caption, not parented into the roster frame. Built on the
        // window root so it floats over the 3D backdrop. spec §3.8.2.
        _charCountCaptionWidget = AddLabel(this, 0, 12, (int)RefW, 28, string.Empty, LblWarm, 2);
        if (_charCountCaption is not null)
            _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
    }

    // =====================================================================================
    // GROUP 2 — bi#21–25 : create-class modal A (inventwindow 340×190), gated hidden.
    // OK = act 62, Cancel = act 63. spec §11.5h Group 2.
    // =====================================================================================

    private void BuildGroup2CreateClassModal()
    {
        _modalCreateClass = NewModalContainer("ModalCreateClass");
        // bi#21 Panel (Reg,Reg,340,190) src(318,647) opaque=1.
        AddPanel(_modalCreateClass, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        // bi#22/23 labels.
        AddLabel(_modalCreateClass, 35, 60, 12, 12, string.Empty, LblWhite); // bi#22
        AddLabel(_modalCreateClass, 10, 100, 12, 12, string.Empty, LblWhite); // bi#23
        // bi#24 OK act 62 / bi#25 Cancel act 63. src(415,Reg) — Y is layout-computed.
        AddButtonAction(_modalCreateClass, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateClassOk); // bi#24 act 62 (OPEN@DEBUGGER src-Y)
        AddButtonAction(_modalCreateClass, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateClassCancel); // bi#25 act 63 (OPEN@DEBUGGER src-Y)
        _modalCreateClass.Visible = false;
    }

    // =====================================================================================
    // GROUP 3 — bi#26–28 : caption-only notice modal B (inventwindow 340×190), gated hidden.
    // OK = act 74 (re-arm all 5 slot actors). spec §11.5h Group 3.
    // =====================================================================================

    private void BuildGroup3NoticeModal()
    {
        _modalNotice = NewModalContainer("ModalNotice");
        // bi#26 Panel (Reg,Reg,340,190) src(318,647) op=1.
        AddPanel(_modalNotice, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        // bi#27 full-panel caption (message-db text).
        _noticeBodyWidget = AddLabel(_modalNotice, 0, 0, 340, 190, string.Empty, LblWhite, 0, true);
        if (_noticeBodyWidget.GetControl() is Label nb) nb.HorizontalAlignment = HorizontalAlignment.Center;
        // bi#28 OK act 74. src(415,Reg).
        AddButtonAction(_modalNotice, AtlasInventWindow, 230, 150, 113, 40,
            415, 860, 415, 860, 415, 900, ActNoticeOk); // bi#28 act 74 (OPEN@DEBUGGER src-Y)
        _modalNotice.Visible = false;
    }

    // =====================================================================================
    // GROUP 4 — bi#29–32 : delete-confirm-style modal C (inventwindow 340×190), gated hidden.
    // Confirm = act 64. spec §11.5h Group 4.
    // OPEN@DEBUGGER spec-flag: the delete-confirm modal ROUTE (which modal the Delete path
    // raises) is debugger-pending (§ HandleCommand "Delete path"). SENSIBLE DEFAULT: the
    // Delete gesture raises THIS modal C (act 64 = single-action confirm), reusing the prior
    // confirm-before-emit behaviour. spec §11.5h / §HandleCommand.
    // =====================================================================================

    private void BuildGroup4DeleteConfirmModal()
    {
        _modalDeleteCfm = NewModalContainer("ModalDeleteConfirm");
        // bi#29 Panel (Reg,Reg,340,190) src(318,647) op=1.
        AddPanel(_modalDeleteCfm, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        // bi#30/31 labels (title/body, runtime-filled from message-db).
        _deleteTitleWidget = AddLabel(_modalDeleteCfm, 24, 60, 292, 12, string.Empty, LblWarm); // bi#30
        _deleteBodyWidget = AddLabel(_modalDeleteCfm, 20, 100, 300, 12, string.Empty, LblWhite); // bi#31
        // bi#32 confirm act 64. src(415,Reg).
        AddButtonAction(_modalDeleteCfm, AtlasInventWindow, 120, 133, 113, 40,
            415, 860, 415, 860, 415, 900, ActDeleteConfirm); // bi#32 act 64 (OPEN@DEBUGGER src-Y)
        _modalDeleteCfm.Visible = false;
    }

    // =====================================================================================
    // GROUP 5 — bi#33–36 : bottom roster action-button row (loginwindow).
    //   bi#33 act 61 delete/conditional overlay (per-slot gated by +0x1548, §3.4)
    //   bi#34 act 4, bi#35 act 5 (no-op pass-through), bi#36 act 6 (move/relocate).
    // spec §11.5h Group 5.
    // =====================================================================================

    private void BuildGroup5BottomRow()
    {
        // Parented to the roster frame so the row rides the same panel. OPEN@DEBUGGER: the
        // src-Y of each button is layout-computed (Reg); placed on a single row.
        var parent = _topStrip ?? this;
        _bottomRow = NewContainer("RosterBottomRow", 8, 712, 577, 20, false);

        // bi#33 act 61 conditional overlay (95×20). src(500,Reg). Per-slot gated — stored.
        var overlay = AddButtonAction(_bottomRow, AtlasLoginWindow, 20, 0, 95, 20,
            500, 1004, 500, 1004, 500, 1004, ActConditionalOverlay); // bi#33 act 61 (OPEN@DEBUGGER src-Y)
        _overlayAction61 = overlay.GetControl();

        // bi#34 act 4 (59×20) src(59,Reg).
        AddButtonAction(_bottomRow, AtlasLoginWindow, 130, 0, 59, 20,
            59, 1004, 59, 1004, 59, 1004, ActBottom4); // bi#34 act 4 (OPEN@DEBUGGER src-Y)
        // bi#35 act 5 no-op pass-through (59×20) src(118,Reg).
        AddButtonAction(_bottomRow, AtlasLoginWindow, 42, 0, 59, 20,
            118, 1004, 118, 1004, 118, 1004, ActBottom5); // bi#35 act 5 (OPEN@DEBUGGER src-Y)
        // bi#36 act 6 move/relocate (59×20) src(295,Reg).
        AddButtonAction(_bottomRow, AtlasLoginWindow, 112, 0, 59, 20,
            295, 1004, 295, 1004, 295, 1004, ActBottom6); // bi#36 act 6 (OPEN@DEBUGGER src-Y)

        _ = parent; // bottomRow is a window-root sibling; the roster frame is its visual anchor.
    }

    // =====================================================================================
    // GROUP 6 — bi#37–65 : right detail panel 244×474 + stat icon / value column.
    // 1 Panel, images (one on mainwindow), 7 value bars, 6 stat-value labels, 1 name label.
    // spec §11.5h Group 6. NO action bindings in this group.
    // =====================================================================================

    private void BuildGroup6RightDetail()
    {
        // OPEN@DEBUGGER: the detail panel Reg origin; placed at the right edge of the canvas.
        const int rx = 772; // OPEN@DEBUGGER spec-flag (Reg dst origin) — 1024-244-8
        const int ry = 60; // OPEN@DEBUGGER spec-flag (Reg dst origin)
        _rightDetail = NewContainer("RightDetail", rx, ry, 244, 474, false);

        // bi#37 Panel (0,0,244,474) login.
        AddPanel(_rightDetail, AtlasLoginWindow, 0, 0, 244, 474, 0, 0, false); // OPEN@DEBUGGER src (Reg,Reg)
        // bi#38 portrait backing.
        AddImage(_rightDetail, AtlasLoginWindow, 0, 0, 215, 93, 809, 543); // bi#38
        // bi#39 uses MAINWINDOW atlas (src-X is Reg).
        AddImage(_rightDetail, AtlasMainWindow, 0, 93, 215, 49, 0, 730); // bi#39 (OPEN@DEBUGGER src-X)
        // bi#40 body art.
        AddImage(_rightDetail, AtlasLoginWindow, 0, 142, 215, 210, 809, 768); // bi#40
        // bi#41/42 corners.
        AddImage(_rightDetail, AtlasLoginWindow, 215, 0, 29, 22, 556, 729); // bi#41
        AddImage(_rightDetail, AtlasLoginWindow, 0, 352, 29, 40, 556, 689); // bi#42
        // bi#43–51 stat icons 1–9.
        AddImage(_rightDetail, AtlasLoginWindow, 12, 33, 34, 18, 771, 596); // bi#43
        AddImage(_rightDetail, AtlasLoginWindow, 12, 57, 34, 18, 771, 542); // bi#44
        AddImage(_rightDetail, AtlasLoginWindow, 12, 109, 34, 18, 771, 614); // bi#45
        AddImage(_rightDetail, AtlasLoginWindow, 12, 73, 34, 18, 771, 632); // bi#46
        AddImage(_rightDetail, AtlasLoginWindow, 12, 190, 34, 18, 771, 650); // bi#47
        AddImage(_rightDetail, AtlasLoginWindow, 12, 214, 34, 18, 771, 668); // bi#48
        AddImage(_rightDetail, AtlasLoginWindow, 12, 238, 34, 18, 297, 980); // bi#49
        AddImage(_rightDetail, AtlasLoginWindow, 12, 262, 34, 18, 331, 980); // bi#50
        AddImage(_rightDetail, AtlasLoginWindow, 12, 286, 34, 18, 365, 980); // bi#51

        // bi#52–58 stat value bars 1–7 (placeholder strip src 140,980).
        int[] barY = [33, 57, 190, 214, 238, 262, 286];
        foreach (var by in barY)
            AddImage(_rightDetail, AtlasLoginWindow, 46, by, 157, 18, 140, 980); // bi#52–58

        // bi#59–64 stat-value labels 1–6 (runtime-substituted). dst per §11.5h table.
        AddLabel(_rightDetail, 51, 193, 35, 12, string.Empty, LblWhite); // bi#59
        AddLabel(_rightDetail, 51, 217, 35, 12, string.Empty, LblWhite); // bi#60
        AddLabel(_rightDetail, 51, 241, 35, 12, string.Empty, LblWhite); // bi#61
        AddLabel(_rightDetail, 51, 265, 35, 12, string.Empty, LblWhite); // bi#62
        AddLabel(_rightDetail, 51, 289, 35, 12, string.Empty, LblWhite); // bi#63
        AddLabel(_rightDetail, 118, 155, 26, 12, string.Empty, LblWhite); // bi#64
        // bi#65 name / title label.
        AddLabel(_rightDetail, 51, 36, 26, 12, string.Empty, LblWarm); // bi#65
    }
}