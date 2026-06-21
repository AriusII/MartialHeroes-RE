using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for per-class stance skill tables:
///     <c>data/script/musajung.do</c>, <c>musasa.do</c>, <c>musama.do</c>, and 9 siblings
///     (assasinjung, assasinsa, assasinma, wizardjung, wizardsa, wizardma, monkjung, monksa, monkma).
///     Each file is a headerless tightly-packed array of fixed 116-byte records.
///     The record count is <c>file_size / 116</c>; any incomplete trailing bytes are ignored.
///     All-zero records (entire 116 bytes == 0x00) are skipped — the engine bulk-loader does not
///     insert null entries into its maps.
///     The decoded records drive per-skill icon-coordinate lookup in the skill window and hotbar:
///     <see cref="DoStanceRecord.IconSrcX" /> and <see cref="DoStanceRecord.IconSrcY" /> tell the
///     renderer where on the 512×512 icon sheet to blit a 23×23 pixel cell.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/ui_manifests.md §2.7 Per-class stance .do files.
///     Verified file sizes: musajung.do = 34,916 bytes (301 records, 0 tail);
///     musasa.do = 34,916 bytes (301 records); musama.do = 25,792 bytes (222 records + 40 tail).
///     Endianness: little-endian throughout.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class DoStanceParser
{
    // ─── record field offsets (all little-endian; cited from spec §2.7) ──────

    // instanceKey u32 @ record+0x00. CODE-CONFIRMED + SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x00 u32 instanceKey": CODE-CONFIRMED + SAMPLE-VERIFIED.
    private const int OffInstanceKey = 0x00;

    // groupSubIndex u32 @ record+0x04. SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x04 u32 groupSubIndex": SAMPLE-VERIFIED.
    private const int OffGroupSubIndex = 0x04;

    // slotIndex u32 @ record+0x08. CODE-CONFIRMED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x08 u32 slotIndex": CODE-CONFIRMED.
    private const int OffSlotIndex = 0x08;

    // classStanceRef u32 @ record+0x0C. CODE-CONFIRMED (1001/1002/1003); PLAUSIBLE (other 9).
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x0C u32 classStanceRef": CODE-CONFIRMED + SAMPLE-VERIFIED.
    private const int OffClassStanceRef = 0x0C;

    // groupId u32 @ record+0x10. SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x10 u32 groupId": SAMPLE-VERIFIED.
    private const int OffGroupId = 0x10;

    // (secondary X variant) u16 @ record+0x14. SAMPLE-VERIFIED (value pattern); name UNKNOWN.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x14 u16 (secondary X variant)": SAMPLE-VERIFIED.
    private const int OffSecondaryXVariant = 0x14;

    // iconSrcX i16 @ record+0x18. CODE-CONFIRMED + SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x18 i16 iconSrcX": CODE-CONFIRMED + SAMPLE-VERIFIED.
    private const int OffIconSrcX = 0x18;

    // iconSrcY i16 @ record+0x1C. CODE-CONFIRMED + SAMPLE-VERIFIED.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x1C i16 iconSrcY": CODE-CONFIRMED + SAMPLE-VERIFIED.
    private const int OffIconSrcY = 0x1C;

    // secondarySpriteX u16 @ record+0x20. SAMPLE-VERIFIED (value pattern); name UNKNOWN.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x20 u16 secondarySpriteX": SAMPLE-VERIFIED.
    private const int OffSecondarySpriteX = 0x20;

    // secondarySpriteY u16 @ record+0x24. SAMPLE-VERIFIED (value pattern); name UNKNOWN.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x24 u16 secondarySpriteY": SAMPLE-VERIFIED.
    private const int OffSecondarySpriteY = 0x24;

    // unmapped tail starts at record+0x28 (72 bytes). UNKNOWN.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 bytes unmapped": UNKNOWN.
    private const int OffTail = 0x28;

    // ─── public API ───────────────────────────────────────────────────────────

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static DoStanceTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>
    ///     Parses a per-class stance <c>.do</c> file from a raw byte span.
    /// </summary>
    /// <param name="span">
    ///     Raw bytes of the file (e.g. from the VFS).
    ///     Any trailing bytes that do not form a complete 116-byte record are silently ignored.
    ///     spec: Docs/RE/formats/ui_manifests.md §2.7 — "non-zero remainder ignored": SAMPLE-VERIFIED.
    /// </param>
    /// <returns>
    ///     A <see cref="DoStanceTable" /> containing all non-zero records, plus the raw counts.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the buffer length is less than zero (should never happen with real data).
    ///     Out-of-bounds reads never occur; the parser exits cleanly for undersized or empty inputs.
    /// </exception>
    public static DoStanceTable Parse(ReadOnlySpan<byte> span)
    {
        // record_count = file_size / 116 (integer division).
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — "record_count = file_size / 116": SAMPLE-VERIFIED.
        var stride = DoStanceRecord.Stride; // 116
        var totalCount = span.Length / stride;
        var trailingBytes = span.Length % stride;

        // Allocate once — worst case all records are non-zero.
        var records = new List<DoStanceRecord>(totalCount);

        for (var i = 0; i < totalCount; i++)
        {
            // Slice the 116-byte record without copying.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "each record is 116 bytes": SAMPLE-VERIFIED.
            var recordBase = i * stride;
            var rec = span.Slice(recordBase, stride);

            // Skip all-zero records — the engine bulk-loader does not insert null entries.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — load-chain: "bulk-loader reads records and inserts into maps".
            // All-zero means instanceKey=0 AND slotIndex=0 AND every byte is 0.
            if (IsAllZero(rec))
                continue;

            // instanceKey u32 LE @ record+0x00. CODE-CONFIRMED + SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x00 u32 instanceKey".
            var instanceKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // groupSubIndex u32 LE @ record+0x04. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x04 u32 groupSubIndex".
            var groupSubIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffGroupSubIndex..]);

            // slotIndex u32 LE @ record+0x08. CODE-CONFIRMED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x08 u32 slotIndex".
            var slotIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotIndex..]);

            // classStanceRef u32 LE @ record+0x0C. CODE-CONFIRMED (1001/1002/1003); PLAUSIBLE (other 9).
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x0C u32 classStanceRef".
            var classStanceRef = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffClassStanceRef..]);

            // groupId u32 LE @ record+0x10. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x10 u32 groupId".
            var groupId = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffGroupId..]);

            // (secondary X variant) u16 LE @ record+0x14. SAMPLE-VERIFIED (pattern); name UNKNOWN.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x14 u16 (secondary X variant)".
            var secondaryXVariant = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondaryXVariant..]);

            // iconSrcX i16 LE @ record+0x18. CODE-CONFIRMED + SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x18 i16 iconSrcX".
            var iconSrcX = BinaryPrimitives.ReadInt16LittleEndian(rec[OffIconSrcX..]);

            // iconSrcY i16 LE @ record+0x1C. CODE-CONFIRMED + SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x1C i16 iconSrcY".
            var iconSrcY = BinaryPrimitives.ReadInt16LittleEndian(rec[OffIconSrcY..]);

            // secondarySpriteX u16 LE @ record+0x20. SAMPLE-VERIFIED (pattern); name UNKNOWN.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x20 u16 secondarySpriteX".
            var secondarySpriteX = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondarySpriteX..]);

            // secondarySpriteY u16 LE @ record+0x24. SAMPLE-VERIFIED (pattern); name UNKNOWN.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x24 u16 secondarySpriteY".
            var secondarySpriteY = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondarySpriteY..]);

            // Unmapped tail: 72 bytes from record+0x28..+0x73. UNKNOWN.
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 bytes unmapped": UNKNOWN.
            var tail = new DoStanceTail72();
            rec.Slice(OffTail, DoStanceRecord.TailByteCount).CopyTo(tail.AsSpan());

            records.Add(new DoStanceRecord
            {
                InstanceKey = instanceKey,
                GroupSubIndex = groupSubIndex,
                SlotIndex = slotIndex,
                ClassStanceRef = classStanceRef,
                GroupId = groupId,
                SecondaryXVariant = secondaryXVariant,
                IconSrcX = iconSrcX,
                IconSrcY = iconSrcY,
                SecondarySpriteX = secondarySpriteX,
                SecondarySpriteY = secondarySpriteY,
                Tail = tail
            });
        }

        return new DoStanceTable(records, totalCount, trailingBytes);
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Returns true when every byte in <paramref name="rec" /> is zero.
    ///     Used to skip the all-zero null records that the engine bulk-loader ignores.
    ///     spec: Docs/RE/formats/ui_manifests.md §2.7 — "skip all-zero records".
    /// </summary>
    private static bool IsAllZero(ReadOnlySpan<byte> rec)
    {
        // IndexOfAnyExcept(0) is O(n) SIMD-accelerated on .NET 8+; returns -1 if all bytes are 0.
        return rec.IndexOfAnyExcept((byte)0) < 0;
    }
}