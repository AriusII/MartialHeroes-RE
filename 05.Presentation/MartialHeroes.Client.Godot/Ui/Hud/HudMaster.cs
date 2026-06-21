// Ui/Hud/HudMaster.cs
//
// The in-game (state 5) HUD master — orchestrator of all core HUD panels.
//
// Faithful counterpart of the recovered MainMaster service-slot model:
//   - Two-pass build: geometry pass (sizes/positions from the HUD-build routine),
//     then per-state reconfigure (text/sound/flags from the reconfigure routine).
//   - 10 core panels + 6 secondary windows (Wave 1 E) + 6 Wave-2 windows + 6 Wave-3 windows
//     = ~28 panels total (27 active + HelpOverlay which is a direct member, not a slot entry).
//   - All chrome from the corrected uitex atlases via HudAtlasLibrary.
//   - Icons via HudIconLibrary; text via HudTextLibrary.
//   - Graceful-null offline: GD.PrintErr + skip, no fake chrome.
//
// HUD-II (CAMPAIGN 17): MopGagePanel target frame recovered (slot 35 — binary-won; prior "slot 177" REFUTED
//   by ui_system.md §1.9.4, 263bd994 RTTI pass); minimap BMP-tile
// mosaic corrected (map%d.dds retired); hotbar cooldown overlay frames added (§5.10a).
// HUD-II Wave 1 E (CAMPAIGN 17): 6 secondary toggle windows added:
//   HudOptionsWindow (OptionPanel, centered 215×204)
//   HudPartyWindow   (PartyPanel, right-dock 318×732)
//   HudTradeWindow   (KeepPanel/TradeKeepWindow, right-dock 318×732)
//   HudFriendWindow  (FriendPanel, right-dock 318×732)
//   HudGuildWindow   (GuildAPanel, right-dock 318×732)
//   HudQuestWindow   (QuestPanel, right-dock 318×732)
// HUD-II Wave 2 E (CAMPAIGN 17): 6 recovered windows + key-dispatch added:
//   HudMessagePanel    (MessagePanel, slot 190, centered 340×190)
//   HudProductWindow   (ProductPanel, slot 230, crafting)
//   HudEmoticonWindow  (EmoticonPanel, +0x370, right-dock 318×732)
//   HudTenderWindow    (TenderInfoPanel, slot 118, centered 512×595)
//   HudMailWindow      (CarrierPigeonPanal, slot 96, top-left 140×195)
//   HudDeliveryWindow  (DeliveryPanel, slot 40, 5×8 grid)
//   HudKeyDispatch     (§8.17 ASCII-keycode action→window→slot dispatch; INI remappable)
// HUD-II Wave 3 E (CAMPAIGN 17): 6 new windows:
//   HudCommandBar      (DefaultMenu, slot 148, persistent bottom strip; §8.23 CODE-CONFIRMED)
//   HudVendorWindow    (SubscriptionPanel, slot 259, NPC vendor buy/sell; §8.22 CODE-CONFIRMED)
//   HudHelpOverlay     (HelpPanel, direct MainMaster member; §8.24 CODE-CONFIRMED)
//   HudAnnouncePanel   (AnnouncePanel, slot 221, scrolling banner; §8.25.1 CODE-CONFIRMED)
//   HudErrorPanel      (ErrorPanel, slot 168, timed notice/error; §8.25.2 CODE-CONFIRMED)
//   HudPetPanel        (PetPanel, slot 194, player-couple; §8.26 CODE-CONFIRMED)
// HUD-II Wave 4 E (CAMPAIGN 17): 6 new windows:
//   HudKeepNpcDialog      (KeepNpcPanel, slot 152, NPC storage/keep dialog menu; §8.27 CODE-CONFIRMED)
//   HudStorageWindow      (KeepPanel, slot 191, player storage 60-cell grid; §8.32 CODE-CONFIRMED)
//   HudStallListWindow    (StallListPanel, slot 228, personal-stall market list; §8.29 CODE-CONFIRMED; key l)
//   HudGuildDiplomacyWindow (BroodWarListPanel, slot 235, guild-diplomacy roster; §8.30 CODE-CONFIRMED; key u)
//   HudGuildWarInfoWindow  (GuildWarInfoPanel, slot 224, guild-war info read-only; §8.31 CODE-CONFIRMED; key j)
//   HudRelationPanel       (RelationPanel, slot 193, relation/teacher/fate window; §8.28 CODE-CONFIRMED)
//
// BuddyRelation (slot 185) reconciliation:
//   BuddyRelation(185) ≠ RelationPanel(193) ≠ FriendPanel(§8.14) — all three are DISTINCT classes.
//   DefaultMenu 4002 routes to ToggleRelation() → opens BOTH slot 185 + slot 193 together.
//   Slot 185 BuddyRelation: wired to HudFriendWindow (closest social window) as interim.
//   HudRelationPanel (slot 193): built here (Wave 4 E).
//   BuddyRelation(185) full layout = TODO(world-campaign) separate deliverable.
//   spec: Docs/RE/specs/ui_system.md §8.28 — "load-bearing 185 ≠ 193 distinction"
//
// spec: Docs/RE/specs/ui_hud_layout.md §0 — MainMaster service-slot model, three routines.
// spec: Docs/RE/specs/runtime_singletons.md §3.10 — MainMaster object layout.
// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — 178 slot stores, HUD-build routine.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — in-game uitex id→DDS binding table.
// spec: Docs/RE/specs/ui_system.md §8.9.1 — OptionPanel (options window).
// spec: Docs/RE/specs/ui_system.md §8.12 — PartyPanel (party window).
// spec: Docs/RE/specs/ui_system.md §8.13 — KeepPanel (trade window).
// spec: Docs/RE/specs/ui_system.md §8.14 — FriendPanel (friend window).
// spec: Docs/RE/specs/ui_system.md §8.15 — GuildAPanel (guild window).
// spec: Docs/RE/specs/ui_system.md §8.16 — QuestPanel (quest window).
// spec: Docs/RE/specs/ui_system.md §8.17 — window-open dispatch + key map CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.18 — ProductPanel (slot 230) CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.19 — EmoticonPanel (+0x370) CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.20 — MessagePanel (slot 190) CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.21 — Tender/Mail/Delivery family CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.22 — NPC vendor slot 259 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.23 — DefaultMenu slot 148 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.24 — HelpPanel direct member CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.25 — AnnouncePanel 221 + ErrorPanel 168 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.26 — PetPanel slot 194 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.27 — KeepNpcPanel slot 152 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.28 — RelationPanel slot 193 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.29 — StallListPanel slot 228 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.30 — BroodWarListPanel slot 235 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.31 — GuildWarInfoPanel slot 224 CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.32 — KeepPanel slot 191 CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game HUD master. Builds and owns all core panels including the recovered MopGagePanel
///     target frame (HUD panel-slot array slot 35 — binary-won; prior "slot 177" REFUTED by §1.9.4).
///     <para>PASSIVE: zero game logic. Reads Application catalogues and channels; emits use-case calls.</para>
///     <para>
///         Two distinct passes (matching the recovered MainMaster build contract):
///         <list type="number">
///             <item>Geometry pass in <see cref="Build" /> — allocates panels with their confirmed rects.</item>
///             <item>Reconfigure pass in <see cref="Reconfigure" /> — sets text/sound/flags without touching rects.</item>
///         </list>
///     </para>
///     spec: Docs/RE/specs/ui_hud_layout.md §0 — MainMaster three-routine model.
///     spec: Docs/RE/specs/ui_hud_layout.md §0.1 — 178 distinct slot stores (slot set confirmed).
///     spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35 (binary-won reversal of "slot 177").
///     spec: Docs/RE/specs/ui_system.md §1.9.4 — "prior 'MopGage = slot 177' REFUTED" (263bd994 RTTI pass).
///     spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel geometry (recovered HUD-II).
///     spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex integer id→DDS key table.
/// </summary>
public sealed partial class HudMaster : Control
{
    // AnnouncePanel — scrolling announce banner (slot 221)
    // spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED
    private HudAnnouncePanel? _announcePanel;
    private HudBuffBar? _buffBar;
    private HudChatPanel? _chatPanel;

