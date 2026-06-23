using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class NpcSpawnParser
{
    private const int RecordStride = 28;

    public static NpcSpawnArray Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static NpcSpawnArray Parse(ReadOnlySpan<byte> span)
    {
        var recordCount = span.Length / RecordStride;
        var records = new NpcSpawnRecord[recordCount];

        for (var i = 0; i < recordCount; i++)
        {
            var offset = i * RecordStride;
            var rec = span.Slice(offset, RecordStride);

            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            var field02Inert = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            var worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            var worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            var facing = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            var spawnType = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            var field20Inert = BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]);

            var field24Inert = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            records[i] = new NpcSpawnRecord
            {
                MobId = mobId,
                Field02Inert = field02Inert,
                WorldX = worldX,
                WorldZ = worldZ,
                Facing = facing,
                SpawnType = spawnType,
                Field20Inert = field20Inert,
                Field24Inert = field24Inert
            };
        }

        return new NpcSpawnArray { Records = records };
    }
}