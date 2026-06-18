// Ui/Hud/HudVendorWindow.cs
//
// In-game NPC vendor / item-shop buy/sell window (SubscriptionPanel class, slot 259).
//
// This is the BUY/SELL storefront opened by NPC KIND 32. It is DISTINCT from:
//   - ProductPanel (§8.18) — NPC CRAFTING / production (slot 230)
//   - KeepNpcPanel (§8.22.6) — the NPC dialog-menu (no item grid, no wire)
//   The vendor at slot 259 is the ONLY one that puts buy/sell transactions on the wire.
//   spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 259.
//
// Geometry (CODE-CONFIRMED child rects — root origin = master-window placed, debugger-pending):
//   Backdrop:           (0, 0, 360, 280), uitex 8, src (300, 722).
//   Confirm/close btn:  (135, 200, 90, 25), uitex 2, NORMAL (837,815)/PRESSED (837,775), action 0.
//   Buy-row buttons:    (264, rowY, 54, 25), uitex 2, NORMAL (798,540)/PRESSED (798,566), action 100..102.
//   Name label (per row): (114, rowY+5, 100, 15).
//   Status label:       (210, 170, 100, 15).
//   Row Y loop: rowY from 70, step 30, while rowY<160 → 3 visible rows (y=70/100/130).
//   spec: Docs/RE/specs/ui_system.md §8.22.1 CODE-CONFIRMED.
//
// Atlas binding (CODE-CONFIRMED uitex ids; DDS = VFS uitex.txt pending):
//   uitex 2 = inventwindow.dds — backdrop + close/buy buttons.
//   uitex 8 = skillwindow.dds  — secondary chrome panels.
//   spec: Docs/RE/specs/ui_system.md §8.22.2 CODE-CONFIRMED.
//
// Captions from msg.xdb (CODE-CONFIRMED):
//   45015 = money/gold label, 45016 = per-item price (÷1,000,000),
//   45020 = generic shop failure, 45021..45024 = specific failure reasons,
//   65008 = purchase-success, 65006/65007/65010..65012 = purchase-failure reasons,
//   54127..54130 = cash-shop action-result (NOT this vendor), 36003..36028 = buy duration notices.
//   spec: Docs/RE/specs/ui_system.md §8.22.7 CODE-CONFIRMED.
//
// Network (CODE-CONFIRMED opcodes; wire-field tables owned by opcodes.md/packets/):
//   C2S CmsgNpcBuyOrAcquire (2/19), CmsgNpcSell (2/20), CmsgShopBuy (2/115 → {npc_id, row×2}).
//   S2C SmsgNpcBuyOrAcquireAck (4/19), SmsgNpcSellItemAck (4/20), SmsgNpcShopSlotClearAck (4/21),
//       SmsgItemShopPurchaseResult (4/113), SmsgItemShopBalanceUpdate (4/115).
//   spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED.
//   No IApplicationUseCases method for ShopBuy → all sends are TODO(world-campaign).
//
// Open mechanism: NPC-interaction KIND 32 (not a HUD hotkey).
//   spec: Docs/RE/specs/ui_system.md §8.22.5 — "KIND 32 (0x20) opens item-shop vendor (slot 259)".
//   Expose Open(npcId) / Show() from HudMaster.
//
// Stock: local client-side shop-script map keyed by NPC id (no C2S on open).
//   Populate fills 6 entries; BuildScene shows 3 rows (scroll/page TBD).
//   spec: Docs/RE/specs/ui_system.md §8.22.5 — "6-entry populate; only buy/sell on wire".
//
// PASSIVE: zero game logic. Row selection = local view state. Net sends = TODO(world-campaign).

