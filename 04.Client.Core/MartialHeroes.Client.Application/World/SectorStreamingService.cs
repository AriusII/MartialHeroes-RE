using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.World;

public sealed class SectorStreamingService
{
    private readonly IClientEventBus _eventBus;

    private readonly HashSet<(int MapX, int MapZ)> _resident = new();

    private readonly (int MapX, int MapZ)[] _ringScratch =
        new (int MapX, int MapZ)[SectorGrid.RequiredSectorCount(2)];

    private readonly ITerrainSectorSource _source;
    private (int MapX, int MapZ) _center;

    private bool _hasCenter;

    public SectorStreamingService(
        ITerrainSectorSource source,
        IClientEventBus eventBus,
        StreamQuality quality = StreamQuality.Medium)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Quality = quality;
    }

    public StreamQuality Quality { get; set; }

    public int ResidentCount => _resident.Count;

    public (int MapX, int MapZ)? Center => _hasCenter ? _center : null;

    public void SetArea(int areaId)
    {
        _source.SetArea(areaId);

        foreach (var sector in _resident) _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));

        _resident.Clear();

        _hasCenter = false;
    }

    public bool IsResident((int MapX, int MapZ) sector)
    {
        return _resident.Contains(sector);
    }

    public ValueTask UpdateForPositionAsync(Vector3Fixed position, CancellationToken cancellationToken = default)
    {
        var (worldX, _, worldZ) = position.ToVector3Float();
        var (mapX, mapZ) = SectorGrid.WorldToSector(worldX, worldZ);
        return UpdateCenterAsync(mapX, mapZ, cancellationToken);
    }

    public async ValueTask UpdateCenterAsync(
        int centerMapX,
        int centerMapZ,
        CancellationToken cancellationToken = default)
    {
        _center = (centerMapX, centerMapZ);
        _hasCenter = true;

        EvictOutOfRange(centerMapX, centerMapZ);

        var ringRadius = SectorGrid.RingRadiusFor(Quality);
        var required = SectorGrid.RequiredSectors(centerMapX, centerMapZ, ringRadius, _ringScratch);

        for (var i = 0; i < required; i++)
        {
            var sector = _ringScratch[i];
            if (_resident.Contains(sector))
                continue;

            _resident.Add(sector);

            var payload =
                await _source.LoadSectorAsync(sector.MapX, sector.MapZ, cancellationToken)
                    .ConfigureAwait(false);

            _eventBus.Publish(new SectorLoadedEvent(sector.MapX, sector.MapZ, payload));
        }
    }

    private void EvictOutOfRange(int centerMapX, int centerMapZ)
    {
        List<(int MapX, int MapZ)>? toEvict = null;
        foreach (var sector in _resident)
            if (SectorGrid.ShouldEvict(centerMapX, centerMapZ, sector.MapX, sector.MapZ))
                (toEvict ??= new List<(int MapX, int MapZ)>()).Add(sector);

        if (toEvict is null) return;

        foreach (var sector in toEvict)
        {
            _resident.Remove(sector);
            _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));
        }
    }
}