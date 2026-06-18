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
// HUD-II (CAMPAIGN 17): MopGagePanel target frame recovered (slot 177); minimap BMP-tile
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
// spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel slot 177 (target frame).
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
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game HUD master. Builds and owns all core panels including the recovered MopGagePanel
/// target frame (HUD slot 177).
///
/// <para>PASSIVE: zero game logic. Reads Application catalogues and channels; emits use-case calls.</para>
///
/// <para>Two distinct passes (matching the recovered MainMaster build contract):
/// <list type="number">
///   <item>Geometry pass in <see cref="Build"/> — allocates panels with their confirmed rects.</item>
///   <item>Reconfigure pass in <see cref="Reconfigure"/> — sets text/sound/flags without touching rects.</item>
/// </list></para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §0 — MainMaster three-routine model.
/// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — 178 distinct slot stores (slot set confirmed).
/// spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel slot 177 (recovered HUD-II).
/// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex integer id→DDS key table.
/// </summary>
public sealed partial class HudMaster : Control
{
    // -------------------------------------------------------------------------
    // Child panel references
    // -------------------------------------------------------------------------

    // Always-on panels
    private HudRightEdgeGauge? _rightEdgeGauge;
    private HudChatPanel? _chatPanel;
    private HudMinimapPanel? _minimapPanel;
    private HudBuffBar? _buffBar;
    private HudSkillHotbar? _skillHotbar;

    // Toggle panels
    private HudInventoryPanel? _inventoryPanel;
    private HudSkillPanel? _skillPanel;
    private HudCharacterStatsPanel? _statsPanel;

    // Target frame (MopGagePanel, HUD slot 177) — recovered in HUD-II pass.
    // spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED.
    private HudTargetFrame? _targetFrame;

    // -------------------------------------------------------------------------
    // Secondary toggle windows (HUD-II Wave 1 E)
    // -------------------------------------------------------------------------

    // Options window (OptionPanel, centered 215×204)
    // spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED
    private HudOptionsWindow? _optionsWindow;

    // Party window (PartyPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
    private HudPartyWindow? _partyWindow;

    // Trade window (KeepPanel/TradeKeepWindow, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED
    private HudTradeWindow? _tradeWindow;

    // Friend window (FriendPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED
    private HudFriendWindow? _friendWindow;

    // Guild window (GuildAPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED
    private HudGuildWindow? _guildWindow;

    // Quest window (QuestPanel, right-dock 318×732)
    // spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED
    private HudQuestWindow? _questWindow;

    // -------------------------------------------------------------------------
    // HUD-II Wave 2 E windows
    // -------------------------------------------------------------------------

    // MessagePanel — system notice / confirm modal (slot 190, centered 340×190)
    // spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED
    private HudMessagePanel? _messagePanel;

    // ProductPanel — NPC production / crafting window (slot 230)
    // spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED
    private HudProductWindow? _productWindow;

    // EmoticonPanel — emote / chat-emoticon picker (stored at +0x370, not slot table)
    // spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED
    private HudEmoticonWindow? _emoticonWindow;

    // TenderInfoPanel — consignment-purchase / info confirm (slot 118, centered 512×595)
    // spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED
    private HudTenderWindow? _tenderWindow;

    // CarrierPigeonPanal — mailbox menu (slot 96, top-left 140×195)
    // + CarrierPigeonReadPanel (slot 98) as sub-window
    // spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED
    private HudMailWindow? _mailWindow;

    // DeliveryPanel — consignment / delivery retrieve box (slot 40, 5×8 grid)
    // spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED
    private HudDeliveryWindow? _deliveryWindow;

    // -------------------------------------------------------------------------
    // HUD-II Wave 3 E windows
    // -------------------------------------------------------------------------

    // DefaultMenu (bottom command strip, slot 148, persistent always-visible)
    // spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED
    private HudCommandBar? _commandBar;

    // NPC vendor / item-shop buy/sell (SubscriptionPanel, slot 259)
    // spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED
    private HudVendorWindow? _vendorWindow;

    // HelpPanel — full-screen help overlay (direct MainMaster member, not a slot entry)
    // spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED
    private HudHelpOverlay? _helpOverlay;

    // AnnouncePanel — scrolling announce banner (slot 221)
    // spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED
    private HudAnnouncePanel? _announcePanel;

    // ErrorPanel — timed floating notice/error modal (slot 168)
    // spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED
    private HudErrorPanel? _errorPanel;

