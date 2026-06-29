using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ItemScaleParser
{
    private const int RecordStride = 8;

    public static ItemScaleRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ItemScaleRecord[] Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"itemscale.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride}.");

        var count = span.Length / RecordStride;
        var results = new ItemScaleRecord[count];

        for (var i = 0; i < count; i++)
        {
            var rec = span.Slice(i * RecordStride, RecordStride);

            results[i] = new ItemScaleRecord
            {
                ItemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]),
                Scale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..])
            };
        }

        return results;
    }
}