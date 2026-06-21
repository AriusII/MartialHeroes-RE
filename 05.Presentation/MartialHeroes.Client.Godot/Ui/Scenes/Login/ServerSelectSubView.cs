// Ui/Scenes/Login/ServerSelectSubView.cs
//
// Server-select sub-view for the Login(1) state.
//
// Renders the server-list overlay (sub-states 34..41) as an internal sub-view of LoginWindow.
// Backed entirely by HudAtlasLibrary + HudTextLibrary — no UiAssetLoader dependency.
//
// Layout (spec: Docs/RE/specs/frontend_layout_tables.md §4):
//   Central server-list area: dst(270,85,483,490), A2 src(0,490).
//   Select strips 400/401: loginwindow_02.dds src(9,6)/(220,6), dst(x-6,97,202,372).
//   Status icons: loginwindow_02.dds dst(x+47,97,100,372), src(448+124·i,6).
//   Pager/name-strip buttons: loginwindow.dds dst(13+47*i,66,47,18), src(596,985)/(643,985), actions 115..124.
//   Population colour (status==0; CORRECTION 2026-06-20, TWO ladders selected by the +6 load-valid flag):
//     +6!=0 (raw count): >1200 red(6001) / >800 orange(6002) / >500 yellow(6003) / ≤500 green(4029).
//     +6==0 (discrete level): ==4 red(6001) / ==3 orange(6002) / ==2 yellow(6003) / else green(4029).
//   Commit gate: status(+2)==0 AND load(+4) < 2400 (not overloaded). spec: frontend_layout_tables.md §4.2.
//   Server display names: msg bank 5001..5040 → name_id = 5000 + server_id (out-of-range → 5901).
//   Count label (+430): EMPTY — the "%4d / %4d" line is dead-debug, never drawn.
//   Special row: SERVER-ID(+0) == 100 (NOT status) → 3 indicator quads A2 src(500,786) 60×39.
//   Selection highlight: A4 src(700,18) 46×168 on the plate whose ServerId == NewServerIndex
//     (uiconfig.lua-sourced NEW_SERVER_INDEX; CORRECTION 2026-06-21: NOT the Lastserver registry value).
//     Lastserver is WRITTEN on commit (persist to registry); it is NOT the highlight key here.
//   Visible plate order is STABLE: page i shows raw records [2i] and [2i+1] sequentially (CORRECTION
//     2026-06-21). Fisher-Yates permutation hits a separate parallel id-vector only; must NEVER reorder
//     displayed plates.
//
// spec: Docs/RE/specs/frontend_layout_tables.md §4
// spec: Docs/RE/specs/frontend_layout_tables.md §4.2 (CORRECTION 2026-06-21)
// spec: Docs/RE/specs/login_flow.md §2.1

using System.Globalization;
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens;
using MartialHeroes.Client.Presentation.Screens.Layout;

// ServerEntry (moved to engine-free layer)

