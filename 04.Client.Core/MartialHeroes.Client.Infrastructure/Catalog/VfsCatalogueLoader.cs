using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// Opens the VFS archive (data.inf + data/data.vfs) and loads the .scr / .csv catalogue files
/// via <see cref="ConfigTableParser"/> and <see cref="ItemsCsvParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// VFS paths are taken verbatim from the spec:
///   <c>data/script/userlevel.scr</c>  — spec: Docs/RE/formats/config_tables.md §2.4
///   <c>data/script/skills.scr</c>     — spec: Docs/RE/formats/config_tables.md §2.8
///   <c>data/script/mobs.scr</c>       — spec: Docs/RE/formats/config_tables.md §2.9
///   <c>data/script/items.scr</c>      — spec: Docs/RE/formats/items_scr.md §4 (RUNTIME item master)
///   <c>data/script/items.csv</c>      — spec: Docs/RE/formats/items_csv.md §6 (authoring-only export)
/// </para>
/// <para>
/// When a file is absent from the VFS, or when the VFS itself cannot be opened, the loader
/// returns empty arrays / an empty result — it never throws in normal operation. The caller
/// (ScrStatCatalogue, ItemCatalogue, SkillCatalogue, MobCatalogue) receives these empties and
/// degrades gracefully (preserving the all-zero / no-record behaviour from before the catalogue
/// was available).
/// </para>
/// <para>
/// The paths to infPath and vfsPath are injected — never hard-coded.
/// spec: PRESERVATION_AND_ARCHITECTURE.md (no hard-coded drive letters).
/// </para>
/// </remarks>
public sealed class VfsCatalogueLoader : IDisposable
{
    // VFS virtual paths — spec: Docs/RE/formats/config_tables.md §1 (Section 1 — VFS container).
    // All paths are stored lowercase; the VFS binary-search key is lowercased: CONFIRMED.
    // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 1 — "lowercase the requested virtual path": CONFIRMED.
    private const string UserLevelScrPath = "data/script/userlevel.scr"; // spec: §2.4
    private const string SkillsScrPath = "data/script/skills.scr"; // spec: §2.8

    private const string MobsScrPath = "data/script/mobs.scr"; // spec: §2.9

    // RUNTIME item master: the shipping client loads data/script/items.scr (binary,
    // fixed 548-byte block + 8×effect_count tail, item_uid u32 @0x034), NOT items.csv.
    // The binary's string table holds zero .csv references and the boot data-loader has no CSV
    // reader among its callees — items.csv is never loaded at runtime.
    // spec: Docs/RE/formats/items_csv.md §6 (runtime source = items.scr; CSV not loaded by the client).
    // spec: Docs/RE/formats/items_scr.md §4 (engineer guidance — walk items.scr; item_uid @0x034).
    private const string ItemsScrPath = "data/script/items.scr"; // spec: items_scr.md §4 (runtime master)

    // AUTHORING-ONLY: items.csv is the human-editable export of items.scr. It is NOT runtime data
    // (CONFIRMED not loaded by the shipping client) — kept solely as a developer/tooling aid.
    // spec: Docs/RE/formats/items_csv.md §6 (authoring/dev export only, not a runtime source).
    private const string ItemsCsvPath = "data/script/items.csv"; // spec: items_csv.md §6 (authoring form, not runtime)

    private readonly MappedVfsArchive? _archive;
    private bool _disposed;

    /// <summary>
    /// Opens the VFS archive from the given paths.
    /// If the archive cannot be opened (files missing, I/O error), the loader is created with
    /// no live archive; all Load* methods return empty results.
    /// </summary>
    /// <param name="infPath">
    /// Absolute path to the VFS index file (e.g. <c>…/data.inf</c>).
    /// Never hard-coded; always injected.
    /// spec: Docs/RE/formats/pak.md §"Two-file scheme" — "Index: data.inf": CONFIRMED.
    /// </param>
    /// <param name="vfsPath">
    /// Absolute path to the VFS data blob (e.g. <c>…/data/data.vfs</c>).
    /// Never hard-coded; always injected.
    /// spec: Docs/RE/formats/pak.md §"Two-file scheme" — "Data blob: data/data.vfs": CONFIRMED.
    /// </param>
    public VfsCatalogueLoader(string infPath, string vfsPath)
    {
        if (string.IsNullOrWhiteSpace(infPath) || string.IsNullOrWhiteSpace(vfsPath))
            return; // degenerate / test stub — no VFS

        try
        {
            _archive = MappedVfsArchive.Open(infPath, vfsPath);
        }
        catch
        {
            // The archive is optional — a missing VFS just means empty catalogues.
            _archive = null;
        }
    }

