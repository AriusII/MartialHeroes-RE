using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Assets.Mapping;

public static class GltfConverter
{
    private const uint GlbMagic = 0x46546C67u;

    private const uint GlbVersion = 2u;

    private const uint ChunkTypeJson = 0x4E4F534Au;

    private const uint ChunkTypeBin = 0x004E4942u;

    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;
    private const int ComponentTypeFloat = 5126;

    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;


    public static void WriteGlb(StaticMesh mesh, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        var use32BitIndices = mesh.Positions.Length > ushort.MaxValue;

        var binaryBuffer = BuildBinaryBuffer(mesh, use32BitIndices,
            out var posOffset, out var posLength,
            out var uvOffset, out var uvLength,
            out var idxOffset, out var idxLength);

        var json = BuildJson(mesh, binaryBuffer.Length, use32BitIndices,
            posOffset, posLength, uvOffset, uvLength, idxOffset, idxLength);

        WriteGlbChunks(output, json, binaryBuffer);
    }


    private static byte[] BuildBinaryBuffer(
        StaticMesh mesh, bool use32Bit,
        out int posOffset, out int posLength,
        out int uvOffset, out int uvLength,
        out int idxOffset, out int idxLength)
    {
        var vertexCount = mesh.Positions.Length;
        var indexCount = mesh.Indices.Length;

        posLength = vertexCount * 3 * sizeof(float);
        uvLength = vertexCount * 2 * sizeof(float);
        var indexStride = use32Bit ? sizeof(uint) : sizeof(ushort);
        idxLength = indexCount * indexStride;

        posOffset = 0;
        var posEnd = posOffset + posLength;
        uvOffset = Align4(posEnd);
        var uvEnd = uvOffset + uvLength;
        idxOffset = Align4(uvEnd);
        var bufSize = Align4(idxOffset + idxLength);

        var buf = new byte[bufSize];

        var cursor = posOffset;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = mesh.Positions[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -p.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), p.Z);
            cursor += 12;
        }

        cursor = uvOffset;
        for (var i = 0; i < vertexCount; i++)
        {
            var uv = mesh.Uvs[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), uv.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), uv.Y);
            cursor += 8;
        }

        cursor = idxOffset;
        for (var tri = 0; tri < indexCount / 3; tri++)
        {
            var i0 = mesh.Indices[tri * 3 + 0];
            var i1 = mesh.Indices[tri * 3 + 1];
            var i2 = mesh.Indices[tri * 3 + 2];

            if (use32Bit)
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

        return buf;
    }


    private static string BuildJson(
        StaticMesh mesh, int bufferByteLength, bool use32Bit,
        int posOffset, int posLength,
        int uvOffset, int uvLength,
        int idxOffset, int idxLength)
    {
        var vertexCount = mesh.Positions.Length;
        var indexCount = mesh.Indices.Length;

        ComputePositionMinMax(mesh.Positions,
            out var minX, out var minY, out var minZ,
            out var maxX, out var maxY, out var maxZ);

        var sb = new StringBuilder(512);
        sb.Append('{');

        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping\"},");

        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");
        sb.Append("\"nodes\":[{\"mesh\":0}],");

        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append("\"POSITION\":0,");
        sb.Append("\"TEXCOORD_0\":1");
        sb.Append("},");
        sb.Append("\"indices\":2");
        sb.Append("}]}],");

        sb.Append("\"accessors\":[");

        sb.Append('{');
        sb.Append("\"bufferView\":0,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC3\",");
        sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],");
        sb.Append($"\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"bufferView\":1,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{ComponentTypeFloat},");
        sb.Append($"\"count\":{vertexCount},");
        sb.Append("\"type\":\"VEC2\"");
        sb.Append("},");

        var indexComponentType = use32Bit ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;
        sb.Append('{');
        sb.Append("\"bufferView\":2,");
        sb.Append("\"byteOffset\":0,");
        sb.Append($"\"componentType\":{indexComponentType},");
        sb.Append($"\"count\":{indexCount},");
        sb.Append("\"type\":\"SCALAR\"");
        sb.Append('}');

        sb.Append("],");

        sb.Append("\"bufferViews\":[");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{posOffset},");
        sb.Append($"\"byteLength\":{posLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{uvOffset},");
        sb.Append($"\"byteLength\":{uvLength},");
        sb.Append($"\"target\":{TargetArrayBuffer}");
        sb.Append("},");

        sb.Append('{');
        sb.Append("\"buffer\":0,");
        sb.Append($"\"byteOffset\":{idxOffset},");
        sb.Append($"\"byteLength\":{idxLength},");
        sb.Append($"\"target\":{TargetElementArrayBuffer}");
        sb.Append('}');

        sb.Append("],");

        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");

        sb.Append('}');
        return sb.ToString();
    }


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

        Span<byte> jsonChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr, (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonChunkHdr[4..], ChunkTypeJson);
        output.Write(jsonChunkHdr);
        output.Write(jsonBytes);
        WritePadding(output, jsonBytes.Length, jsonPadded, 0x20);

        Span<byte> binChunkHdr = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr, (uint)binPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(binChunkHdr[4..], ChunkTypeBin);
        output.Write(binChunkHdr);
        output.Write(binaryBuffer);
        WritePadding(output, binaryBuffer.Length, binPadded, 0x00);
    }

    private static void WritePadding(Stream output, int actualLength, int paddedLength, byte padByte)
    {
        var pad = paddedLength - actualLength;
        if (pad <= 0) return;
        Span<byte> p = stackalloc byte[pad];
        p.Fill(padByte);
        output.Write(p);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

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

        for (var i = 1; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }

    private static string F(float v)
    {
        return v.ToString("G9", CultureInfo.InvariantCulture);
    }


    public static void WriteGlb(SkinnedMesh mesh, Skeleton? skeleton, Stream output,
        AnimationClip[]? clips = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        if (skeleton is null)
        {
            var staticView = ExpandSkinnedMeshToStatic(mesh);
            WriteGlb(staticView, output);
            return;
        }

        WriteGlbSkinned(mesh, skeleton, clips, output);
    }


    private static void WriteGlbSkinned(
        SkinnedMesh mesh, Skeleton skeleton,
        AnimationClip[]? clips,
        Stream output)
    {
        ExpandSkinnedVertices(mesh, skeleton, out var positions, out var uvs,
            out var indices,
            out var jointIndices,
            out var weights);

        var vertexCount = positions.Length;
        var indexCount = indices.Length;
        var boneCount = skeleton.Bones.Length;

        var use32BitIdx = vertexCount > ushort.MaxValue;

        var invBindData = ComputeInverseBindMatrices(skeleton);

        var bin = BuildSkinnedBinaryBuffer(
            positions, uvs, indices, jointIndices, weights, invBindData,
            use32BitIdx,
            out var posOff, out var posLen,
            out var uvOff, out var uvLen,
            out var jntOff, out var jntLen,
            out var wgtOff, out var wgtLen,
            out var idxOff, out var idxLen,
            out var ibmOff, out var ibmLen);

        var animBufferParts = new List<(byte[] data, int timeOff, int timeLen,
            int transOff, int transLen,
            int rotOff, int rotLen,
            int trackCount, int[] keyCounts,
            byte[] boneIds)>();

        var extraBinOffset = Align4(ibmOff + ibmLen);

        var animBin = BuildAnimationBinaryData(clips, skeleton, out var animParts);

        byte[] fullBin;
        if (animBin.Length > 0)
        {
            var baseSize = Align4(ibmOff + ibmLen);
            fullBin = new byte[baseSize + animBin.Length];
            bin.AsSpan(0, bin.Length).CopyTo(fullBin.AsSpan(0));
            animBin.AsSpan().CopyTo(fullBin.AsSpan(baseSize));
            for (var i = 0; i < animParts.Count; i++)
            {
                var p = animParts[i];
                animParts[i] = p with
                {
                    TimeOff = p.TimeOff + baseSize,
                    TransOff = p.TransOff + baseSize,
                    RotOff = p.RotOff + baseSize
                };
            }
        }
        else
        {
            fullBin = bin;
        }

        var json = BuildSkinnedJson(
            positions, vertexCount, indexCount, boneCount, use32BitIdx,
            posOff, posLen, uvOff, uvLen,
            jntOff, jntLen, wgtOff, wgtLen,
            idxOff, idxLen, ibmOff, ibmLen,
            fullBin.Length, skeleton, animParts, clips);

        WriteGlbChunks(output, json, fullBin);
    }


    private static void ExpandSkinnedVertices(
        SkinnedMesh mesh,
        Skeleton skeleton,
        out Vec3[] outPositions,
        out Vec2[] outUvs,
        out ushort[] outIndices,
        out ushort[,] outJointIndices,
        out float[,] outWeights)
    {
        var boneCount = skeleton.Bones.Length;
        var idToIndex = new Dictionary<byte, int>(boneCount);
        for (var i = 0; i < boneCount; i++)
            idToIndex[(byte)(skeleton.Bones[i].SelfId & 0xFF)] = i;

        var origVertexCount = mesh.Positions.Length;
        var perVertexWeights = new List<(int jointIndex, float weight)>[origVertexCount];
        for (var i = 0; i < origVertexCount; i++)
            perVertexWeights[i] = new List<(int, float)>(4);

        foreach (var w in mesh.Weights)
        {
            var vi = (int)w.VertexIndex;
            if (vi >= origVertexCount)
                continue;
            if (w.Weight < 0.0099999998f)
                continue;
            if (!idToIndex.TryGetValue((byte)(w.BoneIndex & 0xFF), out var jointIndex))
                continue;
            perVertexWeights[vi].Add((jointIndex, w.Weight));
        }

        var vertexMap = new Dictionary<(uint vi, float u, float v), ushort>(mesh.Corners.Length);
        var positions = new List<Vec3>(mesh.Corners.Length);
        var uvs = new List<Vec2>(mesh.Corners.Length);
        var indexList = new List<ushort>(mesh.Corners.Length);
        var origVertexMapping = new List<uint>(mesh.Corners.Length);

        foreach (var corner in mesh.Corners)
        {
            var key = (corner.VertexIndex, corner.UvU, corner.UvV);
            if (!vertexMap.TryGetValue(key, out var newIdx))
            {
                newIdx = checked((ushort)positions.Count);
                positions.Add(mesh.Positions[(int)corner.VertexIndex]);
                uvs.Add(new Vec2(corner.UvU, corner.UvV));
                origVertexMapping.Add(corner.VertexIndex);
                vertexMap[key] = newIdx;
            }

            indexList.Add(newIdx);
        }

        var newCount = positions.Count;
        outPositions = positions.ToArray();
        outUvs = uvs.ToArray();
        outIndices = indexList.ToArray();
        outJointIndices = new ushort[newCount, 4];
        outWeights = new float[newCount, 4];

        for (var ni = 0; ni < newCount; ni++)
        {
            var origVi = (int)origVertexMapping[ni];
            var wList = perVertexWeights[origVi];
            wList.Sort((a, b) => b.weight.CompareTo(a.weight));

            var totalWeight = 0f;
            for (var k = 0; k < Math.Min(4, wList.Count); k++)
                totalWeight += wList[k].weight;

            for (var k = 0; k < 4; k++)
                if (k < wList.Count)
                {
                    outJointIndices[ni, k] = (ushort)wList[k].jointIndex;
                    outWeights[ni, k] = totalWeight > 0f ? wList[k].weight / totalWeight : 0f;
                }
                else
                {
                    outJointIndices[ni, k] = 0;
                    outWeights[ni, k] = 0f;
                }
        }
    }


    private static float[] ComputeInverseBindMatrices(Skeleton skeleton)
    {
        var n = skeleton.Bones.Length;
        var result = new float[n * 16];

        var idToIndex = new Dictionary<byte, int>(n);
        for (var i = 0; i < n; i++)
            idToIndex[(byte)(skeleton.Bones[i].SelfId & 0xFF)] = i;

        var worldTx = new float[n * 16];

        var computed = new bool[n];

        void ComputeBone(int idx)
        {
            if (computed[idx]) return;

            var b = skeleton.Bones[idx];
            var parentByte = (byte)(b.ParentId & 0xFF);
            var selfByte = (byte)(b.SelfId & 0xFF);

            var tx = -b.Translation.X;
            var ty = b.Translation.Y;
            var tz = b.Translation.Z;

            var qx = b.Rotation.X;
            var qy = -b.Rotation.Y;
            var qz = -b.Rotation.Z;
            var qw = b.Rotation.W;

            var qlen = MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
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

            var localM = QuatToMatrix(qx, qy, qz, qw, tx, ty, tz);

            if (b.IsRoot || !idToIndex.TryGetValue(parentByte, out var parentIdx))
            {
                localM.AsSpan().CopyTo(worldTx.AsSpan(idx * 16));
            }
            else
            {
                ComputeBone(parentIdx);
                MatMul4x4(worldTx.AsSpan(parentIdx * 16), localM,
                    worldTx.AsSpan(idx * 16));
            }

            computed[idx] = true;
        }

        for (var i = 0; i < n; i++)
            ComputeBone(i);

        for (var i = 0; i < n; i++) InvertTrsMatrix(worldTx.AsSpan(i * 16), result.AsSpan(i * 16));

        return result;
    }

    private static float[] QuatToMatrix(float qx, float qy, float qz, float qw,
        float tx, float ty, float tz)
    {
        float x2 = qx * qx, y2 = qy * qy, z2 = qz * qz;
        float xy = qx * qy, xz = qx * qz, yz = qy * qz;
        float wx = qw * qx, wy = qw * qy, wz = qw * qz;

        return
        [
            1f - 2f * (y2 + z2), 2f * (xy + wz), 2f * (xz - wy), 0f,
            2f * (xy - wz), 1f - 2f * (x2 + z2), 2f * (yz + wx), 0f,
            2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (x2 + y2), 0f,
            tx, ty, tz, 1f
        ];
    }

    private static void MatMul4x4(ReadOnlySpan<float> a, float[] b, Span<float> result)
    {
        for (var col = 0; col < 4; col++)
        for (var row = 0; row < 4; row++)
        {
            var sum = 0f;
            for (var k = 0; k < 4; k++)
                sum += a[k * 4 + row] * b[col * 4 + k];
            result[col * 4 + row] = sum;
        }
    }

    private static void InvertTrsMatrix(ReadOnlySpan<float> m, Span<float> inv)
    {
        float r00 = m[0], r10 = m[1], r20 = m[2];
        float r01 = m[4], r11 = m[5], r21 = m[6];
        float r02 = m[8], r12 = m[9], r22 = m[10];
        float tx = m[12], ty = m[13], tz = m[14];

        float ir00 = r00, ir01 = r10, ir02 = r20;
        float ir10 = r01, ir11 = r11, ir12 = r21;
        float ir20 = r02, ir21 = r12, ir22 = r22;

        var itx = -(ir00 * tx + ir01 * ty + ir02 * tz);
        var ity = -(ir10 * tx + ir11 * ty + ir12 * tz);
        var itz = -(ir20 * tx + ir21 * ty + ir22 * tz);

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
        var vertexCount = positions.Length;
        var indexCount = indices.Length;
        var boneCount = invBindData.Length / 16;

        posLen = vertexCount * 3 * sizeof(float);
        uvLen = vertexCount * 2 * sizeof(float);
        jntLen = vertexCount * 4 * sizeof(ushort);
        wgtLen = vertexCount * 4 * sizeof(float);
        var idxStride = use32BitIdx ? sizeof(uint) : sizeof(ushort);
        idxLen = indexCount * idxStride;
        ibmLen = boneCount * 16 * sizeof(float);

        posOff = 0;
        uvOff = Align4(posOff + posLen);
        jntOff = Align4(uvOff + uvLen);
        wgtOff = Align4(jntOff + jntLen);
        idxOff = Align4(wgtOff + wgtLen);
        ibmOff = Align4(idxOff + idxLen);
        var bufSize = Align4(ibmOff + ibmLen);

        var buf = new byte[bufSize];

        var cursor = posOff;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = positions[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -p.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), p.Z);
            cursor += 12;
        }

        cursor = uvOff;
        for (var i = 0; i < vertexCount; i++)
        {
            var uv = uvs[i];
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), uv.X);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), uv.Y);
            cursor += 8;
        }

        cursor = jntOff;
        for (var i = 0; i < vertexCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor), jointIndices[i, 0]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 2), jointIndices[i, 1]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 4), jointIndices[i, 2]);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(cursor + 6), jointIndices[i, 3]);
            cursor += 8;
        }

        cursor = wgtOff;
        for (var i = 0; i < vertexCount; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), weights[i, 0]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), weights[i, 1]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), weights[i, 2]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 12), weights[i, 3]);
            cursor += 16;
        }

        cursor = idxOff;
        for (var tri = 0; tri < indexCount / 3; tri++)
        {
            var i0 = indices[tri * 3 + 0];
            var i1 = indices[tri * 3 + 1];
            var i2 = indices[tri * 3 + 2];

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

        cursor = ibmOff;
        for (var i = 0; i < invBindData.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), invBindData[i]);
            cursor += 4;
        }

        return buf;
    }

    private static byte[] BuildAnimationBinaryData(
        AnimationClip[]? clips,
        Skeleton skeleton,
        out List<AnimPart> parts)
    {
        parts = [];
        if (clips is null || clips.Length == 0)
            return [];

        var validBoneIds = new HashSet<byte>(skeleton.Bones.Length);
        foreach (var b in skeleton.Bones)
            validBoneIds.Add((byte)(b.SelfId & 0xFF));

        var totalSize = 0;
        foreach (var clip in clips)
        foreach (var track in clip.Tracks)
        {
            var kc = track.Keyframes.Length;
            if (kc == 0) continue;
            totalSize = Align4(totalSize + kc * sizeof(float));
            totalSize = Align4(totalSize + kc * 3 * sizeof(float));
            totalSize = Align4(totalSize + kc * 4 * sizeof(float));
        }

        if (totalSize == 0)
            return [];

        var buf = new byte[totalSize];
        var cursor = 0;

        foreach (var clip in clips)
        {
            var trackCount = clip.Tracks.Length;
            var keyCounts = new int[trackCount];
            var boneIds = new byte[trackCount];

            for (var ti = 0; ti < trackCount; ti++)
            {
                var track = clip.Tracks[ti];
                var kc = track.Keyframes.Length;
                keyCounts[ti] = kc;
                boneIds[ti] = track.BoneId;

                if (kc == 0) continue;

                var thisTimeOff = cursor;
                for (var k = 0; k < kc; k++)
                {
                    var t = k / 10.0f;
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), t);
                    cursor += 4;
                }

                cursor = Align4(cursor);

                var thisTransOff = cursor;
                for (var k = 0; k < kc; k++)
                {
                    var tr = track.Keyframes[k].Translation;
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor), -tr.X);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 4), tr.Y);
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(cursor + 8), tr.Z);
                    cursor += 12;
                }

                cursor = Align4(cursor);

                var thisRotOff = cursor;
                for (var k = 0; k < kc; k++)
                {
                    var q = track.Keyframes[k].Rotation;
                    var qx = q.X;
                    var qy = -q.Y;
                    var qz = -q.Z;
                    var qw = q.W;
                    var qlen = MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
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

                parts.Add(new AnimPart(
                    parts.Count,
                    thisTimeOff,
                    kc * sizeof(float),
                    thisTransOff,
                    kc * 3 * sizeof(float),
                    thisRotOff,
                    kc * 4 * sizeof(float),
                    1,
                    [kc],
                    [track.BoneId]));
            }
        }

        return buf;
    }


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
        ComputePositionMinMax(positions, out var minX, out var minY, out var minZ,
            out var maxX, out var maxY, out var maxZ);

        var indexComponentType = use32BitIdx ? ComponentTypeUnsignedInt : ComponentTypeUnsignedShort;

        var boneNodeIndex = new int[boneCount];
        for (var i = 0; i < boneCount; i++)
            boneNodeIndex[i] = i + 1;

        var idToIndex = new Dictionary<byte, int>(boneCount);
        for (var i = 0; i < boneCount; i++)
            idToIndex[(byte)(skeleton.Bones[i].SelfId & 0xFF)] = i;

        var sb = new StringBuilder(2048);
        sb.Append('{');
        sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MartialHeroes.Assets.Mapping.GltfConverter\"},");

        const int accPosition = 0;
        const int accUv = 1;
        const int accJoints = 2;
        const int accWeights = 3;
        const int accIndices = 4;
        const int accIbm = 5;
        var nextAcc = 6;

        const int bvPos = 0;
        const int bvUv = 1;
        const int bvJnt = 2;
        const int bvWgt = 3;
        const int bvIdx = 4;
        const int bvIbm = 5;
        var nextBv = 6;

        sb.Append("\"scene\":0,");
        sb.Append("\"scenes\":[{\"nodes\":[0]}],");

        sb.Append("\"nodes\":[");

        var rootNodeIndex = -1;
        for (var i = 0; i < boneCount; i++)
            if (skeleton.Bones[i].IsRoot)
            {
                rootNodeIndex = boneNodeIndex[i];
                break;
            }

        if (rootNodeIndex < 0 && boneCount > 0) rootNodeIndex = boneNodeIndex[0];

        sb.Append('{');
        sb.Append("\"mesh\":0,");
        sb.Append("\"skin\":0");
        if (rootNodeIndex >= 0) sb.Append($",\"children\":[{rootNodeIndex}]");

        sb.Append("},");

        for (var i = 0; i < boneCount; i++)
        {
            var b = skeleton.Bones[i];
            var selfByte = (byte)(b.SelfId & 0xFF);
            var parentByte = (byte)(b.ParentId & 0xFF);

            var children = new List<int>();
            for (var j = 0; j < boneCount; j++)
            {
                if (j == i) continue;
                var other = skeleton.Bones[j];
                if (!other.IsRoot && (byte)(other.ParentId & 0xFF) == selfByte)
                    children.Add(boneNodeIndex[j]);
            }

            var bTx = -b.Translation.X;
            var bTy = b.Translation.Y;
            var bTz = b.Translation.Z;

            var bQx = b.Rotation.X;
            var bQy = -b.Rotation.Y;
            var bQz = -b.Rotation.Z;
            var bQw = b.Rotation.W;
            var qlen = MathF.Sqrt(bQx * bQx + bQy * bQy + bQz * bQz + bQw * bQw);
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
                for (var ci = 0; ci < children.Count; ci++)
                {
                    if (ci > 0) sb.Append(',');
                    sb.Append(children[ci]);
                }

                sb.Append(']');
            }

            sb.Append('}');
            if (i < boneCount - 1) sb.Append(',');
        }

        sb.Append("],");

        sb.Append("\"meshes\":[{\"primitives\":[{");
        sb.Append("\"attributes\":{");
        sb.Append($"\"POSITION\":{accPosition},");
        sb.Append($"\"TEXCOORD_0\":{accUv},");
        sb.Append($"\"JOINTS_0\":{accJoints},");
        sb.Append($"\"WEIGHTS_0\":{accWeights}");
        sb.Append("},");
        sb.Append($"\"indices\":{accIndices}");
        sb.Append("}]}],");

        sb.Append("\"skins\":[{");
        sb.Append($"\"inverseBindMatrices\":{accIbm},");
        sb.Append("\"joints\":[");
        for (var i = 0; i < boneCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(boneNodeIndex[i]);
        }

        sb.Append(']');
        if (rootNodeIndex >= 0)
            sb.Append($",\"skeleton\":{rootNodeIndex}");
        sb.Append("}],");

        if (animParts.Count > 0)
        {
            var animAccessors = new StringBuilder();
            var animBufferViews = new StringBuilder();
            var animAccStart = nextAcc;
            var animBvStart = nextBv;

            var animSamplerDescs = new List<(int inputAcc, int transAcc, int rotAcc, byte boneId)>();

            foreach (var part in animParts)
            {
                var kc = part.KeyCounts[0];
                var boneId = part.BoneIds[0];

                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.TimeOff},");
                animBufferViews.Append($"\"byteLength\":{part.TimeLen}");
                animBufferViews.Append('}');

                var maxTime = (kc - 1) / 10.0f;
                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"SCALAR\",");
                animAccessors.Append($"\"min\":[0.0],\"max\":[{F(maxTime)}]");
                animAccessors.Append('}');
                var timeAccIdx = nextAcc++;
                nextBv++;

                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.TransOff},");
                animBufferViews.Append($"\"byteLength\":{part.TransLen}");
                animBufferViews.Append('}');

                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"VEC3\"");
                animAccessors.Append('}');
                var transAccIdx = nextAcc++;
                nextBv++;

                animBufferViews.Append(',');
                animBufferViews.Append('{');
                animBufferViews.Append("\"buffer\":0,");
                animBufferViews.Append($"\"byteOffset\":{part.RotOff},");
                animBufferViews.Append($"\"byteLength\":{part.RotLen}");
                animBufferViews.Append('}');

                animAccessors.Append(',');
                animAccessors.Append('{');
                animAccessors.Append($"\"bufferView\":{nextBv},");
                animAccessors.Append("\"byteOffset\":0,");
                animAccessors.Append($"\"componentType\":{ComponentTypeFloat},");
                animAccessors.Append($"\"count\":{kc},");
                animAccessors.Append("\"type\":\"VEC4\"");
                animAccessors.Append('}');
                var rotAccIdx = nextAcc++;
                nextBv++;

                animSamplerDescs.Add((timeAccIdx, transAccIdx, rotAccIdx, boneId));
            }

            sb.Append("\"animations\":[");
            for (var pi = 0; pi < animSamplerDescs.Count; pi++)
            {
                if (pi > 0) sb.Append(',');
                var (inputAcc, transAcc, rotAcc, boneId) = animSamplerDescs[pi];

                var targetNode = 0;
                if (idToIndex.TryGetValue(boneId, out var boneIdx))
                    targetNode = boneNodeIndex[boneIdx];

                sb.Append('{');
                sb.Append($"\"name\":\"bone_{boneId}_anim\",");

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


            sb.Append("\"accessors\":[");
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvPos},\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC3\",");
            sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
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
            sb.Append("\"accessors\":[");
            sb.Append('{');
            sb.Append($"\"bufferView\":{bvPos},\"byteOffset\":0,");
            sb.Append($"\"componentType\":{ComponentTypeFloat},\"count\":{vertexCount},\"type\":\"VEC3\",");
            sb.Append($"\"min\":[{F(-maxX)},{F(minY)},{F(minZ)}],\"max\":[{F(-minX)},{F(maxY)},{F(maxZ)}]");
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

        sb.Append($"\"buffers\":[{{\"byteLength\":{bufferByteLength}}}]");
        sb.Append('}');
        return sb.ToString();
    }

    private static StaticMesh ExpandSkinnedMeshToStatic(SkinnedMesh mesh)
    {
        var vertexMap = new Dictionary<(uint vi, float u, float v), ushort>(mesh.Corners.Length);
        var positions = new List<Vec3>(mesh.Corners.Length);
        var uvs = new List<Vec2>(mesh.Corners.Length);
        var indices = new List<ushort>(mesh.Corners.Length);

        foreach (var corner in mesh.Corners)
        {
            var key = (corner.VertexIndex, corner.UvU, corner.UvV);
            if (!vertexMap.TryGetValue(key, out var newIdx))
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
            Indices = indices.ToArray()
        };
    }


    internal readonly record struct AnimPart(
        int ClipIndex,
        int TimeOff,
        int TimeLen,
        int TransOff,
        int TransLen,
        int RotOff,
        int RotLen,
        int TrackCount,
        int[] KeyCounts,
        byte[] BoneIds);
}