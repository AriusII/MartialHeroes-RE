using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

/// <summary>
/// Headless structural tests for <see cref="GltfConverter"/> skinning and animation paths.
/// No GPU, no disk, no Godot dependency.
///
/// Validates:
///   - Skinning: JOINTS_0 + WEIGHTS_0 present, skins section with inverseBindMatrices accessor,
///     bone node hierarchy (parent_id → node tree), root sentinel handling.
///   - Animation: samplers/channels present, time = frame_index / 10.0f.
///
/// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — 36 bytes: CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §Quaternion component order XYZW: CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §Root bone sentinel self_id==parent_id==0: CONFIRMED.
/// spec: Docs/RE/formats/animation.md §Timing — 10 fps fixed rate: CONFIRMED.
/// </summary>
public sealed class SkinnedGltfConverterTests
{
    // -------------------------------------------------------------------------
    // Synthetic fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two-bone skeleton:
    ///   bone 0: root (self_id=0, parent_id=0), at origin with identity rotation.
    ///   bone 1: child (self_id=1, parent_id=0), translated (0,1,0).
    ///
    /// spec: Docs/RE/formats/mesh.md §BndBone — self_id, parent_id, local_translation,
    ///   local_rotation XYZW: CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
    /// </summary>
    private static Skeleton MakeTwoBoneSkeleton() => new Skeleton
    {
        ActorId = 1,
        ActorName = "TestSkeleton",
        Bones =
        [
            // Root bone: self_id=0, parent_id=0. IsRoot = true.
            new Bone(0u, 0u, new Vec3(0f, 0f, 0f), new Quat(0f, 0f, 0f, 1f)),
            // Child bone: self_id=1, parent_id=0.
            new Bone(1u, 0u, new Vec3(0f, 1f, 0f), new Quat(0f, 0f, 0f, 1f)),
        ],
    };

    /// <summary>
    /// One-triangle skinned mesh with two per-vertex influences.
    /// Vertex 0 is 100% bone 0, vertex 1 is 50% bone 0 + 50% bone 1, vertex 2 is 100% bone 1.
    ///
    /// spec: Docs/RE/formats/mesh.md §Face record — 36 bytes (3 corners × 12 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §Weight record — vertex_index, bone_index, weight: CONFIRMED.
    /// </summary>
    private static SkinnedMesh MakeTriangleSkin() => new SkinnedMesh
    {
        IdA = 1,
        IdB = 1,
        Name = "TestSkin",
        FaceCount = 1,
        Corners =
        [
            new SknCorner(0, 0f, 0f),
            new SknCorner(1, 1f, 0f),
            new SknCorner(2, 0.5f, 1f),
        ],
        Positions =
        [
            new Vec3(0f, 0f, 0f),
            new Vec3(1f, 0f, 0f),
            new Vec3(0.5f, 1f, 0f),
        ],
        Normals =
        [
            new Vec3(0f, 0f, 1f),
            new Vec3(0f, 0f, 1f),
            new Vec3(0f, 0f, 1f),
        ],
        Weights =
        [
            new SknWeight(0, 0, 1.0f), // vertex 0 → bone 0 (100%)
            new SknWeight(1, 0, 0.5f), // vertex 1 → bone 0 (50%)
            new SknWeight(1, 1, 0.5f), // vertex 1 → bone 1 (50%)
            new SknWeight(2, 1, 1.0f), // vertex 2 → bone 1 (100%)
        ],
    };

