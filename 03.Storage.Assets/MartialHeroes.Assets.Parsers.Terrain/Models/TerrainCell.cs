namespace MartialHeroes.Assets.Parsers.Terrain.Models;

/// <summary>
///     Decoded result of a <c>.ted</c> terrain geometry blob.
///     Represents one 65×65 vertex terrain cell (64×64 quads).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §5. Terrain geometry blob — .ted
///     spec: Docs/RE/formats/terrain.md §5.1 Grid geometry — "65 × 65 vertices": CONFIRMED.
///     Total .ted file size: 46 987 bytes (0xB78B). CONFIRMED (sum of five block sizes).
/// </remarks>
public sealed class TerrainCell
{
    /// <summary>
    ///     Vertex grid dimension (65 per axis).
    ///     spec: Docs/RE/formats/terrain.md §5.1 Grid geometry — "65 × 65 vertices": CONFIRMED.
    /// </summary>
    public const int GridSize = 65;

    /// <summary>
    ///     Total vertex count (65 × 65 = 4 225).
    ///     spec: Docs/RE/formats/terrain.md §5.2 Block 1 — 4225 f32 values: CONFIRMED.
    /// </summary>
    public const int VertexCount = GridSize * GridSize; // 4225

    // -------------------------------------------------------------------------
    // Block 1 — Heightmap
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Height values for each vertex, in row-major order.
    ///     Length = 4 225 (65 × 65). Each value is an IEEE 754 f32 LE.
    ///     Axis orientation: row = Z axis, col = X axis. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.2 — col=X, row=Z: CONFIRMED.
    ///     Height scale factor is UNVERIFIED — values appear to be direct world-space Y units.
    ///     spec: Docs/RE/formats/terrain.md §5.2 Block 1 — "Heightmap: f32le, 65×65 = 4225": CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.3 Block table — byte offset 0, size 16900 (0x4204): CONFIRMED.
    /// </summary>
    public required float[] Heights { get; init; }

    // -------------------------------------------------------------------------
    // Block 2 — Vertex normals
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Decoded vertex normals, one (Nx, Ny, Nz) float triple per vertex, row-major.
    ///     Length = 4 225. Each component was stored as a signed byte (i8) on disk and decoded
    ///     as <c>component = (sbyte)byte / 127.0f</c>.
    ///     Channel order: R=Nx, G=Ny, B=Nz.
    ///     spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "i8 / 127.0: CONFIRMED. R=Nx, G=Ny, B=Nz: CONFIRMED."
    /// </summary>
    public required (float Nx, float Ny, float Nz)[] Normals { get; init; }

    // -------------------------------------------------------------------------
    // Block 3 — Texture index grid
    // -------------------------------------------------------------------------

    /// <summary>
    ///     16 × 16 texture index grid (row-major, Z=row, X=col).
    ///     Length = 256. Values are 1-based (no zero observed; max observed = 11).
    ///     Each byte selects the background texture for a 4×4-quad region.
    ///     spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16": CONFIRMED.
    /// </summary>
    public required byte[] TextureIndexGrid { get; init; }

    // -------------------------------------------------------------------------
    // Block 4 — Quad split / UV orientation flags
    // -------------------------------------------------------------------------

    /// <summary>
    ///     16 × 16 direction / UV-orientation flags (row-major, Z=row, X=col).
    ///     Length = 256. Observed values: 0, 1, 2, 3 only (2 bits used).
    ///     Bit semantics: bit 0x01 = s_flip, bit 0x02 = t_flip. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.7 Block 4 — "bit 0x01=s_flip, 0x02=t_flip: CONFIRMED."
    /// </summary>
    public required byte[] DirectionFlags { get; init; }

    // -------------------------------------------------------------------------
    // Block 5 — Per-vertex diffuse colour
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Per-vertex diffuse colour decoded from the on-disk ×0.5 encoding.
    ///     Length = 4 225. Channel order: R, G, B, A.
    ///     On-disk encoding: each byte = 2 × logical_value. Decode: <c>logical = byte × 0.5f</c>.
    ///     spec: Docs/RE/formats/terrain.md §5.8 Block 5 — "editor ×0.5 storage; loader ×0.5 decode: CONFIRMED."
    /// </summary>
    public required (float R, float G, float B, float A)[] DiffuseColours { get; init; }
}

