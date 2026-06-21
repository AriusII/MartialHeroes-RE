namespace MartialHeroes.Assets.Parsers.DataTables.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  items.scr — Regular item master database
//  spec: Docs/RE/formats/items_scr.md §1 — CONFIRMED model "Model A"
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One 8-byte on-disk effect/upgrade entry from the trailing section of an
///     <c>items.scr</c> record (present only when <c>effect_count &gt; 0</c>).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/items_scr.md §1.5 — on-disk 8-byte layout.
///     The runtime loader expands each on-disk entry into a wider in-memory record;
///     this type carries the raw on-disk bytes only.
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
///     One logical item record from <c>data/script/items.scr</c>.
///     Layout: fixed 548-byte (0x224) block + optional trailing array of <see cref="ItemEffectEntry" />.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/items_scr.md §1.2 — "Record framing: CONFIRMED model".
///     Per-record stride = 0x224 + 8 × effect_count. CONFIRMED.
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

    // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: .skn mesh selector, HIGH.
    // spec: Docs/RE/formats/items_scr.md §1.4.2 — +0x080 = data/char/skin/g<ModelRefKey>.skn (printf selector).
    // spec: Docs/RE/formats/items_scr.md §1.7 — "free numeric stat at +0x080" REFUTED; this is an asset-lookup key.
    /// <summary>
    ///     <c>.skn</c> mesh selector at +0x080. Resolved at actor-spawn via the shared actor-visual catalogue
    ///     to the file path <c>data/char/skin/g&lt;ModelRefKey&gt;.skn</c> (printf-formatted numeric selector).
    ///     Non-zero for item families that carry a visual model; identical across enchant variants.
    ///     spec: Docs/RE/formats/items_scr.md §1.4.2 — "+0x080 is the .skn MESH SELECTOR resolving to
    ///     data/char/skin/g&lt;model_ref_key&gt;.skn (printf selector, HIGH)": CONFIRMED.
    /// </summary>
    public required uint ModelRefKey { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: bind-pose/skeleton POOL id.
    // spec: Docs/RE/formats/items_scr.md §1.4.2 — +0x084 is a POOL id, NOT a direct printf.
    //   It indexes the pre-loaded g{id}.bnd / g{id}.mot pool seeded from bindlist.txt.
    //   Exact item-side g{id}.bnd file is OPEN-RISK A3-2 (not byte-pinned).
    /// <summary>
    ///     Bind-pose/skeleton-pool id at +0x084. Resolved at actor-spawn by id-lookup into the pre-loaded
    ///     bind-pose/skeleton pool (the <c>data/char/bind/g{id}.bnd</c> skeletons +
    ///     <c>data/char/mot/g{id}.mot</c> motions seeded at boot from <c>bindlist.txt</c>).
    ///     This is NOT a direct printf — it is a pool id. Identical across enchant variants; varies by template.
    ///     OPEN-RISK A3-2: the exact item-side <c>g{id}.bnd</c>/<c>.mot</c> file is NOT byte-pinned.
    ///     spec: Docs/RE/formats/items_scr.md §1.4.2 — "+0x084 is a BIND-POSE/SKELETON-POOL ID
    ///     (NOT a direct printf); HIGH that it is a pool id; MEDIUM that the exact item-side g{id} file
    ///     is the 1:1 member": CONFIRMED (pool id HIGH).
    /// </summary>
    public required uint AnimRefKey { get; init; }

    /// <summary>
    ///     Convenience helper: the VFS path of the <c>.skn</c> mesh for this item, derived from
    ///     <see cref="ModelRefKey" /> as the printf selector.
    ///     Format: <c>data/char/skin/g{ModelRefKey}.skn</c>.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/items_scr.md §1.4.2 — the +0x080 printf selector resolves to
    ///     <c>data/char/skin/g%d.skn</c> at actor spawn. HIGH confidence.
    /// </remarks>
    public string SknVfsPath => $"data/char/skin/g{ModelRefKey}.skn"; // spec: Docs/RE/formats/items_scr.md §1.4.2

    /// <summary>
    ///     The bind-pose/skeleton-pool id for this item record — a direct alias for <see cref="AnimRefKey" />.
    ///     This is a pool id, NOT a file path (OPEN-RISK A3-2: the exact <c>g{id}.bnd</c>/<c>.mot</c>
    ///     file is not byte-pinned; do NOT format a file path from this value).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/items_scr.md §1.4.2 — "+0x084 is the pool id; do NOT format a file path
    ///     for it — OPEN-RISK A3-2". HIGH that it is a pool id.
    /// </remarks>
    public uint BindPosePoolId => AnimRefKey; // spec: Docs/RE/formats/items_scr.md §1.4.2 — pool id, NOT a printf

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
    /// <summary>
    ///     Opaque 4 bytes at +0x0A4. Read and retained; no consumer semantics settled.
    ///     Reads as a plausible small float for some weapon families, but role unconfirmed.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — +0x0A4 (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque0A4 { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4.1 — record_discriminator u8 @on-disk +0xBA: tested != 14.
    // CORRECTED CYCLE 7 (IDB 263bd994): the loader reads the 548-byte block directly into the staging buffer
    // (read base = record start). There is NO 0x18 shift — the earlier "+0xD2 / 0x18-ahead working buffer"
    // model is REFUTED. The CONFIRMED effect_count at on-disk +0x220 anchors this discriminator at +0xBA.
    // Prior "+0xB8 item_type_tag" is also REFUTED (see §1.7).
    /// <summary>
    ///     Record discriminator byte at on-disk offset +0xBA. The loader branches on this value != 14.
    ///     The loader reads the 548-byte block directly into the staging buffer (read base = record start),
    ///     so staging-buffer +0xBA = on-disk +0xBA — there is NO 0x18 shift.
    ///     The earlier "+0xD2 / 0x18-shift" model is REFUTED (CYCLE 7, IDB 263bd994).
    ///     Full discriminator value enumeration is DBG-pending.
    ///     DO NOT confuse with +0x0B8 (REFUTED) or +0xD2 (REFUTED) — see items_scr.md §1.7.
    ///     spec: Docs/RE/formats/items_scr.md §1.4.1 — on-disk +0xBA tested != 14: loader-resolved.
    /// </summary>
    public required byte RecordDiscriminator { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4.1 — dispatch flags @on-disk +0xCD..+0xD0 (4 bytes).
    // For each byte, == 1 maps to comparison codes 1 / 26 / 11 / 16 respectively.
    // Per-flag semantics are DBG-pending. No 0x18 shift (same staging-buffer base as discriminator).
    /// <summary>
    ///     Four dispatch flag bytes at on-disk offsets +0xCD, +0xCE, +0xCF, +0xD0.
    ///     The loader consults each alongside <see cref="RecordDiscriminator" />; for each byte,
    ///     a value of 1 maps to comparison codes 1 / 26 / 11 / 16 respectively.
    ///     Per-flag semantics are DBG-pending; retain as opaque until a live-debugger pass settles them.
    ///     spec: Docs/RE/formats/items_scr.md §1.4.1 — dispatch flags +0xCD..+0xD0: loader-resolved.
    /// </summary>
    public required ReadOnlyMemory<byte> DispatchFlags { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
    /// <summary>
    ///     Opaque 4 bytes at +0x200. Read and retained; no consumer semantics settled.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — +0x200 (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque200 { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
    /// <summary>
    ///     Opaque 4 bytes at +0x21C. Read and retained; non-zero in only a small subset of records.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — +0x21C (opaque): DBG-pending.
    /// </summary>
    public required ReadOnlyMemory<byte> Opaque21C { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED (90,937/90,937).
    /// <summary>Count of trailing 8-byte effect/upgrade entries; drives per-record stride.</summary>
    public required byte EffectCount { get; init; }

    // spec: Docs/RE/formats/items_scr.md §1.5 — effect entries: PLAUSIBLE (read shape).
    /// <summary>
    ///     Optional trailing effect/upgrade entries (empty when <see cref="EffectCount" /> == 0).
    ///     spec: Docs/RE/formats/items_scr.md §1.5.
    /// </summary>
    public required IReadOnlyList<ItemEffectEntry> Effects { get; init; }

    /// <summary>
    ///     Full 548-byte (0x224) raw fixed block, zero-copy slice of the original buffer.
    ///     Carries all fields including those not yet mapped to typed properties.
    /// </summary>
    public required ReadOnlyMemory<byte> FixedBlockRaw { get; init; }
}