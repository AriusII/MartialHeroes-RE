
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudStorageWindow : Control
{

    private const float PanelW = 318f;
    private const float PanelH = 732f;

    private const int Cols = 6;
    private const int Rows = 10;
    private const int TotalCells = 60;
    private const float CellSize = 38f;
    private const float GridOriginX = 45f;
    private const float GridOriginY = 162f;
    private const int CellActionBase = 200;

    private const float BackdropY = 85f;
    private const float TitleBarY = 36f;
    private const float PageTabAX = 25f;
    private const float PageTabAY = 105f;
    private const float PageTabBX = 90f;
    private const float PageTabBY = 105f;
    private const float MoneyBtnW = 53f;
    private const float MoneyBtnH = 22f;
    private const float MoneyBtnY = 592f;
    private const float DepositX = 183f;
    private const float WithdrawX = 238f;
    private const float CloseX = 259f;
    private const float CloseY = 655f;
    private const float CloseW = 59f;
    private const float CloseH = 77f;

    private const int PrimaryTexId = 2;
    private const int SecondaryTexId = 4;
    private const int LabelTexId = 14;
    private const int CellIconTexId = 78;

    private const int MsgTabA = 2213;
    private const int MsgTabB = 2214;
    private const int MsgDeposit = 2215;
    private const int MsgWithdraw = 2216;
    private const int MsgCannotStore = 2142;
    private const int MsgQuestGuard = 38004;
    private const int MsgNotEnoughGold = 45023;

    private const int StorageSlotBase = 56;
    private const int SlotPerPage = 60;

    private readonly Button[] _cellBtns = new Button[TotalCells];
    private int _activePage;


    private bool _open;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudStorageWindow";

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

        var bd = new Panel { Name = "Backdrop" };
        bd.Position = new Vector2(0f, BackdropY);
        bd.Size = new Vector2(PanelW, 625f);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.3f, 0.55f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        var title = new Panel { Name = "TitleBar" };
        title.Position = new Vector2(0f, TitleBarY);
        title.Size = new Vector2(PanelW, 50f);
        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.12f, 0.10f, 0.07f, 0.95f);
        title.AddThemeStyleboxOverride("panel", titleStyle);
        AddChild(title);

        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Storage",
            Position = new Vector2(10f, TitleBarY + 16f),
            Size = new Vector2(180f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

        var tabA = new Button
        {
            Name = "TabA",
            Text = text.GetCaption(MsgTabA, "Page 1"),
            Position = new Vector2(PageTabAX, PageTabAY),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabA.Pressed += () => OnPageTab(0);
        AddChild(tabA);

        var tabB = new Button
        {
            Name = "TabB",
            Text = text.GetCaption(MsgTabB, "Page 2"),
            Position = new Vector2(PageTabBX, PageTabBY),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabB.Pressed += () => OnPageTab(1);
        AddChild(tabB);

        for (var i = 0; i < TotalCells; i++)
        {
            var col = i % Cols;
            var row = i / Cols;
            var cx = GridOriginX + col * CellSize;
            var cy = GridOriginY + row * CellSize;
            var actionId = CellActionBase + i;

            var cell = new Button
            {
                Name = $"Cell{i:D2}",
                Text = "",
                Position = new Vector2(cx, cy),
                Size = new Vector2(CellSize, CellSize),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            var captured = i;
            cell.Pressed += () => OnCellClick(captured);
            AddChild(cell);
            _cellBtns[i] = cell;
        }

        var infoLbl = new Label
        {
            Name = "InfoLabel",
            Text = "",
            Position = new Vector2(51f, 598f),
            Size = new Vector2(128f, 15f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(infoLbl);

        var depositBtn = new Button
        {
            Name = "DepositBtn",
            Text = text.GetCaption(MsgDeposit, "Deposit"),
            Position = new Vector2(DepositX, MoneyBtnY),
            Size = new Vector2(MoneyBtnW, MoneyBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        depositBtn.Pressed += OnDeposit;
        AddChild(depositBtn);

        var withdrawBtn = new Button
        {
            Name = "WithdrawBtn",
            Text = text.GetCaption(MsgWithdraw, "Withdraw"),
            Position = new Vector2(WithdrawX, MoneyBtnY),
            Size = new Vector2(MoneyBtnW, MoneyBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        withdrawBtn.Pressed += OnWithdraw;
        AddChild(withdrawBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "OK",
            Position = new Vector2(CloseX, CloseY),
            Size = new Vector2(CloseW, CloseH),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += OnClose;
        AddChild(closeBtn);

        GD.Print("[HudStorageWindow] Built — KeepPanel slot 191 (60-cell 10×6 storage grid). " +
                 "Cell grid: x=45+38·col, y=162+38·row, actions 200..259. " +
                 "Page tabs 261/262; deposit 263; withdraw 264; close 260. " +
                 "Open: ONLY via KeepNpcPanel sel 1 + C2S 2/142. No hotkey. " +
                 "Item move: TODO(world-campaign): C2S 2/46 (move) + 2/44 (quick-move). " +
                 "Storage contents: TODO(capture): shared item-panel S2C via unified-slot base +56. " +
                 "spec: Docs/RE/specs/ui_system.md §8.32 CODE-CONFIRMED.");
    }


    public void Open()
    {
        _open = true;
        _activePage = 0;
        Visible = true;
        GD.Print("[HudStorageWindow] Open → TODO(world-campaign): C2S 2/142 (storage open request). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.5/§8.32.6 CODE-CONFIRMED.");
    }

    private void OnClose()
    {
        _open = false;
        Visible = false;
        GD.Print("[HudStorageWindow] Closed (action 260). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }


    private void OnPageTab(int page)
    {
        _activePage = page;
        GD.Print($"[HudStorageWindow] Page tab {page} (action {261 + page}). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    private void OnCellClick(int cellIndex)
    {
        var action = CellActionBase + cellIndex;
        var unifiedSlot = action + SlotPerPage * _activePage + StorageSlotBase;
        GD.Print($"[HudStorageWindow] Cell {cellIndex} clicked (action {action}, " +
                 $"unified slot {unifiedSlot}, page {_activePage}). " +
                 "TODO(world-campaign): C2S 2/46 (move) or 2/44 (quick-move). " +
                 "spec: Docs/RE/specs/ui_system.md §8.32.4 CODE-CONFIRMED.");
    }

    private void OnDeposit()
    {
        GD.Print("[HudStorageWindow] Deposit money (action 263). " +
                 $"TODO(world-campaign): gold gate ≥1,000,000; number-entry dialog (msg {MsgDeposit}); " +
                 "C2S 2/142. spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }

    private void OnWithdraw()
    {
        GD.Print("[HudStorageWindow] Withdraw money (action 264). " +
                 $"TODO(world-campaign): stored>0 gate; number-entry dialog (msg {MsgWithdraw}); " +
                 "C2S 2/142. spec: Docs/RE/specs/ui_system.md §8.32.4.");
    }


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            OnClose();
            GetViewport().SetInputAsHandled();
        }
    }
}