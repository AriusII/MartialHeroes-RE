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
//   Population colour bands (from msg.xdb caption ids 6001/6002/6003):
//     > 1200 → red (6001), > 800 → orange (6002), > 500 → yellow (6003), ≤500 → green (no msg, numeric).
//   Load-guard: status==0 AND population < 2400 (not overloaded).
//   Server display names: msg.xdb ids 5001..5040 (server_id N → id 5000+N).
//   Population/count label: font slot 4. spec: §4 "population/count label … font slot 4, formatted %4d / %4d".
//   Status-color indicator quads ×3: A2 src(500,786) 60×39, hidden by default. spec: §4.
//
// spec: Docs/RE/specs/frontend_layout_tables.md §4
// spec: Docs/RE/specs/login_flow.md §2.1

using Godot;
using MartialHeroes.Client.Godot.Screens; // ServerEntry record defined in ServerSelectScreen.cs
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui;
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
/// <para>Subscribe to <see cref="ServerSelected"/> to receive the chosen server id.
/// Passive intent only — never mutates domain state.</para>
///
/// spec: Docs/RE/specs/frontend_layout_tables.md §4
/// </summary>
public sealed partial class ServerSelectSubView : Control
{
    // Atlas paths. spec: Docs/RE/specs/frontend_layout_tables.md §1 / §4
    private const string AtlasD = "data/ui/loginwindow_02.dds";
    private const string AtlasB = "data/ui/loginwindow.dds";

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

    // Population colour thresholds. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int PopRedThreshold = 1200;
    private const int PopOrangeThreshold = 800;
    private const int PopYellowThreshold = 500;

    // Population colour DWORDs (ARGB). spec: Docs/RE/specs/frontend_layout_tables.md §4
    private static readonly Color PopColorRed = Color.Color8(255, 0, 0);
    private static readonly Color PopColorOrange = Color.Color8(237, 104, 6);
    private static readonly Color PopColorYellow = Color.Color8(255, 255, 0);
    private static readonly Color PopColorGreen = Color.Color8(181, 255, 122);

    // Server name caption id base. spec: Docs/RE/specs/frontend_layout_tables.md §4
    private const int ServerNameCaptionBase = 5000; // server_id N → caption 5000+N

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary _text;
    private IReadOnlyList<ServerEntry> _servers = [];
    private int _page;

