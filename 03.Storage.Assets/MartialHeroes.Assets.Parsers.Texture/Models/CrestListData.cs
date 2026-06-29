namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed record CrestListEntry(string FileName, string VfsPath);

public sealed class CrestListManifest
{
    private readonly CrestListEntry[] _entries;

    internal CrestListManifest(CrestListEntry[] entries)
    {
        _entries = entries;
    }

    public static CrestListManifest Empty { get; } = new([]);

    public int Count => _entries.Length;

    public IReadOnlyList<CrestListEntry> Entries => _entries;
}