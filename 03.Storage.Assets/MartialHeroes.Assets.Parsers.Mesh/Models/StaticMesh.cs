using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Mesh.Models;

public sealed class StaticMesh
{
    public required Vec3[] Positions { get; init; }

    public required Vec2[] Uvs { get; init; }

    public required ushort[] Indices { get; init; }
}