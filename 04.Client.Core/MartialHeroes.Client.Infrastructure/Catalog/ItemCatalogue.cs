using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// In-memory lookup catalogue for item definitions parsed from <c>data/script/items.csv</c>.
/// Provides lookup by item ID, returning a typed <see cref="ItemCatalogueRecord"/> derived from
/// the confirmed columns of the CSV.
/// </summary>
/// <remarks>
/// <para>
/// items.csv is the human-editable source for item definitions.
/// spec: Docs/RE/formats/config_tables.md §4 items.csv — "VFS path: data/script/items.csv": CONFIRMED.
/// spec: Docs/RE/formats/config_tables.md §4.1 — "Total rows: 89,712; Columns: 139; Encoding:
///   CP949/EUC-KR (no BOM); Delimiter: comma; No header row": CONFIRMED.
/// </para>
/// <para>
/// Only columns with CONFIRMED or HIGH confidence are surfaced as typed properties on
/// <see cref="ItemCatalogueRecord"/>. UNVERIFIED columns are discarded. Any consumer needing
/// a column not yet surfaced must add it to the record with a spec citation and confidence tag.
/// </para>
/// <para>
/// CP949 decoding is handled upstream by <see cref="MartialHeroes.Assets.Parsers.ItemsCsvParser"/>.
/// spec: Docs/RE/formats/config_tables.md §4.1 — "Encoding: CP949/EUC-KR": CONFIRMED.
/// </para>
/// </remarks>
public sealed class ItemCatalogue
{
    private readonly Dictionary<uint, ItemCatalogueRecord> _byId;

    /// <summary>
    /// Constructs the catalogue from pre-parsed CSV rows.
    /// If <paramref name="rows"/> is empty, <see cref="TryGet"/> will always return
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="rows">
    /// Rows as returned by <see cref="MartialHeroes.Assets.Parsers.ItemsCsvParser.Parse"/>.
    /// spec: Docs/RE/formats/config_tables.md §4.3 Column index table.
    /// </param>
    public ItemCatalogue(ItemCsvRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _byId = new Dictionary<uint, ItemCatalogueRecord>(rows.Length);

        foreach (ItemCsvRow row in rows)
        {
            // col1: item_id uint32. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §4.3 — "col1 item_id uint32: CONFIRMED".
            var record = new ItemCatalogueRecord
            {
                // ── Identity ────────────────────────────────────────────────
                // col0: name_cp949 string. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col0 name_cp949 string: CONFIRMED".
                Name = row.NameCp949,

                // col1: item_id uint32. CONFIRMED.
                ItemId = row.ItemId,

                // col2: description_cp949 string. CONFIRMED (\\=in-game newline preserved verbatim).
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col2 description_cp949 string: CONFIRMED".
                Description = row.DescriptionCp949,

                // col6: item_subtype uint32. CONFIRMED. See §4.6 subtype reference table.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col6 item_subtype uint32: CONFIRMED".
                ItemSubtype = row.ItemSubtype,

                // ── Flags and meta ───────────────────────────────────────────
                // col16: sell_price uint32. CONFIRMED. Doubles per enchant level.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col16 sell_price uint32: CONFIRMED".
                SellPrice = row.SellPrice,

                // col17: npc_purchaseable uint8. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col17 npc_purchaseable: HIGH".
                NpcPurchaseable = row.NpcPurchaseable != 0,

                // col18: enabled uint8. CONFIRMED. 1 = live item visible to players.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col18 enabled uint8: CONFIRMED".
                Enabled = row.Enabled != 0,

                // ── Stacking, tier, durability ───────────────────────────────
                // col19: max_stack uint16. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col19 max_stack: CONFIRMED".
                MaxStack = row.MaxStack,

                // col22: item_tier_rank uint16. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col22 item_tier_rank: CONFIRMED".
                ItemTierRank = row.ItemTierRank,

                // col23: max_durability uint16. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col23 max_durability: HIGH".
                MaxDurability = row.MaxDurability,

                // ── Required stats (cols 24–28) ──────────────────────────────
                // col24: req_STR. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col24 req_STR uint16: HIGH".
                ReqStr = row.ReqStr,

                // col25: req_CON. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col25 req_CON uint16: HIGH".
                ReqCon = row.ReqCon,

                // col26: req_AGI. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col26 req_AGI uint16: HIGH".
                ReqAgi = row.ReqAgi,

                // col27: req_INT. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col27 req_INT uint16: HIGH".
                ReqInt = row.ReqInt,

                // col28: req_CHI. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col28 req_CHI uint16: HIGH".
                ReqChi = row.ReqChi,

                // ── Class restriction flags (cols 29–32) ─────────────────────
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col29–32 class flags: HIGH".
                ClassYi = row.ClassYi != 0,
                ClassYe = row.ClassYe != 0,
                ClassIn = row.ClassIn != 0,
                ClassJi = row.ClassJi != 0,

                // ── Enchant ──────────────────────────────────────────────────
                // col47: enchant_level. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col47 enchant_level uint8: CONFIRMED".
                EnchantLevel = row.EnchantLevel,

                // ── Bonus stats ──────────────────────────────────────────────
                // col64: bonus_atk. HIGH. col65: bonus_HP. CONFIRMED. col68: bonus_ext_atk. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col64/65/68: HIGH/CONFIRMED".
                BonusAtk = row.BonusAtk,
                BonusHp = row.BonusHp,
                BonusExtAtk = row.BonusExtAtk,

                // col84: bonus_chi. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col84 bonus_chi: HIGH".
                BonusChi = row.BonusChi,

                // col87: min_attack. CONFIRMED. col90: max_attack. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col87/90 min/max attack: CONFIRMED".
                MinAttack = row.MinAttack,
                MaxAttack = row.MaxAttack,

                // col93: bonus_defense_A. HIGH. col94: phys_defense. CONFIRMED. col96: armor_defense. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col93/94/96: HIGH/CONFIRMED/HIGH".
                BonusDefenseA = row.BonusDefenseA,
                PhysDefense = row.PhysDefense,
                ArmorDefense = row.ArmorDefense,

                // ── Consumable ───────────────────────────────────────────────
                // col112: duration_minutes. CONFIRMED.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col112 duration_minutes: CONFIRMED".
                DurationMinutes = row.DurationMinutes,

                // col120: is_consumable. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col120 is_consumable: HIGH".
                IsConsumable = row.IsConsumable != 0,

                // col127: gem_category. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col127 gem_category: HIGH".
                GemCategory = row.GemCategory,

                // col128: equippable_flag. HIGH.
                // spec: Docs/RE/formats/config_tables.md §4.3 — "col128 equippable_flag: HIGH".
                Equippable = row.EquippableFlag != 0,
            };

            // Use the last occurrence if duplicate IDs appear (they are enchant-level variants;
            // callers that need all variants should use the raw rows directly).
            _byId[row.ItemId] = record;
        }
    }

