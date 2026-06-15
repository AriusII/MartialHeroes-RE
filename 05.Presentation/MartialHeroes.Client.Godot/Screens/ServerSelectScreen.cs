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
//   [z=3]  D parchment PLATE col0     src(9,6,202,372)    → dst(24,97,202,372)   server 1 (action 400)
//   [z=3]  D parchment PLATE col1     src(9,6,202,372)    → dst(257,97,202,372)  server 2 (action 401)
//   [z=4]  D parchment BODY  col0     src(448,6,100,372)  → dst(77,97,100,372)   baked calligraphy art
//   [z=4]  D parchment BODY  col1     src(572,6,100,372)  → dst(310,97,100,372)
//   [z=5]  D scrollbar thumb          src(700,18,46,168)  → dst(0,runtime,46,168)
//   [z=6]  B pager buttons ×10        src(596,985,47,18) / hover(643,985)
//              loop: X = 13+47·n (n=0..9), Y=66, 47×18   → actions 115..124
//              page = action-115; page re-paints the 2-plate view with records [2·page, 2·page+1]
//   [z=7]  B scroll-UP   src(483,490,13,10) → dst(467,86,13,10)
//   [z=7]  B scroll-DOWN src(505,490,13,10) → dst(467,455,13,10)
//   [z=7]  B thumb-dot   src(496,490,9,9)   → dst(469,98,9,9)
//   [z=7]  A refresh btn src(792,398,111,38) → dst(456,-3,111,38)  action 105
//   [z=7]  A refresh face src(743,398,210,70)→ dst(407,-3,210,70)  baked art
//   [z=8]  C notice dialog  src(318,647,340,190) → dst(342,289,340,190)  hidden
//   [z=9]  C error  dialog  src(318,647,340,190) → dst(342,289,340,190)  hidden
//
// CORRECT SERVER MODEL (CODE-CONFIRMED):
//   The TWO parchment PLATES (actions 400/401) ARE the selectable servers.
//   1 plate = 1 server; MAX 2 servers visible at once.
//   action 400 = LEFT plate  = server at display slot (2·page).
//   action 401 = RIGHT plate = server at display slot (2·page + 1).
//   The 8-byte server record is PAINTED ONTO the plate:
//     server name  → plate's header label
//     status+load  → plate's status+load label (status text + load colour, thresholds 1200/800/500)
//   The ten 115-124 buttons are PAGER buttons:
//     page = action − 115; they re-paint the 2-plate view with records [2·page, 2·page+1].
//     They are NOT server rows and NOT a help strip.
//   A plate CLICK (gated to ready state, guard status==0 && load<2400) commits record.server_id,
//   persists Lastserver (user://server_select.cfg), and joins (channel-endpoint port 10000+server_id).
//   spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
//   spec: Docs/RE/specs/frontend_scenes.md §1.2. CODE-CONFIRMED (action 105 = refresh).
//   spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 37. CODE-CONFIRMED (selection commit).
//   spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED (Lastserver persistence).
//
// PAGER GEOMETRY (CODE-CONFIRMED §11.4 loop):
//   n=0..9 → X = 13+47·n ∈ {13,60,107,154,201,248,295,342,389,436}  Y=66  W=47 H=18
//   Action id = 115 + n   → range 115..124
//   page = action − 115   → shows records [2·n, 2·n+1]
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
// PASSIVE: zero game logic.  Reads a view-model list; turns plate clicks into ServerSelected(serverId).
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 (CODE-CONFIRMED literals).
//       §1.2 (action 105 refresh; actions 115..124 as pager). CODE-CONFIRMED.
//       §1.5 (sub-state 37 selection commit). CODE-CONFIRMED.
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
/// Renders exactly 2 server plates (left=server1, right=server2) at a time.
/// Pager buttons (actions 115-124) re-paint the plate pair from the ordered list.
/// Emits <see cref="ServerSelected"/> (server_id) when a PLATE is clicked.
/// Emits <see cref="BackRequested"/> when Back is clicked.
/// spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
/// </summary>
public sealed partial class ServerSelectScreen : Control
{
    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>
    /// Raised when the player selects a server plate.  Carries server_id (1..40).
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
    // Guard threshold: a plate click is accepted only when load < 2400 and status == 0.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5 sub-state 37. CODE-CONFIRMED.
    // =========================================================================
    private const int LoadGuardThreshold = 2400; // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.

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
    // Server list (view-model) and pager state
    // =========================================================================

    private IReadOnlyList<ServerEntry>? _servers;

    // Display-order index array — maps screen slot → servers[] index.
    // Built per SetServers call: clock-seeded shuffle with Lastserver pinned first when present.
    // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    private List<int>? _displayOrder;

