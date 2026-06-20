using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

/// <summary>
///     Parser for <c>.ted</c> terrain geometry blob files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §5. Terrain geometry blob — .ted
///     <para>
///         The .ted blob contains no file-level header — the five sequential data blocks begin at
///         offset 0 with no magic number or version prefix.
///         spec: Docs/RE/formats/terrain.md §5.2 — "no file-level header observed": UNVERIFIED
///         (the VFS abstraction could silently consume a header).
///     </para>
///     <para>
///         Total file size: 46 987 bytes (0xB78B).
///         spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46 987 bytes (0xB78B)": CONFIRMED (sum of five block
///         sizes).
///     </para>
///     <para>
///         Five sequential fixed-size blocks with no padding between them:
///         Block 1 — Heightmap:     65×65 f32le values     @ offset 0,     size 16 900 bytes (0x4204). CONFIRMED.
///         Block 2 — Normals:       65×65 RGB u8×3         @ offset 16900, size 12 675 bytes (0x3183). CONFIRMED.
///         Block 3 — Lookup table:  256 u8 entries          @ offset 29575, size 256 bytes (0x100).    CONFIRMED.
///         Block 4 — Direction map: 256 u8 entries          @ offset 29831, size 256 bytes (0x100).    CONFIRMED.
///         Block 5 — Diffuse RGBA:  65×65 RGBA u8×4        @ offset 30087, size 16 900 bytes (0x4204). CONFIRMED.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies.
///     </para>
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
    ///     Parses the raw bytes of a <c>.ted</c> file into a <see cref="TerrainCell" />.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded terrain cell.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the buffer is smaller than the expected 46 987 bytes.
    /// </exception>
    public static TerrainCell Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>
    ///     Parses from a <see cref="ReadOnlySpan{byte}" />.
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
        var heights = new float[VertexCount];
        var heightRaw = data.Slice(HeightmapOffset, HeightmapSize);
        // Reinterpret the byte span directly as f32 values — valid on LE hosts.
        // On big-endian hosts a byte-swap would be needed; .NET 10 targets are always LE in practice.
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, float>(heightRaw).CopyTo(heights);
        else
            // Fallback for big-endian hosts: read field-by-field.
            for (var i = 0; i < VertexCount; i++)
                heights[i] = BinaryPrimitives.ReadSingleLittleEndian(heightRaw[(i * 4)..]);

        // ---- Block 2: Vertex normals (4225 × 3 signed bytes, i8 / 127.0f) ----
        // spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "each component is i8; decode as N = (sbyte)b / 127.0f": CONFIRMED.
        // Channel order: R=Nx, G=Ny, B=Nz. spec: §5.5 — "R=Nx, G=Ny, B=Nz": CONFIRMED.
        var normals = new (float Nx, float Ny, float Nz)[VertexCount];
        var normalRaw = data.Slice(NormalsOffset, NormalsSize);
        for (var i = 0; i < VertexCount; i++)
        {
            // Divisor 127.0 is a literal constant in the loader.
            // spec: Docs/RE/formats/terrain.md §5.5 — "divisor 127.0 literal constant in loader": CONFIRMED.
            var nx = (sbyte)normalRaw[i * 3 + 0] / 127.0f; // R = Nx
            var ny = (sbyte)normalRaw[i * 3 + 1] / 127.0f; // G = Ny
            var nz = (sbyte)normalRaw[i * 3 + 2] / 127.0f; // B = Nz
            normals[i] = (nx, ny, nz);
        }

        // ---- Block 3: Texture index grid (16 × 16 = 256 u8 bytes) ----
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 16×16 grid": CONFIRMED.
        // RESOLVED (CYCLE 1, ida_anchor 263bd994): the parser stores each byte RAW with NO
        // idx-1 decrement and NO value-below-1 clamp. The idx-1 decrement is a REAL, FIXED,
        // statically isolable code site in the per-cell texture FINALIZE routine (a 16×16 loop):
        // each cell's byte is first clamped to [1, count], then indexed as perCellTexList[byte-1].
        // That clamp+decrement is the FINALIZE (render-domain) consumer's job — NOT the parser's.
        // The parser stores the raw block-3 byte; the finalize path applies the -1.
        // The intTexId from .map is stored into the per-cell list with NO -1; pool accessors
        // index pool_base + 76*intTexId DIRECTLY — intTexId IS the 0-based pool slot.
        // spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — "idx-1 decrement RESOLVED": CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join —
        //   "intTexId IS the 0-based record index, used DIRECTLY — NO -1; the only -1 is on the .ted byte".
        var textureIndexGrid = new byte[LookupSize];
        data.Slice(LookupOffset, LookupSize).CopyTo(textureIndexGrid);

        // ---- Block 4: Quad split / UV orientation flags (16 × 16 = 256 u8 bytes, values 0-3) ----
        // spec: Docs/RE/formats/terrain.md §5.7 Block 4 — "u8, observed values 0-3": CONFIRMED.
        // Exact bit-to-geometry mapping: UNVERIFIED.
        var directionFlags = new byte[DirectionSize];
        data.Slice(DirectionOffset, DirectionSize).CopyTo(directionFlags);

        // ---- Block 5: Per-vertex diffuse colour (4225 × 4 bytes, RGBA) ----
        // Encoding: on-disk byte = 2 × logical_value; decode as logical = byte × 0.5f.
        // spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "editor ×0.5 storage; loader ×0.5 at runtime": CONFIRMED.
        // Channel order: R, G, B, A. spec: §5.8 — "RGBA, not ARGB": CONFIRMED.
        var diffuseColours = new (float R, float G, float B, float A)[VertexCount];
        var diffuseRaw = data.Slice(DiffuseOffset, DiffuseSize);
        for (var i = 0; i < VertexCount; i++)
        {
            var r = diffuseRaw[i * 4 + 0] * 0.5f;
            var g = diffuseRaw[i * 4 + 1] * 0.5f;
            var b = diffuseRaw[i * 4 + 2] * 0.5f;
            var a = diffuseRaw[i * 4 + 3] * 0.5f;
            diffuseColours[i] = (r, g, b, a);
        }

        return new TerrainCell
        {
            Heights = heights,
            Normals = normals,
            TextureIndexGrid = textureIndexGrid,
            DirectionFlags = directionFlags,
            DiffuseColours = diffuseColours
        };
    }
}