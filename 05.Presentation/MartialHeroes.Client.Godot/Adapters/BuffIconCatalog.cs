// Adapters/BuffIconCatalog.cs
//
// Resolves per-buff 23×23 (or 25×25) AtlasTextures from the shared stateicon.dds atlas,
// keyed by buff_id via the buff_icon_position.xdb position table.
//
// Data pipeline:
//   1. Loads data/ui/skillicon/stateicon.dds — 512×512 DXT2 shared atlas for all buff icons.
//      spec: Docs/RE/formats/misc_data.md §1.3 — "atlas is data/ui/skillicon/stateicon.dds (512×512 DXT2)".
//   2. Loads data/script/buff_icon_position.xdb via BuffIconPositionParser (Assets.Parsers).
//      spec: Docs/RE/formats/misc_data.md §1.3 — "record stride 12 bytes: u32 buff_id, i32 atlas_x, i32 atlas_y".
//   3. GetIcon(buffId) looks up (atlas_x, atlas_y) and selects cell size:
//        buff_id ≤ 80  → 23×23 px  spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED
//        buff_id > 80  → 25×25 px  spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED
//
// Offline / no-VFS: all methods return null gracefully; callers keep placeholder rendering.
// Threading: all public methods must be called on the Godot main thread.
//
// spec: Docs/RE/formats/misc_data.md §1.3 (buff_icon_position.xdb — record layout, stored pixel coords).
// spec: Docs/RE/formats/misc_data.md §1.6 (buff bar render model — 30 slots, cell sizes, atlas binding).

