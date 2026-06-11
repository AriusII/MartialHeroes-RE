namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One corner of a triangle in a <c>.skn</c> skinned mesh.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Face record — 36 bytes (3 corners × 12 bytes each)
/// </remarks>
public readonly record struct SknCorner(
    uint  VertexIndex,
    float UvU,
    /// <summary>
    /// V-flipped relative to the on-disk value: stored as <c>1.0f - uv_v_on_disk</c>.
    /// spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
    /// </summary>
    float UvV);

/// <summary>
/// One skin-weight influence record from a <c>.skn</c> skinned mesh.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Weight record — 12 bytes, little-endian
/// </remarks>
public readonly record struct SknWeight(
    uint  VertexIndex,
    uint  BoneIndex,
    float Weight);

/// <summary>
/// Neutral decoded result of a <c>.skn</c> binary skinned mesh.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh
/// <para>
/// Vertex positions are in the conventional order (position first, then normal) even though the
/// on-disk layout has normal first then position.  The parser re-orders the fields.
/// spec: Docs/RE/formats/mesh.md §Vertex record — "IMPORTANT: on-disk layout is normal first, then position"
/// </para>
/// </remarks>
public sealed class SkinnedMesh
{
    /// <summary>
    /// First numeric identifier for this mesh.
    /// spec: Docs/RE/formats/mesh.md §Header — id_a: CONFIRMED.
    /// </summary>
    public required uint IdA { get; init; }

    /// <summary>
    /// Bind-pose reference ID; used to associate this skin with a <c>.bnd</c> skeleton.
    /// spec: Docs/RE/formats/mesh.md §Header — id_b: CONFIRMED.
    /// </summary>
    public required uint IdB { get; init; }

    /// <summary>
    /// Mesh name from the length-prefixed header string.
    /// spec: Docs/RE/formats/mesh.md §Header — name: CONFIRMED (presence); UNVERIFIED (exact wire encoding).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Triangle face data: <c>FaceCount × 3</c> corners.
    /// spec: Docs/RE/formats/mesh.md §Face table.
    /// </summary>
    public required SknCorner[] Corners { get; init; }

    /// <summary>Number of triangles.</summary>
    public required uint FaceCount { get; init; }

    /// <summary>
    /// Vertex positions (re-ordered from on-disk: position first, normal second in the record).
    /// spec: Docs/RE/formats/mesh.md §Vertex record — "pos_x stored second on disk at sub-offset 12".
    /// </summary>
    public required Vec3[] Positions { get; init; }

    /// <summary>
    /// Vertex normals (re-ordered from on-disk: normal first on disk at sub-offset 0).
    /// spec: Docs/RE/formats/mesh.md §Vertex record — "normal_x stored first on disk at sub-offset 0".
    /// </summary>
    public required Vec3[] Normals { get; init; }

    /// <summary>
    /// Skin weight influences.
    /// spec: Docs/RE/formats/mesh.md §Weight / skin table.
    /// </summary>
    public required SknWeight[] Weights { get; init; }
}
