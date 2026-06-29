namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed record UserjointEntry(
    int JointIndex,
    int Value0,
    int Value1,
    int Value2,
    int Value3);

public sealed class UserjointTable
{
    private readonly Dictionary<int, UserjointEntry> _byJointIndex;

    internal UserjointTable(IReadOnlyList<UserjointEntry> entries)
    {
        Entries = entries;
        _byJointIndex = new Dictionary<int, UserjointEntry>(entries.Count);
        foreach (var entry in entries)
            _byJointIndex.TryAdd(entry.JointIndex, entry);
    }

    public int Count => Entries.Count;

    public IReadOnlyList<UserjointEntry> Entries { get; }

    public UserjointEntry? GetByJointIndex(int jointIndex)
    {
        return _byJointIndex.TryGetValue(jointIndex, out var entry) ? entry : null;
    }
}