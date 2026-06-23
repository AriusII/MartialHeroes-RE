
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EquipRefEntry
{
    public const int WireSize = 16;

    public readonly uint PartGid;

    public readonly EquipEntryTailBuffer Tail;

    [InlineArray(12)]
    public struct EquipEntryTailBuffer
    {
        private byte _element0;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CharacterListSlotRecord
{

    public const int DescriptorSize = 880;

    public const int StatBlockSize = 96;

    public const int SlotFlagSize = 1;

    public const int FlagsWordSize = 4;

    public const int WireSize = DescriptorSize + StatBlockSize + SlotFlagSize + FlagsWordSize;


    public const int DescriptorOffset = 0;

    public const int StatBlockOffset = DescriptorSize;

    public const int SlotFlagOffset = StatBlockOffset + StatBlockSize;

    public const int FlagsWordOffset = SlotFlagOffset + SlotFlagSize;


    private const int SdName = 0x00;
    private const int SdNameMaxBytes = 17;
    private const int SdAppearanceVariant = 0x2C;
    private const int SdInternalClass = 0x34;
    private const int SdStateByte = 0x38;
    private const int SdSubLevelByte = 0x39;
    private const int SdLevel = 0x3A;

    private const int
        SdCurrentHp =
            0x3C;

    private const int
        SdVitalB = 0x44;

    private const int SdWorldX = 0x4C;
    private const int SdWorldZ = 0x50;
    private const int SdEquipRefTable = 0x58;
    private const int SdServerClass = 0x74;
    private const int EquipRefEntryCount = 20;


    private const int SbPrimary0 = 0x00;
    private const int SbPrimary1 = 0x04;
    private const int SbPrimary2 = 0x08;
    private const int SbPrimary3 = 0x0C;
    private const int SbPrimary4 = 0x10;
    private const int SbVitalCurrent = 0x14;
    private const int SbRankXp = 0x18;
    private const int SbWithinRankXp = 0x20;
    private const int SbRemainingStatPoints = 0x30;

    public readonly RecordBuffer Raw;

    [InlineArray(WireSize)]
    public struct RecordBuffer
    {
        private byte _element0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> NameField()
    {
        return MemoryMarshal.CreateReadOnlySpan(in Unsafe.As<RecordBuffer, byte>(ref Unsafe.AsRef(in Raw)), WireSize)
            .Slice(SdName, SdNameMaxBytes);
    }

    public readonly byte AppearanceVariant => ReadU8(SdAppearanceVariant);

    public readonly ushort InternalClass => ReadU16(SdInternalClass);

    public readonly byte StateByte => ReadU8(SdStateByte);

    public readonly byte SubLevelByte => ReadU8(SdSubLevelByte);

    public readonly ushort LevelRaw => ReadU16(SdLevel);

    public readonly long CurrentHp => ReadI64(SdCurrentHp);

    public readonly uint VitalB => ReadU32(SdVitalB);

    public readonly float WorldX => ReadF32(SdWorldX);

    public readonly float WorldZ => ReadF32(SdWorldZ);

    public readonly ushort ServerClass => ReadU16(SdServerClass);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref readonly EquipRefEntry EquipEntry(int index)
    {
        if ((uint)index >= EquipRefEntryCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Equip entry index out of range (0..19).");

        var slot =
            AsSpan().Slice(SdEquipRefTable + index * EquipRefEntry.WireSize, EquipRefEntry.WireSize);
        return ref MemoryMarshal.AsRef<EquipRefEntry>(slot);
    }

    public static int EquipEntryCount => EquipRefEntryCount;


    public readonly uint PrimaryStat0 => ReadU32(StatBlockOffset + SbPrimary0);

    public readonly uint PrimaryStat1 => ReadU32(StatBlockOffset + SbPrimary1);

    public readonly uint PrimaryStat2 => ReadU32(StatBlockOffset + SbPrimary2);

    public readonly uint PrimaryStat3 => ReadU32(StatBlockOffset + SbPrimary3);

    public readonly uint PrimaryStat4 => ReadU32(StatBlockOffset + SbPrimary4);

    public readonly uint VitalCurrent => ReadU32(StatBlockOffset + SbVitalCurrent);

    public readonly long RankXp => ReadI64(StatBlockOffset + SbRankXp);

    public readonly uint WithinRankXp => ReadU32(StatBlockOffset + SbWithinRankXp);

    public readonly uint RemainingStatPoints => ReadU32(StatBlockOffset + SbRemainingStatPoints);



    public readonly byte SlotFlag => ReadU8(SlotFlagOffset);

    public readonly uint FlagsWord => ReadU32(FlagsWordOffset);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> AsSpan()
    {
        return MemoryMarshal.CreateReadOnlySpan(in Unsafe.As<RecordBuffer, byte>(ref Unsafe.AsRef(in Raw)), WireSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly byte ReadU8(int offset)
    {
        return AsSpan()[offset];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ushort ReadU16(int offset)
    {
        return BinaryPrimitivesLe.ReadUInt16(AsSpan().Slice(offset, sizeof(ushort)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly uint ReadU32(int offset)
    {
        return BinaryPrimitivesLe.ReadUInt32(AsSpan().Slice(offset, sizeof(uint)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly long ReadI64(int offset)
    {
        return BinaryPrimitivesLe.ReadInt64(AsSpan().Slice(offset, sizeof(long)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly float ReadF32(int offset)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitivesLe.ReadInt32(AsSpan().Slice(offset, sizeof(int))));
    }
}

internal static class BinaryPrimitivesLe
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadUInt16(ReadOnlySpan<byte> s)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ReadUInt32(ReadOnlySpan<byte> s)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadInt32(ReadOnlySpan<byte> s)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long ReadInt64(ReadOnlySpan<byte> s)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(s);
    }
}

public static class CharacterAppearance
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ModelClassId(int internalClass, int appearanceVariant)
    {
        return 5 * (internalClass + 4 * appearanceVariant) - 24;
    }
}

public readonly ref struct SmsgCharacterListReader
{
    public const int SlotCount = 5;

    private readonly ReadOnlySpan<byte> _records;

    public readonly SmsgCharacterListHeader Header;

    public SmsgCharacterListReader(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < SmsgCharacterListHeader.HeaderSize)
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length,
                $"A 3/1 character-list frame requires at least {SmsgCharacterListHeader.HeaderSize} header bytes.");

        Header = MemoryMarshal.Read<SmsgCharacterListHeader>(frame);

        var tail = frame[SmsgCharacterListHeader.HeaderSize..];
        var setBits = BitOperations.PopCount((uint)(Header.SlotMask & ((1 << SlotCount) - 1)));
        var available = tail.Length / CharacterListSlotRecord.WireSize;
        var n = setBits <= available ? setBits : available;
        _records = tail[..(n * CharacterListSlotRecord.WireSize)];
    }

    public readonly byte SlotMask => Header.SlotMask;

    public readonly int PopulatedCount => _records.Length / CharacterListSlotRecord.WireSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsSlotPopulated(int slot)
    {
        return (uint)slot < SlotCount && (Header.SlotMask & (1 << slot)) != 0;
    }

    public readonly ref readonly CharacterListSlotRecord this[int slot]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsSlotPopulated(slot))
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot is out of range or not populated.");

            var below = BitOperations.PopCount((uint)(Header.SlotMask & ((1 << slot) - 1)));
            if (below >= PopulatedCount)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Record bytes for this slot are missing.");

            var rec = _records.Slice(below * CharacterListSlotRecord.WireSize,
                CharacterListSlotRecord.WireSize);
            return ref MemoryMarshal.AsRef<CharacterListSlotRecord>(rec);
        }
    }
}