using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class RegionZonePropertiesParser
{
    public const int RecordCount = 32;
    public const int RecordStride = 48;
    public const int RecordBlockSize = RecordCount * RecordStride;
    public const int OnDiskFileSize = 1664;

    private const int ZoneNameOffset = 0x00;
    private const int ZoneNameSize = 32;
    private const int LabelXOffset = 0x20;
    private const int LabelZOffset = 0x24;
    private const int ZoneTypeOffset = 0x28;
    private const int TailOffset = 0x2C;

    static RegionZonePropertiesParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static RegionZoneProperties[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static RegionZoneProperties[] Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length < RecordBlockSize)
            throw new InvalidDataException(
                $"regiontable*.bin parse error: buffer length {span.Length} is too short for " +
                $"{RecordCount} x {RecordStride}-byte records (expected >= {RecordBlockSize} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §regiontable<NNN>.bin");

        var cp949 = Encoding.GetEncoding(949);
        var results = new RegionZoneProperties[RecordCount];

        for (var i = 0; i < RecordCount; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var zoneName = DecodeNullTerminatedCp949(rec.Slice(ZoneNameOffset, ZoneNameSize), cp949);

            var labelX = BinaryPrimitives.ReadSingleLittleEndian(rec[LabelXOffset..]);

            var labelZ = BinaryPrimitives.ReadSingleLittleEndian(rec[LabelZOffset..]);

            var zoneTypeRaw = BinaryPrimitives.ReadInt32LittleEndian(rec[ZoneTypeOffset..]);

            var zoneType = zoneTypeRaw switch
            {
                0 => RegionZoneType.Safe,
                1 => RegionZoneType.OpenPvp,
                2 => RegionZoneType.Closed,
                _ => RegionZoneType.Safe
            };

            var tail = BinaryPrimitives.ReadInt32LittleEndian(rec[TailOffset..]);

            results[i] = new RegionZoneProperties
            {
                RegionId = i,
                ZoneName = zoneName,
                LabelX = labelX,
                LabelZ = labelZ,
                ZoneType = zoneType,
                TailOpaque = tail
            };
        }

        return results;
    }

    private static string DecodeNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}