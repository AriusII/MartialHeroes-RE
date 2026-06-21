using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
///     Runtime background-texture pool catalogue: resolves a terrain / building / water texture
///     pool slot to its on-disk <c>.dds</c> path. This is the <b>runtime form</b> the original client
///     consumes — it is built from the binary <c>data/map000/texture/bgtexture.lst</c>, NOT from the
///     human-readable <c>bgtexture.txt</c> text mirror (which is absent from a real packed
///     <c>data.vfs</c>).
/// </summary>
/// <remarks>
///     spec: Docs/RE/specs/asset_linkages.md §5 — the GLOBAL TEXTURE POOL: the runtime terrain-texture
///     index is the BINARY <c>bgtexture.lst</c> under <c>data/map000/texture/</c>; <c>bgtexture.txt</c>
///     is an authoring sidecar the client never reads. The loader opens <c>data/map000/texture/bgtexture.lst</c>,
///     builds an index-keyed pool, and resolves each texture as <c>data/map000/texture/&lt;rel&gt;.dds</c>.
///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected, anchor 263bd994) — the pool
///     slot is the 0-based <c>.lst</c> record index, and the <c>.map</c> <c>intTexId</c> IS that slot used
///     DIRECTLY (NO -1); the <c>data/map000/texture/</c> prefix and the <c>.dds</c> extension are added at
///     runtime, not stored in the record. CONFIRMED (CYCLE 1, no drift).
///     <para>
///         <b>Indexing model (CONFIRMED).</b> The pool index equals the record's on-disk position.
///         Every record consumes a slot (the loader steps one pool element per record regardless of its
///         kind byte). A record whose relpath is empty resolves to <see langword="null" /> here — the
///         slot index is still occupied so the numbering of later records is never shifted.
///         spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — every record occupies a slot
///         (stride 48 bytes, flat array, no gaps). CONFIRMED.
///         spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — the kind byte gates a single binary
///         branch (<c>== 0x01</c> STATIC vs <c>!= 0x01</c> NON-STATIC); it does NOT change the slot index.
///         CODE-CONFIRMED (CYCLE 1).
///     </para>
///     <para>
///         <b>Two known terrain instances.</b> The terrain texture repository under
///         <c>data/map000/texture/</c> is global — it serves <i>all</i> areas. The sibling
///         <c>data/effect/texture/bgtexture.lst</c> is the effect-texture pool; it shares this exact
///         format but uses the <c>data/effect/texture/</c> prefix instead (see
///         <see cref="ResolveTexturePath(int, string)" />).
///         spec: Docs/RE/formats/bgtexture_lst.md §Identification — two instances, global terrain pool.
///     </para>
///     <para>This is a pure mapping helper — ZERO rendering / engine dependencies.</para>
/// </remarks>
public sealed class BgTextureCatalog
{
    /// <summary>
    ///     The on-disk texture directory the terrain pool resolves against. The original client
    ///     hardcodes this prefix at the call site (it is not stored in the <c>.lst</c> record).
    ///     spec: Docs/RE/specs/asset_linkages.md §5 — runtime path
    ///     <c>data/map000/texture/&lt;rel&gt;.dds</c>; the terrain pool is GLOBAL under
    ///     <c>map000</c> for all areas. CONFIRMED.
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Identification — terrain instance prefix; the prefix is
    ///     not stored in the record, added at runtime.
    /// </summary>
    public const string TerrainTextureDir = "data/map000/texture/";

    /// <summary>
    ///     The on-disk texture directory for the sibling effect-texture pool
    ///     (<c>data/effect/texture/bgtexture.lst</c>), which shares this exact format.
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Identification — effect instance prefix.
    /// </summary>
    public const string EffectTextureDir = "data/effect/texture/";

    /// <summary>The <c>.dds</c> extension the loader appends at runtime (not stored on disk).</summary>
    private const string DdsExtension = ".dds";

    // Index-keyed pool: slot[i] = relative texture path (no extension) for on-disk record i,
    // or null for an empty-relpath slot. The pool index equals the record's on-disk position
    // — see the indexing-model note in the type remarks.
    private readonly string?[] _relPathBySlot;

    private BgTextureCatalog(string?[] relPathBySlot)
    {
        _relPathBySlot = relPathBySlot;
    }

    /// <summary>
    ///     Number of pool slots (equals the <c>.lst</c> <c>record_count</c>, or the highest text-mirror
    ///     index + 1). Some slots may be empty (resolve to <see langword="null" />).
    /// </summary>
    public int SlotCount => _relPathBySlot.Length;

    /// <summary>
    ///     Builds the catalogue from the binary <c>bgtexture.lst</c> bytes delivered by the VFS — the
    ///     <b>runtime form</b> the original client consumes. The binary <c>bgtexture.lst</c> is the only
    ///     real source; the <c>bgtexture.txt</c> text mirror is an authoring sidecar the client never reads.
    ///     spec: Docs/RE/specs/asset_linkages.md §5 — global texture pool; .lst is the runtime artifact.
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Scope — ".lst BINARY is the file the loader consumes".
    /// </summary>
    /// <param name="lstBytes">Raw <c>bgtexture.lst</c> file bytes (the complete file).</param>
    /// <returns>An index-keyed catalogue mapping each pool slot to its texture relpath.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown by the underlying parser when the buffer is malformed (short header, declared count
    ///     out of the loader's <c>[1, 2000)</c> range, truncated body, or a body length that is not a
    ///     multiple of the 48-byte record stride).
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/bgtexture_lst.md — u32 count, 48-byte records, kind@+0, NUL-term
    ///     relpath char[47]@+1. CONFIRMED. The pool slot is the record's 0-based on-disk position.
    /// </remarks>
    public static BgTextureCatalog FromLst(ReadOnlyMemory<byte> lstBytes)
    {
        return FromLst(BgtextureLstParser.Parse(lstBytes));
    }

