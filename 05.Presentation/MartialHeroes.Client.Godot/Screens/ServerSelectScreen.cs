// Screens/ServerSelectScreen.cs
//
// Server-selection screen (part of the Login flow, displayed after login credential validation
// and before the PIN / enter-game handoff — see spec §1.5 sub-states 34-37).
//
// SPEC SOURCES:
//   spec: Docs/RE/specs/frontend_scenes.md §2 (server selection).
//   spec: Docs/RE/specs/login_flow.md §2.1 (server list 8-byte records, load thresholds, status
//         sentinels).
//   spec: Docs/RE/specs/ui_system.md §8.1 (server-list buttons — action IDs 115..119 for name strips).
//
// OFFLINE / DEV MODE:
//   The real flow fetches the server list via the lobby mini-protocol (port 10000). Since the game
//   servers are DEAD, this screen is ALWAYS populated with a synthetic list when run offline
//   (controlled by the dev_offline_flow flag in client_dir.cfg or the DEV_OFFLINE_FLOW env var).
//   The synthetic list follows the exact 8-byte record shape defined in login_flow.md §2.1 so
//   the presentation rules (color thresholds, status sentinels) are exercised faithfully.
//
// PASSIVE: zero game logic. Reads a server-list view-model (provided by caller) and turns a
// click into a ServerSelected signal carrying the server_id. No packet parsing, no domain state.
//
// Load color thresholds (CODE-CONFIRMED):
//   load > 1200 → "Full" (red)
//   load > 800  → "High" (orange)
//   load > 500  → "Medium" (yellow)
//   load ≤ 500  → "Light" (green)
// Status sentinels (CODE-CONFIRMED):
//   3  → scheduled open (show HH:MM from load/open_time fields)
//   24 → preparing / under check
//   100 → auto-connect sentinel (current selection)
//
// spec: Docs/RE/specs/login_flow.md §2.1 status sentinels and load thresholds. CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §2.3. CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A view-model for one entry in the server list, mirroring the 8-byte lobby record.
/// spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED field order and thresholds.
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
/// Server-selection screen displayed between the login form and the PIN modal.
/// Shows a list of <see cref="ServerEntry"/> rows with load gauges and status labels.
///
/// <para>Emits <see cref="ServerSelectedEventHandler"/> when the player clicks a row.</para>
/// <para>Emits <see cref="BackRequestedEventHandler"/> when Back is clicked.</para>
/// </summary>
public sealed partial class ServerSelectScreen : Control
{
    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>
    /// Raised when the player selects a server row.
    /// Carries the <see cref="ServerEntry.ServerId"/> (1..40).
    /// spec: Docs/RE/specs/frontend_scenes.md §2.5 — "selection commit". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    /// <summary>Raised when the Back button is clicked.</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // =========================================================================
    // Server list (view-model only — injected by BootFlow)
    // =========================================================================

    private IReadOnlyList<ServerEntry>? _servers;

    /// <summary>
    /// The server list to display. Set by the flow node before the scene is added to the tree,
    /// or call <see cref="SetServers"/> at any time to refresh.
    /// </summary>
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

    /// <summary>Optionally inject a shared asset loader. Opened privately when null.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // Container that holds the server rows (rebuilt when Servers changes).
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
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Dark backdrop.
        var bg = new ColorRect { Color = new Color(0.06f, 0.05f, 0.10f, 0.97f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Panel chrome (same dimensions as the login form band).
        var panel = new Panel();
        panel.Position = new Vector2(280, 80);
        panel.Size = new Vector2(460, 520);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.12f, 0.98f),
            BorderColor = new Color(0.50f, 0.43f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        // Title.
        var title = WidgetFactory.MakeLabel("SELECT SERVER", 16, new Color(0.95f, 0.86f, 0.55f));
        title.Position = new Vector2(12, 10);
        panel.AddChild(title);

        // Header row.
        BuildHeaderRow(panel);

        // Scrollable row container.
        var scroll = new ScrollContainer
        {
            Position = new Vector2(0, 56),
            Size = new Vector2(460, 420),
        };
        panel.AddChild(scroll);

        _rowContainer = new VBoxContainer
        {
            // let VBoxContainer size to content
        };
        scroll.AddChild(_rowContainer);

        // Back button.
        var backBtn = new Button
        {
            Text = "Back",
            Position = new Vector2(12, 538),
            Size = new Vector2(100, 30),
        };
        backBtn.Pressed += () => EmitSignal(SignalName.BackRequested);
        AddChild(backBtn);

        // Populate rows if already set.
        if (_servers is not null)
            RebuildRows();
        else
            AddChild(WidgetFactory.MakeLabel("Fetching server list...", 12, new Color(0.7f, 0.7f, 0.7f)));
    }

    private void BuildHeaderRow(Control parent)
    {
        var header = new HBoxContainer
        {
            Position = new Vector2(0, 36),
            Size = new Vector2(460, 20),
        };
        parent.AddChild(header);

        void AddHeader(string text, int minW)
        {
            var lbl = WidgetFactory.MakeLabel(text, 12, new Color(0.70f, 0.70f, 0.75f));
            lbl.CustomMinimumSize = new Vector2(minW, 20);
            header.AddChild(lbl);
        }

        AddHeader("Name", 200);
        AddHeader("Status", 80);
        AddHeader("Load", 80);
        AddHeader("", 80); // NEW badge column
    }

    /// <summary>
    /// Rebuilds the server row list from <see cref="_servers"/>. Called on the main thread only.
    /// spec: Docs/RE/specs/frontend_scenes.md §2 (server list display rules). CODE-CONFIRMED.
    /// </summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
        _servers = servers;
        if (IsInsideTree()) RebuildRows();
    }

