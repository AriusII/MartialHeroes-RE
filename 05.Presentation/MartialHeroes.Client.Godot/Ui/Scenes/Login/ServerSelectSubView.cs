// Ui/Scenes/Login/ServerSelectSubView.cs
//
// Server-select sub-view for the Login(1) state — REBUILT from scratch per §4.3.
//
// Spec: Docs/RE/specs/frontend_layout_tables.md §4.3 (binary-re-derived 2026, IDB 263bd994).
//
// ARCHITECTURAL CONTRACT (Rule 1 — ONE backdrop):
//   This sub-view is parented to LoginWindow._serverListRoot (a transparent container shown at
//   sub-state ≥ 35). The visual chrome frame behind the server list is:
//     • loginwindow.dds src(0,490) at abs(270,85) 483×490 — owned by LoginWindow._bannerFrame
//       (gated visible at state ≥ 35 by ApplyVisibility).
//   ServerSelectSubView must NOT draw any backdrop/frame — doing so causes the documented
//   double-draw white-box clutter.  It renders CONTENT ONLY:
//     title image, detail plates (§4.3.2), page tabs (§4.3.3), status indicators (§4.3.1),
//     and the selection highlight strip (§4.3.1).
//   The refresh (action 105) and back (action 102) controls are children of LoginWindow._formGroup
//   (§4.3.4) — this sub-view NEVER draws them.
//
// RULE 2 — Two plate slots per page, each gated on record existence.
//   plate[i] visible iff record (page*2 + i) exists.  Never render an empty second parchment.
//
// RULE 3 — Tabs HIDDEN unless they map to a real page.
//   All 10 tabs are built hidden.  On each repaint, re-arm three with real art, then show a tab
//   only if its page index < pageCount.  Never leave unused tabs visible (white-box source).
//
// RULE 4 — Exactly ONE refresh (action 105) + ONE back (action 102).
//   These controls belong to LoginWindow._formGroup.  Not in this file.
//
// RULE 5 — Plates in raw record order, centered server names.
//   page i → records [2i] and [2i+1].  Name resolver: 5000+ServerId.  Center-aligned slot-0 label.
//
// Layout (all coords LOCAL to the server-list content panel at canvas (270,85)):
//
// §4.3.1 Title "서버선택": A2 src(0,980) at local(207,44) 70×17.
// §4.3.2 DETAIL PLATES — 2 per page, X = 30+233·i (i=0,1):
//   • Select button  (30+233·i−6,  97, 202×372) A4 N(9,6)/H(220,6), action 400+i.
//   • Name label     (30+233·i,   390, 174×21)  font slot 0, center, msg 5000+ServerId.
//   • Plate face     (30+233·i+47, 97, 100×372) A4 src(448+124·i, 6).
//   • Status caption (30+233·i,   410, 174×20)  font slot 4, coloured per §4.3.5.
//   • Spare label    (30+233·i,   430, 174×20)  EMPTY STRING.
// §4.3.3 PAGE TABS — 10 built hidden; x=13+47·i, y=66, 47×18, A2.
//   Re-arm 3 with real art each repaint; show tab i only if i < pageCount.
// §4.3.6 Selection highlight: A4 src(700,18) 46×168 behind matching plate.
// §4.3.5 Status indicators ×3: A2 src(500,786) 60×39, hidden; re-anchored for ServerId==100 row.
//
// spec: Docs/RE/specs/frontend_layout_tables.md §4 / §4.1 / §4.2 / §4.3

using System.Globalization;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
///     Server-select sub-view (Login sub-states 35..37) — rebuilt from §4.3 (IDB 263bd994).
///     <para>
///         Renders content only (NO backdrop/frame — see §4.3.0): title, two plate slots gated
///         on record existence, 10 page tabs kept hidden unless mapping to a valid page, status
///         indicators, and selection highlight.  Refresh / back live in LoginWindow._formGroup.
///     </para>
///     <para>
///         Strictly passive: all user gestures are translated to <see cref="ServerSelected" />
///         signals.  No domain mutation.
///     </para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3 (binary-re-derived 2026, IDB 263bd994)
/// </summary>
public sealed partial class ServerSelectSubView : Control
{
    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    // -------------------------------------------------------------------------
    // Atlas constants
    // spec: Docs/RE/specs/frontend_layout_tables.md §1
    // A2 = loginwindow.dds, A4 = loginwindow_02.dds
    // -------------------------------------------------------------------------

    private const string AtlasA2 = "data/ui/loginwindow.dds";     // A2 spec: §1
    private const string AtlasA4 = "data/ui/loginwindow_02.dds";  // A4 spec: §1

    // -------------------------------------------------------------------------
    // Content-panel local-origin (the sub-view is full-canvas; these are the
    // local offsets added to localX/localY per §0a.4 / §4.3.1).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 "local to the content panel at (270,85)"
    // -------------------------------------------------------------------------

