
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudKeepNpcDialog : Control
{

    private const float PanelW = 167f;
    private const float PanelH = 239f;

    private const float TopBtnX = 37f;
    private const float TopBtnY = 37f;
    private const float TopBtnW = 90f;
    private const float TopBtnH = 25f;

    private const float SvcBtnX = 25f;
    private const float SvcBtnW = 106f;
    private const float SvcBtnH = 40f;

    private const int BackdropTexId = 4;
    private const int DialogTexId = 2;
    private static readonly float[] SvcBtnY = { 69f, 109f, 149f, 189f };
    private uint _activeNpcId;


    private Action? _onOpenStorage;
    private Action? _onVendor;


    private bool _open;


    public void Wire(Action? onOpenStorage, Action? onVendor = null)
    {
        _onOpenStorage = onOpenStorage;
        _onVendor = onVendor;
    }


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudKeepNpcDialog";

        Size = new Vector2(PanelW, PanelH);
        Position = new Vector2(420f, 240f);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var bd = new Panel { Name = "Backdrop" };
        bd.Position = Vector2.Zero;
        bd.Size = new Vector2(PanelW, PanelH);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.07f, 0.05f, 0.92f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.15f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        var topBtn = new Button
        {
            Name = "OpenStorageBtn",
            Text = "Open Storage",
            Position = new Vector2(TopBtnX, TopBtnY),
            Size = new Vector2(TopBtnW, TopBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        topBtn.Pressed += () => OnSelector(1);
        AddChild(topBtn);

        var btnDialog = new Button
        {
            Name = "BtnDialog",
            Text = "Talk",
            Position = new Vector2(SvcBtnX, SvcBtnY[1]),
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btnDialog.Pressed += () => OnSelector(0);
        AddChild(btnDialog);

        var btnKeep = new Button
        {
            Name = "BtnKeepService",
            Text = "Service",
            Position = new Vector2(SvcBtnX, SvcBtnY[0]),
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btnKeep.Pressed += () => OnSelector(2);
        AddChild(btnKeep);

        var btnQuest = new Button
        {
            Name = "BtnQuest",
            Text = "Quest",
            Position = new Vector2(SvcBtnX, SvcBtnY[2]),
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btnQuest.Pressed += () => OnSelector(3);
        AddChild(btnQuest);

        var btnClose = new Button
        {
            Name = "BtnClose",
            Text = "Close",
            Position = new Vector2(SvcBtnX, SvcBtnY[3]),
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btnClose.Pressed += () => OnSelector(4);
        AddChild(btnClose);

        GD.Print("[HudKeepNpcDialog] Built — KeepNpcPanel slot 152. " +
                 "5-option NPC dialog menu: sel0=dialog sel1=open-storage(→slot 191) " +
                 "sel2=keep-service(TODO world-campaign) sel3=quest(TODO world-campaign) sel4=close. " +
                 "Atlas: uitex 4 (backdrop/top-btn) + uitex 2 (dialog/quest/close) VFS-pending. " +
                 "Emits NO wire packet from this panel. spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED.");
    }


    public void Open(uint npcId = 0)
    {
        _activeNpcId = npcId;
        _open = true;
        Visible = true;
        GD.Print($"[HudKeepNpcDialog] Opened for NPC {npcId}. spec: Docs/RE/specs/ui_system.md §8.27.");
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        GD.Print("[HudKeepNpcDialog] Closed. spec: Docs/RE/specs/ui_system.md §8.27.3 sel 4.");
    }


    private void OnSelector(int sel)
    {
        switch (sel)
        {
            case 0:
                if (_onVendor is not null)
                    _onVendor();
                else
                    GD.Print("[HudKeepNpcDialog] sel 0 = NPC dialog: TODO(world-campaign). " +
                             "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 0.");
                Close();
                break;

            case 1:
                if (_onOpenStorage is not null)
                    _onOpenStorage();
                else
                    GD.Print("[HudKeepNpcDialog] sel 1 = open storage: no delegate wired. " +
                             "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 1.");
                Close();
                break;

            case 2:
                GD.Print("[HudKeepNpcDialog] sel 2 = keep-service list (slot 182): TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 2.");
                Close();
                break;

            case 3:
                GD.Print("[HudKeepNpcDialog] sel 3 = quest offer (slot 215): TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 3.");
                Close();
                break;

            case 4:
                Close();
                break;

            default:
                GD.Print($"[HudKeepNpcDialog] sel {sel} ≥ 5: no-op. " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3.");
                break;
        }
    }


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}