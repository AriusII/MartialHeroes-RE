using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class VfsTerrainSectorSource(MappedVfsArchive? vfs, int areaId) : ITerrainSectorSource
{
    private int _areaId = areaId;

    private HashSet<uint>? _manifestKeys = vfs is not null ? TryLoadManifest(vfs, areaId) : null;

    public void SetArea(int areaId)
    {
        if (areaId == _areaId &&
            _manifestKeys is not null) return;

        _areaId = areaId;
        _manifestKeys = vfs is not null ? TryLoadManifest(vfs, areaId) : null;
    }

    public ValueTask<ReadOnlyMemory<byte>> LoadSectorAsync(
        int mapX,
        int mapZ,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (vfs is null || _manifestKeys is null) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

        var key = LstManifestParser.ComputeKey(mapX, mapZ);
        if (!_manifestKeys.Contains(key)) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

        var tedPath = BuildTedPath(_areaId, mapX, mapZ);

        var bytes = TryGetContent(vfs, tedPath);
        return ValueTask.FromResult(bytes);
    }


    private static string BuildTedPath(int areaId, int mapX, int mapZ)
    {
        var areaTag = AreaTag(areaId);
        return $"data/map{areaTag}/dat/d{areaTag}x{mapX}z{mapZ}.ted";
    }

    private static string BuildLstPath(int areaId)
    {
        var areaTag = AreaTag(areaId);
        return $"data/map{areaTag}/dat/d{areaTag}.lst";
    }

    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }


    private static HashSet<uint>? TryLoadManifest(MappedVfsArchive vfs, int areaId)
    {
        var lstPath = BuildLstPath(areaId);

        try
        {
            if (!vfs.Contains(lstPath)) return null;

            var data = vfs.GetFileContent(lstPath);
            if (data.IsEmpty) return null;

            var manifest = LstManifestParser.Parse(data);
            var keys = new HashSet<uint>(manifest.Entries.Length);
            foreach (var entry in manifest.Entries) keys.Add(entry.Key);

            return keys;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ReadOnlyMemory<byte> TryGetContent(MappedVfsArchive vfs, string path)
    {
        try
        {
            return !vfs.Contains(path) ? ReadOnlyMemory<byte>.Empty : vfs.GetFileContent(path);
        }
        catch (Exception)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}