    private const int PanelX = 270; // spec: §4.3.1 content panel dst(270,85)
    private const int PanelY = 85;  // spec: §4.3.1

    // -------------------------------------------------------------------------
    // Title "서버선택" — baked A2 image at local(207,44) 70×17 src(0,980).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 "Title image | local(207,44) 70×17 A2 src(0,980)"
    // -------------------------------------------------------------------------

    private const int TitleLocalX = 207; // spec: §4.3.1
    private const int TitleLocalY = 44;  // spec: §4.3.1
    private const int TitleSrcX   = 0;   // spec: §4.3.1
    private const int TitleSrcY   = 980; // spec: §4.3.1

    // -------------------------------------------------------------------------
    // Detail plates — 2-column loop, x = 30+233·i, action = 400+i.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2
    // -------------------------------------------------------------------------

    private const int PlateBaseX0       = 30;   // spec: §4.3.2 i=0
    private const int PlateBaseX1       = 263;  // spec: §4.3.2 i=1 (30+233)
    private const int PlateY            = 97;   // spec: §4.3.2

    // Select button (clickable parchment): (x−6, 97, 202×372) A4 N(9,6)/H(220,6)/P(220,6).
    // spec: §4.3.2 "Select button (parchment)"
    private const int PlateSelectOffX   = -6;   // spec: §4.3.2 "30+233·i−6"
    private const int PlateSelectW      = 202;  // spec: §4.3.2
    private const int PlateSelectH      = 372;  // spec: §4.3.2
    private const int PlateNSrcX        = 9;    // spec: §4.3.2 Normal
    private const int PlateNSrcY        = 6;    // spec: §4.3.2
    private const int PlateHSrcX        = 220;  // spec: §4.3.2 Hover/Pressed
    private const int PlateHSrcY        = 6;    // spec: §4.3.2

    // Plate-face image: (x+47, 97, 100×372) A4 src(448+124·i, 6) — per-column calligraphy.
    // spec: §4.3.2 "Plate-face image"
    private const int PlateFaceOffX     = 47;   // spec: §4.3.2 "30+233·i+47"
    private const int PlateFaceW        = 100;  // spec: §4.3.2
    private const int PlateFaceH        = 372;  // spec: §4.3.2
    private const int PlateFaceSrcX0   = 448;  // spec: §4.3.2 i=0 src(448,6)
    private const int PlateFaceSrcX1   = 572;  // spec: §4.3.2 i=1 src(572,6) (448+124)
    private const int PlateFaceSrcY    = 6;    // spec: §4.3.2

    // Name label: (x, 390, 174×21) font slot 0, center-aligned, msg 5000+ServerId.
    // spec: §4.3.2 "Name label"
    private const int NameLabelLocalY  = 390;  // spec: §4.3.2
    private const int NameLabelW       = 174;  // spec: §4.3.2
    private const int NameLabelH       = 21;   // spec: §4.3.2

    // Status/load caption: (x, 410, 174×20) font slot 4, centre, coloured per §4.3.5.
    // spec: §4.3.2 "Status/load caption"
    private const int StatusLabelLocalY = 410;  // spec: §4.3.2
    private const int StatusLabelW      = 174;  // spec: §4.3.2
    private const int StatusLabelH      = 20;   // spec: §4.3.2

    // Spare label: (x, 430, 174×20) empty string — never drawn.
    // spec: §4.3.2 "Spare label — painter sets empty string"
    private const int SpareLabelLocalY  = 430;  // spec: §4.3.2

    // Plate action ids. spec: §4.3.2 action 400/401.
    private const int ActionPlate0 = 400; // spec: §4.3.2
    private const int ActionPlate1 = 401; // spec: §4.3.2

    // -------------------------------------------------------------------------
    // Page tabs — 10 built hidden; local x=13+47·i, y=66, 47×18, A2.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3
    // -------------------------------------------------------------------------

    private const int PagerBaseX    = 13;   // spec: §4.3.3 "13+47·i"
    private const int PagerStrideX  = 47;   // spec: §4.3.3
    private const int PagerLocalY   = 66;   // spec: §4.3.3
    private const int PagerW        = 47;   // spec: §4.3.3
    private const int PagerH        = 18;   // spec: §4.3.3
    private const int PagerCount    = 10;   // spec: §4.3.3
    private const int PagerActionBase = 115; // spec: §4.3.3 action 115+i (page=action−115)

    // Build-time blank art N(596,985)/H(643,985) — initial face (NOT displayed; tabs start hidden).
    // spec: §4.3.3 "built with Normal(596,985)/Hover(643,985)/Pressed(643,985)"
    private const int PagerBuildNSrcX = 596; // spec: §4.3.3
    private const int PagerBuildNSrcY = 985; // spec: §4.3.3
    private const int PagerBuildHSrcX = 643; // spec: §4.3.3
    private const int PagerBuildHSrcY = 985; // spec: §4.3.3

