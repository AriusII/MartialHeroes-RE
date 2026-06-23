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

        float v0X, v0Y, v0Z, v1X, v1Y, v1Z, v2X, v2Y, v2Z;
        if (fx + fz < 1f)
        {

            v0X = 0f;
            v0Y = h00;
            v0Z = 0f;
            v1X = 1f;
            v1Y = h10;
            v1Z = 0f;
            v2X = 0f;
            v2Y = h01;
            v2Z = 1f;
        }
        else
        {

            v0X = 1f;
            v0Y = h10;
            v0Z = 0f;
            v1X = 1f;
            v1Y = h11;
            v1Z = 1f;
            v2X = 0f;
            v2Y = h01;
            v2Z = 1f;
        }

        var e1X = v1X - v0X;
        var e1Y = v1Y - v0Y;
        var e1Z = v1Z - v0Z;
        var e2X = v2X - v0X;
        var e2Y = v2Y - v0Y;
        var e2Z = v2Z - v0Z;
        var nX = e1Y * e2Z - e1Z * e2Y;
        var nY = e1Z * e2X - e1X * e2Z;
        var nZ = e1X * e2Y - e1Y * e2X;

        if (Math.Abs(nY) < 1e-8f)
            return h00;

        var d = -(nX * v0X + nY * v0Y + nZ * v0Z);
        return (-d - nX * fx - nZ * fz) / nY;
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
