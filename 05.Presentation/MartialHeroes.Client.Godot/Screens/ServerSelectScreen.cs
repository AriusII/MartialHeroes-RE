// Screens/ServerSelectScreen.cs — FROM-SCRATCH rewrite, WAVE 3.
//
// Server-selection overlay. Every visual element sources from a real VFS atlas sub-rect.
// No solid-colour fallbacks, no invented servers, no invented English text.
// Missing atlas → GD.PrintErr + skip (no crash). No data → empty list.
//
// DISPLAY MODEL — spec: Docs/RE/specs/frontend_scenes.md §11.4 (CODE-CONFIRMED)
//
//   The two parchment PLATES (actions 400/401) ARE the selectable servers.
//   1 plate = 1 server; max 2 per page.
//   action 400 = LEFT plate  = server at display slot 2·page.
//   action 401 = RIGHT plate = server at display slot 2·page+1.
//   The 8-byte record is painted onto a plate:
//     server name → plate header label (msg bank 5001..5040)
//     status/load → plate status label (colour thresholds §2.3)
//   The ten 115..124 widgets are PAGER buttons (page = action−115).
//   They re-paint the 2-plate view; they are NOT server rows.
//   spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.2 / §2.5. CODE-CONFIRMED.
//
// ATLASES (§11.1, CODE-CONFIRMED):
//   A = data/ui/login_slice1.dds  1024×1024 DXT2 (premultiplied alpha)
//   B = data/ui/loginwindow.dds   1024×1024 DXT5
//   C = data/ui/inventwindow.dds  1024×1024 DXT3
//   D = data/ui/loginwindow_02.dds 1024×1024 DXT2 (premultiplied alpha)
//
// LAYER ORDER (§11.4, CODE-CONFIRMED):
//   z=1  A full-background panel     src(0,0,1024,398)   → dst(0,0,1024,398)
//   z=2  A bottom-bar band           src(0,582,1024,442) → dst(0,326,1024,442)
//   z=3  B ink-wash painting         src(0,0,1024,490)   → dst(0,110,1024,490)  (§11.2a)
//   z=4  D parchment PLATE col0      src(9,6,202,372)    → dst(24,97,202,372)   action 400
//   z=4  D parchment PLATE col1      src(9,6,202,372)    → dst(257,97,202,372)  action 401
//   z=5  D parchment BODY col0       src(448,6,100,372)  → dst(77,97,100,372)
//   z=5  D parchment BODY col1       src(572,6,100,372)  → dst(310,97,100,372)
//   z=6  D scrollbar thumb           src(700,18,46,168)  → dst(0,runtime,46,168)
//   z=7  B pager buttons ×10        N src(596,985,47,18) H(643,985) dst X=13+47·n Y=66 actions 115..124
//   z=8  B scroll-UP                 src(483,490,13,10)  → dst(467,86,13,10)
//   z=8  B scroll-DOWN               src(505,490,13,10)  → dst(467,455,13,10)
//   z=8  B thumb-dot                 src(496,490,9,9)    → dst(469,98,9,9)
//   z=9  A refresh button            N src(792,398,111,38) H(602,416) → dst(456,-3,111,38) action 105
//   z=9  A refresh face plate        src(743,398,210,70) → dst(407,-3,210,70)
//   z=10 C notice dialog #1  (hidden) src(318,647,340,190) → dst(342,289,340,190)
//   z=11 C error  dialog #2  (hidden) src(318,647,340,190) → dst(342,289,340,190)
//
// STATUS / LOAD COLOURS (§2.3, CODE-CONFIRMED):
//   load > 1200 → red    (msg 6001)
//   load >  800 → orange (msg 6002)
//   load >  500 → yellow (msg 6003)
//   load ≤  500 → green  (no special msg)
//   6004 = "maintenance"; 6005 = load read-out "%4d / %4d"
//   Strict greater-than comparisons, evaluated top-down. spec §2.3. CODE-CONFIRMED.
//
// PLATE CLICK GUARD (§11.4/§1.5 sub-state 37, CODE-CONFIRMED):
//   Accepted only when status_code==0 AND load < 2400.
//
// NEW_SERVER_INDEX (§2.7, CODE-CONFIRMED):
//   uiconfig.lua global = 5. The server whose server_id == 5 gets a "NEW" badge.
//
// RANDOMIZED DISPLAY ORDER (§2.7, CODE-CONFIRMED):
//   Clock-seeded Fisher-Yates shuffle; Lastserver pinned at slot 0 when present.
//
// PASSIVE: zero game logic. View state only.

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// View-model for one 8-byte lobby server record.
/// spec: Docs/RE/specs/login_flow.md §2.1 / Docs/RE/specs/frontend_scenes.md §2.2. CODE-CONFIRMED.
/// </summary>
public sealed record ServerEntry(
    /// <summary>Index 1..40 into the client-local name table. spec §2.8.</summary>
    int ServerId,
    /// <summary>Client-local display name (resolved from msg banks 5001..5040). spec §2.8.</summary>
    string DisplayName,
    /// <summary>
    /// Availability sentinel. spec §2.3 special values:
    ///   0 = normal/open; 2/3/4 = status-label variants; 3+open_time!=0 = HH:MM clock;
    ///   100 = auto-connect sentinel.
    /// </summary>
    int StatusCode,
    /// <summary>
    /// Population gauge / scheduled-open hours (when status_code==3).
    /// Colour thresholds: 1200/800/500. spec §2.3. CODE-CONFIRMED.
    /// When status_code==3 and open_time!=0: HH = load/10, load%10. spec §2.4. CODE-CONFIRMED.
    /// </summary>
    int Load,
    /// <summary>
    /// Scheduled-open minutes (when status_code==3, open_time!=0).
    /// MM = open_time/10, open_time%10. spec §2.4. CODE-CONFIRMED.
    /// </summary>
    int OpenTime);

