namespace MartialHeroes.Assets.Parsers.Texture.Models;

/// <summary>
///     One entry from <c>data/ui/skillicon/skillicon.txt</c>.
///     Exactly 4 fields per entry: skill_id, job_id, kind_id, icon_sheet_path.
/// </summary>
/// <param name="SkillId">
///     Skill identifier; cross-references <c>skills.scr</c> ID at +0.
///     spec: Docs/RE/formats/ui_manifests.md §2.3 col 1 — "skill_id: PARSER-CONFIRMED".
/// </param>
/// <param name="JobId">
///     Character class ID: 1=Musa, 2=Assassin, 3=Wizard, 4=Monk.
///     spec: Docs/RE/formats/ui_manifests.md §2.3 col 2 — "job_id: PARSER-CONFIRMED".
/// </param>
/// <param name="KindId">
///     Skill path within class: 1=jung, 2=sa, 3=ma.
///     spec: Docs/RE/formats/ui_manifests.md §2.3 col 3 — "kind_id: PARSER-CONFIRMED".
/// </param>
/// <param name="IconSheetPath">
///     VFS-relative path to the DDS icon sprite sheet.
///     spec: Docs/RE/formats/ui_manifests.md §2.3 col 4 — "icon_sheet_path: PARSER-CONFIRMED".
/// </param>
/// <remarks>
///     spec: Docs/RE/formats/ui_manifests.md §2.3 Column definitions: PARSER-CONFIRMED grammar;
///     SAMPLE-VERIFIED content (22 confirmed sheet entries).
/// </remarks>
public sealed record SkillIconEntry(int SkillId, int JobId, int KindId, string IconSheetPath);

/// <summary>
///     Decoded result of a <c>skillicon.txt</c> file.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/ui_manifests.md §2 data/ui/skillicon/skillicon.txt.
///     Keyed by <c>(skill_id, job_id, kind_id)</c> tuple — PARSER-CONFIRMED (entry registration
///     function stores the entry in the skill icon table keyed by this composite).
/// </remarks>
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

    /// <summary>All parsed entries in file order.</summary>
    public IReadOnlyList<SkillIconEntry> Entries { get; }

    /// <summary>Number of parsed entries.</summary>
    public int Count => Entries.Count;

    /// <summary>
    ///     Returns the icon sheet path for the given <c>(skillId, jobId, kindId)</c> tuple,
    ///     or <see langword="null" /> if not present.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/ui_manifests.md §2.3 — "all 4 fields passed to icon registration
    ///     function keyed by (skill_id, job_id, kind_id)": PARSER-CONFIRMED.
    /// </remarks>
    public SkillIconEntry? GetEntry(int skillId, int jobId, int kindId)
    {
        return _map.TryGetValue((skillId, jobId, kindId), out var e) ? e : null;
    }

    /// <summary>
    ///     Returns all entries for the given <paramref name="jobId" /> (character class).
    ///     spec: Docs/RE/formats/ui_manifests.md §2.3 — job_id identifies character class: PARSER-CONFIRMED.
    /// </summary>
    public IEnumerable<SkillIconEntry> GetEntriesForJob(int jobId)
    {
        return Entries.Where(e => e.JobId == jobId);
    }
}