using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game NPC vendor / item-shop buy/sell window (slot 259).
///
/// <para>Opened by NPC-interaction KIND 32. Shows 3 visible buy-row buttons. Row selection is
/// local view state; the actual buy sends TODO(world-campaign) C2S requests.</para>
///
/// <para>PASSIVE: zero game logic; no domain mutation; net sends = TODO stubs.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 259.
/// </summary>
public sealed partial class HudVendorWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_system.md §8.22.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float BackdropW = 360f; // spec: §8.22.1 — backdrop W=360
    private const float BackdropH = 280f; // spec: §8.22.1 — backdrop H=280

    // Buy-row layout — 3 visible rows at y=70/100/130 (step 30 while <160)
    // spec: §8.22.1 — "rowY from 70, step 30, while rowY<160 → rows at 70/100/130"
    private static readonly int[] RowY = { 70, 100, 130 }; // spec: §8.22.1

    // Close / confirm button
    // spec: §8.22.1 — "(135, 200, 90, 25), uitex 2, src (837,815)/(837,775), action 0"
    private const float CloseBtnX = 135f; // spec: §8.22.1
    private const float CloseBtnY = 200f; // spec: §8.22.1
    private const float CloseBtnW = 90f; // spec: §8.22.1
    private const float CloseBtnH = 25f; // spec: §8.22.1

    // Buy-row buttons
    // spec: §8.22.1 — "(264, rowY, 54, 25), action 100..102"
    private const float BuyBtnX = 264f; // spec: §8.22.1
    private const float BuyBtnW = 54f; // spec: §8.22.1
    private const float BuyBtnH = 25f; // spec: §8.22.1

    // Name label per row
    // spec: §8.22.1 — "(114, rowY+5, 100, 15)"
    private const float NameLblX = 114f; // spec: §8.22.1
    private const float NameLblW = 100f; // spec: §8.22.1
    private const float NameLblH = 15f; // spec: §8.22.1

    // Status label
    // spec: §8.22.1 — "(210, 170, 100, 15)"
    private const float StatusLblX = 210f; // spec: §8.22.1
    private const float StatusLblY = 170f; // spec: §8.22.1

    // msg.xdb caption ids (CODE-CONFIRMED)
    // spec: Docs/RE/specs/ui_system.md §8.22.7 CODE-CONFIRMED
    private const int MsgMoneyLabel = 45015; // spec: §8.22.7 — money/gold label
    private const int MsgItemPrice = 45016; // spec: §8.22.7 — per-item price (÷1,000,000)
    private const int MsgShopFail = 45020; // spec: §8.22.7 — generic shop failure

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private uint _npcId; // NPC id that opened this vendor
    private int _selectedRow = -1; // local row selection; -1 = none
    private readonly Label[] _rowLabels = new Label[3];
    private Label? _moneyLabel;
    private Label? _statusLabel;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the vendor panel with its 3 visible buy-row buttons and labels.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 259.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudVendorWindow";

        // Root origin set by master-window machinery — debugger-pending.
        // Use a centered anchor as a best-effort placeholder.
        // spec: §8.22.8 — "vendor root-window origin = master-window placed; debugger-pending".
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -BackdropW / 2f;
        OffsetTop = -BackdropH / 2f;
        OffsetRight = BackdropW / 2f;
        OffsetBottom = BackdropH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // ── Backdrop (uitex 8, src (300,722), 360×280) ──
        // spec: §8.22.1 — "Backdrop (0,0,360,280), uitex 8, src (300,722)"
        // TODO(assets): bind uitex 8 src (300, 722) when atlas available.
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.AnchorLeft = 0f;
        backdrop.AnchorTop = 0f;
        backdrop.AnchorRight = 0f;
        backdrop.AnchorBottom = 0f;
        backdrop.OffsetLeft = 0f;
        backdrop.OffsetTop = 0f;
        backdrop.OffsetRight = BackdropW;
        backdrop.OffsetBottom = BackdropH;
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.55f, 0.40f, 0.10f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // ── Title label ──
        var title = new Label
        {
            Name = "Title",
            Text = "상점", // "Shop" in Korean — CP949 rendered by engine
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(title);

        // ── Money label (msg.xdb 45015) ──
        // spec: §8.22.3 — "money label formatted via msg.xdb 45015; seeded on open"
        string moneyCaption = text?.GetCaption(MsgMoneyLabel, "[금액]") ?? "[금액]";
        _moneyLabel = new Label
        {
            Name = "MoneyLabel",
            Text = moneyCaption,
            Position = new Vector2(10f, 40f),
            Size = new Vector2(200f, 20f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_moneyLabel);

        // ── 3 visible buy-row buttons + name labels ──
        // spec: §8.22.1 — "rowY from 70, step 30, while rowY<160 → y=70/100/130"
        for (int i = 0; i < RowY.Length; i++)
        {
            int rowIdx = i;
            float rowY = RowY[i]; // spec: §8.22.1

            // Name label: (114, rowY+5, 100, 15)
            // spec: §8.22.1 — "(114, rowY+5, 100, 15)"
            _rowLabels[i] = new Label
            {
                Name = $"RowLabel{i}",
                Text = $"[항목 {i}]", // placeholder — filled from shop-script map
                Position = new Vector2(NameLblX, rowY + 5f), // spec: §8.22.1
                Size = new Vector2(NameLblW, NameLblH), // spec: §8.22.1
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(_rowLabels[i]);

            // Buy button: (264, rowY, 54, 25), action 100..102
            // spec: §8.22.1 — "(264, rowY, 54, 25), uitex 2, src (798,540)/(798,566), action 100+i"
            // TODO(assets): bind uitex 2 src (798,540) NORMAL / (798,566) PRESSED when atlas available.
            var buyBtn = new Button
            {
                Name = $"BuyBtn{i}",
                Text = "구매", // "Buy" — CP949
                Position = new Vector2(BuyBtnX, rowY), // spec: §8.22.1
                Size = new Vector2(BuyBtnW, BuyBtnH), // spec: §8.22.1
                MouseFilter = MouseFilterEnum.Stop,
            };
            int capturedRow = i;
            buyBtn.Pressed += () => OnRowSelect(capturedRow); // action 100+row
            AddChild(buyBtn);
        }

        // ── Status label (210, 170, 100, 15) ──
        // spec: §8.22.1 — "(210, 170, 100, 15)"
        _statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = string.Empty,
            Position = new Vector2(StatusLblX, StatusLblY), // spec: §8.22.1
            Size = new Vector2(100f, 15f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_statusLabel);

        // ── Stock stub ──
        // TODO(capture): vendor stock list — S2C pending.
        // spec: §8.22.5 — "shop stock is a local script table; only buy/sell on wire"
        var stockStub = new Label
        {
            Name = "StockStub",
            Text = "// TODO(capture): vendor stock from shop script map (NPC id keyed)",
            Position = new Vector2(10f, 250f),
            Size = new Vector2(340f, 15f),
            LabelSettings = new LabelSettings { FontSize = 8 },
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(stockStub);

        // ── Confirm / close button (135, 200, 90, 25), action 0 ──
        // spec: §8.22.1 — "(135,200,90,25), uitex 2, src (837,815)/(837,775), action 0"
        // TODO(assets): bind uitex 2 src (837,815) NORMAL / (837,775) PRESSED.
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "닫기", // "Close" — CP949
            Position = new Vector2(CloseBtnX, CloseBtnY), // spec: §8.22.1
            Size = new Vector2(CloseBtnW, CloseBtnH), // spec: §8.22.1
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += () => { Close(); }; // action 0 = close // spec: §8.22.4
        AddChild(closeBtn);

        GD.Print("[HudVendorWindow] Built — NPC vendor slot 259 (3 visible buy-rows, money label, " +
                 "close/confirm btn). " +
                 "Net sends C2S 2/19/2/20/2/115 = TODO(world-campaign). " +
                 "Stock = TODO(capture). " +
                 "spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Open / Close
    // spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED — NPC KIND 32
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the vendor window for a given NPC.
    /// Called by the world NPC-interaction dispatcher on KIND 32.
    /// spec: Docs/RE/specs/ui_system.md §8.22.5 — "KIND 32 (0x20) opens item-shop vendor".
    /// The shop stock is a LOCAL script table keyed by npcId — no open C2S.
    /// TODO(world-campaign): look up the shop-script map by npcId and populate _rowLabels.
    /// </summary>
    public void Open(uint npcId = 0)
    {
        _npcId = npcId;
        _selectedRow = -1;
        _open = true;
        Visible = true;

        // Seed money label placeholder (real value from player gold on S2C 4/115).
        // spec: §8.22.3 — "seeded on open and re-driven on every balance push".
        if (_moneyLabel != null) _moneyLabel.Text = "[금액: ---]"; // TODO(world-campaign): read player gold

        // Populate row labels from the shop-script map (keyed by npcId).
        // spec: §8.22.5 — "6-entry populate from client-side shop script map keyed by NPC id"
        // TODO(world-campaign): populate from shop-script catalogue.
        for (int i = 0; i < _rowLabels.Length; i++)
        {
            if (_rowLabels[i] != null)
                _rowLabels[i].Text = $"[항목 {i} — NPC {npcId}]";
        }

        GD.Print($"[HudVendorWindow] Open — npcId={npcId}. " +
                 "TODO(world-campaign): populate from shop-script map + read player gold. " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED.");
    }

    /// <summary>
    /// Show/hide toggle (for HudMaster.ShowVendor(bool)).
    /// spec: Docs/RE/specs/ui_system.md §8.22.5.
    /// </summary>
    public void Toggle(bool show)
    {
        _open = show;
        Visible = show;
    }

    private void Close()
    {
        // action 0 = confirm/close. spec: §8.22.4 — "action 0 = close / hide".
        _open = false;
        Visible = false;
        GD.Print("[HudVendorWindow] Closed (action 0). " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.4 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnRowSelect(int rowIdx)
    {
        // action 100..102 — select row (store selected index), show item detail via InfoPanel.
        // spec: §8.22.4 — "100..102 = select row + show item info; buy/sell sends from NPC shop manager"
        _selectedRow = rowIdx;
        int actionId = 100 + rowIdx; // spec: §8.22.1 — actions 100..102

        if (_statusLabel != null)
            _statusLabel.Text = $"[선택: {rowIdx}]"; // local view state

        GD.Print($"[HudVendorWindow] Row selected: {rowIdx} (action {actionId}). " +
                 "TODO(world-campaign): C2S CmsgShopBuy (2/115) on confirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.4 CODE-CONFIRMED.");

        // TODO(world-campaign): IApplicationUseCases.ShopBuyAsync({npcId, rowIdx×2})
        // spec: §8.22.5 — "CmsgShopBuy 2/115: {npc_id, selected-row × 2}"
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // ESC (key 27) = close. spec: §8.22.4 — "(Esc, key 27) → close"
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}