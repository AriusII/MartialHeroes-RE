using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class VfsCatalogueLoader : IDisposable
{
    private const string UserLevelScrPath = "data/script/userlevel.scr";
    private const string SkillsScrPath = "data/script/skills.scr";
    private const string MobsScrPath = "data/script/mobs.scr";
    private const string ItemsScrPath = "data/script/items.scr";
    private const string NpcScrPath = "data/script/npc.scr";
    private const string ItemScaleScrPath = "data/script/itemscale.scr";
    private const string ItemEffectScrPath = "data/script/itemeffect.scr";
    private const string ItemSkinlistPath = "data/item/skinlist.txt";
    private const string CharSkinlistPath = "data/char/skinlist.txt";
    private const string Tex10241024ListPath = "data/char/tex10241024list.txt";
    private const string Tex512512ListPath = "data/char/tex512512list.txt";
    private const string Tex256256ListPath = "data/char/tex256256list.txt";
    private const string Tex256512ListPath = "data/char/tex256512list.txt";
    private const string SameEmoticonPath = "data/char/sameemoticon.txt";
    private const string EmoticonPath = "data/char/emoticon.txt";
    private const string CrestListPath = "data/ui/guildicon/crestlist.txt";

    public const string Tex10241024Dir = "data/char/tex10241024/";
    public const string Tex512512Dir = "data/char/tex512512/";
    public const string Tex256256Dir = "data/char/tex256256/";
    public const string Tex256512Dir = "data/char/tex256512/";

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

    public NpcScrRecord[] LoadNpcScr()
    {
        return TryLoad(NpcScrPath, NpcScrParser.Parse);
    }

    public ItemScaleRecord[] LoadItemScaleScr()
    {
        return TryLoad(ItemScaleScrPath, ItemScaleParser.Parse);
    }

    public uint[] LoadItemEffectScr()
    {
        return TryLoad(ItemEffectScrPath, ItemEffectParser.Parse);
    }

    public CharFilenameManifest LoadItemSkinlist()
    {
        return TryLoadOne(ItemSkinlistPath, SkinlistParser.Parse);
    }

    public CharFilenameManifest LoadCharSkinlist()
    {
        return TryLoadOne(CharSkinlistPath, SkinlistParser.Parse);
    }

    public CharFilenameManifest LoadCharTex10241024List()
    {
        return TryLoadOne(Tex10241024ListPath, TexListParser.Parse);
    }

    public CharFilenameManifest LoadCharTex512512List()
    {
        return TryLoadOne(Tex512512ListPath, TexListParser.Parse);
    }

    public CharFilenameManifest LoadCharTex256256List()
    {
        return TryLoadOne(Tex256256ListPath, TexListParser.Parse);
    }

    public CharFilenameManifest LoadCharTex256512List()
    {
        return TryLoadOne(Tex256512ListPath, TexListParser.Parse);
    }

    public SameEmoticonTable LoadSameEmoticon()
    {
        return TryLoadOne(SameEmoticonPath, SameEmoticonParser.Parse);
    }

    public EmoticonTable LoadEmoticon()
    {
        return TryLoadOne(EmoticonPath, EmoticonParser.Parse);
    }

    public CrestListManifest LoadCrestList()
    {
        return TryLoadOne(CrestListPath, static data => CrestListParser.Parse(data.Span));
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

    private T TryLoadOne<T>(string virtualPath, Func<ReadOnlyMemory<byte>, T> parse)
    {
        try
        {
            if (_archive is null || _disposed || !_archive.Contains(virtualPath))
                return parse(ReadOnlyMemory<byte>.Empty);

            var data = _archive.GetFileContent(virtualPath);
            return parse(data);
        }
        catch
        {
            return parse(ReadOnlyMemory<byte>.Empty);
        }
    }
}