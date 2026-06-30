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

        if (record.MinLevel != 0 && record.MinLevel > character.Level) return QuestGateResult.LevelTooLow;

        if (record.MaxLevel != 0 && record.MaxLevel < character.Level) return QuestGateResult.LevelTooHigh;

        if (record.AcceptedFlag(character.ClassIndex) == 0) return QuestGateResult.NotAcceptedForClass;

        if (record.StanceJobGate != 0 && record.StanceJobGate != character.PlayerStance) return QuestGateResult.WrongStance;

        if (record.SecondaryStatMin != 0 && record.SecondaryStatMin > character.SecondaryStat) return QuestGateResult.SecondaryStatTooLow;

        if (record.SecondaryStatMax != 0 && record.SecondaryStatMax < character.SecondaryStat) return QuestGateResult.SecondaryStatTooHigh;

        if (record.TertiaryStatBound != 0 && record.TertiaryStatBound > character.TertiaryStat) return QuestGateResult.TertiaryStatFailed;

        if (activeCategories.IndexOf(record.Category) >= 0) return QuestGateResult.SameCategoryActive;

        if (activeQuestIds.IndexOf(record.QuestId) >= 0) return QuestGateResult.SameIdActive;

        if (record.PrereqChainId != 0 && record.PrereqChainId <= character.ChapterProgress) return QuestGateResult.InChain;

        return QuestGateResult.Available;
    }
}