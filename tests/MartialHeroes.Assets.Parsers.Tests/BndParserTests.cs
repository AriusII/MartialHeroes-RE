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

    private static byte[] Le4(uint v)   { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
    private static byte[] Le4f(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); return b; }

    /// <summary>
    /// Builds a synthetic .bnd binary buffer.
    /// spec: Docs/RE/formats/mesh.md §Format: .bnd.
    /// </summary>
    private static byte[] BuildBnd(
        uint actorId,
        string actorName,
        (uint selfId, uint parentId, float tx, float ty, float tz, float rx, float ry, float rz, float rw, byte[] trailing36)[] bones)
    {
        using var ms = new System.IO.MemoryStream();

        // actor_id u32 LE
        // spec: Docs/RE/formats/mesh.md §Header — actor_id @ +0: CONFIRMED.
        ms.Write(Le4(actorId));

        // actor_name LenStr (1-byte prefix + ASCII)
        // spec: Docs/RE/formats/mesh.md §Header — actor_name: CONFIRMED (presence); UNVERIFIED (encoding).
        byte[] nameBytes = Encoding.ASCII.GetBytes(actorName);
        ms.WriteByte((byte)nameBytes.Length);
        ms.Write(nameBytes);

        // bone_count u8
        // spec: Docs/RE/formats/mesh.md §Header — bone_count: CONFIRMED.
        ms.WriteByte((byte)bones.Length);

        // bone records, each 72 bytes
        // spec: Docs/RE/formats/mesh.md §Bone array — 72 bytes per record: CONFIRMED.
        foreach (var (selfId, parentId, tx, ty, tz, rx, ry, rz, rw, trailing36) in bones)
        {
            // self_id @ +0
            ms.Write(Le4(selfId));
            // parent_id @ +4
            ms.Write(Le4(parentId));
            // translation @ +8 (3 × f32 = 12 bytes)
            ms.Write(Le4f(tx)); ms.Write(Le4f(ty)); ms.Write(Le4f(tz));
            // rotation @ +20 (4 × f32 = 16 bytes)
            ms.Write(Le4f(rx)); ms.Write(Le4f(ry)); ms.Write(Le4f(rz)); ms.Write(Le4f(rw));
            // unknown_36 @ +36 (36 bytes)
            // spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36: UNVERIFIED.
            if (trailing36.Length != 36)
                throw new ArgumentException($"trailing36 must be exactly 36 bytes, got {trailing36.Length}.");
            ms.Write(trailing36);
        }

        return ms.ToArray();
    }

    private static byte[] ZeroTrailing() => new byte[36];

    private static byte[] PatternTrailing(byte seed)
    {
        var b = new byte[36];
        for (int i = 0; i < 36; i++) b[i] = (byte)(seed + i);
        return b;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_Header_ActorIdAndName()
    {
        // spec: Docs/RE/formats/mesh.md §Header — actor_id, actor_name: CONFIRMED (presence).
        byte[] data = BuildBnd(actorId: 100, actorName: "Warrior",
            bones:
            [
                (selfId: 0, parentId: 0, tx: 0f, ty: 0f, tz: 0f, rx: 0f, ry: 0f, rz: 0f, rw: 1f, ZeroTrailing()),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(100u, skel.ActorId);
        Assert.Equal("Warrior", skel.ActorName);
    }

    [Fact]
    public void Parse_BoneCount_MatchesBoneArray()
    {
        // spec: Docs/RE/formats/mesh.md §Header — bone_count: CONFIRMED.
        byte[] data = BuildBnd(actorId: 1, actorName: "S",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()),
                (1, 0, 1f, 2f, 3f, 0f, 0f, 0f, 1f, ZeroTrailing()),
                (2, 1, 4f, 5f, 6f, 0f, 0f, 0f, 1f, ZeroTrailing()),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(3, skel.Bones.Length);
    }

    [Fact]
    public void Parse_BoneFields_TranslationAndRotation()
    {
        // spec: Docs/RE/formats/mesh.md §BndBone record — translation @ +8, rotation @ +20: MEDIUM confidence.
        byte[] data = BuildBnd(actorId: 1, actorName: "T",
            bones:
            [
                (selfId:   1,
                 parentId: 0,
                 tx: 10f, ty: 20f, tz: 30f,
                 rx: 0.1f, ry: 0.2f, rz: 0.3f, rw: 0.9f,
                 ZeroTrailing()),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());
        Bone b = skel.Bones[0];

        Assert.Equal(1u, b.SelfId);
        Assert.Equal(0u, b.ParentId);
        Assert.Equal(new Vec3(10f, 20f, 30f), b.Translation);
        Assert.Equal(new Quat(0.1f, 0.2f, 0.3f, 0.9f), b.Rotation);
    }

    [Fact]
    public void Parse_BoneHierarchy_ParentChildLinks()
    {
        // Verify parent_id encoding links bones as expected.
        // spec: Docs/RE/formats/mesh.md §BndBone record — parent_id @ +4: MEDIUM confidence.
        // Root bone sentinel is UNVERIFIED; here we use parent_id == self_id for the root.
        byte[] data = BuildBnd(actorId: 5, actorName: "H",
            bones:
            [
                (selfId: 0, parentId: 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()), // root
                (selfId: 1, parentId: 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()), // child of root
                (selfId: 2, parentId: 1, 2f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()), // grandchild
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());

        Assert.Equal(0u, skel.Bones[0].ParentId); // root
        Assert.Equal(0u, skel.Bones[1].ParentId); // child → root
        Assert.Equal(1u, skel.Bones[2].ParentId); // grandchild → bone[1]
    }

    [Fact]
    public void Parse_BoneTrailing36_PreservedVerbatim()
    {
        // spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36 @ +36: UNVERIFIED.
        // The trailing bytes must be kept opaque and byte-for-byte identical.
        byte[] trailing = PatternTrailing(0xAA);
        byte[] data = BuildBnd(actorId: 1, actorName: "U",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, trailing),
            ]);

        Skeleton skel = BndParser.Parse(data.AsSpan());
        Bone b = skel.Bones[0];

        Span<byte> extracted = stackalloc byte[36];
        b.Unknown36.CopyTo(extracted);
        Assert.Equal(trailing, extracted.ToArray());
    }

    [Fact]
    public void Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        // Structural validation: truncated buffer must throw rather than read out of bounds.
        byte[] full = BuildBnd(actorId: 1, actorName: "Truncate",
            bones:
            [
                (0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()),
                (1, 0, 1f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing()),
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
            bones: [(0, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, ZeroTrailing())]);

        Skeleton skel = BndParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(9u, skel.ActorId);
        Assert.Equal("Mem", skel.ActorName);
    }
}