    // PetPanel — player-couple / pair-relation window (slot 194, NOT creature-pet)
    // spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED
    private HudPetPanel? _petPanel;

    // -------------------------------------------------------------------------
    // HUD-II Wave 4 E windows
    // -------------------------------------------------------------------------

    // KeepNpcPanel — NPC storage/keep dialog menu (slot 152, opened by KIND-9 NPC click)
    // spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED
    private HudKeepNpcDialog? _keepNpcDialog;

    // KeepPanel — player STORAGE / WAREHOUSE (slot 191, 60-cell 10×6 grid; no hotkey)
    // Opened ONLY via KeepNpcPanel sel 1 + C2S 2/142. DISTINCT from HudTradeWindow (§8.13).
    // spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED
    private HudStorageWindow? _storageWindow;

    // StallListPanel — personal-stall market list (slot 228, key `l`)
    // spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED
    private HudStallListWindow? _stallListWindow;

    // BroodWarListPanel — guild-diplomacy roster (slot 235, key `u`)
    // spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED
    private HudGuildDiplomacyWindow? _guildDiplomacyWindow;

    // GuildWarInfoPanel — guild-war info display, read-only (slot 224, key `j`)
    // spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED
    private HudGuildWarInfoWindow? _guildWarInfoWindow;

    // RelationPanel — relation/teacher/fate window (slot 193)
    // DefaultMenu 4002 opens BOTH slot 185 (BuddyRelation, TODO) + this slot 193 together.
    // BuddyRelation (slot 185) ≠ RelationPanel (193) ≠ FriendPanel (§8.14) — all DISTINCT.
    // spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED
    private HudRelationPanel? _relationPanel;

    // -------------------------------------------------------------------------
    // Services (set by Build)
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;

