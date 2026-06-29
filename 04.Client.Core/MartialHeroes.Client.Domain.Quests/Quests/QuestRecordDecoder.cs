using System.Buffers.Binary;

namespace MartialHeroes.Client.Domain.Quests.Quests;

public static class QuestRecordDecoder
{
    public const int RecordStride = 0x1360;

    private const int OffQuestId = 0x000;
    private const int OffCategory = 0x002;
    private const int OffName = 0x003;
    private const int NameRegionLength = 0x040 - OffName;
    private const int OffStepCodes = 0x040;
    private const int OffOfferDialogue = 0x054;
    private const int OffTurninDialogue = 0x058;
    private const int OffCurrentStepCode = 0x065;
    private const int OffAbandonable = 0x1329;
    private const int OffPrereqChain = 0x1348;
    private const int OffMinLevel = 0x1350;
    private const int OffMaxLevel = 0x1352;
    private const int OffAcceptedFlags = 0x1354;
    private const int OffClassRaceMask = 0x1359;
    private const int OffSecondaryStatMin = 0x135A;
    private const int OffSecondaryStatMax = 0x135B;
    private const int OffTertiaryStatBound = 0x135C;

    public static bool TryDecode(ReadOnlySpan<byte> record, out QuestRecord result)
    {
        result = default;

        if (record.Length < RecordStride) return false;

        var questId = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(OffQuestId, 2));
        if (questId == 0) return false;

        var stepCodes = default(QuestRecord.StepCodeBuffer);
        record.Slice(OffStepCodes, QuestRecord.StepCodeBuffer.Length).CopyTo(stepCodes);

        var acceptedFlags = default(QuestRecord.AcceptedFlagsBuffer);
        record.Slice(OffAcceptedFlags, QuestRecord.AcceptedFlagsBuffer.Length).CopyTo(acceptedFlags);

        result = new QuestRecord(
            questId,
            record[OffCategory],
            Cp949QuestText.Decode(record.Slice(OffName, NameRegionLength)),
            stepCodes,
            record[OffCurrentStepCode],
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(OffOfferDialogue, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(OffTurninDialogue, 4)),
            record[OffAbandonable] != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(OffPrereqChain, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(OffMinLevel, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(OffMaxLevel, 2)),
            acceptedFlags,
            record[OffClassRaceMask],
            record[OffSecondaryStatMin],
            record[OffSecondaryStatMax],
            record[OffTertiaryStatBound]);

        return true;
    }
}