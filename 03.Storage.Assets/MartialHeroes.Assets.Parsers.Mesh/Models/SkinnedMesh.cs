using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Mesh.Models;

/// <summary>
///     One corner of a triangle in a <c>.skn</c> skinned mesh.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mesh.md §Face record — 36 bytes (3 corners × 12 bytes each)
/// </remarks>
public readonly record struct SknCorner(
    uint VertexIndex,
    float UvU,
    /// <summary>
    /// V-flipped relative to the on-disk value: stored as <c>1.0f - uv_v_on_disk</c>.
    /// spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
    /// </summary>
    float UvV);

/// <summary>
///     One skin-weight influence record from a <c>.skn</c> skinned mesh.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/skn.md §Influence (weight) section — 12-byte record: CONFIRMED.
///     spec: Docs/RE/formats/mesh.md §Weight record — 12 bytes, little-endian: CONFIRMED.
///     <para>
///         <b>+0x00</b> <c>VertexIndex</c> u32 — plain vertex index (binary-won: NOT a position key / float-bit compare).
///         spec: Docs/RE/formats/skn.md §Influence record +0x00 — "plain u32 vertex index": CONFIRMED (sample-verified).
///     </para>
///     <para>
///         <b>+0x04</b> <c>BoneIndex</c> u32 — on-disk field name is <c>bone_id</c> in the spec.
///         Despite the C# member name "BoneIndex", this value is a <b>bone ID</b>, not a dense
///         array index: it is resolved base-relative as <c>bone_array[BoneIndex − base_id]</c>
///         against the bound skeleton. For the recovered player skeletons <c>base_id == 0</c>,
///         so ID equals array index — but parsers must not assume <c>base_id == 0</c> in general.
///         spec: Docs/RE/formats/skn.md §Influence record +0x04 — "bone_id, resolved base-relative": CONFIRMED.
///         spec: Docs/RE/formats/mesh.md §Bone addressing — "a bone ID resolved by id − base_id, no indirection table":
///         CONFIRMED.
///     </para>
///     <para>
///         <b>+0x08</b> <c>Weight</c> f32 — records below 0.01 are dropped; survivors normalized per-vertex to 1.0.
///         spec: Docs/RE/formats/skn.md §Per-vertex influence packing — "drop threshold 0.01, normalize": CONFIRMED.
///     </para>
/// </remarks>
public readonly record struct SknWeight(
    /// <summary>
    ///     Zero-based index of the vertex this influence applies to.
    ///     A plain <c>u32</c> vertex index (binary-won: not a position key / float-bit compare).
    ///     spec: Docs/RE/formats/skn.md §Influence record +0x00 — "plain u32 vertex index": CONFIRMED (sample-verified).
    /// </summary>
    uint VertexIndex,
    /// <summary>
    ///     Bone identifier (on-disk name: <c>bone_id</c>). Resolved base-relative against the bound skeleton:
    ///     <c>bone_array[BoneIndex − base_id]</c>. NOT a dense 0-based array subscript.
    ///     spec: Docs/RE/formats/skn.md §Influence record +0x04 — "bone_id, resolved base-relative": CONFIRMED.
    ///     spec: Docs/RE/formats/mesh.md §Bone addressing — "bone_index is a bone ID resolved by id − base_id, no indirection table": CONFIRMED.
    /// </summary>
    uint BoneIndex,
    /// <summary>
    ///     Per-influence blend weight. Records with <c>weight &lt; 0.01</c> are dropped by the
    ///     character loader; survivors are normalised per-vertex to sum to 1.0.
    ///     spec: Docs/RE/formats/skn.md §Per-vertex influence packing — "drop threshold 0.01, normalize": CONFIRMED.
    /// </summary>
    float Weight);

/// <summary>
///     Neutral decoded result of a <c>.skn</c> binary skinned mesh.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh
///     <para>
///         Vertex positions are in the conventional order (position first, then normal) even though the
///         on-disk layout has normal first then position.  The parser re-orders the fields.
///         spec: Docs/RE/formats/mesh.md §Vertex record — "IMPORTANT: on-disk layout is normal first, then position"
///     </para>
/// </remarks>
public sealed class SkinnedMesh
{
    /// <summary>
    ///     First numeric identifier for this mesh.
    ///     spec: Docs/RE/formats/mesh.md §Header — id_a: CONFIRMED.
    /// </summary>
    public required uint IdA { get; init; }

    /// <summary>
    ///     Bind-pose reference ID; used to associate this skin with a <c>.bnd</c> skeleton.
    ///     spec: Docs/RE/formats/mesh.md §Header — id_b: CONFIRMED.
    /// </summary>
    public required uint IdB { get; init; }

    /// <summary>
    ///     Mesh name from the length-prefixed header string.
    ///     spec: Docs/RE/formats/mesh.md §Header — name: CONFIRMED (presence); UNVERIFIED (exact wire encoding).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Triangle face data: <c>FaceCount × 3</c> corners.
    ///     spec: Docs/RE/formats/mesh.md §Face table.
    /// </summary>
    public required SknCorner[] Corners { get; init; }

    /// <summary>Number of triangles.</summary>
    public required uint FaceCount { get; init; }

    /// <summary>
    ///     Vertex positions (re-ordered from on-disk: position first, normal second in the record).
    ///     spec: Docs/RE/formats/mesh.md §Vertex record — "pos_x stored second on disk at sub-offset 12".
    /// </summary>
    public required Vec3[] Positions { get; init; }

    /// <summary>
    ///     Vertex normals (re-ordered from on-disk: normal first on disk at sub-offset 0).
    ///     spec: Docs/RE/formats/mesh.md §Vertex record — "normal_x stored first on disk at sub-offset 0".
    /// </summary>
    public required Vec3[] Normals { get; init; }

    /// <summary>
    ///     Skin weight influences.
    ///     spec: Docs/RE/formats/mesh.md §Weight / skin table.
    /// </summary>
    public required SknWeight[] Weights { get; init; }
}