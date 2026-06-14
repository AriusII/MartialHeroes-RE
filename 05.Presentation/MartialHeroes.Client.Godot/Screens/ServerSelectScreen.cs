// Screens/ServerSelectScreen.cs
//
// Server-selection screen — pixel-faithful rebuild against §11.4 of frontend_scenes.md.
//
// COMPOSITION MODEL (§11.4):
//   Same four atlases as the login screen (A=login_slice1.dds, B=loginwindow.dds,
//   C=InventWindow.dds, D=loginwindow_02.dds). Server selection is a visibility state of the
//   same login window; we implement it as a separate Control but reuse the same atlases.
//
//   Key widgets (§11.4):
//   - Server-list backdrop band: A/D dimmed background band at height-scaled Y=326.
//   - Parchment scroll panel: D (loginwindow_02.dds) per-row 100×372 / 202×372 plates.
//   - Server-row buttons x10 (loop): B@(13,66,47,18) X-step+47, NORMAL src(596,985), HOVER src(643,985).
//     Actions 115..124 (id-115 = row index). spec §11.4. CODE-CONFIRMED.
//   - Refresh button: A@(456,-3,111,38) NORMAL src(792,398). Action 105. spec §11.4. CODE-CONFIRMED.
//   - Refresh-button label plate: A@(407,-3,210,70) src(743,398) — baked art "새로고침".
//   - Connecting dialog (states 35/39): C reuses notice panel (318,647) 340×190, centred.
//     Caption candidate id 4023.
//
// STATUS / LOAD PRESENTATION (§2.3, CODE-CONFIRMED):
//   load > 1200 → Full (red); > 800 → High (orange); > 500 → Medium (yellow); ≤ 500 → Light (green).
//   status_code 3+load==24 → "Preparing"; status_code 3+open_time!=0 → "HH:MM" clock.
//   status_code 100 → auto-connect sentinel.
//
// PASSIVE: zero game logic. Reads a server-list view-model and turns row-click into ServerSelected.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 (CODE-CONFIRMED literals).
//       §2 (server selection presentation rules).
//       §2.3 (status / load presentation). CODE-CONFIRMED.
//       Docs/RE/specs/login_flow.md §2.1 (8-byte record, load thresholds). CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// View-model for one server entry (mirrors the 8-byte lobby record).
/// spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED field order.
/// </summary>
public sealed record ServerEntry(
    /// <summary>Index 1..40 into the client-local name table. spec §2.1.</summary>
    int ServerId,
    /// <summary>Display name (client-local, never on the wire). spec §2.8.</summary>
    string DisplayName,
    /// <summary>Availability sentinel. Special: 3=scheduled, 24=check, 100=current. spec §2.1.</summary>
    int StatusCode,
    /// <summary>Population gauge. Thresholds: 1200/800/500. spec §2.1. CODE-CONFIRMED.</summary>
    int Load,
    /// <summary>Open-time minutes field (meaningful when status_code==3). spec §2.1.</summary>
    int OpenTime,
    /// <summary>True when this server id equals the NEW_SERVER_INDEX Lua global. spec §2.7.</summary>
    bool IsNew = false);

/// <summary>
/// Server-selection screen. Pixel-faithful to §11.4.
/// Emits ServerSelected (server_id) on row click.
/// Emits BackRequested when Back is clicked.
/// </summary>
public sealed partial class ServerSelectScreen : Control
{
    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>
    /// Raised when the player selects a server row. Carries server_id (1..40).
    /// spec: Docs/RE/specs/frontend_scenes.md §2.5. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    /// <summary>Raised when Back is clicked.</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // =========================================================================
    // Server list (view-model, injected by BootFlow)
    // =========================================================================

    private IReadOnlyList<ServerEntry>? _servers;

    /// <summary>Sets the server list. Call before adding to the tree, or call SetServers() after.</summary>
    public IReadOnlyList<ServerEntry>? Servers
    {
        get => _servers;
        set
        {
            _servers = value;
            if (IsInsideTree()) RebuildRows();
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

    private Control? _rowContainer;

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
    // UI construction (§11.4)
    // =========================================================================

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // =======================================================================
        // [L1] Full background art — same as login screen.
        // A@(0,0,1024,398) src(0,0) — login_slice1.dds. spec §11.4 backdrop band. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bgSlice = _assets.Slice(
            LoginLayout.AtlasLoginSlice1, 0, 0, 1024, 398);
        if (bgSlice is not null)
        {
            var bgRect = new TextureRect
            {
                Name = "BgArtPanel",
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
            var fallback = new ColorRect { Color = new Color(0.04f, 0.04f, 0.10f) };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallback);
        }

        // =======================================================================
        // [L2] Main panel chrome — B@(0,110,1024,490) src(0,0) loginwindow.dds.
        // spec §11.4. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? mainPanel = _assets.Slice(
            LoginLayout.AtlasLoginWindow, 0, 0, 1024, 490);
        if (mainPanel is not null)
        {
            var panelRect = new TextureRect
            {
                Name = "MainPanelChrome",
                Texture = mainPanel,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(0, 110),
                Size = new Vector2(1024, 490),
            };
            AddChild(panelRect);
        }