using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
///     Singleton-style facade (created once by the HUD or ClientContext) that resolves buff icon
///     <see cref="AtlasTexture" /> slices from the shared stateicon.dds atlas.
///     <para>
///         <b>Atlas model</b>
///     </para>
///     One shared 512×512 DXT2 atlas (<c>data/ui/skillicon/stateicon.dds</c>) serves every buff
///     and state. The per-buff <c>(atlas_x, atlas_y)</c> pixel origin is read from
///     <c>data/script/buff_icon_position.xdb</c>. Cell size is class-dependent:
///     ≤80 → 23×23 px; &gt;80 → 25×25 px.
///     spec: Docs/RE/formats/misc_data.md §1.3 CODE-CONFIRMED; §1.6 CODE-CONFIRMED.
///     <para>
///         <b>Offline mode</b>
///     </para>
///     All methods return null when VFS is unavailable. Callers keep their existing placeholder look.
/// </summary>
public sealed class BuffIconCatalog : IDisposable
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Spec-cited constants
    // ─────────────────────────────────────────────────────────────────────────

    // Shared atlas path.
    // spec: Docs/RE/formats/misc_data.md §1.3 — "atlas is data/ui/skillicon/stateicon.dds (512×512 DXT2)".
    private const string StateIconAtlasPath = "data/ui/skillicon/stateicon.dds";

    // Position table path.
    // spec: Docs/RE/formats/misc_data.md §1.3 — VFS path confirmed from the static analysis.
    private const string BuffIconPositionXdbPath = "data/script/buff_icon_position.xdb";

    // Atlas dimensions — 512×512 DXT2.
    // spec: Docs/RE/formats/misc_data.md §1.3 — "512×512 DXT2".
    private const int AtlasSize = 512;

    // Cell size for buff_id ≤ 80 (buff class — flowing counter).
    // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "buff_id ≤ 80 → 23×23 px".
    public const int BuffCellSize = 23; // spec: Docs/RE/formats/misc_data.md §1.6 (buff cell 23×23)

    // Cell size for buff_id > 80 (state/debuff class — fixed per-slot position).
    // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "buff_id > 80 → 25×25 px".
    public const int StateCellSize = 25; // spec: Docs/RE/formats/misc_data.md §1.6 (state cell 25×25)

    // The literal boundary value from the slot setter.
    // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "boundary value 80 is a literal comparison".
    public const uint BuffStateBoundary = 80u; // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal state
    // ─────────────────────────────────────────────────────────────────────────

    private readonly RealClientAssets? _assets;

    // Per-buffId AtlasTexture cache (null cached = "tried and failed or absent").
    private readonly Dictionary<uint, AtlasTexture?> _cache = new();
    private bool _atlasAttempted;

    // Lazy-loaded stateicon.dds full-sheet texture.
    private ImageTexture? _atlasTexture;

    private bool _disposed;

    // Lazy-loaded position table (buff_id → BuffIconPositionRecord).
    private BuffIconPositionTable? _table;
    private bool _tableAttempted;

    // ─────────────────────────────────────────────────────────────────────────
    //  Construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Creates a catalog backed by the supplied VFS assets handle.
    ///     Pass <see langword="null" /> for offline / no-VFS mode; all methods return null.
    /// </summary>
    public BuffIconCatalog(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Number of entries in the loaded position table, or 0 when offline / unloaded.</summary>
    public int TableCount => EnsureTable()?.Records.Count ?? 0;

    // ─────────────────────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        // ImageTexture objects are reference-counted by Godot's GC — no manual free needed.
        // _assets is owned by the caller; we do not dispose it here.
    }

    /// <summary>
    ///     Returns an <see cref="AtlasTexture" /> for the buff with the given <paramref name="buffId" />.
    ///     <para>Resolution chain:</para>
    ///     <list type="number">
    ///         <item>Look up <paramref name="buffId" /> in <c>buff_icon_position.xdb</c> for <c>(atlas_x, atlas_y)</c>.</item>
    ///         <item>Select cell size: buffId ≤ 80 → 23×23; &gt;80 → 25×25.</item>
    ///         <item>Build <see cref="AtlasTexture" /> with <c>Region = Rect2(atlas_x, atlas_y, cellSize, cellSize)</c>.</item>
    ///     </list>
    ///     Returns <see langword="null" /> when the VFS is offline, the id is absent from the table,
    ///     or the atlas cannot be loaded.
    ///     spec: Docs/RE/formats/misc_data.md §1.3 — "(atlas_x, atlas_y) are raw stored pixel values,
    ///     never inferred from a formula": CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/misc_data.md §1.6 — cell size selection (23 or 25): CODE-CONFIRMED.
    /// </summary>
    public AtlasTexture? GetIcon(ushort buffId)
    {
        uint key = buffId;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = BuildIcon(buffId);
        _cache[key] = icon;
        return icon;
    }

    /// <summary>
    ///     Returns the cell size (23 or 25) for the given <paramref name="buffId" />.
    ///     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.
    /// </summary>
    public static int CellSizeForId(ushort buffId)
    {
        return buffId <= BuffStateBoundary ? BuffCellSize : StateCellSize;
        // spec: misc_data.md §1.6
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal build
    // ─────────────────────────────────────────────────────────────────────────

    private AtlasTexture? BuildIcon(ushort buffId)
    {
        var table = EnsureTable();
        if (table is null) return null;

        // TryGetById uses uint key per the BuffIconPositionTable API.
        var rec = table.TryGetById(buffId);
        if (rec is null) return null;

        var atlasX = rec.AtlasX;
        var atlasY = rec.AtlasY;

        // Negative coordinates indicate no icon (some entries are sentinel padding).
        if (atlasX < 0 || atlasY < 0) return null;

        var atlas = EnsureAtlas();
        if (atlas is null) return null;

        var cellSize = CellSizeForId(buffId); // spec: Docs/RE/formats/misc_data.md §1.6

        // Bounds-check against 512×512 atlas.
        // spec: Docs/RE/formats/misc_data.md §1.3 — "512×512 DXT2".
        if (atlasX + cellSize > AtlasSize || atlasY + cellSize > AtlasSize) return null;

        return new AtlasTexture
        {
            Atlas = atlas,
            // spec: Docs/RE/formats/misc_data.md §1.3 — "(atlas_x, atlas_y) are raw stored pixel values": CODE-CONFIRMED.
            Region = new Rect2(atlasX, atlasY, cellSize, cellSize),
            FilterClip = true
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Lazy loaders
    // ─────────────────────────────────────────────────────────────────────────

    private BuffIconPositionTable? EnsureTable()
    {
        if (_tableAttempted) return _table;
        _tableAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(BuffIconPositionXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[BuffIconCatalog] buff_icon_position.xdb absent from VFS — " +
                         "buff icons unavailable (offline mode).");
                return null;
            }

            _table = BuffIconPositionParser.Parse(raw);
            GD.Print($"[BuffIconCatalog] buff_icon_position.xdb loaded: {_table.Records.Count} entries. " +
                     "spec: Docs/RE/formats/misc_data.md §1.3 CODE-CONFIRMED + SAMPLE-VERIFIED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BuffIconCatalog] buff_icon_position.xdb parse failed: {ex.Message}");
            _table = null;
        }

        return _table;
    }

    private ImageTexture? EnsureAtlas()
    {
        if (_atlasAttempted) return _atlasTexture;
        _atlasAttempted = true;

        if (_assets is null) return null;

        try
        {
            // spec: Docs/RE/formats/misc_data.md §1.6 — "loads data/ui/skillicon/stateicon.dds (512×512 DXT2)
            //       exactly once and binds that single texture handle to all icon slots": CODE-CONFIRMED.
            _atlasTexture = _assets.LoadTexture(StateIconAtlasPath);
            if (_atlasTexture is not null)
                GD.Print("[BuffIconCatalog] stateicon.dds loaded (512×512 DXT2 shared atlas). " +
                         "spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.");
            else
                GD.Print("[BuffIconCatalog] stateicon.dds loaded as null (format unsupported).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BuffIconCatalog] stateicon.dds load failed: {ex.Message}");
            _atlasTexture = null;
        }

        return _atlasTexture;
    }
}