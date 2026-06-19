namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  citems.scr — Cash-shop item master database
//  spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/citems.scr</c>. Stride: 1052 bytes (0x41C).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.
/// No file header; record count = file_size / 1052 (must be exact). Known: 512 records (538,624 bytes).
/// <para>
/// CORRECTIONS from earlier spec (do not reintroduce):
/// - +0x00 is item_id (dense-array lookup key, non-monotonic, NOT a sequential slot_index).
/// - item_name is at +0x04 (48 bytes), NOT +0x08 (40 bytes).
/// - The field formerly documented as item_ref (u32 at +0x04) DOES NOT EXIST — those bytes are the
///   first 4 bytes of the item_name CP949 string.
/// - The description is NOT a single buffer near 0xDC; it is 10 × 81-byte paragraphs from 0x0E4
///   (capacity 10, '#'-sentinel early-terminated; CONFIRMED §2.4).
///   The former 6-vs-10 OPEN conflict is RESOLVED in favour of 10.
/// - RemainderRaw is the record tail 0x40E..0x41B (14 bytes), NOT 0x2CA (338 bytes).
/// spec: Docs/RE/formats/items_scr.md §2.5 — Corrections.
/// spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10 structural capacity, CONFIRMED.
/// </para>
/// </remarks>
public sealed class CitemsRecord
{
    /// <summary>
    /// Item id — dense-array lookup key used by the loader. Non-monotonic by record position:
    /// billing/premium items carry ids &gt;= 100000 and appear mid-file. NOT a sequential slot index.
    /// spec: Docs/RE/formats/items_scr.md §2.1 — "+0x00 is item_id, NOT slot_index": SAMPLE-VERIFIED (512/512).
    /// spec: Docs/RE/formats/items_scr.md §2.5 — "slot_index" was WRONG; do not reintroduce.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    /// Cash-shop item display name, decoded from the 48-byte CP949 null-padded buffer at +0x04.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
    /// spec: Docs/RE/formats/items_scr.md §2.5 — formerly misidentified as item_ref+name@0x08;
    /// the 4 bytes at +0x04 ARE the start of the name string — no separate item_ref field exists.
    /// </summary>
    public required string ItemName { get; init; }

    // Zero padding 0x30..0x35 — CONFIRMED zero.
    // spec: Docs/RE/formats/items_scr.md §2.2 — pad_30 u8[6] @ 0x30: CONFIRMED (zero). Not stored.

    /// <summary>
    /// Non-zero field; value varies per record; purpose unknown.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
    /// </summary>
    public required ushort Unknown36 { get; init; }

    /// <summary>
    /// Cash-shop price in NX cash points (small values, e.g. ~950, ~3300).
    /// spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
    /// </summary>
    public required uint CashPriceNx { get; init; }

    /// <summary>
    /// Second sequential index; increments per record. Role unknown (possibly a second slot or category index).
    /// spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED (sequential); role UNVERIFIED.
    /// </summary>
    public required uint SlotSeq2 { get; init; }

    // 8-byte padding at 0x40 — zero in all observed records.
    // spec: Docs/RE/formats/items_scr.md §2.2 — pad_40 u8[8] @ 0x40: CONFIRMED (zero). Not stored.

    /// <summary>
    /// Per-record unique identifier; increments across adjacent records (high-range values ~283M band).
    /// spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
    /// </summary>
    public required uint ItemUid { get; init; }

    /// <summary>
    /// Value 1 in all observed records.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
    /// </summary>
    public required uint Flag4C { get; init; }

    /// <summary>
    /// Description paragraphs decoded from the record (each 81 bytes CP949, NUL-terminated within buffer).
    /// 10 paragraphs (capacity); runtime '#'-sentinel early-terminated; CONFIRMED §2.4.
    /// Array length is 0..10 depending on the '#'-sentinel position; only populated paragraphs are included.
    /// Paragraph start offsets (all ten): 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279, 0x2CA, 0x31B, 0x36C, 0x3BD.
    /// The description block ends at 0x40E (= 0x0E4 + 10·81 = 1038); the 14-byte record tail follows at 0x40E..0x41B.
    /// Use <see cref="GetParagraph"/> for bounds-safe indexed access (index &lt; 10).
    /// spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10 structural capacity, CONFIRMED.
    /// spec: Docs/RE/formats/items_scr.md §2.4 — stop at the first '#'-sentinel paragraph.
    /// </summary>
    public required string[] DescParagraphs { get; init; }

    /// <summary>
    /// Returns the paragraph at <paramref name="index"/> (0-based), or <see langword="null"/> when
    /// the index is out of the populated range or <c>&gt;= 10</c>.
    /// </summary>
    /// <param name="index">0-based paragraph index. Must be &lt; 10.</param>
    /// <returns>The CP949-decoded paragraph string, or <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// The consumer accessor bounds the index at &lt; 10 (structural capacity).
    /// spec: Docs/RE/formats/items_scr.md §2.4 — "consumer paragraph accessor bounds the index &lt; 10".
    /// </remarks>
    public string? GetParagraph(int index)
    {
        // Consumer accessor bounds the index < 10 (structural capacity).
        // spec: Docs/RE/formats/items_scr.md §2.4 — consumer paragraph accessor bounds index < 10.
        if ((uint)index >= 10u) return null;
        return index < DescParagraphs.Length ? DescParagraphs[index] : null;
    }

    /// <summary>
    /// Record tail bytes at 0x40E..0x41B (14 bytes) — after the 10-paragraph description block.
    /// Content unmapped; likely duration / equip requirements / icon-graphic id.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — "record tail 0x40E..0x41B (14 bytes)": UNVERIFIED.
    /// spec: Docs/RE/formats/items_scr.md §2.4 — "block ends at 0x40E; 14-byte tail at 0x40E..0x41B".
    /// </summary>
    public required ReadOnlyMemory<byte> RemainderRaw { get; init; }
}

/// <summary>
/// Typed catalogue of all 512 records from <c>data/script/citems.scr</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §2 citems.scr — "fixed 512 records × 1052 bytes": CONFIRMED.
/// Keyed by <see cref="CitemsRecord.ItemUid"/> via <see cref="TryGetByUid"/>.
/// </remarks>
public sealed class CitemsCatalog
{
    private readonly Dictionary<uint, CitemsRecord> _byUid;

    /// <summary>All records in on-disk order.</summary>
    public IReadOnlyList<CitemsRecord> Records { get; }

    internal CitemsCatalog(CitemsRecord[] records)
    {
        Records = records;
        _byUid = new Dictionary<uint, CitemsRecord>(records.Length);
        foreach (var r in records)
            _byUid.TryAdd(r.ItemUid, r);
    }

    /// <summary>
    /// O(1) lookup by <see cref="CitemsRecord.ItemUid"/>. Returns <see langword="null"/> when absent.
    /// spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
    /// </summary>
    public CitemsRecord? TryGetByUid(uint itemUid) =>
        _byUid.TryGetValue(itemUid, out var r) ? r : null;
}