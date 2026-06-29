using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class SkillCatalogue
{
    private const int GlobalCategoryOffset = 4;
    private const int TierByteOffset = 520;
    private const int PrerequisiteOffset = 1280;
    private const int SkillSortOffset = 1306;
    private const int TargetShapeModeOffset = 1308;
    private const int BaseRangeOffset = 1312;
    private const int AoeRadiusOffset = 1316;
    private const int MaxTargetHitsOffset = 1330;
    private const int CastCadenceFactorOffset = 1332;
    private const int CombatRecastOffset = 1334;
    private const int WeaponReqAOffset = 1344;
    private const int WeaponReqBOffset = 1348;
    private const int WeaponReqActiveFlagOffset = 1352;
    private const int HpCostOffset = 1368;
    private const int StaminaCostOffset = 1370;
    private const int CastEffectIdOffset = 1136;
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

            var globalCategory = BinaryPrimitives.ReadUInt32LittleEndian(rec[GlobalCategoryOffset..]);

            var tierByte = rec[TierByteOffset];

            var prerequisite0 = BinaryPrimitives.ReadUInt32LittleEndian(rec[PrerequisiteOffset..]);
            var prerequisite1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[(PrerequisiteOffset + 4)..]);
            var prerequisite2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[(PrerequisiteOffset + 8)..]);

            var skillSort = BinaryPrimitives.ReadUInt16LittleEndian(rec[SkillSortOffset..]);

            var targetShapeByte = rec[TargetShapeModeOffset];
            var targetMode = MapTargetMode(targetShapeByte);

            var baseRange = BinaryPrimitives.ReadSingleLittleEndian(rec[BaseRangeOffset..]);

            var aoeRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[AoeRadiusOffset..]);

            var maxTargetHits = BinaryPrimitives.ReadInt16LittleEndian(rec[MaxTargetHitsOffset..]);

            var castCadenceFactor = BinaryPrimitives.ReadInt16LittleEndian(rec[CastCadenceFactorOffset..]);

            var combatRecast = BinaryPrimitives.ReadUInt16LittleEndian(rec[CombatRecastOffset..]);

            var weaponReqA = BinaryPrimitives.ReadUInt16LittleEndian(rec[WeaponReqAOffset..]);

            var weaponReqB = BinaryPrimitives.ReadUInt32LittleEndian(rec[WeaponReqBOffset..]);

            var weaponReqActive = rec[WeaponReqActiveFlagOffset] != 0;

            var hpCost = BinaryPrimitives.ReadInt16LittleEndian(rec[HpCostOffset..]);

            var staminaCost = BinaryPrimitives.ReadUInt16LittleEndian(rec[StaminaCostOffset..]);

            var castEffectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[CastEffectIdOffset..]);

            var def = new SkillDefinition
            {
                Id = new SkillId(skillId),
                Category = skillSort,
                TargetMode = targetMode,
                GlobalCategory = globalCategory,
                TierByte = tierByte,
                Prerequisite0 = prerequisite0,
                Prerequisite1 = prerequisite1,
                Prerequisite2 = prerequisite2,
                BaseRange = baseRange,
                AoeRadius = aoeRadius,
                MaxTargets = maxTargetHits,
                CastCadenceFactor = castCadenceFactor,
                CooldownCentiseconds = combatRecast,
                WeaponReqA = weaponReqA,
                WeaponReqB = weaponReqB,
                WeaponReqActive = weaponReqActive,
                HpCost = hpCost,
                StaminaCost = staminaCost,
                CastEffectId = castEffectId
            };

            _byId[skillId] = def;
        }
    }

    public int Count => _byId.Count;

    public bool TryGet(SkillId id, out SkillDefinition definition)
    {
        return _byId.TryGetValue(id.Value, out definition);
    }

    public bool TryGetCastEffectId(SkillId id, out uint castEffectId)
    {
        if (_byId.TryGetValue(id.Value, out var def))
        {
            castEffectId = def.CastEffectId;
            return true;
        }

        castEffectId = 0u;
        return false;
    }

    public uint GetCastEffectId(SkillId id)
    {
        return _byId.TryGetValue(id.Value, out var def) ? def.CastEffectId : 0u;
    }

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