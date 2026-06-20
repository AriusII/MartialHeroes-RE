// Ui/Scenes/Select/CharSelectWindow.ListView.cs
//
// Character-select window — SELECT view layout + slot list refresh.
// Part of the CharSelectWindow partial class split (Wave 4b).
//
// Covers: BuildUi(), PushSlotDescriptors(), RefreshInfo(), RefreshSlotButtons(),
//         RefreshCharCountCaption(), BuildCharCountCaption().
//
// PASSIVE: zero game logic, zero domain state.
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4.
// spec: Docs/RE/specs/frontend_scenes.md §3/§11.

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // =========================================================================
    // UI construction — SELECT view
    // =========================================================================

    private void BuildUi()
    {
        // ── LAYER 0: 3D scene SubViewport (full canvas, bottom layer) ──
        // spec: Docs/RE/specs/frontend_scenes.md §3. CODE-CONFIRMED.
        Build3DSceneViewport();

        // ── LAYER 1: 2D chrome (drawn over the 3D viewport) ──

        // Top tabs: Server (1), Channel (2), Back (3).
        // spec: Docs/RE/specs/frontend_scenes.md §11.5b. CODE-CONFIRMED.
        BuildTabButton(
            67, 17, 113, 40,
            675, 795,
            675, 795,
            483, 883, // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            ActionServer);

        BuildTabButton(
            232, 7, 113, 40,
            640, 742,
            640, 742,
            483, 923, // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            ActionChannel);

        BuildTabButton(
            393, 17, 113, 40,
            625, 691,
            625, 691,
            483, 963, // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            ActionBack);

        // Char-count caption — msg 2209, orange, top-centre.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2. CODE-CONFIRMED.
        _charCountCaptionWidget = HudWidgetFactory.MakeLabel(
            0, 12, (int)RefW, 28,
            BuildCharCountCaption(),
            new Color(0.95f, 0.86f, 0.55f));
        if (_charCountCaption is not null)
        {
            _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
            AddChild(_charCountCaption);
        }

        // Left char-info background art: loginwindow.dds src(556,542) 215×147.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d. CODE-CONFIRMED.
        const float infoPanelX = (RefW - 215f) / 2f; // spec §11.5a "X=(W-215)/2". CODE-CONFIRMED.
        const float infoPanelY = 575f; // spec §11.5a "y=575". CODE-CONFIRMED.
        AddAtlasRect(AtlasLoginWindow,
            (int)infoPanelX, (int)(infoPanelY + 142f),
            215, 147, 556, 542); // spec §11.5d. CODE-CONFIRMED.

        // Chrome plates A/B/C from loginwindow.dds.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5a. CODE-CONFIRMED.
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 0, (int)infoPanelY + 12, 200, 46, 608, 793);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 200, (int)infoPanelY + 0, 176, 58, 608, 735);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 376, (int)infoPanelY + 12, 201, 46, 608, 689);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 215, (int)infoPanelY + 0, 29, 22, 556, 729);

        // Info labels (name / level / position) — 3 labels only, NO stat grid on select view.
        // D1: The 96-byte stats block is NOT consumed on the char-select screen. A 2×5 stat-icon
        // grid on the select view is a port fabrication — REMOVED. Spec §3.1 / CYCLE-6b fact 1.
        // D2: Third label = world position, formatted "%d , %d" (PosX, PosZ truncated to int).
        // spec: Docs/RE/specs/frontend_scenes.md §3.2 / CYCLE-6b fact 2. CODE-CONFIRMED.
        var infoLX = infoPanelX + 46f;
        _infoNameWidget = AddInfoLabel(new Vector2(infoLX, infoPanelY + 145f));
        _infoLevelWidget = AddInfoLabel(new Vector2(infoLX, infoPanelY + 169f));
        _infoPositionWidget = AddInfoLabel(new Vector2(infoLX, infoPanelY + 193f));

        // Roster action buttons: Create(4) / Delete(5) / Enter(6) at dst-Y 112 (centred).
        // D5: visibility is slot-occupancy-gated (see RefreshSlotButtons).
        // spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 / CYCLE-6b fact 3. CODE-CONFIRMED.
        // NOTE: action id 61 (highlight art) is omitted — no atlas slice recovered for it.
        // spec: Docs/RE/specs/frontend_scenes.md §3.4 (id 61 = highlight button, atlas-pending).
        const float rosterBarCX = RefW / 2f;
        const float rosterBarLeft = rosterBarCX - (3f * RosterBtnW + 2f * 8f) / 2f;

        _btnCreate = BuildRosterButton((int)rosterBarLeft, (int)RosterBtnY,
            0, 1004, 59, 1004, // spec §8.4. CODE-CONFIRMED.
            ActionCreate);
        _btnDelete = BuildRosterButton((int)(rosterBarLeft + RosterBtnW + 8f), (int)RosterBtnY,
            118, 1004, 177, 1004, // spec §8.4. CODE-CONFIRMED.
            ActionDelete);
        _btnEnter = BuildRosterButton((int)(rosterBarLeft + 2f * (RosterBtnW + 8f)), (int)RosterBtnY,
            236, 1004, 295, 1004, // spec §8.4. CODE-CONFIRMED.
            ActionEnter);

        // No corner-X close widget — the select-window builder constructs none.
        // Window close is the system-close message handler (ESC / system close → state 6 / sub-state 8).
        // spec: Docs/RE/specs/ui_system.md §8.2 ("Window close — NO corner-X widget; premise REFUTED").

        // 3D ray-pick on the viewport container.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.3. CODE-CONFIRMED.
        if (_scene3DContainer is not null)
            _scene3DContainer.GuiInput += OnViewport3DGuiInput;

        // Create sub-form (initially hidden).
        _createForm = BuildCreateForm();
        _createForm.Visible = false;
        AddChild(_createForm);

        RefreshInfo();

        GD.Print($"[CharSelectWindow] built; atlas={Atlas is not null}; " +
                 "slots=BLANK-until-CharacterListEvent; 3D viewport deferred. " +
                 "spec: ui_system.md §8.2/§8.4.");
    }

    // =========================================================================
    // Slot descriptors push to 3D scene
    // =========================================================================

    private void PushSlotDescriptors()
    {
        if (_scene3D is null) return;
        var descs = new (bool IsOccupied, uint SkinClassId)[MaxSlots];
        for (var i = 0; i < MaxSlots; i++)
        {
            var ls = _slots[i];
            descs[i] = (!ls.IsEmpty, !ls.IsEmpty ? ls.ServerClass : 0u);
        }

        _scene3D.SlotDescriptors = descs;
    }

    // =========================================================================
    // Info refresh
    // =========================================================================

    private void RefreshInfo()
    {
        var ls = _slots[_selectedSlot];

        // Name: CP949 string @ descriptor +0x00 (17B cell, ≤16 chars + NUL, space-trimmed).
        // Already decoded and space-trimmed by SpawnDescriptorReader.ReadName() / Cp949Text.Decode().
        // spec: Docs/RE/specs/login_flow.md §3.2.1 (+0x00, 17B, CP949). CODE-CONFIRMED.
        if (_infoName is not null && IsInstanceValid(_infoName))
            _infoName.Text = ls.IsEmpty ? string.Empty : ls.Name;

        // Level: u16 display field @ descriptor +0x3A (the char-select roster display level).
        // The enter-game path uses a different region (~+0x300) — NOT this field.
        // spec: Docs/RE/specs/login_flow.md §3.2.1 (+0x3A, u16 level display). CODE-CONFIRMED.
        if (_infoLevel is not null && IsInstanceValid(_infoLevel))
            _infoLevel.Text = ls.IsEmpty ? string.Empty : ls.Level.ToString();

        // D2: Info-row line 3 = world position, format exactly "%d , %d" (PosX, PosZ as int).
        // The class value is NOT shown here; it drives the 3D preview only (other lane).
        // Axis X/Z pairing is a strong static inference; value-axis is debugger-pending.
        // spec: Docs/RE/specs/frontend_scenes.md §3.2 (info-row line 3 = position "%d , %d";
        //       axis X/Z debugger-pending). CODE-CONFIRMED: CYCLE-6b fact 2.
        if (_infoPosition is not null && IsInstanceValid(_infoPosition))
            _infoPosition.Text = ls.IsEmpty
                ? string.Empty
                : $"{(int)ls.PosX} , {(int)ls.PosZ}";

        // D5: Gate slot action buttons by selected-slot occupancy.
        // spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 / CYCLE-6b fact 3.
        RefreshSlotButtons(ls.IsEmpty);
    }

    /// <summary>
    ///     Shows/hides Create vs Delete+Enter based on whether the selected slot is occupied.
    ///     Occupied  → Delete(5) + Enter(6) shown; Create(4) hidden.
    ///     Empty     → Create(4) shown; Delete(5) + Enter(6) hidden.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 (slot buttons gated by selected-slot
    ///     occupancy). CODE-CONFIRMED: CYCLE-6b fact 3.
    ///     NOTE: action id 61 (highlight art button) is omitted — no atlas slice recovered.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.4 (id 61 = highlight, atlas-pending).
    /// </summary>
    private void RefreshSlotButtons(bool isEmpty)
    {
        if (_btnCreate is not null && IsInstanceValid(_btnCreate))
            _btnCreate.Visible = isEmpty;
        if (_btnDelete is not null && IsInstanceValid(_btnDelete))
            _btnDelete.Visible = !isEmpty;
        if (_btnEnter is not null && IsInstanceValid(_btnEnter))
            _btnEnter.Visible = !isEmpty;
    }

    // =========================================================================
    // Char-count caption — msg 2209. spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
    // =========================================================================

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