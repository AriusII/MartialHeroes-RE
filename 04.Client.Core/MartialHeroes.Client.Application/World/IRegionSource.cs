using MartialHeroes.Client.Domain.Simulation.Simulation;

namespace MartialHeroes.Client.Application.World;

public interface IRegionSource
{
    ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
        int areaId,
        CancellationToken cancellationToken = default);
}