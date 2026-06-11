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
    /// Decoded vertex normals, one (Nx, Ny, Nz) float triple per vertex, row-major.
    /// Length = 4 225. Each component was stored as a signed byte (i8) on disk and decoded
    /// as <c>component = (sbyte)byte / 127.0f</c>.
    /// Channel order: R=Nx, G=Ny, B=Nz.
    /// spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "i8 / 127.0: CONFIRMED. R=Nx, G=Ny, B=Nz: CONFIRMED."
    /// </summary>
    public required (float Nx, float Ny, float Nz)[] Normals { get; init; }

    // -------------------------------------------------------------------------
    // Block 3 — Texture index grid
    // -------------------------------------------------------------------------

    /// <summary>
    /// 16 × 16 texture index grid (row-major, Z=row, X=col).
    /// Length = 256. Values are 1-based (no zero observed; max observed = 11).
    /// Each byte selects the background texture for a 4×4-quad region.
    /// spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16": CONFIRMED.
    /// </summary>
    public required byte[] TextureIndexGrid { get; init; }

    // -------------------------------------------------------------------------
    // Block 4 — Quad split / UV orientation flags
    // -------------------------------------------------------------------------

    /// <summary>
    /// 16 × 16 direction / UV-orientation flags (row-major, Z=row, X=col).
    /// Length = 256. Observed values: 0, 1, 2, 3 only (2 bits used).
    /// Exact bit-to-geometry mapping UNVERIFIED.
    /// spec: Docs/RE/formats/terrain.md §5.7 Block 4 — "u8, values 0-3: CONFIRMED. Bit semantics UNVERIFIED."
    /// </summary>
    public required byte[] DirectionFlags { get; init; }

    // -------------------------------------------------------------------------
    // Block 5 — Per-vertex diffuse colour
    // -------------------------------------------------------------------------

    /// <summary>
    /// Per-vertex diffuse colour decoded from the on-disk ×0.5 encoding.
    /// Length = 4 225. Channel order: R, G, B, A.
    /// On-disk encoding: each byte = 2 × logical_value. Decode: <c>logical = byte × 0.5f</c>.
    /// spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "editor ×0.5 storage; loader ×0.5 decode: CONFIRMED."
    /// </summary>
    public required (float R, float G, float B, float A)[] DiffuseColours { get; init; }
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

    // ── Geometry directives (TERRAIN section) ──────────────────────────────
    // spec: Docs/RE/formats/terrain.md §3.4 Geometry directives (TERRAIN section).
    // All directives below are CONFIRMED.

    /// <summary>
    /// Quad grid width (quads per row).
    /// From the WIDTH directive in TERRAIN sections. Typically 64.
    /// spec: Docs/RE/formats/terrain.md §3.4 — WIDTH integer: CONFIRMED.
    /// Null when directive is absent (e.g. non-TERRAIN sections).
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Quad grid height (quads per column).
    /// From the HEIGHT directive in TERRAIN sections. Typically 64.
    /// spec: Docs/RE/formats/terrain.md §3.4 — HEIGHT integer: CONFIRMED.
    /// Null when directive is absent.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// World-unit spacing between adjacent vertices (quads per world unit).
    /// From the GRID directive in TERRAIN sections. Typically 16.
    /// spec: Docs/RE/formats/terrain.md §3.4 — GRID integer: CONFIRMED.
    /// Null when directive is absent.
    /// </summary>
    public int? Grid { get; init; }

    /// <summary>
    /// Maximum world-Y height value in this cell; informational only.
    /// From the MAX_HEIGHTFILED directive (note: verbatim dropped-L spelling from original files).
    /// spec: Docs/RE/formats/terrain.md §3.4 — MAX_HEIGHTFILED float: CONFIRMED.
    /// Null when directive is absent.
    /// </summary>
    public float? MaxHeightFiled { get; init; }

    /// <summary>
    /// Minimum world-Y height value in this cell; informational only.
    /// From the MIN_HEIGHTFILED directive (note: verbatim dropped-L spelling from original files).
    /// spec: Docs/RE/formats/terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
    /// Null when directive is absent.
    /// </summary>
    public float? MinHeightFiled { get; init; }

    /// <summary>
    /// World-space XZ origin of the cell quad.
    /// From the ORIGIN directive: two comma-separated floats.
    /// Equals ((mapX-10000)*1024, (mapZ-10000)*1024).
    /// spec: Docs/RE/formats/terrain.md §3.4 — ORIGIN float,float: CONFIRMED.
    /// Null when directive is absent.
    /// </summary>
    public (float X, float Z)? Origin { get; init; }
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
/// One tile record from a <c>.mud</c> ambient-sound tile blob.
/// Stride: 8 bytes. All fields are single bytes; no endianness dependency.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §6.2 Record layout — 8 bytes per tile: VERIFIED (all 3 samples).
/// </remarks>
public readonly record struct MudTileRecord(
    /// <summary>Always zero. spec: §6.2 pad0 u8 @ +0: VERIFIED.</summary>
    byte Pad0,
    /// <summary>Always zero. spec: §6.2 pad1 u8 @ +1: VERIFIED.</summary>
    byte Pad1,
    /// <summary>Music BGM group index. 0=no music. spec: §6.2 music_group u8 @ +2: VERIFIED.</summary>
    byte MusicGroup,
    /// <summary>Ambient sound index 0. 0=no sound. spec: §6.2 ambient_idx_0 u8 @ +3: VERIFIED.</summary>
    byte AmbientIdx0,
    /// <summary>Ambient sound index 1. 0=no sound. spec: §6.2 ambient_idx_1 u8 @ +4: VERIFIED.</summary>
    byte AmbientIdx1,
    /// <summary>Effect sound index 0. 0=no sound. spec: §6.2 effect_idx_0 u8 @ +5: VERIFIED.</summary>
    byte EffectIdx0,
    /// <summary>Effect sound index 1. 0=no sound. spec: §6.2 effect_idx_1 u8 @ +6: VERIFIED.</summary>
    byte EffectIdx1,
    /// <summary>Effect sound index 2. Always zero in known samples. spec: §6.2 effect_idx_2 u8 @ +7: VERIFIED (limited).</summary>
    byte EffectIdx2
);

