// Ui/Hud/HudProductWindow.cs
//
// In-game ProductPanel — NPC production / crafting window (slot 230).
//
// NOT a vendor buy/sell panel. This is the CRAFTING / ITEM-MAKE panel. The buy/sell vendor
// is the distinct KeepNpcPanel family. spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED role.
//
// Placement: master HUD child; root window origin set by master-window machinery (debugger-pending).
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 230, "HUD child; child rects panel-local".
//
// Atlas binding (CODE-CONFIRMED):
//   itemshop.dds      — main window chrome / list backdrop
//   product.dds       — recipe grid cells, buttons
//   itemshoppopup.dds — popup / detail sub-panels
//   buywindow.dds     — order / confirm sub-window chrome
//   spec: Docs/RE/specs/ui_system.md §8.18.1 CODE-CONFIRMED.
//
// Recipe grid (CODE-CONFIRMED):
//   4 cols × 2 rows = 8 recipe cells.
//   X cols: {29, 212, 395, 578}, Y rows: {172, 364}.
//   Each cell: frame-button + name-label + two value-labels + icon + make-button.
//   spec: Docs/RE/specs/ui_system.md §8.18.2 CODE-CONFIRMED.
//
// Action map (CODE-CONFIRMED, selected):
//   0..7   = per-cell select (8 cells)
//   8..15  = per-cell buy/make (low)
//   16     = close button
//   36     = make/confirm button (open/commit production order)
//   90     = tip-panel confirm → emits C2S CmsgProductBuy (2/151) selector=200
//   On open: emits CmsgProductBuy (2/151) selector=0 to request recipe list.
//   spec: Docs/RE/specs/ui_system.md §8.18.3 CODE-CONFIRMED / §8.18.5 CODE-CONFIRMED.
//
// Captions — msg.xdb ids (CODE-CONFIRMED):
//   45002 (price), 45004 (have-count), 45011..45014 (detail field labels),
//   714 / 729 / 744 (per-recipe production-state captions).
//   spec: Docs/RE/specs/ui_system.md §8.18.4 CODE-CONFIRMED.
//
// Network:
//   Outbound: C2S CmsgProductBuy (2/151), 1-byte selector body.
//     spec: Docs/RE/packets/ (opcodes.md + packets/).
//   Inbound:  S2C SmsgShopPageUpdate (3/8), 4-byte money value.
//     spec: Docs/RE/specs/ui_system.md §8.18.5 — "recipe list refresh driven by 3/8".
//   No IApplicationUseCases method for ProductBuy exists → all sends are TODO(world-campaign).
//
// PASSIVE: zero game logic. Recipe selection is local (list filter). Network sends are use-case
//   calls (stubbed). 3D item-preview is a placeholder Control (world-campaign wires the canvas).