// LoginLayout, WidgetRect (moved to engine-free layer)

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
///     Server-select sub-view for Login(1) sub-states 34..41.
///     <para>
///         Renders up to two server "plate" sprites for the current page, with pager tabs
///         and per-plate population-colour captions. All atlas drawing comes from
///         <see cref="HudAtlasLibrary" />; caption text from <see cref="HudTextLibrary" />.
///     </para>
///     <para>
///         Subscribe to <see cref="ServerSelected" /> to receive the chosen server id.
///         Passive intent only — never mutates domain state.
///     </para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §4
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

    // Load thresholds for status-caption coloring. spec: Docs/RE/specs/frontend_layout_tables.md §4
    // "status_code==0 with load-valid: load>1200 → red; >800 → orange; >500 → yellow; ≤500 → green"
    private const int PopRedThreshold = 1200; // spec: frontend_layout_tables.md §4
    private const int PopOrangeThreshold = 800; // spec: frontend_layout_tables.md §4
    private const int PopYellowThreshold = 500; // spec: frontend_layout_tables.md §4

    // Discrete load levels (status==0, load-INVALID +6==0): exact-equality colour ladder.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 Branch B (CORRECTION 2026-06-20).
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

    // Population colour DWORDs (ARGB). spec: Docs/RE/specs/frontend_layout_tables.md §4
    private static readonly Color PopColorRed = Color.Color8(255, 0, 0);
    private static readonly Color PopColorOrange = Color.Color8(237, 104, 6);
    private static readonly Color PopColorYellow = Color.Color8(255, 255, 0);
    private static readonly Color PopColorGreen = Color.Color8(181, 255, 122);

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
    private IReadOnlyList<ServerEntry> _servers = [];

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
    ///         CORRECTION 2026-06-21: the authoritative list painter compares each record's <c>ServerId</c>
    ///         against this value — <strong>not</strong> against the <c>Lastserver</c> registry value.
    ///         <c>Lastserver</c> is <em>written</em> on commit (persisted to registry); it is not the
    ///         highlight key in this painter.
    ///     </para>
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Default-selection highlight" (CORRECTION 2026-06-21).
    /// </summary>
    public int NewServerIndex { get; set; } = -1;

    /// <summary>
    ///     Alias retained for call-site compatibility. Forwards to <see cref="NewServerIndex" />.
    ///     The authoritative highlight key is <see cref="NewServerIndex" /> (NEW_SERVER_INDEX from
    ///     uiconfig.lua); Lastserver is only written on commit — it is not read back by this painter.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.2 (CORRECTION 2026-06-21).
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
    ///     Populates the server list. Rebuilds the plate layout to match the count.
    ///     Clears the loading flag (transitions from sub-state 35 fetching to 36/37 resolved).
    ///     Must be called on the main thread (Control mutation).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4 sub-states 35→36→37
    ///     spec: Docs/RE/specs/login_flow.md §2.1
    /// </summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
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

        // STABLE PLATE ORDER (CORRECTION 2026-06-21): page i always shows raw records [2i] and [2i+1]
        // read sequentially from the raw record array. A Fisher-Yates permutation is performed each repaint,
        // but it operates on a SEPARATE PARALLEL server-id vector (only observable effect: which id is
        // persisted to the Lastserver registry). It NEVER reorders the visible plates. Do NOT shuffle
        // the displayed rows.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "The on-screen plate order is STABLE" (CORRECTION 2026-06-21).
        var firstIndex = _page * 2; // spec: frontend_layout_tables.md §4.2 "page i shows raw records [2i]/[2i+1]"
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

    // Builds the default-selection highlight strip behind the plate at (x,y) whose ServerId matches
    // NEW_SERVER_INDEX (uiconfig.lua-sourced; CORRECTION 2026-06-21: not Lastserver).
    // atlas A4 src(700,18) 46×168, positioned at the plate's right edge (x = plate.dstX + plate.width − 48).
    // Draw order: this method is called BEFORE BuildPlate in RebuildVisiblePage, so AddChild here inserts
    // the strip BEFORE the plate node — Godot draws earlier siblings behind later ones, placing the
    // highlight strip behind the selected plate. spec: frontend_layout_tables.md §4.2 "Selection highlight
    // strip" and "drawn behind the selected plate" (CORRECTION 2026-06-21).
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
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 (the on-scroll calligraphy is the per-column
        //   FACE quad src(448+124·i, 6) drawn over the parchment; the server name is small slot-0 text).

        // (1) Parchment select button (clickable). Drawn FIRST = bottom of the plate stack.
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
            TextureDisabled = normal
        };
        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        // (2) NAME label (behind the face). spec: §4 build order.
        AddPlateName(x, e);

        // (3) Calligraphy FACE image (100×372 per-column quad, src(448+124·i, 6)), drawn ON TOP of the
        // parchment button. spec: §4 (z-order CONFIRMED 2026-06-19 — face inserted after the button).
        Texture2D? face = _atlas.SliceByPath(AtlasD, statusSrcX, StatusIconSrcY, StatusIconW, StatusIconH);
        if (face is not null)
            AddChild(new TextureRect
            {
                Position = PanelPoint(x + StatusIconOffsetX, y),
                Size = new Vector2(StatusIconW, StatusIconH),
                Texture = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore // decoration: never blocks the button hit. spec §4.
            });

        // (4) STATUS caption + (5) COUNT label, drawn on top of the face. spec: §4.
        AddPlateStatus(x, e);
        AddPlateCount(x);
    }

    private void OnPlateClicked(int actionId)
    {
        // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "index = (action−400) + 2·page"
        var idx = 2 * _page + (actionId - ActionPlate0);
        if (idx >= 0 && idx < _servers.Count)
        {
            var entry = _servers[idx];

            // Commit guard: status_code == 0 && load < 2400. Failure = silent no-op.
            // ServerEntry.IsSelectable encodes exactly this: StatusCode == 0 && Load < 2400
            // (confirmed in MartialHeroes.Client.Presentation.Screens.ServerEntry).
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.2 "Commit guard".
            if (!entry.IsSelectable)
            {
                GD.Print($"[ServerSelectSubView] Plate action {actionId} ignored: server {entry.ServerId} " +
                         $"unavailable (status={entry.StatusCode}, load={entry.Load}). " +
                         "spec: frontend_layout_tables.md §4.2 commit guard: status==0 && load<2400.");
                return;
            }

            // Intent only — passive view; LoginWindow lane wires SelectServerAsync → connect.
            EmitSignal(SignalName.ServerSelected, entry.ServerId);
        }
    }

    private void BuildPagers()
    {
        // The 10 (115+i) page-jump buttons are a HIDDEN pager strip — re-parked to blank UV on each
        // repaint. They must NOT be rendered as visible "하왕관" tabs.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4
        //   "'Tabs' clarification: the ten 115+i buttons are a HIDDEN page-jump strip re-parked to a
        //    blank UV on each repaint. Do NOT render them as visible tabs."
        // The hit regions are still present (for action dispatch); they are just hidden.
        for (var i = 0; i < PagerCount; i++)
        {
            var x = 13 + i * 47; // spec: frontend_layout_tables.md §4 "pager (13+47·i, 66)"
            var actionId = PagerActionBase + i;

            var btn = new TextureButton
            {
                Position = PanelPoint(x, PagerY),
                Size = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                // No textures — blank UV per spec (hidden pager). spec §4.
                Visible = false // hidden: the shipped server-list shows no visible tab strip. spec §4.
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

    // NAME label @ (x, 390, 174×21): font slot 0, center-aligned, horizontal.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4
    //   "name label (30+233·i, 390, 174×21) … font slot 0 … center-aligned (align mode 2)"
    private void AddPlateName(int plateX, ServerEntry e)
    {
        var name = ResolveServerName(e);
        AddRowLabel(name, plateX, RowLabelY0, RowLabelH0, Colors.White);
    }

    // STATUS CAPTION label @ (x, 410, 174×20): font slot 4, center-aligned, colored per §4 branch.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4
    //   "status caption label (30+233·i, 410, 174×20) font slot 4 center-aligned colored per branch"
    //   "status_code==0 + load-valid: >1200→msg 6001 red; >800→msg 6002 orange; >500→msg 6003 yellow;
    //    ≤500→msg(4029+status_code) green (0xFFB5FF7A) — the available/사용가능 case"
    //   "status_code==3: load==24→msg 6004; else msg 6005 HH:MM"
    //   "other status_code: caption msg(4029+status_code), no color override"
    private void AddPlateStatus(int plateX, ServerEntry e)
    {
        var statusCaption = ResolveStatusCaption(e, out var statusColor);
        AddRowLabel(statusCaption, plateX, RowLabelY1, RowLabelH, statusColor, 4);
    }

    // COUNT label @ (x, 430, 174×20): set to EMPTY STRING per spec §4 CORRECTION 2026-06-19.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4
    //   "count label (30+233·i, 430, 174×20) … set to an EMPTY STRING by the painter.
    //    CORRECTION 2026-06-19: the prior '%4d / %4d population count, font slot 4 at +430' was wrong —
    //    the slot-4 label is the STATUS caption at +410; +430 is left blank."
    private void AddPlateCount(int plateX)
    {
        AddRowLabel(string.Empty, plateX, RowLabelY2, RowLabelH, Colors.White);
    }

    /// <summary>
    ///     Resolves the status-caption text and color for the +410 slot-4 label.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4
    ///     "Status / load coloring … slot-4 status caption at +410; ARGB re-confirmed 2026-06-18/2026-06-19"
    /// </summary>
    private string ResolveStatusCaption(ServerEntry e, out Color color)
    {
        // Load-valid flag = OpenTime/+6 nonzero. spec: §4.1 "+6 load-valid flag" (RESOLVED 2026-06-20).
        var loadValid = e.OpenTime != 0;

        if (e.StatusCode == 0)
        {
            // Two colour ladders, selected by the +6 load-valid flag. The painter
            // (Diamond_LoginWindow_PaintServerList 0x5fcd09) branches on *(record+6):
            //   +6 != 0 → Load is a RAW count, thresholded 1200/800/500;
            //   +6 == 0 → Load is a DISCRETE level, exact-equality 4/3/2.
            // Both reuse the SAME caption msgs (6001/6002/6003) and ARGB colors.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 (two colour ladders, CORRECTION 2026-06-20).
            if (loadValid)
            {
                // Threshold ladder (raw count, strict greater-than).
                if (e.Load > PopRedThreshold) // > 1200 → msg 6001, red 0xFFFF0000
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load > PopOrangeThreshold) // > 800 → msg 6002, orange 0xFFED6806
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load > PopYellowThreshold) // > 500 → msg 6003, yellow 0xFFFFFF00
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }
            else
            {
                // Discrete ladder (load-invalid, exact equality). spec: §4.1 Branch B (2026-06-20).
                if (e.Load == PopLevelRed) // == 4 → msg 6001, red
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load == PopLevelOrange) // == 3 → msg 6002, orange
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load == PopLevelYellow) // == 2 → msg 6003, yellow
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }

            // Default (≤500 raw, or discrete 0/1/5+) → status caption msg(4029+status_code), GREEN
            // 0xFFB5FF7A — the "available/사용가능" case. spec: frontend_layout_tables.md §4.1.
            color = PopColorGreen;
            return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty); // spec: §4 (4029+status_code)
        }

        if (e.StatusCode == 3)
        {
            // Scheduled-open: load==24 → msg 6004 (preparing); else msg 6005 HH:MM.
            // spec: frontend_layout_tables.md §4 "status_code==3: msg 6004 only when load==24, else 6005 HH:MM"
            color = Colors.White;
            if (e.Load == 24)
                return _text.GetCaption((int)LoginLayout.MsgServerPreparing, string.Empty);

            // Faithful to the painter: msg 6005 is snprintf'd with FOUR digit args in order
            // (hourTens, hourOnes, minTens, minOnes), hour = Load(+4), minute = OpenTime(+6).
            // spec: frontend_layout_tables.md §4 "status_code==3 … snprintf(msg 6005, …) = HH:MM from +4/+6".
            var template = _text.GetCaption((int)LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatScheduledTime(template, e.Load, e.OpenTime);
        }

        // Other status codes → caption msg (4029 + status_code), no color override.
        // spec: frontend_layout_tables.md §4 "other status_code → caption msg(4029+status_code), no color override"
        color = Colors.White;
        return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty); // spec: §4 (4029+status_code)
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

    private string ResolveServerName(ServerEntry e)
    {
        // Display name resolves client-side from msg.xdb ids 5001..5040 (server_id N → id 5000+N).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "Name id = 5000 + server_id"
        if (e.ServerId is >= 1 and <= 40)
            return _text.GetCaption(ServerNameCaptionBase + e.ServerId, string.Empty);

        // Out-of-range fallback: msg 5901 (formatted). spec: §2.3
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