// Ui/Scenes/Login/ServerSelectSubView.cs
//
// Server-select sub-view for the Login(1) state.
//
// Renders the server-list overlay (sub-states 34..41) as an internal sub-view of LoginWindow.
// Backed entirely by HudAtlasLibrary + HudTextLibrary — no UiAssetLoader dependency.
//
// Layout (spec: Docs/RE/specs/frontend_scenes.md §11.4 + §11.2a, CODE-CONFIRMED):
//   Central server-list area: dst(270,85,483,490), replacing the notice panel.
//   Select strips 400/401: loginwindow_02.dds src(9,6)/(220,6), dst(x-6,97,202,372).
//   Status icons: loginwindow_02.dds dst(x+47,97,100,372), src(varX,6).
//   Pager/name-strip buttons: loginwindow.dds dst(13+47*i,66,47,18), src(596,985)/(643,985), actions 115..124.
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
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// Server-select sub-view for Login(1) sub-states 34..41.
///
/// <para>Renders up to two server "plate" sprites for the current page, with pager tabs
/// and per-plate population-colour captions. All atlas drawing comes from
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

    // Server-list panel shares the central notice/listbox area.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4.
    private const int PanelX = 270;
    private const int PanelY = 85;

    // Plate source rect in loginwindow_02.dds (D).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "plate src(9,6,202,372)". CODE-CONFIRMED.
    private const int PlateSrcX = 9; // spec §11.4. CODE-CONFIRMED.
    private const int PlateSrcY = 6; // spec §11.4. CODE-CONFIRMED.
    private const int PlateHoverSrcX = 220; // spec §11.4. CODE-CONFIRMED.
    private const int PlateHoverSrcY = 6; // spec §11.4. CODE-CONFIRMED.
    private const int PlateW = 202; // spec §11.4. CODE-CONFIRMED.
    private const int PlateH = 372; // spec §11.4. CODE-CONFIRMED.

    // Two-plate layout, panel-local: x is the record text/header base; selectable strip is x-6.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4.
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

    // Plate action ids (400=left, 401=right).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    private const int ActionPlate0 = 400; // spec §11.4. CODE-CONFIRMED.
    private const int ActionPlate1 = 401; // spec §11.4. CODE-CONFIRMED.

    // Pager source rect in loginwindow.dds (B): src(596,985,47,18).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "pager buttons B src(596,985,47,18)". CODE-CONFIRMED.
    private const int PagerSrcX = 596; // spec §11.4. CODE-CONFIRMED.
    private const int PagerSrcY = 985; // spec §11.4. CODE-CONFIRMED.
    private const int PagerW = 47; // spec §11.4. CODE-CONFIRMED.
    private const int PagerH = 18; // spec §11.4. CODE-CONFIRMED.
    private const int PagerY = 66; // panel-local Y for the pager/name-strip row. spec §11.4. CODE-CONFIRMED.

    // Pager action ids 115..124 (re-page only, no commit).
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.2. CODE-CONFIRMED.
    private const int PagerActionBase = 115; // spec §11.4. CODE-CONFIRMED.
    private const int PagerCount = 10; // spec §11.4 "10 pager tabs". CODE-CONFIRMED.

    // Population colour thresholds.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "population colour msg ids 6001/6002/6003". CODE-CONFIRMED.
    private const int PopRedThreshold = 1200; // > 1200 → msg 6001 (red).    spec §11.4. CODE-CONFIRMED.
    private const int PopOrangeThreshold = 800; // > 800  → msg 6002 (orange). spec §11.4. CODE-CONFIRMED.
    private const int PopYellowThreshold = 500; // > 500  → msg 6003 (yellow). spec §11.4. CODE-CONFIRMED.

    // Population colour captions (msg.xdb ids). Population colours in msg.xdb.
    // spec: Docs/RE/specs/frontend_scenes.md §11.4 "6001=red,6002=orange,6003=yellow". CODE-CONFIRMED.
    private static readonly Color PopColorRed = new(1f, 0f, 0f, 1f); // > 1200. spec §11.4.
    private static readonly Color PopColorOrange = new(1f, 0.5f, 0f, 1f); // > 800.  spec §11.4.
    private static readonly Color PopColorYellow = new(1f, 1f, 0f, 1f); // > 500.  spec §11.4.
    private static readonly Color PopColorWhite = Colors.White; // ≤ 500.  spec §11.4.

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary _text;
    private IReadOnlyList<ServerEntry> _servers = [];
    private int _page;

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    [Signal]
    public delegate void BackRequestedEventHandler();

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
        _text = text;

        // Cover the full 1024×768 canvas; server-list children are offset into the central panel.
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
        _page = 0; // spec §11.4: initial visible page is the first two-server page.
        RebuildLayout();
    }

    // -------------------------------------------------------------------------
    // Layout builder
    // -------------------------------------------------------------------------

    private void RebuildLayout()
    {
        // Remove previous children.
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            Node child = GetChild(i);
            RemoveChild(child);
            child.QueueFree();
        }

        // Build the server-list overlay layers (back-to-front).
        BuildPanelFrame();
        BuildPagers();
        BuildFlagImage();
        // NOTE: no fabricated "back" button — IDA's server-list has no such control at (356,531). The list
        // closes on server selection (OnServerSelected) / the FSM. Removed the non-IDA back button that
        // overlapped the login form. spec: frontend_scenes.md §11.4. (binary: login build this+808).

        RebuildVisiblePage();
    }

    private void RebuildVisiblePage()
    {
        _page = ClampPage(_page);

        if (_servers.Count == 0)
        {
            // Empty server list draws no plates; pager row remains present. Notice id 4027 comes from msg.xdb.
            // spec: Docs/RE/specs/frontend_scenes.md §1.9 / §11.4.
            string msg = _text.GetCaption(LoginLayout.MsgErrNoServers, "[No servers]");
            GD.Print($"[ServerSelectSubView] page 0: no servers. msg {LoginLayout.MsgErrNoServers}: '{msg}'");
            return;
        }

        int firstIndex = _page * 2; // spec §11.4: plate index = 2·page + slot.
        int visibleCount = Math.Min(2, _servers.Count - firstIndex); // spec §11.4: max two servers per page.

        BuildPlate(PlateBaseX0, PlateY, ActionPlate0, firstIndex, StatusIconSrcX0);

        if (visibleCount > 1)
            BuildPlate(PlateBaseX1, PlateY, ActionPlate1, firstIndex + 1, StatusIconSrcX1);

        GD.Print(BuildPageBreadcrumb(firstIndex, visibleCount));
    }

    private void BuildPlate(int x, int y, int actionId, int serverIndex, int statusSrcX)
    {
        Texture2D? status = _atlas.SliceByPath(AtlasD, statusSrcX, StatusIconSrcY, StatusIconW, StatusIconH);
        if (status is not null)
        {
            AddChild(new TextureRect
            {
                Position = PanelPoint(x + StatusIconOffsetX, y),
                Size = new Vector2(StatusIconW, StatusIconH),
                Texture = status,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }

        Texture2D? normal = _atlas.SliceByPath(AtlasD, PlateSrcX, PlateSrcY, PlateW, PlateH);
        Texture2D? hover = _atlas.SliceByPath(AtlasD, PlateHoverSrcX, PlateHoverSrcY, PlateW, PlateH);

        // Make a clickable area backed by a TextureButton so the action can fire.
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
        };

        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        AddPlateCaption(x, _servers[serverIndex]);
    }

    private void OnPlateClicked(int actionId)
    {
        // Plate actions commit the page-relative slot: 400=left, 401=right.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4: index = 2·page + (action − 400).
        int idx = 2 * _page + (actionId - ActionPlate0);
        if (idx >= 0 && idx < _servers.Count)
        {
            ServerEntry entry = _servers[idx];
            if (!entry.IsSelectable)
            {
                GD.Print($"[ServerSelectSubView] Plate action {actionId} ignored: server {entry.ServerId} " +
                         $"unavailable (status={entry.StatusCode}, population={entry.Population}). " +
                         "spec: frontend_scenes.md §11.4.");
                return;
            }

            EmitSignal(SignalName.ServerSelected, entry.ServerId);
        }
    }

    private void BuildPagers()
    {
        // Pager buttons: 10 × (47×18) across the top of the overlay.
        // spec: §11.4 "pager buttons B src(596,985,47,18); actions 115..124". CODE-CONFIRMED.
        for (int i = 0; i < PagerCount; i++)
        {
            // spec: §11.2a "ServerRowBtnX0=13, ServerRowBtnXStep=47". CODE-CONFIRMED.
            int x = 13 + i * 47; // spec §11.2a. CODE-CONFIRMED.

            Texture2D? normal = _atlas.SliceByPath(AtlasB, PagerSrcX, PagerSrcY, PagerW, PagerH);
            int actionId = PagerActionBase + i;

            var btn = new TextureButton
            {
                Position = PanelPoint(x, PagerY),
                Size = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                TextureNormal = normal,
                TextureHover = normal,
                TexturePressed = normal,
                TextureDisabled = normal,
            };

            // Pager buttons re-page only — no commit/selection; captured in closure.
            int capturedAction = actionId;
            btn.Pressed += () => OnPagerClicked(capturedAction);
            AddChild(btn);
        }
    }

    private void BuildPanelFrame()
    {
        // Server-list parchment frame — loginwindow.dds dst(270,85,483,490) src(0,490). IDA: the server-list
        // panel this+808 IS a loginwindow panel sharing the notice panel's frame art. Without it the plates
        // float with no background (the "messy" server list). Added FIRST so it paints behind everything.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 / §11.2a. CODE-CONFIRMED (binary).
        Texture2D? frame = _atlas.SliceByPath(AtlasB,
            LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY,
            LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H);
        if (frame is not null)
        {
            AddChild(new TextureRect
            {
                Position = PanelPoint(0, 0),
                Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),
                Texture = frame,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }

        // Header image — loginwindow.dds dst(207,44,70,17) src(0,980), panel-local (IDA this+916).
        Texture2D? header = _atlas.SliceByPath(AtlasB, 0, 980,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H);
        if (header is not null)
        {
            AddChild(new TextureRect
            {
                Position = PanelPoint(LoginLayout.ListboxHeader.X, LoginLayout.ListboxHeader.Y),
                Size = new Vector2(LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H),
                Texture = header,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
    }

    private void BuildFlagImage()
    {
        // Flag image: loginwindow.dds dst(0,0,60,39) src(500,786).
        // spec: Docs/RE/specs/frontend_scenes.md §11.4.
        WidgetRect r = LoginLayout.SmallBadges;
        Texture2D? tex = _atlas.SliceByPath(AtlasB, r.SrcX, r.SrcY, r.W, r.H);
        if (tex is null)
            return;

        AddChild(new TextureRect
        {
            Position = PanelPoint(r.X, r.Y),
            Size = new Vector2(r.W, r.H),
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    private void BuildBackControl()
    {
        WidgetRect r = LoginLayout.OptionTab1;
        Texture2D? normal = _atlas.SliceByPath(AtlasB, r.SrcX, r.SrcY, r.W, r.H);
        Texture2D? hover = _atlas.SliceByPath(AtlasB, LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            r.W, r.H);

        var btn = new TextureButton
        {
            Position = new Vector2(LoginLayout.OptionTabsPanelX + r.X, LoginLayout.OptionTabsPanelY + r.Y),
            Size = new Vector2(r.W, r.H),
            CustomMinimumSize = new Vector2(r.W, r.H),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = normal,
            TextureHover = hover,
            TexturePressed = hover,
            TextureDisabled = normal,
        };

        btn.Pressed += () =>
        {
            GD.Print("[ServerSelectSubView] Back/close clicked: BackRequested. spec: frontend_scenes.md §11.2f.");
            EmitSignal(SignalName.BackRequested);
        };
        AddChild(btn);
    }

    private void OnPagerClicked(int actionId)
    {
        // Pager actions re-page only and never commit a selected server.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4: page = action − 115, clamped to available pages.
        _page = ClampPage(actionId - PagerActionBase);
        GD.Print(
            $"[ServerSelectSubView] Pager action {actionId} -> page {_page} (re-page only, no commit). spec: frontend_scenes.md §11.4.");
        RebuildLayout();
    }

    private void AddPlateCaption(int plateX, ServerEntry e)
    {
        // Row labels: dst(x,390,174,21), dst(x,410,174,20), dst(x,430,174,20).
        // spec: Docs/RE/specs/frontend_scenes.md §11.4.
        bool available = e.IsSelectable; // spec §11.4 load guard. CODE-CONFIRMED.
        Color popColor = PopColorForPopulation(e.Population); // spec §11.4 population colour bands. CODE-CONFIRMED.
        string name = ResolveServerName(e);
        string loadText = ResolveLoadText(e);
        string statusText = ResolveStatusText(e);

        AddRowLabel(name, plateX, RowLabelY0, RowLabelH0, Colors.White);
        AddRowLabel(loadText, plateX, RowLabelY1, RowLabelH, popColor);
        AddRowLabel(statusText, plateX, RowLabelY2, RowLabelH, available ? Colors.White : PopColorRed);
    }

    private void AddRowLabel(string text, int x, int y, int h, Color color)
    {
        var label = new Label
        {
            Text = text,
            Position = PanelPoint(x, y),
            Size = new Vector2(RowLabelW, h),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
    }

    private int ClampPage(int requestedPage)
    {
        // Pager action N maps to page N−115 and clamps to the available 2-server pages.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4: 10 pager actions 115..124, max 2 servers per page.
        int pageCount = Math.Max(1, (_servers.Count + 1) / 2);
        return Math.Clamp(requestedPage, 0, pageCount - 1);
    }

    private string BuildPageBreadcrumb(int firstIndex, int visibleCount)
    {
        // Maintainer-facing fidelity breadcrumb for the visible page.
        // spec: Docs/RE/specs/frontend_scenes.md §11.4 plate slots are server indices 2·page+0/1.
        string plate0 = DescribePlate(0, firstIndex);
        string plate1 = visibleCount > 1 ? DescribePlate(1, firstIndex + 1) : "plate1=<none>";
        return $"[ServerSelectSubView] page {_page}: {plate0}, {plate1}";
    }

    private string DescribePlate(int plateSlot, int serverIndex)
    {
        ServerEntry e = _servers[serverIndex];
        string name = e.DisplayName.Length > 0 ? e.DisplayName : $"Server {e.ServerId}";
        return $"plate{plateSlot}=server {e.ServerId} '{name}' pop {e.Population} selectable={e.IsSelectable}";
    }

    private static Vector2 PanelPoint(int x, int y) => new(PanelX + x, PanelY + y);

    private string ResolveServerName(ServerEntry e)
    {
        if (e.DisplayName.Length > 0)
            return e.DisplayName;

        if (e.ServerId is >= 1 and <= 40)
            return $"Server {e.ServerId}";

        // msg 5901 is the formatted fallback for out-of-range server ids.
        // spec: Docs/RE/specs/frontend_scenes.md §1.9 / §2.3.
        string fallback = $"Server {e.ServerId}";
        string template = _text.GetCaption(LoginLayout.MsgServerUnknown, fallback);
        return FormatCaption(template, e.ServerId, fallback);
    }

    private string ResolveLoadText(ServerEntry e)
    {
        // Active entries use the load buckets msg 6001/6002/6003; otherwise show the numeric load.
        // spec: Docs/RE/specs/frontend_scenes.md §2.3 / §11.4.
        if (e.StatusCode != 0)
            return e.Population.ToString();

        uint? msgId = e.Population > PopRedThreshold
            ? LoginLayout.MsgServerLoadRed
            : e.Population > PopOrangeThreshold
                ? LoginLayout.MsgServerLoadOrange
                : e.Population > PopYellowThreshold
                    ? LoginLayout.MsgServerLoadYellow
                    : null;

        return msgId is { } id
            ? _text.GetCaption(id, e.Population.ToString())
            : e.Population.ToString();
    }

    private string ResolveStatusText(ServerEntry e)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §2.3 — status==3 uses msg 6004/6005;
        // default status indexes msg 4029..4032.
        if (e.StatusCode == 0)
            return e.IsSelectable ? string.Empty : _text.GetCaption(LoginLayout.MsgServerLoadRed, "[Full]");

        if (e.StatusCode == 3)
        {
            if (e.Population == 24)
                return _text.GetCaption(LoginLayout.MsgServerPreparing, "[Preparing]");

            string template = _text.GetCaption(LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatCaption(template, e.Population, e.Flag, $"{e.Population:00}:{e.Flag:00}");
        }

        if (e.StatusCode is >= 1 and <= 4)
        {
            uint msgId = LoginLayout.MsgServerHeaderFirst + (uint)e.StatusCode - 1;
            return _text.GetCaption(msgId, $"[msg {msgId}]");
        }

        return $"[status {e.StatusCode}]";
    }

    private static string FormatCaption(string template, int value, string fallback)
        => FormatCaption(template, value, 0, fallback);

    private static string FormatCaption(string template, int value0, int value1, string fallback)
    {
        try
        {
            if (template.Contains("{0}", StringComparison.Ordinal) ||
                template.Contains("{0:", StringComparison.Ordinal))
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, value0, value1);

            if (template.Contains("%d", StringComparison.Ordinal))
            {
                string s = template;
                s = ReplaceFirst(s, "%d", value0.ToString(System.Globalization.CultureInfo.InvariantCulture));
                s = ReplaceFirst(s, "%d", value1.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return s;
            }

            if (template.Contains("%02d", StringComparison.Ordinal))
            {
                string s = template;
                s = ReplaceFirst(s, "%02d", value0.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                s = ReplaceFirst(s, "%02d", value1.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                return s;
            }
        }
        catch (FormatException)
        {
            return fallback;
        }

        return template == fallback ? fallback : $"{template} {value0}";
    }

    private static string ReplaceFirst(string value, string oldValue, string newValue)
    {
        int idx = value.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? value : value[..idx] + newValue + value[(idx + oldValue.Length)..];
    }

    // -------------------------------------------------------------------------
    // Population colour
    // spec: Docs/RE/specs/frontend_scenes.md §11.4. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private static Color PopColorForPopulation(int population)
    {
        if (population > PopRedThreshold) return PopColorRed; // > 1200. spec §11.4.
        if (population > PopOrangeThreshold) return PopColorOrange; // > 800.  spec §11.4.
        if (population > PopYellowThreshold) return PopColorYellow; // > 500.  spec §11.4.
        return PopColorWhite; // ≤ 500.  spec §11.4.
    }
}