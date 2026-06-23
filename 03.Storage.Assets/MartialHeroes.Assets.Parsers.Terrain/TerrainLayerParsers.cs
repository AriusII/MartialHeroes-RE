using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

public static class TerrainLayerParsers
{

    private const int TriangleRecordStride = 40;


    private const int FxShortGroupHeaderSize = 20;

    private const int Fx3GroupHeaderSize = 44;


    private const int Fx4FileTileCountSize = 4;

    private const int Fx4TileHeaderSize = 48;

    private const int Fx4TileVertexCountOffset = 0x28;

    private const int Fx4TileIndexCountOffset = 0x2C;

    private const int Fx4VertexStride = 44;


    private const int Fx5GroupHeaderSize = 48;
    private const int Fx5VertexCountOffset = 0x28;
    private const int Fx5IndexCountOffset = 0x2C;


    private const int Fx7GroupHeaderSize = 52;
    private const int Fx7VertexCountOffset = 0x2C;
    private const int Fx7IndexCountOffset = 0x30;
    private const int Fx7VertexStride = 32;


    private const int Fx6GlobalHeaderSize = 32;
    private const int Fx6SubChunkHeaderSize = 8;
    private const int Fx6FooterSize = 28;



    private const int PointLightRecordStride = 60;


    private const int WindKeyframeStride = 24;

    public static CollisionTriangleList ParseUpOrExd(ReadOnlyMemory<byte> data)
    {
        return ParseUpOrExd(data.Span);
    }

    public static CollisionTriangleList ParseUpOrExd(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4)
            throw new InvalidDataException(
                ".up/.exd parse error: buffer too short for triangle_count field. " +
                "spec: Docs/RE/formats/terrain_layers.md §2.1.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var expectedSize = 4 + (long)count * TriangleRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".up/.exd parse error: expected {expectedSize} bytes for {count} triangles, " +
                $"got {span.Length}. spec: Docs/RE/formats/terrain_layers.md §2.1.");

        var triangles = new CollisionTriangle[count];
        var offset = 4;

        for (var i = 0; i < (int)count; i++)
        {
            var rec = span.Slice(offset, TriangleRecordStride);
            triangles[i] = ReadCollisionTriangle(rec);
            offset += TriangleRecordStride;
        }

