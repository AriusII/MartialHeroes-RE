namespace MartialHeroes.Assets.Parsers.Models;

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
    /// Kind / animated flag byte at record offset +0.
    /// Observed <c>0x01</c> in every record of both shipped files.
    /// Semantic UNVERIFIED — likely animated vs. static flag.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — kind u8 @ +0:
    ///   CONFIRMED (value 0x01); semantic UNVERIFIED.
    /// </summary>
    public required byte Kind { get; init; }

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
    /// The 1-based <c>intTexId</c> from a <c>.map</c> file must be decremented by 1 before
    /// calling this method.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
    ///   "intTexId - 1 gives the 0-based .lst record index": CONFIRMED.
    /// </remarks>
    public BgtextureLstRecord? GetByPoolSlot(int poolSlot) =>
        (uint)poolSlot < (uint)_records.Length ? _records[poolSlot] : null;
}
