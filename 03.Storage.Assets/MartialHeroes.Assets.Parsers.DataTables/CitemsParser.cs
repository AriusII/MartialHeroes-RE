using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/script/citems.scr</c> — the cash-shop item master database.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.
///     <para>
///         Format: no file header; fixed stride 1052 bytes (0x41C); record count = file_size / 1052.
///         Known: 512 records × 1052 = 538,624 bytes, exact.
///         spec: Docs/RE/formats/items_scr.md §2.1 — "fixed stride 1052 bytes (0x41C)": CONFIRMED.
///         spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052 = 512": CONFIRMED.
///     </para>
///     <para>
///         CORRECTIONS applied (spec: Docs/RE/formats/items_scr.md §2.5):
///         - +0x00 is item_id (non-monotonic dense-array lookup key), NOT a sequential slot_index.
///         - item_name is at +0x04 (48 bytes), NOT at +0x08 (40 bytes).
///         - There is NO item_ref field at +0x04 — those bytes ARE the item_name.
///         - Description is 10 × 81-byte paragraphs from 0x0E4; '#'-sentinel early-terminates the consumer.
///         The former 6-vs-10 OPEN conflict is RESOLVED in favour of 10. CONFIRMED.
///         spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10, '#'-sentinel CONFIRMED.
///         - Record tail is 0x40E..0x41B (14 bytes), NOT 0x2CA (338 bytes).
///         spec: Docs/RE/formats/items_scr.md §2.4 — "block ends at 0x40E; 14-byte tail".
///     </para>
///     <para>
///         Text encoding: CP949 (EUC-KR), null-padded inside fixed buffers.
///         spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class CitemsParser
{
    // Record stride: 1052 bytes (0x41C). CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.1 — "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
    private const int RecordStride = 1052; // 0x41C

    // Field offsets within a record.
    // spec: Docs/RE/formats/items_scr.md §2.2 — Record layout.

    // item_id u32LE @ 0x00 — dense-array lookup key, non-monotonic, NOT a slot index.
    // CORRECTED CAMPAIGN 10: formerly called slot_index; the loader uses this as an item id key
    // whose values are non-monotonic (billing items >= 100000 appear mid-file).
    // spec: Docs/RE/formats/items_scr.md §2.1 — "+0x00 is item_id, NOT slot_index": SAMPLE-VERIFIED (512/512).
    // spec: Docs/RE/formats/items_scr.md §2.5 — "slot_index" WRONG; field is item_id.
    private const int OffItemId = 0x00; // item_id - dense-array lookup key, non-monotonic, NOT a slot index (§2.5)

    // item_name CP949[48] @ 0x04. CONFIRMED (512/512).
    // NOTE: the 4 bytes at +0x04 are the START of the name string — no separate item_ref field exists.
    // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
    // spec: Docs/RE/formats/items_scr.md §2.5 — "item_ref at +0x04 DOES NOT EXIST — those bytes are item_name".
    private const int OffItemName = 0x04;
    private const int ItemNameLen = 48; // 48-byte fixed buffer, 0x04..0x33 — CONFIRMED

    // unknown_36 u16LE @ 0x36. CONFIRMED present; role UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
    private const int OffUnknown36 = 0x36;

    // cash_price_nx u32LE @ 0x38. CONFIRMED (value); role INFERRED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
    private const int OffCashPriceNx = 0x38;

    // slot_seq_2 u32LE @ 0x3C. CONFIRMED (sequential); role UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED (sequential); role UNVERIFIED.
    private const int OffSlotSeq2 = 0x3C;

    // item_uid u32LE @ 0x48. CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
    private const int OffItemUid = 0x48;

    // flag_4C u32LE @ 0x4C. CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
    private const int OffFlag4C = 0x4C;

    // Description block: 10 paragraphs × 81 bytes each, starting at 0x0E4. CONFIRMED (§2.4).
    // desc_para[i] start = 0x0E4 + i * 81 (i = 0..9). The consumer bounds index < 10; a '#'-sentinel
    // paragraph (first byte == '#') early-terminates iteration.
    // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10 structural capacity, CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.4 — stop at the first '#'-sentinel paragraph (first byte '#').
    private const int OffDescBlock = 0x0E4; // first paragraph start — CONFIRMED
    private const int DescParaWidth = 81; // 0x51 bytes per paragraph — CONFIRMED
    private const int DescParaCount = 10; // structural capacity; CONFIRMED (§2.4). DO NOT change to 6.

    // Record tail @ 0x40E (14 bytes) — the non-paragraph tail after the 10-paragraph description block.
    // 0x0E4 + 10*81 = 0x40E; tail ends at 0x41B inside the 1052-byte (0x41C) record.
    // spec: Docs/RE/formats/items_scr.md §2.2 — "record tail 0x40E..0x41B (14 bytes)": UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §2.4 — "block ends at 0x40E; 14-byte tail at 0x40E..0x41B".
    private const int OffRemainder = 0x40E;
    private const int RemainderLen = 14; // 0x41B - 0x40E + 1 = 14 bytes

    /// <summary>
    ///     Parses <c>data/script/citems.scr</c> into a <see cref="CitemsCatalog" />.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Length must be an exact multiple of 1052.</param>
    /// <returns>A <see cref="CitemsCatalog" /> containing all decoded records.</returns>
    /// <exception cref="InvalidDataException">
    ///     Buffer length is not an exact multiple of 1052 bytes.
    ///     spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052, must be exact".
    /// </exception>
    public static CitemsCatalog Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Validate exact stride divisibility.
        // spec: Docs/RE/formats/items_scr.md §2.3 — "file_size / 1052 = 512 exactly": CONFIRMED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"citems.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/items_scr.md §2.3.");

        var count = span.Length / RecordStride;

        // Register CP949 provider. Idempotent; safe to call multiple times.
        // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949".
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/formats/items_scr.md §Identification — CP949.

        var records = new CitemsRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            // item_id u32LE @ 0x00. Dense-array lookup key, non-monotonic, NOT a sequential slot index.
            // CORRECTED CAMPAIGN 10: field is item_id (billing items >= 100000 appear mid-file).
            // spec: Docs/RE/formats/items_scr.md §2.1 — "+0x00 is item_id, NOT slot_index": SAMPLE-VERIFIED (512/512).
            // spec: Docs/RE/formats/items_scr.md §2.5 — "slot_index" WRONG; field is item_id.
            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // item_name CP949[48] @ 0x04. CONFIRMED (512/512).
            // The bytes at +0x04 ARE the name string — no separate item_ref field exists here.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
            // spec: Docs/RE/formats/items_scr.md §2.5 — correction: formerly (wrong) item_ref+name@0x08 removed.
            var nameBytes = rec.Slice(OffItemName, ItemNameLen);
            var nameNul = nameBytes.IndexOf((byte)0);
            var itemName = nameNul >= 0
                ? cp949.GetString(nameBytes[..nameNul])
                : cp949.GetString(nameBytes);

            // unknown_36 u16LE @ 0x36. CONFIRMED present; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
            var unknown36 = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffUnknown36..]);

            // cash_price_nx u32LE @ 0x38. CONFIRMED (value); role INFERRED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
            var cashPriceNx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffCashPriceNx..]);

            // slot_seq_2 u32LE @ 0x3C. CONFIRMED (sequential); role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED (sequential); role UNVERIFIED.
            var slotSeq2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotSeq2..]);

            // item_uid u32LE @ 0x48. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
            var itemUid = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffItemUid..]);

            // flag_4C u32LE @ 0x4C. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
            var flag4C = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFlag4C..]);

            // 10 × 81-byte description paragraphs from 0x0E4. CONFIRMED (§2.4).
            // desc_para[i] start = 0x0E4 + i * 81 (i = 0..9). Consumer accessor bounds index < 10.
            // '#'-sentinel: stop at the first paragraph whose first byte is '#' (0x23).
            // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para_count = 10 structural capacity, CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.4 — stop at the first '#'-sentinel paragraph (first byte '#').
            // spec: Docs/RE/formats/items_scr.md §2.5 — correction: NOT a single buffer near 0xDC.
            var descParaList = new List<string>(DescParaCount);
            for (var p = 0; p < DescParaCount; p++)
            {
                var paraOff = OffDescBlock + p * DescParaWidth;
                // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para[i] start = 0x0E4 + i * 81: CONFIRMED.
                var paraBytes = rec.Slice(paraOff, DescParaWidth);
                // '#'-sentinel early-termination: if the first byte is '#' (0x23), stop.
                // spec: Docs/RE/formats/items_scr.md §2.4 — stop at the first '#'-sentinel paragraph.
                if (paraBytes[0] == 0x23) // '#' sentinel
                    break;
                var paraNul = paraBytes.IndexOf((byte)0);
                descParaList.Add(paraNul >= 0
                    ? cp949.GetString(paraBytes[..paraNul])
                    : cp949.GetString(paraBytes));
            }

            var descParagraphs = descParaList.ToArray();

            // Record tail @ 0x40E..0x41B (14 bytes). UNVERIFIED — likely duration/equip requirements/icon id.
            // spec: Docs/RE/formats/items_scr.md §2.2 — "record tail 0x40E..0x41B (14 bytes)": UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.4 — "block ends at 0x40E; 14-byte tail at 0x40E..0x41B".
            var remainderRaw = data.Slice(recBase + OffRemainder, RemainderLen);

            records[i] = new CitemsRecord
            {
                ItemId = itemId,
                ItemName = itemName,
                Unknown36 = unknown36,
                CashPriceNx = cashPriceNx,
                SlotSeq2 = slotSeq2,
                ItemUid = itemUid,
                Flag4C = flag4C,
                DescParagraphs = descParagraphs,
                RemainderRaw = remainderRaw
            };
        }

        return new CitemsCatalog(records);
    }
}