
using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudCharacterStatsPanel : Control
{

    private const float StatsX = 180f;
    private const float StatsY = 95f;
    private const float StatsW = 130f;
    private const float StatsH = 196f;

    private const int StatActionStart = 300;
    private const int StatActionCount = 13;

    private const int StatNameMsgStart = 60001;
    private const int StatNameMsgEnd = 60022;

    private const int InvTexId = 2;
    private const int TradeTexId = 4;


    private readonly Label[] _statValueLabels = new Label[StatNameMsgEnd - StatNameMsgStart + 1];
    private Label _charLevelLabel = null!;
    private Label _charNameLabel = null!;
    private ClientContext? _ctx;
    private ChannelReader<StatAllocationView>? _statAllocations;


    private bool _visible;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext ctx)
    {
        Name = "HudCharacterStatsPanel";
        _ctx = ctx;

        Position = new Vector2(StatsX, StatsY);
        Size = new Vector2(StatsW * 3f + 10f, StatsH + 60f);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var mainChrome = atlas.GetById(InvTexId);
        var borderChrome = atlas.GetById(TradeTexId);
        if (mainChrome is null)
            GD.PrintErr("[HudCharacterStatsPanel] inventwindow.dds (uitex 2) unavailable. " +
                        "spec: Docs/RE/specs/ui_system.md §8.7.");
        if (borderChrome is null)
            GD.PrintErr("[HudCharacterStatsPanel] tradekeepwindow.dds (uitex 4) unavailable. " +
                        "spec: Docs/RE/specs/ui_system.md §8.7.");

        var bg = new Panel { Name = "Bg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.93f);
        bgStyle.SetBorderWidthAll(2);
        bgStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        if (mainChrome is not null)
        {
            var chromeTex = new TextureRect
            {
                Name = "Chrome",
                Texture = mainChrome,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore
            };
            chromeTex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chromeTex);
        }

        if (borderChrome is not null)
        {
            var borderTex = new TextureRect
            {
                Name = "BorderStrip",
                Texture = borderChrome,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore
            };
            borderTex.AnchorLeft = 0f;
            borderTex.AnchorTop = 0f;
            borderTex.AnchorRight = 1f;
            borderTex.AnchorBottom = 0f;
            borderTex.OffsetTop = 0f;
            borderTex.OffsetBottom = 16f;
            AddChild(borderTex);
        }

        _charNameLabel = new Label
        {
            Name = "CharName",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _charNameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _charNameLabel.OffsetLeft = 6f;
        _charNameLabel.OffsetTop = 18f;
        _charNameLabel.OffsetRight = 200f;
        _charNameLabel.OffsetBottom = 34f;
        AddChild(_charNameLabel);

        _charLevelLabel = new Label
        {
            Name = "CharLevel",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _charLevelLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _charLevelLabel.OffsetLeft = -60f;
        _charLevelLabel.OffsetTop = 18f;
        _charLevelLabel.OffsetRight = -4f;
        _charLevelLabel.OffsetBottom = 34f;
        AddChild(_charLevelLabel);

        BuildStatSubPanel("PanelA", 0f, 36f, StatsW, StatsH,
            StatNameMsgStart, StatNameMsgStart + 8, text, ctx);
        BuildStatSubPanel("PanelB", StatsW + 5f, 36f, StatsW, StatsH,
            StatNameMsgStart + 9, StatNameMsgStart + 17, text, ctx);
        BuildStatSubPanel("PanelC", StatsW * 2f + 10f, 36f, StatsW, StatsH,
            StatNameMsgStart + 18, StatNameMsgEnd, text, ctx);

        GD.Print("[HudCharacterStatsPanel] Built — StatusPanel + 3 sub-panels (A/B/C). " +
                 "Action ids 300-312; stat names msg.xdb 60001-60022. " +
                 "inventwindow.dds (uitex 2) + tradekeepwindow.dds (uitex 4). " +
                 "spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED. " +
                 "TODO(spec): per-sub-panel coordinates not recovered.");
    }

    private void BuildStatSubPanel(string panelName, float x, float y, float w, float h,
        int msgStart, int msgEnd, HudTextLibrary text, ClientContext ctx)
    {
        var panel = new Control
        {
            Name = panelName,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(panel);

        var rowH = 18f;
        var startY = 4f;
        var rowIdx = 0;

        for (var msgId = msgStart; msgId <= msgEnd; msgId++)
        {
            var statIdx = msgId - StatNameMsgStart;
            var rowY = startY + rowIdx * rowH;

            var statName = text.GetCaption(msgId, $"stat{statIdx}");
            var nameLabel = new Label
            {
                Name = $"StatName{msgId}",
                Text = statName,
                HorizontalAlignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Ignore
            };
            nameLabel.AnchorLeft = 0f;
            nameLabel.AnchorTop = 0f;
            nameLabel.AnchorRight = 0f;
            nameLabel.AnchorBottom = 0f;
            nameLabel.OffsetLeft = 2f;
            nameLabel.OffsetTop = rowY;
            nameLabel.OffsetRight = w * 0.6f;
            nameLabel.OffsetBottom = rowY + rowH;
            panel.AddChild(nameLabel);

            var valueLabel = new Label
            {
                Name = $"StatValue{msgId}",
                Text = "—",
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = MouseFilterEnum.Ignore
            };
            valueLabel.AnchorLeft = 0f;
            valueLabel.AnchorTop = 0f;
            valueLabel.AnchorRight = 0f;
            valueLabel.AnchorBottom = 0f;
            valueLabel.OffsetLeft = w * 0.62f;
            valueLabel.OffsetTop = rowY;
            valueLabel.OffsetRight = w - 2f;
            valueLabel.OffsetBottom = rowY + rowH;
            panel.AddChild(valueLabel);

            if (statIdx >= 0 && statIdx < _statValueLabels.Length)
                _statValueLabels[statIdx] = valueLabel;

            rowIdx++;
        }

    }


    public void BindHub(IHudEventHub hub)
    {
        _statAllocations = hub.StatAllocations;
        GD.Print("[HudCharacterStatsPanel] BindHub: StatAllocations channel connected.");
    }


    public override void _Process(double delta)
    {
        if (_statAllocations is null) return;

        StatAllocationView? latest = null;
        while (_statAllocations.TryRead(out var ev))
            latest = ev;

        if (latest is null) return;
        ApplyStatView(latest);
    }

    private void ApplyStatView(StatAllocationView view)
    {
        SetStatLabel(0, view.BaseStr, view.DeltaStr);
        SetStatLabel(1, view.BaseInt, view.DeltaInt);
        SetStatLabel(2, view.BaseAgi, view.DeltaAgi);
        SetStatLabel(3, view.BaseDex, view.DeltaDex);
        SetStatLabel(4, view.BaseCon, view.DeltaCon);

    }

    private void SetStatLabel(int idx, uint baseVal, uint delta)
    {
        if ((uint)idx >= (uint)_statValueLabels.Length) return;
        var lbl = _statValueLabels[idx];
        if (lbl is null) return;
        lbl.Text = delta > 0 ? $"{baseVal}+{delta}" : $"{baseVal}";
    }


    public void Toggle()
    {
        _visible = !_visible;
        Visible = _visible;
        GD.Print($"[HudCharacterStatsPanel] Toggle → visible={_visible}.");
    }
}