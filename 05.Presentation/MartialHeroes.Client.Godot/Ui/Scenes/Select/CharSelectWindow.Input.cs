// Ui/Scenes/Select/CharSelectWindow.Input.cs
//
// Char-select window — 3D ray-pick, enter/delete intents, create-class/face view steps,
// name validation, toast, and mouse-up release for press-and-hold actions (70/71/72/73).
//
// Actions 70/71 set the press-and-hold actor-yaw flags (_rotatePressLeft/_rotatePressRight)
// on mouse-down; those flags are cleared here on mouse-up so the model stops spinning.
// Actions 72/73 drive camera boom-zoom via CharSelectScene3D.SetCameraZoomAction; the zoom
// field is reset to 0 on mouse-up (same release discipline as the original field at main+6276).
// spec: Docs/RE/scenes/charselect.md §6.3 (yaw ±2·dt; boom-zoom dt×10; reset on mouse-up).
//
// PASSIVE: zero game logic, zero domain state. Turns gestures into signals (intents).
// spec: Docs/RE/specs/frontend_scenes.md §3/§4/§11.
// spec: Docs/RE/scenes/charselect.md §6.3.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // =========================================================================
    // 3D ray-pick. spec: frontend_scenes.md §3.3.3. CODE-CONFIRMED.
    // =========================================================================

    private void OnViewport3DGuiInput(InputEvent ev)
    {
        if (_scene3D is null || _scene3DViewport is null || _scene3DContainer is null) return;
        if (_createFormVisible) return;

        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            Vector2 vpSize = new(_scene3DViewport.Size.X, _scene3DViewport.Size.Y);
            var ctrlSize = _scene3DContainer.Size;
            var scaleX = ctrlSize.X > 0f ? vpSize.X / ctrlSize.X : 1f;
            var scaleY = ctrlSize.Y > 0f ? vpSize.Y / ctrlSize.Y : 1f;
            Vector2 vpPos = new(mb.Position.X * scaleX, mb.Position.Y * scaleY);

            var hit = _scene3D.TryHitTestSlot(vpPos);
            if (hit >= 0)
            {
                _selectedSlot = hit;
                RefreshInfo();
                _scene3D?.SetSelectedSlot(hit);
                GD.Print($"[CharSelectWindow] 3D ray-pick → slot {hit}. spec §3.3.3.");
            }
        }
    }

    // =========================================================================
    // Press-and-hold release for actions 70/71 (actor yaw) and 72/73 (boom-zoom).
    // Called from the action buttons' mouse-up events so the model/camera stops.
    // spec: charselect.md §6.3 — "reset field to 0 on mouse-up" (boom-zoom);
    //       yaw: the same press-and-hold convention applied symmetrically.
    // =========================================================================

    /// <summary>
    ///     Clears the actor-yaw left flag (action 70 mouse-up). The yaw tick in _Process stops.
    ///     spec: Docs/RE/scenes/charselect.md §6.3 (actor yaw — SelectWindow_TickSelectedPreviewYaw).
    /// </summary>
    internal void OnActorYawLeftReleased()
    {
        _rotatePressLeft = false;
    }

    /// <summary>
    ///     Clears the actor-yaw right flag (action 71 mouse-up). The yaw tick in _Process stops.
    ///     spec: Docs/RE/scenes/charselect.md §6.3.
    /// </summary>
    internal void OnActorYawRightReleased()
    {
        _rotatePressRight = false;
    }

    /// <summary>
    ///     Resets the camera boom-zoom action field to 0 (mouse-up for actions 72/73).
    ///     The camera rig stops accumulating distance on the next _Process tick.
    ///     spec: Docs/RE/scenes/charselect.md §6.3 — "reset field to 0 on mouse-up; no clamp".
    /// </summary>
    internal void OnBoomZoomReleased()
    {
        GetCameraRig()?.SetZoomAction(0); // 0 = no active zoom action. spec: charselect.md §6.3
    }

    // =========================================================================
    // Enter / Delete intents (routed from OnAction).
    // =========================================================================

    private void OnEnterPressed()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Enter on empty slot {_selectedSlot} → Create form.");
            ShowCreateForm();
            return;
        }

        // OPEN@DEBUGGER spec-flag: the server-supplied per-slot occupied/selectable flag
        // (+0x148C + slot) gates the 1/7 select/manage click in the original binary.
        // spec: Docs/RE/specs/frontend_scenes.md §3.4.
        // SENSIBLE DEFAULT: we treat a non-empty LiveSlot as selectable (byte nonzero = occupied).
        // The lock/creating flag (+0x1548) would also block enter; we have no runtime source for
        // that byte yet. When the server sends the per-slot flag in 3/1 roster, wire it here.
        GD.Print($"[CharSelectWindow] EnterGameRequested: name='{ls.Name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.EnterGameRequested, ls.Name, _selectedSlot);
    }

    private void OnDeletePressed()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Delete on empty slot {_selectedSlot} ignored.");
            return;
        }

        // Raise the delete-confirm modal C (act 64). spec §11.5h Group 4 / §HandleCommand.
        _pendingDeleteSlot = _selectedSlot;
        if (_deleteTitleWidget?.GetControl() is Label t)
            t.Text = Text?.GetCaption(MsgDeleteConfirmTitle, string.Empty) ?? string.Empty;
        if (_deleteBodyWidget?.GetControl() is Label b)
            b.Text = Text?.GetCaption(MsgDeleteConfirmBody, string.Empty) ?? string.Empty;
        if (_modalDeleteCfm is not null) _modalDeleteCfm.Visible = true;
        GD.Print($"[CharSelectWindow] delete-confirm modal raised for slot {_selectedSlot}. spec §11.5h G4.");
    }

    /// <summary>
    ///     Delete-confirm OK (act 64). Emits DeleteCharacterRequested ONLY after confirmation.
    ///     spec §11.5h Group 4 / §5.
    /// </summary>
    private void OnDeleteConfirmYes(int _)
    {
        var slot = _pendingDeleteSlot;
        if (_modalDeleteCfm is not null) _modalDeleteCfm.Visible = false;
        _pendingDeleteSlot = -1;

        if (slot < 0 || slot >= MaxSlots) return;
        var ls = _slots[slot];
        if (ls.IsEmpty) return;

        GD.Print($"[CharSelectWindow] delete confirmed: name='{ls.Name}' slot={slot}; " +
                 "emitting DeleteCharacterRequested. spec §5.");
        EmitSignal(SignalName.DeleteCharacterRequested, slot, ls.Name);
    }

    // =========================================================================
    // Rename modal (act 59 OK / act 60 Cancel). Same name rule as create (§4.4).
    // spec: Docs/RE/specs/frontend_scenes.md §6; Docs/RE/packets/cmsg_char_rename.yaml.
    // =========================================================================

    /// <summary>
    ///     Raises the rename modal E for the currently-selected occupied slot, seeding the entry with
    ///     the current name. View-only (no send). OPEN@DEBUGGER: the exact on-screen control that opens
    ///     rename in the original is debugger-pending; we route it from a sensible-default trigger
    ///     (see OnAction). spec §6 / §11.5h Group 11.
    /// </summary>
    private void ShowRenameModal()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Rename on empty slot {_selectedSlot} ignored. spec §6.");
            return;
        }

        _pendingRenameSlot = _selectedSlot;
        if (_renameModalTitle is not null && IsInstanceValid(_renameModalTitle))
            _renameModalTitle.Text = ls.Name;
        if (_renameEntry is not null && IsInstanceValid(_renameEntry))
            _renameEntry.Text = ls.Name;
        if (_renameToast is not null && IsInstanceValid(_renameToast))
            _renameToast.Visible = false;
        if (_modalRename is not null) _modalRename.Visible = true;

        GD.Print(
            $"[CharSelectWindow] rename modal raised for slot {_selectedSlot} ('{ls.Name}'). spec §6 / §11.5h G11.");
    }

    /// <summary>
    ///     Rename OK (act 59). Validates the new name locally (same §4.4 rule as create) and, only on a
    ///     valid name, emits <c>RenameCharacterRequested(slot, newName)</c> — SelectScene forwards it to
    ///     <c>UseCases.RenameCharacterAsync</c> (1/13, 18-byte body). Invalid → modal-local toast, no emit.
    ///     PASSIVE: no domain mutation, no optimistic rename. spec §6 / §4.4.
    /// </summary>
    private void OnRenameConfirm()
    {
        var slot = _pendingRenameSlot;
        if (slot < 0 || slot >= MaxSlots || _slots[slot].IsEmpty)
        {
            HideModal(_modalRename);
            _pendingRenameSlot = -1;
            return;
        }

        var newName = _renameEntry?.Text.Trim() ?? string.Empty;
        if (!ValidateName(newName, out var toastMsg))
        {
            if (_renameToast is not null && IsInstanceValid(_renameToast))
            {
                _renameToast.Text = toastMsg;
                _renameToast.Visible = true;
            }

            GD.Print($"[CharSelectWindow] rename rejected: '{newName}' → toast (no send). spec §6/§4.4.");
            return;
        }

        GD.Print($"[CharSelectWindow] RenameCharacterRequested: slot={slot} newName='{newName}'; " +
                 "emitting (→ UseCases.RenameCharacterAsync, 1/13). spec §6.");
        EmitSignal(SignalName.RenameCharacterRequested, slot, newName);
        HideModal(_modalRename);
        _pendingRenameSlot = -1;
    }

    // =========================================================================
    // Create-form view steps (class-pick / face). Routed from OnAction.
    // =========================================================================

    private void OnCreateClassAction(int actionId)
    {
        var uiIndex = actionId - ActClass0; // 10–13 → 0–3.
        if (uiIndex >= 0 && uiIndex < 4) SetCreateClass(uiIndex);
    }

    private void OnFaceAction(int actionId)
    {
        // act 21 = increment, 22 = decrement. spec §4.2. CODE-CONFIRMED.
        var delta = actionId == ActFacePlus ? +1 : -1;
        var before = _createFaceIndex;
        _createFaceIndex = Math.Clamp(_createFaceIndex + delta, FaceMin, FaceMax);

        // Thread the face index into the 3D create preview as PASSIVE view state. Per
        // frontend_scenes.md §4.2 (CODE-CONFIRMED) face stepping does NOT rebuild the 3D actor — only a
        // class change re-spawns the mesh — so UpdateFaceIndex only records the value (it does not
        // re-spawn). spec: Docs/RE/specs/frontend_scenes.md §4.2 ("Face ± does NOT rebuild the 3D actor").
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createFaceIndex = _createPreview3D.UpdateFaceIndex(_createFaceIndex);

        // Real on-screen feedback: the face-value label (bi#90) + a brief create-form toast.
        if (_createFaceLabel is not null && IsInstanceValid(_createFaceLabel))
            _createFaceLabel.Text = _createFaceIndex.ToString();
        ShowCreateToast(_createFaceIndex == before
            ? $"FACE {_createFaceIndex} ({FaceMin}..{FaceMax})" // clamped at the rail
            : $"FACE {_createFaceIndex}");

        GD.Print($"[CharSelectWindow] face={_createFaceIndex} (range {FaceMin}..{FaceMax}); " +
                 "2D-only (no 3D rebuild). spec §4.2.");
    }

    /// <summary>
    ///     Appearance / sub-spinner steppers (acts 25/26, 27–36, 66–69). FAITHFUL: 2D-only — they
    ///     record the point-buy stat display value into the window's <c>_statValues</c> array (collected
    ///     later at create-confirm and passed via the signal to layer-04 Application) AND forward the step
    ///     to the passive appearance-seed on the 3D preview (no rebuild, §4.2). NO point-buy math here —
    ///     layer-04 Application validates/normalises before writing the 52-byte 1/6 body.
    ///     <para>
    ///         The 5 point-buy stats map to <c>_statValues</c> indices 0..4 via the binary-confirmed
    ///         non-sequential spinner pairing (seeds 0..4 = Stat0..Stat4, confirmed per
    ///         <c>create_payload_layout.md</c> SHA 263bd994).
    ///     </para>
    ///     spec: Docs/RE/scenes/charselect.md §4.3 (spinner action ids — binary-confirmed non-sequential).
    ///     spec: Docs/RE/specs/character_creation.md §2.1 (point-buy: floor 10, seed 5 budget).
    /// </summary>
    private void OnAppearanceStep(int actionId, int seedIndex, int delta, string label)
    {
        // --- Record into the window's own stat display values (Stat0..4, floor = StatFloor = 10). ---
        // Layer-05 only records what the player pressed; NO math, NO budget enforcement here
        // (that is layer-04 Application's responsibility via BuildPointBuy / PointBuySeed).
        // spec: Docs/RE/specs/character_creation.md §2.1.
        var stored = 0;
        if (seedIndex >= 0 && seedIndex < StatGridRows)
        {
            // Clamp at the floor so the displayed value never goes below 10 (passive guard — the real
            // enforcement lives in Application; this just prevents obviously wrong display values).
            var next = Math.Max(StatFloor, _statValues[seedIndex] + delta);
            _statValues[seedIndex] = next;
            stored = next;
        }

        // --- Forward 2D-only step to the 3D create preview (NO actor rebuild, §4.2). ---
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createPreview3D.UpdateAppearanceSeed(seedIndex, delta); // 2D passive sink, no rebuild

        ShowCreateToast($"{label} {stored}");
        GD.Print($"[CharSelectWindow] appearance step action {actionId}: seed[{seedIndex}] " +
                 $"{(delta >= 0 ? "+" : "")}{delta} → _statValues[{seedIndex}]={stored} (2D-only; no 3D rebuild). " +
                 "spec charselect.md §4.3; character_creation.md §2.1.");
    }

    // =========================================================================
    // Name validation. spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
    // =========================================================================

    private bool ValidateName(string name, out string toastMsg)
    {
        if (name.Length < 2)
        {
            toastMsg = Text?.GetCaption(2190u, string.Empty) ?? string.Empty;
            return false;
        }

        foreach (var c in name)
        {
            if (c >= 'a' && c <= 'z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c >= '가' && c <= '힣') continue;
            if (c >= 'ᄀ' && c <= 'ᇿ') continue;
            if (c >= '㄰' && c <= '㆏') continue;
            toastMsg = Text?.GetCaption(12012, string.Empty) ?? string.Empty;
            return false;
        }

        toastMsg = string.Empty;
        return true;
    }

    private void ShowToast(string message)
    {
        if (_nameToast is null || !IsInstanceValid(_nameToast)) return;
        _nameToast.Text = message;
        _nameToast.Visible = true;
        _toastTimer = 3.0;
    }

    /// <summary>
    ///     Brief create-form feedback toast — a dedicated overlay label on the window root (NOT the
    ///     name-modal toast, which is hidden while the create form is open). Used by the face /
    ///     appearance steppers to give real on-screen feedback for a 2D-only view step. Auto-hides on
    ///     the shared toast timer in _Process. Strictly passive (view state only).
    /// </summary>
    private void ShowCreateToast(string message)
    {
        if (_createToast is null || !IsInstanceValid(_createToast)) return;
        _createToast.Text = message;
        _createToast.Visible = true;
        _createToastTimer = 2.0;
    }
}