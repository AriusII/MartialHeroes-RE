using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.sod</c> collision solid blob files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §11. Collision solid blob — .sod
/// <para>
/// Top-level layout:
///   solidCount u32le | solidCount × 108-byte SolidRecord[]
///   then, for each solid:
///     quadCount u32le | quadCount × 48-byte QuadRecord[]
/// spec: Docs/RE/formats/terrain.md §11.1 Top-level layout: CONFIRMED.
/// </para>
/// <para>
/// SolidRecord 108 bytes: AABB +0..+15 VERIFIED, _reserved_a +016..+059 all-zero VERIFIED,
///   quad_count_embedded +060 VERIFIED, _authoring_ptr +064 stale pointer VERIFIED,
///   _reserved_b +068..+107 all-zero VERIFIED.
/// spec: Docs/RE/formats/terrain.md §11.2 SolidRecord: CONFIRMED (stride + AABB + reserved fields).
/// </para>
/// <para>
/// QuadRecord 48 bytes: four XZ corners +0..+31 VERIFIED; trailing scalars +32..+47 PARTIAL.
/// spec: Docs/RE/formats/terrain.md §11.3 QuadRecord — correction 2026-06-12: corners, not slope/intercept.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class SodBlobParser
{
    // SolidRecord stride: 108 bytes (0x6C). CONFIRMED.
    // spec: Docs/RE/formats/terrain.md §11.2 — "108 bytes (0x6C)": CONFIRMED (stride).
    private const int SolidRecordStride = 108; // 0x6C

    // QuadRecord stride: 48 bytes. CONFIRMED.
    // spec: Docs/RE/formats/terrain.md §11.3 — "48 bytes (0x30)": CONFIRMED (stride).
    private const int QuadRecordStride = 48; // 0x30

    // SolidRecord field offsets (all VERIFIED or CONFIRMED).
    // spec: Docs/RE/formats/terrain.md §11.2.
    private const int SolidAabbXMinOffset  =  0;  // f32 VERIFIED
    private const int SolidAabbZMinOffset  =  4;  // f32 VERIFIED
    private const int SolidAabbXMaxOffset  =  8;  // f32 VERIFIED
    private const int SolidAabbZMaxOffset  = 12;  // f32 VERIFIED
    // +016..+059: _reserved_a (all-zero, VERIFIED — meaning UNVERIFIED)
    // +060: quad_count_embedded u32 (VERIFIED — parser reads stream copy instead)
    // +064: _authoring_ptr u32 (stale pointer, VERIFIED — parser ignores it)
    // +068..+107: _reserved_b (all-zero, VERIFIED)

    // QuadRecord field offsets.
    // spec: Docs/RE/formats/terrain.md §11.3.
    private const int QuadX0Offset     =  0;  // f32 VERIFIED
    private const int QuadZ0Offset     =  4;  // f32 VERIFIED
    private const int QuadX1Offset     =  8;  // f32 VERIFIED
    private const int QuadZ1Offset     = 12;  // f32 VERIFIED
    private const int QuadX2Offset     = 16;  // f32 VERIFIED
    private const int QuadZ2Offset     = 20;  // f32 VERIFIED
    private const int QuadX3Offset     = 24;  // f32 VERIFIED
    private const int QuadZ3Offset     = 28;  // f32 VERIFIED
    private const int QuadPlane0Offset = 32;  // f32 PARTIAL
    private const int QuadPlane1Offset = 36;  // f32 PARTIAL
    private const int QuadPlane2Offset = 40;  // f32 PARTIAL
    private const int QuadPlane3Offset = 44;  // f32 PARTIAL

    /// <summary>
    /// Parses the raw bytes of a <c>.sod</c> file into a <see cref="SodBlob"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded SodBlob with typed AABB and quad data.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static SodBlob Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span, data);

    private static SodBlob Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        int offset = 0;

        // solidCount u32le @ offset 0.
        // spec: Docs/RE/formats/terrain.md §11.1 — "solidCount u32le @ offset 0: CONFIRMED".
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                ".sod parse error: buffer too short for solidCount field. " +
                "spec: Docs/RE/formats/terrain.md §11.1.");

        uint solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        // Read solidCount × 108-byte solid records.
        // spec: Docs/RE/formats/terrain.md §11.1 — "SolidRecord[solidCount] — fixed stride 108 bytes each": CONFIRMED.
        long solidBlockBytes = (long)solidCount * SolidRecordStride;
        if (offset + solidBlockBytes > span.Length)
            throw new InvalidDataException(
                $".sod parse error: SolidRecord array truncated — solidCount={solidCount} requires " +
                $"{solidBlockBytes} bytes at offset {offset}, but buffer length is {span.Length}. " +
                "spec: Docs/RE/formats/terrain.md §11.1.");

        // Collect raw solid records and decode AABB from each.
        var rawSolids = new ReadOnlyMemory<byte>[(int)solidCount];
        // Decoded AABB data extracted now; quads added below.
        var solidAabbXMin = new float[(int)solidCount];
        var solidAabbZMin = new float[(int)solidCount];
        var solidAabbXMax = new float[(int)solidCount];
        var solidAabbZMax = new float[(int)solidCount];

        for (int s = 0; s < (int)solidCount; s++)
        {
            rawSolids[s] = backing.Slice(offset, SolidRecordStride);
            ReadOnlySpan<byte> solidSpan = span.Slice(offset, SolidRecordStride);

            // AABB +0..+15 (VERIFIED).
            // spec: Docs/RE/formats/terrain.md §11.2 — aabb_xmin f32 @ +000: VERIFIED.
            solidAabbXMin[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbXMinOffset..]);
            solidAabbZMin[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbZMinOffset..]);
            solidAabbXMax[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbXMaxOffset..]);
            solidAabbZMax[s] = BinaryPrimitives.ReadSingleLittleEndian(solidSpan[SolidAabbZMaxOffset..]);

            offset += SolidRecordStride;
        }

        // Read per-solid quad lists.
        // spec: Docs/RE/formats/terrain.md §11.1 — "per-solid quadCount u32le + QuadRecord[quadCount]".
        var triCounts = new uint[(int)solidCount];
        var rawTris = new ReadOnlyMemory<byte>[(int)solidCount];
        var decodedQuadsPerSolid = new CollisionQuad[(int)solidCount][];

        for (int s = 0; s < (int)solidCount; s++)
        {
            // quadCount u32le (stream copy — NOT the embedded copy in the SolidRecord).
            // spec: Docs/RE/formats/terrain.md §11.1 — "quadCount u32le appears AFTER the flat SolidRecord array": CONFIRMED.
            if (offset + 4 > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: quadCount for solid[{s}] truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain.md §11.1.");

            uint quadCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;
            triCounts[s] = quadCount;

            long quadBytes = (long)quadCount * QuadRecordStride;
            if (offset + quadBytes > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: QuadRecord data for solid[{s}] truncated — " +
                    $"quadCount={quadCount} requires {quadBytes} bytes at offset {offset}, " +
                    $"but buffer length is {span.Length}. " +
                    "spec: Docs/RE/formats/terrain.md §11.3.");

            rawTris[s] = backing.Slice(offset, (int)quadBytes);

            // Decode each QuadRecord.
            // spec: Docs/RE/formats/terrain.md §11.3 QuadRecord (48 bytes): four XZ corners VERIFIED; trailing scalars PARTIAL.
            var quads = new CollisionQuad[(int)quadCount];
            for (int q = 0; q < (int)quadCount; q++)
            {
                int qOff = offset + q * QuadRecordStride;
                ReadOnlySpan<byte> qSpan = span.Slice(qOff, QuadRecordStride);

                // Corners +0..+31 (VERIFIED).
                // spec: Docs/RE/formats/terrain.md §11.3 — x0 f32 @ +000: VERIFIED.
                quads[q] = new CollisionQuad
                {
                    X0 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadX0Offset..]),
                    Z0 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadZ0Offset..]),
                    X1 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadX1Offset..]),
                    Z1 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadZ1Offset..]),
                    X2 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadX2Offset..]),
                    Z2 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadZ2Offset..]),
                    X3 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadX3Offset..]),
                    Z3 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadZ3Offset..]),
                    // Trailing scalars +032..+047 (PARTIAL).
                    // spec: Docs/RE/formats/terrain.md §11.3 — plane0..plane3 f32 +032..+047: PARTIAL.
                    Plane0 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadPlane0Offset..]),
                    Plane1 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadPlane1Offset..]),
                    Plane2 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadPlane2Offset..]),
                    Plane3 = BinaryPrimitives.ReadSingleLittleEndian(qSpan[QuadPlane3Offset..]),
                };
            }

            decodedQuadsPerSolid[s] = quads;
            offset += (int)quadBytes;
        }

        // Assemble typed SolidRecord array.
        var solids = new SolidRecord[(int)solidCount];
        for (int s = 0; s < (int)solidCount; s++)
        {
            solids[s] = new SolidRecord
            {
                AabbXMin  = solidAabbXMin[s],
                AabbZMin  = solidAabbZMin[s],
                AabbXMax  = solidAabbXMax[s],
                AabbZMax  = solidAabbZMax[s],
                Quads     = decodedQuadsPerSolid[s],
                RawRecord = rawSolids[s],
            };
        }

        return new SodBlob
        {
            SolidCount       = solidCount,
            Solids           = solids,
            RawSolidRecords  = rawSolids,
            TriangleCounts   = triCounts,
            RawTriangleData  = rawTris,
        };
    }
}
