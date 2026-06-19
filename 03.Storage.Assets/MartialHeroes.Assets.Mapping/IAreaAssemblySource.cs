using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Engine-free port the <c>AreaComposer</c> (Stage B) consumes to fetch raw per-cell bytes and
/// per-area tables WITHOUT knowing the VFS implementation.
/// </summary>
/// <remarks>
/// This is the composition contract between the layer-03 AreaComposer and the layer-04
/// infrastructure that owns the VFS.  Consumers receive BCL types and existing Parsers catalogue
/// types only — no VFS type leakage, no Godot types, ZERO rendering dependencies.
/// <para>
/// Every member below is cited directly from the governing specs:
/// spec: Docs/RE/specs/assembly_graph.md §1 — World-boot chain (per area, then per cell).
/// spec: Docs/RE/formats/area_inventory.md §1A — area → cell fan-out + per-cell open order.
/// </para>
/// </remarks>
public interface IAreaAssemblySource
{
    /// <summary>
    /// The area identifier this source represents.
    /// Corresponds to <c>areaId</c> in the streaming ring and cell-key arithmetic.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/area_inventory.md §1.1 — area identifier (0..300 range);
    ///   the directory root for an area is <c>data/map&lt;NNN&gt;/</c>.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "area id → d&lt;NNN&gt;.lst cell-key set".
    /// </remarks>
    int AreaId { get; }

    /// <summary>
    /// The full cell-key membership set for this area, loaded from <c>d&lt;NNN&gt;.lst</c>.
    /// A cell key is the packed value <c>mapZ + 100000 · mapX</c>; a cell may only be loaded
    /// if its key is in this set.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/area_inventory.md §1A.1 — "a cell may only be loaded if its key is in that set".
    /// spec: Docs/RE/formats/area_inventory.md §1A.2 — "cell_key = mapZ + 100000 * mapX". CODE-CONFIRMED.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "area id → d&lt;NNN&gt;.lst cell-key set".
    /// </remarks>
    IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys { get; }

    /// <summary>
    /// The global terrain/building texture catalogue, built from
    /// <c>data/map000/texture/bgtexture.lst</c>.  Textures are global under <c>map000</c>
    /// for ALL areas — this catalogue is shared across the whole world.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "textures are global under map000 for every area".
    /// spec: Docs/RE/formats/bgtexture_lst.md §Identification —
    ///   "data/map000/texture/bgtexture.lst — terrain background textures (global for all areas)".
    /// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
    ///   "intTexId IS the 0-based record index, used DIRECTLY — NO -1".  CONFIRMED.
    /// </remarks>
    BgtextureLstCatalog TerrainTextureCatalog { get; }

    /// <summary>
    /// Opens a per-cell blob by <paramref name="mapX"/> / <paramref name="mapZ"/> grid coordinates
    /// and file <paramref name="extension"/>, returning its raw VFS bytes as a
    /// <see cref="ReadOnlyMemory{T}"/> slice.
    /// Returns <see langword="false"/> when the file is absent; missing <c>.bud</c>, <c>.sod</c>,
    /// or <c>.mud</c> files are NOT errors (see area_inventory §3 — presence is per-area optional).
    /// </summary>
    /// <param name="mapX">X grid index of the cell.</param>
    /// <param name="mapZ">Z grid index of the cell.</param>
    /// <param name="extension">
    /// File extension INCLUDING the leading dot, e.g. <c>".mud"</c>, <c>".map"</c>,
    /// <c>".ted"</c>, <c>".sod"</c>, <c>".bud"</c>, <c>".fx1"</c>...<c>".fx7"</c>, <c>".exd"</c>.
    /// </param>
    /// <param name="bytes">
    /// When <see langword="true"/> is returned: the raw bytes of the file as a zero-copy VFS slice.
    /// The caller must not outlive the <see cref="IAreaAssemblySource"/> instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the file is present and bytes are valid;
    /// <see langword="false"/> when absent.
    /// </returns>
    /// <remarks>
    /// spec: Docs/RE/formats/area_inventory.md §1A.4 — per-cell open order: .mud → .gad (stub) → .map;
    ///   sub-assets (.ted/.up/.bud/.sod/.fx1..fx7/.exd) are opened INSIDE the .map parse via
    ///   DATAFILE tokens, not by a cell-key→filename rule.  CODE-CONFIRMED.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — per-cell find/load (the streaming gate).
    /// </remarks>
    bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes);

    /// <summary>
    /// Opens a sub-asset blob by its full VFS logical path (a <c>DATAFILE</c> token read from
    /// the <c>.map</c> parse), returning its raw bytes.
    /// Sub-asset filenames inside <c>.map</c> are DATA-DRIVEN — they are literal <c>DATAFILE</c>
    /// tokens, NOT derived from a cell-key → filename rule.
    /// Returns <see langword="false"/> when the file is absent.
    /// </summary>
    /// <param name="vfsLogicalPath">
    /// Full VFS logical path of the sub-asset, exactly as it appears in the <c>DATAFILE</c> token
    /// of the <c>.map</c> text (e.g. <c>"data/map002/dat/d002x10003z10001.ted"</c>).
    /// </param>
    /// <param name="bytes">
    /// When <see langword="true"/> is returned: the raw bytes of the file as a zero-copy VFS slice.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the file is present; <see langword="false"/> when absent.
    /// </returns>
    /// <remarks>
    /// spec: Docs/RE/formats/area_inventory.md §1A.4 — "sub-asset filenames are DATA-DRIVEN — read
    ///   as literal DATAFILE tokens from the .map text — NOT derived from a cell-key → filename rule."
    ///   CODE-CONFIRMED.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — "the .map parse fans the sub-assets; DATAFILE tokens
    ///   scoped to each section pull .ted/.sod/.bud/.up/.fx1-7/.exd".
    /// </remarks>
    bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes);
}