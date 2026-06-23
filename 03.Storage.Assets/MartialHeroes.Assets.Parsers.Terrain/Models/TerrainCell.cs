namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public sealed class TerrainCell
{
    public const int GridSize = 65;

    public const int VertexCount = GridSize * GridSize;


    public required float[] Heights { get; init; }


    public required (float Nx, float Ny, float Nz)[] Normals { get; init; }


    public required byte[] TextureIndexGrid { get; init; }


    public required byte[] DirectionFlags { get; init; }


    public required (float R, float G, float B, float A)[] DiffuseColours { get; init; }
}

public readonly record struct LstCellEntry(uint Key, int MapX, int MapZ);

public sealed class LstManifest
{
    public required LstCellEntry[] Entries { get; init; }
}

public sealed class MapSection
{
    public required string Keyword { get; init; }

    public string? DataFile { get; init; }

    public required (int Flag, int TexId)[] Textures { get; init; }


    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? Grid { get; init; }

    public float? MaxHeightFiled { get; init; }

    public float? MinHeightFiled { get; init; }

    public (float X, float Z)? Origin { get; init; }
}

public sealed class MapDescriptor
{
    public required MapSection[] Sections { get; init; }
}

public readonly record struct MudTileRecord(
    byte Pad0,
    byte Pad1,
    byte MusicGroup,
    byte AmbientIdx0,
    byte AmbientIdx1,
    byte EffectIdx0,
    byte EffectIdx1,
    byte EffectIdx2
);

public sealed class MudBlob
{
    public const int GridCols = 64;

    public const int GridRows = 64;

    public const int RecordStride = 8;

    public const int FixedSize = GridRows * GridCols * RecordStride;

    public required MudTileRecord[] Tiles { get; init; }
}

public sealed class SodBlob
{
    public required uint SolidCount { get; init; }

    public required SolidRecord[] Solids { get; init; }


    public required ReadOnlyMemory<byte>[] RawSolidRecords { get; init; }

    public required uint[] TriangleCounts { get; init; }

    public required ReadOnlyMemory<byte>[] RawTriangleData { get; init; }
}

public sealed class SolidRecord
{

    public required float AabbMinX { get; init; }

    public required float AabbMinZ { get; init; }

    public required float AabbMaxX { get; init; }

    public required float AabbMaxZ { get; init; }


    public required WallSegment[] Segments { get; init; }


    public required ReadOnlyMemory<byte> RawRecord { get; init; }
}

public sealed class WallSegment
{

    public required float AabbMinX { get; init; }

    public required float AabbMinZ { get; init; }

    public required float AabbMaxX { get; init; }

    public required float AabbMaxZ { get; init; }


    public required float P0X { get; init; }

    public required float P0Z { get; init; }

    public required float P1X { get; init; }

    public required float P1Z { get; init; }


    public required float Slope { get; init; }

    public required float XConst { get; init; }

    public required float Intercept { get; init; }

    public required uint AxisFlag { get; init; }
}

public readonly record struct MudTile(
    byte BgmIdx,
    byte BgeIdx0,
    byte BgeIdx1,
    byte EffIdx0,
    byte EffIdx1,
    byte EffIdx2
)
{
    public static MudTile FromRecord(MudTileRecord rec)
    {
        return new MudTile(rec.MusicGroup, rec.AmbientIdx0, rec.AmbientIdx1, rec.EffectIdx0, rec.EffectIdx1,
            rec.EffectIdx2);
    }
}