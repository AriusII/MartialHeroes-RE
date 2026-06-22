// Ui/Scenes/Login/ServerSelectSubView.cs
//
// Server-select sub-view for the Login(1) state.
//
// Renders the server-list overlay (sub-states 35..37) as an internal sub-view of LoginWindow.
// Backed entirely by HudAtlasLibrary + HudTextLibrary — no UiAssetLoader dependency.
//
// Layout (spec: Docs/RE/specs/frontend_layout_tables.md §4.3, G2 debugger/decompile-confirmed 2026 / IDB 263bd994):
//
// §4.3.1 NAME-STRIP TABS — 10 buttons localX=13+47·i, localY=66, 47×18, A2 src(596,985)/hover(643,985),
//   actions 115..124 (page = action−115). All coords LOCAL to the column panel at (270,85).
//
// §4.3.2 DETAIL PLATES — 2 per page side-by-side, X base = 30+233·i (i=0,1):
//   • Name label      (30+233·i, 390, 174×21) font slot 0, center-aligned, msg 5000+ServerId.
//   • Plate face img  (30+233·i+47, 97, 100×372) A4 src(448+124·i, 6) — baked per-column calligraphy.
//   • Select button   (30+233·i−6, 97, 202×372) A4 N(9,6)/H(220,6), action 400+i.
//   • Status caption  (30+233·i, 410, 174×20) font slot 4, coloured per §4.3.4.
//   • Spare label     (30+233·i, 430, 174×20) EMPTY STRING — not drawn.
//
// §4.3.3 RECORD→PAGE: 8-byte {ServerId i16, StatusCode i16, LoadCount i16, OpenTimeFlag i16};
//   page cursor = 16·page bytes → record index 2·page; 2 records per page (loop guard: total−2·page).
//   Server name: ServerId 1..40 → msg 5000+ServerId; out-of-range → fallback 5901.
//
// §4.3.4 POPULATION COLOUR LADDER (StatusCode==0 && OpenTimeFlag!=0, threshold branch):
//   LoadCount > 1200 → red 0xFFFF0000; 801..1200 → orange 0xFFED6806; 501..800 → yellow 0xFFFFFF00;
//   ≤500 → green 0xFFB5FF7A. Discrete branch (OpenTimeFlag==0): ==4 red / ==3 orange / ==2 yellow / else green.
//
// §4.3.5 SELECTABLE GATE:
//   Paint-time: StatusCode==0 → button enabled; StatusCode!=0 → button Disabled.
//   Click handler: StatusCode==0 && LoadCount<2400 before commit (sub-gate 'to-confirm' in spec; apply both).
//   Selection highlight: plate whose ServerId == NEW_SERVER_INDEX (uiconfig.lua). Lastserver is written on
//   commit, NOT the painter highlight key.
//
// §4.3.6 PAGER: 3 glyph controls using actions 115..124; no "N servers" count text.
//
// spec: Docs/RE/specs/frontend_layout_tables.md §4.3 (consolidated implementable reference — G2 confirmed)
// spec: Docs/RE/specs/frontend_layout_tables.md §4.1 (name/status/population resolver tables)
// spec: Docs/RE/specs/frontend_layout_tables.md §4.2 (pager re-arm geometry, commit guard)
// spec: Docs/RE/specs/login_flow.md §2.1

using System.Globalization;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens.Layout;

// ServerListEntryView (canonical published server-list view — Client.Application.Contracts.Events)

// LoginLayout, WidgetRect (moved to engine-free layer)

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
///     Server-select sub-view for Login(1) sub-states 35..37.
///     <para>
///         Renders TWO server "plate" sprites per page (§4.3.2), 10 name-strip page tabs (§4.3.1),
///         a 3-glyph pager (§4.3.6), and per-plate population-colour status captions (§4.3.4).
///         All atlas drawing comes from <see cref="HudAtlasLibrary" />; caption text from
///         <see cref="HudTextLibrary" />. All coordinates are LOCAL to the server-list column panel at
///         canvas (270,85) — <c>PanelPoint()</c> translates to absolute canvas positions.
///     </para>
///     <para>
///         Plate interactivity: paint-time gate <c>StatusCode==0</c> (§4.3.5); click-handler gate
///         <c>StatusCode==0 &amp;&amp; LoadCount&lt;2400</c> before commit (§4.3.5 to-confirm).
///         Subscribe to <see cref="ServerSelected" /> to receive the chosen server id.
///         Passive intent only — never mutates domain state.
///     </para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3 (G2 debugger/decompile-confirmed, IDB 263bd994)
/// </summary>
public sealed partial class ServerSelectSubView : Control
{
    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    // Atlas paths. spec: Docs/RE/specs/frontend_layout_tables.md §1 / §4
    private const string AtlasD = "data/ui/loginwindow_02.dds"; // A4 = loginwindow_02.dds
    private const string AtlasB = "data/ui/loginwindow.dds"; // A2 = loginwindow.dds
    private const string AtlasA1 = "data/ui/login_slice1.dds"; // A1 = login_slice1.dds

    // Full-screen A2 backdrop behind the list-box: dst(0,110,1024,490) src(0,0). spec §4.
    // "Backdrop is TWO layers: a full-screen A2 image at (0,110) 1024×490, source (0,0), drawn FIRST"
    private const int BackdropX = 0; // spec: frontend_layout_tables.md §4
    private const int BackdropY = 110; // spec: frontend_layout_tables.md §4
    private const int BackdropW = 1024; // spec: frontend_layout_tables.md §4
    private const int BackdropH = 490; // spec: frontend_layout_tables.md §4

