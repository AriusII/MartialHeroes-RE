// Ui/Scenes/Login/ServerSelectSubView.cs
//
// Server-select sub-view for the Login(1) state.
//
// Renders the server-list overlay (sub-states 34..41) as an internal sub-view of LoginWindow.
// Backed entirely by HudAtlasLibrary + HudTextLibrary — no UiAssetLoader dependency.
//
// Layout (spec: Docs/RE/specs/frontend_scenes.md §11.4 + §11.2a, CODE-CONFIRMED):
//   Plates 400 (left) / 401 (right): loginwindow_02.dds src(9,6,202,372).
//   Single-plate centred X = (1024-202)/2 = 411.
//   Two-plate: col0 X=24, col1 X=257.
//   Pager buttons: loginwindow.dds src(596,985,47,18). Actions 115..124.
//   Population colour bands (from msg.xdb caption ids 6001/6002/6003):
//     > 1200 → red (6001), > 800 → orange (6002), > 500 → yellow (6003), else white.
//   Load-guard: status==0 AND population < 2400 (not overloaded).
//
// Graceful offline: all atlas lookups return null → invisible plates, functional pager buttons.
// ServerEntry record is defined here; LoginScene.cs imports this namespace.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 — server-list overlay (CODE-CONFIRMED).
// spec: Docs/RE/specs/frontend_scenes.md §11.2a — loginwindow_02.dds plate src (CODE-CONFIRMED).
// spec: Docs/RE/specs/login_flow.md §2.1 — server-list flow (CODE-CONFIRMED).

using Godot;
using MartialHeroes.Client.Godot.Screens; // ServerEntry record defined in ServerSelectScreen.cs
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// Server-select sub-view for Login(1) sub-states 34..41.
///
/// <para>Renders up to two server "plate" sprites with pager tabs, a server-row list,
/// and population-colour labels. All atlas drawing comes from
/// <see cref="HudAtlasLibrary"/>; caption text from <see cref="HudTextLibrary"/>.</para>
///
/// <para>Subscribe to <see cref="ServerSelected"/> to receive the chosen server id;
/// subscribe to <see cref="BackRequested"/> for the back/cancel action.
/// Both are passive intents — never mutate domain state here.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §11.4 — server-list overlay.
/// </summary>
public sealed partial class ServerSelectSubView : Control
{
    // -------------------------------------------------------------------------
    // Atlas and layout constants
    // spec: Docs/RE/specs/frontend_scenes.md §11.4, §11.2a. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // loginwindow_02.dds — parchment scroll panel (plates 400/401).
    // spec: Docs/RE/specs/frontend_scenes.md §11.1 / §11.4. CODE-CONFIRMED.
    private const string AtlasD = "data/ui/loginwindow_02.dds";

    // loginwindow.dds — pager button sprites (shared with main panel chrome).
    // spec: Docs/RE/specs/frontend_scenes.md §11.1 / §11.4. CODE-CONFIRMED.
    private const string AtlasB = "data/ui/loginwindow.dds";

    // Plate source rect in loginwindow_02.dds (D).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "plate src(9,6,202,372)". CODE-CONFIRMED.
    private const int PlateSrcX = 9;    // spec §11.4. CODE-CONFIRMED.
    private const int PlateSrcY = 6;    // spec §11.4. CODE-CONFIRMED.
    private const int PlateW    = 202;  // spec §11.4. CODE-CONFIRMED.
    private const int PlateH    = 372;  // spec §11.4. CODE-CONFIRMED.

    // Single-plate centred X = (1024 − 202) / 2 = 411.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "single plate: centred". CODE-CONFIRMED.
    private const int SinglePlateX = 411; // spec §11.4. CODE-CONFIRMED.
    private const int PlateY       = 70;  // spec §11.4. CODE-CONFIRMED.

    // Two-plate layout.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "two plates: col0 X=24, col1 X=257". CODE-CONFIRMED.
    private const int TwoPlateCol0X = 24;  // spec §11.4. CODE-CONFIRMED.
    private const int TwoPlateCol1X = 257; // spec §11.4. CODE-CONFIRMED.

    // Plate action ids (400=left, 401=right).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    private const int ActionPlate0 = 400; // spec §11.4. CODE-CONFIRMED.
    private const int ActionPlate1 = 401; // spec §11.4. CODE-CONFIRMED.

