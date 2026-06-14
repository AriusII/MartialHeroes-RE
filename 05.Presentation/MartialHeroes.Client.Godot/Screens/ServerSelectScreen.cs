// Screens/ServerSelectScreen.cs
//
// Server-selection overlay — pixel-faithful to §11.4 of frontend_scenes.md.
//
// COMPOSITION MODEL (§11.4, CODE-CONFIRMED):
//   All four atlases from the login set: A=login_slice1.dds, B=loginwindow.dds,
//   C=InventWindow.dds, D=loginwindow_02.dds (parchment, DXT2 premultiplied).
//
//   Layout on the 1024×768 reference canvas (§11.0, §11.4):
//   [z=1]  A full-background panel    src(0,0,1024,398)   → dst(0,0,1024,398)
//   [z=2]  A bottom-bar band          src(0,582,1024,442) → dst(0,326,1024,442)  (Y=326 = 326×768/768)
//   [z=2.5]B ink-wash painting        src(0,0,1024,490)   → dst(0,110,1024,490)  shared with Login §11.2a
//   [z=3]  D parchment PLATE col0     src(9,6,202,372)    → dst(24,97,202,372)   channel toggle 400
//   [z=3]  D parchment PLATE col1     src(9,6,202,372)    → dst(257,97,202,372)  channel toggle 401
//   [z=4]  D parchment BODY  col0     src(448,6,100,372)  → dst(77,97,100,372)   baked calligraphy art
//   [z=4]  D parchment BODY  col1     src(572,6,100,372)  → dst(310,97,100,372)
//   [z=5]  D scrollbar thumb          src(700,18,46,168)  → dst(0,runtime,46,168)
//   [z=6]  B server-row buttons ×10  src(596,985,47,18) / hover(643,985)
//              loop: X = 13+47·n (n=0..9), Y=66, 47×18   → actions 115..124
//   [z=7]  B scroll-UP   src(483,490,13,10) → dst(467,86,13,10)
//   [z=7]  B scroll-DOWN src(505,490,13,10) → dst(467,455,13,10)
//   [z=7]  B thumb-dot   src(496,490,9,9)   → dst(469,98,9,9)
//   [z=7]  A refresh btn src(792,398,111,38) → dst(456,-3,111,38)  action 105
//   [z=7]  A refresh face src(743,398,210,70)→ dst(407,-3,210,70)  baked art
//   [z=8]  C notice dialog  src(318,647,340,190) → dst(342,289,340,190)  hidden
//   [z=9]  C error  dialog  src(318,647,340,190) → dst(342,289,340,190)  hidden
//
//   Server-row content list:  placed inside the left parchment body (dst 77,97..469).
//   The selected server's channel info appears in the right parchment body (dst 310,97..469).
//
// ROW BUTTON GEOMETRY (CODE-CONFIRMED §11.4 loop):
//   n=0..9 → X = 13+47·n ∈ {13,60,107,154,201,248,295,342,389,436}  Y=66  W=47 H=18
//   Action id = 115 + n   → range 115..124
//
// STATUS / LOAD PRESENTATION (§2.3, CODE-CONFIRMED):
//   load > 1200 → Full (red); > 800 → High (orange); > 500 → Medium (yellow); ≤ 500 → Light (green).
//   status 2/3/4 with open_time==0 → fixed label (no clock). spec §2.3. CODE-CONFIRMED.
//   status_code==3, load==24 → "Preparing" (load==24 is a load sentinel, NOT a status code). spec §2.3.
//   status_code==3, open_time!=0 → "HH:MM" clock: HH=(load/10,load%10), MM=(open_time/10,open_time%10). §2.4.
//   status_code==100 → auto-connect sentinel.
//   NOTE: 24 is a LOAD sentinel under status 3; it is NOT a top-level status code. spec §2.3. CODE-CONFIRMED.
//
// NEW BADGE (§2.7, CODE-CONFIRMED):
//   NEW_SERVER_INDEX = 5 (uiconfig.lua global; hardcoded for offline flow). spec §2.7. CODE-CONFIRMED.
//   The record whose server_id == NEW_SERVER_INDEX receives the "NEW" badge — resolved at render time.
//
// RANDOMIZED ORDER (§2.7, CODE-CONFIRMED):
//   Display order is shuffled clock-seeded, with Lastserver anchored at slot 0 when present.
//   Lastserver is persisted via Godot ConfigFile (user://) on selection. spec §2.5. CODE-CONFIRMED.
//
// PASSIVE: zero game logic.  Reads a view-model list; turns row clicks into ServerSelected(serverId).
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 (CODE-CONFIRMED literals).
//       §1.9 (msg.xdb id table — column headers 4029/4030/4031/4032). CODE-CONFIRMED.
//       §2 (server-selection presentation rules).
//       §2.3 (status / load color thresholds). CODE-CONFIRMED.
//       §2.4 (scheduled-open HH:MM clock packing — /10,%10 digit-split). CODE-CONFIRMED.
//       §2.5 (Lastserver persistence). CODE-CONFIRMED.
//       §2.7 (randomized display order; NEW_SERVER_INDEX badge). CODE-CONFIRMED.
//       §2.8 (localized server names — string banks 5001..5040). CODE-CONFIRMED.
//       Docs/RE/specs/login_flow.md §2.1 (8-byte record decode). CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// View-model for one server entry — mirrors the 8-byte lobby record.
/// spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED field order.
/// </summary>
public sealed record ServerEntry(
    /// <summary>Index 1..40 into the client-local name table. spec §2.8.</summary>
    int ServerId,
    /// <summary>Display name (client-local, never on the wire). spec §2.8.
    /// TODO: resolve via string banks 5001..5040 (msg.xdb) when MsgXdbCatalog is wired. spec §2.8.</summary>
    string DisplayName,
    /// <summary>Availability sentinel. Special values per §2.3:
    ///   0 = normal/open; 2/3/4 = status-label variants; 3+open_time!=0 = scheduled clock;
    ///   100 = auto-connect sentinel. NOTE: 24 is a load sentinel under status 3, NOT a status code.</summary>
    int StatusCode,
    /// <summary>Population gauge / scheduled HH. Thresholds: 1200/800/500. spec §2.1. CODE-CONFIRMED.
    ///   When status_code==3 and open_time!=0: load is the hours field (HH = load/10, load%10). spec §2.4.</summary>
    int Load,
    /// <summary>Scheduled open-time minutes field (when status_code==3). spec §2.4.
    ///   MM = open_time/10, open_time%10 (digit-split). CODE-CONFIRMED.</summary>
    int OpenTime);