        // =======================================================================
        // [L3] Parchment scroll panel (D = loginwindow_02.dds).
        // Two channel-tab block bodies side-by-side — the hanging parchment scrolls
        // with the 化神昇仙 calligraphy visible.
        // spec §11.4 "Parchment scroll panel (server tab)". CODE-CONFIRMED.
        // =======================================================================
        for (int blk = 0; blk < 2; blk++)
        {
            int blockX = 30 + blk * 233;
            int bodySrcV = 448 + blk * 124;

            AtlasTexture? blockBody = _assets.Slice(
                LoginLayout.AtlasLoginWindow02, bodySrcV, 6, 100, 372);
            if (blockBody is not null)
            {
                var bodyRect = new TextureRect
                {
                    Name = $"ParchmentBlock{blk}",
                    Texture = blockBody,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Position = new Vector2(blockX + 47, 97),
                    Size = new Vector2(100, 372),
                };
                AddChild(bodyRect);
            }
        }

        // =======================================================================
        // [L4] Refresh button — A@(456,-3,111,38) NORMAL src(792,398). Action 105.
        // spec §11.4 "Refresh button". CODE-CONFIRMED.
        // Note: Y=-3 means it bleeds slightly above the main panel top. The art is placed
        // at canvas Y = 110 + (-3) = 107.
        // =======================================================================
        var refreshBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            456, 107, 111, 38, // canvas Y = 110 - 3 = 107
            792, 398, // NORMAL src(792,398). spec §11.2c. CODE-CONFIRMED.
            602, 416, // HOVER src(602,416). spec §11.2c. CODE-CONFIRMED.
            602, 416, // PRESSED = HOVER.
            105, // Action 105 = Refresh / Help strip. spec §1.2. CODE-CONFIRMED.
            caption: "", captionTint: Colors.White);
        refreshBtn.Name = "RefreshButton";
        refreshBtn.ActionFired += _ => OnRefreshPressed();
        AddChild(refreshBtn);

        // Refresh button face/label plate (baked art "새로고침").
        // A@(407,-3,210,70) src(743,398). spec §11.4. CODE-CONFIRMED.
        AtlasTexture? refreshFace = _assets.Slice(LoginLayout.AtlasLoginSlice1, 743, 398, 210, 70);
        if (refreshFace is not null)
        {
            var facePlate = new TextureRect
            {
                Name = "RefreshFacePlate",
                Texture = refreshFace,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(407, 107),
                Size = new Vector2(210, 70),
            };
            AddChild(facePlate);
        }

        // =======================================================================
        // [L5] Server row list area.
        // The 10 row buttons are mapped from the left-panel scroll area.
        // We build a scroll container centred on the canvas covering x=270..750, y=165..600.
        // spec §11.4 "Server-row buttons x10". CODE-CONFIRMED.
        // =======================================================================
        // Scroll container for the row list.
        var listPanel = new Panel
        {
            Name = "ServerListPanel",
            Position = new Vector2(270, 165),
            Size = new Vector2(480, 420),
        };
        var listStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.10f, 0.85f),
            BorderColor = new Color(0.40f, 0.35f, 0.20f),
        };
        listStyle.SetBorderWidthAll(1);
        listPanel.AddThemeStyleboxOverride("panel", listStyle);
        AddChild(listPanel);

        // Column header row. spec §11.4 "List column header labels". CODE-CONFIRMED captions 4029..4032.
        var headerRow = new HBoxContainer
        {
            Name = "HeaderRow",
            Position = new Vector2(4, 4),
            Size = new Vector2(472, 20),
        };
        listPanel.AddChild(headerRow);

        void AddHdr(uint msgId, string fallback, int minW)
        {
            var lbl = WidgetFactory.MakeLabel(_assets.Text(msgId, fallback),
                LoginLayout.FontBodyHeight, new Color(0.80f, 0.80f, 0.60f));
            lbl.CustomMinimumSize = new Vector2(minW, 20);
            headerRow.AddChild(lbl);
        }

        // Caption ids 4029..4032 — column headers. spec §11.4. CODE-CONFIRMED.
        AddHdr(4029u, "Server", 220);
        AddHdr(4030u, "Status", 80);
        AddHdr(4031u, "Load", 80);
        AddHdr(4032u, "", 60);

        // Scroll + row container.
        var scroll = new ScrollContainer
        {
            Name = "RowScroll",
            Position = new Vector2(0, 28),
            Size = new Vector2(480, 392),
        };
        listPanel.AddChild(scroll);

        _rowContainer = new VBoxContainer { Name = "RowContainer" };
        scroll.AddChild(_rowContainer);

        // =======================================================================
        // [L6] Back button (offline convenience — not in spec as a canvas widget).
        // Placed at bottom-left below the list panel. PLAUSIBLE position.
        // =======================================================================
        var backBtn = new Button
        {
            Name = "BackButton",
            Text = "Back",
            Position = new Vector2(270, 598),
            Size = new Vector2(100, 28),
        };
        backBtn.Pressed += () => EmitSignal(SignalName.BackRequested);
        AddChild(backBtn);

        // Populate rows if already set.
        if (_servers is not null)
            RebuildRows();
        else
        {
            var waiting = WidgetFactory.MakeLabel(
                "Fetching server list...", LoginLayout.FontBodyHeight, new Color(0.65f, 0.65f, 0.65f));
            _rowContainer?.AddChild(waiting);
        }

        // =======================================================================
        // [L7] Zone-select ambient VFX — zone_sel_u.xeff (effect_id 380000000, 11 sub-effects).
        // Placed as a full-rect Control behind the list panel so the particle layer is visible
        // around the main UI chrome without obscuring interactive widgets.
        // spec: Docs/RE/formats/effects.md §A.15 — zone_sel_u.xeff; effect_id 380000000;
        //   sub_effect_count 11; SAMPLE-VERIFIED.
        // =======================================================================
        var vfxPlayer = new FrontEndEffectPlayer
        {
            Name = "ZoneSelEffect",
            // Full-canvas 2D effect.
            // spec: Docs/RE/formats/effects.md §A.15 — zone-select effect; SAMPLE-VERIFIED.
            XeffVfsPath = "data/effect/xeff/zone_sel_u.xeff",
            SharedRealAssets = SharedRealAssets,
            MouseFilter = MouseFilterEnum.Ignore, // never intercept input
            // Z-index above other 2D children so glow particles are visible on the dark background.
            ZIndex = 10,
        };
        // Size to the full reference canvas so the elliptical placement is correct.
        vfxPlayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(vfxPlayer);

        GD.Print("[ServerSelectScreen] Built (§11.4 pixel-faithful). Awaiting server list.");
    }

    // =========================================================================
    // Server list population
    // =========================================================================

    /// <summary>Sets the server list and rebuilds the row display.</summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        if (IsInsideTree()) RebuildRows();
    }

    private void RebuildRows()
    {
        if (_rowContainer is null) return;

        foreach (Node child in _rowContainer.GetChildren())
            child.QueueFree();

        if (_servers is null || _servers.Count == 0)
        {
            // spec §1.5 sub-state 36 "server list empty → msg 4027". CODE-CONFIRMED.
            var empty = WidgetFactory.MakeLabel(
                _assets.Text(LoginLayout.MsgErrNoServers, "No servers available."),
                LoginLayout.FontBodyHeight, new Color(0.80f, 0.40f, 0.40f));
            _rowContainer.AddChild(empty);
            return;
        }

        foreach (ServerEntry entry in _servers)
        {
            Control row = BuildRow(entry);
            _rowContainer.AddChild(row);
        }

        GD.Print($"[ServerSelectScreen] Rows built: {_servers.Count} entries.");
    }

    private Control BuildRow(ServerEntry entry)
    {
        // H1 fix: row uses an atlas button from loginwindow.dds as the click target,
        // matching the official parchment-row look. spec §11.4 "Server-row buttons x10 (loop):
        // B@(13,66,47,18) X-step+47, NORMAL src(596,985), HOVER src(643,985)." CODE-CONFIRMED.
        // In the official client the 47×18 atlas button is the visible row frame; we stretch it
        // to fill the full row width (472px) so it acts as a parchment background bar.
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(472, 30) };

        // Atlas button — the loginwindow.dds server-row art (47×18 stretched to row width).
        // We build it via WidgetFactory.MakeStateButton so it gets normal/hover/pressed states.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §11.2c. CODE-CONFIRMED src rects.
        int captureId = entry.ServerId;
        int rowActionId = LoginLayout.ServerRowActionBase + (entry.ServerId - 1); // action 115+idx
        var rowBtn = WidgetFactory.MakeStateButton(
            _assets,
            LoginLayout.AtlasLoginWindow,
            0, 0, // panel-local position (inside row)
            472, LoginLayout.ServerRowBtnH, // full row width × spec height (18px; padded via container)
            LoginLayout.ServerRowBtnNormalSrcX, LoginLayout.ServerRowBtnNormalSrcY, // NORMAL (596,985)
            LoginLayout.ServerRowBtnHoverSrcX, LoginLayout.ServerRowBtnHoverSrcY, // HOVER  (643,985)
            LoginLayout.ServerRowBtnHoverSrcX, LoginLayout.ServerRowBtnHoverSrcY, // PRESSED = HOVER
            rowActionId,
            caption: entry.DisplayName + (entry.IsNew ? " [NEW]" : ""),
            captionTint: new Color(0.90f, 0.85f, 0.60f)); // parchment-gold text
        rowBtn.Name = $"RowBtn{entry.ServerId}";
        rowBtn.ActionFired += _ =>
        {
            GD.Print($"[ServerSelectScreen] Server selected (atlas row): id={captureId} name='{entry.DisplayName}'.");
            EmitSignal(SignalName.ServerSelected, captureId);
        };
        row.AddChild(rowBtn);

        // Status, load, and population labels are overlaid on the atlas row button via absolute
        // positioning within the same HBoxContainer. They do NOT intercept mouse events.
        // spec §2.3 / §11.4. CODE-CONFIRMED positions (right columns of the row).
        string statusText = GetStatusText(entry);
        Color statusColor = GetStatusColor(entry);
        var statusLbl = WidgetFactory.MakeLabel(statusText, LoginLayout.FontBodyHeight, statusColor);
        statusLbl.CustomMinimumSize = new Vector2(80, LoginLayout.ServerRowBtnH);
        statusLbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(statusLbl);

        // Load gauge. spec §2.3 / login_flow.md §2.1. CODE-CONFIRMED.
        (string loadText, Color loadColor) = GetLoadDisplay(entry.Load);
        var loadLbl = WidgetFactory.MakeLabel(loadText, LoginLayout.FontBodyHeight, loadColor);
        loadLbl.CustomMinimumSize = new Vector2(80, LoginLayout.ServerRowBtnH);
        loadLbl.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(loadLbl);

        // Population availability captions 6001..6005. spec §11.4. CODE-CONFIRMED.
        uint popCapId = entry.Load > 1200 ? 6005u :
            entry.Load > 800 ? 6004u :
            entry.Load > 500 ? 6003u :
            entry.Load > 0 ? 6002u : 6001u;
        string popText = _assets.Text(popCapId, "");
        if (!string.IsNullOrEmpty(popText))
        {
            var popLbl = WidgetFactory.MakeLabel(popText, LoginLayout.FontBodyHeight, loadColor);
            popLbl.CustomMinimumSize = new Vector2(60, LoginLayout.ServerRowBtnH);
            popLbl.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(popLbl);
        }
        else
        {
            var spacer = new Control { CustomMinimumSize = new Vector2(60, LoginLayout.ServerRowBtnH) };
            row.AddChild(spacer);
        }

        return row;
    }

    // =========================================================================
    // Status / load presentation helpers (§2.3). CODE-CONFIRMED.
    // =========================================================================

    private static string GetStatusText(ServerEntry entry)
    {
        return entry.StatusCode switch
        {
            // spec §2.3 "status_code==3 and load==24 → preparing". CODE-CONFIRMED.
            3 when entry.Load == 24 => "Preparing",
            // spec §2.3 "status_code==3 and open_time!=0 → HH:MM". CODE-CONFIRMED.
            3 when entry.OpenTime != 0 => $"{entry.Load / 10:D2}:{entry.OpenTime % 60:D2}",
            3 => "Scheduled",
            // spec §2.3 "status_code==100 → auto-connect sentinel". CODE-CONFIRMED.
            100 => "Current",
            // spec §2.3 status 24 → "under check". CODE-CONFIRMED.
            24 => "Checking",
            > 0 => "Open",
            _ => "Offline",
        };
    }

    private static Color GetStatusColor(ServerEntry entry)
    {
        return entry.StatusCode is > 0 and not 24
            ? new Color(0.55f, 0.90f, 0.55f)
            : new Color(0.70f, 0.50f, 0.40f);
    }

    private static (string text, Color color) GetLoadDisplay(int load)
    {
        // spec: login_flow.md §2.1 load thresholds. CODE-CONFIRMED.
        if (load > 1200) return ("Full", new Color(0.90f, 0.25f, 0.25f));
        if (load > 800) return ("High", new Color(0.95f, 0.60f, 0.15f));
        if (load > 500) return ("Medium", new Color(0.95f, 0.90f, 0.15f));
        return ("Light", new Color(0.50f, 0.90f, 0.50f));
    }

    // =========================================================================
    // Intent handlers
    // =========================================================================

    private void OnRefreshPressed()
    {
        // In a live build this re-fetches the server list from the lobby (port 10000).
        // Action 105 has a 10-second cooldown per spec §1.2. CODE-CONFIRMED.
        // In offline mode this is a no-op.
        GD.Print("[ServerSelectScreen] Refresh button (action 105) pressed — no-op in offline mode.");
    }
}