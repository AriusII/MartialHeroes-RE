namespace MartialHeroes.Assets.Parsers.Texture.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  sky%d.box — Sky-dome geometry
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One 20-byte vertex from a sky-dome mesh in a <c>sky%d.box</c> file.
///     Position (x, y, z) + UV (u, v), each as f32 LE.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sky.md §A.4 Vertex decode (20-byte stride) — SAMPLE-UNVERIFIED (MED confidence).
///     The 12-byte position + 8-byte UV reading is the most plausible layout; an alternative packing
///     (12-byte position + 4-byte colour + 4-byte UV) also fits 20 bytes. Flag for runtime confirmation.
///     Coordinate note: world geometry negates Z when mapping to target engine (see project conventions);
///     this struct faithfully stores the source bytes — do NOT pre-bake the axis flip here.
///     spec: Docs/RE/formats/sky.md §A.4 — coordinate note.
/// </remarks>
public readonly record struct SkyBoxVertex(
    /// <summary>Position X component (f32 LE, world space).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.4 — position.x @ sub-offset 0x00 (f32): MED</remarks>
    float X,
    /// <summary>Position Y component (f32 LE, world space).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.4 — position.y @ sub-offset 0x04 (f32): MED</remarks>
    float Y,
    /// <summary>Position Z component (f32 LE, world space).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.4 — position.z @ sub-offset 0x08 (f32): MED</remarks>
    float Z,
    /// <summary>UV texture coordinate U (f32 LE).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.4 — uv.u @ sub-offset 0x0C (f32): MED</remarks>
    float U,
    /// <summary>UV texture coordinate V (f32 LE).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.4 — uv.v @ sub-offset 0x10 (f32): MED</remarks>
    float V);

/// <summary>
///     Geometry for one sky-dome mesh (one per skybox texture).
///     Contains a vertex array and an index array decoded from <c>sky%d.box</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sky.md §A.3 Per-mesh vertex array, §A.5 Per-mesh index array.
///     Vertex cap: 300 (0x12C). Index cap: 900 (0x384).
/// </remarks>
public sealed class SkyBoxMesh
{
    /// <summary>Maximum number of vertices per mesh (validation cap).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.3 — "Cap: 300 (0x12C)": HIGH</remarks>
    public const int MaxVertices = 300; // spec: Docs/RE/formats/sky.md §A.3

    /// <summary>Maximum number of indices per mesh (validation cap).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.5 — "Cap: 900 (0x384)": HIGH</remarks>
    public const int MaxIndices = 900; // spec: Docs/RE/formats/sky.md §A.5

    /// <summary>Vertex stride in bytes.</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.3 — "Vertex stride: 20 bytes": HIGH</remarks>
    public const int VertexStride = 20; // spec: Docs/RE/formats/sky.md §A.3

    /// <summary>Index width in bytes (u16).</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.5 — "Index width: 16-bit (u16)": HIGH</remarks>
    public const int IndexWidth = 2; // spec: Docs/RE/formats/sky.md §A.5

    /// <summary>
    ///     Decoded vertices, one per 20-byte stride.
    ///     Count is bounded by <see cref="MaxVertices" />.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.3, §A.4</remarks>
    public required SkyBoxVertex[] Vertices { get; init; }

    /// <summary>
    ///     Decoded u16 triangle indices.
    ///     Count is bounded by <see cref="MaxIndices" />.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.5 — u16 index array: HIGH</remarks>
    public required ushort[] Indices { get; init; }
}

/// <summary>
///     Decoded result of a <c>sky%d.box</c> sky-dome geometry file.
///     Contains a texture-name table and one mesh (vertices + indices) per texture slot.
///     No magic number, no version field — file identity comes from the VFS path.
///     Little-endian throughout.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sky.md §A — Section A sky%d.box (SAMPLE-UNVERIFIED — VFS-only).
///     spec: Docs/RE/formats/sky.md §A.1 Header — texture_count u32 @ 0x00: HIGH.
///     spec: Docs/RE/formats/sky.md §A.6 Overall structure — all vertex arrays precede all index arrays.
///     Logical path pattern: <c>data/sky/dat/sky{area_id}.box</c>.
///     Texture names expand to <c>data/sky/texture/{name}.dds</c>.
/// </remarks>
public sealed class SkyBoxData
{
    /// <summary>Maximum number of skybox textures / meshes (enforced during parse).</summary>
    /// <remarks>
    ///     No explicit cap is stated in the spec for texture_count; this parser applies a
    ///     conservative sanity limit. The per-mesh caps (300 vertices, 900 indices) are spec-stated.
    ///     spec: Docs/RE/formats/sky.md §A.3, §A.5.
    /// </remarks>
    public const int MaxTextures = 64; // conservative sanity cap; not in spec

    /// <summary>Fixed byte width of each texture-name record.</summary>
    /// <remarks>spec: Docs/RE/formats/sky.md §A.2 — "Record stride: 47 bytes": HIGH</remarks>
    public const int TextureNameStride = 47; // spec: Docs/RE/formats/sky.md §A.2

    /// <summary>
    ///     Texture names in file order.
    ///     Each name expands to <c>data/sky/texture/{name}.dds</c>.
    ///     Length equals the number of meshes in <see cref="Meshes" />.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sky.md §A.2 — texture_name char[47] per record: HIGH.
    ///     Null-terminated ASCII within the 47-byte field.
    /// </remarks>
    public required string[] TextureNames { get; init; }

    /// <summary>
    ///     Per-texture sky-dome meshes, in the same order as <see cref="TextureNames" />.
    ///     Each mesh has its own vertex and index array.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sky.md §A.3 Per-mesh vertex array,
    ///     §A.5 Per-mesh index array,
    ///     §A.6 Overall structure — all vertex arrays, then all index arrays.
    /// </remarks>
    public required SkyBoxMesh[] Meshes { get; init; }
}