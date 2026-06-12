using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Infrastructure.Catalog;

/// <summary>
/// In-memory lookup catalogue for skill definitions parsed from <c>data/script/skills.scr</c>.
/// Maps skill IDs to <see cref="SkillDefinition"/> Domain objects.
/// </summary>
/// <remarks>
/// <para>
/// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — "stride: 1504 bytes, ~194 real records":
///   CONFIRMED.
/// spec: Docs/RE/structs/skill.md Part A.2 — fixed-block field layout 1504 bytes.
/// </para>
/// <para>
/// <b>Valid record filter.</b> Not all records in the file contain real skill data; many are
/// zero-padded or garbage. A record is valid iff:
///   skill_id &gt; 0 and skill_id &lt; 10,000,000.
/// spec: Docs/RE/formats/config_tables.md §2.8 — "A real record is distinguished from garbage by
///   a plausible skill ID (&lt; 10,000,000) and a plausible category index (&lt; 300)": CONFIRMED.
/// </para>
/// <para>
/// <b>UNVERIFIED fields.</b> Only the confirmed offsets from the spec are decoded. All other byte
/// regions are discarded. The full list of open questions is in spec §2.8 (skills.scr known unknowns).
/// </para>
/// </remarks>
public sealed class SkillCatalogue
{
    // ── Confirmed field offsets within the 1504-byte fixed block ────────────
    // spec: Docs/RE/structs/skill.md Part A.2.1 / A.2.5.

    // +0 u32 SkillId. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.1 — "+0 u32 SkillId: SAMPLE-VERIFIED".
    private const int SkillIdOffset = 0;

    // +1306 u16 SkillSort. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1306 u16 SkillSort: SAMPLE-VERIFIED".
    private const int SkillSortOffset = 1306;

    // +1308 u8 TargetShapeMode. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.5 / A.5 — "+1308 u8 TargetShapeMode: CONFIRMED".
    private const int TargetShapeModeOffset = 1308;

    // +1312 f32 BaseRange. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1312 f32 BaseRange: SAMPLE-VERIFIED".
    private const int BaseRangeOffset = 1312;

    // +1316 f32 AoeRadius. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1316 f32 AoeRadius: SAMPLE-VERIFIED".
    private const int AoeRadiusOffset = 1316;

    // +1330 i16 MaxTargetHits. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1330 i16 MaxTargetHits (engine clamps to 40): SAMPLE-VERIFIED".
    private const int MaxTargetHitsOffset = 1330;

    // +1332 i16 MpCostGateFactor. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1332 i16 MpCostGateFactor: SAMPLE-VERIFIED".
    private const int MpCostGateFactorOffset = 1332;

    // +1334 u16 CombatRecast. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1334 u16 CombatRecast (centi-seconds): SAMPLE-VERIFIED".
    private const int CombatRecastOffset = 1334;

    // +1344 u16 WeaponReqA. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1344 u16 WeaponReqIdA: CONFIRMED".
    private const int WeaponReqAOffset = 1344;

    // +1348 u32 WeaponReqB. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1348 u32 WeaponReqIdB: CONFIRMED".
    private const int WeaponReqBOffset = 1348;

    // +1352 u8 WeaponReqActiveFlag. CONFIRMED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1352 u8 WeaponReqActiveFlag: CONFIRMED".
    private const int WeaponReqActiveFlagOffset = 1352;

    // +1368 i16 CastCost. CONFIRMED (structure); value 0 in all sample records; semantic UNVERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1368 i16 CastCost: CONFIRMED (structure); semantic UNVERIFIED".
    private const int CastCostOffset = 1368;

    // +1370 u16 StaminaCost. SAMPLE-VERIFIED.
    // spec: Docs/RE/structs/skill.md §A.2.5 — "+1370 u16 StaminaCost: SAMPLE-VERIFIED".
    private const int StaminaCostOffset = 1370;

    private readonly Dictionary<uint, SkillDefinition> _byId;