    /// <summary>
    /// Creates an <see cref="ItemCatalogue"/> by loading <c>items.csv</c> from the given loader.
    /// </summary>
    public static ItemCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ItemCatalogue(loader.LoadItemsCsv());
    }

    /// <summary>Number of items in this catalogue.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Looks up an item by its unique ID.
    /// Returns <see langword="null"/> when the ID is not present.
    /// spec: Docs/RE/formats/config_tables.md §4.3 — col1 item_id is the unique key.
    /// </summary>
    public ItemCatalogueRecord? TryGet(uint itemId) =>
        _byId.TryGetValue(itemId, out var r) ? r : null;
}

/// <summary>
/// A decoded item record, derived from confirmed / HIGH-confidence columns of items.csv.
/// All column annotations cite spec: Docs/RE/formats/config_tables.md §4.3.
/// </summary>
/// <remarks>
/// Columns with UNVERIFIED confidence are intentionally omitted. Use
/// <see cref="ItemCsvRow.RawColumns"/> via the parser if you need untyped access.
/// </remarks>
public sealed record ItemCatalogueRecord
{
    // ── Identity ───────────────────────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 0–6: CONFIRMED.

    /// <summary>Display name (CP949-decoded). col0. CONFIRMED.</summary>
    public required string Name { get; init; }

    /// <summary>Unique item ID. col1. CONFIRMED.</summary>
    public required uint ItemId { get; init; }

    /// <summary>Tooltip / lore text (CP949-decoded; <c>\\</c>=in-game newline). col2. CONFIRMED.</summary>
    public required string Description { get; init; }

    /// <summary>Item category code. col6. CONFIRMED. See §4.6 subtype table.</summary>
    public required uint ItemSubtype { get; init; }

    // ── Flags and meta ──────────────────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 16–18.

    /// <summary>NPC sell price in game currency. col16. CONFIRMED.</summary>
    public required uint SellPrice { get; init; }

    /// <summary>True = available for direct NPC purchase. col17. HIGH.</summary>
    public required bool NpcPurchaseable { get; init; }

