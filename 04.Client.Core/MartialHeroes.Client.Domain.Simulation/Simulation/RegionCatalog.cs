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
    private readonly ZoneType[] _zoneTypes;


    public RegionCatalog(
        uint width,
        uint height,
        ReadOnlySpan<byte> cells,
        int originX,
        int originZ,
        ReadOnlySpan<uint> rawZoneTypes)
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
        for (var i = 0; i < 32; i++)
            _zoneTypes[i] = ToZoneType(rawZoneTypes[i]);
    }


    public ZoneType Resolve(float worldX, float worldZ)
    {
        var col = (int)((worldX - _originX) / CellSize);
        var row = (int)((worldZ - _originZ) / CellSize);

        if (col < 0 || row < 0 || (uint)col >= _width || (uint)row >= _height)
            return _zoneTypes[0];

        var index = col + row * (int)_width;

        if ((uint)index >= (uint)_cells.Length)
            return _zoneTypes[0];

        int regionId = _cells[index];

        if (regionId > MaxRegionId)
            return ZoneType.OpenPvp;

        return _zoneTypes[regionId];
    }

    public ZoneType ZoneTypeForRegion(int regionId)
    {
        if (regionId < 0 || regionId > MaxRegionId)
            return ZoneType.OpenPvp;
        return _zoneTypes[regionId];
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