    /// <summary>
    ///     Builds the catalogue from an already-parsed <see cref="BgtextureLstCatalog" />.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — the pool slot is the record's
    ///     0-based on-disk index; a record with an empty relpath yields no texture for that slot.
    ///     CONFIRMED.
    /// </remarks>
    public static BgTextureCatalog FromLst(BgtextureLstCatalog parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // The pool index is the record's on-disk position; every record occupies a slot.
        // A record with an empty relpath leaves the slot empty (no texture path constructed)
        // but still holds its slot index so later records keep their positions.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes, flat array;
        //   slot index = registration position. CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — the 6 observed kind values
        //   (0x01, 0x02, 0x0A, 0x0B, 0x0C, 0x14); the kind byte gates dispatch only (== 0x01 STATIC
        //   vs != 0x01 NON-STATIC), it does NOT gate slot allocation. CODE-CONFIRMED (CYCLE 1).
        var slots = new string?[parsed.Count];
        foreach (var record in parsed.Records)
            // An empty relpath means no texture path can be constructed for this slot.
            // We key strictly by the record's on-disk Index so numbering never shifts.
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path char[47] @ +1;
            //   a zero-length relpath = no texture path for this slot.
            slots[record.Index] = record.RelPath.Length == 0
                ? null
                : record.RelPath;

        return new BgTextureCatalog(slots);
    }

    /// <summary>
    ///     Resolves a <b>0-based pool slot</b> — a <c>.map</c> <c>TEXTURES{}</c> <c>intTexId</c> used
    ///     DIRECTLY (or a <c>.ted</c> per-cell byte already mapped through the cell texture list) — to the
    ///     texture path <b>relative to the texture directory</b>, WITHOUT the <c>.dds</c> extension —
    ///     or <see langword="null" /> when the slot is empty / out of range.
    /// </summary>
    /// <param name="poolSlot">
    ///     The 0-based pool slot (== the <c>.lst</c> record index == the <c>.map</c> <c>intTexId</c>), used
    ///     directly with NO subtraction.
    /// </param>
    /// <returns>
    ///     The relative path (e.g. <c>"terrain/g3"</c>), or <see langword="null" /> for an empty-relpath
    ///     or out-of-range slot.
    /// </returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected, anchor 263bd994) — the
    ///     pool accessor reads <c>pool_base + stride * intTexId</c> with NO subtraction: the <c>.map</c>
    ///     <c>intTexId</c> IS the 0-based pool slot, used DIRECTLY. The earlier "1-based intTexId minus 1"
    ///     reading was WRONG and is REFUTED. CODE-CONFIRMED, CYCLE 1, no drift.
    ///     spec: Docs/RE/specs/asset_linkages.md §5.1 — terrain texture join restated; intTexId → pool slot.
    ///     <para>
    ///         The ONE legitimate <c>-1</c> in the terrain chain is on the <c>.ted</c> per-cell byte, NOT here:
    ///         the byte is clamped to <c>[1, count]</c> (both <c>&lt;1</c> and <c>&gt;count</c> → 1) and indexes
    ///         the cell texture list as <c>perCellTexList[byte - 1]</c>, yielding the <c>intTexId</c> passed to
    ///         this method directly. That clamp + <c>-1</c> are the render-domain consumer's job, not this
    ///         catalogue's. spec: Docs/RE/formats/terrain.md §5.6 — Ted_ResolvePatchTextures.
    ///     </para>
    /// </remarks>
    public string? ResolveRelativePath(int poolSlot)
    {
        return (uint)poolSlot < (uint)_relPathBySlot.Length ? _relPathBySlot[poolSlot] : null;
    }

    /// <summary>
    ///     Resolves a <b>0-based pool slot</b> to its full VFS texture path,
    ///     <c>&lt;textureDir&gt;&lt;rel&gt;.dds</c> — or <see langword="null" /> when the slot is empty
    ///     / out of range.
    /// </summary>
    /// <param name="poolSlot">The 0-based pool slot (see <see cref="ResolveRelativePath" />).</param>
    /// <param name="textureDir">
    ///     The texture directory prefix; use <see cref="TerrainTextureDir" /> for the terrain pool or
    ///     <see cref="EffectTextureDir" /> for the effect pool. Must end with a path separator.
    /// </param>
    /// <returns>
    ///     The full VFS path (e.g. <c>"data/map000/texture/terrain/g3.dds"</c>), or
    ///     <see langword="null" /> for an empty / out-of-range slot.
    /// </returns>
    /// <remarks>
    ///     spec: Docs/RE/specs/asset_linkages.md §5 — runtime path <c>data/map000/texture/&lt;rel&gt;.dds</c>;
    ///     the prefix and extension are added at runtime (not stored in the record). CONFIRMED.
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path is stored WITHOUT the
    ///     <c>.dds</c> extension and WITHOUT the texture directory prefix. CONFIRMED.
    /// </remarks>
    public string? ResolveTexturePath(int poolSlot, string textureDir = TerrainTextureDir)
    {
        ArgumentNullException.ThrowIfNull(textureDir);
        var rel = ResolveRelativePath(poolSlot);
        return rel is null ? null : textureDir + rel + DdsExtension;
    }
}