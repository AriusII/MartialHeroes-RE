using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for terrain layer sidecar files:
/// <c>.up</c>, <c>.exd</c>, <c>.sod.pre</c>, <c>.fx1</c>–<c>.fx6</c>,
/// <c>light*.bin</c>, <c>point_light*.bin</c>, <c>wind*.bin</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain_layers.md
/// spec: Docs/RE/formats/terrain.md §9 (.up / .exd) and §11 (.sod)
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class TerrainLayerParsers
{
    // ─── .up / .exd  (identical binary layout) ────────────────────────────────

    // Triangle record stride: 40 bytes (10 × f32le).
    // spec: Docs/RE/formats/terrain_layers.md §2.1 — stride 40 bytes: CONFIRMED.
    private const int TriangleRecordStride = 40;

    /// <summary>
    /// Parses an <c>.up</c> or <c>.exd</c> collision triangle list.
    /// Layout is identical for both files.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded collision triangle list.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §2.1 File layout (.up): CONFIRMED.
    /// spec: Docs/RE/formats/terrain_layers.md §3.1 File layout (.exd): CONFIRMED (identical to .up).
    /// File-size formula: 4 + triangle_count × 40.
    /// </remarks>
    public static CollisionTriangleList ParseUpOrExd(ReadOnlyMemory<byte> data) =>
        ParseUpOrExd(data.Span);

    /// <inheritdoc cref="ParseUpOrExd(ReadOnlyMemory{byte})"/>
    public static CollisionTriangleList ParseUpOrExd(ReadOnlySpan<byte> span)
    {
        // triangle_count u32le @ offset 0.
        // spec: Docs/RE/formats/terrain_layers.md §2.1 — triangle_count u32 @ 0x00: CONFIRMED.
        if (span.Length < 4)
            throw new InvalidDataException(
                ".up/.exd parse error: buffer too short for triangle_count field. " +
                "spec: Docs/RE/formats/terrain_layers.md §2.1.");

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span);
        long expectedSize = 4 + (long)count * TriangleRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".up/.exd parse error: expected {expectedSize} bytes for {count} triangles, " +
                $"got {span.Length}. spec: Docs/RE/formats/terrain_layers.md §2.1.");

        var triangles = new CollisionTriangle[count];
        int offset = 4;

        for (int i = 0; i < (int)count; i++)
        {
            ReadOnlySpan<byte> rec = span.Slice(offset, TriangleRecordStride);
            // spec: Docs/RE/formats/terrain_layers.md §2.1 Triangle record (40 bytes): CONFIRMED.
            triangles[i] = ReadCollisionTriangle(rec);
            offset += TriangleRecordStride;
        }

        return new CollisionTriangleList { Triangles = triangles };
    }

    private static CollisionTriangle ReadCollisionTriangle(ReadOnlySpan<byte> rec)
    {
        // All 10 × f32le fields confirmed.
        // spec: Docs/RE/formats/terrain_layers.md §2.1 — v1_x @ +0x00 through plane_height @ +0x24: CONFIRMED.
        return new CollisionTriangle(
            BinaryPrimitives.ReadSingleLittleEndian(rec[0..]), // v1_x
            BinaryPrimitives.ReadSingleLittleEndian(rec[4..]), // v1_y
            BinaryPrimitives.ReadSingleLittleEndian(rec[8..]), // v1_z
            BinaryPrimitives.ReadSingleLittleEndian(rec[12..]), // v2_x
            BinaryPrimitives.ReadSingleLittleEndian(rec[16..]), // v2_y
            BinaryPrimitives.ReadSingleLittleEndian(rec[20..]), // v2_z
            BinaryPrimitives.ReadSingleLittleEndian(rec[24..]), // v3_x
            BinaryPrimitives.ReadSingleLittleEndian(rec[28..]), // v3_y
            BinaryPrimitives.ReadSingleLittleEndian(rec[32..]), // v3_z
            BinaryPrimitives.ReadSingleLittleEndian(rec[36..]) // plane_height @ +0x24: CONFIRMED.
        );
    }

    // ─── .sod.pre  collision polygon vertex cache ──────────────────────────────

    /// <summary>
    /// Parses a <c>.sod.pre</c> collision polygon vertex cache.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded vertex cache.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §4.1 File layout: CONFIRMED (3 samples).
    /// Header: version u32 @ +0x00, vertex_count u32 @ +0x04.
    /// Vertex record: world_x f32 @ +0x00, world_z f32 @ +0x04 (8 bytes each).
    /// File-size formula: 8 + vertex_count × 8.
    /// </remarks>
    public static SodPreCache ParseSodPre(ReadOnlyMemory<byte> data) =>
        ParseSodPre(data.Span);

    /// <inheritdoc cref="ParseSodPre(ReadOnlyMemory{byte})"/>
    public static SodPreCache ParseSodPre(ReadOnlySpan<byte> span)
    {
        // Header: version u32 @ +0x00, vertex_count u32 @ +0x04.
        // spec: Docs/RE/formats/terrain_layers.md §4.1 — version u32 @ +0x00: CONFIRMED.
        // spec: Docs/RE/formats/terrain_layers.md §4.1 — vertex_count u32 @ +0x04: CONFIRMED.
        if (span.Length < 8)
            throw new InvalidDataException(
                ".sod.pre parse error: buffer too short for 8-byte header. " +
                "spec: Docs/RE/formats/terrain_layers.md §4.1.");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        long expectedSize = 8 + (long)vertexCount * 8;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".sod.pre parse error: expected {expectedSize} bytes for {vertexCount} vertices, " +
                $"got {span.Length}. spec: Docs/RE/formats/terrain_layers.md §4.1.");

        var vertices = new (float WorldX, float WorldZ)[(int)vertexCount];
        int offset = 8;
        for (int i = 0; i < (int)vertexCount; i++)
        {
            // spec: Docs/RE/formats/terrain_layers.md §4.1 — world_x f32 @ +0, world_z f32 @ +4: CONFIRMED.
            float wx = BinaryPrimitives.ReadSingleLittleEndian(span[offset..]);
            float wz = BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]);
            vertices[i] = (wx, wz);
            offset += 8;
        }

        return new SodPreCache { Version = version, Vertices = vertices };
    }

    // ─── FX vertex helper readers ──────────────────────────────────────────────

    private static FxVertex36 ReadFxVertex36(ReadOnlySpan<byte> rec)
    {
        // VF_36: XYZ f32×3 + normal f32×3 + RGBA u8×4 + UV f32×2 = 36 bytes.
        // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_36 (36 B): CONFIRMED.
        return new FxVertex36(
            BinaryPrimitives.ReadSingleLittleEndian(rec[0..]),
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
        // VF_44: VF_36 + f32 U1 + f32 V1 = 44 bytes.
        // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
        return new FxVertex44(
            BinaryPrimitives.ReadSingleLittleEndian(rec[0..]),
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
        // VF_32: XYZ f32×3 + normal f32×3 + UV f32×2 = 32 bytes. No colour.
        // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_32 (32 B): CONFIRMED (FX6 only).
        return new FxVertex32(
            BinaryPrimitives.ReadSingleLittleEndian(rec[0..]),
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
        ReadOnlySpan<byte> raw = span.Slice(offset, count * 2);
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, ushort>(raw).CopyTo(arr);
        else
            for (int i = 0; i < count; i++)
                arr[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw[(i * 2)..]);
        return arr;
    }

    // ─── FX1/FX2/FX3 group-array sizes ───────────────────────────────────────

    // FX1/FX2 per-group header: 20 bytes (5 × u32: group_flags_0, group_flags_1, render_state, vertex_count, index_count).
    // spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 — group header 20 B: CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.1a — universal group-array model: CONFIRMED (two-witness).
    private const int FxShortGroupHeaderSize = 20; // FX1, FX2

    // FX3 per-group header: 44 bytes (5 × base + 6 × extra = 11 × u32).
    // spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 — group header 44 B: CONFIRMED.
    private const int Fx3GroupHeaderSize = 44; // FX3 extended group header

    // ─── .fx1 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an <c>.fx1</c> terrain overlay layer file.
    /// Universal group-array layout: u32 group_count, then group_count × [20-byte group header + VF_36 vertices + u16 indices].
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded FX1 layer.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 Format: CONFIRMED (3 samples, exact size match).
    /// spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count is the leading u32 (NOT a constant): CONFIRMED (two-witness).
    /// render_state @ group+0x08 is CONFIRMED-variable (NOT a fixed constant=15).
    /// File-size formula: 4 + Σ over groups (20 + vertex_count × 36 + index_count × 2).
    /// </remarks>
    public static Fx1Layer ParseFx1(ReadOnlyMemory<byte> data) =>
        ParseFx1(data.Span, data);

    private static Fx1Layer ParseFx1(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // group_count u32 @ file offset 0x00. NOT a constant and NOT a sub-format selector.
        // spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count: CONFIRMED (two-witness; corpus 1..61).
        // spec: Docs/RE/formats/terrain_layers.md §1.5 FX1.
        EnsureFx("fx1", span, 4);
        uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = 4;

        var groups = new Fx1Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            // Per-group header: 20 bytes.
            // spec: Docs/RE/formats/terrain_layers.md §1.1a — per-group header: group_flags_0 @ +0x00,
            //   group_flags_1 @ +0x04, render_state @ +0x08 (CONFIRMED-variable), vertex_count @ +0x0C, index_count @ +0x10.
            if (offset + FxShortGroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx1 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.1a.");

            uint groupFlags0 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset)..]); // +0x00 UNVERIFIED
            uint groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]); // +0x04 UNVERIFIED
            uint renderState =
                BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]); // +0x08 CONFIRMED-variable
            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]); // +0x0C CONFIRMED
            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]); // +0x10 CONFIRMED
            offset += FxShortGroupHeaderSize;

            long geoBytes = (long)vertexCount * 36 + (long)indexCount * 2;
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
                GroupFlags0 = groupFlags0,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = ReadOnlyMemory<byte>.Empty, // no extra header for FX1
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx1Layer { GroupCount = groupCount, Groups = groups };
    }

    // ─── .fx2 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an <c>.fx2</c> terrain overlay layer file.
    /// Universal group-array layout: u32 group_count, then group_count × [20-byte group header + VF_44 vertices + u16 indices].
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.6 FX2 Format: CONFIRMED (3 samples, exact size match).
    /// spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count is the leading u32: CONFIRMED (two-witness).
    /// render_state @ group+0x08 is CONFIRMED-variable (NOT a fixed constant).
    /// File-size formula: 4 + Σ over groups (20 + vertex_count × 44 + index_count × 2).
    /// </remarks>
    public static Fx2Layer ParseFx2(ReadOnlyMemory<byte> data) =>
        ParseFx2(data.Span);

    private static Fx2Layer ParseFx2(ReadOnlySpan<byte> span)
    {
        // group_count u32 @ file offset 0x00.
        // spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count: CONFIRMED. §1.6 FX2.
        EnsureFx("fx2", span, 4);
        uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = 4;

        var groups = new Fx2Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            // Per-group header: 20 bytes (same as FX1).
            // spec: Docs/RE/formats/terrain_layers.md §1.6 — same group header as FX1 (§1.1a): CONFIRMED.
            if (offset + FxShortGroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx2 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.1a.");

            uint groupFlags0 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset)..]);
            uint groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            uint renderState = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]); // CONFIRMED-variable
            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 12)..]);
            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]);
            offset += FxShortGroupHeaderSize;

            long geoBytes = (long)vertexCount * 44 + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx2 parse error: group[{g}] geometry truncated at offset {offset} " +
                    $"(need {geoBytes} bytes). spec: Docs/RE/formats/terrain_layers.md §1.6.");

            var vertices = new FxVertex44[(int)vertexCount];
            for (int v = 0; v < (int)vertexCount; v++)
                vertices[v] = ReadFxVertex44(span.Slice(offset + v * 44, 44));
            offset += (int)vertexCount * 44;
            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            groups[g] = new Fx2Group
            {
                GroupFlags0 = groupFlags0,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = ReadOnlyMemory<byte>.Empty,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx2Layer { GroupCount = groupCount, Groups = groups };
    }

    // ─── .fx3 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an <c>.fx3</c> terrain overlay layer file.
    /// Universal group-array layout: u32 group_count, then group_count × [44-byte extended group header + VF_36 vertices + u16 indices].
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 Format: CONFIRMED (3 samples, exact size match).
    /// spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count is the leading u32: CONFIRMED (two-witness).
    /// The FX3 group header is wider (44 bytes) than FX1/FX2 (20 bytes) due to extra leading words.
    /// render_state @ group+0x08 is CONFIRMED-variable (NOT a fixed constant=5).
    /// File-size formula: 4 + Σ over groups (44 + vertex_count × 36 + index_count × 2).
    /// </remarks>
    public static Fx3Layer ParseFx3(ReadOnlyMemory<byte> data) =>
        ParseFx3(data.Span, data);

    private static Fx3Layer ParseFx3(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // group_count u32 @ file offset 0x00.
        // spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count: CONFIRMED. §1.7 FX3.
        EnsureFx("fx3", span, 4);
        uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = 4;

        var groups = new Fx3Group[(int)groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            // Per-group header: 44 bytes (extended).
            // spec: Docs/RE/formats/terrain_layers.md §1.7 — group header 44 B: CONFIRMED.
            //   group_flags_0 @ +0x00, group_flags_1 @ +0x04, render_state @ +0x08 (CONFIRMED-variable),
            //   unknown_3..unknown_8 @ +0x0C..+0x23 (UNVERIFIED), vertex_count @ +0x24, index_count @ +0x28.
            if (offset + Fx3GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx3 parse error: group[{g}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.7.");

            uint groupFlags0 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset)..]); // +0x00 UNVERIFIED
            uint groupFlags1 = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]); // +0x04 UNVERIFIED
            uint renderState =
                BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]); // +0x08 CONFIRMED-variable
            // Extra words at group+0x0C..+0x23 (32 bytes, unknown_3..unknown_8) — all UNVERIFIED.
            // spec: Docs/RE/formats/terrain_layers.md §1.7 — unknown_3..unknown_8 @ +0x0C..+0x23: UNVERIFIED.
            ReadOnlyMemory<byte> rawExtra = backing.IsEmpty
                ? span.Slice(offset + 12, 32).ToArray()
                : backing.Slice(offset + 12, 32);
            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 36)..]); // +0x24 CONFIRMED
            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 40)..]); // +0x28 CONFIRMED
            offset += Fx3GroupHeaderSize;

            long geoBytes = (long)vertexCount * 36 + (long)indexCount * 2;
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
                GroupFlags0 = groupFlags0,
                GroupFlags1 = groupFlags1,
                RenderState = renderState,
                RawHeaderExtra = rawExtra,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx3Layer { GroupCount = groupCount, Groups = groups };
    }

    // ─── .fx4 ─────────────────────────────────────────────────────────────────

    // FX4 file header: 4 bytes (u32 tile_count).
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — u32 tile_count @ file offset 0x00: CONFIRMED (parser-verified).
    private const int Fx4FileTileCountSize = 4;

    // FX4 per-tile header: 48 bytes (fixed).
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — TileHeader (48 bytes): CONFIRMED (parser-verified).
    private const int Fx4TileHeaderSize = 48;

    // vertex_count @ tile-relative offset +0x28: CONFIRMED (parser-verified).
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — vertex_count u32 @ +0x28: CONFIRMED.
    private const int Fx4TileVertexCountOffset = 0x28; // 40

    // index_count @ tile-relative offset +0x2C: CONFIRMED (parser-verified).
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — index_count u32 @ +0x2C: CONFIRMED.
    private const int Fx4TileIndexCountOffset = 0x2C; // 44

    // VF_44 vertex stride: 44 bytes. CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — VertexData (vertex_count × 44, VF_44): CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_44 (44 B): CONFIRMED.
    private const int Fx4VertexStride = 44;

    /// <summary>
    /// Parses an <c>.fx4</c> terrain overlay layer file.
    /// Layout: u32 tile_count, then tile_count tiles (each: 48-byte TileHeader + VF_44 vertices + u16 indices).
    /// Vertex format: VF_44 (44 B). UV channels: 2. Per-vertex colour: yes.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded FX4 layer.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 FX4 Format: CONFIRMED-FROM-LOADER.
    /// File-size formula: 4 + Σ over tiles (48 + vertex_count × 44 + index_count × 2).
    /// For a single tile: 52 + vertex_count × 44 + index_count × 2.
    /// The loader reads the 48-byte tile header atomically and consumes only vertex_count @ +0x28
    /// and index_count @ +0x2C. The leading 40 bytes (tile_metadata) are read-but-not-consumed.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_metadata @ +0x00 (40 bytes): UNVERIFIED semantics.
    /// FX4 and FX5 share identical loader control flow; they differ only in vertex stride.
    /// spec: Docs/RE/formats/terrain_layers.md §1.11 — "FX4 and FX5 differ only in vertex stride": CONFIRMED.
    /// </remarks>
    public static Fx4Layer ParseFx4(ReadOnlyMemory<byte> data) =>
        ParseFx4(data.Span, data);

    private static Fx4Layer ParseFx4(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // tile_count u32 LE @ file offset 0x00.
        // spec: Docs/RE/formats/terrain_layers.md §1.11 — tile_count u32 @ 0x00: CONFIRMED.
        if (span.Length < Fx4FileTileCountSize)
            throw new InvalidDataException(
                $".fx4 parse error: buffer too short for tile_count field (need {Fx4FileTileCountSize}, " +
                $"got {span.Length}). spec: Docs/RE/formats/terrain_layers.md §1.11.");

        uint tileCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = Fx4FileTileCountSize;

        var tiles = new Fx4Tile[tileCount];

        for (uint t = 0; t < tileCount; t++)
        {
            // Per-tile header: 48 bytes (fixed, read atomically).
            // spec: Docs/RE/formats/terrain_layers.md §1.11 — TileHeader (48 bytes): CONFIRMED.
            if (offset + Fx4TileHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx4 parse error: tile[{t}] header truncated at offset {offset} " +
                    $"(need {Fx4TileHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.11.");

            // Store raw tile header for faithful round-trip.
            ReadOnlyMemory<byte> rawTileHdr = backing.IsEmpty
                ? span.Slice(offset, Fx4TileHeaderSize).ToArray()
                : backing.Slice(offset, Fx4TileHeaderSize);

            // vertex_count u32 @ tile-relative +0x28.
            // spec: Docs/RE/formats/terrain_layers.md §1.11 — vertex_count u32 @ +0x28: CONFIRMED.
            uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx4TileVertexCountOffset, 4));

            // index_count u32 @ tile-relative +0x2C.
            // spec: Docs/RE/formats/terrain_layers.md §1.11 — index_count u32 @ +0x2C: CONFIRMED.
            uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx4TileIndexCountOffset, 4));

            offset += Fx4TileHeaderSize;

            // Validate geometry block size before reading.
            long geoBytes = (long)vertexCount * Fx4VertexStride + (long)indexCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx4 parse error: tile[{t}] geometry truncated at offset {offset} — " +
                    $"need {geoBytes} bytes (vertexCount={vertexCount}×44 + indexCount={indexCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.11.");

            // VertexData: vertex_count × 44 bytes (VF_44).
            // spec: Docs/RE/formats/terrain_layers.md §1.11 — VertexData (vertex_count × 44, VF_44): CONFIRMED.
            var vertices = new FxVertex44[(int)vertexCount];
            for (int v = 0; v < (int)vertexCount; v++)
                vertices[v] = ReadFxVertex44(span.Slice(offset + v * Fx4VertexStride, Fx4VertexStride));
            offset += (int)vertexCount * Fx4VertexStride;

            // IndexData: index_count × 2 bytes (u16 triangle list).
            // spec: Docs/RE/formats/terrain_layers.md §1.11 — IndexData (index_count × 2, u16): CONFIRMED.
            var indices = ReadU16Indices(span, offset, (int)indexCount);
            offset += (int)indexCount * 2;

            tiles[t] = new Fx4Tile
            {
                RawTileHeader = rawTileHdr,
                VertexCount = vertexCount,
                IndexCount = indexCount,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx4Layer { TileCount = tileCount, Tiles = tiles };
    }

    // ─── .fx5 ─────────────────────────────────────────────────────────────────

    // FX5 uses the universal group-array model (§1.1a): u32 group_count + group_count × [ 48-byte group header + VF_36 vertices + u16 indices ].
    // vertex_count @ group-relative +0x28: CONFIRMED (§1.8).
    // index_count  @ group-relative +0x2C: CONFIRMED (§1.8).
    // FX5 and FX4 share identical loader control flow; they differ only in vertex stride (FX5 = VF_36/36 B, FX4 = VF_44/44 B).
    // spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Format: CONFIRMED (group-array model, §1.1a).
    // spec: Docs/RE/formats/terrain_layers.md §1.11 — "FX4 and FX5 differ only in vertex stride": CONFIRMED.
    private const int Fx5GroupHeaderSize = 48; // 48-byte per-group header (same as FX4)
    private const int Fx5VertexCountOffset = 0x28; // vertex_count @ group-relative +0x28
    private const int Fx5IndexCountOffset = 0x2C; // index_count  @ group-relative +0x2C

    /// <summary>
    /// Parses an <c>.fx5</c> terrain overlay layer file.
    /// Universal group-array layout: u32 group_count, then group_count × [ 48-byte group header + VF_36 vertices + u16 indices ].
    /// vertex_count @ group-relative +0x28; index_count @ group-relative +0x2C.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded FX5 layer.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Format: CONFIRMED (group-array model §1.1a).
    /// spec: Docs/RE/formats/terrain_layers.md §1.1a — universal group-array model: CONFIRMED (two-witness).
    /// File-size formula: 4 + Σ over groups (48 + vertex_count × 36 + index_count × 2).
    /// CORRECTION (CAMPAIGN VFS-MASTERY): Prior "40-byte Section_Header + 12-byte SubChunk_Header" model is REFUTED.
    /// The correct model is the universal group-array (§1.1a): 48-byte group header with vertex_count@+0x28 and index_count@+0x2C.
    /// </remarks>
    public static Fx5Layer ParseFx5(ReadOnlyMemory<byte> data) =>
        ParseFx5(data.Span, data);

    private static Fx5Layer ParseFx5(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // group_count u32 @ file offset 0x00.
        // spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count: CONFIRMED. §1.8 FX5.
        EnsureFx("fx5", span, 4);
        uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = 4;

        var sections = new Fx5Section[groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            // Per-group header: 48 bytes (fixed, read atomically).
            // spec: Docs/RE/formats/terrain_layers.md §1.8 — group header 48 B: CONFIRMED.
            if (offset + Fx5GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx5 parse error: group[{g}] header truncated at offset {offset} " +
                    $"(need {Fx5GroupHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.8.");

            // Store raw group header for faithful round-trip.
            ReadOnlyMemory<byte> rawGroupHdr = backing.IsEmpty
                ? span.Slice(offset, Fx5GroupHeaderSize).ToArray()
                : backing.Slice(offset, Fx5GroupHeaderSize);

            // vertex_count u32 @ group-relative +0x28.
            // spec: Docs/RE/formats/terrain_layers.md §1.8 — vertex_count u32 @ +0x28: CONFIRMED.
            uint vertCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx5VertexCountOffset, 4));

            // index_count u32 @ group-relative +0x2C.
            // spec: Docs/RE/formats/terrain_layers.md §1.8 — index_count u32 @ +0x2C: CONFIRMED.
            uint idxCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx5IndexCountOffset, 4));

            offset += Fx5GroupHeaderSize;

            // Validate geometry block.
            long needed = (long)vertCount * 36 + (long)idxCount * 2;
            if (offset + needed > span.Length)
                throw new InvalidDataException(
                    $".fx5 parse error: group[{g}] geometry truncated at offset {offset} — " +
                    $"need {needed} bytes (vertCount={vertCount}×36 + idxCount={idxCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.8.");

            // VertexData: vertex_count × 36 bytes (VF_36).
            // spec: Docs/RE/formats/terrain_layers.md §1.8 — VertexData (vertex_count × 36, VF_36): CONFIRMED.
            var vertices = ReadVf36Array(span, offset, (int)vertCount);
            offset += (int)vertCount * 36;

            // IndexData: index_count × 2 bytes (u16 triangle list).
            // spec: Docs/RE/formats/terrain_layers.md §1.3 — u16 triangle list: CONFIRMED.
            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            sections[g] = new Fx5Section
            {
                RawSectionHeader = rawGroupHdr,
                // RawSubChunkHeader is vestigial in the corrected model; store empty.
                RawSubChunkHeader = ReadOnlyMemory<byte>.Empty,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx5Layer { Sections = sections };
    }

    // ─── .fx7 ─────────────────────────────────────────────────────────────────

    // FX7 uses the universal group-array model (§1.1a): u32 group_count + group_count × [ 52-byte group header + VF_32 vertices + u16 indices ].
    // The FX7 group header is 52 bytes (13 × u32/f32 fields per §1.10), with:
    //   vertex_count @ group-relative +0x2C: DUAL-SAMPLE (§1.10).
    //   index_count  @ group-relative +0x30: DUAL-SAMPLE (§1.10).
    // Vertex format: VF_32 (32 B) — same as FX6, NO per-vertex colour field.
    // spec: Docs/RE/formats/terrain_layers.md §1.10 FX7 Format: PLAUSIBLE (dual-sample).
    // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_32 (32 B): CONFIRMED.
    private const int Fx7GroupHeaderSize = 52; // 52-byte per-group header (§1.10)
    private const int Fx7VertexCountOffset = 0x2C; // vertex_count @ group-relative +0x2C (§1.10)
    private const int Fx7IndexCountOffset = 0x30; // index_count  @ group-relative +0x30 (§1.10)
    private const int Fx7VertexStride = 32; // VF_32 stride: 32 bytes

    /// <summary>
    /// Parses an <c>.fx7</c> terrain overlay layer file.
    /// Universal group-array layout: u32 group_count, then group_count × [ 52-byte group header + VF_32 vertices + u16 indices ].
    /// vertex_count @ group-relative +0x2C; index_count @ group-relative +0x30.
    /// Vertex format: VF_32 (no per-vertex colour, same as FX6).
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded FX7 layer.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.10 FX7 Format: PLAUSIBLE (dual-sample, group-array model §1.1a).
    /// spec: Docs/RE/formats/terrain_layers.md §1.2 VF_32 (32 B): CONFIRMED (same as FX6).
    /// File-size formula: 4 + Σ over groups (52 + vertex_count × 32 + index_count × 2).
    /// </remarks>
    public static Fx7Layer ParseFx7(ReadOnlyMemory<byte> data) =>
        ParseFx7(data.Span, data);

    private static Fx7Layer ParseFx7(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // group_count u32 @ file offset 0x00.
        // spec: Docs/RE/formats/terrain_layers.md §1.1a — group_count: CONFIRMED. §1.10 FX7.
        EnsureFx("fx7", span, 4);
        uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        int offset = 4;

        var groups = new Fx7Group[groupCount];
        for (uint g = 0; g < groupCount; g++)
        {
            // Per-group header: 52 bytes (fixed, read atomically).
            // spec: Docs/RE/formats/terrain_layers.md §1.10 — group header 52 B: DUAL-SAMPLE.
            if (offset + Fx7GroupHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx7 parse error: group[{g}] header truncated at offset {offset} " +
                    $"(need {Fx7GroupHeaderSize}, remaining {span.Length - offset}). " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.10.");

            // Store raw group header for faithful round-trip.
            ReadOnlyMemory<byte> rawGroupHdr = backing.IsEmpty
                ? span.Slice(offset, Fx7GroupHeaderSize).ToArray()
                : backing.Slice(offset, Fx7GroupHeaderSize);

            // vertex_count u32 @ group-relative +0x2C.
            // spec: Docs/RE/formats/terrain_layers.md §1.10 — vertex_count u32 @ +0x2C: DUAL-SAMPLE.
            uint vertCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx7VertexCountOffset, 4));

            // index_count u32 @ group-relative +0x30.
            // spec: Docs/RE/formats/terrain_layers.md §1.10 — index_count u32 @ +0x30: DUAL-SAMPLE.
            uint idxCount = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(offset + Fx7IndexCountOffset, 4));

            offset += Fx7GroupHeaderSize;

            // Validate geometry block.
            long geoBytes = (long)vertCount * Fx7VertexStride + (long)idxCount * 2;
            if (offset + geoBytes > span.Length)
                throw new InvalidDataException(
                    $".fx7 parse error: group[{g}] geometry truncated at offset {offset} — " +
                    $"need {geoBytes} bytes (vertCount={vertCount}×32 + idxCount={idxCount}×2), " +
                    $"remaining {span.Length - offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.10.");

            // VertexData: vertex_count × 32 bytes (VF_32, no per-vertex colour).
            // spec: Docs/RE/formats/terrain_layers.md §1.10 — VertexData (vertex_count × 32, VF_32): DUAL-SAMPLE.
            // spec: Docs/RE/formats/terrain_layers.md §1.2 VF_32 (32 B): CONFIRMED.
            var vertices = new FxVertex32[(int)vertCount];
            for (int v = 0; v < (int)vertCount; v++)
                vertices[v] = ReadFxVertex32(span.Slice(offset + v * Fx7VertexStride, Fx7VertexStride));
            offset += (int)vertCount * Fx7VertexStride;

            // IndexData: index_count × 2 bytes (u16 triangle list).
            // spec: Docs/RE/formats/terrain_layers.md §1.3 — u16 triangle list: CONFIRMED.
            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            groups[g] = new Fx7Group
            {
                RawGroupHeader = rawGroupHdr,
                VertexCount = vertCount,
                IndexCount = idxCount,
                Vertices = vertices,
                Indices = indices,
            };
        }

        return new Fx7Layer { GroupCount = groupCount, Groups = groups };
    }

    // ─── .fx6 ─────────────────────────────────────────────────────────────────

    // FX6 global header: 32 bytes. SubChunk header: 8 bytes. Footer (non-final): 28 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §1.9 FX6 Format: CONFIRMED.
    private const int Fx6GlobalHeaderSize = 32;
    private const int Fx6SubChunkHeaderSize = 8;
    private const int Fx6FooterSize = 28;

    /// <summary>
    /// Parses an <c>.fx6</c> terrain overlay layer file.
    /// Global header: 32 bytes. sub_chunk_count VF_32 sub-chunks.
    /// The final sub-chunk has no footer.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.9 FX6 Format: CONFIRMED (3 samples, 29 444 bytes each).
    /// File-size formula: 32 + (sub_chunk_count - 1) × 736 + 708.
    /// Per-sub-chunk formula: 8 + vert_count × 32 + idx_count × 2 [+ 28 footer for non-final].
    /// </remarks>
    public static Fx6Layer ParseFx6(ReadOnlyMemory<byte> data) =>
        ParseFx6(data.Span, data);

    private static Fx6Layer ParseFx6(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < Fx6GlobalHeaderSize)
            throw new InvalidDataException(
                $".fx6 parse error: buffer too short for 32-byte global header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §1.9.");

        // sub_chunk_count u32 @ GlobalHeader+0x00. CONFIRMED.
        // spec: Docs/RE/formats/terrain_layers.md §1.9 GlobalHeader — sub_chunk_count u32 @ +0x00: CONFIRMED.
        uint subChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        ReadOnlyMemory<byte> rawGlobalRest = backing.IsEmpty
            ? span.Slice(4, 28).ToArray()
            : backing.Slice(4, 28);
        int offset = Fx6GlobalHeaderSize;

        var subChunks = new Fx6SubChunk[(int)subChunkCount];
        for (int s = 0; s < (int)subChunkCount; s++)
        {
            bool isFinal = s == (int)subChunkCount - 1;

            if (offset + Fx6SubChunkHeaderSize > span.Length)
                throw new InvalidDataException(
                    $".fx6 parse error: sub-chunk[{s}] header truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.9.");

            // SubChunk_Header: vert_count u32, idx_count u32. CONFIRMED.
            // spec: Docs/RE/formats/terrain_layers.md §1.9 SubChunk_Header — vert_count, idx_count: CONFIRMED.
            uint vertCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            uint idxCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            offset += Fx6SubChunkHeaderSize;

            long geoBytes = (long)vertCount * 32 + (long)idxCount * 2;
            if (offset + geoBytes + (isFinal ? 0 : Fx6FooterSize) > span.Length)
                throw new InvalidDataException(
                    $".fx6 parse error: sub-chunk[{s}] geometry or footer truncated at offset {offset}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.9.");

            var vertices = new FxVertex32[(int)vertCount];
            for (int v = 0; v < (int)vertCount; v++)
                vertices[v] = ReadFxVertex32(span.Slice(offset + v * 32, 32));
            offset += (int)vertCount * 32;

            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            ReadOnlyMemory<byte> rawFooter;
            if (!isFinal)
            {
                // Footer (28 bytes) — all fields UNVERIFIED.
                // spec: Docs/RE/formats/terrain_layers.md §1.9 Footer (28 bytes): UNVERIFIED.
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
                RawFooter = rawFooter,
            };
        }

        return new Fx6Layer
        {
            SubChunkCount = subChunkCount,
            RawGlobalHeaderRest = rawGlobalRest,
            SubChunks = subChunks,
        };
    }

    // ─── light*.bin ───────────────────────────────────────────────────────────

    // light*.bin: fixed size 5312 bytes (0x14C0).
    // spec: Docs/RE/formats/terrain_layers.md §6.1 Blob layout — "exactly 5312 bytes": CONFIRMED (parser).
    private const int LightBinFixedSize = 5312; // 0x14C0
    private const int LightKeyframeCount = 48;
    private const int LightKeyframeStride = 48;

    // Section offsets.
    // spec: Docs/RE/formats/terrain_layers.md §6.1:
    //   Section A (directional) @ 0x0000, 2304 bytes: CONFIRMED (parser).
    //   Section B (ambient) @ 0x0930, 2304 bytes: CONFIRMED (parser).
    //   Section C (fog) @ 0x1260, 192 bytes: MEDIUM.
    //   Trailing @ 0x1320: UNVERIFIED.
    private const int SectionAOffset = 0x0000; // 2304 bytes directional
    private const int SectionBOffset = 0x0930; // 2304 bytes ambient
    private const int SectionCOffset = 0x1260; // 192 bytes fog (48 × f32)
    private const int TrailingOffset = 0x1320;
    private const int TrailingSize = LightBinFixedSize - TrailingOffset; // 416

    /// <summary>
    /// Parses a <c>light%d.bin</c> sky-lighting keyframe file.
    /// Fixed size: 5312 bytes. No magic, no version.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §6.1 Blob layout: CONFIRMED (parser-analysis; no samples).
    /// </remarks>
    public static LightBinData ParseLightBin(ReadOnlyMemory<byte> data) =>
        ParseLightBin(data.Span, data);

    private static LightBinData ParseLightBin(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < LightBinFixedSize)
            throw new InvalidDataException(
                $"light*.bin parse error: expected {LightBinFixedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §6.1.");

        var dirKf = ReadLightKeyframes(span, SectionAOffset, LightKeyframeCount, backing);
        var ambKf = ReadLightKeyframes(span, SectionBOffset, LightKeyframeCount, backing);

        var fog = new float[LightKeyframeCount];
        for (int i = 0; i < LightKeyframeCount; i++)
            fog[i] = BinaryPrimitives.ReadSingleLittleEndian(span[(SectionCOffset + i * 4)..]);

        ReadOnlyMemory<byte> rawTrailing = backing.IsEmpty
            ? span.Slice(TrailingOffset, TrailingSize).ToArray()
            : backing.Slice(TrailingOffset, TrailingSize);

        return new LightBinData
        {
            DirectionalKeyframes = dirKf,
            AmbientKeyframes = ambKf,
            FogDensity = fog,
            RawTrailing = rawTrailing,
        };
    }

    private static LightKeyframe[] ReadLightKeyframes(
        ReadOnlySpan<byte> span, int sectionOffset, int count, ReadOnlyMemory<byte> backing)
    {
        var kf = new LightKeyframe[count];
        for (int i = 0; i < count; i++)
        {
            int slotOffset = sectionOffset + i * LightKeyframeStride;
            // sun_colour[0..2] f32×3 @ slot+0x00: CONFIRMED.
            // spec: Docs/RE/formats/terrain_layers.md §6.2.
            float c0 = BinaryPrimitives.ReadSingleLittleEndian(span[(slotOffset)..]);
            float c1 = BinaryPrimitives.ReadSingleLittleEndian(span[(slotOffset + 4)..]);
            float c2 = BinaryPrimitives.ReadSingleLittleEndian(span[(slotOffset + 8)..]);
            // Remaining 36 bytes (9 floats) PARTIALLY UNVERIFIED.
            ReadOnlyMemory<byte> rawRest = backing.IsEmpty
                ? span.Slice(slotOffset + 12, 36).ToArray()
                : backing.Slice(slotOffset + 12, 36);
            kf[i] = new LightKeyframe
            {
                SunColour0 = c0,
                SunColour1 = c1,
                SunColour2 = c2,
                RawRest = rawRest,
            };
        }

        return kf;
    }

    // ─── point_light*.bin ─────────────────────────────────────────────────────

    // Record stride: 60 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §7.2 Point-light record (60 bytes): CONFIRMED (parser).
    private const int PointLightRecordStride = 60;

    /// <summary>
    /// Parses a <c>point_light%d.bin</c> point-light array file.
    /// Header: 8 bytes. Records: count × 60 bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §7.1 File header (8 bytes): CONFIRMED (parser).
    /// spec: Docs/RE/formats/terrain_layers.md §7.2 Point-light record (60 bytes): CONFIRMED (parser).
    /// No sample bytes available.
    /// </remarks>
    public static PointLightBinData ParsePointLightBin(ReadOnlyMemory<byte> data) =>
        ParsePointLightBin(data.Span, data);

    private static PointLightBinData ParsePointLightBin(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < 8)
            throw new InvalidDataException(
                $"point_light*.bin parse error: buffer too short for 8-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §7.1.");

        uint intensityScale = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        long expectedSize = 8 + (long)count * PointLightRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"point_light*.bin parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §7.1.");

        var records = new PointLightRecord[(int)count];
        for (int i = 0; i < (int)count; i++)
        {
            int recOffset = 8 + i * PointLightRecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recOffset, PointLightRecordStride);
            // colour_group_1..3 (9 × f32, offsets +0..+0x23): CONFIRMED.
            // spec: Docs/RE/formats/terrain_layers.md §7.2 — colour_group_1..3: CONFIRMED.
            var cg1 = new float[3];
            var cg2 = new float[3];
            var cg3 = new float[3];
            for (int c = 0; c < 3; c++)
            {
                cg1[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(c * 4)..]);
                cg2[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(12 + c * 4)..]);
                cg3[c] = BinaryPrimitives.ReadSingleLittleEndian(rec[(24 + c * 4)..]);
            }

            // Raw positions+range: +0x24..+0x33 (16 bytes). UNVERIFIED.
            ReadOnlyMemory<byte> rawRest = backing.IsEmpty
                ? rec.Slice(36, 16).ToArray()
                : backing.Slice(recOffset + 36, 16);
            uint enabledFlag = BinaryPrimitives.ReadUInt32LittleEndian(rec[52..]);
            uint unknown4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[56..]);

            records[i] = new PointLightRecord
            {
                ColourGroup1 = cg1,
                ColourGroup2 = cg2,
                ColourGroup3 = cg3,
                RawRest = rawRest,
                EnabledFlag = enabledFlag,
                Unknown4 = unknown4,
            };
        }

        return new PointLightBinData { IntensityScale = intensityScale, Records = records };
    }

    // ─── wind*.bin ────────────────────────────────────────────────────────────

    // Wind keyframe stride: 24 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §8.2 Wind keyframe (24 bytes): CONFIRMED (header only for 0-entry samples).
    private const int WindKeyframeStride = 24;

    /// <summary>
    /// Parses a <c>wind%d.bin</c> foliage-sway keyframe file.
    /// Header: 8 bytes. Keyframes: count × 24 bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §8.1 File header: CONFIRMED (3 zero-entry samples).
    /// spec: Docs/RE/formats/terrain_layers.md §8.2 Wind keyframe record: UNVERIFIED (no non-zero samples).
    /// </remarks>
    public static WindBinData ParseWindBin(ReadOnlyMemory<byte> data) =>
        ParseWindBin(data.Span, data);

    private static WindBinData ParseWindBin(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < 8)
            throw new InvalidDataException(
                $"wind*.bin parse error: buffer too short for 8-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/terrain_layers.md §8.1.");

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint flag2 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        long expectedSize = 8 + (long)count * WindKeyframeStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"wind*.bin parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §8.1.");

        var keyframes = new ReadOnlyMemory<byte>[(int)count];
        for (int i = 0; i < (int)count; i++)
        {
            int kfOffset = 8 + i * WindKeyframeStride;
            keyframes[i] = backing.IsEmpty
                ? span.Slice(kfOffset, WindKeyframeStride).ToArray()
                : backing.Slice(kfOffset, WindKeyframeStride);
        }

        return new WindBinData { Count = count, Flag2 = flag2, RawKeyframes = keyframes };
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

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
        for (int i = 0; i < count; i++)
            arr[i] = ReadFxVertex36(span.Slice(start + i * 36, 36));
        return arr;
    }
}