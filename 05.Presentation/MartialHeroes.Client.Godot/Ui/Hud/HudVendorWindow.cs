using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudVendorWindow : Control
{
    private const float BackdropW = 360f;
    private const float BackdropH = 280f;

    private const float CloseBtnX = 135f;
    private const float CloseBtnY = 200f;
    private const float CloseBtnW = 90f;
    private const float CloseBtnH = 25f;

    private const float BuyBtnX = 264f;
    private const float BuyBtnW = 54f;
    private const float BuyBtnH = 25f;

    private const float NameLblX = 114f;
    private const float NameLblW = 100f;
    private const float NameLblH = 15f;

    private const float StatusLblX = 210f;
    private const float StatusLblY = 170f;

    private const int MsgMoneyLabel = 45015;
    private const int MsgItemPrice = 45016;
    private const int MsgShopFail = 45020;

    private static readonly int[] RowY = { 70, 100, 130 };
    private readonly Label[] _rowLabels = new Label[3];
    private Label? _moneyLabel;
    private uint _npcId;


    private bool _open;
    private int _selectedRow = -1;
    private Label? _statusLabel;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudVendorWindow";

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

        var title = new Label
        {
            Name = "Title",
            Text = "상점",
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(title);

        var moneyCaption = text?.GetCaption(MsgMoneyLabel, "[금액]") ?? "[금액]";
        _moneyLabel = new Label
        {
            Name = "MoneyLabel",
            Text = moneyCaption,
            Position = new Vector2(10f, 40f),
            Size = new Vector2(200f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_moneyLabel);

        for (var i = 0; i < RowY.Length; i++)
        {
            var rowIdx = i;
            float rowY = RowY[i];

            _rowLabels[i] = new Label
            {
                Name = $"RowLabel{i}",
                Text = string.Empty,
                Position = new Vector2(NameLblX, rowY + 5f),
                Size = new Vector2(NameLblW, NameLblH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(_rowLabels[i]);

            var buyBtn = new Button
            {
                Name = $"BuyBtn{i}",
                Text = "구매",
                Position = new Vector2(BuyBtnX, rowY),
                Size = new Vector2(BuyBtnW, BuyBtnH),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedRow = i;
            buyBtn.Pressed += () => OnRowSelect(capturedRow);
            AddChild(buyBtn);
        }

        _statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = string.Empty,
            Position = new Vector2(StatusLblX, StatusLblY),
            Size = new Vector2(100f, 15f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_statusLabel);


        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "닫기",
            Position = new Vector2(CloseBtnX, CloseBtnY),
            Size = new Vector2(CloseBtnW, CloseBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => { Close(); };
        AddChild(closeBtn);

        GD.Print("[HudVendorWindow] Built — NPC vendor slot 259 (3 visible buy-rows, money label, " +
                 "close/confirm btn). " +
                 "Net sends C2S 2/19/2/20/2/115 = TODO(world-campaign). " +
                 "Stock = TODO(capture). " +
                 "spec: Docs/RE/specs/ui_system.md §8.22 CODE-CONFIRMED.");
    }


    public void Open(uint npcId = 0)
    {
        _npcId = npcId;
        _selectedRow = -1;
        _open = true;
        Visible = true;


        GD.Print($"[HudVendorWindow] Open — npcId={npcId}. " +
                 "TODO(world-campaign): populate from real shop data + read player gold (no placeholders). " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.5 CODE-CONFIRMED.");
    }

    public void Toggle(bool show)
    {
        _open = show;
        Visible = show;
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        GD.Print("[HudVendorWindow] Closed (action 0). " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.4 CODE-CONFIRMED.");
    }


    private void OnRowSelect(int rowIdx)
    {
        _selectedRow = rowIdx;
        var actionId = 100 + rowIdx;


        GD.Print($"[HudVendorWindow] Row selected: {rowIdx} (action {actionId}). " +
                 "TODO(world-campaign): C2S CmsgShopBuy (2/115) on confirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.22.4 CODE-CONFIRMED.");
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}