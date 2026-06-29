namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class CharFilenameManifest
{
    private readonly string[] _entries;
    private readonly HashSet<string> _lookup;

    internal CharFilenameManifest(string[] entries)
    {
        _entries = entries;
        _lookup = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _entries.Length;

    public IReadOnlyList<string> Entries => _entries;

    public bool Contains(string filename)
    {
        return _lookup.Contains(filename);
    }
}