using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private const int ActCmdCreateNew = 1;
    private const int ActCmdEnterGame = 2;
    private const int ActCmdCreateAlt = 3;
    private const int ActBottom4 = 4;
    private const int ActBottom5 = 5;

    private const int ActBottom6 = 6;

    private const int ActConditionalOverlay = 61;

    private const int ActClass0 = 10;

    private const int ActFacePlus = 21;
    private const int ActFaceMinus = 22;

    private const int ActSpinner25 = 25;
    private const int ActSpinner26 = 26;
    private const int ActColA0 = 27;

    private const int ActColB0 = 30;

    private const int ActCreateConfirm = 35;
    private const int ActCreateCancel = 36;

    private const int ActSlotSelectConfirm = 54;
    private const int ActSlotSelectCancel = 55;

    private const int ActRenameOk = 59;
    private const int ActRenameCancel = 60;

    private const int ActRelocateConfirm = 62;
    private const int ActRelocateCancel = 63;

    private const int ActPanelClose = 64;

    private const int ActNoticePanel = 65;

    private const int ActAppSpin66 = 66;
    private const int ActAppSpin67 = 67;
    private const int ActAppSpin68 = 68;
    private const int ActAppSpin69 = 69;

    private const int ActActorYawLeft = 70;
    private const int ActActorYawRight = 71;

    private const int ActBoomZoomIn = 72;
    private const int ActBoomZoomOut = 73;

    private const int ActNoticeOk = 74;

    private const int ActCornerClose = 1000;

    private const int ActCreateClassOk = ActRelocateConfirm;
    private const int ActCreateClassCancel = ActRelocateCancel;
    private const int ActDeleteConfirm = ActPanelClose;
    private const int ActNav70 = ActActorYawLeft;
    private const int ActNav71 = ActActorYawRight;
    private const int ActToggle72 = ActBoomZoomIn;

    private const int ActToggle73 = ActBoomZoomOut;

    private CharSelectCameraRig? _cameraRig;

    private CharSelectCameraRig? GetCameraRig()
    {
        if (_cameraRig is not null && IsInstanceValid(_cameraRig))
            return _cameraRig;
        _cameraRig = _scene3DViewport?.FindChild("CharSelectCameraRig", true, false) as CharSelectCameraRig;
        return _cameraRig;
    }


    private void OnAction(int actionId)
    {
        Audio?.PlayUiClick();

        switch (actionId)
        {
            case ActCmdCreateNew:
            case ActCmdCreateAlt:
                ShowCreateForm();
                break;

            case ActCmdEnterGame:
                OnEnterPressed();
                break;

            case ActBottom4:
                ShowCreateForm();
                break;

            case ActBottom5:
                GD.Print("[CharSelectWindow] action 5 (select-and-play / no-op). spec charselect.md §4.3.");
                break;

            case ActBottom6:
                OnSlotSelectConfirm();
                break;

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

            case >= ActClass0 and <= ActClass0 + 3:
                OnCreateClassAction(actionId);
                break;

            case ActFacePlus:
            case ActFaceMinus:
                OnFaceAction(actionId);
                break;

            case ActSpinner25:
                OnAppearanceStep(actionId, 0, +1, "STAT0");
                break;
            case ActSpinner26:
                OnAppearanceStep(actionId, 1, +1, "STAT1");
                break;
            case ActColA0:
                OnAppearanceStep(actionId, 2, +1, "STAT2");
                break;
            case ActColA0 + 1:
                OnAppearanceStep(actionId, 4, +1, "STAT4");
                break;
            case ActColA0 + 2:
                OnAppearanceStep(actionId, 3, +1, "STAT3");
                break;
            case ActColB0:
                OnAppearanceStep(actionId, 0, -1, "STAT0");
                break;
            case ActColB0 + 1:
                OnAppearanceStep(actionId, 1, -1, "STAT1");
                break;
            case ActColB0 + 2:
                OnAppearanceStep(actionId, 2, -1, "STAT2");
                break;
            case ActColB0 + 3:
                OnAppearanceStep(actionId, 4, -1, "STAT4");
                break;
            case ActColB0 + 4:
                OnAppearanceStep(actionId, 3, -1, "STAT3");
                break;

            case ActCreateConfirm:
                OnCreateNameConfirm();
                break;

            case ActCreateCancel:
                HideModal(_modalCreateName);
                HideCreateForm();
                GD.Print("[CharSelectWindow] action 36 → CREATE-CANCEL: hide modal + reset to select. " +
                         "spec charselect.md §4.3.");
                break;

            case ActAppSpin66:
            case ActAppSpin67:
                _rotatePressLeft = true;
                GD.Print($"[CharSelectWindow] action {actionId} → actor-yaw button + (this+6068). " +
                         "spec charselect.md §4.3/§6.3.");
                break;
            case ActAppSpin68:
            case ActAppSpin69:
                _rotatePressRight = true;
                GD.Print($"[CharSelectWindow] action {actionId} → actor-yaw button − (this+6072). " +
                         "spec charselect.md §4.3/§6.3.");
                break;

            case ActActorYawLeft:
                _rotatePressLeft = true;
                GD.Print("[CharSelectWindow] action 70 → actor-yaw drag-hold dir A. spec charselect.md §6.3.");
                break;
            case ActActorYawRight:
                _rotatePressRight = true;
                GD.Print("[CharSelectWindow] action 71 → actor-yaw drag-hold dir B. spec charselect.md §6.3.");
                break;

            case ActBoomZoomIn:
                GetCameraRig()?.SetZoomAction(ActBoomZoomIn);
                GD.Print("[CharSelectWindow] action 72 → boom-zoom IN (no clamp). spec charselect.md §6.3.");
                break;
            case ActBoomZoomOut:
                GetCameraRig()?.SetZoomAction(ActBoomZoomOut);
                GD.Print("[CharSelectWindow] action 73 → boom-zoom OUT (no clamp). spec charselect.md §6.3.");
                break;

            case ActRelocateConfirm:
                HideModal(_modalCreateClass);
                GD.Print("[CharSelectWindow] action 62 (move/relocate confirm) GATED — " +
                         "1/14 MoveCharacter send stubbed; move-vs-delete label CONTESTED. " +
                         "spec: charselect.md §4.3. Resolve via live dbg before un-gating.");
                break;
            case ActRelocateCancel:
                HideModal(_modalCreateClass);
                GD.Print("[CharSelectWindow] action 63 → relocate CANCEL. spec charselect.md §4.3.");
                break;

            case ActSlotSelectConfirm:
                OnSlotSelectConfirm();
                HideModal(_modalCreateName);
                break;
            case ActSlotSelectCancel:
                HideModal(_modalCreateName);
                GD.Print("[CharSelectWindow] action 55 → slot-select CANCEL. spec charselect.md §4.3.");
                break;

            case ActPanelClose:
                HideModal(_modalDeleteCfm);
                GD.Print("[CharSelectWindow] action 64 → plain panel-close (no send). spec charselect.md §4.3.");
                break;

            case ActRenameOk:
                OnRenameConfirm();
                break;
            case ActRenameCancel:
                HideModal(_modalRename);
                _pendingRenameSlot = -1;
                break;

            case ActNoticeOk:
                HideModal(_modalNotice);
                _scene3D?.RefreshSlotActors(_realAssets);
                break;
            case ActNoticePanel:
                if (_noticeSmall is not null) _noticeSmall.Visible = false;
                break;

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