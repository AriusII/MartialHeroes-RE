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
// Background — BMP-tile mosaic (CONFIRMED, spec §3.1 / §3.1a / §3.2):
//   Path template: data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp
//   where {prefix} is the per-area map-area tag string (e.g. "002" for area 2),
//   {cellX}/{cellZ} are computed as:
//       cellX = ((int)(worldX + 20480.0) >> 10) + 9980
//       cellZ = ((int)(worldZ + 20480.0) >> 10) + 9980
//   The radar draws a 2×2 tile window (NOT 3×3) of 128-px tiles.
//   spec: Docs/RE/specs/minimap.md §3.1a CYCLE 7 CONFIRMED — "2×2 nested tile loop";
//         §2.5 CONFIRMED — "Corner-minimap window: 2×2 tiles = 256×256 px".
//
//   *** VERDICT (SAMPLE-VERIFIED §3.2): these BMP tiles are ABSENT from the entire VFS. ***
//   A full census of the 43,347-entry VFS found ZERO data/effect/map/d*.bmp files.
//   The null-safe handling here is CORRECT and INTENTIONAL — a blank radar is the faithful
//   reproduction of the original when the tile assets are not present.
//   Do NOT fabricate tiles; do NOT change the null-safe path.
//   spec: Docs/RE/specs/minimap.md §3.2 — "tiles absent from the entire VFS: SAMPLE-VERIFIED".
//
//   The real client falls back to a "default fill texture" on bind-failure → blank radar.
//   A faithful reimplementation would need to generate top-down cell thumbnails from terrain
//   .ted data, or render only live blips over a plain background.
//   spec: Docs/RE/specs/minimap.md §3.2 — "reimplementation consequence".
//
// Player marker: uitex texture-group id 13 (rotated player arrow).
//   spec: Docs/RE/specs/minimap.md §3.3 — blip id table: id 13 = local-player arrow 16×16.
//   (Note: the earlier "key 52" was the generic actor blip id, not the player-arrow id.)
//
// Actions (CODE-CONFIRMED):
//   5000 = sound + GPS toggle
//   5001 = collapse/expand (height 16 ↔ 195)
//   5002 = tooltip
//   5003 = open full map (TotalMapPanel)
//
// World→minimap pixel transform (CONFIRMED §2.2 / §2.2a):
//   px = worldX × 0.125 + 66.5    (1:8 scale; +66.5 = panel half-size = player at centre)
//   py = worldZ × 0.125 + 66.5
//   Cull: draw only if 0 ≤ px ≤ 133 AND 0 ≤ py ≤ 133.
//   spec: Docs/RE/specs/minimap.md §2.2 CONFIRMED.
//
// PASSIVE: drains IHudEventHub for ZoneChanges; no domain logic.
// TODO(world-campaign): player tile XZ from ActorMovedEvent for mosaic recentre.
//
// spec: Docs/RE/specs/minimap.md §2.2 / §3.1 / §3.1a / §3.2 / §3.3 CODE-CONFIRMED + sample-verified.
// spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED (placement).

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     Top-right corner radar minimap. Class MapPanel (RTTI-confirmed).
///     <para>
///         PASSIVE: assembles a 2×2 BMP-tile mosaic using the spec-confirmed cell formula
///         (§3.1a) from <c>data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp</c> at area-load time.
///         Zero game logic.
///     </para>
///     <para>
///         <b>VERDICT (SAMPLE-VERIFIED §3.2):</b> BMP minimap tiles are <b>absent from the entire shipped VFS</b>
///         (43,347 files, zero <c>data/effect/map/d*.bmp</c>). Null-safe handling is <b>correct and intentional</b>.
///         Blank radar = faithful reproduction of original client fallback. Do NOT fabricate tiles.
///         spec: Docs/RE/specs/minimap.md §3.2 SAMPLE-VERIFIED.
///     </para>
///     <para>
///         The old <c>data/ui/map/map%d.dds</c> path is INCORRECT — vestigial class, only map1.dds exists.
///         Player arrow uses texture-group id 13 (rotated 16×16 arrow).
///         spec: Docs/RE/specs/minimap.md §3.3.
///     </para>
///     spec: Docs/RE/specs/minimap.md §2.2/§3.1/§3.1a/§3.2/§3.3 CONFIRMED + SAMPLE-VERIFIED.
///     spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED (placement).
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

    // BMP tile mosaic path template.
    // Format: data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp
    // {prefix} = per-area area-tag string (e.g. "002" for area 2); NOT a D3-formatted integer.
    //   The tag is the same string that names the per-area binaries: map{tag}.bin, region{tag}.bin, etc.
    // {cellX},{cellZ} computed via the spec §3.1a cell formula (see LoadMosaic).
    // spec: Docs/RE/specs/minimap.md §3.1 CONFIRMED — path template "data/effect/map/d%sx%dz%d.bmp".
    // *** TILES ARE ABSENT FROM THE VFS — null-safe handling is correct. See §3.2 SAMPLE-VERIFIED. ***
    private const string BmpTilePattern = "data/effect/map/d{0}x{1}z{2}.bmp"; // spec: minimap.md §3.1

    // Each tile is 128 × 128 px (1024 world units × 0.125 scale = 128 px).
    // spec: Docs/RE/specs/minimap.md §2.5 CONFIRMED — "Minimap pixels per cell tile: 128".
    private const int TilePx = 128; // spec: minimap.md §2.5

    // Radar draws a 2×2 tile window (NOT 3×3 — confirmed §3.1a / §2.5).
    // spec: Docs/RE/specs/minimap.md §3.1a CYCLE 7 CONFIRMED — "2×2 nested tile loop".
    // spec: Docs/RE/specs/minimap.md §2.5 CONFIRMED — "Corner-minimap window: 2×2 tiles = 256×256 px".
    private const int MosaicDim = 2; // spec: minimap.md §3.1a — 2×2 tile window

    // Cell-index formula constants.
    // spec: Docs/RE/specs/minimap.md §3.1a CONFIRMED — operands byte-present.
    private const float CellBias = 20480f; // spec: minimap.md §3.1a — "(worldX + 20480.0) >> 10"
    private const int CellSize = 1024; // spec: minimap.md §2.5 — ">>10 = ÷1024 (world cell size)"
    private const int CellOrigin = 9980; // spec: minimap.md §3.1a — "+9980 global cell-index origin"

    // Player marker uitex texture-group id (local-player arrow, 16×16, rotated to heading).
    // spec: Docs/RE/specs/minimap.md §3.3 CONFIRMED — "Local-player arrow: 16×16, texture-group id 13".
    private const int PlayerMarkerGlyphKey = 13; // spec: minimap.md §3.3

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    // 2×2 grid of TextureRect tiles (row-major, [row][col]).
    // spec: Docs/RE/specs/minimap.md §3.1a — "2×2 nested tile loop".
    private readonly TextureRect[,] _mosaicTiles = new TextureRect[MosaicDim, MosaicDim];

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------

    private HudAtlasLibrary? _atlas;
    private bool _collapsed;
    private int _currentAreaId = 2; // default area 2 (town) matching the world default

    // Mosaic viewport container (clips tiles to the 133×133 body)
    private Control? _mosaicContainer;
    private Control? _playerBlip;

    // Current player world position for mosaic centring (world X and world Z).
    // Cell indices are computed from these via the §3.1a formula at LoadMosaic time.
    // spec: Docs/RE/specs/minimap.md §3.1a CONFIRMED — player pos read at actor +0x428 (worldX) / +0x430 (worldZ).
    // TODO(world-campaign): update _playerWorldX / _playerWorldZ from ActorMovedEvent.
    private float _playerWorldX;
    private float _playerWorldZ;
    private ChannelReader<ZoneChangedEvent>? _zoneChanges;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: positions the minimap at top-right, screen_width−135, Y=0, and builds
    ///     the 2×2 BMP-tile mosaic container.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED (placement).
    ///     spec: Docs/RE/specs/minimap.md §3.1/§3.1a — 2×2 BMP-tile mosaic, path and cell formula.
    ///     spec: Docs/RE/specs/minimap.md §3.2 SAMPLE-VERIFIED — tiles absent from VFS; null-safe is correct.
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
        var bodyInset = (MinimapW - BodyInnerSide) / 2f;
        _mosaicContainer = new Control
        {
            Name = "MosaicContainer",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore
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

        // Build 2×2 tile grid inside the mosaic container.
        // Each tile is TilePx × TilePx (128 px). The original draws a 256×256 px window
        // (2×128) and clips it to the 133 px body. We scale to fit inside the 133-px body:
        // 2 × 128 = 256 px of world tile → scaled to 133 px (port choice — display-only).
        // In the real engine each tile is full-res and the radar pans+clips them.
        // spec: Docs/RE/specs/minimap.md §3.1a — "2×2 nested tile loop; each tile 128 px".
        // spec: Docs/RE/specs/minimap.md §2.5 — "Corner-minimap window: 2×2 tiles = 256×256 px".
        var scaledTile = BodyInnerSide / MosaicDim; // 133/2 ≈ 66.5 px per tile at display scale
        for (var row = 0; row < MosaicDim; row++)
        for (var col = 0; col < MosaicDim; col++)
        {
            var tile = new TextureRect
            {
                Name = $"Tile{row}_{col}",
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(col * scaledTile, row * scaledTile),
                Size = new Vector2(scaledTile, scaledTile),
                MouseFilter = MouseFilterEnum.Ignore
                // Texture = null until LoadMosaic populates it
            };
            _mosaicContainer.AddChild(tile);
            _mosaicTiles[row, col] = tile;
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
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_playerBlip);

        // Collapse / expand toggle button (action 5001)
        // spec: ui_hud_layout.md §5.4 — "collapse/expand action 5001, height 16 ↔ 195"
        var collapseBtn = new Button
        {
            Name = "CollapseBtn",
            Text = "▲",
            CustomMinimumSize = new Vector2(MinimapW, 16f),
            MouseFilter = MouseFilterEnum.Stop
        };
        collapseBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        collapseBtn.OffsetBottom = CollapsedH;
        collapseBtn.Pressed += ToggleCollapse;
        AddChild(collapseBtn);

        // Load the initial BMP-tile mosaic for the default area + player world position.
        // NOTE: BMP minimap tiles are absent from the shipped VFS (SAMPLE-VERIFIED).
        // This call will produce all-null tiles — blank radar is the faithful reproduction.
        // spec: Docs/RE/specs/minimap.md §3.2 SAMPLE-VERIFIED.
        LoadMosaic(_currentAreaId, _playerWorldX, _playerWorldZ);

        GD.Print("[HudMinimapPanel] Built — top-right corner, 135×195, body 133×133, 2×2 BMP-tile mosaic. " +
                 "spec: Docs/RE/specs/minimap.md §3.1/§3.1a/§3.3 (placement: ui_hud_layout.md §3.3).");
        GD.Print("[HudMinimapPanel] VERDICT: BMP tiles data/effect/map/d*.bmp ABSENT from VFS " +
                 "(SAMPLE-VERIFIED, minimap.md §3.2). Blank radar is faithful reproduction. " +
                 "map%d.dds path is NOT used (vestigial class). spec: minimap.md §3.2.");
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

        while (_zoneChanges.TryRead(out var ev))
            if (ev is null)
                continue;
        // ZoneChangedEvent carries zone type (Safe/OpenPvp/Closed), not area id directly.
        // TODO(world-campaign): subscribe to an area-change event that carries the numeric
        // area id, then call LoadMosaic(newAreaId, playerTileX, playerTileZ).
        // spec: ui_hud_layout.md §5.4a — "area3 = zero-padded 3-digit current-area id"
    }

    // -------------------------------------------------------------------------
    // BMP-tile mosaic loader
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads the 2×2 BMP-tile window centred at world position (<paramref name="worldX" />,
    ///     <paramref name="worldZ" />) in area <paramref name="areaTag" /> and populates the
    ///     mosaic TextureRect grid.
    ///     <para>
    ///         Cell indices are computed using the spec-confirmed formula:
    ///         <c>cellX = ((int)(worldX + 20480.0) &gt;&gt; 10) + 9980</c>
    ///         <c>cellZ = ((int)(worldZ + 20480.0) &gt;&gt; 10) + 9980</c>
    ///         The 2×2 loop visits <c>{cellX, cellX+1} × {cellZ, cellZ+1}</c>.
    ///     </para>
    ///     <para>
    ///         <b>VERDICT (SAMPLE-VERIFIED):</b> these BMP tiles are <b>absent from the entire VFS</b>.
    ///         A full census of the 43,347-entry VFS found ZERO <c>data/effect/map/d*.bmp</c> files.
    ///         Missing tiles (null tex) are skipped gracefully — this is the correct faithful
    ///         reproduction of the original client's blank-radar fallback.
    ///         Do NOT fabricate tiles.
    ///         spec: Docs/RE/specs/minimap.md §3.2 SAMPLE-VERIFIED.
    ///     </para>
    ///     spec: Docs/RE/specs/minimap.md §3.1 — path template "d{prefix}x{cellX}z{cellZ}.bmp".
    ///     spec: Docs/RE/specs/minimap.md §3.1a CONFIRMED — 2×2 tile loop; cell formula operands byte-present.
    ///     spec: Docs/RE/specs/minimap.md §2.5 CONFIRMED — CellBias=20480, CellSize=1024, CellOrigin=9980.
    /// </summary>
    private void LoadMosaic(int areaId, float worldX, float worldZ)
    {
        if (_atlas is null || _mosaicContainer is null) return;

        // Area-tag string: the per-area loader tag (same tag used in map{tag}.bin, region{tag}.bin, etc.).
        // For area id N the tag is zero-padded to 3 digits: "002", "003", etc.
        // spec: Docs/RE/specs/minimap.md §3.7 — "map-area tag string, same tag for all four per-area binaries".
        // spec: Docs/RE/specs/minimap.md §3.1 — path uses %s format (string prefix, not integer).
        var areaTag = areaId.ToString("D3"); // e.g. area 2 → "002". spec: minimap.md §3.7

        // Compute base cell indices from world position.
        // spec: Docs/RE/specs/minimap.md §3.1a CONFIRMED — operands byte-present in IDB 263bd994.
        // cellX = ((int)(worldX + 20480.0) >> 10) + 9980
        // cellZ = ((int)(worldZ + 20480.0) >> 10) + 9980
        var baseCellX = (int)(worldX + CellBias) / CellSize + CellOrigin; // spec: minimap.md §3.1a
        var baseCellZ = (int)(worldZ + CellBias) / CellSize + CellOrigin; // spec: minimap.md §3.1a

        var anyLoaded = false;

        // 2×2 tile loop: {baseCellX, baseCellX+1} × {baseCellZ, baseCellZ+1}.
        // spec: Docs/RE/specs/minimap.md §3.1a — "2×2 nested tile loop".
        for (var row = 0; row < MosaicDim; row++)
        for (var col = 0; col < MosaicDim; col++)
        {
            var tx = baseCellX + col; // spec: minimap.md §3.1a
            var tz = baseCellZ + row; // spec: minimap.md §3.1a

            // spec: Docs/RE/specs/minimap.md §3.1 — "data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp"
            var path = string.Format(BmpTilePattern, areaTag, tx, tz);

            var tex = _atlas.GetByPath(path);
            _mosaicTiles[row, col].Texture = tex; // null-safe; renders black when null

            if (tex is not null)
            {
                anyLoaded = true;
                GD.Print($"[HudMinimapPanel] Tile loaded: {path}. " +
                         "spec: Docs/RE/specs/minimap.md §3.1.");
            }
            else
            {
                // EXPECTED: BMP minimap tiles are absent from the shipped VFS (SAMPLE-VERIFIED §3.2).
                // The original client falls back to a default fill texture → blank radar.
                // This null path is the correct faithful reproduction — do NOT fabricate tiles.
                // spec: Docs/RE/specs/minimap.md §3.2 SAMPLE-VERIFIED — "absent from entire VFS".
                GD.PrintErr($"[HudMinimapPanel] BMP tile '{path}' absent (EXPECTED — tiles absent from VFS " +
                            "per minimap.md §3.2 SAMPLE-VERIFIED). Blank tile rendered (faithful fallback).");
            }
        }

        if (!anyLoaded)
            GD.Print($"[HudMinimapPanel] No BMP tiles loaded for area {areaId} (tag={areaTag}) " +
                     $"world({worldX},{worldZ}) → cells ({baseCellX},{baseCellZ}).." +
                     $"({baseCellX + 1},{baseCellZ + 1}). " +
                     "EXPECTED — BMP tiles absent from VFS per minimap.md §3.2 SAMPLE-VERIFIED. " +
                     "Blank radar is the faithful reproduction.");
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