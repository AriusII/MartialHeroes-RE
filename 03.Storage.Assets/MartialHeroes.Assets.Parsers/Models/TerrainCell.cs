namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Decoded result of a <c>.ted</c> terrain geometry blob.
/// Represents one 65×65 vertex terrain cell (64×64 quads).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §5. Terrain geometry blob — .ted
/// spec: Docs/RE/formats/terrain.md §5.1 Grid geometry — "65 × 65 vertices": CONFIRMED.
/// Total .ted file size: 46 987 bytes (0xB78B). CONFIRMED (sum of five block sizes).
/// </remarks>
public sealed class TerrainCell
{
    /// <summary>
    /// Vertex grid dimension (65 per axis).
    /// spec: Docs/RE/formats/terrain.md §5.1 Grid geometry — "65 × 65 vertices": CONFIRMED.
    /// </summary>
    public const int GridSize = 65;

    /// <summary>
    /// Total vertex count (65 × 65 = 4 225).
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — 4225 f32 values: CONFIRMED.
    /// </summary>
    public const int VertexCount = GridSize * GridSize; // 4225

    // -------------------------------------------------------------------------
    // Block 1 — Heightmap
    // -------------------------------------------------------------------------

    /// <summary>
    /// Height values for each vertex, in row-major order.
    /// Length = 4 225 (65 × 65). Each value is an IEEE 754 f32 LE.
    /// Axis orientation of row-major storage is UNVERIFIED (see spec §5.3).
    /// Height scale factor is UNVERIFIED — values appear to be direct world-space Y units.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — "Heightmap: f32le, 65×65 = 4225": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — byte offset 0, size 16900 (0x4204): CONFIRMED.
    /// </summary>
    public required float[] Heights { get; init; }

    // -------------------------------------------------------------------------
    // Block 2 — Vertex normals
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packed RGB normal triples, one per vertex, in row-major order.
    /// Length = 4 225 elements × 3 bytes each; stored here as flat byte array of length 12 675.
    /// Encoding convention (0–255 → −1…+1 or 128-bias) is UNVERIFIED — see spec §5.3.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 2 — "Vertex normals: u8×3 (R,G,B), 65×65": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 2 — byte offset 16900, size 12675 (0x3183): CONFIRMED.
    /// </summary>
    public required byte[] Normals { get; init; }

    // -------------------------------------------------------------------------
    // Block 3 — Lookup table (purpose UNVERIFIED)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 256-byte lookup table. Purpose UNVERIFIED; may be a palette or texture-blend index lookup.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 3 — "Lookup table: u8, 256 entries": CONFIRMED (size).
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 3 — byte offset 29575, size 256 (0x100): CONFIRMED.
    /// Purpose: UNVERIFIED.
    /// </summary>
    public required byte[] LookupTable { get; init; }

    // -------------------------------------------------------------------------
    // Block 4 — Direction map (purpose UNVERIFIED)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 256-byte direction map. Purpose UNVERIFIED; may encode per-quad surface orientation or material flags.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 4 — "Direction map: u8, 256 entries": CONFIRMED (size).
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 4 — byte offset 29831, size 256 (0x100): CONFIRMED.
    /// Purpose: UNVERIFIED.
    /// </summary>
    public required byte[] DirectionMap { get; init; }

    // -------------------------------------------------------------------------
    // Block 5 — Diffuse colour map
    // -------------------------------------------------------------------------

    /// <summary>
    /// Diffuse RGBA colour per vertex, packed as flat byte array (4 bytes per vertex = RGBA order).
    /// Length = 4 225 × 4 = 16 900 bytes.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 5 — "Diffuse colour: u8×4 (R,G,B,A), 65×65": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 5 — byte offset 30087, size 16900 (0x4204): CONFIRMED.
    /// </summary>
    public required byte[] DiffuseColours { get; init; }
}

/// <summary>
/// Entry from a <c>.lst</c> per-area cell manifest.
/// Encodes one valid <c>(mapX, mapZ)</c> coordinate pair.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §1.2 Per-area cell manifest — .lst
/// Key formula: key = mapZ + 100000 * mapX. CONFIRMED.
/// </remarks>
public readonly record struct LstCellEntry(uint Key, int MapX, int MapZ);

