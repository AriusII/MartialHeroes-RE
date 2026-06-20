using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
///     Converts a <see cref="BudScene" /> (.bud static-object cell) to a self-contained
///     GLB (binary glTF 2.0) stream.
///     Each <see cref="BudObject" /> becomes one glTF mesh / primitive.
///     A single glTF node references a multi-primitive mesh so all objects are in one file.
///     Coordinate-system conventions:
///     The .bud format uses a left-handed Y-up world space (D3D9 Windows client convention).
///     glTF 2.0 mandates right-handed Y-up.
///     Conversion: negate the X component of every position and normal to flip handedness.
///     spec: Docs/RE/formats/terrain_scene.md §Coordinate system —
///     "X and Z are horizontal plane axes; Y is the vertical/height axis": CONFIRMED.
///     spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED (same D3D9 convention).
///     glTF 2.0 spec §3.4: right-handed Y-up, −Z forward.
///     Winding order: negating X reverses the winding of every triangle (CW ↔ CCW).
///     To restore counter-clockwise front faces in glTF we swap index[1] and index[2]
///     within each triangle.
///     spec: Docs/RE/formats/terrain_scene.md §Index array — triangle list, 0-based u16: CONFIRMED.
///     glTF 2.0 spec §3.7.2.1: counter-clockwise winding defines front faces.
///     UV coordinates:
///     The .bud format stores UV as world-scale tiled values (observed range ~24–29).
///     No top-left → bottom-left flip is needed because glTF stores UVs with V=0 at
///     the top (same convention used by the .bud world-scale tiling).
///     spec: Docs/RE/formats/terrain_scene.md §Vertex record — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
///     Normals:
///     The .bud vertex record contains confirmed normal XYZ (unit-length float32).
///     The X component is negated (same handedness flip as position).
///     spec: Docs/RE/formats/terrain_scene.md §Vertex record — normal_x/y/z: CONFIRMED.
/// </summary>
public static class BudSceneGltfConverter
{
    // -------------------------------------------------------------------------
    // glTF / GLB constants
    // glTF 2.0 spec §binary-gltf §chunks
    // -------------------------------------------------------------------------

    /// <summary>GLB magic = ASCII "glTF" (LE u32). glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbMagic = 0x46546C67u;

    /// <summary>GLB version = 2. glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbVersion = 2u;

    /// <summary>GLB JSON chunk type. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeJson = 0x4E4F534Au; // 'JSON'

    /// <summary>GLB binary chunk type. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeBin = 0x004E4942u; // 'BIN\0'

    // glTF accessor componentType constants — glTF 2.0 spec §Accessor.componentType
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeFloat = 5126;

    // glTF buffer view target — glTF 2.0 spec §BufferView.target
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Writes a GLB containing all <see cref="BudObject" /> entries from <paramref name="scene" />
    ///     as separate mesh primitives (one primitive per object).
    ///     The stream does not need to be seekable.
    /// </summary>
    /// <param name="scene">Parsed .bud cell from <c>TerrainSceneParser</c>.</param>
    /// <param name="output">Destination stream (does not need to be seekable).</param>
    public static void WriteGlb(BudScene scene, Stream output)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(output);

        if (scene.Objects.Length == 0)
        {
            WriteEmptyGlb(output);
            return;
        }

        // ---- Step 1: Build per-object binary sections ----
        // Each object gets its own contiguous region in the shared binary buffer:
        //   [positions VEC3 f32] [normals VEC3 f32] [uvs VEC2 f32] [indices u16]
        // Sections are 4-byte aligned.
        var objectCount = scene.Objects.Length;
        var sections = new ObjectSection[objectCount];

        var bufferCursor = 0;
        for (var i = 0; i < objectCount; i++)
        {
            var obj = scene.Objects[i];
            var vertexCount = obj.Vertices.Length;
            var indexCount = obj.Indices.Length;

            var posLen = vertexCount * 3 * sizeof(float); // VEC3 f32
            var nrmLen = vertexCount * 3 * sizeof(float); // VEC3 f32
            var uvLen = vertexCount * 2 * sizeof(float); // VEC2 f32
            var idxLen = indexCount * sizeof(ushort); // u16

            var posOff = bufferCursor;
            var nrmOff = Align4(posOff + posLen);
            var uvOff = Align4(nrmOff + nrmLen);
            var idxOff = Align4(uvOff + uvLen);
            bufferCursor = Align4(idxOff + idxLen);

            sections[i] = new ObjectSection(
                vertexCount,
                indexCount,
                posOff, posLen,
                nrmOff, nrmLen,
                uvOff, uvLen,
                idxOff, idxLen);
        }

        var binaryBuffer = new byte[bufferCursor];

