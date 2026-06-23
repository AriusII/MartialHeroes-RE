using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class VfsCatalogueLoader : IDisposable
{
    private const string UserLevelScrPath = "data/script/userlevel.scr";
    private const string SkillsScrPath = "data/script/skills.scr";
    private const string MobsScrPath = "data/script/mobs.scr";
    private const string ItemsScrPath = "data/script/items.scr";

    private readonly MappedVfsArchive? _archive;
    private bool _disposed;

    public VfsCatalogueLoader(string infPath, string vfsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vfsPath);

        try
        {
            _archive = MappedVfsArchive.Open(infPath, vfsPath);
        }
        catch
        {
            _archive = null;
        }
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _archive?.Dispose();
    }


    public LevelBaseEntry[] LoadUserLevelScr()
    {
        return TryLoad(UserLevelScrPath, ConfigTableParser.ParseUserLevelScr);
    }

    public SkillCatalogEntry[] LoadSkillsScr()
    {
        return TryLoad(SkillsScrPath, ConfigTableParser.ParseSkillsScr);
    }

    public MobCatalogEntry[] LoadMobsScr()
    {
        return TryLoad(MobsScrPath, ConfigTableParser.ParseMobsScr);
    }

    public ItemsScrRecord[] LoadItemsScr()
    {
        return TryLoad(ItemsScrPath, static data => ItemsScrParser.Parse(data).ToArray());
    }


    private T[] TryLoad<T>(string virtualPath, Func<ReadOnlyMemory<byte>, T[]> parse)
    {
        if (_archive is null || _disposed)
            return [];

        try
        {
            if (!_archive.Contains(virtualPath))
                return [];

            var data = _archive.GetFileContent(virtualPath);
            return parse(data);
        }
        catch
        {
            return [];
        }
    }
}