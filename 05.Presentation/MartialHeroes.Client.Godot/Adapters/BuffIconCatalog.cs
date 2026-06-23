using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

public sealed class BuffIconCatalog : IDisposable
{
    private const string StateIconAtlasPath = "data/ui/skillicon/stateicon.dds";

    private const string BuffIconPositionXdbPath = "data/script/buff_icon_position.xdb";

    private const int AtlasSize = 512;

    public const int BuffCellSize = 23;

    public const int StateCellSize = 25;

    public const uint BuffStateBoundary = 80u;


    private readonly RealClientAssets? _assets;

    private readonly Dictionary<uint, AtlasTexture?> _cache = new();
    private bool _atlasAttempted;

    private ImageTexture? _atlasTexture;

    private bool _disposed;

    private BuffIconPositionTable? _table;
    private bool _tableAttempted;


    public BuffIconCatalog(RealClientAssets? assets)
    {
        _assets = assets;
    }


    public int TableCount => EnsureTable()?.Records.Count ?? 0;


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
    }

    public AtlasTexture? GetIcon(ushort buffId)
    {
        uint key = buffId;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = BuildIcon(buffId);
        _cache[key] = icon;
        return icon;
    }

    public static int CellSizeForId(ushort buffId)
    {
        return buffId <= BuffStateBoundary ? BuffCellSize : StateCellSize;
    }


    private AtlasTexture? BuildIcon(ushort buffId)
    {
        var table = EnsureTable();
        if (table is null) return null;

        var rec = table.TryGetById(buffId);
        if (rec is null) return null;

        var atlasX = rec.AtlasX;
        var atlasY = rec.AtlasY;

        if (atlasX < 0 || atlasY < 0) return null;

        var atlas = EnsureAtlas();
        if (atlas is null) return null;

        var cellSize = CellSizeForId(buffId);

        if (atlasX + cellSize > AtlasSize || atlasY + cellSize > AtlasSize) return null;

        return new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(atlasX, atlasY, cellSize, cellSize),
            FilterClip = true
        };
    }


    private BuffIconPositionTable? EnsureTable()
    {
        if (_tableAttempted) return _table;
        _tableAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(BuffIconPositionXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[BuffIconCatalog] buff_icon_position.xdb absent from VFS — " +
                         "buff icons unavailable (offline mode).");
                return null;
            }

            _table = BuffIconPositionParser.Parse(raw);
            GD.Print($"[BuffIconCatalog] buff_icon_position.xdb loaded: {_table.Records.Count} entries. " +
                     "spec: Docs/RE/formats/misc_data.md §1.3 CODE-CONFIRMED + SAMPLE-VERIFIED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BuffIconCatalog] buff_icon_position.xdb parse failed: {ex.Message}");
            _table = null;
        }

        return _table;
    }

    private ImageTexture? EnsureAtlas()
    {
        if (_atlasAttempted) return _atlasTexture;
        _atlasAttempted = true;

        if (_assets is null) return null;

        try
        {
            _atlasTexture = _assets.LoadTexture(StateIconAtlasPath);
            if (_atlasTexture is not null)
                GD.Print("[BuffIconCatalog] stateicon.dds loaded (512×512 DXT2 shared atlas). " +
                         "spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.");
            else
                GD.Print("[BuffIconCatalog] stateicon.dds loaded as null (format unsupported).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BuffIconCatalog] stateicon.dds load failed: {ex.Message}");
            _atlasTexture = null;
        }

        return _atlasTexture;
    }
}