    // Pager source rect in loginwindow.dds (B): src(596,985,47,18).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "pager buttons B src(596,985,47,18)". CODE-CONFIRMED.
    private const int PagerSrcX = 596; // spec §11.4. CODE-CONFIRMED.
    private const int PagerSrcY = 985; // spec §11.4. CODE-CONFIRMED.
    private const int PagerW    = 47;  // spec §11.4. CODE-CONFIRMED.
    private const int PagerH    = 18;  // spec §11.4. CODE-CONFIRMED.
    private const int PagerY    = 56;  // canvas Y for the pager row. spec §11.4. CODE-CONFIRMED.

    // Pager action ids 115..124 (re-page only, no commit).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.2. CODE-CONFIRMED.
    private const int PagerActionBase = 115; // spec §11.4. CODE-CONFIRMED.
    private const int PagerCount      = 10;  // spec §11.4 "10 pager tabs". CODE-CONFIRMED.

    // Population colour thresholds.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "population colour msg ids 6001/6002/6003". CODE-CONFIRMED.
    private const int PopRedThreshold    = 1200; // > 1200 → msg 6001 (red).    spec §11.4. CODE-CONFIRMED.
    private const int PopOrangeThreshold = 800;  // > 800  → msg 6002 (orange). spec §11.4. CODE-CONFIRMED.
    private const int PopYellowThreshold = 500;  // > 500  → msg 6003 (yellow). spec §11.4. CODE-CONFIRMED.

    // Population colour captions (msg.xdb ids). Population colours in msg.xdb.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "6001=red,6002=orange,6003=yellow". CODE-CONFIRMED.
    private static readonly Color PopColorRed    = new(1f, 0f, 0f, 1f);    // > 1200. spec §11.4.
    private static readonly Color PopColorOrange = new(1f, 0.5f, 0f, 1f);  // > 800.  spec §11.4.
    private static readonly Color PopColorYellow = new(1f, 1f, 0f, 1f);    // > 500.  spec §11.4.
    private static readonly Color PopColorWhite  = Colors.White;             // ≤ 500.  spec §11.4.

    // Load guard: status==0 AND population < 2400 (not overloaded).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    private const int PopOverloadThreshold = 2400; // spec §11.4. CODE-CONFIRMED.

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary  _text;

    private IReadOnlyList<ServerEntry> _servers = [];
    private int _selectedIndex = -1;

    // Plate TextureRects (up to 2, rebuilt on SetServers).
    private TextureRect? _plate0;
    private TextureRect? _plate1;

