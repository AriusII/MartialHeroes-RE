// Ui/Hud/HudGuildWindow.cs
//
// In-game Guild window — `GuildAPanel` (50-member roster, paged 10 visible).
//
// Geometry (PARTIAL — outer rect positioned by host at HUD-open time):
//   Content footprint spans y ≈ 26..690, width ≈ 0..420.
//   Port choice: right-docked 318×732 (consistent with the right-dock family).
//   spec: Docs/RE/specs/ui_system.md §8.15 — "positioned by host; content 0..420 / 26..690".
//
// Atlas (PARTIAL — DDS name manifest-gated/UNVERIFIED; candidates guildnewwindow.dds):
//   Every widget built with texture 0; atlas assigned per-instance post-build.
//   Port: use uitex 0 (no atlas) with fallback colour-coded cells.
//   spec: Docs/RE/specs/ui_system.md §8.15 — "manifest-gated/UNVERIFIED DDS name".
//
// Member list — 10 visible rows, 23px stride, 50-cap paged:
//   Col1 (name): x=11, action 0..9.
//   Col2 (class/title): x=80, action 10..19.
//   Col3 (level): x=203, action 20..29.
//   Col4 (status/loc): x=252, action 30..39.
//   Per-row action button: x=202, action 4613..4622.
//   Paging: action 4600 (−10) / 4601 (+10).
//   spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
//
// Tabs / sub-panels:
//   Member list (default), action-bar (4501..4508), rank/grade (4509 open; 4513..4516 markers),
//   member-position (4550 open; 4554..4556 markers; 4700..4703 name textboxes; 4705 apply).
//   Close = action 40; refresh = 41; resync (30s throttle) = 42.
//   spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
//
// Captions (msg.xdb ids — CODE-CONFIRMED):
//   21083 (guild level/tier), 21122/21123/21124 (rank-tab labels),
//   21129 (footer label A), 21140/21141/21142 (position-tab labels).
//   spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
//
// Outbound: C2S 2/30 CmsgGuildOp (8-byte body) → TODO(world-campaign).
// Inbound: S2C 4/65 SmsgGuildInfoFullSync (1812B) → stub rows empty: TODO(capture).
//   spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
//
// Toggle hotkey: NO dedicated hotkey (CODE-CONFIRMED). Context-driven open (guild NPC/right-click).
//   TODO(spec): no hotkey; open via context action.
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game Guild window (GuildAPanel — 50-member roster, 10 visible rows paged).
///     <para>
///         PASSIVE: renders member rows from Application events; emits guild op intents as
///         use-case calls (stubbed pending world-campaign). Inbound S2C 4/65 (1812B) stubbed.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudGuildWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float GuildW = 318f;
    private const float GuildH = 732f;

    // Member list constants
    // spec: ui_system.md §8.15 — "10 visible rows, 23px stride, baseline y=164+23r"
    private const int VisibleRows = 10; // spec: ui_system.md §8.15
    private const float RowBaseY = 164f; // spec: ui_system.md §8.15 — "y = 164 + 23·r"
    private const float RowStrideY = 23f; // spec: ui_system.md §8.15
    private const int MemberCap = 50; // spec: ui_system.md §8.15 CODE-CONFIRMED

    // Column x-coordinates
    // spec: ui_system.md §8.15 — col1 x=11, col2 x=80, col3 x=203, col4 x=252
    private const float Col1X = 11f; // spec: ui_system.md §8.15 — name column
    private const float Col2X = 80f; // spec: ui_system.md §8.15 — class/title column
    private const float Col3X = 203f; // spec: ui_system.md §8.15 — level column
    private const float Col4X = 252f; // spec: ui_system.md §8.15 — status/location column
    private const float ColW = 61f; // col1→col2 gap ≈ 61; col2→col3 ≈ 115; col3→col4 ≈ 41; col4 ≈ 56
    private const float RowH = 14f; // spec: ui_system.md §8.15 — each column button 61/115/41/56 × 14

    // Row-action button (per-row kick/manage)
    // spec: ui_system.md §8.15 — "row action button 202, 162+23r, 43×14, action 4613..4622"
    private const float RowActX = 202f; // spec: ui_system.md §8.15
    private const float RowActW = 43f; // spec: ui_system.md §8.15
    private const int RowActBase = 4613; // spec: ui_system.md §8.15

    // Paging action ids
    // spec: ui_system.md §8.15 — "page-UP action 4600 (−10 rows), page-DOWN action 4601 (+10 rows)"
    private const int ActionPageUp = 4600; // spec: ui_system.md §8.15
    private const int ActionPageDown = 4601; // spec: ui_system.md §8.15

    // msg.xdb ids
    // spec: ui_system.md §8.15 CODE-CONFIRMED
    private const int MsgGuildLevel = 21083; // spec: ui_system.md §8.15
    private const int MsgRankLabelA = 21122; // spec: ui_system.md §8.15
    private const int MsgRankLabelB = 21123; // spec: ui_system.md §8.15
    private const int MsgRankLabelC = 21124; // spec: ui_system.md §8.15 — drawn red
    private const int MsgFooterA = 21129; // spec: ui_system.md §8.15
    private const int MsgPosLabelA = 21140; // spec: ui_system.md §8.15
    private const int MsgPosLabelB = 21141; // spec: ui_system.md §8.15
    private const int MsgPosLabelC = 21142; // spec: ui_system.md §8.15

    // Resync 30s throttle
    // spec: ui_system.md §8.15 — "resync request action 42 (30s throttle)"
    private const double ResyncThrottleSecs = 30.0;

    private readonly Label[] _col1Labels = new Label[VisibleRows]; // name
    private readonly Label[] _col2Labels = new Label[VisibleRows]; // class/title
    private readonly Label[] _col3Labels = new Label[VisibleRows]; // level
    private readonly Label[] _col4Labels = new Label[VisibleRows]; // status/loc
    private readonly Button[] _rowActBtns = new Button[VisibleRows];
    private int _currentPage; // 0-based page (10 members per page)
    private double _lastResyncTime = -ResyncThrottleSecs;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the GuildAPanel (50-cap, 10 visible rows).
    ///     spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudGuildWindow";

        // Right-anchored, off-screen until opened
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

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.06f, 0.10f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.2f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Guild level/tier label (msg 21083)
        // spec: ui_system.md §8.15 — msg 21083 "guild level/tier %d" CODE-CONFIRMED
        var guildLevelLbl = new Label
        {
            Name = "GuildLevelLabel",
            Text = text.GetCaption(MsgGuildLevel, $"[msg {MsgGuildLevel}]"),
            Position = new Vector2(11f, 30f),
            Size = new Vector2(200f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(guildLevelLbl);

        // Page-up / page-down pager buttons
        // spec: ui_system.md §8.15 — "page-up/down buttons 395×25 at (0,26)/(0,267), action 4600/4601"
        var pageUpBtn = new Button
        {
            Name = "PageUp",
            Text = "▲",
            Position = new Vector2(0f, 26f),
            Size = new Vector2(295f, 25f), // adjusted for 318-wide port
            MouseFilter = MouseFilterEnum.Stop
        };
        pageUpBtn.Pressed += () => ChangePage(-1); // action 4600
        AddChild(pageUpBtn);

        var pageDownBtn = new Button
        {
            Name = "PageDown",
            Text = "▼",
            Position = new Vector2(0f, 267f),
            Size = new Vector2(295f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageDownBtn.Pressed += () => ChangePage(+1); // action 4601
        AddChild(pageDownBtn);

        // Member list rows — 10 visible rows
        // spec: ui_system.md §8.15 — "y = 164 + 23·r; col x=11/80/203/252; row h=14"
        BuildMemberRows(text);

        // Bottom action bar (4501..4508)
        // spec: ui_system.md §8.15 — "icon buttons 4501..4508 (invite/kick/promote/demote/leave/disband/notice/guild-cap)"
        BuildActionBar(text);

        // Close button (action 40) / refresh (41) / resync (42)
        // spec: ui_system.md §8.15 CODE-CONFIRMED
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "✕",
            Position = new Vector2(295f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false); // action 40
        AddChild(closeBtn);

        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "↺",
            Position = new Vector2(270f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        refreshBtn.Pressed += OnRefresh; // action 41
        AddChild(refreshBtn);

        var resyncBtn = new Button
        {
            Name = "ResyncBtn",
            Text = "⟳",
            Position = new Vector2(245f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        resyncBtn.Pressed += OnResync; // action 42
        AddChild(resyncBtn);

        GD.Print("[HudGuildWindow] Built — GuildAPanel (50-member cap, 10 visible rows, 23px stride, y=164+23r). " +
                 "Columns: name x=11 / class x=80 / level x=203 / status x=252. " +
                 "Inbound: TODO(capture): guild member-record value fields (S2C 4/65 1812B). " +
                 "Outbound: TODO(world-campaign): C2S 2/30 CmsgGuildOp (8B). " +
                 "No dedicated hotkey (CODE-CONFIRMED — context-driven open). " +
                 "spec: Docs/RE/specs/ui_system.md §8.15 CODE-CONFIRMED.");
    }

    private void BuildMemberRows(HudTextLibrary text)
    {
        // spec: ui_system.md §8.15 — "10 visible rows; per row: col1(name,11), col2(class,80), col3(level,203), col4(status,252)"
        for (var r = 0; r < VisibleRows; r++)
        {
            var rowY = RowBaseY + r * RowStrideY; // spec: y = 164 + 23·r

            // Col1 — name (clickable, action 0..9)
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

            // Col2 — class/title (action 10..19)
            var col2 = new Label
            {
                Name = $"MemberClass{r}",
                Text = "",
                Position = new Vector2(Col2X, rowY),
                Size = new Vector2(115f, RowH), // col2→col3 gap
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col2);
            _col2Labels[r] = col2;

            // Col3 — level (action 20..29)
            var col3 = new Label
            {
                Name = $"MemberLevel{r}",
                Text = "",
                Position = new Vector2(Col3X, rowY),
                Size = new Vector2(41f, RowH), // col3→col4 gap
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col3);
            _col3Labels[r] = col3;

            // Col4 — status/location (action 30..39)
            var col4 = new Label
            {
                Name = $"MemberStatus{r}",
                Text = "",
                Position = new Vector2(Col4X, rowY),
                Size = new Vector2(56f, RowH), // col4 width
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(col4);
            _col4Labels[r] = col4;

            // Per-row action button (kick/manage) — dst(202, 162+23r, 43, 14), action 4613+r
            // spec: ui_system.md §8.15 CODE-CONFIRMED
            var rowActY = 162f + r * RowStrideY; // spec: "202, 162+23r"
            var rowActBtn = new Button
            {
                Name = $"RowAction{r}",
                Text = "⚙",
                Position = new Vector2(RowActX, rowActY),
                Size = new Vector2(RowActW, RowH),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedR = r;
            rowActBtn.Pressed += () => OnRowAction(capturedR); // action 4613+r
            AddChild(rowActBtn);
            _rowActBtns[r] = rowActBtn;
        }
    }

    private void BuildActionBar(HudTextLibrary text)
    {
        // spec: ui_system.md §8.15 — "icon buttons 4501..4508 (invite/kick/promote/demote/leave/disband/notice/4508=guild-cap)"
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

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void ChangePage(int direction)
    {
        // spec: ui_system.md §8.15 — "action 4600 = −10 rows, 4601 = +10 rows"
        var maxPage = (MemberCap - 1) / VisibleRows; // 0..4 for 50 members
        _currentPage = Math.Clamp(_currentPage + direction, 0, maxPage);
        GD.Print(
            $"[HudGuildWindow] Page → {_currentPage} (showing members {_currentPage * VisibleRows}..{_currentPage * VisibleRows + VisibleRows - 1}). " +
            "spec: Docs/RE/specs/ui_system.md §8.15.");
        // TODO(world-campaign): repopulate visible rows from cached guild roster
    }

    private void OnRowAction(int rowIndex)
    {
        // spec: ui_system.md §8.15 — action 4613+r = per-row kick/manage
        var globalMember = _currentPage * VisibleRows + rowIndex;
        GD.Print($"[HudGuildWindow] row action for member index {globalMember} (action {4613 + rowIndex}). " +
                 "TODO(world-campaign): C2S 2/30 CmsgGuildOp. " +
                 "spec: Docs/RE/specs/ui_system.md §8.15.");
    }

    private void OnGuildAction(int actionId)
    {
        // spec: ui_system.md §8.15 — "actions 4501..4508 (invite/kick/promote/demote/leave/disband/notice/cap)"
        // 4508 = guild-cap toggle, writes CHAR_GUILDCAP_ENABLE to per-char INI
        if (actionId == 4508)
            GD.Print("[HudGuildWindow] action 4508 = guild-cap toggle (writes CHAR_GUILDCAP_ENABLE). " +
                     "spec: Docs/RE/specs/ui_system.md §8.15.");
        else
            GD.Print($"[HudGuildWindow] guild action {actionId} → TODO(world-campaign): C2S 2/30 CmsgGuildOp. " +
                     "spec: Docs/RE/specs/ui_system.md §8.15.");
    }

    private void OnRefresh()
    {
        // spec: ui_system.md §8.15 — action 41 = refresh/info
        GD.Print("[HudGuildWindow] action 41 = refresh. " +
                 "spec: Docs/RE/specs/ui_system.md §8.15.");
    }

    private void OnResync()
    {
        // spec: ui_system.md §8.15 — "action 42 = resync request (30s throttle)"
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastResyncTime < ResyncThrottleSecs)
        {
            GD.Print("[HudGuildWindow] Resync throttled (30s cooldown). " +
                     "spec: Docs/RE/specs/ui_system.md §8.15.");
            return;
        }

        _lastResyncTime = now;
        // TODO(world-campaign): IApplicationUseCases.GuildOp → C2S 2/30 (resync op)
        GD.Print("[HudGuildWindow] action 42 = resync (30s throttle) → TODO(world-campaign): C2S 2/30. " +
                 "spec: Docs/RE/specs/ui_system.md §8.15.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows or hides the guild window.
    ///     No dedicated hotkey (CODE-CONFIRMED). Opened by guild NPC/context action.
    ///     spec: Docs/RE/specs/ui_system.md §8.15 — "no dedicated guild hotkey CODE-CONFIRMED; context-driven open".
    ///     TODO(spec): no hotkey — open via guild context action.
    /// </summary>
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