/// <summary>
/// Server-selection overlay.  Pixel-faithful to §11.4.
/// Emits <see cref="ServerSelected"/> (server_id) on row click.
/// Emits <see cref="BackRequested"/> when Back is clicked.
/// </summary>
public sealed partial class ServerSelectScreen : Control
{
    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>
    /// Raised when the player selects a server row.  Carries server_id (1..40).
    /// spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    /// <summary>Raised when Back is clicked (returns to login).</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // =========================================================================
    // NEW_SERVER_INDEX — which server_id receives the "NEW" badge.
    // uiconfig.lua global (value 5 in the sampled client); hardcoded for offline flow.
    // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    // =========================================================================
    private const int NewServerIndex = 5; // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.

    // =========================================================================
    // Lastserver persistence (user:// ConfigFile, layer-05 only).
    // Written on selection; read on build to anchor the remembered server first.
    // spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED.
    // =========================================================================
    private const string LastServerCfgPath = "user://server_select.cfg"; // layer-05 only, never in core.
    private const string LastServerCfgSection = "server_select";
    private const string LastServerCfgKey = "Lastserver";

    private static void PersistLastServer(int serverId)
    {
        // Write Lastserver to user:// ConfigFile. spec §2.5. CODE-CONFIRMED (registry in legacy;
        // user:// ConfigFile is the Godot equivalent for layer-05).
        var cfg = new ConfigFile();
        cfg.SetValue(LastServerCfgSection, LastServerCfgKey, serverId);
        cfg.Save(LastServerCfgPath);
    }

    private static int LoadLastServer()
    {
        // Read Lastserver from user:// ConfigFile. Returns 0 (none) if absent. spec §2.5.
        var cfg = new ConfigFile();
        if (cfg.Load(LastServerCfgPath) != Error.Ok) return 0;
        return (int)cfg.GetValue(LastServerCfgSection, LastServerCfgKey, 0);
    }

    // =========================================================================
    // Server list (view-model)
    // =========================================================================

    private IReadOnlyList<ServerEntry>? _servers;

    /// <summary>
    /// Sets the server list.  Call before adding to the tree, or call <see cref="SetServers"/> after.
    /// </summary>
    public IReadOnlyList<ServerEntry>? Servers
    {
        get => _servers;
        set
        {
            _servers = value;
            if (_servers is not null)
                _displayOrder = BuildDisplayOrder(_servers);
            if (IsInsideTree()) RebuildServerLabels();
        }
    }

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>Optionally inject a shared asset loader.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    /// <summary>
    /// Optionally inject a real-client VFS handle for loading .xeff effect textures.
    /// When null the effect player shows a fallback visual.
    /// </summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // Internal state: which of the 10 row-tabs is currently "active" (selected/highlighted).
    private int _activeRowIndex = -1; // -1 = none selected yet

