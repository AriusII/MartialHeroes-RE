using MartialHeroes.Assets.Parsers.World;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class VfsRegionSource(MappedVfsArchive? vfs) : IRegionSource
{
    private const string RegionGridPathFmt = "data/map{0:D3}/region{0:D3}.bin";

    private const string RegionTablePathFmt = "data/map{0:D3}/regiontable{0:D3}.bin";

    public ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
        int areaId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (vfs is null)
            return ValueTask.FromResult<RegionCatalog?>(null);

        try
        {
            var gridPath = string.Format(RegionGridPathFmt, areaId);
            if (!vfs.Contains(gridPath))
                return ValueTask.FromResult<RegionCatalog?>(null);

            var gridBytes = vfs.GetFileContent(gridPath);
            if (gridBytes.IsEmpty)
                return ValueTask.FromResult<RegionCatalog?>(null);

            var grid = RegionGridParser.Parse(gridBytes);

            var tablePath = string.Format(RegionTablePathFmt, areaId);
            if (!vfs.Contains(tablePath))
                return ValueTask.FromResult<RegionCatalog?>(null);

            var tableBytes = vfs.GetFileContent(tablePath);
            if (tableBytes.IsEmpty)
                return ValueTask.FromResult<RegionCatalog?>(null);

            var records = RegionZoneTableParser.Parse(tableBytes);

            var rawZoneTypes = new uint[RegionZoneTableParser.RecordCount];
            for (var i = 0; i < records.Length && i < rawZoneTypes.Length; i++)
                rawZoneTypes[i] = records[i].ZoneTypeRaw;

            var catalog = new RegionCatalog(
                grid.Width,
                grid.Height,
                grid.Cells.Span,
                grid.OriginX,
                grid.OriginZ,
                rawZoneTypes);

            return ValueTask.FromResult<RegionCatalog?>(catalog);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ValueTask.FromResult<RegionCatalog?>(null);
        }
    }
}