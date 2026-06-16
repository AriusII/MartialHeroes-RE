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

    // model_ref_key u32 @ 0x080 — loader-resolved as model-reference key.
    // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
    // spec: Docs/RE/formats/items_scr.md §1.7 — "free numeric stats at +0x080/+0x084" REFUTED; they are asset-lookup keys.
    private const int OffModelRefKey = 0x080;

    // anim_ref_key u32 @ 0x084 — loader-resolved as animation-reference key.
    // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
    private const int OffAnimRefKey = 0x084;

    // opaque_0a4 4 bytes @ 0x0A4 — read and retained; no consumer semantics settled.
    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
    private const int OffOpaque0A4 = 0x0A4;

    // record_discriminator u8 @ on-disk +0xD2 — tested != 14 by the loader.
    // CORRECTED CAMPAIGN 10 (two-witness: stack-frame analysis + black-box):
    //   The prior "+0x0BA" was the loader's internal notation against its working buffer whose base
    //   sits 0x18 bytes ahead of the record start: +0xBA + 0x18 = +0xD2 (on-disk).
    //   Engineers reading the file from disk MUST use +0xD2. Full discriminator value enumeration is DBG-pending.
    // spec: Docs/RE/formats/items_scr.md §1.4.1 — on-disk +0xD2 tested != 14: loader-resolved.
    // spec: Docs/RE/formats/items_scr.md §1.7 — "+0x0B8 item_type_tag" REFUTED.
    private const int OffRecordDiscriminator = 0x0D2; // on-disk +0xD2; spec: Docs/RE/formats/items_scr.md §1.4.1

    // opaque_200 4 bytes @ 0x200 — read and retained; no consumer semantics settled.
    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
    private const int OffOpaque200 = 0x200;

    // opaque_21c 4 bytes @ 0x21C — read and retained; no consumer semantics settled.
    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
    private const int OffOpaque21C = 0x21C;

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

        // model_ref_key u32 @ 0x080. Loader-resolved as model-reference key.
        // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
        // spec: Docs/RE/formats/items_scr.md §1.7 — free-stat reading at +0x080 REFUTED; it is an asset-lookup key.
        uint modelRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffModelRefKey..]);

        // anim_ref_key u32 @ 0x084. Loader-resolved as animation-reference key.
        // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
        uint animRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffAnimRefKey..]);

        // opaque_0a4: 4 bytes @ 0x0A4. Read and retained; no consumer semantics settled — DBG-pending.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
        // Dispatch offset base note: the loader stages records against a working buffer 0x18 bytes ahead;
        // its internal branch offsets are relative to that 0x18 base. On-disk offsets here are absolute.
        // spec: Docs/RE/formats/items_scr.md §1.4 "Loader buffer base" note.
        ReadOnlyMemory<byte> opaque0A4 = data.Slice(recordOffset + OffOpaque0A4, 4);

        // record_discriminator u8 @ on-disk +0xD2. Tested != 14 by the loader for per-record routing.
        // CORRECTED CAMPAIGN 10: on-disk offset is +0xD2 (the loader's "+0xBA" is its 0x18-shifted internal
        // working-buffer notation; +0xBA + 0x18 = +0xD2 on-disk — see §1.4.1).
        // spec: Docs/RE/formats/items_scr.md §1.4.1 — on-disk +0xD2 tested != 14: loader-resolved.
        // spec: Docs/RE/formats/items_scr.md §1.7 — "+0x0B8 item_type_tag" REFUTED; DO NOT reintroduce.
        // Full discriminator value enumeration is DBG-pending; read the byte, assign no meaning.
        byte recordDiscriminator = fixedBlock[OffRecordDiscriminator];

        // opaque_200: 4 bytes @ 0x200. Read and retained; no consumer semantics settled — DBG-pending.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
        ReadOnlyMemory<byte> opaque200 = data.Slice(recordOffset + OffOpaque200, 4);

        // opaque_21c: 4 bytes @ 0x21C. Read and retained; no consumer semantics settled — DBG-pending.
        // Non-zero in only a small subset of records.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
        ReadOnlyMemory<byte> opaque21C = data.Slice(recordOffset + OffOpaque21C, 4);

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
            ModelRefKey = modelRefKey,
            AnimRefKey = animRefKey,
            Opaque0A4 = opaque0A4,
            RecordDiscriminator = recordDiscriminator,
            Opaque200 = opaque200,
            Opaque21C = opaque21C,
            EffectCount = effectCount,
            Effects = effects,
            FixedBlockRaw = data.Slice(recordOffset, FixedBlockSize),
        };
    }
}