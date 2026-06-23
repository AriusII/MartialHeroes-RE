
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{

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


    internal void OnActorYawLeftReleased()
    {
        _rotatePressLeft = false;
    }

    internal void OnActorYawRightReleased()
    {
        _rotatePressRight = false;
    }

    internal void OnBoomZoomReleased()
    {
        GetCameraRig()?.SetZoomAction(0);
    }


    private void OnEnterPressed()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Enter on empty slot {_selectedSlot} → Create form.");
            ShowCreateForm();
            return;
        }

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

        _pendingDeleteSlot = _selectedSlot;
        if (_deleteTitleWidget?.GetControl() is Label t)
            t.Text = Text?.GetCaption(MsgDeleteConfirmTitle, string.Empty) ?? string.Empty;
        if (_deleteBodyWidget?.GetControl() is Label b)
            b.Text = Text?.GetCaption(MsgDeleteConfirmBody, string.Empty) ?? string.Empty;
        if (_modalDeleteCfm is not null) _modalDeleteCfm.Visible = true;
        GD.Print($"[CharSelectWindow] delete-confirm modal raised for slot {_selectedSlot}. spec §11.5h G4.");
    }

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


    private void OnCreateClassAction(int actionId)
    {
        var uiIndex = actionId - ActClass0;
        if (uiIndex >= 0 && uiIndex < 4) SetCreateClass(uiIndex);
    }

    private void OnFaceAction(int actionId)
    {
        var delta = actionId == ActFacePlus ? +1 : -1;
        var before = _createFaceIndex;
        _createFaceIndex = Math.Clamp(_createFaceIndex + delta, FaceMin, FaceMax);

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createFaceIndex = _createPreview3D.UpdateFaceIndex(_createFaceIndex);

        if (_createFaceLabel is not null && IsInstanceValid(_createFaceLabel))
            _createFaceLabel.Text = _createFaceIndex.ToString();
        ShowCreateToast(_createFaceIndex == before
            ? $"FACE {_createFaceIndex} ({FaceMin}..{FaceMax})"
            : $"FACE {_createFaceIndex}");

        GD.Print($"[CharSelectWindow] face={_createFaceIndex} (range {FaceMin}..{FaceMax}); " +
                 "2D-only (no 3D rebuild). spec §4.2.");
    }

    private void OnAppearanceStep(int actionId, int seedIndex, int delta, string label)
    {
        var stored = 0;
        if (seedIndex >= 0 && seedIndex < StatGridRows)
        {
            var next = Math.Max(StatFloor, _statValues[seedIndex] + delta);
            _statValues[seedIndex] = next;
            stored = next;
        }

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createPreview3D.UpdateAppearanceSeed(seedIndex, delta);

        ShowCreateToast($"{label} {stored}");
        GD.Print($"[CharSelectWindow] appearance step action {actionId}: seed[{seedIndex}] " +
                 $"{(delta >= 0 ? "+" : "")}{delta} → _statValues[{seedIndex}]={stored} (2D-only; no 3D rebuild). " +
                 "spec charselect.md §4.3; character_creation.md §2.1.");
    }


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

    private void ShowCreateToast(string message)
    {
        if (_createToast is null || !IsInstanceValid(_createToast)) return;
        _createToast.Text = message;
        _createToast.Visible = true;
        _createToastTimer = 2.0;
    }
}