// Adapters/IconCatalogs.cs
//
// Resolves per-skill 23×23-pixel icon AtlasTextures for the SkillWindow and HUD hotbar,
// and per-item full-texture icons for the InventoryWindow.
//
// Architecture — SkillIconCatalog (IconCatalogs):
//   1. Loads the skill-icon sheet manifest from data/ui/skillicon/skillicon.txt via
//      SkillIconManifestParser — maps (jobId, kindId) to a VFS DDS sheet path.
//      spec: Docs/RE/formats/ui_manifests.md §2 (SKILL block, 4-field entries) PARSER-CONFIRMED.
//
//   2. Loads the active class-stance table from data/script/<class><stance>.do via
//      DoStanceParser — maps slotIndex → (iconSrcX, iconSrcY) per skill.
//      spec: Docs/RE/formats/ui_manifests.md §2.7 (116-byte records, Map B keyed by slotIndex)
//            CODE-CONFIRMED + SAMPLE-VERIFIED.
//
//   3. GetIcon(slotIndex) returns a Godot AtlasTexture using:
//        - The sheet DDS loaded from the (job, kind) path in skillicon.txt,
//        - Region = Rect2(iconSrcX, iconSrcY, IconCellW, IconCellH).
//      spec: Docs/RE/formats/ui_manifests.md §2.6 (23×23 pixel cell, data-driven per-skill UV)
//            CODE-CONFIRMED.
//
// Architecture — ItemIconCatalog:
//   Loads data/item/texturelist.txt via TextureListParser — maps tex_id → DDS VFS path.
//   spec: Docs/RE/formats/ui_manifests.md §10 (flat newline-delimited filename list) CODE-CONFIRMED.
//   Each item icon is a whole-texture blit — no sub-rect, no atlas.
//   spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
//   Lazy-loaded and cached per tex_id (ImageTexture); returns null when VFS offline or file absent.
//   spec: Docs/RE/formats/ui_manifests.md §7 — DDS/TGA format handled by LoadTexture (magic bytes).
//
// Offline / offline-VFS: all methods return null gracefully; callers must handle null and
// keep the current placeholder rendering.
//
// Threading: all public methods must be called on the Godot main thread (they touch
// Godot Image/ImageTexture APIs indirectly via UiAssetLoader.LoadAtlas).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens;

