using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Stub parser for <c>.sod</c> collision solid blob files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §8. Collision solid blob — .sod
/// <para>
/// Top-level layout:
///   solidCount u32le | solidCount × 108-byte SolidRecord | per-record triangle lists.
/// </para>
/// <para>
/// Only the strides (108 bytes per solid record, 48 bytes per triangle) and
/// solidCount / triCount fields are CONFIRMED. All internal field layouts are UNVERIFIED.
/// spec: Docs/RE/formats/terrain.md §8.2 — "stride 108 bytes CONFIRMED; fields UNVERIFIED".
/// spec: Docs/RE/formats/terrain.md §8.3 — "stride 48 bytes CONFIRMED; fields UNVERIFIED".
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class SodBlobParser
{
    // SolidRecord stride: 108 bytes (0x6C). CONFIRMED (stride only; internal layout UNVERIFIED).
    // spec: Docs/RE/formats/terrain.md §8.2 — "108 bytes (0x6C)": CONFIRMED (stride).
    private const int SolidRecordStride = 108; // 0x6C

    // Triangle struct stride: 48 bytes. CONFIRMED (stride only; internal layout UNVERIFIED).
    // spec: Docs/RE/formats/terrain.md §8.3 — "48-byte triangle structs": CONFIRMED (stride).
    private const int TriangleStride = 48;

    /// <summary>
    /// Parses the raw bytes of a <c>.sod</c> file into a <see cref="SodBlob"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded (mostly opaque) sod blob.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static SodBlob Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span, data);

    private static SodBlob Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        int offset = 0;

        // solidCount u32le @ offset 0.
        // spec: Docs/RE/formats/terrain.md §8.1 — "solidCount u32le @ offset 0: CONFIRMED".
        if (offset + 4 > span.Length)
            throw new InvalidDataException(
                ".sod parse error: buffer too short for solidCount field.");

        uint solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        // Read solidCount × 108-byte solid records.
        // spec: Docs/RE/formats/terrain.md §8.1 — "solidCount × 108-byte SolidRecord[]: CONFIRMED (stride)".
        long solidBlockBytes = (long)solidCount * SolidRecordStride;
        if (offset + solidBlockBytes > span.Length)
            throw new InvalidDataException(
                $".sod parse error: SolidRecord array truncated — solidCount={solidCount} requires " +
                $"{solidBlockBytes} bytes at offset {offset}, but buffer length is {span.Length}.");

        var rawSolids = new ReadOnlyMemory<byte>[(int)solidCount];
        for (int s = 0; s < (int)solidCount; s++)
        {
            // Slice 108-byte record; field layout UNVERIFIED.
            // spec: Docs/RE/formats/terrain.md §8.2 — "internal layout UNVERIFIED".
            rawSolids[s] = backing.Slice(offset, SolidRecordStride);
            offset += SolidRecordStride;
        }

        // Read per-solid triangle lists.
        // spec: Docs/RE/formats/terrain.md §8.3 Per-record triangle data.
        var triCounts = new uint[(int)solidCount];
        var rawTris = new ReadOnlyMemory<byte>[(int)solidCount];

        for (int s = 0; s < (int)solidCount; s++)
        {
            // triCount u32le per record.
            // spec: Docs/RE/formats/terrain.md §8.3 — "triCount u32le: CONFIRMED".
            if (offset + 4 > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: triCount for solid[{s}] truncated at offset {offset}.");

            uint triCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;
            triCounts[s] = triCount;

            // triCount × 48-byte triangle structs; field layout UNVERIFIED.
            // spec: Docs/RE/formats/terrain.md §8.3 — "triCount × 48 bytes CONFIRMED (stride); fields UNVERIFIED".
            long triBytes = (long)triCount * TriangleStride;
            if (offset + triBytes > span.Length)
                throw new InvalidDataException(
                    $".sod parse error: triangle data for solid[{s}] truncated — " +
                    $"triCount={triCount} requires {triBytes} bytes at offset {offset}, " +
                    $"but buffer length is {span.Length}.");

            rawTris[s] = backing.Slice(offset, (int)triBytes);
            offset += (int)triBytes;
        }

        return new SodBlob
        {
            SolidCount = solidCount,
            RawSolidRecords = rawSolids,
            TriangleCounts = triCounts,
            RawTriangleData = rawTris,
        };
    }
}