    // -------------------------------------------------------------------------
    // Construction entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: allocates all core panels at their confirmed rects.
    /// Mirrors the HUD-build routine's flat sequence of allocate→size→attach→slot-store.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — HUD-build routine (builds panels).
    /// </summary>
    public void Build(ClientContext ctx, HudAtlasLibrary atlas, HudIconLibrary icons, HudTextLibrary text)
    {
        // Root: full-rect, mouse-ignore so world input passes through.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _hub = ctx.HudEventHub;

        // Geometry pass — panel order matches paint order (insertion = back-to-front).

        // 1. Right-edge HP/MP gauge composite
        //    spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula
        //    Strips at screen_width−135, Y=200 / Y=250, W=140, H=35
        _rightEdgeGauge = new HudRightEdgeGauge();
        AddChild(_rightEdgeGauge);
        _rightEdgeGauge.Build(atlas);

        // 2. Chat panel (output window 448×324 + input box 330×20)
        //    spec: Docs/RE/specs/ui_hud_layout.md §1.2 CONFIRMED
        _chatPanel = new HudChatPanel();
        AddChild(_chatPanel);
        _chatPanel.Build(atlas);

        // 3. Minimap (MapPanel, 135×195, top-right)
        //    spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED
        _minimapPanel = new HudMinimapPanel();
        AddChild(_minimapPanel);
        _minimapPanel.Build(atlas);

        // 4. Buff bar (data-driven from buff_icon_position.xdb, base X=545)
        //    spec: Docs/RE/specs/ui_hud_layout.md §2 / §5.10 CODE-CONFIRMED
        _buffBar = new HudBuffBar();
        AddChild(_buffBar);
        _buffBar.Build(icons);

        // 5. Skill hotbar (container origin 349,13; 9 slots data-driven)
        //    spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.10 CODE-CONFIRMED container origin
        _skillHotbar = new HudSkillHotbar();
        AddChild(_skillHotbar);
        _skillHotbar.Build(atlas, icons);

        // 6. Inventory window (318×732, right-anchored; key I)
        //    spec: Docs/RE/specs/ui_hud_layout.md §1.1 / §5.3 CODE-CONFIRMED
        //    spec: Docs/RE/specs/ui_system.md §8.10.1 — ItemPanel 8×5 grid
        _inventoryPanel = new HudInventoryPanel();
        AddChild(_inventoryPanel);
        _inventoryPanel.Build(atlas, ctx);

        // 7. Skill window (964×655, centred; key K)
        //    spec: Docs/RE/specs/ui_system.md §8.8 SkillPanel builder CODE-CONFIRMED
        //    spec: Docs/RE/specs/ui_hud_layout.md §5.2 — parked at (43, -655)
        _skillPanel = new HudSkillPanel();
        AddChild(_skillPanel);
        _skillPanel.Build(atlas, text);

        // 8. Character stats window (StatusPanel, centred; key C)
        //    spec: Docs/RE/specs/ui_system.md §8.7 StatusPanel builder CODE-CONFIRMED
        _statsPanel = new HudCharacterStatsPanel();
        AddChild(_statsPanel);
        _statsPanel.Build(atlas, text, ctx);

        // 9. Selected-target plate (MopGagePanel, HUD slot 177) — recovered in HUD-II pass.
        //    Screen-width-centred W=226, top-anchored, transparent container.
        //    Children bind uitex id 1 chrome; HP fill = min(172, 172·hpRatio) px wide.
        //    spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED (slot 177).
        _targetFrame = new HudTargetFrame();
        AddChild(_targetFrame);
        _targetFrame.Build(atlas);

        // 10. Options window (OptionPanel, centered 215×204)
        //     spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED
        _optionsWindow = new HudOptionsWindow();
        AddChild(_optionsWindow);
        _optionsWindow.Build(atlas, text);

        // 11. Party window (PartyPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
        _partyWindow = new HudPartyWindow();
        AddChild(_partyWindow);
        _partyWindow.Build(atlas, text);

        // 12. Trade window (KeepPanel/TradeKeepWindow, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED
        _tradeWindow = new HudTradeWindow();
        AddChild(_tradeWindow);
        _tradeWindow.Build(atlas, text);

        // 13. Friend window (FriendPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED
        _friendWindow = new HudFriendWindow();
        AddChild(_friendWindow);
        _friendWindow.Build(atlas, text);

        // 14. Guild window (GuildAPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED
        _guildWindow = new HudGuildWindow();
        AddChild(_guildWindow);
        _guildWindow.Build(atlas, text);

        // 15. Quest window (QuestPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED
        _questWindow = new HudQuestWindow();
        AddChild(_questWindow);
        _questWindow.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 2 E panels (16→22)
        // -------------------------------------------------------------------------

        // 16. MessagePanel — system notice / confirm modal (slot 190, centered 340×190)
        //     spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 190.
        _messagePanel = new HudMessagePanel();
        AddChild(_messagePanel);
        _messagePanel.Build(atlas);

        // 17. ProductPanel — NPC production / crafting window (slot 230)
        //     spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 230.
        _productWindow = new HudProductWindow();
        AddChild(_productWindow);
        _productWindow.Build(atlas, text);

        // 18. EmoticonPanel — emote / chat-emoticon picker (+0x370)
        //     spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — MainWindow +0x370.
        _emoticonWindow = new HudEmoticonWindow();
        AddChild(_emoticonWindow);
        _emoticonWindow.Build(atlas, ctx);

        // 19. TenderInfoPanel — consignment-purchase / info confirm (slot 118, 512×595)
        //     spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 118.
        _tenderWindow = new HudTenderWindow();
        AddChild(_tenderWindow);
        _tenderWindow.Build(atlas, text);

        // 20. CarrierPigeonPanal — mailbox menu (slot 96, top-left 140×195)
        //     + CarrierPigeonReadPanel (slot 98) as sub-window.
        //     spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 96/98.
        _mailWindow = new HudMailWindow();
        AddChild(_mailWindow);
        _mailWindow.Build(atlas, text);

        // 21. DeliveryPanel — consignment / delivery retrieve box (slot 40, 5×8 grid)
        //     spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 40.
        _deliveryWindow = new HudDeliveryWindow();
        AddChild(_deliveryWindow);
        _deliveryWindow.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 3 E panels (22→27 + help overlay)
        // -------------------------------------------------------------------------

        // 22. HudCommandBar — bottom command strip (DefaultMenu, slot 148, always-visible)
        //     spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 148, always-built strip.
        _commandBar = new HudCommandBar();
        AddChild(_commandBar);
        _commandBar.Wire(
            onInventory: ToggleInventory,
            onSkill: ToggleSkill,
            onQuest: ToggleQuest,
            onStats: ToggleStats,
            onHelp: ToggleHelp,
            onParty: ToggleParty,
            onProduct: ToggleProduct,
            onRelation: ToggleRelation // DefaultMenu 4002 → RelationPanel(193) + BuddyRelation(185 interim)
            // spec: ui_system.md §8.28.5 CODE-CONFIRMED — "4002 group-open opens BOTH 185+193"
        );
        _commandBar.Build(atlas);

        // 23. HudVendorWindow — NPC vendor / item-shop buy/sell (slot 259)
        //     spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 259.
        _vendorWindow = new HudVendorWindow();
        AddChild(_vendorWindow);
        _vendorWindow.Build(atlas, text);

        // 24. HudHelpOverlay — full-screen help image (direct MainMaster member, not slot entry)
        //     spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.
        //     Note: NOT a service-slot entry; direct member of the root HUD window.
        _helpOverlay = new HudHelpOverlay();
        AddChild(_helpOverlay);
        _helpOverlay.Build(atlas);

        // 25. HudAnnouncePanel — scrolling announce banner (slot 221)
        //     spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 221.
        _announcePanel = new HudAnnouncePanel();
        AddChild(_announcePanel);
        _announcePanel.Build();

        // 26. HudErrorPanel — timed floating notice/error modal (slot 168)
        //     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 168.
        _errorPanel = new HudErrorPanel();
        AddChild(_errorPanel);
        _errorPanel.Build();
        // Wire the AnnouncePanel delegate (§8.25.1 — "forwards banner to AnnouncePanel when present")
        _errorPanel.SetAnnounceDelegate(_announcePanel);

        // 27. HudPetPanel — player-couple/pair-relation window (slot 194, NOT creature-pet)
        //     spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 194.
        _petPanel = new HudPetPanel();
        AddChild(_petPanel);
        _petPanel.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 4 E panels (28→33)
        // -------------------------------------------------------------------------

        // 28. HudStorageWindow — player storage/warehouse (KeepPanel, slot 191, 60-cell grid)
        //     Opened ONLY via KeepNpcPanel sel 1 + C2S 2/142. No hotkey.
        //     DISTINCT from HudTradeWindow (§8.13 TradeKeepWindow).
        //     spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED.
        _storageWindow = new HudStorageWindow();
        AddChild(_storageWindow);
        _storageWindow.Build(atlas, text);

        // 29. HudKeepNpcDialog — NPC storage/keep dialog menu (KeepNpcPanel, slot 152)
        //     Opened by world NPC-click dispatcher when NPC is KIND 9.
        //     Routes sel 1 → _storageWindow.Open(); routes to vendor (slot 259) via ShowVendor.
        //     spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED.
        _keepNpcDialog = new HudKeepNpcDialog();
        AddChild(_keepNpcDialog);
        _keepNpcDialog.Wire(
            onOpenStorage: () => _storageWindow?.Open(), // sel 1 → KeepPanel §8.27.3
            onVendor: () => _vendorWindow?.Open() // sel 0 → NPC dialog (vendor in absence of dedicated dialog window)
        );
        _keepNpcDialog.Build(atlas);

        // 30. HudStallListWindow — personal-stall market list (StallListPanel, slot 228, key `l`)
        //     spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED.
        _stallListWindow = new HudStallListWindow();
        AddChild(_stallListWindow);
        _stallListWindow.Build(atlas, text);

        // 31. HudGuildDiplomacyWindow — guild-diplomacy roster (BroodWarListPanel, slot 235, key `u`)
        //     spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED.
        _guildDiplomacyWindow = new HudGuildDiplomacyWindow();
        AddChild(_guildDiplomacyWindow);
        _guildDiplomacyWindow.Build(atlas);

        // 32. HudGuildWarInfoWindow — guild-war info display, read-only (GuildWarInfoPanel, slot 224, key `j`)
        //     spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED.
        _guildWarInfoWindow = new HudGuildWarInfoWindow();
        AddChild(_guildWarInfoWindow);
        _guildWarInfoWindow.Build(atlas);

        // 33. HudRelationPanel — relation/teacher/fate window (RelationPanel, slot 193)
        //     DefaultMenu 4002 opens BOTH slot 185 (BuddyRelation, TODO) + this slot 193 together.
        //     spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED.
        _relationPanel = new HudRelationPanel();
        AddChild(_relationPanel);
        _relationPanel.Build(atlas, text);

        GD.Print("[HudMaster] Build complete — ~33 panels total " +
                 "(10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E, + HelpOverlay member). " +
                 "Wave-1 E: Options(§8.9.1) Party(§8.12) Trade(§8.13) Friend(§8.14) Guild(§8.15) Quest(§8.16). " +
                 "Wave-2 E: Message(§8.20) Product(§8.18) Emoticon(§8.19) Tender(§8.21.1) Mail(§8.21.2-4) Delivery(§8.21.5). " +
                 "Wave-3 E: CommandBar(§8.23) Vendor(§8.22) HelpOverlay(§8.24) Announce(§8.25.1) Error(§8.25.2) Pet(§8.26). " +
                 "Wave-4 E: Storage(§8.32/slot191) KeepNpcDialog(§8.27/slot152) StallList(§8.29/slot228/l) " +
                 "GuildDiplomacy(§8.30/slot235/u) GuildWarInfo(§8.31/slot224/j) Relation(§8.28/slot193). " +
                 "Key dispatch: §8.17 ASCII-keycode map wired in _Input (i/b/s/q/k/g/h/l/u/j/Esc). " +
                 "CommandBar entry→slot wiring: Inventory(4001/158) Skill(4003/159) Quest(4004/206) Status(4005/146) " +
                 "Help(4011/§8.24) Party(4012/220) Product(4013/230) Relation(4002/185+193). " +
                 "KeepNpcDialog: sel1→Storage; sel0→Vendor(interim). " +
                 "BuddyRelation(185) reconciliation: interim→FriendWindow; full build TODO(world-campaign). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel slot 177 recovered HUD-II. " +
                 "spec: Docs/RE/specs/ui_system.md §8.17/§8.18/§8.19/§8.20/§8.21/§8.22/§8.23/§8.24/§8.25/§8.26 CODE-CONFIRMED. " +
                 "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 CODE-CONFIRMED.");
    }

