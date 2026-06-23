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


    public MudSoundGrid? SoundGrid { get; init; }


    public float? SampleHeight(float localX, float localZ)
    {
        if (Slot0GroundTexGrid is null)
            return null;

        const float spacing = 16f;

        var cx = Math.Clamp(localX, 0f, 64f * spacing);
        var cz = Math.Clamp(localZ, 0f, 64f * spacing);

        var gx = cx / spacing;
        var gz = cz / spacing;

        var ix = (int)gx;
        var iz = (int)gz;

        ix = Math.Clamp(ix, 0, 63);
        iz = Math.Clamp(iz, 0, 63);

        var fx = gx - ix;
        var fz = gz - iz;

        var h = Slot0GroundTexGrid.Heights;
        var h00 = h[iz * TerrainCell.GridSize + ix];
        var h10 = h[iz * TerrainCell.GridSize + ix + 1];
        var h01 = h[(iz + 1) * TerrainCell.GridSize + ix];
        var h11 = h[(iz + 1) * TerrainCell.GridSize + ix + 1];

        return h00 * (1f - fx) * (1f - fz)
               + h10 * fx * (1f - fz)
               + h01 * (1f - fx) * fz
               + h11 * fx * fz;
    }
}

public sealed class AssembledArea
{

    public required int AreaId { get; init; }


    public required IReadOnlyList<(int MapX, int MapZ)> CellKeys { get; init; }


    public int? MapOptionValue { get; init; }

    public int? RegionValue { get; init; }


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