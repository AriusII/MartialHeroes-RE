// Ui/Scenes/Select/CharSelectWindow.CreateForm.cs
//
// Character-select window — Create sub-form + Name modal + form lifecycle helpers.
// Part of the CharSelectWindow partial class split (Wave 4b).
//
// Covers: BuildCreateForm(), BuildNameModal(), ShowCreateForm(), HideCreateForm(),
//         ShowNameModal(), HideNameModal(), SetCreateClass(),
//         ClassCaption(), GetDescLines().
//
// PASSIVE: zero game logic, zero domain state.
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4.
// spec: Docs/RE/specs/frontend_scenes.md §4/§11.

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // =========================================================================
    // Create sub-form (3-column layout over the carved-wall scene backdrop).
    // spec: Docs/RE/specs/frontend_scenes.md §4. CODE-CONFIRMED.
    // =========================================================================

    private Control BuildCreateForm()
    {
        var form = new Control
        {
            Name = "CreateForm",
            MouseFilter = MouseFilterEnum.Stop
        };
        form.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Char-count caption (same msg 2209 as select view).
        // spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
        BuildCreateTopCaption(form);

        // ── Create preview 3D — full-screen backdrop (OwnWorld3D=true).
        // spec: frontend_scenes.md §4/§3.7.6. CODE-CONFIRMED.
        BuildCreatePreview3DNode(form);

        // ── LEFT COLUMN: class buttons + npc.scr description ──
        // spec: frontend_scenes.md §4/§11.5e. CODE-CONFIRMED.
        form.AddChild(BuildCreateLeftPanel());

        // ── CENTER COLUMN: face ± + turntable ──
        // spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        form.AddChild(BuildCreateCenterPanel());

        // ── RIGHT COLUMN: chrome plate + description lower plate ──
        // spec: frontend_scenes.md §11.5e/§4.1.1. CODE-CONFIRMED.
        form.AddChild(BuildCreateRightPanel());

        // ── Point-buy ± stat grid on the create form (10 cells, 2×5, actions 25-34) ──
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        form.AddChild(BuildCreateStatPanel());

        // Confirm (35) / Cancel (36) buttons on the create form.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        BuildCreateActionButtons(form);

        // Name modal (on-demand).
        _nameModal = BuildNameModal();
        _nameModal.Visible = false;
        form.AddChild(_nameModal);

        return form;
    }

    // ── Top caption ──────────────────────────────────────────────────────────

    private void BuildCreateTopCaption(Control form)
    {
        var topCaptionWidget = HudWidgetFactory.MakeLabel(
            0, 14, (int)RefW, 28,
            BuildCharCountCaption(),
            new Color(0.95f, 0.86f, 0.55f));
        if (topCaptionWidget.GetControl() is Label topCaptionLabel)
        {
            topCaptionLabel.Name = "CreateCountCaption";
            topCaptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
            form.AddChild(topCaptionLabel);
        }
    }

    // ── Create preview 3D node ────────────────────────────────────────────────

    private void BuildCreatePreview3DNode(Control form)
    {
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            MouseFilter = MouseFilterEnum.Ignore,
            SharedRealAssets = _realAssets,
            InternalClassId = UiToInternal[_createUiClass]
        };
        _createPreview3D.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        form.AddChild(_createPreview3D);
    }

    // ── Left panel: class buttons + npc.scr description lines ────────────────

    private Control BuildCreateLeftPanel()
    {
        var leftPanel = new Control
        {
            Name = "CreateLeft",
            Position = new Vector2(8f, 46f),
            Size = new Vector2(200f, 680f),
            MouseFilter = MouseFilterEnum.Pass
        };

        // 4 class buttons — vertical column, actions 10-13.
        // NORMAL src-Y 1005, NORMAL src-X {590,635,680,725}, HOVER src-X {770,815,860,905}.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        const int classBtnStride = 28; // vertical stride in left column.
        const int classBtnColX = 8;
        const int classBtnBaseY = 10;
        for (var ci = 0; ci < 4; ci++)
        {
            var btnY = classBtnBaseY + ci * classBtnStride;

            if (Atlas is not null)
            {
                var classBtn = HudWidgetFactory.MakeButton3(
                    Atlas, AtlasLoginWindow,
                    classBtnColX, btnY, ClassBtnW, ClassBtnH,
                    ClassBtnNormSrcX[ci], ClassBtnSrcY, // NORMAL. spec §8.2+§8.4. CODE-CONFIRMED.
                    ClassBtnHovSrcX[ci], ClassBtnSrcY, // HOVER.  spec §8.2+§8.4. CODE-CONFIRMED.
                    ActionClass0 + ci); // 10/11/12/13.
                classBtn.ActionFired += OnCreateClassAction;
                classBtn.GetControl()!.Name = $"ClassBtn{ci}";
                leftPanel.AddChild(classBtn.GetControl()!);
            }
        }

        // npc.scr description — 3 lines from NpcScrDescriptions (CP949, already decoded).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1. CODE-CONFIRMED.
        var descLines = GetDescLines(_createUiClass);
        const float descBaseY = classBtnBaseY + 4 * classBtnStride + 16f;
        const float descStride = 60f;
        var descColor = new Color(0.82f, 0.82f, 0.88f);

        _createDescLine0Widget = HudWidgetFactory.MakeLabel(
            6, (int)descBaseY, (int)(200f - 12f), 56,
            descLines.Length > 0 ? descLines[0] : string.Empty, descColor, multiline: true);
        if (_createDescLine0Widget.GetControl() is Label d0Lbl)
        {
            d0Lbl.Name = "DescLine0";
            leftPanel.AddChild(d0Lbl);
        }

        _createDescLine1Widget = HudWidgetFactory.MakeLabel(
            6, (int)(descBaseY + descStride), (int)(200f - 12f), 56,
            descLines.Length > 1 ? descLines[1] : string.Empty, descColor, multiline: true);
        if (_createDescLine1Widget.GetControl() is Label d1Lbl)
        {
            d1Lbl.Name = "DescLine1";
            leftPanel.AddChild(d1Lbl);
        }

        _createDescLine2Widget = HudWidgetFactory.MakeLabel(
            6, (int)(descBaseY + 2f * descStride), (int)(200f - 12f), 56,
            descLines.Length > 2 ? descLines[2] : string.Empty, descColor, multiline: true);
        if (_createDescLine2Widget.GetControl() is Label d2Lbl)
        {
            d2Lbl.Name = "DescLine2";
            leftPanel.AddChild(d2Lbl);
        }

        return leftPanel;
    }

    // ── Center panel: face ± steppers + turntable buttons ────────────────────

    private Control BuildCreateCenterPanel()
    {
        var centerPanel = new Control
        {
            Name = "CreateCenter",
            Position = new Vector2(215f, 46f),
            Size = new Vector2(595f, 680f),
            MouseFilter = MouseFilterEnum.Pass
        };

        // Face ± buttons, actions 21/22. spec §8.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var faceDecrBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                40, 626, 28, 22,
                483, 490, // face-decrement glyph. spec §8.2. CODE-CONFIRMED.
                ActionFaceDecr);
            faceDecrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceDecrBtn.GetControl()!);

            var faceIncrBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                106, 626, 28, 22,
                505, 490, // face-increment glyph. spec §8.2. CODE-CONFIRMED.
                ActionFaceIncr);
            faceIncrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceIncrBtn.GetControl()!);
        }

        _createFaceLabelWidget = HudWidgetFactory.MakeLabel(
            73, 629, 28, 20,
            _createFaceIndex.ToString(),
            new Color(0.95f, 0.95f, 0.95f));
        if (_createFaceLabel is not null)
        {
            _createFaceLabel.Name = "FaceIndexLabel";
            _createFaceLabel.HorizontalAlignment = HorizontalAlignment.Center;
            centerPanel.AddChild(_createFaceLabel);
        }

        // Turntable L/R (press-and-hold ≈±2 rad/s), actions 23/24.
        // spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var rotLeft = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                150, 626, 36, 22, 483, 490, 23);
            rotLeft.GetControl()!.Name = "RotLeft";
            rotLeft.GetControl()!.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressLeft = mb.Pressed;
            };
            centerPanel.AddChild(rotLeft.GetControl()!);

            var rotRight = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                192, 626, 36, 22, 505, 490, 24);
            rotRight.GetControl()!.Name = "RotRight";
            rotRight.GetControl()!.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressRight = mb.Pressed;
            };
            centerPanel.AddChild(rotRight.GetControl()!);
        }

        return centerPanel;
    }

    // ── Right panel: chrome plate + description lower plate ──────────────────

    private Control BuildCreateRightPanel()
    {
        var rightPanel = new Control
        {
            Name = "CreateRight",
            Position = new Vector2(815f, 46f),
            Size = new Vector2(201f, 680f),
            MouseFilter = MouseFilterEnum.Pass
        };

        AddAtlasRectTo(rightPanel, AtlasMainWindow, 0, 0, 201, 274, 0, 0);
        AddAtlasRectTo(rightPanel, AtlasInventWindow, 0, 274, 201, 190, 318, 647);

        return rightPanel;
    }

    // ── Stat grid panel: 10 cells (2×5), point-buy ± actions 25-34 ───────────

    private Control BuildCreateStatPanel()
    {
        // The same 10-cell grid doubles as the create-form point-buy ± buttons.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        var statPanel = new Control
        {
            Name = "CreateStatGrid",
            Position = new Vector2(8f, 300f),
            Size = new Vector2(200f, 200f),
            MouseFilter = MouseFilterEnum.Pass
        };

        for (var row = 0; row < StatGridRows; row++)
        {
            var y = row * StatGridStride;

            // Col 1 — actions 25/27/29/31/33.
            BuildGridCellInto(statPanel, 0, y,
                StatCol1NormX, StatCol1NormY,
                StatCol1HovX, StatCol1NormY,
                ActionPointBuyBase + row * 2);

            // Col 2 — actions 26/28/30/32/34.
            BuildGridCellInto(statPanel, StatIconW + 4, y,
                StatCol2NormX, StatCol2NormY,
                StatCol2HovX, StatCol2NormY,
                ActionPointBuyBase + row * 2 + 1);
        }

        return statPanel;
    }

    // ── Confirm / Cancel button pair ─────────────────────────────────────────

    private void BuildCreateActionButtons(Control form)
    {
        // Confirm (35) / Cancel (36) buttons on the create form.
        // dst-Y 325 on create-plate row art V=1004.
        // Confirm: dst-X 42, NORMAL(354,1004), HOVER src-X 413.
        // Cancel:  dst-X 112, NORMAL(472,1004), HOVER src-X 531.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        if (Atlas is null) return;

        var confirmBtn = HudWidgetFactory.MakeButton3(
            Atlas, AtlasLoginWindow,
            ConfirmDstX, ConfirmCancelY, ConfirmCancelW, ConfirmCancelH,
            ConfirmNormSrcX, ConfirmCancelSrcY, // NORMAL. spec §8.4. CODE-CONFIRMED.
            ConfirmHovSrcX, ConfirmCancelSrcY, // HOVER.  spec §8.4. CODE-CONFIRMED.
            ActionConfirm);
        confirmBtn.ActionFired += OnCreateConfirmAction;
        form.AddChild(confirmBtn.GetControl()!);

        var cancelBtn = HudWidgetFactory.MakeButton3(
            Atlas, AtlasLoginWindow,
            CancelDstX, ConfirmCancelY, ConfirmCancelW, ConfirmCancelH,
            CancelNormSrcX, ConfirmCancelSrcY, // NORMAL. spec §8.4. CODE-CONFIRMED.
            CancelHovSrcX, ConfirmCancelSrcY, // HOVER.  spec §8.4. CODE-CONFIRMED.
            ActionCancel);
        cancelBtn.ActionFired += _ => HideCreateForm();
        form.AddChild(cancelBtn.GetControl()!);
    }

    // =========================================================================
    // Name modal — on-demand overlay; class name title + name textbox + Confirm/Cancel.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1/§4.4. CODE-CONFIRMED.
    // =========================================================================

    private Control BuildNameModal()
    {
        var modalX = (RefW - ModalW) / 2f;
        var modalY = (RefH - ModalH) / 2f;

        var modal = new Control
        {
            Name = "NameModal",
            Position = new Vector2(modalX, modalY),
            Size = new Vector2(ModalW, ModalH),
            MouseFilter = MouseFilterEnum.Stop
        };

        // Chrome: inventwindow.dds 340×190 src(318,647). spec §8.3. CODE-CONFIRMED.
        var bg = Atlas?.SliceByPath(AtlasInventWindow, 318, 647, (int)ModalW, (int)ModalH);
        if (bg is not null)
        {
            var bgRect = new TextureRect
            {
                Name = "NameModalBg",
                Texture = bg,
                Position = Vector2.Zero,
                Size = new Vector2(ModalW, ModalH),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            modal.AddChild(bgRect);
        }

        // Class name title (msg 14003..14006 — updated on class change).
        // spec: frontend_scenes.md §4.1.1. CODE-CONFIRMED.
        _nameModalTitleWidget = HudWidgetFactory.MakeLabel(
            12, 12, (int)(ModalW - 24f), 22,
            ClassCaption(_createUiClass),
            new Color(0.95f, 0.86f, 0.55f));
        if (_nameModalTitle is not null)
        {
            _nameModalTitle.Name = "NameModalTitle";
            _nameModalTitle.HorizontalAlignment = HorizontalAlignment.Center;
            modal.AddChild(_nameModalTitle);
        }

        // Name LineEdit (textbox). spec: §8.2/§4.4. CODE-CONFIRMED.
        _nameEntry = new LineEdit
        {
            Name = "NameEntry",
            Position = new Vector2(NameBoxX, NameBoxY),
            Size = new Vector2(NameBoxW, NameBoxH)
        };
        _nameEntry.AddThemeFontSizeOverride("font_size", 13);
        modal.AddChild(_nameEntry);

        // Toast label (msg 2075). spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
        _nameToastWidget = HudWidgetFactory.MakeLabel(
            (int)NameBoxX, 96, (int)NameBoxW, 30,
            string.Empty,
            new Color(1.0f, 0.35f, 0.20f),
            multiline: true);
        if (_nameToast is not null)
        {
            _nameToast.Name = "NameToast";
            _nameToast.Visible = false;
            modal.AddChild(_nameToast);
        }

        // Modal Confirm (35) / Cancel (36) via inventwindow.dds.
        // spec: §8.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var ok = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                55, 136, 113, 40,
                302, 860, // spec §8.3. CODE-CONFIRMED.
                ActionConfirm,
                Text?.GetCaption(2301u, string.Empty) ?? string.Empty);
            ok.ActionFired += OnModalConfirm;
            modal.AddChild(ok.GetControl()!);

            var cancel = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                174, 136, 113, 40,
                302, 900, // spec §8.3. CODE-CONFIRMED.
                ActionCancel,
                Text?.GetCaption(2302u, string.Empty) ?? string.Empty);
            cancel.ActionFired += _ => HideNameModal();
            modal.AddChild(cancel.GetControl()!);
        }

        return modal;
    }

    // =========================================================================
    // Delete-confirm modal — raised by Delete (action 5) on an occupied slot.
    // spec: Docs/RE/specs/frontend_scenes.md §5 + §11.5d (DELETE-confirm popup). CODE-CONFIRMED.
    //   Dragon frame: inventwindow.dds src(318,647) 340×190 centred at (342,289).
    //   Yes = action 54 → emits DeleteCharacterRequested; No = action 55 → dismiss.
    //   Body caption msg 14001; title caption msg 14002.
    // =========================================================================

    private Control BuildDeleteConfirmModal()
    {
        var modal = new Control
        {
            Name = "DeleteConfirmModal",
            Position = new Vector2(DeleteModalX, DeleteModalY),
            Size = new Vector2(DeleteModalW, DeleteModalH),
            MouseFilter = MouseFilterEnum.Stop
        };

        // Dragon frame background: inventwindow.dds src(318,647) 340×190.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d (shared dragon-frame quad). CODE-CONFIRMED.
        var bg = Atlas?.SliceByPath(AtlasInventWindow, 318, 647, (int)DeleteModalW, (int)DeleteModalH);
        if (bg is not null)
        {
            var bgRect = new TextureRect
            {
                Name = "DeleteModalBg",
                Texture = bg,
                Position = Vector2.Zero,
                Size = new Vector2(DeleteModalW, DeleteModalH),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            modal.AddChild(bgRect);
        }

        // Title caption (msg 14002). spec §11.5d. CODE-CONFIRMED.
        var titleWidget = HudWidgetFactory.MakeLabel(
            12, 12, (int)(DeleteModalW - 24f), 22,
            Text?.GetCaption(MsgDeleteConfirmTitle, string.Empty) ?? string.Empty,
            new Color(0.95f, 0.86f, 0.55f));
        if (titleWidget.GetControl() is Label titleLbl)
        {
            titleLbl.Name = "DeleteModalTitle";
            titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
            modal.AddChild(titleLbl);
        }

        // Body caption (msg 14001). spec §11.5d. CODE-CONFIRMED.
        var bodyWidget = HudWidgetFactory.MakeLabel(
            12, 46, (int)(DeleteModalW - 24f), 60,
            Text?.GetCaption(MsgDeleteConfirmBody, string.Empty) ?? string.Empty,
            new Color(0.85f, 0.85f, 0.9f),
            multiline: true);
        if (bodyWidget.GetControl() is Label bodyLbl)
        {
            bodyLbl.Name = "DeleteModalBody";
            bodyLbl.HorizontalAlignment = HorizontalAlignment.Center;
            modal.AddChild(bodyLbl);
        }

        // Yes (54) / No (55) buttons via inventwindow.dds.
        // Layout mirrors the name modal OK/Cancel pair (same dragon-frame quad geometry).
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d (Yes 54 / No 55). CODE-CONFIRMED.
        if (Atlas is not null)
        {
            // MakeButton2 = 2-state (NORMAL only). src (302,860) = shared dragon-frame OK art.
            // spec: Docs/RE/specs/ui_system.md §8.3 (dragon-frame OK/Cancel src). CODE-CONFIRMED.
            var yesBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                55, 136, 113, 40,
                302, 860, // spec §8.3. CODE-CONFIRMED.
                ActionDeleteYes,
                Text?.GetCaption(2301u, string.Empty) ?? string.Empty);
            yesBtn.ActionFired += OnDeleteConfirmYes;
            modal.AddChild(yesBtn.GetControl()!);

            var noBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                174, 136, 113, 40,
                302, 900, // spec §8.3. CODE-CONFIRMED.
                ActionDeleteNo,
                Text?.GetCaption(2302u, string.Empty) ?? string.Empty);
            noBtn.ActionFired += _ => HideDeleteConfirmModal();
            modal.AddChild(noBtn.GetControl()!);
        }

        return modal;
    }

    private void ShowDeleteConfirmModal(int slotIndex)
    {
        _pendingDeleteSlot = slotIndex;
        if (_deleteConfirmModal is not null && IsInstanceValid(_deleteConfirmModal))
            _deleteConfirmModal.Visible = true;
        GD.Print($"[CharSelectWindow] Delete-confirm modal opened for slot {slotIndex}. " +
                 "spec: frontend_scenes.md §5+§11.5d.");
    }

    private void HideDeleteConfirmModal()
    {
        _pendingDeleteSlot = -1;
        if (_deleteConfirmModal is not null && IsInstanceValid(_deleteConfirmModal))
            _deleteConfirmModal.Visible = false;
    }

    // =========================================================================
    // Create form lifecycle helpers
    // =========================================================================

    private void ShowCreateForm()
    {
        _createFormVisible = true;
        if (_createForm is not null && IsInstanceValid(_createForm))
            _createForm.Visible = true;

        // Hide char-select 3D backdrop — create preview fills the screen with its own cell.
        // spec: frontend_scenes.md §4/§3.7.6. CODE-CONFIRMED.
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = false;

        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;

        // Dismiss any pending delete-confirm if the user somehow opens Create while it is showing.
        HideDeleteConfirmModal();

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            _createPreview3D.RebuildForClass();
        }

        _toastTimer = 0.0;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;

        _rotatePressLeft = false;
        _rotatePressRight = false;

        GD.Print($"[CharSelectWindow] Create form opened (uiClass={_createUiClass} → " +
                 $"internal={UiToInternal[_createUiClass]}). spec: frontend_scenes.md §4.");
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        if (_createForm is not null && IsInstanceValid(_createForm))
            _createForm.Visible = false;

        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = true;

        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;

        _rotatePressLeft = false;
        _rotatePressRight = false;
        _toastTimer = 0.0;
    }

    private void ShowNameModal()
    {
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);
        if (_nameEntry is not null && IsInstanceValid(_nameEntry))
            _nameEntry.Text = string.Empty;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;
        _toastTimer = 0.0;
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = true;
        GD.Print($"[CharSelectWindow] Name modal opened for class {_createUiClass}.");
    }

    private void HideNameModal()
    {
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;
        _toastTimer = 0.0;
    }

    private void SetCreateClass(int uiIndex)
    {
        _createUiClass = Math.Clamp(uiIndex, 0, 3);
        var internalClass = UiToInternal[_createUiClass];
        GD.Print($"[CharSelectWindow] Create class: UI={_createUiClass} → internal={internalClass}.");

        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);

        var lines = GetDescLines(_createUiClass);
        if (_createDescLine0 is not null && IsInstanceValid(_createDescLine0))
            _createDescLine0.Text = lines.Length > 0 ? lines[0] : string.Empty;
        if (_createDescLine1 is not null && IsInstanceValid(_createDescLine1))
            _createDescLine1.Text = lines.Length > 1 ? lines[1] : string.Empty;
        if (_createDescLine2 is not null && IsInstanceValid(_createDescLine2))
            _createDescLine2.Text = lines.Length > 2 ? lines[2] : string.Empty;

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = internalClass;
            _createPreview3D.RebuildForClass();
        }
    }

    // =========================================================================
    // Class caption + npc.scr description helpers
    // =========================================================================

    private string ClassCaption(int uiIndex)
    {
        var safe = Math.Clamp(uiIndex, 0, 3);
        return Text?.GetCaption(ClassMsgIds[safe], string.Empty) ?? string.Empty;
    }

    private string[] GetDescLines(int uiIndex)
    {
        var joined = _npcScrDesc.GetDescription(Math.Clamp(uiIndex, 0, 3));
        if (string.IsNullOrEmpty(joined)) return [];
        return joined.Split('\n');
    }
}