    /// <summary>
    /// Reconfigure pass: sets text/sound/flags on already-built panels.
    /// Mirrors the per-GameState reconfigure routine (assigns no rectangles).
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — reconfigure routine (text/sound/flags only).
    /// </summary>
    public void Reconfigure()
    {
        // The reconfigure routine in the binary re-reads already-built panels and sets their
        // label text (CP949, from msg database), font slots, colours, visibility flags, and entry
        // sounds. It assigns NO rectangles. In our port the panels read text via HudTextLibrary
        // at their own build time, so a separate reconfigure call is a no-op for now.
        // TODO(world-campaign): trigger per-state reconfigure on scene-state changes.
        GD.Print("[HudMaster] Reconfigure called. spec: Docs/RE/specs/ui_hud_layout.md §0.1.");
    }

    /// <summary>
    /// Binds all panels to the Application HUD event hub.
    /// Call after Build and after the node is in the scene tree.
    /// </summary>
    public void BindHub(ClientContext ctx)
    {
        var hub = ctx.HudEventHub;
        _rightEdgeGauge?.BindHub(hub);
        _chatPanel?.BindHub(hub);
        _chatPanel?.WireSendIntent(ctx);
        _minimapPanel?.BindHub(hub);
        _buffBar?.BindHub(hub);
        _inventoryPanel?.BindHub(hub);
        _statsPanel?.BindHub(hub);
        _targetFrame?.BindHub(hub);
        // Secondary windows — only PartyPanel has a BindHub (others drain no hub channel yet)
        // spec: Docs/RE/specs/ui_system.md §8.12 — party roster via S2C 5/21 + 5/38 (TODO world-campaign)
        _partyWindow?.BindHub(hub);
        // Wave-3 E panels have no hub channels yet (all world-campaign).
        // ErrorPanel/AnnouncePanel: TODO(world-campaign) wire global notice sink (4/500 etc.)
        // PetPanel: TODO(world-campaign) wire SmsgActorPairRelation 5/53.
        // spec: Docs/RE/specs/ui_system.md §8.25.3 — notice routing (world-campaign).
        // spec: Docs/RE/specs/ui_system.md §8.26.4 — PetPanel open via 5/53 (world-campaign).

        GD.Print(
            "[HudMaster] BindHub: ~33 panels connected (10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E). " +
            "Party hub wired (stub). Wave-3/4 panels have no hub channel yet " +
            "(TODO world-campaign: global notice sink, 5/53 pair-relation, S2C 4/74/4/81/5/73, relation roster push). " +
            "spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel slot 177. " +
            "spec: Docs/RE/specs/ui_system.md §8.12 — PartyPanel hub stub (TODO world-campaign). " +
            "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 — Wave-4 TODO hubs.");
    }

