// Ui/Hud/HudCommandBar.cs
//
// In-game DefaultMenu — persistent horizontal bottom command bar (slot 148).
//
// This is the COMMAND STRIP at the bottom of the screen, NOT a radial menu. The name
// "DefaultMenu" is the legacy class name; slot 191 was REFUTED as DefaultMenu — it is
// KeepPanel. The correct slot is 148 (CODE-CONFIRMED).
//   spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_system.md §8.23.1 — Geometry CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 148 (horizontal strip, always-built).
//
// Geometry (CODE-CONFIRMED):
//   Main bar: W=1024, H=45, bottom-anchored at y=screen_height−60.
//   Sub-strip: (0, 45, 1024, 20), uitex 1, src (0, 1002).
//   Entry buttons: 29×29 at y=10, two x-clusters.
//   Quick-slot buttons: 21×21 at y=14, x={856,898,940}, uitex 26.
//   Mode buttons (attack/peace): 128×46 at x=448, y=0, uitex 1 (mode strip).
//   Collapse toggle: (1008, 30, 11, 11), uitex 1, src (939,809).
//   spec: Docs/RE/specs/ui_system.md §8.23.1 CODE-CONFIRMED.
//
// Action → MainWindow-slot map (CODE-CONFIRMED):
//   4001 → ItemPanel 158 (Inventory), 4002 → RelationPanel 185,
//   4003 → SkillPanel 159, 4004 → QuestPanel 206 (mode 2),
//   4005 → StatusPanel 146, 4011 → HelpPanel (member, slot 322 / button 176),
//   4012 → PartyPanel 220, 4013 → ProductPanel 230 (billing/scene-gated),
//   4022 → warstone 240, 4024 → QuestPanel 206 (mode 7).
//   spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.
//
// Atlas binding (CODE-CONFIRMED uitex ids; DDS file = VFS uitex.txt pending):
//   uitex 1 = main HUD button atlas (bar chrome, all 29×29 entry buttons).
//   uitex 26 = quick-slot button atlas (three 21×21 stance buttons).
//   uitex 4 = secondary chrome (collapse toggle, face panel).
//   uitex 14 = label/text atlas.
//   spec: Docs/RE/specs/ui_system.md §8.23.2 CODE-CONFIRMED.
//
// Bar height (CODE-CONFIRMED): self-expand action 4000 toggles 30 ↔ 254 px.
//   spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED — "vertical bar-height toggle 30↔254".
//
// Captions from msg.xdb (CODE-CONFIRMED):
//   10082 = chat-busy notice (action 4007),
//   45003 = ProductPanel-unavailable (action 4013),
//   2222 = number-format template (expanded-bar label).
//   spec: Docs/RE/specs/ui_system.md §8.23.5 CODE-CONFIRMED.
//
// PASSIVE: zero game logic. Each entry button calls an existing HudMaster Toggle*/Show* method.
// Entries for panels not yet built are logged as TODO(world-campaign).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game bottom command bar (DefaultMenu, slot 148).
///     <para>
///         Persistent strip anchored to the screen bottom. Each button fires a
///         <see cref="HudMaster" /> toggle method, making the HUD fully mouse-navigable
///         (complements the §8.17 keyboard dispatch).
///     </para>
///     <para>PASSIVE: zero game logic. Entry clicks emit HudMaster toggle intents.</para>
///     spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 148.
/// </summary>
public sealed partial class HudCommandBar : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_system.md §8.23.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float BarHeight = 45f; // spec: §8.23.1 — main bar H=45
    private const float SubStripH = 20f; // spec: §8.23.1 — sub-strip H=20
    private const float EntryBtnSize = 29f; // spec: §8.23.1 — entry buttons 29×29
    private const float EntryBtnY = 10f; // spec: §8.23.1 — entry buttons at y=10
    private const float QuickBtnSize = 21f; // spec: §8.23.1 — quick-slot buttons 21×21
    private const float QuickBtnY = 14f; // spec: §8.23.1 — quick-slot buttons at y=14

    // msg.xdb caption ids (CODE-CONFIRMED)
    // spec: Docs/RE/specs/ui_system.md §8.23.5 CODE-CONFIRMED
    private const int MsgChatBusy = 10082; // spec: §8.23.5 — action 4007 chat-busy notice
    private const int MsgProductUnavailable = 45003; // spec: §8.23.5 — action 4013 ProductPanel-unavailable
    private const int MsgNumberFormat = 2222; // spec: §8.23.5 — expanded-bar number-format template

    // Left x-cluster entry-button positions (§8.23.1 approximate — exact VFS/debugger-pending)
    // spec: Docs/RE/specs/ui_system.md §8.23.1 — "left cluster ≈ 218..373"
    private static readonly float[] LeftClusterX = { 218f, 250f, 282f, 314f, 346f };

    // Right x-cluster entry-button positions
    // spec: Docs/RE/specs/ui_system.md §8.23.1 — "right cluster ≈ 622..808"
    private static readonly float[] RightClusterX = { 622f, 654f, 686f, 718f, 750f, 782f };

    // Quick-slot button x positions (§8.23.1 CODE-CONFIRMED)
    // spec: Docs/RE/specs/ui_system.md §8.23.1 — "x={856,898,940}"
    private static readonly float[] QuickSlotX = { 856f, 898f, 940f }; // spec: §8.23.1

    // View state
    private bool _expanded; // bar-height toggle (false = 30 px collapsed, true = 254 px expanded)
    private Panel? _mainBar;
    private Action? _onHelp; // action 4011 → HelpPanel (member, §8.24)

    // -------------------------------------------------------------------------
    // Callback wiring (set by HudMaster.Build via Wire)
    // -------------------------------------------------------------------------

    private Action? _onInventory; // action 4001 → slot 158 ItemPanel
    private Action? _onParty; // action 4012 → slot 220 PartyPanel
    private Action? _onProduct; // action 4013 → slot 230 ProductPanel
    private Action? _onQuest; // action 4004/4024 → slot 206 QuestPanel
    private Action? _onRelation; // action 4002 → slot 185 RelationPanel
    private Action? _onSkill; // action 4003 → slot 159 SkillPanel
    private Action? _onStats; // action 4005 → slot 146 StatusPanel
    private Panel? _subStrip;

    // -------------------------------------------------------------------------
    // Wiring (called by HudMaster.Build)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Wires each entry button to its HudMaster toggle/show method.
    ///     Call BEFORE Build or immediately after to connect the callbacks.
    ///     spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED — entry→action-id→slot map.
    /// </summary>
    public void Wire(
        Action? onInventory,
        Action? onSkill,
        Action? onQuest,
        Action? onStats,
        Action? onHelp,
        Action? onParty,
        Action? onProduct,
        Action? onRelation = null)
    {
        _onInventory = onInventory;
        _onSkill = onSkill;
        _onQuest = onQuest;
        _onStats = onStats;
        _onHelp = onHelp;
        _onParty = onParty;
        _onProduct = onProduct;
        _onRelation = onRelation;
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the bottom command strip at its confirmed bottom-anchor.
    ///     Button captions are resolved from <paramref name="text" /> (msg.xdb id lookup) per §14.1;
    ///     caption ids pending RE are 0-keyed with the legacy string as fallback.
    ///     spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.23.1 CODE-CONFIRMED — geometry.
    ///     spec: Docs/RE/scenes/ingame.md §14.1 — all localized labels resolve via msg.xdb numeric id.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 148, always-built.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary? text = null)
    {
        Name = "HudCommandBar";

        // Bottom-anchored, full width.
        // spec: §8.23.1 — "y = screen_height − 60; W=1024, H=45"
        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetTop = -(BarHeight + SubStripH); // collapsed: bar 45 + sub-strip 20
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;

        // ── Main bar (uitex 1, src (0,957), 1024×45) ──
        // spec: §8.23.1 CODE-CONFIRMED — main bar at (0,0,1024,45), uitex 1, src (0,957)
        _mainBar = new Panel { Name = "MainBar" };
        _mainBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var barStyle = new StyleBoxFlat();
        barStyle.BgColor = new Color(0.08f, 0.06f, 0.04f, 0.92f);
        barStyle.SetBorderWidthAll(1);
        barStyle.BorderColor = new Color(0.55f, 0.40f, 0.10f, 0.9f);
        _mainBar.AddThemeStyleboxOverride("panel", barStyle);
        // TODO(assets): bind uitex 1 src (0, 957) 1024×45 when atlas available.
        // spec: §8.23.1 — uitex 1 "main HUD button atlas" src (0, 957).
        AddChild(_mainBar);

        // ── Sub-strip (uitex 1, src (0,1002), 1024×20) ──
        // spec: §8.23.1 CODE-CONFIRMED — sub-strip at (0,45,1024,20), uitex 1, src (0,1002)
        _subStrip = new Panel { Name = "SubStrip" };
        _subStrip.AnchorLeft = 0f;
        _subStrip.AnchorTop = 0f;
        _subStrip.AnchorRight = 1f;
        _subStrip.AnchorBottom = 0f;
        _subStrip.OffsetTop = BarHeight;
        _subStrip.OffsetBottom = BarHeight + SubStripH;
        var subStyle = new StyleBoxFlat();
        subStyle.BgColor = new Color(0.05f, 0.04f, 0.02f, 0.9f);
        subStyle.SetBorderWidthAll(1);
        subStyle.BorderColor = new Color(0.40f, 0.30f, 0.08f, 0.8f);
        _subStrip.AddThemeStyleboxOverride("panel", subStyle);
        AddChild(_subStrip);

        // ── Left cluster entry buttons ──
        // spec: §8.23.1 — "left cluster ≈ 218..373, 29×29 at y=10"
        // spec: ingame.md §14.1 — all labels resolve via msg.xdb numeric id; caption ids pending RE use 0+fallback.
        // Map: 4001=Inventory, 4002=Relation, 4003=Skill, 4004=Quest, 4005=Status
        // TODO(world-campaign): recover exact caption ids for each button from the msg.xdb scan.
        BuildEntryButton("BtnInventory", LeftClusterX[0], EntryBtnY,
            text?.GetCaption(0, "목록") ?? "목록", 4001, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnRelation", LeftClusterX[1], EntryBtnY,
            text?.GetCaption(0, "관계") ?? "관계", 4002, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnSkill", LeftClusterX[2], EntryBtnY,
            text?.GetCaption(0, "스킬") ?? "스킬", 4003, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnQuest", LeftClusterX[3], EntryBtnY,
            text?.GetCaption(0, "퀘스트") ?? "퀘스트", 4004, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnStatus", LeftClusterX[4], EntryBtnY,
            text?.GetCaption(0, "상태") ?? "상태", 4005, OnAction); // spec: ingame.md §14.1 — id TBD

        // ── Right cluster entry buttons ──
        // spec: §8.23.1 — "right cluster ≈ 622..808, 29×29 at y=10"
        // spec: ingame.md §14.1 — captions via msg.xdb; ids pending RE use 0+fallback.
        // Map: 4011=Help, 4012=Party, 4013=Product, 4022=Warstone, 4009=slot161, 4010=slot164
        BuildEntryButton("BtnHelp", RightClusterX[0], EntryBtnY,
            text?.GetCaption(0, "도움말") ?? "도움말", 4011, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnParty", RightClusterX[1], EntryBtnY,
            text?.GetCaption(0, "파티") ?? "파티", 4012, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnProduct", RightClusterX[2], EntryBtnY,
            text?.GetCaption(0, "제작") ?? "제작", 4013, OnAction); // spec: ingame.md §14.1 — id TBD
        BuildEntryButton("BtnSlot161", RightClusterX[3], EntryBtnY,
            text?.GetCaption(0, "[161]") ?? "[161]", 4009, OnAction); // slot 161 — id TBD
        BuildEntryButton("BtnSlot164", RightClusterX[4], EntryBtnY,
            text?.GetCaption(0, "[164]") ?? "[164]", 4010, OnAction); // slot 164 — id TBD
        BuildEntryButton("BtnWarstone", RightClusterX[5], EntryBtnY,
            text?.GetCaption(0, "전투석") ?? "전투석", 4022, OnAction); // spec: ingame.md §14.1 — id TBD

        // ── Attack/Peace mode buttons (128×46, x=448, y=0) ──
        // spec: §8.23.1 — "attack/peace mode buttons 128×46 at x=448, y=0, uitex 1"
        BuildModeButton("BtnAttackMode", 448f, 0f, 128f, 46f, 4014, OnAction); // attack-mode ON
        BuildModeButton("BtnPeaceMode", 448f + 128f, 0f, 128f, 46f, 4015, OnAction); // peace-mode

        // ── Quick-slot stance buttons (21×21, y=14) ──
        // spec: §8.23.1 — "x={856,898,940}, 21×21 at y=14, uitex 26"
        for (var i = 0; i < QuickSlotX.Length; i++)
        {
            var action = 4019 + i; // 4019/4020/4021 = stance buttons 0/1/2 // spec: §8.23.3
            var capturedI = i;
            BuildQuickSlotButton($"QuickSlot{i}", QuickSlotX[i], QuickBtnY, action, OnAction);
        }

        // ── Collapse toggle (1008, 30, 11×11) ──
        // spec: §8.23.1 — "corner toggle (1008,30,11,11), uitex 1, src (939,809), action 4023"
        var collapseBtn = new Button
        {
            Name = "CollapseToggle",
            Text = "▲",
            Position = new Vector2(1008f - (BarHeight + SubStripH), 30f), // right-relative approx
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        // Anchor right-bottom relative
        collapseBtn.AnchorLeft = 1f;
        collapseBtn.AnchorRight = 1f;
        collapseBtn.AnchorTop = 0f;
        collapseBtn.AnchorBottom = 0f;
        collapseBtn.OffsetLeft = -16f;
        collapseBtn.OffsetRight = -5f;
        collapseBtn.OffsetTop = 30f;
        collapseBtn.OffsetBottom = 41f;
        collapseBtn.Pressed += () => OnAction(4023); // spec: §8.23.3 — action 4023 = collapse toggle
        AddChild(collapseBtn);

        GD.Print("[HudCommandBar] Built — DefaultMenu slot 148 (bottom command strip). " +
                 "5 left-cluster entries (Inventory/Relation/Skill/Quest/Status) + " +
                 "6 right-cluster entries (Help/Party/Product/slot161/slot164/Warstone) + " +
                 "mode buttons + 3 quick-slot buttons + collapse toggle. " +
                 "Entry→slot wiring via HudMaster callbacks. " +
                 "TODO(world-campaign): slot 161, slot 164, warstone (240), relation (185), mode sends. " +
                 "spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Entry-button factory helpers
    // -------------------------------------------------------------------------

    private void BuildEntryButton(string nodeName, float x, float y, string label, int actionId, Action<int> handler)
    {
        var btn = new Button
        {
            Name = nodeName,
            Text = label,
            Position = new Vector2(x, y),
            Size = new Vector2(EntryBtnSize, EntryBtnSize), // spec: §8.23.1 — 29×29
            MouseFilter = MouseFilterEnum.Stop
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }

    private void BuildModeButton(string nodeName, float x, float y, float w, float h, int actionId, Action<int> handler)
    {
        // spec: §8.23.1 — attack/peace mode buttons 128×46 at x=448, y=0, uitex 1
        var btn = new Button
        {
            Name = nodeName,
            Text = actionId == 4014 ? "⚔" : "☮",
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            MouseFilter = MouseFilterEnum.Stop
        };
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }

    private void BuildQuickSlotButton(string nodeName, float x, float y, int actionId, Action<int> handler)
    {
        // spec: §8.23.1 — quick-slot buttons 21×21 at y=14, x={856,898,940}, uitex 26
        var btn = new Button
        {
            Name = nodeName,
            Text = "●",
            Position = new Vector2(x, y),
            Size = new Vector2(QuickBtnSize, QuickBtnSize), // spec: §8.23.1 — 21×21
            MouseFilter = MouseFilterEnum.Stop
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        var captured = actionId;
        btn.Pressed += () => handler(captured);
        _mainBar?.AddChild(btn);
    }

    // -------------------------------------------------------------------------
    // Action dispatcher — mirrors the DefaultMenu vtable slot-6 handler
    // spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        // spec: §8.23.3 — each action opens/toggles its target slot and mutually-closes siblings.
        // spec: §8.23.4 — "each group handler is mutually-exclusive: opens own panel, closes visible siblings".
        switch (actionId)
        {
            case 4000:
                // Self-expand: toggle bar height 30↔254 px.
                // spec: §8.23 — "vertical bar-height toggle (30 px collapsed ↔ 254 px expanded)"
                _expanded = !_expanded;
                // In our port: just toggle the sub-strip visibility as a proxy.
                if (_subStrip != null) _subStrip.Visible = _expanded;
                GD.Print($"[HudCommandBar] action 4000 — bar expand: {_expanded}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23 CODE-CONFIRMED.");
                break;

            case 4001:
                // Inventory (ItemPanel, slot 158). spec: §8.23.3 — "4001 → 158 toggle; close siblings"
                _onInventory?.Invoke();
                GD.Print("[HudCommandBar] action 4001 → Inventory (slot 158). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4002:
                // Relation/Buddy panel (slot 185). spec: §8.23.3 — "4002 → slot 185"
                // TODO(world-campaign): _onRelation?.Invoke() when RelationPanel built.
                _onRelation?.Invoke();
                GD.Print("[HudCommandBar] action 4002 → RelationPanel slot 185 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4003:
                // Skill (SkillPanel, slot 159). spec: §8.23.3 — "4003 → 159 toggle; close siblings"
                _onSkill?.Invoke();
                GD.Print("[HudCommandBar] action 4003 → Skill (slot 159). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4004:
                // Quest tracker mode 2 (QuestPanel, slot 206). spec: §8.23.3 — "4004 → 206 mode 2"
                _onQuest?.Invoke();
                GD.Print("[HudCommandBar] action 4004 → Quest (slot 206 mode 2). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4005:
                // Status group (StatusPanel, slot 146). spec: §8.23.3 — "4005 → slot 146; close siblings"
                _onStats?.Invoke();
                GD.Print("[HudCommandBar] action 4005 → StatusPanel (slot 146). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4006:
            case 4008:
                // Relation/focus group (slot 193). spec: §8.23.3 — "4006/4008 → slot 193"
                // TODO(world-campaign): wire to RelationPanel slot 193 when built.
                GD.Print($"[HudCommandBar] action {actionId} → RelationPanel slot 193 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4007:
                // Attack-stance alt (msg 10082 on chat-busy). spec: §8.23.3
                // TODO(world-campaign): chat-busy check → msg.xdb 10082 notice.
                _onStats?.Invoke();
                GD.Print("[HudCommandBar] action 4007 → status group (chat-busy: msg 10082 — TODO). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4009:
                // Generic visibility toggle for slot 161. spec: §8.23.3 — "4009 → slot 161"
                // TODO(world-campaign): toggle slot 161 when panel is built.
                GD.Print("[HudCommandBar] action 4009 → slot 161 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4010:
                // Generic visibility toggle for slot 164. spec: §8.23.3 — "4010 → slot 164"
                // TODO(world-campaign): toggle slot 164 when panel is built.
                GD.Print("[HudCommandBar] action 4010 → slot 164 — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4011:
                // Help toggle — HelpPanel member + docked help button slot 176.
                // spec: §8.23.3 — "4011 → slot 322 (HelpPanel) / 176 (button); press/unpress docked help button"
                _onHelp?.Invoke();
                GD.Print("[HudCommandBar] action 4011 → HelpPanel (member §8.24) / button slot 176. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4012:
                // Party (PartyPanel, slot 220). spec: §8.23.3 — "4012 → 220; close siblings"
                _onParty?.Invoke();
                GD.Print("[HudCommandBar] action 4012 → Party (slot 220). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4013:
                // ProductPanel (slot 230, billing+scene-gated). spec: §8.23.3 — "4013 → 230 (billing-gated)"
                // TODO(world-campaign): billing gate (msg.xdb 45003 on fail) + scene-state≠11 gate.
                _onProduct?.Invoke();
                GD.Print("[HudCommandBar] action 4013 → ProductPanel (slot 230). " +
                         "TODO(world-campaign): billing+scene gates (msg 45003). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4014:
                // Attack-mode OFF. spec: §8.23.3 — "4014 → attack-mode-off + FX"
                // TODO(world-campaign): emit attack-mode-off C2S + refresh weapon FX.
                GD.Print("[HudCommandBar] action 4014 → attack-mode OFF — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4015:
                // Attack-mode ON. spec: §8.23.3 — "4015 → attack-mode-on"
                // TODO(world-campaign): emit attack-mode-on C2S.
                GD.Print("[HudCommandBar] action 4015 → attack-mode ON — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4019:
            case 4020:
            case 4021:
                // Quick-slot / stance dispatcher 0/1/2. spec: §8.23.3 — "4019/4020/4021 stance buttons"
                // TODO(world-campaign): per-index stance dispatcher.
                GD.Print($"[HudCommandBar] action {actionId} → quick-slot stance {actionId - 4019} — " +
                         "TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4022:
                // Warstone panel (slot 240). spec: §8.23.3 — "4022 → slot 240"
                // TODO(world-campaign): toggle warstone when built.
                GD.Print("[HudCommandBar] action 4022 → warstone (slot 240) — TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4023:
                // Collapse toggle. spec: §8.23.3 — "4023 → toggle layout/expand sub-state + relayout"
                _expanded = !_expanded;
                if (_subStrip != null) _subStrip.Visible = _expanded;
                GD.Print($"[HudCommandBar] action 4023 — collapse toggle: {_expanded}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            case 4024:
                // Quest tracker mode 7 (QuestPanel, slot 206). spec: §8.23.3 — "4024 → 206 mode 7"
                _onQuest?.Invoke();
                GD.Print("[HudCommandBar] action 4024 → Quest (slot 206 mode 7). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;

            default:
                GD.Print($"[HudCommandBar] action {actionId} — unhandled (residual). " +
                         "spec: Docs/RE/specs/ui_system.md §8.23.3 CODE-CONFIRMED.");
                break;
        }
    }
}