namespace MartialHeroes.Assets.Parsers.Models;

// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations

/// <summary>
/// Loader dispatch bucket derived from the <c>bgtexture.lst</c> <c>kind</c> byte.
/// The loader makes a SINGLE binary branch: <c>kind == 0x01</c> → static render-object;
/// <c>kind != 0x01</c> → scroll/animated render-object. The six-value <see cref="BgTextureKind"/>
/// enum is data-only (no per-value jump table in the loader).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "kind byte gates a single binary branch:
///   ==0x01 STATIC render-object, !=0x01 NON-STATIC (scroll/animated)". CODE-CONFIRMED, CYCLE 1.
/// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "the 6-value enum is DATA-ONLY and is
///   never re-branched in the loader (CONFIRMED, CYCLE 1)". Every pool entry joins the shared texture
///   scheduler pool regardless of kind.
/// </remarks>
public enum BgTextureRenderBucket
{
    /// <summary>
    /// <c>kind == 0x01</c> — STATIC render-object type (plain static ground-texture object,
    /// the default and majority).
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "kind==0x01 wires the STATIC render-object type".
    /// </summary>
    Static, // spec: bgtexture_lst.md §Enumerations — kind==0x01: STATIC render-object. CODE-CONFIRMED CYCLE 1.

    /// <summary>
    /// <c>kind != 0x01</c> — NON-STATIC (scroll / animated) render-object type.
    /// Covers all other kind values (0x02, 0x0A, 0x0B, 0x0C, 0x14, …).
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "kind!=0x01 wires the NON-STATIC render-object type".
    /// </summary>
    ScrollAnimated, // spec: bgtexture_lst.md §Enumerations — kind!=0x01: NON-STATIC render-object. CODE-CONFIRMED CYCLE 1.
}

/// <summary>
/// Material render-mode tag from the <c>bgtexture.lst</c> <c>kind</c> byte (record +0).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "kind (record +0) — material render-mode tag":
///   non-constant; at least six distinct values across 2,330 records; value→relpath-family
///   correlation is HIGH; exact engine rendering behaviour (scroll speed, sway, alpha-test, etc.)
///   is INFERRED from relpath families, NOT confirmed against the engine's shader/material table.
/// <para>
/// The earlier "animated vs. static boolean" reading is SUPERSEDED — a single bit could not produce
/// a value as high as 0x14.
/// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "earlier boolean reading is retired".
/// </para>
/// <para>
/// The <see cref="Unknown"/> sentinel covers any byte value not in the six-value observed set;
/// the raw byte is preserved in <see cref="BgtextureLstRecord.KindRaw"/>.
/// </para>
/// </remarks>
public enum BgTextureKind : byte
{
    /// <summary>
    /// 0x01 — Plain static ground tiles: stone, cliff, soil, generic terrain (the default).
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x01 = KIND_STATIC: HIGH.
    /// </summary>
    Static = 0x01, // spec: bgtexture_lst.md §Enumerations — KIND_STATIC: HIGH

    /// <summary>
    /// 0x02 — Water, lava, moss, wet surfaces: scrolling-UV / animated material.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x02 = KIND_SCROLL: HIGH.
    /// </summary>
    ScrollUv = 0x02, // spec: bgtexture_lst.md §Enumerations — KIND_SCROLL: HIGH

    /// <summary>
    /// 0x0A — Grass tiles.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x0A = KIND_GRASS: HIGH.
    /// </summary>
    Grass = 0x0A, // spec: bgtexture_lst.md §Enumerations — KIND_GRASS: HIGH

    /// <summary>
    /// 0x0B — Herb / plant tiles.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x0B = KIND_PLANT: HIGH.
    /// </summary>
    Plant = 0x0B, // spec: bgtexture_lst.md §Enumerations — KIND_PLANT: HIGH

    /// <summary>
    /// 0x0C — Tree-bark / trunk patch.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x0C = KIND_TREE_BARK: HIGH.
    /// </summary>
    TreeBark = 0x0C, // spec: bgtexture_lst.md §Enumerations — KIND_TREE_BARK: HIGH

    /// <summary>
    /// 0x14 — Dense tree foliage, branches, canopy.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind 0x14 = KIND_FOLIAGE: HIGH.
    /// </summary>
    Foliage = 0x14, // spec: bgtexture_lst.md §Enumerations — KIND_FOLIAGE: HIGH

    /// <summary>
    /// Sentinel for any kind byte value not in the six-value observed set.
    /// The raw byte is available on <see cref="BgtextureLstRecord.KindRaw"/>.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — values beyond the six observed: UNKNOWN.
    /// </summary>
    Unknown = 0xFF, // Sentinel — not an on-disk value; used when the raw byte is unrecognised.
}