    /// <summary>
    /// Forwards key-I toggle to the inventory+skill pair (they toggle together).
    /// spec: Docs/RE/specs/ui_system.md §8.10.1 — [I] toggles slots 158+159 together.
    /// </summary>
    public void ToggleInventory()
    {
        _inventoryPanel?.Toggle();
        _skillPanel?.Toggle();
    }

    /// <summary>
    /// Forwards key-K toggle to the skill window only.
    /// </summary>
    public void ToggleSkill()
    {
        _skillPanel?.Toggle();
    }

    /// <summary>
    /// Forwards key-C toggle to the character stats window.
    /// </summary>
    public void ToggleStats()
    {
        _statsPanel?.Toggle();
    }

    // -------------------------------------------------------------------------
    // Secondary window toggle methods (HUD-II Wave 1 E)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the Options window (OptionPanel).
    /// Toggle key: UNVERIFIED (keybind-table/debugger-pending). ESC always closes.
    /// spec: Docs/RE/specs/ui_system.md §8.9.1 — "open trigger UNVERIFIED; ESC CODE-CONFIRMED".
    /// TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleOptions() => _optionsWindow?.Toggle();

    /// <summary>
    /// Toggles the Party window (PartyPanel).
    /// Toggle key: UNVERIFIED (key-table/capture-pending).
    /// spec: Docs/RE/specs/ui_system.md §8.12 — "toggle hotkey key-table/capture-pending".
    /// TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleParty() => _partyWindow?.Toggle();

