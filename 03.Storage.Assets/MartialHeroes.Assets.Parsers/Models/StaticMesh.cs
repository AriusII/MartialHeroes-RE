namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Neutral decoded result of a <c>.xobj</c> ASCII static mesh.
/// Normals are NOT carried here: they are discarded per spec.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh
/// <para>
/// <see cref="Positions"/> and <see cref="Uvs"/> are parallel arrays: element <c>i</c>
/// belongs to vertex <c>i</c>.
/// UV V-coordinate is already flipped: stored as <c>1.0f - v_on_disk</c>.
/// spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory"
/// </para>
/// </remarks>
public sealed class StaticMesh
{
    /// <summary>Vertex positions.</summary>
    public required Vec3[] Positions { get; init; }

    /// <summary>
    /// UV coordinates, V-flipped relative to the on-disk value.
    /// spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: CONFIRMED.
    /// </summary>
    public required Vec2[] Uvs { get; init; }

    /// <summary>
    /// Triangle index list as u16 values (truncated from on-disk u32).
    /// Length is always a multiple of 3 (num_triangles × 3).
    /// spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
    /// </summary>
    public required ushort[] Indices { get; init; }
}