    // Per-repaint blank reset: N(500,792)/H(500,810)/P(500,810).
    // spec: §4.3.4 "each repaint all 10 tabs reset to blank N(500,792)/H(500,810)/P(500,810)"
    private const int PagerBlankNSrcX = 500; // spec: §4.3.4
    private const int PagerBlankNSrcY = 792; // spec: §4.3.4
    private const int PagerBlankHSrcX = 500; // spec: §4.3.4
    private const int PagerBlankHSrcY = 810; // spec: §4.3.4

    // Real pager art for tabs 1, 2, 3 (zero-indexed). spec: §4.3.4 "pager re-arm geometry".
    private const int Pager1NSrcX = 500;  // spec: §4.3.4 strip[1] N(500,828)
    private const int Pager1NSrcY = 828;  // spec: §4.3.4
    private const int Pager1HSrcX = 500;  // spec: §4.3.4 strip[1] H(500,846)
    private const int Pager1HSrcY = 846;  // spec: §4.3.4
    private const int Pager2NSrcX = 500;  // spec: §4.3.4 strip[2] N(500,864)
    private const int Pager2NSrcY = 864;  // spec: §4.3.4
    private const int Pager2HSrcX = 605;  // spec: §4.3.4 strip[2] H(605,985)
    private const int Pager2HSrcY = 985;  // spec: §4.3.4
    private const int Pager3NSrcX = 710;  // spec: §4.3.4 strip[3] N(710,985)
    private const int Pager3NSrcY = 985;  // spec: §4.3.4
    private const int Pager3HSrcX = 815;  // spec: §4.3.4 strip[3] H(815,985)
    private const int Pager3HSrcY = 985;  // spec: §4.3.4

    // -------------------------------------------------------------------------
    // Selection highlight strip — A4 src(700,18) 46×168, drawn behind the matching plate.
    // Highlight key: ServerId == NEW_SERVER_INDEX (from uiconfig.lua), NOT Lastserver.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 / §4.3.6
    // -------------------------------------------------------------------------

    private const int HighlightSrcX = 700;  // spec: §4.3.1
    private const int HighlightSrcY = 18;   // spec: §4.3.1
    private const int HighlightW    = 46;   // spec: §4.3.1
    private const int HighlightH    = 168;  // spec: §4.3.1

    // -------------------------------------------------------------------------
    // Special-row sentinel: record ServerId (+0) == 100 → display-only, lights indicator quads.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 (ServerId==100 sentinel, NOT StatusCode)
    // -------------------------------------------------------------------------

    private const int SpecialRowServerId = 100; // spec: §4.3.5 / §4.1

    // -------------------------------------------------------------------------
    // Name / status / population resolvers.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.5 / §4.1
    // -------------------------------------------------------------------------

    // Name: ServerId 1..40 → msg 5000+ServerId; out-of-range → msg 5901.
    // spec: §4.3.5 "5000+ServerId (flat, no multiplier)"
    private const int ServerNameMsgBase = 5000; // spec: §4.1 / §4.3.5

    // Status caption: 4029+StatusCode (StatusCode 0..3 → 4029..4032).
    // spec: §4.3.5 "caption_id = 4029+StatusCode"
    private const int StatusCaptionMsgBase = 4029; // spec: §4.1 / §4.3.5

    // Population colour thresholds (StatusCode==0, OpenTimeFlag!=0 branch A).
    // spec: §4.3.5 "Branch A — LoadCount > 1200 red / 801..1200 orange / 501..800 yellow / ≤500 green"
    private const int PopThreshRed    = 1200; // spec: §4.3.5
    private const int PopThreshOrange = 800;  // spec: §4.3.5 (> 800 → orange)
    private const int PopThreshYellow = 500;  // spec: §4.3.5 (> 500 → yellow)

    // Discrete-level branch B (OpenTimeFlag==0): exact equality.
    // spec: §4.3.5 "Branch B — ==4 red / ==3 orange / ==2 yellow / else green"
    private const int PopLevelRed    = 4; // spec: §4.3.5
    private const int PopLevelOrange = 3; // spec: §4.3.5
    private const int PopLevelYellow = 2; // spec: §4.3.5

    // Population colour ARGB DWORDs. spec: §4.3.5 (G2-confirmed, IDB 263bd994)
    private static readonly Color PopColorRed    = Color.Color8(255, 0,   0);   // 0xFFFF0000 spec: §4.3.5
    private static readonly Color PopColorOrange = Color.Color8(237, 104, 6);   // 0xFFED6806 spec: §4.3.5
    private static readonly Color PopColorYellow = Color.Color8(255, 255, 0);   // 0xFFFFFF00 spec: §4.3.5
    private static readonly Color PopColorGreen  = Color.Color8(181, 255, 122); // 0xFFB5FF7A spec: §4.3.5

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary  _text;

    // Per-repaint pager tab node references (built once, updated each repaint).
    // Stored so we can re-skin and re-show/hide without a full rebuild.
    private readonly TextureButton?[] _pagerTabs = new TextureButton?[PagerCount];

