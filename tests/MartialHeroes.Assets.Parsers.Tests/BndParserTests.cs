using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="BndParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/mesh.md §Format: .bnd — binary bind-pose skeleton
/// </summary>
public sealed class BndParserTests
{
    // -----------------------------------------------------------------------
    // Fixture builder
    // -----------------------------------------------------------------------

    private static byte[] Le4(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] Le4f(float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        return b;
    }

    /// <summary>
    /// Builds a synthetic .bnd binary buffer using the confirmed on-disk layout.
    /// spec: Docs/RE/formats/mesh.md §Format: .bnd.
    /// <para>
    /// Wire layout:
    ///   actor_id u32 LE
    ///   actor_name LenStr  [u32 LE length prefix + ASCII body, no null terminator]
    ///   bone_count u32 LE  [full u32; low byte is count]
    ///   bone_count × 36-byte bone records
    /// </para>
    /// </summary>
    private static byte[] BuildBnd(
        uint actorId,
        string actorName,
        (uint selfId, uint parentId, float tx, float ty, float tz, float rx, float ry, float rz, float rw)[] bones)
    {
        using var ms = new System.IO.MemoryStream();

        // actor_id u32 LE
        // spec: Docs/RE/formats/mesh.md §Header — actor_id @ +0: CONFIRMED.
        ms.Write(Le4(actorId));

        // actor_name LenStr: 4-byte u32 LE length prefix + ASCII body, no null terminator.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
        //   "The prefix is a 4-byte little-endian u32." CONFIRMED.
        byte[] nameBytes = Encoding.ASCII.GetBytes(actorName);
        ms.Write(Le4((uint)nameBytes.Length));
        ms.Write(nameBytes);

        // bone_count u32 LE (full 4-byte field; only the low byte is the effective count).
        // spec: Docs/RE/formats/mesh.md §Header — bone_count:
        //   "On-disk representation is a full u32; only the low byte is stored." CONFIRMED.
        ms.Write(Le4((uint)bones.Length));

        // bone records — each exactly 36 bytes on disk.
        // spec: Docs/RE/formats/mesh.md §Bone array — "Total on-disk per bone: 36 bytes." CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Bone array — CORRECTION:
        //   "The 72-byte figure is the in-memory object size … The on-disk record is 36 bytes.
        //    There is no uncharacterized trailing region on disk." CONFIRMED.
        foreach (var (selfId, parentId, tx, ty, tz, rx, ry, rz, rw) in bones)
        {
            // self_id @ sub-offset +0: u32 LE
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — self_id @ +0: CONFIRMED.
            ms.Write(Le4(selfId));
            // parent_id @ sub-offset +4: u32 LE
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — parent_id @ +4: CONFIRMED.
            ms.Write(Le4(parentId));
            // local_translation @ sub-offset +8: f32[3] — X, Y, Z
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_translation @ +8: CONFIRMED.
            ms.Write(Le4f(tx));
            ms.Write(Le4f(ty));
            ms.Write(Le4f(tz));
            // local_rotation @ sub-offset +20: f32[4] — XYZW (scalar W last, at +32)
            // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_rotation @ +20 (XYZW): CONFIRMED.
            ms.Write(Le4f(rx));
            ms.Write(Le4f(ry));
            ms.Write(Le4f(rz));
            ms.Write(Le4f(rw));
            // Total per bone: 4+4+12+16 = 36 bytes. No trailing block.
        }

        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_Header_ActorIdAndName()
    {
        // spec: Docs/RE/formats/mesh.md §Header — actor_id, actor_name: CONFIRMED.
        byte[] data = BuildBnd(actorId: 100, actorName: "Warrior",
            bones:
            [
                (selfId: 0, parentId: 0, tx: 0f, ty: 0f, tz: 0f, rx: 0f, ry: 0f, rz: 0f, rw: 1f),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(100u, skel.ActorId);
        Assert.Equal("Warrior", skel.ActorName);
    }

    [Fact]
    public void Parse_BoneCount_MatchesBoneArray()
    {
        // spec: Docs/RE/formats/mesh.md §Header — bone_count u32 LE (low byte is count): CONFIRMED.
        byte[] data = BuildBnd(actorId: 1, actorName: "S",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
                (1, 0, 1f, 2f, 3f, 0f, 0f, 0f, 1f),
                (2, 1, 4f, 5f, 6f, 0f, 0f, 0f, 1f),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(3, skel.Bones.Length);
    }

    [Fact]
    public void Parse_BoneFields_TranslationAndRotation()
    {
        // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_translation @ +8, local_rotation @ +20: CONFIRMED.
        byte[] data = BuildBnd(actorId: 1, actorName: "T",
            bones:
            [
                (selfId: 1,
                    parentId: 0,
                    tx: 10f, ty: 20f, tz: 30f,
                    rx: 0.1f, ry: 0.2f, rz: 0.3f, rw: 0.9f),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());
        Bone b = skel.Bones[0];

        Assert.Equal(1u, b.SelfId);
        Assert.Equal(0u, b.ParentId);
        Assert.Equal(new Vec3(10f, 20f, 30f), b.Translation);
        // Quaternion is XYZW — W (scalar) stored last at sub-offset +32.
        // spec: Docs/RE/formats/mesh.md §Quaternion component order: CONFIRMED.
        Assert.Equal(new Quat(0.1f, 0.2f, 0.3f, 0.9f), b.Rotation);
    }

    [Fact]
    public void Parse_RootBoneSentinel_Detected()
    {
        // Root bone sentinel: self_id == 0 && parent_id == 0 (both low bytes zero).
        // spec: Docs/RE/formats/mesh.md §Root bone sentinel: CONFIRMED.
        byte[] data = BuildBnd(actorId: 5, actorName: "H",
            bones:
            [
                (selfId: 0, parentId: 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f), // root
                (selfId: 1, parentId: 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f), // child of root
                (selfId: 2, parentId: 1, 2f, 0f, 0f, 0f, 0f, 0f, 1f), // grandchild
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.True(skel.Bones[0].IsRoot, "bone[0] (self_id=0, parent_id=0) must be root");
        Assert.False(skel.Bones[1].IsRoot, "bone[1] (self_id=1) must NOT be root");
        Assert.False(skel.Bones[2].IsRoot, "bone[2] (self_id=2) must NOT be root");
    }

    [Fact]
    public void Parse_BoneHierarchy_ParentChildLinks()
    {
        // Verify parent_id links bones as expected.
        // spec: Docs/RE/formats/mesh.md §BndBone on-disk record — parent_id @ +4: CONFIRMED.
        byte[] data = BuildBnd(actorId: 5, actorName: "H",
            bones:
            [
                (selfId: 0, parentId: 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f), // root
                (selfId: 1, parentId: 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f), // child of root
                (selfId: 2, parentId: 1, 2f, 0f, 0f, 0f, 0f, 0f, 1f), // grandchild
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(0u, skel.Bones[0].ParentId); // root
        Assert.Equal(0u, skel.Bones[1].ParentId); // child → root
        Assert.Equal(1u, skel.Bones[2].ParentId); // grandchild → bone[1]
    }

    [Fact]
    public void Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        // Structural validation: truncated buffer must throw rather than read out of bounds.
        byte[] full = BuildBnd(actorId: 1, actorName: "Truncate",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
                (1, 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f),
            ]);

        // Cut the buffer before the second bone record is complete.
        byte[] truncated = full[..(full.Length - 10)];

        Assert.Throws<InvalidDataException>(() => BndParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Parse_ZeroBones_EmptyArray()
    {
        byte[] data = BuildBnd(actorId: 7, actorName: "Empty", bones: []);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(7u, skel.ActorId);
        Assert.Empty(skel.Bones);
    }

    [Fact]
    public void Parse_ReadOnlyMemory_Overload()
    {
        byte[] data = BuildBnd(actorId: 9, actorName: "Mem",
            bones: [(0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f)]);

        Skeleton skel = BndParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(9u, skel.ActorId);
        Assert.Equal("Mem", skel.ActorName);
    }

    [Fact]
    public void Parse_OnDiskBoneRecord_Is36Bytes()
    {
        // Verify that each bone record contributes exactly 36 bytes to the fixture.
        // This is a structural check: build a zero-bone and one-bone fixture and diff the sizes.
        // spec: Docs/RE/formats/mesh.md §Bone array — "Total on-disk per bone: 36 bytes." CONFIRMED.
        byte[] zero = BuildBnd(actorId: 1, actorName: "A", bones: []);
        byte[] one = BuildBnd(actorId: 1, actorName: "A", bones: [(0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f)]);
        byte[] three = BuildBnd(actorId: 1, actorName: "A",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
                (1, 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f),
                (2, 1, 2f, 0f, 0f, 0f, 0f, 0f, 1f),
            ]);

        Assert.Equal(36, one.Length - zero.Length);
        Assert.Equal(36 * 3, three.Length - zero.Length);
    }

    [Fact]
    public void Parse_QuaternionXyzwOrder_ScalarWLast()
    {
        // The quaternion is stored X,Y,Z,W on disk — W (scalar) is at sub-offset +32.
        // spec: Docs/RE/formats/mesh.md §Quaternion component order:
        //   "X at sub-offset +20, Y at +24, Z at +28, W (scalar) at +32." CONFIRMED.
        // We encode a non-trivial quaternion and verify all four components round-trip.
        byte[] data = BuildBnd(actorId: 1, actorName: "Q",
            bones:
            [
                (selfId: 0, parentId: 0,
                    tx: 0f, ty: 0f, tz: 0f,
                    rx: 0.5f, ry: -0.5f, rz: 0.5f, rw: 0.5f),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());
        Quat q = skel.Bones[0].Rotation;

        Assert.Equal(0.5f, q.X, precision: 6);
        Assert.Equal(-0.5f, q.Y, precision: 6);
        Assert.Equal(0.5f, q.Z, precision: 6);
        Assert.Equal(0.5f, q.W, precision: 6);
    }
}