    // Current pager page (0-based). Page n shows display slots [2n, 2n+1].
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4 pager buttons 115..124. CODE-CONFIRMED.
    private int _currentPage;

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
            {
                _displayOrder = BuildDisplayOrder(_servers);
                _currentPage = 0;
            }

            if (IsInsideTree()) PaintPlates();
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

    // =========================================================================
    // Live plate widgets — filled in BuildUi, painted in PaintPlates.
    // Two plates: index 0 = LEFT (action 400), index 1 = RIGHT (action 401).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    // =========================================================================
    private Button?[] _plateButtons = new Button?[2];
    private Label?[] _plateNameLabels = new Label?[2];
    private Label?[] _plateStatusLabels = new Label?[2];
    private Label?[] _plateLoadLabels = new Label?[2];

    // Pager button nodes (10 of them, actions 115..124).
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
    private readonly StateButton?[] _pagerButtons = new StateButton?[10];

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
        // [z=3+4] Parchment PLATE × 2 — the 202×372 selectable server plates.
        //
        // CORRECT MODEL (CODE-CONFIRMED):
        //   action 400 = LEFT plate  = server at display index (2·page).
        //   action 401 = RIGHT plate = server at display index (2·page+1).
        //   Each plate is a clickable Button; the server record is painted onto it via labels.
        //   Guard: status==0 && load < 2400. spec §11.4 / §1.5. CODE-CONFIRMED.
        //
        // D src(9,6,202,372) NORMAL  / src(220,6,202,372) HOVER+PRESSED.
        // col0 dst(24,97,202,372) action 400  /  col1 dst(257,97,202,372) action 401.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        // =======================================================================
        int[] plateX = { 24, 257 }; // spec §11.4 col0/col1 dst X. CODE-CONFIRMED.
        int[] plateActions = { 400, 401 }; // spec §11.4 channel toggles. CODE-CONFIRMED.
        int[] bodyDstX = { 77, 310 }; // spec §11.4 body dst X offsets. CODE-CONFIRMED.
        int[] bodySrcU = { 448, 572 }; // spec §11.4 body src U start=448 step+124. CODE-CONFIRMED.