    /// <summary>
    /// Minimal <see cref="AnimationClip"/> with two tracks (bones 0 and 1), 3 keyframes each.
    ///
    /// spec: Docs/RE/formats/animation.md §Timing — 10 fps, duration = frame_count × 0.1: CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §Keyframe record — XYZ translation + XYZW rotation: CONFIRMED.
    /// </summary>
    private static AnimationClip MakeClip() => new AnimationClip
    {
        IdA = 1,
        IdB = 2,
        Name = "TestClip",
        FrameCount = 3,
        Tracks =
        [
            new AnimationTrack
            {
                BoneId = 0,
                TrackDescriptorHigh24 = 0,
                Keyframes =
                [
                    new Keyframe { Translation = new Vec3(0f, 0f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                    new Keyframe { Translation = new Vec3(0f, 0.5f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                    new Keyframe { Translation = new Vec3(0f, 1f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                ],
            },
            new AnimationTrack
            {
                BoneId = 1,
                TrackDescriptorHigh24 = 0,
                Keyframes =
                [
                    new Keyframe { Translation = new Vec3(0f, 1f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                    new Keyframe { Translation = new Vec3(0f, 1.5f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                    new Keyframe { Translation = new Vec3(0f, 2f, 0f), Rotation = new Quat(0f, 0f, 0f, 1f) },
                ],
            },
        ],
    };

    // -------------------------------------------------------------------------
    // Skinning structural tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_WithSkeleton_EmitsValidGlbHeader()
    {
        // glTF 2.0 spec §binary-gltf §Header: magic "glTF", version 2.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeTriangleSkin(), MakeTwoBoneSkeleton(), ms);
        byte[] glb = ms.ToArray();

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8));

        Assert.Equal(0x46546C67u, magic);
        Assert.Equal(2u, version);
        Assert.Equal((uint)glb.Length, length);
    }

    [Fact]
    public void WriteGlb_WithSkeleton_Json_HasSkinsSection()
    {
        // glTF 2.0 spec §Skins: skins array required for skinned meshes.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("skins", out var skins));
        Assert.Equal(1, skins.GetArrayLength()); // one skin
    }

    [Fact]
    public void WriteGlb_WithSkeleton_Skin_HasInverseBindMatricesAccessor()
    {
        // glTF 2.0 spec §Skins — inverseBindMatrices: index into accessors array.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var skin = doc.RootElement.GetProperty("skins")[0];
        Assert.True(skin.TryGetProperty("inverseBindMatrices", out _));

        int ibmAccIdx = skin.GetProperty("inverseBindMatrices").GetInt32();
        var ibmAcc = doc.RootElement.GetProperty("accessors")[ibmAccIdx];
        Assert.Equal("MAT4", ibmAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, ibmAcc.GetProperty("componentType").GetInt32());
        Assert.Equal(2, ibmAcc.GetProperty("count").GetInt32()); // 2 bones
    }

    [Fact]
    public void WriteGlb_WithSkeleton_Skin_JointsListHasTwoBones()
    {
        // glTF 2.0 spec §Skins — joints: array of node indices for each bone.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var joints = doc.RootElement.GetProperty("skins")[0].GetProperty("joints");
        Assert.Equal(2, joints.GetArrayLength()); // 2 bones
    }

    [Fact]
    public void WriteGlb_WithSkeleton_MeshPrimitive_HasJoints0AndWeights0()
    {
        // glTF 2.0 spec §Meshes — JOINTS_0 and WEIGHTS_0 required for skinned meshes.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var attrs = doc.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");

        Assert.True(attrs.TryGetProperty("JOINTS_0", out _));
        Assert.True(attrs.TryGetProperty("WEIGHTS_0", out _));
    }

    [Fact]
    public void WriteGlb_WithSkeleton_Joints0Accessor_IsVec4UnsignedShort()
    {
        // glTF 2.0 spec §Meshes — JOINTS_0: VEC4 UNSIGNED_SHORT (or UNSIGNED_BYTE).
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var attrs = doc.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        int jntAccIdx = attrs.GetProperty("JOINTS_0").GetInt32();
        var jntAcc = doc.RootElement.GetProperty("accessors")[jntAccIdx];

        Assert.Equal("VEC4", jntAcc.GetProperty("type").GetString());
        Assert.Equal(5123 /*UNSIGNED_SHORT*/, jntAcc.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_WithSkeleton_Weights0Accessor_IsVec4Float()
    {
        // glTF 2.0 spec §Meshes — WEIGHTS_0: VEC4 FLOAT.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var attrs = doc.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        int wgtAccIdx = attrs.GetProperty("WEIGHTS_0").GetInt32();
        var wgtAcc = doc.RootElement.GetProperty("accessors")[wgtAccIdx];

        Assert.Equal("VEC4", wgtAcc.GetProperty("type").GetString());
        Assert.Equal(5126 /*FLOAT*/, wgtAcc.GetProperty("componentType").GetInt32());
    }

    [Fact]
    public void WriteGlb_WithSkeleton_NodeHierarchy_HasBoneNodes()
    {
        // Expect at least 3 nodes: mesh node (0) + 2 bone nodes.
        // glTF 2.0 spec §Nodes and Hierarchy.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        int nodeCount = doc.RootElement.GetProperty("nodes").GetArrayLength();
        Assert.True(nodeCount >= 3, $"Expected >= 3 nodes (mesh + 2 bones), got {nodeCount}");
    }

    [Fact]
    public void WriteGlb_WithSkeleton_RootBoneNode_HasNoParent()
    {
        // The root bone sentinel (self_id==parent_id==0) should not appear as a child of
        // any other bone node.
        // spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton());
        using var doc = JsonDocument.Parse(json);

        var nodes = doc.RootElement.GetProperty("nodes");
        int rootNodeIndex = -1;

        // Root bone is "bone_0" by name convention (self_id = 0 & 0xFF = 0).
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (nodes[i].TryGetProperty("name", out var nameEl) &&
                nameEl.GetString() == "bone_0")
            {
                rootNodeIndex = i;
                break;
            }
        }

        Assert.True(rootNodeIndex >= 0, "Could not find root bone node 'bone_0'.");

        // Verify no other bone node lists rootNodeIndex as a child.
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (!nodes[i].TryGetProperty("name", out _)) continue; // skip non-bone
            if (!nodes[i].TryGetProperty("children", out var ch)) continue;
            foreach (var child in ch.EnumerateArray())
            {
                // The root bone should only appear as a child of the mesh node (node 0),
                // not as a child of another bone node.
                if (child.GetInt32() == rootNodeIndex)
                {
                    Assert.True(i == 0,
                        $"Root bone (node {rootNodeIndex}) appears as child of non-mesh node {i}.");
                }
            }
        }
    }

    [Fact]
    public void WriteGlb_WithSkeleton_BoneWeights_InBinaryBuffer_SumToOne()
    {
        // For vertex 0: 100% bone 0 → weights = (1.0, 0, 0, 0).
        // glTF 2.0 spec §Meshes — WEIGHTS_0: vertex weights should sum to ≤ 1.0.
        // spec: Docs/RE/formats/mesh.md §Weight record — "engine normalises weights per vertex": CONFIRMED.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeTriangleSkin(), MakeTwoBoneSkeleton(), ms);
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        var attrs = doc.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        int wgtAccIdx = attrs.GetProperty("WEIGHTS_0").GetInt32();
        var wgtAcc = doc.RootElement.GetProperty("accessors")[wgtAccIdx];
        int wgtBvIdx = wgtAcc.GetProperty("bufferView").GetInt32();
        var wgtBv = doc.RootElement.GetProperty("bufferViews")[wgtBvIdx];
        int wgtByteOffset = wgtBv.GetProperty("byteOffset").GetInt32();

        byte[] binData = ExtractBinChunk(glb);

        // Vertex 0: weights at wgtByteOffset.
        float w0 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(wgtByteOffset + 0));
        float w1 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(wgtByteOffset + 4));
        float w2 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(wgtByteOffset + 8));
        float w3 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(wgtByteOffset + 12));

        float sum = w0 + w1 + w2 + w3;
        Assert.Equal(1.0f, sum, precision: 5);
    }

    // -------------------------------------------------------------------------
    // Animation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_WithAnimation_Json_HasAnimationsSection()
    {
        // glTF 2.0 spec §Animations: animations array must be present when clips are provided.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton(), MakeClip());
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("animations", out var anims));
        Assert.True(anims.GetArrayLength() > 0, "Expected at least one animation.");
    }

    [Fact]
    public void WriteGlb_WithAnimation_Animation_HasSamplersAndChannels()
    {
        // glTF 2.0 spec §Animations: each animation must have samplers and channels.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton(), MakeClip());
        using var doc = JsonDocument.Parse(json);

        var anim = doc.RootElement.GetProperty("animations")[0];
        Assert.True(anim.TryGetProperty("samplers", out var samplers));
        Assert.True(anim.TryGetProperty("channels", out var channels));
        Assert.True(samplers.GetArrayLength() > 0);
        Assert.True(channels.GetArrayLength() > 0);
    }

    [Fact]
    public void WriteGlb_WithAnimation_Sampler_UsesLinearInterpolation()
    {
        // glTF 2.0 spec §Animations — interpolation "LINEAR" for both translation and rotation.
        // spec: Docs/RE/formats/animation.md §Translation interpolation: CONFIRMED.
        // spec: Docs/RE/formats/animation.md §Rotation interpolation: engine uses SLERP;
        //   glTF has no explicit SLERP type, so LINEAR is emitted.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton(), MakeClip());
        using var doc = JsonDocument.Parse(json);

        var anim = doc.RootElement.GetProperty("animations")[0];
        foreach (var sampler in anim.GetProperty("samplers").EnumerateArray())
        {
            string? interp = sampler.GetProperty("interpolation").GetString();
            Assert.Equal("LINEAR", interp);
        }
    }

    [Fact]
    public void WriteGlb_WithAnimation_Channel_HasTranslationAndRotationPaths()
    {
        // glTF 2.0 spec §Animations — channel target.path must be "translation" or "rotation".
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton(), MakeClip());
        using var doc = JsonDocument.Parse(json);

        var channels = doc.RootElement.GetProperty("animations")[0].GetProperty("channels");
        var paths = new HashSet<string>();
        foreach (var ch in channels.EnumerateArray())
        {
            string? path = ch.GetProperty("target").GetProperty("path").GetString();
            if (path is not null) paths.Add(path);
        }

        Assert.Contains("translation", paths);
        Assert.Contains("rotation", paths);
    }

    [Fact]
    public void WriteGlb_WithAnimation_TimeAccessor_HasCorrectMinMax()
    {
        // For 3 keyframes at 10 fps: times = [0.0, 0.1, 0.2].
        // min = 0.0, max = 0.2.
        // spec: Docs/RE/formats/animation.md §Timing — frame_count × 0.1 = duration: CONFIRMED.
        // spec: Docs/RE/formats/animation.md §Timing — 10 fps fixed rate: CONFIRMED.
        string json = ExtractJson(MakeTriangleSkin(), MakeTwoBoneSkeleton(), MakeClip());
        using var doc = JsonDocument.Parse(json);

        var anim = doc.RootElement.GetProperty("animations")[0];
        int samplerInputAccIdx = anim.GetProperty("samplers")[0].GetProperty("input").GetInt32();
        var inputAcc = doc.RootElement.GetProperty("accessors")[samplerInputAccIdx];

        float minTime = inputAcc.GetProperty("min")[0].GetSingle();
        float maxTime = inputAcc.GetProperty("max")[0].GetSingle();

        Assert.Equal(0.0f, minTime, precision: 5);
        Assert.Equal(0.2f, maxTime, precision: 4); // (3-1)/10 = 0.2
    }

    [Fact]
    public void WriteGlb_WithAnimation_TimeAccessor_BinaryBytes_AreCorrect()
    {
        // First track, 3 keyframes: times in binary should be 0.0f, 0.1f, 0.2f.
        // spec: Docs/RE/formats/animation.md §Timing — k / 10.0f: CONFIRMED.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeTriangleSkin(), MakeTwoBoneSkeleton(), ms,
            new[] { MakeClip() });
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        var anim = doc.RootElement.GetProperty("animations")[0];
        int samplerInputAccIdx = anim.GetProperty("samplers")[0].GetProperty("input").GetInt32();
        var inputAcc = doc.RootElement.GetProperty("accessors")[samplerInputAccIdx];
        int bvIdx = inputAcc.GetProperty("bufferView").GetInt32();
        var bv = doc.RootElement.GetProperty("bufferViews")[bvIdx];
        int off = bv.GetProperty("byteOffset").GetInt32();

        byte[] binData = ExtractBinChunk(glb);

        float t0 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(off + 0));
        float t1 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(off + 4));
        float t2 = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(off + 8));

        Assert.Equal(0.0f, t0, precision: 5);
        Assert.Equal(0.1f, t1, precision: 5);
        Assert.Equal(0.2f, t2, precision: 4);
    }

    [Fact]
    public void WriteGlb_WithAnimation_TranslationAccessor_XIsNegated()
    {
        // Handedness flip: X component of translation is negated.
        // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x/y/z: CONFIRMED.
        // glTF 2.0 §3.4: right-handed.
        // Keyframe 0 of track 0: translation = (0,0,0) → after flip X = 0 (unchanged here).
        // Use a clip with non-zero X translation to test the flip.
        var clipWithXTranslation = new AnimationClip
        {
            IdA = 10,
            IdB = 11,
            Name = "XTest",
            FrameCount = 1,
            Tracks =
            [
                new AnimationTrack
                {
                    BoneId = 0,
                    TrackDescriptorHigh24 = 0,
                    Keyframes =
                    [
                        new Keyframe
                        {
                            Translation = new Vec3(2f, 0f, 0f), // X=2 on disk → X=-2 after flip
                            Rotation = new Quat(0f, 0f, 0f, 1f),
                        },
                    ],
                },
            ],
        };

        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeTriangleSkin(), MakeTwoBoneSkeleton(), ms,
            new[] { clipWithXTranslation });
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        var anim = doc.RootElement.GetProperty("animations")[0];
        int transAccIdx = anim.GetProperty("samplers")[0].GetProperty("output").GetInt32();
        var transAcc = doc.RootElement.GetProperty("accessors")[transAccIdx];
        int bvIdx = transAcc.GetProperty("bufferView").GetInt32();
        var bv = doc.RootElement.GetProperty("bufferViews")[bvIdx];
        int off = bv.GetProperty("byteOffset").GetInt32();

        byte[] binData = ExtractBinChunk(glb);
        float translationX = BinaryPrimitives.ReadSingleLittleEndian(binData.AsSpan(off));
        Assert.Equal(-2f, translationX, precision: 5); // negated
    }

    // -------------------------------------------------------------------------
    // Null skeleton fallback (existing behaviour preserved)
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteGlb_NullSkeleton_FallsBackToBaseGeometry_NoSkins()
    {
        // When skeleton is null the old static-mesh fallback is used — no skins section.
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(MakeTriangleSkin(), skeleton: null, ms);
        byte[] glb = ms.ToArray();

        string json = ExtractJson(glb);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("skins", out _),
            "No skins section expected when skeleton is null.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ExtractJson(SkinnedMesh mesh, Skeleton? skeleton,
        AnimationClip? clip = null)
    {
        using var ms = new MemoryStream();
        GltfConverter.WriteGlb(mesh, skeleton, ms,
            clip is not null ? new[] { clip } : null);
        return ExtractJson(ms.ToArray());
    }

    private static string ExtractJson(byte[] glb)
    {
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return Encoding.UTF8.GetString(glb, 20, (int)jsonLength).TrimEnd(' ');
    }

    private static byte[] ExtractBinChunk(byte[] glb)
    {
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        int binHdrOffset = 12 + 8 + (int)jsonLength;
        uint binLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHdrOffset));
        int binDataOffset = binHdrOffset + 8;
        return glb[binDataOffset..(binDataOffset + (int)binLength)];
    }
}