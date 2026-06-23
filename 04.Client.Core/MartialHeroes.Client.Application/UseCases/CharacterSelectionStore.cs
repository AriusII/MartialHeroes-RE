using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Application.UseCases;

public sealed class CharacterSelectionStore
{
    public enum SelectOutcome
    {
        Invalid,
        Blank,
        Confirmed
    }

    public const int MaxSlots = 5;

    public const int MaxSlotIndex = MaxSlots - 1;

    public const string BlankSlotSentinel = "@BLANK@";

    private readonly CharacterSlotRecord?[] _slots = new CharacterSlotRecord?[MaxSlots];

    public CharacterSlotRecord? Chosen { get; private set; }

    public void Reset()
    {
        Array.Clear(_slots);
        Chosen = null;
    }

    public void Retain(in CharacterSlotRecord record)
    {
        if ((uint)record.SlotIndex >=
            MaxSlots) return;

        _slots[record.SlotIndex] = record;
    }

    public CharacterSlotRecord? Get(int slotIndex)
    {
        return (uint)slotIndex < MaxSlots ? _slots[slotIndex] : null;
    }

    public IReadOnlyList<CharacterSlotRecord?> Snapshot()
    {
        return (CharacterSlotRecord?[])_slots.Clone();
    }

    public ImmutableArray<CharacterListSlot> ProjectRetainedRoster()
    {
        var builder = ImmutableArray.CreateBuilder<CharacterListSlot>();

        for (var slot = 0; slot < MaxSlots; slot++)
        {
            var record = _slots[slot];
            if (record is null) continue;

            builder.Add(new CharacterListSlot(
                record.SlotIndex, record.Name, record.Level, record.ServerClass, record.CurrentHp,
                record.WorldX, record.WorldZ,
                record.InternalClass, record.AppearanceVariant, record.FaceA, record.EquipGids,
                record.SlotFlag, record.BillingFlags));
        }

        return builder.ToImmutable();
    }

    public SelectOutcome Confirm(int slotIndex)
    {
        if ((uint)slotIndex >= MaxSlots)
            return SelectOutcome.Invalid;

        var record = _slots[slotIndex];
        if (record is null) return SelectOutcome.Invalid;

        if (string.Equals(record.Name, BlankSlotSentinel,
                StringComparison.Ordinal))
            return SelectOutcome.Blank;

        Chosen = record;
        return SelectOutcome.Confirmed;
    }
}

public sealed class CharacterSlotRecord
{
    public CharacterSlotRecord(
        int slotIndex, ReadOnlySpan<byte> rawDescriptorAndStats, byte slotFlag, uint billingFlags = 0)
    {
        SlotIndex = slotIndex;
        SlotFlag = slotFlag;
        BillingFlags = billingFlags;
        RawDescriptor = rawDescriptorAndStats.ToArray();

        var reader = new SpawnDescriptorReader(rawDescriptorAndStats[..SpawnDescriptorReader.Size]);
        Name = reader.ReadName();
        Level = reader.ReadLevel();
        ServerClass = reader.ReadServerClass();
        CurrentHp = reader.ReadCurrentHpClamped();
        WorldX = reader.ReadWorldX();
        WorldZ = reader.ReadWorldZ();

        InternalClass = reader.ReadInternalClass();
        AppearanceVariant = reader.ReadAppearanceVariant();
        FaceA = reader.ReadFaceA();
        Span<uint> gearScratch = stackalloc uint[SpawnDescriptorReader.VisibleGearSlots.Length];
        reader.ReadVisibleGearGids(gearScratch);
        EquipGids = [..gearScratch];
    }

    public int SlotIndex { get; }

    public string Name { get; }

    public ushort Level { get; }

    public ushort ServerClass { get; }

    public uint CurrentHp { get; }

    public float WorldX { get; }

    public float WorldZ { get; }

    public ushort InternalClass { get; }

    public byte AppearanceVariant { get; }

    public ushort FaceA { get; }

    public ImmutableArray<uint> EquipGids { get; }

    public ReadOnlyMemory<byte> RawDescriptor { get; }

    public byte SlotFlag { get; }

    public uint BillingFlags { get; }
}