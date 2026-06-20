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
///     Layout: POSITION12 (3 × f32) + DIFFUSE4 (1 × u32 padding / uninitialised) + TEXCOORD8 (2 × f32).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/effects.md §A.11 — "shared mesh table, 24-byte stride": CONFIRMED.
///     spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex (6 × f32):
///     pos_x @ +0, pos_y @ +4, pos_z @ +8, DIFFUSE/padding @ +12 (uninitialised/zero), tex_u @ +16, tex_v @ +20.
///     Normals are read from disk and discarded (not carried in memory).
///     V-flip applied to tex_v: in-memory tex_v = 1.0 − disk_tex_v.
///     spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory": CONFIRMED.
///     DIFFUSE4 field: byte layout is uninitialised by the loader (default zero). Retained here for faithful
///     24-byte stride representation consistent with what the shared mesh table stores.
///     spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
/// </remarks>
public readonly record struct XobjVertex(
    /// <summary>Position X. spec: mesh.md §In-memory vertex layout pos_x @ +0: CONFIRMED.</summary>
    float PosX,
    /// <summary>Position Y. spec: mesh.md §In-memory vertex layout pos_y @ +4: CONFIRMED.</summary>
    float PosY,
    /// <summary>Position Z. spec: mesh.md §In-memory vertex layout pos_z @ +8: CONFIRMED.</summary>
    float PosZ,
    /// <summary>
    /// DIFFUSE4 / padding dword at in-memory offset +12. Not written by the loader (stays zero).
    /// Exposed for faithful 24-byte stride representation.
    /// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
    /// </summary>
    uint Diffuse,
    /// <summary>
    /// Texture U coordinate. V-coordinate is already flipped (1.0 − disk value).
    /// spec: mesh.md §In-memory vertex layout tex_u @ +16: CONFIRMED.
    /// </summary>
    float TexU,
    /// <summary>
    /// Texture V coordinate (stored as 1.0 − disk_tex_v).
    /// spec: mesh.md §In-memory vertex layout tex_v @ +20 (1.0 − disk): CONFIRMED.
    /// </summary>
    float TexV);

/// <summary>
///     Decoded result of a <c>.xobj</c> ASCII mesh file for use as a mesh-particle emitter shape.
///     Feeds the shared mesh table indexed by a .xeff element's resource_id (&lt; 10000).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/effects.md §A.11 — .xobj files define emitter shapes for emitter_type == 1
///     via the shared mesh table (24-byte stride, indexed by resource_id &lt; 10000): CONFIRMED.
///     spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh: CONFIRMED (3 samples).
///     File is plain text (CRLF). No binary header, no magic.
///     Read order: slot_id (discard), face_count, face_count×3 indices, vertex_count, vertex_count×8-token vertices.
///     spec: Docs/RE/formats/mesh.md §Preamble + §Index list + §Vertex count + §Vertex data rows: CONFIRMED.
///     Normals are read from disk and discarded; not present in this model.
///     spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded; not kept in memory": CONFIRMED.
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