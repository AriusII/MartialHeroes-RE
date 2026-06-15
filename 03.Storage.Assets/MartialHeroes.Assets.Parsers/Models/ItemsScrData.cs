using System.Buffers.Binary;

namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  items.scr — Regular item master database
//  spec: Docs/RE/formats/items_scr.md §1 — CONFIRMED model "Model A"
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One 8-byte on-disk effect/upgrade entry from the trailing section of an
/// <c>items.scr</c> record (present only when <c>effect_count &gt; 0</c>).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §1.5 — on-disk 8-byte layout.
/// The runtime loader expands each on-disk entry into a wider in-memory record;
/// this type carries the raw on-disk bytes only.
/// </remarks>
public sealed class ItemEffectEntry
{
    // spec: Docs/RE/formats/items_scr.md §1.5 — effect_a u16 @+0x00: PLAUSIBLE (read shape); role UNVERIFIED.
    /// <summary>Effect field A (u16, role UNVERIFIED).</summary>
    public required ushort EffectA { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — effect_b s16 @+0x02: PLAUSIBLE (signedness); role UNVERIFIED.
    /// <summary>Effect field B (signed s16, role UNVERIFIED).</summary>
    public required short EffectB { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — effect_c u16 @+0x04: PLAUSIBLE (read shape); role UNVERIFIED.
    /// <summary>Effect field C (u16, role UNVERIFIED).</summary>
    public required ushort EffectC { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — effect_d u8 @+0x06: PLAUSIBLE (read shape); role UNVERIFIED.
    /// <summary>Effect field D (u8, role UNVERIFIED).</summary>
    public required byte EffectD { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — pad/unused @+0x07: PLAUSIBLE.
    // Byte 7 is padding; not stored.
}

/// <summary>
/// One logical item record from <c>data/script/items.scr</c>.
/// Layout: fixed 548-byte (0x224) block + optional trailing array of <see cref="ItemEffectEntry"/>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_scr.md §1.2 — "Record framing: CONFIRMED model".
/// Per-record stride = 0x224 + 8 × effect_count. CONFIRMED.
/// </remarks>
public sealed class ItemsScrRecord
{
    // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED (90,937/90,937).
    /// <summary>Item display name; fixed 52-byte CP949 buffer, NUL-terminated, zero-padded.</summary>
    public required string ItemName { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32 @0x034: CONFIRMED (90,937/90,937).
    /// <summary>Per-record unique identifier; increments by 1 within a family.</summary>
    public required uint ItemUid { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc CP949 @0x038: CONFIRMED present; exact extent UNVERIFIED.
    /// <summary>Item description string; CP949, NUL-terminated from offset 0x038 within the fixed block.</summary>
    public required string ItemDesc { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved as model-reference key.
    // spec: Docs/RE/formats/items_scr.md §1.7 — "free numeric stat at +0x080" REFUTED; this is an asset-lookup key.
    /// <summary>
    /// Model-reference key at +0x080. Loader-resolved against the model/asset-lookup path.
    /// Non-zero for item families that carry a visual model; identical across enchant variants.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
    /// </summary>
    public required uint ModelRefKey { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved as animation-reference key.
    /// <summary>
    /// Animation-reference key at +0x084. Loader-resolved against the animation/asset-lookup path.
    /// Identical across enchant variants of one base item; varies by item template/category.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
    /// </summary>
    public required uint AnimRefKey { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
    /// <summary>
    /// Opaque 4 bytes at +0x0A4. Read and retained; no consumer semantics settled.
    /// Reads as a plausible small float for some weapon families, but role unconfirmed.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque0A4 { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4.1 — record_discriminator u8 @+0xBA: loader-resolved (tested != 14).
    // CORRECTED CAMPAIGN VFS-MASTERY: prior "+0xB8 item_type_tag" is REFUTED (see §1.7).
    /// <summary>
    /// Record discriminator byte at +0x0BA. The loader branches on this value != 14.
    /// Full discriminator value enumeration is DBG-pending.
    /// DO NOT confuse with +0x0B8 (REFUTED — see items_scr.md §1.7).
    /// spec: Docs/RE/formats/items_scr.md §1.4.1 — discriminator @+0xBA tested != 14: loader-resolved.
    /// </summary>
    public required byte RecordDiscriminator { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
    /// <summary>
    /// Opaque 4 bytes at +0x200. Read and retained; no consumer semantics settled.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque200 { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
    /// <summary>
    /// Opaque 4 bytes at +0x21C. Read and retained; non-zero in only a small subset of records.
    /// spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque21C { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED (90,937/90,937).
    /// <summary>Count of trailing 8-byte effect/upgrade entries; drives per-record stride.</summary>
    public required byte EffectCount { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — effect entries: PLAUSIBLE (read shape).
    /// <summary>
    /// Optional trailing effect/upgrade entries (empty when <see cref="EffectCount"/> == 0).
    /// spec: Docs/RE/formats/items_scr.md §1.5.
    /// </summary>
    public required IReadOnlyList<ItemEffectEntry> Effects { get; init; }

    /// <summary>
    /// Full 548-byte (0x224) raw fixed block, zero-copy slice of the original buffer.
    /// Carries all fields including those not yet mapped to typed properties.
    /// </summary>
    public required ReadOnlyMemory<byte> FixedBlockRaw { get; init; }
}