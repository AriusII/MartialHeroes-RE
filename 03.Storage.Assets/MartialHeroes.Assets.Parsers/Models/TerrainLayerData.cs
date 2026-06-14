namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .up / .exd  triangle collision files
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One collision triangle from an <c>.up</c> or <c>.exd</c> file.
/// Record stride: 40 bytes (10 × f32 LE).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §2.1 Triangle record (40 bytes): CONFIRMED.
/// spec: Docs/RE/formats/terrain.md §9.2 Triangle record — "40 bytes (10 × f32le)": CONFIRMED.
/// The last field (plane_height) equals vertex Y in all sampled flat triangles.
/// </remarks>
public readonly record struct CollisionTriangle(
    float V1X,
    float V1Y,
    float V1Z,
    float V2X,
    float V2Y,
    float V2Z,
    float V3X,
    float V3Y,
    float V3Z,
    /// <summary>
    /// Extra field at record offset +0x24.
    /// Equals vertex Y in all flat samples; behaviour for non-planar geometry UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §2.1 — plane_height @ +0x24: CONFIRMED (flat-triangle case).
    /// </summary>
    float PlaneHeight);

/// <summary>
/// Decoded result of a <c>.up</c> (upper terrain) or <c>.exd</c> (extra terrain) file.
/// Both formats use identical binary layouts.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §2.1 File layout: CONFIRMED (3 samples, exact size match).
/// spec: Docs/RE/formats/terrain_layers.md §3.1 File layout: CONFIRMED (.exd identical to .up).
/// File-size formula: 4 + triangle_count × 40.
/// </remarks>
public sealed class CollisionTriangleList
{
    /// <summary>
    /// All collision triangles decoded from the file.
    /// spec: Docs/RE/formats/terrain_layers.md §2.1 — triangle_count u32 @ offset 0: CONFIRMED.
    /// </summary>
    public required CollisionTriangle[] Triangles { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .sod.pre  collision polygon vertex cache
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of a <c>.sod.pre</c> collision polygon vertex cache.
/// Stores XZ world-space polygon corner points cached from the companion <c>.sod</c> file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §4.1 File layout: CONFIRMED (3 samples, exact size match).
/// File header: version u32 @ +0x00, vertex_count u32 @ +0x04 (CONFIRMED).
/// Vertex record: world_x f32 @ +0x00, world_z f32 @ +0x04 (CONFIRMED).
/// File-size formula: 8 + vertex_count × 8.
/// </remarks>
public sealed class SodPreCache
{
    /// <summary>
    /// Version field from header.
    /// Observed value: 1. Constant across all 3 samples.
    /// spec: Docs/RE/formats/terrain_layers.md §4.1 — version u32 @ +0x00: CONFIRMED.
    /// </summary>
    public required uint Version { get; init; }

    /// <summary>
    /// Polygon corner vertices in XZ world space, in on-disk order.
    /// spec: Docs/RE/formats/terrain_layers.md §4.1 — vertex_count, world_x, world_z: CONFIRMED.
    /// </summary>
    public required (float WorldX, float WorldZ)[] Vertices { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .fx1 – .fx7  terrain overlay mesh layers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Common vertex with colour (VF_36): 36 bytes on disk.
/// XYZ position (f32×3), XYZ normal (f32×3), RGBA colour (u8×4), UV0 (f32×2).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_36 (36 B): CONFIRMED.
/// RGBA order: R, G, B, A.
/// </remarks>
public readonly record struct FxVertex36(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    byte R,
    byte G,
    byte B,
    byte A,
    float U0,
    float V0);

/// <summary>
/// Extended vertex with two UV sets (VF_44): 44 bytes on disk.
/// Adds f32 U1, V1 to VF_36.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
/// </remarks>
public readonly record struct FxVertex44(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    byte R,
    byte G,
    byte B,
    byte A,
    float U0,
    float V0,
    float U1,
    float V1);

/// <summary>
/// Compact vertex without colour (VF_32): 32 bytes on disk.
/// XYZ position (f32×3), XYZ normal (f32×3), UV0 (f32×2). No colour field.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_32 (32 B): CONFIRMED (FX6 only).
/// </remarks>
public readonly record struct FxVertex32(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    float U0,
    float V0);

// ─── FX1 ───────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of an <c>.fx1</c> terrain overlay layer file.
/// Header: 24 bytes. Vertex format: VF_36. Single UV channel.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 Format: CONFIRMED (3 samples, exact size match).
/// Header fields at 0x00–0x14: type_tag, unknown_1, unknown_2, render_state, mesh_count, index_count.
/// render_state value 15 observed; semantic UNVERIFIED.
/// </remarks>
public sealed class Fx1Layer
{
    /// <summary>type_tag u32 @ 0x00. Observed: 1. spec: §1.5 CONFIRMED (constant=1).</summary>
    public required uint TypeTag { get; init; }

    /// <summary>unknown_1 u32 @ 0x04. Observed: 1. spec: §1.5 UNVERIFIED.</summary>
    public required uint Unknown1 { get; init; }

    /// <summary>unknown_2 u32 @ 0x08. Observed: 0. spec: §1.5 UNVERIFIED.</summary>
    public required uint Unknown2 { get; init; }

    /// <summary>render_state u32 @ 0x0C. Observed: 15. Semantic UNVERIFIED. spec: §1.5 UNVERIFIED.</summary>
    public required uint RenderState { get; init; }

    /// <summary>Vertex array (VF_36). spec: §1.5 CONFIRMED.</summary>
    public required FxVertex36[] Vertices { get; init; }

    /// <summary>Index array (u16). spec: §1.5 CONFIRMED.</summary>
    public required ushort[] Indices { get; init; }
}

// ─── FX2 ───────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of an <c>.fx2</c> terrain overlay layer file.
/// Header: 24 bytes (same layout as FX1). Vertex format: VF_44 (dual UV).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.6 FX2 Format: CONFIRMED (3 samples, exact size match).
/// </remarks>
public sealed class Fx2Layer
{
    /// <summary>type_tag u32 @ 0x00. spec: §1.6 CONFIRMED.</summary>
    public required uint TypeTag { get; init; }

    /// <summary>unknown_1 u32 @ 0x04. spec: §1.6 UNVERIFIED.</summary>
    public required uint Unknown1 { get; init; }

    /// <summary>unknown_2 u32 @ 0x08. spec: §1.6 UNVERIFIED.</summary>
    public required uint Unknown2 { get; init; }

    /// <summary>render_state u32 @ 0x0C. Observed: 15. Semantic UNVERIFIED. spec: §1.6 UNVERIFIED.</summary>
    public required uint RenderState { get; init; }

    /// <summary>Vertex array (VF_44). spec: §1.6 CONFIRMED.</summary>
    public required FxVertex44[] Vertices { get; init; }

    /// <summary>Index array (u16). spec: §1.6 CONFIRMED.</summary>
    public required ushort[] Indices { get; init; }
}

// ─── FX3 ───────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of an <c>.fx3</c> terrain overlay layer file.
/// Header: 48 bytes (extended vs FX1/FX2). Vertex format: VF_36.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 Format: CONFIRMED (3 samples, exact size match).
/// Extra header fields at 0x10–0x27 are all constant and UNVERIFIED.
/// </remarks>
public sealed class Fx3Layer
{
    /// <summary>type_tag u32 @ 0x00. CONFIRMED. spec: §1.7.</summary>
    public required uint TypeTag { get; init; }

    /// <summary>unknown_1 u32 @ 0x04. UNVERIFIED. spec: §1.7.</summary>
    public required uint Unknown1 { get; init; }

    /// <summary>unknown_2 u32 @ 0x08. UNVERIFIED. spec: §1.7.</summary>
    public required uint Unknown2 { get; init; }

    /// <summary>render_state u32 @ 0x0C. Observed: 5. Semantic UNVERIFIED. spec: §1.7.</summary>
    public required uint RenderState { get; init; }

    /// <summary>
    /// Raw bytes of the extended header region 0x10–0x27 (32 bytes, 8 u32/f32 fields).
    /// All constant in samples; semantics UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.7 — unknown_3..unknown_8: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawHeaderExtra { get; init; }

    /// <summary>Vertex array (VF_36). spec: §1.7 CONFIRMED.</summary>
    public required FxVertex36[] Vertices { get; init; }

    /// <summary>Index array (u16). spec: §1.7 CONFIRMED.</summary>
    public required ushort[] Indices { get; init; }
}

// ─── FX4 ───────────────────────────────────────────────────────────────────

/// <summary>
/// One tile within an <c>.fx4</c> terrain overlay layer file.
/// Each tile carries a fixed 48-byte header, a VF_44 vertex block, and a u16 index block.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.11 FX4 Format: CONFIRMED-FROM-LOADER.
/// File layout: u32 tile_count, then per tile: TileHeader(48 B) + VertexData + IndexData.
/// vertex_count @ tile-relative +0x28: CONFIRMED (parser-verified).
/// index_count  @ tile-relative +0x2C: CONFIRMED (parser-verified).
/// VF_44 vertex format (44 B stride): CONFIRMED (parser-verified).
/// The leading 40 bytes of the tile header (tile_metadata) are read-but-not-consumed: UNVERIFIED semantics.
/// spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_metadata @ +0x00 (40 bytes): UNVERIFIED.
/// </remarks>
public sealed class Fx4Tile
{
    /// <summary>
    /// Raw 48-byte tile header. Only bytes at +0x28 (vertex_count) and +0x2C (index_count) are consumed.
    /// The leading 40 bytes (tile_metadata) are preserved for faithful byte-level storage.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — per-tile header (48 bytes): CONFIRMED-FROM-LOADER.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_metadata @ +0x00 (40 bytes): UNVERIFIED (read-but-not-consumed).
    /// </summary>
    public required ReadOnlyMemory<byte> RawTileHeader { get; init; }

    /// <summary>
    /// Vertex count (u32 LE @ tile-relative +0x28). Drives the vertex_count × 44 VF_44 read.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — vertex_count u32 @ +0x28: CONFIRMED (parser-verified).
    /// </summary>
    public required uint VertexCount { get; init; }

    /// <summary>
    /// Index count (u32 LE @ tile-relative +0x2C). Drives the index_count × 2 u16 read.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — index_count u32 @ +0x2C: CONFIRMED (parser-verified).
    /// </summary>
    public required uint IndexCount { get; init; }

    /// <summary>
    /// Vertex buffer (VF_44). Leading position float3 (X, Y, Z) is parser-verified via AABB compute.
    /// Remaining 32 bytes (normal + RGBA + UV0 + UV1) match the §1.2 VF_44 description.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — VertexData (vertex_count × 44, VF_44): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
    /// </summary>
    public required FxVertex44[] Vertices { get; init; }

    /// <summary>
    /// Index buffer (u16, plain triangle list).
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — IndexData (index_count × 2, u16): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.3 Index format — u16 triangle list: CONFIRMED.
    /// </summary>
    public required ushort[] Indices { get; init; }
}

/// <summary>
/// Decoded result of an <c>.fx4</c> terrain overlay layer file.
/// Flat tile array: u32 tile_count, then tile_count tiles (each 48-byte header + VF_44 vertices + u16 indices).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.11 FX4 Format: CONFIRMED-FROM-LOADER.
/// File layout: u32 tileCount at 0x00, then per tile: TileHeader(48 B) + VertexData(vertex_count×44) + IndexData(index_count×2).
/// File-size formula: 4 + Σ over tiles (48 + vertex_count × 44 + index_count × 2).
/// Cross-confirmed by FX5 loader which uses the same control flow (only stride differs: VF_36 vs VF_44).
/// spec: Docs/RE/formats/terrain_layers.md §1.11 — "FX4 and FX5 differ only in vertex stride": CONFIRMED.
/// Vertex format: VF_44 (44 B) — same as FX2, not FX1/FX3/FX5 which use VF_36 (36 B).
/// spec: Docs/RE/formats/terrain_layers.md §1.12 FX layer summary table: CONFIRMED.
/// </remarks>
public sealed class Fx4Layer
{
    /// <summary>
    /// tile_count u32 LE @ file offset 0x00. Drives the per-tile read loop.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_count u32 @ 0x00: CONFIRMED (parser-verified).
    /// </summary>
    public required uint TileCount { get; init; }

    /// <summary>
    /// Tiles decoded from the file in on-disk order. Length == TileCount.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — per-tile loop: CONFIRMED.
    /// </summary>
    public required Fx4Tile[] Tiles { get; init; }
}

// ─── FX5 ───────────────────────────────────────────────────────────────────

/// <summary>
/// One section within an <c>.fx5</c> file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Format: CONFIRMED (single-section).
/// Multi-section boundary UNVERIFIED for sections beyond section 0.
/// Section_Header: 40 bytes; SubChunk_Header: 12 bytes (section 0 confirmed).
/// </remarks>
public sealed class Fx5Section
{
    /// <summary>
    /// Raw 40-byte section header. All fields UNVERIFIED except direction_x/y/z (f32).
    /// spec: Docs/RE/formats/terrain_layers.md §1.8 Section_Header (40 bytes): UNVERIFIED (semantic).
    /// </summary>
    public required ReadOnlyMemory<byte> RawSectionHeader { get; init; }

    /// <summary>
    /// Raw 12-byte sub-chunk header.
    /// Confirmed for section 0; layout UNVERIFIED for sections > 0.
    /// spec: Docs/RE/formats/terrain_layers.md §1.8 SubChunk_Header (12 bytes): CONFIRMED (section 0).
    /// </summary>
    public required ReadOnlyMemory<byte> RawSubChunkHeader { get; init; }

    /// <summary>Vertex array (VF_36). spec: §1.8 CONFIRMED.</summary>
    public required FxVertex36[] Vertices { get; init; }

    /// <summary>Index array (u16). spec: §1.8 CONFIRMED.</summary>
    public required ushort[] Indices { get; init; }
}

/// <summary>
/// Decoded result of an <c>.fx5</c> terrain overlay layer file.
/// Variable number of sections; total section count derived from accumulated sizes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Format: CONFIRMED (single-section);
/// multi-section boundary UNVERIFIED (Known Unknown #1).
/// </remarks>
public sealed class Fx5Layer
{
    /// <summary>Sections in on-disk order. spec: §1.8 CONFIRMED.</summary>
    public required Fx5Section[] Sections { get; init; }
}

// ─── FX6 ───────────────────────────────────────────────────────────────────

/// <summary>
/// One sub-chunk within an <c>.fx6</c> file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.9 FX6 Format: CONFIRMED (3 samples, exact size match).
/// SubChunk_Header: 8 bytes. Vertex format: VF_32 (32 B). Footer: 28 bytes (non-final sub-chunks only).
/// </remarks>
public sealed class Fx6SubChunk
{
    /// <summary>Vertex array (VF_32). spec: §1.9 CONFIRMED.</summary>
    public required FxVertex32[] Vertices { get; init; }

    /// <summary>Index array (u16). spec: §1.9 CONFIRMED.</summary>
    public required ushort[] Indices { get; init; }

    /// <summary>
    /// Raw footer bytes (28 bytes), or empty for the final sub-chunk.
    /// All footer fields UNVERIFIED. spec: §1.9 Footer (28 bytes): UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawFooter { get; init; }
}

/// <summary>
/// Decoded result of an <c>.fx6</c> terrain overlay layer file.
/// Global header: 32 bytes. Contains sub_chunk_count sub-chunks.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §1.9 FX6 Format: CONFIRMED (3 samples, 29 444 bytes each).
/// File-size formula: 32 + (sub_chunk_count - 1) × 736 + 708.
/// </remarks>
public sealed class Fx6Layer
{
    /// <summary>
    /// sub_chunk_count u32 @ GlobalHeader+0x00. Observed: 40. spec: §1.9 CONFIRMED.
    /// </summary>
    public required uint SubChunkCount { get; init; }

    /// <summary>
    /// Raw global header bytes 0x04–0x1F (28 bytes). All fields UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §1.9 GlobalHeader remaining fields: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawGlobalHeaderRest { get; init; }

    /// <summary>Sub-chunks in on-disk order. spec: §1.9 CONFIRMED.</summary>
    public required Fx6SubChunk[] SubChunks { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  light*.bin / point_light*.bin / wind*.bin  sky-lighting blobs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One directional-light or ambient-light keyframe slot from <c>light%d.bin</c>.
/// 48 bytes (12 × f32).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §6.2 Section A keyframe slot (48 bytes).
/// Fields sun_colour[0..2] CONFIRMED; others UNVERIFIED.
/// </remarks>
public sealed class LightKeyframe
{
    /// <summary>sun_colour[0] f32 @ slot+0x00. CONFIRMED.</summary>
    public required float SunColour0 { get; init; }

    /// <summary>sun_colour[1] f32 @ slot+0x04. CONFIRMED.</summary>
    public required float SunColour1 { get; init; }

    /// <summary>sun_colour[2] f32 @ slot+0x08. CONFIRMED.</summary>
    public required float SunColour2 { get; init; }

    /// <summary>Remaining 9 floats (offsets +0x0C..+0x2C) — raw bytes. Partially UNVERIFIED.</summary>
    public required ReadOnlyMemory<byte> RawRest { get; init; }
}

/// <summary>
/// Decoded result of a <c>light%d.bin</c> sky-lighting keyframe file.
/// Fixed size: 5312 bytes. No magic, no version.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §6.1 Blob layout: CONFIRMED (parser-analysis only; no samples).
/// 48 directional slots + 48 ambient slots + 48 fog scalars.
/// </remarks>
public sealed class LightBinData
{
    /// <summary>
    /// 48 directional-light keyframe slots.
    /// spec: Docs/RE/formats/terrain_layers.md §6.1 Section A (0x0000–0x08FF): CONFIRMED (parser).
    /// </summary>
    public required LightKeyframe[] DirectionalKeyframes { get; init; }

    /// <summary>
    /// 48 ambient-light keyframe slots.
    /// spec: Docs/RE/formats/terrain_layers.md §6.1 Section B (0x0930–0x122F): CONFIRMED (parser).
    /// </summary>
    public required LightKeyframe[] AmbientKeyframes { get; init; }

    /// <summary>
    /// 48 fog density scalars (f32 each).
    /// Value 1.0 is the no-override sentinel.
    /// spec: Docs/RE/formats/terrain_layers.md §6.4 Section C (0x1260): MEDIUM confidence.
    /// </summary>
    public required float[] FogDensity { get; init; }

    /// <summary>
    /// Trailing 416 bytes (0x1320–0x14BF). Purpose UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §6.1 trailing region: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawTrailing { get; init; }
}

/// <summary>
/// One point-light record from <c>point_light%d.bin</c>.
/// Record stride: 60 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §7.2 Point-light record (60 bytes): CONFIRMED (parser-analysis).
/// colour_group_1..3 (9 × f32) CONFIRMED; positions and other fields UNVERIFIED.
/// </remarks>
public sealed class PointLightRecord
{
    /// <summary>colour_group_1[0..2] f32×3 @ +0x00. CONFIRMED.</summary>
    public required float[] ColourGroup1 { get; init; } // length 3

    /// <summary>colour_group_2[0..2] f32×3 @ +0x0C. CONFIRMED.</summary>
    public required float[] ColourGroup2 { get; init; } // length 3

    /// <summary>colour_group_3[0..2] f32×3 @ +0x18. CONFIRMED.</summary>
    public required float[] ColourGroup3 { get; init; } // length 3

    /// <summary>
    /// Raw remaining bytes at +0x24..+0x38 (offsets 36–56, 21 bytes — positions+range+enabled).
    /// Positions and range UNVERIFIED.
    /// spec: §7.2 — unknown_0..unknown_3 UNVERIFIED; enabled_flag CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawRest { get; init; }

    /// <summary>
    /// enabled_flag u32 @ +0x34. 0 = active; non-zero = skip.
    /// spec: Docs/RE/formats/terrain_layers.md §7.2 — enabled_flag: CONFIRMED.
    /// </summary>
    public required uint EnabledFlag { get; init; }

    /// <summary>
    /// unknown_4 u32 @ +0x38. UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_layers.md §7.2 — unknown_4: UNVERIFIED.
    /// </summary>
    public required uint Unknown4 { get; init; }
}

/// <summary>
/// Decoded result of a <c>point_light%d.bin</c> point-light array file.
/// File size: 8 + count × 60.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §7.1 File header + §7.2 Point-light record: CONFIRMED (parser-analysis).
/// No sample bytes available; parser-analysis only.
/// </remarks>
public sealed class PointLightBinData
{
    /// <summary>
    /// intensity_scale u32 @ +0x00. Global colour multiplier.
    /// spec: Docs/RE/formats/terrain_layers.md §7.1 — intensity_scale: CONFIRMED.
    /// </summary>
    public required uint IntensityScale { get; init; }

    /// <summary>Point-light records. spec: §7.1 — count: CONFIRMED.</summary>
    public required PointLightRecord[] Records { get; init; }
}

/// <summary>
/// Decoded result of a <c>wind%d.bin</c> foliage-sway keyframe file.
/// File size: 8 + count × 24.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md §8.1 File header (8 bytes): CONFIRMED (3 zero-entry samples).
/// spec: Docs/RE/formats/terrain_layers.md §8.2 Wind keyframe record (24 bytes): UNVERIFIED (no non-zero samples).
/// </remarks>
public sealed class WindBinData
{
    /// <summary>count u32 @ +0x00. CONFIRMED.</summary>
    public required uint Count { get; init; }

    /// <summary>flag2 u32 @ +0x04. Non-zero enables foliage-sway seeding. CONFIRMED.</summary>
    public required uint Flag2 { get; init; }

    /// <summary>
    /// Wind keyframe records (24 bytes each). Fields 0–4 UNVERIFIED; sway_seed at +0x14 MEDIUM.
    /// spec: Docs/RE/formats/terrain_layers.md §8.2 — sway_seed @ +0x14: MEDIUM.
    /// </summary>
    public required ReadOnlyMemory<byte>[] RawKeyframes { get; init; }
}