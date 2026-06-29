using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class TedTerrainParser
{
    private const int VertexCount = TerrainCell.VertexCount;

    private const int HeightmapOffset = 0;
    private const int HeightmapSize = VertexCount * 4;

    private const int NormalsOffset = HeightmapOffset + HeightmapSize;
    private const int NormalsSize = VertexCount * 3;

    private const int LookupOffset = NormalsOffset + NormalsSize;
    private const int LookupSize = 256;

    private const int DirectionOffset = LookupOffset + LookupSize;
    private const int DirectionSize = 256;

    private const int DiffuseOffset = DirectionOffset + DirectionSize;
    private const int DiffuseSize = VertexCount * 4;

    private const int TotalSize = DiffuseOffset + DiffuseSize;

    public static TerrainCell Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static TerrainCell Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < TotalSize)
            throw new InvalidDataException(
                $".ted parse error: buffer too small — expected at least {TotalSize} bytes, " +
                $"got {data.Length} bytes. " +
                $"spec: Docs/RE/formats/terrain.md §5.1.");

        var heights = new float[VertexCount];
        var heightRaw = data.Slice(HeightmapOffset, HeightmapSize);
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, float>(heightRaw).CopyTo(heights);
        else
            for (var i = 0; i < VertexCount; i++)
                heights[i] = BinaryPrimitives.ReadSingleLittleEndian(heightRaw[(i * 4)..]);

        var normals = new (float Nx, float Ny, float Nz)[VertexCount];
        var normalRaw = data.Slice(NormalsOffset, NormalsSize);
        for (var i = 0; i < VertexCount; i++)
        {
            var nx = (sbyte)normalRaw[i * 3 + 0] / 127.0f;
            var ny = (sbyte)normalRaw[i * 3 + 1] / 127.0f;
            var nz = (sbyte)normalRaw[i * 3 + 2] / 127.0f;
            normals[i] = (nx, ny, nz);
        }

        var textureIndexGrid = new byte[LookupSize];
        data.Slice(LookupOffset, LookupSize).CopyTo(textureIndexGrid);

        var directionFlags = new byte[DirectionSize];
        data.Slice(DirectionOffset, DirectionSize).CopyTo(directionFlags);

        var diffuseColours = new (float R, float G, float B, float A)[VertexCount];
        var diffuseRaw = data.Slice(DiffuseOffset, DiffuseSize);
        for (var i = 0; i < VertexCount; i++)
        {
            var r = diffuseRaw[i * 4 + 0] * 0.5f;
            var g = diffuseRaw[i * 4 + 1] * 0.5f;
            var b = diffuseRaw[i * 4 + 2] * 0.5f;
            var a = (float)diffuseRaw[i * 4 + 3];
            diffuseColours[i] = (r, g, b, a);
        }

        return new TerrainCell
        {
            Heights = heights,
            Normals = normals,
            TextureIndexGrid = textureIndexGrid,
            DirectionFlags = directionFlags,
            DiffuseColours = diffuseColours
        };
    }
}