/// <summary>
///     Entry from a <c>.lst</c> per-area cell manifest.
///     Encodes one valid <c>(mapX, mapZ)</c> coordinate pair.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §1.2 Per-area cell manifest — .lst
///     Key formula: key = mapZ + 100000 * mapX. CONFIRMED.
/// </remarks>
public readonly record struct LstCellEntry(uint Key, int MapX, int MapZ);

/// <summary>
///     Decoded result of a <c>.lst</c> per-area cell manifest.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §1.2 Per-area cell manifest — .lst
///     Binary layout: u32le count | count × u32le keys. CONFIRMED.
/// </remarks>
public sealed class LstManifest
{
    /// <summary>
    ///     All valid cell entries for this area, in on-disk order.
    ///     spec: Docs/RE/formats/terrain.md §1.2 — keys[]: CONFIRMED.
    /// </summary>
    public required LstCellEntry[] Entries { get; init; }
}

/// <summary>
///     One section from a <c>.map</c> text scene descriptor.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format).
/// </remarks>
public sealed class MapSection
{
    /// <summary>
    ///     Section keyword (e.g. "TERRAIN", "SOLID", "BUILDING", "FX1").
    ///     spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
    /// </summary>
    public required string Keyword { get; init; }

    /// <summary>
    ///     Path from the DATAFILE directive, or null if the section has no DATAFILE.
    ///     spec: Docs/RE/formats/terrain.md §3.2 DATAFILE directive: CONFIRMED.
    /// </summary>
    public string? DataFile { get; init; }

    /// <summary>
    ///     Texture entries from the TEXTURES block.
    ///     Each entry is (intFlag, intTexId); semantics of intFlag are UNVERIFIED.
    ///     spec: Docs/RE/formats/terrain.md §3.3 TEXTURES directive: CONFIRMED (structure).
    ///     Semantics of intFlag: UNVERIFIED.
    /// </summary>
    public required (int Flag, int TexId)[] Textures { get; init; }

    // ── Geometry directives (TERRAIN section) ──────────────────────────────
    // spec: Docs/RE/formats/terrain.md §3.4 Geometry directives (TERRAIN section).
    // All directives below are CONFIRMED.

    /// <summary>
    ///     Quad grid width (quads per row).
    ///     From the WIDTH directive in TERRAIN sections. Typically 64.
    ///     spec: Docs/RE/formats/terrain.md §3.4 — WIDTH integer: CONFIRMED.
    ///     Null when directive is absent (e.g. non-TERRAIN sections).
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    ///     Quad grid height (quads per column).
    ///     From the HEIGHT directive in TERRAIN sections. Typically 64.
    ///     spec: Docs/RE/formats/terrain.md §3.4 — HEIGHT integer: CONFIRMED.
    ///     Null when directive is absent.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    ///     World-unit spacing between adjacent vertices (quads per world unit).
    ///     From the GRID directive in TERRAIN sections. Typically 16.
    ///     spec: Docs/RE/formats/terrain.md §3.4 — GRID integer: CONFIRMED.
    ///     Null when directive is absent.
    /// </summary>
    public int? Grid { get; init; }

    /// <summary>
    ///     Maximum world-Y height value in this cell; informational only.
    ///     From the MAX_HEIGHTFILED directive (note: verbatim dropped-L spelling from original files).
    ///     spec: Docs/RE/formats/terrain.md §3.4 — MAX_HEIGHTFILED float: CONFIRMED.
    ///     Null when directive is absent.
    /// </summary>
    public float? MaxHeightFiled { get; init; }

    /// <summary>
    ///     Minimum world-Y height value in this cell; informational only.
    ///     From the MIN_HEIGHTFILED directive (note: verbatim dropped-L spelling from original files).
    ///     spec: Docs/RE/formats/terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
    ///     Null when directive is absent.
    /// </summary>
    public float? MinHeightFiled { get; init; }

