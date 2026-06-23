namespace MartialHeroes.Client.Application.World;

public interface ITerrainSectorSource
{
    ValueTask<ReadOnlyMemory<byte>> LoadSectorAsync(
        int mapX,
        int mapZ,
        CancellationToken cancellationToken = default);

    void SetArea(int areaId)
    {
    }
}