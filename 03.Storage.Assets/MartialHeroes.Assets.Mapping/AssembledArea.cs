using MartialHeroes.Assets.Parsers.Audio.Models;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

public sealed class AssembledCell
{
    public required int MapX { get; init; }
    public required int MapZ { get; init; }
    public long CellKey => MapZ + 100000L * MapX;
    public TerrainCell? Slot0GroundTexGrid { get; init; }
    public BudScene? Slot1BuildingObjectGrid { get; init; }
    public Fx1Layer? Slot2Fx1 { get; init; }
    public Fx2Layer? Slot3Fx2 { get; init; }
    public Fx3Layer? Slot4Fx3 { get; init; }
    public Fx4Layer? Slot5Fx4 { get; init; }
    public Fx5Layer? Slot6Fx5 { get; init; }
    public Fx6Layer? Slot7Fx6 { get; init; }
    public Fx7Layer? Slot8Fx7 { get; init; }
    public string?[]? ResolvedTexturePaths { get; init; }
    public string?[]? ResolvedBuildingTexturePaths { get; init; }
    public SodBlob? Collision { get; init; }
    public CollisionTriangleList? ExtraTerrainTriangles { get; init; }
    public CollisionTriangleList? OverhangTriangles { get; init; }
    public MudSoundGrid? SoundGrid { get; init; }

    public float? SampleHeight(float localX, float localZ)
    {
        if (Slot0GroundTexGrid is null)
            return null;

        return Slot0GroundTexGrid.SampleGroundHeight(localX, localZ);
    }
}

public sealed class AssembledArea
{
    public required int AreaId { get; init; }

    public required IReadOnlyList<(int MapX, int MapZ)> CellKeys { get; init; }

    public required IReadOnlyList<SpawnDescriptor> Spawns { get; init; }

    public required IReadOnlyDictionary<(int MapX, int MapZ), AssembledCell> Cells { get; init; }
}

public readonly record struct SpawnDescriptor
{
    public float WorldX { get; init; }

    public float WorldZ { get; init; }

    public float Yaw { get; init; }

    public int VisualId { get; init; }

    public bool IsNpc { get; init; }
}