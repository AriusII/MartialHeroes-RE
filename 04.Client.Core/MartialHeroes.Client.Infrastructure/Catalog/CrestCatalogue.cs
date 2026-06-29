using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class CrestCatalogue
{
    private readonly CrestListEntry[] _entries;

    public CrestCatalogue(CrestListManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _entries = [.. manifest.Entries];
    }

    public int Count => _entries.Length;

    public IReadOnlyList<CrestListEntry> Entries => _entries;

    public CrestListEntry? GetByIndex(int index)
    {
        return (uint)index < (uint)_entries.Length ? _entries[index] : null;
    }

    public static CrestCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new CrestCatalogue(loader.LoadCrestList());
    }
}