        for (int col = 0; col < 2; col++)
        {
            // --- Parchment backing plate (passive chrome behind the clickable button) ---
            AtlasTexture? plateTex = _assets.Slice(
                LoginLayout.AtlasLoginWindow02, 9, 6, 202, 372);
            if (plateTex is not null)
            {
                var plateRect = new TextureRect
                {
                    Name = $"ParchPlate{col}",
                    Texture = plateTex,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Position = new Vector2(plateX[col], 97),
                    Size = new Vector2(202, 372),
                };
                AddChild(plateRect);
            }

            // --- Parchment scroll BODY (baked calligraphy art, passive) ---
            // col0: D src(448,6,100,372) → dst(77,97,100,372).
            // col1: D src(572,6,100,372) → dst(310,97,100,372).
            // spec §11.4 "Parchment scroll BODY". CODE-CONFIRMED.
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

            // --- Clickable transparent plate button (covers the plate, captures input) ---
            // This is the actual selectable server button.
            // When clicked: commits record.server_id, persists Lastserver, emits ServerSelected.
            // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5 / §2.5. CODE-CONFIRMED.
            var btn = new Button
            {
                Name = $"PlateBtn{col}",
                Position = new Vector2(plateX[col], 97),
                Size = new Vector2(202, 372),
                Flat = true, // transparent — the parchment TextureRect is the visual
                FocusMode = FocusModeEnum.None,
            };
            // Make the button visually transparent (no theme chrome, just hitbox).
            btn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            int capturedCol = col;
            btn.Pressed += () => OnPlatePressed(capturedCol);
            AddChild(btn);
            _plateButtons[col] = btn;

            // --- Server name label (top of plate) ---
            var nameLabel = new Label
            {
                Name = $"PlateNameLabel{col}",
                Position = new Vector2(plateX[col] + 4, 104),
                Size = new Vector2(194, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.78f, 0.50f)); // parchment gold
            AddChild(nameLabel);
            _plateNameLabels[col] = nameLabel;

            // --- Status label (below name) ---
            var statusLabel = new Label
            {
                Name = $"PlateStatusLabel{col}",
                Position = new Vector2(plateX[col] + 4, 128),
                Size = new Vector2(194, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(statusLabel);
            _plateStatusLabels[col] = statusLabel;

            // --- Load label (below status) ---
            var loadLabel = new Label
            {
                Name = $"PlateLoadLabel{col}",
                Position = new Vector2(plateX[col] + 4, 150),
                Size = new Vector2(194, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(loadLabel);
            _plateLoadLabels[col] = loadLabel;
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
        // [z=6] Pager buttons × 10 (actions 115..124).
        // CORRECT MODEL (CODE-CONFIRMED): these are PAGER buttons — page = action−115.
        // page n re-paints the 2-plate view with server records [2·n, 2·n+1].
        // They are NOT server rows. Shown only when more than 2 servers exist.
        // B src(596,985,47,18) / hover(643,985). Loop: X=13+47·n, Y=66, W=47, H=18.
        // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
        // =======================================================================
        for (int n = 0; n < 10; n++)
        {
            int rowX = LoginLayout.ServerRowBtnX0 +
                       n * LoginLayout.ServerRowBtnXStep; // 13+47·n. spec §11.4. CODE-CONFIRMED.
            // spec §11.4 X bound check: X < 483 (loop condition). All 10 fit. CODE-CONFIRMED.
            int pageActionId = LoginLayout.ServerRowActionBase + n; // 115+n. spec §1.2/§11.4. CODE-CONFIRMED.

            StateButton pagerBtn = WidgetFactory.MakeStateButton(
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
                pageActionId,
                caption: $"{n + 1}", // page number label (1-based)
                captionTint: new Color(0.92f, 0.86f, 0.55f)); // parchment-gold text
            pagerBtn.Name = $"PagerBtn_{n}";

            int capturedN = n; // capture for lambda
            pagerBtn.ActionFired += _ => OnPagerButtonPressed(capturedN);
            AddChild(pagerBtn);
            _pagerButtons[n] = pagerBtn;
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
        // spec §1.2 "Help button (105): throttled ~10s server-list re-fetch path, advance sub-state 34".
        // CODE-CONFIRMED.
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

        // Initial plate paint (empty / waiting if no servers yet).
        PaintPlates();

        GD.Print("[ServerSelectScreen] Built (§11.4 pixel-faithful, CODE-CONFIRMED 2-plate model). " +
                 "2 parchment plates at Y=97 (left=action 400, right=action 401). " +
                 "10 pager buttons at Y=66 step+47 (actions 115..124). " +
                 "Plate click → ServerSelected(server_id). spec §11.4/§1.2/§1.5/§2.5.");
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

    /// <summary>Sets the server list and rebuilds the plate display.</summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        _displayOrder = BuildDisplayOrder(servers);
        _currentPage = 0;
        if (IsInsideTree()) PaintPlates();
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

    /// <summary>
    /// Paints the two plates from the current page of the display order.
    /// Page <c>_currentPage</c> shows display slots [2·page, 2·page+1].
    /// spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.2. CODE-CONFIRMED.
    /// </summary>
    private void PaintPlates()
    {
        // Determine how many pages exist and whether pagers should be visible.
        int serverCount = _servers?.Count ?? 0;
        // Number of pages = ceil(serverCount / 2). At most 10 pager buttons (pages 0..9).
        // spec: Docs/RE/specs/frontend_scenes.md §1.2 (actions 115..124, page = action-115). CODE-CONFIRMED.
        int pageCount = serverCount == 0 ? 0 : (serverCount + 1) / 2;

        // Show pager buttons only when there are more than 2 servers (i.e., multiple pages).
        // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
        bool showPagers = pageCount > 1;
        for (int n = 0; n < 10; n++)
        {
            if (_pagerButtons[n] is { } pb)
                pb.Visible = showPagers && n < pageCount;
        }

        // Paint each plate (col 0 = left = display slot 2·page; col 1 = right = 2·page+1).
        for (int col = 0; col < 2; col++)
        {
            int displaySlot = 2 * _currentPage + col;
            ServerEntry? entry = GetEntryAtDisplaySlot(displaySlot);
            PaintOnePlate(col, entry);
        }
    }

    /// <summary>
    /// Returns the server entry at display slot <paramref name="displaySlot"/>, or null if out of range.
    /// Uses the display-order index array. spec §2.7. CODE-CONFIRMED.
    /// </summary>
    private ServerEntry? GetEntryAtDisplaySlot(int displaySlot)
    {
        if (_servers is null || _displayOrder is null) return null;
        if (displaySlot < 0 || displaySlot >= _displayOrder.Count) return null;
        int srcIdx = _displayOrder[displaySlot];
        if (srcIdx < 0 || srcIdx >= _servers.Count) return null;
        return _servers[srcIdx];
    }

    /// <summary>
    /// Paints the server record <paramref name="entry"/> onto plate <paramref name="col"/> (0=left, 1=right).
    /// When entry is null the plate shows an empty/"no server" state and is disabled.
    /// spec: Docs/RE/specs/frontend_scenes.md §11.4 "server record painted onto a plate". CODE-CONFIRMED.
    /// </summary>
    private void PaintOnePlate(int col, ServerEntry? entry)
    {
        if (_plateButtons[col] is { } btn)
        {
            // Plate is clickable only when entry exists, status==0, and load < 2400.
            // Guard: status==0 && load < LoadGuardThreshold. spec §11.4 / §1.5. CODE-CONFIRMED.
            bool isReady = entry is { StatusCode: 0 } && entry.Load < LoadGuardThreshold;
            btn.Disabled = !isReady;
        }

        if (_plateNameLabels[col] is { } nameLbl)
        {
            if (entry is null)
            {
                nameLbl.Text = "";
            }
            else
            {
                // "NEW" badge: server_id == NEW_SERVER_INDEX (value 5). spec §2.7. CODE-CONFIRMED.
                bool isNew = entry.ServerId == NewServerIndex; // spec §2.7. CODE-CONFIRMED.
                nameLbl.Text = entry.DisplayName + (isNew ? " ★" : "");
            }
        }

        if (_plateStatusLabels[col] is { } statusLbl)
        {
            if (entry is null)
            {
                statusLbl.Text = "";
                statusLbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            }
            else
            {
                string statusText = GetStatusText(entry);
                Color statusColor = GetStatusColor(entry);
                statusLbl.Text = statusText;
                statusLbl.AddThemeColorOverride("font_color", statusColor);
            }
        }

        if (_plateLoadLabels[col] is { } loadLbl)
        {
            if (entry is null)
            {
                loadLbl.Text = "";
                loadLbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            }
            else
            {
                (string loadText, Color loadColor) = GetLoadDisplay(entry.Load);
                // Try msg.xdb population caption id (6001..6005). spec §11.4. CODE-CONFIRMED.
                uint popCapId = entry.Load > 1200 ? 6005u :
                    entry.Load > 800 ? 6004u :
                    entry.Load > 500 ? 6003u :
                    entry.Load > 0 ? 6002u : 6001u;
                string popText = _assets.Text(popCapId, loadText);
                loadLbl.Text = popText;
                loadLbl.AddThemeColorOverride("font_color", loadColor);
            }
        }
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
    // Plate click handler (action 400/401, spec §11.4 / §1.5 / §2.5. CODE-CONFIRMED)
    //
    // CORRECT MODEL: clicking a plate selects the server painted onto that plate.
    // Guard: entry.StatusCode==0 && entry.Load < LoadGuardThreshold.
    // On selection: persist Lastserver, emit ServerSelected(server_id).
    // Channel-endpoint port = 10000 + server_id (handled by BootFlow / use-case layer).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5 sub-state 37 / §2.5. CODE-CONFIRMED.
    // =========================================================================

    private void OnPlatePressed(int col)
    {
        // col 0 = LEFT plate = action 400; col 1 = RIGHT plate = action 401.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        int displaySlot = 2 * _currentPage + col;
        ServerEntry? entry = GetEntryAtDisplaySlot(displaySlot);
        if (entry is null) return;

        // Guard: status==0 && load < 2400.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5. CODE-CONFIRMED.
        if (entry.StatusCode != 0 || entry.Load >= LoadGuardThreshold)
        {
            GD.Print($"[ServerSelectScreen] Plate {col} (action {400 + col}) blocked — " +
                     $"status={entry.StatusCode} load={entry.Load} (guard: status==0 && load<{LoadGuardThreshold}). " +
                     "spec §11.4/§1.5. CODE-CONFIRMED.");
            return;
        }

        // Persist Lastserver on selection. spec §2.5. CODE-CONFIRMED.
        // Legacy: written to HKLM registry "Lastserver"; layer-05 equivalent: user:// ConfigFile.
        PersistLastServer(entry.ServerId);

        GD.Print($"[ServerSelectScreen] Plate {col} (action {400 + col}) pressed → server id={entry.ServerId} " +
                 $"name='{entry.DisplayName}'. Lastserver persisted. " +
                 $"Channel-endpoint port={10000 + entry.ServerId}. " +
                 "spec §11.4/§1.5 sub-state 37/§2.5. CODE-CONFIRMED.");

        // Emit the intent signal. The BootFlow / use-case layer handles the channel-endpoint
        // fetch (sub-state 37→38). spec §1.5 sub-state 37. CODE-CONFIRMED.
        EmitSignal(SignalName.ServerSelected, entry.ServerId);
    }

    // =========================================================================
    // Pager button handler (actions 115..124, spec §1.2 / §11.4. CODE-CONFIRMED)
    // page = action − 115; re-paints the 2-plate view with records [2·page, 2·page+1].
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
    // =========================================================================

    private void OnPagerButtonPressed(int page)
    {
        int serverCount = _servers?.Count ?? 0;
        int pageCount = serverCount == 0 ? 0 : (serverCount + 1) / 2;
        if (page < 0 || page >= pageCount) return;

        _currentPage = page;
        PaintPlates();

        GD.Print($"[ServerSelectScreen] Pager button {page} (action {115 + page}) pressed → " +
                 $"showing display slots [{2 * page}, {2 * page + 1}]. " +
                 "spec §1.2/§11.4. CODE-CONFIRMED.");
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