/// <summary>
/// Server-selection overlay — pixel-faithful to §11.4.
/// Renders exactly two server plates at a time. Pager buttons (115..124) re-paint the pair.
/// Emits <see cref="ServerSelected"/> (server_id) when a plate is clicked.
/// Emits <see cref="BackRequested"/> when Back is activated.
/// spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
/// </summary>
public sealed partial class ServerSelectScreen : Control
{
    // =========================================================================
    // Public signals — consumed by BootFlow.cs. Must not be renamed.
    // =========================================================================

    /// <summary>Raised when a server plate is clicked. Carries server_id (1..40).</summary>
    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    /// <summary>Raised when Back is activated.</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // =========================================================================
    // Constants
    // =========================================================================

    // NEW_SERVER_INDEX: the server_id that gets the "NEW" badge.
    // uiconfig.lua global, value 5 in the sampled client.
    // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    private const int NewServerIndex = 5; // spec: frontend_scenes.md §2.7. CODE-CONFIRMED.

    // Plate click guard: accepted only when status_code==0 AND load < 2400.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5 sub-state 37. CODE-CONFIRMED.
    private const int LoadGuardThreshold = 2400; // spec: frontend_scenes.md §11.4. CODE-CONFIRMED.

    // Lastserver persistence (layer-05 only — Godot ConfigFile equivalent of Win32 registry value).
    // spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED.
    private const string LastServerCfgPath = "user://server_select.cfg";
    private const string LastServerCfgSection = "server_select";
    private const string LastServerCfgKey = "Lastserver";

    // =========================================================================
    // View state
    // =========================================================================

    private IReadOnlyList<ServerEntry>? _servers;

    // Display-order index array: maps screen slot → _servers[] index.
    // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
    private List<int>? _displayOrder;

    // Current pager page (0-based). Page n shows display slots [2n, 2n+1].
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
    private int _currentPage;

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    // Plate widgets (index 0 = LEFT action 400, index 1 = RIGHT action 401).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    private readonly Button?[] _plateButtons = new Button?[2];
    private readonly Label?[] _plateNameLabels = new Label?[2];
    private readonly Label?[] _plateStatusLabels = new Label?[2];
    private readonly Label?[] _plateLoadLabels = new Label?[2];

    // Pager buttons (10 of them, actions 115..124).
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
    private readonly TextureButton?[] _pagerButtons = new TextureButton?[10];

    // =========================================================================
    // Injection point — consumed by BootFlow.cs. Must not be renamed.
    // =========================================================================

