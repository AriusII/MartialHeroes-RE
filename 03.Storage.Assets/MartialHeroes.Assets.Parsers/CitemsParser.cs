using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/citems.scr</c> — the cash-shop item master database.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §2 citems.scr — CONFIRMED.
/// <para>
/// Format: no file header; fixed stride 1052 bytes (0x41C); record count = file_size / 1052.
/// Known: 512 records × 1052 = 539,648 bytes, exact.
/// spec: Docs/RE/formats/items_scr.md §2.1 — "fixed stride 1052 bytes (0x41C)": CONFIRMED.
/// spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052 = 512": CONFIRMED.
/// </para>
/// <para>
/// CORRECTIONS applied (spec: Docs/RE/formats/items_scr.md §2.5):
/// - item_name is at +0x04 (48 bytes), NOT at +0x08 (40 bytes).
/// - There is NO item_ref field at +0x04 — those bytes ARE the item_name.
/// - Description is 6 × 81-byte paragraphs from 0x0E4 (NOT a single buffer near 0xDC).
/// </para>
/// <para>
/// Text encoding: CP949 (EUC-KR), null-padded inside fixed buffers.
/// spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class CitemsParser
{
    // Record stride: 1052 bytes (0x41C). CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.1 — "Fixed stride: 1052 bytes (0x41C)": CONFIRMED.
    private const int RecordStride = 1052; // 0x41C

    // Field offsets within a record.
    // spec: Docs/RE/formats/items_scr.md §2.2 — Record layout.

    // slot_index u32LE @ 0x00. CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index u32LE @ 0x00: CONFIRMED (512/512).
    private const int OffSlotIndex = 0x00;

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

    // Description block: 6 paragraphs × 81 bytes each, starting at 0x0E4.
    // desc_para[i] start = 0x0E4 + i * 81 (i = 0..5). CONFIRMED (512/512).
    // spec: Docs/RE/formats/items_scr.md §2.4 — "6 × 81-byte paragraphs from 0x0E4": CONFIRMED (512/512).
    private const int OffDescBlock = 0x0E4; // first paragraph start — CONFIRMED
    private const int DescParaWidth = 81; // 0x51 bytes per paragraph — CONFIRMED
    private const int DescParaCount = 6; // 6 paragraphs — CONFIRMED

    // Record remainder @ 0x2CA (338 bytes). UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §2.2 — remainder 0x2CA..0x41B: UNVERIFIED.
    private const int OffRemainder = 0x2CA;
    private const int RemainderLen = RecordStride - OffRemainder; // 338

    /// <summary>
    /// Parses <c>data/script/citems.scr</c> into a <see cref="CitemsCatalog"/>.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Length must be an exact multiple of 1052.</param>
    /// <returns>A <see cref="CitemsCatalog"/> containing all decoded records.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer length is not an exact multiple of 1052 bytes.
    /// spec: Docs/RE/formats/items_scr.md §2.3 — "record count = file_size / 1052, must be exact".
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

        int count = span.Length / RecordStride;

        // Register CP949 provider. Idempotent; safe to call multiple times.
        // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949".
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/formats/items_scr.md §Identification — CP949.

        var records = new CitemsRecord[count];

        for (int i = 0; i < count; i++)
        {
            int recBase = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // slot_index u32LE @ 0x00. Sequential 1-based. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — slot_index u32LE @ 0x00: CONFIRMED (512/512).
            uint slotIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotIndex..]);

            // item_name CP949[48] @ 0x04. CONFIRMED (512/512).
            // The bytes at +0x04 ARE the name string — no separate item_ref field exists here.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_name CP949[48] @ 0x04: CONFIRMED (512/512).
            // spec: Docs/RE/formats/items_scr.md §2.5 — correction: formerly (wrong) item_ref+name@0x08 removed.
            ReadOnlySpan<byte> nameBytes = rec.Slice(OffItemName, ItemNameLen);
            int nameNul = nameBytes.IndexOf((byte)0);
            string itemName = nameNul >= 0
                ? cp949.GetString(nameBytes[..nameNul])
                : cp949.GetString(nameBytes);

            // unknown_36 u16LE @ 0x36. CONFIRMED present; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — unknown_36 u16LE @ 0x36: CONFIRMED present; role UNVERIFIED.
            ushort unknown36 = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffUnknown36..]);

            // cash_price_nx u32LE @ 0x38. CONFIRMED (value); role INFERRED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — cash_price_nx u32LE @ 0x38: CONFIRMED (value); role INFERRED.
            uint cashPriceNx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffCashPriceNx..]);

            // slot_seq_2 u32LE @ 0x3C. CONFIRMED (sequential); role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — slot_seq_2 u32LE @ 0x3C: CONFIRMED (sequential); role UNVERIFIED.
            uint slotSeq2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotSeq2..]);

            // item_uid u32LE @ 0x48. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — item_uid u32LE @ 0x48: CONFIRMED.
            uint itemUid = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffItemUid..]);

            // flag_4C u32LE @ 0x4C. CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — flag_4C u32LE @ 0x4C: CONFIRMED.
            uint flag4C = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFlag4C..]);

            // 6 × 81-byte description paragraphs from 0x0E4. CONFIRMED (512/512).
            // desc_para[i] start = 0x0E4 + i * 81 (i = 0..5).
            // spec: Docs/RE/formats/items_scr.md §2.4 — 6 × 81-byte paragraphs: CONFIRMED.
            // spec: Docs/RE/formats/items_scr.md §2.5 — correction: NOT a single buffer near 0xDC.
            var descParagraphs = new string[DescParaCount];
            for (int p = 0; p < DescParaCount; p++)
            {
                int paraOff = OffDescBlock + p * DescParaWidth;
                // spec: Docs/RE/formats/items_scr.md §2.4 — desc_para[i] start = 0x0E4 + i * 81: CONFIRMED.
                ReadOnlySpan<byte> paraBytes = rec.Slice(paraOff, DescParaWidth);
                int paraNul = paraBytes.IndexOf((byte)0);
                descParagraphs[p] = paraNul >= 0
                    ? cp949.GetString(paraBytes[..paraNul])
                    : cp949.GetString(paraBytes);
            }

            // Record remainder @ 0x2CA (338 bytes). UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §2.2 — remainder 0x2CA..0x41B: UNVERIFIED.
            ReadOnlyMemory<byte> remainderRaw = data.Slice(recBase + OffRemainder, RemainderLen);

            records[i] = new CitemsRecord
            {
                SlotIndex = slotIndex,
                ItemName = itemName,
                Unknown36 = unknown36,
                CashPriceNx = cashPriceNx,
                SlotSeq2 = slotSeq2,
                ItemUid = itemUid,
                Flag4C = flag4C,
                DescParagraphs = descParagraphs,
                RemainderRaw = remainderRaw,
            };
        }

        return new CitemsCatalog(records);
    }
}