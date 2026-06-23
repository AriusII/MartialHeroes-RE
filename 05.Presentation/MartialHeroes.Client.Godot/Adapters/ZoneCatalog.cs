
using Godot;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Parsers.World;
using MartialHeroes.Assets.Parsers.World.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

public sealed class ZoneCatalog
{
    private const string MapSettingPath = "data/script/mapsetting.scr";

    private const string
        RegionTablePathFmt = "data/map{0:D3}/regiontable{0:D3}.bin";

    private const string RegionGridPathFmt = "data/map{0:D3}/region{0:D3}.bin";

    private const int RegionTableAreaCeiling = 300;


    private readonly RealClientAssets? _assets;

    private readonly Dictionary<int, RegionGrid?> _gridCache = new();

    private readonly Dictionary<int, RegionTableRecord[]?> _regionCache = new();

    private MapZoneRecord[]? _zones;
    private bool _zonesAttempted;


    public ZoneCatalog(RealClientAssets? assets)
    {
        _assets = assets;
    }

    public IReadOnlyList<MapZoneRecord> AllZones => EnsureZones() ?? Array.Empty<MapZoneRecord>();


    public string GetZoneName(float legacyWorldX, float legacyWorldZ)
    {
        var zones = EnsureZones();
        if (zones is null) return string.Empty;

        foreach (var z in zones)
        {
            if (z.ZoneId == 0 || string.IsNullOrEmpty(z.ZoneName)) continue;

            if (legacyWorldX >= z.WorldMinX && legacyWorldX <= z.WorldMaxX &&
                legacyWorldZ >= z.WorldMinZ && legacyWorldZ <= z.WorldMaxZ)
                return z.ZoneName;
        }

        return string.Empty;
    }

    public MapZoneRecord? GetZoneRecord(float legacyWorldX, float legacyWorldZ)
    {
        var zones = EnsureZones();
        if (zones is null) return null;

        foreach (var z in zones)
        {
            if (z.ZoneId == 0 || string.IsNullOrEmpty(z.ZoneName)) continue;
            if (legacyWorldX >= z.WorldMinX && legacyWorldX <= z.WorldMaxX &&
                legacyWorldZ >= z.WorldMinZ && legacyWorldZ <= z.WorldMaxZ)
                return z;
        }

        return null;
    }

    public string GetNearestSubZoneName(int areaId, float legacyWorldX, float legacyWorldZ,
        MapZoneRecord? zoneRecord = null)
    {
        var recs = EnsureRegionTable(areaId);
        if (recs is null || recs.Length == 0) return string.Empty;

        var grid = EnsureRegionGrid(areaId);
        if (grid is not null)
        {
            int regionId = grid.GetRegionId((int)legacyWorldX, (int)legacyWorldZ);

            foreach (var r in recs)
                if (r.RegionId == regionId)
                    return r.ZoneName ?? string.Empty;

            return string.Empty;
        }

        if (zoneRecord is not null)
            foreach (var r in recs)
                if (r.RegionId == areaId)
                    return r.ZoneName ?? string.Empty;

        return string.Empty;
    }


    private MapZoneRecord[]? EnsureZones()
    {
        if (_zonesAttempted) return _zones;
        _zonesAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(MapSettingPath);
            if (raw.IsEmpty)
            {
                GodotPrint($"[ZoneCatalog] {MapSettingPath} absent from VFS — zone names unavailable.");
                return null;
            }

            _zones = MapSettingScrParser.Parse(raw);
            GodotPrint($"[ZoneCatalog] mapsetting.scr loaded: {_zones.Length} zone records. " +
                       "spec: Docs/RE/formats/misc_data.md §7.1 SAMPLE-VERIFIED.");
        }
        catch (InvalidDataException ex)
        {
            GodotPrintErr($"[ZoneCatalog] mapsetting.scr parse failed: {ex.Message}");
            _zones = null;
        }
        catch (Exception ex)
        {
            GodotPrintErr($"[ZoneCatalog] mapsetting.scr load error: {ex.Message}");
            _zones = null;
        }

        return _zones;
    }

    private RegionTableRecord[]? EnsureRegionTable(int areaId)
    {
        if (_regionCache.TryGetValue(areaId, out var cached)) return cached;

        if (_assets is null)
        {
            _regionCache[areaId] = null;
            return null;
        }

        if (areaId < 0 || areaId > RegionTableAreaCeiling)
        {
            _regionCache[areaId] = null;
            return null;
        }

        try
        {
            var path = string.Format(RegionTablePathFmt, areaId);
            var raw = _assets.GetRaw(path);
            if (raw.IsEmpty)
            {
                _regionCache[areaId] = null;
                return null;
            }

            var recs = RegionTableParser.Parse(raw);
            _regionCache[areaId] = recs;
            GodotPrint($"[ZoneCatalog] regiontable{areaId:D3}.bin loaded: {recs.Length} records. " +
                       "spec: Docs/RE/formats/region_grid.md §regiontable CONFIRMED.");
            return recs;
        }
        catch (InvalidDataException ex)
        {
            GodotPrintErr($"[ZoneCatalog] regiontable{areaId:D3}.bin parse failed: {ex.Message}");
            _regionCache[areaId] = null;
            return null;
        }
        catch (Exception ex)
        {
            GodotPrintErr($"[ZoneCatalog] regiontable{areaId:D3}.bin load error: {ex.Message}");
            _regionCache[areaId] = null;
            return null;
        }
    }

    private RegionGrid? EnsureRegionGrid(int areaId)
    {
        if (_gridCache.TryGetValue(areaId, out var cached)) return cached;

        if (_assets is null || areaId < 0 || areaId > RegionTableAreaCeiling)
        {
            _gridCache[areaId] = null;
            return null;
        }

        try
        {
            var path = string.Format(RegionGridPathFmt, areaId);
            var raw = _assets.GetRaw(path);
            if (raw.IsEmpty)
            {
                _gridCache[areaId] = null;
                return null;
            }

            var grid = RegionBinParser.Parse(raw);
            _gridCache[areaId] = grid;
            GodotPrint($"[ZoneCatalog] region{areaId:D3}.bin grid loaded: {grid.Width}×{grid.Height}. " +
                       "spec: Docs/RE/formats/region_grid.md §Layout A.");
            return grid;
        }
        catch (InvalidDataException ex)
        {
            GodotPrintErr($"[ZoneCatalog] region{areaId:D3}.bin parse failed: {ex.Message}");
            _gridCache[areaId] = null;
            return null;
        }
        catch (Exception ex)
        {
            GodotPrintErr($"[ZoneCatalog] region{areaId:D3}.bin load error: {ex.Message}");
            _gridCache[areaId] = null;
            return null;
        }
    }


    private static void GodotPrint(string msg)
    {
        GD.Print(msg);
    }

    private static void GodotPrintErr(string msg)
    {
        GD.PrintErr(msg);
    }
}