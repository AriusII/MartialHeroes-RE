using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.World;

public sealed class RegionService
{
    private readonly IHudEventHub _hub;

    private readonly IRegionSource _source;


    private RegionCatalog? _catalog;

    private bool _everPublished;

    private ZoneType _lastPublished;


    public RegionService(IRegionSource source, IHudEventHub hub)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public ZoneType CurrentZone => _everPublished ? _lastPublished : ZoneType.Safe;


    public async ValueTask LoadAreaAsync(int areaId, CancellationToken cancellationToken = default)
    {
        _catalog = null;
        _everPublished = false;

        try
        {
            var catalog =
                await _source.LoadRegionCatalogAsync(areaId, cancellationToken).ConfigureAwait(false);

            _catalog = catalog;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
    }

    public void UpdatePosition(float worldX, float worldZ)
    {
        var zone = _catalog?.Resolve(worldX, worldZ) ?? ZoneType.Safe;

        if (_everPublished && zone == _lastPublished) return;

        _everPublished = true;
        _lastPublished = zone;
        _hub.PublishZoneChanged(new ZoneChangedEvent(zone));
    }
}