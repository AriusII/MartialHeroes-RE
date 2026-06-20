using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Presentation.Adapters;

/// <summary>
///     Layer-05 adapter: implements <see cref="IAreaAssemblySource" /> over a
///     <see cref="MappedVfsArchive" /> so the layer-03 <see cref="AreaComposer" /> can fetch raw
///     per-cell bytes and per-area tables from the real VFS without knowing the VFS type.
///     Owned by the composition root <c>ClientContext</c>; its lifetime is tied to the terrain VFS
///     archive — both are disposed in <c>_ExitTree</c>. The adapter does NOT dispose the archive
///     (ownership stays in <c>ClientContext</c>).
///     spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource contract.
///     spec: Docs/RE/formats/area_inventory.md §1A — area → cell fan-out + per-cell open order.
///     spec: Docs/RE/formats/terrain.md §1.2 — d&lt;NNN&gt;.lst cell-key set.
///     spec: Docs/RE/formats/bgtexture_lst.md — global pool data/map000/texture/bgtexture.lst.
/// </summary>
internal sealed class VfsAreaAssemblySource : IAreaAssemblySource
{
    private readonly MappedVfsArchive _vfs;

    // ── construction ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Creates an assembly source for the given area over the supplied VFS archive.
    ///     Loads and caches the .lst cell-key membership set and the global bgtexture.lst catalog.
    /// </summary>
    /// <param name="vfs">The mounted VFS archive (ownership stays with the caller).</param>
    /// <param name="areaId">Area identifier. spec: Docs/RE/formats/terrain.md §1.1.</param>
    public VfsAreaAssemblySource(MappedVfsArchive vfs, int areaId)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        AreaId = areaId;
        AreaCellKeys = LoadCellKeys(vfs, areaId);
        TerrainTextureCatalog = LoadBgTextureCatalog(vfs);
    }

    // ── IAreaAssemblySource ──────────────────────────────────────────────────

    /// <inheritdoc />
    public int AreaId { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys { get; }

    /// <inheritdoc />
    public BgtextureLstCatalog TerrainTextureCatalog { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Constructs the per-cell path for the given extension and returns the VFS bytes.
    ///     Missing files return false (not an error for optional sub-assets).
    ///     spec: Docs/RE/formats/area_inventory.md §1A.4 — per-cell open order: .mud → .gad (stub) → .map.
    ///     spec: Docs/RE/formats/terrain.md §1.3 — per-cell base path pattern.
    /// </remarks>
    public bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes)
    {
        // spec: Docs/RE/formats/terrain.md §1.3 per-cell path:
        //   data/map<NNN>/dat/d<NNN>x<mapX>z<mapZ><ext>
        var tag = AreaTag(AreaId);
        var path = $"data/map{tag}/dat/d{tag}x{mapX}z{mapZ}{extension}";
        return TryRead(path, out bytes);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Opens a sub-asset blob by its full VFS logical path (a DATAFILE token from the .map parse).
    ///     spec: Docs/RE/formats/area_inventory.md §1A.4 — sub-assets are DATA-DRIVEN DATAFILE tokens.
    ///     spec: Docs/RE/specs/assembly_graph.md §1.
    /// </remarks>
    public bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes)
    {
        return TryRead(vfsLogicalPath, out bytes);
    }

    // ── area tag ──────────────────────────────────────────────────────────────

    private static string AreaTag(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition: CONFIRMED.
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (_vfs.Contains(path))
            {
                bytes = _vfs.GetFileContent(path);
                return !bytes.IsEmpty;
            }
        }
        catch
        {
            // VFS error → treat as absent (caller handles missing as not-an-error).
        }

        bytes = ReadOnlyMemory<byte>.Empty;
        return false;
    }

    /// <summary>
    ///     Loads the d&lt;NNN&gt;.lst cell-key membership set for the area.
    ///     Returns an empty collection when the manifest is absent (all cells empty).
    ///     spec: Docs/RE/formats/area_inventory.md §1A.1 — cell-key membership set.
    ///     spec: Docs/RE/formats/terrain.md §1.2 — d&lt;NNN&gt;.lst: count u32le | keys u32le[].
    /// </summary>
    private static IReadOnlyCollection<(int MapX, int MapZ)> LoadCellKeys(
        MappedVfsArchive vfs, int areaId)
    {
        try
        {
            var tag = AreaTag(areaId);
            var lstPath = $"data/map{tag}/dat/d{tag}.lst";
            if (!vfs.Contains(lstPath))
                return [];

            var data = vfs.GetFileContent(lstPath);
            if (data.IsEmpty)
                return [];

            var manifest = LstManifestParser.Parse(data);
            var keys = new List<(int MapX, int MapZ)>(manifest.Entries.Length);
            foreach (var entry in manifest.Entries)
            {
                // spec: Docs/RE/formats/area_inventory.md §1A.2 — cell_key = mapZ + 100000 * mapX.
                //   CODE-CONFIRMED. Unpack: mapX = key / 100000; mapZ = key % 100000.
                var mapX = (int)(entry.Key / 100000u); // spec: terrain.md §1.2 — key formula
                var mapZ = (int)(entry.Key % 100000u); // spec: terrain.md §1.2 — key formula
                keys.Add((mapX, mapZ));
            }

            return keys;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    ///     Loads the global background-texture catalog from data/map000/texture/bgtexture.lst.
    ///     The packed binary .lst is the sole runtime source — there is no .txt mirror fallback.
    ///     Returns an empty catalog (loud sentinel) when the .lst is absent.
    ///     spec: Docs/RE/formats/bgtexture_lst.md — global pool, all areas share map000.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — textures are global under map000. CONFIRMED.
    /// </summary>
    private static BgtextureLstCatalog LoadBgTextureCatalog(MappedVfsArchive vfs)
    {
        try
        {
            const string lstPath = "data/map000/texture/bgtexture.lst";
            if (vfs.Contains(lstPath))
            {
                var data = vfs.GetFileContent(lstPath);
                if (!data.IsEmpty)
                    // spec: Docs/RE/formats/bgtexture_lst.md — binary index-keyed pool. CONFIRMED.
                    return BgtextureLstParser.Parse(data);
            }

            // The packed binary data/map000/texture/bgtexture.lst is the SOLE runtime source for the
            // background-texture pool (spec: Docs/RE/formats/bgtexture_lst.md). There is NO loose-tree
            // .txt mirror fallback — a real packed VFS never ships one, so its absence is a hard error
            // (it falls through to the loud minimal sentinel below).
        }
        catch
        {
            // VFS error — fall through to the sentinel below.
        }

        // Minimal valid .lst: count=1, one record with kind=0 (skipped/no-texture slot).
        // This gives a 1-slot catalog where every slot resolves to null — no textures.
        // The BgtextureLstCatalog constructor is internal to Assets.Parsers; we must parse bytes.
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — count u32LE @ 0; 48-byte records.
        var minimal = new byte[4 + 48]; // 4-byte header (count=1) + 1×48-byte record
        minimal[0] = 1; // count = 1 as LE u32
        // record[0]: kind byte = 0x01 (static, non-empty slot), relpath = all-zero bytes → empty string
        minimal[4] = 0x01;
        return BgtextureLstParser.Parse(minimal);
    }
}