    /// <summary>True = live item visible to players. col18. CONFIRMED.</summary>
    public required bool Enabled { get; init; }

    // ── Stacking, tier, durability ───────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 19–23.

    /// <summary>Maximum stack size. col19. CONFIRMED.</summary>
    public required ushort MaxStack { get; init; }

    /// <summary>Item tier / quality rank. col22. CONFIRMED.</summary>
    public required ushort ItemTierRank { get; init; }

    /// <summary>Maximum durability. col23. HIGH.</summary>
    public required ushort MaxDurability { get; init; }

    // ── Required stats (cols 24–28) ─────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 24–28: HIGH.
    // NOTE: col-order stat identity caveat from spec §4.3 applies —
    //   the exact STR/CON/AGI/INT/CHI order in these five columns is HIGH but the
    //   definitive cross-check with the runtime order is UNVERIFIED.
    //   spec: §Known unknowns #44 — "req column stat order UNVERIFIED (CSV-specific vs runtime)".

    /// <summary>Required Strength. col24. HIGH. Stat-column order caveat: see spec §Known unknowns #44.</summary>
    public required ushort ReqStr { get; init; }

    /// <summary>Required Constitution. col25. HIGH.</summary>
    public required ushort ReqCon { get; init; }

    /// <summary>Required Agility. col26. HIGH.</summary>
    public required ushort ReqAgi { get; init; }

    /// <summary>Required Intelligence. col27. HIGH.</summary>
    public required ushort ReqInt { get; init; }

    /// <summary>Required Chi / Internal Force. col28. HIGH.</summary>
    public required ushort ReqChi { get; init; }

    // ── Class restriction flags (cols 29–32) ────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 29–32: HIGH.

    /// <summary>Yi (義) class can equip. col29. HIGH.</summary>
    public required bool ClassYi { get; init; }

    /// <summary>Ye (禮) class can equip. col30. HIGH.</summary>
    public required bool ClassYe { get; init; }

    /// <summary>In (仁) class can equip. col31. HIGH.</summary>
    public required bool ClassIn { get; init; }

    /// <summary>Ji (智) class can equip. col32. HIGH.</summary>
    public required bool ClassJi { get; init; }

    // ── Enchant ──────────────────────────────────────────────────────────────────

    /// <summary>Current enchant level (0 = base). col47. CONFIRMED.</summary>
    public required byte EnchantLevel { get; init; }

    // ── Bonus stats ──────────────────────────────────────────────────────────────
    // spec: Docs/RE/formats/config_tables.md §4.3 cols 64–96.

    /// <summary>Bonus attack power. col64. HIGH.</summary>
    public required uint BonusAtk { get; init; }

    /// <summary>Bonus HP. col65. CONFIRMED.</summary>
    public required uint BonusHp { get; init; }

    /// <summary>Bonus external/physical attack. col68. HIGH.</summary>
    public required uint BonusExtAtk { get; init; }

    /// <summary>Bonus Chi / internal force. col84. HIGH.</summary>
    public required uint BonusChi { get; init; }

    /// <summary>Minimum attack damage. col87. CONFIRMED.</summary>
    public required uint MinAttack { get; init; }

    /// <summary>Maximum attack damage. col90. CONFIRMED.</summary>
    public required uint MaxAttack { get; init; }

    /// <summary>Defense bonus A. col93. HIGH.</summary>
    public required uint BonusDefenseA { get; init; }

    /// <summary>Physical defense. col94. CONFIRMED.</summary>
    public required uint PhysDefense { get; init; }

    /// <summary>Armour defense value. col96. HIGH.</summary>
    public required uint ArmorDefense { get; init; }

    // ── Consumable ───────────────────────────────────────────────────────────────

    /// <summary>Duration for time-limited items (minutes); 0 = permanent. col112. CONFIRMED.</summary>
    public required uint DurationMinutes { get; init; }

    /// <summary>True = item has a use/consume effect. col120. HIGH.</summary>
    public required bool IsConsumable { get; init; }

    /// <summary>
    /// Gem socket category. 1=atk(red), 2=ext-atk(blue), 3=chi(green), 4=HP(black), 5=def(yellow).
    /// col127. HIGH. spec: Docs/RE/formats/config_tables.md §4.4 gem stone → stat column mapping.
    /// </summary>
    public required byte GemCategory { get; init; }

    /// <summary>True = item can be equipped or socketed. col128. HIGH.</summary>
    public required bool Equippable { get; init; }
}