    private void RebuildRows()
    {
        if (_rowContainer is null) return;

        // Clear existing rows.
        foreach (Node child in _rowContainer.GetChildren())
            child.QueueFree();

        if (_servers is null || _servers.Count == 0)
        {
            // spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 36 — empty → msg 4027.
            var empty = WidgetFactory.MakeLabel(
                _assets.Text(4027u, "No servers available."),
                12,
                new Color(0.80f, 0.40f, 0.40f));
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

    /// <summary>Builds one server row control.</summary>
    private Control BuildRow(ServerEntry entry)
    {
        var row = new HBoxContainer();
        row.CustomMinimumSize = new Vector2(440, 28);

        // Server name button (action ids 115..119 in legacy, per spec §8.1 action table).
        // spec: Docs/RE/specs/frontend_scenes.md §1.2 — "Server name-strip buttons ×5:
        //        actions 115..119 (range)". CODE-CONFIRMED.
        int captureId = entry.ServerId;
        var nameBtn = new Button
        {
            Text = entry.DisplayName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        nameBtn.Pressed += () =>
        {
            GD.Print($"[ServerSelectScreen] Server selected: id={captureId} name='{entry.DisplayName}'.");
            EmitSignal(SignalName.ServerSelected, captureId);
        };
        row.AddChild(nameBtn);

        // Status label.
        // spec: Docs/RE/specs/frontend_scenes.md §2.3 status-code special values. CODE-CONFIRMED.
        string statusText = entry.StatusCode switch
        {
            // spec: §2.3 — status_code == 3 and load == 24 → "preparing / check". CODE-CONFIRMED.
            3 when entry.Load == 24 => "Preparing",
            // spec: §2.3 — status_code == 3 and open_time != 0 → "HH:MM". CODE-CONFIRMED.
            3 => $"{entry.Load / 10:D2}:{entry.OpenTime % 60:D2}",
            // spec: §2.3 — status_code == 100 → "Connected". CODE-CONFIRMED.
            100 => "Connected",
            // spec: §2.3 — status_code == 24 → "Under Check". CODE-CONFIRMED.
            24 => "Checking",
            // All other positive values → show as open.
            > 0 => "Open",
            // Invalid / unavailable.
            _ => "Offline",
        };
        Color statusColor = entry.StatusCode is > 0 and not 24
            ? new Color(0.55f, 0.90f, 0.55f)
            : new Color(0.70f, 0.50f, 0.40f);

        var statusLbl = WidgetFactory.MakeLabel(statusText, 12, statusColor);
        statusLbl.CustomMinimumSize = new Vector2(80, 28);
        row.AddChild(statusLbl);

        // Load gauge.
        // spec: Docs/RE/specs/login_flow.md §2.1 load thresholds 1200/800/500. CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §2.3 load color thresholds. CODE-CONFIRMED.
        string loadText;
        Color loadColor;
        if (entry.Load > 1200)
        {
            // spec: load > 1200 → highest / "full" tier. CODE-CONFIRMED.
            loadText = "Full";
            loadColor = new Color(0.90f, 0.25f, 0.25f);
        }
        else if (entry.Load > 800)
        {
            // spec: load > 800 → high tier. CODE-CONFIRMED.
            loadText = "High";
            loadColor = new Color(0.95f, 0.60f, 0.15f);
        }
        else if (entry.Load > 500)
        {
            // spec: load > 500 → medium tier. CODE-CONFIRMED.
            loadText = "Medium";
            loadColor = new Color(0.95f, 0.90f, 0.15f);
        }
        else
        {
            // spec: load ≤ 500 → default tier. CODE-CONFIRMED.
            loadText = "Light";
            loadColor = new Color(0.50f, 0.90f, 0.50f);
        }

        var loadLbl = WidgetFactory.MakeLabel(loadText, 12, loadColor);
        loadLbl.CustomMinimumSize = new Vector2(80, 28);
        row.AddChild(loadLbl);

        // NEW badge.
        // spec: Docs/RE/specs/frontend_scenes.md §2.7 — NEW_SERVER_INDEX badge. CODE-CONFIRMED.
        if (entry.IsNew)
        {
            var newBadge = WidgetFactory.MakeLabel("NEW", 12, new Color(1.0f, 0.85f, 0.15f));
            newBadge.CustomMinimumSize = new Vector2(40, 28);
            row.AddChild(newBadge);
        }
        else
        {
            var spacer = new Control { CustomMinimumSize = new Vector2(40, 28) };
            row.AddChild(spacer);
        }

        return row;
    }
}
