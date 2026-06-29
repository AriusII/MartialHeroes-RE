namespace MartialHeroes.Client.Domain.Quests.Quests;

public static class QuestEligibility
{
    public static QuestGateResult Evaluate(
        in QuestRecord record,
        in QuestCharacterContext character,
        ReadOnlySpan<byte> activeCategories,
        ReadOnlySpan<ushort> activeQuestIds)
    {
        if (record.IsEmpty) return QuestGateResult.RecordNotFound;

        if (character.Level < record.MinLevel) return QuestGateResult.LevelTooLow;

        if (character.Level > record.MaxLevel) return QuestGateResult.LevelTooHigh;

        if (record.AcceptedFlag(character.AcceptedSlotIndex) == 0) return QuestGateResult.NotAcceptedForSlot;

        if ((record.ClassRaceMask & character.ClassRaceMask) == 0) return QuestGateResult.WrongClassRace;

        if (character.SecondaryStat < record.SecondaryStatMin) return QuestGateResult.SecondaryStatTooLow;

        if (character.SecondaryStat > record.SecondaryStatMax) return QuestGateResult.SecondaryStatTooHigh;

        if (character.TertiaryStat < record.TertiaryStatBound) return QuestGateResult.TertiaryStatFailed;

        if (activeCategories.IndexOf(record.Category) >= 0) return QuestGateResult.SameCategoryActive;

        if (activeQuestIds.IndexOf(record.QuestId) >= 0) return QuestGateResult.SameIdActive;

        if (!character.PrerequisiteChainSatisfied) return QuestGateResult.PrerequisiteNotMet;

        return QuestGateResult.Available;
    }
}