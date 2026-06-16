using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Converts <see cref="StaticMesh"/> (and optionally <see cref="SkinnedMesh"/> + <see cref="Skeleton"/>)
/// to a self-contained GLB (binary glTF 2.0) stream.
///
/// Coordinate-system conventions handled:
///   Legacy format uses a left-handed Y-up coordinate system (matches D3D9 default).
///   glTF 2.0 mandates right-handed Y-up.
///
///   STATIC MESH path (<see cref="WriteGlb(StaticMesh,Stream)"/>):
///     Handedness flip: negate the X component of every position — a standalone static mesh
///     with no skeleton to mirror against. Winding swap (i1↔i2) restores CCW front faces.
///     spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED positions.
///
///   SKINNED MESH path (<see cref="WriteGlb(SkinnedMesh,Skeleton?,Stream,AnimationClip[])"/>):
///     Handedness flip: uniform Z-negate (x,y,z) → (x,y,−z), applied identically to bone
///     bind translations, mesh vertex positions, and keyframe translations. Quaternions map
///     as (x,y,z,w) → (−x,−y,z,w) (negate the two components orthogonal to Z).
///     Winding swap (i1↔i2) still required — Z-negate also has det = −1.
///     spec: Docs/RE/specs/skinning.md §7 'no axis negation inside skinning math'; §8(b)
///       'Pick one handedness conversion — the world Z-negate — and apply it uniformly'.
///     Mirrors Helpers/WorldCoordinates.SkinToGodot / SkinQuatToGodot in the Godot layer.
///     NOTE: the static mesh X-negate is INTENTIONALLY DIFFERENT from the skinned Z-negate.
///     See skinning.md §8(b) for rationale. Do not unify without a deliberate spec decision.
///
///   UV origin: the parser already stores V = 1.0 - v_on_disk so the UV origin is
///   bottom-left, which matches glTF / OpenGL convention. No further V-flip is needed.
///   spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v". CONFIRMED.
///
///   Index representation: parser carries u16 indices (max 65535 per mesh).
///   If any index exceeds 65535 the emitter upgrades to u32 automatically.
///   spec: Docs/RE/formats/mesh.md §Index list — vertex_index[n]: CONFIRMED.
///
/// Output is DETERMINISTIC: no timestamps, no GUIDs, no random ordering.
/// </summary>
public static class GltfConverter
{
    // -------------------------------------------------------------------------
    // glTF / GLB constants
    // glTF 2.0 spec §binary-gltf §chunks
    // -------------------------------------------------------------------------

    /// <summary>GLB magic = ASCII "glTF" (LE u32). glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbMagic = 0x46546C67u; // 'glTF'

    /// <summary>GLB version = 2. glTF 2.0 spec §binary-gltf §Header.</summary>
    private const uint GlbVersion = 2u;

    /// <summary>GLB chunk type for JSON. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeJson = 0x4E4F534Au; // 'JSON'

    /// <summary>GLB chunk type for binary buffer. glTF 2.0 spec §binary-gltf §Chunks.</summary>
    private const uint ChunkTypeBin = 0x004E4942u; // 'BIN\0'

    // glTF accessor componentType constants — glTF 2.0 spec §Accessor.componentType
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    // glTF buffer view target constants — glTF 2.0 spec §BufferView.target
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a GLB containing the given static mesh to <paramref name="output"/>.
    /// The stream does not need to be seekable.
    /// </summary>
    public static void WriteGlb(StaticMesh mesh, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        bool use32BitIndices = mesh.Positions.Length > ushort.MaxValue;

        byte[] binaryBuffer = BuildBinaryBuffer(mesh, use32BitIndices,
            out int posOffset, out int posLength,
            out int uvOffset, out int uvLength,
            out int idxOffset, out int idxLength);

        string json = BuildJson(mesh, binaryBuffer.Length, use32BitIndices,
            posOffset, posLength, uvOffset, uvLength, idxOffset, idxLength);

        WriteGlbChunks(output, json, binaryBuffer);
    }

