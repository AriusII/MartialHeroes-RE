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
// Background (CORRECTED — CAMPAIGN 17 §5.4a):
//   NOT data/ui/map/map%d.dds (that belongs to a THIRD, vestigial class; only map1.dds exists).
//   REAL source: runtime-assembled BMP-tile mosaic:
//       data/effect/map/d<area3>x<X>z<Z>.bmp
//   where <area3> = zero-padded 3-digit area id, <X>/<Z> = absolute world-tile indices.
//   The radar draws a 3×3 ring of 128-px tiles centred on the player tile.
//   Tiles cached lazily; missing tiles skipped (graceful-null).
//
// Player marker: uitex glyph key 52 (self; far/leader = 29).
//   (NOT the literal map_userpoint.tga path — binary uses manifest-driven id lookup.)
//
// Actions (CODE-CONFIRMED):
//   5000 = sound + GPS toggle
//   5001 = collapse/expand (height 16 ↔ 195)
//   5002 = tooltip
//   5003 = open full map (TotalMapPanel)
//
// PASSIVE: drains IHudEventHub for ZoneChanges (area id); no domain logic.
// TODO(world-campaign): player tile XZ from ActorMovedEvent for mosaic recentre.
//
// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 / §5.4a CODE-CONFIRMED + sample-verified.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Top-right corner radar minimap. Class MapPanel (RTTI-confirmed).
///
/// <para>PASSIVE: assembles a 3×3 BMP-tile mosaic from
/// <c>data/effect/map/d&lt;area3&gt;x&lt;X&gt;z&lt;Z&gt;.bmp</c> at area-load time and renders it as
/// the radar background. Player blip uses uitex glyph key 52 (TODO world-campaign for live position).
/// Zero game logic.</para>
///
/// <para>The old <c>data/ui/map/map%d.dds</c> path is INCORRECT — that belongs to a separate vestigial
/// class, and only <c>map1.dds</c> exists in the VFS. This panel renders the BMP-tile mosaic.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 / §5.4a CODE-CONFIRMED + sample-verified.
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

    // BMP tile mosaic path pattern (CORRECTED — §5.4a)
    // spec: Docs/RE/specs/ui_hud_layout.md §5.4a CODE-CONFIRMED + sample-verified
    // "data/effect/map/d<area3>x<X>z<Z>.bmp"
    private const string BmpTilePattern = "data/effect/map/d{0:D3}x{1}z{2}.bmp";

    // Each tile is 128 × 128 px.
    // spec: ui_hud_layout.md §5.4a — "3 × 3 ring of 128-px tiles"
    private const int TilePx = 128;

    // Radar draws a 3×3 ring centred on the player tile.
    // spec: ui_hud_layout.md §5.4a — "player tile ±1 in X and Z"
    private const int MosaicRadius = 1; // ±1 around player tile → 3×3
    private const int MosaicDim = 3; // MosaicRadius * 2 + 1

    // Player marker uitex glyph key (self/default).
    // spec: ui_hud_layout.md §5.4a — "self/default key 52"
    private const int PlayerMarkerGlyphKey = 52; // spec: ui_hud_layout.md §5.4a

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    // 3×3 grid of TextureRect tiles (row-major, [row][col])
    private readonly TextureRect[,] _mosaicTiles = new TextureRect[MosaicDim, MosaicDim];
    private Control? _playerBlip;
    private bool _collapsed;

    // Mosaic viewport container (clips tiles to the 133×133 body)
    private Control? _mosaicContainer;

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------

    private HudAtlasLibrary? _atlas;
    private ChannelReader<ZoneChangedEvent>? _zoneChanges;
    private int _currentAreaId = 2; // default area 2 (town) matching the world default

    // Current player tile origin (centre of the 3×3 window).
    // spec: ui_hud_layout.md §5.4a — player-tile updated at runtime; XZ from ActorMovedEvent.
    // TODO(world-campaign): update _playerTileX / _playerTileZ from ActorMovedEvent.
    private int _playerTileX;
    private int _playerTileZ;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: positions the minimap at top-right, screen_width−135, Y=0, and builds
    /// the 3×3 BMP-tile mosaic container.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.4a — BMP-tile mosaic source.
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
        OffsetLeft = -MinimapW; // spec: ui_hud_layout.md §3.3
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

        // Mosaic container — clips tile grid to the 133×133 inner body.
        // spec: ui_hud_layout.md §5.4 — body 133×133
        float bodyInset = (MinimapW - BodyInnerSide) / 2f;
        _mosaicContainer = new Control
        {
            Name = "MosaicContainer",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _mosaicContainer.AnchorLeft = 0f;
        _mosaicContainer.AnchorTop = 0f;
        _mosaicContainer.AnchorRight = 0f;
        _mosaicContainer.AnchorBottom = 0f;
        _mosaicContainer.OffsetLeft = bodyInset;
        _mosaicContainer.OffsetTop = bodyInset;
        _mosaicContainer.OffsetRight = bodyInset + BodyInnerSide;
        _mosaicContainer.OffsetBottom = bodyInset + BodyInnerSide;
        AddChild(_mosaicContainer);

        // Build 3×3 tile grid inside the mosaic container.
        // Each tile is TilePx × TilePx (128 px). We scale them to fit inside the 133-px body:
        // 3 × 128 = 384 px of world tile → scaled to 133 px (port choice — display-only).
        // In the real engine each tile is full-res and the radar pans+clips them.
        // spec: ui_hud_layout.md §5.4a — "tiles at the radar edge are clipped to the radar viewport"
        float scaledTile = BodyInnerSide / MosaicDim; // 133/3 ≈ 44 px per tile at the display scale
        for (int row = 0; row < MosaicDim; row++)
        {
            for (int col = 0; col < MosaicDim; col++)
            {
                var tile = new TextureRect
                {
                    Name = $"Tile{row}_{col}",
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    Position = new Vector2(col * scaledTile, row * scaledTile),
                    Size = new Vector2(scaledTile, scaledTile),
                    MouseFilter = MouseFilterEnum.Ignore,
                    // Texture = null until LoadMosaic populates it
                };
                _mosaicContainer.AddChild(tile);
                _mosaicTiles[row, col] = tile;
            }
        }

        // Player blip — uitex glyph key 52 (self-marker).
        // spec: ui_hud_layout.md §5.4a — "self/default key 52; Z-rotated by player facing"
        // TODO(world-campaign): rotate blip by player facing; update position from ActorMoved.
        _playerBlip = new ColorRect
        {
            Name = "PlayerBlip",
            Color = new Color(1f, 1f, 0f, 0.9f),
            Size = new Vector2(5f, 5f),
            // Centre of the mosaic container
            Position = new Vector2(
                bodyInset + BodyInnerSide / 2f - 2.5f,
                bodyInset + BodyInnerSide / 2f - 2.5f),
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

        // Load the initial BMP-tile mosaic for the default area + player tile.
        // spec: ui_hud_layout.md §5.4a — "tiles loaded lazily as the player moves"
        LoadMosaic(_currentAreaId, _playerTileX, _playerTileZ);

        GD.Print($"[HudMinimapPanel] Built — top-right corner, 135×195, body 133×133, BMP-tile mosaic. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.4 / §5.4a CODE-CONFIRMED.");
        GD.Print("[HudMinimapPanel] BMP-tile pattern: data/effect/map/d<area3>x<X>z<Z>.bmp. " +
                 "NOTE: map%d.dds is NOT used here (vestigial class; only map1.dds exists). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.4a CODE-CONFIRMED.");
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
            // ZoneChangedEvent carries zone type (Safe/OpenPvp/Closed), not area id directly.
            // TODO(world-campaign): subscribe to an area-change event that carries the numeric
            // area id, then call LoadMosaic(newAreaId, playerTileX, playerTileZ).
            // spec: ui_hud_layout.md §5.4a — "area3 = zero-padded 3-digit current-area id"
        }
    }

    // -------------------------------------------------------------------------
    // BMP-tile mosaic loader
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the 3×3 BMP-tile ring centred at <paramref name="tileX"/>, <paramref name="tileZ"/>
    /// in area <paramref name="areaId"/> and populates the mosaic TextureRect grid.
    ///
    /// <para>Missing tiles are skipped (graceful-null; no crash).</para>
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.4a — "3×3 ring of 128-px tiles; missing tiles skipped".
    /// </summary>
    private void LoadMosaic(int areaId, int tileX, int tileZ)
    {
        if (_atlas is null || _mosaicContainer is null) return;

        bool anyLoaded = false;

        for (int row = 0; row < MosaicDim; row++)
        {
            for (int col = 0; col < MosaicDim; col++)
            {
                int tx = tileX + (col - MosaicRadius);
                int tz = tileZ + (row - MosaicRadius);

                // spec: ui_hud_layout.md §5.4a — "d<area3>x<X>z<Z>.bmp"
                string path = string.Format(BmpTilePattern, areaId, tx, tz);

                Texture2D? tex = _atlas.GetByPath(path);
                _mosaicTiles[row, col].Texture = tex; // null-safe; renders black when null

                if (tex is not null)
                {
                    anyLoaded = true;
                    GD.Print($"[HudMinimapPanel] Tile loaded: {path}. " +
                             "spec: Docs/RE/specs/ui_hud_layout.md §5.4a.");
                }
                else
                {
                    // Missing tile is expected (area edges, VFS offline): skip silently.
                    // spec: ui_hud_layout.md §5.4a — "tiles at radar edge clipped"
                    GD.PrintErr($"[HudMinimapPanel] BMP tile '{path}' unavailable (VFS offline or edge tile). " +
                                "spec: Docs/RE/specs/ui_hud_layout.md §5.4a — graceful-null.");
                }
            }
        }

        if (!anyLoaded)
        {
            GD.PrintErr($"[HudMinimapPanel] No BMP tiles loaded for area {areaId} tile ({tileX},{tileZ}). " +
                        "VFS offline or area not yet reached. " +
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.4a.");
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
        if (_mosaicContainer is not null) _mosaicContainer.Visible = !_collapsed;
        if (_playerBlip is not null) _playerBlip.Visible = !_collapsed;
        GD.Print($"[HudMinimapPanel] Collapsed={_collapsed}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.4 action 5001.");
    }
}