    /// <summary>
    ///     World-space XZ origin of the cell quad.
    ///     From the ORIGIN directive: two comma-separated floats.
    ///     Equals ((mapX-10000)*1024, (mapZ-10000)*1024).
    ///     spec: Docs/RE/formats/terrain.md §3.4 — ORIGIN float,float: CONFIRMED.
    ///     Null when directive is absent.
    /// </summary>
    public (float X, float Z)? Origin { get; init; }
}

/// <summary>
///     Decoded result of a <c>.map</c> plain-text scene descriptor.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §3. The .map scene descriptor (text format).
/// </remarks>
public sealed class MapDescriptor
{
    /// <summary>
    ///     All sections found in this .map file, in parse order.
    ///     spec: Docs/RE/formats/terrain.md §3.1 Sections: CONFIRMED.
    /// </summary>
    public required MapSection[] Sections { get; init; }
}

/// <summary>
///     One tile record from a <c>.mud</c> ambient-sound tile blob.
///     Stride: 8 bytes. All fields are single bytes; no endianness dependency.
///     Bytes 0-1 (unread0/unread1) are never read by the runtime and are always zero.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mud.md §Tile record layout — 8-byte stride, all fields CONFIRMED.
///     Canonical field names from spec: unread0 @ +0, unread1 @ +1, bgmZoneId @ +2,
///     bgeAmbientId0 @ +3, bgeAmbientId1 @ +4, effId0 @ +5, effId1 @ +6, effId2 @ +7.
/// </remarks>
public readonly record struct MudTileRecord(
    /// <summary>Never read by runtime — always zero on disk. spec: Docs/RE/formats/mud.md — unread0 u8 @ +0.</summary>
    byte Pad0,
    /// <summary>Never read by runtime — always zero on disk. spec: Docs/RE/formats/mud.md — unread1 u8 @ +1.</summary>
    byte Pad1,
    /// <summary>BGM zone index. 0=no music. spec: Docs/RE/formats/mud.md — bgmZoneId u8 @ +2: CONFIRMED.</summary>
    byte MusicGroup,
    /// <summary>Ambient-loop sound index 0. 0=no sound. spec: Docs/RE/formats/mud.md — bgeAmbientId0 u8 @ +3: CONFIRMED.</summary>
    byte AmbientIdx0,
    /// <summary>Ambient-loop sound index 1. 0=no sound. spec: Docs/RE/formats/mud.md — bgeAmbientId1 u8 @ +4: CONFIRMED.</summary>
    byte AmbientIdx1,
    /// <summary>Effect sound index 0. 0=no sound. spec: Docs/RE/formats/mud.md — effId0 u8 @ +5: CONFIRMED.</summary>
    byte EffectIdx0,
    /// <summary>Effect sound index 1. 0=no sound. spec: Docs/RE/formats/mud.md — effId1 u8 @ +6: CONFIRMED.</summary>
    byte EffectIdx1,
    /// <summary>Effect sound index 2. Always zero in known samples. spec: Docs/RE/formats/mud.md — effId2 u8 @ +7: CONFIRMED.</summary>
    byte EffectIdx2
);

/// <summary>
///     Decoded result of a <c>.mud</c> ambient-sound tile blob.
///     Fixed size: 32 768 bytes (0x8000) = 64 × 64 × 8 B.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mud.md — canonical source for .mud format.
///     Total size CONFIRMED. Grid 64×64 CONFIRMED. Record stride 8 B CONFIRMED. All fields CONFIRMED.
///     tile_index formula: col + (row &lt;&lt; 6); col = floor(worldX/16) &amp; 0x3F; row = floor(worldZ/16) &amp; 0x3F.
///     spec: Docs/RE/formats/mud.md §Indexing — "tile_index = col + (row &lt;&lt; 6)": CONFIRMED.
/// </remarks>
public sealed class MudBlob
{
    /// <summary>
    ///     Grid width (columns, X axis). 64.
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — GridCols = 64: CONFIRMED.
    /// </summary>
    public const int GridCols = 64;

