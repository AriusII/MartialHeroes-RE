using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class RebindableAreaAssemblySource(MappedVfsArchive vfs, int initialAreaId) : IAreaAssemblySource
{
    private readonly MappedVfsArchive _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
    private VfsAreaAssemblySource _inner = new(vfs, initialAreaId);


    public int AreaId => _inner.AreaId;

    public IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys => _inner.AreaCellKeys;

    public BgtextureLstCatalog TerrainTextureCatalog => _inner.TerrainTextureCatalog;

    public bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes)
    {
        return _inner.TryGetCellFile(mapX, mapZ, extension, out bytes);
    }

    public bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes)
    {
        return _inner.TryGetCellFileByName(vfsLogicalPath, out bytes);
    }


    public void SetArea(int areaId)
    {
        if (areaId == _inner.AreaId) return;
        _inner = new VfsAreaAssemblySource(_vfs, areaId);
    }
}