        return new CollisionTriangleList { Triangles = triangles };
    }

    private static CollisionTriangle ReadCollisionTriangle(ReadOnlySpan<byte> rec)
    {
        return new CollisionTriangle(
            BinaryPrimitives.ReadSingleLittleEndian(rec[..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[24..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[28..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[32..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[36..])
        );
    }


    public static SodPreCache ParseSodPre(ReadOnlyMemory<byte> data)
    {
        return ParseSodPre(data.Span);
    }

    public static SodPreCache ParseSodPre(ReadOnlySpan<byte> span)
    {
        if (span.Length < 8)
            throw new InvalidDataException(
                ".sod.pre parse error: buffer too short for 8-byte header. " +
                "spec: Docs/RE/formats/terrain_layers.md §4.1.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        var expectedSize = 8 + (long)vertexCount * 8;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".sod.pre parse error: expected {expectedSize} bytes for {vertexCount} vertices, " +
                $"got {span.Length}. spec: Docs/RE/formats/terrain_layers.md §4.1.");

        var vertices = new (float WorldX, float WorldZ)[(int)vertexCount];
        var offset = 8;
        for (var i = 0; i < (int)vertexCount; i++)
        {
            var wx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            var wz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            vertices[i] = (wx, wz);
            offset += 8;
        }

        return new SodPreCache { Version = version, Vertices = vertices };
    }


    private static FxVertex36 ReadFxVertex36(ReadOnlySpan<byte> rec)
    {
        return new FxVertex36(
            BinaryPrimitives.ReadSingleLittleEndian(rec[..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[20..]),
            rec[24], rec[25], rec[26], rec[27],
            BinaryPrimitives.ReadSingleLittleEndian(rec[28..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[32..])
        );
    }

    private static FxVertex44 ReadFxVertex44(ReadOnlySpan<byte> rec)
    {
        return new FxVertex44(
            BinaryPrimitives.ReadSingleLittleEndian(rec[..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[20..]),
            rec[24], rec[25], rec[26], rec[27],
            BinaryPrimitives.ReadSingleLittleEndian(rec[28..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[32..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[36..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[40..])
        );
    }

    private static FxVertex32 ReadFxVertex32(ReadOnlySpan<byte> rec)
    {
        return new FxVertex32(
            BinaryPrimitives.ReadSingleLittleEndian(rec[..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[24..]),
            BinaryPrimitives.ReadSingleLittleEndian(rec[28..])
        );
    }

    private static ushort[] ReadU16Indices(ReadOnlySpan<byte> span, int offset, int count,
        ReadOnlyMemory<byte>? backing = null)
    {
        var arr = new ushort[count];
        var raw = span.Slice(offset, count * 2);
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, ushort>(raw).CopyTo(arr);
        else
            for (var i = 0; i < count; i++)
                arr[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw[(i * 2)..]);
        return arr;
    }


    public static Fx1Layer ParseFx1(ReadOnlyMemory<byte> data)
    {
        return ParseFx1(data.Span, data);
    }

    private static Fx1Layer ParseFx1(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx1", span, 4);
        var groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = 4;

        var groups = new Fx1Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            if (offset + FxShortGroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx1 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.1a.");

            var textureIndex1Based = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            var groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            var renderState =
                BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);
            offset += FxShortGroupHeaderSize;

            var geoBytes = (long)vertexCount * 36 + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx1 parse error: group[{g}] geometry truncated at offset {offset} " +
                    $"(need {geoBytes} bytes). spec: Docs/RE/formats/terrain_layers.md §1.5.");

            var vertices = ReadVf36Array(span, offset, (int)vertexCount);
            offset += (int)vertexCount * 36;
            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            groups[g] = new Fx1Group
            {
                TextureIndex1Based = textureIndex1Based,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = ReadOnlyMemory<byte>.Empty,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx1Layer { GroupCount = groupCount, Groups = groups };
    }


    public static Fx2Layer ParseFx2(ReadOnlyMemory<byte> data)
    {
        return ParseFx2(data.Span);
    }

    private static Fx2Layer ParseFx2(ReadOnlySpan<byte> span)
    {
        EnsureFx("fx2", span, 4);
        var groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = 4;

        var groups = new Fx2Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            if (offset + FxShortGroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx2 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.1a.");

            var textureIndex1Based = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            var groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            var renderState = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);
            offset += FxShortGroupHeaderSize;

            var geoBytes = (long)vertexCount * 44 + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx2 parse error: group[{g}] geometry truncated at offset {offset} " +
                    $"(need {geoBytes} bytes). spec: Docs/RE/formats/terrain_layers.md §1.6.");

            var vertices = new FxVertex44[(int)vertexCount];
            for (var v = 0; v < (int)vertexCount; v++)
                vertices[v] = ReadFxVertex44(span.Slice(offset + v * 44, 44));
            offset += (int)vertexCount * 44;
            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            groups[g] = new Fx2Group
            {
                TextureIndex1Based = textureIndex1Based,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = ReadOnlyMemory<byte>.Empty,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx2Layer { GroupCount = groupCount, Groups = groups };
    }


    public static Fx3Layer ParseFx3(ReadOnlyMemory<byte> data)
    {
        return ParseFx3(data.Span, data);
    }

    private static Fx3Layer ParseFx3(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx3", span, 4);
        var groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = 4;

        var groups = new Fx3Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            if (offset + Fx3GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx3 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.7.");

            var textureIndex1Based = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            var groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            var renderState =
                BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
            var rawExtra = backing.IsEmpty
                ? span.Slice(offset + 12, 32).ToArray()
                : backing.Slice(offset + 12, 32);
            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 36)..]);
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 40)..]);
            offset += Fx3GroupHeaderSize;

            var geoBytes = (long)vertexCount * 36 + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx3 parse error: group[{g}] geometry truncated at offset {offset} " +
                    $"(need {geoBytes} bytes). spec: Docs/RE/formats/terrain_layers.md §1.7.");

            var vertices = ReadVf36Array(span, offset, (int)vertexCount);
            offset += (int)vertexCount * 36;
            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            groups[g] = new Fx3Group
            {
                TextureIndex1Based = textureIndex1Based,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = rawExtra,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx3Layer { GroupCount = groupCount, Groups = groups };
    }

    public static Fx4Layer ParseFx4(ReadOnlyMemory<byte> data)
    {
        return ParseFx4(data.Span, data);
    }

    private static Fx4Layer ParseFx4(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < Fx4FileTileCountSize)
            throw new InvalidDataException(
                $".fx4 parse error: buffer too short for tile_count field (need {Fx4FileTileCountSize}, " +
                $"got {span.Length}). spec: Docs/RE/formats/terrain_layers.md §1.11.");

        var tileCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = Fx4FileTileCountSize;

        var tiles = new Fx4Tile[tileCount];

        for (uint t = 0; t < tileCount; t++)
        {
            if (offset + Fx4TileHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx4 parse error: tile[{t}] header truncated at offset {offset} " +
                    $"(need {Fx4TileHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.11.");

            var rawTileHdr = backing.IsEmpty
                ? span.Slice(offset, Fx4TileHeaderSize).ToArray()
                : backing.Slice(offset, Fx4TileHeaderSize);

            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx4TileVertexCountOffset, 4));

            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx4TileIndexCountOffset, 4));

            offset += Fx4TileHeaderSize;

            var geoBytes = (long)vertexCount * Fx4VertexStride + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx4 parse error: tile[{t}] geometry truncated at offset {offset} — " +
                    $"need {geoBytes} bytes (vertexCount={vertexCount}×44 + indexCount={indexCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.11.");

            var vertices = new FxVertex44[(int)vertexCount];
            for (var v = 0; v < (int)vertexCount; v++)
                vertices[v] = ReadFxVertex44(span.Slice(offset + v * Fx4VertexStride, Fx4VertexStride));
            offset += (int)vertexCount * Fx4VertexStride;

            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            tiles[t] = new Fx4Tile
            {
                RawTileHeader = rawTileHdr,
                VertexCount = vertexCount,
                IndexCount = indexCount,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx4Layer { TileCount = tileCount, Tiles = tiles };
    }

    public static Fx5Layer ParseFx5(ReadOnlyMemory<byte> data)
    {
        return ParseFx5(data.Span, data);
    }

    private static Fx5Layer ParseFx5(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx5", span, 4);
        var groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = 4;

        var sections = new Fx5Section[groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            if (offset + Fx5GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx5 parse error: group[{g}] header truncated at offset {offset} " +
                    $"(need {Fx5GroupHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.8.");

            var rawGroupHdr = backing.IsEmpty
                ? span.Slice(offset, Fx5GroupHeaderSize).ToArray()
                : backing.Slice(offset, Fx5GroupHeaderSize);

            var vertCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx5VertexCountOffset, 4));

            var idxCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx5IndexCountOffset, 4));

            offset += Fx5GroupHeaderSize;

            var needed = (long)vertCount * 36 + (long)idxCount * 2;
            if (offset + needed > span.Length)
                throw new InvalidDataException(
                    $".fx5 parse error: group[{g}] geometry truncated at offset {offset} — " +
                    $"need {needed} bytes (vertCount={vertCount}×36 + idxCount={idxCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.8.");

            var vertices = ReadVf36Array(span, offset, (int)vertCount);
            offset += (int)vertCount * 36;

            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            sections[g] = new Fx5Section
            {
                RawSectionHeader = rawGroupHdr,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx5Layer { Sections = sections };
    }

    public static Fx7Layer ParseFx7(ReadOnlyMemory<byte> data)
    {
        return ParseFx7(data.Span, data);
    }

    private static Fx7Layer ParseFx7(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx7", span, 4);
        var groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var offset = 4;

        var groups = new Fx7Group[groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            if (offset + Fx7GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx7 parse error: group[{g}] header truncated at offset {offset} " +
                    $"(need {Fx7GroupHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.10.");

            var rawGroupHdr = backing.IsEmpty
                ? span.Slice(offset, Fx7GroupHeaderSize).ToArray()
                : backing.Slice(offset, Fx7GroupHeaderSize);

            var vertCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx7VertexCountOffset, 4));

            var idxCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx7IndexCountOffset, 4));

            offset += Fx7GroupHeaderSize;

            var geoBytes = (long)vertCount * Fx7VertexStride + (long)idxCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx7 parse error: group[{g}] geometry truncated at offset {offset} — " +
                    $"need {geoBytes} bytes (vertCount={vertCount}×32 + idxCount={idxCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.10.");

            var vertices = new FxVertex32[(int)vertCount];
            for (var v = 0; v < (int)vertCount; v++)
                vertices[v] = ReadFxVertex32(span.Slice(offset + v * Fx7VertexStride, Fx7VertexStride));
            offset += (int)vertCount * Fx7VertexStride;

            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            groups[g] = new Fx7Group
            {
                RawGroupHeader = rawGroupHdr,
                VertexCount = vertCount,
                IndexCount = idxCount,
                Vertices = vertices,
                Indices = indices
            };
        }

        return new Fx7Layer { GroupCount = groupCount, Groups = groups };
    }

    public static Fx6Layer ParseFx6(ReadOnlyMemory<byte> data)
    {
        return ParseFx6(data.Span, data);
    }

    private static Fx6Layer ParseFx6(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < Fx6GlobalHeaderSize)
            throw new InvalidDataException(
                $".fx6 parse error: buffer too short for 32-byte global header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §1.9.");

        var subChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var rawGlobalRest = backing.IsEmpty
            ? span.Slice(4, 28).ToArray()
            : backing.Slice(4, 28);
        var offset = Fx6GlobalHeaderSize;

        var subChunks = new Fx6SubChunk[(int)subChunkCount];
        for (var s = 0; s < (int)subChunkCount; s++)
        {
            var isFinal = s == (int)subChunkCount - 1;

            if (offset + Fx6SubChunkHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx6 parse error: sub-chunk[{s}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.9.");

            var vertCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            var idxCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            offset += Fx6SubChunkHeaderSize;

            var geoBytes = (long)vertCount * 32 + (long)idxCount * 2;
            if (offset + geoBytes + (isFinal ? 0 : Fx6FooterSize) > span.Length)
                throw new InvalidDataException(
                    $".fx6 parse error: sub-chunk[{s}] geometry or footer truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.9.");

            var vertices = new FxVertex32[(int)vertCount];
            for (var v = 0; v < (int)vertCount; v++)
                vertices[v] = ReadFxVertex32(span.Slice(offset + v * 32, 32));
            offset += (int)vertCount * 32;

            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            ReadOnlyMemory<byte> rawFooter;
            if (!isFinal)
            {
                rawFooter = backing.IsEmpty
                    ? span.Slice(offset, Fx6FooterSize).ToArray()
                    : backing.Slice(offset, Fx6FooterSize);
                offset += Fx6FooterSize;
            }
            else
            {
                rawFooter = ReadOnlyMemory<byte>.Empty;
            }

            subChunks[s] = new Fx6SubChunk
            {
                Vertices = vertices,
                Indices = indices,
                RawFooter = rawFooter
            };
        }

        return new Fx6Layer
        {
            SubChunkCount = subChunkCount,
            RawGlobalHeaderRest = rawGlobalRest,
            SubChunks = subChunks
        };
    }

    public static PointLightBinData ParsePointLightBin(ReadOnlyMemory<byte> data)
    {
        return ParsePointLightBin(data.Span, data);
    }

    private static PointLightBinData ParsePointLightBin(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < 8)
            throw new InvalidDataException(
                $"point_light*.bin parse error: buffer too short for 8-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §7.1.");

        var intensityScale = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var expectedSize = 8 + (long)count * PointLightRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"point_light*.bin parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §7.1.");

        var records = new PointLightRecord[(int)count];
        for (var i = 0; i < (int)count; i++)
        {
            var recOffset = 8 + i * PointLightRecordStride;
            var rec = span.Slice(recOffset, PointLightRecordStride);
            var cg1 = new float[3];
            var cg2 = new float[3];
            var cg3 = new float[3];
            for (var c = 0; c < 3; c++)
            {
                cg1[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(c * 4)..]);
                cg2[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(12 + c * 4)..]);
                cg3[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(24 + c * 4)..]);
            }

            var rawRest = backing.IsEmpty
                ? rec.Slice(36, 16).ToArray()
                : backing.Slice(recOffset + 36, 16);
            var enabledFlag = BinaryPrimitives.ReadUInt32LittleEndian(rec[52..]);
            var unknown4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[56..]);

            records[i] = new PointLightRecord
            {
                ColourGroup1 = cg1,
                ColourGroup2 = cg2,
                ColourGroup3 = cg3,
                RawRest = rawRest,
                EnabledFlag = enabledFlag,
                Unknown4 = unknown4
            };
        }

        return new PointLightBinData { IntensityScale = intensityScale, Records = records };
    }

    public static WindBinData ParseWindBin(ReadOnlyMemory<byte> data)
    {
        return ParseWindBin(data.Span, data);
    }

    private static WindBinData ParseWindBin(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < 8)
            throw new InvalidDataException(
                $"wind*.bin parse error: buffer too short for 8-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §8.1.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var flag2 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var expectedSize = 8 + (long)count * WindKeyframeStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"wind*.bin parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §8.1.");

        var keyframes = new ReadOnlyMemory<byte>[(int)count];
        for (var i = 0; i < (int)count; i++)
        {
            var kfOffset = 8 + i * WindKeyframeStride;
            keyframes[i] = backing.IsEmpty
                ? span.Slice(kfOffset, WindKeyframeStride).ToArray()
                : backing.Slice(kfOffset, WindKeyframeStride);
        }

        return new WindBinData { Count = count, Flag2 = flag2, RawKeyframes = keyframes };
    }


    private static void EnsureFx(string ext, ReadOnlySpan<byte> span, int minSize)
    {
        if (span.Length < minSize)
            throw new InvalidDataException(
                $".{ext} parse error: buffer too short for {minSize}-byte header (got {span.Length}). " +
                $"spec: Docs/RE/formats/terrain_layers.md §1.");
    }

    private static FxVertex36[] ReadVf36Array(ReadOnlySpan<byte> span, int start, int count)
    {
        var arr = new FxVertex36[count];
        for (var i = 0; i < count; i++)
            arr[i] = ReadFxVertex36(span.Slice(start + i * 36, 36));
        return arr;
    }
}