    /// <summary>
    /// Shows the Trade window (KeepPanel/TradeKeepWindow).
    /// Normally opened by S2C 4/23 phase=3 (no hotkey).
    /// spec: Docs/RE/specs/ui_system.md §8.13 — "no hotkey; opened by trade state machine S2C 4/23".
    /// </summary>
    public void ShowTrade(bool show) => _tradeWindow?.Toggle(show);

    /// <summary>
    /// Toggles the Friend window (FriendPanel).
    /// Toggle key: UNVERIFIED. ESC closes.
    /// spec: Docs/RE/specs/ui_system.md §8.14 — "open key UNVERIFIED; ESC blurs+closes".
    /// TODO(spec): toggle hotkey.
    /// </summary>
    public void ToggleFriend() => _friendWindow?.Toggle();

    /// <summary>
    /// Toggles the Guild window (GuildAPanel).
    /// No dedicated hotkey (CODE-CONFIRMED). Opened by guild NPC/context action.
    /// spec: Docs/RE/specs/ui_system.md §8.15 — "no dedicated guild hotkey CODE-CONFIRMED".
    /// TODO(spec): open via guild context action (no hotkey).
    /// </summary>
    public void ToggleGuild() => _guildWindow?.Toggle();

    /// <summary>
    /// Toggles the Quest window (QuestPanel).
    /// Key 'q' (ASCII 113) — CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.17.1 — "103 'q' = QuestPanel + close-all, slot 206 CODE-CONFIRMED".
    /// </summary>
    public void ToggleQuest() => _questWindow?.Toggle();

    // -------------------------------------------------------------------------
    // HUD-II Wave 2 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows or hides the system notice / confirm modal (MessagePanel, slot 190).
    /// Raised by client-side code paths (hotbar/skill-link confirm etc.) — no hotkey.
    /// Use <see cref="ShowNotice"/> / <see cref="ShowConfirm"/> for caller API.
    /// spec: Docs/RE/specs/ui_system.md §8.20.6 — "reached as master slot 190; raised by client-side code".
    /// </summary>
    public void ToggleMessage(bool? forceState = null)
    {
        if (forceState.HasValue && !forceState.Value)
            _messagePanel?.ShowNotice(string.Empty); // close by hiding (internal)
        // Direct callers should use ShowNotice / ShowConfirm; Toggle is for ESC-close.
    }

    /// <summary>
    /// Shows a system notice modal (mode 0 — single OK).
    /// spec: Docs/RE/specs/ui_system.md §8.20 — mode 0 CODE-CONFIRMED.
    /// </summary>
    public void ShowNotice(string text) => _messagePanel?.ShowNotice(text);

    /// <summary>
    /// Shows a confirm modal (mode 1 — Yes/No).
    /// spec: Docs/RE/specs/ui_system.md §8.20 — mode 1 CODE-CONFIRMED.
    /// </summary>
    public void ShowConfirm(string text, Action? onYes = null, Action? onNo = null)
        => _messagePanel?.ShowConfirm(text, onYes, onNo);

    /// <summary>
    /// Toggles the production / crafting window (ProductPanel, slot 230).
    /// Keys: DefaultMenu action 4013 / keybind path (billing-gated).
    /// spec: Docs/RE/specs/ui_system.md §8.17.3 — "action 4013 → slot 230 CODE-CONFIRMED".
    /// spec: Docs/RE/specs/ui_system.md §8.18.5 — "opened from DefaultMenu radial action 4013".
    /// </summary>
    public void ToggleProduct() => _productWindow?.Toggle();

    /// <summary>
    /// Toggles the emote / chat-emoticon picker (EmoticonPanel, +0x370).
    /// Open trigger (hotkey/toolbar action id) = residual (debugger-pending).
    /// spec: Docs/RE/specs/ui_system.md §8.19.7 — "toolbar action id SHOWING = residual".
    /// TODO(spec): toggle hotkey — residual (debugger-pending).
    /// </summary>
    public void ToggleEmoticon() => _emoticonWindow?.Toggle();