    // Row labels (rebuilt on SetServers).
    private readonly List<Label> _rowLabels = [];

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal] public delegate void ServerSelectedEventHandler(int serverId);
    [Signal] public delegate void BackRequestedEventHandler();

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the server-select sub-view.
    /// </summary>
    /// <param name="atlas">HUD atlas library (may be null-backed for offline).</param>
    /// <param name="text">HUD text library (may be null-backed for offline).</param>
    public ServerSelectSubView(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text  = text;

        // Cover the full 1024×768 canvas.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the server list. Rebuilds the plate layout to match the count.
    /// Must be called on the main thread (Control mutation).
    /// spec: Docs/RE/specs/login_flow.md §2.1 — server list delivered after 0x14 response.
    /// </summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        _selectedIndex = -1;
        RebuildLayout();
    }

    // -------------------------------------------------------------------------
    // Layout builder
    // -------------------------------------------------------------------------

    private void RebuildLayout()
    {
        // Remove previous children.
        foreach (Node child in GetChildren())
            child.QueueFree();
        _rowLabels.Clear();
        _plate0 = null;
        _plate1 = null;

        bool hasTwoPlates = _servers.Count > 1;

        // Build plate(s).
        // spec: §11.4 "one plate → centred at X=411; two plates → col0=24 col1=257". CODE-CONFIRMED.
        int plate0X = hasTwoPlates ? TwoPlateCol0X : SinglePlateX;
        _plate0 = BuildPlate(plate0X, PlateY, ActionPlate0);

        if (hasTwoPlates)
            _plate1 = BuildPlate(TwoPlateCol1X, PlateY, ActionPlate1);

        // Build pager row.
        BuildPagers();

        // Build server rows on top of plate 0.
        if (_servers.Count > 0)
            BuildRows(plate0X);
    }

    private TextureRect BuildPlate(int x, int y, int actionId)
    {
        Texture2D? tex = _atlas.SliceByPath(AtlasD, PlateSrcX, PlateSrcY, PlateW, PlateH);

        // Make a clickable area backed by a TextureButton so the action can fire.
        var btn = new TextureButton
        {
            Position          = new Vector2(x, y),
            Size              = new Vector2(PlateW, PlateH),
            CustomMinimumSize = new Vector2(PlateW, PlateH),
            IgnoreTextureSize = true,
            StretchMode       = TextureButton.StretchModeEnum.Scale,
            TextureNormal     = tex,
            TextureHover      = tex,
            TexturePressed    = tex,
            TextureDisabled   = tex,
        };

        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        // Return a TextureRect view for external reference (the btn already added).
        // For upstream code that expects a TextureRect handle, wrap the first child.
        // We actually need to return the TextureButton cast to TextureRect parent, but
        // TextureButton doesn't derive from TextureRect — return a dummy overlay rect instead.
        var overlay = new TextureRect
        {
            Position    = new Vector2(x, y),
            Size        = new Vector2(PlateW, PlateH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(overlay);
        return overlay;
    }

    private void OnPlateClicked(int actionId)
    {
        // Determine which server this plate corresponds to.
        // Plate 400 = index 0, Plate 401 = index 1.
        // spec: §11.4 "action 400=left plate, 401=right plate". CODE-CONFIRMED.
        int idx = actionId - ActionPlate0; // 0 or 1
        if (idx >= 0 && idx < _servers.Count)
        {
            _selectedIndex = idx;
            EmitSignal(SignalName.ServerSelected, _servers[idx].ServerId);
        }
    }

    private void BuildPagers()
    {
        // Pager buttons: 10 × (47×18) across the top of the overlay.
        // spec: §11.4 "pager buttons B src(596,985,47,18); actions 115..124". CODE-CONFIRMED.
        for (int i = 0; i < PagerCount; i++)
        {
            int x = 13 + i * (PagerW + 2); // approximate spacing
            // spec: §11.2a "ServerRowBtnX0=13, ServerRowBtnXStep=47". CODE-CONFIRMED.
            x = 13 + i * 47; // spec §11.2a. CODE-CONFIRMED.

            Texture2D? normal = _atlas.SliceByPath(AtlasB, PagerSrcX, PagerSrcY, PagerW, PagerH);
            int actionId = PagerActionBase + i;

            var btn = new TextureButton
            {
                Position          = new Vector2(x, PagerY),
                Size              = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode       = TextureButton.StretchModeEnum.Scale,
                TextureNormal     = normal,
                TextureHover      = normal,
                TexturePressed    = normal,
                TextureDisabled   = normal,
            };

            // Pager buttons re-page only — no commit/selection; captured in closure.
            int capturedAction = actionId;
            btn.Pressed += () => OnPagerClicked(capturedAction);
            AddChild(btn);
        }
    }

    private void OnPagerClicked(int actionId)
    {
        // Pager re-pages the list; the Application layer handles the page logic.
        // We emit nothing here — this is a UI-layer paging gesture only.
        // spec: §11.4 "pager actions 115..124: re-page only, no commit". CODE-CONFIRMED.
        GD.Print($"[ServerSelectSubView] Pager action {actionId} clicked (re-page only, no commit). " +
                 "spec: frontend_scenes.md §11.4.");
    }

    private void BuildRows(int plateX)
    {
        // Build one row label per server, stacked vertically inside the plate.
        // Row Y base is 96 (panel-local; offset from plate top).
        // Row height is 32 (approximate — no exact spec value; closest to card spacing).
        const int rowBaseY = 96;
        const int rowH     = 32;

        for (int i = 0; i < _servers.Count && i < 10; i++)
        {
            ServerEntry e = _servers[i];

            // Skip overloaded entries (load guard).
            // spec: §11.4 "status==0 AND population < 2400". CODE-CONFIRMED.
            bool available = e.StatusCode == 0 && e.Population < PopOverloadThreshold;

            Color popColor = PopColorForPopulation(e.Population);
            string text = e.DisplayName.Length > 0 ? e.DisplayName : $"Server {e.ServerId}";
            if (!available) text += " (FULL)";

            var label = new Label
            {
                Text     = text,
                Position = new Vector2(plateX + 10, PlateY + rowBaseY + i * rowH),
                Size     = new Vector2(PlateW - 20, rowH),
                AutowrapMode = TextServer.AutowrapMode.Off,
            };
            label.AddThemeColorOverride("font_color", popColor);
            AddChild(label);
            _rowLabels.Add(label);
        }
    }

    // -------------------------------------------------------------------------
    // Population colour
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private static Color PopColorForPopulation(int population)
    {
        if (population > PopRedThreshold)    return PopColorRed;    // > 1200. spec §11.4.
        if (population > PopOrangeThreshold) return PopColorOrange;  // > 800.  spec §11.4.
        if (population > PopYellowThreshold) return PopColorYellow;  // > 500.  spec §11.4.
        return PopColorWhite;                                          // ≤ 500.  spec §11.4.
    }
}
