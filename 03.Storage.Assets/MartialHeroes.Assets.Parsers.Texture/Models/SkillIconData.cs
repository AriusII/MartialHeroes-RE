namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed record SkillIconEntry(int SkillId, int JobId, int KindId, string IconSheetPath);

public sealed class SkillIconManifest
{
    private readonly Dictionary<(int, int, int), SkillIconEntry> _map;

    internal SkillIconManifest(IReadOnlyList<SkillIconEntry> entries)
    {
        Entries = entries;
        _map = new Dictionary<(int, int, int), SkillIconEntry>(entries.Count);
        foreach (var e in entries)
            _map[(e.SkillId, e.JobId, e.KindId)] = e;
    }

    public IReadOnlyList<SkillIconEntry> Entries { get; }

    public int Count => Entries.Count;

    public SkillIconEntry? GetEntry(int skillId, int jobId, int kindId)
    {
        return _map.GetValueOrDefault((skillId, jobId, kindId));
    }

    public IEnumerable<SkillIconEntry> GetEntriesForJob(int jobId)
    {
        return Entries.Where(e => e.JobId == jobId);
    }
}