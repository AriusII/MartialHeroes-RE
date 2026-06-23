namespace MartialHeroes.Assets.Parsers.Mesh.Models;

public readonly record struct XobjVertex(
    float PosX,
    float PosY,
    float PosZ,
    uint Diffuse,
    float TexU,
    float TexV);

public sealed class XobjMeshData
{
    public required ushort[] Indices { get; init; }

    public required XobjVertex[] Vertices { get; init; }
}