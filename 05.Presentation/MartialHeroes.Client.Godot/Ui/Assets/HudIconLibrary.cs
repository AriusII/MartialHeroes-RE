using System.Buffers.Binary;
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Assets;

public sealed class HudIconLibrary : IDisposable
{
    public const int SkillIconW = 23;
    public const int SkillIconH = 23;

    public const int BuffOriginStep = 25;

    public const int BuffIconDrawW = 21;
    public const int BuffIconDrawH = 21;

    private const string SkillIconTxtPath = "data/ui/skillicon/skillicon.txt";

    private const string TextureListPath = "data/item/texturelist.txt";

    private const string BuffSheetPath = "data/ui/skillicon/stateicon.dds";


    private readonly RealClientAssets? _assets;
    private readonly HudAtlasLibrary _atlas;

    private readonly Dictionary<int, ImageTexture?> _itemCache = new();

    private Dictionary<uint, (uint SpriteX, uint SpriteY)>? _buffPositions;
    private bool _buffPositionsAttempted;

    private Texture2D? _buffSheet;
    private bool _buffSheetAttempted;

    private bool _disposed;

    private DoStanceTable? _doTable;
    private bool _doTableAttempted;
    private string? _doTablePath;

    private TextureListManifest? _itemManifest;
    private bool _itemManifestAttempted;

    private SkillIconManifest? _skillManifest;
    private bool _skillManifestAttempted;

    private Texture2D? _skillSheet;
    private bool _skillSheetAttempted;
    private string? _skillSheetPath;


    public HudIconLibrary(RealClientAssets? assets, HudAtlasLibrary atlas)
    {
        _assets = assets;
        _atlas = atlas;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _itemCache.Clear();
    }


    public void ActivateSkillStance(int jobId, int kindId, int classStanceRef, string doVfsPath)
    {
        _doTable = null;
        _doTableAttempted = false;
        _doTablePath = doVfsPath;

        _skillSheet = null;
        _skillSheetAttempted = false;

        var manifest = EnsureSkillManifest();
        if (manifest is not null)
        {
            var entry = manifest.GetEntry(classStanceRef, jobId, kindId);
            _skillSheetPath = entry?.IconSheetPath;
        }
    }

    public AtlasTexture? GetSkillIcon(uint slotIndex)
    {
        var table = EnsureDoTable();
        if (table is null) return null;

        var rec = table.GetBySlotIndex(slotIndex);
        if (rec is null) return null;

        if (rec.IconSrcX < 0 || rec.IconSrcY < 0) return null;

        var sheet = EnsureSkillSheet();
        if (sheet is null) return null;

        if (rec.IconSrcX + SkillIconW > 512 || rec.IconSrcY + SkillIconH > 512) return null;

        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(rec.IconSrcX, rec.IconSrcY, SkillIconW, SkillIconH),
            FilterClip = true
        };
    }


    public ImageTexture? GetItemIcon(int texId)
    {
        if (_itemCache.TryGetValue(texId, out var cached))
            return cached;

        var manifest = EnsureItemManifest();
        if (manifest is null)
        {
            _itemCache[texId] = null;
            return null;
        }

        var entry = manifest.GetById(texId);
        if (entry is null)
        {
            _itemCache[texId] = null;
            return null;
        }

        ImageTexture? tex = null;
        if (_assets is not null)
            try
            {
                tex = _assets.LoadTexture(entry.VfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[HudIconLibrary] GetItemIcon(texId={texId}, '{entry.VfsPath}'): {ex.Message}");
            }

        _itemCache[texId] = tex;
        return tex;
    }


    public AtlasTexture? GetBuffIcon(uint buffId)
    {
        var positions = EnsureBuffPositions();
        if (positions is null) return null;

        if (!positions.TryGetValue(buffId, out (uint SpriteX, uint SpriteY) pos)) return null;

        var sheet = EnsureBuffSheet();
        if (sheet is null) return null;

        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2((int)pos.SpriteX, (int)pos.SpriteY, BuffIconDrawW, BuffIconDrawH),
            FilterClip = true
        };
    }


    private SkillIconManifest? EnsureSkillManifest()
    {
        if (_skillManifestAttempted) return _skillManifest;
        _skillManifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(SkillIconTxtPath);
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
            var raw = _assets.GetRaw(_doTablePath);
            if (raw.IsEmpty)
            {
                GD.Print($"[HudIconLibrary] {_doTablePath} absent — skill icon coords unavailable.");
                return null;
            }

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
            var raw = _assets.GetRaw(TextureListPath);
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

        _buffSheet = _atlas.GetByPath(BuffSheetPath);
        return _buffSheet;
    }

    private Dictionary<uint, (uint, uint)>? EnsureBuffPositions()
    {
        if (_buffPositionsAttempted) return _buffPositions;
        _buffPositionsAttempted = true;

        if (_assets is null) return null;

        const string BuffXdbPath = "data/script/buff_icon_position.xdb";
        try
        {
            var raw = _assets.GetRaw(BuffXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudIconLibrary] buff_icon_position.xdb absent — buff icons unavailable.");
                return null;
            }

            var span = raw.Span;
            const int Stride = 12;
            var count = span.Length / Stride;
            _buffPositions = new Dictionary<uint, (uint, uint)>(count);

            for (var i = 0; i < count; i++)
            {
                var offset = i * Stride;
                var buffId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
                var spriteX = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
                var spriteY = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 8, 4));
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
}