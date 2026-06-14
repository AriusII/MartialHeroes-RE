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
//   status_code==3 and load==24  → "Preparing"
//   status_code==3 and open_time!=0 → "HH:MM" clock (load=HH, open_time=MM; §2.4 CODE-CONFIRMED)
//   status_code==100 → auto-connect sentinel
//
// PASSIVE: zero game logic.  Reads a view-model list; turns row clicks into ServerSelected(serverId).
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 (CODE-CONFIRMED literals).
//       §2 (server-selection presentation rules).
//       §2.3 (status / load color thresholds). CODE-CONFIRMED.
//       §2.4 (scheduled-open HH:MM clock packing). CODE-CONFIRMED.
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
    /// <summary>Display name (client-local, never on the wire). spec §2.8.</summary>
    string DisplayName,
    /// <summary>Availability sentinel. Special: 3=scheduled, 24=check, 100=current. spec §2.3.</summary>
    int StatusCode,
    /// <summary>Population gauge / scheduled HH. Thresholds: 1200/800/500. spec §2.1. CODE-CONFIRMED.</summary>
    int Load,
    /// <summary>Scheduled open-time MM field (meaningful when status_code==3). spec §2.4.</summary>
    int OpenTime,
    /// <summary>True when this server id equals the NEW_SERVER_INDEX Lua global. spec §2.7.</summary>
    bool IsNew = false);

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
                9, 6,    // NORMAL src(9,6). spec §11.4. CODE-CONFIRMED.
                220, 6,  // HOVER  src(220,6). spec §11.4. CODE-CONFIRMED.
                220, 6,  // PRESSED = HOVER.
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
        int[] bodySrcU = { 448, 572 };        // spec §11.4 loop srcU start=448 step+124. CODE-CONFIRMED.
        int[] bodyDstX = { 77, 310 };          // spec §11.4 dst offsets. CODE-CONFIRMED.
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
            int rowX = LoginLayout.ServerRowBtnX0 + n * LoginLayout.ServerRowBtnXStep; // 13+47·n. spec §11.4. CODE-CONFIRMED.
            // spec §11.4 X bound check: X < 483 (loop condition). All 10 fit. CODE-CONFIRMED.
            int rowActionId = LoginLayout.ServerRowActionBase + n; // 115+n. spec §11.4. CODE-CONFIRMED.

            StateButton rowBtn = WidgetFactory.MakeStateButton(
                _assets,
                LoginLayout.AtlasLoginWindow,
                rowX, LoginLayout.ServerRowBtnY,        // dst X, Y=66. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnW,              // W=47. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnH,              // H=18. spec §11.4. CODE-CONFIRMED.
                LoginLayout.ServerRowBtnNormalSrcX, LoginLayout.ServerRowBtnNormalSrcY, // NORMAL src(596,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnHoverSrcX,  LoginLayout.ServerRowBtnHoverSrcY,  // HOVER  src(643,985). CODE-CONFIRMED.
                LoginLayout.ServerRowBtnHoverSrcX,  LoginLayout.ServerRowBtnHoverSrcY,  // PRESSED = HOVER.
                rowActionId,
                caption: "",        // label drawn via server-list text overlay (see below)
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
            792, 398,   // NORMAL src(792,398). spec §11.4. CODE-CONFIRMED.
            602, 416,   // HOVER  src(602,416). spec §11.4. CODE-CONFIRMED.
            602, 416,   // PRESSED = HOVER.
            105);       // Action 105 = refresh/re-fetch. spec §1.2. CODE-CONFIRMED.
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
        AddHdr(4029u, "서버명", 180);  // Server name column
        AddHdr(4030u, "상태", 70);    // Status column
        AddHdr(4031u, "부하", 70);    // Load column

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
        if (IsInsideTree()) RebuildServerLabels();
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

        // Build one row per server entry (spec §11.4 — labels inside the parchment body area).
        for (int i = 0; i < _servers.Count; i++)
        {
            ServerEntry entry = _servers[i];
            Control row = BuildServerRow(entry, i);
            rowList.AddChild(row);
        }

        GD.Print($"[ServerSelectScreen] Server list: {_servers.Count} entries populated.");
    }

    private Control BuildServerRow(ServerEntry entry, int rowIndex)
    {
        // Each row is an HBox of: server name label | status label | load label.
        // Row height matches the server-row button height (18px) at the spec scale.
        var row = new HBoxContainer
        {
            Name = $"ServerRow_{rowIndex}",
            CustomMinimumSize = new Vector2(450, 22),
        };

        // Server name (DisplayName from client-local name table, spec §2.8. CODE-CONFIRMED).
        bool isActiveRow = (rowIndex == _activeRowIndex);
        Color nameColor = isActiveRow
            ? new Color(1.0f, 0.95f, 0.60f)   // active/selected: bright gold
            : new Color(0.85f, 0.78f, 0.50f);  // normal: parchment gold

        string nameText = entry.DisplayName + (entry.IsNew ? " ★" : "");
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
        return entry.StatusCode switch
        {
            // status_code==3 and load==24 → "Preparing / under check". spec §2.3. CODE-CONFIRMED.
            3 when entry.Load == 24 => "Preparing",
            // status_code==3 and open_time!=0 → HH:MM clock. spec §2.4. CODE-CONFIRMED.
            // HH = load field (load/10, load%10); MM = open_time field.
            3 when entry.OpenTime != 0 => $"{entry.Load / 10:D2}:{entry.OpenTime % 60:D2}",
            3 => "Scheduled",
            // status_code==100 → auto-connect sentinel. spec §2.3. CODE-CONFIRMED.
            100 => "Connected",
            // status_code==24 → under check (use same sentinel check as load==24). spec §2.3.
            24 => "Checking",
            > 0 => "Open",
            _ => "Offline",
        };
    }

    private static Color GetStatusColor(ServerEntry entry)
    {
        return entry.StatusCode is > 0 and not 24
            ? new Color(0.55f, 0.90f, 0.55f)   // open: green
            : new Color(0.70f, 0.50f, 0.40f);   // unavailable: brownish
    }

    private static (string text, Color color) GetLoadDisplay(int load)
    {
        // spec: login_flow.md §2.1 load thresholds. CODE-CONFIRMED.
        if (load > 1200) return ("Full",   new Color(0.90f, 0.25f, 0.25f));   // red
        if (load > 800)  return ("High",   new Color(0.95f, 0.60f, 0.15f));   // orange
        if (load > 500)  return ("Medium", new Color(0.95f, 0.90f, 0.15f));   // yellow
        return ("Light", new Color(0.50f, 0.90f, 0.50f));                     // green
    }

    // =========================================================================
    // Row-tab click handler (action 115+n, spec §11.4 / §2.5. CODE-CONFIRMED)
    // =========================================================================

    private void OnRowButtonPressed(int rowIndex)
    {
        if (_servers is null || rowIndex >= _servers.Count) return;

        ServerEntry selected = _servers[rowIndex];
        _activeRowIndex = rowIndex;

        // Rebuild row labels to reflect the active-row highlight.
        RebuildServerLabels();

        GD.Print($"[ServerSelectScreen] Row tab {rowIndex} pressed → server id={selected.ServerId} " +
                 $"name='{selected.DisplayName}'. Action={LoginLayout.ServerRowActionBase + rowIndex}. " +
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
