using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.lst</c> per-area cell manifest files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §1.2 Per-area cell manifest — .lst
/// <para>
/// Binary layout (CONFIRMED — inferred from parser read sequence; no magic or version field observed):
///   count u32 LE | count × u32 LE keys
/// </para>
/// <para>
/// Cell key formula: key = mapZ + 100000 * mapX. CONFIRMED.
/// Decomposition: mapX = key / 100000; mapZ = key % 100000.
/// </para>
/// <para>
/// Known unknowns: whether the file begins with a magic number or version prefix is UNVERIFIED.
/// The parser was observed reading a 4-byte count then a contiguous array of 4-byte keys.
/// spec: Docs/RE/formats/terrain.md §1.2 — "Known unknowns: whether the file begins with a magic
/// number or version prefix is unverified." UNVERIFIED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class LstManifestParser
{
    /// <summary>
    /// Parses the raw bytes of a <c>.lst</c> file into an <see cref="LstManifest"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded manifest.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown on truncation or buffer overrun.
    /// </exception>
    public static LstManifest Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static LstManifest Parse(ReadOnlySpan<byte> data)
    {
        int offset = 0;

        // count u32 LE @ offset 0 — number of valid cell entries.
        // spec: Docs/RE/formats/terrain.md §1.2 — "count u32le @ offset 0: CONFIRMED".
        if (offset + 4 > data.Length)
            throw new InvalidDataException(
                $".lst parse error: buffer too short for count field (buffer length {data.Length}).");

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        // keys u32le[] — one per cell entry.
        // spec: Docs/RE/formats/terrain.md §1.2 — "keys u32le[count] @ offset 4: CONFIRMED".
        long keysBytes = (long)count * 4;
        if (offset + keysBytes > data.Length)
            throw new InvalidDataException(
                $".lst parse error: key array truncated — count={count} requires {keysBytes} bytes " +
                $"at offset {offset}, but buffer length is {data.Length}.");

        LstCellEntry[] entries = new LstCellEntry[(int)count];

        for (int i = 0; i < (int)count; i++)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            // Decompose key → (mapX, mapZ).
            // Key formula: key = mapZ + 100000 * mapX  →  mapX = key / 100000, mapZ = key % 100000.
            // spec: Docs/RE/formats/terrain.md §1.2 — "key = mapZ + 100000 * mapX": CONFIRMED.
            int mapX = (int)(key / 100000u);
            int mapZ = (int)(key % 100000u);

            entries[i] = new LstCellEntry(key, mapX, mapZ);
        }

        return new LstManifest { Entries = entries };
    }

    /// <summary>
    /// Computes the cell key for a given <c>(mapX, mapZ)</c> pair.
    /// </summary>
    /// <param name="mapX">Cell X coordinate.</param>
    /// <param name="mapZ">Cell Z coordinate.</param>
    /// <returns>Cell key as stored in the .lst file.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain.md §1.2 — "key = mapZ + 100000 * mapX": CONFIRMED.
    /// </remarks>
    public static uint ComputeKey(int mapX, int mapZ) =>
        (uint)((uint)mapZ + 100000u * (uint)mapX);
}