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

    // spec: Docs/RE/formats/items_scr.md §1.4 — stat_f32 f32 @0x0A4: PARTIAL; semantic UNVERIFIED.
    /// <summary>
    /// Inferred float stat at +0x0A4 (e.g. small magnitudes for weapons: 1.0, 4.0, 40.0).
    /// Semantic UNVERIFIED.
    /// </summary>
    public required float StatF32 { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — item_type_tag u32 @0x0B8: PARTIAL; role UNVERIFIED.
    /// <summary>Packed item-type tag; candidate equip-slot/category indicator. Role UNVERIFIED.</summary>
    public required uint ItemTypeTag { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — template_ref u32 @0x200: UNVERIFIED.
    /// <summary>Sequential template/index reference; increases across weapon/armour tiers. Role UNVERIFIED.</summary>
    public required uint TemplateRef { get; init; }

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