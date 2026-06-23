using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

public static class RegionZoneTableParser
{
    public const int RecordCount = 32;

    public const int RecordStride = 48;

    public const int ExpectedTableSize = RecordCount * RecordStride;

    private const int OpaqueLeadingSize = 40;

    private const int ZoneTypeOffset = 40;

    private const int OpaqueTrailingOffset = 44;
    private const int OpaqueTrailingSize = 4;


    public static RegionZoneRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < ExpectedTableSize)
            throw new InvalidDataException(
                $"regiontable*.bin zone-type parse error: buffer length {span.Length} is too " +
                $"short for 32 × 48-byte records (expected ≥ {ExpectedTableSize} bytes). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.");

        var records = new RegionZoneRecord[RecordCount];

        for (var regionId = 0; regionId < RecordCount; regionId++)
        {
            var recBase = regionId * RecordStride;

            var opaqueLeading = data.Slice(recBase, OpaqueLeadingSize);

            var zoneTypeRaw = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(recBase + ZoneTypeOffset, 4));

            var opaqueTrailing = data.Slice(recBase + OpaqueTrailingOffset, OpaqueTrailingSize);

            records[regionId] = new RegionZoneRecord
            {
                RegionId = regionId,
                ZoneTypeRaw = zoneTypeRaw,
                OpaqueLeading = opaqueLeading,
                OpaqueTrailing = opaqueTrailing
            };
        }

        return records;
    }
}