/// <summary>
/// One record from a <c>bgtexture.lst</c> binary texture-index file.
/// Stride: 48 bytes  (1 byte kind + 47 bytes relpath).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.
/// <para>
/// The record is position-addressed: its 0-based index in the flat array is the pool slot
/// that terrain and building geometry reference.  There is no separate id field on disk.
/// spec: Docs/RE/formats/bgtexture_lst.md §Header layout —
///   "the binary has no id field; records are addressed by position": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class BgtextureLstRecord
{
    /// <summary>
    /// 0-based record index (the pool slot).  Derived from position in the flat array;
    /// not stored on disk.
    /// spec: Docs/RE/formats/bgtexture_lst.md — "records are addressed by position": CONFIRMED.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Raw kind byte at record offset +0.
    /// Observed values: 0x01, 0x02, 0x0A, 0x0B, 0x0C, 0x14 (across 2,330 records).
    /// Use <see cref="KindEnum"/> for a typed view; this raw byte is preserved for unknown values.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — kind u8 @ +0:
    ///   CONFIRMED (non-constant; 6 distinct values; render-mode tag, NOT boolean animated flag).
    /// </summary>
    public required byte KindRaw { get; init; }

    /// <summary>
    /// Loader dispatch bucket for this record: <see cref="BgTextureRenderBucket.Static"/> when
    /// <see cref="KindRaw"/> == <c>0x01</c>; <see cref="BgTextureRenderBucket.ScrollAnimated"/> otherwise.
    /// This is the actual binary branch the loader makes — the only pool-init dispatch.
    /// The six-value <see cref="KindEnum"/> is data-only and is never re-branched in the loader.
    /// Every entry joins the shared texture scheduler pool regardless of bucket.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "kind byte gates a single binary branch:
    ///   ==0x01 STATIC render-object, !=0x01 NON-STATIC (scroll/animated)". CODE-CONFIRMED, CYCLE 1.
    /// </remarks>
    public BgTextureRenderBucket RenderBucket =>
        KindRaw == 0x01 // spec: bgtexture_lst.md §Enumerations — ==0x01 STATIC, !=0x01 NON-STATIC. CODE-CONFIRMED CYCLE 1.
            ? BgTextureRenderBucket.Static
            : BgTextureRenderBucket.ScrollAnimated;

    /// <summary>
    /// Decoded material render-mode tag for this record.
    /// Maps the raw kind byte to <see cref="BgTextureKind"/>; returns <see cref="BgTextureKind.Unknown"/>
    /// when the byte value is not in the six-value observed set.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — kind value→render-mode mapping: HIGH.
    /// The earlier "animated vs. static flag" interpretation is SUPERSEDED — the byte is a
    /// render-mode selector with six distinct observed values, not a boolean.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "earlier boolean reading is retired".
    /// The 6-value enum is DATA-ONLY (no per-value loader branch/jump-table). Use <see cref="RenderBucket"/>
    /// for the loader's actual dispatch; use this property only for data-side render-mode categorisation.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "6-value enum is DATA-ONLY, CYCLE 1 CONFIRMED".
    /// </remarks>
    public BgTextureKind KindEnum => KindRaw switch
    {
        0x01 => BgTextureKind.Static, // spec: bgtexture_lst.md §Enumerations — KIND_STATIC: HIGH
        0x02 => BgTextureKind.ScrollUv, // spec: bgtexture_lst.md §Enumerations — KIND_SCROLL: HIGH
        0x0A => BgTextureKind.Grass, // spec: bgtexture_lst.md §Enumerations — KIND_GRASS: HIGH
        0x0B => BgTextureKind.Plant, // spec: bgtexture_lst.md §Enumerations — KIND_PLANT: HIGH
        0x0C => BgTextureKind.TreeBark, // spec: bgtexture_lst.md §Enumerations — KIND_TREE_BARK: HIGH
        0x14 => BgTextureKind.Foliage, // spec: bgtexture_lst.md §Enumerations — KIND_FOLIAGE: HIGH
        _ => BgTextureKind.Unknown, // unrecognised; inspect KindRaw for the raw byte
    };

    /// <summary>
    /// Texture path relative to the file's own texture directory, WITHOUT the <c>.dds</c>
    /// extension.  Null-terminated within the 47-byte field, zero-padded to fill it.
    /// Example: <c>"terrain/g3"</c> resolves to
    /// <c>data/map000/texture/terrain/g3.dds</c> (terrain instance).
    /// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path char[47] @ +1:
    ///   CONFIRMED.
    /// </summary>
    public required string RelPath { get; init; }
}

/// <summary>
/// Decoded result of a <c>bgtexture.lst</c> binary texture-index file.
/// Provides O(1) pool-index look-up by position.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md — flat 48-byte record array, no inter-record padding:
///   CONFIRMED.
/// <para>
/// Two known instances in the VFS:
/// <list type="bullet">
///   <item><c>data/map000/texture/bgtexture.lst</c> — 1 222 records, 58 660 bytes</item>
///   <item><c>data/effect/texture/bgtexture.lst</c>  — 1 108 records, 53 188 bytes</item>
/// </list>
/// spec: Docs/RE/formats/bgtexture_lst.md §Header layout — size formula 4 + record_count*48: CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class BgtextureLstCatalog
{
    private readonly BgtextureLstRecord[] _records;

    internal BgtextureLstCatalog(BgtextureLstRecord[] records) => _records = records;

    /// <summary>
    /// Total number of records (equals <c>record_count</c> from the file header).
    /// spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
    /// </summary>
    public int Count => _records.Length;

    /// <summary>
    /// All records in on-disk order.  Index 0 = pool slot 0.
    /// spec: Docs/RE/formats/bgtexture_lst.md — "records are addressed by position": CONFIRMED.
    /// </summary>
    public IReadOnlyList<BgtextureLstRecord> Records => _records;

    /// <summary>
    /// Looks up a record by its 0-based pool slot, or returns <see langword="null"/> when
    /// the index is out of range.
    /// </summary>
    /// <remarks>
    /// The <c>intTexId</c> from a <c>.map</c> file is already the 0-based pool slot and is
    /// passed to this method directly. The only <c>-1</c> in the terrain chain is the
    /// <c>.ted</c> byte indexing the per-cell <c>TEXTURES{}</c> list.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
    ///   "intTexId IS the 0-based record index, used DIRECTLY — NO -1": CONFIRMED.
    /// </remarks>
    public BgtextureLstRecord? GetByPoolSlot(int poolSlot) =>
        (uint)poolSlot < (uint)_records.Length ? _records[poolSlot] : null;
}