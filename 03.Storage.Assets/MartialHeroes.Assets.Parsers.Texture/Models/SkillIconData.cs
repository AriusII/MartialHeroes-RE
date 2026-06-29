namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed record SkillIconEntry(int SkillId, int JobId, int KindId, string IconSheetPath);

public sealed class SkillIconManifest
{
    private readonly Dictionary<int, SkillIconEntry[]> _byJob;
    private readonly Dictionary<(int, int, int), SkillIconEntry> _map;

    internal SkillIconManifest(IReadOnlyList<SkillIconEntry> entries)
    {
        Entries = entries;
        _map = new Dictionary<(int, int, int), SkillIconEntry>(entries.Count);

        var jobBuckets = new Dictionary<int, List<SkillIconEntry>>();
        foreach (var e in entries)
        {
            _map[(e.SkillId, e.JobId, e.KindId)] = e;

            if (!jobBuckets.TryGetValue(e.JobId, out var bucket))
            {
                bucket = [];
                jobBuckets[e.JobId] = bucket;
            }

            bucket.Add(e);
        }

        _byJob = new Dictionary<int, SkillIconEntry[]>(jobBuckets.Count);
        foreach (var pair in jobBuckets)
            _byJob[pair.Key] = pair.Value.ToArray();
    }

    public IReadOnlyList<SkillIconEntry> Entries { get; }

    public int Count => Entries.Count;

    public SkillIconEntry? GetEntry(int skillId, int jobId, int kindId)
    {
        return _map.GetValueOrDefault((skillId, jobId, kindId));
    }

    public IEnumerable<SkillIconEntry> GetEntriesForJob(int jobId)
    {
        return _byJob.TryGetValue(jobId, out var bucket) ? bucket : [];
    }
}