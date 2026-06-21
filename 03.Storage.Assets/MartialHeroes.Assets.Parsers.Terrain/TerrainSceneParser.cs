using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

/// <summary>
///     Parser for <c>.bud</c> cell building blob files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain_scene.md
///     spec: Docs/RE/formats/terrain.md §8. Building mesh blob — .bud
///     <para>
///         No magic bytes; file begins with objectCount u32le.
///         spec: Docs/RE/formats/terrain_scene.md §File-level header — "no magic / no version": CONFIRMED.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies.
///     </para>
/// </remarks>
public static class TerrainSceneParser
{
    // Vertex count cap from legacy loader — warn-only, NOT enforced as a hard limit.
    // The loader allocates and reads the FULL vertex_count, then logs a warning if count > 3072.
    // It never throws, clamps, or truncates at this cap. Real VFS files exceed it.
    // spec: Docs/RE/formats/terrain_scene.md §vertex_count — "cap behaviour: loader-resolved: warn-and-continue on full count": CONFIRMED.
    // spec: Docs/RE/formats/terrain_scene.md §9 (implementor checklist) §10.3 (open question).
    private const uint VertexCountWarnThreshold = 3072; // 0xC00 — log-only, never enforce

    // Vertex stride: 32 bytes (8 × f32le).
    // spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
    private const int VertexStride = 32;

    /// <summary>
    ///     Parses the raw bytes of a <c>.bud</c> file into a <see cref="BudScene" />.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded scene containing all static objects.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown on buffer truncation only.
    ///     A vertex_count above 3072 does NOT throw — the legacy loader warns and continues,
    ///     reading the full declared count.
    ///     spec: Docs/RE/formats/terrain_scene.md §vertex_count — warn-and-continue: CONFIRMED (loader-resolved).
    /// </exception>
    public static BudScene Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static BudScene Parse(ReadOnlySpan<byte> span)
    {
        return Parse(span, ReadOnlyMemory<byte>.Empty);
    }

    private static BudScene Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        var offset = 0;

        // objectCount u32le @ offset 0.
        // spec: Docs/RE/formats/terrain_scene.md §File-level header — objectCount u32 @ 0x00: CONFIRMED.
        EnsureBytes(span, offset, 4, "objectCount");
        var objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        var objects = new BudObject[objectCount];

        for (var i = 0; i < (int)objectCount; i++)
        {
            // ─── Object header (9 bytes) ────────────────────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Object header (9 bytes): CONFIRMED.
            EnsureBytes(span, offset, 9, $"object[{i}] header");

            // type_byte u8 @ +0x00.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — type_byte u8 @ +0x00: PARTIAL.
            var typeByte = span[offset];
            offset += 1;

            // tex_id u32le @ +0x01.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
            var texId = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            // vertex_count u32le @ +0x05.
            // The legacy loader reads the FULL declared vertex_count and logs a warning if > 3072.
            // It never throws, clamps, or truncates. This parser mirrors that behaviour.
            // spec: Docs/RE/formats/terrain_scene.md §3.2.1 vertex_count — warn-and-continue: CONFIRMED (loader-resolved).
            // spec: Docs/RE/formats/terrain_scene.md §9 implementor checklist point 2.
            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            if (vertexCount > VertexCountWarnThreshold)
                // Mirror the legacy loader's log-only guard: warn and continue reading the full count.
                // Do NOT throw, clamp, or truncate — real VFS files legitimately exceed 3072.
                // spec: Docs/RE/formats/terrain_scene.md §vertex_count — "log-only and runs AFTER the full-count read": CONFIRMED.
                Debug.WriteLine(
                    $"[TerrainSceneParser] .bud object[{i}] vertex_count {vertexCount} " +
                    $"exceeds the legacy warn-threshold {VertexCountWarnThreshold} — continuing " +
                    "(warn-only, not an error). " +
                    "spec: Docs/RE/formats/terrain_scene.md §vertex_count warn-and-continue.");

            // ─── Vertex array (32 bytes × vertexCount) ─────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Vertex array (32 bytes per vertex): CONFIRMED.
            var vertexBytes = (long)vertexCount * VertexStride;
            EnsureBytes(span, offset, vertexBytes, $"object[{i}] vertex array");

            var vertices = new BudVertex[(int)vertexCount];
            if (BitConverter.IsLittleEndian)
            {
                // Zero-copy reinterpret of the vertex block; each BudVertex is exactly 32 bytes.
                var vBlock = span.Slice(offset, (int)vertexBytes);
                var floatBlock = MemoryMarshal.Cast<byte, float>(vBlock);
                for (var v = 0; v < (int)vertexCount; v++)
                {
                    var fi = v * 8;
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
                for (var v = 0; v < (int)vertexCount; v++)
                {
                    var vOff = offset + v * VertexStride;
                    vertices[v] = new BudVertex(
                        BinaryPrimitives.ReadSingleLittleEndian(span[vOff..]),
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
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            // ─── Index array (2 × indexCount bytes) ────────────────────────────
            // spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
            var indexBytes = (long)indexCount * 2;
            EnsureBytes(span, offset, indexBytes, $"object[{i}] index array");

            var indices = new ushort[(int)indexCount];
            var iBlock = span.Slice(offset, (int)indexBytes);
            if (BitConverter.IsLittleEndian)
                MemoryMarshal.Cast<byte, ushort>(iBlock).CopyTo(indices);
            else
                for (var idx = 0; idx < (int)indexCount; idx++)
                    indices[idx] = BinaryPrimitives.ReadUInt16LittleEndian(iBlock[(idx * 2)..]);

            offset += (int)indexBytes;

            objects[i] = new BudObject
            {
                TypeByte = typeByte,
                TexId = texId,
                Vertices = vertices,
                Indices = indices
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