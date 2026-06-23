using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudDeliveryWindow : Control
{
    private const int GridCols = 5;
    private const int GridRows = 8;
    private const int GridCellCount = GridCols * GridRows;

    private const int CellActionBase = 500;

    private const int TabActionBase = 573;
    private const int TabActionCount = 6;

    private const int ScrollThumb = 583;
    private const int ScrollDown = 584;
    private const int ScrollUp = 585;

    private const int QtyDec = 601;
    private const int QtyInc = 602;

    private const int ClaimPrepBase = 565;
    private const int ClaimPrepCount = 8;

    private const int ViewSwitchBase = 541;
    private const int ViewSwitchCount = 8;

    private const int SelectAllAction = 580;

    private const int Msg40010 = 40010;
    private const int Msg16005 = 16005;
    private const int Msg55016 = 55016;

    private const float PanelW = 380f;
    private const float PanelH = 550f;


    private bool _open;
    private int _selectedCell = -1;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudDeliveryWindow";

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

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        AddChild(new Label
        {
            Name = "Title",
            Text = "Delivery Box",
            Position = new Vector2(10f, 8f),
            MouseFilter = MouseFilterEnum.Ignore
        });

        string[] tabLabels = { "Own", "Others", "Sale", "Tab4", "Tab5", "Tab6" };
        for (var i = 0; i < TabActionCount; i++)
        {
            var capturedAction = TabActionBase + i;
            var tab = new Button
            {
                Name = $"Tab{i}",
                Text = tabLabels[i],
                Position = new Vector2(10f + i * 58f, 30f),
                Size = new Vector2(52f, 22f),
                MouseFilter = MouseFilterEnum.Stop
            };
            tab.Pressed += () => OnTabPressed(capturedAction);
            AddChild(tab);
        }

        const float cellSize = 44f;
        const float cellStride = 46f;
        const float gridX = 10f;
        const float gridY = 60f;

        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var cellIdx = row * GridCols + col;
            var actionId = CellActionBase + cellIdx;
            var capturedIdx = cellIdx;

            var cell = new Button
            {
                Name = $"Cell{actionId}",
                Text = string.Empty,
                Position = new Vector2(gridX + col * cellStride, gridY + row * cellStride),
                Size = new Vector2(cellSize, cellSize),
                MouseFilter = MouseFilterEnum.Stop
            };
            cell.Pressed += () => OnCellPressed(capturedIdx);
            AddChild(cell);
        }

        var scrollUp = new Button
        {
            Name = "ScrollUp",
            Text = "▲",
            Position = new Vector2(PanelW - 26f, gridY),
            Size = new Vector2(18f, 18f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollUp.Pressed += () => OnScrollUp();
        AddChild(scrollUp);

        var scrollDown = new Button
        {
            Name = "ScrollDown",
            Text = "▼",
            Position = new Vector2(PanelW - 26f, gridY + GridRows * cellStride - 20f),
            Size = new Vector2(18f, 18f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollDown.Pressed += () => OnScrollDown();
        AddChild(scrollDown);

        AddChild(new Label
        {
            Text = "Qty:",
            Position = new Vector2(10f, PanelH - 80f),
            MouseFilter = MouseFilterEnum.Ignore
        });
        var qtyDec = new Button
        {
            Name = "QtyDec",
            Text = "-",
            Position = new Vector2(40f, PanelH - 82f),
            Size = new Vector2(22f, 22f),
            MouseFilter = MouseFilterEnum.Stop
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
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(qtyDisplay);

        var qtyInc = new Button
        {
            Name = "QtyInc",
            Text = "+",
            Position = new Vector2(100f, PanelH - 82f),
            Size = new Vector2(22f, 22f),
            MouseFilter = MouseFilterEnum.Stop
        };
        qtyInc.Pressed += () => OnQty(QtyInc);
        AddChild(qtyInc);

        var claimBtn = new Button
        {
            Name = "ClaimBtn",
            Text = "Claim",
            Position = new Vector2(140f, PanelH - 82f),
            Size = new Vector2(100f, 30f),
            MouseFilter = MouseFilterEnum.Stop
        };
        claimBtn.Pressed += OnClaim;
        AddChild(claimBtn);

        var selectAllBtn = new Button
        {
            Name = "SelectAllBtn",
            Text = "Select All",
            Position = new Vector2(250f, PanelH - 82f),
            Size = new Vector2(80f, 30f),
            MouseFilter = MouseFilterEnum.Stop
        };
        selectAllBtn.Pressed += () =>
        {
            GD.Print($"[HudDeliveryWindow] Select-all (action {SelectAllAction}). spec: §8.21.5.");
        };
        AddChild(selectAllBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(PanelW - 28f, 8f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        AddChild(new Label
        {
            Name = "DeliveryListStub",
            Text = string.Empty,
            Position = new Vector2(10f, PanelH - 48f),
            Size = new Vector2(PanelW - 20f, 40f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        });

        GD.Print("[HudDeliveryWindow] Built — DeliveryPanel slot 40 (5×8 grid 40 cells, action 500..539). " +
                 "Tabs 573..578, scroll 583/584/585, qty 601/602, claim 565..572. " +
                 "Outbound C2S 2/71 CmsgDeliveryClaim = TODO(world-campaign). " +
                 "Delivery list = TODO(capture). " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.");
    }


    private void OnTabPressed(int actionId)
    {
        GD.Print($"[HudDeliveryWindow] Tab action {actionId}. spec: §8.21.5.");
    }

    private void OnCellPressed(int cellIdx)
    {
        _selectedCell = cellIdx;
        GD.Print($"[HudDeliveryWindow] Cell selected: {cellIdx} (action {CellActionBase + cellIdx}). " +
                 "spec: §8.21.5 CODE-CONFIRMED.");
    }

    private void OnScrollUp()
    {
        GD.Print($"[HudDeliveryWindow] Scroll up (action {ScrollUp}). spec: §8.21.5.");
    }

    private void OnScrollDown()
    {
        GD.Print($"[HudDeliveryWindow] Scroll down (action {ScrollDown}). spec: §8.21.5.");
    }

    private void OnQty(int actionId)
    {
        GD.Print($"[HudDeliveryWindow] Qty action {actionId}. spec: §8.21.5.");
    }

    private void OnClaim()
    {
        if (_selectedCell < 0)
        {
            GD.PrintErr($"[HudDeliveryWindow] No cell selected for claim. msg.xdb {Msg16005}. spec: §8.21.5.");
            return;
        }

        GD.Print($"[HudDeliveryWindow] Claim intent: cell={_selectedCell}. " +
                 "TODO(world-campaign): C2S 2/71 CmsgDeliveryClaim (4B). " +
                 $"No-bag-slot caption: msg.xdb {Msg55016}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.5 CODE-CONFIRMED.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        if (_open) _selectedCell = -1;
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