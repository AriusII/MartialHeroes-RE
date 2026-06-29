using System.Buffers.Binary;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ItemEffectParser
{
    private const int EntryStride = 4;

    public static uint[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static uint[] Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length % EntryStride != 0)
            throw new InvalidDataException(
                $"itemeffect.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {EntryStride}.");

        var count = span.Length / EntryStride;
        var results = new uint[count];

        for (var i = 0; i < count; i++)
            results[i] = BinaryPrimitives.ReadUInt32LittleEndian(span[(i * EntryStride)..]);

        return results;
    }
}