    // Title "서버선택" image: A2 dst(207,44) 70×17 src(0,980). spec §4.
    // Absolute canvas coords. "Title '서버선택' = baked A2 IMAGE … src(0,980)"
    private const int TitleAbsX = 207; // spec: frontend_layout_tables.md §4
    private const int TitleAbsY = 44; // spec: frontend_layout_tables.md §4
    private const int TitleSrcX = 0; // spec: frontend_layout_tables.md §4 "source(0,980)"
    private const int TitleSrcY = 980; // spec: frontend_layout_tables.md §4

    // Server-list panel shares the central notice/listbox area.
    private const int PanelX = 270;
    private const int PanelY = 85;

    // Plate source rect in loginwindow_02.dds. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int PlateSrcX = 9;
    private const int PlateSrcY = 6;
    private const int PlateHoverSrcX = 220;
    private const int PlateHoverSrcY = 6;
    private const int PlateW = 202;
    private const int PlateH = 372;

    // Two-plate layout, panel-local. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int PlateBaseX0 = 30;
    private const int PlateBaseX1 = 263;
    private const int PlateY = 97;
    private const int PlateStripOffsetX = -6;

    private const int StatusIconOffsetX = 47;
    private const int StatusIconW = 100;
    private const int StatusIconH = 372;
    private const int StatusIconSrcY = 6;
    private const int StatusIconSrcX0 = 448;
    private const int StatusIconSrcX1 = 572;

    private const int RowLabelY0 = 390;
    private const int RowLabelY1 = 410;
    private const int RowLabelY2 = 430;
    private const int RowLabelW = 174;
    private const int RowLabelH0 = 21;
    private const int RowLabelH = 20;

    // Plate action ids. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int ActionPlate0 = 400;
    private const int ActionPlate1 = 401;

    // Pager source rect in loginwindow.dds. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int PagerSrcX = 596;
    private const int PagerSrcY = 985;
    private const int PagerW = 47;
    private const int PagerH = 18;
    private const int PagerY = 66;

    // Pager action ids 115..124. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int PagerActionBase = 115;
    private const int PagerCount = 10;

    // Population colour ladder thresholds (StatusCode==0, OpenTimeFlag!=0 threshold branch).
    // Boundaries: >1200 red; 801..1200 orange; 501..800 yellow; ≤500 green.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 (G2 debugger/decompile-confirmed, IDB 263bd994)
    private const int PopRedThreshold = 1200; // spec: frontend_layout_tables.md §4.3.4 LoadCount>1200 → red
    private const int PopOrangeThreshold = 800; // spec: frontend_layout_tables.md §4.3.4 801..1200 → orange (>800 strict)
    private const int PopYellowThreshold = 500; // spec: frontend_layout_tables.md §4.3.4 501..800 → yellow (>500 strict)

    // Discrete-level branch (StatusCode==0, OpenTimeFlag==0): exact-equality colour ladder.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 "Discrete-level branch"
    private const int PopLevelRed = 4; // == 4 → msg 6001 red
    private const int PopLevelOrange = 3; // == 3 → msg 6002 orange
    private const int PopLevelYellow = 2; // == 2 → msg 6003 yellow

    // Special-row sentinel: a record whose SERVER-ID field (+0) == 100 is a display-only event row
    // (out of the 1..40 name range → msg 5901; shows the 3 indicator quads). Tested on +0, NOT +2 status.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.1/§4.2 "server_id == 100 gate" (CORRECTION 2026-06-20).
    private const int SpecialRowServerId = 100;

    // Default-selection highlight strip: atlas A4 (loginwindow_02.dds) src(700,18) 46×168, drawn on the
    // plate whose ServerId matches NEW_SERVER_INDEX (the single uiconfig.lua-sourced value).
    // CORRECTION 2026-06-21: the highlight key is NEW_SERVER_INDEX, NOT the Lastserver registry value.
    // Lastserver is written on commit (persist); whether it is read back to pre-highlight is done
    // elsewhere and was not re-confirmed — do not assert it as this painter's behaviour.
    // The painter repositions the strip to the plate's right edge (x = plate.dstX + plate.width − 48).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Default-selection highlight" (CORRECTION 2026-06-21).
    private const int HighlightSrcX = 700;
    private const int HighlightSrcY = 18;
    private const int HighlightW = 46;
    private const int HighlightH = 168;
    private const int HighlightRightInset = 48; // plate-right − 48, per the painter

    // Status caption msg base: 4029 + status_code → caption text. spec: frontend_layout_tables.md §4
    // "msg 4029/4030/4031/4032 are the STATUS CAPTIONS (keyed by status_code)"
    private const int StatusCaptionMsgBase = 4029; // spec: frontend_layout_tables.md §4

    // Server name caption id base. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int ServerNameCaptionBase = 5000; // server_id N → caption 5000+N

    // Pager re-arm blank-atlas source rects (loginwindow.dds A2).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.2
    //   "each repaint all 10 pager strips reset to blank atlas region N(500,792)/H(500,810)/P(500,810)"
    private const int PagerBlankNSrcX = 500; // spec: frontend_layout_tables.md §4.2
    private const int PagerBlankNSrcY = 792; // spec: frontend_layout_tables.md §4.2
    private const int PagerBlankHSrcX = 500; // spec: frontend_layout_tables.md §4.2
    private const int PagerBlankHSrcY = 810; // spec: frontend_layout_tables.md §4.2
    private const int PagerBlankPSrcY = 810; // spec: frontend_layout_tables.md §4.2 (P same as H)

