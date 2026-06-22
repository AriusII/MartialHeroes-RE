// Ui/Hud/HudMaster.Builder.cs
//
// Partial — geometry pass (Build), reconfigure pass (Reconfigure), and hub-binding (BindHub).
// Mirrors the three-routine MainMaster build contract recovered from doida.exe.
//
// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — HUD-build routine (builds panels).
// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — reconfigure routine (text/sound/flags only).
// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex integer id→DDS key table.

using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    /// <summary>
    ///     Geometry pass: allocates all core panels at their confirmed rects.
    ///     Mirrors the HUD-build routine's flat sequence of allocate→size→attach→slot-store.
    ///     BUILDER PATTERN: each panel is constructed via AddPanel&lt;T&gt;() which assigns the field
    ///     reference AND calls AddChild() in the same order as the original flat sequence.
    ///     The 3 panels requiring pre-build wiring (CommandBar, KeepNpcDialog, ErrorPanel) are
    ///     kept as explicit inline steps to guarantee Wire() precedes Build() per the spec.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §0.1 — HUD-build routine (builds panels).
    /// </summary>
    public void Build(ClientContext ctx, HudAtlasLibrary atlas, HudIconLibrary icons, HudTextLibrary text)
    {
        // Root: full-rect, mouse-ignore so world input passes through.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _hub = ctx.HudEventHub;

        // -----------------------------------------------------------------------
        // Geometry pass — panel order matches paint order (insertion = back-to-front).
        // Each AddPanel<T>() call: new T() → AddChild(t) → return t (caller assigns field).
        // Invariant: insertion order here == AddChild order == z/paint order.
        // -----------------------------------------------------------------------

        // 0. PlayerStatusPanel — HP/MP/stamina gauges + portrait + condition bar + level (slot 15)
        //    "Built top-left (dst 0,0; w=285, h=88); base atlas = UI-manifest key 4."
        //    PASSIVE: drains IHudEventHub.Vitals; updates only fills and labels.
        //    spec: Docs/RE/scenes/ingame.md §2 — Core HUD group; §2.1 internal layout; §2.4 src-rects.
        _playerStatusPanel = AddPanel<HudPlayerStatusPanel>();
        _playerStatusPanel.Build(atlas);

        // 1. Right-edge HP/MP gauge composite
        //    spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula
        //    Strips at screen_width−135, Y=200 / Y=250, W=140, H=35
        _rightEdgeGauge = AddPanel<HudRightEdgeGauge>();
        _rightEdgeGauge.Build(atlas);

        // 2. Chat panel (output window 448×324 + input box 330×20)
        //    spec: Docs/RE/specs/ui_hud_layout.md §1.2 CONFIRMED
        _chatPanel = AddPanel<HudChatPanel>();
        _chatPanel.Build(atlas);

        // 3. Minimap (MapPanel, 135×195, top-right)
        //    spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED
        _minimapPanel = AddPanel<HudMinimapPanel>();
        _minimapPanel.Build(atlas);

        // 4. Buff bar (data-driven from buff_icon_position.xdb, base X=545)
        //    spec: Docs/RE/specs/ui_hud_layout.md §2 / §5.10 CODE-CONFIRMED
        _buffBar = AddPanel<HudBuffBar>();
        _buffBar.Build(icons);

        // 5. Skill hotbar (container origin 349,13; 9 slots data-driven)
        //    spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.10 CODE-CONFIRMED container origin
        _skillHotbar = AddPanel<HudSkillHotbar>();
        _skillHotbar.Build(atlas, icons);

        // 6. Inventory window (318×732, right-anchored; key I)
        //    spec: Docs/RE/specs/ui_hud_layout.md §1.1 / §5.3 CODE-CONFIRMED
        //    spec: Docs/RE/specs/ui_system.md §8.10.1 — ItemPanel 8×5 grid
        _inventoryPanel = AddPanel<HudInventoryPanel>();
        _inventoryPanel.Build(atlas, ctx);

        // 7. Skill window (964×655, centred; key K)
        //    spec: Docs/RE/specs/ui_system.md §8.8 SkillPanel builder CODE-CONFIRMED
        //    spec: Docs/RE/specs/ui_hud_layout.md §5.2 — parked at (43, -655)
        _skillPanel = AddPanel<HudSkillPanel>();
        _skillPanel.Build(atlas, text);

        // 8. Character stats window (StatusPanel, centred; key C)
        //    spec: Docs/RE/specs/ui_system.md §8.7 StatusPanel builder CODE-CONFIRMED
        _statsPanel = AddPanel<HudCharacterStatsPanel>();
        _statsPanel.Build(atlas, text, ctx);

        // 9. Selected-target plate (MopGagePanel, HUD panel-slot array slot 35) — recovered in HUD-II pass.
        //    SLOT CORRECTED: binary-won reversal per ui_system.md §1.9.3/§1.9.4 (263bd994 RTTI pass).
        //    Slot 177 = plain GUComponent image (bottom command-bar trailing element, §2.3) — NOT this panel.
        //    Screen-width-centred W=226, top-anchored, transparent container.
        //    Children bind uitex id 1 chrome; HP fill = min(172, 172·hpRatio) px wide.
        //    spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35 (binary-won).
        //    spec: Docs/RE/specs/ui_system.md §1.9.4 — "prior 'MopGage = slot 177' REFUTED".
        //    spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel geometry, HP bar formula, child widgets.
        _targetFrame = AddPanel<HudTargetFrame>();
        _targetFrame.Build(atlas);

        // 10. Options window (OptionPanel, centered 215×204)
        //     spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED
        _optionsWindow = AddPanel<HudOptionsWindow>();
        _optionsWindow.Build(atlas, text);

        // 11. Party window (PartyPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
        _partyWindow = AddPanel<HudPartyWindow>();
        _partyWindow.Build(atlas, text);

        // 12. Trade window (KeepPanel/TradeKeepWindow, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED
        _tradeWindow = AddPanel<HudTradeWindow>();
        _tradeWindow.Build(atlas, text);

        // 13. Friend window (FriendPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED
        _friendWindow = AddPanel<HudFriendWindow>();
        _friendWindow.Build(atlas, text);

        // 14. Guild window (GuildAPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED
        _guildWindow = AddPanel<HudGuildWindow>();
        _guildWindow.Build(atlas, text);

        // 15. Quest window (QuestPanel, right-dock 318×732)
        //     spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED
        _questWindow = AddPanel<HudQuestWindow>();
        _questWindow.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 2 E panels (16→21)
        // -------------------------------------------------------------------------

        // 16. MessagePanel — system notice / confirm modal (slot 190, centered 340×190)
        //     spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 190.
        _messagePanel = AddPanel<HudMessagePanel>();
        _messagePanel.Build(atlas);

        // 17. ProductPanel — NPC production / crafting window (slot 230)
        //     spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 230.
        _productWindow = AddPanel<HudProductWindow>();
        _productWindow.Build(atlas, text);

        // 18. EmoticonPanel — emote / chat-emoticon picker (+0x370)
        //     spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — MainWindow +0x370.
        _emoticonWindow = AddPanel<HudEmoticonWindow>();
        _emoticonWindow.Build(atlas, ctx);

        // 19. TenderInfoPanel — consignment-purchase / info confirm (slot 118, 512×595)
        //     spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 118.
        _tenderWindow = AddPanel<HudTenderWindow>();
        _tenderWindow.Build(atlas, text);

        // 20. CarrierPigeonPanal — mailbox menu (slot 96, top-left 140×195)
        //     + CarrierPigeonReadPanel (slot 98) as sub-window.
        //     spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 96/98.
        _mailWindow = AddPanel<HudMailWindow>();
        _mailWindow.Build(atlas, text);

        // 21. DeliveryPanel — consignment / delivery retrieve box (slot 40, 5×8 grid)
        //     spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 40.
        _deliveryWindow = AddPanel<HudDeliveryWindow>();
        _deliveryWindow.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 3 E panels (22→27 + help overlay)
        // -------------------------------------------------------------------------

        // 22. HudCommandBar — bottom command strip (DefaultMenu, slot 148, always-visible)
        //     SPECIAL WIRING: Wire() called BEFORE Build() per §8.23 recovered build contract.
        //     Wire() injects Toggle* delegates before the button nodes are realized.
        //     spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 148, always-built strip.
        _commandBar = AddPanel<HudCommandBar>();
        _commandBar.Wire(
            ToggleInventory,
            ToggleSkill,
            ToggleQuest,
            ToggleStats,
            ToggleHelp,
            ToggleParty,
            ToggleProduct,
            ToggleRelation // DefaultMenu 4002 → RelationPanel(193) + BuddyRelation(185 interim)
            // spec: ui_system.md §8.28.5 CODE-CONFIRMED — "4002 group-open opens BOTH 185+193"
        );
        // Pass text library so button captions resolve from msg.xdb (ingame.md §14.1).
        // spec: Docs/RE/scenes/ingame.md §14.1 — all localized HUD labels resolve via msg.xdb numeric id.
        _commandBar.Build(atlas, text);

        // 23. HudVendorWindow — NPC vendor / item-shop buy/sell (slot 259)
        //     spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 259.
        _vendorWindow = AddPanel<HudVendorWindow>();
        _vendorWindow.Build(atlas, text);

        // 24. HudHelpOverlay — full-screen help image (direct MainMaster member, not slot entry)
        //     spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.
        //     Note: NOT a service-slot entry; direct member of the root HUD window.
        _helpOverlay = AddPanel<HudHelpOverlay>();
        _helpOverlay.Build(atlas);

        // 25. HudAnnouncePanel — scrolling announce banner (slot 221)
        //     spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 221.
        _announcePanel = AddPanel<HudAnnouncePanel>();
        _announcePanel.Build();

        // 26. HudErrorPanel — timed floating notice/error modal (slot 168)
        //     SPECIAL WIRING: SetAnnounceDelegate() called AFTER Build() — AnnouncePanel (slot 25)
        //     must already exist (it does — added at step 25 above). Order is load-bearing.
        //     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 168.
        _errorPanel = AddPanel<HudErrorPanel>();
        _errorPanel.Build();
        // Wire the AnnouncePanel delegate (§8.25.1 — "forwards banner to AnnouncePanel when present")
        _errorPanel.SetAnnounceDelegate(_announcePanel);

        // 27. HudPetPanel — player-couple/pair-relation window (slot 194, NOT creature-pet)
        //     spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 194.
        _petPanel = AddPanel<HudPetPanel>();
        _petPanel.Build(atlas, text);

        // -------------------------------------------------------------------------
        // HUD-II Wave 4 E panels (28→33)
        // -------------------------------------------------------------------------

        // 28. HudStorageWindow — player storage/warehouse (KeepPanel, slot 191, 60-cell grid)
        //     Opened ONLY via KeepNpcPanel sel 1 + C2S 2/142. No hotkey.
        //     DISTINCT from HudTradeWindow (§8.13 TradeKeepWindow).
        //     spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED.
        _storageWindow = AddPanel<HudStorageWindow>();
        _storageWindow.Build(atlas, text);

        // 29. HudKeepNpcDialog — NPC storage/keep dialog menu (KeepNpcPanel, slot 152)
        //     SPECIAL WIRING: Wire() called BEFORE Build() — lambdas capture _storageWindow and
        //     _vendorWindow, which must both be added (steps 28 and 23) before this step. Order
        //     is load-bearing: _storageWindow at step 28, _vendorWindow at step 23.
        //     Opened by world NPC-click dispatcher when NPC is KIND 9.
        //     spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED.
        _keepNpcDialog = AddPanel<HudKeepNpcDialog>();
        _keepNpcDialog.Wire(
            () => _storageWindow?.Open(), // sel 1 → KeepPanel §8.27.3
            () => _vendorWindow?.Open() // sel 0 → NPC dialog (vendor in absence of dedicated dialog window)
        );
        _keepNpcDialog.Build(atlas);

        // 30. HudStallListWindow — personal-stall market list (StallListPanel, slot 228, key `l`)
        //     spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED.
        _stallListWindow = AddPanel<HudStallListWindow>();
        _stallListWindow.Build(atlas, text);

        // 31. HudGuildDiplomacyWindow — guild-diplomacy roster (BroodWarListPanel, slot 235, key `u`)
        //     spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED.
        _guildDiplomacyWindow = AddPanel<HudGuildDiplomacyWindow>();
        _guildDiplomacyWindow.Build(atlas);

        // 32. HudGuildWarInfoWindow — guild-war info display, read-only (GuildWarInfoPanel, slot 224, key `j`)
        //     spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED.
        _guildWarInfoWindow = AddPanel<HudGuildWarInfoWindow>();
        _guildWarInfoWindow.Build(atlas);

        // 33. HudRelationPanel — relation/teacher/fate window (RelationPanel, slot 193)
        //     DefaultMenu 4002 opens BOTH slot 185 (BuddyRelation, TODO) + this slot 193 together.
        //     spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED.
        _relationPanel = AddPanel<HudRelationPanel>();
        _relationPanel.Build(atlas, text);

        GD.Print("[HudMaster] Build complete — ~34 panels total " +
                 "(1 PlayerStatus/slot15 + 10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E, + HelpOverlay member). " +
                 "PlayerStatusPanel(§2.1/slot15): HP/MP/Stamina fills + condition bar + portrait + level label. " +
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
                 "spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel slot 35 (binary-won; 'slot 177' REFUTED §1.9.4). " +
                 "spec: Docs/RE/specs/ui_system.md §8.17/§8.18/§8.19/§8.20/§8.21/§8.22/§8.23/§8.24/§8.25/§8.26 CODE-CONFIRMED. " +
                 "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 CODE-CONFIRMED.");
    }

    /// <summary>
    ///     Reconfigure pass: sets text/sound/flags on already-built panels.
    ///     Mirrors the per-GameState reconfigure routine (assigns no rectangles).
    ///     spec: Docs/RE/specs/ui_hud_layout.md §0.1 — reconfigure routine (text/sound/flags only).
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
    ///     Binds all panels to the Application HUD event hub.
    ///     Call after Build and after the node is in the scene tree.
    /// </summary>
    public void BindHub(ClientContext ctx)
    {
        var hub = ctx.HudEventHub;
        _playerStatusPanel?.BindHub(hub);
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
            "[HudMaster] BindHub: ~34 panels connected (1 PlayerStatus/slot15 + 10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E). " +
            "Party hub wired (stub). Wave-3/4 panels have no hub channel yet " +
            "(TODO world-campaign: global notice sink, 5/53 pair-relation, S2C 4/74/4/81/5/73, relation roster push). " +
            "spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel slot 35 (binary-won; 'slot 177' REFUTED §1.9.4). " +
            "spec: Docs/RE/specs/ui_system.md §8.12 — PartyPanel hub stub (TODO world-campaign). " +
            "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 — Wave-4 TODO hubs.");
    }

    // -----------------------------------------------------------------------
    // Builder helper
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Instantiates a panel of type <typeparamref name="T" />, calls AddChild() to place it
    ///     in the scene at the current tail position, and returns the instance for the caller to
    ///     assign to its <c>_field</c>.
    ///     <para>
    ///         This is the uniform single-step of the builder pattern: the one line that was
    ///         previously three lines (new → AddChild → assign). Callers still assign the returned
    ///         value to their private field so that Input/Toggles partials can reference it — no
    ///         field is dropped or renamed.
    ///     </para>
    ///     <para>
    ///         AddChild insertion order == paint/z-order (back-to-front). The invariant is
    ///         preserved because Build() calls AddPanel in the canonical sequence (1→33) and
    ///         AddChild is always called inside this helper, never skipped.
    ///     </para>
    /// </summary>
    private T AddPanel<T>() where T : Control, new()
    {
        var panel = new T();
        AddChild(panel);
        return panel;
    }
}