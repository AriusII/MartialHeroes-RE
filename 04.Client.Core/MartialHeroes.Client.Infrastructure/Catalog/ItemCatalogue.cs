using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
///     In-memory lookup catalogue for item definitions parsed from the <b>runtime</b> item master
///     <c>data/script/items.scr</c>. Provides lookup by item UID, returning a typed
///     <see cref="ItemCatalogueRecord" /> derived from the CONFIRMED / loader-resolved fields of the
///     binary record.
/// </summary>
/// <remarks>
///     <para>
///         <b>Runtime source = items.scr.</b> The shipping client loads the binary <c>items.scr</c> master at
///         runtime, not <c>items.csv</c> (which is an authoring/dev export the client never reads). This
///         catalogue is therefore built from <see cref="ItemsScrRecord" /> values produced by
///         <see cref="ItemsScrParser" />.
///         spec: Docs/RE/formats/items_csv.md §6 — "A faithful re-implementation MUST read items.scr for
///         runtime item data and treat items.csv as a human-editable side export only": CONFIRMED.
///         spec: Docs/RE/formats/items_scr.md §4 — engineer guidance (walk items.scr; read item_uid @0x034).
///     </para>
///     <para>
///         <b>Only CONFIRMED / loader-resolved fields are surfaced.</b> The items.scr fixed-block numeric
///         stat ROLES (attack / defense / required-stat columns) are UNVERIFIED / DBG-pending
///         (spec: items_scr.md §1.6 Known Unknowns), so this catalogue deliberately does NOT invent typed
///         stat properties from them. The surfaced fields are the SAMPLE-VERIFIED / loader-resolved ones:
///         item name (+0x000), item UID (+0x034), description (+0x038), the model / animation reference keys
///         (+0x080 / +0x084), the record discriminator (on-disk +0x0D2), and the effect count (+0x220).
///         spec: Docs/RE/formats/items_scr.md §1.4 (field layout) / §1.6 (numeric stat roles UNVERIFIED).
///     </para>
///     <para>
///         CP949 decoding (names / descriptions) is handled upstream by
///         <see cref="ItemsScrParser" />.
///         spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
///     </para>
/// </remarks>
public sealed class ItemCatalogue
{
    private readonly Dictionary<uint, ItemCatalogueRecord> _byId;

    /// <summary>
    ///     Constructs the runtime catalogue from pre-parsed <c>items.scr</c> records.
    ///     If <paramref name="records" /> is empty, <see cref="TryGet" /> will always return
    ///     <see langword="null" />.
    /// </summary>
    /// <param name="records">
    ///     Records as returned by <see cref="ItemsScrParser.Parse" />.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 fixed-block field layout.
    /// </param>
    public ItemCatalogue(IReadOnlyList<ItemsScrRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _byId = new Dictionary<uint, ItemCatalogueRecord>(records.Count);

        foreach (var row in records)
        {
            var record = new ItemCatalogueRecord
            {
                // item_name CP949[52] @0x000. CONFIRMED (90,937/90,937).
                // spec: Docs/RE/formats/items_scr.md §1.4 — item_name CP949[52] @0x000: CONFIRMED.
                Name = row.ItemName,

                // item_uid u32 @0x034 — the per-record lookup-tree key. SAMPLE-VERIFIED.
                // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid u32 @0x034: SAMPLE-VERIFIED.
                ItemId = row.ItemUid,

                // item_desc CP949 NUL-terminated @0x038. CONFIRMED present.
                // spec: Docs/RE/formats/items_scr.md §1.4 — item_desc CP949 @0x038: CONFIRMED present.
                Description = row.ItemDesc,

                // model_ref_key u32 @0x080 — loader-resolved model/asset-lookup key.
                // spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
                ModelRefKey = row.ModelRefKey,

                // anim_ref_key u32 @0x084 — loader-resolved animation/asset-lookup key.
                // spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
                AnimRefKey = row.AnimRefKey,

                // record_discriminator u8 @ on-disk +0x0D2 — the loader branches on this (!= 14).
                // spec: Docs/RE/formats/items_scr.md §1.4.1 — discriminator on-disk +0xD2 tested != 14.
                RecordDiscriminator = row.RecordDiscriminator,

                // effect_count u8 @0x220 — count of trailing 8-byte effect entries. CONFIRMED.
                // spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED.
                EffectCount = row.EffectCount
            };

            // Each record's item_uid is unique; when a family carries colliding uids (e.g. when a
            // partial walk re-reads), the last occurrence wins.
            // spec: Docs/RE/formats/items_scr.md §1.4 — item_uid is the per-record lookup-tree key.
            _byId[row.ItemUid] = record;
        }
    }

