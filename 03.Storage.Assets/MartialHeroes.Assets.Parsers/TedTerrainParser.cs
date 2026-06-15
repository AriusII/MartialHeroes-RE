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

        // ---- Block 2: Vertex normals (4225 × 3 signed bytes, i8 / 127.0f) ----
        // spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "each component is i8; decode as N = (sbyte)b / 127.0f": CONFIRMED.
        // Channel order: R=Nx, G=Ny, B=Nz. spec: §5.5 — "R=Nx, G=Ny, B=Nz": CONFIRMED.
        var normals = new (float Nx, float Ny, float Nz)[VertexCount];
        ReadOnlySpan<byte> normalRaw = data.Slice(NormalsOffset, NormalsSize);
        for (int i = 0; i < VertexCount; i++)
        {
            // Divisor 127.0 is a literal constant in the loader.
            // spec: Docs/RE/formats/terrain.md §5.5 — "divisor 127.0 literal constant in loader": CONFIRMED.
            float nx = (sbyte)normalRaw[i * 3 + 0] / 127.0f; // R = Nx
            float ny = (sbyte)normalRaw[i * 3 + 1] / 127.0f; // G = Ny
            float nz = (sbyte)normalRaw[i * 3 + 2] / 127.0f; // B = Nz
            normals[i] = (nx, ny, nz);
        }

        // ---- Block 3: Texture index grid (16 × 16 = 256 u8 bytes, 1-based) ----
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16 grid": CONFIRMED.
        // Value 0 is NOT a no-texture sentinel. The legacy loader clamps 0 → 1 (renders slot 1).
        // There is no sentinel branch; a byte below 1 is silently incremented to 1.
        // spec: Docs/RE/formats/terrain.md §5.9 — "b < 1 → b = 1 (clamp, no-sentinel branch)": CONFIRMED (two-witness, 2026-06-15).
        // spec: Docs/RE/formats/terrain.md §14 (recently promoted) — "0 = no-texture sentinel REFUTED".
        byte[] textureIndexGrid = new byte[LookupSize];
        data.Slice(LookupOffset, LookupSize).CopyTo(textureIndexGrid);
        // Apply the legacy clamp: any value below 1 becomes 1.
        for (int i = 0; i < LookupSize; i++)
        {
            if (textureIndexGrid[i] < 1)
                textureIndexGrid[i] = 1; // clamp-to-1: spec: terrain.md §5.9 — "if (b < 1) b = 1": CONFIRMED.
        }

        // ---- Block 4: Quad split / UV orientation flags (16 × 16 = 256 u8 bytes, values 0-3) ----
        // spec: Docs/RE/formats/terrain.md §5.7 Block 4 — "u8, observed values 0-3": CONFIRMED.
        // Exact bit-to-geometry mapping: UNVERIFIED.
        byte[] directionFlags = new byte[DirectionSize];
        data.Slice(DirectionOffset, DirectionSize).CopyTo(directionFlags);

        // ---- Block 5: Per-vertex diffuse colour (4225 × 4 bytes, RGBA) ----
        // Encoding: on-disk byte = 2 × logical_value; decode as logical = byte × 0.5f.
        // spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "editor ×0.5 storage; loader ×0.5 at runtime": CONFIRMED.
        // Channel order: R, G, B, A. spec: §5.8 — "RGBA, not ARGB": CONFIRMED.
        var diffuseColours = new (float R, float G, float B, float A)[VertexCount];
        ReadOnlySpan<byte> diffuseRaw = data.Slice(DiffuseOffset, DiffuseSize);
        for (int i = 0; i < VertexCount; i++)
        {
            float r = diffuseRaw[i * 4 + 0] * 0.5f;
            float g = diffuseRaw[i * 4 + 1] * 0.5f;
            float b = diffuseRaw[i * 4 + 2] * 0.5f;
            float a = diffuseRaw[i * 4 + 3] * 0.5f;
            diffuseColours[i] = (r, g, b, a);
        }

        return new TerrainCell
        {
            Heights = heights,
            Normals = normals,
            TextureIndexGrid = textureIndexGrid,
            DirectionFlags = directionFlags,
            DiffuseColours = diffuseColours,
        };
    }
}