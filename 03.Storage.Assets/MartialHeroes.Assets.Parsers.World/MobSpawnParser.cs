using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class MobSpawnParser
{
    private const int RecordStride = 20;
    private const int Field0Size = 4;
    private const int RawRemainingSize = RecordStride - Field0Size;

    public static MobSpawnRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var recordCount = span.Length / RecordStride;
        if (recordCount == 0)
            return [];

        var results = new MobSpawnRecord[recordCount];

        for (var i = 0; i < recordCount; i++)
        {
            var offset = i * RecordStride;
            var field0 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, Field0Size));
            var rawRemaining = data.Slice(offset + Field0Size, RawRemainingSize);

            results[i] = new MobSpawnRecord
            {
                Field0 = field0,
                RawRemaining = rawRemaining
            };
        }

        return results;
    }
}