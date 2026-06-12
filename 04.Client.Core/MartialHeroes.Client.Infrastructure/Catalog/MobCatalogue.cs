using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// In-memory lookup catalogue for mob (monster) definitions parsed from
/// <c>data/script/mobs.scr</c>.
/// </summary>
/// <remarks>
/// <para>
/// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr — "stride: 488 bytes, 3,997 records": CONFIRMED.
/// </para>
/// <para>
/// Only confirmed / plausible-range fields are surfaced. The majority of the 488-byte record is
/// UNVERIFIED and is preserved as the raw record in <see cref="MobRecord.Raw"/> for future analysis.
/// spec: §2.9 — "internal layout: majority UNVERIFIED".
/// </para>
/// </remarks>
public sealed class MobCatalogue
{
    private readonly Dictionary<ushort, MobRecord> _byId;

    /// <summary>
    /// Constructs the catalogue from pre-parsed mob catalogue entries.
    /// </summary>
    /// <param name="entries">
    /// Records as returned by <see cref="MartialHeroes.Assets.Parsers.ConfigTableParser.ParseMobsScr"/>.
    /// spec: Docs/RE/formats/config_tables.md §2.9.
    /// </param>
    public MobCatalogue(MobCatalogEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _byId = new Dictionary<ushort, MobRecord>(entries.Length);

        foreach (MobCatalogEntry e in entries)
        {
            var record = new MobRecord
            {
                // u16 mob ID (map key) @ +0. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
                Id = e.Id,

                // u8 mob type @ +324. 0=normal; 11=boss/elite; 12=special. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type (11=boss/elite): CONFIRMED".
                Type = e.Type,

                // i32 mob level @ +244. -1=not set; boss range 36..46. CONFIRMED (boss validation path).
                // spec: Docs/RE/formats/config_tables.md §2.9 — "+244 i32 Mob level: CONFIRMED (boss validation path)".
                Level = e.MobLevel,

                // u32 spawn timer in seconds @ +248. Range 33..41006; boss ~40s. CONFIRMED (plausible range).
                // spec: Docs/RE/formats/config_tables.md §2.9 — "+248 u32 Spawn timer (seconds): CONFIRMED (plausible range)".
                SpawnTimerSeconds = e.SpawnTimer,

                // Full raw record for future analysis of UNVERIFIED fields.
                Raw = e.Raw,
            };

            _byId[e.Id] = record;
        }
    }

    /// <summary>
    /// Creates a <see cref="MobCatalogue"/> by loading <c>mobs.scr</c> from the given loader.
    /// </summary>
    public static MobCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new MobCatalogue(loader.LoadMobsScr());
    }

    /// <summary>Number of mobs in this catalogue.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Looks up a mob by its ID.
    /// Returns <see langword="null"/> when the ID is not present.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — Mob ID u16 @ +0 is the map key.
    /// </summary>
    public MobRecord? TryGet(ushort mobId) =>
        _byId.TryGetValue(mobId, out var r) ? r : null;
}

/// <summary>
/// A decoded mob record. Only confirmed / plausible-range fields are surfaced.
/// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr.
/// </summary>
public sealed record MobRecord
{
    /// <summary>
    /// Mob ID (map key). u16 @ +0.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "Mob ID u16 @ +0: CONFIRMED".
    /// </summary>
    public required ushort Id { get; init; }

    /// <summary>
    /// Mob type byte @ +324.
    /// 0 = normal (3,749 records); 2–10 = sub-types; 11 = boss/elite (125 records); 12 = special.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+324 u8 Mob type: CONFIRMED".
    /// </summary>
    public required byte Type { get; init; }

    /// <summary>
    /// Mob level @ +244. −1 = not set; 0 = trivial; boss range 36..46 for ID range 14000–14009.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+244 i32 Mob level: CONFIRMED (boss validation path)".
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Spawn timer in seconds @ +248. Range 33..41,006 in sample; boss default ≈ 40 s.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "+248 u32 Spawn timer (seconds): CONFIRMED (plausible range)".
    /// </summary>
    public required uint SpawnTimerSeconds { get; init; }

    /// <summary>
    /// True if this is a boss/elite mob (Type == 11).
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "mob type byte = 11 → boss/elite": CONFIRMED.
    /// </summary>
    public bool IsBoss => Type == 11;

    /// <summary>
    /// Full raw 488-byte record. Fields between confirmed offsets are UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "mobs.scr stride 488 bytes: CONFIRMED".
    /// </summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }
}