using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class MobSpawnParser
{
    private const int RecordStride = 20;

    public static MobSpawnRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MobSpawnRecord[] Parse(ReadOnlySpan<byte> span)
    {
        var recordCount = span.Length / RecordStride;
        if (recordCount == 0)
            return [];

        var results = new MobSpawnRecord[recordCount];

        for (var i = 0; i < recordCount; i++)
        {
            var offset = i * RecordStride;
            var rec = span.Slice(offset, RecordStride);

            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            var pad = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            var worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            var worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            var fieldC = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            var field10 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);

            results[i] = new MobSpawnRecord
            {
                MobId = mobId,
                Pad = pad,
                WorldX = worldX,
                WorldZ = worldZ,
                FieldC = fieldC,
                Field10 = field10
            };
        }

        return results;
    }
}