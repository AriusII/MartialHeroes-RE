using System.Globalization;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class ServerSelectSubView : Control
{
    [Signal]
    public delegate void ServerSelectedEventHandler(int serverId);


    private const string AtlasA2 = "data/ui/loginwindow.dds";
    private const string AtlasA4 = "data/ui/loginwindow_02.dds";


    private const int PanelX = 270;
    private const int PanelY = 85;


    private const int TitleLocalX = 207;
    private const int TitleLocalY = 44;
    private const int TitleSrcX = 0;
    private const int TitleSrcY = 980;


    private const int PlateBaseX0 = 30;
    private const int PlateBaseX1 = 263;
    private const int PlateY = 97;

    private const int PlateSelectOffX = -6;
    private const int PlateSelectW = 202;
    private const int PlateSelectH = 372;
    private const int PlateNSrcX = 9;
    private const int PlateNSrcY = 6;
    private const int PlateHSrcX = 220;
    private const int PlateHSrcY = 6;

    private const int PlateFaceOffX = 47;
    private const int PlateFaceW = 100;
    private const int PlateFaceH = 372;
    private const int PlateFaceSrcX0 = 448;
    private const int PlateFaceSrcX1 = 572;
    private const int PlateFaceSrcY = 6;

    private const int NameLabelLocalY = 390;
    private const int NameLabelW = 174;
    private const int NameLabelH = 21;

    private const int StatusLabelLocalY = 410;
    private const int StatusLabelW = 174;
    private const int StatusLabelH = 20;

    private const int SpareLabelLocalY = 430;

    private const int ActionPlate0 = 400;
    private const int ActionPlate1 = 401;


    private const int PagerBaseX = 13;
    private const int PagerStrideX = 47;
    private const int PagerLocalY = 66;
    private const int PagerW = 47;
    private const int PagerH = 18;
    private const int PagerCount = 10;
    private const int PagerActionBase = 115;

    private const int PagerBuildNSrcX = 596;
    private const int PagerBuildNSrcY = 985;
    private const int PagerBuildHSrcX = 643;
    private const int PagerBuildHSrcY = 985;

    private const int PagerBlankNSrcX = 500;
    private const int PagerBlankNSrcY = 792;
    private const int PagerBlankHSrcX = 500;
    private const int PagerBlankHSrcY = 810;

    private const int Pager1NSrcX = 500;
    private const int Pager1NSrcY = 828;
    private const int Pager1HSrcX = 500;
    private const int Pager1HSrcY = 846;
    private const int Pager2NSrcX = 500;
    private const int Pager2NSrcY = 864;
    private const int Pager2HSrcX = 605;
    private const int Pager2HSrcY = 985;
    private const int Pager3NSrcX = 710;
    private const int Pager3NSrcY = 985;
    private const int Pager3HSrcX = 815;
    private const int Pager3HSrcY = 985;


    private const int HighlightSrcX = 700;
    private const int HighlightSrcY = 18;
    private const int HighlightW = 46;
    private const int HighlightH = 168;


    private const int SpecialRowServerId = 100;


    private const int ServerNameMsgBase = 5000;

    private const int StatusCaptionMsgBase = 4029;

    private const int PopThreshRed = 1200;
    private const int PopThreshOrange = 800;
    private const int PopThreshYellow = 500;

    private const int PopLevelRed = 4;
    private const int PopLevelOrange = 3;
    private const int PopLevelYellow = 2;

    private static readonly Color PopColorRed = Color.Color8(255, 0, 0);
    private static readonly Color PopColorOrange = Color.Color8(237, 104, 6);
    private static readonly Color PopColorYellow = Color.Color8(255, 255, 0);
    private static readonly Color PopColorGreen = Color.Color8(181, 255, 122);


    private readonly HudAtlasLibrary _atlas;

    private readonly TextureButton?[] _pagerTabs = new TextureButton?[PagerCount];

    private readonly TextureRect?[] _statusIndicators = new TextureRect?[3];
    private readonly HudTextLibrary _text;

    private bool _loading = true;
    private int _page;
    private IReadOnlyList<ServerListEntryView> _servers = [];


    public ServerSelectSubView(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
    }

    public int NewServerIndex { get; set; } = -1;

    public int LastServerId
    {
        get => NewServerIndex;
        set => NewServerIndex = value;
    }


    public void SetServers(IReadOnlyList<ServerListEntryView> servers)
    {
        _loading = false;
        _servers = servers;
        _page = 0;
        RebuildLayout();
    }


    private void RebuildLayout()
    {
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

        BuildTitle();
        BuildStatusIndicators();
        BuildPagerTabs();
        RebuildVisiblePage();
    }


    private void BuildTitle()
    {
        var tex = _atlas.SliceByPath(
            AtlasA2,
            TitleSrcX, TitleSrcY,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H);
        if (tex is null) return;

        AddChild(new TextureRect
        {
            Position = LocalToCanvas(TitleLocalX, TitleLocalY),
            Size = new Vector2(LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H),
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }


    private void BuildStatusIndicators()
    {
        var tex = _atlas.SliceByPath(
            AtlasA2,
            LoginLayout.StatusIndicatorSrcX, LoginLayout.StatusIndicatorSrcY,
            LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH);

        for (var i = 0; i < LoginLayout.StatusIndicatorCount; i++)
        {
            var rect = new TextureRect
            {
                Texture = tex,
                Size = new Vector2(LoginLayout.StatusIndicatorW, LoginLayout.StatusIndicatorH),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false
            };
            _statusIndicators[i] = rect;
            AddChild(rect);
        }
    }


    private void BuildPagerTabs()
    {
        for (var i = 0; i < PagerCount; i++)
        {
            var localX = PagerBaseX + i * PagerStrideX;
            var actionId = PagerActionBase + i;

            var buildN = _atlas.SliceByPath(AtlasA2, PagerBuildNSrcX, PagerBuildNSrcY, PagerW, PagerH);
            var buildH = _atlas.SliceByPath(AtlasA2, PagerBuildHSrcX, PagerBuildHSrcY, PagerW, PagerH);

            var btn = new TextureButton
            {
                Position = LocalToCanvas(localX, PagerLocalY),
                Size = new Vector2(PagerW, PagerH),
                CustomMinimumSize = new Vector2(PagerW, PagerH),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                TextureNormal = buildN,
                TextureHover = buildH,
                TexturePressed = buildH,
                Visible = false
            };

            var captured = actionId;
            btn.Pressed += () => OnPagerClicked(captured);
            _pagerTabs[i] = btn;
            AddChild(btn);
        }
    }


    private void RebuildVisiblePage()
    {
        _page = ClampPage(_page);

        foreach (var ind in _statusIndicators)
            if (ind is not null)
                ind.Visible = false;

        RearmPagerTabs();

        if (_loading)
        {
            GD.Print("[ServerSelectSubView] state 35 — loading server list (waiting for SetServers).");
            return;
        }

        if (_servers.Count == 0)
        {
            GD.Print($"[ServerSelectSubView] state 36 error — no servers (msg {LoginLayout.MsgErrNoServers}).");
            return;
        }

        var firstIdx = _page * 2;
        var visibleCount = Math.Min(2, _servers.Count - firstIdx);

        int[] plateX = [PlateBaseX0, PlateBaseX1];
        int[] faceSrcX = [PlateFaceSrcX0, PlateFaceSrcX1];
        int[] actions = [ActionPlate0, ActionPlate1];

        for (var slot = 0; slot < visibleCount; slot++)
        {
            var idx = firstIdx + slot;

            if (NewServerIndex >= 0 && _servers[idx].ServerId == NewServerIndex)
                BuildSelectionHighlight(plateX[slot], PlateY);

            BuildPlate(plateX[slot], PlateY, actions[slot], idx, faceSrcX[slot]);

            if (_servers[idx].ServerId == SpecialRowServerId)
                AnchorStatusIndicators(plateX[slot], PlateY);
        }

        GD.Print(BuildPageBreadcrumb(firstIdx, visibleCount));
    }


    private void RearmPagerTabs()
    {
        if (_servers.Count == 0 || _loading)
        {
            foreach (var t in _pagerTabs)
                if (t is not null)
                    t.Visible = false;
            return;
        }

        var pageCount = (_servers.Count + 1) / 2;

        for (var i = 0; i < PagerCount; i++)
        {
            var tab = _pagerTabs[i];
            if (tab is null) continue;

            var blankN = _atlas.SliceByPath(AtlasA2, PagerBlankNSrcX, PagerBlankNSrcY, PagerW, PagerH);
            var blankH = _atlas.SliceByPath(AtlasA2, PagerBlankHSrcX, PagerBlankHSrcY, PagerW, PagerH);
            tab.TextureNormal = blankN;
            tab.TextureHover = blankH;
            tab.TexturePressed = blankH;

            switch (i)
            {
                case 1:
                    tab.TextureNormal = _atlas.SliceByPath(AtlasA2, Pager1NSrcX, Pager1NSrcY, PagerW, PagerH);
                    tab.TextureHover = _atlas.SliceByPath(AtlasA2, Pager1HSrcX, Pager1HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
                case 2:
                    tab.TextureNormal = _atlas.SliceByPath(AtlasA2, Pager2NSrcX, Pager2NSrcY, PagerW, PagerH);
                    tab.TextureHover = _atlas.SliceByPath(AtlasA2, Pager2HSrcX, Pager2HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
                case 3:
                    tab.TextureNormal = _atlas.SliceByPath(AtlasA2, Pager3NSrcX, Pager3NSrcY, PagerW, PagerH);
                    tab.TextureHover = _atlas.SliceByPath(AtlasA2, Pager3HSrcX, Pager3HSrcY, PagerW, PagerH);
                    tab.TexturePressed = tab.TextureHover;
                    break;
            }

            tab.Visible = i < pageCount;
        }
    }


    private void BuildPlate(int x, int y, int actionId, int serverIndex, int faceSrcX)
    {
        var e = _servers[serverIndex];


        var btnN = _atlas.SliceByPath(AtlasA4, PlateNSrcX, PlateNSrcY, PlateSelectW, PlateSelectH);
        var btnH = _atlas.SliceByPath(AtlasA4, PlateHSrcX, PlateHSrcY, PlateSelectW, PlateSelectH);
        var btn = new TextureButton
        {
            Position = LocalToCanvas(x + PlateSelectOffX, y),
            Size = new Vector2(PlateSelectW, PlateSelectH),
            CustomMinimumSize = new Vector2(PlateSelectW, PlateSelectH),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = btnN,
            TextureHover = btnH,
            TexturePressed = btnH,
            TextureDisabled = btnN,
            Disabled = e.StatusCode != 0
        };
        btn.Pressed += () => OnPlateClicked(actionId);
        AddChild(btn);

        AddRowLabel(ResolveServerName(e), x, NameLabelLocalY, NameLabelW, NameLabelH, Colors.White);

        var face = _atlas.SliceByPath(AtlasA4, faceSrcX, PlateFaceSrcY, PlateFaceW, PlateFaceH);
        if (face is not null)
            AddChild(new TextureRect
            {
                Position = LocalToCanvas(x + PlateFaceOffX, y),
                Size = new Vector2(PlateFaceW, PlateFaceH),
                Texture = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });

        var statusText = ResolveStatusCaption(e, out var statusColor);
        AddRowLabel(statusText, x, StatusLabelLocalY, StatusLabelW, StatusLabelH, statusColor, 4);

        AddRowLabel(string.Empty, x, SpareLabelLocalY, StatusLabelW, StatusLabelH, Colors.White);
    }


    private void BuildSelectionHighlight(int plateLocalX, int plateLocalY)
    {
        var tex = _atlas.SliceByPath(AtlasA4, HighlightSrcX, HighlightSrcY, HighlightW, HighlightH);
        if (tex is null) return;

        var hx = plateLocalX + PlateSelectOffX;
        var hy = plateLocalY + (PlateSelectH - HighlightH) / 2;
        AddChild(new TextureRect
        {
            Position = LocalToCanvas(hx, hy),
            Size = new Vector2(HighlightW, HighlightH),
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }


    private void AnchorStatusIndicators(int anchorX, int anchorY)
    {
        if (_statusIndicators[0] is { } ind0)
        {
            ind0.Position = LocalToCanvas(anchorX - 30, anchorY - 13);
            ind0.Visible = true;
        }

        for (var i = 1; i <= 2; i++)
            if (_statusIndicators[i] is { } ind)
            {
                ind.Position = LocalToCanvas(anchorX + 139, anchorY + 13);
                ind.Visible = true;
            }
    }


    private void AddRowLabel(string text, int localX, int localY, int w, int h, Color color,
        int fontSlot = 0, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var lbl = new Label
        {
            Text = text,
            Position = LocalToCanvas(localX, localY),
            Size = new Vector2(w, h),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore
        };
        lbl.AddThemeColorOverride("font_color", color);
        HudFont.ApplyToLabel(lbl, fontSlot);
        AddChild(lbl);
    }


    private string ResolveServerName(ServerListEntryView e)
    {
        if (e.ServerId is >= 1 and <= 40)
            return _text.GetCaption(ServerNameMsgBase + e.ServerId, string.Empty);

        var tmpl = _text.GetCaption(LoginLayout.MsgServerUnknown, string.Empty);
        return FormatCaption(tmpl, e.ServerId, string.Empty);
    }


    private string ResolveStatusCaption(ServerListEntryView e, out Color color)
    {
        var loadValid = e.OpenTime != 0;

        if (e.StatusCode == 0)
        {
            if (loadValid)
            {
                if (e.Load > PopThreshRed)
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load > PopThreshOrange)
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load > PopThreshYellow)
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }
            else
            {
                if (e.Load == PopLevelRed)
                {
                    color = PopColorRed;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadRed, string.Empty);
                }

                if (e.Load == PopLevelOrange)
                {
                    color = PopColorOrange;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadOrange, string.Empty);
                }

                if (e.Load == PopLevelYellow)
                {
                    color = PopColorYellow;
                    return _text.GetCaption((int)LoginLayout.MsgServerLoadYellow, string.Empty);
                }
            }

            color = PopColorGreen;
            return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty);
        }

        if (e.StatusCode == 3)
        {
            color = Colors.White;
            if (e.Load == 24)
                return _text.GetCaption((int)LoginLayout.MsgServerPreparing, string.Empty);

            var tmpl = _text.GetCaption((int)LoginLayout.MsgServerClockFormat, "{0:00}:{1:00}");
            return FormatScheduledTime(tmpl, e.Load, e.OpenTime);
        }

        color = Colors.White;
        return _text.GetCaption(StatusCaptionMsgBase + e.StatusCode, string.Empty);
    }


    private void OnPlateClicked(int actionId)
    {
        var idx = 2 * _page + (actionId - ActionPlate0);
        if (idx < 0 || idx >= _servers.Count) return;

        var entry = _servers[idx];

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
        _page = ClampPage(actionId - PagerActionBase);
        GD.Print($"[ServerSelectSubView] Pager action {actionId} → page {_page}. spec: §4.3.3.");
        RebuildLayout();
    }


    private int ClampPage(int requested)
    {
        var pageCount = Math.Max(1, (_servers.Count + 1) / 2);
        return Math.Clamp(requested, 0, pageCount - 1);
    }

    private static Vector2 LocalToCanvas(int localX, int localY)
    {
        return new Vector2(PanelX + localX, PanelY + localY);
    }

    private string BuildPageBreadcrumb(int firstIdx, int visibleCount)
    {
        var sb = $"[ServerSelectSubView] page {_page}: ";
        for (var slot = 0; slot < visibleCount; slot++)
        {
            var e = _servers[firstIdx + slot];
            var name = ResolveServerName(e);
            sb += $"plate{slot}=srv{e.ServerId}'{name}' load={e.Load} sel={e.IsSelectable}; ";
        }

        if (visibleCount < 2) sb += "plate1=<none>;";
        return sb.TrimEnd();
    }


    private static string FormatScheduledTime(string template, int hour, int minute)
    {
        int hh = hour / 10 % 10, hl = hour % 10;
        int mh = minute / 10 % 10, ml = minute % 10;
        var fallback = $"{hour:00}:{minute:00}";

        if (template.Length == 0) return fallback;

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
        {
            count++;
            idx += token.Length;
        }

        return count;
    }

    private static string FormatCaption(string template, int v0, string fallback)
    {
        return FormatCaption(template, v0, 0, fallback);
    }

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
        catch (FormatException)
        {
            return fallback;
        }

        return template.Length > 0 ? template : fallback;
    }

    private static string ReplaceFirst(string value, string old, string replacement)
    {
        var idx = value.IndexOf(old, StringComparison.Ordinal);
        return idx < 0 ? value : value[..idx] + replacement + value[(idx + old.Length)..];
    }
}