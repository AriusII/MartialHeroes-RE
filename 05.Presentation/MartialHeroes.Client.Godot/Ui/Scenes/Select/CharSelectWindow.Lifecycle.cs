// Ui/Scenes/Select/CharSelectWindow.Lifecycle.cs
//
// Char-select window — 3D viewport wiring, modal/form show-hide, info + caption refresh,
// and slot-descriptor push. Consolidates the reused seams that survived the §11.5h table-rase
// (ApplyCharacterList, 3D scene, name validation all stay; only the widget tree was rebuilt).
//
// PASSIVE: zero game logic, zero domain state.
// spec: Docs/RE/specs/frontend_scenes.md §3 (scene/info) / §11.5h (inventory).

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // tradekeep atlas — used only for the corner CLOSE chrome (bi#106). spec §11.5h Group 9.
    private const string AtlasTradeKeep = "data/ui/tradekeep.dds";
    private HudLabel? _deleteBodyWidget;
    private HudLabel? _deleteTitleWidget;

    // New caption widgets referenced by the inventory groups (declared here, fields-only).
    private HudLabel? _noticeBodyWidget;

    // The slot the rename modal is currently editing (view state only; -1 = none).
    private int _pendingRenameSlot = -1;

    // Rename modal E (G11) widgets — the new-name entry + title + validation toast.
    private LineEdit? _renameEntry;
    private HudLabel? _renameModalTitleWidget;
    private HudLabel? _renameToastWidget;

    // Convenience accessors (resolve backing Label for alignment etc.).
    private Label? _noticeBody => (Label?)_noticeBodyWidget?.GetControl();
    private Label? _renameModalTitle => (Label?)_renameModalTitleWidget?.GetControl();
    private Label? _renameToast => (Label?)_renameToastWidget?.GetControl();

    // =========================================================================
    // 3D scene SubViewport. spec: frontend_scenes.md §3.7. CODE-CONFIRMED. (reused seam)
    // =========================================================================

    private void Build3DSceneViewport()
    {
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768), // spec §8.0 "1024×768". CODE-CONFIRMED.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false
        };

        _scene3D = new CharSelectScene3D { Name = "CharSelectScene3D" };
        PushSlotDescriptors();
        _scene3DViewport.AddChild(_scene3D);

        _scene3DContainer = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Pass
        };
        _scene3DContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scene3DContainer.AddChild(_scene3DViewport);
        AddChild(_scene3DContainer);

        // Backmost: all 2D chrome siblings added afterward paint on top.
        // spec: Docs/RE/specs/ui_system.md §7.3 (paint order = insertion order, later on top).
        MoveChild(_scene3DContainer, 0);

        Callable.From(InitialiseScene3D).CallDeferred();
    }

    private void InitialiseScene3D()
    {
        if (_scene3D is null || !IsInstanceValid(_scene3D)) return;
        try
        {
            _scene3D.Initialise(_realAssets);
            _scene3D.SetSelectedSlot(_selectedSlot);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] InitialiseScene3D failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Modal container helper — a centred 340×190 host on the window root, hidden by default.
    // Each of the 5 modals is a DISTINCT container (antidote i). spec §11.5h.
    // =========================================================================

    private Control NewModalContainer(string name)
    {
        var x = (RefW - ModalW) / 2f;
        var y = (RefH - ModalH) / 2f;
        var c = new Control
        {
            Name = name,
            Position = new Vector2(x, y),
            Size = new Vector2(ModalW, ModalH),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false
        };
        AddChild(c);
        return c;
    }

    // =========================================================================
    // Create sub-form show/hide. The create sub-form = inner sub-panel (G8) + per-slot
    // controls (G7) + 3D create preview. spec §4 / §11.5h Groups 7–8.
    // =========================================================================

    private void ShowCreateForm()
    {
        _createFormVisible = true;

        // Reset point-buy stat display values to the seed floor on each create-form open.
        // spec: Docs/RE/specs/character_creation.md §2.1 — seed 10/10/10/10/10 + 5 budget.
        for (var i = 0; i < StatGridRows; i++)
            _statValues[i] = StatFloor; // spec: character_creation.md §2.1 (seed 10)

        if (_perSlotControls is not null) _perSlotControls.Visible = true;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = true;

        // Hide the char-select 3D lineup; show the create preview cell instead.
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = false;

        EnsureCreatePreview3D();
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.Visible = true;
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            // Seed the preview's passive face-index view state from the create form's current value, so
            // the steppers' UpdateFaceIndex sink starts in sync. Face stepping never re-spawns the actor
            // (frontend_scenes.md §4.2); only the class rebuild below does.
            _createPreview3D.UpdateFaceIndex(_createFaceIndex);
            _createPreview3D.RebuildForClass();
        }

        _rotatePressLeft = false;
        _rotatePressRight = false;
        SyncClassCaptions();
        GD.Print($"[CharSelectWindow] Create form opened (uiClass={_createUiClass} → " +
                 $"internal={UiToInternal[_createUiClass]}); stats reset to seed {StatFloor}. " +
                 "spec §4 / §11.5h Groups 7–8; character_creation.md §2.1.");
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        if (_perSlotControls is not null) _perSlotControls.Visible = false;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = false;
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createPreview3D.Visible = false;
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = true;
        HideModal(_modalCreateClass);
        HideModal(_modalCreateName);
        _rotatePressLeft = false;
        _rotatePressRight = false;
    }

    private void EnsureCreatePreview3D()
    {
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D)) return;
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            MouseFilter = MouseFilterEnum.Ignore,
            SharedRealAssets = _realAssets,
            InternalClassId = UiToInternal[_createUiClass],
            Visible = false
        };
        _createPreview3D.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_createPreview3D);
        MoveChild(_createPreview3D, 1); // just above the 3D lineup, below 2D chrome
    }

    // =========================================================================
    // Create-NAME modal (act 62 → raise; act 54 → confirm). spec §4.1.1 / §11.5h Group 10.
    // =========================================================================

    private void ShowCreateNameModal()
    {
        HideModal(_modalCreateClass);
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);
        if (_nameEntry is not null && IsInstanceValid(_nameEntry))
            _nameEntry.Text = string.Empty;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;
        _toastTimer = 0.0;
        if (_modalCreateName is not null) _modalCreateName.Visible = true;
        GD.Print($"[CharSelectWindow] create-NAME modal opened for class {_createUiClass}. spec §11.5h G10.");
    }

    private void OnCreateNameConfirm()
    {
        var name = _nameEntry?.Text.Trim() ?? string.Empty;
        if (!ValidateName(name, out var toastMsg))
        {
            ShowToast(toastMsg);
            GD.Print($"[CharSelectWindow] create name rejected: '{name}' → toast.");
            return;
        }

        var internalClass = UiToInternal[_createUiClass];

        // Collect the 5 point-buy stat display values in Stat0..Stat4 order.
        // NON-SEQUENTIAL spinner pairing (binary-confirmed SHA 263bd994):
        //   Stat0 = spinners 25+/30−  (_statValues[0])
        //   Stat1 = spinners 26+/31−  (_statValues[1])
        //   Stat2 = spinners 27+/32−  (_statValues[2])
        //   Stat3 = spinners 29+/34−  (_statValues[3])
        //   Stat4 = spinners 28+/33−  (_statValues[4])
        // These are passed verbatim; layer-04 Application validates/normalises before the 1/6 send.
        // spec: Docs/RE/scenes/charselect.md §4.3; Docs/RE/specs/character_creation.md §2.1.
        var stats = new int[StatGridRows];
        for (var i = 0; i < StatGridRows; i++)
            stats[i] = _statValues[i];

        GD.Print($"[CharSelectWindow] CreateCharacterRequested: name='{name}' " +
                 $"class={internalClass} face={_createFaceIndex} " +
                 $"stats=({stats[0]},{stats[1]},{stats[2]},{stats[3]},{stats[4]}). " +
                 "spec §4/§8; charselect.md §4.3; character_creation.md §2.1.");
        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex, stats);
        HideModal(_modalCreateName);
        HideCreateForm();
        RefreshCharCountCaption();
    }

    // =========================================================================
    // Slot-select confirm (act 6 move/relocate; also the slot-select route). spec §HandleCommand.
    // =========================================================================

    private void OnSlotSelectConfirm()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            ShowCreateForm();
            return;
        }

        EmitSignal(SignalName.EnterGameRequested, ls.Name, _selectedSlot);
    }

    // =========================================================================
    // Class caption sync — class-pick changes the create-preview + captions.
    // =========================================================================

    private void SetCreateClass(int uiIndex)
    {
        _createUiClass = Math.Clamp(uiIndex, 0, 3);
        SyncClassCaptions();

        // Class change REBUILDS the 3D create-preview actor (the ONLY create-form action that does —
        // face/appearance steps are 2D-only, §4.2). The backdrop/camera/environment persist.
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            _createPreview3D.RebuildForClass();
        }

        // Per-class preview voice/BGM (replaces the lobby BGM on the single category-0 slot, by UI
        // index: UI 0→Monk 910065000, 1→Musa 910062000, 2→Salsu 910064000, 3→Dosa 910063000).
        // spec: Docs/RE/specs/sound.md §15.6b + §4.1 voice-SFX table. CODE-CONFIRMED.
        Audio?.PlayClassPreviewBgm(_createUiClass);

        GD.Print($"[CharSelectWindow] class change → uiClass={_createUiClass} " +
                 $"(internal={UiToInternal[_createUiClass]}); 3D preview rebuilt + per-class BGM. " +
                 "spec §4.1 / sound.md §15.6b.");
    }

    private void SyncClassCaptions()
    {
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);

        var lines = GetDescLines(_createUiClass);
        if (_createDescLine0 is not null && IsInstanceValid(_createDescLine0))
            _createDescLine0.Text = lines.Length > 0 ? lines[0] : string.Empty;
        if (_createDescLine1 is not null && IsInstanceValid(_createDescLine1))
            _createDescLine1.Text = lines.Length > 1 ? lines[1] : string.Empty;
        if (_createDescLine2 is not null && IsInstanceValid(_createDescLine2))
            _createDescLine2.Text = lines.Length > 2 ? lines[2] : string.Empty;
    }

    private string ClassCaption(int uiIndex)
    {
        var safe = Math.Clamp(uiIndex, 0, 3);
        return Text?.GetCaption(ClassMsgIds[safe], string.Empty) ?? string.Empty;
    }

    private string[] GetDescLines(int uiIndex)
    {
        // VERBATIM three lines (empties preserved) so each maps 1:1 onto its description label —
        // a blank middle line keeps line 3 on label 3 (the 3-line npc.scr render polish).
        // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 (three CP949 lines, top to bottom).
        return _npcScrDesc.GetDescriptionLines(Math.Clamp(uiIndex, 0, 3));
    }

    // =========================================================================
    // Slot descriptors push to 3D scene (reused seam). spec §3.5.2.
    // =========================================================================

    private void PushSlotDescriptors()
    {
        if (_scene3D is null) return;

        var descs = new CharSelectScene3D.SlotDescriptor[MaxSlots];
        for (var i = 0; i < MaxSlots; i++)
        {
            var ls = _slots[i];
            if (ls.IsEmpty)
            {
                descs[i] = default;
                continue;
            }

            var internalClass = ls.InternalClass != 0 ? ls.InternalClass : ls.ServerClass;
            // spec: Docs/RE/structs/spawn_descriptor.md (+0x34 internal_class, +0x2C variant, +0x2E faceA, +0x58 equip table);
            //       Docs/RE/packets/3-1_character_list.yaml
            descs[i] = new CharSelectScene3D.SlotDescriptor(
                true,
                internalClass, // +0x34 verbatim (ServerClass fallback when 0)
                ls.AppearanceVariant, // +0x2C
                ls.FaceA, // +0x2E
                ls.EquipGids.IsDefaultOrEmpty ? null : ls.EquipGids.ToArray()); // +0x58
        }

        _scene3D.SlotDescriptors = descs;
    }

    // =========================================================================
    // Info-row + char-count caption refresh. spec §3.2 / §3.8.2.
    // =========================================================================

    private void RefreshInfo()
    {
        var ls = _slots[_selectedSlot];

        if (_infoName is not null && IsInstanceValid(_infoName))
            _infoName.Text = ls.IsEmpty ? string.Empty : ls.Name;
        if (_infoLevel is not null && IsInstanceValid(_infoLevel))
            _infoLevel.Text = ls.IsEmpty ? string.Empty : ls.Level.ToString();
        // Info-row line 3 = world position "%d , %d" (PosX, PosZ). spec §3.2 CYCLE-6b fact 2.
        if (_infoPosition is not null && IsInstanceValid(_infoPosition))
            _infoPosition.Text = ls.IsEmpty ? string.Empty : $"{(int)ls.PosX} , {(int)ls.PosZ}";

        RefreshAction61Overlay(ls);
    }

    /// <summary>
    ///     Per-slot gating of the action-61 conditional overlay button (bi#33).
    ///     spec §3.4 / §11.5c: shown iff the +0x1548+slot byte is 0 (selectable), hidden if
    ///     nonzero (locked/creating).
    ///     OPEN@DEBUGGER spec-flag: the exact +0x1548 byte semantic (occupied vs locked vs
    ///     deletion-pending) and the button's on-screen role are debugger-pending. SENSIBLE
    ///     DEFAULT: we treat an OCCUPIED slot as "byte 0 → overlay shown" (the overlay then
    ///     routes to delete/manage on the live slot); an empty slot hides it.
    /// </summary>
    private void RefreshAction61Overlay(LiveSlot ls)
    {
        if (_overlayAction61 is null || !IsInstanceValid(_overlayAction61)) return;
        // SENSIBLE DEFAULT mapping of the +0x1548 byte: occupied slot ⇒ byte 0 ⇒ shown.
        _overlayAction61.Visible = !ls.IsEmpty;
    }

    private string BuildCharCountCaption()
    {
        var count = 0;
        for (var i = 0; i < MaxSlots; i++)
            if (!_slots[i].IsEmpty)
                count++;

        var template = Text?.GetCaption(MsgCharCount, string.Empty) ?? string.Empty;
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return template.Contains("%d")
            ? template.Replace("%d", count.ToString())
            : string.Format(template, count);
    }

    private void RefreshCharCountCaption()
    {
        if (_charCountCaption is null || !IsInstanceValid(_charCountCaption)) return;
        _charCountCaption.Text = BuildCharCountCaption();
    }
}