    /// <summary>
    /// Shows or hides the tender/consignment window (TenderInfoPanel, slot 118).
    /// Opened from NPC consignment interactions (no hotkey).
    /// spec: Docs/RE/specs/ui_system.md §8.21.1 — "open from NPC interaction; opcode-pending".
    /// </summary>
    public void ShowTender(bool show) => _tenderWindow?.Toggle(show);

    /// <summary>
    /// Shows or hides the mailbox menu (CarrierPigeonPanal, slot 96).
    /// Opened from NPC service interactions (no hotkey).
    /// spec: Docs/RE/specs/ui_system.md §8.21.2 — "open from NPC-service; opcode debugger-pending".
    /// </summary>
    public void ShowMail(bool show) => _mailWindow?.Toggle(show);

    /// <summary>
    /// Shows or hides the delivery retrieve box (DeliveryPanel, slot 40).
    /// Opened from NPC service interactions (no hotkey).
    /// spec: Docs/RE/specs/ui_system.md §8.21.5 — "open from NPC-service; opcode debugger-pending".
    /// </summary>
    public void ShowDelivery(bool show) => _deliveryWindow?.Toggle(show);

    // -------------------------------------------------------------------------
    // HUD-II Wave 3 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the HelpPanel full-screen overlay (direct MainMaster member, §8.24).
    /// Called by key 'h' (§8.17.1) and HudCommandBar action 4011 (§8.23.3).
    /// spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED — "key h = toggle help overlay".
    /// </summary>
    public void ToggleHelp() => _helpOverlay?.Toggle();

    /// <summary>
    /// Opens the NPC vendor / item-shop window (slot 259) for the given NPC.
    /// Called by the world NPC-interaction dispatcher on KIND 32.
    /// spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED — "KIND 32 opens slot 259".
    /// </summary>
    public void ShowVendor(uint npcId = 0) => _vendorWindow?.Open(npcId);

    /// <summary>
    /// Shows a scrolling announce banner (AnnouncePanel, slot 221).
    /// spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
    /// </summary>
    public void ShowAnnounce(string text) => _announcePanel?.ShowAnnounce(text);

    /// <summary>
    /// Shows a timed error/notice modal (ErrorPanel, slot 168).
    /// Also delegates the banner to AnnouncePanel (§8.25.1).
    /// spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
    /// TODO(world-campaign): wire 4/500 SmsgShowPopupByCode sink.
    /// spec: Docs/RE/specs/ui_system.md §8.25.3 — SmsgShowPopupByCode 4/500.
    /// </summary>
    public void ShowError(string text, double seconds = 5.0) => _errorPanel?.ShowError(text, seconds);

    /// <summary>
    /// Shows the PetPanel (player-couple companion window, slot 194) with partner data.
    /// Called by S2C SmsgActorPairRelation (5/53).
    /// spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.
    /// TODO(world-campaign): wire from 5/53 packet handler.
    /// </summary>
    public void ShowPetPanel(string partnerName, int partnerLevel, float gauge0 = 0f, float gauge1 = 0f)
        => _petPanel?.ShowPartner(partnerName, partnerLevel, gauge0, gauge1);

    /// <summary>
    /// Hides the PetPanel (relation clear or partner death).
    /// spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.
    /// </summary>
    public void ClearPetPanel() => _petPanel?.ClearPartner();

    // -------------------------------------------------------------------------
    // HUD-II Wave 4 E toggle/show methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the KeepNpcPanel dialog (slot 152) for the given KIND-9 NPC.
    /// Called by the world NPC-click dispatcher when NPC kind is 9.
    /// spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED — "KIND 9 → open KeepNpcPanel".
    /// </summary>
    public void ShowKeepNpcDialog(uint npcId = 0) => _keepNpcDialog?.Open(npcId);

    /// <summary>
    /// Opens the player storage/warehouse window (KeepPanel, slot 191).
    /// Normally called from KeepNpcPanel sel 1 via HudKeepNpcDialog routing.
    /// spec: Docs/RE/specs/ui_system.md §8.32.5 CODE-CONFIRMED — "opened via KeepNpcPanel sel 1 only".
    /// </summary>
    public void ShowStorage() => _storageWindow?.Open();

    /// <summary>
    /// Toggles the personal-stall market list (StallListPanel, slot 228).
    /// Key `l` (ASCII 108) — CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED — "key l toggle".
    /// </summary>
    public void ToggleStallList() => _stallListWindow?.Toggle();

