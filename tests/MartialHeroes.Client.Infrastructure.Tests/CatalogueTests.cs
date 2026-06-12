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
    /// When DivisorC = 0 the divide-by-zero guard fires and the curve value is 0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 —
    ///   "divisor C = 0 (phases 1 and 5): grid lookup skipped via a divide-by-zero guard": CONFIRMED.
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_DivisorCZero_YieldsZeroCurveEntry()
    {
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 1, divisorC: 0, posScale: 1.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries);
        var hp = catalogue.GetHpBaseCurve();

        Assert.Equal(0L, hp.BaseForLevel(1));
    }

    /// <summary>
    /// When DivisorC = 2 the formula (10/2)×3×posScale×100 = 1500 for posScale=1.0.
    /// spec: Docs/RE/formats/config_tables.md §2.4 —
    ///   "when C=2 formula yields 15.0 (using B=3.0)": CONFIRMED.
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_DivisorCTwo_YieldsExpectedValue()
    {
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 12, divisorC: 2, posScale: 1.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries);
        var hp = catalogue.GetHpBaseCurve();

        // (10/2)×3 × 1.0 × 100 = 1500
        Assert.Equal(1500L, hp.BaseForLevel(1));
    }

    /// <summary>
    /// Curve grows monotonically across tier transitions: L1(0) &lt; L12(1500) &lt; L36(2250).
    /// spec: Docs/RE/formats/config_tables.md §2.4 "Transition summary" table: CONFIRMED.
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_CurvesGrowMonotonically_AcrossTierTransitions()
    {
        // L1: divisorC=0 (phases 1/5 guard) → curve value 0
        // L12: divisorC=2, posScale=1.0     → (10/2)×3×1.0×100 = 1500
        // L36: divisorC=4, posScale=3.0     → (10/4)×3×3.0×100 = 2250
        // spec: §2.4 — "L1..L11: divisorC=0; L12..L23: divisorC=2; L36..L144: divisorC=4": CONFIRMED.
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 1,  divisorC: 0, posScale: 1.0f),
            MakeLevelEntry(level: 12, divisorC: 2, posScale: 1.0f),
            MakeLevelEntry(level: 36, divisorC: 4, posScale: 3.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries);
        var hp = catalogue.GetHpBaseCurve();

        long l1  = hp.BaseForLevel(1);
        long l12 = hp.BaseForLevel(2);
        long l36 = hp.BaseForLevel(3);

        Assert.True(l12 > l1,  $"L12 ({l12}) should be > L1 ({l1})");
        Assert.True(l36 > l12, $"L36 ({l36}) should be > L12 ({l12})");
    }

    /// <summary>
    /// HP and MP curves are both produced (same formula applied to both).
    /// spec: Docs/RE/formats/config_tables.md §2.4 —
    ///   "Named-stat mapping for each of the four float positions: UNVERIFIED;
    ///    both curves use group[0] until mapping is confirmed".
    /// </summary>
    [Fact]
    public void ScrStatCatalogue_HpAndMpCurves_BothNonEmpty()
    {
        LevelBaseEntry[] entries =
        [
            MakeLevelEntry(level: 12, divisorC: 2, posScale: 1.0f),
        ];

        var catalogue = new ScrStatCatalogue(entries);

        Assert.False(catalogue.GetHpBaseCurve().IsEmpty);
        Assert.False(catalogue.GetMpBaseCurve().IsEmpty);
        Assert.Equal(catalogue.GetHpBaseCurve().BaseForLevel(1),
                     catalogue.GetMpBaseCurve().BaseForLevel(1));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ItemCatalogue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An empty catalogue has count 0 and TryGet returns null for any id.
    /// </summary>
    [Fact]
    public void ItemCatalogue_Empty_TryGetReturnsNull()
    {
        var catalogue = new ItemCatalogue([]);

        Assert.Equal(0, catalogue.Count);
        Assert.Null(catalogue.TryGet(1u));
    }

    /// <summary>
    /// A known item is retrievable by ID with all confirmed fields set correctly.
    /// spec: Docs/RE/formats/config_tables.md §4.3 col0–col22: CONFIRMED.
    /// </summary>
    [Fact]
    public void ItemCatalogue_TryGet_ReturnsCorrectRecord()
    {
        ItemCsvRow sword = MakeItemRow(
            itemId: 1001u, name: "IronSword",
            subtype: 1u, sellPrice: 1000u,
            enabled: 1, maxStack: 1, tierRank: 144,
            minAtk: 100u, maxAtk: 200u);

        ItemCsvRow potion = MakeItemRow(
            itemId: 2001u, name: "HealPotion",
            subtype: 1001u, sellPrice: 50u,
            enabled: 1, maxStack: 20, tierRank: 1,
            minAtk: 0u, maxAtk: 0u);

        var catalogue = new ItemCatalogue([sword, potion]);

        Assert.Equal(2, catalogue.Count);

        ItemCatalogueRecord? swordRec = catalogue.TryGet(1001u);
        Assert.NotNull(swordRec);
        Assert.Equal("IronSword", swordRec.Name);
        // col1 item_id. spec: §4.3 — "col1 item_id uint32: CONFIRMED".
        Assert.Equal(1001u, swordRec.ItemId);
        // col6 item_subtype. spec: §4.3 — "col6 item_subtype: CONFIRMED".
        Assert.Equal(1u, swordRec.ItemSubtype);
        // col16 sell_price. spec: §4.3 — "col16 sell_price: CONFIRMED".
        Assert.Equal(1000u, swordRec.SellPrice);
        // col18 enabled. spec: §4.3 — "col18 enabled uint8: CONFIRMED".
        Assert.True(swordRec.Enabled);
        // col22 item_tier_rank. spec: §4.3 — "col22 item_tier_rank: CONFIRMED".
        Assert.Equal((ushort)144, swordRec.ItemTierRank);
        // col87 min_attack. spec: §4.3 — "col87 min_attack: CONFIRMED".
        Assert.Equal(100u, swordRec.MinAttack);
        // col90 max_attack. spec: §4.3 — "col90 max_attack: CONFIRMED".
        Assert.Equal(200u, swordRec.MaxAttack);

        ItemCatalogueRecord? potionRec = catalogue.TryGet(2001u);
        Assert.NotNull(potionRec);
        Assert.Equal("HealPotion", potionRec.Name);
        // col19 max_stack. spec: §4.3 — "col19 max_stack: CONFIRMED".
        Assert.Equal((ushort)20, potionRec.MaxStack);

        Assert.Null(catalogue.TryGet(9999u));
    }

    /// <summary>
    /// CP949-decoded Korean text is surfaced correctly by the item catalogue.
    /// The test encodes a known Korean name as CP949 bytes, feeds them through ItemsCsvParser,
    /// and verifies the decoded string matches.
    /// spec: Docs/RE/formats/config_tables.md §4.1 — "Encoding: CP949/EUC-KR (no BOM)": CONFIRMED.
    /// spec: Docs/RE/formats/config_tables.md §4.3 — "col0 name_cp949 string: CONFIRMED".
    /// </summary>
    [Fact]
    public void ItemCatalogue_Cp949KoreanName_DecodesCorrectly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        // "태산검" = "Taesan Sword" — a representative Korean item name.
        string koreanName = "태산검";
        string csvLine = BuildMinimalCsvLine(col0: koreanName, itemId: 99001u);

        // Encode as CP949 bytes then parse to simulate the real VFS → parser pipeline.
        // spec: §4.1 — "Encoding: CP949/EUC-KR (no BOM)": CONFIRMED.
        byte[] cp949Bytes = cp949.GetBytes(csvLine);
        ItemCsvRow[] rows = ItemsCsvParser.Parse(cp949Bytes.AsSpan());
        Assert.Single(rows);

        var catalogue = new ItemCatalogue(rows);
        ItemCatalogueRecord? item = catalogue.TryGet(99001u);

        Assert.NotNull(item);
        Assert.Equal(koreanName, item.Name);
    }

    /// <summary>
    /// When two rows share the same ItemId, the last row wins (enchant-variant dedup rule).
    /// </summary>
    [Fact]
    public void ItemCatalogue_DuplicateItemId_LastRowWins()
    {
        ItemCsvRow base0 = MakeItemRow(1u, "BaseItem",   1u, 100u, 1, 1, 10, 0u, 0u);
        ItemCsvRow ench1 = MakeItemRow(1u, "EnchItem+1", 1u, 200u, 1, 1, 10, 0u, 0u);

        var catalogue = new ItemCatalogue([base0, ench1]);

        // Only one entry (deduped), last occurrence wins.
        Assert.Equal(1, catalogue.Count);
        Assert.Equal("EnchItem+1", catalogue.TryGet(1u)!.Name);
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
            MakeSkillEntry(skillId: 11u,  skillSort: 7, targetMode: 0,
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
            Level             = level,
            TierStepA         = 0,
            TierStepB         = 0,
            DivisorC          = divisorC,
            StatScalePositive = [posScale, posScale, posScale, posScale],
            StatScaleNegative = [-posScale, -posScale, -posScale, -posScale],
            Body              = body,
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
            RawRecord       = rec,
            TrailingCount   = 0,
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
            Id         = mobId,
            Type       = mobType,
            MobLevel   = level,
            SpawnTimer = spawnTimer,
            Raw        = raw,
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
            NameCp949         = name,
            ItemId            = itemId,
            DescriptionCp949  = "",
            LinkedItemId      = 0u,
            BaseRefId         = 0u,
            SecondaryRefId    = 0u,
            ItemSubtype       = subtype,
            BonusFlagA        = 0,
            BonusFlagB        = 0,
            EnhancementSize   = 0,
            SellPrice         = sellPrice,
            NpcPurchaseable   = 0,
            Enabled           = enabled,
            MaxStack          = maxStack,
            ItemTierRank      = tierRank,
            MaxDurability     = (ushort)(maxStack == 1 ? 300u : 1u),
            ReqStr            = 0,
            ReqCon            = 0,
            ReqAgi            = 0,
            ReqInt            = 0,
            ReqChi            = 0,
            ClassYi           = 1,
            ClassYe           = 1,
            ClassIn           = 1,
            ClassJi           = 1,
            EnchantLevel      = 0,
            GemPower          = 0,
            BonusAtk          = 0u,
            BonusHp           = 0u,
            BonusExtAtk       = 0u,
            AttackSpeed       = 1.0f,
            DodgeRate         = 0f,
            BonusChi          = 0u,
            WeaponStatA       = 0u,
            WeaponStatB       = 0u,
            MinAttack         = minAtk,
            MaxAttack         = maxAtk,
            BonusDefenseA     = 0u,
            PhysDefense       = 0u,
            ArmorDefense      = 0u,
            DurationMinutes   = 0u,
            ExpireMode        = 0,
            ConsumableValue   = 0u,
            IsConsumable      = 0,
            GemCategory       = 0,
            EquippableFlag    = (byte)(maxStack == 1 ? 1 : 0),
            HasEffect         = 0,
            EffectType        = 0,
            EffectStrength    = 0,
            ModelSetId        = 0,
            ModelType         = 0,
            RawColumns        = new string[139],
        };
    }

    /// <summary>
    /// Builds a 139-column CSV line with the given col0 (name) and col1 (itemId).
    /// All other columns default to "0".
    /// spec: Docs/RE/formats/config_tables.md §4.1 — "Columns per row: 139": CONFIRMED.
    /// </summary>
    private static string BuildMinimalCsvLine(string col0, uint itemId)
    {
        var cols = new string[139];
        Array.Fill(cols, "0");
        cols[0]  = col0;
        cols[1]  = itemId.ToString();
        cols[2]  = "";   // description (empty)
        cols[18] = "1";  // enabled
        cols[19] = "1";  // max_stack
        cols[22] = "1";  // item_tier_rank
        cols[23] = "1";  // max_durability
        cols[29] = "1";  // class_yi
        cols[30] = "1";  // class_ye
        cols[31] = "1";  // class_in
        cols[32] = "1";  // class_ji
        return string.Join(",", cols);
    }
}
