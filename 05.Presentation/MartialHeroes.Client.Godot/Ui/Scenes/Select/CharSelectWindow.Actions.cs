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
    private const int ActDeleteCancel = 76;

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
                OnDeletePressed();
                break;

            case ActBottom6:
                OnSlotSelectConfirm();
                break;

            case ActConditionalOverlay:
                if (_selectedSlot >= 0 && _selectedSlot < MaxSlots && !_slots[_selectedSlot].IsEmpty)
                {
                    _pendingRelocateToSlot = FindNextEmptySlot(_selectedSlot);
                    if (_modalCreateClass is not null) _modalCreateClass.Visible = true;
                    GD.Print($"[CharSelectWindow] action 61: relocate overlay opened (from={_selectedSlot} to={_pendingRelocateToSlot}). spec: charselect.md §4.3.");
                }
                else
                {
                    if (_noticeSmall is not null) _noticeSmall.Visible = true;
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
                break;

            case ActAppSpin66:
            case ActAppSpin67:
                _rotatePressLeft = true;
                break;
            case ActAppSpin68:
            case ActAppSpin69:
                _rotatePressRight = true;
                break;

            case ActActorYawLeft:
                _rotatePressLeft = true;
                break;
            case ActActorYawRight:
                _rotatePressRight = true;
                break;

            case ActBoomZoomIn:
                GetCameraRig()?.SetZoomAction(ActBoomZoomIn);
                break;
            case ActBoomZoomOut:
                GetCameraRig()?.SetZoomAction(ActBoomZoomOut);
                break;

            case ActRelocateConfirm:
                HideModal(_modalCreateClass);
                if (_pendingRelocateToSlot >= 0)
                {
                    if (_selectedSlot >= 0 && _selectedSlot < MaxSlots && !_slots[_selectedSlot].IsEmpty &&
                        _pendingRelocateToSlot < MaxSlots && _pendingRelocateToSlot != _selectedSlot)
                    {
                        GD.Print($"[CharSelectWindow] MoveCharacterSlotRequested: from={_selectedSlot} to={_pendingRelocateToSlot}. spec: charselect.md §4.3 action 62 / opcode 1/14.");
                        EmitSignal(SignalName.MoveCharacterSlotRequested, _selectedSlot, _pendingRelocateToSlot);
                    }
                    else
                    {
                        GD.Print($"[CharSelectWindow] action 62 relocate: no valid target slot (from={_selectedSlot} to={_pendingRelocateToSlot}); no send.");
                    }
                    _pendingRelocateToSlot = -1;
                }
                else
                {
                    ShowCreateNameModal();
                }
                break;
            case ActRelocateCancel:
                HideModal(_modalCreateClass);
                _pendingRelocateToSlot = -1;
                break;

            case ActSlotSelectConfirm:
                OnSlotSelectConfirm();
                HideModal(_modalCreateName);
                break;
            case ActSlotSelectCancel:
                HideModal(_modalCreateName);
                break;

            case ActPanelClose:
                if (_pendingDeleteSlot >= 0)
                    OnDeleteConfirmYes(0);
                else
                    HideModal(_modalDeleteCfm);
                break;

            case ActDeleteCancel:
                HideModal(_modalDeleteCfm);
                _pendingDeleteSlot = -1;
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
                if (_carrierPigeonPanel is not null && IsInstanceValid(_carrierPigeonPanel))
                    _carrierPigeonPanel.Visible = false;
                break;

            case ActCornerClose:
                EmitSignal(SignalName.BackRequested);
                break;

            default:
                GD.Print($"[CharSelectWindow] unbound action {actionId}.");
                break;
        }
    }

    private int FindNextEmptySlot(int excludeSlot)
    {
        for (var i = 0; i < MaxSlots; i++)
        {
            if (i != excludeSlot && _slots[i].IsEmpty)
                return i;
        }
        return (excludeSlot + 1) % MaxSlots;
    }

    private static void HideModal(Control? modal)
    {
        if (modal is not null && IsInstanceValid(modal))
            modal.Visible = false;
    }
}