    /// <summary>
    /// Constructs the catalogue from pre-parsed skill catalogue entries.
    /// Records with invalid skill IDs are silently skipped.
    /// spec: Docs/RE/formats/config_tables.md §2.8 — valid record filter: plausible id &lt; 10,000,000.
    /// </summary>
    public SkillCatalogue(SkillCatalogEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _byId = new Dictionary<uint, SkillDefinition>(entries.Length);

        foreach (SkillCatalogEntry entry in entries)
        {
            ReadOnlySpan<byte> rec = entry.RawRecord.Span;

            if (rec.Length < SkillScrMinRecordSize)
                continue;

            // +0 u32 SkillId. CONFIRMED.
            // spec: Docs/RE/structs/skill.md §A.2.1 — "+0 u32 SkillId: SAMPLE-VERIFIED".
            uint skillId = BinaryPrimitives.ReadUInt32LittleEndian(rec[SkillIdOffset..]);

            // Valid-record filter: skip id=0 and id >= 10,000,000 (garbage / padding records).
            // spec: Docs/RE/formats/config_tables.md §2.8 — "A real record is distinguished from
            //   garbage by a plausible skill ID (< 10,000,000)": CONFIRMED.
            if (skillId == 0 || skillId >= 10_000_000u)
                continue;

            // +1306 u16 SkillSort (used as Category in Domain model). SAMPLE-VERIFIED.
            // spec: Docs/RE/structs/skill.md §A.2.5 — "+1306 u16 SkillSort: SAMPLE-VERIFIED".
            ushort skillSort = BinaryPrimitives.ReadUInt16LittleEndian(rec[SkillSortOffset..]);

            // +1308 u8 TargetShapeMode. CONFIRMED.
            // spec: Docs/RE/structs/skill.md §A.2.5 — "+1308 u8 TargetShapeMode: CONFIRMED".
            byte targetShapeByte = rec[TargetShapeModeOffset];
            SkillTargetMode targetMode = MapTargetMode(targetShapeByte);

            // +1312 f32 BaseRange. SAMPLE-VERIFIED.
            float baseRange = BinaryPrimitives.ReadSingleLittleEndian(rec[BaseRangeOffset..]);

            // +1316 f32 AoeRadius. SAMPLE-VERIFIED.
            float aoeRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[AoeRadiusOffset..]);

            // +1330 i16 MaxTargetHits (engine clamps to 40). SAMPLE-VERIFIED.
            short maxTargetHits = BinaryPrimitives.ReadInt16LittleEndian(rec[MaxTargetHitsOffset..]);

            // +1332 i16 MpCostGateFactor. SAMPLE-VERIFIED.
            short mpCostFactor = BinaryPrimitives.ReadInt16LittleEndian(rec[MpCostGateFactorOffset..]);

            // +1334 u16 CombatRecast (centi-seconds). SAMPLE-VERIFIED.
            ushort combatRecast = BinaryPrimitives.ReadUInt16LittleEndian(rec[CombatRecastOffset..]);

            // +1344 u16 WeaponReqA. CONFIRMED.
            ushort weaponReqA = BinaryPrimitives.ReadUInt16LittleEndian(rec[WeaponReqAOffset..]);

            // +1348 u32 WeaponReqB. CONFIRMED.
            uint weaponReqB = BinaryPrimitives.ReadUInt32LittleEndian(rec[WeaponReqBOffset..]);

            // +1352 u8 WeaponReqActiveFlag. CONFIRMED.
            bool weaponReqActive = rec[WeaponReqActiveFlagOffset] != 0;

            // +1368 i16 CastCost. CONFIRMED (structure); semantic UNVERIFIED; 0 in all sample records.
            short castCost = BinaryPrimitives.ReadInt16LittleEndian(rec[CastCostOffset..]);

            // +1370 u16 StaminaCost. SAMPLE-VERIFIED.
            ushort staminaCost = BinaryPrimitives.ReadUInt16LittleEndian(rec[StaminaCostOffset..]);

            var def = new SkillDefinition
            {
                Id = new SkillId(skillId),
                Category = skillSort,
                TargetMode = targetMode,
                BaseRange = baseRange,
                AoeRadius = aoeRadius,
                MaxTargets = maxTargetHits,
                MpCostFactor = mpCostFactor,
                CooldownCentiseconds = combatRecast,
                WeaponReqA = weaponReqA,
                WeaponReqB = weaponReqB,
                WeaponReqActive = weaponReqActive,
                ConsumedCost = castCost,
                StaminaCost = staminaCost,
            };

            _byId[skillId] = def;
        }
    }

    // Minimum record size to safely read all confirmed fields (up to StaminaCost at +1370+2=+1372).
    private const int SkillScrMinRecordSize = 1372;

    /// <summary>
    /// Creates a <see cref="SkillCatalogue"/> by loading <c>skills.scr</c> from the given loader.
    /// </summary>
    public static SkillCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new SkillCatalogue(loader.LoadSkillsScr());
    }

    /// <summary>Number of valid skills in this catalogue.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Looks up a skill by its ID.
    /// Returns <see langword="null"/> when the ID is not present.
    /// spec: Docs/RE/structs/skill.md §A.2.1 — SkillId is the catalog key.
    /// </summary>
    public SkillDefinition? TryGet(uint skillId) =>
        _byId.TryGetValue(skillId, out var d) ? d : null;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps the raw TargetShapeMode byte to the Domain enum.
    /// spec: Docs/RE/structs/skill.md §A.5 TargetShapeMode.
    /// Values 0..11 are confirmed; anything else maps to <see cref="SkillTargetMode.SingleSelfOrPrimary"/>.
    /// </summary>
    private static SkillTargetMode MapTargetMode(byte raw) =>
        raw switch
        {
            // 0 = Self / single (movement skills use this). spec: §A.5 value 0: CONFIRMED.
            0 => SkillTargetMode.SingleSelfOrPrimary,
            // 1 = Single target with faction gate. spec: §A.5 value 1: CONFIRMED.
            1 => SkillTargetMode.SingleTarget,
            // 2 = Single enemy / heal. spec: §A.5 value 2: CONFIRMED.
            2 => SkillTargetMode.SingleEnemyOrHeal,
            // 3 = Chain / nearby AoE. spec: §A.5 value 3: CONFIRMED.
            3 => SkillTargetMode.ChainNearbyAoe,
            // 4 = Cone / forward line AoE. spec: §A.5 value 4: CONFIRMED.
            4 => SkillTargetMode.ConeForwardAoe,
            // 5 = Ground / point only. spec: §A.5 value 5: CONFIRMED.
            5 => SkillTargetMode.GroundPoint,
            // 6 = Party AoE. spec: §A.5 value 6: CONFIRMED.
            6 => SkillTargetMode.PartyAoe,
            // 7 = Faction/group-gated single. spec: §A.5 value 7: CONFIRMED.
            7 => SkillTargetMode.FactionGatedSingle,
            // 8 = Combo-chain trigger. spec: §A.5 value 8 (present in sample, behaviour UNVERIFIED).
            // Map to SingleSelfOrPrimary as a safe fallback until the mode is confirmed.
            8 => SkillTargetMode.SingleSelfOrPrimary,
            // 9 = PK-gated single. spec: §A.5 value 9: CONFIRMED.
            9 => SkillTargetMode.PkGatedSingle,
            // 10 = Radial AoE (both factions). spec: §A.5 value 10: CONFIRMED.
            10 => SkillTargetMode.RadialAoeBothFactions,
            // 11 = Self-only. spec: §A.5 value 11: CONFIRMED.
            11 => SkillTargetMode.SelfOnly,
            // Unknown / future value — default to Self.
            _ => SkillTargetMode.SingleSelfOrPrimary,
        };
}