    // -------------------------------------------------------------------------
    // Binary buffer construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packs positions (VEC3 f32), UVs (VEC2 f32), and indices (u16 or u32) into one buffer.
    /// Each section is 4-byte aligned as required by glTF 2.0 spec §BufferView.byteOffset alignment.
    /// </summary>
    private static byte[] BuildBinaryBuffer(
        StaticMesh mesh, bool use32Bit,
        out int posOffset, out int posLength,
        out int uvOffset, out int uvLength,
        out int idxOffset, out int idxLength)
    {
        int vertexCount = mesh.Positions.Length;
        int indexCount = mesh.Indices.Length;

        posLength = vertexCount * 3 * sizeof(float); // VEC3 f32
        uvLength = vertexCount * 2 * sizeof(float); // VEC2 f32
        int indexStride = use32Bit ? sizeof(uint) : sizeof(ushort);
        idxLength = indexCount * indexStride;

        // Align each section to 4 bytes
        posOffset = 0;
        int posEnd = posOffset + posLength;
        uvOffset = Align4(posEnd);
        int uvEnd = uvOffset + uvLength;
        idxOffset = Align4(uvEnd);
        int bufSize = Align4(idxOffset + idxLength);

        byte[] buf = new byte[bufSize];

        // Write positions
        // Coordinate flip: negate X to convert left-handed D3D9 → right-handed glTF.
        // spec: Docs/RE/formats/mesh.md §Vertex list — pos_x/y/z: CONFIRMED.
        // glTF 2.0 spec §3.4: right-handed Y-up.
        int cursor = posOffset;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec3 p = mesh.Positions[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -p.X); // flip X
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), p.Z);
            cursor += 12;
        }

        // Write UVs
        // V is already bottom-left (parser applied 1.0 - v_on_disk).
        // spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: CONFIRMED.
        cursor = uvOffset;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec2 uv = mesh.Uvs[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), uv.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), uv.Y);
            cursor += 8;
        }

        // Write indices
        // Winding order: the handedness flip (negate X) also reverses winding order.
        // To maintain counter-clockwise front faces in glTF we swap index[1] and index[2]
        // in each triangle, reversing the winding.
        // spec: Docs/RE/formats/mesh.md §Index list — "winding order as stored; no winding reversal on load". CONFIRMED.
        // glTF 2.0 spec §3.7.2.1: counter-clockwise winding defines the front face.
        cursor = idxOffset;
        for (int tri = 0; tri < indexCount / 3; tri++)
        {
            ushort i0 = mesh.Indices[tri * 3 + 0];
            ushort i1 = mesh.Indices[tri * 3 + 1];
            ushort i2 = mesh.Indices[tri * 3 + 2];

            if (use32Bit)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 4), i2); // swapped
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 8), i1); // swapped
                cursor += 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), i2); // swapped
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), i1); // swapped
                cursor += 6;
            }
        }

        return buf;
    }

    // -------------------------------------------------------------------------
    // glTF JSON construction
    // -------------------------------------------------------------------------

    private static string BuildJson(
        StaticMesh mesh, int bufferByteLength, bool use32Bit,
        int posOffset, int posLength,
        int uvOffset, int uvLength,
        int idxOffset, int idxLength)
    {
        int vertexCount = mesh.Positions.Length;
        int indexCount = mesh.Indices.Length;

        // Compute accessor min/max for positions (required by glTF spec §Accessor).
        ComputePositionMinMax(mesh.Positions,
            out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ);

        var sb = new StringBuilder(512);
        sb.Append('{');

        // asset
        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping\"},");

        // scene
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        // meshes — one primitive with POSITION, TEXCOORD_0, and indices
        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append("\"POSITION\":0,"); // accessor 0
        sb.Append("\"TEXCOORD_0\":1"); // accessor 1
        sb.Append("},");
        sb.Append("\"indices\":2"); // accessor 2
        sb.Append("}]}],");

        // accessors
        sb.Append("\"accessors\":[");

        // accessor 0 — positions VEC3 f32
        // X has been negated; update min/max accordingly.
        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\",");
        // min/max: X was negated so swap and negate
        sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],");
        sb.Append($"\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
        sb.Append("},");

        // accessor 1 — UVs VEC2 f32  (no min/max required for TEXCOORD)
        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC2\"");
        sb.Append("},");

        // accessor 2 — indices SCALAR u16 or u32
        int indexComponentType = use32Bit ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;
        sb.Append('{');
        sb.Append("\"bufferView\":2,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],"); // end accessors

        // bufferViews
        sb.Append("\"bufferViews\":[");

        // bufferView 0 — positions
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOffset},");
        sb.Append($"\"byteLength\":{posLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 1 — UVs
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{uvOffset},");
        sb.Append($"\"byteLength\":{uvLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        // bufferView 2 — indices
        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOffset},");
        sb.Append($"\"byteLength\":{idxLength},");
        sb.Append($"\"target\":{TargetElementArrayBuffer}");
        sb.Append('}');

        sb.Append("],"); // end bufferViews

        // buffer
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");

        sb.Append('}');
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // GLB container writing
    // glTF 2.0 spec §binary-gltf
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the 12-byte GLB header followed by the JSON chunk and BIN chunk.
    /// Chunk data is padded to 4-byte boundaries (JSON with 0x20 spaces, BIN with 0x00 bytes).
    /// glTF 2.0 spec §binary-gltf §Padding.
    /// </summary>
    private static void WriteGlbChunks(Stream output, string json, byte[] binaryBuffer)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPadded = Align4(jsonBytes.Length);
        int binPadded = Align4(binaryBuffer.Length);

        // Total file length:
        // 12 (header) + 8 (JSON chunk header) + jsonPadded + 8 (BIN chunk header) + binPadded
        uint totalLength = (uint)(12 + 8 + jsonPadded + 8 + binPadded);

        Span<byte> hdr = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[4..], GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr[8..], totalLength);
        output.Write(hdr);

        // JSON chunk
        Span<byte> jsonChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr[4..], ChunkTypeJson);
        output.Write(jsonChunkHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20); // space-padded

        // BIN chunk
        Span<byte> binChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr[4..], ChunkTypeBin);
        output.Write(binChunkHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00); // zero-padded
    }

    private static void WritePadding(Stream output, int actualLength, int paddedLength, byte padByte)
    {
        int pad = paddedLength - actualLength;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int value) => (value + 3) & ~3;

    /// <summary>
    /// Computes min/max of the original (pre-conversion) positions.
    /// The caller is responsible for applying the handedness conversion (X-negate for
    /// static meshes; Z-negate for skinned meshes) to the min/max values before emitting
    /// the glTF accessor.
    /// </summary>
    private static void ComputePositionMinMax(
        Vec3[] positions,
        out float minX, out float minY, out float minZ,
        out float maxX, out float maxY, out float maxZ)
    {
        if (positions.Length == 0)
        {
            minX = minY = minZ = 0f;
            maxX = maxY = maxZ = 0f;
            return;
        }

        minX = maxX = positions[0].X;
        minY = maxY = positions[0].Y;
        minZ = maxZ = positions[0].Z;

        for (int i = 1; i < positions.Length; i++)
        {
            Vec3 p = positions[i];
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }

    /// <summary>Formats a float with enough precision to be round-trippable in JSON.</summary>
    private static string F(float v) =>
        v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------------
    // SkinnedMesh overload — FULL skinning (wave-7 stub replaced)
    //
    // Skinning conventions resolved from spec:
    //   - Bone record on-disk: 36 bytes. self_id, parent_id (u32 LE, low byte used),
    //     local_translation (f32[3]), local_rotation XYZW (f32[4]).
    //     spec: Docs/RE/formats/mesh.md §BndBone on-disk record — 36 bytes: CONFIRMED.
    //   - Quaternion order: XYZW (scalar W last).
    //     spec: Docs/RE/formats/mesh.md §Quaternion component order: CONFIRMED.
    //   - Root bone sentinel: self_id==0 && parent_id==0 (low bytes).
    //     spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
    //   - Weight normalization: per-vertex sum must equal 1.0; enforced by the parser.
    //     spec: Docs/RE/formats/mesh.md §Weight record — "engine normalises weights per vertex": CONFIRMED.
    //   - glTF skinning: JOINTS_0 (VEC4 UNSIGNED_SHORT) + WEIGHTS_0 (VEC4 FLOAT normalised).
    //     Up to 4 influences per vertex; unused slots get joint index 0 and weight 0.
    //     glTF 2.0 spec §Skins, §Accessor.
    //   - Inverse-bind matrices: computed from the bone rest-pose world transform.
    //     The .bnd file does NOT store pre-computed inverse-bind matrices — they are derived.
    //     spec: Docs/RE/formats/mesh.md §Bone array —
    //       "remaining fields … computed by post-load routines": CONFIRMED.
    //     Computation: accumulate parent chain (root→bone) to get world transform,
    //     then invert that 4×4 matrix. glTF 2.0 spec §Skins — inverseBindMatrices.
    //   - JOINTS_0 contains per-vertex bone indices into the skeleton's joint array
    //     (glTF joint array index, not self_id). SknWeight.BoneIndex is the zero-based
    //     index into the Skeleton.Bones array.
    //     spec: Docs/RE/formats/mesh.md §Weight record — bone_index: CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a GLB containing the skinned mesh with full skeleton and optional animations.
    /// </summary>
    /// <param name="mesh">Parsed skinned mesh from <c>SknParser</c>.</param>
    /// <param name="skeleton">
    /// Bind-pose skeleton from <c>BndParser</c>. If null, only base geometry is emitted
    /// (no JOINTS_0/WEIGHTS_0, no skin node).
    /// </param>
    /// <param name="clips">
    /// Optional animation clips to embed. Each clip becomes one glTF animation.
    /// Pass null or empty to omit the animations section.
    /// </param>
    /// <param name="output">Destination stream (does not need to be seekable).</param>
    public static void WriteGlb(SkinnedMesh mesh, Skeleton? skeleton, Stream output,
        AnimationClip[]? clips = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        if (skeleton is null)
        {
            // Fallback: emit only base geometry (no skinning data).
            StaticMesh staticView = ExpandSkinnedMeshToStatic(mesh);
            WriteGlb(staticView, output);
            return;
        }

        WriteGlbSkinned(mesh, skeleton, clips, output);
    }

    // -------------------------------------------------------------------------
    // Full skinned GLB emission
    // -------------------------------------------------------------------------

    private static void WriteGlbSkinned(
        SkinnedMesh mesh, Skeleton skeleton,
        AnimationClip[]? clips,
        Stream output)
    {
        // ---- Step 1: Expand corner table → unique (position, uv, boneWeights) vertices ----
        // Each corner references a vertex index for position; bone weights are per-vertex.
        // We expand to a flat vertex list keyed on (vertexIndex, u, v) so each unique
        // position+UV combination gets its own entry with the correct bone influences.

        ExpandSkinnedVertices(mesh, out Vec3[] positions, out Vec2[] uvs,
            out ushort[] indices,
            out ushort[,] jointIndices, // [newVertex, 0..3]
            out float[,] weights); // [newVertex, 0..3]

        int vertexCount = positions.Length;
        int indexCount = indices.Length;
        int boneCount = skeleton.Bones.Length;

        bool use32BitIdx = vertexCount > ushort.MaxValue;

        // ---- Step 2: Compute inverse-bind matrices (one per bone) ----
        // spec: Docs/RE/formats/mesh.md §Bone array — local_translation, local_rotation XYZW: CONFIRMED.
        // glTF 2.0 spec §Skins — inverseBindMatrices: one MAT4 per joint.
        float[] invBindData = ComputeInverseBindMatrices(skeleton); // boneCount × 16 floats

        // ---- Step 3: Build binary buffer ----
        byte[] bin = BuildSkinnedBinaryBuffer(
            positions, uvs, indices, jointIndices, weights, invBindData,
            use32BitIdx,
            out int posOff, out int posLen,
            out int uvOff, out int uvLen,
            out int jntOff, out int jntLen,
            out int wgtOff, out int wgtLen,
            out int idxOff, out int idxLen,
            out int ibmOff, out int ibmLen);

        // ---- Step 4: Build animation binary data (samplers input/output) ----
        // Each clip → one glTF animation.
        // spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps": CONFIRMED.
        // Time in seconds = keyframe_index / 10.0f.
        var animBufferParts = new List<(byte[] data, int timeOff, int timeLen,
            int transOff, int transLen,
            int rotOff, int rotLen,
            int trackCount, int[] keyCounts,
            byte[] boneIds)>();

        int extraBinOffset = Align4(ibmOff + ibmLen);
        int runningOffset = extraBinOffset;

        // We need to build all animation binary data, then concatenate with the main buffer.
        // Strategy: build anim binary separately, then append to bin.
        byte[] animBin = BuildAnimationBinaryData(clips, skeleton, out var animParts);

        // Merge buffers
        byte[] fullBin;
        if (animBin.Length > 0)
        {
            int baseSize = Align4(ibmOff + ibmLen);
            fullBin = new byte[baseSize + animBin.Length];
            bin.AsSpan(0, bin.Length).CopyTo(fullBin.AsSpan(0));
            animBin.AsSpan().CopyTo(fullBin.AsSpan(baseSize));
            // Adjust anim part offsets by baseSize
            for (int i = 0; i < animParts.Count; i++)
            {
                var p = animParts[i];
                animParts[i] = p with
                {
                    TimeOff = p.TimeOff + baseSize,
                    TransOff = p.TransOff + baseSize,
                    RotOff = p.RotOff + baseSize,
                };
            }
        }
        else
        {
            fullBin = bin;
        }

        // ---- Step 5: Build JSON ----
        string json = BuildSkinnedJson(
            positions, vertexCount, indexCount, boneCount, use32BitIdx,
            posOff, posLen, uvOff, uvLen,
            jntOff, jntLen, wgtOff, wgtLen,
            idxOff, idxLen, ibmOff, ibmLen,
            fullBin.Length, skeleton, animParts, clips);

        // ---- Step 6: Write GLB ----
        WriteGlbChunks(output, json, fullBin);
    }

    // -------------------------------------------------------------------------
    // Vertex expansion (corner table → flat arrays with bone influences)
    // -------------------------------------------------------------------------

    private static void ExpandSkinnedVertices(
        SkinnedMesh mesh,
        out Vec3[] outPositions,
        out Vec2[] outUvs,
        out ushort[] outIndices,
        out ushort[,] outJointIndices,
        out float[,] outWeights)
    {
        // Collect per-original-vertex weight lists (up to 4 influences per vertex).
        // spec: Docs/RE/formats/mesh.md §Weight record — vertex_index, bone_index, weight: CONFIRMED.
        // glTF 2.0 spec §Meshes §Skins: JOINTS_0 and WEIGHTS_0 carry up to 4 influences.
        int origVertexCount = mesh.Positions.Length;
        var perVertexWeights = new List<(uint boneIndex, float weight)>[origVertexCount];
        for (int i = 0; i < origVertexCount; i++)
            perVertexWeights[i] = new List<(uint, float)>(4);

        foreach (SknWeight w in mesh.Weights)
        {
            int vi = (int)w.VertexIndex;
            if (vi < origVertexCount)
                perVertexWeights[vi].Add((w.BoneIndex, w.Weight));
        }

        // Expand corners → unique (vertexIndex, u, v) entries.
        var vertexMap = new Dictionary<(uint vi, float u, float v), ushort>(mesh.Corners.Length);
        var positions = new List<Vec3>(mesh.Corners.Length);
        var uvs = new List<Vec2>(mesh.Corners.Length);
        var indexList = new List<ushort>(mesh.Corners.Length);
        // Store the original vertex index for each new vertex so we can copy bone weights.
        var origVertexMapping = new List<uint>(mesh.Corners.Length);

        foreach (SknCorner corner in mesh.Corners)
        {
            var key = (corner.VertexIndex, corner.UvU, corner.UvV);
            if (!vertexMap.TryGetValue(key, out ushort newIdx))
            {
                newIdx = checked((ushort)positions.Count);
                positions.Add(mesh.Positions[(int)corner.VertexIndex]);
                uvs.Add(new Vec2(corner.UvU, corner.UvV));
                origVertexMapping.Add(corner.VertexIndex);
                vertexMap[key] = newIdx;
            }

            indexList.Add(newIdx);
        }

        int newCount = positions.Count;
        outPositions = positions.ToArray();
        outUvs = uvs.ToArray();
        outIndices = indexList.ToArray();
        outJointIndices = new ushort[newCount, 4];
        outWeights = new float[newCount, 4];

        for (int ni = 0; ni < newCount; ni++)
        {
            int origVi = (int)origVertexMapping[ni];
            var wList = perVertexWeights[origVi];
            // Sort by weight descending, take up to 4.
            wList.Sort((a, b) => b.weight.CompareTo(a.weight));

            float totalWeight = 0f;
            for (int k = 0; k < Math.Min(4, wList.Count); k++)
                totalWeight += wList[k].weight;

            for (int k = 0; k < 4; k++)
            {
                if (k < wList.Count)
                {
                    outJointIndices[ni, k] = (ushort)wList[k].boneIndex;
                    outWeights[ni, k] = totalWeight > 0f ? wList[k].weight / totalWeight : 0f;
                }
                else
                {
                    outJointIndices[ni, k] = 0;
                    outWeights[ni, k] = 0f;
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Inverse-bind matrix computation
    // glTF 2.0 spec §Skins — inverseBindMatrices: one column-major MAT4 per joint.
    // spec: Docs/RE/formats/mesh.md §Bone array — local_translation @ +8, local_rotation XYZW @ +20: CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes the inverse bind-pose world transform for each bone.
    /// Returns a flat array of <c>boneCount × 16</c> floats in column-major (glTF) order.
    ///
    /// Implementation note — scale omission:
    ///   The spec (skinning.md §5.3/§9) states per-mesh and per-node scales are generally
    ///   non-unit and must be applied. However, for this glTF dev-tool export, scale is
    ///   intentionally omitted (assumed 1.0) because the parsed <see cref="Skeleton"/> type
    ///   does not carry per-node scale fields — scale values live on the runtime skin object,
    ///   not in the .bnd file. A future importer feeding these GLBs into Godot Skeleton3D
    ///   would need the parser to expose scale before this can be corrected.
    ///   spec: Docs/RE/specs/skinning.md §3.1 quaternion world walk; §5.3/§9 per-mesh/node scale.
    /// </summary>
    private static float[] ComputeInverseBindMatrices(Skeleton skeleton)
    {
        int n = skeleton.Bones.Length;
        float[] result = new float[n * 16];

        // Build a lookup from self_id (low byte) to index in the Bones array.
        // spec: Docs/RE/formats/mesh.md §BndBone — self_id low byte only: CONFIRMED.
        var idToIndex = new Dictionary<byte, int>(n);
        for (int i = 0; i < n; i++)
            idToIndex[(byte)(skeleton.Bones[i].SelfId & 0xFF)] = i;

        // Compute world transforms by traversing the parent chain.
        // Each world transform is Translation × Rotation (no scale in spec).
        // Store as column-major 4×4 float matrices.
        float[] worldTx = new float[n * 16]; // world transform per bone

        // Process bones: we need to ensure parents are processed before children.
        // The spec says parent_id → self_id linkage; we do a simple dependency-ordered pass.
        bool[] computed = new bool[n];

        void ComputeBone(int idx)
        {
            if (computed[idx]) return;

            Bone b = skeleton.Bones[idx];
            byte parentByte = (byte)(b.ParentId & 0xFF);
            byte selfByte = (byte)(b.SelfId & 0xFF);

            // Local transform: Translation + Rotation (XYZW).
            // spec: Docs/RE/formats/mesh.md §BndBone — local_translation @ +8, local_rotation XYZW @ +20: CONFIRMED.
            // Uniform Z-negate convention: (x,y,z) → (x,y,−z); quaternion (x,y,z,w) → (−x,−y,z,w).
            // spec: Docs/RE/specs/skinning.md §7 'No axis negation or mirroring happens inside the
            //   skinning math'; §8(b) 'Pick one handedness conversion — the world Z-negate — and apply
            //   it uniformly to bone bind translations, mesh vertex positions, AND keyframe translations.'
            // glTF 2.0 spec §3.4: right-handed Y-up.
            float tx = b.Translation.X;
            float ty = b.Translation.Y;
            float tz = -b.Translation.Z;

            float qx = -b.Rotation.X;
            float qy = -b.Rotation.Y;
            float qz = b.Rotation.Z;
            float qw = b.Rotation.W;

            // Normalise quaternion.
            float qlen = MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (qlen > 1e-6f)
            {
                qx /= qlen;
                qy /= qlen;
                qz /= qlen;
                qw /= qlen;
            }
            else
            {
                qx = 0f;
                qy = 0f;
                qz = 0f;
                qw = 1f;
            }

            // Build local 4×4 column-major TRS matrix (rotation + translation, no scale).
            float[] localM = QuatToMatrix(qx, qy, qz, qw, tx, ty, tz);

            if (b.IsRoot || !idToIndex.TryGetValue(parentByte, out int parentIdx))
            {
                // Root bone: world == local.
                localM.AsSpan().CopyTo(worldTx.AsSpan(idx * 16));
            }
            else
            {
                ComputeBone(parentIdx); // ensure parent is ready
                // World = Parent.World × Local
                MatMul4x4(worldTx.AsSpan(parentIdx * 16), localM,
                    worldTx.AsSpan(idx * 16));
            }

            computed[idx] = true;
        }

        for (int i = 0; i < n; i++)
            ComputeBone(i);

        // Invert each world transform to get inverse-bind matrix.
        for (int i = 0; i < n; i++)
        {
            InvertTrsMatrix(worldTx.AsSpan(i * 16), result.AsSpan(i * 16));
        }

        return result;
    }

    /// <summary>
    /// Builds a column-major 4×4 TRS matrix from a rotation quaternion (XYZW) and translation.
    /// No scale component.
    /// glTF 2.0 spec §Nodes and Hierarchy — TRS decomposition.
    /// </summary>
    private static float[] QuatToMatrix(float qx, float qy, float qz, float qw,
        float tx, float ty, float tz)
    {
        // Column-major: [col0.xyzw, col1.xyzw, col2.xyzw, col3.xyzw]
        // Rotation from quaternion (standard derivation, right-handed).
        float x2 = qx * qx, y2 = qy * qy, z2 = qz * qz;
        float xy = qx * qy, xz = qx * qz, yz = qy * qz;
        float wx = qw * qx, wy = qw * qy, wz = qw * qz;

        return
        [
            1f - 2f * (y2 + z2), 2f * (xy + wz), 2f * (xz - wy), 0f, // col0
            2f * (xy - wz), 1f - 2f * (x2 + z2), 2f * (yz + wx), 0f, // col1
            2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (x2 + y2), 0f, // col2
            tx, ty, tz, 1f, // col3
        ];
    }

    /// <summary>
    /// Multiplies two 4×4 column-major matrices: result = a × b.
    /// </summary>
    private static void MatMul4x4(ReadOnlySpan<float> a, float[] b, Span<float> result)
    {
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                float sum = 0f;
                for (int k = 0; k < 4; k++)
                    sum += a[k * 4 + row] * b[col * 4 + k];
                result[col * 4 + row] = sum;
            }
        }
    }

    /// <summary>
    /// Inverts a 4×4 TRS column-major matrix (assumes only rotation + translation, no scale).
    /// For a pure rotation-translation matrix: inv(M) = [R^T | -R^T * t].
    /// </summary>
    private static void InvertTrsMatrix(ReadOnlySpan<float> m, Span<float> inv)
    {
        // Extract rotation (upper-left 3×3) and translation (column 3, rows 0-2).
        // Column-major: m[col*4+row].
        float r00 = m[0], r10 = m[1], r20 = m[2]; // col0
        float r01 = m[4], r11 = m[5], r21 = m[6]; // col1
        float r02 = m[8], r12 = m[9], r22 = m[10]; // col2
        float tx = m[12], ty = m[13], tz = m[14]; // col3

        // Transpose of rotation block.
        float ir00 = r00, ir01 = r10, ir02 = r20;
        float ir10 = r01, ir11 = r11, ir12 = r21;
        float ir20 = r02, ir21 = r12, ir22 = r22;

        // -R^T * t
        float itx = -(ir00 * tx + ir01 * ty + ir02 * tz);
        float ity = -(ir10 * tx + ir11 * ty + ir12 * tz);
        float itz = -(ir20 * tx + ir21 * ty + ir22 * tz);

        inv[0] = ir00;
        inv[1] = ir10;
        inv[2] = ir20;
        inv[3] = 0f;
        inv[4] = ir01;
        inv[5] = ir11;
        inv[6] = ir21;
        inv[7] = 0f;
        inv[8] = ir02;
        inv[9] = ir12;
        inv[10] = ir22;
        inv[11] = 0f;
        inv[12] = itx;
        inv[13] = ity;
        inv[14] = itz;
        inv[15] = 1f;
    }

    // -------------------------------------------------------------------------
    // Skinned binary buffer construction
    // -------------------------------------------------------------------------

    private static byte[] BuildSkinnedBinaryBuffer(
        Vec3[] positions, Vec2[] uvs, ushort[] indices,
        ushort[,] jointIndices, float[,] weights,
        float[] invBindData, bool use32BitIdx,
        out int posOff, out int posLen,
        out int uvOff, out int uvLen,
        out int jntOff, out int jntLen,
        out int wgtOff, out int wgtLen,
        out int idxOff, out int idxLen,
        out int ibmOff, out int ibmLen)
    {
        int vertexCount = positions.Length;
        int indexCount = indices.Length;
        int boneCount = invBindData.Length / 16;

        posLen = vertexCount * 3 * sizeof(float); // VEC3 f32
        uvLen = vertexCount * 2 * sizeof(float); // VEC2 f32
        jntLen = vertexCount * 4 * sizeof(ushort); // VEC4 UNSIGNED_SHORT
        wgtLen = vertexCount * 4 * sizeof(float); // VEC4 f32
        int idxStride = use32BitIdx ? sizeof(uint) : sizeof(ushort);
        idxLen = indexCount * idxStride;
        ibmLen = boneCount * 16 * sizeof(float); // MAT4 f32 per bone

        posOff = 0;
        uvOff = Align4(posOff + posLen);
        jntOff = Align4(uvOff + uvLen);
        wgtOff = Align4(jntOff + jntLen);
        idxOff = Align4(wgtOff + wgtLen);
        ibmOff = Align4(idxOff + idxLen);
        int bufSize = Align4(ibmOff + ibmLen);

        byte[] buf = new byte[bufSize];

        // ---- Positions (Z-negated) ----
        // Skinned meshes use the uniform Z-negate convention: (x,y,z) → (x,y,−z).
        // spec: Docs/RE/specs/skinning.md §7 'No axis negation … skinning math'; §8(b) 'world Z-negate
        //   applied uniformly to bone bind translations, mesh vertex positions, AND keyframe translations.'
        // glTF 2.0 spec §3.4: right-handed Y-up.
        int cursor = posOff;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec3 p = positions[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), p.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), -p.Z);
            cursor += 12;
        }

        // ---- UVs ----
        // V already bottom-left from parser.
        // spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
        cursor = uvOff;
        for (int i = 0; i < vertexCount; i++)
        {
            Vec2 uv = uvs[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), uv.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), uv.Y);
            cursor += 8;
        }

        // ---- JOINTS_0 (VEC4 UNSIGNED_SHORT) ----
        // glTF 2.0 spec §Meshes — JOINTS_0: four joint indices per vertex.
        cursor = jntOff;
        for (int i = 0; i < vertexCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), jointIndices[i, 0]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), jointIndices[i, 1]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), jointIndices[i, 2]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 6), jointIndices[i, 3]);
            cursor += 8;
        }

        // ---- WEIGHTS_0 (VEC4 f32) ----
        // glTF 2.0 spec §Meshes — WEIGHTS_0: four bone weights per vertex, summing to ≤ 1.0.
        cursor = wgtOff;
        for (int i = 0; i < vertexCount; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), weights[i, 0]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), weights[i, 1]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), weights[i, 2]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 12), weights[i, 3]);
            cursor += 16;
        }

        // ---- Indices (winding-reversed) ----
        // Z-negate (det = −1) reverses winding; swap i1↔i2 to restore CCW front faces in glTF.
        // spec: Docs/RE/specs/skinning.md §8(b) Z-negate convention.
        // spec: Docs/RE/formats/mesh.md §Index list: CONFIRMED. glTF 2.0 §3.7.2.1.
        cursor = idxOff;
        for (int tri = 0; tri < indexCount / 3; tri++)
        {
            ushort i0 = indices[tri * 3 + 0];
            ushort i1 = indices[tri * 3 + 1];
            ushort i2 = indices[tri * 3 + 2];

            if (use32BitIdx)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 4), i2);
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(cursor + 8), i1);
                cursor += 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), i0);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), i2);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), i1);
                cursor += 6;
            }
        }

        // ---- Inverse-bind matrices (MAT4 f32, column-major) ----
        // glTF 2.0 spec §Skins — inverseBindMatrices accessor: MAT4 FLOAT.
        cursor = ibmOff;
        for (int i = 0; i < invBindData.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), invBindData[i]);
            cursor += 4;
        }

        return buf;
    }

    // -------------------------------------------------------------------------
    // Animation binary data construction
    // spec: Docs/RE/formats/animation.md §Timing — 10 fps: CONFIRMED.
    // spec: Docs/RE/formats/animation.md §Keyframe record — translation XYZ + rotation XYZW: CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Holds per-clip byte ranges within the animation binary buffer.
    /// </summary>
    internal readonly record struct AnimPart(
        int ClipIndex,
        int TimeOff,
        int TimeLen, // input sampler (time in seconds)
        int TransOff,
        int TransLen, // output translation VEC3
        int RotOff,
        int RotLen, // output rotation VEC4 (quaternion)
        int TrackCount,
        int[] KeyCounts, // keyframe count per track
        byte[] BoneIds); // bone_id per track

    private static byte[] BuildAnimationBinaryData(
        AnimationClip[]? clips,
        Skeleton skeleton,
        out List<AnimPart> parts)
    {
        parts = [];
        if (clips is null || clips.Length == 0)
            return [];

        // Build a set of valid bone self_ids for quick lookup.
        var validBoneIds = new HashSet<byte>(skeleton.Bones.Length);
        foreach (Bone b in skeleton.Bones)
            validBoneIds.Add((byte)(b.SelfId & 0xFF));

        // First pass: compute total byte size needed.
        int totalSize = 0;
        foreach (AnimationClip clip in clips)
        {
            foreach (AnimationTrack track in clip.Tracks)
            {
                int kc = track.Keyframes.Length;
                if (kc == 0) continue;
                // time: kc × f32
                totalSize = Align4(totalSize + kc * sizeof(float));
                // translation: kc × VEC3 f32
                totalSize = Align4(totalSize + kc * 3 * sizeof(float));
                // rotation: kc × VEC4 f32
                totalSize = Align4(totalSize + kc * 4 * sizeof(float));
            }
        }

        if (totalSize == 0)
            return [];

        byte[] buf = new byte[totalSize];
        int cursor = 0;

        foreach (AnimationClip clip in clips)
        {
            int trackCount = clip.Tracks.Length;
            int[] keyCounts = new int[trackCount];
            byte[] boneIds = new byte[trackCount];

            for (int ti = 0; ti < trackCount; ti++)
            {
                AnimationTrack track = clip.Tracks[ti];
                int kc = track.Keyframes.Length;
                keyCounts[ti] = kc;
                boneIds[ti] = track.BoneId;

                if (kc == 0) continue;

                // Time accessor (input sampler).
                // spec: Docs/RE/formats/animation.md §Timing — frame_index / 10.0f = seconds: CONFIRMED.
                int thisTimeOff = cursor;
                for (int k = 0; k < kc; k++)
                {
                    float t = k / 10.0f; // 10 fps fixed rate
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), t);
                    cursor += 4;
                }

                cursor = Align4(cursor);

                int thisTransOff = cursor;
                // Translation accessor (output).
                // Uniform Z-negate convention: (x,y,z) → (x,y,−z).
                // spec: Docs/RE/specs/skinning.md §8(b) 'apply [Z-negate] uniformly … keyframe translations.'
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x/y/z: CONFIRMED.
                // glTF 2.0 §3.4: right-handed.
                for (int k = 0; k < kc; k++)
                {
                    Vec3 tr = track.Keyframes[k].Translation;
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), tr.X);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), tr.Y);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), -tr.Z);
                    cursor += 12;
                }

                cursor = Align4(cursor);

                int thisRotOff = cursor;
                // Rotation accessor (output), quaternion XYZW → glTF stores XYZW too.
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation XYZW: CONFIRMED.
                // glTF 2.0 §Animations: rotation accessor stores XYZW (scalar W last).
                // Uniform Z-negate convention: quaternion (x,y,z,w) → (−x,−y,z,w).
                // spec: Docs/RE/specs/skinning.md §8(b) 'under [Z-negate] a quaternion (x,y,z,w)
                //   maps to (−x,−y,z,w)'.
                // Note on interpolation: glTF LINEAR interpolation for quaternion channels is
                //   defined by the glTF spec as nlerp (not strict slerp). The original engine
                //   used slerp (spec: Docs/RE/formats/animation.md §Rotation interpolation: CONFIRMED).
                //   glTF viewers implementing the spec will apply nlerp; the visual difference is
                //   negligible for small angular deltas between keyframes.
                for (int k = 0; k < kc; k++)
                {
                    Quat q = track.Keyframes[k].Rotation;
                    float qx = -q.X;
                    float qy = -q.Y;
                    float qz = q.Z;
                    float qw = q.W;
                    // Normalise
                    float qlen = MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                    if (qlen > 1e-6f)
                    {
                        qx /= qlen;
                        qy /= qlen;
                        qz /= qlen;
                        qw /= qlen;
                    }
                    else
                    {
                        qx = 0f;
                        qy = 0f;
                        qz = 0f;
                        qw = 1f;
                    }

                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), qx);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), qy);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), qz);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 12), qw);
                    cursor += 16;
                }

                cursor = Align4(cursor);

                // One AnimPart per track: each track has its own time/translation/rotation buffer views.
                parts.Add(new AnimPart(
                    ClipIndex: parts.Count,
                    TimeOff: thisTimeOff,
                    TimeLen: kc * sizeof(float),
                    TransOff: thisTransOff,
                    TransLen: kc * 3 * sizeof(float),
                    RotOff: thisRotOff,
                    RotLen: kc * 4 * sizeof(float),
                    TrackCount: 1,
                    KeyCounts: [kc],
                    BoneIds: [track.BoneId]));
            }
        }

        return buf;
    }

    // -------------------------------------------------------------------------
    // Skinned glTF JSON construction
    // -------------------------------------------------------------------------

    private static string BuildSkinnedJson(
        Vec3[] positions, int vertexCount, int indexCount, int boneCount,
        bool use32BitIdx,
        int posOff, int posLen,
        int uvOff, int uvLen,
        int jntOff, int jntLen,
        int wgtOff, int wgtLen,
        int idxOff, int idxLen,
        int ibmOff, int ibmLen,
        int bufferByteLength,
        Skeleton skeleton,
        List<AnimPart> animParts,
        AnimationClip[]? clips)
    {
        // Compute position min/max (original space; Z-negate is applied by the caller's min/max expr).
        // spec: Docs/RE/specs/skinning.md §8(b) Z-negate convention.
        ComputePositionMinMax(positions, out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ);

        int indexComponentType = use32BitIdx ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;

        // Build bone → node index map.
        // glTF nodes: index 0 = mesh node, indices 1..boneCount = bone nodes.
        // spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
        var boneNodeIndex = new int[boneCount];
        for (int i = 0; i < boneCount; i++)
            boneNodeIndex[i] = i + 1; // node 0 = mesh node, nodes 1..N = bones

        // Build parent → children map (by index in Bones array).
        var idToIndex = new Dictionary<byte, int>(boneCount);
        for (int i = 0; i < boneCount; i++)
            idToIndex[(byte)(skeleton.Bones[i].SelfId & 0xFF)] = i;

        var sb = new StringBuilder(2048);
        sb.Append('{');
        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.GltfConverter\"},");

        // ---- Accessor indices bookkeeping ----
        // 0: POSITION, 1: TEXCOORD_0, 2: JOINTS_0, 3: WEIGHTS_0, 4: indices, 5: IBM,
        // then animation time/translation/rotation accessors follow.
        const int accPosition = 0;
        const int accUv = 1;
        const int accJoints = 2;
        const int accWeights = 3;
        const int accIndices = 4;
        const int accIbm = 5;
        int nextAcc = 6;

        // BufferView indices:
        const int bvPos = 0;
        const int bvUv = 1;
        const int bvJnt = 2;
        const int bvWgt = 3;
        const int bvIdx = 4;
        const int bvIbm = 5;
        int nextBv = 6;

        // scene / nodes
        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");

        // Nodes: 0 = mesh node (with skin ref), 1..N = bone nodes.
        sb.Append("\"nodes\":[");

        // Node 0: mesh node, references skin 0 and mesh 0.
        // Find root bone node index.
        int rootNodeIndex = -1;
        for (int i = 0; i < boneCount; i++)
        {
            if (skeleton.Bones[i].IsRoot)
            {
                rootNodeIndex = boneNodeIndex[i];
                break;
            }
        }

        if (rootNodeIndex < 0 && boneCount > 0) rootNodeIndex = boneNodeIndex[0];

        sb.Append('{');
        sb.Append("\"mesh\":0,");
        sb.Append("\"skin\":0");
        if (rootNodeIndex >= 0)
        {
            sb.Append($",\"children\":[{rootNodeIndex}]");
        }

        sb.Append("},");

        // Bone nodes.
        for (int i = 0; i < boneCount; i++)
        {
            Bone b = skeleton.Bones[i];
            byte selfByte = (byte)(b.SelfId & 0xFF);
            byte parentByte = (byte)(b.ParentId & 0xFF);

            // Children of this bone (other bones whose parent_id == this bone's self_id).
            var children = new List<int>();
            for (int j = 0; j < boneCount; j++)
            {
                if (j == i) continue;
                Bone other = skeleton.Bones[j];
                if (!other.IsRoot && (byte)(other.ParentId & 0xFF) == selfByte)
                    children.Add(boneNodeIndex[j]);
            }

            // Apply uniform Z-negate convention to bone translation: (x,y,z) → (x,y,−z).
            // spec: Docs/RE/specs/skinning.md §8(b) 'apply [Z-negate] uniformly to bone bind translations'.
            // spec: Docs/RE/formats/mesh.md §BndBone — local_translation @ +8: CONFIRMED.
            float bTx = b.Translation.X;
            float bTy = b.Translation.Y;
            float bTz = -b.Translation.Z;

            // Apply uniform Z-negate convention to bone rotation: (x,y,z,w) → (−x,−y,z,w).
            // spec: Docs/RE/specs/skinning.md §8(b) 'under [Z-negate] a quaternion (x,y,z,w)
            //   maps to (−x,−y,z,w)'.
            float bQx = -b.Rotation.X;
            float bQy = -b.Rotation.Y;
            float bQz = b.Rotation.Z;
            float bQw = b.Rotation.W;
            float qlen = MathF.Sqrt(bQx * bQx + bQy * bQy + bQz * bQz + bQw * bQw);
            if (qlen > 1e-6f)
            {
                bQx /= qlen;
                bQy /= qlen;
                bQz /= qlen;
                bQw /= qlen;
            }
            else
            {
                bQx = 0f;
                bQy = 0f;
                bQz = 0f;
                bQw = 1f;
            }

            sb.Append('{');
            sb.Append($"\"name\":\"bone_{selfByte}\",");
            sb.Append($"\"translation\":[{F(bTx)},{F(bTy)},{F(bTz)}],");
            sb.Append($"\"rotation\":[{F(bQx)},{F(bQy)},{F(bQz)},{F(bQw)}]");
            if (children.Count > 0)
            {
                sb.Append(",\"children\":[");
                for (int ci = 0; ci < children.Count; ci++)
                {
                    if (ci > 0) sb.Append(',');
                    sb.Append(children[ci]);
                }

                sb.Append(']');
            }

            sb.Append('}');
            if (i < boneCount - 1) sb.Append(',');
        }

        sb.Append("],"); // end nodes

        // Mesh
        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append($"\"POSITION\":{accPosition},");
        sb.Append($"\"TEXCOORD_0\":{accUv},");
        sb.Append($"\"JOINTS_0\":{accJoints},");
        sb.Append($"\"WEIGHTS_0\":{accWeights}");
        sb.Append("},");
        sb.Append($"\"indices\":{accIndices}");
        sb.Append("}]}],");

        // Skin
        sb.Append("\"skins\":[{");
        sb.Append($"\"inverseBindMatrices\":{accIbm},");
        sb.Append("\"joints\":[");
        for (int i = 0; i < boneCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(boneNodeIndex[i]);
        }

        sb.Append(']');
        if (rootNodeIndex >= 0)
            sb.Append($",\"skeleton\":{rootNodeIndex}");
        sb.Append("}],");

        // Animations
        if (animParts.Count > 0)
        {
            // Build accessor and bufferView indices for animation data.
            // We emit one accessor+bufferView per data segment (time, trans, rot) per track.
            var animAccessors = new StringBuilder();
            var animBufferViews = new StringBuilder();
            int animAccStart = nextAcc;
            int animBvStart = nextBv;

            var animSamplerDescs = new List<(int inputAcc, int transAcc, int rotAcc, byte boneId)>();

            foreach (AnimPart part in animParts)
            {
                int kc = part.KeyCounts[0];
                byte boneId = part.BoneIds[0];

                // BufferView: time
                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.TimeOff},");
                animBufferViews.Append($"\"byteLength\":{part.TimeLen}");
                animBufferViews.Append('}');

                // Accessor: time (input, SCALAR f32, min/max required by glTF spec)
                float maxTime = (kc - 1) / 10.0f;
                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"SCALAR\",");
                animAccessors.Append($"\"min\":[0.0],\"max\":[{F(maxTime)}]");
                animAccessors.Append('}');
                int timeAccIdx = nextAcc++;
                nextBv++;

                // BufferView: translation
                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.TransOff},");
                animBufferViews.Append($"\"byteLength\":{part.TransLen}");
                animBufferViews.Append('}');

                // Accessor: translation (VEC3 f32)
                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"VEC3\"");
                animAccessors.Append('}');
                int transAccIdx = nextAcc++;
                nextBv++;

                // BufferView: rotation
                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.RotOff},");
                animBufferViews.Append($"\"byteLength\":{part.RotLen}");
                animBufferViews.Append('}');

                // Accessor: rotation (VEC4 f32)
                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"VEC4\"");
                animAccessors.Append('}');
                int rotAccIdx = nextAcc++;
                nextBv++;

                animSamplerDescs.Add((timeAccIdx, transAccIdx, rotAccIdx, boneId));
            }

            // Build animations array: group tracks by clip.
            // For now we group all tracks into a single glTF animation named after clip[0].
            // (A more sophisticated grouping would use clip boundaries; since we flatten
            //  per-track into animParts, we emit one animation per track here.)
            sb.Append("\"animations\":[");
            for (int pi = 0; pi < animSamplerDescs.Count; pi++)
            {
                if (pi > 0) sb.Append(',');
                var (inputAcc, transAcc, rotAcc, boneId) = animSamplerDescs[pi];

                // Find the node index for this bone_id.
                int targetNode = 0;
                if (idToIndex.TryGetValue(boneId, out int boneIdx))
                    targetNode = boneNodeIndex[boneIdx];

                sb.Append('{');
                sb.Append($"\"name\":\"bone_{boneId}_anim\",");

                // Samplers: 0 = translation, 1 = rotation.
                // glTF 2.0 spec §Animations — samplers interpolation = "LINEAR".
                // Translation uses LINEAR (spec: Docs/RE/formats/animation.md §Translation interpolation: CONFIRMED).
                // Rotation: the original engine uses SLERP; glTF LINEAR for quaternions is nlerp per spec.
                // spec: Docs/RE/formats/animation.md §Rotation interpolation: CONFIRMED (SLERP in engine).
                // glTF 2.0 spec §Animations: rotation LINEAR = nlerp/slerp as implemented by viewer.
                // We emit LINEAR for both as glTF has no explicit SLERP sampler type.
                sb.Append("\"samplers\":[");
                sb.Append($"{{\"input\":{inputAcc},\"interpolation\":\"LINEAR\",\"output\":{transAcc}}},");
                sb.Append($"{{\"input\":{inputAcc},\"interpolation\":\"LINEAR\",\"output\":{rotAcc}}}");
                sb.Append("],");

                sb.Append("\"channels\":[");
                sb.Append($"{{\"sampler\":0,\"target\":{{\"node\":{targetNode},\"path\":\"translation\"}}}},");
                sb.Append($"{{\"sampler\":1,\"target\":{{\"node\":{targetNode},\"path\":\"rotation\"}}}}");
                sb.Append(']');
                sb.Append('}');
            }

            sb.Append("],");

            // Append animation accessors and bufferViews to main strings.
            // We'll build the full accessors/bufferViews sections below including these.
            // Pass them into the final sections.

            // Accessors
            sb.Append("\"accessors\":[");
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvPos},\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC3\",");
            // Z-negate: emitted positions are (x,y,−z), so accessor min/max reflect that.
            // spec: Docs/RE/specs/skinning.md §8(b) Z-negate convention.
            sb.Append($"\"min\":[{F(minX)},{F(minY)},{F(-maxZ)}],\"max\":[{F(maxX)},{F(maxY)},{F(-minZ)}]");
            sb.Append("},");
            sb.Append(
                $"{{\"bufferView\":{bvUv},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC2\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvJnt},\"byteOffset\":0,\"componentType\":{ComponentTypeUnsignedShort},\"count\":{vertexCount},\"type\":\"VEC4\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvWgt},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC4\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvIdx},\"byteOffset\":0,\"componentType\":{indexComponentType},\"count\":{indexCount},\"type\":\"SCALAR\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvIbm},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{boneCount},\"type\":\"MAT4\"}}");
            sb.Append(animAccessors);
            sb.Append("],");

            // BufferViews
            sb.Append("\"bufferViews\":[");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{posOff},\"byteLength\":{posLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{uvOff},\"byteLength\":{uvLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{jntOff},\"byteLength\":{jntLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{wgtOff},\"byteLength\":{wgtLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{idxOff},\"byteLength\":{idxLen},\"target\":{TargetElementArrayBuffer}}},");
            sb.Append($"{{\"buffer\":0,\"byteOffset\":{ibmOff},\"byteLength\":{ibmLen}}}");
            sb.Append(animBufferViews);
            sb.Append("],");
        }
        else
        {
            // Accessors (no animations)
            sb.Append("\"accessors\":[");
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvPos},\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC3\",");
            // Z-negate: emitted positions are (x,y,−z), so accessor min/max reflect that.
            // spec: Docs/RE/specs/skinning.md §8(b) Z-negate convention.
            sb.Append($"\"min\":[{F(minX)},{F(minY)},{F(-maxZ)}],\"max\":[{F(maxX)},{F(maxY)},{F(-minZ)}]");
            sb.Append("},");
            sb.Append(
                $"{{\"bufferView\":{bvUv},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC2\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvJnt},\"byteOffset\":0,\"componentType\":{ComponentTypeUnsignedShort},\"count\":{vertexCount},\"type\":\"VEC4\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvWgt},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC4\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvIdx},\"byteOffset\":0,\"componentType\":{indexComponentType},\"count\":{indexCount},\"type\":\"SCALAR\"}},");
            sb.Append(
                $"{{\"bufferView\":{bvIbm},\"byteOffset\":0,\"componentType\":{ComponentTypeFloat},\"count\":{boneCount},\"type\":\"MAT4\"}}");
            sb.Append("],");

            // BufferViews
            sb.Append("\"bufferViews\":[");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{posOff},\"byteLength\":{posLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{uvOff},\"byteLength\":{uvLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{jntOff},\"byteLength\":{jntLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{wgtOff},\"byteLength\":{wgtLen},\"target\":{TargetArrayBuffer}}},");
            sb.Append(
                $"{{\"buffer\":0,\"byteOffset\":{idxOff},\"byteLength\":{idxLen},\"target\":{TargetElementArrayBuffer}}},");
            sb.Append($"{{\"buffer\":0,\"byteOffset\":{ibmOff},\"byteLength\":{ibmLen}}}");
            sb.Append("],");
        }

        // Buffer
        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Expands the face/corner table of a skinned mesh into a flat array of unique
    /// (position, UV) vertices suitable for static mesh emission.
    /// The corner table uses an index into the vertex array for position, while UV
    /// is stored per-corner; this produces a deduplication by (vertexIndex, u, v).
    /// </summary>
    private static StaticMesh ExpandSkinnedMeshToStatic(SkinnedMesh mesh)
    {
        // Each corner has a vertex index (position) and its own UV.
        // Multiple corners can reference the same vertex with different UVs.
        // Build a deduplicated (vertexIndex, u, v) → new index map.
        var vertexMap = new Dictionary<(uint vi, float u, float v), ushort>(mesh.Corners.Length);
        var positions = new List<Vec3>(mesh.Corners.Length);
        var uvs = new List<Vec2>(mesh.Corners.Length);
        var indices = new List<ushort>(mesh.Corners.Length);

        foreach (SknCorner corner in mesh.Corners)
        {
            var key = (corner.VertexIndex, corner.UvU, corner.UvV);
            if (!vertexMap.TryGetValue(key, out ushort newIdx))
            {
                newIdx = checked((ushort)positions.Count);
                positions.Add(mesh.Positions[(int)corner.VertexIndex]);
                uvs.Add(new Vec2(corner.UvU, corner.UvV));
                vertexMap[key] = newIdx;
            }

            indices.Add(newIdx);
        }

        return new StaticMesh
        {
            Positions = positions.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices.ToArray(),
        };
    }
}