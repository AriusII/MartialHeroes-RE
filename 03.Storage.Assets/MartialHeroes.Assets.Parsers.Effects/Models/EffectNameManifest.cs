namespace MartialHeroes.Assets.Parsers.Effects.Models;

public sealed class EffectNameEntry
{
    public required int Index { get; init; }

    public required string Name { get; init; }
}

public sealed class EffectNameManifest
{
    private readonly EffectNameEntry[] _entries;

    public EffectNameManifest(EffectNameEntry[] entries)
    {
        _entries = entries;
    }

    public static EffectNameManifest Empty { get; } = new([]);

    public int Count => _entries.Length;

    public IReadOnlyList<EffectNameEntry> Entries => _entries;
}