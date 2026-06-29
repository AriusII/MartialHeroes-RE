using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public sealed class RegionCatalog
{
    private const int CellSize = 256;
    private const int MaxRegionId = 31;
    private readonly byte[] _cells;
    private readonly uint _height;
    private readonly int _originX;
    private readonly int _originZ;
    private readonly uint _width;
    private readonly string[] _zoneNames;
    private readonly ZoneType[] _zoneTypes;


    public RegionCatalog(
        uint width,
        uint height,
        ReadOnlySpan<byte> cells,
        int originX,
        int originZ,
        ReadOnlySpan<uint> rawZoneTypes,
        IReadOnlyList<string>? zoneNames = null)
    {
        var expectedCells = (long)width * height;
        if (cells.Length != expectedCells)
            throw new ArgumentException(
                $"RegionCatalog: cells length {cells.Length} ≠ width × height ({width} × {height} = {expectedCells}). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1.");

        if (rawZoneTypes.Length != 32)
            throw new ArgumentException(
                $"RegionCatalog: rawZoneTypes length {rawZoneTypes.Length} ≠ 32 (the fixed record count). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.");

        _width = width;
        _height = height;
        _originX = originX;
        _originZ = originZ;
        _cells = cells.ToArray();

        _zoneTypes = new ZoneType[32];
        _zoneNames = new string[32];
        for (var i = 0; i < 32; i++)
        {
            _zoneTypes[i] = ToZoneType(rawZoneTypes[i]);
            _zoneNames[i] = zoneNames is not null && i < zoneNames.Count
                ? zoneNames[i] ?? string.Empty
                : string.Empty;
        }
    }


    public ZoneType Resolve(float worldX, float worldZ)
    {
        var regionId = RegionIdAt(worldX, worldZ);

        if (regionId > MaxRegionId)
            return ZoneType.OpenPvp;

        return _zoneTypes[regionId];
    }


    public string ResolveZoneName(float worldX, float worldZ)
    {
        var regionId = RegionIdAt(worldX, worldZ);

        if (regionId > MaxRegionId)
            return string.Empty;

        return _zoneNames[regionId];
    }


    public string ZoneNameOf(int regionId)
    {
        if ((uint)regionId > MaxRegionId)
            return string.Empty;

        return _zoneNames[regionId];
    }


    private int RegionIdAt(float worldX, float worldZ)
    {
        var col = (int)((worldX - _originX) / CellSize);
        var row = (int)((worldZ - _originZ) / CellSize);

        if (col < 0 || row < 0 || (uint)col >= _width || (uint)row >= _height)
            return 0;

        var index = col + row * (int)_width;

        if ((uint)index >= (uint)_cells.Length)
            return 0;

        return _cells[index];
    }


    private static ZoneType ToZoneType(uint raw)
    {
        return raw switch
        {
            0 => ZoneType.Safe,
            1 => ZoneType.OpenPvp,
            2 => ZoneType.Closed,
            _ => ZoneType.Safe
        };
    }
}