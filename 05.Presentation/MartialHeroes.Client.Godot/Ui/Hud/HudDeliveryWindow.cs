// Ui/Hud/HudDeliveryWindow.cs
//
// In-game DeliveryPanel — consignment / delivery retrieve box (slot 40).
//
// Placement: master HUD child; placed by master-window machinery (debugger-pending).
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 40, "HUD child; 40-cell 5×8 grid".
//
// Atlas (CODE-CONFIRMED):
//   delivery.dds — main chrome.
//   spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
//
// Item grid (CODE-CONFIRMED):
//   40-slot (5 columns × 8 rows) grid, cell action ids 500..539.
//   Category-7 item tooltips (same path as inventory).
//   spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
//
// Page tabs (CODE-CONFIRMED): actions 573..578 (own/others/sale categories).
// Scrollbar (CODE-CONFIRMED): thumb 583, down 584, up 585.
// Quantity +/- on retrieve confirm: 601 (dec), 602 (inc).
// Select-all / list button: 580.
// View-switch: actions 541..548.
// Claim-prepare: actions 565..572 (stages recipient name + up to 5 item records → InfoPanel confirm).
//   Confirm "Yes" → C2S CmsgDeliveryClaim (2/71).
// spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
//
// Each item record = 16 bytes (item-id, key, two scalars) + owner-id field.
//   Item record owner-id compared to local player id → my-side vs other-side.
//   spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
//
// Caption msg.xdb ids (CODE-CONFIRMED):
//   40010 — generic delivery caption.
//   16005 — "no content".
//   55016 — "no free bag slots when claiming".
//   spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
//
// Network:
//   Outbound: C2S CmsgDeliveryClaim (2/71, 4B) — deferred-confirm.
//     spec: Docs/RE/packets/ — CmsgDeliveryClaim (2/71).
//   Inbound:  S2C delivery list populate — opcode unknown (TODO(capture)).
//   spec: Docs/RE/specs/ui_system.md §8.21.5/§8.21.7 CODE-CONFIRMED / populate residual.
//
// PASSIVE: zero game logic. Claim path emits use-case call (stub; no method exists yet).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game delivery/consignment retrieve box (DeliveryPanel, slot 40).
///
/// <para>PASSIVE: 5×8 item grid (40 cells, action 500..539). Claim flow (actions 565..572)
/// stages items and fires C2S CmsgDeliveryClaim (2/71) via use-case (stub).
/// Delivery list = TODO(capture).</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 40.
/// </summary>
public sealed partial class HudDeliveryWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Item grid layout: 5 cols × 8 rows = 40 cells.
    // spec: ui_system.md §8.21.5 — "40-slot item grid (5 × 8)"
    private const int GridCols = 5; // spec: §8.21.5
    private const int GridRows = 8; // spec: §8.21.5
    private const int GridCellCount = GridCols * GridRows; // spec: §8.21.5 — 40 cells

    // Cell action id base (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "cell action ids 500..539"
    private const int CellActionBase = 500; // spec: §8.21.5

    // Page tab action ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "page tabs: actions 573..578"
    private const int TabActionBase = 573; // spec: §8.21.5
    private const int TabActionCount = 6; // spec: §8.21.5 — 573..578

    // Scrollbar action ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "scrollbar: thumb 583, down 584, up 585"
    private const int ScrollThumb = 583; // spec: §8.21.5
    private const int ScrollDown = 584; // spec: §8.21.5
    private const int ScrollUp = 585; // spec: §8.21.5

    // Quantity controls (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "quantity +/-: 601 (dec), 602 (inc)"
    private const int QtyDec = 601; // spec: §8.21.5
    private const int QtyInc = 602; // spec: §8.21.5

    // Claim-prepare action range (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "claim-prepare: 565..572"
    private const int ClaimPrepBase = 565; // spec: §8.21.5
    private const int ClaimPrepCount = 8; // spec: §8.21.5 — 565..572

    // View-switch actions (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "view-switch: 541..548"
    private const int ViewSwitchBase = 541; // spec: §8.21.5
    private const int ViewSwitchCount = 8; // spec: §8.21.5 — 541..548

    // Select-all / list button (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 — "select-all / list button: 580"
    private const int SelectAllAction = 580; // spec: §8.21.5

    // Caption msg.xdb ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.5 CODE-CONFIRMED
    private const int Msg40010 = 40010; // spec: §8.21.5 — generic delivery caption
    private const int Msg16005 = 16005; // spec: §8.21.5 — "no content"
    private const int Msg55016 = 55016; // spec: §8.21.5 — "no free bag slots when claiming"

    // Approximate panel size (exact = debugger-pending; item-preview/slot area inferred)
    private const float PanelW = 380f;
    private const float PanelH = 550f;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _selectedCell = -1;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the DeliveryPanel with its 40-cell 5×8 item grid.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 40.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudDeliveryWindow";

        // Master-window places root origin — debugger-pending.
        // Use screen-centred placeholder.
        // spec: ui_hud_layout.md §5.13 — "master-window placed".
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = -PanelH / 2f;
        OffsetRight = PanelW / 2f;
        OffsetBottom = PanelH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop (art from delivery.dds when VFS present)
        // spec: ui_system.md §8.21.5 — "atlas delivery.dds"
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Title
        AddChild(new Label
        {
            Name = "Title",
            Text = "Delivery Box",
            Position = new Vector2(10f, 8f),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // Page tab buttons (actions 573..578 = 6 category tabs)
        // spec: ui_system.md §8.21.5 — "page tabs: actions 573..578 (own/others/sale categories)"
        string[] tabLabels = { "Own", "Others", "Sale", "Tab4", "Tab5", "Tab6" };
        for (int i = 0; i < TabActionCount; i++)
        {
            int capturedAction = TabActionBase + i;
            var tab = new Button
            {
                Name = $"Tab{i}",
                Text = tabLabels[i],
                Position = new Vector2(10f + i * 58f, 30f),
                Size = new Vector2(52f, 22f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            tab.Pressed += () => OnTabPressed(capturedAction);
            AddChild(tab);
        }

        // 5×8 item grid (40 cells, action ids 500..539)
        // spec: ui_system.md §8.21.5 — "40-slot item grid (5 × 8): action ids 500..539"
        const float cellSize = 44f;
        const float cellStride = 46f;
        const float gridX = 10f;
        const float gridY = 60f;

        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                int cellIdx = row * GridCols + col;
                int actionId = CellActionBase + cellIdx; // spec: §8.21.5 — 500..539
                int capturedIdx = cellIdx;

                var cell = new Button
                {
                    Name = $"Cell{actionId}",
                    Text = string.Empty,
                    Position = new Vector2(gridX + col * cellStride, gridY + row * cellStride),
                    Size = new Vector2(cellSize, cellSize),
                    MouseFilter = MouseFilterEnum.Stop,
                };
                cell.Pressed += () => OnCellPressed(capturedIdx);
                AddChild(cell);
            }
        }

        // Scrollbar controls (thumb 583, down 584, up 585)
        // spec: ui_system.md §8.21.5 — "scrollbar: thumb 583, down 584, up 585"
        var scrollUp = new Button
        {
            Name = "ScrollUp",
            Text = "▲",
            Position = new Vector2(PanelW - 26f, gridY),
            Size = new Vector2(18f, 18f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrollUp.Pressed += () => OnScrollUp();
        AddChild(scrollUp);

        var scrollDown = new Button
        {
            Name = "ScrollDown",
            Text = "▼",
            Position = new Vector2(PanelW - 26f, gridY + GridRows * cellStride - 20f),
            Size = new Vector2(18f, 18f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrollDown.Pressed += () => OnScrollDown();
        AddChild(scrollDown);

        // Quantity controls (601/602)
        // spec: ui_system.md §8.21.5 — "Quantity +/-: 601 (dec), 602 (inc)"
        AddChild(new Label
        {
            Text = "Qty:",
            Position = new Vector2(10f, PanelH - 80f),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var qtyDec = new Button
        {
            Name = "QtyDec",
            Text = "-",
            Position = new Vector2(40f, PanelH - 82f),
            Size = new Vector2(22f, 22f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        qtyDec.Pressed += () => OnQty(QtyDec);
        AddChild(qtyDec);

        var qtyDisplay = new Label
        {
            Name = "QtyDisplay",
            Text = "1",
            Position = new Vector2(66f, PanelH - 80f),
            Size = new Vector2(30f, 20f),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(qtyDisplay);

        var qtyInc = new Button
        {
            Name = "QtyInc",
            Text = "+",
            Position = new Vector2(100f, PanelH - 82f),
            Size = new Vector2(22f, 22f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        qtyInc.Pressed += () => OnQty(QtyInc);
        AddChild(qtyInc);

        // Claim button (triggers claim-prepare → InfoPanel confirm → C2S 2/71)
        // spec: ui_system.md §8.21.5 — "claim-prepare (565..572) stages recipient name + up to 5 items → InfoPanel → C2S 2/71"
        var claimBtn = new Button
        {
            Name = "ClaimBtn",
            Text = "Claim",
            Position = new Vector2(140f, PanelH - 82f),
            Size = new Vector2(100f, 30f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        claimBtn.Pressed += OnClaim;
        AddChild(claimBtn);

        // Select-all / list button (action 580)
        // spec: ui_system.md §8.21.5 — "select-all / list button: 580"
        var selectAllBtn = new Button
        {
            Name = "SelectAllBtn",
            Text = "Select All",
            Position = new Vector2(250f, PanelH - 82f),
            Size = new Vector2(80f, 30f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        selectAllBtn.Pressed += () =>
        {
            GD.Print($"[HudDeliveryWindow] Select-all (action {SelectAllAction}). spec: §8.21.5.");
        };
        AddChild(selectAllBtn);

        // Close button
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(PanelW - 28f, 8f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        // Delivery list stub
        // TODO(capture): populate delivery list from inbound S2C (opcode unknown).
        // spec: ui_system.md §8.21.7 — "S2C populate for delivery box not walked (TODO capture)"
        AddChild(new Label
        {
            Name = "DeliveryListStub",
            Text = "// TODO(capture): delivery list populate. S2C opcode unknown (capture-pending).\n" +
                   "// spec: Docs/RE/specs/ui_system.md §8.21.7.",
            Position = new Vector2(10f, PanelH - 48f),
            Size = new Vector2(PanelW - 20f, 40f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore,
        });

        GD.Print("[HudDeliveryWindow] Built — DeliveryPanel slot 40 (5×8 grid 40 cells, action 500..539). " +
                 "Tabs 573..578, scroll 583/584/585, qty 601/602, claim 565..572. " +
                 "Outbound C2S 2/71 CmsgDeliveryClaim = TODO(world-campaign). " +
                 "Delivery list = TODO(capture). " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnTabPressed(int actionId)
    {
        // spec: ui_system.md §8.21.5 — "page tabs 573..578 (own/others/sale categories)"
        GD.Print($"[HudDeliveryWindow] Tab action {actionId}. spec: §8.21.5.");
    }

    private void OnCellPressed(int cellIdx)
    {
        // spec: ui_system.md §8.21.5 — "action ids 500..539; category-7 item tooltips"
        _selectedCell = cellIdx;
        GD.Print($"[HudDeliveryWindow] Cell selected: {cellIdx} (action {CellActionBase + cellIdx}). " +
                 "spec: §8.21.5 CODE-CONFIRMED.");
    }

    private void OnScrollUp()
    {
        // spec: ui_system.md §8.21.5 — "scrollbar up 585"
        GD.Print($"[HudDeliveryWindow] Scroll up (action {ScrollUp}). spec: §8.21.5.");
    }

    private void OnScrollDown()
    {
        // spec: ui_system.md §8.21.5 — "scrollbar down 584"
        GD.Print($"[HudDeliveryWindow] Scroll down (action {ScrollDown}). spec: §8.21.5.");
    }

    private void OnQty(int actionId)
    {
        // spec: ui_system.md §8.21.5 — "quantity +/-: 601 (dec), 602 (inc)"
        GD.Print($"[HudDeliveryWindow] Qty action {actionId}. spec: §8.21.5.");
    }

    private void OnClaim()
    {
        // spec: ui_system.md §8.21.5 — "claim flow (deferred-confirm): stages recipient + up to 5 items,
        //   opens InfoPanel confirm; confirm 'Yes' emits C2S CmsgDeliveryClaim (2/71)"
        if (_selectedCell < 0)
        {
            GD.PrintErr($"[HudDeliveryWindow] No cell selected for claim. msg.xdb {Msg16005}. spec: §8.21.5.");
            return;
        }

        // TODO(world-campaign): IApplicationUseCases.DeliveryClaimAsync (C2S 2/71, 4B).
        // spec: Docs/RE/packets/ — CmsgDeliveryClaim (2/71, 4B).
        GD.Print($"[HudDeliveryWindow] Claim intent: cell={_selectedCell}. " +
                 "TODO(world-campaign): C2S 2/71 CmsgDeliveryClaim (4B). " +
                 $"No-bag-slot caption: msg.xdb {Msg55016}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows or hides the delivery window.
    /// Opened from NPC service interaction; specific open opcode = debugger-pending.
    /// spec: Docs/RE/specs/ui_system.md §8.21.7 — "open from NPC-service; opcode not isolated".
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        if (_open)
        {
            _selectedCell = -1;
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