namespace MartialHeroes.Assets.Parsers.Mesh.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xobj ASCII primitive meshes (data/effect/xobj/)
//  spec: Docs/RE/formats/effects.md §A.11
//  spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh
//
//  These are plain-text CRLF files. They define emitter shapes for emitter_type == 1
//  (mesh-particle objects) via the shared mesh table indexed by resource_id (< 10000).
//  In-memory vertex layout: 24 bytes per vertex (POSITION12 + DIFFUSE4 + TEXCOORD8).
//  spec: Docs/RE/formats/effects.md §A.11 — "shared mesh table, 24-byte stride": CONFIRMED.
//  spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex: CONFIRMED.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One vertex in a <c>.xobj</c> mesh-particle shape, as laid out in the 24-byte shared mesh table.
///     Layout: POSITION12 (3 × f32) + DIFFUSE4 (u32, constructor-default 0xFF000000) + TEXCOORD8 (2 × f32).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xobj.md §Runtime Vertex (24 bytes) — parser-verified.
///     spec: Docs/RE/formats/xobj.md §Named constants — XOBJ_VERTEX_SIZE = 24, XOBJ_VERTEX_DEFAULT_DIFFUSE = 0xFF000000.
///     pos_x @ +0x00, pos_y @ +0x04, pos_z @ +0x08.
///     Diffuse @ +0x0C — NOT read from disk; set by the vertex constructor to opaque-default 0xFF000000
///     (ARGB: A=0xFF/opaque, R=G=B=0/black). Whether a later path overwrites it is UNVERIFIED.
///     tex_u @ +0x10 (stored as-is), tex_v @ +0x14 (stored as 1.0 − disk_v).
///     Normals are read from disk and discarded (not carried in memory).
///     spec: Docs/RE/formats/xobj.md §Per-vertex line — tokens 4/5/6 (n.x/y/z) — DISCARDED: parser-verified.
/// </remarks>
public readonly record struct XobjVertex(
    /// <summary>Position X. spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x00: parser-verified.</summary>
    float PosX,
    /// <summary>Position Y. spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x04: parser-verified.</summary>
    float PosY,
    /// <summary>Position Z. spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x08: parser-verified.</summary>
    float PosZ,
    /// <summary>
    ///     DIFFUSE4 dword at runtime offset +0x0C. NOT read from disk. Constructor-default = 0xFF000000
    ///     (ARGB: opaque black). Whether a later runtime path overwrites it is UNVERIFIED.
    ///     spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x0C — "Not read from file. Set by the vertex
    ///     constructor to opaque-default: 0xFF000000": parser-verified.
    ///     spec: Docs/RE/formats/xobj.md §Named constants — XOBJ_VERTEX_DEFAULT_DIFFUSE = 0xFF000000.
    /// </summary>
    uint Diffuse,
    /// <summary>
    ///     Texture U coordinate (stored as-is from disk).
    ///     spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x10 / §Per-vertex line token 7: parser-verified.
    /// </summary>
    float TexU,
    /// <summary>
    ///     Texture V coordinate (stored as 1.0 − disk_v — V-flip applied on load).
    ///     spec: Docs/RE/formats/xobj.md §Runtime Vertex +0x14 / §Per-vertex line token 8: parser-verified.
    /// </summary>
    float TexV);

/// <summary>
///     Decoded result of a <c>.xobj</c> ASCII mesh file for use as a mesh-particle emitter shape.
///     Feeds the shared mesh table indexed by a .xeff element's resource_id (&lt; 10000).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xobj.md §Part 2 — .xobj mesh body; §Part 3 — Runtime structures: parser-verified.
///     spec: Docs/RE/formats/xobj.md §Read algorithm — marker (discard), tri_count, indices (u16), vert_count,
///     per-vertex 8 tokens (pos XYZ, normals discarded, u, 1.0−v): parser-verified + sample-verified.
///     spec: Docs/RE/formats/xobj.md §Named constants — XOBJ_VERTEX_SIZE = 24, XOBJ_VERTEX_DEFAULT_DIFFUSE = 0xFF000000.
///     Normals are read from disk and discarded; not present in this model.
///     spec: Docs/RE/formats/xobj.md §Per-vertex line — "tokens 4/5/6: n.x/y/z — DISCARDED": parser-verified.
/// </remarks>
public sealed class XobjMeshData
{
    /// <summary>
    ///     Triangle index list (u16, 0-based). Length always divisible by 3.
    ///     spec: Docs/RE/formats/mesh.md §Index list — face_count × 3 indices: CONFIRMED.
    /// </summary>
    public required ushort[] Indices { get; init; }

    /// <summary>
    ///     Vertex buffer with the 24-byte per-vertex layout (POSITION12 + DIFFUSE4 + TEXCOORD8).
    ///     spec: Docs/RE/formats/effects.md §A.11 — shared mesh table 24-byte stride: CONFIRMED.
    ///     spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex: CONFIRMED.
    /// </summary>
    public required XobjVertex[] Vertices { get; init; }
}