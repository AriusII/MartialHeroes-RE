using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class DoStanceParser
{
    private const int OffInstanceKey = 0x00;

    private const int OffGroupSubIndex = 0x04;

    private const int OffSlotIndex = 0x08;

    private const int OffClassStanceRef = 0x0C;

    private const int OffGroupId = 0x10;

    private const int OffSecondaryXVariant = 0x14;

    private const int OffIconSrcX = 0x18;

    private const int OffIconSrcY = 0x1C;

    private const int OffSecondarySpriteX = 0x20;

    private const int OffSecondarySpriteY = 0x24;

    private const int OffTail = 0x28;


    public static DoStanceTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static DoStanceTable Parse(ReadOnlySpan<byte> span)
    {
        var stride = DoStanceRecord.Stride;
        var totalCount = span.Length / stride;
        var trailingBytes = span.Length % stride;

        var records = new List<DoStanceRecord>(totalCount);

        for (var i = 0; i < totalCount; i++)
        {
            var recordBase = i * stride;
            var rec = span.Slice(recordBase, stride);

            if (IsAllZero(rec))
                continue;

            var instanceKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var groupSubIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffGroupSubIndex..]);

            var slotIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotIndex..]);

            var classStanceRef = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffClassStanceRef..]);

            var groupId = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffGroupId..]);

            var secondaryXVariant = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondaryXVariant..]);

            var iconSrcX = BinaryPrimitives.ReadInt16LittleEndian(rec[OffIconSrcX..]);

            var iconSrcY = BinaryPrimitives.ReadInt16LittleEndian(rec[OffIconSrcY..]);

            var secondarySpriteX = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondarySpriteX..]);

            var secondarySpriteY = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffSecondarySpriteY..]);

            var tail = new DoStanceTail72();
            rec.Slice(OffTail, DoStanceRecord.TailByteCount).CopyTo(tail.AsSpan());

            records.Add(new DoStanceRecord
            {
                InstanceKey = instanceKey,
                GroupSubIndex = groupSubIndex,
                SlotIndex = slotIndex,
                ClassStanceRef = classStanceRef,
                GroupId = groupId,
                SecondaryXVariant = secondaryXVariant,
                IconSrcX = iconSrcX,
                IconSrcY = iconSrcY,
                SecondarySpriteX = secondarySpriteX,
                SecondarySpriteY = secondarySpriteY,
                Tail = tail
            });
        }

        return new DoStanceTable(records, totalCount, trailingBytes);
    }


    private static bool IsAllZero(ReadOnlySpan<byte> rec)
    {
        return rec.IndexOfAnyExcept((byte)0) < 0;
    }
}