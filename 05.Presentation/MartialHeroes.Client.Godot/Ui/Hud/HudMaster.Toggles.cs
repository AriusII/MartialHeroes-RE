// Ui/Hud/HudMaster.Toggles.cs
//
// Partial — panel toggle/show public API.
// Every method here is a pure view intent: it opens or closes a panel; zero game logic.
//
// spec: Docs/RE/specs/ui_system.md §8.10.1 — [I] toggles slots 158+159 together.
// spec: Docs/RE/specs/ui_system.md §8.17 — ASCII-keycode window-open dispatch CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.28.5 — "4002 group-open opens BOTH 185+193".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    /// <summary>
    ///     Forwards key-I toggle to the inventory+skill pair (they toggle together).
    ///     spec: Docs/RE/specs/ui_system.md §8.10.1 — [I] toggles slots 158+159 together.
    /// </summary>
    public void ToggleInventory()
    {
        _inventoryPanel?.Toggle();
        _skillPanel?.Toggle();
    }

    /// <summary>
    ///     Forwards key-K toggle to the skill window only.
    /// </summary>
    public void ToggleSkill()
    {
        _skillPanel?.Toggle();
    }

    /// <summary>
    ///     Forwards key-C toggle to the character stats window.
    /// </summary>
    public void ToggleStats()
    {
        _statsPanel?.Toggle();
    }

    // -------------------------------------------------------------------------
    // Secondary window toggle methods (HUD-II Wave 1 E)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the Options window (OptionPanel).
    ///     Toggle key: UNVERIFIED (keybind-table/debugger-pending). ESC always closes.
    ///     spec: Docs/RE/specs/ui_system.md §8.9.1 — "open trigger UNVERIFIED; ESC CODE-CONFIRMED".
    ///     TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleOptions()
    {
        _optionsWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the Party window (PartyPanel).
    ///     Toggle key: UNVERIFIED (key-table/capture-pending).
    ///     spec: Docs/RE/specs/ui_system.md §8.12 — "toggle hotkey key-table/capture-pending".
    ///     TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleParty()
    {
        _partyWindow?.Toggle();
    }

    /// <summary>
    ///     Shows the Trade window (KeepPanel/TradeKeepWindow).
    ///     Normally opened by S2C 4/23 phase=3 (no hotkey).
    ///     spec: Docs/RE/specs/ui_system.md §8.13 — "no hotkey; opened by trade state machine S2C 4/23".
    /// </summary>
    public void ShowTrade(bool show)
    {
        _tradeWindow?.Toggle(show);
    }

    /// <summary>
    ///     Toggles the Friend window (FriendPanel).
    ///     Toggle key: UNVERIFIED. ESC closes.
    ///     spec: Docs/RE/specs/ui_system.md §8.14 — "open key UNVERIFIED; ESC blurs+closes".
    ///     TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleFriend()
    {
        _friendWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the Guild window (GuildAPanel).
    ///     No dedicated hotkey (CODE-CONFIRMED). Opened by guild NPC/context action.
    ///     spec: Docs/RE/specs/ui_system.md §8.15 — "no dedicated guild hotkey CODE-CONFIRMED".
    ///     TODO(spec): open via guild context action (no hotkey).
    /// </summary>
    public void ToggleGuild()
    {
        _guildWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the Quest window (QuestPanel).
    ///     Key 'q' (ASCII 113) — CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.17.1 — "103 'q' = QuestPanel + close-all, slot 206 CODE-CONFIRMED".
    /// </summary>
    public void ToggleQuest()
    {
        _questWindow?.Toggle();
    }

    // -------------------------------------------------------------------------
    // HUD-II Wave 2 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows or hides the system notice / confirm modal (MessagePanel, slot 190).
    ///     Raised by client-side code paths (hotbar/skill-link confirm etc.) — no hotkey.
    ///     Use <see cref="ShowNotice" /> / <see cref="ShowConfirm" /> for caller API.
    ///     spec: Docs/RE/specs/ui_system.md §8.20.6 — "reached as master slot 190; raised by client-side code".
    /// </summary>
    public void ToggleMessage(bool? forceState = null)
    {
        if (forceState.HasValue && !forceState.Value)
            _messagePanel?.ShowNotice(string.Empty); // close by hiding (internal)
        // Direct callers should use ShowNotice / ShowConfirm; Toggle is for ESC-close.
    }

    /// <summary>
    ///     Shows a system notice modal (mode 0 — single OK).
    ///     spec: Docs/RE/specs/ui_system.md §8.20 — mode 0 CODE-CONFIRMED.
    /// </summary>
    public void ShowNotice(string text)
    {
        _messagePanel?.ShowNotice(text);
    }

    /// <summary>
    ///     Shows a confirm modal (mode 1 — Yes/No).
    ///     spec: Docs/RE/specs/ui_system.md §8.20 — mode 1 CODE-CONFIRMED.
    /// </summary>
    public void ShowConfirm(string text, Action? onYes = null, Action? onNo = null)
    {
        _messagePanel?.ShowConfirm(text, onYes, onNo);
    }

    /// <summary>
    ///     Toggles the production / crafting window (ProductPanel, slot 230).
    ///     Keys: DefaultMenu action 4013 / keybind path (billing-gated).
    ///     spec: Docs/RE/specs/ui_system.md §8.17.3 — "action 4013 → slot 230 CODE-CONFIRMED".
    ///     spec: Docs/RE/specs/ui_system.md §8.18.5 — "opened from DefaultMenu radial action 4013".
    /// </summary>
    public void ToggleProduct()
    {
        _productWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the emote / chat-emoticon picker (EmoticonPanel, +0x370).
    ///     Open trigger (hotkey/toolbar action id) = residual (debugger-pending).
    ///     spec: Docs/RE/specs/ui_system.md §8.19.7 — "toolbar action id SHOWING = residual".
    ///     TODO(spec): toggle hotkey — residual (debugger-pending).
    /// </summary>
    public void ToggleEmoticon()
    {
        _emoticonWindow?.Toggle();
    }

    /// <summary>
    ///     Shows or hides the tender/consignment window (TenderInfoPanel, slot 118).
    ///     Opened from NPC consignment interactions (no hotkey).
    ///     spec: Docs/RE/specs/ui_system.md §8.21.1 — "open from NPC interaction; opcode-pending".
    /// </summary>
    public void ShowTender(bool show)
    {
        _tenderWindow?.Toggle(show);
    }

    /// <summary>
    ///     Shows or hides the mailbox menu (CarrierPigeonPanal, slot 96).
    ///     Opened from NPC service interactions (no hotkey).
    ///     spec: Docs/RE/specs/ui_system.md §8.21.2 — "open from NPC-service; opcode debugger-pending".
    /// </summary>
    public void ShowMail(bool show)
    {
        _mailWindow?.Toggle(show);
    }

    /// <summary>
    ///     Shows or hides the delivery retrieve box (DeliveryPanel, slot 40).
    ///     Opened from NPC service interactions (no hotkey).
    ///     spec: Docs/RE/specs/ui_system.md §8.21.5 — "open from NPC-service; opcode debugger-pending".
    /// </summary>
    public void ShowDelivery(bool show)
    {
        _deliveryWindow?.Toggle(show);
    }

    // -------------------------------------------------------------------------
    // HUD-II Wave 3 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the HelpPanel full-screen overlay (direct MainMaster member, §8.24).
    ///     Called by key 'h' (§8.17.1) and HudCommandBar action 4011 (§8.23.3).
    ///     spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED — "key h = toggle help overlay".
    /// </summary>
    public void ToggleHelp()
    {
        _helpOverlay?.Toggle();
    }

    /// <summary>
    ///     Opens the NPC vendor / item-shop window (slot 259) for the given NPC.
    ///     Called by the world NPC-interaction dispatcher on KIND 32.
    ///     spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED — "KIND 32 opens slot 259".
    /// </summary>
    public void ShowVendor(uint npcId = 0)
    {
        _vendorWindow?.Open(npcId);
    }

    /// <summary>
    ///     Shows a scrolling announce banner (AnnouncePanel, slot 221).
    ///     spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
    /// </summary>
    public void ShowAnnounce(string text)
    {
        _announcePanel?.ShowAnnounce(text);
    }

    /// <summary>
    ///     Shows a timed error/notice modal (ErrorPanel, slot 168).
    ///     Also delegates the banner to AnnouncePanel (§8.25.1).
    ///     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
    ///     TODO(world-campaign): wire 4/500 SmsgShowPopupByCode sink.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.3 — SmsgShowPopupByCode 4/500.
    /// </summary>
    public void ShowError(string text, double seconds = 5.0)
    {
        _errorPanel?.ShowError(text, seconds);
    }

    /// <summary>
    ///     Shows the PetPanel (player-couple companion window, slot 194) with partner data.
    ///     Called by S2C SmsgActorPairRelation (5/53).
    ///     spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.
    ///     TODO(world-campaign): wire from 5/53 packet handler.
    /// </summary>
    public void ShowPetPanel(string partnerName, int partnerLevel, float gauge0 = 0f, float gauge1 = 0f)
    {
        _petPanel?.ShowPartner(partnerName, partnerLevel, gauge0, gauge1);
    }

    /// <summary>
    ///     Hides the PetPanel (relation clear or partner death).
    ///     spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.
    /// </summary>
    public void ClearPetPanel()
    {
        _petPanel?.ClearPartner();
    }

    // -------------------------------------------------------------------------
    // HUD-II Wave 4 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Opens the KeepNpcPanel dialog (slot 152) for the given KIND-9 NPC.
    ///     Called by the world NPC-click dispatcher when NPC kind is 9.
    ///     spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED — "KIND 9 → open KeepNpcPanel".
    /// </summary>
    public void ShowKeepNpcDialog(uint npcId = 0)
    {
        _keepNpcDialog?.Open(npcId);
    }

    /// <summary>
    ///     Opens the player storage/warehouse window (KeepPanel, slot 191).
    ///     Normally called from KeepNpcPanel sel 1 via HudKeepNpcDialog routing.
    ///     spec: Docs/RE/specs/ui_system.md §8.32.5 CODE-CONFIRMED — "opened via KeepNpcPanel sel 1 only".
    /// </summary>
    public void ShowStorage()
    {
        _storageWindow?.Open();
    }

    /// <summary>
    ///     Toggles the personal-stall market list (StallListPanel, slot 228).
    ///     Key `l` (ASCII 108) — CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED — "key l toggle".
    /// </summary>
    public void ToggleStallList()
    {
        _stallListWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the guild-diplomacy roster (BroodWarListPanel, slot 235).
    ///     Key `u` (ASCII 117) — CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED — "key u toggle".
    /// </summary>
    public void ToggleGuildDiplomacy()
    {
        _guildDiplomacyWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the guild-war info display window (GuildWarInfoPanel, slot 224).
    ///     Key `j` (ASCII 106) — CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED — "key j toggle".
    /// </summary>
    public void ToggleGuildWarInfo()
    {
        _guildWarInfoWindow?.Toggle();
    }

    /// <summary>
    ///     Toggles the RelationPanel (slot 193).
    ///     Called by DefaultMenu action 4002 (which also opens BuddyRelation slot 185).
    ///     BuddyRelation (slot 185) interim: routes to ToggleFriend() until full BuddyRelation is built.
    ///     spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED — "4002 opens both 185+193".
    ///     spec: Docs/RE/specs/ui_system.md §8.28 — "BuddyRelation(185) = separate deliverable TODO(world-campaign)".
    /// </summary>
    public void ToggleRelation()
    {
        // Opens BOTH slot 185 (BuddyRelation) AND slot 193 (RelationPanel) together per §8.28.5.
        // Slot 185 BuddyRelation: interim → FriendWindow (closest social window) pending full build.
        // spec: ui_system.md §8.28.5 CODE-CONFIRMED — "group-open opens BOTH slot 185 + slot 193"
        _friendWindow?.Toggle(); // interim for BuddyRelation(185) — TODO(world-campaign) build HudBuddyRelation
        _relationPanel?.Toggle();
        GD.Print("[HudMaster] ToggleRelation → RelationPanel(193) toggled; " +
                 "BuddyRelation(185) interim→FriendWindow (TODO world-campaign: HudBuddyRelation). " +
                 "spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED.");
    }
}