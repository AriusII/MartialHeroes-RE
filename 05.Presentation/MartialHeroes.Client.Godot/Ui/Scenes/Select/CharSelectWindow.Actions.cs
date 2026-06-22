// Ui/Scenes/Select/CharSelectWindow.Actions.cs
//
// The action-id model and the single OnAction(int) command dispatcher — the SelectWindow's
// command vocabulary. Rebuilt to the DEFINITIVE binary-confirmed map (build 263bd994,
// SelectWindow_HandleCommand type-4 switch body, fully settled).
//
// CREATE-FLOW IDs (binary-confirmed):
//   35 = CREATE-CONFIRM  → OnCreateNameConfirm() → EmitSignal CreateCharacterRequested (1/6 52B).
//   36 = CREATE-CANCEL   → HideModal + HideCreateForm (SelectWindow_ResetScene + scene BGM).
//   25-34 = stat spinners (10 ids, 5 stats × +/−; PASSIVE toast/preview only — no point-buy math here).
//   21/22 = FACE+/FACE−  (clamp 1..7, 2D only).
//   10–13 = class pick.
//
// SELECT / RENAME / ENTER IDs:
//   54 = slot-lock select confirm (1/7 mode 0).   55 = select-slot cancel.
//   59 = rename confirm (1/13).                   60 = rename cancel.
//   62 = delete/move-out confirm (GATED, 1/14).   63 = cancel.
//   64 = plain panel-close (no send).
//   72/73 = camera boom-zoom drag-hold.            70/71 = actor-yaw drag-hold.
//
// spec: Docs/RE/scenes/charselect.md §4.3 + §6.3.
// Dirty-file confirmation: Docs/RE/_dirty/cycle12/charselect/create_flow_actions.md (SHA 263bd994).

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // -------------------------------------------------------------------------
    // Action-id constants — DEFINITIVE binary-confirmed map (build 263bd994).
    // spec: Docs/RE/scenes/charselect.md §4.3.
    // Dirty-file: Docs/RE/_dirty/cycle12/charselect/create_flow_actions.md.
    // -------------------------------------------------------------------------

    // ---- Select-scene top strip / entry actions (G1/G5) ----
    private const int ActCmdCreateNew = 1; // create-new (open create form)      spec: charselect.md §4.3
    private const int ActCmdEnterGame = 2; // enter-world (1/9, boom-settle gated) spec: charselect.md §4.3
    private const int ActCmdCreateAlt = 3; // select/preview slot                 spec: charselect.md §4.3
    private const int ActBottom4 = 4; // confirm on panel this+5816           spec: charselect.md §4.3
    private const int ActBottom5 = 5; // select-and-play (1/7 mode1)          spec: charselect.md §4.3

    private const int ActBottom6 = 6; // commit-highlight slot                spec: charselect.md §4.3

    // bi#33 — conditionally opens the move/relocate overlay (+5364) when a slot is picked.
    private const int ActConditionalOverlay = 61; // '=' conditional play overlay         spec: charselect.md §4.3

    // ---- Class-pick tabs (G8) — create sub-form only. UI index = act − 10. ----
    // 10=Monk(internal 4), 11=Musa(1), 12=Dosa(3), 13=Salsu(2). spec: charselect.md §4.3.
    private const int ActClass0 = 10; // base of 4 class-pick ids (10..13)   spec: charselect.md §4.3

    // ---- Face ± steppers (create form, 2D only, clamp 1..7) ----
    private const int ActFacePlus = 21; // FACE + (clamp max 7)                spec: charselect.md §4.3
    private const int ActFaceMinus = 22; // FACE − (clamp min 1)                spec: charselect.md §4.3

    // ---- Stat spinners (create form, 10 ids — 25..34, 5 stats × +/−). ----
    // PASSIVE: toast/preview only; no point-buy math in this layer.
    // Pairing (NON-sequential per binary): Stat0 25+/30−, Stat1 26+/31−, Stat2 27+/32−,
    //   Stat3 29+/34−, Stat4 28+/33−. ColA (increment) = 25..29, ColB (decrement) = 30..34.
    // spec: charselect.md §4.3; dirty-file create_flow_actions.md spinnerActionIds.
    private const int ActSpinner25 = 25; // Stat0 INCREMENT (createBlob+0x1C)   spec: charselect.md §4.3
    private const int ActSpinner26 = 26; // Stat1 INCREMENT (createBlob+0x20)   spec: charselect.md §4.3
    private const int ActColA0 = 27; // ColA base: ids 27,28,29 = Stat2+,Stat4+,Stat3+ spec: charselect.md §4.3

    private const int ActColB0 = 30; // ColB base: ids 30..34 = Stat0−..Stat3−     spec: charselect.md §4.3
    // NOTE: ColA rows 0..4 produce ids 27,28,29 PLUS 25 and 26 are handled separately above.
    //       ColB rows 0..4 produce ids 30,31,32,33,34 — NONE of which overlap 35 or 36.

    // ---- CREATE-CONFIRM and CREATE-CANCEL (binary-confirmed, §4.3) ----
    // 35 = CREATE-CONFIRM: name validate → EmitSignal CreateCharacterRequested (1/6 52B).
    // 36 = CREATE-CANCEL:  reset-to-select-scene (SelectWindow_ResetScene + scene BGM).
    // spec: charselect.md §4.3; dirty-file create_flow_actions.md createConfirmAction/createCancelAction.
    private const int ActCreateConfirm = 35; // CREATE-CONFIRM (send 1/6)            spec: charselect.md §4.3
    private const int ActCreateCancel = 36; // CREATE-CANCEL / reset-to-select      spec: charselect.md §4.3

    // ---- Select-slot confirm panel (+0x17D0) — acts 54/55. ----
    // 54 = slot-lock select CONFIRM: sends opcode 1/7 mode 0.   ASCII '6'.
    // 55 = select-slot CANCEL: hides panel.                     ASCII '7'.
    // spec: charselect.md §4.3 selectConfirmActions.
    private const int ActSlotSelectConfirm = 54; // slot-lock select confirm (1/7 mode 0) spec: charselect.md §4.3
    private const int ActSlotSelectCancel = 55; // select-slot panel cancel              spec: charselect.md §4.3

    // ---- Rename panel (G11 / +0x179C) ----
    // 59 = rename CONFIRM: sends opcode 1/13 RenameCharacter (18-byte body). ASCII ';'.
    // 60 = rename CANCEL:  blur + disable-IME, hide rename panel.            ASCII '<'.
    private const int ActRenameOk = 59; // rename confirm (1/13)                spec: charselect.md §4.3
    private const int ActRenameCancel = 60; // rename cancel                        spec: charselect.md §4.3

    // ---- Move/relocate overlay (+0x15B0) — acts 62/63. ----
    // 62 = move/relocate CONFIRM: sends opcode 1/14 MoveCharacter. ASCII '>'.
    //   CRITICAL SAFETY: 1/14 is DESTRUCTIVE; send GATED/STUBBED until label is resolved.
    // 63 = move/relocate CANCEL: hides overlay.                    ASCII '?'.
    private const int ActRelocateConfirm = 62; // move/relocate confirm (GATED, 1/14)  spec: charselect.md §4.3
    private const int ActRelocateCancel = 63; // move/relocate cancel                 spec: charselect.md §4.3

    // ---- Overlay-close panel (+0x15C8) — action 64 PLAIN CLOSE (no message send). ----
    private const int ActPanelClose = 64; // plain panel-close (no send). '@'     spec: charselect.md §4.3

    // ---- Small notice panel (G9) ----
    private const int ActNoticePanel = 65; // debug log / small-notice dismiss. 'A' spec: charselect.md §4.3

    // ---- Actor-yaw BUTTON cluster (G8) — press-and-hold yaw (field this+6068/6072). ----
    // 66/67 = yaw+ button / hold; 68/69 = yaw− button / hold. ASCII 'B'..'E'.
    // spec: charselect.md §4.3 + §6.3 ("66..69 actor-yaw buttons, this+6068/6072").
    private const int ActAppSpin66 = 66; // actor-yaw button + (this+6068)       spec: charselect.md §6.3
    private const int ActAppSpin67 = 67; // actor-yaw button +/hold              spec: charselect.md §6.3
    private const int ActAppSpin68 = 68; // actor-yaw button − (this+6072)       spec: charselect.md §6.3
    private const int ActAppSpin69 = 69; // actor-yaw button −/hold              spec: charselect.md §6.3

    // ---- Actor-yaw DRAG-hold (G8) — field this+6084; cleared on drag-release. ----
    // 70 = yaw drag dir A; 71 = yaw drag dir B. ASCII 'F'/'G'.
    // spec: charselect.md §4.3 + §6.3 ("70/71 drive actor yaw; separate field from camera").
    private const int ActActorYawLeft = 70; // actor-yaw drag-hold dir A (this+6084) spec: charselect.md §6.3
    private const int ActActorYawRight = 71; // actor-yaw drag-hold dir B             spec: charselect.md §6.3

    // ---- Camera boom-zoom DRAG-hold — field this+6088; SelectWindow_TickCameraBoomZoom. ----
    // 72 = zoom dir A; 73 = zoom dir B. ASCII 'H'/'I'. No clamp per spec.
    // spec: charselect.md §4.3 + §6.3.
    private const int ActBoomZoomIn = 72; // camera boom-zoom drag-hold dir A     spec: charselect.md §6.3
    private const int ActBoomZoomOut = 73; // camera boom-zoom drag-hold dir B     spec: charselect.md §6.3

    // ---- Caption-only notice modal B (G3) / deselect-refresh (G5). ----
    // 74 = 'J' deselect / refresh-all preview actors; clear highlight. spec: charselect.md §4.3.
    private const int ActNoticeOk = 74; // deselect / refresh-all               spec: charselect.md §4.3

    // Corner CLOSE (bi#106) — sentinel, not one of the 42; plain parent-add.
    private const int ActCornerClose = 1000;

    // ---- Groups.cs / Groups2.cs forward aliases (canonical names used there). ----
    // These map the widget-builder constant names to the definitive ids above.
    // No backward-compat fiction: every alias maps to its CORRECT canonical id.
    // spec: charselect.md §4.3 (build 263bd994 confirmed).
    private const int ActCreateClassOk = ActRelocateConfirm; // 62 — move/relocate confirm (GATED)
    private const int ActCreateClassCancel = ActRelocateCancel; // 63 — move/relocate cancel
    private const int ActDeleteConfirm = ActPanelClose; // 64 — plain panel-close (no send)
    private const int ActNav70 = ActActorYawLeft; // 70 — actor-yaw drag-hold dir A
    private const int ActNav71 = ActActorYawRight; // 71 — actor-yaw drag-hold dir B
    private const int ActToggle72 = ActBoomZoomIn; // 72 — camera boom-zoom drag dir A

    private const int ActToggle73 = ActBoomZoomOut; // 73 — camera boom-zoom drag dir B
    // -------------------------------------------------------------------------
    // Camera rig reference — resolved lazily from the scene sub-tree so Actions.cs can
    // forward boom-zoom actions directly to CharSelectCameraRig.SetZoomAction without
    // requiring a public method on CharSelectScene3D.
    // spec: charselect.md §6.3 — boom-zoom driven by actions 72/73 on CharSelectCameraRig.
    // -------------------------------------------------------------------------

    private CharSelectCameraRig? _cameraRig;

    /// <summary>
    ///     Returns the first <see cref="CharSelectCameraRig" /> in the scene sub-tree, walking from
    ///     the 3D viewport on first call and caching the result. Returns null if the rig is not yet
    ///     created (dolly not started). Used exclusively for boom-zoom action forwarding (acts 72/73).
    ///     spec: charselect.md §6.3.
    /// </summary>
    private CharSelectCameraRig? GetCameraRig()
    {
        if (_cameraRig is not null && IsInstanceValid(_cameraRig))
            return _cameraRig;
        // The rig is a child of CharSelectScene3D, which lives inside _scene3DViewport.
        // FindChild walks the full sub-tree; owned = false includes all descendants.
        _cameraRig = _scene3DViewport?.FindChild("CharSelectCameraRig", true, false) as CharSelectCameraRig;
        return _cameraRig;
    }
    // REMOVED: ActCreateNameOk = 54 (was wrong — 54 = slot-select, not create-confirm).
    //          ActCreateNameCancel = 55 (was wrong — 55 = slot-select cancel).
    // Groups2.cs now uses ActCreateConfirm (35) and ActCreateCancel (36) directly.

    // -------------------------------------------------------------------------
    // Single command dispatcher — every bound action lands here.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Routes a fired action id to its intent. Mirrors SelectWindow_HandleCommand (type-4 switch).
    ///     PASSIVE: emits a signal (intent forwarded by SelectScene to IApplicationUseCases) or
    ///     raises/dismisses a view-only modal. NEVER mutates domain state, validates, or sends.
    ///     spec: Docs/RE/scenes/charselect.md §4.3 (action-id roster) + §6.3 (interaction).
    ///     Dirty-file: Docs/RE/_dirty/cycle12/charselect/create_flow_actions.md (SHA 263bd994).
    /// </summary>
    private void OnAction(int actionId)
    {
        // Generic UI click cue at the HEAD of each action, before the branch runs.
        // spec: Docs/RE/specs/sound.md §15.1 / §15.2 — owner window plays the category-2 cue.
        Audio?.PlayUiClick();

        switch (actionId)
        {
            // ---- Select-scene entry actions (1/2/3) ----
            case ActCmdCreateNew: // 1 = create-new → open create form
            case ActCmdCreateAlt: // 3 = select/preview slot (sensible default: open create form)
                ShowCreateForm();
                break;

            case ActCmdEnterGame: // 2 = enter-world (1/9, boom-settle gated)
                OnEnterPressed();
                break;

            // ---- Bottom roster row (4/5/6) ----
            case ActBottom4: // 4 = confirm on panel this+5816 (sensible default: open create form)
                ShowCreateForm();
                break;

            case ActBottom5: // 5 = select-and-play (1/7 mode1) — no-op pass-through here
                GD.Print("[CharSelectWindow] action 5 (select-and-play / no-op). spec charselect.md §4.3.");
                break;

            case ActBottom6: // 6 = commit-highlight slot (sensible default: slot-select confirm)
                OnSlotSelectConfirm();
                break;

            // ---- Conditional play overlay (61) ----
            // '=' Opens the select-confirm panel +5364 when a slot is picked; else error tooltip.
            // spec: charselect.md §4.3 action 61.
            case ActConditionalOverlay:
                if (_selectedSlot >= 0 && _selectedSlot < MaxSlots && !_slots[_selectedSlot].IsEmpty)
                {
                    if (_modalCreateClass is not null) _modalCreateClass.Visible = true;
                    GD.Print($"[CharSelectWindow] action 61 → open relocate overlay for slot {_selectedSlot}. " +
                             "spec charselect.md §4.3.");
                }
                else
                {
                    if (_noticeSmall is not null) _noticeSmall.Visible = true;
                    GD.Print("[CharSelectWindow] action 61 → no slot selected → error tooltip. " +
                             "spec charselect.md §4.3.");
                }

                break;

            // ---- Class-pick tabs (create form, 10–13) ----
            // 10=Monk(internal 4), 11=Musa(1), 12=Dosa(3), 13=Salsu(2). spec: charselect.md §4.3.
            case >= ActClass0 and <= ActClass0 + 3:
                OnCreateClassAction(actionId); // UI index = actionId − 10
                break;

            // ---- Face ± steppers (create form, 2D only, clamp 1..7) ----
            case ActFacePlus: // 21 = FACE + (clamp max 7)
            case ActFaceMinus: // 22 = FACE − (clamp min 1)
                OnFaceAction(actionId);
                break;

            // ---- Stat spinners (create form, ids 25-34, PASSIVE — no point-buy math here). ----
            // Binary-confirmed pairing (NON-sequential): Stat0 25+/30−, Stat1 26+/31−,
            //   Stat2 27+/32−, Stat3 29+/34−, Stat4 28+/33−.
            // spec: charselect.md §4.3; dirty-file create_flow_actions.md spinnerActionIds.
            case ActSpinner25: // 25 = Stat0 INCREMENT (createBlob+0x1C)
                OnAppearanceStep(actionId, 0, +1, "STAT0");
                break;
            case ActSpinner26: // 26 = Stat1 INCREMENT (createBlob+0x20)
                OnAppearanceStep(actionId, 1, +1, "STAT1");
                break;
            case ActColA0: // 27 = Stat2 INCREMENT (createBlob+0x24)
                OnAppearanceStep(actionId, 2, +1, "STAT2");
                break;
            case ActColA0 + 1: // 28 = Stat4 INCREMENT (createBlob+0x2C) NON-SEQUENTIAL
                OnAppearanceStep(actionId, 4, +1, "STAT4");
                break;
            case ActColA0 + 2: // 29 = Stat3 INCREMENT (createBlob+0x28) NON-SEQUENTIAL
                OnAppearanceStep(actionId, 3, +1, "STAT3");
                break;
            case ActColB0: // 30 = Stat0 DECREMENT (createBlob+0x1C)
                OnAppearanceStep(actionId, 0, -1, "STAT0");
                break;
            case ActColB0 + 1: // 31 = Stat1 DECREMENT (createBlob+0x20)
                OnAppearanceStep(actionId, 1, -1, "STAT1");
                break;
            case ActColB0 + 2: // 32 = Stat2 DECREMENT (createBlob+0x24)
                OnAppearanceStep(actionId, 2, -1, "STAT2");
                break;
            case ActColB0 + 3: // 33 = Stat4 DECREMENT (createBlob+0x2C) guard budget>0
                OnAppearanceStep(actionId, 4, -1, "STAT4");
                break;
            case ActColB0 + 4: // 34 = Stat3 DECREMENT (createBlob+0x28)
                OnAppearanceStep(actionId, 3, -1, "STAT3");
                break;

            // ---- CREATE-CONFIRM (35) — THE create send. ----
            // Name validate → EmitSignal CreateCharacterRequested → SelectScene → UseCases (1/6 52B).
            // spec: charselect.md §4.3 createConfirmAction=35; dirty-file create_flow_actions.md id=35.
            case ActCreateConfirm: // 35
                OnCreateNameConfirm();
                break;

            // ---- CREATE-CANCEL (36) — reset-to-select-scene. ----
            // Hides the name modal + create form (SelectWindow_ResetScene + scene BGM).
            // spec: charselect.md §4.3 createCancelAction=36; dirty-file create_flow_actions.md id=36.
            case ActCreateCancel: // 36
                HideModal(_modalCreateName);
                HideCreateForm();
                GD.Print("[CharSelectWindow] action 36 → CREATE-CANCEL: hide modal + reset to select. " +
                         "spec charselect.md §4.3.");
                break;

            // ---- Actor-yaw BUTTON cluster (66..69, this+6068/6072) ----
            // 66/67 = yaw+ button/hold (this+6068); 68/69 = yaw− button/hold (this+6072).
            // spec: charselect.md §4.3 + §6.3 ("66..69 actor-yaw buttons, this+6068/6072").
            case ActAppSpin66: // 66 yaw button + (this+6068)
            case ActAppSpin67: // 67 yaw button +/hold
                _rotatePressLeft = true;
                GD.Print($"[CharSelectWindow] action {actionId} → actor-yaw button + (this+6068). " +
                         "spec charselect.md §4.3/§6.3.");
                break;
            case ActAppSpin68: // 68 yaw button − (this+6072)
            case ActAppSpin69: // 69 yaw button −/hold
                _rotatePressRight = true;
                GD.Print($"[CharSelectWindow] action {actionId} → actor-yaw button − (this+6072). " +
                         "spec charselect.md §4.3/§6.3.");
                break;

            // ---- Actor-yaw DRAG-hold (70/71, this+6084) — press-and-hold. ----
            // 70 = yaw drag dir A; 71 = yaw drag dir B. Cleared on drag-release in Input.cs.
            // spec: charselect.md §4.3 + §6.3 ("70/71 drive actor yaw; separate field from camera").
            case ActActorYawLeft: // 70 actor-yaw drag-hold dir A (this+6084)
                _rotatePressLeft = true;
                GD.Print("[CharSelectWindow] action 70 → actor-yaw drag-hold dir A. spec charselect.md §6.3.");
                break;
            case ActActorYawRight: // 71 actor-yaw drag-hold dir B (this+6084)
                _rotatePressRight = true;
                GD.Print("[CharSelectWindow] action 71 → actor-yaw drag-hold dir B. spec charselect.md §6.3.");
                break;

            // ---- Camera boom-zoom DRAG-hold (72/73, this+6088). ----
            // No clamp per spec §6.3. Released via OnBoomZoomReleased() in Input.cs on mouse-up.
            // spec: charselect.md §4.3 + §6.3.
            case ActBoomZoomIn: // 72 zoom dir A (this+6088)
                GetCameraRig()?.SetZoomAction(ActBoomZoomIn); // spec: charselect.md §6.3 (+0x1688)
                GD.Print("[CharSelectWindow] action 72 → boom-zoom IN (no clamp). spec charselect.md §6.3.");
                break;
            case ActBoomZoomOut: // 73 zoom dir B (this+6088)
                GetCameraRig()?.SetZoomAction(ActBoomZoomOut); // spec: charselect.md §6.3 (+0x168C)
                GD.Print("[CharSelectWindow] action 73 → boom-zoom OUT (no clamp). spec charselect.md §6.3.");
                break;

            // ---- Move/relocate overlay (G2) — acts 62/63. DESTRUCTIVE PATH GATED. ----
            // 62 = move/relocate CONFIRM (would send 1/14 MoveCharacter). GATED/STUBBED.
            // spec: charselect.md §4.3 moveActions.
            case ActRelocateConfirm: // 62 GATED
                HideModal(_modalCreateClass);
                GD.Print("[CharSelectWindow] action 62 (move/relocate confirm) GATED — " +
                         "1/14 MoveCharacter send stubbed; move-vs-delete label CONTESTED. " +
                         "spec: charselect.md §4.3. Resolve via live dbg before un-gating.");
                break;
            case ActRelocateCancel: // 63
                HideModal(_modalCreateClass);
                GD.Print("[CharSelectWindow] action 63 → relocate CANCEL. spec charselect.md §4.3.");
                break;

            // ---- Select-slot confirm panel (54/55). ----
            // 54 = slot-lock select CONFIRM: emit EnterGameRequested → SelectScene → UseCases (1/7).
            // 55 = select-slot CANCEL: hide panel.
            // spec: charselect.md §4.3 selectConfirmActions "54=slot-lock select (1/7 mode0)".
            case ActSlotSelectConfirm: // 54
                OnSlotSelectConfirm();
                HideModal(_modalCreateName);
                break;
            case ActSlotSelectCancel: // 55
                HideModal(_modalCreateName);
                GD.Print("[CharSelectWindow] action 55 → slot-select CANCEL. spec charselect.md §4.3.");
                break;

            // ---- Plain panel-close (64) — NO message send. '@'. ----
            // spec: charselect.md §4.3 panelCloseAction.
            case ActPanelClose: // 64
                HideModal(_modalDeleteCfm);
                GD.Print("[CharSelectWindow] action 64 → plain panel-close (no send). spec charselect.md §4.3.");
                break;

            // ---- Rename modal E (59/60). ----
            // 59 = rename CONFIRM (1/13, 18-byte body). 60 = rename CANCEL.
            // spec: charselect.md §4.3.
            case ActRenameOk: // 59
                OnRenameConfirm();
                break;
            case ActRenameCancel: // 60
                HideModal(_modalRename);
                _pendingRenameSlot = -1;
                break;

            // ---- Notice modals (74/65). ----
            case ActNoticeOk: // 74 = 'J' deselect / refresh-all; re-arm slot actors
                HideModal(_modalNotice);
                _scene3D?.RefreshSlotActors(_realAssets);
                break;
            case ActNoticePanel: // 65 = 'A' small-notice dismiss
                if (_noticeSmall is not null) _noticeSmall.Visible = false;
                break;

            // ---- Corner CLOSE (sentinel 1000) ----
            case ActCornerClose:
                EmitSignal(SignalName.BackRequested);
                break;

            default:
                GD.Print($"[CharSelectWindow] unbound action {actionId} (no route). spec charselect.md §4.3.");
                break;
        }
    }

    private static void HideModal(Control? modal)
    {
        if (modal is not null && IsInstanceValid(modal))
            modal.Visible = false;
    }
}