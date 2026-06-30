using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudGuildWindow : Control
{
    private const float GuildW = 318f;
    private const float GuildH = 732f;

    private const int VisibleRows = 10;
    private const float RowBaseY = 164f;
    private const float RowStrideY = 23f;
    private const int MemberCap = 50;

    private const float Col1X = 11f;
    private const float Col2X = 80f;
    private const float Col3X = 203f;
    private const float Col4X = 252f;
    private const float ColW = 61f;
    private const float RowH = 14f;

    private const float RowActX = 202f;
    private const float RowActW = 43f;
    private const int RowActBase = 4613;

    private const int ActionPageUp = 4600;
    private const int ActionPageDown = 4601;

    private const int MsgGuildLevel = 21083;
    private const int MsgRankLabelA = 21122;
    private const int MsgRankLabelB = 21123;
    private const int MsgRankLabelC = 21124;
    private const int MsgFooterA = 21129;
    private const int MsgPosLabelA = 21140;
    private const int MsgPosLabelB = 21141;
    private const int MsgPosLabelC = 21142;

    private const double ResyncThrottleSecs = 30.0;

    private const byte OpRefresh = 41;
    private const byte OpResync = 42;

    private readonly Label[] _col1Labels = new Label[VisibleRows];
    private readonly Label[] _col2Labels = new Label[VisibleRows];
    private readonly Label[] _col3Labels = new Label[VisibleRows];
    private readonly Label[] _col4Labels = new Label[VisibleRows];
    private readonly Button[] _rowActBtns = new Button[VisibleRows];
    private readonly uint[] _memberActorIds = new uint[MemberCap];

    private int _currentPage;
    private double _lastResyncTime = -ResyncThrottleSecs;
    private int _selectedMember = -1;
    private bool _open;
    private ClientContext? _ctx;
    private IHudEventHub? _hub;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext ctx)
    {
        _ctx = ctx;

        Name = "HudGuildWindow";

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = GuildW;
        OffsetTop = 0f;
        OffsetRight = GuildW + GuildW;
        OffsetBottom = GuildH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.06f, 0.10f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.2f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var guildLevelLbl = new Label
        {
            Name = "GuildLevelLabel",
            Text = text.GetCaption(MsgGuildLevel, $"[msg {MsgGuildLevel}]"),
            Position = new Vector2(11f, 30f),
            Size = new Vector2(200f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(guildLevelLbl);

        var pageUpBtn = new Button
        {
            Name = "PageUp",
            Text = "▲",
            Position = new Vector2(0f, 26f),
            Size = new Vector2(295f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageUpBtn.Pressed += () => ChangePage(-1);
        AddChild(pageUpBtn);

        var pageDownBtn = new Button
        {
            Name = "PageDown",
            Text = "▼",
            Position = new Vector2(0f, 267f),
            Size = new Vector2(295f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageDownBtn.Pressed += () => ChangePage(+1);
        AddChild(pageDownBtn);

        BuildMemberRows(text);

        BuildActionBar(text);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "✕",
            Position = new Vector2(295f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "↺",
            Position = new Vector2(270f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        refreshBtn.Pressed += OnRefresh;
        AddChild(refreshBtn);

        var resyncBtn = new Button
        {
            Name = "ResyncBtn",
            Text = "⟳",
            Position = new Vector2(245f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        resyncBtn.Pressed += OnResync;
        AddChild(resyncBtn);

        GD.Print("[HudGuildWindow] Built — GuildAPanel (50-member cap, 10 visible rows, 23px stride, y=164+23r). " +
                 "Columns: name x=11 / class x=80 / level x=203 / status x=252. " +
                 "Inbound: TODO(channel-supplement): GuildRosterEvent + GuildMemberPatchEvent + GuildStateChangedEvent need IHudEventHub channels. " +
                 "Outbound: SendGuildActionAsync (C2S 2/30). " +
                 "No dedicated hotkey (CODE-CONFIRMED — context-driven open). " +
                 "spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.");
    }

    private void BuildMemberRows(HudTextLibrary text)
    {
        for (var r = 0; r < VisibleRows; r++)
        {
            var rowY = RowBaseY + r * RowStrideY;

            var col1 = new Label
            {
                Name = $"MemberName{r}",
                Text = "",
                Position = new Vector2(Col1X, rowY),
                Size = new Vector2(ColW, RowH),
                MouseFilter = MouseFilterEnum.Stop
            };
            AddChild(col1);
            _col1Labels[r] = col1;

            var col2 = new Label
            {
                Name = $"MemberClass{r}",
                Text = "",
                Position = new Vector2(Col2X, rowY),
                Size = new Vector2(115f, RowH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col2);
            _col2Labels[r] = col2;

            var col3 = new Label
            {
                Name = $"MemberLevel{r}",
                Text = "",
                Position = new Vector2(Col3X, rowY),
                Size = new Vector2(41f, RowH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col3);
            _col3Labels[r] = col3;

            var col4 = new Label
            {
                Name = $"MemberStatus{r}",
                Text = "",
                Position = new Vector2(Col4X, rowY),
                Size = new Vector2(56f, RowH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col4);
            _col4Labels[r] = col4;

            var rowActY = 162f + r * RowStrideY;
            var rowActBtn = new Button
            {
                Name = $"RowAction{r}",
                Text = "⚙",
                Position = new Vector2(RowActX, rowActY),
                Size = new Vector2(RowActW, RowH),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedR = r;
            rowActBtn.Pressed += () => OnRowAction(capturedR);
            AddChild(rowActBtn);
            _rowActBtns[r] = rowActBtn;
        }
    }

    private void BuildActionBar(HudTextLibrary text)
    {
        string[] labels = { "Invite", "Kick", "Promote", "Demote", "Leave", "Disband", "Notice", "Cap" };
        for (var i = 0; i < labels.Length; i++)
        {
            var actionId = 4501 + i;
            var btn = new Button
            {
                Name = $"GuildAction{actionId}",
                Text = labels[i],
                Position = new Vector2(10f + i * 38f, 690f),
                Size = new Vector2(36f, 22f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var captured = actionId;
            btn.Pressed += () => OnGuildAction(captured);
            AddChild(btn);
        }
    }


    public void BindHub(IHudEventHub hub)
    {
        _hub = hub;
    }

    public override void _Process(double delta)
    {
    }

    public void OnGuildRoster(GuildRosterEvent evt)
    {
        if (evt.Gate != 1) return;

        for (var i = 0; i < evt.Members.Length && i < MemberCap; i++)
            _memberActorIds[i] = evt.Members[i].ActorId;

        RefreshVisibleRows(evt);
    }

    public void OnGuildMemberPatch(GuildMemberPatchEvent evt)
    {
        RefreshVisibleRowsFromCache();
    }

    public void OnGuildStateChanged(GuildStateChangedEvent evt)
    {
        GD.Print($"[HudGuildWindow] GuildStateChangedEvent: applyGate={evt.ApplyGate} action={evt.Action} result={evt.Result}.");
    }


    private void RefreshVisibleRows(GuildRosterEvent roster)
    {
        var pageStart = _currentPage * VisibleRows;
        for (var r = 0; r < VisibleRows; r++)
        {
            var idx = pageStart + r;
            if (idx < roster.Members.Length)
            {
                var m = roster.Members[idx];
                _col1Labels[r].Text = m.Name;
                _col2Labels[r].Text = m.Rank.ToString();
                _col3Labels[r].Text = m.Points.ToString();
                _col4Labels[r].Text = m.Online != 0 ? "온" : "오";
            }
            else
            {
                _col1Labels[r].Text = "";
                _col2Labels[r].Text = "";
                _col3Labels[r].Text = "";
                _col4Labels[r].Text = "";
            }
        }
    }

    private void RefreshVisibleRowsFromCache()
    {
        var pageStart = _currentPage * VisibleRows;
        for (var r = 0; r < VisibleRows; r++)
        {
            var idx = pageStart + r;
            if (_memberActorIds[idx] == 0u)
            {
                _col1Labels[r].Text = "";
                _col2Labels[r].Text = "";
                _col3Labels[r].Text = "";
                _col4Labels[r].Text = "";
            }
        }
    }

    private void ChangePage(int direction)
    {
        var maxPage = (MemberCap - 1) / VisibleRows;
        _currentPage = Math.Clamp(_currentPage + direction, 0, maxPage);
        RefreshVisibleRowsFromCache();
    }

    private void OnRowAction(int rowIndex)
    {
        _selectedMember = _currentPage * VisibleRows + rowIndex;
    }

    private void OnGuildAction(int actionId)
    {
        var mode = (byte)(actionId - 4501);
        var targetId = _selectedMember >= 0 ? _memberActorIds[_selectedMember] : 0u;
        if (_ctx is not null)
            _ = _ctx.UseCases.SendGuildActionAsync(mode, targetId);
    }

    private void OnRefresh()
    {
        if (_ctx is not null)
            _ = _ctx.UseCases.SendGuildActionAsync(OpRefresh, 0u);
    }

    private void OnResync()
    {
        var now = global::Godot.Time.GetTicksMsec() / 1000.0;
        if (now - _lastResyncTime < ResyncThrottleSecs) return;
        _lastResyncTime = now;
        if (_ctx is not null)
            _ = _ctx.UseCases.SendGuildActionAsync(OpResync, 0u);
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -GuildW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = GuildW;
            OffsetRight = GuildW + GuildW;
        }

        Visible = _open;
    }
}
