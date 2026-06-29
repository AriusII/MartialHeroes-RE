namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class ExpCurveEntry
{
    public required ushort Level { get; init; }

    public required ushort Const64 { get; init; }

    public required uint PrimaryExp { get; init; }

    public required uint Reserved { get; init; }

    public required uint SecondaryExp { get; init; }

    public required uint TertiaryExp { get; init; }
}

public sealed class LevelBaseEntry
{
    public required ushort Level { get; init; }


    public required ushort TierStepA { get; init; }

    public required ushort TierStepB { get; init; }

    public required ushort DivisorC { get; init; }


    public required float[] StatScalePositive { get; init; }

    public required float[] StatScaleNegative { get; init; }


    public required ReadOnlyMemory<byte> Body { get; init; }


    public ushort StepA => TierStepA;

    public ushort StepB => TierStepB;
}

public sealed class UserPointEntry
{
    public required ushort Key { get; init; }

    public required ushort Const25 { get; init; }

    public required ushort StatGroup1Gain { get; init; }


    public required uint StatGroup1Cumulative { get; init; }

    public required ushort StatGroup2Gain { get; init; }


    public required uint StatGroup2Cumulative { get; init; }

    public required ushort SecondaryCurveLow { get; init; }

    public required ushort SecondaryCurveHigh { get; init; }

    public required uint TertiaryValue1 { get; init; }

    public required uint TertiaryValue2 { get; init; }

    public required ReadOnlyMemory<byte> Body { get; init; }
}

public sealed class UsersClassBlock
{
    public required byte ClassId { get; init; }

    public required float[] StatGroupA { get; init; }

    public required float[] ClassSpecificRatios { get; init; }

    public required ReadOnlyMemory<byte> RawBlock { get; init; }
}

public sealed class UsersBlock
{
    public const int FixedSize = 496;

    public const int ClassBlockSize = 124;

    public required UsersClassBlock[] ClassBlocks { get; init; }

    public required ReadOnlyMemory<byte> RawData { get; init; }
}

public sealed class SkillCatalogEntry
{
    public required ReadOnlyMemory<byte> RawRecord { get; init; }

    public required byte TrailingCount { get; init; }

    public required ReadOnlyMemory<byte>[] TrailingEntries { get; init; }
}

public sealed class MobCatalogEntry
{
    public required ushort Id { get; init; }

    public required byte Type { get; init; }

    public required int MobLevel { get; init; }

    public required uint SpawnTimer { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class TextCommandRecord
{
    public required uint CommandId { get; init; }

    public required string CommandName { get; init; }

    public required byte ArgumentFlag { get; init; }

    public required uint SubCommandId { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class EmoticonRecord
{
    public required uint EmoteId { get; init; }

    public required byte CategoryFlag { get; init; }

    public required uint SecondaryKey { get; init; }

    public required uint ActionLink { get; init; }

    public required int DstX { get; init; }

    public required int DstY { get; init; }

    public required int GlyphSrcX { get; init; }

    public required int GlyphSrcY { get; init; }

    public required int LabelSrcX { get; init; }

    public required int LabelSrcY { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class MsgInfoRecord
{
    public required uint MessageId { get; init; }

    public required uint DialogFlag { get; init; }

    public required string TextLine1 { get; init; }

    public required string TextLine2 { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class ItemsExtraRecord
{
    public required uint ItemId { get; init; }

    public required bool IsSentinel { get; init; }

    public required float AnimScale { get; init; }

    public required int AttachFieldA { get; init; }

    public required int AttachFieldB { get; init; }

    public required int AttachX { get; init; }

    public required int AttachY { get; init; }

    public required int AttachZ { get; init; }

    public required int RotXDeg { get; init; }

    public required int RotYDeg { get; init; }

    public required int RotZDeg { get; init; }

    public required int Field40 { get; init; }

    public required uint RarityTier { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class ItemCsvRow
{
    public required string NameCp949 { get; init; }

    public required uint ItemId { get; init; }

    public required string DescriptionCp949 { get; init; }

    public required uint BaseItemId { get; init; }

    public required uint SecondaryTypeId { get; init; }

    public required uint Col6Flag { get; init; }

    public required string[] RawColumns { get; init; }
}

public sealed class NpcScrRecord
{
    public required uint Id { get; init; }

    public required byte Kind { get; init; }

    public required short Job { get; init; }

    public required string[] NameSlots { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}

public sealed class QuestScrRecord
{
    public required ushort QuestId { get; init; }

    public required byte Category { get; init; }

    public required string QuestName { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}