    // Pager re-arm real-art source rects for strips 1, 2, 3 (loginwindow.dds A2).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.2
    //   "strip[1] → N(500,828)/H(500,846)"
    private const int Pager1NSrcX = 500; // spec: frontend_layout_tables.md §4.2
    private const int Pager1NSrcY = 828; // spec: frontend_layout_tables.md §4.2
    private const int Pager1HSrcX = 500; // spec: frontend_layout_tables.md §4.2

    private const int Pager1HSrcY = 846; // spec: frontend_layout_tables.md §4.2

    //   "strip[2] → N(500,864)/H(605,985)"
    private const int Pager2NSrcX = 500; // spec: frontend_layout_tables.md §4.2
    private const int Pager2NSrcY = 864; // spec: frontend_layout_tables.md §4.2
    private const int Pager2HSrcX = 605; // spec: frontend_layout_tables.md §4.2

    private const int Pager2HSrcY = 985; // spec: frontend_layout_tables.md §4.2

    //   "strip[3] → N(710,985)/H(815,985)"
    private const int Pager3NSrcX = 710; // spec: frontend_layout_tables.md §4.2
    private const int Pager3NSrcY = 985; // spec: frontend_layout_tables.md §4.2
    private const int Pager3HSrcX = 815; // spec: frontend_layout_tables.md §4.2
    private const int Pager3HSrcY = 985; // spec: frontend_layout_tables.md §4.2

    // Population colour DWORDs (ARGB). spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4
    // 0xFFFF0000 red / 0xFFED6806 orange / 0xFFFFFF00 yellow / 0xFFB5FF7A green (G2-confirmed, IDB 263bd994)
    private static readonly Color PopColorRed = Color.Color8(255, 0, 0); // spec: §4.3.4 0xFFFF0000
    private static readonly Color PopColorOrange = Color.Color8(237, 104, 6); // spec: §4.3.4 0xFFED6806
    private static readonly Color PopColorYellow = Color.Color8(255, 255, 0); // spec: §4.3.4 0xFFFFFF00
    private static readonly Color PopColorGreen = Color.Color8(181, 255, 122); // spec: §4.3.4 0xFFB5FF7A

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;

    // Three status-color indicator quads (A2 src(500,786) 60×39), hidden by default.
    // Re-anchored around a status==100 special row when present.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status-color indicator quads ×3"
    private readonly TextureRect?[] _statusIndicators = new TextureRect?[3];
    private readonly HudTextLibrary _text;