    // Three status-color indicator quads (A2 src(500,786) 60×39), hidden by default.
    // Re-anchored around a status==100 special row when present.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status-color indicator quads ×3"
    private readonly TextureRect?[] _statusIndicators = new TextureRect?[3];

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the server-select sub-view.
    /// </summary>
    public ServerSelectSubView(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the server list. Rebuilds the plate layout to match the count.
    /// Must be called on the main thread (Control mutation).
    /// spec: Docs/RE/specs/login_flow.md §2.1
    /// </summary>
    public void SetServers(IReadOnlyList<ServerEntry> servers)
    {
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
        for (int i = 0; i < _statusIndicators.Length; i++)
            _statusIndicators[i] = null;

        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            Node child = GetChild(i);
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
        AtlasTexture? tex = _atlas.SliceByPath(AtlasB,
            LoginLayout.StatusIndicatorSrcX, LoginLayout.StatusIndicatorSrcY,
            LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH);

        for (int i = 0; i < LoginLayout.StatusIndicatorCount; i++) // spec: §4 (×3)
        {
            var rect = new TextureRect
            {
                Texture = tex,
                Size = new Vector2(LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false, // hidden by default; shown when status==100 row present
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
        foreach (TextureRect? ind in _statusIndicators)
            if (ind is not null)
                ind.Visible = false;

        if (_servers.Count == 0)
        {
            string msg = _text.GetCaption(LoginLayout.MsgErrNoServers, string.Empty);
            GD.Print($"[ServerSelectSubView] page 0: no servers. msg {LoginLayout.MsgErrNoServers}: '{msg}'");
            return;
        }

        int firstIndex = _page * 2; // spec: frontend_layout_tables.md §4 "plate index = 2·page + slot"
        int visibleCount = Math.Min(2, _servers.Count - firstIndex);

        int[] plateX = [PlateBaseX0, PlateBaseX1];
        int[] statusSrcX = [StatusIconSrcX0, StatusIconSrcX1];
        int[] actions = [ActionPlate0, ActionPlate1];

        for (int slot = 0; slot < visibleCount; slot++)
        {
            int idx = firstIndex + slot;
            BuildPlate(plateX[slot], PlateY, actions[slot], idx, statusSrcX[slot]);

            // Re-anchor the 3 status indicators around a status==100 special row.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status==100 gate … 3 quads re-anchored around it"
            if (_servers[idx].StatusCode == 100)
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
        for (int i = 1; i <= 2; i++)
        {
            if (_statusIndicators[i] is { } ind)
            {
                ind.Position = PanelPoint(anchorX + 139, anchorY + 13);
                ind.Visible = true;
            }
        }
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
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "index = (action−400) + 2·page"
        int idx = 2 * _page + (actionId - ActionPlate0);
        if (idx >= 0 && idx < _servers.Count)
        {
            ServerEntry entry = _servers[idx];
            if (!entry.IsSelectable)
            {
                GD.Print($"[ServerSelectSubView] Plate action {actionId} ignored: server {entry.ServerId} " +
                         $"unavailable (status={entry.StatusCode}, load={entry.Load}). " +
                         "spec: frontend_layout_tables.md §4.");
                return;
            }

            EmitSignal(SignalName.ServerSelected, entry.ServerId);
        }
    }

    private void BuildPagers()
    {
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "pager tabs (13+47·i,66,47,18); actions 115..124"
        for (int i = 0; i < PagerCount; i++)
        {
            int x = 13 + i * 47; // spec: Docs/RE/specs/frontend_layout_tables.md §4 "pager (13+47·i, 66)"

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

            int capturedAction = actionId;
            btn.Pressed += () => OnPagerClicked(capturedAction);
            AddChild(btn);
        }
    }

    private void BuildPanelFrame()
    {
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

    private void OnPagerClicked(int actionId)
    {
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "page = action − 115 (absolute, not relative)"
        _page = ClampPage(actionId - PagerActionBase);
        GD.Print(
            $"[ServerSelectSubView] Pager action {actionId} -> page {_page} (re-page only). spec: frontend_layout_tables.md §4.");
        RebuildLayout();
    }

    private void AddPlateCaption(int plateX, ServerEntry e)
    {
        bool available = e.IsSelectable;
        Color popColor = PopColorForLoad(e.Load);
        string name = ResolveServerName(e);
        string loadText = ResolveLoadText(e);
        string statusText = ResolveStatusText(e);

        // Name label (left-aligned per §4 "prefer left"). Font slot 0 (default).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4
        AddRowLabel(name, plateX, RowLabelY0, RowLabelH0, Colors.White, fontSlot: 0,
            align: HorizontalAlignment.Left);

        // Status/info label. Font slot 0.
        // spec: Docs/RE/specs/frontend_layout_tables.md §4
        AddRowLabel(statusText, plateX, RowLabelY1, RowLabelH,
            available ? Colors.White : PopColorRed, fontSlot: 0);

        // Population/count label: font slot 4, formatted "%4d / %4d".
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "population/count label … font slot 4, formatted %4d / %4d"
        AddRowLabel(loadText, plateX, RowLabelY2, RowLabelH, popColor, fontSlot: 4);
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
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        // Apply font slot override (slot 4 = DotumChe 12 w800 for population label).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 (slot 4 = DotumChe 12 w800)
        HudFont.ApplyToLabel(label, fontSlot);
        AddChild(label);
    }

    private int ClampPage(int requestedPage)
    {
        int pageCount = Math.Max(1, (_servers.Count + 1) / 2);
        return Math.Clamp(requestedPage, 0, pageCount - 1);
    }

    private string BuildPageBreadcrumb(int firstIndex, int visibleCount)
    {
        string plate0 = DescribePlate(0, firstIndex);
        string plate1 = visibleCount > 1 ? DescribePlate(1, firstIndex + 1) : "plate1=<none>";
        return $"[ServerSelectSubView] page {_page}: {plate0}, {plate1}";
    }

    private string DescribePlate(int plateSlot, int serverIndex)
    {
        ServerEntry e = _servers[serverIndex];
        string name = ResolveServerName(e);
        return $"plate{plateSlot}=server {e.ServerId} '{name}' load {e.Load} selectable={e.IsSelectable}";
    }

    private static Vector2 PanelPoint(int x, int y) => new(PanelX + x, PanelY + y);

    private string ResolveServerName(ServerEntry e)
    {
        // Display name resolves client-side from msg.xdb ids 5001..5040 (server_id N → id 5000+N).
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 "Name id = 5000 + server_id"
        if (e.ServerId is >= 1 and <= 40)
            return _text.GetCaption(ServerNameCaptionBase + e.ServerId, string.Empty);

        // Out-of-range fallback: msg 5901 (formatted). spec: §2.3
        string template = _text.GetCaption(LoginLayout.MsgServerUnknown, string.Empty);
        return FormatCaption(template, e.ServerId, string.Empty);
    }

    private string ResolveLoadText(ServerEntry e)
    {
        // spec: Docs/RE/specs/frontend_layout_tables.md §4 — load-bucket captions 6001/6002/6003
        if (e.StatusCode != 0)
            return e.Load.ToString();

        uint? msgId = e.Load > PopRedThreshold
            ? LoginLayout.MsgServerLoadRed
            : e.Load > PopOrangeThreshold
                ? LoginLayout.MsgServerLoadOrange
                : e.Load > PopYellowThreshold
                    ? LoginLayout.MsgServerLoadYellow
                    : null;

        return msgId is { } id
            ? _text.GetCaption(id, e.Load.ToString())
            : e.Load.ToString();
    }

    private string ResolveStatusText(ServerEntry e)
    {
        // spec: Docs/RE/packets/lobby.yaml Record Shape A — status_code == 3 is scheduled-open.
        if (e.StatusCode == 0)
            return e.IsSelectable ? string.Empty : _text.GetCaption(LoginLayout.MsgServerLoadRed, string.Empty);

        if (e.StatusCode == 3)
        {
            // Load == 24 is the "preparing" sentinel; any other Load is a valid hour.
            // spec: Docs/RE/specs/frontend_layout_tables.md §4
            if (e.Load == 24)
                return _text.GetCaption(LoginLayout.MsgServerPreparing, string.Empty);

            string template = _text.GetCaption(LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatCaption(template, e.Load, e.OpenTime, $"{e.Load:00}:{e.OpenTime:00}");
        }

        if (e.StatusCode is >= 1 and <= 4)
        {
            uint msgId = LoginLayout.MsgServerHeaderFirst + (uint)e.StatusCode - 1;
            return _text.GetCaption(msgId, string.Empty);
        }

        return string.Empty;
    }

    // Formats a caption template from msg.xdb that may use {0}/{0:00} or %02d placeholders.
    // Only used for status_code==3 HH:MM (MsgServerClockFormat) and the out-of-range server name fallback.
    private static string FormatCaption(string template, int value0, string fallback)
        => FormatCaption(template, value0, 0, fallback);

    private static string FormatCaption(string template, int value0, int value1, string fallback)
    {
        if (template.Length == 0) return fallback;
        try
        {
            if (template.Contains("{0", StringComparison.Ordinal))
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, value0, value1);

            if (template.Contains("%02d", StringComparison.Ordinal))
            {
                string s = ReplaceFirst(template, "%02d",
                    value0.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%02d",
                    value1.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (template.Contains("%d", StringComparison.Ordinal))
            {
                string s = ReplaceFirst(template, "%d",
                    value0.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return ReplaceFirst(s, "%d", value1.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        int idx = value.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? value : value[..idx] + newValue + value[(idx + oldValue.Length)..];
    }

    // -------------------------------------------------------------------------
    // Load colour. spec: Docs/RE/specs/frontend_layout_tables.md §4
    // -------------------------------------------------------------------------

    private static Color PopColorForLoad(int load)
    {
        if (load > PopRedThreshold) return PopColorRed;
        if (load > PopOrangeThreshold) return PopColorOrange;
        if (load > PopYellowThreshold) return PopColorYellow;
        return PopColorGreen;
    }
}