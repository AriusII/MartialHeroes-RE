namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public sealed class BudScene
{
    public required BudObject[] Objects { get; init; }
}

public sealed class BudObject
{
    public required byte TypeByte { get; init; }

    public required uint TexId { get; init; }

    public required BudVertex[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public readonly record struct BudVertex(
    float PosX,
    float PosY,
    float PosZ,
    float NormalX,
    float NormalY,
    float NormalZ,
    float UvU,
    float UvV);