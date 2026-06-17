// Ui/Hud/HudMaster.cs
//
// The in-game (state 5) HUD master — orchestrator of all core HUD panels.
//
// Faithful counterpart of the recovered MainMaster service-slot model:
//   - Two-pass build: geometry pass (sizes/positions from the HUD-build routine),
//     then per-state reconfigure (text/sound/flags from the reconfigure routine).
//   - 9 core panels built here; the target frame is OMITTED (HUD-II follow-up — real
//     OtherInfo/MopGagePanel/GagePanel class family not recovered).
//   - All chrome from the corrected uitex atlases via HudAtlasLibrary.
//   - Icons via HudIconLibrary; text via HudTextLibrary.
//   - Graceful-null offline: GD.PrintErr + skip, no fake chrome.
//
// spec: Docs/RE/specs/ui_hud_layout.md §0 — MainMaster service-slot model, three routines.
// spec: Docs/RE/specs/runtime_singletons.md §3.10 — MainMaster object layout.
// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — 178 slot stores, HUD-build routine.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — in-game uitex id→DDS binding table.

using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game HUD master. Builds and owns all 9 core panels.
///
/// <para>PASSIVE: zero game logic. Reads Application catalogues and channels; emits use-case calls.</para>
///
/// <para>Two distinct passes (matching the recovered MainMaster build contract):
/// <list type="number">
///   <item>Geometry pass in <see cref="Build"/> — allocates panels with their confirmed rects.</item>
///   <item>Reconfigure pass in <see cref="Reconfigure"/> — sets text/sound/flags without touching rects.</item>
/// </list></para>
///
/// <para>HUD-II deferred: the real selected-target plate (OtherInfo/MopGagePanel/GagePanel) is NOT
/// built here. Slot 135 is UpgradeProcessPanel, not a target plate. See §5.5a note.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §0 — MainMaster three-routine model.
/// spec: Docs/RE/specs/ui_hud_layout.md §0.1 — 178 distinct slot stores (slot set confirmed).
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

    // HUD-II: target plate not yet recovered
    // // HUD-II: real target plate not yet recovered — OtherInfo/MopGagePanel/GagePanel family

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

        // HUD-II: real target plate not yet recovered
        GD.Print("[HudMaster] Build complete — 9 core panels allocated. " +
                 "HUD-II deferred: target frame (OtherInfo/MopGagePanel/GagePanel not recovered). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.5a");
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
        GD.Print("[HudMaster] BindHub: all panels connected to IHudEventHub.");
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

    public override void _Process(double delta)
    {
        // Per-frame hub drain delegated to individual panels; HudMaster owns no direct channel reads.
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key) return;
        if (!key.Pressed || key.Echo) return;

        // HUD UI toggle keys. World/camera input handled by godot-input-engineer's InputRouter.
        // spec: Docs/RE/specs/input_ui.md §3a / §15 — I, K, C are UI toggle keys.
        switch (key.Keycode)
        {
            case Key.I:
                // Inventory toggle — slots 158+159 (inventory+skill windows, per spec §8.10.1).
                // spec: Docs/RE/specs/ui_system.md §8.10.1 — "[I] toggles slots 158+159".
                ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.K:
                // Skill book toggle.
                // spec: Docs/RE/specs/ui_system.md §15 — K = skill window toggle.
                ToggleSkill();
                GetViewport().SetInputAsHandled();
                break;

            case Key.C:
                // Character stats toggle.
                // spec: Docs/RE/formats/misc_data.md §5 — discript.sc category 102 "(C)".
                ToggleStats();
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
