// Ui/Scenes/Select/CharSelectWindow.Groups2.cs
//
// The 124-widget inventory, groups 7–11 (per-slot controls, inner sub-panel, close/chrome,
// create-NAME modal, rename modal). Continuation of CharSelectWindow.Groups.cs.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.5h. CODE-CONFIRMED.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // =====================================================================================
    // GROUP 7 — bi#66–81 : per-slot mini controls (face steppers + stat spinners).
    //   bi#66 act 22 face−, bi#67 act 21 face+, bi#68 act 25 (Stat0+), bi#69 act 26 (Stat1+),
    //   bi#70–74 act 27–31 (column A rows 1–5, ActColA0+r = 27,28,29,30,31),
    //   bi#75–79 act 30–34 (column B rows 1–5, ActColB0+r = 30,31,32,33,34),
    //   bi#80 bottom OK (plain), bi#81 bottom Cancel (plain).
    //
    // BINARY-CONFIRMED (create_flow_actions.md, SHA 263bd994):
    //   INCREMENT ids = 25,26,27,28,29; DECREMENT ids = 30,31,32,33,34.
    //   bi#68=act25, bi#69=act26 are separately handled; bi#70-74 fill ColA rows, bi#75-79 fill ColB.
    //   CRITICAL: old ActColB0 was 32 (ids 32-36), swallowing ids 35/36 = CREATE-CONFIRM/CANCEL.
    //   FIXED: ActColB0=30 so ColB emits ids 30-34 only — no overlap with 35 or 36.
    // spec §11.5h Group 7; charselect.md §4.3; dirty-file create_flow_actions.md.
    // =====================================================================================

    private void BuildGroup7PerSlotControls()
    {
        _perSlotControls = NewContainer("PerSlotControls", 8, 46, 244, 360, false);

        // bi#66 face − act 22 (25×18) src(Reg,551).
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 48, 109, 25, 18,
            483, 551, 483, 551, 483, 551, ActFaceMinus); // bi#66 act 22 (OPEN@DEBUGGER src-X)
        // bi#67 face + act 21 (25×18) src(Reg,576).
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 73, 109, 25, 18,
            483, 576, 483, 576, 483, 576, ActFacePlus); // bi#67 act 21 (OPEN@DEBUGGER src-X)
        // bi#68 Stat0 INCREMENT act 25 (25×18) src(Reg,551).
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 48, 153, 25, 18,
            483, 551, 483, 551, 483, 551, ActSpinner25); // bi#68 act 25 Stat0+ (OPEN@DEBUGGER src-X)
        // bi#69 Stat1 INCREMENT act 26 (25×18) src(Reg,576).
        AddButtonAction(_perSlotControls, AtlasLoginWindow, 73, 153, 25, 18,
            483, 576, 483, 576, 483, 576, ActSpinner26); // bi#69 act 26 Stat1+ (OPEN@DEBUGGER src-X)

        // bi#70–74 column-A rows 1–5, ActColA0+r (r=0..4) = ids 27,28,29,30,31. (24×16) src(Reg,548).
        // spec: charselect.md §4.3; dirty-file create_flow_actions.md spinnerActionIds.
        int[] rowY = [191, 215, 239, 263, 287];
        for (var r = 0; r < 5; r++)
            AddButtonAction(_perSlotControls, AtlasLoginWindow, 154, rowY[r], 24, 16,
                500, 548, 500, 548, 500, 548, ActColA0 + r); // bi#70–74 act 27–31 (OPEN@DEBUGGER src-X)

        // bi#75–79 column-B rows 1–5, ActColB0+r (r=0..4) = ids 30,31,32,33,34. (24×16) src(Reg,572).
        // ActColB0=30 (corrected from 32 — old 32-36 wrongly swallowed ids 35 and 36).
        // spec: charselect.md §4.3 createConfirmAction=35/createCancelAction=36 (NOT spinner ids).
        for (var r = 0; r < 5; r++)
            AddButtonAction(_perSlotControls, AtlasLoginWindow, 178, rowY[r], 24, 16,
                524, 572, 524, 572, 524, 572, ActColB0 + r); // bi#75–79 act 30–34 (OPEN@DEBUGGER src-X)

        // bi#80 bottom OK (plain parent-add) (59×20) src(Reg,413).
        AddButtonPlain(_perSlotControls, AtlasLoginWindow, 42, 325, 59, 20, 354, 413); // bi#80 (OPEN@DEBUGGER src-X)
        // bi#81 bottom Cancel (plain parent-add) (59×20) src(Reg,531).
        AddButtonPlain(_perSlotControls, AtlasLoginWindow, 112, 325, 59, 20, 472, 531); // bi#81 (OPEN@DEBUGGER src-X)

        _perSlotControls.Visible = false; // create sub-form only
    }

    // =====================================================================================
    // GROUP 8 — bi#82–105 : inner sub-panel (the SECOND 244×187 panel: class-pick + spinners).
    // DISTINCT widget from bi#2 (antidote i): its own parent, re-blits the SAME corner sprites.
    //   bi#86–89 class tabs act 10–13, bi#97/98 act 66/67, bi#100/101 act 68/69,
    //   bi#102–105 nav/sort-filter act 70–73, bi#93/94 plain OK/Cancel.
    // spec §11.5h Group 8.
    // =====================================================================================

    private void BuildGroup8InnerSubPanel()
    {
        // OPEN@DEBUGGER: the inner sub-panel Reg origin; placed left of the create column.
        const int sx = 8; // OPEN@DEBUGGER spec-flag (Reg dst origin)
        const int sy = 46; // OPEN@DEBUGGER spec-flag (Reg dst origin)
        _innerSubPanel = NewContainer("InnerSubPanel", sx, sy, 244, 440, false);

        // bi#82 Panel (Reg,Reg,244,187) login — the SECOND 244×187 panel.
        AddPanel(_innerSubPanel, AtlasLoginWindow, 0, 0, 244, 187, 0, 0, false); // OPEN@DEBUGGER src
        // bi#83–85 mirror of bi#9–11 art (shared sprites reused in a DIFFERENT panel).
        AddImage(_innerSubPanel, AtlasLoginWindow, 0, 0, 215, 147, 556, 542); // bi#83 (OPEN@DEBUGGER dst-X Reg)
        AddImage(_innerSubPanel, AtlasLoginWindow, 215, 0, 29, 22, 556, 729); // bi#84 (OPEN@DEBUGGER dst-X Reg)
        AddImage(_innerSubPanel, AtlasLoginWindow, 0, 147, 29, 40, 556, 689); // bi#85 (OPEN@DEBUGGER dst-X Reg)

        // bi#86–89 class tabs act 10–13 (45×19) src(Reg,{Reg,815,860,905}).
        // The class-pick column; UI index = act − 10. spec §4.1.
        int[] tabY = [30, 30, 30, 30];
        int[] tabSrcY = [770, 815, 860, 905]; // bi#86 src-Y is Reg; 87/88/89 are 815/860/905.
        int[] tabX = [0, 46, 92, 138]; // OPEN@DEBUGGER dst-X (Reg) — laid in a row
        for (var i = 0; i < 4; i++)
            AddButtonAction(_innerSubPanel, AtlasLoginWindow, tabX[i], tabY[i], 45, 19,
                590, tabSrcY[i], 770, tabSrcY[i], 590, tabSrcY[i], ActClass0 + i); // bi#86–89 act 10–13

        // bi#90–92 stacked labels 1–3. bi#90 hosts the create face-index value (no dedicated
        // face-value widget exists among the 124; the steppers are bi#66/67). spec §11.5h.
        _createFaceLabelWidget = AddLabel(_innerSubPanel, 8, 72, 24, 12,
            _createFaceIndex.ToString(), LblWhite); // bi#90 (OPEN@DEBUGGER dst-X Reg)
        AddLabel(_innerSubPanel, 8, 86, 12, 12, string.Empty, LblWhite); // bi#91
        AddLabel(_innerSubPanel, 8, 100, 12, 12, string.Empty, LblWhite); // bi#92

        // bi#93/94 OK/Cancel pair (plain parent-add) (59×20) src(Reg,413)/(Reg,531).
        AddButtonPlain(_innerSubPanel, AtlasLoginWindow, 8, 110, 59, 20, 354, 413); // bi#93 (OPEN@DEBUGGER dst/src-X)
        AddButtonPlain(_innerSubPanel, AtlasLoginWindow, 72, 110, 59, 20, 472, 531); // bi#94 (OPEN@DEBUGGER dst/src-X)

        // bi#95 big caption block.
        _createDescLine0Widget = AddLabel(_innerSubPanel, 8, 148, 215, 274,
            string.Empty, LblWhite, 0, true); // bi#95 (OPEN@DEBUGGER dst-X Reg) — src(405,600) art via label bg n/a
        // bi#96 caption value.
        _createDescLine1Widget = AddLabel(_innerSubPanel, 8, 148, 12, 12, string.Empty, LblWhite); // bi#96

        // bi#97/98 appearance spinner −/+ act 66/67 (27×18) src(Reg,674)/(Reg,701).
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 388, 27, 18,
            500, 674, 500, 674, 500, 674, ActAppSpin66); // bi#97 act 66 (OPEN@DEBUGGER src/dst-X)
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 40, 388, 27, 18,
            500, 701, 500, 701, 500, 701, ActAppSpin67); // bi#98 act 67 (OPEN@DEBUGGER src/dst-X)

        // bi#99 caption block 2.
        _createDescLine2Widget = AddLabel(_innerSubPanel, 8, 410, 190, 14,
            string.Empty, LblWhite, 0, true); // bi#99 (OPEN@DEBUGGER dst-X/H Reg)

        // bi#100/101 appearance spinner −/+ act 68/69 (27×18) src(Reg,674)/(Reg,701).
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 576, 27, 18,
            500, 674, 500, 674, 500, 674, ActAppSpin68); // bi#100 act 68 (OPEN@DEBUGGER src/dst-X)
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 40, 576, 27, 18,
            500, 701, 500, 701, 500, 701, ActAppSpin69); // bi#101 act 69 (OPEN@DEBUGGER src/dst-X)

        // bi#102/103 nav/sort-filter act 70/71 src(Reg,820); bi#104/105 toggle act 72/73 src(Reg,600).
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 600, 80, 60,
            500, 820, 500, 820, 500, 820, ActNav70); // bi#102 act 70 (OPEN@DEBUGGER src/dst)
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 92, 600, 80, 60,
            500, 820, 500, 820, 500, 820, ActNav71); // bi#103 act 71 (OPEN@DEBUGGER src/dst — w Reg)
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 8, 664, 30, 30,
            500, 600, 500, 600, 500, 600, ActToggle72); // bi#104 act 72 (OPEN@DEBUGGER src/dst)
        AddButtonAction(_innerSubPanel, AtlasLoginWindow, 42, 664, 30, 30,
            500, 600, 500, 600, 500, 600, ActToggle73); // bi#105 act 73 (OPEN@DEBUGGER src/dst)

        _innerSubPanel.Visible = false; // create sub-form only
    }

    // =====================================================================================
    // GROUP 9 — bi#106–109 : close button + small notice panel + textbox + exit caption.
    //   bi#106 corner CLOSE (plain parent-add; action binding OPEN@DEBUGGER),
    //   bi#107 small notice panel act 65, bi#108 editable textbox #1, bi#109 exit caption (font 4).
    // spec §11.5h Group 9.
    // =====================================================================================

    private void BuildGroup9CloseAndChrome()
    {
        // bi#106 corner CLOSE (23×23) at (971,610) — plain parent-add on the window root.
        // OPEN@DEBUGGER spec-flag: its action binding is not a parent-add-with-action site in
        // the routine (§11.5h). SENSIBLE DEFAULT: wire it as a system-close → BackRequested so
        // the corner-X is functional, reusing the existing back behaviour. spec §11.5h Group 9.
        var close = AddButtonPlain(this, AtlasTradeKeep, 971, 610, 23, 23, 0, 0); // bi#106 (OPEN@DEBUGGER src + action)
        if (close.GetControl() is { } closeCtrl)
            closeCtrl.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                    OnAction(ActCornerClose); // OPEN@DEBUGGER: corner-CLOSE → Back (sensible default)
            };

        // bi#107 small notice panel act 65 (176×42) at (430,100) src(132,295) op=1.
        // It is a Panel bound to an action in §11.5h — built as a panel with a click action.
        _noticeSmall = NewContainer("NoticeSmall", 430, 100, 176, 42, true);
        AddPanel(_noticeSmall, AtlasInventWindow, 0, 0, 176, 42, 132, 295, true);
        _noticeSmall.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                OnAction(ActNoticePanel); // bi#107 act 65
        };
        _wActionBindings++; // bi#107 carries action 65 (panel-level binding)
        _noticeSmall.Visible = false;

        // bi#108 editable textbox #1 (140,60,140,12) — caption from message-db.
        // Parented into the small notice panel.
        AddTextbox(_noticeSmall, 54, 60, 140, 12, 0, 0); // bi#108

        // bi#109 exit-panel caption (font slot 4). dst is Reg; placed near the notice.
        AddLabel(_noticeSmall, 8, 8, 160, 12, string.Empty, LblWarm, 4); // bi#109 (OPEN@DEBUGGER dst Reg)
    }

    // =====================================================================================
    // GROUP 10 — bi#110–115 : create-NAME modal D (inventwindow 340×190), gated hidden.
    //   bi#112 OK act 35 (CREATE-CONFIRM → 1/6 send), bi#113 Cancel act 36 (CREATE-CANCEL).
    //   Binary-confirmed: 35=create-confirm, 36=create-cancel.
    //   spec §11.5h Group 10; charselect.md §4.3 createConfirmAction=35/createCancelAction=36.
    // =====================================================================================

    private void BuildGroup10CreateNameModal()
    {
        _modalCreateName = NewModalContainer("ModalCreateName");
        // bi#110 Panel (Reg,Reg,340,190) src(318,647) op=1.
        AddPanel(_modalCreateName, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        // bi#111 label.
        _nameModalTitleWidget = AddLabel(_modalCreateName, 70, 70, 200, 12, string.Empty, LblWarm); // bi#111
        if (_nameModalTitle is not null) _nameModalTitle.HorizontalAlignment = HorizontalAlignment.Center;
        // bi#112 OK act 35 (CREATE-CONFIRM) / bi#113 Cancel act 36 (CREATE-CANCEL).
        // spec: charselect.md §4.3 createConfirmAction=35; dirty-file create_flow_actions.md.
        AddButtonAction(_modalCreateName, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateConfirm); // bi#112 act 35 (OPEN@DEBUGGER src-Y)
        AddButtonAction(_modalCreateName, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActCreateCancel); // bi#113 act 36 (OPEN@DEBUGGER src-Y)
        // bi#114/115 value labels.
        _nameToastWidget = AddLabel(_modalCreateName, 30, 100, 280, 12, string.Empty,
            new Color(1.0f, 0.35f, 0.20f)); // bi#114 (OPEN@DEBUGGER dst-X Reg) — doubles as toast
        AddLabel(_modalCreateName, 30, 100, 12, 12, string.Empty, LblWhite); // bi#115 (OPEN@DEBUGGER dst-X Reg)

        // The create-NAME edit field: the original puts the editable NAME field in the rename
        // modal (bi#122). For a functional create flow we host a LineEdit here as the name
        // entry (sensible default, reusing the prior validation seam). It is NOT counted among
        // the 124 in-routine widgets (it is a host control, like a Godot LineEdit).
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

    // =====================================================================================
    // GROUP 11 — bi#116–123 : rename modal E (inventwindow 340×190), gated hidden.
    //   bi#117 OK act 59 (rename send), bi#118 Cancel act 60,
    //   bi#121 name-field underline strip Image (InventWindow), bi#122 editable NAME Textbox.
    // spec §11.5h Group 11.
    // =====================================================================================

    private void BuildGroup11RenameModal()
    {
        _modalRename = NewModalContainer("ModalRename");
        // bi#116 Panel (Reg,Reg,340,190) src(318,647) op=1.
        AddPanel(_modalRename, AtlasInventWindow, 0, 0, 340, 190, 318, 647, true);
        // bi#117 OK act 59 / bi#118 Cancel act 60. dst-Y is Reg.
        AddButtonAction(_modalRename, AtlasInventWindow, 55, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActRenameOk); // bi#117 act 59 (OPEN@DEBUGGER dst/src-Y)
        AddButtonAction(_modalRename, AtlasInventWindow, 174, 136, 113, 40,
            415, 860, 415, 860, 415, 900, ActRenameCancel); // bi#118 act 60 (OPEN@DEBUGGER dst/src-Y)
        // bi#119/120 labels. bi#119 doubles as the rename-modal title (current slot name).
        _renameModalTitleWidget =
            AddLabel(_modalRename, 25, 20, 290, 12, string.Empty, LblWarm); // bi#119 (OPEN@DEBUGGER dst Reg)
        AddLabel(_modalRename, 25, 77, 12, 12, string.Empty, LblWhite); // bi#120
        // bi#121 name-field underline strip Image (InventWindow) (274×18) src(419,1003).
        AddImage(_modalRename, AtlasInventWindow, 55, 75, 274, 18, 419, 1003); // bi#121
        // bi#122 editable NAME Textbox (274×18) caption from message-db — the new-name entry. We host a
        // LineEdit on this field so the rename flow is functional (the in-routine Textbox is the chrome).
        AddTextbox(_modalRename, 60, 80, 274, 18, 16, 0); // bi#122
        _renameEntry = new LineEdit
        {
            Name = "RenameNameEntry",
            Position = new Vector2(60, 80),
            Size = new Vector2(274, 18)
        };
        _renameEntry.AddThemeFontSizeOverride("font_size", 13);
        _modalRename.AddChild(_renameEntry);
        // bi#123 wide status line (font slot 2) — doubles as the rename validation toast.
        _renameToastWidget = AddLabel(_modalRename, 20, 110, 300, 20, string.Empty,
            new Color(1.0f, 0.35f, 0.20f), 2); // bi#123 (OPEN@DEBUGGER dst-X Reg)

        _modalRename.Visible = false;
    }

    // =====================================================================================
    // Open visibility broadcast — the single in-builder SetChildrenVisible(roster) at open.
    // Shows the roster set; gates the create sub-form, the 5 modals, and the per-slot
    // controls hidden until raised by the command handler. spec §11.5h (post-Group-8 broadcast).
    // =====================================================================================

    private void ApplyOpenVisibility()
    {
        if (_perSlotControls is not null) _perSlotControls.Visible = false;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = false;
        // Right detail panel rides with the selected slot; visible at rest (shows the lineup).
        if (_rightDetail is not null) _rightDetail.Visible = true;
        if (_topStrip is not null) _topStrip.Visible = true;
        if (_bottomRow is not null) _bottomRow.Visible = true;
    }
}