    // Label container inside the left parchment body (for server list content).
    private Control? _serverListContent;

    // Display-order index array — maps screen slot → servers[] index.
    // Built per SetServers call: clock-seeded shuffle with Lastserver pinned first when present.
    // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    private List<int>? _displayOrder;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ServerSelectScreen] Build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // =========================================================================
    // UI construction (§11.4 pixel-exact)
    // =========================================================================

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // =======================================================================
        // [z=1] Full background art panel.
        // A src(0,0,1024,398) → dst(0,0,1024,398). spec §11.4 "Full background art panel". CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bgSlice = _assets.Slice(
            LoginLayout.AtlasLoginSlice1, 0, 0, 1024, 398);
        if (bgSlice is not null)
        {
            var bgRect = new TextureRect
            {
                Name = "BgArt",
                Texture = bgSlice,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(0, 0),
                Size = new Vector2(1024, 398),
            };
            AddChild(bgRect);
        }
        else
        {
            // Offline fallback: dark background.
            var fb = new ColorRect
            {
                Name = "BgFallback",
                Color = new Color(0.05f, 0.04f, 0.08f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            fb.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fb);
        }

        // =======================================================================
        // [z=2] Bottom-bar band (the stone plate the ID/PW form sits on — reused here).
        // A src(0,582,1024,442) → dst(0,326,1024,442). Y=326 = 326×768/768. spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bottomBar = _assets.Slice(
            LoginLayout.AtlasLoginSlice1, 0, 582, 1024, 442);
        if (bottomBar is not null)
        {
            var barRect = new TextureRect
            {
                Name = "BottomBar",
                Texture = bottomBar,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(0, LoginLayout.BottomBarCanvasY), // Y=326. spec §11.0. CODE-CONFIRMED.
                Size = new Vector2(1024, 442),
            };
            AddChild(barRect);
        }

        // =======================================================================
        // [z=2.5] INK-WASH PAINTING BACKDROP — loginwindow.dds (B).
        // B@(0,110,1024,490) src(0,0) — the same warrior ink-wash painting that backs the Login screen.
        // Must sit BEHIND the two parchment plates (z=3) and all widgets, but IN FRONT of the
        // bottom stone bar (z=2) and the full-background art panel (z=1).
        // Without this layer the parchments float on bare stone — the official client shows
        // the full painting behind them at all times (spec §11.2a / §11.4 shared backdrop).
        // spec: Docs/RE/specs/frontend_scenes.md §11.2a "Main panel art". CODE-CONFIRMED.
        //       dst(0,110,1024,490) src(0,0,1024,490) — loginwindow.dds.
        // =======================================================================
        AtlasTexture? paintingBackdrop = _assets.Slice(
            LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY, // src(0,0). spec §11.2a. CODE-CONFIRMED.
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H); // 1024×490. spec §11.2a. CODE-CONFIRMED.
        if (paintingBackdrop is not null)
        {
            var paintingRect = new TextureRect
            {
                Name = "PaintingBackdrop",
                Texture = paintingBackdrop,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(LoginLayout.MainPanel.X,
                    LoginLayout.MainPanel.Y), // dst(0,110). spec §11.2a. CODE-CONFIRMED.
                Size = new Vector2(LoginLayout.MainPanel.W,
                    LoginLayout.MainPanel.H), // 1024×490. spec §11.2a. CODE-CONFIRMED.
            };
            AddChild(paintingRect);
        }
        else
        {
            GD.PrintErr("[ServerSelectScreen] loginwindow.dds Slice returned NULL — painting backdrop missing!");
        }

        // =======================================================================
        // [z=3] Parchment PLATE × 2 — the 202×372 channel backing plates.
        // D src(9,6,202,372) NORMAL  / src(220,6,202,372) HOVER+PRESSED.
        // col0 dst(24,97,202,372) action 400  /  col1 dst(257,97,202,372) action 401.
        // spec §11.4 "Parchment row/tab PLATE". CODE-CONFIRMED.
        // =======================================================================
        int[] plateX = { 24, 257 };
        int[] plateActions = { 400, 401 };
        for (int col = 0; col < 2; col++)
        {
            // Using StateButton so the plate responds to hover/pressed states as per spec.
            StateButton plateTog = WidgetFactory.MakeStateButton(
                _assets,
                LoginLayout.AtlasLoginWindow02,
                plateX[col], 97,
                202, 372,
                9, 6, // NORMAL src(9,6). spec §11.4. CODE-CONFIRMED.
                220, 6, // HOVER  src(220,6). spec §11.4. CODE-CONFIRMED.
                220, 6, // PRESSED = HOVER.
                plateActions[col]);
            plateTog.Name = $"ParchPlate{col}";
            plateTog.MouseFilter = MouseFilterEnum.Ignore; // passive chrome; row buttons handle input
            AddChild(plateTog);
        }

        // =======================================================================
        // [z=4] Parchment scroll BODY × 2 — the baked calligraphy art panels.
        // col0: D src(448,6,100,372) → dst(77,97,100,372).
        // col1: D src(572,6,100,372) → dst(310,97,100,372).
        // spec §11.4 "Parchment scroll BODY". CODE-CONFIRMED.
        // =======================================================================
        int[] bodySrcU = { 448, 572 }; // spec §11.4 loop srcU start=448 step+124. CODE-CONFIRMED.
        int[] bodyDstX = { 77, 310 }; // spec §11.4 dst offsets. CODE-CONFIRMED.
        for (int col = 0; col < 2; col++)
        {
            AtlasTexture? bodyTex = _assets.Slice(
                LoginLayout.AtlasLoginWindow02, bodySrcU[col], 6, 100, 372);
            if (bodyTex is not null)
            {
                var bodyRect = new TextureRect
                {
                    Name = $"ParchBody{col}",
                    Texture = bodyTex,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Position = new Vector2(bodyDstX[col], 97),
                    Size = new Vector2(100, 372),
                };
                AddChild(bodyRect);
            }
        }

        // =======================================================================
        // [z=5] Parchment scrollbar thumb (dynamic Y at runtime).
        // D src(700,18,46,168) → dst(0,runtime,46,168). spec §11.4. CODE-CONFIRMED.
        // Static placement for now (Y=97 = top of parchment area).
        // =======================================================================
        AtlasTexture? thumbTex = _assets.Slice(
            LoginLayout.AtlasLoginWindow02, 700, 18, 46, 168);
        if (thumbTex is not null)
        {
            var thumbRect = new TextureRect
            {
                Name = "ParchScrollThumb",
                Texture = thumbTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(0, 97),
                Size = new Vector2(46, 168),
            };
            AddChild(thumbRect);
        }

        // =======================================================================
        // [z=6] Server-row buttons × 10 (the horizontal tab strip above the parchment).
        // B src(596,985,47,18) / hover(643,985). Loop: X=13+47·n, Y=66, W=47, H=18. Actions 115..124.
        // spec §11.4 "Server-row buttons x10 (loop)". CODE-CONFIRMED.
        //
        // Each button displays the selected server's DISPLAY NAME (resolved from ServerId 1..40 via
        // the client-local name table, spec §2.8 / string banks 5001..5040).  In dev-offline mode the
        // ServerEntry.DisplayName field carries the synthetic names.
        //
        // Row index n: action = 115+n; maps to servers[n] if n < servers.Count.
        // =======================================================================
        for (int n = 0; n < 10; n++)
        {
            int rowX = LoginLayout.ServerRowBtnX0 +
                       n * LoginLayout.ServerRowBtnXStep; // 13+47·n. spec §11.4. CODE-CONFIRMED.
            // spec §11.4 X bound check: X < 483 (loop condition). All 10 fit. CODE-CONFIRMED.
            int rowActionId = LoginLayout.ServerRowActionBase + n; // 115+n. spec §11.4. CODE-CONFIRMED.

            StateButton rowBtn = WidgetFactory.MakeStateButton(
                _assets,
                LoginLayout.AtlasLoginWindow,
                rowX, LoginLayout.ServerRowBtnY, // dst X, Y=66. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnW, // W=47. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnH, // H=18. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnNormalSrcX,
                LoginLayout.ServerRowBtnNormalSrcY, // NORMAL src(596,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnHoverSrcX,
                LoginLayout.ServerRowBtnHoverSrcY, // HOVER  src(643,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnHoverSrcX, LoginLayout.ServerRowBtnHoverSrcY, // PRESSED = HOVER.
                rowActionId,
                caption: "", // label drawn via server-list text overlay (see below)
                captionTint: new Color(0.92f, 0.86f, 0.55f)); // parchment-gold text
            rowBtn.Name = $"ServerRowBtn_{n}";

            int capturedN = n; // capture for lambda
            rowBtn.ActionFired += _ => OnRowButtonPressed(capturedN);
            AddChild(rowBtn);
        }

        // =======================================================================
        // [z=7a] List scroll-UP arrow.
        // B src(483,490,13,10) → dst(467,86,13,10). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? upArrow = _assets.Slice(LoginLayout.AtlasLoginWindow, 483, 490, 13, 10);
        if (upArrow is not null)
        {
            var upRect = new TextureRect
            {
                Name = "ScrollUp",
                Texture = upArrow,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(467, 86),
                Size = new Vector2(13, 10),
            };
            AddChild(upRect);
        }

        // =======================================================================
        // [z=7b] List scroll-DOWN arrow.
        // B src(505,490,13,10) → dst(467,455,13,10). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? downArrow = _assets.Slice(LoginLayout.AtlasLoginWindow, 505, 490, 13, 10);
        if (downArrow is not null)
        {
            var downRect = new TextureRect
            {
                Name = "ScrollDown",
                Texture = downArrow,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(467, 455),
                Size = new Vector2(13, 10),
            };
            AddChild(downRect);
        }

        // =======================================================================
        // [z=7c] Scrollbar thumb / commit dot.
        // B src(496,490,9,9) → dst(469,98,9,9). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? commitDot = _assets.Slice(LoginLayout.AtlasLoginWindow, 496, 490, 9, 9);
        if (commitDot is not null)
        {
            var dotRect = new TextureRect
            {
                Name = "ScrollCommitDot",
                Texture = commitDot,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(469, 98),
                Size = new Vector2(9, 9),
            };
            AddChild(dotRect);
        }

        // =======================================================================
        // [z=7d] Refresh button.
        // A src(792,398,111,38) → dst(456,-3,111,38). Action 105.
        // spec §11.4 "Refresh button". CODE-CONFIRMED.
        // Canvas Y: -3 = above the main panel top (panel starts at 0 here, so Y=-3 is slightly above).
        // =======================================================================
        StateButton refreshBtn = WidgetFactory.MakeStateButton(
            _assets,
            LoginLayout.AtlasLoginSlice1,
            456, -3,
            111, 38,
            792, 398, // NORMAL src(792,398). spec §11.4. CODE-CONFIRMED.
            602, 416, // HOVER  src(602,416). spec §11.4. CODE-CONFIRMED.
            602, 416, // PRESSED = HOVER.
            105); // Action 105 = refresh/re-fetch. spec §1.2. CODE-CONFIRMED.
        refreshBtn.Name = "RefreshBtn";
        refreshBtn.ActionFired += _ => OnRefreshPressed();
        AddChild(refreshBtn);

        // =======================================================================
        // [z=7e] Refresh button face plate (baked Korean art).
        // A src(743,398,210,70) → dst(407,-3,210,70). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? refreshFace = _assets.Slice(
            LoginLayout.AtlasLoginSlice1, 743, 398, 210, 70);
        if (refreshFace is not null)
        {
            var facePlate = new TextureRect
            {
                Name = "RefreshFacePlate",
                Texture = refreshFace,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(407, -3),
                Size = new Vector2(210, 70),
            };
            AddChild(facePlate);
        }

        // =======================================================================
        // [z=8] Notice dialog #1 — hidden until needed (msg 4027/4028).
        // C src(318,647,340,190) → dst(342,289,340,190). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        // (Hidden at build time — shown by the tick on sub-state 36 error branches.)
        // We build a placeholder control here; in a live build it would display the msg.xdb caption.
        var noticeDialog = BuildDialogPanel("NoticeDialog1", 342, 289, 340, 190);
        noticeDialog.Visible = false;
        AddChild(noticeDialog);

        // =======================================================================
        // [z=9] Error dialog #2 — hidden until needed (connect-fail / endpoint error).
        // C src(318,647,340,190) → dst(342,289,340,190). spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        var errorDialog = BuildDialogPanel("ErrorDialog2", 342, 289, 340, 190);
        errorDialog.Visible = false;
        AddChild(errorDialog);

        // =======================================================================
        // Server list content overlay — floated over the left parchment body area.
        // The server names, status and load labels are drawn here, inside the parchment body.
        // Bounds: left parchment body dst(77,97) size(226,372) (spans both PLATE + BODY widths).
        // spec §11.4 "Server-row content list" / "List column header labels". CODE-CONFIRMED.
        // =======================================================================
        _serverListContent = new Control
        {
            Name = "ServerListContent",
            // Position inside the left parchment plate area.
            // Plate col0 dst(24,97,202,372). Content sits just inside the top of the plate.
            Position = new Vector2(26, 120),
            Size = new Vector2(460, 340),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_serverListContent);

        // Column headers (msg.xdb ids 4029..4032; §11.4 / §1.9 CODE-CONFIRMED).
        var headerRow = new HBoxContainer
        {
            Name = "ColumnHeaders",
            Position = Vector2.Zero,
            Size = new Vector2(460, 18),
        };
        _serverListContent.AddChild(headerRow);

        void AddHdr(uint msgId, string fallback, int minW)
        {
            Label lbl = WidgetFactory.MakeLabel(
                _assets.Text(msgId, fallback),
                LoginLayout.FontBodyHeight,
                new Color(0.85f, 0.78f, 0.50f));
            lbl.CustomMinimumSize = new Vector2(minW, 18);
            headerRow.AddChild(lbl);
        }

        // Caption ids 4029..4032 — column headers. spec §11.4 / §1.9. CODE-CONFIRMED.
        // NOTE §1.9 CORRECTION: msg 4029 IS the server-list column header (NOT an endpoint-fetch error).
        // The IDA pass confirmed 4029 is the first column header caption. spec §1.9 CODE-CONFIRMED.
        // "PLAUSIBLE" label in §1.9 table was the pre-IDA reading; the IDA pass resolved it as confirmed.
        AddHdr(4029u, "서버명", 180); // Server name column header. spec §11.4 / §1.9. CODE-CONFIRMED.
        AddHdr(4030u, "상태", 70); // Status column header. spec §11.4 / §1.9. CODE-CONFIRMED.
        AddHdr(4031u, "부하", 70); // Load column header. spec §11.4 / §1.9. CODE-CONFIRMED.

        // Row list (populated on SetServers / Servers setter).
        var rowList = new VBoxContainer
        {
            Name = "RowList",
            Position = new Vector2(0, 22),
            Size = new Vector2(460, 316),
        };
        _serverListContent.AddChild(rowList);

        // Populate rows if servers were set before _Ready.
        if (_servers is not null)
            RebuildServerLabels();
        else
        {
            var waitingLbl = WidgetFactory.MakeLabel(
                "Fetching server list…", LoginLayout.FontBodyHeight,
                new Color(0.65f, 0.65f, 0.65f));
            rowList.AddChild(waitingLbl);
        }

        GD.Print("[ServerSelectScreen] Built (§11.4 pixel-faithful). " +
                 "10 row-tabs at Y=66 step+47; 2 parchment plates at Y=97; server list overlaid.");
    }

    // =========================================================================
    // Dialog panel builder (notice / error dialogs, §11.4 / §11.2d)
    // =========================================================================

    private Control BuildDialogPanel(string nodeName, int x, int y, int w, int h)
    {
        // Frame: C src(318,647,340,190) → dst rect. spec §11.4 / §11.2d. CODE-CONFIRMED.
        var panel = new Control
        {
            Name = nodeName,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
        };

        AtlasTexture? frameTex = _assets.Slice(
            LoginLayout.AtlasInventWindow,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY,
            LoginLayout.ModalChromeW, LoginLayout.ModalChromeH);
        if (frameTex is not null)
        {
            var frameRect = new TextureRect
            {
                Name = "DialogFrame",
                Texture = frameTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = Vector2.Zero,
                Size = new Vector2(w, h),
            };
            panel.AddChild(frameRect);
        }
        else
        {
            var fb = new ColorRect
            {
                Color = new Color(0.08f, 0.06f, 0.04f, 0.92f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            fb.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel.AddChild(fb);
        }

        return panel;
    }

    // =========================================================================
    // Server list population
    // =========================================================================

    /// <summary>Sets the server list and rebuilds the row display.</summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        _displayOrder = BuildDisplayOrder(servers);
        if (IsInsideTree()) RebuildServerLabels();
    }

    /// <summary>
    /// Builds the display-order index array from the server list.
    /// When Lastserver is present: clock-seeded shuffle with the remembered server pinned at slot 0.
    /// When absent: plain sequential order.
    /// spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    /// </summary>
    private static List<int> BuildDisplayOrder(IReadOnlyList<ServerEntry> servers)
    {
        int count = servers.Count;
        var order = new List<int>(count);
        for (int i = 0; i < count; i++) order.Add(i);

        int lastServerId = LoadLastServer();
        if (lastServerId <= 0)
        {
            // No Lastserver — plain sequential order. spec §2.7.
            return order;
        }

        // Find the remembered server's index in the server list.
        int pinnedIdx = -1;
        for (int i = 0; i < count; i++)
        {
            if (servers[i].ServerId == lastServerId)
            {
                pinnedIdx = i;
                break;
            }
        }

        if (pinnedIdx < 0)
        {
            // Remembered server not in current list — sequential order.
            return order;
        }

        // Clock-seeded shuffle of the non-pinned entries. spec §2.7 "seeded from the clock". CODE-CONFIRMED.
        var rng = new Random((int)(global::Godot.Time.GetTicksMsec() & 0x7FFFFFFF));
        // Remove the pinned entry from the pool, shuffle the rest, then prepend the pinned entry.
        order.RemoveAt(pinnedIdx);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        order.Insert(0, pinnedIdx); // pin Lastserver first. spec §2.7. CODE-CONFIRMED.
        return order;
    }

    private void RebuildServerLabels()
    {
        if (_serverListContent is null) return;

        // Find the RowList VBoxContainer inside the content panel.
        Node? rowListNode = _serverListContent.FindChild("RowList", owned: false);
        if (rowListNode is not VBoxContainer rowList) return;

        // Clear existing rows.
        foreach (Node child in rowList.GetChildren())
            child.QueueFree();

        if (_servers is null || _servers.Count == 0)
        {
            // spec §1.5 sub-state 36: "server list empty → msg 4027". CODE-CONFIRMED.
            Label emptyLbl = WidgetFactory.MakeLabel(
                _assets.Text(LoginLayout.MsgErrNoServers, "No servers available."),
                LoginLayout.FontBodyHeight,
                new Color(0.80f, 0.40f, 0.40f));
            rowList.AddChild(emptyLbl);
            return;
        }

        // Build one row per server entry using the display order (shuffled with Lastserver pinned first).
        // spec §2.7: "randomized display order, Lastserver anchored first". CODE-CONFIRMED.
        var order = _displayOrder ?? BuildDisplayOrder(_servers);
        for (int slot = 0; slot < order.Count; slot++)
        {
            int srcIdx = order[slot];
            ServerEntry entry = _servers[srcIdx];
            Control row = BuildServerRow(entry, srcIdx, slot);
            rowList.AddChild(row);
        }

        GD.Print($"[ServerSelectScreen] Server list: {_servers.Count} entries populated " +
                 $"(display order: {string.Join(",", order)}).");
    }

    private Control BuildServerRow(ServerEntry entry, int rowIndex, int displaySlot)
    {
        // Each row is an HBox of: server name label | status label | load label.
        // Row height matches the server-row button height (18px) at the spec scale.
        var row = new HBoxContainer
        {
            Name = $"ServerRow_{displaySlot}",
            CustomMinimumSize = new Vector2(450, 22),
        };

        // Server name (DisplayName from client-local name table, spec §2.8. CODE-CONFIRMED).
        // TODO: resolve via string banks 5001..5040 (msg.xdb) when MsgXdbCatalog is wired. spec §2.8.
        bool isActiveRow = (rowIndex == _activeRowIndex);
        Color nameColor = isActiveRow
            ? new Color(1.0f, 0.95f, 0.60f) // active/selected: bright gold
            : new Color(0.85f, 0.78f, 0.50f); // normal: parchment gold

        // "NEW" badge: shown when server_id == NEW_SERVER_INDEX (value 5 from uiconfig.lua).
        // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
        bool isNew = (entry.ServerId == NewServerIndex); // spec §2.7. CODE-CONFIRMED.
        string nameText = entry.DisplayName + (isNew ? " ★" : "");
        Label nameLbl = WidgetFactory.MakeLabel(nameText, LoginLayout.FontBodyHeight, nameColor);
        nameLbl.CustomMinimumSize = new Vector2(180, 22);
        nameLbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(nameLbl);

        // Status (§2.3, CODE-CONFIRMED).
        string statusText = GetStatusText(entry);
        Color statusColor = GetStatusColor(entry);
        Label statusLbl = WidgetFactory.MakeLabel(statusText, LoginLayout.FontBodyHeight, statusColor);
        statusLbl.CustomMinimumSize = new Vector2(70, 22);
        statusLbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(statusLbl);

        // Load / population (§2.3 thresholds, CODE-CONFIRMED; population captions 6001..6005, §11.4).
        (string loadText, Color loadColor) = GetLoadDisplay(entry.Load);

        // Try msg.xdb population caption id (6001..6005). spec §11.4. CODE-CONFIRMED.
        uint popCapId = entry.Load > 1200 ? 6005u :
            entry.Load > 800 ? 6004u :
            entry.Load > 500 ? 6003u :
            entry.Load > 0 ? 6002u : 6001u;
        string popText = _assets.Text(popCapId, loadText);

        Label loadLbl = WidgetFactory.MakeLabel(popText, LoginLayout.FontBodyHeight, loadColor);
        loadLbl.CustomMinimumSize = new Vector2(70, 22);
        loadLbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(loadLbl);

        return row;
    }

    // =========================================================================
    // Status / load presentation helpers (§2.3, §2.4). CODE-CONFIRMED.
    // =========================================================================

    private static string GetStatusText(ServerEntry entry)
    {
        // Status special-value rules per §2.3. CODE-CONFIRMED.
        // IMPORTANT: 24 is a LOAD sentinel under status 3, NOT a top-level status code. spec §2.3.
        return entry.StatusCode switch
        {
            // status 0 → normal/open, falls through to load-color path.
            0 => "Open",

            // status 2/3/4 with open_time==0 → fixed "status as label" branch. spec §2.3. CODE-CONFIRMED.
            (2 or 3 or 4) when entry.OpenTime == 0 && entry.Load == 24
                => "Preparing", // load==24 is a "preparing / under check" sentinel. spec §2.3. CODE-CONFIRMED.
            (2 or 3 or 4) when entry.OpenTime == 0
                => "Scheduled", // fixed status label (no clock available). spec §2.3.

            // status 3 with open_time!=0 → scheduled-open clock. spec §2.4. CODE-CONFIRMED.
            // HH = load/10, load%10 — DIGIT-SPLIT. MM = open_time/10, open_time%10 — DIGIT-SPLIT.
            // NOTE: previous code used OpenTime%60 for MM — WRONG. Corrected to /10,%10 per §2.4.
            3 when entry.OpenTime != 0
                => $"{entry.Load / 10}{entry.Load % 10}:{entry.OpenTime / 10}{entry.OpenTime % 10}",

            // status 100 → auto-connect sentinel ("this is the connected / current selection"). spec §2.3. CODE-CONFIRMED.
            100 => "Connected",

            // Any other positive status → open.
            > 0 => "Open",

            // status 0 or negative → offline.
            _ => "Offline",
        };
    }

    private static Color GetStatusColor(ServerEntry entry)
    {
        // NOTE: 24 is NOT a status code — it is a load sentinel under status 3. spec §2.3. CODE-CONFIRMED.
        // Status 2/3/4 with open_time==0 and status 3+clock → "scheduled / preparing" coloring.
        // Status 0 or positive → open (green). Status 100 → connected (green). Others → brownish.
        return entry.StatusCode switch
        {
            (2 or 3 or 4) when entry.OpenTime == 0 => new Color(0.80f, 0.70f, 0.40f), // scheduled/preparing: gold
            3 when entry.OpenTime != 0 => new Color(0.80f, 0.70f, 0.40f), // scheduled clock: gold
            0 or 100 or > 0 => new Color(0.55f, 0.90f, 0.55f), // open/connected: green
            _ => new Color(0.70f, 0.50f, 0.40f), // offline: brownish
        };
    }

    private static (string text, Color color) GetLoadDisplay(int load)
    {
        // spec: login_flow.md §2.1 load thresholds. CODE-CONFIRMED.
        if (load > 1200) return ("Full", new Color(0.90f, 0.25f, 0.25f)); // red
        if (load > 800) return ("High", new Color(0.95f, 0.60f, 0.15f)); // orange
        if (load > 500) return ("Medium", new Color(0.95f, 0.90f, 0.15f)); // yellow
        return ("Light", new Color(0.50f, 0.90f, 0.50f)); // green
    }

    // =========================================================================
    // Row-tab click handler (action 115+n, spec §11.4 / §2.5. CODE-CONFIRMED)
    // =========================================================================

    private void OnRowButtonPressed(int rowIndex)
    {
        if (_servers is null || rowIndex >= _servers.Count) return;

        ServerEntry selected = _servers[rowIndex];
        _activeRowIndex = rowIndex;

        // Persist Lastserver on selection. spec §2.5. CODE-CONFIRMED.
        // Legacy: written to HKLM registry "Lastserver"; layer-05 equivalent: user:// ConfigFile.
        PersistLastServer(selected.ServerId);

        // Rebuild row labels to reflect the active-row highlight.
        RebuildServerLabels();

        GD.Print($"[ServerSelectScreen] Row tab {rowIndex} pressed → server id={selected.ServerId} " +
                 $"name='{selected.DisplayName}'. Lastserver persisted. Action={LoginLayout.ServerRowActionBase + rowIndex}. " +
                 "spec §11.4 / §2.5. CODE-CONFIRMED.");

        // Emit the intent signal. The BootFlow / use-case layer handles the channel-endpoint
        // fetch (sub-state 37→38). spec §1.5 sub-state 37. CODE-CONFIRMED.
        EmitSignal(SignalName.ServerSelected, selected.ServerId);
    }

    // =========================================================================
    // Refresh handler (action 105, 10-second cooldown per spec §1.2. CODE-CONFIRMED)
    // =========================================================================

    private void OnRefreshPressed()
    {
        // In a live build this re-enters the server-list fetch (sub-state 34).
        // In offline / dev mode this is a no-op.
        // spec §1.2 "Help button (105): throttled ~10s server-list re-fetch path, advance sub-state 34".
        // CODE-CONFIRMED.
        GD.Print("[ServerSelectScreen] Refresh (action 105) pressed — re-fetch sub-state 34. spec §1.2.");
    }
}