    /// <summary>Inject a shared UiAssetLoader.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    /// <summary>Sets the server list and triggers a plate repaint.</summary>
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
            GD.PrintErr($"[ServerSelectScreen] _Ready: BuildUi threw — {ex.Message}");
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

        // -----------------------------------------------------------------------
        // z=1: Full background art panel.
        // A src(0,0,1024,398) → dst(0,0,1024,398).
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("BgArt", LoginLayout.AtlasLoginSlice1, 0, 0, 1024, 398, 0, 0, 1024, 398,
            "A src(0,0,1024,398) § 11.4");

        // -----------------------------------------------------------------------
        // z=2: Bottom-bar band.
        // A src(0,582,1024,442) → dst(0,326,1024,442). Y=326=326×768/768.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("BottomBar", LoginLayout.AtlasLoginSlice1, 0, 582, 1024, 442,
            0, LoginLayout.BottomBarCanvasY, 1024, 442,
            "A src(0,582,1024,442) §11.4");

        // -----------------------------------------------------------------------
        // z=3: Ink-wash painting backdrop (loginwindow.dds).
        // B src(0,0,1024,490) → dst(0,110,1024,490).
        // Sits behind the parchment plates. spec §11.2a "Main panel art". CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("PaintingBackdrop", LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY, // src(0,0). CODE-CONFIRMED.
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H, // 1024×490. CODE-CONFIRMED.
            LoginLayout.MainPanel.X, LoginLayout.MainPanel.Y, // dst(0,110). CODE-CONFIRMED.
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H,
            "B src(0,0,1024,490) §11.2a/§11.4");

        // -----------------------------------------------------------------------
        // z=4+5: Two parchment PLATES and BODY columns.
        // PLATE: D src(9,6,202,372) NORMAL / src(220,6) HOVER+PRESSED.
        //   col0 dst(24,97,202,372) action 400.
        //   col1 dst(257,97,202,372) action 401.
        // BODY:  col0 D src(448,6,100,372) → dst(77,97,100,372).
        //        col1 D src(572,6,100,372) → dst(310,97,100,372).
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        int[] plateDstX = { 24, 257 }; // spec §11.4. CODE-CONFIRMED.
        int[] bodyDstX = { 77, 310 }; // spec §11.4. CODE-CONFIRMED.
        int[] bodySrcU = { 448, 572 }; // spec §11.4 src U start=448 step+124. CODE-CONFIRMED.

        for (int col = 0; col < 2; col++)
        {
            // Parchment PLATE background (passive chrome, under the clickable button).
            // Normal state: D src(9,6,202,372). spec §11.4. CODE-CONFIRMED.
            AddImageSlice($"ParchPlate{col}", LoginLayout.AtlasLoginWindow02,
                9, 6, 202, 372,
                plateDstX[col], 97, 202, 372,
                $"D src(9,6,202,372) col{col} §11.4");

            // Parchment BODY (baked calligraphy art, passive).
            // col0: D src(448,6,100,372) → dst(77,97). col1: D src(572,6,100,372) → dst(310,97).
            // spec §11.4. CODE-CONFIRMED.
            AddImageSlice($"ParchBody{col}", LoginLayout.AtlasLoginWindow02,
                bodySrcU[col], 6, 100, 372,
                bodyDstX[col], 97, 100, 372,
                $"D src({bodySrcU[col]},6,100,372) col{col} §11.4");

            // Transparent clickable button covering the plate — captures input.
            // On click: commits server, persists Lastserver, emits ServerSelected.
            // spec §11.4 / §1.5 / §2.5. CODE-CONFIRMED.
            var btn = new Button
            {
                Name = $"PlateBtn{col}",
                Position = new Vector2(plateDstX[col], 97),
                Size = new Vector2(202, 372),
                Flat = true,
                FocusMode = FocusModeEnum.None,
            };
            // Invisible style so the parchment TextureRect underneath is the only visual.
            btn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            int capturedCol = col;
            btn.Pressed += () => OnPlatePressed(capturedCol);
            AddChild(btn);
            _plateButtons[col] = btn;

            // Server name label — centred on the parchment plate.
            // Painted by PaintOnePlate from the server record.
            // spec §11.4 "name → plate header label". CODE-CONFIRMED.
            var nameLbl = new Label
            {
                Name = $"PlateNameLbl{col}",
                Position = new Vector2(plateDstX[col] + 4, 200),
                Size = new Vector2(194, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                ClipText = true,
            };
            nameLbl.AddThemeFontSizeOverride("font_size", 13);
            AddChild(nameLbl);
            _plateNameLabels[col] = nameLbl;

            // Status label (below name).
            // spec §11.4 "status/load → plate status+load label". CODE-CONFIRMED.
            var statusLbl = new Label
            {
                Name = $"PlateStatusLbl{col}",
                Position = new Vector2(plateDstX[col] + 4, 228),
                Size = new Vector2(194, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            statusLbl.AddThemeFontSizeOverride("font_size", 11);
            AddChild(statusLbl);
            _plateStatusLabels[col] = statusLbl;

            // Load label (below status).
            // spec §11.4 "population captions 6001..6005". CODE-CONFIRMED.
            var loadLbl = new Label
            {
                Name = $"PlateLoadLbl{col}",
                Position = new Vector2(plateDstX[col] + 4, 250),
                Size = new Vector2(194, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            loadLbl.AddThemeFontSizeOverride("font_size", 11);
            AddChild(loadLbl);
            _plateLoadLabels[col] = loadLbl;
        }

        // -----------------------------------------------------------------------
        // z=6: Parchment scrollbar thumb (static initial placement at Y=97).
        // D src(700,18,46,168) → dst(0,runtime,46,168).
        // spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("ParchThumb", LoginLayout.AtlasLoginWindow02, 700, 18, 46, 168,
            0, 97, 46, 168, "D src(700,18,46,168) §11.4");

        // -----------------------------------------------------------------------
        // z=7: Pager buttons × 10 (actions 115..124).
        // B src(596,985,47,18) NORMAL / src(643,985) HOVER+PRESSED.
        // Loop: X=13+47·n (n=0..9), Y=66, W=47, H=18. Actions 115..124.
        // These are PAGER buttons — NOT server rows.
        // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        for (int n = 0; n < 10; n++)
        {
            int pagerX = LoginLayout.ServerRowBtnX0 + n * LoginLayout.ServerRowBtnXStep;
            // spec §11.4 loop: X=13+47·n while X<483 → exactly 10 iterations. CODE-CONFIRMED.

            AtlasTexture? pagerNormal = _assets.Slice(
                LoginLayout.AtlasLoginWindow,
                LoginLayout.ServerRowBtnNormalSrcX, LoginLayout.ServerRowBtnNormalSrcY, // src(596,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnW, LoginLayout.ServerRowBtnH); // 47×18. CODE-CONFIRMED.
            AtlasTexture? pagerHover = _assets.Slice(
                LoginLayout.AtlasLoginWindow,
                LoginLayout.ServerRowBtnHoverSrcX, LoginLayout.ServerRowBtnHoverSrcY, // src(643,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnW, LoginLayout.ServerRowBtnH);

            if (pagerNormal is null && n == 0)
                GD.PrintErr("[ServerSelectScreen] loginwindow.dds pager slice returned null — " +
                            "pager buttons absent (VFS offline). spec: frontend_scenes.md §11.4. CODE-CONFIRMED path.");

            if (pagerNormal is null)
            {
                _pagerButtons[n] = null;
                continue; // skip — no solid-colour fallback
            }

            var pb = new TextureButton
            {
                Name = $"PagerBtn_{n}",
                Position = new Vector2(pagerX, LoginLayout.ServerRowBtnY),
                CustomMinimumSize = new Vector2(LoginLayout.ServerRowBtnW, LoginLayout.ServerRowBtnH),
                TextureNormal = pagerNormal,
                Visible = false, // PaintPlates shows them only when multiple pages exist
            };
            if (pagerHover is not null)
            {
                pb.TextureHover = pagerHover;
                pb.TexturePressed = pagerHover; // PRESSED = HOVER per spec §11.4. CODE-CONFIRMED.
            }

            int capturedN = n;
            pb.Pressed += () => OnPagerPressed(capturedN);
            AddChild(pb);
            _pagerButtons[n] = pb;
        }

        // -----------------------------------------------------------------------
        // z=8a: List scroll-UP arrow.
        // B src(483,490,13,10) → dst(467,86,13,10). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("ScrollUp", LoginLayout.AtlasLoginWindow, 483, 490, 13, 10,
            467, 86, 13, 10, "B src(483,490,13,10) §11.4");

        // -----------------------------------------------------------------------
        // z=8b: List scroll-DOWN arrow.
        // B src(505,490,13,10) → dst(467,455,13,10). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("ScrollDown", LoginLayout.AtlasLoginWindow, 505, 490, 13, 10,
            467, 455, 13, 10, "B src(505,490,13,10) §11.4");

        // -----------------------------------------------------------------------
        // z=8c: Scrollbar thumb / commit dot.
        // B src(496,490,9,9) → dst(469,98,9,9). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("ScrollDot", LoginLayout.AtlasLoginWindow, 496, 490, 9, 9,
            469, 98, 9, 9, "B src(496,490,9,9) §11.4");

        // -----------------------------------------------------------------------
        // z=9a: Refresh button.
        // A N src(792,398,111,38) H/P src(602,416) → dst(456,-3,111,38). Action 105.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.2. CODE-CONFIRMED.
        // spec §1.2 "Help button (105): throttled ~10s server-list re-fetch path". CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AtlasTexture? refreshN = _assets.Slice(LoginLayout.AtlasLoginSlice1, 792, 398, 111, 38);
        AtlasTexture? refreshH = _assets.Slice(LoginLayout.AtlasLoginSlice1, 602, 416, 111, 38);

        if (refreshN is not null)
        {
            var refreshBtn = new TextureButton
            {
                Name = "RefreshBtn",
                Position = new Vector2(456, -3),
                CustomMinimumSize = new Vector2(111, 38),
                TextureNormal = refreshN,
            };
            if (refreshH is not null)
            {
                refreshBtn.TextureHover = refreshH;
                refreshBtn.TexturePressed = refreshH;
            }

            refreshBtn.Pressed += OnRefreshPressed;
            AddChild(refreshBtn);
        }
        else
        {
            GD.PrintErr("[ServerSelectScreen] login_slice1.dds refresh-button slice returned null " +
                        "— refresh button absent. spec: frontend_scenes.md §11.4. CODE-CONFIRMED path.");
        }

        // -----------------------------------------------------------------------
        // z=9b: Refresh button face plate (baked calligraphy art, passive).
        // A src(743,398,210,70) → dst(407,-3,210,70). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        AddImageSlice("RefreshFace", LoginLayout.AtlasLoginSlice1, 743, 398, 210, 70,
            407, -3, 210, 70, "A src(743,398,210,70) §11.4");

        // -----------------------------------------------------------------------
        // z=10: Notice dialog #1 (hidden at build time).
        // C src(318,647,340,190) → dst(342,289,340,190). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        var noticeDialog = BuildDialogPanel("NoticeDialog1", 342, 289, 340, 190);
        noticeDialog.Visible = false;
        AddChild(noticeDialog);

        // -----------------------------------------------------------------------
        // z=11: Error dialog #2 (hidden at build time).
        // C src(318,647,340,190) → dst(342,289,340,190). spec §11.4. CODE-CONFIRMED.
        // -----------------------------------------------------------------------
        var errorDialog = BuildDialogPanel("ErrorDialog2", 342, 289, 340, 190);
        errorDialog.Visible = false;
        AddChild(errorDialog);

        // Initial plate paint — empty if no servers yet.
        PaintPlates();

        GD.Print("[ServerSelectScreen] Built. 2 parchment plates (actions 400/401) + " +
                 "10 pager buttons (actions 115..124). Server list EMPTY until SetServers called. " +
                 "spec: frontend_scenes.md §11.4. CODE-CONFIRMED.");
    }

    // =========================================================================
    // Helper: add a passive TextureRect slice. Logs + skips if atlas missing.
    // =========================================================================

    private void AddImageSlice(string nodeName,
        string atlas, int srcX, int srcY, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH,
        string specNote)
    {
        AtlasTexture? tex = _assets.Slice(atlas, srcX, srcY, srcW, srcH);
        if (tex is null)
        {
            GD.PrintErr($"[ServerSelectScreen] Slice null: {nodeName} ({specNote}) — absent (VFS offline).");
            return;
        }

        var rect = new TextureRect
        {
            Name = nodeName,
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(dstX, dstY),
            Size = new Vector2(dstW, dstH),
        };
        AddChild(rect);
    }

    // =========================================================================
    // Dialog panel builder (notice/error, §11.4 / §11.2d). CODE-CONFIRMED.
    // =========================================================================

    private Control BuildDialogPanel(string nodeName, int x, int y, int w, int h)
    {
        // Frame: C src(318,647,340,190) stretched to the dst rect.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        var panel = new Control
        {
            Name = nodeName,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
        };

        AtlasTexture? frameTex = _assets.Slice(
            LoginLayout.AtlasInventWindow,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY, // src(318,647). CODE-CONFIRMED.
            LoginLayout.ModalChromeW, LoginLayout.ModalChromeH); // 340×190. CODE-CONFIRMED.

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
            GD.PrintErr($"[ServerSelectScreen] {nodeName}: InventWindow.dds dialog slice null — " +
                        "dialog frame absent. spec: frontend_scenes.md §11.4 / §11.2d. CODE-CONFIRMED path.");
        }

        return panel;
    }

    // =========================================================================
    // Public: set the server list (called by BootFlow).
    // =========================================================================

    /// <summary>Sets the server list and rebuilds the plate display. Empty list = no rows.</summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        _displayOrder = BuildDisplayOrder(servers);
        _currentPage = 0;
        if (IsInsideTree()) PaintPlates();
    }

    // =========================================================================
    // Display-order construction (§2.7). CODE-CONFIRMED.
    // Clock-seeded Fisher-Yates shuffle; Lastserver pinned at slot 0 when present.
    // =========================================================================

    private static List<int> BuildDisplayOrder(IReadOnlyList<ServerEntry> servers)
    {
        int count = servers.Count;
        var order = new List<int>(count);
        for (int i = 0; i < count; i++) order.Add(i);

        int lastId = LoadLastServer();
        if (lastId <= 0) return order; // no Lastserver — plain sequential. spec §2.7.

        int pinnedIdx = -1;
        for (int i = 0; i < count; i++)
        {
            if (servers[i].ServerId == lastId)
            {
                pinnedIdx = i;
                break;
            }
        }

        if (pinnedIdx < 0) return order; // not found — sequential.

        // Clock-seeded shuffle of non-pinned entries. spec §2.7. CODE-CONFIRMED.
        var rng = new Random((int)(global::Godot.Time.GetTicksMsec() & 0x7FFF_FFFF));
        order.RemoveAt(pinnedIdx);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        order.Insert(0, pinnedIdx); // pin at slot 0. spec §2.7. CODE-CONFIRMED.
        return order;
    }

    // =========================================================================
    // Plate painter (§11.4 / §2.3 / §2.4). CODE-CONFIRMED.
    // =========================================================================

    private void PaintPlates()
    {
        int serverCount = _servers?.Count ?? 0;
        // Page count = ceil(serverCount / 2). Max 10 pages (10 pager buttons). spec §11.4.
        int pageCount = serverCount == 0 ? 0 : (serverCount + 1) / 2;

        // Show pager buttons only when there is more than one page.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
        bool showPagers = pageCount > 1;
        for (int n = 0; n < 10; n++)
        {
            if (_pagerButtons[n] is { } pb)
                pb.Visible = showPagers && n < pageCount;
        }

        // Paint each plate: col 0 = display slot 2·page; col 1 = display slot 2·page+1.
        for (int col = 0; col < 2; col++)
        {
            ServerEntry? entry = GetEntryAtDisplaySlot(2 * _currentPage + col);
            PaintOnePlate(col, entry);
        }
    }

    private ServerEntry? GetEntryAtDisplaySlot(int displaySlot)
    {
        if (_servers is null || _displayOrder is null) return null;
        if (displaySlot < 0 || displaySlot >= _displayOrder.Count) return null;
        int srcIdx = _displayOrder[displaySlot];
        if (srcIdx < 0 || srcIdx >= _servers.Count) return null;
        return _servers[srcIdx];
    }

    private void PaintOnePlate(int col, ServerEntry? entry)
    {
        // Plate is clickable only when entry exists, status==0, and load < 2400.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5. CODE-CONFIRMED.
        bool isReady = entry is { StatusCode: 0 } && entry.Load < LoadGuardThreshold;
        if (_plateButtons[col] is { } btn) btn.Disabled = !isReady;

        // Name label.
        // spec §11.4 "name → plate header label". CODE-CONFIRMED.
        if (_plateNameLabels[col] is { } nameLbl)
        {
            if (entry is null)
            {
                nameLbl.Text = "";
            }
            else
            {
                // NEW badge: server_id == NEW_SERVER_INDEX (5). spec §2.7. CODE-CONFIRMED.
                // Rendered as a small suffix — the original draws a separate badge widget;
                // here a text marker is the closest passive equivalent.
                bool isNew = entry.ServerId == NewServerIndex;
                nameLbl.Text = isNew ? entry.DisplayName + " N" : entry.DisplayName;
                nameLbl.AddThemeColorOverride("font_color", new Color(0.92f, 0.82f, 0.50f));
            }
        }

        // Status label — sourced from msg.xdb ids via _assets.Text when available.
        // spec §2.3. CODE-CONFIRMED colour/text rules.
        if (_plateStatusLabels[col] is { } statusLbl)
        {
            if (entry is null)
            {
                statusLbl.Text = "";
            }
            else
            {
                (string text, Color color) = GetStatusPresentation(entry);
                statusLbl.Text = text;
                statusLbl.AddThemeColorOverride("font_color", color);
            }
        }

        // Load label — colour thresholds per §2.3. CODE-CONFIRMED.
        // Load > 1200 → red (msg 6001); > 800 → orange (msg 6002); > 500 → yellow (msg 6003);
        // ≤ 500 → green (no special msg). spec §2.3. CODE-CONFIRMED.
        if (_plateLoadLabels[col] is { } loadLbl)
        {
            if (entry is null)
            {
                loadLbl.Text = "";
            }
            else
            {
                (Color loadColor, uint msgId) = GetLoadColor(entry.Load);
                // Attempt to resolve the caption from the msg.xdb catalogue.
                // When catalogue is absent, Text() returns the empty string → label stays blank.
                // spec §2.3 "population captions 6001..6005". CODE-CONFIRMED.
                string loadText = msgId > 0 ? _assets.Text(msgId, "") : "";
                loadLbl.Text = loadText;
                loadLbl.AddThemeColorOverride("font_color", loadColor);
            }
        }
    }

    // =========================================================================
    // Status / load presentation helpers (§2.3, §2.4). CODE-CONFIRMED.
    // =========================================================================

    private (string text, Color color) GetStatusPresentation(ServerEntry e)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §2.3. CODE-CONFIRMED status rules.
        // NOTE: 24 is a LOAD sentinel under status_code 3, NOT a top-level status code.
        switch (e.StatusCode)
        {
            case 0:
                // Normal/open → falls through to load-colour path. spec §2.3. CODE-CONFIRMED.
                // Status text from msg.xdb; empty when catalogue absent.
                return ("", new Color(0.55f, 0.90f, 0.55f)); // green

            case 2 or 3 or 4 when e.OpenTime == 0 && e.Load == 24:
                // load==24 under status 3 = "preparing / under check". spec §2.3. CODE-CONFIRMED.
                // Caption id 6004 = "maintenance". spec §2.3. CODE-CONFIRMED.
                return (_assets.Text(6004u, ""), new Color(0.80f, 0.70f, 0.40f));

            case 2 or 3 or 4 when e.OpenTime == 0:
                // Fixed status-as-label branch (no clock). spec §2.3. CODE-CONFIRMED.
                return (_assets.Text(6004u, ""), new Color(0.80f, 0.70f, 0.40f));

            case 3 when e.OpenTime != 0:
                // Scheduled-open clock. HH = load/10, load%10; MM = open_time/10, open_time%10.
                // spec: Docs/RE/specs/frontend_scenes.md §2.4. CODE-CONFIRMED (digit-split math).
                string hh = $"{e.Load / 10}{e.Load % 10}";
                string mm = $"{e.OpenTime / 10}{e.OpenTime % 10}";
                return ($"{hh}:{mm}", new Color(0.80f, 0.70f, 0.40f));

            case 100:
                // Auto-connect sentinel ("connected / current selection"). spec §2.3. CODE-CONFIRMED.
                return ("", new Color(0.55f, 0.90f, 0.55f)); // green

            default:
                return ("", new Color(0.70f, 0.50f, 0.40f));
        }
    }

    private static (Color color, uint msgId) GetLoadColor(int load)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §2.3. CODE-CONFIRMED thresholds (strict >).
        if (load > 1200) return (new Color(0.90f, 0.25f, 0.25f), 6001u); // red
        if (load > 800) return (new Color(0.95f, 0.60f, 0.15f), 6002u); // orange
        if (load > 500) return (new Color(0.95f, 0.90f, 0.15f), 6003u); // yellow
        return (new Color(0.50f, 0.90f, 0.50f), 0u); // green (no special msg)
    }

    // =========================================================================
    // Plate click handler (actions 400/401). CODE-CONFIRMED.
    // Guard: status==0 && load < 2400. Persist Lastserver. Emit ServerSelected.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5 sub-state 37 / §2.5. CODE-CONFIRMED.
    // =========================================================================

    private void OnPlatePressed(int col)
    {
        int displaySlot = 2 * _currentPage + col;
        ServerEntry? entry = GetEntryAtDisplaySlot(displaySlot);
        if (entry is null) return;

        // Guard: status==0 && load < 2400.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5. CODE-CONFIRMED.
        if (entry.StatusCode != 0 || entry.Load >= LoadGuardThreshold)
        {
            GD.Print($"[ServerSelectScreen] Plate {col} (action {400 + col}) blocked — " +
                     $"status={entry.StatusCode} load={entry.Load} guard=status==0&&load<{LoadGuardThreshold}. " +
                     "spec §11.4/§1.5. CODE-CONFIRMED.");
            return;
        }

        // Persist Lastserver. spec §2.5. CODE-CONFIRMED.
        PersistLastServer(entry.ServerId);

        GD.Print($"[ServerSelectScreen] Plate {col} (action {400 + col}) → server_id={entry.ServerId}. " +
                 $"Lastserver persisted. Channel port={10000 + entry.ServerId}. " +
                 "spec §11.4/§1.5/§2.5. CODE-CONFIRMED.");

        EmitSignal(SignalName.ServerSelected, entry.ServerId);
    }

    // =========================================================================
    // Pager button handler (actions 115..124). CODE-CONFIRMED.
    // page = action − 115. Re-paints the 2-plate view.
    // spec: Docs/RE/specs/frontend_scenes.md §1.2 / §11.4. CODE-CONFIRMED.
    // =========================================================================

    private void OnPagerPressed(int page)
    {
        int serverCount = _servers?.Count ?? 0;
        int pageCount = serverCount == 0 ? 0 : (serverCount + 1) / 2;
        if (page < 0 || page >= pageCount) return;

        _currentPage = page;
        PaintPlates();

        GD.Print($"[ServerSelectScreen] Pager {page} (action {115 + page}) → " +
                 $"display slots [{2 * page}, {2 * page + 1}]. spec §1.2/§11.4. CODE-CONFIRMED.");
    }

    // =========================================================================
    // Refresh handler (action 105). spec §1.2. CODE-CONFIRMED.
    // Throttled ~10s re-fetch. In this passive view, re-emits BackRequested to let
    // BootFlow drive the re-fetch use-case (sub-state 34).
    // =========================================================================

    private void OnRefreshPressed()
    {
        // Delegate to BootFlow: re-fetch is a use-case, not view logic.
        GD.Print("[ServerSelectScreen] Refresh (action 105) pressed. spec §1.2. CODE-CONFIRMED.");
        EmitSignal(SignalName.BackRequested);
    }

    // =========================================================================
    // Lastserver persistence helpers. spec §2.5. CODE-CONFIRMED.
    // =========================================================================

    private static void PersistLastServer(int serverId)
    {
        // Godot ConfigFile equivalent of Win32 registry HKLM\software\crspace\do\Lastserver.
        // spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        cfg.SetValue(LastServerCfgSection, LastServerCfgKey, serverId);
        cfg.Save(LastServerCfgPath);
    }

    private static int LoadLastServer()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(LastServerCfgPath) != Error.Ok) return 0;
        return (int)cfg.GetValue(LastServerCfgSection, LastServerCfgKey, 0);
    }
}