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
///     spec: Docs/RE/specs/asset_pipeline.md §3 chain B — the runtime terrain-texture index is the
///     BINARY <c>bgtexture.lst</c>; <c>bgtexture.txt</c> is absent from the image. The loader opens
///     <c>data/map000/texture/</c> + <c>bgtexture.lst</c>, builds an index-keyed pool, and resolves
///     each texture as <c>data/map000/texture/&lt;rel&gt;.dds</c>. CONFIRMED.
///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected, 263bd994) — the pool slot is
///     the 0-based <c>.lst</c> record index, and the <c>.map</c> <c>intTexId</c> IS that slot used DIRECTLY
///     (NO -1; the accessor reads <c>pool[0]+stride*intTexId</c> at IDA 0x445833 / 0x44a46d); the path is
///     <c>data/map000/texture/&lt;rel_path&gt;.dds</c> (the <c>data/map000/texture/</c> prefix and the
///     <c>.dds</c> extension are added at runtime, not stored in the record). CONFIRMED.
///     <para>
///         <b>Indexing model (CONFIRMED).</b> The pool index equals the record's on-disk position.
///         Every record consumes a slot (the loader steps one pool element per record regardless of its
///         kind byte). Records whose <c>kind</c> byte is <c>0</c> are <b>skipped at build time</b> — no
///         texture path is constructed for them — but they still occupy their slot index, so the numbering
///         of later records is never shifted. Such empty slots resolve to <see langword="null" /> here.
///         spec: Docs/RE/specs/asset_pipeline.md §3 chain B — "Kind selector: … 0 ⇒ slot skipped (no
///         element built)"; the loader builds one pool element per record (index stored on the element),
///         so the index is the registration position. CONFIRMED.
///         spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — the kind byte gates the pool init path
///         (<c>== 0x01</c> animated vs the static path) but does not change the slot index. CONFIRMED.
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
    ///     spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime path
    ///     <c>data/map000/texture/&lt;rel&gt;.dds</c>; the terrain pool is global under
    ///     <c>map000</c> for all areas. CONFIRMED.
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
    // or null for an empty / kind-0 / out-of-range slot. The pool index is the registration
    // position — see the indexing-model note in the type remarks.
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
    ///     real source; the loose-tree <c>bgtexture.txt</c> text mirror is absent from a real packed
    ///     <c>data.vfs</c>. spec: Docs/RE/specs/asset_pipeline.md §3 chain B.
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
    ///     0-based on-disk index; a record with an empty relpath (a kind-0 / skipped slot) yields
    ///     no texture. CONFIRMED.
    /// </remarks>
    public static BgTextureCatalog FromLst(BgtextureLstCatalog parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // The pool index is the record's on-disk position; every record occupies a slot. A record
        // whose kind byte is 0 is built-skipped (no element) — represented here as an empty slot —
        // but it still holds its slot index, so later records keep their positions.
        // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — slot index = registration position;
        //   kind 0 ⇒ no element built. CONFIRMED.
        var slots = new string?[parsed.Count];
        foreach (var record in parsed.Records)
            // A kind-0 record (or one with an empty relpath) leaves the slot empty: the original
            // loader skips path construction for it (its kind byte is 0), and the slot resolves to
            // no texture. We key strictly by the record's on-disk Index so numbering never shifts.
            // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — "0 ⇒ slot skipped (no element built)".
            slots[record.Index] = record.KindRaw == 0 || record.RelPath.Length == 0
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
    ///     The relative path (e.g. <c>"terrain/g3"</c>), or <see langword="null" /> for an empty,
    ///     kind-0, or out-of-range slot.
    /// </returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected, 263bd994) — the pool
    ///     accessor reads <c>pool[0]+stride*intTexId</c> with NO subtraction (IDA 0x445833 / 0x44a46d /
    ///     store 0x44b267): the <c>.map</c> <c>intTexId</c> IS the 0-based pool slot. The earlier
    ///     "1-based intTexId minus 1" reading was WRONG and is REFUTED.
    ///     <para>
    ///         The ONE legitimate <c>-1</c> in the terrain chain is on the <c>.ted</c> per-cell byte, NOT here:
    ///         the byte is clamped to <c>[1, count]</c> (both <c>&lt;1</c> and <c>&gt;count</c> → 1) and indexes
    ///         the cell texture list as <c>perCellTexList[byte - 1]</c>, yielding the <c>intTexId</c> passed to
    ///         this method directly. That clamp + <c>-1</c> are the render-domain consumer's job, not this
    ///         catalogue's. spec: Docs/RE/formats/terrain.md §5.6 — Ted_ResolvePatchTextures (IDA 0x44b296).
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
    ///     spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime path
    ///     <c>data/map000/texture/&lt;rel&gt;.dds</c>; the prefix and extension are added at runtime.
    ///     CONFIRMED.
    /// </remarks>
    public string? ResolveTexturePath(int poolSlot, string textureDir = TerrainTextureDir)
    {
        ArgumentNullException.ThrowIfNull(textureDir);
        var rel = ResolveRelativePath(poolSlot);
        return rel is null ? null : textureDir + rel + DdsExtension;
    }
}