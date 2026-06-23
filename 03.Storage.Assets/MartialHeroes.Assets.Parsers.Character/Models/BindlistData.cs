namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class BindlistData
{
    private readonly string[] _entries;
    private readonly HashSet<string> _lookup;

    internal BindlistData(string[] entries)
    {
        _entries = entries;
        _lookup = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _entries.Length;

    public IReadOnlyList<string> Entries => _entries;

    public bool IsRegistered(string bndFilename)
    {
        return _lookup.Contains(bndFilename);
    }
}