using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game production / crafting window (ProductPanel, slot 230).
///
/// <para>PASSIVE: recipe selection is local UI state. Network sends are TODO stubs.
/// No game-rule math; no domain mutation.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 230.
/// </summary>
public sealed partial class HudProductWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // -------------------------------------------------------------------------

    // Recipe grid layout — 4 cols × 2 rows = 8 cells.
    // spec: ui_system.md §8.18.2 CODE-CONFIRMED — X={29,212,395,578} Y rows {172,364}
    private static readonly int[] RecipeCellX = { 29, 212, 395, 578 }; // spec: §8.18.2
    private static readonly int[] RecipeCellY = { 172, 364 }; // spec: §8.18.2
    private const int RecipeCellCount = 8; // spec: §8.18.2 — 4-col × 2-row = 8 cells

    // msg.xdb caption ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.18.4 CODE-CONFIRMED
    private const int MsgPrice = 45002; // spec: §8.18.4
    private const int MsgHaveCount = 45004; // spec: §8.18.4
    private const int MsgDetailLbl0 = 45011; // spec: §8.18.4
    private const int MsgDetailLbl1 = 45012; // spec: §8.18.4
    private const int MsgDetailLbl2 = 45013; // spec: §8.18.4
    private const int MsgDetailLbl3 = 45014; // spec: §8.18.4
    private const int MsgProdState0 = 714; // spec: §8.18.4 — per-recipe production-state caption 0
    private const int MsgProdState1 = 729; // spec: §8.18.4 — per-recipe production-state caption 1
    private const int MsgProdState2 = 744; // spec: §8.18.4 — per-recipe production-state caption 2

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _selectedRecipe = -1; // local selection; -1 = none

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the ProductPanel with its 4×2 recipe grid and sub-panel shells.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 230.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudProductWindow";

        // The master-window machinery places the root origin — debugger-pending.
        // We use a centered anchor as a best-effort placeholder.
        // spec: ui_hud_layout.md §5.13 — "master-window placed; child rects panel-local (§8.18)".
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        // Approximate size from sub-panel rects; exact root size = debugger-pending.
        // spec: ui_system.md §8.18.2 — item-preview/order panel (20,0..) 781×630 is the largest sub.
        OffsetLeft = -400f;
        OffsetTop = -350f;
        OffsetRight = 400f;
        OffsetBottom = 350f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.40f, 0.20f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Title label
        var title = new Label
        {
            Name = "Title",
            Text = "Production / Crafting", // placeholder — real text from msg.xdb via HudTextLibrary
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(title);

        // 3D item-preview placeholder
        // TODO(world-campaign): replace with live GUCanvas3D equivalent (ArrayMesh preview).
        // spec: ui_system.md §8.18.2 — "3D item preview/order panel (20,0..) 781×630"
        var previewPlaceholder = new ColorRect
        {
            Name = "ItemPreview3D_TODO",
            Color = new Color(0.10f, 0.10f, 0.18f, 0.7f),
            Position = new Vector2(20f, 40f),
            Size = new Vector2(120f, 80f),
        };
        AddChild(previewPlaceholder);
        var previewLbl = new Label
        {
            Name = "PreviewLbl",
            Text = "// TODO(world-campaign): live craft preview",
            Position = new Vector2(20f, 42f),
            Size = new Vector2(200f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(previewLbl);

        // 4×2 recipe grid (8 cells)
        // spec: ui_system.md §8.18.2 — X={29,212,395,578} Y rows {172,364}
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int cellIdx = row * 4 + col;
                int cellX = RecipeCellX[col]; // spec: §8.18.2
                int cellY = RecipeCellY[row]; // spec: §8.18.2

                // Cell frame button (action id = cell index, 0..7)
                // spec: ui_system.md §8.18.3 — actions 0..7 = per-cell select
                int capturedIdx = cellIdx;
                var cellBtn = new Button
                {
                    Name = $"RecipeCell{cellIdx}",
                    Text = $"[Recipe {cellIdx}]", // placeholder — populated from recipe list
                    Position = new Vector2(cellX, cellY),
                    Size = new Vector2(160f, 40f),
                    MouseFilter = MouseFilterEnum.Stop,
                };
                cellBtn.Pressed += () => OnRecipeSelect(capturedIdx);
                AddChild(cellBtn);

                // Make button (action id = cellIdx + 8, per §8.18.3)
                // spec: ui_system.md §8.18.3 — actions 8..15 = per-cell buy/make (low)
                var makeBtn = new Button
                {
                    Name = $"MakeBtn{cellIdx}",
                    Text = "Make",
                    Position = new Vector2(cellX + 110f, cellY + 45f),
                    Size = new Vector2(50f, 20f),
                    MouseFilter = MouseFilterEnum.Stop,
                };
                int capturedMakeIdx = cellIdx + 8;
                makeBtn.Pressed += () => OnMakeAction(capturedMakeIdx);
                AddChild(makeBtn);
            }
        }

        // Detail field labels from msg.xdb (CODE-CONFIRMED ids)
        // spec: ui_system.md §8.18.4 — 45011..45014
        int[] detailIds = { MsgDetailLbl0, MsgDetailLbl1, MsgDetailLbl2, MsgDetailLbl3 };
        for (int i = 0; i < detailIds.Length; i++)
        {
            string caption = text?.GetCaption(detailIds[i], $"[msg {detailIds[i]}]") ?? $"[msg {detailIds[i]}]";
            var dlbl = new Label
            {
                Name = $"DetailLbl{i}",
                Text = caption,
                Position = new Vector2(20f, 40f + i * 22f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(dlbl);
        }

        // Quantity textbox (action 70, max length 16)
        // spec: ui_system.md §8.18.3 — "Quantity textbox (action 70, max length 16) with +/- adjustment"
        var qtyBox = new LineEdit
        {
            Name = "QtyTextbox",
            Text = "1",
            MaxLength = 16, // spec: §8.18.3 — max length 16
            Position = new Vector2(20f, 510f),
            Size = new Vector2(80f, 24f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(qtyBox);

        // Confirm / Make button (action 36)
        // spec: ui_system.md §8.18.3 — "action 36 = make / confirm button (open/commit production order)"
        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Confirm Order",
            Position = new Vector2(110f, 510f),
            Size = new Vector2(120f, 30f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        confirmBtn.Pressed += OnConfirmOrder;
        AddChild(confirmBtn);

        // Close button (action 16)
        // spec: ui_system.md §8.18.3 — "action 16 = bottom close button"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(760f, 10f),
            Size = new Vector2(24f, 24f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        // Recipe list stub
        // TODO(capture): populate recipe list from inbound SmsgShopPageUpdate (3/8).
        var listStub = new Label
        {
            Name = "RecipeListStub",
            Text = "// TODO(capture): recipe list from S2C 3/8 SmsgShopPageUpdate",
            Position = new Vector2(10f, 660f),
            Size = new Vector2(780f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(listStub);

        GD.Print("[HudProductWindow] Built — ProductPanel slot 230 (4×2 recipe grid, qty textbox, " +
                 "3D preview placeholder, action 36/90/0..15). " +
                 "Outbound C2S 2/151 = TODO(world-campaign). Recipe list = TODO(capture: 3/8). " +
                 "spec: Docs/RE/specs/ui_system.md §8.18 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnRecipeSelect(int cellIdx)
    {
        // spec: ui_system.md §8.18.3 — action 0..7 = select recipe slot, fill detail labels
        _selectedRecipe = cellIdx;
        GD.Print($"[HudProductWindow] Recipe select: cell {cellIdx}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.18.3 — action 0..7.");
    }

    private void OnMakeAction(int actionId)
    {
        // spec: ui_system.md §8.18.3 — action 8..15 = add/queue recipe (mode 1)
        GD.Print($"[HudProductWindow] Make action {actionId}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.18.3 — actions 8..15.");
        // TODO(world-campaign): IApplicationUseCases.ProductBuyAsync (C2S 2/151, selector=0)
        // spec: Docs/RE/specs/ui_system.md §8.18.5 — "2/151 selector body = 0 on open"
    }

    private void OnConfirmOrder()
    {
        // spec: ui_system.md §8.18.3 — action 36 = open/commit production order
        // spec: ui_system.md §8.18.5 — action 90 emits C2S 2/151 selector = 200 (0xC8)
        GD.Print("[HudProductWindow] Confirm order (action 36/90). " +
                 "TODO(world-campaign): IApplicationUseCases.ProductBuyAsync(selector=200). " +
                 "spec: Docs/RE/specs/ui_system.md §8.18.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // spec: Docs/RE/specs/ui_system.md §8.18.5 — "opened from DefaultMenu radial action 4013"
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the production/crafting window.
    /// On open emits C2S CmsgProductBuy (2/151) selector=0 to request the recipe list.
    /// spec: Docs/RE/specs/ui_system.md §8.18.5 CODE-CONFIRMED.
    /// TODO(world-campaign): IApplicationUseCases.ProductBuyAsync when method is exposed.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        bool wasOpen = _open;
        _open = forceState ?? !_open;
        Visible = _open;

        if (_open && !wasOpen)
        {
            // On open: request current production list / money.
            // spec: ui_system.md §8.18.5 — "emits C2S CmsgProductBuy (2/151) selector body = 0 to request list"
            GD.Print("[HudProductWindow] Opened → TODO(world-campaign): emit C2S 2/151 selector=0. " +
                     "spec: Docs/RE/specs/ui_system.md §8.18.5 CODE-CONFIRMED.");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}