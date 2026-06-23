using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class SodBlobParser
{
    private const int SolidRecordStride = 108;

    private const int WallSegmentStride = 48;

    private const int SolidAabbMinXOffset = 0x00;
    private const int SolidAabbMinZOffset = 0x04;
    private const int SolidAabbMaxXOffset = 0x08;

    private const int SolidAabbMaxZOffset = 0x0C;

    private const int SegAabbMinXOffset = 0x00;
    private const int SegAabbMinZOffset = 0x04;
    private const int SegAabbMaxXOffset = 0x08;
    private const int SegAabbMaxZOffset = 0x0C;
    private const int SegP0XOffset = 0x10;
    private const int SegP0ZOffset = 0x14;
    private const int SegP1XOffset = 0x18;
    private const int SegP1ZOffset = 0x1C;
    private const int SegSlopeOffset = 0x20;
    private const int SegXConstOffset = 0x24;
    private const int SegInterceptOffset = 0x28;
    private const int SegAxisFlagOffset = 0x2C;

    public static SodBlob Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    private static SodBlob Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        var offset = 0;

        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                ".sod parse error: buffer too short for solidCount field. " +
                "spec: Docs/RE/formats/sod.md §Container structure.");

        var solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        var solidBlockBytes = (long)solidCount * SolidRecordStride;
        if (offset + solidBlockBytes > span.Length)
            throw new InvalidDataException(
                $".sod parse error: SolidRecord array truncated — solidCount={solidCount} requires " +
                $"{solidBlockBytes} bytes at offset {offset}, but buffer length is {span.Length}. " +
                "spec: Docs/RE/formats/sod.md §Container structure.");

        var rawSolids = new ReadOnlyMemory<byte>[(int)solidCount];
        var solidAabbMinX = new float[(int)solidCount];
        var solidAabbMinZ = new float[(int)solidCount];
        var solidAabbMaxX = new float[(int)solidCount];
        var solidAabbMaxZ = new float[(int)solidCount];

        for (var s = 0; s < (int)solidCount; s++)
        {
            rawSolids[s] = backing.IsEmpty
                ? (ReadOnlyMemory<byte>)span.Slice(offset, SolidRecordStride).ToArray()
                : backing.Slice(offset, SolidRecordStride);
            var solidSpan = span.Slice(offset, SolidRecordStride);

            solidAabbMinX[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[..]);
            solidAabbMinZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMinZOffset..]);
            solidAabbMaxX[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMaxXOffset..]);
            solidAabbMaxZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMaxZOffset..]);


            offset += SolidRecordStride;
        }

        var segmentCounts = new uint[(int)solidCount];
        var rawSegmentData = new ReadOnlyMemory<byte>[(int)solidCount];
        var decodedSegmentsPerSolid = new WallSegment[(int)solidCount][];

        for (var s = 0; s < (int)solidCount; s++)
        {
            if (offset + 4 > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: quadCount for solid[{s}] truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/sod.md §Container structure.");

            var segCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;
            segmentCounts[s] = segCount;

            var segBlockBytes = (long)segCount * WallSegmentStride;
            if (offset + segBlockBytes > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: WallSegment data for solid[{s}] truncated — " +
                    $"quadCount={segCount} requires {segBlockBytes} bytes at offset {offset}, " +
                    $"but buffer length is {span.Length}. " +
                    "spec: Docs/RE/formats/sod.md §QuadRecord.");

            rawSegmentData[s] = backing.IsEmpty
                ? (ReadOnlyMemory<byte>)span.Slice(offset, (int)segBlockBytes).ToArray()
                : backing.Slice(offset, (int)segBlockBytes);

            var segs = new WallSegment[(int)segCount];
            for (var q = 0; q < (int)segCount; q++)
            {
                var qOff = offset + q * WallSegmentStride;
                var qSpan = span.Slice(qOff, WallSegmentStride);

                segs[q] = new WallSegment
                {
                    AabbMinX = BinaryPrimitives.ReadSingleLittleEndian(qSpan[..]),
                    AabbMinZ = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMinZOffset..]),
                    AabbMaxX = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMaxXOffset..]),
                    AabbMaxZ = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMaxZOffset..]),

                    P0X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP0XOffset..]),
                    P0Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP0ZOffset..]),
                    P1X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP1XOffset..]),
                    P1Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP1ZOffset..]),

                    Slope = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegSlopeOffset..]),
                    XConst = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegXConstOffset..]),
                    Intercept = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegInterceptOffset..]),
                    AxisFlag = BinaryPrimitives.ReadUInt32LittleEndian(qSpan[SegAxisFlagOffset..])
                };
            }

            decodedSegmentsPerSolid[s] = segs;
            offset += (int)segBlockBytes;
        }

        var solids = new SolidRecord[(int)solidCount];
        for (var s = 0; s < (int)solidCount; s++)
            solids[s] = new SolidRecord
            {
                AabbMinX = solidAabbMinX[s],
                AabbMinZ = solidAabbMinZ[s],
                AabbMaxX = solidAabbMaxX[s],
                AabbMaxZ = solidAabbMaxZ[s],
                Segments = decodedSegmentsPerSolid[s],
                RawRecord = rawSolids[s]
            };

        return new SodBlob
        {
            SolidCount = solidCount,
            Solids = solids,
            RawSolidRecords = rawSolids,
            TriangleCounts = segmentCounts,
            RawTriangleData = rawSegmentData
        };
    }
}