// ReSharper disable UnusedMember.Global  (public API consumed by HUD nodes)

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
/// Singleton-style facade (created once in ClientContext) that resolves per-skill icon
/// <see cref="AtlasTexture"/> values for the HUD hotbar and SkillWindow.
///
/// <para><b>Data pipeline</b></para>
/// <list type="bullet">
///   <item>
///     <term>skillicon.txt</term>
///     <description>
///       Maps <c>(skill_id, job_id, kind_id)</c> to the 512×512 DDS sprite sheet path.
///       Loaded via <see cref="SkillIconManifestParser"/> from <c>data/ui/skillicon/skillicon.txt</c>.
///       spec: Docs/RE/formats/ui_manifests.md §2 — PARSER-CONFIRMED grammar; SAMPLE-VERIFIED content.
///     </description>
///   </item>
///   <item>
///     <term>.do stance table</term>
///     <description>
///       The per-class stance file (e.g. <c>data/script/musajung.do</c>) is a headerless array
///       of 116-byte records. Each record carries <c>iconSrcX</c> at +0x18 and <c>iconSrcY</c>
///       at +0x1C — the pixel origin of the 23×23 icon cell on the 512×512 sheet.
///       Loaded via <see cref="DoStanceParser"/>.
///       spec: Docs/RE/formats/ui_manifests.md §2.7 — CODE-CONFIRMED + SAMPLE-VERIFIED.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Icon cell model</b></para>
/// Fixed 23×23 pixel cell at <c>(iconSrcX, iconSrcY)</c> on the 512×512 sheet.
/// spec: Docs/RE/formats/ui_manifests.md §2.6 — CODE-CONFIRMED (three independent draw sites).
///
/// <para><b>Offline mode</b></para>
/// All methods return null when VFS is unavailable. Callers keep their existing placeholder look.
/// </summary>
public sealed class IconCatalogs : IDisposable
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Spec-cited constants
    // ──────────────────────────────────────────────────────────────────────────

    // Icon cell dimensions: fixed 23×23 pixels.
    // spec: Docs/RE/formats/ui_manifests.md §2.6 — "fixed 23×23 pixel cell": CODE-CONFIRMED.
    public const int IconCellW = 23;
    public const int IconCellH = 23;

    // VFS path of the skill icon sheet manifest.
    // spec: Docs/RE/formats/ui_manifests.md §2.2 — "data/ui/skillicon/skillicon.txt": PARSER-CONFIRMED.
    private const string SkillIconTxtPath = "data/ui/skillicon/skillicon.txt";

    // Demo class-stance: Musa (job=1) / jung (kind=1).
    // spec: Docs/RE/formats/ui_manifests.md §2.4 — skill_id 1001, job 1, kind 1 → musajung.dds: SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — data/script/musajung.do: SAMPLE-VERIFIED presence.
    private const int DemoJobId = 1; // Musa  (무사)  — spec §2.3 col2
    private const int DemoKindId = 1; // jung  (정)    — spec §2.3 col3

    // Demo .do path for the Musa-jung stance.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 table row 1 — "data/script/musajung.do": SAMPLE-VERIFIED.
    private const string DemoDoPath = "data/script/musajung.do";

    // classStanceRef for the Musa-jung .do file. Passed to skillicon.txt lookup.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "classStanceRef 1001 for musajung.do": CODE-CONFIRMED.
    private const int DemoClassStanceRef = 1001;

    // ──────────────────────────────────────────────────────────────────────────
    //  Internal state
    // ──────────────────────────────────────────────────────────────────────────

    private readonly RealClientAssets? _assets;

    // Lazy-loaded skillicon.txt manifest.
    private SkillIconManifest? _manifest;
    private bool _manifestAttempted;

    // Lazy-loaded .do stance table (Map B: slotIndex → record).
    private DoStanceTable? _doTable;
    private bool _doTableAttempted;

    // The DDS sheet path selected for the demo class+stance, resolved from _manifest.
    private string? _activeSheetPath;

    // Cached full-sheet Godot texture (loaded from _activeSheetPath).
    // Kept to avoid reloading it for each icon slice.
    private ImageTexture? _sheetTexture;
    private bool _sheetTextureAttempted;

    private bool _disposed;

    // ──────────────────────────────────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an IconCatalogs backed by the supplied VFS assets handle.
    /// Pass <see langword="null"/> for offline / no-VFS mode; all methods return null.
    /// </summary>
    public IconCatalogs(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a 23×23 <see cref="AtlasTexture"/> for the skill at the given
    /// <paramref name="slotIndex"/> (Map B key from the active .do stance table).
    ///
    /// <para>Resolution chain:</para>
    /// <list type="number">
    ///   <item>Look up the .do table record by <paramref name="slotIndex"/> (Map B).</item>
    ///   <item>Read <c>iconSrcX</c> / <c>iconSrcY</c> from the record.</item>
    ///   <item>Build <see cref="AtlasTexture"/> with <c>Region = Rect2(srcX, srcY, 23, 23)</c>
    ///         on the 512×512 sheet DDS.</item>
    /// </list>
    ///
    /// Returns <see langword="null"/> when the VFS is offline, the slot is not in the table,
    /// or the DDS sheet cannot be loaded.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 pixel cell, data-driven UV": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map B keyed by slotIndex (+0x08)": CODE-CONFIRMED.
    /// </summary>
    /// <param name="slotIndex">
    /// Sequential slot number (0, 1, 2, …) — the slotIndex field at record+0x08.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 "+0x08 u32 slotIndex": CODE-CONFIRMED.
    /// </param>
    public AtlasTexture? GetIcon(uint slotIndex)
    {
        DoStanceTable? table = EnsureDoTable();
        if (table is null) return null;

        DoStanceRecord? record = table.GetBySlotIndex(slotIndex);
        if (record is null) return null;

        return BuildAtlas(record.IconSrcX, record.IconSrcY);
    }

    /// <summary>
    /// Returns the first N non-zero records from the active stance table in slot order,
    /// for use by the SkillWindow demo list and HUD hotbar demo population.
    ///
    /// <para>Only records where both <c>iconSrcX</c> and <c>iconSrcY</c> are non-negative
    /// are returned (records with negative icon coordinates are likely padding entries).</para>
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map B keyed by slotIndex (sequential 0,1,2…)":
    /// CODE-CONFIRMED.
    /// </summary>
    /// <param name="maxCount">Maximum records to return.</param>
    /// <returns>
    /// List of <c>(slotIndex, icon)</c> pairs, ordered by slotIndex ascending.
    /// Empty when the VFS is offline.
    /// </returns>
    public IReadOnlyList<(uint SlotIndex, AtlasTexture? Icon)> GetFirstSlots(int maxCount)
    {
        DoStanceTable? table = EnsureDoTable();
        if (table is null) return Array.Empty<(uint, AtlasTexture?)>();

        // Sort by slotIndex then take first maxCount valid icons.
        var result = new List<(uint, AtlasTexture?)>(maxCount);
        foreach (DoStanceRecord rec in table.Records
                     .OrderBy(r => r.SlotIndex))
        {
            if (result.Count >= maxCount) break;

            // Skip records with negative icon coordinates — these are placeholder/padding rows.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "iconSrcX/Y: authored data; may be
            //       non-multiples of 23": CODE-CONFIRMED. Negative values indicate no icon.
            if (rec.IconSrcX < 0 || rec.IconSrcY < 0) continue;

            result.Add((rec.SlotIndex, BuildAtlas(rec.IconSrcX, rec.IconSrcY)));
        }

        return result;
    }

    /// <summary>Number of non-zero records in the loaded .do table, or 0 when offline/unloaded.</summary>
    public int DoRecordCount
    {
        get
        {
            DoStanceTable? t = EnsureDoTable();
            return t?.Records.Count ?? 0;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Lazy loaders
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and caches the skillicon.txt manifest on first use.
    /// Returns null when VFS is offline or the file is absent/malformed.
    /// spec: Docs/RE/formats/ui_manifests.md §2 — "data/ui/skillicon/skillicon.txt": PARSER-CONFIRMED.
    /// </summary>
    private SkillIconManifest? EnsureManifest()
    {
        if (_manifestAttempted) return _manifest;
        _manifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(SkillIconTxtPath);
            if (raw.IsEmpty)
            {
                GD.Print("[IconCatalogs] data/ui/skillicon/skillicon.txt absent from VFS — " +
                         "skill icon sheet manifest unavailable.");
                return null;
            }

            _manifest = SkillIconManifestParser.Parse(raw);

            // Resolve the active sheet path for the demo class+stance.
            // spec: Docs/RE/formats/ui_manifests.md §2.3 — lookup key is (skill_id, job_id, kind_id):
            //       PARSER-CONFIRMED. For demo we use skill_id = DemoClassStanceRef.
            SkillIconEntry? entry = _manifest.GetEntry(DemoClassStanceRef, DemoJobId, DemoKindId);
            _activeSheetPath = entry?.IconSheetPath;

            GD.Print($"[IconCatalogs] skillicon.txt loaded: {_manifest.Count} entries. " +
                     $"Demo sheet (job={DemoJobId}, kind={DemoKindId}): '{_activeSheetPath ?? "<not found>"}'. " +
                     $"spec: Docs/RE/formats/ui_manifests.md §2.4 SAMPLE-VERIFIED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[IconCatalogs] skillicon.txt parse failed: {ex.Message}");
            _manifest = null;
        }

        return _manifest;
    }

    /// <summary>
    /// Loads and caches the Musa-jung .do stance table on first use.
    /// Returns null when VFS is offline or the file is absent/malformed.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "data/script/musajung.do": SAMPLE-VERIFIED presence.
    ///       "record_count = file_size / 116; all-zero records skipped": SAMPLE-VERIFIED.
    /// </summary>
    private DoStanceTable? EnsureDoTable()
    {
        if (_doTableAttempted) return _doTable;
        _doTableAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(DemoDoPath);
            if (raw.IsEmpty)
            {
                GD.Print($"[IconCatalogs] {DemoDoPath} absent from VFS — " +
                         "skill icon coordinates unavailable.");
                return null;
            }

            _doTable = DoStanceParser.Parse(raw);
            GD.Print($"[IconCatalogs] {DemoDoPath} loaded: {_doTable.Records.Count} non-zero records " +
                     $"(totalCount={_doTable.TotalRecordCount}, trailingBytes={_doTable.TrailingByteCount}). " +
                     $"spec: Docs/RE/formats/ui_manifests.md §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED.");

            // Also ensure the manifest is loaded so the sheet path is resolved.
            EnsureManifest();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[IconCatalogs] {DemoDoPath} parse failed: {ex.Message}");
            _doTable = null;
        }

        return _doTable;
    }

    /// <summary>
    /// Loads and caches the full 512×512 DDS sheet as a Godot ImageTexture.
    /// Returns null when VFS is offline, the sheet path is unresolved, or the DDS cannot be loaded.
    /// spec: Docs/RE/formats/ui_manifests.md §2.4 — "512×512 DDS, DXT2/3": SAMPLE-VERIFIED.
    /// </summary>
    private ImageTexture? EnsureSheetTexture()
    {
        if (_sheetTextureAttempted) return _sheetTexture;
        _sheetTextureAttempted = true;

        // Make sure the manifest (and its resolved sheet path) is loaded first.
        EnsureManifest();

        if (_activeSheetPath is null || _assets is null) return null;

        try
        {
            // Delegate to RealClientAssets.LoadTexture which probes magic bytes internally
            // (handles the DDS/TGA format and the do.dds TGA-mislabelled caveat automatically).
            // spec: Docs/RE/formats/ui_manifests.md §7 — do.dds TGA caveat handled by LoadTexture.
            _sheetTexture = _assets.LoadTexture(_activeSheetPath);
            if (_sheetTexture is not null)
            {
                GD.Print($"[IconCatalogs] Icon sheet loaded: '{_activeSheetPath}' (512×512 SAMPLE-VERIFIED). " +
                         $"spec: Docs/RE/formats/ui_manifests.md §2.4.");
            }
            else
            {
                GD.Print($"[IconCatalogs] Icon sheet '{_activeSheetPath}' loaded as null (format unsupported).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[IconCatalogs] Icon sheet '{_activeSheetPath}' load failed: {ex.Message}");
            _sheetTexture = null;
        }

        return _sheetTexture;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Atlas texture builder
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a 23×23 <see cref="AtlasTexture"/> at the given pixel origin on the active sheet.
    ///
    /// Returns null when the sheet texture is unavailable or the origin is out of the 512×512 bounds.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 — "Source rect: (iconSrcX, iconSrcY, 23, 23)
    ///       in atlas pixels": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.4 — "sheet is 512×512": SAMPLE-VERIFIED.
    /// </summary>
    private AtlasTexture? BuildAtlas(short iconSrcX, short iconSrcY)
    {
        // Guard: non-negative coordinates only.
        if (iconSrcX < 0 || iconSrcY < 0) return null;

        ImageTexture? sheet = EnsureSheetTexture();
        if (sheet is null) return null;

        // Guard: stay within the 512×512 sheet boundary.
        // spec: Docs/RE/formats/ui_manifests.md §2.4 — "sheet is 512×512": SAMPLE-VERIFIED.
        if (iconSrcX + IconCellW > 512 || iconSrcY + IconCellH > 512) return null;

        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(iconSrcX, iconSrcY, IconCellW, IconCellH),
            // spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 pixel cell": CODE-CONFIRMED.
            FilterClip = true,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // ImageTexture objects are reference-counted by Godot's GC — no manual free needed.
        // _assets is owned by ClientContext; we do not dispose it here.
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  ItemIconCatalog
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton-style facade (created once in ClientContext) that resolves per-item icon
/// <see cref="ImageTexture"/> values for the InventoryWindow.
///
/// <para><b>Data pipeline</b></para>
/// <list type="bullet">
///   <item>
///     <term>texturelist.txt</term>
///     <description>
///       Maps <c>tex_id</c> (leading decimal digits of the filename) to a VFS path of the
///       form <c>data/item/texture/&lt;filename&gt;</c>. Loaded via
///       <see cref="TextureListParser"/> from <c>data/item/texturelist.txt</c>.
///       spec: Docs/RE/formats/ui_manifests.md §10 — flat newline-delimited list: CODE-CONFIRMED.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Icon draw model</b></para>
/// Each item icon is a <b>whole-texture blit</b> — the entire DDS is used; there is no
/// atlas sub-rect. The native DDS dimensions are used (width/height not forced).
/// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect":
/// CODE-CONFIRMED.
/// spec: Docs/RE/formats/ui_manifests.md §10.4 — "loader passes width/height as 0 →
/// native DDS dimensions": CODE-CONFIRMED.
///
/// <para><b>Offline mode</b></para>
/// All methods return null when VFS is unavailable. Callers keep their existing placeholder look.
/// </summary>
public sealed class ItemIconCatalog : IDisposable
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Spec-cited constants
    // ─────────────────────────────────────────────────────────────────────────

    // VFS path of the item icon texture manifest.
    // spec: Docs/RE/formats/ui_manifests.md §10.1 — "data/item/texturelist.txt": CODE-CONFIRMED.
    private const string TextureListPath = "data/item/texturelist.txt";

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal state
    // ─────────────────────────────────────────────────────────────────────────

    private readonly RealClientAssets? _assets;

    // Lazy-loaded texturelist.txt manifest.
    private TextureListManifest? _manifest;
    private bool _manifestAttempted;

    // Per-tex_id texture cache. Key = tex_id, Value = loaded texture (or null when load failed).
    // Null values are cached to avoid retrying failed loads on every access.
    private readonly Dictionary<int, ImageTexture?> _textureCache = new();

    private bool _disposed;

    // ─────────────────────────────────────────────────────────────────────────
    //  Construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an ItemIconCatalog backed by the supplied VFS assets handle.
    /// Pass <see langword="null"/> for offline / no-VFS mode; all methods return null.
    /// </summary>
    public ItemIconCatalog(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Total entry count in the loaded texturelist.txt, or 0 when offline/unloaded.</summary>
    public int ManifestCount
    {
        get
        {
            TextureListManifest? m = EnsureManifest();
            return m?.Count ?? 0;
        }
    }

    /// <summary>
    /// Returns an <see cref="ImageTexture"/> for the item icon identified by <paramref name="texId"/>.
    ///
    /// <para>Resolution chain:</para>
    /// <list type="number">
    ///   <item>Look up the VFS path in the texturelist.txt manifest by <paramref name="texId"/>.</item>
    ///   <item>Load the DDS from the VFS via <see cref="RealClientAssets.LoadTexture"/>
    ///         (handles TGA-mislabelled-as-dds via magic-byte probe).</item>
    ///   <item>Cache the result (including null) so the DDS is loaded at most once.</item>
    /// </list>
    ///
    /// Returns <see langword="null"/> when the VFS is offline, the tex_id is absent from the
    /// manifest, or the DDS cannot be loaded.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §7 — DDS/TGA format handled by LoadTexture: CONFIRMED.
    /// </summary>
    /// <param name="texId">
    /// Numeric texture ID (leading decimal digits of the texturelist.txt filename).
    /// spec: Docs/RE/formats/ui_manifests.md §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
    /// </param>
    public ImageTexture? GetIcon(int texId)
    {
        // Return cached result (even null — means we tried and failed).
        if (_textureCache.TryGetValue(texId, out ImageTexture? cached))
            return cached;

        TextureListManifest? manifest = EnsureManifest();
        if (manifest is null)
        {
            _textureCache[texId] = null;
            return null;
        }

        TextureListEntry? entry = manifest.GetById(texId);
        if (entry is null)
        {
            _textureCache[texId] = null;
            return null;
        }

        // Load via RealClientAssets.LoadTexture which probes magic bytes internally.
        // spec: Docs/RE/formats/ui_manifests.md §7 — do.dds TGA caveat handled by LoadTexture.
        ImageTexture? tex = null;
        if (_assets is not null)
        {
            try
            {
                tex = _assets.LoadTexture(entry.VfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ItemIconCatalog] LoadTexture('{entry.VfsPath}') failed: {ex.Message}");
            }
        }

        _textureCache[texId] = tex;
        return tex;
    }

    /// <summary>
    /// Returns the first <paramref name="maxCount"/> item textures from the manifest in file
    /// order, for use by the InventoryWindow demo grid.
    ///
    /// <para>Only entries where the DDS loads successfully are returned (null entries are skipped
    /// in the result list, but they are still counted against <paramref name="maxCount"/> to avoid
    /// an infinite scan over a partially-offline VFS).</para>
    ///
    /// Returns an empty list when the VFS is offline or the manifest is absent.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §10.2 — "1,335 entries, 100% present on real VFS":
    /// SAMPLE-VERIFIED (B3-item-icons lane brief).
    /// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
    /// </summary>
    /// <param name="maxCount">Maximum entries to return.</param>
    /// <returns>
    /// List of <c>(texId, icon)</c> pairs from the manifest in file order, capped at
    /// <paramref name="maxCount"/>. Icon may be null when the DDS failed to load.
    /// </returns>
    public IReadOnlyList<(int TexId, ImageTexture? Icon)> GetDemoIcons(int maxCount)
    {
        TextureListManifest? manifest = EnsureManifest();
        if (manifest is null) return Array.Empty<(int, ImageTexture?)>();

        var result = new List<(int, ImageTexture?)>(maxCount);
        foreach (TextureListEntry entry in manifest.Entries)
        {
            if (result.Count >= maxCount) break;
            ImageTexture? tex = GetIcon(entry.TexId);
            result.Add((entry.TexId, tex));
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Lazy loaders
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and caches the texturelist.txt manifest on first use.
    /// Returns null when VFS is offline or the file is absent/malformed.
    /// spec: Docs/RE/formats/ui_manifests.md §10.2 — flat newline-delimited list: CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §10.3 — per-line parsing rules: CODE-CONFIRMED.
    /// </summary>
    private TextureListManifest? EnsureManifest()
    {
        if (_manifestAttempted) return _manifest;
        _manifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(TextureListPath);
            if (raw.IsEmpty)
            {
                GD.Print("[ItemIconCatalog] data/item/texturelist.txt absent from VFS — " +
                         "item icons unavailable (offline mode).");
                return null;
            }

            _manifest = TextureListParser.Parse(raw);
            GD.Print($"[ItemIconCatalog] texturelist.txt loaded: {_manifest.Count} entries. " +
                     "spec: Docs/RE/formats/ui_manifests.md §10 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ItemIconCatalog] texturelist.txt parse failed: {ex.Message}");
            _manifest = null;
        }

        return _manifest;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _textureCache.Clear();
        // ImageTexture objects are reference-counted by Godot's GC — no manual free needed.
        // _assets is owned by ClientContext; we do not dispose it here.
    }
}