    /// <summary>
    /// Toggles the guild-diplomacy roster (BroodWarListPanel, slot 235).
    /// Key `u` (ASCII 117) — CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED — "key u toggle".
    /// </summary>
    public void ToggleGuildDiplomacy() => _guildDiplomacyWindow?.Toggle();

    /// <summary>
    /// Toggles the guild-war info display window (GuildWarInfoPanel, slot 224).
    /// Key `j` (ASCII 106) — CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED — "key j toggle".
    /// </summary>
    public void ToggleGuildWarInfo() => _guildWarInfoWindow?.Toggle();

    /// <summary>
    /// Toggles the RelationPanel (slot 193).
    /// Called by DefaultMenu action 4002 (which also opens BuddyRelation slot 185).
    /// BuddyRelation (slot 185) interim: routes to ToggleFriend() until full BuddyRelation is built.
    /// spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED — "4002 opens both 185+193".
    /// spec: Docs/RE/specs/ui_system.md §8.28 — "BuddyRelation(185) = separate deliverable TODO(world-campaign)".
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

    /// <summary>
    /// Returns true when the screen point (x, y) falls inside any visible HUD control.
    /// Used by HudInputHandler to gate world clicks: if a HUD panel is under the cursor,
    /// the click is consumed and does not fall through to click-to-move.
    /// Includes all 16 panels (10 core + 6 secondary) — the child loop covers them all.
    /// spec: Docs/RE/specs/input_ui.md §3 — "UI hit-test always before world interaction".
    /// </summary>
    public bool HitTest(int x, int y)
    {
        var pt = new Vector2(x, y);
        for (int i = 0; i < GetChildCount(); i++)
        {
            if (GetChild(i) is Control c && c.Visible && c.GetRect().HasPoint(pt))
                return true;
        }

        return false;
    }

    public override void _Process(double delta)
    {
        // Per-frame hub drain delegated to individual panels; HudMaster owns no direct channel reads.
    }

    // -------------------------------------------------------------------------
    // §8.17 ASCII-keycode window-open dispatch (CODE-CONFIRMED keys)
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
    // -------------------------------------------------------------------------

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
                // 'i' (ASCII 105) — Inventory open (group-toggle), slot 146/158 group.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "105 'i' = inventory group-toggle CODE-CONFIRMED".
                // Also backfills Wave-1 TODO: §8.10.1 "[I] toggles slots 158+159 together".
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.B:
                // 'b' (ASCII 98) — Inventory bag (ItemPanel), slot 158.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "98 'b' = ItemPanel slot 158 CODE-CONFIRMED".
                // Backfills Wave-1 hotkey TODO on inventory.
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.S:
                // 's' (ASCII 115) — Skill window (SkillPanel) toggle group, slot 159.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "115 's' = SkillPanel slot 159 CODE-CONFIRMED".
                // Backfills Wave-1 hotkey TODO on skill.
                ToggleSkill();
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
                // 'k' (ASCII 107) — Party open (PartyPanel) + close-others, slot 220.
                // spec: Docs/RE/specs/ui_system.md §8.17.1 — "107 'k' = PartyPanel slot 220 CODE-CONFIRMED".
                // Backfills Wave-1 hotkey TODO on party.
                ToggleParty();
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
    /// ESC key handler — closes the topmost visible toggle window.
    /// Mirrors the legacy "close-top / collapse" behaviour from §8.17.1.
    /// spec: Docs/RE/specs/ui_system.md §8.17.1 — "27 Esc = close inventory group / collapse dock CODE-CONFIRMED".
    /// </summary>
    private void OnEscapeKey()
    {
        // Close priority: bottom strip panels first, then the core toggle windows.
        // The legacy dispatcher closes the inventory group (slots 146+158+159) on Esc.
        // spec: ui_system.md §8.17.1 CODE-CONFIRMED — "close inventory group / collapse dock".

        // Wave-4 E: KeepNpcDialog (NPC menu — close before storage)
        if (_keepNpcDialog?.Visible == true)
        {
            /* let its own _Input handle ESC */
            return;
        }

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
        {
            /* let its own _Input handle ESC */
            return;
        }

        // Wave-3 E: Vendor window
        if (_vendorWindow?.Visible == true)
        {
            _vendorWindow.Toggle(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Wave-2 E modals (highest priority — they grab focus)
        if (_messagePanel?.Visible == true)
        {
            /* let its own _Input handle ESC */
            return;
        }

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
            return;
        }
    }
}