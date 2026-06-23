using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;


public static class MobInfoPanelParser
{
    private const int HeaderSize = 4;

    private const int RecordStride = MiPanelData.RecordStride;

    private const int
        OffWidgetId =
            0x00;

    private const int
        OffFieldA0 =
            0x04;

    private const int
        OffFieldA1 =
            0x08;

    private const int
        OffFieldKind =
            0x0C;

    private const int
        OffFieldB0 =
            0x10;

    private const int
        OffFieldB1 =
            0x14;

    private const int
        OffFieldLink =
            0x18;

    public static MiPanelData Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MiPanelData Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length < HeaderSize)
            throw new InvalidDataException(
                $".mi parse error: buffer length {span.Length} is too short for the 4-byte header. " +
                "spec: Docs/RE/formats/mi.md §Header layout.");

        var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        var expectedLength = HeaderSize + (long)recordCount * RecordStride;
        if (span.Length != expectedLength)
            throw new InvalidDataException(
                $".mi parse error: buffer length {span.Length} does not match expected " +
                $"4 + {recordCount} x 28 = {expectedLength} bytes. " +
                "spec: Docs/RE/formats/mi.md §Container structure.");

        var records = new MiWidgetRecord[(int)recordCount];
        for (var i = 0; i < (int)recordCount; i++)
        {
            var recBase = HeaderSize + i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            records[i] = new MiWidgetRecord
            {
                WidgetId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]),

                FieldA0 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldA0..]),

                FieldA1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldA1..]),

                FieldKind = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldKind..]),

                FieldB0 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldB0..]),

                FieldB1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldB1..]),

                FieldLink = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldLink..])
            };
        }

        return new MiPanelData
        {
            RecordCount = recordCount,
            Records = records
        };
    }
}