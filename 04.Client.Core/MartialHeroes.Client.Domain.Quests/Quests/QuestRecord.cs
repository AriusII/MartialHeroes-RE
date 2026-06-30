using System.Runtime.CompilerServices;

namespace MartialHeroes.Client.Domain.Quests.Quests;

public readonly struct QuestRecord
{
    public readonly ushort QuestId;

    public readonly byte Category;

    public readonly string Name;

    public readonly StepCodeBuffer StepCodes;

    public readonly byte CurrentStepCode;

    public readonly uint OfferDialogueHandle;

    public readonly uint TurninDialogueHandle;

    public readonly bool Abandonable;

    public readonly uint PrereqChainId;

    public readonly ushort MinLevel;

    public readonly ushort MaxLevel;

    public readonly AcceptedFlagsBuffer AcceptedFlags;

    public readonly byte StanceJobGate;

    public readonly byte SecondaryStatMin;

    public readonly byte SecondaryStatMax;

    public readonly byte TertiaryStatBound;

    public QuestRecord(
        ushort questId,
        byte category,
        string name,
        StepCodeBuffer stepCodes,
        byte currentStepCode,
        uint offerDialogueHandle,
        uint turninDialogueHandle,
        bool abandonable,
        uint prereqChainId,
        ushort minLevel,
        ushort maxLevel,
        AcceptedFlagsBuffer acceptedFlags,
        byte stanceJobGate,
        byte secondaryStatMin,
        byte secondaryStatMax,
        byte tertiaryStatBound)
    {
        QuestId = questId;
        Category = category;
        Name = name;
        StepCodes = stepCodes;
        CurrentStepCode = currentStepCode;
        OfferDialogueHandle = offerDialogueHandle;
        TurninDialogueHandle = turninDialogueHandle;
        Abandonable = abandonable;
        PrereqChainId = prereqChainId;
        MinLevel = minLevel;
        MaxLevel = maxLevel;
        AcceptedFlags = acceptedFlags;
        StanceJobGate = stanceJobGate;
        SecondaryStatMin = secondaryStatMin;
        SecondaryStatMax = secondaryStatMax;
        TertiaryStatBound = tertiaryStatBound;
    }

    public bool IsEmpty => QuestId == 0;

    public byte AcceptedFlag(int index)
    {
        return (uint)index < AcceptedFlagsBuffer.Length ? AcceptedFlags[index] : (byte)0;
    }

    [InlineArray(6)]
    public struct StepCodeBuffer
    {
        public const int Length = 6;

        private byte _element0;
    }

    [InlineArray(5)]
    public struct AcceptedFlagsBuffer
    {
        public const int Length = 5;

        private byte _element0;
    }
}