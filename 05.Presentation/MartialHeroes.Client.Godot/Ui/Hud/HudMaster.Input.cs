
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key) return;
        if (!key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.I:
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.B:
                ToggleGuildDiplomacy();
                GD.Print("[HudMaster] 'b' → war list (BroodWarListPanel §13.1). " +
                         "spec: Docs/RE/scenes/ingame.md §13.1 — 'b = war list' (wins over ui_system.md §8.17.1).");
                GetViewport().SetInputAsHandled();
                break;

            case Key.S:
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.C:
                ToggleStats();
                GetViewport().SetInputAsHandled();
                break;

            case Key.K:
                ToggleSkill();
                GD.Print("[HudMaster] 'k' → skill-window family (§13.1). " +
                         "spec: Docs/RE/scenes/ingame.md §13.1 — 'k = close-many-panels (and skill-window family)' (wins over ui_system.md §8.17.1).");
                GetViewport().SetInputAsHandled();
                break;

            case Key.Q:
                ToggleQuest();
                GetViewport().SetInputAsHandled();
                break;

            case Key.G:
                GD.Print("[HudMaster] 'g' (DefaultMenu §8.17.1 slot 191): panel not yet built. " +
                         "TODO(world-campaign): DefaultMenu radial. " +
                         "spec: Docs/RE/specs/ui_system.md §8.17.1 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.H:
                ToggleHelp();
                GD.Print("[HudMaster] 'h' → HelpPanel toggle (§8.17.1/§8.24.3 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.L:
                ToggleStallList();
                GD.Print("[HudMaster] 'l' → StallListPanel toggle (§8.29.5 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.U:
                ToggleGuildDiplomacy();
                GD.Print("[HudMaster] 'u' → BroodWarListPanel toggle (§8.30.5 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.J:
                ToggleGuildWarInfo();
                GD.Print("[HudMaster] 'j' → GuildWarInfoPanel toggle (§8.31.6 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.Space:
                GD.Print("[HudMaster] Space (§8.17.1): Space→help REFUTED in static (§8.24.3); not dispatching. " +
                         "spec: Docs/RE/specs/ui_system.md §8.24.3 — Space REFUTED.");
                break;

            case Key.Escape:
                OnEscapeKey();
                break;
        }
    }

    private void OnEscapeKey()
    {

        if (_keepNpcDialog?.Visible == true)
            return;

        if (_storageWindow?.Visible == true)
        {
            _storageWindow.Visible = false;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_stallListWindow?.Visible == true)
        {
            _stallListWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_guildDiplomacyWindow?.Visible == true)
        {
            _guildDiplomacyWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_guildWarInfoWindow?.Visible == true)
        {
            _guildWarInfoWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_relationPanel?.Visible == true)
        {
            _relationPanel.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_helpOverlay?.Visible == true)
        {
            _helpOverlay.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_errorPanel?.Visible == true)
            return;

        if (_vendorWindow?.Visible == true)
        {
            _vendorWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_messagePanel?.Visible == true)
            return;

        if (_productWindow?.Visible == true)
        {
            _productWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_emoticonWindow?.Visible == true)
        {
            _emoticonWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_tenderWindow?.Visible == true)
        {
            _tenderWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_mailWindow?.Visible == true)
        {
            _mailWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_deliveryWindow?.Visible == true)
        {
            _deliveryWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_optionsWindow?.Visible == true)
        {
            _optionsWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_questWindow?.Visible == true)
        {
            _questWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_partyWindow?.Visible == true)
        {
            _partyWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_friendWindow?.Visible == true)
        {
            _friendWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_guildWindow?.Visible == true)
        {
            _guildWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_inventoryPanel?.Visible == true || _skillPanel?.Visible == true || _statsPanel?.Visible == true)
        {
            if (_inventoryPanel?.Visible == true) _inventoryPanel.Toggle();
            if (_skillPanel?.Visible == true) _skillPanel.Toggle();
            if (_statsPanel?.Visible == true) _statsPanel.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
}