/// <summary>
/// Decoded result of a <c>.mud</c> ambient-sound tile blob.
/// Fixed size: 32 768 bytes (0x8000) = 64 × 64 × 8 B.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §6 Ambient-sound tile blob — .mud.
/// Total size CONFIRMED. Grid 64×64 CONFIRMED. Record stride 8 B CONFIRMED. All fields VERIFIED.
/// Index formula: col = floor(worldX/16) &amp; 0x3F; row = floor(worldZ/16) &amp; 0x3F.
/// spec: Docs/RE/formats/terrain.md §6.1 index formula: CONFIRMED.
/// </remarks>
public sealed class MudBlob
{
    /// <summary>
    /// Grid width (columns, X axis). 64.
    /// spec: Docs/RE/formats/terrain.md §6.1 — GridCols = 64: CONFIRMED.
    /// </summary>
    public const int GridCols = 64;

    /// <summary>
    /// Grid height (rows, Z axis). 64.
    /// spec: Docs/RE/formats/terrain.md §6.1 — GridRows = 64: CONFIRMED.
    /// </summary>
    public const int GridRows = 64;

    /// <summary>
    /// Record stride in bytes. 8.
    /// spec: Docs/RE/formats/terrain.md §6.1 — RecordStride = 8: CONFIRMED.
    /// </summary>
    public const int RecordStride = 8;

    /// <summary>
    /// Expected fixed size of a .mud file in bytes (64 × 64 × 8 = 32 768).
    /// spec: Docs/RE/formats/terrain.md §6 — FixedSize = 32 768 bytes (0x8000): CONFIRMED.
    /// </summary>
    public const int FixedSize = GridRows * GridCols * RecordStride; // 32768

    /// <summary>
    /// Decoded tile grid, row-major (row = Z axis, col = X axis).
    /// Length = 4 096 (64 × 64). Index = row × 64 + col.
    /// spec: Docs/RE/formats/terrain.md §6.1 — Row-major (Z=row, X=col): CONFIRMED.
    /// </summary>
    public required MudTileRecord[] Tiles { get; init; }
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