    // -------------------------------------------------------------------------
    // HUD-II Wave 3 E windows
    // -------------------------------------------------------------------------

    // DefaultMenu (bottom command strip, slot 148, persistent always-visible)
    // spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED
    private HudCommandBar? _commandBar;

    // DeliveryPanel — consignment / delivery retrieve box (slot 40, 5×8 grid)
    // spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED
    private HudDeliveryWindow? _deliveryWindow;

    // EmoticonPanel — emote / chat-emoticon picker (stored at +0x370, not slot table)
    // spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED
    private HudEmoticonWindow? _emoticonWindow;

    // ErrorPanel — timed floating notice/error modal (slot 168)
    // spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED
    private HudErrorPanel? _errorPanel;

    // Friend window (FriendPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED
    private HudFriendWindow? _friendWindow;

    // BroodWarListPanel — guild-diplomacy roster (slot 235, key `u`)
    // spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED
    private HudGuildDiplomacyWindow? _guildDiplomacyWindow;

    // GuildWarInfoPanel — guild-war info display, read-only (slot 224, key `j`)
    // spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED
    private HudGuildWarInfoWindow? _guildWarInfoWindow;

    // Guild window (GuildAPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED
    private HudGuildWindow? _guildWindow;

    // HelpPanel — full-screen help overlay (direct MainMaster member, not a slot entry)
    // spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED
    private HudHelpOverlay? _helpOverlay;