/// <summary>
/// Decoded result of a <c>.lst</c> per-area cell manifest.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §1.2 Per-area cell manifest — .lst
/// Binary layout: u32le count | count × u32le keys. CONFIRMED.
/// </remarks>
public sealed class LstManifest
{
    /// <summary>
    /// All valid cell entries for this area, in on-disk order.
    /// spec: Docs/RE/formats/terrain.md §1.2 — keys[]: CONFIRMED.
    /// </summary>
    public required LstCellEntry[] Entries { get; init; }
}

/// <summary>
/// One section from a <c>.map</c> text scene descriptor.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format).
/// </remarks>
public sealed class MapSection
{
    /// <summary>
    /// Section keyword (e.g. "TERRAIN", "SOLID", "BUILDING", "FX1").
    /// spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
    /// </summary>
    public required string Keyword { get; init; }

    /// <summary>
    /// Path from the DATAFILE directive, or null if the section has no DATAFILE.
    /// spec: Docs/RE/formats/terrain.md §3.2 DATAFILE directive: CONFIRMED.
    /// </summary>
    public string? DataFile { get; init; }

    /// <summary>
    /// Texture entries from the TEXTURES block.
    /// Each entry is (intFlag, intTexId); semantics of intFlag are UNVERIFIED.
    /// spec: Docs/RE/formats/terrain.md §3.3 TEXTURES directive: CONFIRMED (structure).
    /// Semantics of intFlag: UNVERIFIED.
    /// </summary>
    public required (int Flag, int TexId)[] Textures { get; init; }
}

/// <summary>
/// Decoded result of a <c>.map</c> plain-text scene descriptor.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format).
/// </remarks>
public sealed class MapDescriptor
{
    /// <summary>
    /// All sections found in this .map file, in parse order.
    /// spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
    /// </summary>
    public required MapSection[] Sections { get; init; }
}

/// <summary>
/// Stub for a <c>.mud</c> ambient/audio tile blob.
/// The internal layout of the 32 768-byte body is UNVERIFIED.
/// Only the fixed size (32 768 bytes) is confirmed.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §6. Ambient/audio tile blob — .mud
/// Fixed size: 32 768 bytes (0x8000). CONFIRMED (fixed read size).
/// Internal structure: UNVERIFIED.
/// "A hypothesis present in the analysis notes suggests a 64×64 grid of 8-byte records
///  (64×64×8=32768) but this was not derived from any observed parse loop." UNVERIFIED.
/// </remarks>
public sealed class MudBlob
{
    /// <summary>
    /// Expected fixed size of a .mud file in bytes.
    /// spec: Docs/RE/formats/terrain.md §6 — "32 768 bytes (0x8000)": CONFIRMED.
    /// </summary>
    public const int FixedSize = 32768; // 0x8000

    /// <summary>
    /// Raw opaque bytes. Internal layout UNVERIFIED.
    /// spec: Docs/RE/formats/terrain.md §6 — "internal structure UNVERIFIED".
    /// </summary>
    public required ReadOnlyMemory<byte> RawData { get; init; }
}

/// <summary>
/// Stub for a <c>.sod</c> collision solid blob.
/// Top-level count and stride are confirmed; internal field layouts are UNVERIFIED.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §8. Collision solid blob — .sod
/// </remarks>
public sealed class SodBlob
{
    /// <summary>
    /// Number of solid records in this file.
    /// spec: Docs/RE/formats/terrain.md §8.1 — solidCount u32le @ offset 0: CONFIRMED.
    /// </summary>
    public required uint SolidCount { get; init; }

    /// <summary>
    /// Raw solid records, each exactly 108 bytes.
    /// Stride (108 bytes) is CONFIRMED; internal field layout is UNVERIFIED.
    /// spec: Docs/RE/formats/terrain.md §8.2 SolidRecord — "108 bytes (0x6C)": CONFIRMED (stride).
    /// Fields: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte>[] RawSolidRecords { get; init; }

    /// <summary>
    /// Per-solid triangle counts.
    /// spec: Docs/RE/formats/terrain.md §8.3 Per-record triangle data — triCount u32le: CONFIRMED.
    /// </summary>
    public required uint[] TriangleCounts { get; init; }

    /// <summary>
    /// Per-solid raw triangle data, each triangle is 48 bytes.
    /// Stride (48 bytes) is CONFIRMED; internal field layout is UNVERIFIED.
    /// spec: Docs/RE/formats/terrain.md §8.3 Per-record triangle data — "48-byte triangle structs": CONFIRMED (stride).
    /// Fields: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte>[] RawTriangleData { get; init; }
}