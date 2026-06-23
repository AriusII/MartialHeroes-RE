using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudTenderWindow : Control
{
    private const float TenderW = 512f;
    private const float TenderH = 595f;

    private const int DetailRequestCap = 30;

    private const int MsgNotEnoughGold = 66006;
    private const int MsgDetailOverCap = 66007;
    private int _detailRequestCount;


    private bool _open;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudTenderWindow";

        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -TenderW / 2f;
        OffsetTop = -TenderH / 2f;
        OffsetRight = TenderW / 2f;
        OffsetBottom = TenderH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.05f, 0.08f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.40f, 0.55f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        AddChild(new Label
        {
            Name = "Title",
            Text = "Consignment Purchase",
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        });

        var previewPlaceholder = new ColorRect
        {
            Name = "ItemPreview3D_TODO",
            Color = new Color(0.10f, 0.08f, 0.14f, 0.7f),
            Position = new Vector2(10f, 40f),
            Size = new Vector2(100f, 80f)
        };
        AddChild(previewPlaceholder);
        AddChild(new Label
        {
            Name = "PreviewLbl",
            Text = string.Empty,
            Position = new Vector2(10f, 42f),
            Size = new Vector2(200f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        });

        var listStub = new Label
        {
            Name = "TenderListStub",
            Text = string.Empty,
            Position = new Vector2(10f, 140f),
            Size = new Vector2(492f, 60f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(listStub);

        AddChild(new Label
        {
            Name = "ItemStatStub",
            Text = "Item info: (populated from tender listing)",
            Position = new Vector2(10f, 220f),
            MouseFilter = MouseFilterEnum.Ignore
        });

        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Confirm Purchase",
            Position = new Vector2(10f, 530f),
            Size = new Vector2(160f, 35f),
            MouseFilter = MouseFilterEnum.Stop
        };
        confirmBtn.Pressed += OnConfirm;
        AddChild(confirmBtn);

        var detailBtn = new Button
        {
            Name = "DetailBtn",
            Text = "Request Detail",
            Position = new Vector2(180f, 530f),
            Size = new Vector2(140f, 35f),
            MouseFilter = MouseFilterEnum.Stop
        };
        detailBtn.Pressed += OnRequestDetail;
        AddChild(detailBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(TenderW - 30f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        GD.Print("[HudTenderWindow] Built — TenderInfoPanel slot 118 (512×595, screen-centred). " +
                 "Action 0=confirm→C2S 2/118 TODO(world-campaign). Detail cap=30 (msg 66007). " +
                 "Tender listing = TODO(capture). spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.");
    }


    private void OnConfirm()
    {
        GD.Print("[HudTenderWindow] Confirm purchase → TODO(world-campaign): C2S 2/118 CmsgTenderConfirm. " +
                 $"Not-enough-gold path: msg.xdb {MsgNotEnoughGold}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.");
    }

    private void OnRequestDetail()
    {
        if (_detailRequestCount >= DetailRequestCap)
        {
            GD.PrintErr($"[HudTenderWindow] Detail request over cap ({DetailRequestCap}). " +
                        $"msg.xdb {MsgDetailOverCap}. spec: §8.21.1 CODE-CONFIRMED.");
            return;
        }

        _detailRequestCount++;
        GD.Print($"[HudTenderWindow] Detail requested ({_detailRequestCount}/{DetailRequestCap}). " +
                 "TODO(world-campaign): detail via InfoPanel category builder. spec: §8.21.1.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        if (_open)
            _detailRequestCount = 0;
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