    /// <summary>Number of items in this catalogue.</summary>
    public int Count => _byId.Count;

    /// <summary>
    ///     Creates the <b>runtime</b> <see cref="ItemCatalogue" /> by loading <c>items.scr</c> from the
    ///     given loader. This is the faithful runtime path.
    ///     spec: Docs/RE/formats/items_csv.md §6 (runtime item data comes from items.scr).
    /// </summary>
    public static ItemCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ItemCatalogue(loader.LoadItemsScr());
    }

    /// <summary>
    ///     Looks up an item by its unique UID (items.scr <c>item_uid</c> @0x034).
    ///     Returns <see langword="null" /> when the ID is not present.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — item_uid is the per-record lookup-tree key.
    /// </summary>
    public ItemCatalogueRecord? TryGet(uint itemId)
    {
        return _byId.TryGetValue(itemId, out var r) ? r : null;
    }
}

/// <summary>
///     A decoded item record, derived from the CONFIRMED / loader-resolved fields of the runtime
///     <c>items.scr</c> master. All field annotations cite spec: Docs/RE/formats/items_scr.md §1.4.
/// </summary>
/// <remarks>
///     The fixed-block numeric stat ROLES (attack / defense / required-stat columns) are UNVERIFIED /
///     DBG-pending (spec: items_scr.md §1.6) and are intentionally NOT modelled here; a faithful
///     catalogue must not invent stat values. When a debugger pass pins those roles, add them with a
///     spec citation and confidence tag.
/// </remarks>
public sealed record ItemCatalogueRecord
{
    /// <summary>Display name (CP949-decoded), item_name @0x000. CONFIRMED.</summary>
    public required string Name { get; init; }

    /// <summary>Per-record unique id (item_uid @0x034); the catalogue lookup key. SAMPLE-VERIFIED.</summary>
    public required uint ItemId { get; init; }

    /// <summary>Description text (CP949-decoded), item_desc @0x038. CONFIRMED present.</summary>
    public required string Description { get; init; }

    /// <summary>
    ///     Model-reference key (@0x080); loader-resolved against the model/asset-lookup path.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — model_ref_key u32 @0x080: loader-resolved.
    /// </summary>
    public required uint ModelRefKey { get; init; }

    /// <summary>
    ///     Animation-reference key (@0x084); loader-resolved against the animation/asset-lookup path.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — anim_ref_key u32 @0x084: loader-resolved.
    /// </summary>
    public required uint AnimRefKey { get; init; }

    /// <summary>
    ///     Record discriminator byte at on-disk +0x0D2; the loader branches on this (tested != 14).
    ///     Full value enumeration is DBG-pending — carried as a raw byte, no meaning assigned.
    ///     spec: Docs/RE/formats/items_scr.md §1.4.1 — discriminator on-disk +0xD2 tested != 14.
    /// </summary>
    public required byte RecordDiscriminator { get; init; }

    /// <summary>
    ///     Count of trailing 8-byte effect/upgrade entries (effect_count @0x220). CONFIRMED.
    ///     spec: Docs/RE/formats/items_scr.md §1.4 — effect_count u8 @0x220: CONFIRMED.
    /// </summary>
    public required byte EffectCount { get; init; }
}