    // -------------------------------------------------------------------------
    // Services (set by Build)
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;

    // Toggle panels
    private HudInventoryPanel? _inventoryPanel;

    // -------------------------------------------------------------------------
    // HUD-II Wave 4 E windows
    // -------------------------------------------------------------------------

    // KeepNpcPanel — NPC storage/keep dialog menu (slot 152, opened by KIND-9 NPC click)
    // spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED
    private HudKeepNpcDialog? _keepNpcDialog;

    // CarrierPigeonPanal — mailbox menu (slot 96, top-left 140×195)
    // + CarrierPigeonReadPanel (slot 98) as sub-window
    // spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED
    private HudMailWindow? _mailWindow;

    // -------------------------------------------------------------------------
    // HUD-II Wave 2 E windows
    // -------------------------------------------------------------------------

    // MessagePanel — system notice / confirm modal (slot 190, centered 340×190)
    // spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED
    private HudMessagePanel? _messagePanel;
    private HudMinimapPanel? _minimapPanel;

    // -------------------------------------------------------------------------
    // Secondary toggle windows (HUD-II Wave 1 E)
    // -------------------------------------------------------------------------

    // Options window (OptionPanel, centered 215×204)
    // spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED
    private HudOptionsWindow? _optionsWindow;

    // Party window (PartyPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
    private HudPartyWindow? _partyWindow;

    // PetPanel — player-couple / pair-relation window (slot 194, NOT creature-pet)
    // spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED
    private HudPetPanel? _petPanel;

    // ProductPanel — NPC production / crafting window (slot 230)
    // spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED
    private HudProductWindow? _productWindow;

    // Quest window (QuestPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED
    private HudQuestWindow? _questWindow;

    // RelationPanel — relation/teacher/fate window (slot 193)
    // DefaultMenu 4002 opens BOTH slot 185 (BuddyRelation, TODO) + this slot 193 together.
    // BuddyRelation (slot 185) ≠ RelationPanel (193) ≠ FriendPanel (§8.14) — all DISTINCT.
    // spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED
    private HudRelationPanel? _relationPanel;
    // -------------------------------------------------------------------------
    // Child panel references
    // -------------------------------------------------------------------------

    // Always-on panels
    private HudRightEdgeGauge? _rightEdgeGauge;
    private HudSkillHotbar? _skillHotbar;
    private HudSkillPanel? _skillPanel;

    // StallListPanel — personal-stall market list (slot 228, key `l`)
    // spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED
    private HudStallListWindow? _stallListWindow;
    private HudCharacterStatsPanel? _statsPanel;

    // KeepPanel — player STORAGE / WAREHOUSE (slot 191, 60-cell 10×6 grid; no hotkey)
    // Opened ONLY via KeepNpcPanel sel 1 + C2S 2/142. DISTINCT from HudTradeWindow (§8.13).
    // spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED
    private HudStorageWindow? _storageWindow;

    // Target frame (MopGagePanel, HUD panel-slot array slot 35) — recovered in HUD-II pass.
    // SLOT CORRECTED: binary-won reversal per ui_system.md §1.9.3/§1.9.4 (263bd994 RTTI pass).
    // Slot 177 is a plain GUComponent image (bottom command-bar trailing image, §2.3) — NOT this panel.
    // spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35.
    // spec: Docs/RE/specs/ui_system.md §1.9.4 — "prior 'MopGage = slot 177' REFUTED".
    // spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED.
    private HudTargetFrame? _targetFrame;

    // TenderInfoPanel — consignment-purchase / info confirm (slot 118, centered 512×595)
    // spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED
    private HudTenderWindow? _tenderWindow;

    // Trade window (KeepPanel/TradeKeepWindow, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED
    private HudTradeWindow? _tradeWindow;

    // NPC vendor / item-shop buy/sell (SubscriptionPanel, slot 259)
    // spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED
    private HudVendorWindow? _vendorWindow;

    // -------------------------------------------------------------------------
    // Frame lifecycle
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // Per-frame hub drain delegated to individual panels; HudMaster owns no direct channel reads.
    }

    // -------------------------------------------------------------------------
    // HUD hit-test gate
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns true when the screen point (x, y) falls inside any visible HUD control.
    ///     Used by HudInputHandler to gate world clicks: if a HUD panel is under the cursor,
    ///     the click is consumed and does not fall through to click-to-move.
    ///     Includes all 16 panels (10 core + 6 secondary) — the child loop covers them all.
    ///     spec: Docs/RE/specs/input_ui.md §3 — "UI hit-test always before world interaction".
    /// </summary>
    public bool HitTest(int x, int y)
    {
        var pt = new Vector2(x, y);
        for (var i = 0; i < GetChildCount(); i++)
            if (GetChild(i) is Control c && c.Visible && c.GetRect().HasPoint(pt))
                return true;

        return false;
    }
}