// Ui/Hud/HudMinimapPanel.cs
//
// Minimap corner radar — MapPanel (class RTTI-confirmed).
//
// Placement (CODE-CONFIRMED call-site re-pinned):
//   X = screen_width − 135, Y = 0, W = 135, H = 195.
//   Collapsed height = 16 (toggle action 5001).
//   Body inner = 133 × 133.
//
// In Godot terms: AnchorLeft=1, AnchorRight=1 (top-right corner).
//   OffsetLeft = −135, OffsetRight = 0.
//   OffsetTop = 0, OffsetBottom = 195.
//
// Background texture: per-area data/ui/map/map%d.dds, keyed by current-area id.
// Player marker: id-keyed glyph from uitex registry; drawn as Z-rotated blip.
//   (NOT the literal map_userpoint.tga path — the binary uses manifest-driven id lookup.)
//   spec: ui_hud_layout.md §5.4 — corrected minimap marker note.
//
// Actions (CODE-CONFIRMED):
//   5000 = sound + GPS toggle
//   5001 = collapse/expand (height 16 ↔ 195)
//   5002 = tooltip
//   5003 = open full map (TotalMapPanel)
//
// PASSIVE: drains IHudEventHub for ZoneChanges (area id); no domain logic.
// TODO(world-campaign): player blip position from ActorMovedEvent.
//
// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Top-right corner radar minimap. Class MapPanel (RTTI-confirmed).
///
/// <para>PASSIVE: renders the per-area map DDS as background. Player blip position wired via
/// world-campaign follow-up. Zero game logic.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudMinimapPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float MinimapW = 135f; // spec: ui_hud_layout.md §3.3
    private const float MinimapH = 195f; // spec: ui_hud_layout.md §3.3 (full height)
    private const float CollapsedH = 16f; // spec: ui_hud_layout.md §5.4 — collapsed height
    private const float BodyInnerSide = 133f; // spec: ui_hud_layout.md §5.4 — inner body 133×133

    // Per-area map DDS path pattern.
    // spec: ui_hud_layout.md §5.4 — "data/ui/map/map%d.dds keyed by current-area id".
    private const string MapDdsPattern = "data/ui/map/map{0}.dds"; // spec: ui_hud_layout.md §5.4

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private TextureRect _mapBg = null!;
    private Control _playerBlip = null!;
    private bool _collapsed;

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------

    private HudAtlasLibrary? _atlas;
    private ChannelReader<ZoneChangedEvent>? _zoneChanges;
    private int _currentAreaId = 2; // default area 2 (town) matching the world default

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: positions the minimap at top-right, screen_width−135, Y=0.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudMinimapPanel";
        _atlas = atlas;

        // Top-right corner anchor
        // spec: ui_hud_layout.md §3.3 — "screen_width−135, Y=0"
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -MinimapW;  // spec: ui_hud_layout.md §3.3
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = MinimapH; // spec: ui_hud_layout.md §3.3
        MouseFilter = MouseFilterEnum.Stop;

        // Border / chrome panel
        var frame = new Panel { Name = "Frame" };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var frameStyle = new StyleBoxFlat();
        frameStyle.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.85f);
        frameStyle.SetBorderWidthAll(1);
        frameStyle.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.9f);
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        AddChild(frame);

        // Map background image (inner body 133×133, centred in the 135×195 panel)
        // spec: ui_hud_layout.md §5.4 — body 133×133
        _mapBg = new TextureRect
        {
            Name = "MapBg",
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // Manual anchor+offset (LayoutPreset.Custom not available in Godot 4.6.3)
        float bodyInset = (MinimapW - BodyInnerSide) / 2f;
        _mapBg.AnchorLeft = 0f;
        _mapBg.AnchorTop = 0f;
        _mapBg.AnchorRight = 0f;
        _mapBg.AnchorBottom = 0f;
        _mapBg.OffsetLeft = bodyInset;
        _mapBg.OffsetTop = bodyInset;
        _mapBg.OffsetRight = bodyInset + BodyInnerSide;
        _mapBg.OffsetBottom = bodyInset + BodyInnerSide;
        AddChild(_mapBg);
        LoadMapTexture(_currentAreaId);

        // Player blip — simple dot marker (placeholder; real blip = id-keyed glyph from uitex)
        // spec: ui_hud_layout.md §5.4 — "player marker = manifest-driven blip glyph, Z-rotation"
        // TODO(world-campaign): rotate blip by player facing quaternion; update position from ActorMoved.
        _playerBlip = new ColorRect
        {
            Name = "PlayerBlip",
            Color = new Color(1f, 1f, 0f, 0.9f),
            Size = new Vector2(5f, 5f),
            Position = new Vector2(MinimapW / 2f, MinimapH / 2f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_playerBlip);

        // Collapse / expand toggle button (action 5001)
        // spec: ui_hud_layout.md §5.4 — "collapse/expand action 5001, height 16 ↔ 195"
        var collapseBtn = new Button
        {
            Name = "CollapseBtn",
            Text = "▲",
            CustomMinimumSize = new Vector2(MinimapW, 16f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        collapseBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        collapseBtn.OffsetBottom = CollapsedH;
        collapseBtn.Pressed += ToggleCollapse;
        AddChild(collapseBtn);

        GD.Print($"[HudMinimapPanel] Built — top-right corner, 135×195, body 133×133. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds to the HUD event hub's ZoneChanges channel.</summary>
    public void BindHub(IHudEventHub hub)
    {
        _zoneChanges = hub.ZoneChanges;
        GD.Print("[HudMinimapPanel] BindHub: ZoneChanges channel connected.");
    }

    public override void _Process(double delta)
    {
        if (_zoneChanges is null) return;

        while (_zoneChanges.TryRead(out ZoneChangedEvent? ev))
        {
            if (ev is null) continue;
            // ZoneChangedEvent carries the zone type; area id is a world-side concept.
            // TODO(world-campaign): listen for area-change event to reload map DDS.
        }
    }

    // -------------------------------------------------------------------------
    // Map texture loading
    // -------------------------------------------------------------------------

    private void LoadMapTexture(int areaId)
    {
        if (_atlas is null) return;

        string path = string.Format(MapDdsPattern, areaId);
        Texture2D? tex = _atlas.GetByPath(path);
        if (tex is not null)
        {
            _mapBg.Texture = tex;
            GD.Print($"[HudMinimapPanel] Map texture loaded: {path}. " +
                     "spec: Docs/RE/specs/ui_hud_layout.md §5.4.");
        }
        else
        {
            GD.PrintErr($"[HudMinimapPanel] Map texture '{path}' unavailable (VFS offline or not found). " +
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.4 — per-area map%d.dds.");
        }
    }

    // -------------------------------------------------------------------------
    // Collapse/expand
    // -------------------------------------------------------------------------

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        // spec: ui_hud_layout.md §5.4 — "action 5001 toggles height 16 ↔ 195, width stays 135"
        OffsetBottom = _collapsed ? CollapsedH : MinimapH;
        _mapBg.Visible = !_collapsed;
        _playerBlip.Visible = !_collapsed;
        GD.Print($"[HudMinimapPanel] Collapsed={_collapsed}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.4 action 5001.");
    }
}
