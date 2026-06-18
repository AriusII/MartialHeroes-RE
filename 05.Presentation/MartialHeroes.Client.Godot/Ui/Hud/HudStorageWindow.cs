// Ui/Hud/HudStorageWindow.cs
//
// In-game player STORAGE / WAREHOUSE window — `KeepPanel` (master service slot 191).
//
// This is the 60-cell item storage grid. DISTINCT from the trade window (HudTradeWindow,
// which is the TradeKeepWindow/§8.13). KeepPanel has no hotkey and no DefaultMenu entry —
// it is opened ONLY via HudKeepNpcDialog (KIND-9 NPC → sel 1).
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   Width = 318 (backdrop). Constructor id 318, width 732.
//   spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED
//
// Cell grid (CODE-CONFIRMED):
//   60 cells = 10 rows × 6 columns, each 38×38.
//   Cell i: col=i%6, row=i/6, x=45+38·col, y=162+38·row, action=200+i (actions 200..259).
//   spec: Docs/RE/specs/ui_system.md §8.32.1 CODE-CONFIRMED
//
// Chrome (CODE-CONFIRMED):
//   Backdrop      (0,  85, 318, 625)  uitex 4  src (317, 0)
//   Title bar     (0,  36, 318,  50)  uitex 2  src (0, 683)
//   Header glyph  (140, 60, 39,  17)  uitex 4  src (248, 686)
//   Page/tab A    (25, 105,  65, 20)  uitex 4  action 261
//   Page/tab B    (90, 105,  65, 20)  uitex 4  action 262
//   Info label    (51, 598, 128, 15)  — text label
//   Deposit-money (183, 592, 53,  22)  uitex 4  action 263
//   Withdraw-money(238, 592, 53,  22)  uitex 4  action 264
//   Close/OK      (259, 655, 59,  77)  uitex 2  action 260
//   spec: Docs/RE/specs/ui_system.md §8.32.2 CODE-CONFIRMED
//
// Atlas (uitex ids — CODE-CONFIRMED):
//   uitex 2 = title/header bar, close button (primary keep chrome)
//   uitex 4 = backdrop, header glyph, page/money buttons
//   uitex 14 = label/text/font atlas
//   uitex 78 = 60 cell item-icon image components
//   uitex 70/71/72/74 = four quality-tint cell overlays
//   spec: Docs/RE/specs/ui_system.md §8.32.3 CODE-CONFIRMED
//
// Action map (CODE-CONFIRMED):
//   200..259 hover → tooltip; click holding → C2S 2/46 (move) or 2/44 (quick-move)
//   260 = close / OK
//   261 / 262 = page/tab A / B
//   263 = deposit money (opens number-entry dialog; msg 2215; ≥1,000,000 gold gate; else msg 45023)
//   264 = withdraw money (opens number-entry dialog; msg 2216; stored>0 gate; else msg 45023)
//   ESC (key 27) = close
//   spec: Docs/RE/specs/ui_system.md §8.32.4 CODE-CONFIRMED
//
// Storage slot math (CODE-CONFIRMED):
//   slot = action + 60·page + 56 (unified slot space base +56)
//   spec: Docs/RE/specs/ui_system.md §8.32.4 CODE-CONFIRMED
//
// Open: ONLY via KeepNpcPanel sel 1 (KIND-9 NPC) + C2S 2/142 (storage open request).
//   No keyboard hotkey, no DefaultMenu action.
//   spec: Docs/RE/specs/ui_system.md §8.32.5 CODE-CONFIRMED
//
// Opcodes (canonical names, major/minor only):
//   C2S 2/46 = CmsgItemMove (12B)
//   C2S 2/44 = CmsgItemQuickMove (12B)
//   C2S 2/142 = storage open request + money deposit/withdraw (16B; op-byte = action−7)
//   S2C populate = shared item-panel family via unified-slot base +56 (capture-pending specific minor)
//   spec: Docs/RE/specs/ui_system.md §8.32.6/§8.32.7 CODE-CONFIRMED
//
// Captions (msg.xdb ids CODE-CONFIRMED):
//   2213/2214 = page/tab A/B; 2215 = deposit dialog; 2216 = withdraw dialog
//   2219 = quantity-split dialog; 2142 = "cannot store this"; 38004 = quest-keep guard; 45023 = not enough gold
//   spec: Docs/RE/specs/ui_system.md §8.32.8 CODE-CONFIRMED
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed pending world-campaign).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game player storage / warehouse window (KeepPanel, master service slot 191).
///
/// <para>A 60-cell (10×6) item grid for the player's warehouse storage. Opened ONLY via the
/// KeepNpcPanel (KIND-9 NPC interaction → sel 1 → C2S 2/142). No hotkey, no DefaultMenu entry.
/// DISTINCT from HudTradeWindow (KeepPanel/TradeKeepWindow §8.13).</para>
///
/// <para>PASSIVE: zero game logic. Cell clicks emit move intents as use-case calls (stubbed).
/// Storage contents arrive via shared item-panel S2C family (capture-pending specific minor).</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudStorageWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Panel width from constructor id 318
    // spec: ui_system.md §8.32 — "constructor id 318, width 732"
    private const float PanelW = 318f; // spec: ui_system.md §8.32 CODE-CONFIRMED
    private const float PanelH = 732f; // spec: ui_system.md §8.32 — "width 732" (height = full dock)

    // Cell grid constants
    // spec: ui_system.md §8.32.1 CODE-CONFIRMED
    private const int Cols = 6; // spec: ui_system.md §8.32.1 — "6 columns"
    private const int Rows = 10; // spec: ui_system.md §8.32.1 — "10 rows"
    private const int TotalCells = 60; // spec: ui_system.md §8.32.1 — "60 cells"
    private const float CellSize = 38f; // spec: ui_system.md §8.32.1 — "each cell 38×38"
    private const float GridOriginX = 45f; // spec: ui_system.md §8.32.1 — "x=45+38·col"
    private const float GridOriginY = 162f; // spec: ui_system.md §8.32.1 — "y=162+38·row"
    private const int CellActionBase = 200; // spec: ui_system.md §8.32.1 — "action id = 200+i"

    // Chrome geometry
    // spec: ui_system.md §8.32.2 CODE-CONFIRMED
    private const float BackdropY = 85f; // src: (317, 0) uitex 4
    private const float TitleBarY = 36f; // src: (0, 683) uitex 2
    private const float PageTabAX = 25f; // action 261
    private const float PageTabAY = 105f;
    private const float PageTabBX = 90f; // action 262
    private const float PageTabBY = 105f;
    private const float MoneyBtnW = 53f;
    private const float MoneyBtnH = 22f;
    private const float MoneyBtnY = 592f;
    private const float DepositX = 183f; // action 263
    private const float WithdrawX = 238f; // action 264
    private const float CloseX = 259f; // action 260
    private const float CloseY = 655f;
    private const float CloseW = 59f;
    private const float CloseH = 77f;

    // Atlas uitex ids (CODE-CONFIRMED; DDS resolution via uitex.txt VFS-pending)
    // spec: ui_system.md §8.32.3 CODE-CONFIRMED
    private const int PrimaryTexId = 2; // title/header bar, close btn
    private const int SecondaryTexId = 4; // backdrop, glyph, page/money buttons
    private const int LabelTexId = 14; // text/font atlas
    private const int CellIconTexId = 78; // 60 cell item-icon images

    // msg.xdb caption ids (CODE-CONFIRMED; CP949 text VFS-pending)
    // spec: ui_system.md §8.32.8 CODE-CONFIRMED
    private const int MsgTabA = 2213; // page/tab A caption
    private const int MsgTabB = 2214; // page/tab B caption
    private const int MsgDeposit = 2215; // deposit-money dialog title
    private const int MsgWithdraw = 2216; // withdraw-money dialog title
    private const int MsgCannotStore = 2142; // "cannot store this bound item" notice
    private const int MsgQuestGuard = 38004; // quest-keep guard notice
    private const int MsgNotEnoughGold = 45023; // "not enough gold / nothing stored"

    // Storage slot math base (CODE-CONFIRMED)
    // spec: ui_system.md §8.32.4 — "slot = action + 60·page + 56"
    private const int StorageSlotBase = 56; // spec: ui_system.md §8.32.4 CODE-CONFIRMED
    private const int SlotPerPage = 60; // spec: ui_system.md §8.32.4 CODE-CONFIRMED

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _activePage; // 0 or 1 (two page tabs)

    // 60 cell buttons (parallel to the cell grid)
    private readonly Button[] _cellBtns = new Button[TotalCells];

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the 60-cell storage grid plus chrome.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.32.1/§8.32.2 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudStorageWindow";

        // Right-anchored off-screen (no hotkey; opened only via KeepNpcPanel)
        // spec: ui_system.md §8.32.5 — "no keyboard hotkey; opened via KeepNpcPanel sel 1"
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -PanelW;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = PanelH;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop (uitex 4 — src 317,0 — 318×625 at y=85)
        // spec: ui_system.md §8.32.2 — "(0,85,318,625) uitex 4 src (317,0)"
        var bd = new Panel { Name = "Backdrop" };
        bd.Position = new Vector2(0f, BackdropY);
        bd.Size = new Vector2(PanelW, 625f);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.3f, 0.55f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        // Title bar (uitex 2, src 0,683 — 318×50 at y=36)
        // spec: ui_system.md §8.32.2 — "(0,36,318,50) uitex 2 src (0,683)"
        var title = new Panel { Name = "TitleBar" };
        title.Position = new Vector2(0f, TitleBarY);
        title.Size = new Vector2(PanelW, 50f);
        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.12f, 0.10f, 0.07f, 0.95f);
        title.AddThemeStyleboxOverride("panel", titleStyle);
        AddChild(title);

        // Title label
        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Storage",
            Position = new Vector2(10f, TitleBarY + 16f),
            Size = new Vector2(180f, 20f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(titleLbl);

        // Page/tab A button (action 261)
        // spec: ui_system.md §8.32.2 — "(25,105,65,20) uitex 4 action 261"
        // spec: ui_system.md §8.32.8 — "msg 2213 = page/tab A caption"
        var tabA = new Button
        {
            Name = "TabA",
            Text = text.GetCaption(MsgTabA, "Page 1"),
            Position = new Vector2(PageTabAX, PageTabAY),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        tabA.Pressed += () => OnPageTab(0); // action 261
        AddChild(tabA);

        // Page/tab B button (action 262)
        // spec: ui_system.md §8.32.2 — "(90,105,65,20) uitex 4 action 262"
        // spec: ui_system.md §8.32.8 — "msg 2214 = page/tab B caption"
        var tabB = new Button
        {
            Name = "TabB",
            Text = text.GetCaption(MsgTabB, "Page 2"),
            Position = new Vector2(PageTabBX, PageTabBY),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        tabB.Pressed += () => OnPageTab(1); // action 262
        AddChild(tabB);

        // Build the 60 cell buttons (actions 200..259)
        // spec: ui_system.md §8.32.1 CODE-CONFIRMED
        for (int i = 0; i < TotalCells; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            float cx = GridOriginX + col * CellSize; // spec: ui_system.md §8.32.1 "x=45+38·col"
            float cy = GridOriginY + row * CellSize; // spec: ui_system.md §8.32.1 "y=162+38·row"
            int actionId = CellActionBase + i; // spec: ui_system.md §8.32.1 "action=200+i"

            var cell = new Button
            {
                Name = $"Cell{i:D2}",
                Text = "",
                Position = new Vector2(cx, cy),
                Size = new Vector2(CellSize, CellSize),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop,
            };
            int captured = i;
            cell.Pressed += () => OnCellClick(captured); // action 200+i
            AddChild(cell);
            _cellBtns[i] = cell;
        }

        // Info-line label (51, 598, 128, 15)
        // spec: ui_system.md §8.32.2 — "(51,598,128,15)"
        var infoLbl = new Label
        {
            Name = "InfoLabel",
            Text = "",
            Position = new Vector2(51f, 598f),
            Size = new Vector2(128f, 15f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(infoLbl);

        // Deposit-money button (action 263)
        // spec: ui_system.md §8.32.2 — "(183,592,53,22) uitex 4 action 263"
        // spec: ui_system.md §8.32.8 — "msg 2215 = deposit dialog title"
        var depositBtn = new Button
        {
            Name = "DepositBtn",
            Text = text.GetCaption(MsgDeposit, "Deposit"),
            Position = new Vector2(DepositX, MoneyBtnY),
            Size = new Vector2(MoneyBtnW, MoneyBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        depositBtn.Pressed += OnDeposit; // action 263
        AddChild(depositBtn);

        // Withdraw-money button (action 264)
        // spec: ui_system.md §8.32.2 — "(238,592,53,22) uitex 4 action 264"
        // spec: ui_system.md §8.32.8 — "msg 2216 = withdraw dialog title"
        var withdrawBtn = new Button
        {
            Name = "WithdrawBtn",
            Text = text.GetCaption(MsgWithdraw, "Withdraw"),
            Position = new Vector2(WithdrawX, MoneyBtnY),
            Size = new Vector2(MoneyBtnW, MoneyBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        withdrawBtn.Pressed += OnWithdraw; // action 264
        AddChild(withdrawBtn);

        // Close / OK button (action 260)
        // spec: ui_system.md §8.32.2 — "(259,655,59,77) uitex 2 action 260"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "OK",
            Position = new Vector2(CloseX, CloseY),
            Size = new Vector2(CloseW, CloseH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += OnClose; // action 260
        AddChild(closeBtn);

        GD.Print("[HudStorageWindow] Built — KeepPanel slot 191 (60-cell 10×6 storage grid). " +
                 $"Cell grid: x=45+38·col, y=162+38·row, actions 200..259. " +
                 "Page tabs 261/262; deposit 263; withdraw 264; close 260. " +
                 "Open: ONLY via KeepNpcPanel sel 1 + C2S 2/142. No hotkey. " +
                 "Item move: TODO(world-campaign): C2S 2/46 (move) + 2/44 (quick-move). " +
                 "Storage contents: TODO(capture): shared item-panel S2C via unified-slot base +56. " +
                 "spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Open / close
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the storage window (called from HudKeepNpcDialog sel 1).
    /// Also emits C2S 2/142 (storage open request) via use-case — stubbed.
    /// spec: Docs/RE/specs/ui_system.md §8.32.5 — "KIND-9 NPC → KeepNpcPanel → sel 1 → C2S 2/142 → show slot 191".
    /// </summary>
    public void Open()
    {
        _open = true;
        _activePage = 0;
        Visible = true;
        // TODO(world-campaign): IApplicationUseCases.StorageOpen(npcId) → C2S 2/142
        GD.Print("[HudStorageWindow] Open → TODO(world-campaign): C2S 2/142 (storage open request). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.5/§8.32.6 CODE-CONFIRMED.");
    }

    private void OnClose()
    {
        // action 260 / ESC
        // spec: ui_system.md §8.32.4 — "260: close, restore world HUD panels"
        _open = false;
        Visible = false;
        GD.Print("[HudStorageWindow] Closed (action 260). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnPageTab(int page)
    {
        // actions 261 (page 0) / 262 (page 1)
        // spec: ui_system.md §8.32.4 — "261: page/tab A → set active page 0; 262: → page 1"
        _activePage = page;
        GD.Print($"[HudStorageWindow] Page tab {page} (action {261 + page}). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    private void OnCellClick(int cellIndex)
    {
        // action 200 + cellIndex
        // spec: ui_system.md §8.32.4 — "200..259 click: place/move or pick-up"
        // Unified slot = action + 60·page + 56
        // spec: ui_system.md §8.32.4 CODE-CONFIRMED — "slot = action + 60·page + 56"
        int action = CellActionBase + cellIndex;
        int unifiedSlot = action + SlotPerPage * _activePage + StorageSlotBase;
        // TODO(world-campaign): drag/drop → C2S 2/46 (move) or 2/44 (quick-move)
        GD.Print($"[HudStorageWindow] Cell {cellIndex} clicked (action {action}, " +
                 $"unified slot {unifiedSlot}, page {_activePage}). " +
                 "TODO(world-campaign): C2S 2/46 (move) or 2/44 (quick-move). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4 CODE-CONFIRMED.");
    }

    private void OnDeposit()
    {
        // action 263 — open number-entry dialog (deposit mode)
        // spec: ui_system.md §8.32.4 — "263: if gold ≥ 1,000,000 → number-entry dialog (deposit); else msg 45023"
        // TODO(world-campaign): check gold; open number-entry dialog (msg 2215); C2S 2/142 op-byte=deposit
        GD.Print("[HudStorageWindow] Deposit money (action 263). " +
                 $"TODO(world-campaign): gold gate ≥1,000,000; number-entry dialog (msg {MsgDeposit}); " +
                 "C2S 2/142. spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    private void OnWithdraw()
    {
        // action 264 — open number-entry dialog (withdraw mode)
        // spec: ui_system.md §8.32.4 — "264: if stored money > 0 → number-entry dialog (withdraw); else msg 45023"
        // TODO(world-campaign): check stored gold; number-entry dialog (msg 2216); C2S 2/142 op-byte=withdraw
        GD.Print("[HudStorageWindow] Withdraw money (action 264). " +
                 $"TODO(world-campaign): stored>0 gate; number-entry dialog (msg {MsgWithdraw}); " +
                 "C2S 2/142. spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            // spec: ui_system.md §8.32.4 — "ESC (key 27, when visible): close (same as 260)"
            OnClose();
            GetViewport().SetInputAsHandled();
        }
    }
}