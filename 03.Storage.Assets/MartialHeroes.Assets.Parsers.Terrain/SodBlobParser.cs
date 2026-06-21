using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

/// <summary>
///     Parser for <c>.sod</c> per-cell wall-collision segment blob files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sod.md — wall-segment (slope-intercept line z=m·x+b) set; in-memory
///     quadtree built at load time. BINARY-WON (CYCLE 7, anchor 263bd994):
///     NOT ray-parity point-in-polygon and NOT four-corner quads.
///     <para>
///         Top-level layout (spec: Docs/RE/formats/sod.md §Container structure):
///         u32 solidCount | solidCount × 108-byte SolidRecord[] (read in one pass) |
///         for each solid: u32 quadCount | quadCount × 48-byte WallSegment[]
///     </para>
///     <para>
///         SolidRecord 108 bytes (spec: Docs/RE/formats/sod.md §SolidRecord):
///         aabbMinX/Z/MaxX/Z f32×4 +0x00..+0x0F (parser + sample);
///         +0x10..+0x3B 44 bytes zero on disk (runtime-only fields, ignored on read);
///         quadCount u32 @ +0x3C — embedded redundant copy (authoritative copy is the stream word);
///         quadArrayPtr u32 @ +0x40 — on-disk garbage, overwritten at load (ignored on read);
///         +0x44..+0x6B 40 bytes zero on disk (runtime use, ignored on read).
///     </para>
///     <para>
///         WallSegment (QuadRecord) 48 bytes (spec: Docs/RE/formats/sod.md §QuadRecord):
///         AABB aabbMinX/Z/MaxX/Z f32×4 @ +0x00..+0x0F (parser + sample);
///         endpoint p0x/p0z/p1x/p1z f32×4 @ +0x10..+0x1F (sample);
///         slope f32 @ +0x20, xConst f32 @ +0x24, intercept f32 @ +0x28, axisFlag u32 @ +0x2C
///         (all: parser + sample). Line equation: z = slope·x + intercept; axisFlag==1 = vertical case.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class SodBlobParser
{
    // SolidRecord stride: 108 bytes (0x6C). CONFIRMED.
    // spec: Docs/RE/formats/sod.md §SolidRecord — "stride 108 (0x6C)".
    private const int SolidRecordStride = 108; // 0x6C

    // WallSegment (QuadRecord) stride: 48 bytes (0x30). CONFIRMED.
    // spec: Docs/RE/formats/sod.md §QuadRecord — "stride 48 (0x30)".
    private const int WallSegmentStride = 48; // 0x30

    // SolidRecord field offsets.
    // spec: Docs/RE/formats/sod.md §SolidRecord.
    private const int SolidAabbMinXOffset = 0x00; // f32 aabbMinX — parser + sample
    private const int SolidAabbMinZOffset = 0x04; // f32 aabbMinZ — parser + sample
    private const int SolidAabbMaxXOffset = 0x08; // f32 aabbMaxX — parser + sample

    private const int SolidAabbMaxZOffset = 0x0C; // f32 aabbMaxZ — parser + sample
    // +0x10..+0x3B (44 bytes): zero on disk, runtime-only (node-grid / pointer / center). Ignored on read.
    // spec: Docs/RE/formats/sod.md §SolidRecord — "+0x10 44 (zero on disk)".
    // +0x3C (4 bytes): u32 quadCount embedded — redundant copy; authoritative copy is the stream word. Ignored on read.
    // spec: Docs/RE/formats/sod.md §SolidRecord — "+0x3C (+60) u32 quadCount embedded: re-read from stream".
    // +0x40 (4 bytes): quadArrayPtr — on-disk garbage, overwritten at load. Ignored on read.
    // spec: Docs/RE/formats/sod.md §SolidRecord — "+0x40 (+64) u32 quadArrayPtr on-disk garbage".
    // +0x44..+0x6B (40 bytes): zero on disk, runtime use. Ignored on read.

    // WallSegment (QuadRecord) field offsets.
    // spec: Docs/RE/formats/sod.md §QuadRecord.
    private const int SegAabbMinXOffset = 0x00; // f32 aabbMinX — parser + sample
    private const int SegAabbMinZOffset = 0x04; // f32 aabbMinZ — parser + sample
    private const int SegAabbMaxXOffset = 0x08; // f32 aabbMaxX — parser + sample
    private const int SegAabbMaxZOffset = 0x0C; // f32 aabbMaxZ — parser + sample
    private const int SegP0XOffset = 0x10; // f32 p0x endpoint-0 X — sample
    private const int SegP0ZOffset = 0x14; // f32 p0z endpoint-0 Z — sample
    private const int SegP1XOffset = 0x18; // f32 p1x endpoint-1 X — sample
    private const int SegP1ZOffset = 0x1C; // f32 p1z endpoint-1 Z — sample
    private const int SegSlopeOffset = 0x20; // f32 slope (m) in z=m·x+b — parser + sample
    private const int SegXConstOffset = 0x24; // f32 xConst (vertical-axis special case) — parser + sample
    private const int SegInterceptOffset = 0x28; // f32 intercept (b) in z=m·x+b — parser + sample
    private const int SegAxisFlagOffset = 0x2C; // u32 axisFlag (==1 means vertical / axis-aligned) — parser + sample

    /// <summary>
    ///     Parses the raw bytes of a <c>.sod</c> file into a <see cref="SodBlob" />.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded <see cref="SodBlob" /> with wall-segment collision data.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown on truncation or buffer overrun.
    /// </exception>
    public static SodBlob Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    private static SodBlob Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        var offset = 0;

        // solidCount u32le @ offset 0.
        // spec: Docs/RE/formats/sod.md §Container structure — "u32 solidCount".
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                ".sod parse error: buffer too short for solidCount field. " +
                "spec: Docs/RE/formats/sod.md §Container structure.");

        var solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        // Read the whole solid array in one pass — solidCount × 108-byte SolidRecord[].
        // spec: Docs/RE/formats/sod.md §Container structure —
        //   "108 × N SolidRecord[N] read in a single pass; quadCount arrays follow after".
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
            // Retain a zero-copy slice of the raw record.
            rawSolids[s] = backing.IsEmpty
                ? (ReadOnlyMemory<byte>)span.Slice(offset, SolidRecordStride).ToArray()
                : backing.Slice(offset, SolidRecordStride);
            var solidSpan = span.Slice(offset, SolidRecordStride);

            // AABB +0x00..+0x0F — four f32 world-space XZ bounds.
            // spec: Docs/RE/formats/sod.md §SolidRecord — aabbMinX/Z f32 @ +0x00/+0x04 (parser + sample).
            solidAabbMinX[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[..]);
            solidAabbMinZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMinZOffset..]);
            solidAabbMaxX[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMaxXOffset..]);
            solidAabbMaxZ[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbMaxZOffset..]);

            // +0x10..+0x3B: zero on disk, runtime-only — ignored.
            // spec: Docs/RE/formats/sod.md §SolidRecord — "+0x10 44 (zero on disk)".
            // +0x3C: embedded quadCount (redundant) — not read here; authoritative stream word read below.
            // spec: Docs/RE/formats/sod.md §SolidRecord — "+0x3C (+60) u32 quadCount: Also re-read from stream".
            // +0x40: quadArrayPtr (on-disk garbage) — ignored.
            // +0x44..+0x6B: runtime use, zero on disk — ignored.

            offset += SolidRecordStride;
        }

        // Per-solid wall-segment blocks follow the entire solid array.
        // spec: Docs/RE/formats/sod.md §Container structure —
        //   "for i in 0..solidCount-1: u32 quadCount + QuadRecord[quadCount]".
        var segmentCounts = new uint[(int)solidCount];
        var rawSegmentData = new ReadOnlyMemory<byte>[(int)solidCount];
        var decodedSegmentsPerSolid = new WallSegment[(int)solidCount][];

        for (var s = 0; s < (int)solidCount; s++)
        {
            // quadCount u32le — stream copy (authoritative; embedded SolidRecord copy at +0x3C is redundant).
            // spec: Docs/RE/formats/sod.md §Container structure — "u32 quadCount" preceding each solid's segment array.
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

            // Decode each WallSegment (QuadRecord) — slope-intercept line z = slope·x + intercept.
            // spec: Docs/RE/formats/sod.md §QuadRecord — BINARY-WON (CYCLE 7, anchor 263bd994):
            //   each record is a wall SEGMENT; collision is slope-intercept line intersection + AABB clamping.
            var segs = new WallSegment[(int)segCount];
            for (var q = 0; q < (int)segCount; q++)
            {
                var qOff = offset + q * WallSegmentStride;
                var qSpan = span.Slice(qOff, WallSegmentStride);

                segs[q] = new WallSegment
                {
                    // 2D segment AABB +0x00..+0x0F.
                    // spec: Docs/RE/formats/sod.md §QuadRecord — aabbMinX f32 @ +0x00 (parser + sample).
                    AabbMinX = BinaryPrimitives.ReadSingleLittleEndian(qSpan[..]),
                    AabbMinZ = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMinZOffset..]),
                    AabbMaxX = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMaxXOffset..]),
                    AabbMaxZ = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegAabbMaxZOffset..]),

                    // Segment endpoints p0/p1 +0x10..+0x1F — bound the segment within its AABB.
                    // spec: Docs/RE/formats/sod.md §QuadRecord — "+0x10 f32 p0x, +0x14 f32 p0z" (sample).
                    P0X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP0XOffset..]),
                    P0Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP0ZOffset..]),
                    P1X = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP1XOffset..]),
                    P1Z = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegP1ZOffset..]),

                    // Slope-intercept line equation z = slope·x + intercept.
                    // spec: Docs/RE/formats/sod.md §QuadRecord — "+0x20 f32 slope (m)" (parser + sample).
                    Slope = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegSlopeOffset..]),
                    // xConst: X value when the wall is vertical (axisFlag==1); x = xConst along Z.
                    // spec: Docs/RE/formats/sod.md §QuadRecord — "+0x24 f32 xConst" (parser + sample).
                    XConst = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegXConstOffset..]),
                    // Intercept b in z = slope·x + b.
                    // spec: Docs/RE/formats/sod.md §QuadRecord — "+0x28 f32 intercept (b)" (parser + sample).
                    Intercept = BinaryPrimitives.ReadSingleLittleEndian(qSpan[SegInterceptOffset..]),
                    // axisFlag u32: ==1 means vertical/axis-aligned wall (use xConst, not slope/intercept).
                    // spec: Docs/RE/formats/sod.md §QuadRecord — "+0x2C u32 axisFlag" (parser + sample).
                    AxisFlag = BinaryPrimitives.ReadUInt32LittleEndian(qSpan[SegAxisFlagOffset..])
                };
            }

            decodedSegmentsPerSolid[s] = segs;
            offset += (int)segBlockBytes;
        }

        // Assemble typed SolidRecord array.
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