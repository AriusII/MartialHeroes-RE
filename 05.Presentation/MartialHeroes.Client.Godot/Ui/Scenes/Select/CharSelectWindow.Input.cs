// Ui/Scenes/Select/CharSelectWindow.Input.cs
//
// Character-select window — button callbacks, input handlers, name validation.
// Part of the CharSelectWindow partial class split (Wave 4b).
//
// Covers: OnTabButtonFired(), OnRosterButtonFired(), OnEnterPressed(), OnDeletePressed(),
//         OnCreateClassAction(), OnFaceAction(), OnCreateConfirmAction(), OnModalConfirm(),
//         OnViewport3DGuiInput(), ValidateName(), ShowToast().
//
// PASSIVE: zero game logic, zero domain state. Turns gestures into signals (intents).
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4.
// spec: Docs/RE/specs/frontend_scenes.md §3/§4/§11.

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
                GD.Print($"[CharSelectWindow] 3D ray-pick → slot {hit}. spec: frontend_scenes.md §3.3.3.");
            }
        }
    }

    // =========================================================================
    // Action handlers
    // =========================================================================

    private void OnTabButtonFired(int actionId)
    {
        switch (actionId)
        {
            case ActionBack:
                // D11: Back tab (action 3) is a create-panel back/cancel, NOT a scene-level back.
                // Server-list/login transition via Back tab is REFUTED by static IDA 2026-06-20.
                // If the create form is open, close it; otherwise this is a no-op.
                // spec: Docs/RE/specs/frontend_scenes.md §11.5 / CYCLE-6b fact 5 (Back tab =
                //       create-cancel, NOT scene back; server-list transition REFUTED).
                GD.Print("[CharSelectWindow] Back tab (action 3) — create-cancel / no-op. " +
                         "BackRequested NOT emitted. spec: frontend_scenes.md §11.5 / CYCLE-6b fact 5.");
                if (_createFormVisible)
                    HideCreateForm();
                break;
            default:
                GD.Print($"[CharSelectWindow] Tab action {actionId} — stub (offline/server-select pending).");
                break;
        }
    }

    private void OnRosterButtonFired(int actionId)
    {
        switch (actionId)
        {
            case ActionCreate:
                GD.Print("[CharSelectWindow] Create (action 4).");
                ShowCreateForm();
                break;
            case ActionDelete:
                OnDeletePressed();
                break;
            case ActionEnter:
                OnEnterPressed();
                break;
        }
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

        GD.Print($"[CharSelectWindow] DeleteCharacterRequested: name='{ls.Name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.DeleteCharacterRequested, _selectedSlot, ls.Name);
    }

    private void OnCreateClassAction(int actionId)
    {
        var uiIndex = actionId - ActionClass0; // 10-13 → 0-3.
        if (uiIndex >= 0 && uiIndex < 4) SetCreateClass(uiIndex);
    }

    private void OnFaceAction(int actionId)
    {
        // ActionId 21 = increment, 22 = decrement. spec §8.2. CODE-CONFIRMED.
        var delta = actionId == ActionFaceIncr ? +1 : -1;
        _createFaceIndex = Math.Clamp(_createFaceIndex + delta, FaceMin, FaceMax);
        GD.Print($"[CharSelectWindow] face={_createFaceIndex} " +
                 $"(range {FaceMin}..{FaceMax}). spec: frontend_scenes.md §4.2. CODE-CONFIRMED.");
        if (_createFaceLabel is not null && IsInstanceValid(_createFaceLabel))
            _createFaceLabel.Text = _createFaceIndex.ToString();
    }

    private void OnCreateConfirmAction(int _)
    {
        // Confirm (35) on the create form itself opens the name modal.
        // spec: frontend_scenes.md §4/§4.1.1. CODE-CONFIRMED.
        ShowNameModal();
    }

    private void OnModalConfirm(int _)
    {
        var name = _nameEntry?.Text.Trim() ?? string.Empty;
        if (!ValidateName(name, out var toastMsg))
        {
            ShowToast(toastMsg);
            GD.Print($"[CharSelectWindow] Create name rejected: '{name}' → toast.");
            return;
        }

        var internalClass = UiToInternal[_createUiClass];
        GD.Print($"[CharSelectWindow] CreateCharacterRequested: name='{name}' " +
                 $"class={internalClass} face={_createFaceIndex}. spec: frontend_scenes.md §4/§8.");
        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex);
        HideCreateForm();
        RefreshCharCountCaption();
    }

    // =========================================================================
    // Name validation. spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
    // =========================================================================

    private bool ValidateName(string name, out string toastMsg)
    {
        // Empty → msg 2190. spec §4.4/§8.2. CODE-CONFIRMED.
        if (name.Length < 2)
        {
            toastMsg = Text?.GetCaption(2190u, string.Empty) ?? string.Empty;
            return false;
        }

        // Charset: a-z + 0-9 + CP949 Hangul. spec §4.4. CODE-CONFIRMED.
        foreach (var c in name)
        {
            if (c >= 'a' && c <= 'z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c >= '가' && c <= '힣') continue;
            if (c >= 'ᄀ' && c <= 'ᇿ') continue;
            if (c >= '㄰' && c <= '㆏') continue;
            // Charset violation → msg 12012. spec §8.2. CODE-CONFIRMED.
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
}