    /// <summary>
    ///     Grid height (rows, Z axis). 64.
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — GridRows = 64: CONFIRMED.
    /// </summary>
    public const int GridRows = 64;

    /// <summary>
    ///     Record stride in bytes. 8.
    ///     spec: Docs/RE/formats/mud.md §Tile record layout — RecordStride = 8: CONFIRMED.
    /// </summary>
    public const int RecordStride = 8;

    /// <summary>
    ///     Expected fixed size of a .mud file in bytes (64 × 64 × 8 = 32 768).
    ///     spec: Docs/RE/formats/mud.md §Fixed size — FixedSize = 32 768 bytes (0x8000): CONFIRMED.
    /// </summary>
    public const int FixedSize = GridRows * GridCols * RecordStride; // 32768

    /// <summary>
    ///     Decoded tile grid, row-major (row = Z axis, col = X axis).
    ///     Length = 4 096 (64 × 64). Index = col + (row &lt;&lt; 6).
    ///     spec: Docs/RE/formats/mud.md §Indexing — row-major Z=row, X=col: CONFIRMED.
    /// </summary>
    public required MudTileRecord[] Tiles { get; init; }
}

/// <summary>
///     Decoded <c>.sod</c> per-cell wall-collision segment blob.
///     Top-level solidCount (CONFIRMED), SolidRecord stride 108 B (CONFIRMED),
///     WallSegment (QuadRecord) stride 48 B (CONFIRMED).
///     BINARY-WON (CYCLE 7, anchor 263bd994): slope-intercept line z=m·x+b, NOT 4-corner ray-parity PIP.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sod.md — authoritative binary-won spec.
///     spec: Docs/RE/formats/sod.md §Container structure: CONFIRMED.
/// </remarks>
public sealed class SodBlob
{
    /// <summary>
    ///     Number of solid records in this file.
    ///     spec: Docs/RE/formats/sod.md §Container structure — "u32 solidCount": CONFIRMED.
    /// </summary>
    public required uint SolidCount { get; init; }

    /// <summary>
    ///     Decoded solid records. One per solid, each with a typed AABB and wall segments.
    ///     spec: Docs/RE/formats/sod.md §SolidRecord: CONFIRMED (stride + AABB fields).
    /// </summary>
    public required SolidRecord[] Solids { get; init; }

    // ── Backward-compat raw accessors (kept so existing Mapping/Godot consumers don't break) ──

    /// <summary>
    ///     Raw solid records, each exactly 108 bytes (backward compat — prefer <see cref="Solids" />).
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — stride 108 bytes (0x6C): CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte>[] RawSolidRecords { get; init; }

    /// <summary>
    ///     Per-solid wall-segment counts (backward compat — prefer <see cref="Solids" />).
    ///     spec: Docs/RE/formats/sod.md §Container structure — "u32 quadCount per solid (stream copy)": CONFIRMED.
    /// </summary>
    public required uint[] TriangleCounts { get; init; }

    /// <summary>
    ///     Per-solid raw wall-segment data; each segment is 48 bytes (backward compat — prefer <see cref="Solids" />).
    ///     spec: Docs/RE/formats/sod.md §QuadRecord — stride 48 bytes (0x30): CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte>[] RawTriangleData { get; init; }
}

/// <summary>
///     One solid record from a <c>.sod</c> file (108 bytes on disk).
///     Contains the solid's 2D XZ AABB and an array of decoded <see cref="WallSegment" /> records.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sod.md §SolidRecord — stride 108 bytes (0x6C): CONFIRMED.
///     AABB fields +0x00..+0x0F: parser + sample.
///     +0x10..+0x3B (44 bytes): zero on disk, runtime-only — ignored on read.
///     quadCount embedded @ +0x3C u32: redundant; stream word is authoritative.
///     quadArrayPtr @ +0x40 u32: on-disk garbage, overwritten at load — ignored on read.
///     +0x44..+0x6B (40 bytes): zero on disk, runtime use — ignored on read.
/// </remarks>
public sealed class SolidRecord
{
    // ── AABB +0x00..+0x0F (parser + sample) ─────────────────────────────────