    // Sub-state 35 loading sentinel: true from sub-view creation until SetServers() is called.
    // Distinguishes the "fetching" state (35 — list not yet received) from the error state (36 —
    // received zero records → msg 4027). Without this flag the empty-list breadcrumb would be
    // incorrectly printed while the server-list worker is still running.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 sub-state 35 "fetching: show progress"
    private bool _loading = true;
    private int _page;
    private IReadOnlyList<ServerListEntryView> _servers = [];

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates the server-select sub-view.
    /// </summary>
    public ServerSelectSubView(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>
    ///     The <c>NEW_SERVER_INDEX</c> value sourced from <c>uiconfig.lua</c>, used to pre-highlight the
    ///     default server plate. <c>-1</c> = no highlight (default when uiconfig.lua does not supply a value).
    ///     <para>
    ///         The authoritative list painter compares each record's <c>ServerId</c> against this value —
    ///         <strong>not</strong> against the <c>Lastserver</c> registry value. <c>Lastserver</c> is
    ///         <em>written</em> on commit (persisted to registry); it is not the highlight key in this painter.
    ///     </para>
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 "Default-highlight key" (static-confirmed)
    /// </summary>
    public int NewServerIndex { get; set; } = -1;

    /// <summary>
    ///     Alias retained for call-site compatibility. Forwards to <see cref="NewServerIndex" />.
    ///     The authoritative highlight key is <see cref="NewServerIndex" /> (NEW_SERVER_INDEX from
    ///     uiconfig.lua); Lastserver is only written on commit — it is not read back by this painter.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 "Default-highlight key" (static-confirmed)
    /// </summary>
    public int LastServerId
    {
        get => NewServerIndex;
        set => NewServerIndex = value;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Populates the server list. Rebuilds the 2-plate-per-page layout to match the record count.
    ///     Clears the loading flag (transitions from sub-state 35 fetching to 36/37 resolved).
    ///     Must be called on the main thread (Control mutation).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 (2 detail plates per page)
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3 (record-to-plate mapping)
    ///     spec: Docs/RE/specs/login_flow.md §2.1 (sub-states 35→36→37)
    /// </summary>
    public void SetServers(IReadOnlyList<ServerListEntryView> servers)
    {
        _loading = false; // spec: §4 sub-state 36 — fetch result received; transition from 35.
        _servers = servers;
        _page = 0;
        RebuildLayout();
    }

    // -------------------------------------------------------------------------
    // Layout builder
    // -------------------------------------------------------------------------

    private void RebuildLayout()
    {
        // Reset indicator refs before freeing (they may be children).
        for (var i = 0; i < _statusIndicators.Length; i++)
            _statusIndicators[i] = null;

        for (var i = GetChildCount() - 1; i >= 0; i--)
        {
            var child = GetChild(i);
            RemoveChild(child);
            child.QueueFree();
        }

        BuildPanelFrame();
        BuildPagers();
        BuildFlagImage();
        BuildStatusIndicators();

        RebuildVisiblePage();
    }

    // Build 3 status-color indicator quads, hidden by default.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status-color indicator quads ×3:
    //   atlas A2 (loginwindow.dds) src(500,786) 60×39, hidden by default"
    private void BuildStatusIndicators()
    {
        var tex = _atlas.SliceByPath(AtlasB,
            LoginLayout.StatusIndicatorSrcX, LoginLayout.StatusIndicatorSrcY,
            LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH);

        for (var i = 0; i < LoginLayout.StatusIndicatorCount; i++) // spec: §4 (×3)
        {
            var rect = new TextureRect
            {
                Texture = tex,
                Size = new Vector2(LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false // hidden by default; shown when status==100 row present
            };
            _statusIndicators[i] = rect;
            AddChild(rect);
        }
    }

    private void RebuildVisiblePage()
    {
        _page = ClampPage(_page);

        // Hide all status indicators by default; re-anchored if status==100 found.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status-color indicator quads … hidden by default"
        foreach (var ind in _statusIndicators)
            if (ind is not null)
                ind.Visible = false;

        if (_loading)
        {
            // Sub-state 35: server-list fetch worker is still running; no records yet.
            // Render an empty list and wait for SetServers() to supply records.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4 sub-state 35 "fetching: show progress"
            GD.Print(
                "[ServerSelectSubView] state 35 — loading server list (fetch worker running, waiting for SetServers).");
            return;
        }

        if (_servers.Count == 0)
        {
            // Sub-state 36 error branch: worker returned zero records → msg 4027.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4 sub-state 36 "0 records → msg 4027"
            var msg = _text.GetCaption(LoginLayout.MsgErrNoServers, string.Empty);
            GD.Print($"[ServerSelectSubView] state 36 error: no servers. msg {LoginLayout.MsgErrNoServers}: '{msg}'");
            return;
        }

        // STABLE PLATE ORDER: page i shows raw records [2i] and [2i+1] from the record array.
        // Fisher-Yates permutation runs on a SEPARATE parallel server-id vector only (effect: Lastserver);
        // it NEVER reorders the displayed plates. Must NOT shuffle displayed rows.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3 (record-to-plate mapping, page cursor = 16·page)
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "on-screen plate order is STABLE"
        var firstIndex = _page * 2; // record index = 2·page (byte offset = 16·page; 8 bytes/record)
        var visibleCount = Math.Min(2, _servers.Count - firstIndex);

        int[] plateX = [PlateBaseX0, PlateBaseX1];
        int[] statusSrcX = [StatusIconSrcX0, StatusIconSrcX1];
        int[] actions = [ActionPlate0, ActionPlate1];

        for (var slot = 0; slot < visibleCount; slot++)
        {
            var idx = firstIndex + slot;

            // Default-selection highlight (drawn BEHIND the plate): only when this plate's ServerId
            // matches NEW_SERVER_INDEX (uiconfig.lua-sourced). CORRECTION 2026-06-21: highlight key is
            // NewServerIndex, not the Lastserver registry value. Lastserver is written on commit.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Default-selection highlight" (CORRECTION 2026-06-21).
            if (NewServerIndex >= 0 && _servers[idx].ServerId == NewServerIndex)
                BuildSelectionHighlight(plateX[slot], PlateY);

            BuildPlate(plateX[slot], PlateY, actions[slot], idx, statusSrcX[slot]);

            // Re-anchor the 3 status indicators around the SERVER-ID==100 special row.
            // The painter (0x5fcd09) tests record[+0] (server id), NOT record[+2] (status).
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.1/§4.2 "server_id == 100 gate" (CORRECTION 2026-06-20)
            if (_servers[idx].ServerId == SpecialRowServerId)
                AnchorStatusIndicators(plateX[slot], PlateY);
        }

        GD.Print(BuildPageBreadcrumb(firstIndex, visibleCount));
    }

    // Re-anchor and show the 3 status-color indicator quads around the status==100 special row.
    // anchorX/anchorY = the plate's dst-X/dst-Y fields (the plate-widget destination corner).
    // quad 0 → (anchorX−30, anchorY−13)
    // quad 1 → (anchorX+139, anchorY+13)
    // quad 2 → (anchorX+139, anchorY+13)  (faithful duplicate — overlaps quad 1 exactly)
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "Quad anchoring"
    private void AnchorStatusIndicators(int anchorX, int anchorY)
    {
        // Quad 0: (anchorX−30, anchorY−13). spec: frontend_layout_tables.md §4
        if (_statusIndicators[0] is { } ind0)
        {
            ind0.Position = PanelPoint(anchorX - 30, anchorY - 13);
            ind0.Visible = true;
        }

        // Quads 1 and 2: (anchorX+139, anchorY+13) — overlap exactly (faithful duplicate).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "quads 1 and 2 overlap exactly"
        for (var i = 1; i <= 2; i++)
            if (_statusIndicators[i] is { } ind)
            {
                ind.Position = PanelPoint(anchorX + 139, anchorY + 13);
                ind.Visible = true;
            }
    }

    // Default-selection highlight strip: atlas A4 src(700,18) 46×168, drawn BEHIND the highlighted plate.
    // Highlight key = ServerId matches NEW_SERVER_INDEX (uiconfig.lua-sourced) — NOT Lastserver.
    // Lastserver is written on commit; it is not the painter highlight key (static-confirmed, §4.3.5).
    // Draw order: called BEFORE BuildPlate so AddChild inserts the strip BEFORE the plate node — Godot
    // draws earlier siblings behind later ones, placing the highlight under the selected plate.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 "Default-highlight key" (static-confirmed)
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Selection highlight strip"
    private void BuildSelectionHighlight(int x, int y)
    {
        Texture2D? strip = _atlas.SliceByPath(AtlasD, HighlightSrcX, HighlightSrcY, HighlightW, HighlightH);
        if (strip is null)
            return;

        var hx = x + PlateStripOffsetX + PlateW - HighlightRightInset; // plate-right − 48, per the painter
        var hy = y + (PlateH - HighlightH) / 2; // y kept on the plate; strip is shorter than the plate
        AddChild(new TextureRect
        {
            Position = PanelPoint(hx, hy),
            Size = new Vector2(HighlightW, HighlightH),
            Texture = strip,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private void BuildPlate(int x, int y, int actionId, int serverIndex, int statusSrcX)
    {
        var e = _servers[serverIndex];

        // Per-plate build/draw order is the binary's (CONFIRMED, element-level pass 2026-06-19):
        //   (1) parchment select BUTTON → (2) NAME label → (3) calligraphy FACE → (4) STATUS caption →
        //   (5) COUNT label. Insertion order IS paint order, so the FACE (added 3rd) draws ON TOP of the
        //   parchment button (added 1st) — that is what makes the scroll calligraphy visible (the prior
        //   order drew the opaque parchment over the face, hiding it → empty scroll).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 (plate widget inventory — G2-confirmed, IDB 263bd994)
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 (selectable gate)

        // (1) Parchment select button (clickable). Drawn FIRST = bottom of the plate stack.
        // Paint-time selectable gate (§4.3.5 static-confirmed): the paint routine enables plate
        // interactivity only when StatusCode==0. When StatusCode!=0 the button is rendered as Disabled.
        // The click handler ALSO enforces LoadCount<2400 (commit sub-gate, "to-confirm" in §4.3.5 —
        // applied via entry.IsSelectable which checks both gates). See OnPlateClicked.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5
        Texture2D? normal = _atlas.SliceByPath(AtlasD, PlateSrcX, PlateSrcY, PlateW, PlateH);
        Texture2D? hover = _atlas.SliceByPath(AtlasD, PlateHoverSrcX, PlateHoverSrcY, PlateW, PlateH);

        var btn = new TextureButton
        {
            Position = PanelPoint(x + PlateStripOffsetX, y),
            Size = new Vector2(PlateW, PlateH),
            CustomMinimumSize = new Vector2(PlateW, PlateH),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = normal,
            TextureHover = hover,
            TexturePressed = hover,
            TextureDisabled = normal,
            // Paint-time gate: StatusCode!=0 → Disabled (non-interactive appearance).
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 "StatusCode==0 toggles plate interactivity"
            Disabled = e.StatusCode != 0
        };
        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        // (2) NAME label (behind the face).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 "Name label (30+233·i, 390, 174×21)"
        AddPlateName(x, e);

        // (3) Calligraphy FACE image (100×372 per-column quad, src(448+124·i, 6)), drawn ON TOP of the
        // parchment button (z-order CONFIRMED 2026-06-19 — face inserted after the button).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 "Plate face image (30+233·i+47, 97, 100×372)"
        Texture2D? face = _atlas.SliceByPath(AtlasD, statusSrcX, StatusIconSrcY, StatusIconW, StatusIconH);
        if (face is not null)
            AddChild(new TextureRect
            {
                Position = PanelPoint(x + StatusIconOffsetX, y),
                Size = new Vector2(StatusIconW, StatusIconH),
                Texture = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore // decoration: never blocks button hit. spec §4.3.2.
            });

        // (4) STATUS caption (§4.3.2 "Status/load caption", y=410, font slot 4, coloured per §4.3.4).
        // (5) SPARE label (§4.3.2 "Spare label", y=430, EMPTY STRING).
        AddPlateStatus(x, e);
        AddPlateCount(x);
    }

    private void OnPlateClicked(int actionId)
    {
        // Record index: (action−400) + 2·page. spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3
        var idx = 2 * _page + (actionId - ActionPlate0);
        if (idx >= 0 && idx < _servers.Count)
        {
            var entry = _servers[idx];

            // Click-handler commit guard (§4.3.5): StatusCode==0 AND LoadCount<2400.
            // The paint-time gate (Disabled=StatusCode!=0) already prevents most non-selectable clicks,
            // but the spec says to apply BOTH gates in the handler as well ("to-confirm" sub-gate).
            // ServerListEntryView.IsSelectable encodes StatusCode==0 && Load<2400.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 (selectable gate, commit guard)
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Commit guard"; login_flow.md §2.1.
            if (!entry.IsSelectable)
            {
                GD.Print($"[ServerSelectSubView] Plate action {actionId} ignored: server {entry.ServerId} " +
                         $"unavailable (status={entry.StatusCode}, load={entry.Load}). " +
                         "spec: frontend_layout_tables.md §4.3.5 commit guard: StatusCode==0 && LoadCount<2400.");
                return;
            }

            // Intent only — passive view; LoginWindow lane wires SelectServerAsync → connect.
            EmitSignal(SignalName.ServerSelected, entry.ServerId);
        }
    }

    private void BuildPagers()
    {
        // §4.3.1 NAME-STRIP TABS: 10 buttons (115+i), localX=13+47·i, localY=66, 47×18, A2.
        // §4.3.6 PAGER: 3 glyph controls repositioned each repaint using actions in 115..124.
        // Per-repaint re-arm: all 10 strips reset to blank N(500,792)/H(500,810)/P(500,810);
        // then strip[1]→N(500,828)/H(500,846), strip[2]→N(500,864)/H(605,985), strip[3]→N(710,985)/H(815,985).
        // Strips with real art are shown; blank strips stay hidden.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 (name-strip tabs, G2-confirmed, IDB 263bd994)
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.6 (pager, no record-count text)
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 (pager re-arm geometry, CONFIRMED binary-exact)
        for (var i = 0; i < PagerCount; i++)
        {
            var x = 13 + i * 47; // spec: frontend_layout_tables.md §4.3.1 "localX = 13+47·i" (G2-confirmed)
            var actionId = PagerActionBase + i;

            // All 10 strips start at the blank atlas region. spec: §4.2 "all 10 reset to blank".
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 N(500,792)/H(500,810)/P(500,810)
            Texture2D? texN = _atlas.SliceByPath(AtlasB, PagerBlankNSrcX, PagerBlankNSrcY, PagerW, PagerH);
            Texture2D? texH = _atlas.SliceByPath(AtlasB, PagerBlankHSrcX, PagerBlankHSrcY, PagerW, PagerH);
            Texture2D? texP = _atlas.SliceByPath(AtlasB, PagerBlankHSrcX, PagerBlankPSrcY, PagerW, PagerH);
            var hasRealArt = false;

            // Three strips get real art after the blank reset. spec: §4.2.
            switch (i)
            {
                case 1:
                    // strip[1] → N(500,828)/H(500,846). spec: frontend_layout_tables.md §4.2
                    texN = _atlas.SliceByPath(AtlasB, Pager1NSrcX, Pager1NSrcY, PagerW, PagerH);
                    texH = _atlas.SliceByPath(AtlasB, Pager1HSrcX, Pager1HSrcY, PagerW, PagerH);
                    texP = texH;
                    hasRealArt = true;
                    break;
                case 2:
                    // strip[2] → N(500,864)/H(605,985). spec: frontend_layout_tables.md §4.2
                    texN = _atlas.SliceByPath(AtlasB, Pager2NSrcX, Pager2NSrcY, PagerW, PagerH);
                    texH = _atlas.SliceByPath(AtlasB, Pager2HSrcX, Pager2HSrcY, PagerW, PagerH);
                    texP = texH;
                    hasRealArt = true;
                    break;
                case 3:
                    // strip[3] → N(710,985)/H(815,985). spec: frontend_layout_tables.md §4.2
                    texN = _atlas.SliceByPath(AtlasB, Pager3NSrcX, Pager3NSrcY, PagerW, PagerH);
                    texH = _atlas.SliceByPath(AtlasB, Pager3HSrcX, Pager3HSrcY, PagerW, PagerH);
                    texP = texH;
                    hasRealArt = true;
                    break;
            }

            var btn = new TextureButton
            {
                Position = PanelPoint(x, PagerY),
                Size = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                TextureNormal = texN,
                TextureHover = texH,
                TexturePressed = texP,
                // Strips with real art are shown; blank-UV strips stay hidden (the invisible hit-strip).
                // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 / §4 "hidden page-jump strip"
                Visible = hasRealArt
            };

            var capturedAction = actionId;
            btn.Pressed += () => OnPagerClicked(capturedAction);
            AddChild(btn);
        }
    }

    private void BuildPanelFrame()
    {
        // BACKDROP LAYER 1 (drawn first): full-screen A2 (loginwindow.dds) dst(0,110,1024,490) src(0,0).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4
        //   "Backdrop is TWO layers: a full-screen A2 image at (0,110) 1024×490, source (0,0), drawn FIRST"
        // Absolute canvas coords — not panel-relative. spec §4.
        Texture2D? backdrop = _atlas.SliceByPath(AtlasB, 0, 0, BackdropW, BackdropH); // src(0,0)
        if (backdrop is not null)
            AddChild(new TextureRect
            {
                Position = new Vector2(BackdropX, BackdropY), // abs (0,110) spec §4
                Size = new Vector2(BackdropW, BackdropH), // 1024×490 spec §4
                Texture = backdrop,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });

        // BACKDROP LAYER 2: list-box scroll panel (270,85,483,490) src(0,490). spec §4.
        Texture2D? frame = _atlas.SliceByPath(AtlasB,
            LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY,
            LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H);
        if (frame is not null)
            AddChild(new TextureRect
            {
                Position = PanelPoint(0, 0),
                Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),
                Texture = frame,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });

        // Title "서버선택": baked A2 image dst(207,44) 70×17 src(0,980). spec §4.
        // "Title '서버선택' = a baked atlas image (not a msg string): atlas A2, dst(207,44) 70×17, source(0,980)"
        // Absolute canvas coords — not panel-relative. spec §4.
        Texture2D? title = _atlas.SliceByPath(AtlasB, TitleSrcX, TitleSrcY,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H);
        if (title is not null)
            AddChild(new TextureRect
            {
                Position = new Vector2(TitleAbsX, TitleAbsY), // abs (207,44) spec §4
                Size = new Vector2(LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H),
                Texture = title,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });
    }

    private void BuildFlagImage()
    {
        // EVENT badge: baked A1 (login_slice1.dds) dst(407,−3) 210×70 src(743,398).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4
        //   "EVENT badge = a baked image: atlas A1, dst(407,−3) 210×70, source(743,398)"
        // Uses LoginLayout.QuitDecoPlate which captures exactly this rect (A1 src 743,398 210×70 dst 407,−3).
        // Absolute canvas coords — not panel-relative. spec §4.
        var r = LoginLayout.QuitDecoPlate; // A1 dst(407,-3,210,70) src(743,398). spec §4.
        Texture2D? tex = _atlas.SliceByPath(AtlasA1, r.SrcX, r.SrcY, r.W, r.H);
        if (tex is null)
            return;

        AddChild(new TextureRect
        {
            Position = new Vector2(r.X, r.Y), // abs (407,-3) spec §4
            Size = new Vector2(r.W, r.H), // 210×70 spec §4
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private void OnPagerClicked(int actionId)
    {
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "page = action − 115 (absolute, not relative)"
        _page = ClampPage(actionId - PagerActionBase);
        GD.Print(
            $"[ServerSelectSubView] Pager action {actionId} -> page {_page} (re-page only). spec: frontend_layout_tables.md §4.");
        RebuildLayout();
    }

    // NAME label @ (30+233·i, 390, 174×21): font slot 0, center-aligned (align mode 2).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 "Name label (30+233·i, 390) font slot 0 center-aligned"
    private void AddPlateName(int plateX, ServerListEntryView e)
    {
        var name = ResolveServerName(e);
        AddRowLabel(name, plateX, RowLabelY0, RowLabelH0, Colors.White);
    }

    // STATUS/LOAD CAPTION label @ (30+233·i, 410, 174×20): font slot 4, center-aligned, coloured per §4.3.4.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 "Status/load caption (30+233·i, 410) font slot 4"
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 (colour ladder — G2-confirmed, IDB 263bd994)
    private void AddPlateStatus(int plateX, ServerListEntryView e)
    {
        var statusCaption = ResolveStatusCaption(e, out var statusColor);
        AddRowLabel(statusCaption, plateX, RowLabelY1, RowLabelH, statusColor, 4);
    }

    // SPARE label @ (30+233·i, 430, 174×20): EMPTY STRING — not drawn.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 "Spare label (30+233·i, 430) — empty string"
    // The earlier "%4d / %4d population count" at +430 was dead-debug, never drawn (superseded 2026-06-19).
    private void AddPlateCount(int plateX)
    {
        AddRowLabel(string.Empty, plateX, RowLabelY2, RowLabelH, Colors.White);
    }

    /// <summary>
    ///     Resolves the status-caption text and colour for the y=410 slot-4 label (§4.3.2).
    ///     Implements the two colour branches from §4.3.4 and the status-3 scheduled-open path.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 (colour ladder — G2-confirmed, IDB 263bd994)
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.1 (status caption resolver 4029+StatusCode)
    /// </summary>
    private string ResolveStatusCaption(ServerListEntryView e, out Color color)
    {
        // OpenTimeFlag (+6): nonzero → LoadCount is a raw threshold count (Branch A);
        // zero → LoadCount is a discrete level 4/3/2 (Branch B).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3 "OpenTimeFlag" / §4.3.4 "Discrete-level branch"
        var loadValid = e.OpenTime != 0; // e.OpenTime = wire field +6 (OpenTimeFlag)

        if (e.StatusCode == 0)
        {
            // Two colour ladders selected by OpenTimeFlag (+6).
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4
            if (loadValid)
            {
                // Branch A: threshold ladder (LoadCount is raw count, strict greater-than).
                // Boundaries: >1200 red / 801..1200 orange / 501..800 yellow / ≤500 green.
                // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 (threshold branch, G2-confirmed)
                if (e.Load > PopRedThreshold) // LoadCount > 1200 → red 0xFFFF0000
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load > PopOrangeThreshold) // LoadCount 801..1200 → orange 0xFFED6806
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load > PopYellowThreshold) // LoadCount 501..800 → yellow 0xFFFFFF00
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }
            else
            {
                // Branch B: discrete-level ladder (LoadCount is exact equality, OpenTimeFlag==0).
                // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 "Discrete-level branch"
                if (e.Load == PopLevelRed) // == 4 → red 0xFFFF0000
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load == PopLevelOrange) // == 3 → orange 0xFFED6806
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load == PopLevelYellow) // == 2 → yellow 0xFFFFFF00
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }

            // Default (≤500 raw or discrete 0/1/5+) → status caption msg(4029+StatusCode), green 0xFFB5FF7A.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.4 "≤500 → 4029 (status caption reused)"
            color = PopColorGreen;
            return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty); // 4029+StatusCode
        }

        if (e.StatusCode == 3)
        {
            // Scheduled-open branch: LoadCount==24 → msg 6004; else msg 6005 HH:MM.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 "StatusCode==3: msg 6004 / 6005"
            color = Colors.White;
            if (e.Load == 24)
                return _text.GetCaption((int)LoginLayout.MsgServerPreparing, string.Empty);

            // msg 6005 snprintf'd as HH:MM: Load(+4)=hour, OpenTime(+6)=minute.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 "snprintf(msg 6005, …) HH:MM from +4/+6"
            var template = _text.GetCaption((int)LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatScheduledTime(template, e.Load, e.OpenTime);
        }

        // Other StatusCodes → caption msg(4029+StatusCode), no colour override.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 "caption_id = 4029+StatusCode, StatusCode 0..3"
        color = Colors.White;
        return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty); // 4029+StatusCode
    }

    private void AddRowLabel(string text, int x, int y, int h, Color color,
        int fontSlot = 0, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label
        {
            Text = text,
            Position = PanelPoint(x, y),
            Size = new Vector2(RowLabelW, h),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        // Apply font slot override (slot 4 = DotumChe 12 w800 for population label).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 (slot 4 = DotumChe 12 w800)
        HudFont.ApplyToLabel(label, fontSlot);
        AddChild(label);
    }

    private int ClampPage(int requestedPage)
    {
        var pageCount = Math.Max(1, (_servers.Count + 1) / 2);
        return Math.Clamp(requestedPage, 0, pageCount - 1);
    }

    private string BuildPageBreadcrumb(int firstIndex, int visibleCount)
    {
        var plate0 = DescribePlate(0, firstIndex);
        var plate1 = visibleCount > 1 ? DescribePlate(1, firstIndex + 1) : "plate1=<none>";
        return $"[ServerSelectSubView] page {_page}: {plate0}, {plate1}";
    }

    private string DescribePlate(int plateSlot, int serverIndex)
    {
        var e = _servers[serverIndex];
        var name = ResolveServerName(e);
        return $"plate{plateSlot}=server {e.ServerId} '{name}' load {e.Load} selectable={e.IsSelectable}";
    }

    private static Vector2 PanelPoint(int x, int y)
    {
        return new Vector2(PanelX + x, PanelY + y);
    }

    private string ResolveServerName(ServerListEntryView e)
    {
        // Name resolver: ServerId 1..40 → name_id = 5000+ServerId (msg bank 5001..5040, flat, no multiplier).
        // Out-of-range ServerId → fallback caption 5901 ("unknown server #n" template).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3 "Server name resolution"
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 "Name resolver" (DROP 5301 base — superseded)
        if (e.ServerId is >= 1 and <= 40)
            return _text.GetCaption(ServerNameCaptionBase + e.ServerId, string.Empty);

        // Out-of-range → fallback 5901. spec: §4.3.3 / §4.1
        var template = _text.GetCaption(LoginLayout.MsgServerUnknown, string.Empty);
        return FormatCaption(template, e.ServerId, string.Empty);
    }


    // Formats the status_code==3 scheduled-open caption (msg 6005). The painter snprintf's FOUR digit args
    // in order (hourTens, hourOnes, minTens, minOnes). We honour whichever placeholder style the CP949
    // msg uses: four single %d (the binary's exact form) → four digits; {0}/{0:00} or %02d → hour/minute;
    // otherwise a plain HH:MM fallback. spec: frontend_layout_tables.md §4 (status_code==3 → msg 6005).
    private static string FormatScheduledTime(string template, int hour, int minute)
    {
        int hourTens = hour / 10 % 10, hourOnes = hour % 10;
        int minTens = minute / 10 % 10, minOnes = minute % 10;
        var fallback = $"{hour:00}:{minute:00}";

        if (template.Length == 0)
            return fallback;

        // Four single %d (the binary's snprintf form): substitute the four digits in order.
        if (CountOccurrences(template, "%d") >= 4)
        {
            var s = template;
            foreach (var d in (ReadOnlySpan<int>)[hourTens, hourOnes, minTens, minOnes])
                s = ReplaceFirst(s, "%d", d.ToString(CultureInfo.InvariantCulture));
            return s;
        }

        // {0}/{0:00} or %02d styles take hour + minute as whole values.
        return FormatCaption(template, hour, minute, fallback);
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0, idx = 0;
        while ((idx = value.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += token.Length;
        }

        return count;
    }

    // Formats a caption template from msg.xdb that may use {0}/{0:00} or %02d placeholders.
    // Only used for status_code==3 HH:MM (MsgServerClockFormat) and the out-of-range server name fallback.
    private static string FormatCaption(string template, int value0, string fallback)
    {
        return FormatCaption(template, value0, 0, fallback);
    }

    private static string FormatCaption(string template, int value0, int value1, string fallback)
    {
        if (template.Length == 0) return fallback;
        try
        {
            if (template.Contains("{0", StringComparison.Ordinal))
                return string.Format(CultureInfo.InvariantCulture, template, value0, value1);

            if (template.Contains("%02d", StringComparison.Ordinal))
            {
                var s = ReplaceFirst(template, "%02d",
                    value0.ToString("00", CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%02d",
                    value1.ToString("00", CultureInfo.InvariantCulture));
            }

            if (template.Contains("%d", StringComparison.Ordinal))
            {
                var s = ReplaceFirst(template, "%d",
                    value0.ToString(CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%d", value1.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (FormatException)
        {
            return fallback;
        }

        return template.Length > 0 ? template : fallback;
    }

    private static string ReplaceFirst(string value, string oldValue, string newValue)
    {
        var idx = value.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? value : value[..idx] + newValue + value[(idx + oldValue.Length)..];
    }
}