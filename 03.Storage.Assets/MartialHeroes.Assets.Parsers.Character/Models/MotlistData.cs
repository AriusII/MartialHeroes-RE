namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class MotlistData
{
    public const string MotDirPrefix = "data/char/mot/";

    private readonly string[] _entries;

    internal MotlistData(string[] entries)
    {
        _entries = entries;
    }

    public int Count => _entries.Length;

    public IReadOnlyList<string> Entries => _entries;
}