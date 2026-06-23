using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class SkillCatalogue
{
    private const int SkillIdOffset = 0;
    private const int SkillSortOffset = 1306;
    private const int TargetShapeModeOffset = 1308;
    private const int BaseRangeOffset = 1312;
    private const int AoeRadiusOffset = 1316;
    private const int MaxTargetHitsOffset = 1330;
    private const int MpCostGateFactorOffset = 1332;
    private const int CombatRecastOffset = 1334;
    private const int WeaponReqAOffset = 1344;
    private const int WeaponReqBOffset = 1348;
    private const int WeaponReqActiveFlagOffset = 1352;
    private const int CastCostOffset = 1368;
    private const int StaminaCostOffset = 1370;
    private const int SkillScrMinRecordSize = 1372;
    private readonly Dictionary<uint, SkillDefinition> _byId;

    public SkillCatalogue(SkillCatalogEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _byId = new Dictionary<uint, SkillDefinition>(entries.Length);

        foreach (var entry in entries)
        {
            var rec = entry.RawRecord.Span;

            if (rec.Length < SkillScrMinRecordSize)
                continue;

            var skillId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            if (skillId == 0 || skillId >= 10_000_000u)
                continue;

            var skillSort = BinaryPrimitives.ReadUInt16LittleEndian(rec[SkillSortOffset..]);

            var targetShapeByte = rec[TargetShapeModeOffset];
            var targetMode = MapTargetMode(targetShapeByte);

            var baseRange = BinaryPrimitives.ReadSingleLittleEndian(rec[BaseRangeOffset..]);

            var aoeRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[AoeRadiusOffset..]);

            var maxTargetHits = BinaryPrimitives.ReadInt16LittleEndian(rec[MaxTargetHitsOffset..]);

            var mpCostFactor = BinaryPrimitives.ReadInt16LittleEndian(rec[MpCostGateFactorOffset..]);

            var combatRecast = BinaryPrimitives.ReadUInt16LittleEndian(rec[CombatRecastOffset..]);

            var weaponReqA = BinaryPrimitives.ReadUInt16LittleEndian(rec[WeaponReqAOffset..]);

            var weaponReqB = BinaryPrimitives.ReadUInt32LittleEndian(rec[WeaponReqBOffset..]);

            var weaponReqActive = rec[WeaponReqActiveFlagOffset] != 0;

            var castCost = BinaryPrimitives.ReadInt16LittleEndian(rec[CastCostOffset..]);

            var staminaCost = BinaryPrimitives.ReadUInt16LittleEndian(rec[StaminaCostOffset..]);

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
                StaminaCost = staminaCost
            };

            _byId[skillId] = def;
        }
    }

    public int Count => _byId.Count;

    public static SkillCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new SkillCatalogue(loader.LoadSkillsScr());
    }


    private static SkillTargetMode MapTargetMode(byte raw)
    {
        return raw switch
        {
            0 => SkillTargetMode.SingleSelfOrPrimary,
            1 => SkillTargetMode.SingleTarget,
            2 => SkillTargetMode.SingleEnemyOrHeal,
            3 => SkillTargetMode.ChainNearbyAoe,
            4 => SkillTargetMode.ConeForwardAoe,
            5 => SkillTargetMode.GroundPoint,
            6 => SkillTargetMode.PartyAoe,
            7 => SkillTargetMode.FactionGatedSingle,
            8 => SkillTargetMode.SingleSelfOrPrimary,
            9 => SkillTargetMode.PkGatedSingle,
            10 => SkillTargetMode.RadialAoeBothFactions,
            11 => SkillTargetMode.SelfOnly,
            _ => SkillTargetMode.SingleSelfOrPrimary
        };
    }
}