        // ---- Step 2: Fill binary buffer ----
        for (var i = 0; i < objectCount; i++)
        {
            var obj = scene.Objects[i];
            var sec = sections[i];
            WriteObjectBinary(binaryBuffer, obj, sec);
        }

        // ---- Step 3: Build JSON ----
        var json = BuildJson(scene, sections, binaryBuffer.Length);

        // ---- Step 4: Write GLB ----
        WriteGlbChunks(output, json, binaryBuffer);
    }

    // -------------------------------------------------------------------------
    // Per-object binary fill
    // -------------------------------------------------------------------------

    private static void WriteObjectBinary(byte[] buf, BudObject obj, ObjectSection sec)
    {
        var vertexCount = obj.Vertices.Length;
        var indexCount = obj.Indices.Length;

        // ---- Positions (X-flipped for left-handed → right-handed conversion) ----
        // spec: Docs/RE/formats/terrain_scene.md §Vertex record — pos_x/y/z: CONFIRMED.
        // glTF 2.0 spec §3.4: negate X to convert D3D9 LH to glTF RH.
        var cursor = sec.PosOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -vert.PosX); // X negated
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.PosY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), vert.PosZ);
            cursor += 12;
        }

        // ---- Normals (X-flipped, same convention as positions) ----
        // spec: Docs/RE/formats/terrain_scene.md §Vertex record — normal_x @ +0x0C: CONFIRMED.
        // Unit-length confirmed by spec.
        cursor = sec.NrmOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -vert.NormalX); // X negated
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.NormalY);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), vert.NormalZ);
            cursor += 12;
        }

        // ---- UVs ----
        // spec: Docs/RE/formats/terrain_scene.md §Vertex record — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
        // World-scale tiled values; no V-flip applied (no top-left/bottom-left mismatch documented
        // for terrain geometry UVs — the spec notes "tiled / world-scale mapping").
        cursor = sec.UvOff;
        for (var v = 0; v < vertexCount; v++)
        {
            var vert = obj.Vertices[v];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), vert.UvU);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), vert.UvV);
            cursor += 8;
        }

        // ---- Indices (winding-swapped to maintain CCW in glTF after X-flip) ----
        // spec: Docs/RE/formats/terrain_scene.md §Index array — u16 triangle list: CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: CCW front faces.
        // Negating X reverses winding CW↔CCW; swap i1↔i2 to restore CCW.
        cursor = sec.IdxOff;
        for (var tri = 0; tri < indexCount / 3; tri++)
        {
            var i0 = obj.Indices[tri * 3 + 0];
            var i1 = obj.Indices[tri * 3 + 1];
            var i2 = obj.Indices[tri * 3 + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), i0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), i2); // swapped
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), i1); // swapped
            cursor += 6;
        }
    }

    // -------------------------------------------------------------------------
    // glTF JSON construction
    // -------------------------------------------------------------------------

    private static string BuildJson(BudScene scene, ObjectSection[] sections, int bufferByteLength)
    {
        var objectCount = scene.Objects.Length;

        // Accessor and bufferView indices:
        // Per object: [pos acc, nrm acc, uv acc, idx acc] → 4 accessors × objectCount
        // Per object: [pos bv, nrm bv, uv bv, idx bv]    → 4 buffer views × objectCount

        var sb = new StringBuilder(512 + objectCount * 256);
        sb.Append('{');

        // asset
        sb.Append(
            "\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.BudSceneGltfConverter\"},");

        // scene / nodes / meshes
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        // mesh — one primitive per BudObject
        sb.Append("\"meshes\":[{\"primitives\":[");
        for (var i = 0; i < objectCount; i++)
        {
            if (i > 0) sb.Append(',');
            var accBase = i * 4; // 4 accessors per object: pos=0, nrm=1, uv=2, idx=3

            sb.Append('{');
            sb.Append("\"attributes\":{");
            sb.Append($"\"POSITION\":{accBase},");
            sb.Append($"\"NORMAL\":{accBase + 1},");
            sb.Append($"\"TEXCOORD_0\":{accBase + 2}");
            sb.Append("},");
            sb.Append($"\"indices\":{accBase + 3}");

            // Attach extras: texId for material resolution by the consumer.
            // spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id @ +0x01: PARTIAL.
            // 1-based index into the TEXTURES list of the enclosing BUILDING section.
            var obj = scene.Objects[i];
            sb.Append($",\"extras\":{{\"texId\":{obj.TexId},\"typeByte\":{obj.TypeByte}}}");
            sb.Append('}');
        }

        sb.Append("]}],");

        // accessors — 4 per object: POSITION(0), NORMAL(1), TEXCOORD_0(2), indices(3)
        // We track a global accessor separator flag to avoid leading/trailing commas.
        sb.Append("\"accessors\":[");
        var firstAcc = true;
        for (var i = 0; i < objectCount; i++)
        {
            var sec = sections[i];
            var obj = scene.Objects[i];
            var bvBase = i * 4; // 4 buffer views per object

            // Compute position min/max (with X-flip) for the required accessor bounds.
            // glTF 2.0 spec §Accessor: min/max required for POSITION.
            ComputePosMinMax(obj.Vertices,
                out var minX, out var minY, out var minZ,
                out var maxX, out var maxY, out var maxZ);

            // accessor: POSITION VEC3 f32
            if (!firstAcc) sb.Append(',');
            firstAcc = false;
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC3\",");
            sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],");
            sb.Append($"\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
            sb.Append('}');

            // accessor: NORMAL VEC3 f32
            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 1},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC3\"");
            sb.Append('}');

            // accessor: TEXCOORD_0 VEC2 f32
            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 2},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},");
            sb.Append($"\"count\":{sec.VertexCount},");
            sb.Append("\"type\":\"VEC2\"");
            sb.Append('}');

            // accessor: indices SCALAR u16
            sb.Append(',');
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvBase + 3},");
            sb.Append("\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeUnsignedShort},");
            sb.Append($"\"count\":{sec.IndexCount},");
            sb.Append("\"type\":\"SCALAR\"");
            sb.Append('}');
        }

        sb.Append("],");

        // bufferViews — 4 per object: positions, normals, UVs, indices
        sb.Append("\"bufferViews\":[");
        var firstBv = true;
        for (var i = 0; i < objectCount; i++)
        {
            var sec = sections[i];

            // bufferView: positions
            if (!firstBv) sb.Append(',');
            firstBv = false;
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.PosOff},");
            sb.Append($"\"byteLength\":{sec.PosLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            // bufferView: normals
            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.NrmOff},");
            sb.Append($"\"byteLength\":{sec.NrmLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            // bufferView: UVs
            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.UvOff},");
            sb.Append($"\"byteLength\":{sec.UvLen},");
            sb.Append($"\"target\":{TargetArrayBuffer}");
            sb.Append('}');

            // bufferView: indices
            sb.Append(',');
            sb.Append('{');
            sb.Append("\"buffer\":0,");
            sb.Append($"\"byteOffset\":{sec.IdxOff},");
            sb.Append($"\"byteLength\":{sec.IdxLen},");
            sb.Append($"\"target\":{TargetElementArrayBuffer}");
            sb.Append('}');
        }

        sb.Append("],");

        // buffer
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");

        sb.Append('}');
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // GLB container writing
    // glTF 2.0 spec §binary-gltf
    // -------------------------------------------------------------------------

    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = Align4(jsonBytes.Length);
        var binPadded = Align4(binaryBuffer.Length);

        var totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..], totalLength);
        output.Write(hdr);

        // JSON chunk
        Span<byte> jsonHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHdr[4..], ChunkTypeJson);
        output.Write(jsonHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        // BIN chunk
        Span<byte> binHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binHdr[4..], ChunkTypeBin);
        output.Write(binHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }

    private static void WriteEmptyGlb(Stream output)
    {
        // Emit a minimal valid GLB with zero objects (empty scene).
        const string emptyJson =
            "{\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.BudSceneGltfConverter\"}," +
            "\"scene\":0,\"scenes\":[{\"nodes\":[]}],\"meshes\":[],\"buffers\":[]}";
        WriteGlbChunks(output, emptyJson, Array.Empty<byte>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void WritePadding(Stream output, int actual, int padded, byte padByte)
    {
        var pad = padded - actual;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int v)
    {
        return (v + 3) & ~3;
    }

    private static string F(float v)
    {
        return v.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static void ComputePosMinMax(
        BudVertex[] vertices,
        out float minX, out float minY, out float minZ,
        out float maxX, out float maxY, out float maxZ)
    {
        if (vertices.Length == 0)
        {
            minX = minY = minZ = 0f;
            maxX = maxY = maxZ = 0f;
            return;
        }

        minX = maxX = vertices[0].PosX;
        minY = maxY = vertices[0].PosY;
        minZ = maxZ = vertices[0].PosZ;

        for (var i = 1; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v.PosX < minX) minX = v.PosX;
            if (v.PosX > maxX) maxX = v.PosX;
            if (v.PosY < minY) minY = v.PosY;
            if (v.PosY > maxY) maxY = v.PosY;
            if (v.PosZ < minZ) minZ = v.PosZ;
            if (v.PosZ > maxZ) maxZ = v.PosZ;
        }
    }

    // -------------------------------------------------------------------------
    // Per-object buffer section descriptor (layout within the shared binary buffer)
    // -------------------------------------------------------------------------

    private readonly record struct ObjectSection(
        int VertexCount,
        int IndexCount,
        int PosOff,
        int PosLen,
        int NrmOff,
        int NrmLen,
        int UvOff,
        int UvLen,
        int IdxOff,
        int IdxLen);
}