using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Mesh.Models;

public readonly record struct SknCorner(
    uint VertexIndex,
    float UvU,
    float UvV);

public readonly record struct SknWeight(
    uint VertexIndex,
    uint BoneIndex,
    float Weight);

public sealed class SkinnedMesh
{
    public required uint IdA { get; init; }

    public required uint IdB { get; init; }

    public required string Name { get; init; }

    public required SknCorner[] Corners { get; init; }

    public required uint FaceCount { get; init; }

    public required Vec3[] Positions { get; init; }

    public required Vec3[] Normals { get; init; }

    public required SknWeight[] Weights { get; init; }
}