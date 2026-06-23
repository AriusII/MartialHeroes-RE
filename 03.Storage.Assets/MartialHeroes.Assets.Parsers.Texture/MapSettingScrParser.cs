using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class MapSettingScrParser
{
    private const int RecordStride = 84;

    static MapSettingScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static MapZoneRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"mapsetting.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (MAPSETTING_RECORD_BYTES). " +
                "spec: Docs/RE/formats/misc_data.md §7.1.");

        var count = span.Length / RecordStride;
        var results = new MapZoneRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);
            var zoneId = BinaryPrimitives.ReadInt32LittleEndian(rec[..]);
            var zoneName = ReadNullTerminatedCp949(rec.Slice(0x04, 36), cp949);
            var worldMinX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x28..]);
            var worldMinZ = BinaryPrimitives.ReadInt32LittleEndian(rec[0x2C..]);
            var worldMaxX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x30..]);
            var worldMaxZ = BinaryPrimitives.ReadInt32LittleEndian(rec[0x34..]);
            var flagsA = BinaryPrimitives.ReadInt32LittleEndian(rec[0x38..]);
            var flagsB = BinaryPrimitives.ReadInt32LittleEndian(rec[0x3C..]);
            var fogDensity = BinaryPrimitives.ReadSingleLittleEndian(rec[0x40..]);
            var unknown44 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x44..]);
            var unknown48 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x48..]);
            var unknown4C = BinaryPrimitives.ReadInt32LittleEndian(rec[0x4C..]);
            var unknown50 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x50..]);

            results[i] = new MapZoneRecord
            {
                ZoneId = zoneId,
                ZoneName = zoneName,
                WorldMinX = worldMinX,
                WorldMinZ = worldMinZ,
                WorldMaxX = worldMaxX,
                WorldMaxZ = worldMaxZ,
                FlagsA = flagsA,
                FlagsB = flagsB,
                FogDensity = fogDensity,
                Unknown0x44 = unknown44,
                Unknown0x48 = unknown48,
                Unknown0x4C = unknown4C,
                Unknown0x50 = unknown50
            };
        }

        return results;
    }


    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        return len == 0 ? string.Empty : cp949.GetString(field[..len]);
    }
}