namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class ItemEffectCatalogue
{
    private readonly uint[] _typeCodes;

    public ItemEffectCatalogue(uint[] typeCodes)
    {
        ArgumentNullException.ThrowIfNull(typeCodes);
        _typeCodes = typeCodes;
    }

    public int Count => _typeCodes.Length;

    public bool TryGetTypeCode(int index, out uint typeCode)
    {
        if ((uint)index < (uint)_typeCodes.Length)
        {
            typeCode = _typeCodes[index];
            return true;
        }

        typeCode = 0;
        return false;
    }

    public static ItemEffectCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ItemEffectCatalogue(loader.LoadItemEffectScr());
    }
}