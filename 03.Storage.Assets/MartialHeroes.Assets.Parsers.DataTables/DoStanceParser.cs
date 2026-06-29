using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class DoStanceParser
{
    private const int OffInstanceKey = 0x00;
    private const int OffStanceFilter = 0x04;
    private const int OffSlotIndex = 0x08;
    private const int OffClassStanceRef = 0x0C;
    private const int OffWidgetPosX = 0x10;
    private const int OffWidgetPosYRaw = 0x14;
    private const int OffIconSrcX = 0x18;
    private const int OffIconSrcY = 0x1C;
    private const int OffLevelBarSrcX = 0x20;
    private const int OffLevelBarSrcY = 0x24;

    private const int OffHasOverlay0 = 0x28;
    private const int OffHasOverlay1 = 0x29;
    private const int OffHasOverlay2 = 0x2A;

    private const int OffOverlay0Dx = 0x2C;
    private const int OffOverlay1Dx = 0x30;
    private const int OffOverlay2Dx = 0x34;
    private const int OffOverlay0Dy = 0x38;
    private const int OffOverlay1Dy = 0x3C;
    private const int OffOverlay2Dy = 0x40;
    private const int OffOverlay0SrcX = 0x44;
    private const int OffOverlay0SrcY = 0x48;
    private const int OffOverlay1SrcX = 0x4C;
    private const int OffOverlay1SrcY = 0x50;
    private const int OffOverlay2SrcX = 0x54;
    private const int OffOverlay2SrcY = 0x58;
    private const int OffOverlay0W = 0x5C;
    private const int OffOverlay0H = 0x60;
    private const int OffOverlay1W = 0x64;
    private const int OffOverlay1H = 0x68;
    private const int OffOverlay2W = 0x6C;
    private const int OffOverlay2H = 0x70;


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

            records.Add(new DoStanceRecord
            {
                InstanceKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]),
                StanceFilter = rec[OffStanceFilter],
                SlotIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotIndex..]),
                ClassStanceRef = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffClassStanceRef..]),
                WidgetPosX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffWidgetPosX..]),
                WidgetPosYRaw = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffWidgetPosYRaw..]),
                IconSrcX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffIconSrcX..]),
                IconSrcY = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffIconSrcY..]),
                LevelBarSrcX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffLevelBarSrcX..]),
                LevelBarSrcY = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffLevelBarSrcY..]),
                Overlay0 = new DoStanceOverlay
                {
                    Present = rec[OffHasOverlay0] != 0,
                    Dx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0Dx..]),
                    Dy = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0Dy..]),
                    SrcX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0SrcX..]),
                    SrcY = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0SrcY..]),
                    Width = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0W..]),
                    Height = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay0H..])
                },
                Overlay1 = new DoStanceOverlay
                {
                    Present = rec[OffHasOverlay1] != 0,
                    Dx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1Dx..]),
                    Dy = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1Dy..]),
                    SrcX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1SrcX..]),
                    SrcY = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1SrcY..]),
                    Width = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1W..]),
                    Height = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay1H..])
                },
                Overlay2 = new DoStanceOverlay
                {
                    Present = rec[OffHasOverlay2] != 0,
                    Dx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2Dx..]),
                    Dy = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2Dy..]),
                    SrcX = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2SrcX..]),
                    SrcY = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2SrcY..]),
                    Width = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2W..]),
                    Height = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffOverlay2H..])
                }
            });
        }

        return new DoStanceTable(records, totalCount, trailingBytes);
    }


    private static bool IsAllZero(ReadOnlySpan<byte> rec)
    {
        return rec.IndexOfAnyExcept((byte)0) < 0;
    }
}