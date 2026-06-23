using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class RegionTableParser
{
    private const int RecordStride = 48;
    private const int RecordCount = 32;
    private const int ExpectedMinSize = RecordCount * RecordStride;
    private const int ZoneNameOffset = 0x00;
    private const int ZoneNameSize = 40;
    private const int ZoneTypeOffset = 0x28;
    private const int TailOffset = 0x2C;

    static RegionTableParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static RegionTableRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < ExpectedMinSize)
            throw new InvalidDataException(
                $"regiontable*.bin parse error: buffer length {span.Length} is too short for " +
                $"{RecordCount} × {RecordStride}-byte records (expected ≥ {ExpectedMinSize} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §regiontable.");

        var cp949 = Encoding.GetEncoding(949);
        var results = new RegionTableRecord[RecordCount];

        for (var i = 0; i < RecordCount; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var zoneName = ReadNullTerminatedCp949(rec.Slice(ZoneNameOffset, ZoneNameSize), cp949);

            var zoneType = BinaryPrimitives.ReadUInt32LittleEndian(rec[ZoneTypeOffset..]);

            var tail = BinaryPrimitives.ReadUInt32LittleEndian(rec[TailOffset..]);

            results[i] = new RegionTableRecord
            {
                RegionId = i,
                ZoneName = zoneName,
                ZoneType = zoneType,
                TailOpaque = tail
            };
        }

        return results;
    }


    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}