    /// <summary>
    ///     AABB minimum X (world-space XZ plane).
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — aabbMinX f32 @ +0x00 (parser + sample).
    /// </summary>
    public required float AabbMinX { get; init; }

    /// <summary>
    ///     AABB minimum Z (world-space XZ plane).
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — aabbMinZ f32 @ +0x04 (parser + sample).
    /// </summary>
    public required float AabbMinZ { get; init; }

    /// <summary>
    ///     AABB maximum X (world-space XZ plane).
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — aabbMaxX f32 @ +0x08 (parser + sample).
    /// </summary>
    public required float AabbMaxX { get; init; }

    /// <summary>
    ///     AABB maximum Z (world-space XZ plane). Equals the union of all owned WallSegment AABBs.
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — aabbMaxZ f32 @ +0x0C (parser + sample).
    /// </summary>
    public required float AabbMaxZ { get; init; }

    // ── Decoded wall-segment array ────────────────────────────────────────────

    /// <summary>
    ///     Decoded wall segments (wall-collision lines) belonging to this solid.
    ///     spec: Docs/RE/formats/sod.md §QuadRecord: CONFIRMED (slope-intercept line, AABB, endpoints, axisFlag).
    /// </summary>
    public required WallSegment[] Segments { get; init; }

    // ── Raw 108-byte record ───────────────────────────────────────────────────

    /// <summary>
    ///     Full 108-byte raw record, including on-disk-zero and runtime-only regions.
    ///     spec: Docs/RE/formats/sod.md §SolidRecord — full stride layout: CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawRecord { get; init; }
}

/// <summary>
///     One wall-collision segment from a <c>.sod</c> file (48 bytes on disk).
///     Encodes a wall line in slope-intercept form: z = slope·x + intercept.
///     When axisFlag == 1 the wall is vertical/axis-aligned; use xConst instead.
///     Collision is line/segment intersection against this record's AABB, NOT ray-parity PIP.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sod.md §QuadRecord — stride 48 bytes (0x30): CONFIRMED.
///     BINARY-WON (CYCLE 7, anchor 263bd994): slope-intercept wall-segment, NOT four XZ corners.
///     AABB +0x00..+0x0F (parser + sample); endpoints +0x10..+0x1F (sample);
///     slope/xConst/intercept/axisFlag +0x20..+0x2F (parser + sample).
/// </remarks>
public sealed class WallSegment
{
    // ── 2D segment AABB +0x00..+0x0F (parser + sample) ──────────────────────

    /// <summary>Segment AABB minimum X. spec: Docs/RE/formats/sod.md §QuadRecord — aabbMinX f32 @ +0x00.</summary>
    public required float AabbMinX { get; init; }

    /// <summary>Segment AABB minimum Z. spec: Docs/RE/formats/sod.md §QuadRecord — aabbMinZ f32 @ +0x04.</summary>
    public required float AabbMinZ { get; init; }

    /// <summary>Segment AABB maximum X. spec: Docs/RE/formats/sod.md §QuadRecord — aabbMaxX f32 @ +0x08.</summary>
    public required float AabbMaxX { get; init; }

    /// <summary>Segment AABB maximum Z. spec: Docs/RE/formats/sod.md §QuadRecord — aabbMaxZ f32 @ +0x0C.</summary>
    public required float AabbMaxZ { get; init; }

    // ── Segment endpoints +0x10..+0x1F (sample) ──────────────────────────────

    /// <summary>Endpoint 0 X. spec: Docs/RE/formats/sod.md §QuadRecord — p0x f32 @ +0x10 (sample).</summary>
    public required float P0X { get; init; }

    /// <summary>Endpoint 0 Z. spec: Docs/RE/formats/sod.md §QuadRecord — p0z f32 @ +0x14 (sample).</summary>
    public required float P0Z { get; init; }

    /// <summary>Endpoint 1 X. spec: Docs/RE/formats/sod.md §QuadRecord — p1x f32 @ +0x18 (sample).</summary>
    public required float P1X { get; init; }