    /// <summary>
    /// Creates a loader with no backing VFS (all loads return empty).
    /// Useful in tests and in environments where the VFS data directory is not present.
    /// </summary>
    public VfsCatalogueLoader()
    {
        _archive = null;
    }

    // ── Per-file load helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Loads and parses <c>data/script/userlevel.scr</c>.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr.
    /// </summary>
    public LevelBaseEntry[] LoadUserLevelScr()
    {
        return TryLoad(UserLevelScrPath, ConfigTableParser.ParseUserLevelScr);
    }

    /// <summary>
    /// Loads and parses <c>data/script/skills.scr</c>.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr.
    /// </summary>
    public SkillCatalogEntry[] LoadSkillsScr()
    {
        return TryLoad(SkillsScrPath, ConfigTableParser.ParseSkillsScr);
    }

    /// <summary>
    /// Loads and parses <c>data/script/mobs.scr</c>.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr.
    /// </summary>
    public MobCatalogEntry[] LoadMobsScr()
    {
        return TryLoad(MobsScrPath, ConfigTableParser.ParseMobsScr);
    }

    /// <summary>
    /// Loads and parses <c>data/script/items.scr</c> — the <b>runtime</b> item master database.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// <para>
    /// This is the file the shipping client actually loads at runtime (the boot data-loader's callee
    /// set contains the items.scr reader and zero .csv references). Each record is a fixed 548-byte
    /// (0x224) block + an optional 8-byte effect tail; the walk runs to EOF (no stored count).
    /// spec: Docs/RE/formats/items_csv.md §6 (runtime item data MUST come from items.scr).
    /// spec: Docs/RE/formats/items_scr.md §1.2 / §4 (record framing + engineer guidance).
    /// </para>
    /// </summary>
    public ItemsScrRecord[] LoadItemsScr()
    {
        // ItemsScrParser.Parse returns a lazy IEnumerable (records carry zero-copy slices of the VFS
        // buffer, which stays mapped for the loader's lifetime); materialise to an array here so the
        // TryLoad<T[]> contract and the catch-all degrade-to-empty semantics are preserved.
        // spec: Docs/RE/formats/items_scr.md §1.3 (walk to EOF; no stored count).
        return TryLoad(ItemsScrPath, static data => ItemsScrParser.Parse(data).ToArray());
    }

    /// <summary>
    /// Loads and parses <c>data/script/items.csv</c> — an <b>authoring/dev export</b> of the item data.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// <para>
    /// <b>Tooling only — NOT a runtime source.</b> The shipping client does not load this CSV at all
    /// (zero .csv string references; no CSV reader in the boot data-loader callee set). The CSV is the
    /// human-editable parallel of the binary <c>items.scr</c> master; runtime item data comes from
    /// <see cref="LoadItemsScr"/>. This method is retained purely for developer tooling that needs the
    /// flat-text view (e.g. export/diff against the binary).
    /// spec: Docs/RE/formats/items_csv.md §6 (CONFIRMED authoring/dev export only, not loaded by the client).
    /// </para>
    /// </summary>
    public ItemCsvRow[] LoadItemsCsv()
    {
        return TryLoad(ItemsCsvPath, ItemsCsvParser.Parse);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _archive?.Dispose();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private T[] TryLoad<T>(string virtualPath, Func<ReadOnlyMemory<byte>, T[]> parse)
    {
        if (_archive is null || _disposed)
            return [];

        try
        {
            if (!_archive.Contains(virtualPath))
                return [];

            ReadOnlyMemory<byte> data = _archive.GetFileContent(virtualPath);
            return parse(data);
        }
        catch
        {
            // Missing file, I/O error, or parse error → degrade to empty catalogue.
            return [];
        }
    }
}