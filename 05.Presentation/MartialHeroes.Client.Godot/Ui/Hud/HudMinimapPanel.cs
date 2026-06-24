using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMinimapPanel : Control
{
    private const float MinimapW = 135f;
    private const float MinimapH = 195f;
    private const float CollapsedH = 16f;
    private const float BodyInnerSide = 133f;

    private const string BmpTilePattern = "data/effect/map/d{0}x{1}z{2}.bmp";

    private const int TilePx = 128;

    private const int MosaicDim = 2;

    private const float CellBias = 20480f;
    private const int CellSize = 1024;
    private const int CellOrigin = 9980;

    private const int PlayerMarkerGlyphKey = 13;


    private readonly TextureRect[,] _mosaicTiles = new TextureRect[MosaicDim, MosaicDim];


    private HudAtlasLibrary? _atlas;
    private bool _collapsed;
    private int _currentAreaId = 2;

    private Control? _mosaicContainer;
    private Control? _playerBlip;

    private float _playerWorldX;
    private float _playerWorldZ;
    private ChannelReader<ZoneChangedEvent>? _zoneChanges;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudMinimapPanel";
        _atlas = atlas;

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -MinimapW;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = MinimapH;
        MouseFilter = MouseFilterEnum.Stop;

        var frame = new Panel { Name = "Frame" };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var frameStyle = new StyleBoxFlat();
        frameStyle.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.85f);
        frameStyle.SetBorderWidthAll(1);
        frameStyle.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.9f);
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        AddChild(frame);

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

        var scaledTile = BodyInnerSide / MosaicDim;
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
            };
            _mosaicContainer.AddChild(tile);
            _mosaicTiles[row, col] = tile;
        }

        _playerBlip = new ColorRect
        {
            Name = "PlayerBlip",
            Color = new Color(1f, 1f, 0f, 0.9f),
            Size = new Vector2(5f, 5f),
            Position = new Vector2(
                bodyInset + BodyInnerSide / 2f - 2.5f,
                bodyInset + BodyInnerSide / 2f - 2.5f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_playerBlip);

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

        LoadMosaic(_currentAreaId, _playerWorldX, _playerWorldZ);

        GD.Print("[HudMinimapPanel] Built — top-right corner, 135×195, body 133×133, 2×2 BMP-tile mosaic. " +
                 "spec: Docs/RE/specs/minimap.md §3.1/§3.1a/§3.3 (placement: ui_hud_layout.md §3.3).");
        GD.Print("[HudMinimapPanel] VERDICT: BMP tiles data/effect/map/d*.bmp ABSENT from VFS " +
                 "(SAMPLE-VERIFIED, minimap.md §3.2). Blank radar is faithful reproduction. " +
                 "map%d.dds path is NOT used (vestigial class). spec: minimap.md §3.2.");
    }


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
    }


    public void OnSectorLoaded(int mapX, int mapZ)
    {
        LoadMosaic(_currentAreaId, _playerWorldX, _playerWorldZ);
    }

    public void OnSectorUnloaded(int mapX, int mapZ)
    {
        LoadMosaic(_currentAreaId, _playerWorldX, _playerWorldZ);
    }

    private void LoadMosaic(int areaId, float worldX, float worldZ)
    {
        if (_atlas is null || _mosaicContainer is null) return;

        var areaTag = areaId.ToString("D3");

        var baseCellX = (int)(worldX + CellBias) / CellSize + CellOrigin;
        var baseCellZ = (int)(worldZ + CellBias) / CellSize + CellOrigin;

        var anyLoaded = false;

        for (var row = 0; row < MosaicDim; row++)
        for (var col = 0; col < MosaicDim; col++)
        {
            var tx = baseCellX + col;
            var tz = baseCellZ + row;

            var path = string.Format(BmpTilePattern, areaTag, tx, tz);

            var tex = _atlas.GetByPath(path);
            _mosaicTiles[row, col].Texture = tex;

            if (tex is not null)
            {
                anyLoaded = true;
                GD.Print($"[HudMinimapPanel] Tile loaded: {path}. " +
                         "spec: Docs/RE/specs/minimap.md §3.1.");
            }
            else
            {
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


    public void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        OffsetBottom = _collapsed ? CollapsedH : MinimapH;
        if (_mosaicContainer is not null) _mosaicContainer.Visible = !_collapsed;
        if (_playerBlip is not null) _playerBlip.Visible = !_collapsed;
        GD.Print($"[HudMinimapPanel] Collapsed={_collapsed}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.4 action 5001.");
    }
}