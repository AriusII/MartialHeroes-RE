using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudTradeWindow : Control
{
    private const float TradeW = 318f;
    private const float TradeH = 732f;

    private const int GridCols = 6;
    private const int GridRows = 10;
    private const int GridCellCount = GridCols * GridRows;
    private const int CellSide = 38;
    private const float GridOriginX = 45f;
    private const float GridOriginY = 162f;

    private const int CellActionBase = 200;

    private const int MainTexId = 4;
    private const int BottomTexId = 2;

    private const int MsgAddMoney = 2213;
    private const int MsgSetMoney = 2214;
    private const byte PhaseCancel = 0;
    private const byte PhaseReady = 1;
    private const byte PhaseCommit = 4;

    private readonly Panel[] _gridCells = new Panel[GridCellCount];
    private readonly Label[] _cellLabels = new Label[GridCellCount];
    private readonly uint[] _myCells = new uint[GridCellCount];
    private readonly uint[] _theirCells = new uint[GridCellCount];
    private int _activeSide;

    private ChannelReader<TradeSlotUpdatedEvent>? _tradeSlots;
    private ChannelReader<TradeSessionPhaseEvent>? _tradeSessions;
    private Label? _moneyLabel;
    private Label? _lockLabel;
    private long _myCoin;
    private long _theirCoin;
    private byte _myPhase;
    private byte _theirPhase;


    private bool _open;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudTradeWindow";

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = TradeW;
        OffsetTop = 0f;
        OffsetRight = TradeW + TradeW;
        OffsetBottom = TradeH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.5f, 0.35f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var mainTex = atlas.GetById(MainTexId);
        if (mainTex is null)
            GD.PrintErr("[HudTradeWindow] tradekeepwindow.dds (uitex 4) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.13.");

        if (mainTex is not null)
        {
            var frameSlice = atlas.SliceById(MainTexId, 317, 0, 318, 625);
            if (frameSlice is not null)
            {
                var frameImg = new TextureRect
                {
                    Name = "MainFrame",
                    Texture = frameSlice,
                    Position = new Vector2(0f, 85f),
                    Size = new Vector2(318f, 625f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(frameImg);
            }
        }

        if (atlas.GetById(BottomTexId) is not null)
        {
            var footerSlice = atlas.SliceById(BottomTexId, 0, 683, 318, 50);
            if (footerSlice is not null)
            {
                var footerImg = new TextureRect
                {
                    Name = "FooterStrip",
                    Texture = footerSlice,
                    Position = new Vector2(0f, 36f),
                    Size = new Vector2(318f, 50f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(footerImg);
            }
        }

        var sideA = new Button
        {
            Name = "SideA",
            Text = "My offer",
            Position = new Vector2(25f, 105f),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        sideA.Pressed += () => SetActiveSide(0);
        AddChild(sideA);

        var sideB = new Button
        {
            Name = "SideB",
            Text = "Their offer",
            Position = new Vector2(90f, 105f),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        sideB.Pressed += () => SetActiveSide(1);
        AddChild(sideB);

        _moneyLabel = new Label
        {
            Name = "MoneyLabel",
            Text = "0",
            Position = new Vector2(51f, 598f),
            Size = new Vector2(128f, 15f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_moneyLabel);

        _lockLabel = new Label
        {
            Name = "LockLabel",
            Text = "",
            Position = new Vector2(25f, 128f),
            Size = new Vector2(268f, 15f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_lockLabel);

        var addMoneyCaption = text.GetCaption(MsgAddMoney, $"[{MsgAddMoney}]");
        var addMoneyBtn = new Button
        {
            Name = "AddMoneyBtn",
            Text = addMoneyCaption,
            Position = new Vector2(183f, 592f),
            Size = new Vector2(53f, 22f),
            MouseFilter = MouseFilterEnum.Stop
        };
        addMoneyBtn.Pressed += () => OnAddMoney();
        AddChild(addMoneyBtn);

        var setMoneyCaption = text.GetCaption(MsgSetMoney, $"[{MsgSetMoney}]");
        var setMoneyBtn = new Button
        {
            Name = "SetMoneyBtn",
            Text = setMoneyCaption,
            Position = new Vector2(238f, 592f),
            Size = new Vector2(53f, 22f),
            MouseFilter = MouseFilterEnum.Stop
        };
        setMoneyBtn.Pressed += () => OnSetMoney();
        AddChild(setMoneyBtn);

        var lockBtn = new Button
        {
            Name = "LockBtn",
            Text = "LOCK",
            Position = new Vector2(259f, 655f),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        lockBtn.Pressed += () => OnLockCommit();
        AddChild(lockBtn);

        BuildTradeGrid(atlas);

        GD.Print("[HudTradeWindow] Built — 318×732 right-anchored KeepPanel/TradeKeepWindow. " +
                 "ONE 60-cell grid (10×6, 38×38, origin 45,162, actions 200..259), toggled my/their. " +
                 "Inbound S2C 4/24 + 4/25 WIRED via BindHub (TradeSlots/TradeSessions channels): cells fill " +
                 "by OwnerId side discriminator + SlotIndex + ItemId + Category; money/lock from 4/25 Phase + Coin. " +
                 "Money-amount column (4/24 Category 0xFF) and 4/25 commit record tail = capture-pending, NOT decoded. " +
                 "Outbound C2S 2/23/24/25: TODO(world-campaign). " +
                 "Open trigger: S2C 4/23 phase=3 (no hotkey). " +
                 "spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.");
    }

    private void BuildTradeGrid(HudAtlasLibrary atlas)
    {
        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var idx = row * GridCols + col;
            var x = GridOriginX + col * CellSide;
            var y = GridOriginY + row * CellSide;

            var cell = new Panel
            {
                Name = $"TradeCell{CellActionBase + idx}",
                Position = new Vector2(x, y),
                Size = new Vector2(CellSide, CellSide),
                MouseFilter = MouseFilterEnum.Stop
            };
            var cs = new StyleBoxFlat();
            cs.BgColor = new Color(0.06f, 0.08f, 0.07f, 0.75f);
            cs.SetBorderWidthAll(1);
            cs.BorderColor = new Color(0.3f, 0.45f, 0.3f, 0.65f);
            cell.AddThemeStyleboxOverride("panel", cs);
            AddChild(cell);
            _gridCells[idx] = cell;

            var cellLabel = new Label
            {
                Name = $"TradeCellLabel{CellActionBase + idx}",
                Text = "",
                Position = new Vector2(2f, 2f),
                Size = new Vector2(CellSide - 4f, CellSide - 4f),
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            cell.AddChild(cellLabel);
            _cellLabels[idx] = cellLabel;
        }
    }


    private void SetActiveSide(int side)
    {
        _activeSide = side;
        RenderActiveSide();
        RenderMoneyAndLock();
        GD.Print($"[HudTradeWindow] Side toggle → side={side} (0=my-offer, 1=their-offer) — " +
                 "repopulated from live trade state. spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    public void BindHub(IHudEventHub hub)
    {
        _tradeSlots = hub.TradeSlots;
        _tradeSessions = hub.TradeSessions;
        GD.Print("[HudTradeWindow] BindHub: TradeSlots (S2C 4/24 SmsgUserTradeSlotUpdate) + TradeSessions " +
                 "(S2C 4/25 SmsgUserTradeFullResponse) channels connected. Cells fill from confirmed fields " +
                 "(OwnerId side discriminator @0x28, SlotIndex @0x24, ItemId @0xC, Category @0xA); money/lock " +
                 "from 4/25 Phase @0x08 + Coin @0x0C. Money-amount column on 4/24 (Category 0xFF) and the 4/25 " +
                 "commit record tail stay capture-pending and are NOT decoded. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13; packets/4-24_trade_slot_update.yaml; " +
                 "packets/4-25_trade_full_response.yaml.");
    }

    public override void _Process(double delta)
    {
        if (_tradeSlots is not null)
            while (_tradeSlots.TryRead(out var slot))
                if (slot is not null)
                    ApplyTradeSlot(slot);

        if (_tradeSessions is not null)
            while (_tradeSessions.TryRead(out var session))
                if (session is not null)
                    ApplyTradeSession(session);
    }

    private void ApplyTradeSlot(TradeSlotUpdatedEvent slot)
    {
        if (slot.IsMoneySlot) return;
        if (slot.SlotIndex >= GridCellCount) return;
        if (!slot.Apply) return;

        var target = slot.IsLocalSide ? _myCells : _theirCells;
        target[slot.SlotIndex] = slot.ItemId;

        if ((slot.IsLocalSide ? 0 : 1) == _activeSide)
            RenderCell(slot.SlotIndex);
    }

    private void ApplyTradeSession(TradeSessionPhaseEvent session)
    {
        switch (session.Phase)
        {
            case PhaseCancel:
                Array.Clear(_myCells, 0, _myCells.Length);
                Array.Clear(_theirCells, 0, _theirCells.Length);
                _myCoin = 0;
                _theirCoin = 0;
                _myPhase = PhaseCancel;
                _theirPhase = PhaseCancel;
                RenderActiveSide();
                break;

            default:
                if (session.IsLocalSide)
                {
                    _myCoin = session.Coin;
                    _myPhase = session.Phase;
                }
                else
                {
                    _theirCoin = session.Coin;
                    _theirPhase = session.Phase;
                }

                break;
        }

        RenderMoneyAndLock();
    }

    private void RenderActiveSide()
    {
        for (var i = 0; i < GridCellCount; i++)
            RenderCell(i);
    }

    private void RenderCell(int index)
    {
        if (_cellLabels[index] is not { } label) return;
        var itemId = (_activeSide == 0 ? _myCells : _theirCells)[index];
        label.Text = itemId == 0u ? "" : itemId.ToString();
    }

    private void RenderMoneyAndLock()
    {
        var coin = _activeSide == 0 ? _myCoin : _theirCoin;
        if (_moneyLabel is not null) _moneyLabel.Text = coin.ToString();

        if (_lockLabel is null) return;
        var myState = PhaseText(_myPhase);
        var theirState = PhaseText(_theirPhase);
        _lockLabel.Text = $"me: {myState}  /  them: {theirState}";
    }

    private static string PhaseText(byte phase)
    {
        return phase switch
        {
            PhaseReady => "ready",
            PhaseCommit => "commit",
            _ => "open"
        };
    }


    private void OnAddMoney()
    {
        GD.Print("[HudTradeWindow] action 263 = add-money → TODO(world-campaign): C2S 2/24. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    private void OnSetMoney()
    {
        GD.Print("[HudTradeWindow] action 264 = set-money → TODO(world-campaign): C2S 2/24. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    private void OnLockCommit()
    {
        GD.Print("[HudTradeWindow] action 260 = lock/commit → TODO(world-campaign): C2S 2/25 CmsgTradeConfirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -TradeW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = TradeW;
            OffsetRight = TradeW + TradeW;
        }

        Visible = _open;
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