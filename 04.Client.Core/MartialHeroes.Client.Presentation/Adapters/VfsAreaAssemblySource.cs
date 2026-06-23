using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Presentation.Adapters;

internal sealed class VfsAreaAssemblySource : IAreaAssemblySource
{
    private readonly MappedVfsArchive _vfs;


    public VfsAreaAssemblySource(MappedVfsArchive vfs, int areaId)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        AreaId = areaId;
        AreaCellKeys = LoadCellKeys(vfs, areaId);
        TerrainTextureCatalog = LoadBgTextureCatalog(vfs);
    }


    public int AreaId { get; }

    public IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys { get; }

    public BgtextureLstCatalog TerrainTextureCatalog { get; }

    public bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes)
    {
        var tag = AreaTag(AreaId);
        var path = $"data/map{tag}/dat/d{tag}x{mapX}z{mapZ}{extension}";
        return TryRead(path, out bytes);
    }

    public bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes)
    {
        return TryRead(vfsLogicalPath, out bytes);
    }


    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }


    private bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (_vfs.Contains(path))
            {
                bytes = _vfs.GetFileContent(path);
                return !bytes.IsEmpty;
            }
        }
        catch
        {
        }

        bytes = ReadOnlyMemory<byte>.Empty;
        return false;
    }

    private static IReadOnlyCollection<(int MapX, int MapZ)> LoadCellKeys(
        MappedVfsArchive vfs, int areaId)
    {
        try
        {
            var tag = AreaTag(areaId);
            var lstPath = $"data/map{tag}/dat/d{tag}.lst";
            if (!vfs.Contains(lstPath))
                return [];

            var data = vfs.GetFileContent(lstPath);
            if (data.IsEmpty)
                return [];

            var manifest = LstManifestParser.Parse(data);
            var keys = new List<(int MapX, int MapZ)>(manifest.Entries.Length);
            foreach (var entry in manifest.Entries)
            {
                var mapX = (int)(entry.Key / 100000u);
                var mapZ = (int)(entry.Key % 100000u);
                keys.Add((mapX, mapZ));
            }

            return keys;
        }
        catch
        {
            return [];
        }
    }

    private static BgtextureLstCatalog LoadBgTextureCatalog(MappedVfsArchive vfs)
    {
        try
        {
            const string lstPath = "data/map000/texture/bgtexture.lst";
            if (vfs.Contains(lstPath))
            {
                var data = vfs.GetFileContent(lstPath);
                if (!data.IsEmpty)
                    return BgtextureLstParser.Parse(data);
            }
        }
        catch
        {
        }

        return BgtextureLstCatalog.Empty;
    }
}