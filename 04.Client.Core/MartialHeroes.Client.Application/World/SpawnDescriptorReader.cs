using System.Buffers.Binary;
using MartialHeroes.Client.Application.Contracts.Hud;

namespace MartialHeroes.Client.Application.World;

public readonly ref struct SpawnDescriptorReader
{
    private const int OffName = 0x00;
    private const int NameMaxBytes = 17;

    private const int
        OffAppearanceVariant = 0x2C;

    private const int
        OffFaceA = 0x2E;

    private const int
        OffInternalClass = 0x34;

    private const int
        OffEquipTable = 0x58;

    private const int EquipEntryStride = 16;
    private const int EquipEntryCount = 20;

    private const int OffLevel = 0x3A;

    private const int OffCurrentHp = 0x3C;

    private const int
        OffVitalB = 0x44;

    private const int OffWorldX = 0x4C;
    private const int OffWorldZ = 0x50;
    private const int OffServerClass = 0x74;

    public const int Size = 880;

    private readonly ReadOnlySpan<byte> _data;

    public SpawnDescriptorReader(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < Size)
            throw new ArgumentOutOfRangeException(
                nameof(descriptor), descriptor.Length,
                $"SpawnDescriptor requires at least {Size} bytes.");

        _data = descriptor;
    }

    public string ReadName()
    {
        return Cp949Text.Decode(_data.Slice(OffName, NameMaxBytes));
    }

    public ushort ReadLevel()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffLevel, 2));
    }

    public long ReadCurrentHp()
    {
        return BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(OffCurrentHp, 8));
    }

    public uint ReadCurrentHpClamped()
    {
        var hp = ReadCurrentHp();
        if (hp <= 0) return 0u;
        return hp >= uint.MaxValue ? uint.MaxValue : (uint)hp;
    }

    public uint ReadVitalB()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(OffVitalB, 4));
    }

    public float ReadWorldX()
    {
        return BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldX, 4));
    }

    public float ReadWorldZ()
    {
        return BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(OffWorldZ, 4));
    }

    public ushort ReadServerClass()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffServerClass, 2));
    }

    public ushort ReadInternalClass()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffInternalClass, 2));
    }

    public byte ReadAppearanceVariant()
    {
        return _data[OffAppearanceVariant];
    }

    public ushort ReadFaceA()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(OffFaceA, 2));
    }

    public uint ReadEquipPartGid(int entryIndex)
    {
        if ((uint)entryIndex >= EquipEntryCount)
            throw new ArgumentOutOfRangeException(
                nameof(entryIndex), entryIndex,
                $"Equip-table entry index must be in [0, {EquipEntryCount}).");

        var off = OffEquipTable + entryIndex * EquipEntryStride;
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(off, 4));
    }

    public static ReadOnlySpan<int> VisibleGearSlots => [3, 4, 6, 2, 11, 14];

    public int ReadVisibleGearGids(Span<uint> destination)
    {
        var slots = VisibleGearSlots;
        if (destination.Length < slots.Length)
            throw new ArgumentException(
                $"Destination must hold at least {slots.Length} gids.", nameof(destination));

        for (var i = 0; i < slots.Length; i++)
            destination[i] = ReadEquipPartGid(slots[i]);

        return slots.Length;
    }
}