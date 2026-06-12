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
///   <c>data/script/items.csv</c>      — spec: Docs/RE/formats/config_tables.md §4.1
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
    private const string SkillsScrPath    = "data/script/skills.scr";    // spec: §2.8
    private const string MobsScrPath      = "data/script/mobs.scr";      // spec: §2.9
    private const string ItemsCsvPath     = "data/script/items.csv";     // spec: §4.1

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
    /// Loads and parses <c>data/script/items.csv</c>.
    /// Returns an empty array if the file is absent or the archive is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §4.1 items.csv.
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
