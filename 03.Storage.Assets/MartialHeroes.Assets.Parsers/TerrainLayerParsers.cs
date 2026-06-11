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

    // ─── .fx1 ─────────────────────────────────────────────────────────────────

    // FX1 header size: 24 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 Header (24 bytes): CONFIRMED.
    private const int Fx1HeaderSize = 24;

    /// <summary>
    /// Parses an <c>.fx1</c> terrain overlay layer file.
    /// Header: 24 bytes. Vertex format: VF_36 (36 B). UV channels: 1.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS.</param>
    /// <returns>Decoded FX1 layer.</returns>
    /// <exception cref="InvalidDataException">Thrown on truncation.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 Format: CONFIRMED (3 samples, exact size match).
    /// File-size formula: 24 + mesh_count × 36 + index_count × 2.
    /// </remarks>
    public static Fx1Layer ParseFx1(ReadOnlyMemory<byte> data) =>
        ParseFx1(data.Span, data);

    private static Fx1Layer ParseFx1(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx1", span, Fx1HeaderSize);
        // spec: Docs/RE/formats/terrain_layers.md §1.5 Header:
        //   type_tag u32 @ 0x00: CONFIRMED. unknown_1 @ 0x04: UNVERIFIED. unknown_2 @ 0x08: UNVERIFIED.
        //   render_state @ 0x0C: UNVERIFIED. mesh_count @ 0x10: CONFIRMED. index_count @ 0x14: CONFIRMED.
        uint typeTag = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint unknown1 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint unknown2 = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        uint renderState = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        uint meshCount = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
        uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);

        long expectedSize = Fx1HeaderSize + (long)meshCount * 36 + (long)indexCount * 2;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".fx1 parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §1.5.");

        var vertices = ReadVf36Array(span, Fx1HeaderSize, (int)meshCount);
        int idxOffset = Fx1HeaderSize + (int)meshCount * 36;
        var indices = ReadU16Indices(span, idxOffset, (int)indexCount);

        return new Fx1Layer
        {
            TypeTag = typeTag,
            Unknown1 = unknown1,
            Unknown2 = unknown2,
            RenderState = renderState,
            Vertices = vertices,
            Indices = indices,
        };
    }

    // ─── .fx2 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an <c>.fx2</c> terrain overlay layer file.
    /// Header: 24 bytes (same as FX1). Vertex format: VF_44 (44 B). UV channels: 2.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.6 FX2 Format: CONFIRMED (3 samples, exact size match).
    /// File-size formula: 24 + mesh_count × 44 + index_count × 2.
    /// </remarks>
    public static Fx2Layer ParseFx2(ReadOnlyMemory<byte> data) =>
        ParseFx2(data.Span);

    private static Fx2Layer ParseFx2(ReadOnlySpan<byte> span)
    {
        EnsureFx("fx2", span, Fx1HeaderSize);
        // spec: Docs/RE/formats/terrain_layers.md §1.6 Header — same fields as FX1: CONFIRMED.
        uint typeTag = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint unknown1 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint unknown2 = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        uint renderState = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        uint meshCount = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
        uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);

        long expectedSize = Fx1HeaderSize + (long)meshCount * 44 + (long)indexCount * 2;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".fx2 parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §1.6.");

        var vertices = new FxVertex44[(int)meshCount];
        for (int i = 0; i < (int)meshCount; i++)
            vertices[i] = ReadFxVertex44(span.Slice(Fx1HeaderSize + i * 44, 44));
        int idxOffset = Fx1HeaderSize + (int)meshCount * 44;
        var indices = ReadU16Indices(span, idxOffset, (int)indexCount);

        return new Fx2Layer
        {
            TypeTag = typeTag,
            Unknown1 = unknown1,
            Unknown2 = unknown2,
            RenderState = renderState,
            Vertices = vertices,
            Indices = indices,
        };
    }

    // ─── .fx3 ─────────────────────────────────────────────────────────────────

    // FX3 header size: 48 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 Header (48 bytes): CONFIRMED.
    private const int Fx3HeaderSize = 48;

    /// <summary>
    /// Parses an <c>.fx3</c> terrain overlay layer file.
    /// Header: 48 bytes (extended). Vertex format: VF_36.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 Format: CONFIRMED (3 samples, exact size match).
    /// File-size formula: 48 + mesh_count × 36 + index_count × 2.
    /// </remarks>
    public static Fx3Layer ParseFx3(ReadOnlyMemory<byte> data) =>
        ParseFx3(data.Span, data);

    private static Fx3Layer ParseFx3(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        EnsureFx("fx3", span, Fx3HeaderSize);
        // spec: Docs/RE/formats/terrain_layers.md §1.7 Header:
        //   type_tag @ 0x00: CONFIRMED. unknown_1 @ 0x04, unknown_2 @ 0x08: UNVERIFIED.
        //   render_state @ 0x0C: UNVERIFIED. unknown_3..8 @ 0x10–0x27: UNVERIFIED.
        //   mesh_count @ 0x28: CONFIRMED. index_count @ 0x2C: CONFIRMED.
        uint typeTag = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint unknown1 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint unknown2 = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        uint renderState = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        // Extended header bytes 0x10–0x27 (32 bytes) — all UNVERIFIED.
        // spec: Docs/RE/formats/terrain_layers.md §1.7 — unknown_3..unknown_8: UNVERIFIED.
        ReadOnlyMemory<byte> rawExtra = backing.IsEmpty
            ? span.Slice(16, 32).ToArray()
            : backing.Slice(16, 32);

        uint meshCount = BinaryPrimitives.ReadUInt32LittleEndian(span[40..]);
        uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span[44..]);

        long expectedSize = Fx3HeaderSize + (long)meshCount * 36 + (long)indexCount * 2;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".fx3 parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain_layers.md §1.7.");

        var vertices = ReadVf36Array(span, Fx3HeaderSize, (int)meshCount);
        int idxOffset = Fx3HeaderSize + (int)meshCount * 36;
        var indices = ReadU16Indices(span, idxOffset, (int)indexCount);

        return new Fx3Layer
        {
            TypeTag = typeTag,
            Unknown1 = unknown1,
            Unknown2 = unknown2,
            RenderState = renderState,
            RawHeaderExtra = rawExtra,
            Vertices = vertices,
            Indices = indices,
        };
    }

    // ─── .fx5 ─────────────────────────────────────────────────────────────────

    // FX5 section header: 40 bytes. SubChunk header: 12 bytes.
    // spec: Docs/RE/formats/terrain_layers.md §1.8 — Section_Header 40 B + SubChunk_Header 12 B: CONFIRMED (single-section).
    private const int Fx5SectionHeaderSize = 40;
    private const int Fx5SubChunkHeaderSize = 12;

    /// <summary>
    /// Parses an <c>.fx5</c> terrain overlay layer file.
    /// Sections are parsed sequentially until end of buffer.
    /// Multi-section boundary is UNVERIFIED beyond section 0.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Format: CONFIRMED (single-section).
    /// Multi-section (Known Unknown #1): UNVERIFIED.
    /// </remarks>
    public static Fx5Layer ParseFx5(ReadOnlyMemory<byte> data) =>
        ParseFx5(data.Span, data);

    private static Fx5Layer ParseFx5(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        var sections = new List<Fx5Section>();
        int offset = 0;

        while (offset < span.Length)
        {
            if (offset + Fx5SectionHeaderSize + Fx5SubChunkHeaderSize > span.Length)
                break; // Trailing data too short for a full section — stop gracefully.

            // Section_Header (40 bytes) — all semantics UNVERIFIED except direction floats.
            // spec: Docs/RE/formats/terrain_layers.md §1.8 Section_Header (40 bytes): UNVERIFIED.
            ReadOnlyMemory<byte> rawSectionHdr = backing.IsEmpty
                ? span.Slice(offset, Fx5SectionHeaderSize).ToArray()
                : backing.Slice(offset, Fx5SectionHeaderSize);
            offset += Fx5SectionHeaderSize;

            // SubChunk_Header (12 bytes): flags u32 + vert_count u32 + idx_count u32.
            // spec: Docs/RE/formats/terrain_layers.md §1.8 SubChunk_Header — vert_count CONFIRMED, idx_count CONFIRMED.
            ReadOnlyMemory<byte> rawSubHdr = backing.IsEmpty
                ? span.Slice(offset, Fx5SubChunkHeaderSize).ToArray()
                : backing.Slice(offset, Fx5SubChunkHeaderSize);
            ReadOnlySpan<byte> subHdrSpan = span.Slice(offset, Fx5SubChunkHeaderSize);
            // flags u32 @ +0: UNVERIFIED.
            uint vertCount = BinaryPrimitives.ReadUInt32LittleEndian(subHdrSpan[4..]);
            uint idxCount = BinaryPrimitives.ReadUInt32LittleEndian(subHdrSpan[8..]);
            offset += Fx5SubChunkHeaderSize;

            long needed = (long)vertCount * 36 + (long)idxCount * 2;
            if (offset + needed > span.Length)
                throw new InvalidDataException(
                    $".fx5 parse error: section {sections.Count} geometry truncated — " +
                    $"need {needed} bytes at offset {offset}, buffer length {span.Length}. " +
                    "spec: Docs/RE/formats/terrain_layers.md §1.8.");

            var vertices = ReadVf36Array(span, offset, (int)vertCount);
            offset += (int)vertCount * 36;
            var indices = ReadU16Indices(span, offset, (int)idxCount);
            offset += (int)idxCount * 2;

            sections.Add(new Fx5Section
            {
                RawSectionHeader = rawSectionHdr,
                RawSubChunkHeader = rawSubHdr,
                Vertices = vertices,
                Indices = indices,
            });
        }

        return new Fx5Layer { Sections = sections.ToArray() };
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