using MartialHeroes.Client.Application.Assets;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class VfsCatalogueStore : ICatalogueAssembler
{
    private const string NpcScrPath = "data/script/npc.scr";
    private const string ItemsScrPath = "data/script/items.scr";
    private const string ItemScaleScrPath = "data/script/itemscale.scr";
    private const string ItemEffectScrPath = "data/script/itemeffect.scr";
    private const string ItemSkinlistPath = "data/item/skinlist.txt";
    private const string SameEmoticonPath = "data/char/sameemoticon.txt";
    private const string CrestListPath = "data/ui/guildicon/crestlist.txt";
    private readonly Lock _gate = new();

    private readonly VfsCatalogueLoader _loader;
    private CharacterVisualCatalogue? _characterVisuals;
    private CrestCatalogue? _crests;
    private EmoticonCatalogue? _emoticons;
    private ItemEffectCatalogue? _itemEffects;
    private ItemCatalogue? _items;
    private ItemScaleCatalogue? _itemScales;

    private NpcCatalogue? _npc;

    public VfsCatalogueStore(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
    }

    public NpcCatalogue Npc
    {
        get
        {
            lock (_gate)
            {
                return _npc ??= NpcCatalogue.FromLoader(_loader);
            }
        }
    }

    public ItemCatalogue Items
    {
        get
        {
            lock (_gate)
            {
                return _items ??= ItemCatalogue.FromLoader(_loader);
            }
        }
    }

    public ItemScaleCatalogue ItemScales
    {
        get
        {
            lock (_gate)
            {
                return _itemScales ??= ItemScaleCatalogue.FromLoader(_loader);
            }
        }
    }

    public ItemEffectCatalogue ItemEffects
    {
        get
        {
            lock (_gate)
            {
                return _itemEffects ??= ItemEffectCatalogue.FromLoader(_loader);
            }
        }
    }

    public CharacterVisualCatalogue CharacterVisuals
    {
        get
        {
            lock (_gate)
            {
                return _characterVisuals ??= CharacterVisualCatalogue.FromLoader(_loader);
            }
        }
    }

    public EmoticonCatalogue Emoticons
    {
        get
        {
            lock (_gate)
            {
                return _emoticons ??= EmoticonCatalogue.FromLoader(_loader);
            }
        }
    }

    public CrestCatalogue Crests
    {
        get
        {
            lock (_gate)
            {
                return _crests ??= CrestCatalogue.FromLoader(_loader);
            }
        }
    }

    public bool TryAssemble(string logicalPath)
    {
        switch (logicalPath)
        {
            case NpcScrPath:
                _ = Npc;
                return true;
            case ItemsScrPath:
                _ = Items;
                return true;
            case ItemScaleScrPath:
                _ = ItemScales;
                return true;
            case ItemEffectScrPath:
                _ = ItemEffects;
                return true;
            case ItemSkinlistPath:
                _ = CharacterVisuals;
                return true;
            case SameEmoticonPath:
                _ = Emoticons;
                return true;
            case CrestListPath:
                _ = Crests;
                return true;
            default:
                return false;
        }
    }

    public void EnsureBuilt()
    {
        _ = Npc;
        _ = Items;
        _ = ItemScales;
        _ = ItemEffects;
        _ = CharacterVisuals;
        _ = Emoticons;
        _ = Crests;
    }
}