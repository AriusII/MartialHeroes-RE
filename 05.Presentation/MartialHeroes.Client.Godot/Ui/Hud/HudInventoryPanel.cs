// Ui/Hud/HudInventoryPanel.cs
//
// In-game inventory bag — `ItemPanel` (key I toggle, right-anchored).
//
// Placement (CODE-CONFIRMED):
//   W = 318, H = 732, X = screen_width + 318, Y = 0 (right-anchored, slides in).
//   spec: Docs/RE/specs/ui_hud_layout.md §1.1 / §5.3 CODE-CONFIRMED.
//
// Item grid — ItemPanel 8×5 = 40 cells (CODE-CONFIRMED 2026-06-17):
//   - 8 columns × 5 rows = 40 cells; each cell 38 × 38 px.
//   - Cell pitch = +38 px on both axes (flush, no gutter).
//   - Cell action ids: 0..39.
//   - 318 × 623 backdrop body.
//   spec: Docs/RE/specs/ui_system.md §8.10.1 CODE-CONFIRMED.
//
// Equip sub-grid: 20 cells (action ids 50..69), 38×38 buttons.
//   spec: Docs/RE/specs/ui_system.md §8.10.1 CODE-CONFIRMED.
//
// Equipment paperdoll: hand-placed per-slot (explicit per-slot x,y).
//   spec: Docs/RE/specs/ui_system.md §8.10.1 CODE-CONFIRMED.
//
// Chrome atlas: data/ui/inventwindow.dds (uitex 2).
//   spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 2 = inventwindow.dds.
//   Cell overlays: uitex 14 (count/qty), 69 (state), 71 (rarity), 78 (highlight).
//
// Toggle: hotkey I — toggled together with the skill panel (slots 158+159).
//   spec: Docs/RE/specs/ui_system.md §8.10.1 — "[I] toggles slots 158+159 + sound 862020102".
//
// PASSIVE: reads ClientContext.ItemCatalogue for item names/icons; zero game logic.
//   Drag/drop intent → use-case call (no local mutation).
//   TODO(world-campaign): live inventory-contents event wiring.