    // Three status-color indicator quads (A2 src(500,786) 60×39), hidden by default.
    // Re-anchored around a ServerId==100 special row when present.
    // spec: §4.3.1 "Status-indicator quad ×3 | A2 src(500,786) 60×39 | hidden"
    private readonly TextureRect?[] _statusIndicators = new TextureRect?[3];

    // Sub-state 35 loading sentinel: true until SetServers() is called.
    private bool _loading = true;
    private int  _page;
    private IReadOnlyList<ServerListEntryView> _servers = [];

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>Creates the server-select sub-view.</summary>
    public ServerSelectSubView(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text  = text;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>
    ///     The <c>NEW_SERVER_INDEX</c> value from <c>uiconfig.lua</c>, used to pre-highlight
    ///     the default server plate.  <c>-1</c> = no highlight.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.6 "Default highlight key"
    /// </summary>
    public int NewServerIndex { get; set; } = -1;

    /// <summary>
    ///     Alias for call-site compatibility — forwards to <see cref="NewServerIndex" />.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.6
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
    ///     Populates the server list. Clears the loading flag and rebuilds the layout.
    ///     Must be called on the main thread.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2 / §4.3.3
    /// </summary>
    public void SetServers(IReadOnlyList<ServerListEntryView> servers)
    {
        _loading = false;
        _servers = servers;
        _page    = 0;
        RebuildLayout();
    }

    // -------------------------------------------------------------------------
    // Layout builder — full rebuild on SetServers / page change
    // -------------------------------------------------------------------------

    private void RebuildLayout()
    {
        // Reset indicator refs before freeing children.
        for (var i = 0; i < _statusIndicators.Length; i++)
            _statusIndicators[i] = null;
        for (var i = 0; i < _pagerTabs.Length; i++)
            _pagerTabs[i] = null;

        for (var i = GetChildCount() - 1; i >= 0; i--)
        {
            var child = GetChild(i);
            RemoveChild(child);
            child.QueueFree();
        }

        // Build in z-order (add-order = paint order).
        // §4.3.1: title image, then per-record plate groups (highlight behind plate), then overlays.
        BuildTitle();
        BuildStatusIndicators();
        BuildPagerTabs();
        RebuildVisiblePage();
    }

    // -------------------------------------------------------------------------
    // Title "서버선택" — A2 local(207,44) 70×17 src(0,980). §4.3.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 "Title | local(207,44) 70×17 A2 src(0,980)"
    // -------------------------------------------------------------------------

    private void BuildTitle()
    {
        var tex = _atlas.SliceByPath(
            AtlasA2,
            TitleSrcX, TitleSrcY,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H); // 70×17 spec: §4.3.1
        if (tex is null) return;

        AddChild(new TextureRect
        {
            Position   = LocalToCanvas(TitleLocalX, TitleLocalY), // local(207,44) → canvas(477,129)
            Size       = new Vector2(LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H),
            Texture    = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    // -------------------------------------------------------------------------
    // Status-indicator quads ×3 — A2 src(500,786) 60×39, hidden. §4.3.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1
    // -------------------------------------------------------------------------

    private void BuildStatusIndicators()
    {
        var tex = _atlas.SliceByPath(
            AtlasA2,
            LoginLayout.StatusIndicatorSrcX, LoginLayout.StatusIndicatorSrcY,
            LoginLayout.StatusIndicatorW,    LoginLayout.StatusIndicatorH);

        for (var i = 0; i < LoginLayout.StatusIndicatorCount; i++) // spec: §4.3.1 ×3
        {
            var rect = new TextureRect
            {
                Texture     = tex,
                Size        = new Vector2(LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible     = false // hidden by default. spec: §4.3.1.
            };
            _statusIndicators[i] = rect;
            AddChild(rect);
        }
    }

    // -------------------------------------------------------------------------
    // Page tabs (10 built hidden) — A2, local x=13+47·i, y=66, 47×18. §4.3.3.
    // All 10 start hidden; RebuildVisiblePage re-skins and re-shows the valid ones.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3
    // -------------------------------------------------------------------------

    private void BuildPagerTabs()
    {
        for (var i = 0; i < PagerCount; i++)
        {
            var localX  = PagerBaseX + i * PagerStrideX; // spec: §4.3.3 "13+47·i"
            var actionId = PagerActionBase + i;           // spec: §4.3.3 "115+i"

            // Build-time face art (tab 0 default / tabs 1,2 alternate as per §4.3.3).
            // These are overwritten each repaint; the initial face doesn't matter because
            // all tabs are built HIDDEN. spec: §4.3.3.
            var buildN = _atlas.SliceByPath(AtlasA2, PagerBuildNSrcX, PagerBuildNSrcY, PagerW, PagerH);
            var buildH = _atlas.SliceByPath(AtlasA2, PagerBuildHSrcX, PagerBuildHSrcY, PagerW, PagerH);

            var btn = new TextureButton
            {
                Position         = LocalToCanvas(localX, PagerLocalY),
                Size             = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode      = TextureButton.StretchModeEnum.Scale,
                TextureNormal    = buildN,
                TextureHover     = buildH,
                TexturePressed   = buildH,
                // RULE 3: built HIDDEN — only shown when the page is valid. spec: §4.3.3.
                Visible = false
            };

            var captured = actionId;
            btn.Pressed += () => OnPagerClicked(captured);
            _pagerTabs[i] = btn;
            AddChild(btn);
        }
    }

    // -------------------------------------------------------------------------
    // Per-page repaint — plates, pager visibility, indicators.
    // -------------------------------------------------------------------------

    private void RebuildVisiblePage()
    {
        _page = ClampPage(_page);

        // Hide all status indicators by default. spec: §4.3.1.
        foreach (var ind in _statusIndicators)
            if (ind is not null) ind.Visible = false;

        // RULE 3 — Re-arm pager tabs. §4.3.3 / §4.3.4.
        RearmPagerTabs();

        if (_loading)
        {
            // Sub-state 35: fetch worker running, records not yet received.
            // spec: §4.3.7 state 35 "fetching — progress shown"
            GD.Print("[ServerSelectSubView] state 35 — loading server list (waiting for SetServers).");
            return;
        }

        if (_servers.Count == 0)
        {
            // Sub-state 36 error: 0 records → LoginWindow.RaiseServerListError (msg 4027).
            // spec: §4.3.7 / §2.2 state 36 "0 records → msg 4027"
            GD.Print($"[ServerSelectSubView] state 36 error — no servers (msg {LoginLayout.MsgErrNoServers}).");
            return;
        }

        // STABLE PLATE ORDER: page i → records [2i] and [2i+1]. §4.3.6.
        // spec: §4.3.6 "page i shows raw records [2·page] and [2·page+1] in order"
        var firstIdx     = _page * 2;
        var visibleCount = Math.Min(2, _servers.Count - firstIdx); // RULE 2 gated.

        int[]  plateX    = [PlateBaseX0, PlateBaseX1];
        int[]  faceSrcX  = [PlateFaceSrcX0, PlateFaceSrcX1];
        int[]  actions   = [ActionPlate0, ActionPlate1];

        for (var slot = 0; slot < visibleCount; slot++)
        {
            var idx = firstIdx + slot;

            // Selection highlight — drawn BEFORE the plate so it sits behind it. §4.3.6.
            // Highlight key = NEW_SERVER_INDEX (NOT Lastserver). spec: §4.3.6.
            if (NewServerIndex >= 0 && _servers[idx].ServerId == NewServerIndex)
                BuildSelectionHighlight(plateX[slot], PlateY);

            BuildPlate(plateX[slot], PlateY, actions[slot], idx, faceSrcX[slot]);

            // ServerId==100 special-row: re-anchor indicator quads. spec: §4.3.5 / §4.1.
            if (_servers[idx].ServerId == SpecialRowServerId)
                AnchorStatusIndicators(plateX[slot], PlateY);
        }

        GD.Print(BuildPageBreadcrumb(firstIdx, visibleCount));
    }

    // -------------------------------------------------------------------------
    // Pager tab re-arm — RULE 3.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.3 / §4.3.4
    // -------------------------------------------------------------------------

    private void RearmPagerTabs()
    {
        if (_servers.Count == 0 || _loading)
        {
            // No valid pages — ensure all tabs hidden.
            foreach (var t in _pagerTabs)
                if (t is not null) t.Visible = false;
            return;
        }

        // Total pages: ceil(serverCount / 2). spec: §4.3.6 "remaining-record guard".
        var pageCount = (_servers.Count + 1) / 2;

        for (var i = 0; i < PagerCount; i++)
        {
            var tab = _pagerTabs[i];
            if (tab is null) continue;

            // Step 1: reset to blank art. spec: §4.3.4 "all 10 tabs reset to blank N(500,792)/H(500,810)/P(500,810)".
            var blankN = _atlas.SliceByPath(AtlasA2, PagerBlankNSrcX, PagerBlankNSrcY, PagerW, PagerH);
            var blankH = _atlas.SliceByPath(AtlasA2, PagerBlankHSrcX, PagerBlankHSrcY, PagerW, PagerH);
            tab.TextureNormal  = blankN;
            tab.TextureHover   = blankH;
            tab.TexturePressed = blankH;

            // Step 2: give real art to tabs 1, 2, 3. spec: §4.3.4 "three of them get real pager art".
            switch (i)
            {
                case 1:
                    // strip[1] → N(500,828)/H(500,846). spec: §4.3.4.
                    tab.TextureNormal  = _atlas.SliceByPath(AtlasA2, Pager1NSrcX, Pager1NSrcY, PagerW, PagerH);
                    tab.TextureHover   = _atlas.SliceByPath(AtlasA2, Pager1HSrcX, Pager1HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
                case 2:
                    // strip[2] → N(500,864)/H(605,985). spec: §4.3.4.
                    tab.TextureNormal  = _atlas.SliceByPath(AtlasA2, Pager2NSrcX, Pager2NSrcY, PagerW, PagerH);
                    tab.TextureHover   = _atlas.SliceByPath(AtlasA2, Pager2HSrcX, Pager2HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
                case 3:
                    // strip[3] → N(710,985)/H(815,985). spec: §4.3.4.
                    tab.TextureNormal  = _atlas.SliceByPath(AtlasA2, Pager3NSrcX, Pager3NSrcY, PagerW, PagerH);
                    tab.TextureHover   = _atlas.SliceByPath(AtlasA2, Pager3HSrcX, Pager3HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
            }

            // Step 3: RULE 3 — show tab only when page i exists. spec: §4.3.3
            // "only strips for valid pages are made visible; the remainder stay hidden"
            tab.Visible = i < pageCount;
        }
    }

    // -------------------------------------------------------------------------
    // Detail plate builder — 5 widgets per slot, insertion = paint order. §4.3.2.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.2
    // -------------------------------------------------------------------------

    private void BuildPlate(int x, int y, int actionId, int serverIndex, int faceSrcX)
    {
        var e = _servers[serverIndex];

        // Insertion / paint order (§4.3.2):
        //   (1) select button (parchment) → (2) name label → (3) face image → (4) status caption → (5) spare label.
        // The face is drawn AFTER / ON TOP OF the button — that is the z-order that makes the
        // baked calligraphy visible (opaque parchment would hide it if inserted later). §4.3.2.

        // (1) Select button (parchment) — clickable; paint-time gate StatusCode==0. §4.3.5.
        // spec: §4.3.2 "Select button (parchment) (x−6, 97, 202×372) A4 N(9,6)/H(220,6)/P(220,6)"
        var btnN = _atlas.SliceByPath(AtlasA4, PlateNSrcX, PlateNSrcY, PlateSelectW, PlateSelectH);
        var btnH = _atlas.SliceByPath(AtlasA4, PlateHSrcX, PlateHSrcY, PlateSelectW, PlateSelectH);
        var btn  = new TextureButton
        {
            Position          = LocalToCanvas(x + PlateSelectOffX, y),
            Size              = new Vector2(PlateSelectW, PlateSelectH),
            CustomMinimumSize = new Vector2(PlateSelectW, PlateSelectH),
            IgnoreTextureSize = true,
            StretchMode       = TextureButton.StretchModeEnum.Scale,
            TextureNormal     = btnN,
            TextureHover      = btnH,
            TexturePressed    = btnH,
            TextureDisabled   = btnN,
            // Paint-time gate: StatusCode!=0 → Disabled (non-interactive). spec: §4.3.5.
            Disabled = e.StatusCode != 0
        };
        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        // (2) Name label: font slot 0, center-aligned, msg 5000+ServerId. §4.3.2.
        // spec: §4.3.2 "Name label (x, 390, 174×21) font slot 0 center-aligned"
        AddRowLabel(ResolveServerName(e), x, NameLabelLocalY, NameLabelW, NameLabelH, Colors.White, fontSlot: 0);

        // (3) Plate-face image: (x+47, 97, 100×372) A4 src(448+124·i, 6). §4.3.2.
        // Drawn AFTER the button — ON TOP in paint order (face shows through). spec: §4.3.2 z-order.
        var face = _atlas.SliceByPath(AtlasA4, faceSrcX, PlateFaceSrcY, PlateFaceW, PlateFaceH);
        if (face is not null)
            AddChild(new TextureRect
            {
                Position    = LocalToCanvas(x + PlateFaceOffX, y),
                Size        = new Vector2(PlateFaceW, PlateFaceH),
                Texture     = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore // decoration only — never blocks input. spec: §4.3.2.
            });

        // (4) Status/load caption: font slot 4, center, coloured per §4.3.5. §4.3.2.
        // spec: §4.3.2 "Status/load caption (x, 410, 174×20) font slot 4"
        var statusText  = ResolveStatusCaption(e, out var statusColor);
        AddRowLabel(statusText, x, StatusLabelLocalY, StatusLabelW, StatusLabelH, statusColor, fontSlot: 4);

        // (5) Spare label: empty string — never drawn. §4.3.2.
        // spec: §4.3.2 "Spare label (x, 430, 174×20) painter sets EMPTY STRING"
        AddRowLabel(string.Empty, x, SpareLabelLocalY, StatusLabelW, StatusLabelH, Colors.White, fontSlot: 0);
    }

    // -------------------------------------------------------------------------
    // Selection highlight strip — A4 src(700,18) 46×168, behind the plate. §4.3.1 / §4.3.6.
    // Called BEFORE BuildPlate so the strip is inserted earlier (behind in paint order).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.3.1 "Selection-highlight strip"
    // -------------------------------------------------------------------------

    private void BuildSelectionHighlight(int plateLocalX, int plateLocalY)
    {
        var tex = _atlas.SliceByPath(AtlasA4, HighlightSrcX, HighlightSrcY, HighlightW, HighlightH);
        if (tex is null) return;

        // The highlight positions behind the plate. Use the parchment button origin for alignment.
        // Centre the highlight strip vertically on the plate. spec: §4.3.1.
        var hx = plateLocalX + PlateSelectOffX; // left-align with parchment button
        var hy = plateLocalY + (PlateSelectH - HighlightH) / 2; // vertical centre on plate
        AddChild(new TextureRect
        {
            Position    = LocalToCanvas(hx, hy),
            Size        = new Vector2(HighlightW, HighlightH),
            Texture     = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    // -------------------------------------------------------------------------
    // Status indicator re-anchor for ServerId==100 special row. §4.3.5 / §4.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 / §4.3.5
    // anchorX/Y = the plate's local dst-X/dst-Y (the select-button origin).
    // quad 0 → (anchorX−30, anchorY−13); quads 1 and 2 → (anchorX+139, anchorY+13).
    // -------------------------------------------------------------------------

    private void AnchorStatusIndicators(int anchorX, int anchorY)
    {
        // Quad 0. spec: §4.1 "(anchorX−30, anchorY−13)".
        if (_statusIndicators[0] is { } ind0)
        {
            ind0.Position = LocalToCanvas(anchorX - 30, anchorY - 13);
            ind0.Visible  = true;
        }

        // Quads 1 and 2 — overlap exactly (faithful duplicate). spec: §4.1 "(anchorX+139, anchorY+13)".
        for (var i = 1; i <= 2; i++)
            if (_statusIndicators[i] is { } ind)
            {
                ind.Position = LocalToCanvas(anchorX + 139, anchorY + 13);
                ind.Visible  = true;
            }
    }

    // -------------------------------------------------------------------------
    // Label helper — adds a row label as a child at LocalToCanvas(x, y).
    // -------------------------------------------------------------------------

    private void AddRowLabel(string text, int localX, int localY, int w, int h, Color color,
        int fontSlot = 0, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var lbl = new Label
        {
            Text               = text,
            Position           = LocalToCanvas(localX, localY),
            Size               = new Vector2(w, h),
            HorizontalAlignment = align,
            VerticalAlignment  = VerticalAlignment.Center,
            AutowrapMode       = TextServer.AutowrapMode.Off,
            MouseFilter        = MouseFilterEnum.Ignore
        };
        lbl.AddThemeColorOverride("font_color", color);
        HudFont.ApplyToLabel(lbl, fontSlot);
        AddChild(lbl);
    }

    // -------------------------------------------------------------------------
    // Name resolver — §4.3.5 / §4.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 / §4.3.5
    // ServerId 1..40 → msg 5000+ServerId; out-of-range → msg 5901 fallback.
    // -------------------------------------------------------------------------

    private string ResolveServerName(ServerListEntryView e)
    {
        if (e.ServerId is >= 1 and <= 40)
            return _text.GetCaption(ServerNameMsgBase + e.ServerId, string.Empty); // 5001..5040 spec: §4.1

        // Out-of-range → fallback 5901. spec: §4.1 / §4.3.5.
        var tmpl = _text.GetCaption(LoginLayout.MsgServerUnknown, string.Empty);
        return FormatCaption(tmpl, e.ServerId, string.Empty);
    }

    // -------------------------------------------------------------------------
    // Status caption + colour resolver — §4.3.5 / §4.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4.1 / §4.3.5
    // -------------------------------------------------------------------------

    private string ResolveStatusCaption(ServerListEntryView e, out Color color)
    {
        var loadValid = e.OpenTime != 0; // +6 != 0 → Branch A (threshold). spec: §4.3.5.

        if (e.StatusCode == 0)
        {
            if (loadValid)
            {
                // Branch A: raw threshold ladder. spec: §4.3.5.
                if (e.Load > PopThreshRed)    { color = PopColorRed;    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed,    string.Empty); }
                if (e.Load > PopThreshOrange) { color = PopColorOrange; return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty); }
                if (e.Load > PopThreshYellow) { color = PopColorYellow; return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty); }
            }
            else
            {
                // Branch B: discrete equality. spec: §4.3.5.
                if (e.Load == PopLevelRed)    { color = PopColorRed;    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed,    string.Empty); }
                if (e.Load == PopLevelOrange) { color = PopColorOrange; return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty); }
                if (e.Load == PopLevelYellow) { color = PopColorYellow; return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty); }
            }

            // Default (≤500 or discrete 0/1) → green, status caption 4029. spec: §4.3.5.
            color = PopColorGreen;
            return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty); // 4029+0=4029
        }

        if (e.StatusCode == 3)
        {
            // Scheduled-open: LoadCount==24 → msg 6004; else msg 6005 HH:MM. spec: §4.1.
            color = Colors.White;
            if (e.Load == 24)
                return _text.GetCaption((int)LoginLayout.MsgServerPreparing, string.Empty); // msg 6004

            var tmpl = _text.GetCaption((int)LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatScheduledTime(tmpl, e.Load, e.OpenTime);
        }

        // Other StatusCodes: caption 4029+StatusCode, no colour override. spec: §4.1.
        color = Colors.White;
        return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty);
    }

    // -------------------------------------------------------------------------
    // Click handlers
    // -------------------------------------------------------------------------

    private void OnPlateClicked(int actionId)
    {
        // Record index: (action−400) + 2·page. spec: §4.3.2 / §4.3.6.
        var idx = 2 * _page + (actionId - ActionPlate0);
        if (idx < 0 || idx >= _servers.Count) return;

        var entry = _servers[idx];

        // Click-handler commit guard: StatusCode==0 && LoadCount<2400. spec: §4.3.6.
        if (!entry.IsSelectable)
        {
            GD.Print($"[ServerSelectSubView] Plate action {actionId} ignored: server {entry.ServerId} " +
                     $"unavailable (status={entry.StatusCode}, load={entry.Load}). " +
                     "spec: §4.3.6 commit guard StatusCode==0 && LoadCount<2400.");
            return;
        }

        EmitSignal(SignalName.ServerSelected, entry.ServerId);
    }

    private void OnPagerClicked(int actionId)
    {
        // page = action − 115 (absolute, not relative). spec: §4.3.3 / §4.3.4.
        _page = ClampPage(actionId - PagerActionBase);
        GD.Print($"[ServerSelectSubView] Pager action {actionId} → page {_page}. spec: §4.3.3.");
        RebuildLayout();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private int ClampPage(int requested)
    {
        var pageCount = Math.Max(1, (_servers.Count + 1) / 2);
        return Math.Clamp(requested, 0, pageCount - 1);
    }

    /// <summary>
    ///     Converts content-panel local coordinates to canvas (absolute) coordinates.
    ///     All positions in §4.3 are LOCAL to the content panel at canvas (270,85).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §0a.2 / §0a.4 / §4.3.1
    /// </summary>
    private static Vector2 LocalToCanvas(int localX, int localY)
        => new(PanelX + localX, PanelY + localY);

    private string BuildPageBreadcrumb(int firstIdx, int visibleCount)
    {
        var sb = $"[ServerSelectSubView] page {_page}: ";
        for (var slot = 0; slot < visibleCount; slot++)
        {
            var e    = _servers[firstIdx + slot];
            var name = ResolveServerName(e);
            sb += $"plate{slot}=srv{e.ServerId}'{name}' load={e.Load} sel={e.IsSelectable}; ";
        }

        if (visibleCount < 2) sb += "plate1=<none>;";
        return sb.TrimEnd();
    }

    // -------------------------------------------------------------------------
    // Status_code==3 HH:MM formatter. spec: §4.1 "msg 6005 snprintf".
    // -------------------------------------------------------------------------

    private static string FormatScheduledTime(string template, int hour, int minute)
    {
        int hh = hour / 10 % 10, hl = hour % 10;
        int mh = minute / 10 % 10, ml = minute % 10;
        var fallback = $"{hour:00}:{minute:00}";

        if (template.Length == 0) return fallback;

        // Four %d (binary's snprintf form): substitute four digit args in order.
        if (CountOccurrences(template, "%d") >= 4)
        {
            var s = template;
            foreach (var d in (ReadOnlySpan<int>)[hh, hl, mh, ml])
                s = ReplaceFirst(s, "%d", d.ToString(CultureInfo.InvariantCulture));
            return s;
        }

        return FormatCaption(template, hour, minute, fallback);
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0, idx = 0;
        while ((idx = value.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        { count++; idx += token.Length; }
        return count;
    }

    private static string FormatCaption(string template, int v0, string fallback)
        => FormatCaption(template, v0, 0, fallback);

    private static string FormatCaption(string template, int v0, int v1, string fallback)
    {
        if (template.Length == 0) return fallback;
        try
        {
            if (template.Contains("{0", StringComparison.Ordinal))
                return string.Format(CultureInfo.InvariantCulture, template, v0, v1);
            if (template.Contains("%02d", StringComparison.Ordinal))
            {
                var s = ReplaceFirst(template, "%02d", v0.ToString("00", CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%02d", v1.ToString("00", CultureInfo.InvariantCulture));
            }
            if (template.Contains("%d", StringComparison.Ordinal))
            {
                var s = ReplaceFirst(template, "%d", v0.ToString(CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%d", v1.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (FormatException) { return fallback; }
        return template.Length > 0 ? template : fallback;
    }

    private static string ReplaceFirst(string value, string old, string replacement)
    {
        var idx = value.IndexOf(old, StringComparison.Ordinal);
        return idx < 0 ? value : value[..idx] + replacement + value[(idx + old.Length)..];
    }
}
