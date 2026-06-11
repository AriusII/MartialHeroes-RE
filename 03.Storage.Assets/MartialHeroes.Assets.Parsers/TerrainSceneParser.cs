using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.bud</c> cell building blob files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_scene.md
/// spec: Docs/RE/formats/terrain.md §8. Building mesh blob — .bud
/// <para>
/// No magic bytes; file begins with objectCount u32le.
/// spec: Docs/RE/formats/terrain_scene.md §File-level header — "no magic / no version": CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class TerrainSceneParser
{
    // Max vertex count enforced by legacy loader.
    // spec: Docs/RE/formats/terrain_scene.md §vertex_count — "Must be ≤ 3072 (0xC00)": CONFIRMED.
    private const uint MaxVertexCount = 3072; // 0xC00

    // Vertex stride: 32 bytes (8 × f32le).
    // spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
    private const int VertexStride = 32;

    /// <summary>
    /// Parses the raw bytes of a <c>.bud</c> file into a <see cref="BudScene"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded scene containing all static objects.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation or vertex-count overflow.</exception>
    public static BudScene Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span, data);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static BudScene Parse(ReadOnlySpan<byte> span) =>
        Parse(span, ReadOnlyMemory<byte>.Empty);

    private static BudScene Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        int offset = 0;

        // objectCount u32le @ offset 0.
        // spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32 @ 0x00: CONFIRMED.
        EnsureBytes(span, offset, 4, "objectCount");
        uint objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        var objects = new BudObject[objectCount];

        for (int i = 0; i < (int)objectCount; i++)
        {
            // ─── Object header (9 bytes) ────────────────────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Object header (9 bytes): CONFIRMED.
            EnsureBytes(span, offset, 9, $"object[{i}] header");

            // type_byte u8 @ +0x00.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — type_byte u8 @ +0x00: PARTIAL.
            byte typeByte = span[offset];
            offset += 1;

            // tex_id u32le @ +0x01.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
            uint texId = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            // vertex_count u32le @ +0x05. Must be ≤ 3072.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — vertex_count u32 @ +0x05: CONFIRMED.
            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            if (vertexCount > MaxVertexCount)
                throw new InvalidDataException(
                    $".bud parse error: object[{i}] vertex_count {vertexCount} exceeds max " +
                    $"{MaxVertexCount}. spec: Docs/RE/formats/terrain_scene.md §vertex_count.");

            // ─── Vertex array (32 bytes × vertexCount) ─────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Vertex array (32 bytes per vertex): CONFIRMED.
            long vertexBytes = (long)vertexCount * VertexStride;
            EnsureBytes(span, offset, vertexBytes, $"object[{i}] vertex array");

            var vertices = new BudVertex[(int)vertexCount];
            if (BitConverter.IsLittleEndian)
            {
                // Zero-copy reinterpret of the vertex block; each BudVertex is exactly 32 bytes.
                ReadOnlySpan<byte> vBlock = span.Slice(offset, (int)vertexBytes);
                var floatBlock = MemoryMarshal.Cast<byte, float>(vBlock);
                for (int v = 0; v < (int)vertexCount; v++)
                {
                    int fi = v * 8;
                    // spec: Docs/RE/formats/terrain_scene.md §Vertex record:
                    //   pos_x @ +0x00, pos_y @ +0x04, pos_z @ +0x08: CONFIRMED.
                    //   normal_x @ +0x0C, normal_y @ +0x10, normal_z @ +0x14: CONFIRMED.
                    //   uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
                    vertices[v] = new BudVertex(
                        floatBlock[fi], // pos_x
                        floatBlock[fi + 1], // pos_y
                        floatBlock[fi + 2], // pos_z
                        floatBlock[fi + 3], // normal_x
                        floatBlock[fi + 4], // normal_y
                        floatBlock[fi + 5], // normal_z
                        floatBlock[fi + 6], // uv_u
                        floatBlock[fi + 7] // uv_v
                    );
                }
            }
            else
            {
                for (int v = 0; v < (int)vertexCount; v++)
                {
                    int vOff = offset + v * VertexStride;
                    vertices[v] = new BudVertex(
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 4)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 8)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 12)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 16)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 20)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 24)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(span[(vOff + 28)..])
                    );
                }
            }

            offset += (int)vertexBytes;

            // ─── Index header (4 bytes) ─────────────────────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Index header — index_count u32: CONFIRMED.
            EnsureBytes(span, offset, 4, $"object[{i}] index_count");
            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            // ─── Index array (2 × indexCount bytes) ────────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
            long indexBytes = (long)indexCount * 2;
            EnsureBytes(span, offset, indexBytes, $"object[{i}] index array");

            var indices = new ushort[(int)indexCount];
            ReadOnlySpan<byte> iBlock = span.Slice(offset, (int)indexBytes);
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, ushort>(iBlock).CopyTo(indices);
            }
            else
            {
                for (int idx = 0; idx < (int)indexCount; idx++)
                    indices[idx] = BinaryPrimitives.ReadUInt16LittleEndian(iBlock[(idx * 2)..]);
            }

            offset += (int)indexBytes;

            objects[i] = new BudObject
            {
                TypeByte = typeByte,
                TexId = texId,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new BudScene { Objects = objects };
    }

    private static void EnsureBytes(ReadOnlySpan<byte> span, int offset, long needed, string fieldName)
    {
        if (offset + needed > span.Length)
            throw new InvalidDataException(
                $".bud parse error: buffer truncated reading '{fieldName}' — " +
                $"need {needed} bytes at offset {offset}, buffer length {span.Length}. " +
                $"spec: Docs/RE/formats/terrain_scene.md §File-size formula.");
    }
}