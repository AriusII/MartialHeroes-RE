using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Simulation.Simulation;

namespace MartialHeroes.Client.Application.World;

public sealed class SectorStreamingService(
    ITerrainSectorSource source,
    IClientEventBus eventBus,
    StreamQuality quality = StreamQuality.Medium)
{
    private readonly IClientEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly HashSet<(int MapX, int MapZ)> _resident = [];

    private readonly (int MapX, int MapZ)[] _ringScratch =
        new (int MapX, int MapZ)[SectorGrid.RequiredSectorCount(2)];

    private readonly ITerrainSectorSource _source = source ?? throw new ArgumentNullException(nameof(source));

    private bool _hasCenter;

    public StreamQuality Quality { get; set; } = quality;

    public int ResidentCount => _resident.Count;

    public void SetArea(int areaId)
    {
        _source.SetArea(areaId);

        foreach (var sector in _resident) _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));

        _resident.Clear();

        _hasCenter = false;
    }

    public async ValueTask UpdateCenterAsync(
        int centerMapX,
        int centerMapZ,
        CancellationToken cancellationToken = default)
    {
        _hasCenter = true;

        EvictOutOfRange(centerMapX, centerMapZ);

        var ringRadius = SectorGrid.RingRadiusFor(Quality);
        var required = SectorGrid.RequiredSectors(centerMapX, centerMapZ, ringRadius, _ringScratch);

        for (var i = 0; i < required; i++)
        {
            var sector = _ringScratch[i];
            if (!_resident.Add(sector))
                continue;

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
                (toEvict ??= []).Add(sector);

        if (toEvict is null) return;

        foreach (var sector in toEvict)
        {
            _resident.Remove(sector);
            _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));
        }
    }
}