using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class GameVerParser
{
    private const int MinFileSize = 28;

    private const int MinFieldCount = 7;

    public static GameVerData Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < MinFileSize || span.Length % 4 != 0)
            throw new InvalidDataException(
                $"game.ver parse error: buffer is {span.Length} bytes; " +
                $"expected a multiple of 4 with at least {MinFieldCount} u32 elements ({MinFileSize} bytes minimum). " +
                "spec: Docs/RE/formats/game_ver.md §Identification.");


        var field0 = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        var field1 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        var field2 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        var field3 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);

        var field4 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);

        var versionSource = BinaryPrimitives.ReadUInt32LittleEndian(span[0x14..]);

        var field6 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x18..]);

        return new GameVerData
        {
            Field0 = field0,
            Field1 = field1,
            Field2 = field2,
            Field3 = field3,
            Field4 = field4,
            VersionSourceField = versionSource,
            Field6 = field6
        };
    }
}