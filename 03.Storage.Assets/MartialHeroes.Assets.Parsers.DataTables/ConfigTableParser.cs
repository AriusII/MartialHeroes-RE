using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ConfigTableParser
{

    private const int ExpScrStride = 20;


    private const int UserLevelScrStride = 60;


    private const int UserPointScrStride = 32;



    private const int SkillScrMainStride = 1504;

    private const int SkillScrTrailingCountOffset = 1500;
    private const int SkillScrTrailingStride = 8;


    private const int MobScrStride = 488;

    private const int MobIdOffset = 0;
    private const int MobTypeOffset = 324;
    private const int MobLevelOffset = 244;
    private const int SpawnTimerOffset = 248;

    public static ExpCurveEntry[] ParseExpScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % ExpScrStride != 0)
            throw new InvalidDataException(
                $"exp.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {ExpScrStride}. spec: Docs/RE/formats/config_tables.md §2.3.");

        var count = span.Length / ExpScrStride;
        var results = new ExpCurveEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * ExpScrStride;
            var rec = span.Slice(recOffset, ExpScrStride);

            var level = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            var const64 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            var primaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var reserved = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            var secondaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            var tertiaryExp = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            results[i] = new ExpCurveEntry
            {
                Level = level,
                Const64 = const64,
                PrimaryExp = primaryExp,
                Reserved = reserved,
                SecondaryExp = secondaryExp,
                TertiaryExp = tertiaryExp
            };
        }

        return results;
    }

    public static LevelBaseEntry[] ParseUserLevelScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % UserLevelScrStride != 0)
            throw new InvalidDataException(
                $"userlevel.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserLevelScrStride}. spec: Docs/RE/formats/config_tables.md §2.4.");

        var count = span.Length / UserLevelScrStride;
        var results = new LevelBaseEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * UserLevelScrStride;
            var rec = span.Slice(recOffset, UserLevelScrStride);

            var level = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);


            var tierStepA = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);
            var tierStepB = BinaryPrimitives.ReadUInt16LittleEndian(rec[6..]);
            var divisorC = BinaryPrimitives.ReadUInt16LittleEndian(rec[8..]);

            var statScalePositive = new float[4];
            for (var s = 0; s < 4; s++)
                statScalePositive[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(12 + s * 4)..]);

            var statScaleNegative = new float[4];
            for (var s = 0; s < 4; s++)
                statScaleNegative[s] = BinaryPrimitives.ReadSingleLittleEndian(rec[(28 + s * 4)..]);

            var body = data.Slice(recOffset, UserLevelScrStride);

            results[i] = new LevelBaseEntry
            {
                Level = level,
                TierStepA = tierStepA,
                TierStepB = tierStepB,
                DivisorC = divisorC,
                StatScalePositive = statScalePositive,
                StatScaleNegative = statScaleNegative,
                Body = body
            };
        }

        return results;
    }

    public static UserPointEntry[] ParseUserPointScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % UserPointScrStride != 0)
            throw new InvalidDataException(
                $"userpoint.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {UserPointScrStride}. spec: Docs/RE/formats/config_tables.md §2.5.");

        var count = span.Length / UserPointScrStride;
        var results = new UserPointEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * UserPointScrStride;
            var rec = span.Slice(recOffset, UserPointScrStride);

            var key = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            var const25 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            var statGroup1Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);


            var statGroup1Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            var statGroup2Gain = BinaryPrimitives.ReadUInt16LittleEndian(rec[12..]);


            var statGroup2Cumul = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            var secCurveLow = BinaryPrimitives.ReadUInt16LittleEndian(rec[20..]);

            var secCurveHigh = BinaryPrimitives.ReadUInt16LittleEndian(rec[22..]);

            var tertiary1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            var tertiary2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[28..]);

            results[i] = new UserPointEntry
            {
                Key = key,
                Const25 = const25,
                StatGroup1Gain = statGroup1Gain,
                StatGroup1Cumulative = statGroup1Cumul,
                StatGroup2Gain = statGroup2Gain,
                StatGroup2Cumulative = statGroup2Cumul,
                SecondaryCurveLow = secCurveLow,
                SecondaryCurveHigh = secCurveHigh,
                TertiaryValue1 = tertiary1,
                TertiaryValue2 = tertiary2,
                Body = data.Slice(recOffset, UserPointScrStride)
            };
        }

        return results;
    }


    public static UsersBlock ParseUsersScr(ReadOnlyMemory<byte> data)
    {
        if (data.Length != UsersBlock.FixedSize)
            throw new InvalidDataException(
                $"users.scr parse error: expected exactly {UsersBlock.FixedSize} bytes " +
                $"(single structure, no stride, no record loop), " +
                $"got {data.Length}. spec: Docs/RE/formats/config_tables.md §2.6.");

        var span = data.Span;

        var classBlocks = new UsersClassBlock[4];
        for (var b = 0; b < 4; b++)
        {
            var windowBase = b * UsersBlock.ClassBlockSize;
            var blk = span.Slice(windowBase, UsersBlock.ClassBlockSize);

            var classId = blk[0];

            var statGroupA = new float[3];
            for (var s = 0; s < 3; s++)
                statGroupA[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(4 + s * 4)..]);

            var classRatios = new float[8];
            for (var s = 0; s < 8; s++)
                classRatios[s] = BinaryPrimitives.ReadSingleLittleEndian(blk[(92 + s * 4)..]);

            classBlocks[b] = new UsersClassBlock
            {
                ClassId = classId,
                StatGroupA = statGroupA,
                ClassSpecificRatios = classRatios,
                RawBlock = data.Slice(windowBase, UsersBlock.ClassBlockSize)
            };
        }

        return new UsersBlock { ClassBlocks = classBlocks, RawData = data };
    }

    public static SkillCatalogEntry[] ParseSkillsScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length == 0)
            return [];

        if (span.Length < SkillScrMainStride)
            throw new InvalidDataException(
                $"skills.scr parse error: buffer too small for one record " +
                $"(need {SkillScrMainStride} bytes, got {span.Length}).");

        var results = new List<SkillCatalogEntry>();
        var offset = 0;

        while (offset < span.Length)
        {
            if (offset + SkillScrMainStride > span.Length)
                throw new InvalidDataException(
                    $"skills.scr parse error: main record truncated at offset {offset}.");

            var rawRecord = data.Slice(offset, SkillScrMainStride);

            var trailingCount = span[offset + SkillScrTrailingCountOffset];

            offset += SkillScrMainStride;

            var trailingEntries = new ReadOnlyMemory<byte>[trailingCount];
            for (var ti = 0; ti < trailingCount; ti++)
            {
                if (offset + SkillScrTrailingStride > span.Length)
                    throw new InvalidDataException(
                        $"skills.scr parse error: trailing entry [{ti}] truncated at offset {offset}.");

                trailingEntries[ti] = data.Slice(offset, SkillScrTrailingStride);
                offset += SkillScrTrailingStride;
            }

            results.Add(new SkillCatalogEntry
            {
                RawRecord = rawRecord,
                TrailingCount = trailingCount,
                TrailingEntries = trailingEntries
            });
        }

        return results.ToArray();
    }

    public static MobCatalogEntry[] ParseMobsScr(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % MobScrStride != 0)
            throw new InvalidDataException(
                $"mobs.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {MobScrStride}. spec: Docs/RE/formats/config_tables.md §2.9.");

        var count = span.Length / MobScrStride;
        var results = new MobCatalogEntry[count];

        for (var i = 0; i < count; i++)
        {
            var recOffset = i * MobScrStride;

            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(recOffset + MobIdOffset, 2));

            var mobType = span[recOffset + MobTypeOffset];

            var mobLevel = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(recOffset + MobLevelOffset, 4));

            var spawnTimer = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(recOffset + SpawnTimerOffset, 4));

            var raw = data.Slice(recOffset, MobScrStride);

            results[i] = new MobCatalogEntry
            {
                Id = mobId,
                Type = mobType,
                MobLevel = mobLevel,
                SpawnTimer = spawnTimer,
                Raw = raw
            };
        }

        return results;
    }
}