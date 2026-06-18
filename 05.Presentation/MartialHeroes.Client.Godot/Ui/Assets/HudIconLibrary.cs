// Ui/Assets/HudIconLibrary.cs
//
// Icon library for the shared HUD substrate.
//
// Two icon families:
//   1. Skill icons  — per-skill 23×23 AtlasTexture slices on the (job, kind) 512×512 DDS sheet.
//      Source-rect is DATA-DRIVEN per skill from the per-class .do stance file (+0x18/+0x1C).
//      spec: Docs/RE/formats/ui_manifests.md §2.6  — "fixed 23×23 cell, data-driven UV": CODE-CONFIRMED.
//      spec: Docs/RE/formats/ui_manifests.md §2.7  — per-class .do records: CODE-CONFIRMED + SAMPLE-VERIFIED.
//      spec: Docs/RE/formats/ui_manifests.md §2.4  — skillicon.txt 12 entries SAMPLE-VERIFIED.
//
//   2. Item icons   — whole-texture blit per item; no sub-rect; atlas not used.
//      spec: Docs/RE/formats/ui_manifests.md §10   — texturelist.txt grammar CODE-CONFIRMED.
//      spec: Docs/RE/formats/ui_manifests.md §10.5 — whole-texture blit CODE-CONFIRMED.
//
//   3. Buff icons   — 25-step sprite-sheet origin from buff_icon_position.xdb;
//      draw-cell size sprite-sheet-pending (21×21 flagged, not confirmed).
//      spec: Docs/RE/formats/xdb_tables.md §2      — stride 12B, {buff_id, sprite_x, sprite_y} CONFIRMED.
//
// Offline / VFS-absent: all methods return null. Callers must handle null gracefully.
//
// spec: Docs/RE/formats/ui_manifests.md §2, §10.
// spec: Docs/RE/formats/xdb_tables.md §2.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Ui.Assets;

