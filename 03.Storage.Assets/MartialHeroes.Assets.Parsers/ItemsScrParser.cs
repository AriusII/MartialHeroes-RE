using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/items.scr</c> — the regular item master database.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §1 — CONFIRMED "Model A".
/// <para>
/// File-level structure: NO header, NO leading count. The file begins directly at byte 0 with the
/// first record. Parsing terminates at EOF (short final read is allowed).
/// spec: Docs/RE/formats/items_scr.md §1.1 — "No file-level header; no leading count": CONFIRMED.
/// </para>
/// <para>
/// Per-record layout:
///   [ fixed 548-byte (0x224) block ][ effect_count × 8 bytes trailing effect entries ]
/// Per-record stride = 0x224 + 8 × effect_count.
/// spec: Docs/RE/formats/items_scr.md §1.2 — CONFIRMED model.
/// </para>
/// <para>
/// SUPERSEDED MODELS (DO NOT REINTRODUCE):
///   - Variable-stride "stats block at 0x38 + desc_width" — REFUTED.
///   - Three-sub-record "[A, B, C]" group model — REFUTED.
/// spec: Docs/RE/formats/items_scr.md §1.7 — Superseded / Refuted.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class ItemsScrParser
{
    // Fixed leading-block size: 548 bytes (0x224). CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
    private const int FixedBlockSize = 0x224; // 548

    // CP949 encoding — hoisted to avoid per-record construction (≈90,937 records per file).
    // The static constructor registers the provider before the field is used;
    // DecodeRecord accesses this field only after Parse() has run, ensuring correct init order.
    // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
    private static readonly System.Text.Encoding Cp949;

    static ItemsScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    // item_name CP949[52] @ 0x000. CONFIRMED (90,937/90,937).
    // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED.
    private const int OffItemName = 0x000;
    private const int ItemNameLen = 52; // 52-byte fixed buffer — CONFIRMED

    // item_uid u32LE @ 0x034. CONFIRMED (90,937/90,937).
    // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32LE @0x034: CONFIRMED.
    private const int OffItemUid = 0x034;

    // item_desc CP949 NUL-term @ 0x038 — CONFIRMED present; exact extent within block UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc CP949 @0x038: CONFIRMED present; extent UNVERIFIED.
    private const int OffItemDesc = 0x038;

    // stat_f32 f32 @ 0x0A4 — PARTIAL; semantic UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §1.4 — stat_f32 f32 @0x0A4: PARTIAL; semantic UNVERIFIED.
    private const int OffStatF32 = 0x0A4;

    // item_type_tag u32 @ 0x0B8 — PARTIAL; role UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §1.4 — item_type_tag u32 @0x0B8: PARTIAL; role UNVERIFIED.
    private const int OffItemTypeTag = 0x0B8;

    // template_ref u32 @ 0x200 — UNVERIFIED.
    // spec: Docs/RE/formats/items_scr.md §1.4 — template_ref u32 @0x200: UNVERIFIED.
    private const int OffTemplateRef = 0x200;

    // effect_count u8 @ 0x220. CONFIRMED (90,937/90,937).
    // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED.
    private const int OffEffectCount = 0x220;

    // Trailing effect entry stride: 8 bytes on disk. CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §1.5 — "Entry stride: 8 bytes on disk": CONFIRMED.
    private const int EffectEntryStride = 8;

    // Offsets within each 8-byte trailing effect entry.
    // spec: Docs/RE/formats/items_scr.md §1.5 — on-disk field layout.
    private const int OffEffectA = 0x00; // u16 — PLAUSIBLE
    private const int OffEffectB = 0x02; // s16 (signed) — PLAUSIBLE
    private const int OffEffectC = 0x04; // u16 — PLAUSIBLE
    private const int OffEffectD = 0x06; // u8  — PLAUSIBLE
    // byte 0x07 = pad/unused — PLAUSIBLE

    /// <summary>
    /// Parses all records from <c>data/script/items.scr</c>.
    /// Walks the file to EOF, handling variable-stride records
    /// (stride = 0x224 + 8 × effect_count).
    /// A short final read at EOF is handled defensively and terminates iteration.
    /// </summary>
    /// <param name="data">
    /// Raw file bytes from the VFS (full <c>items.scr</c> payload).
    /// </param>
    /// <returns>
    /// Enumerable of all decoded <see cref="ItemsScrRecord"/> instances.
    /// Caller may use <c>ToArray()</c> or <c>ToList()</c> to materialise.
    /// </returns>
    /// <remarks>
    /// spec: Docs/RE/formats/items_scr.md §1.3 — "Walk to EOF; no stored count": CONFIRMED.
    /// spec: Docs/RE/formats/items_scr.md §1.2 — "Per-record stride = 0x224 + 8 × effect_count": CONFIRMED.
    /// </remarks>
    public static IEnumerable<ItemsScrRecord> Parse(ReadOnlyMemory<byte> data)
    {
        // Register CP949 provider. Idempotent; safe to call multiple times.
        // The static Cp949 field is evaluated lazily on first access after this call.
        // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        int pos = 0;
        int totalLen = data.Length;

        while (pos < totalLen)
        {
            // Defensive: check there are enough bytes for the fixed block.
            // A short final read terminates iteration (the loader likewise loops until EOF/short read).
            // spec: Docs/RE/formats/items_scr.md §1.3 — "Terminate at EOF / short read": CONFIRMED.
            if (totalLen - pos < FixedBlockSize)
                break; // short final read — EOF handling

            // Decode the record via a non-iterator helper (spans cannot cross yield boundaries).
            // spec: Docs/RE/formats/items_scr.md §1.2 — "Per-record stride = 0x224 + 8 × effect_count": CONFIRMED.
            ItemsScrRecord record = DecodeRecord(data, pos, totalLen);
            int recordStride = FixedBlockSize + record.EffectCount * EffectEntryStride;

            yield return record;

            pos += recordStride;
        }
    }

    /// <summary>
    /// Decodes a single <c>items.scr</c> record starting at <paramref name="recordOffset"/>.
    /// Isolated from the iterator so that <see cref="ReadOnlySpan{T}"/> locals do not cross
    /// <c>yield</c> boundaries (CS4007).
    /// </summary>
    private static ItemsScrRecord DecodeRecord(ReadOnlyMemory<byte> data, int recordOffset, int totalLen)
    {
        // M1: in-method bounds guard — makes this method safe even when called directly.
        // The outer loop already breaks on short reads; this guard closes the local safety gap.
        // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
        if ((uint)recordOffset + (uint)FixedBlockSize > (uint)totalLen)
            throw new InvalidDataException(
                $"items.scr parse error: record at offset {recordOffset}: " +
                $"fixed block requires {FixedBlockSize} bytes but only {totalLen - recordOffset} bytes remain. " +
                "spec: Docs/RE/formats/items_scr.md §1.2.");

        // Take a span of just the fixed block — lifetime bounded to this call frame.
        ReadOnlySpan<byte> fixedBlock = data.Span.Slice(recordOffset, FixedBlockSize);

        // item_name CP949[52] @ 0x000. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED (90,937/90,937).
        ReadOnlySpan<byte> nameBytes = fixedBlock.Slice(OffItemName, ItemNameLen);
        int nameNul = nameBytes.IndexOf((byte)0);
        string itemName = nameNul >= 0
            ? Cp949.GetString(nameBytes[..nameNul])
            : Cp949.GetString(nameBytes);

        // item_uid u32LE @ 0x034. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32LE @0x034: CONFIRMED (90,937/90,937).
        uint itemUid = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffItemUid..]);

        // item_desc CP949 NUL-terminated @ 0x038. CONFIRMED present; extent within block UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc @0x038: CONFIRMED present; extent UNVERIFIED.
        ReadOnlySpan<byte> descRegion = fixedBlock[OffItemDesc..];
        int descNul = descRegion.IndexOf((byte)0);
        string itemDesc = descNul >= 0
            ? Cp949.GetString(descRegion[..descNul])
            : Cp949.GetString(descRegion);

        // stat_f32 f32 @ 0x0A4. PARTIAL; semantic UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — stat_f32 f32 @0x0A4: PARTIAL; semantic UNVERIFIED.
        float statF32 = BinaryPrimitives.ReadSingleLittleEndian(fixedBlock[OffStatF32..]);

        // item_type_tag u32 @ 0x0B8. PARTIAL; role UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_type_tag u32 @0x0B8: PARTIAL; role UNVERIFIED.
        uint itemTypeTag = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffItemTypeTag..]);

        // template_ref u32 @ 0x200. UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — template_ref u32 @0x200: UNVERIFIED.
        uint templateRef = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffTemplateRef..]);

        // effect_count u8 @ 0x220. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED (90,937/90,937).
        byte effectCount = fixedBlock[OffEffectCount];

        // Validate there are enough bytes for the trailing effect array.
        int trailingSize = effectCount * EffectEntryStride;
        if (recordOffset + FixedBlockSize + trailingSize > totalLen)
            throw new InvalidDataException(
                $"items.scr parse error: record at offset {recordOffset}: " +
                $"effect_count={effectCount} requires {trailingSize} trailing bytes but only " +
                $"{totalLen - recordOffset - FixedBlockSize} bytes remain. " +
                "spec: Docs/RE/formats/items_scr.md §1.2.");

        // Decode trailing effect entries.
        // N1: use Array.Empty when there are no effects to avoid a zero-length heap allocation.
        // spec: Docs/RE/formats/items_scr.md §1.5 — on-disk 8-byte effect entry layout.
        var effects = effectCount == 0
            ? Array.Empty<ItemEffectEntry>()
            : new ItemEffectEntry[effectCount];
        ReadOnlySpan<byte> fullSpan = data.Span;
        int effectsBase = recordOffset + FixedBlockSize;
        for (int e = 0; e < effectCount; e++)
        {
            int entryBase = effectsBase + e * EffectEntryStride;
            ReadOnlySpan<byte> entry = fullSpan.Slice(entryBase, EffectEntryStride);

            // effect_a u16 @+0x00 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_a u16 @+0x00.
            ushort effectA = BinaryPrimitives.ReadUInt16LittleEndian(entry[OffEffectA..]);

            // effect_b s16 @+0x02 (signed) — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_b s16 @+0x02.
            short effectB = BinaryPrimitives.ReadInt16LittleEndian(entry[OffEffectB..]);

            // effect_c u16 @+0x04 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_c u16 @+0x04.
            ushort effectC = BinaryPrimitives.ReadUInt16LittleEndian(entry[OffEffectC..]);

            // effect_d u8 @+0x06 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_d u8 @+0x06.
            byte effectD = entry[OffEffectD];
            // byte at +0x07 is pad/unused — not stored.

            effects[e] = new ItemEffectEntry
            {
                EffectA = effectA,
                EffectB = effectB,
                EffectC = effectC,
                EffectD = effectD,
            };
        }

        return new ItemsScrRecord
        {
            ItemName = itemName,
            ItemUid = itemUid,
            ItemDesc = itemDesc,
            StatF32 = statF32,
            ItemTypeTag = itemTypeTag,
            TemplateRef = templateRef,
            EffectCount = effectCount,
            Effects = effects,
            FixedBlockRaw = data.Slice(recordOffset, FixedBlockSize),
        };
    }
}