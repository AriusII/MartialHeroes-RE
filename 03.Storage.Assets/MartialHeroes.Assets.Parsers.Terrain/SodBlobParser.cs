using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class SodBlobParser
{
    private const int SolidRecordStride = 108;

    private const int QuadRecordStride = 48;

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
            solidAabbMinZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[0x04..]);
            solidAabbMaxX[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[0x08..]);
            solidAabbMaxZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[0x0C..]);

            offset += SolidRecordStride;
        }

        var quadCounts = new uint[(int)solidCount];
        var rawQuadData = new ReadOnlyMemory<byte>[(int)solidCount];
        var decodedQuadsPerSolid = new SodQuad[(int)solidCount][];

        for (var s = 0; s < (int)solidCount; s++)
        {
            if (offset + 4 > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: quadCount for solid[{s}] truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/sod.md §Container structure.");

            var quadCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;
            quadCounts[s] = quadCount;

            var quadBlockBytes = (long)quadCount * QuadRecordStride;
            if (offset + quadBlockBytes > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: QuadRecord data for solid[{s}] truncated — " +
                    $"quadCount={quadCount} requires {quadBlockBytes} bytes at offset {offset}, " +
                    $"but buffer length is {span.Length}. " +
                    "spec: Docs/RE/formats/sod.md §QuadRecord.");

            rawQuadData[s] = backing.IsEmpty
                ? (ReadOnlyMemory<byte>)span.Slice(offset, (int)quadBlockBytes).ToArray()
                : backing.Slice(offset, (int)quadBlockBytes);

            var quads = new SodQuad[(int)quadCount];
            for (var q = 0; q < (int)quadCount; q++)
            {
                var qOff = offset + q * QuadRecordStride;
                var qSpan = span.Slice(qOff, QuadRecordStride);

                quads[q] = new SodQuad
                {
                    C0X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[..]),
                    C0Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x04..]),
                    C1X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x08..]),
                    C1Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x0C..]),
                    C2X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x10..]),
                    C2Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x14..]),
                    C3X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x18..]),
                    C3Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x1C..]),
                    Opaque0 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x20..]),
                    Opaque1 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x24..]),
                    Opaque2 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x28..]),
                    Opaque3 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[0x2C..])
                };
            }

            decodedQuadsPerSolid[s] = quads;
            offset += (int)quadBlockBytes;
        }

        var solids = new SodSolid[(int)solidCount];
        for (var s = 0; s < (int)solidCount; s++)
            solids[s] = new SodSolid
            {
                AabbMinX = solidAabbMinX[s],
                AabbMinZ = solidAabbMinZ[s],
                AabbMaxX = solidAabbMaxX[s],
                AabbMaxZ = solidAabbMaxZ[s],
                Quads = decodedQuadsPerSolid[s],
                RawRecord = rawSolids[s]
            };

        return new SodBlob
        {
            SolidCount = solidCount,
            Solids = solids,
            RawSolidRecords = rawSolids,
            QuadCounts = quadCounts,
            RawQuadData = rawQuadData
        };
    }
}