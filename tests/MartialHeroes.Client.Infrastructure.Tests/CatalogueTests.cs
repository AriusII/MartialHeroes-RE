using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Tests for the Catalog layer adapters: <see cref="ScrStatCatalogue"/>,
/// <see cref="ItemCatalogue"/>, <see cref="SkillCatalogue"/>, <see cref="MobCatalogue"/>,
/// and <see cref="VfsCatalogueLoader"/>.
/// </summary>
/// <remarks>
/// All test fixtures are constructed in memory using the exact record types from
/// <c>Assets.Parsers.Models</c> that the catalogue constructors accept. No real VFS files
/// are opened. Byte layouts conform to specs cited in the production code.
/// </remarks>
public sealed class CatalogueTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // ScrStatCatalogue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An empty entry array produces empty curves (all-zero / fallback behaviour preserved).
    /// spec: Docs/RE/formats/config_tables.md §IMPORTANT —
    ///   "empty catalogue = prior all-zero behaviour preserved".
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_EmptyEntries_ReturnEmptyCurves()
    {
        var catalogue = new ScrStatCatalogue([]);

        Assert.True(catalogue.GetHpBaseCurve().IsEmpty);
        Assert.True(catalogue.GetMpBaseCurve().IsEmpty);
    }

    /// <summary>
    /// FIDELITY DEFAULT: even with non-empty userlevel.scr entries, the default catalogue returns the
    /// empty (0-base) curves. The real per-level HP/MP base computation is UNVERIFIED (the
    /// float-position→named-stat mapping and the actual (10/A)×B → HP/MP formula are open questions),
    /// so no magnitude is invented — the level-base term stays at its provisional 0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 (open questions #1/#2 — HP/MP base UNVERIFIED);
    ///       §IMPORTANT (level-base term provisionally 0 until userlevel.scr is decoded).
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_DefaultMode_ReturnsEmptyCurves_NoInventedMagnitude()
    {
        // These entries previously fed a no-spec-basis 100× proxy curve. The fidelity-correct default
        // must NOT emit a fabricated magnitude — it returns the empty (0-base) curve instead.
        // spec: Docs/RE/formats/config_tables.md §2.4 (#1/#2 UNVERIFIED).
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 1, divisorC: 0, posScale: 1.0f),
            MakeLevelEntry(level: 12, divisorC: 2, posScale: 1.0f),
            MakeLevelEntry(level: 36, divisorC: 4, posScale: 3.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries);

        Assert.True(catalogue.GetHpBaseCurve().IsEmpty);
        Assert.True(catalogue.GetMpBaseCurve().IsEmpty);
        Assert.Equal(0L, catalogue.GetHpBaseCurve().BaseForLevel(1));
        Assert.Equal(0L, catalogue.GetMpBaseCurve().BaseForLevel(36));
    }

    /// <summary>
    /// OPT-IN PROVISIONAL PROXY (no spec basis; debugging/tooling only): when explicitly requested
    /// via <c>useProvisionalCurve: true</c>, the catalogue builds the monotonic placeholder curve.
    /// This proxy is NEVER the default fidelity path and is exercised here only to keep the opt-in
    /// path covered. The 100× scale has no spec provenance.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — (10/A)×B; the scale constant itself has no spec.
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_OptInProvisionalCurve_BuildsMonotonicPlaceholder()
    {
        // L1: divisorC=0 (divide-by-zero guard) → 0
        // L12: divisorC=2, posScale=1.0 → (10/2)×3×1.0×100 = 1500
        // L36: divisorC=4, posScale=3.0 → (10/4)×3×3.0×100 = 2250
        // spec: §2.4 — "C=2 → 15.0; C=4 → 7.5 (B=3.0)" (value confirmed; scale is implementation-only).
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 1, divisorC: 0, posScale: 1.0f),
            MakeLevelEntry(level: 12, divisorC: 2, posScale: 1.0f),
            MakeLevelEntry(level: 36, divisorC: 4, posScale: 3.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries, useProvisionalCurve: true);
        var hp = catalogue.GetHpBaseCurve();

        Assert.False(hp.IsEmpty);
        long l1 = hp.BaseForLevel(1);
        long l12 = hp.BaseForLevel(2);
        long l36 = hp.BaseForLevel(3);
        Assert.Equal(0L, l1);
        Assert.Equal(1500L, l12);
        Assert.Equal(2250L, l36);
        Assert.True(l12 > l1 && l36 > l12, "proxy curve must grow monotonically across tiers");
        // Proxy uses group[0] for both HP and MP until the per-stat mapping is pinned.
        Assert.Equal(hp.BaseForLevel(2), catalogue.GetMpBaseCurve().BaseForLevel(2));
    }

    /// <summary>
    /// The opt-in proxy with an empty entry array still yields empty curves (no spurious allocation).
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_OptInProvisionalCurve_EmptyEntries_ReturnEmptyCurves()
    {
        var catalogue = new ScrStatCatalogue([], useProvisionalCurve: true);

        Assert.True(catalogue.GetHpBaseCurve().IsEmpty);
        Assert.True(catalogue.GetMpBaseCurve().IsEmpty);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ItemCatalogue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An empty catalogue has count 0 and TryGet returns null for any id.
    /// Built from the runtime items.scr record source.
    /// spec: Docs/RE/formats/items_csv.md §6 (runtime source = items.scr).
    /// </summary>
    [Fact]
    public void ItemCatalogue_Empty_TryGetReturnsNull()
    {
        var catalogue = new ItemCatalogue(Array.Empty<ItemsScrRecord>());

        Assert.Equal(0, catalogue.Count);
        Assert.Null(catalogue.TryGet(1u));
    }

    /// <summary>
    /// A known item is retrievable by its UID with the CONFIRMED / loader-resolved items.scr fields.
    /// The runtime catalogue surfaces only the SAMPLE-VERIFIED / loader-resolved fields — name
    /// (+0x000), item_uid (+0x034), description (+0x038), model_ref_key (+0x080), anim_ref_key
    /// (+0x084), record_discriminator (on-disk +0x0D2), effect_count (+0x220). The fixed-block numeric
    /// stat roles are UNVERIFIED and intentionally not surfaced.
    /// spec: Docs/RE/formats/items_scr.md §1.4 (field layout) / §1.6 (stat roles UNVERIFIED).
    /// </summary>
    [Fact]
    public void ItemCatalogue_TryGet_ReturnsConfirmedScrFields()
    {
        ItemsScrRecord sword = MakeScrRecord(
            itemUid: 1001u, name: "IronSword", desc: "A sturdy iron blade.",
            modelRefKey: 5000u, animRefKey: 6000u, discriminator: 1, effectCount: 1);

        ItemsScrRecord potion = MakeScrRecord(
            itemUid: 2001u, name: "HealPotion", desc: "Restores HP.",
            modelRefKey: 0u, animRefKey: 0u, discriminator: 14, effectCount: 0);

        var catalogue = new ItemCatalogue(new[] { sword, potion });

        Assert.Equal(2, catalogue.Count);

        ItemCatalogueRecord? swordRec = catalogue.TryGet(1001u);
        Assert.NotNull(swordRec);
        // item_name @0x000. spec: items_scr.md §1.4 — item_name CP949[52]: CONFIRMED.
        Assert.Equal("IronSword", swordRec.Name);
        // item_uid @0x034 — the lookup key. spec: items_scr.md §1.4 — item_uid u32: SAMPLE-VERIFIED.
        Assert.Equal(1001u, swordRec.ItemId);
        // item_desc @0x038. spec: items_scr.md §1.4 — item_desc CP949: CONFIRMED present.
        Assert.Equal("A sturdy iron blade.", swordRec.Description);
        // model_ref_key @0x080 / anim_ref_key @0x084. spec: items_scr.md §1.4 — loader-resolved.
        Assert.Equal(5000u, swordRec.ModelRefKey);
        Assert.Equal(6000u, swordRec.AnimRefKey);
        // record_discriminator on-disk +0x0D2 (loader branches != 14). spec: items_scr.md §1.4.1.
        Assert.Equal((byte)1, swordRec.RecordDiscriminator);
        // effect_count @0x220. spec: items_scr.md §1.4 — effect_count u8: CONFIRMED.
        Assert.Equal((byte)1, swordRec.EffectCount);

        ItemCatalogueRecord? potionRec = catalogue.TryGet(2001u);
        Assert.NotNull(potionRec);
        Assert.Equal("HealPotion", potionRec.Name);
        Assert.Equal((byte)14, potionRec.RecordDiscriminator);
        Assert.Equal((byte)0, potionRec.EffectCount);

        Assert.Null(catalogue.TryGet(9999u));
    }

    /// <summary>
    /// CP949-decoded Korean text is surfaced correctly from a real items.scr record, decoded by the
    /// canonical <see cref="ItemsScrParser"/> off raw VFS-shaped bytes.
    /// spec: Docs/RE/formats/items_scr.md §Identification — "Text encoding: CP949": CONFIRMED.
    /// </summary>
    [Fact]
    public void ItemCatalogue_Cp949KoreanName_DecodesCorrectly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        // "태산검" = "Taesan Sword" — a representative Korean item name.
        string koreanName = "태산검";

        // Build a single 548-byte items.scr record (effect_count = 0) with the CP949 name @0x000 and
        // the UID @0x034, then parse it through the canonical binary parser — exercising the real
        // VFS → ItemsScrParser → ItemCatalogue runtime pipeline.
        // spec: Docs/RE/formats/items_scr.md §1.2 (fixed 548-byte block); §1.4 (name @0x000, uid @0x034).
        byte[] block = new byte[0x224];
        byte[] nameBytes = cp949.GetBytes(koreanName);
        Array.Copy(nameBytes, 0, block, 0x000, nameBytes.Length); // NUL-padded inside the 52-byte window
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0x034), 99001u);
        block[0x220] = 0; // effect_count = 0

        ItemsScrRecord[] rows = ItemsScrParser.Parse(block).ToArray();
        Assert.Single(rows);

        var catalogue = new ItemCatalogue(rows);
        ItemCatalogueRecord? item = catalogue.TryGet(99001u);

        Assert.NotNull(item);
        Assert.Equal(koreanName, item.Name);
    }

    /// <summary>
    /// When two records share the same item_uid, the last record wins.
    /// spec: Docs/RE/formats/items_scr.md §1.4 (item_uid is the per-record lookup-tree key).
    /// </summary>
    [Fact]
    public void ItemCatalogue_DuplicateItemUid_LastRecordWins()
    {
        ItemsScrRecord base0 = MakeScrRecord(1u, "BaseItem", "", 0u, 0u, 0, 0);
        ItemsScrRecord variant = MakeScrRecord(1u, "EnchItem+1", "", 0u, 0u, 0, 0);

        var catalogue = new ItemCatalogue(new[] { base0, variant });

        Assert.Equal(1, catalogue.Count);
        Assert.Equal("EnchItem+1", catalogue.TryGet(1u)!.Name);
    }

    /// <summary>
    /// TOOLING-ONLY path: the items.csv constructor still builds a catalogue (for dev/export tooling),
    /// mapping only the CONFIRMED columns that exist in the runtime record (name / id / description).
    /// The CSV is NOT a runtime source — the shipping client loads items.scr.
    /// spec: Docs/RE/formats/items_csv.md §6 (authoring/dev export only, not loaded by the client).
    /// </summary>
    [Fact]
    public void ItemCatalogue_ItemsCsvConstructor_IsToolingOnly_MapsConfirmedColumns()
    {
        ItemCsvRow row = MakeItemRow(
            itemId: 4242u, name: "ToolingItem",
            subtype: 1u, sellPrice: 100u,
            enabled: 1, maxStack: 1, tierRank: 1,
            minAtk: 0u, maxAtk: 0u);

        var catalogue = new ItemCatalogue(new[] { row });

        Assert.Equal(1, catalogue.Count);
        ItemCatalogueRecord? rec = catalogue.TryGet(4242u);
        Assert.NotNull(rec);
        // col0 name / col1 id are mapped. spec: items_csv.md §1 (col0/col1): HIGH.
        Assert.Equal("ToolingItem", rec.Name);
        Assert.Equal(4242u, rec.ItemId);
        // The CSV has no loader-resolved runtime fields; the tooling path leaves them zero.
        // spec: items_csv.md §6 (CSV is a flat parallel, not the runtime record).
        Assert.Equal(0u, rec.ModelRefKey);
        Assert.Equal((byte)0, rec.RecordDiscriminator);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SkillCatalogue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A valid skill record (skillId &gt; 0 and &lt; 10,000,000) is indexed and retrievable.
    /// spec: Docs/RE/formats/config_tables.md §2.8 —
    ///   "valid record: plausible skill ID (&lt; 10,000,000)": CONFIRMED.
    /// </summary>
    [Fact]
    public void SkillCatalogue_ValidRecord_IsRetrievable()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 11u, skillSort: 7, targetMode: 0,
                baseRange: 35.0f, aoeRadius: 0.0f,
                maxHits: 1, mpFactor: 0, recast: 0, staminaCost: 0),
        ];

        var catalogue = new SkillCatalogue(entries);

        Assert.Equal(1, catalogue.Count);
        SkillDefinition? def = catalogue.TryGet(11u);
        Assert.NotNull(def);
        Assert.Equal(new SkillId(11u), def.Value.Id);
        // SkillSort (+1306) used as Category in Domain model.
        // spec: skill.md §A.2.5 "+1306 u16 SkillSort": SAMPLE-VERIFIED.
        Assert.Equal((ushort)7, def.Value.Category);
        // TargetShapeMode 0 → SingleSelfOrPrimary.
        // spec: skill.md §A.5 value 0: CONFIRMED.
        Assert.Equal(SkillTargetMode.SingleSelfOrPrimary, def.Value.TargetMode);
        Assert.Equal(35.0f, def.Value.BaseRange, precision: 4);
    }

    /// <summary>
    /// Records with skillId = 0 are filtered out (padding / empty records).
    /// spec: Docs/RE/formats/config_tables.md §2.8 — "real record … plausible skill ID": CONFIRMED.
    /// </summary>
    [Fact]
    public void SkillCatalogue_SkillIdZero_Filtered()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 0u, skillSort: 1, targetMode: 0,
                baseRange: 0f, aoeRadius: 0f,
                maxHits: 0, mpFactor: 0, recast: 0, staminaCost: 0),
        ];

        var catalogue = new SkillCatalogue(entries);

        Assert.Equal(0, catalogue.Count);
    }

    /// <summary>
    /// Records with skillId &gt;= 10,000,000 are filtered out (garbage / padding records).
    /// spec: Docs/RE/formats/config_tables.md §2.8 — "plausible skill ID (&lt; 10,000,000)": CONFIRMED.
    /// </summary>
    [Fact]
    public void SkillCatalogue_GarbageRecord_Filtered()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 10_000_000u, skillSort: 2, targetMode: 2,
                baseRange: 50f, aoeRadius: 30f,
                maxHits: 3, mpFactor: 10, recast: 600, staminaCost: 20),
        ];

        var catalogue = new SkillCatalogue(entries);

        Assert.Equal(0, catalogue.Count);
        Assert.Null(catalogue.TryGet(10_000_000u));
    }

    /// <summary>
    /// Multiple valid skills are all indexed.
    /// </summary>
    [Fact]
    public void SkillCatalogue_MultipleSkills_AllIndexed()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 11u, skillSort: 7, targetMode: 0,
                baseRange: 35f, aoeRadius: 0f,
                maxHits: 1, mpFactor: 0, recast: 0, staminaCost: 0),
            MakeSkillEntry(skillId: 100u, skillSort: 2, targetMode: 3,
                baseRange: 50f, aoeRadius: 30f,
                maxHits: 3, mpFactor: 10, recast: 600, staminaCost: 25),
        ];

        var catalogue = new SkillCatalogue(entries);

        Assert.Equal(2, catalogue.Count);
        Assert.NotNull(catalogue.TryGet(11u));
        Assert.NotNull(catalogue.TryGet(100u));
        Assert.Null(catalogue.TryGet(999u));
    }

    /// <summary>
    /// CombatRecast and StaminaCost are decoded at their confirmed offsets (+1334 and +1370).
    /// spec: Docs/RE/structs/skill.md §A.2.5 — "+1334 u16 CombatRecast": SAMPLE-VERIFIED.
    /// spec: Docs/RE/structs/skill.md §A.2.5 — "+1370 u16 StaminaCost": SAMPLE-VERIFIED.
    /// </summary>
    [Fact]
    public void SkillCatalogue_CombatSkill_HasCorrectCooldownAndStamina()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 200u, skillSort: 2, targetMode: 3,
                baseRange: 60f, aoeRadius: 40f,
                maxHits: 3, mpFactor: 10, recast: 600, staminaCost: 30),
        ];

        var catalogue = new SkillCatalogue(entries);
        SkillDefinition? def = catalogue.TryGet(200u);

        Assert.NotNull(def);
        // CombatRecast = 600 centi-seconds → CooldownMs = 60 000 ms.
        // spec: skill.md §A.2.5 — "+1334 u16 CombatRecast (×100 → ms)": SAMPLE-VERIFIED.
        Assert.Equal((ushort)600, def.Value.CooldownCentiseconds);
        Assert.Equal(60_000, def.Value.CooldownMs);
        // StaminaCost = 30.
        // spec: skill.md §A.2.5 — "+1370 u16 StaminaCost": SAMPLE-VERIFIED.
        Assert.Equal((ushort)30, def.Value.StaminaCost);
    }

    /// <summary>
    /// TargetShapeMode byte 8 (combo-chain trigger) has no enum member; falls back to
    /// SingleSelfOrPrimary as a safe default.
    /// spec: Docs/RE/structs/skill.md §A.5 —
    ///   "value 8 present in sample; behaviour UNVERIFIED — fallback documented".
    /// </summary>
    [Fact]
    public void SkillCatalogue_TargetMode8_FallsBackToSingleSelf()
    {
        SkillCatalogEntry[] entries =
        [
            MakeSkillEntry(skillId: 77u, skillSort: 2, targetMode: 8,
                baseRange: 0f, aoeRadius: 0f,
                maxHits: 1, mpFactor: 0, recast: 0, staminaCost: 0),
        ];

        var catalogue = new SkillCatalogue(entries);
        SkillDefinition? def = catalogue.TryGet(77u);

        Assert.NotNull(def);
        Assert.Equal(SkillTargetMode.SingleSelfOrPrimary, def.Value.TargetMode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MobCatalogue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A normal mob is retrievable with the expected fields.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — mob ID u16 @ +0: CONFIRMED.
    /// </summary>
    [Fact]
    public void MobCatalogue_NormalMob_IsRetrievable()
    {
        MobCatalogEntry[] entries =
        [
            MakeMobEntry(mobId: 11, mobType: 0, level: 10, spawnTimer: 60u),
        ];

        var catalogue = new MobCatalogue(entries);

        Assert.Equal(1, catalogue.Count);
        MobRecord? mob = catalogue.TryGet(11);
        Assert.NotNull(mob);
        Assert.Equal((ushort)11, mob.Id);
        Assert.Equal((byte)0, mob.Type);
        Assert.Equal(10, mob.Level);
        Assert.Equal(60u, mob.SpawnTimerSeconds);
        Assert.False(mob.IsBoss);
    }

    /// <summary>
    /// A boss mob (type=11) has IsBoss=true.
    /// spec: Docs/RE/formats/config_tables.md §2.9 —
    ///   "mob type byte = 11 → boss/elite": CONFIRMED.
    /// </summary>
    [Fact]
    public void MobCatalogue_BossMob_IsBossTrue()
    {
        MobCatalogEntry[] entries =
        [
            MakeMobEntry(mobId: 14001, mobType: 11, level: 40, spawnTimer: 40u),
        ];

        var catalogue = new MobCatalogue(entries);
        MobRecord? boss = catalogue.TryGet(14001);

        Assert.NotNull(boss);
        Assert.True(boss.IsBoss);
        // Boss level range 36..46 for IDs 14000-14009.
        // spec: §2.9 — "+244 i32 Mob level: CONFIRMED (boss validation path)".
        Assert.Equal(40, boss.Level);
    }

    /// <summary>
    /// Level = -1 (not set / sentinel) is indexed and accessible.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — "−1=not set".
    /// </summary>
    [Fact]
    public void MobCatalogue_LevelMinusOne_IsIndexed()
    {
        MobCatalogEntry[] entries =
        [
            MakeMobEntry(mobId: 9999, mobType: 0, level: -1, spawnTimer: 0u),
        ];

        var catalogue = new MobCatalogue(entries);
        MobRecord? mob = catalogue.TryGet(9999);

        Assert.NotNull(mob);
        Assert.Equal(-1, mob.Level);
    }

    /// <summary>
    /// An empty catalogue returns null for any lookup.
    /// </summary>
    [Fact]
    public void MobCatalogue_Empty_TryGetReturnsNull()
    {
        var catalogue = new MobCatalogue([]);
        Assert.Equal(0, catalogue.Count);
        Assert.Null(catalogue.TryGet(1));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VfsCatalogueLoader
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A no-archive loader (internal constructor) returns empty results for all load methods.
    /// </summary>
    [Fact]
    public void VfsCatalogueLoader_NoVfs_ReturnsEmptyResults()
    {
        using var loader = new VfsCatalogueLoader();

        Assert.Empty(loader.LoadUserLevelScr());
        Assert.Empty(loader.LoadSkillsScr());
        Assert.Empty(loader.LoadMobsScr());
        // Runtime item master (items.scr) and the tooling-only export (items.csv) both degrade empty.
        // spec: Docs/RE/formats/items_csv.md §6 (runtime source = items.scr).
        Assert.Empty(loader.LoadItemsScr());
        Assert.Empty(loader.LoadItemsCsv());
    }

    /// <summary>
    /// Invalid VFS paths degrade gracefully — constructor must not throw.
    /// </summary>
    [Fact]
    public void VfsCatalogueLoader_InvalidPaths_DegradesGracefully()
    {
        using var loader = new VfsCatalogueLoader(
            infPath: "/nonexistent/data.inf",
            vfsPath: "/nonexistent/data.vfs");

        Assert.Empty(loader.LoadUserLevelScr());
        Assert.Empty(loader.LoadItemsScr());
        Assert.Empty(loader.LoadItemsCsv());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ScrStatCatalogue + VfsCatalogueLoader integration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ScrStatCatalogue.FromLoader with an empty loader returns empty curves —
    /// preserves the all-zero EmptyStatCatalogueSource-compatible behaviour.
    /// spec: Docs/RE/formats/config_tables.md §IMPORTANT.
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_FromEmptyLoader_ReturnsEmptyCurves()
    {
        using var loader = new VfsCatalogueLoader();
        var catalogue = ScrStatCatalogue.FromLoader(loader);

        Assert.True(catalogue.GetHpBaseCurve().IsEmpty);
        Assert.True(catalogue.GetMpBaseCurve().IsEmpty);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Synthetic fixture builders
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="LevelBaseEntry"/> with the given level, divisorC and positive scale.
    /// spec: Docs/RE/formats/config_tables.md §2.4 — stride 60 bytes: CONFIRMED.
    /// </summary>
    private static LevelBaseEntry MakeLevelEntry(ushort level, ushort divisorC, float posScale)
    {
        // Synthesise the 60-byte body to match the confirmed record layout:
        // +0  u16 level        — spec: §2.4 "+0 u16 Level index: CONFIRMED"
        // +2  u16 pad          — spec: §2.4 "+2 u16 always zero: CONFIRMED"
        // +4  u16 tierStepA    — spec: §2.4 "+4 u16 Tier step counter A: CONFIRMED"
        // +6  u16 tierStepB    — spec: §2.4 "+6 u16 Tier step counter B: CONFIRMED"
        // +8  u16 divisorC     — spec: §2.4 "+8 u16 Divisor index C: CONFIRMED"
        // +10 u16 pad          — spec: §2.4 "+10 u16 always zero: CONFIRMED"
        // +12 4×f32 pos-scale  — spec: §2.4 "+12 4×f32 positive-scale group: CONFIRMED"
        // +28 4×f32 neg-scale  — spec: §2.4 "+28 4×f32 negative-scale group: CONFIRMED"
        // +44 4×f32 reserved=0 — spec: §2.4 "+44 4×f32 Reserved group (all 0.0): CONFIRMED"
        byte[] body = new byte[60];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), level);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8), divisorC);
        for (int i = 0; i < 4; i++)
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(12 + i * 4), posScale);
        for (int i = 0; i < 4; i++)
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(28 + i * 4), -posScale);

        return new LevelBaseEntry
        {
            Level = level,
            TierStepA = 0,
            TierStepB = 0,
            DivisorC = divisorC,
            StatScalePositive = [posScale, posScale, posScale, posScale],
            StatScaleNegative = [-posScale, -posScale, -posScale, -posScale],
            Body = body,
        };
    }

    /// <summary>
    /// Creates a 1504-byte <see cref="SkillCatalogEntry"/> with the given field values written
    /// at confirmed offsets within the raw record.
    /// spec: Docs/RE/structs/skill.md §A.2 fixed-block field layout: SAMPLE-VERIFIED.
    /// </summary>
    private static SkillCatalogEntry MakeSkillEntry(
        uint skillId, ushort skillSort, byte targetMode,
        float baseRange, float aoeRadius,
        short maxHits, short mpFactor, ushort recast, ushort staminaCost)
    {
        // Fixed 1504-byte record.
        // spec: Docs/RE/formats/config_tables.md §2.8 — "stride: 1504 bytes": CONFIRMED.
        byte[] rec = new byte[1504];

        // +0 u32 SkillId.       spec: skill.md §A.2.1 "+0 u32 SkillId": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0), skillId);
        // +1306 u16 SkillSort.  spec: skill.md §A.2.5 "+1306 u16 SkillSort": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1306), skillSort);
        // +1308 u8 TargetShapeMode.  spec: skill.md §A.5 "+1308 u8 TargetShapeMode": CONFIRMED.
        rec[1308] = targetMode;
        // +1312 f32 BaseRange.  spec: skill.md §A.2.5 "+1312 f32 BaseRange": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteSingleLittleEndian(rec.AsSpan(1312), baseRange);
        // +1316 f32 AoeRadius.  spec: skill.md §A.2.5 "+1316 f32 AoeRadius": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteSingleLittleEndian(rec.AsSpan(1316), aoeRadius);
        // +1330 i16 MaxTargetHits.  spec: skill.md §A.2.5 "+1330 i16 MaxTargetHits": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteInt16LittleEndian(rec.AsSpan(1330), maxHits);
        // +1332 i16 MpCostGateFactor.  spec: skill.md §A.2.5 "+1332 i16 MpCostGateFactor": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteInt16LittleEndian(rec.AsSpan(1332), mpFactor);
        // +1334 u16 CombatRecast.  spec: skill.md §A.2.5 "+1334 u16 CombatRecast": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1334), recast);
        // +1370 u16 StaminaCost.  spec: skill.md §A.2.5 "+1370 u16 StaminaCost": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1370), staminaCost);

        return new SkillCatalogEntry
        {
            RawRecord = rec,
            TrailingCount = 0,
            TrailingEntries = [],
        };
    }

    /// <summary>
    /// Creates a 488-byte <see cref="MobCatalogEntry"/> with confirmed fields at their offsets.
    /// spec: Docs/RE/formats/config_tables.md §2.9 — stride 488 bytes: CONFIRMED.
    /// </summary>
    private static MobCatalogEntry MakeMobEntry(ushort mobId, byte mobType, int level, uint spawnTimer)
    {
        byte[] raw = new byte[488];
        // +0 u16 mob ID.    spec: §2.9 — "Mob ID u16 @ +0: CONFIRMED".
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0), mobId);
        // +244 i32 level.   spec: §2.9 — "+244 i32 Mob level: CONFIRMED".
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(244), level);
        // +248 u32 timer.   spec: §2.9 — "+248 u32 Spawn timer: CONFIRMED (plausible range)".
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(248), spawnTimer);
        // +324 u8 type.     spec: §2.9 — "+324 u8 Mob type: CONFIRMED".
        raw[324] = mobType;

        return new MobCatalogEntry
        {
            Id = mobId,
            Type = mobType,
            MobLevel = level,
            SpawnTimer = spawnTimer,
            Raw = raw,
        };
    }

    /// <summary>
    /// Creates an <see cref="ItemsScrRecord"/> directly (bypasses the binary parser) for use as a
    /// runtime-catalogue fixture. Populates the CONFIRMED / loader-resolved fields the catalogue reads.
    /// spec: Docs/RE/formats/items_scr.md §1.4 fixed-block field layout.
    /// </summary>
    private static ItemsScrRecord MakeScrRecord(
        uint itemUid, string name, string desc,
        uint modelRefKey, uint animRefKey, byte discriminator, byte effectCount)
    {
        return new ItemsScrRecord
        {
            ItemName = name,
            ItemUid = itemUid,
            ItemDesc = desc,
            ModelRefKey = modelRefKey,
            AnimRefKey = animRefKey,
            Opaque0A4 = ReadOnlyMemory<byte>.Empty,
            RecordDiscriminator = discriminator,
            Opaque200 = ReadOnlyMemory<byte>.Empty,
            Opaque21C = ReadOnlyMemory<byte>.Empty,
            EffectCount = effectCount,
            Effects = Array.Empty<ItemEffectEntry>(),
            FixedBlockRaw = ReadOnlyMemory<byte>.Empty,
        };
    }

    /// <summary>
    /// Creates an <see cref="ItemCsvRow"/> directly (bypasses the CSV parser) for use as a fixture.
    /// All required fields are populated; fields unneeded for a given test default to 0/"".
    /// </summary>
    private static ItemCsvRow MakeItemRow(
        uint itemId, string name, uint subtype, uint sellPrice,
        byte enabled, ushort maxStack, ushort tierRank,
        uint minAtk, uint maxAtk)
    {
        return new ItemCsvRow
        {
            NameCp949 = name,
            ItemId = itemId,
            DescriptionCp949 = "",
            LinkedItemId = 0u,
            BaseRefId = 0u,
            SecondaryRefId = 0u,
            ItemSubtype = subtype,
            BonusFlagA = 0,
            BonusFlagB = 0,
            EnhancementSize = 0,
            SellPrice = sellPrice,
            NpcPurchaseable = 0,
            Enabled = enabled,
            MaxStack = maxStack,
            ItemTierRank = tierRank,
            MaxDurability = (ushort)(maxStack == 1 ? 300u : 1u),
            ReqStr = 0,
            ReqCon = 0,
            ReqAgi = 0,
            ReqInt = 0,
            ReqChi = 0,
            ClassYi = 1,
            ClassYe = 1,
            ClassIn = 1,
            ClassJi = 1,
            EnchantLevel = 0,
            GemPower = 0,
            BonusAtk = 0u,
            BonusHp = 0u,
            BonusExtAtk = 0u,
            AttackSpeed = 1.0f,
            DodgeRate = 0f,
            BonusChi = 0u,
            WeaponStatA = 0u,
            WeaponStatB = 0u,
            MinAttack = minAtk,
            MaxAttack = maxAtk,
            BonusDefenseA = 0u,
            PhysDefense = 0u,
            ArmorDefense = 0u,
            DurationMinutes = 0u,
            ExpireMode = 0,
            ConsumableValue = 0u,
            IsConsumable = 0,
            GemCategory = 0,
            EquippableFlag = (byte)(maxStack == 1 ? 1 : 0),
            HasEffect = 0,
            EffectType = 0,
            EffectStrength = 0,
            ModelSetId = 0,
            ModelType = 0,
            RawColumns = new string[139],
        };
    }
}