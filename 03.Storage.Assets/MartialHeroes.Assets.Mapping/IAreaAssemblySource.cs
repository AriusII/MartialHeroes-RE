using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Mapping;

public interface IAreaAssemblySource
{
    int AreaId { get; }

    IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys { get; }

    BgtextureLstCatalog TerrainTextureCatalog { get; }

    bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes);

    bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes);

    bool TryGetAreaNpcSpawns(out ReadOnlyMemory<byte> npcSpawnBytes);
}