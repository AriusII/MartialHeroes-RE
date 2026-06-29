namespace MartialHeroes.Assets.Parsers.Terrain.Models;

public sealed class TerrainCell
{
    public const int GridSize = 65;
    public const int VertexCount = GridSize * GridSize;
    public const int QuadCount = GridSize - 1;
    public const float GridSpacing = 16.0f;
    private const int PatchGridSize = 16;
    private const int QuadsPerPatch = QuadCount / PatchGridSize;
    private const float SteepHeightSpan = 8.0f;

    public required float[] Heights { get; init; }
    public required (float Nx, float Ny, float Nz)[] Normals { get; init; }
    public required byte[] TextureIndexGrid { get; init; }
    public required byte[] DirectionFlags { get; init; }
    public required (float R, float G, float B, float A)[] DiffuseColours { get; init; }

    public float SampleGroundHeight(float localX, float localZ)
    {
        var maxWorld = QuadCount * GridSpacing;
        var cx = localX < 0f ? 0f : localX > maxWorld ? maxWorld : localX;
        var cz = localZ < 0f ? 0f : localZ > maxWorld ? maxWorld : localZ;

        var fx = cx / GridSpacing;
        var fz = cz / GridSpacing;

        var col = (int)fx;
        var row = (int)fz;
        if (col >= QuadCount) col = QuadCount - 1;
        if (row >= QuadCount) row = QuadCount - 1;

        var patchCol = col / QuadsPerPatch;
        var patchRow = row / QuadsPerPatch;

        if (IsPatchSteep(patchRow, patchCol))
        {
            var fracX = fx - col;
            var fracZ = fz - row;

            var h00 = Heights[row * GridSize + col];
            var h10 = Heights[row * GridSize + col + 1];
            var h01 = Heights[(row + 1) * GridSize + col];
            var h11 = Heights[(row + 1) * GridSize + col + 1];

            if (fracX + fracZ <= 1f)
                return TriPlaneY(0f, 0f, h00, 1f, 0f, h10, 0f, 1f, h01, fracX, fracZ);
            return TriPlaneY(1f, 0f, h10, 1f, 1f, h11, 0f, 1f, h01, fracX, fracZ);
        }

        var originRow = patchRow * QuadsPerPatch;
        var originCol = patchCol * QuadsPerPatch;

        var c00 = Heights[originRow * GridSize + originCol];
        var c10 = Heights[originRow * GridSize + originCol + QuadsPerPatch];
        var c01 = Heights[(originRow + QuadsPerPatch) * GridSize + originCol];
        var c11 = Heights[(originRow + QuadsPerPatch) * GridSize + originCol + QuadsPerPatch];

        var patchWorld = QuadsPerPatch * GridSpacing;
        var u = (cx - originCol * GridSpacing) / patchWorld;
        var w = (cz - originRow * GridSpacing) / patchWorld;

        if (u + w <= 1f)
            return TriPlaneY(0f, 0f, c00, 1f, 0f, c10, 0f, 1f, c01, u, w);
        return TriPlaneY(1f, 0f, c10, 1f, 1f, c11, 0f, 1f, c01, u, w);
    }

    private bool IsPatchSteep(int patchRow, int patchCol)
    {
        var originRow = patchRow * QuadsPerPatch;
        var originCol = patchCol * QuadsPerPatch;
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var r = 0; r <= QuadsPerPatch; r++)
        {
            var rowBase = (originRow + r) * GridSize + originCol;
            for (var c = 0; c <= QuadsPerPatch; c++)
            {
                var h = Heights[rowBase + c];
                if (h < min) min = h;
                if (h > max) max = h;
            }
        }

        return max - min > SteepHeightSpan;
    }

    private static float TriPlaneY(
        float x0, float z0, float y0,
        float x1, float z1, float y1,
        float x2, float z2, float y2,
        float px, float pz)
    {
        var ax = x1 - x0;
        var ay = y1 - y0;
        var az = z1 - z0;
        var bx = x2 - x0;
        var by = y2 - y0;
        var bz = z2 - z0;
        var ny = az * bx - ax * bz;
        if (MathF.Abs(ny) < 1e-10f)
            return y0;
        var nx = ay * bz - az * by;
        var nz = ax * by - ay * bx;
        return y0 - (nx * (px - x0) + nz * (pz - z0)) / ny;
    }
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
    public required SodSolid[] Solids { get; init; }
}

public readonly record struct SodSolid(
    float AabbMinX,
    float AabbMinZ,
    float AabbMaxX,
    float AabbMaxZ,
    SodQuad[] Quads,
    ReadOnlyMemory<byte> RawRecord);

public readonly record struct SodQuad(
    float FootprintMinX,
    float FootprintMinZ,
    float FootprintMaxX,
    float FootprintMaxZ,
    float P0X,
    float P0Z,
    float P1X,
    float P1Z,
    float Slope,
    float XConst,
    float Intercept,
    uint AxisFlag);

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