using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game inventory bag (ItemPanel). 318×732 right-anchored; key-I toggles it.
///
/// <para>PASSIVE: reads item catalogue for display; emits equip/move intents as use-case calls.
/// Zero game logic — no validation, no optimistic state mutation.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §1.1 / §5.3 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_system.md §8.10.1 — ItemPanel 8×5/40-cell grid CODE-CONFIRMED.
/// </summary>
public sealed partial class HudInventoryPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float InvW = 318f; // spec: ui_hud_layout.md §1.1 — W=318 CODE-CONFIRMED

    private const float InvH = 732f; // spec: ui_hud_layout.md §1.1 — H=732 CODE-CONFIRMED
    // X = screen_width + 318 → off-screen right anchor until revealed.
    // spec: ui_hud_layout.md §1.1 — "Anchor X = screen_width + 318 (right-anchored)"

    // Item grid constants (CODE-CONFIRMED §8.10.1)
    private const int GridCols = 8; // spec: ui_system.md §8.10.1 — 8 columns
    private const int GridRows = 5; // spec: ui_system.md §8.10.1 — 5 rows = 40 cells
    private const int CellSide = 38; // spec: ui_system.md §8.10.1 — 38×38 px, +38 pitch
    private const int GridCellCount = GridCols * GridRows; // = 40

    private const int EquipCellCount = 20; // spec: ui_system.md §8.10.1 — equip sub-grid 20 cells

    // Action ids: main grid 0..39, equip 50..69.
    private const int ActionIdEquipStart = 50; // spec: ui_system.md §8.10.1

    // Chrome atlas
    // spec: ui_system.md §8.6.1 — uitex 2 = data/ui/inventwindow.dds
    private const int InvTexId = 2; // spec: ui_system.md §8.6.1

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private readonly TextureRect[] _gridCells = new TextureRect[GridCellCount];
    private readonly TextureRect[] _equipCells = new TextureRect[EquipCellCount];

    // -------------------------------------------------------------------------
    // View state (not domain state)
    // -------------------------------------------------------------------------

    private bool _visible;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the 318×732 right-anchored panel with the 8×5 item grid.
    /// Initially hidden (off-screen); revealed by key-I toggle.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "X=screen_width+318, Y=0, W=318, H=732".
    /// spec: Docs/RE/specs/ui_system.md §8.10.1 — ItemPanel 8×5 grid CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, ClientContext ctx)
    {
        Name = "HudInventoryPanel";

        // Right-anchored, off-screen until toggled.
        // screen_width + 318 → AnchorLeft=1, OffsetLeft=+318 (past the right edge)
        // spec: ui_hud_layout.md §1.1 CODE-CONFIRMED
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = InvW; // +318 beyond right edge → off-screen
        OffsetTop = 0f;
        OffsetRight = InvW + InvW; // +636
        OffsetBottom = InvH;
        Visible = false; // hidden by default; key-I reveals it
        MouseFilter = MouseFilterEnum.Stop;

        // Load chrome texture (inventwindow.dds, uitex 2)
        // spec: ui_system.md §8.6.1 — uitex 2 = data/ui/inventwindow.dds
        Texture2D? chromeTex = atlas.GetById(InvTexId);
        if (chromeTex is null)
            GD.PrintErr("[HudInventoryPanel] inventwindow.dds (uitex 2) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.6.1.");

        // Window backdrop (318×732 background)
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.92f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.2f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Chrome overlay from inventwindow.dds
        if (chromeTex is not null)
        {
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = chromeTex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chrome);
        }

        // Title label
        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "인벤토리", // CP949 — "Inventory" (will be replaced by msg.xdb if available)
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        titleLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        titleLbl.OffsetBottom = 30f;
        AddChild(titleLbl);

        // 8×5 item grid — 40 cells, 38×38 each, flush +38 pitch
        // spec: ui_system.md §8.10.1 CODE-CONFIRMED — "8×5=40 cells, 38×38 px, +38 pitch, action 0..39"
        const int GridStartX = 8; // modest inset
        const int GridStartY = 36; // below header area
        BuildItemGrid(atlas, GridStartX, GridStartY);

        // 20-cell equip sub-grid (action ids 50..69)
        // spec: ui_system.md §8.10.1 CODE-CONFIRMED — "20 cells, 38×38, action ids 50..69"
        // Placed below the main grid
        int equipStartY = GridStartY + GridRows * CellSide + 10;
        BuildEquipGrid(atlas, GridStartX, equipStartY);

        // Equipment paperdoll placeholder (hand-placed per-slot; exact positions not recovered)
        // spec: ui_system.md §8.10.1 — "hand-placed per-slot (x,y) coordinates"
        BuildPaperdoll(GridStartX, equipStartY + EquipCellCount / 4 * CellSide + 10);

        GD.Print("[HudInventoryPanel] Built — 318×732 right-anchored (off-screen). " +
                 "8×5=40-cell grid (38×38, +38 pitch, actions 0..39). " +
                 "20-cell equip sub-grid (actions 50..69). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §1.1; ui_system.md §8.10.1 CODE-CONFIRMED.");
    }

    private void BuildItemGrid(HudAtlasLibrary atlas, int startX, int startY)
    {
        // spec: ui_system.md §8.10.1 CODE-CONFIRMED — 8×5 main grid, 38×38 cells, +38 pitch
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                int idx = row * GridCols + col;
                int x = startX + col * CellSide;
                int y = startY + row * CellSide;

                var cell = new TextureRect
                {
                    Name = $"ItemCell{idx}",
                    Position = new Vector2(x, y),
                    Size = new Vector2(CellSide, CellSide),
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Stop,
                };

                // Cell background — dark with border
                // (In the real client the cell bg is a 38×38 slice from inventwindow.dds)
                // TODO(spec): bind correct cell bg sub-rect from inventwindow.dds atlas.
                var cellPanel = new Panel { Name = "CellBg" };
                cellPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                var cs = new StyleBoxFlat();
                cs.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.75f);
                cs.SetBorderWidthAll(1);
                cs.BorderColor = new Color(0.35f, 0.3f, 0.2f, 0.7f);
                cellPanel.AddThemeStyleboxOverride("panel", cs);
                cell.AddChild(cellPanel);

                AddChild(cell);
                _gridCells[idx] = cell;
            }
        }
    }

    private void BuildEquipGrid(HudAtlasLibrary atlas, int startX, int startY)
    {
        // spec: ui_system.md §8.10.1 CODE-CONFIRMED — equip sub-grid 20 cells, 38×38, action 50..69
        for (int i = 0; i < EquipCellCount; i++)
        {
            int col = i % GridCols;
            int row = i / GridCols;
            int x = startX + col * CellSide;
            int y = startY + row * CellSide;

            var cell = new TextureRect
            {
                Name = $"EquipCell{i}",
                Position = new Vector2(x, y),
                Size = new Vector2(CellSide, CellSide),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Stop,
            };

            var cellPanel = new Panel { Name = "CellBg" };
            cellPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var cs = new StyleBoxFlat();
            cs.BgColor = new Color(0.09f, 0.06f, 0.12f, 0.75f);
            cs.SetBorderWidthAll(1);
            cs.BorderColor = new Color(0.4f, 0.25f, 0.45f, 0.7f);
            cellPanel.AddThemeStyleboxOverride("panel", cs);
            cell.AddChild(cellPanel);

            AddChild(cell);
            _equipCells[i] = cell;
        }
    }

    private void BuildPaperdoll(int x, int y)
    {
        // spec: ui_system.md §8.10.1 — "hand-placed equipment paperdoll"
        // Exact per-slot coordinates are NOT recovered (hand-placed = no uniform grid).
        // This placeholder marks the paperdoll area.
        // TODO(spec): recover per-slot paperdoll (x,y) positions from the builder.
        var paperdollPlaceholder = new Label
        {
            Name = "PaperdollPlaceholder",
            Text = "[paperdoll]",
            Position = new Vector2(x, y),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(paperdollPlaceholder);
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds to the HUD event hub.
    /// TODO(world-campaign): wire inventory-contents events when the hub publishes them.
    /// </summary>
    public void BindHub(IHudEventHub hub)
    {
        // IHudEventHub does not yet expose an inventory-contents channel.
        // TODO(world-campaign): add inventory event channel to IHudEventHub; drain here.
        GD.Print("[HudInventoryPanel] BindHub: inventory-contents event wiring deferred (TODO world-campaign).");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the inventory panel visibility and slides it in/out.
    /// Called by HudMaster on key-I press.
    /// spec: ui_system.md §8.10.1 — "[I] toggles slots 158+159 (inventory+skill) together".
    /// </summary>
    public void Toggle()
    {
        _visible = !_visible;

        if (_visible)
        {
            // Slide in: move from off-screen (OffsetLeft=+318) to on-screen (OffsetLeft=−318).
            // spec: ui_hud_layout.md §1.1 — "X=screen_width+318 (off-screen); reveal = X=screen_width−318"
            OffsetLeft = -InvW;
            OffsetRight = 0f;
        }
        else
        {
            // Slide out back to off-screen position.
            OffsetLeft = InvW;
            OffsetRight = InvW + InvW;
        }

        Visible = _visible;
        GD.Print($"[HudInventoryPanel] Toggle → visible={_visible}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §1.1 / ui_system.md §8.10.1.");
    }
}