    /// <summary>Endpoint 1 Z. spec: Docs/RE/formats/sod.md §QuadRecord — p1z f32 @ +0x1C (sample).</summary>
    public required float P1Z { get; init; }

    // ── Slope-intercept line equation +0x20..+0x2F (parser + sample) ─────────

    /// <summary>
    ///     Slope m in z = m·x + b.
    ///     spec: Docs/RE/formats/sod.md §QuadRecord — slope f32 @ +0x20 (parser + sample).
    /// </summary>
    public required float Slope { get; init; }

    /// <summary>
    ///     X constant used when axisFlag == 1 (vertical/axis-aligned wall: x = XConst along Z axis).
    ///     spec: Docs/RE/formats/sod.md §QuadRecord — xConst f32 @ +0x24 (parser + sample).
    /// </summary>
    public required float XConst { get; init; }

    /// <summary>
    ///     Intercept b in z = m·x + b.
    ///     spec: Docs/RE/formats/sod.md §QuadRecord — intercept f32 @ +0x28 (parser + sample).
    /// </summary>
    public required float Intercept { get; init; }

    /// <summary>
    ///     Axis-alignment flag. Value == 1 means vertical wall (use XConst, not Slope/Intercept).
    ///     spec: Docs/RE/formats/sod.md §QuadRecord — axisFlag u32 @ +0x2C (parser + sample).
    /// </summary>
    public required uint AxisFlag { get; init; }
}

/// <summary>
///     Typed record for one tile from a <c>.mud</c> ambient-sound blob, exposing
///     the VERIFIED semantic field names.
///     This is an alias / view over <see cref="MudTileRecord" /> that promotes the
///     semantic names from the spec.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §6.2 Record layout (8 bytes, all VERIFIED).
/// </remarks>
public readonly record struct MudTile(
    /// <summary>
    /// Music BGM group index. 0=no music. 1-based into the per-area BGM sound table (48-byte entry stride).
    /// spec: Docs/RE/formats/terrain.md §6.2 — music_group u8 @ +2: VERIFIED.
    /// </summary>
    byte BgmIdx,
    /// <summary>
    /// Ambient-loop sound index 0 (first slot). 0=no sound. 1-based into the per-area BGE table.
    /// spec: Docs/RE/formats/terrain.md §6.2 — ambient_idx_0 u8 @ +3: VERIFIED.
    /// </summary>
    byte BgeIdx0,
    /// <summary>
    /// Ambient-loop sound index 1 (second slot). 0=no sound.
    /// spec: Docs/RE/formats/terrain.md §6.2 — ambient_idx_1 u8 @ +4: VERIFIED.
    /// </summary>
    byte BgeIdx1,
    /// <summary>
    /// Effect sound index 0 (first slot). 0=no sound. 1-based into the per-area EFF table.
    /// spec: Docs/RE/formats/terrain.md §6.2 — effect_idx_0 u8 @ +5: VERIFIED.
    /// </summary>
    byte EffIdx0,
    /// <summary>
    /// Effect sound index 1 (second slot). 0=no sound.
    /// spec: Docs/RE/formats/terrain.md §6.2 — effect_idx_1 u8 @ +6: VERIFIED.
    /// </summary>
    byte EffIdx1,
    /// <summary>
    /// Effect sound index 2 (third slot). 0 in all known samples. 0=no sound.
    /// spec: Docs/RE/formats/terrain.md §6.2 — effect_idx_2 u8 @ +7: VERIFIED (limited, always zero).
    /// </summary>
    byte EffIdx2
)
{
    /// <summary>
    ///     Constructs a <see cref="MudTile" /> from an existing <see cref="MudTileRecord" />,
    ///     extracting only the semantic sound-index fields (bytes 2–7).
    ///     spec: Docs/RE/formats/terrain.md §6.2 — bytes 0–1 always zero and never read: VERIFIED.
    /// </summary>
    public static MudTile FromRecord(MudTileRecord rec)
    {
        return new MudTile(rec.MusicGroup, rec.AmbientIdx0, rec.AmbientIdx1, rec.EffectIdx0, rec.EffectIdx1,
            rec.EffectIdx2);
    }
}