/// <summary>
/// Shared HUD icon library — resolves skill, item, and buff icon textures.
///
/// <para>One instance per session, created by the composition root (ClientContext).</para>
/// <para>All methods return <see langword="null"/> when the VFS is unavailable or an id is absent.
/// Callers MUST handle null and render nothing.</para>
///
/// spec: Docs/RE/formats/ui_manifests.md §2 (skill icons), §10 (item icons).
/// spec: Docs/RE/formats/xdb_tables.md §2 (buff icon positions).
/// </summary>
public sealed class HudIconLibrary : IDisposable
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // -------------------------------------------------------------------------

    // Skill icon cell size — fixed 23×23 pixels on the 512×512 skill icon sheet.
    // spec: Docs/RE/formats/ui_manifests.md §2.6 — "fixed 23×23 pixel cell": CODE-CONFIRMED.
    public const int SkillIconW = 23; // spec: Docs/RE/formats/ui_manifests.md §2.6
    public const int SkillIconH = 23; // spec: Docs/RE/formats/ui_manifests.md §2.6

    // Buff icon origin spacing — 25 pixels on both axes on the buff-icon sprite sheet.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "origin spacing 25 CORRECTED (sample-verified)".
    public const int BuffOriginStep = 25; // spec: Docs/RE/formats/xdb_tables.md §2

    // Buff icon draw-cell size — 21×21 provisionally; needs sprite-sheet adjudication.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "draw-cell 21×21 sprite-sheet-pending".
    // TODO(spec): runtime-only — confirm draw-cell size from the stateicon.dds sprite sheet.
    public const int BuffIconDrawW = 21; // spec: Docs/RE/formats/xdb_tables.md §2 (sprite-sheet-pending)
    public const int BuffIconDrawH = 21; // spec: Docs/RE/formats/xdb_tables.md §2 (sprite-sheet-pending)

    // VFS paths.
    // spec: Docs/RE/formats/ui_manifests.md §2.2 — skillicon.txt: PARSER-CONFIRMED.
    private const string SkillIconTxtPath = "data/ui/skillicon/skillicon.txt";

    // spec: Docs/RE/formats/ui_manifests.md §10.1 — texturelist.txt: CODE-CONFIRMED.
    private const string TextureListPath = "data/item/texturelist.txt";

    // Buff-icon sprite sheet loaded via uitex.txt id 0026.
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — id 0026 = "data/ui/skillicon/stateicon.dds" (512×512).
    private const string BuffSheetPath = "data/ui/skillicon/stateicon.dds";

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    private readonly RealClientAssets? _assets;
    private readonly HudAtlasLibrary _atlas;

    // Skill icon: skillicon.txt manifest.
    private SkillIconManifest? _skillManifest;
    private bool _skillManifestAttempted;

    // Skill icon: per-stance .do table (Map B keyed by slotIndex).
    private DoStanceTable? _doTable;
    private bool _doTableAttempted;
    private string? _doTablePath;

    // Skill icon: cached full-sheet texture.
    private Texture2D? _skillSheet;
    private bool _skillSheetAttempted;
    private string? _skillSheetPath;

    // Item icon: texturelist.txt manifest.
    private TextureListManifest? _itemManifest;
    private bool _itemManifestAttempted;

    // Item icon: per-tex_id texture cache.
    private readonly Dictionary<int, ImageTexture?> _itemCache = new();

    // Buff icon: cached sprite sheet.
    private Texture2D? _buffSheet;
    private bool _buffSheetAttempted;

    // Buff icon position table: buff_id → (sprite_x, sprite_y).
    // Loaded from buff_icon_position.xdb; stored in a dictionary for O(1) lookup.
    // spec: Docs/RE/formats/xdb_tables.md §2 — non-contiguous buff_id; must dict-lookup.
    private Dictionary<uint, (uint SpriteX, uint SpriteY)>? _buffPositions;
    private bool _buffPositionsAttempted;

    private bool _disposed;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a HudIconLibrary. The <paramref name="atlas"/> is used for sheet loading
    /// (shared caching); pass the session-singleton <see cref="HudAtlasLibrary"/>.
    /// Pass <paramref name="assets"/> = <see langword="null"/> for offline mode.
    /// </summary>
    public HudIconLibrary(RealClientAssets? assets, HudAtlasLibrary atlas)
    {
        _assets = assets;
        _atlas = atlas;
    }

    // -------------------------------------------------------------------------
    // Skill icons
    // -------------------------------------------------------------------------

    /// <summary>
    /// Activates the skill icon set for the given character class-stance combination.
    ///
    /// <para>Call once per session when the player's class and stance are known (e.g. on
    /// character select or class-change). Loads the .do stance table and resolves the
    /// 512×512 DDS sheet from skillicon.txt.</para>
    ///
    /// <para>jobId: 1=Musa, 2=Assassin, 3=Wizard, 4=Monk.
    /// kindId: 1=jung, 2=sa, 3=ma.</para>
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "active .do file selected at character
    ///   load time by class×stance dispatcher": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.3 — skillicon.txt columns.
    /// </summary>
    public void ActivateSkillStance(int jobId, int kindId, int classStanceRef, string doVfsPath)
    {
        // Reset stance state so next call to EnsureSkillSheet re-resolves.
        _doTable = null;
        _doTableAttempted = false;
        _doTablePath = doVfsPath;

        _skillSheet = null;
        _skillSheetAttempted = false;

        // Pre-resolve the sheet path from the skillicon.txt manifest.
        SkillIconManifest? manifest = EnsureSkillManifest();
        if (manifest is not null)
        {
            SkillIconEntry? entry = manifest.GetEntry(classStanceRef, jobId, kindId);
            _skillSheetPath = entry?.IconSheetPath;
        }
    }

    /// <summary>
    /// Returns a 23×23 <see cref="AtlasTexture"/> for the skill at the given
    /// <paramref name="slotIndex"/> (Map B key from the active .do stance table),
    /// or <see langword="null"/> when unavailable.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 cell at (iconSrcX, iconSrcY)".
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map B keyed by slotIndex (+0x08)".
    /// </summary>
    public AtlasTexture? GetSkillIcon(uint slotIndex)
    {
        DoStanceTable? table = EnsureDoTable();
        if (table is null) return null;

        DoStanceRecord? rec = table.GetBySlotIndex(slotIndex);
        if (rec is null) return null;

        // Negative icon coordinates indicate no-icon / padding rows.
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — authored data; some non-multiples of 23.
        if (rec.IconSrcX < 0 || rec.IconSrcY < 0) return null;

        Texture2D? sheet = EnsureSkillSheet();
        if (sheet is null) return null;

        // Stay within the 512×512 sheet boundary.
        // spec: Docs/RE/formats/ui_manifests.md §2.4 — "512×512 SAMPLE-VERIFIED".
        if (rec.IconSrcX + SkillIconW > 512 || rec.IconSrcY + SkillIconH > 512) return null;

        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(rec.IconSrcX, rec.IconSrcY, SkillIconW, SkillIconH),
            FilterClip = true,
        };
    }

    // -------------------------------------------------------------------------
    // Item icons
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the whole-texture <see cref="ImageTexture"/> for the item identified by
    /// <paramref name="texId"/> (texturelist.txt id), or <see langword="null"/> when unavailable.
    ///
    /// <para>Each item icon is a whole-texture blit — the entire DDS at native dimensions.
    /// No atlas sub-rect is used.</para>
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
    /// </summary>
    public ImageTexture? GetItemIcon(int texId)
    {
        if (_itemCache.TryGetValue(texId, out ImageTexture? cached))
            return cached;

        TextureListManifest? manifest = EnsureItemManifest();
        if (manifest is null)
        {
            _itemCache[texId] = null;
            return null;
        }

        TextureListEntry? entry = manifest.GetById(texId);
        if (entry is null)
        {
            _itemCache[texId] = null;
            return null;
        }

        ImageTexture? tex = null;
        if (_assets is not null)
        {
            try
            {
                // LoadTexture probes magic bytes (handles .dds-named TGA files).
                tex = _assets.LoadTexture(entry.VfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[HudIconLibrary] GetItemIcon(texId={texId}, '{entry.VfsPath}'): {ex.Message}");
            }
        }

        _itemCache[texId] = tex;
        return tex;
    }

    // -------------------------------------------------------------------------
    // Buff icons
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an <see cref="AtlasTexture"/> for the buff identified by
    /// <paramref name="buffId"/>, sliced from the stateicon.dds sprite sheet at the
    /// (sprite_x, sprite_y) origin from buff_icon_position.xdb.
    ///
    /// <para>Draw cell size is 21×21 (sprite-sheet-pending — see spec note). Use
    /// <see cref="BuffIconDrawW"/>/<see cref="BuffIconDrawH"/> constants.</para>
    ///
    /// Returns <see langword="null"/> when the VFS is offline, buff_id absent, or
    /// the sheet cannot be loaded.
    ///
    /// spec: Docs/RE/formats/xdb_tables.md §2 — stride 12B {u32 buff_id, u32 sprite_x, u32 sprite_y}.
    /// spec: Docs/RE/formats/xdb_tables.md §2 — origin spacing 25 CONFIRMED; draw-cell sprite-sheet-pending.
    /// spec: Docs/RE/formats/ui_manifests.md §1.4 — id 0026 = stateicon.dds (512×512 SAMPLE-VERIFIED).
    /// </summary>
    public AtlasTexture? GetBuffIcon(uint buffId)
    {
        Dictionary<uint, (uint, uint)>? positions = EnsureBuffPositions();
        if (positions is null) return null;

        if (!positions.TryGetValue(buffId, out (uint SpriteX, uint SpriteY) pos)) return null;

        Texture2D? sheet = EnsureBuffSheet();
        if (sheet is null) return null;

        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2((int)pos.SpriteX, (int)pos.SpriteY, BuffIconDrawW, BuffIconDrawH),
            FilterClip = true,
        };
    }

    // -------------------------------------------------------------------------
    // Lazy loaders
    // -------------------------------------------------------------------------

    private SkillIconManifest? EnsureSkillManifest()
    {
        if (_skillManifestAttempted) return _skillManifest;
        _skillManifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(SkillIconTxtPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudIconLibrary] skillicon.txt absent — skill icons unavailable.");
                return null;
            }

            _skillManifest = SkillIconManifestParser.Parse(raw);
            GD.Print($"[HudIconLibrary] skillicon.txt loaded: {_skillManifest.Count} entries. " +
                     "spec: Docs/RE/formats/ui_manifests.md §2.4 SAMPLE-VERIFIED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudIconLibrary] skillicon.txt parse failed: {ex.Message}");
            _skillManifest = null;
        }

        return _skillManifest;
    }

    private DoStanceTable? EnsureDoTable()
    {
        if (_doTableAttempted) return _doTable;
        _doTableAttempted = true;

        if (_assets is null || _doTablePath is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(_doTablePath);
            if (raw.IsEmpty)
            {
                GD.Print($"[HudIconLibrary] {_doTablePath} absent — skill icon coords unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/ui_manifests.md §2.7 — 116-byte records, no header.
            _doTable = DoStanceParser.Parse(raw);
            GD.Print($"[HudIconLibrary] .do table '{_doTablePath}' loaded: " +
                     $"{_doTable.Records.Count} records. " +
                     "spec: Docs/RE/formats/ui_manifests.md §2.7 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudIconLibrary] .do table '{_doTablePath}' parse failed: {ex.Message}");
            _doTable = null;
        }

        return _doTable;
    }

    private Texture2D? EnsureSkillSheet()
    {
        if (_skillSheetAttempted) return _skillSheet;
        _skillSheetAttempted = true;

        if (_skillSheetPath is null) return null;

        _skillSheet = _atlas.GetByPath(_skillSheetPath);
        return _skillSheet;
    }

    private TextureListManifest? EnsureItemManifest()
    {
        if (_itemManifestAttempted) return _itemManifest;
        _itemManifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(TextureListPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudIconLibrary] texturelist.txt absent — item icons unavailable.");
                return null;
            }

            _itemManifest = TextureListParser.Parse(raw);
            GD.Print($"[HudIconLibrary] texturelist.txt loaded: {_itemManifest.Count} entries. " +
                     "spec: Docs/RE/formats/ui_manifests.md §10 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudIconLibrary] texturelist.txt parse failed: {ex.Message}");
            _itemManifest = null;
        }

        return _itemManifest;
    }

    private Texture2D? EnsureBuffSheet()
    {
        if (_buffSheetAttempted) return _buffSheet;
        _buffSheetAttempted = true;

        // stateicon.dds is 512×512 — spec: Docs/RE/formats/ui_manifests.md §1.4 id 0026.
        _buffSheet = _atlas.GetByPath(BuffSheetPath);
        return _buffSheet;
    }

    private Dictionary<uint, (uint, uint)>? EnsureBuffPositions()
    {
        if (_buffPositionsAttempted) return _buffPositions;
        _buffPositionsAttempted = true;

        if (_assets is null) return null;

        // spec: Docs/RE/formats/xdb_tables.md §2 — "data/script/buff_icon_position.xdb".
        const string BuffXdbPath = "data/script/buff_icon_position.xdb";
        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(BuffXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudIconLibrary] buff_icon_position.xdb absent — buff icons unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/xdb_tables.md §2 — stride 12B, 134 records, no header.
            // Record layout: +0 u32 buff_id, +4 u32 sprite_x, +8 u32 sprite_y.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "non-contiguous buff_id; must dict-lookup".
            ReadOnlySpan<byte> span = raw.Span;
            const int Stride = 12; // spec: Docs/RE/formats/xdb_tables.md §2 — stride 12B CONFIRMED.
            int count = span.Length / Stride;
            _buffPositions = new Dictionary<uint, (uint, uint)>(count);

            for (int i = 0; i < count; i++)
            {
                int offset = i * Stride;
                uint buffId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
                uint spriteX = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
                uint spriteY = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 8, 4));
                _buffPositions[buffId] = (spriteX, spriteY);
            }

            GD.Print($"[HudIconLibrary] buff_icon_position.xdb loaded: {_buffPositions.Count} records. " +
                     "spec: Docs/RE/formats/xdb_tables.md §2 CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudIconLibrary] buff_icon_position.xdb parse failed: {ex.Message}");
            _buffPositions = null;
        }

        return _buffPositions;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _itemCache.Clear();
        // Godot Texture2D/ImageTexture objects are reference-counted; no manual free needed.
        // _assets and _atlas are owned by ClientContext; we do not dispose them here.
    }
}