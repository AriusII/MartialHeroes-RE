using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/script/items.scr</c> — the regular item master database.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/items_scr.md §1 — CONFIRMED "Model A".
///     <para>
///         File-level structure: NO header, NO leading count. The file begins directly at byte 0 with the
///         first record. Parsing terminates at EOF (short final read is allowed).
///         spec: Docs/RE/formats/items_scr.md §1.1 — "No file-level header; no leading count": CONFIRMED.
///     </para>
///     <para>
///         Per-record layout:
///         [ fixed 548-byte (0x224) block ][ effect_count × 8 bytes trailing effect entries ]
///         Per-record stride = 0x224 + 8 × effect_count.
///         spec: Docs/RE/formats/items_scr.md §1.2 — CONFIRMED model.
///     </para>
///     <para>
///         SUPERSEDED MODELS (DO NOT REINTRODUCE):
///         - Variable-stride "stats block at 0x38 + desc_width" — REFUTED.
///         - Three-sub-record "[A, B, C]" group model — REFUTED.
///         spec: Docs/RE/formats/items_scr.md §1.7 — Superseded / Refuted.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class ItemsScrParser
{
    // Fixed leading-block size: 548 bytes (0x224). CONFIRMED.
    // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
    private const int FixedBlockSize = 0x224; // 548

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

    // record_discriminator u8 @ on-disk +0xBA — tested != 14 by the loader.
    // CORRECTED CYCLE 7 (IDB 263bd994, two-witness: loader branch + black-box):
    //   The loader reads the 548-byte block DIRECTLY into the staging buffer (read base = record start).
    //   There is NO 0x18 shift — the earlier "+0xD2 / 0x18-ahead working buffer" model is REFUTED.
    //   The CONFIRMED effect_count at on-disk +0x220 lands at staging-buffer +0x220 with no shift,
    //   anchoring the discriminator at staging-buffer +0xBA = on-disk +0xBA. Full enumeration DBG-pending.
    // spec: Docs/RE/formats/items_scr.md §1.4.1 — on-disk +0xBA tested != 14: loader-resolved.
    // spec: Docs/RE/formats/items_scr.md §1.7 — "+0x0B8 item_type_tag" REFUTED; "+0xD2/0x18-shift" REFUTED.
    private const int OffRecordDiscriminator = 0x0BA; // on-disk +0xBA; spec: Docs/RE/formats/items_scr.md §1.4.1

    // dispatch flag bytes @ on-disk +0xCD, +0xCE, +0xCF, +0xD0 — consulted alongside the discriminator.
    // For each flag byte, == 1 maps to comparison codes 1 / 26 / 11 / 16 respectively.
    // Per-flag semantics are DBG-pending. No 0x18 shift (same staging-buffer base as discriminator).
    // spec: Docs/RE/formats/items_scr.md §1.4.1 — dispatch flags on-disk +0xCD..+0xD0: loader-resolved.
    private const int OffDispatchFlags = 0x0CD; // 4 consecutive bytes; spec: Docs/RE/formats/items_scr.md §1.4.1

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

    // CP949 encoding — hoisted to avoid per-record construction (≈90,937 records per file).
    // The static constructor registers the provider before the field is used;
    // DecodeRecord accesses this field only after Parse() has run, ensuring correct init order.
    // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
    private static readonly Encoding Cp949;

    static ItemsScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }
    // byte 0x07 = pad/unused — PLAUSIBLE

    /// <summary>
    ///     Parses all records from <c>data/script/items.scr</c>.
    ///     Walks the file to EOF, handling variable-stride records
    ///     (stride = 0x224 + 8 × effect_count).
    ///     A short final read at EOF is handled defensively and terminates iteration.
    /// </summary>
    /// <param name="data">
    ///     Raw file bytes from the VFS (full <c>items.scr</c> payload).
    /// </param>
    /// <returns>
    ///     Enumerable of all decoded <see cref="ItemsScrRecord" /> instances.
    ///     Caller may use <c>ToArray()</c> or <c>ToList()</c> to materialise.
    /// </returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/items_scr.md §1.3 — "Walk to EOF; no stored count": CONFIRMED.
    ///     spec: Docs/RE/formats/items_scr.md §1.2 — "Per-record stride = 0x224 + 8 × effect_count": CONFIRMED.
    /// </remarks>
    public static IEnumerable<ItemsScrRecord> Parse(ReadOnlyMemory<byte> data)
    {
        // CP949 provider + encoding are registered once in the static constructor; no per-call
        // re-registration here.
        // spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
        var pos = 0;
        var totalLen = data.Length;

        while (pos < totalLen)
        {
            // Defensive: check there are enough bytes for the fixed block.
            // A short final read terminates iteration (the loader likewise loops until EOF/short read).
            // spec: Docs/RE/formats/items_scr.md §1.3 — "Terminate at EOF / short read": CONFIRMED.
            if (totalLen - pos < FixedBlockSize)
                break; // short final read — EOF handling

            // Decode the record via a non-iterator helper (spans cannot cross yield boundaries).
            // spec: Docs/RE/formats/items_scr.md §1.2 — "Per-record stride = 0x224 + 8 × effect_count": CONFIRMED.
            var record = DecodeRecord(data, pos, totalLen);
            var recordStride = FixedBlockSize + record.EffectCount * EffectEntryStride;

            yield return record;

            pos += recordStride;
        }
    }

    /// <summary>
    ///     Decodes a single <c>items.scr</c> record starting at <paramref name="recordOffset" />.
    ///     Isolated from the iterator so that <see cref="ReadOnlySpan{T}" /> locals do not cross
    ///     <c>yield</c> boundaries (CS4007).
    /// </summary>
    private static ItemsScrRecord DecodeRecord(ReadOnlyMemory<byte> data, int recordOffset, int totalLen)
    {
        // M1: in-method bounds guard — makes this method safe even when called directly.
        // The outer loop already breaks on short reads; this guard closes the local safety gap.
        // spec: Docs/RE/formats/items_scr.md §1.2 — "fixed 548-byte (0x224) block": CONFIRMED.
        if ((uint)recordOffset + FixedBlockSize > (uint)totalLen)
            throw new InvalidDataException(
                $"items.scr parse error: record at offset {recordOffset}: " +
                $"fixed block requires {FixedBlockSize} bytes but only {totalLen - recordOffset} bytes remain. " +
                "spec: Docs/RE/formats/items_scr.md §1.2.");

        // Take a span of just the fixed block — lifetime bounded to this call frame.
        var fixedBlock = data.Span.Slice(recordOffset, FixedBlockSize);

        // item_name CP949[52] @ 0x000. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED (90,937/90,937).
        var nameBytes = fixedBlock.Slice(OffItemName, ItemNameLen);
        var nameNul = nameBytes.IndexOf((byte)0);
        var itemName = nameNul >= 0
            ? Cp949.GetString(nameBytes[..nameNul])
            : Cp949.GetString(nameBytes);

        // item_uid u32LE @ 0x034. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32LE @0x034: CONFIRMED (90,937/90,937).
        var itemUid = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffItemUid..]);

        // item_desc CP949 NUL-terminated @ 0x038. CONFIRMED present; extent within block UNVERIFIED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc @0x038: CONFIRMED present; extent UNVERIFIED.
        var descRegion = fixedBlock[OffItemDesc..];
        var descNul = descRegion.IndexOf((byte)0);
        var itemDesc = descNul >= 0
            ? Cp949.GetString(descRegion[..descNul])
            : Cp949.GetString(descRegion);

        // model_ref_key u32 @ 0x080. Loader-resolved as model-reference key.
        // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
        // spec: Docs/RE/formats/items_scr.md §1.7 — free-stat reading at +0x080 REFUTED; it is an asset-lookup key.
        var modelRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffModelRefKey..]);

        // anim_ref_key u32 @ 0x084. Loader-resolved as animation-reference key.
        // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
        var animRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffAnimRefKey..]);

        // opaque_0a4: 4 bytes @ 0x0A4. Read and retained; no consumer semantics settled — DBG-pending.
        // Offset is absolute from record start; there is NO 0x18 working-buffer shift.
        // The "+0xD2 / 0x18-ahead working buffer" model is REFUTED (CYCLE 7, IDB 263bd994) — see §1.7.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
        // spec: Docs/RE/formats/items_scr.md §1.7 — "+0xD2/0x18-shift" REFUTED.
        var opaque0A4 = data.Slice(recordOffset + OffOpaque0A4, 4);

        // record_discriminator u8 @ on-disk +0xBA. Tested != 14 by the loader for per-record routing.
        // CORRECTED CYCLE 7 (IDB 263bd994): the loader reads the 548-byte block directly into the staging
        // buffer (read base = record start); staging-buffer +0xBA = on-disk +0xBA — NO 0x18 shift.
        // The earlier +0xD2 / "0x18-ahead working buffer" model is REFUTED. See §1.4.1 and §1.7.
        // spec: Docs/RE/formats/items_scr.md §1.4.1 — on-disk +0xBA tested != 14: loader-resolved.
        // spec: Docs/RE/formats/items_scr.md §1.7 — "+0xD2/0x18-shift" REFUTED; "+0xB8 item_type_tag" REFUTED.
        // Full discriminator value enumeration is DBG-pending; read the byte, assign no meaning.
        var recordDiscriminator = fixedBlock[OffRecordDiscriminator];

        // dispatch flag bytes @ on-disk +0xCD..+0xD0 (4 consecutive bytes).
        // The loader consults each alongside the discriminator; == 1 maps to codes 1 / 26 / 11 / 16.
        // Per-flag semantics are DBG-pending; read and retain, assign no meaning.
        // spec: Docs/RE/formats/items_scr.md §1.4.1 — dispatch flags +0xCD..+0xD0: loader-resolved.
        var dispatchFlags = data.Slice(recordOffset + OffDispatchFlags, 4);

        // opaque_200: 4 bytes @ 0x200. Read and retained; no consumer semantics settled — DBG-pending.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
        var opaque200 = data.Slice(recordOffset + OffOpaque200, 4);

        // opaque_21c: 4 bytes @ 0x21C. Read and retained; no consumer semantics settled — DBG-pending.
        // Non-zero in only a small subset of records.
        // spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
        var opaque21C = data.Slice(recordOffset + OffOpaque21C, 4);

        // effect_count u8 @ 0x220. CONFIRMED.
        // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED (90,937/90,937).
        var effectCount = fixedBlock[OffEffectCount];

        // Validate there are enough bytes for the trailing effect array.
        var trailingSize = effectCount * EffectEntryStride;
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
        var fullSpan = data.Span;
        var effectsBase = recordOffset + FixedBlockSize;
        for (var e = 0; e < effectCount; e++)
        {
            var entryBase = effectsBase + e * EffectEntryStride;
            var entry = fullSpan.Slice(entryBase, EffectEntryStride);

            // effect_a u16 @+0x00 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_a u16 @+0x00.
            var effectA = BinaryPrimitives.ReadUInt16LittleEndian(entry[..]);

            // effect_b s16 @+0x02 (signed) — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_b s16 @+0x02.
            var effectB = BinaryPrimitives.ReadInt16LittleEndian(entry[OffEffectB..]);

            // effect_c u16 @+0x04 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_c u16 @+0x04.
            var effectC = BinaryPrimitives.ReadUInt16LittleEndian(entry[OffEffectC..]);

            // effect_d u8 @+0x06 — PLAUSIBLE; role UNVERIFIED.
            // spec: Docs/RE/formats/items_scr.md §1.5 — effect_d u8 @+0x06.
            var effectD = entry[OffEffectD];
            // byte at +0x07 is pad/unused — not stored.

            effects[e] = new ItemEffectEntry
            {
                EffectA = effectA,
                EffectB = effectB,
                EffectC = effectC,
                EffectD = effectD
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
            DispatchFlags = dispatchFlags,
            Opaque200 = opaque200,
            Opaque21C = opaque21C,
            EffectCount = effectCount,
            Effects = effects,
            FixedBlockRaw = data.Slice(recordOffset, FixedBlockSize)
        };
    }
}