namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public readonly record struct CollisionTriangle(
    float V1X,
    float V1Y,
    float V1Z,
    float V2X,
    float V2Y,
    float V2Z,
    float V3X,
    float V3Y,
    float V3Z,
    float PlaneHeight);

public sealed class CollisionTriangleList
{
    public required CollisionTriangle[] Triangles { get; init; }
}

public sealed class SodPreCache
{
    public required uint Version { get; init; }
    public required uint VertexCount { get; init; }

    public required (float WorldX, float WorldZ)[] Vertices { get; init; }
}

public readonly record struct FxVertex36(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    byte R,
    byte G,
    byte B,
    byte A,
    float U0,
    float V0);

public readonly record struct FxVertex44(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    byte R,
    byte G,
    byte B,
    byte A,
    float U0,
    float V0,
    float U1,
    float V1);

public readonly record struct FxVertex32(
    float X,
    float Y,
    float Z,
    float NX,
    float NY,
    float NZ,
    float U0,
    float V0);

public class FxGroup
{
    public required uint TextureIndex1Based { get; init; }

    public required uint GroupFlags1 { get; init; }

    public required uint RenderState { get; init; }

    public required ReadOnlyMemory<byte> RawHeaderExtra { get; init; }
}

public sealed class Fx1Group : FxGroup
{
    public required FxVertex36[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx1Layer
{
    public required uint GroupCount { get; init; }

    public required Fx1Group[] Groups { get; init; }
}

public sealed class Fx2Group : FxGroup
{
    public required FxVertex44[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx2Layer
{
    public required uint GroupCount { get; init; }

    public required Fx2Group[] Groups { get; init; }
}

public sealed class Fx3Group : FxGroup
{
    public required FxVertex36[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx3Layer
{
    public required uint GroupCount { get; init; }

    public required Fx3Group[] Groups { get; init; }
}

public sealed class Fx4Tile
{
    public required ReadOnlyMemory<byte> RawTileHeader { get; init; }

    public required uint VertexCount { get; init; }

    public required uint IndexCount { get; init; }

    public required FxVertex44[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx4Layer
{
    public required uint TileCount { get; init; }

    public required Fx4Tile[] Tiles { get; init; }
}

public sealed class Fx5Section
{
    public required ReadOnlyMemory<byte> RawSectionHeader { get; init; }

    public required FxVertex36[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx5Layer
{
    public required Fx5Section[] Sections { get; init; }
}

public sealed class Fx7Group
{
    public required ReadOnlyMemory<byte> RawGroupHeader { get; init; }

    public required uint VertexCount { get; init; }

    public required uint IndexCount { get; init; }

    public required FxVertex32[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx7Layer
{
    public required uint GroupCount { get; init; }

    public required Fx7Group[] Groups { get; init; }
}

public sealed class Fx6Group
{
    public required uint TextureIndex1Based { get; init; }

    public required ReadOnlyMemory<byte> RawHeaderExtra { get; init; }

    public required FxVertex32[] Vertices { get; init; }

    public required ushort[] Indices { get; init; }
}

public sealed class Fx6Layer
{
    public required uint GroupCount { get; init; }

    public required Fx6Group[] Groups { get; init; }
}

public readonly record struct PointLightRecord(
    float ColorDiffuseR,
    float ColorDiffuseG,
    float ColorDiffuseB,
    float ColorBR,
    float ColorBG,
    float ColorBB,
    float ColorCR,
    float ColorCG,
    float ColorCB,
    float PositionX,
    float PositionY,
    float PositionZ,
    float Range,
    uint Unknown34,
    int TypeFlag);

public sealed class PointLightBinData
{
    public required float ProximityRadius { get; init; }

    public required PointLightRecord[] Records { get; init; }
}