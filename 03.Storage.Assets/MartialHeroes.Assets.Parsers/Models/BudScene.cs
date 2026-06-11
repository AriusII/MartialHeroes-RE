namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Decoded result of a <c>.bud</c> cell building blob.
/// Contains all static-object (building / prop) geometry for one map cell.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32 @ offset 0: CONFIRMED.
/// spec: Docs/RE/formats/terrain_scene.md §Per-object layout.
/// No magic bytes; file starts directly with objectCount.
/// </remarks>
public sealed class BudScene
{
    /// <summary>
    /// All static objects in this cell, in on-disk order.
    /// spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount: CONFIRMED.
    /// </summary>
    public required BudObject[] Objects { get; init; }
}

/// <summary>
/// One static object (building / prop) within a <c>.bud</c> cell.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_scene.md §Per-object layout — Object header (9 bytes): CONFIRMED.
/// spec: Docs/RE/formats/terrain_scene.md §Vertex array (32 bytes per vertex): CONFIRMED.
/// spec: Docs/RE/formats/terrain_scene.md §Index header + Index array (u16): CONFIRMED.
/// </remarks>
public sealed class BudObject
{
    /// <summary>
    /// Object sub-class discriminator.
    /// Only value 0 observed; full enumeration UNVERIFIED.
    /// spec: Docs/RE/formats/terrain_scene.md §Object header — type_byte u8 @ +0x00: PARTIAL.
    /// </summary>
    public required byte TypeByte { get; init; }

    /// <summary>
    /// 1-based index into the TEXTURES list of the enclosing BUILDING section.
    /// spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
    /// 1-based convention assumed (only value 1 observed).
    /// </summary>
    public required uint TexId { get; init; }

    /// <summary>
    /// Vertex array, each 32 bytes: pos XYZ (f32), normal XYZ (f32), uv UV (f32).
    /// spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
    /// Max 3072 vertices enforced at parse time.
    /// spec: Docs/RE/formats/terrain_scene.md §vertex_count — "Must be ≤ 3072": CONFIRMED.
    /// </summary>
    public required BudVertex[] Vertices { get; init; }

    /// <summary>
    /// Triangle list indices (0-based, u16). Always divisible by 3.
    /// spec: Docs/RE/formats/terrain_scene.md §Index array — u16 indices, triangle list: CONFIRMED.
    /// </summary>
    public required ushort[] Indices { get; init; }
}

/// <summary>
/// One vertex in a <c>.bud</c> static object mesh (32 bytes on disk).
/// All fields are IEEE 754 f32 little-endian.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes):
///   pos_x @ +0x00: CONFIRMED, pos_y @ +0x04: CONFIRMED, pos_z @ +0x08: CONFIRMED.
///   normal_x @ +0x0C: CONFIRMED, normal_y @ +0x10: CONFIRMED, normal_z @ +0x14: CONFIRMED.
///   uv_u @ +0x18: CONFIRMED, uv_v @ +0x1C: CONFIRMED.
/// </remarks>
public readonly record struct BudVertex(
    float PosX, float PosY, float PosZ,
    float NormalX, float NormalY, float NormalZ,
    float UvU, float UvV);
