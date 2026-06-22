// Ui/Hud/HudMaster.Input.cs
//
// Partial — §8.17 ASCII-keycode window-open dispatch (_Input) + ESC close-priority handler.
//
// The legacy client stores each action id as an ASCII char and reads it from
// INI section [<account>_KEYSET] for per-account remapping. Default keys below
// are the factory ASCII keycodes per §8.17.1. The global input-blocked / cinematic
// flag (no window opens during cinematics) is enforced by the scene state machine
// (world state = 5 only); we inherit that gate by being built only in state 5.
//
// Action ids ARE ASCII keycodes — this is why toolbar buttons and keyboard keys
// open the same window: both raise the same integer.
//
// spec: Docs/RE/specs/ui_system.md §8.17 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.17.1 — ASCII-keycode action→window→service slot table.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key) return;
        if (!key.Pressed || key.Echo) return;

        // HUD UI toggle keys. World/camera input handled by godot-input-engineer's InputRouter.
        // Factory default keys from §8.17.1. INI remapping: [<account>_KEYSET] %s_KEYSET.
        // spec: Docs/RE/specs/ui_system.md §8.17 CODE-CONFIRMED.
        switch (key.Keycode)
        {
            case Key.I:
                // 'i' — inventory/character group.
                // spec: Docs/RE/scenes/ingame.md §13.1 — "i = inventory/character group" (ingame.md wins over ui_system.md).
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "105 'i' = inventory group-toggle CODE-CONFIRMED" (corroborating).
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.B:
                // 'b' — war list (BroodWarListPanel, slot 235).
                // spec: Docs/RE/scenes/ingame.md §13.1 — "b = war list" (ingame.md §13.1 WINS over ui_system.md §8.17.1).
                // NOTE: ui_system.md §8.17.1 "98 'b' = ItemPanel slot 158" is SUPERSEDED by ingame.md §13.1.
                // 'b' must NOT toggle inventory or skill — it opens the war list.
                ToggleGuildDiplomacy(); // war list = BroodWarListPanel (slot 235, key 'b' per §13.1)
                GD.Print("[HudMaster] 'b' → war list (BroodWarListPanel §13.1). " +
                         "spec: Docs/RE/scenes/ingame.md §13.1 — 'b = war list' (wins over ui_system.md §8.17.1).");
                GetViewport().SetInputAsHandled();
                break;

            case Key.S:
                // 's' — inventory group.
                // spec: Docs/RE/scenes/ingame.md §13.1 — "s = inventory group" (ingame.md wins over ui_system.md).
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.C:
                // 'c' — Character stats (StatusPanel) toggle, slot 146.
                // No direct §8.17 entry for 'c'; recovered via category-102 in discript.sc.
                // spec: Docs/RE/formats/misc_data.md §5 — discript.sc category 102 "(C)".
                ToggleStats();
                GetViewport().SetInputAsHandled();
                break;

            case Key.K:
                // 'k' — close-many-panels (and skill-window family).
                // spec: Docs/RE/scenes/ingame.md §13.1 — "k = close-many-panels (and skill-window family)" (wins).
                // NOTE: ui_system.md §8.17.1 "107 'k' = PartyPanel slot 220" is SUPERSEDED by ingame.md §13.1.
                // Per §13.1 'k' toggles the skill window family (SkillPanel); close-many handled by ESC chain.
                ToggleSkill();
                GD.Print("[HudMaster] 'k' → skill-window family (§13.1). " +
                         "spec: Docs/RE/scenes/ingame.md §13.1 — 'k = close-many-panels (and skill-window family)' (wins over ui_system.md §8.17.1).");
                GetViewport().SetInputAsHandled();
                break;

            case Key.Q:
                // 'q' (ASCII 113) — Quest open (QuestPanel) + close-all + UI transition, slot 206.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "113 'q' = QuestPanel slot 206 CODE-CONFIRMED".
                // Backfills Wave-1 hotkey TODO on quest.
                ToggleQuest();
                GetViewport().SetInputAsHandled();
                break;

            case Key.G:
                // 'g' (ASCII 103) — DefaultMenu (radial quick-menu), slot 191.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "103 'g' = DefaultMenu slot 191 CODE-CONFIRMED".
                // DefaultMenu not yet built as a full panel — deferred (TODO world-campaign).
                // Fall through (no panel to open yet).
                GD.Print("[HudMaster] 'g' (DefaultMenu §8.17.1 slot 191): panel not yet built. " +
                         "TODO(world-campaign): DefaultMenu radial. " +
                         "spec: Docs/RE/specs/ui_system.md §8.17.1 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.H:
                // 'h' (ASCII 104) — Help window (HelpPanel, literal data/ui/help.dds), direct member.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "104 'h' = HelpPanel slot 322 CODE-CONFIRMED".
                // spec: Docs/RE/specs/ui_system.md §8.24.3 — "Toggle = key h; Space REFUTED".
                ToggleHelp();
                GD.Print("[HudMaster] 'h' → HelpPanel toggle (§8.17.1/§8.24.3 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.L:
                // 'l' (ASCII 108) — StallListPanel toggle, slot 228.
                // spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED — "key l toggle slot 228".
                ToggleStallList();
                GD.Print("[HudMaster] 'l' → StallListPanel toggle (§8.29.5 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.U:
                // 'u' (ASCII 117) — BroodWarListPanel toggle, slot 235.
                // spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED — "key u toggle slot 235".
                ToggleGuildDiplomacy();
                GD.Print("[HudMaster] 'u' → BroodWarListPanel toggle (§8.30.5 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.J:
                // 'j' (ASCII 106) — GuildWarInfoPanel toggle, slot 224.
                // spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED — "key j toggle slot 224".
                ToggleGuildWarInfo();
                GD.Print("[HudMaster] 'j' → GuildWarInfoPanel toggle (§8.31.6 CODE-CONFIRMED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;

            case Key.Space:
                // Space (ASCII 32) — Help close + toggle help button state, slot 322 / 176.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "32 Space = help close / toggle CODE-CONFIRMED".
                // NOTE: Space → help is REFUTED in static code (§8.24.3). We log but do NOT dispatch.
                // spec: Docs/RE/specs/ui_system.md §8.24.3 — "Space trigger REFUTED in static code".
                GD.Print("[HudMaster] Space (§8.17.1): Space→help REFUTED in static (§8.24.3); not dispatching. " +
                         "spec: Docs/RE/specs/ui_system.md §8.24.3 — Space REFUTED.");
                // Do NOT SetInputAsHandled; let Space fall through to world input.
                break;

            case Key.Escape:
                // ESC (ASCII 27) — close inventory group / collapse dock-frame group, slot 146 + group.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "27 Esc = close inventory group CODE-CONFIRMED".
                // In our port: close any open toggle window (top-to-bottom priority).
                OnEscapeKey();
                // Do NOT SetInputAsHandled globally — sub-panel _Input ESC handlers may need it first.
                break;
        }
    }

    /// <summary>
    ///     ESC key handler — closes the topmost visible toggle window.
    ///     Mirrors the legacy "close-top / collapse" behaviour from §8.17.1.
    ///     spec: Docs/RE/specs/ui_system.md §8.17.1 — "27 Esc = close inventory group / collapse dock CODE-CONFIRMED".
    /// </summary>
    private void OnEscapeKey()
    {
        // Close priority: bottom strip panels first, then the core toggle windows.
        // The legacy dispatcher closes the inventory group (slots 146+158+159) on Esc.
        // spec: ui_system.md §8.17.1 CODE-CONFIRMED — "close inventory group / collapse dock".

        // Wave-4 E: KeepNpcDialog (NPC menu — close before storage)
        if (_keepNpcDialog?.Visible == true)
            /* let its own _Input handle ESC */
            return;

        // Wave-4 E: StorageWindow
        if (_storageWindow?.Visible == true)
        {
            _storageWindow.Visible = false;
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-4 E: StallListWindow
        if (_stallListWindow?.Visible == true)
        {
            _stallListWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-4 E: GuildDiplomacyWindow
        if (_guildDiplomacyWindow?.Visible == true)
        {
            _guildDiplomacyWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-4 E: GuildWarInfoWindow
        if (_guildWarInfoWindow?.Visible == true)
        {
            _guildWarInfoWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-4 E: RelationPanel
        if (_relationPanel?.Visible == true)
        {
            _relationPanel.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-3 E: HelpOverlay (highest priority — full-screen, catches any key)
        if (_helpOverlay?.Visible == true)
        {
            _helpOverlay.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-3 E: ErrorPanel (timed modal; lets its own ESC handler fire first)
        if (_errorPanel?.Visible == true)
            /* let its own _Input handle ESC */
            return;

        // Wave-3 E: Vendor window
        if (_vendorWindow?.Visible == true)
        {
            _vendorWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-2 E modals (highest priority — they grab focus)
        if (_messagePanel?.Visible == true)
            /* let its own _Input handle ESC */
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

        // Wave-1 E windows
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

        // Core toggle panels (inventory group = slots 158+159+146)
        // spec: ui_system.md §8.17.1 — "Esc → close inventory group / collapse dock"
        if (_inventoryPanel?.Visible == true || _skillPanel?.Visible == true || _statsPanel?.Visible == true)
        {
            // These panels have Toggle() (no bool param); only call if currently open.
            if (_inventoryPanel?.Visible == true) _inventoryPanel.Toggle();
            if (_skillPanel?.Visible == true) _skillPanel.Toggle();
            if (_statsPanel?.Visible == true) _statsPanel.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
}