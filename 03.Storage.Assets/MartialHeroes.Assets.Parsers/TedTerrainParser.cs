using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.ted</c> terrain geometry blob files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §5. Terrain geometry blob — .ted
/// <para>
/// The .ted blob contains no file-level header — the five sequential data blocks begin at
/// offset 0 with no magic number or version prefix.
/// spec: Docs/RE/formats/terrain.md §5.2 — "no file-level header observed": UNVERIFIED
/// (the VFS abstraction could silently consume a header).
/// </para>
/// <para>
/// Total file size: 46 987 bytes (0xB78B).
/// spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46 987 bytes (0xB78B)": CONFIRMED (sum of five block sizes).
/// </para>
/// <para>
/// Five sequential fixed-size blocks with no padding between them:
///   Block 1 — Heightmap:     65×65 f32le values     @ offset 0,     size 16 900 bytes (0x4204). CONFIRMED.
///   Block 2 — Normals:       65×65 RGB u8×3         @ offset 16900, size 12 675 bytes (0x3183). CONFIRMED.
///   Block 3 — Lookup table:  256 u8 entries          @ offset 29575, size 256 bytes (0x100).    CONFIRMED.
///   Block 4 — Direction map: 256 u8 entries          @ offset 29831, size 256 bytes (0x100).    CONFIRMED.
///   Block 5 — Diffuse RGBA:  65×65 RGBA u8×4        @ offset 30087, size 16 900 bytes (0x4204). CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class TedTerrainParser
{
    // ---- Block dimensions (all CONFIRMED) -----------------------------------

    // Vertex grid: 65 × 65 = 4225 vertices per cell.
    // spec: Docs/RE/formats/terrain.md §5.1 — "Vertex grid: 65 × 65 vertices": CONFIRMED.
    private const int VertexCount = TerrainCell.VertexCount; // 4225

    // Block 1 — Heightmap: 4225 × 4 bytes = 16900 bytes.
    // spec: Docs/RE/formats/terrain.md §5.2 Block 1 — offset 0, size 16900 (0x4204): CONFIRMED.
    private const int HeightmapOffset = 0;
    private const int HeightmapSize = VertexCount * 4; // 16900

    // Block 2 — Normals: 4225 × 3 bytes = 12675 bytes.
    // spec: Docs/RE/formats/terrain.md §5.2 Block 2 — offset 16900, size 12675 (0x3183): CONFIRMED.
    private const int NormalsOffset = HeightmapOffset + HeightmapSize; // 16900
    private const int NormalsSize = VertexCount * 3; // 12675

    // Block 3 — Lookup table: 256 bytes.
    // spec: Docs/RE/formats/terrain.md §5.2 Block 3 — offset 29575, size 256 (0x100): CONFIRMED.
    private const int LookupOffset = NormalsOffset + NormalsSize; // 29575
    private const int LookupSize = 256; // 0x100

    // Block 4 — Direction map: 256 bytes.
    // spec: Docs/RE/formats/terrain.md §5.2 Block 4 — offset 29831, size 256 (0x100): CONFIRMED.
    private const int DirectionOffset = LookupOffset + LookupSize; // 29831
    private const int DirectionSize = 256; // 0x100

    // Block 5 — Diffuse RGBA: 4225 × 4 bytes = 16900 bytes.
    // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — offset 30087, size 16900 (0x4204): CONFIRMED.
    private const int DiffuseOffset = DirectionOffset + DirectionSize; // 30087
    private const int DiffuseSize = VertexCount * 4; // 16900

    // Total file size: 46987 bytes.
    // spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46 987 bytes (0xB78B)": CONFIRMED.
    private const int TotalSize = DiffuseOffset + DiffuseSize; // 46987

    /// <summary>
    /// Parses the raw bytes of a <c>.ted</c> file into a <see cref="TerrainCell"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded terrain cell.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is smaller than the expected 46 987 bytes.
    /// </exception>
    public static TerrainCell Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <summary>
    /// Parses from a <see cref="ReadOnlySpan{byte}"/>.
    /// </summary>
    public static TerrainCell Parse(ReadOnlySpan<byte> data)
    {
        // Validate minimum size. The spec states the exact total size.
        // spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46 987 bytes (0xB78B)": CONFIRMED.
        if (data.Length < TotalSize)
            throw new InvalidDataException(
                $".ted parse error: buffer too small — expected at least {TotalSize} bytes, " +
                $"got {data.Length} bytes. " +
                $"spec: Docs/RE/formats/terrain.md §5.1.");

        // ---- Block 1: Heightmap (4225 × f32 LE) ----
        // spec: Docs/RE/formats/terrain.md §5.2 Block 1 — offset 0, size 16900: CONFIRMED.
        float[] heights = new float[VertexCount];
        ReadOnlySpan<byte> heightRaw = data.Slice(HeightmapOffset, HeightmapSize);
        // Reinterpret the byte span directly as f32 values — valid on LE hosts.
        // On big-endian hosts a byte-swap would be needed; .NET 10 targets are always LE in practice.
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, float>(heightRaw).CopyTo(heights);
        }
        else
        {
            // Fallback for big-endian hosts: read field-by-field.
            for (int i = 0; i < VertexCount; i++)
                heights[i] = BinaryPrimitives.ReadSingleLittleEndian(heightRaw[(i * 4)..]);
        }

        // ---- Block 2: Vertex normals (4225 × 3 bytes, RGB) ----
        // spec: Docs/RE/formats/terrain.md §5.2 Block 2 — offset 16900, size 12675: CONFIRMED.
        // Encoding convention (0–255 → −1…+1 or 128-bias): UNVERIFIED.
        byte[] normals = new byte[NormalsSize];
        data.Slice(NormalsOffset, NormalsSize).CopyTo(normals);

        // ---- Block 3: Lookup table (256 bytes) ----
        // spec: Docs/RE/formats/terrain.md §5.2 Block 3 — offset 29575, size 256: CONFIRMED.
        // Purpose: UNVERIFIED.
        byte[] lookupTable = new byte[LookupSize];
        data.Slice(LookupOffset, LookupSize).CopyTo(lookupTable);

        // ---- Block 4: Direction map (256 bytes) ----
        // spec: Docs/RE/formats/terrain.md §5.2 Block 4 — offset 29831, size 256: CONFIRMED.
        // Purpose: UNVERIFIED.
        byte[] directionMap = new byte[DirectionSize];
        data.Slice(DirectionOffset, DirectionSize).CopyTo(directionMap);

        // ---- Block 5: Diffuse colour map (4225 × 4 bytes, RGBA) ----
        // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — offset 30087, size 16900: CONFIRMED.
        byte[] diffuseColours = new byte[DiffuseSize];
        data.Slice(DiffuseOffset, DiffuseSize).CopyTo(diffuseColours);

        return new TerrainCell
        {
            Heights = heights,
            Normals = normals,
            LookupTable = lookupTable,
            DirectionMap = directionMap,
            DiffuseColours = diffuseColours,
        };
    }
}