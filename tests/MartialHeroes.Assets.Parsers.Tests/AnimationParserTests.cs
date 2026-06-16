using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="AnimationParser"/>.
/// All fixtures are built in-memory without any real game file.
/// spec: Docs/RE/formats/animation.md
/// </summary>
public sealed class AnimationParserTests
{
    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------

    private static void WriteU32LE(System.IO.Stream s, uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteF32LE(System.IO.Stream s, float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteLenStr(System.IO.Stream s, string text)
    {
        byte[] body = Encoding.ASCII.GetBytes(text);
        // hypothesis — LenStr width unverified, see formats/animation.md §LenStr encoding.
        // Implemented with 4-byte prefix by analogy with .skn/.bnd.
        WriteU32LE(s, (uint)body.Length);
        s.Write(body);
    }

    /// <summary>
    /// Builds a synthetic .mot binary buffer.
    /// spec: Docs/RE/formats/animation.md §Header layout, §Track array layout, §Keyframe record.
    /// </summary>
    private static byte[] BuildMot(
        uint idA, uint idB, string name, uint frameCount,
        (uint trackDescriptor, (float tx, float ty, float tz, float rx, float ry, float rz, float rw)[] keys)[] tracks)
    {
        using var ms = new System.IO.MemoryStream();

        // Header: id_a u32 LE @ 0
        // spec: Docs/RE/formats/animation.md §Header layout — id_a @ +0: CONFIRMED.
        WriteU32LE(ms, idA);

        // id_b u32 LE @ 4
        // spec: Docs/RE/formats/animation.md §Header layout — id_b @ +4: CONFIRMED.
        WriteU32LE(ms, idB);

        // name LenStr — 4-byte u32 LE prefix + ASCII body.
        // hypothesis — LenStr width unverified, see formats/animation.md §LenStr encoding.
        WriteLenStr(ms, name);

        // frame_count u32 LE
        // spec: Docs/RE/formats/animation.md §Header layout — frame_count u32 LE: CONFIRMED.
        WriteU32LE(ms, frameCount);

        // track_count u32 LE
        // spec: Docs/RE/formats/animation.md §Track count: CONFIRMED.
        WriteU32LE(ms, (uint)tracks.Length);

        foreach (var (trackDescriptor, keys) in tracks)
        {
            // track_descriptor u32 LE — low byte = bone_id.
            // spec: Docs/RE/formats/animation.md §Per-track record — track_descriptor: CONFIRMED (low byte).
            WriteU32LE(ms, trackDescriptor);

            // key_count u32 LE
            // spec: Docs/RE/formats/animation.md §Per-track record — key_count: CONFIRMED.
            WriteU32LE(ms, (uint)keys.Length);

            // keyframes — each 28 bytes: translation XYZ f32[3] + rotation XYZW f32[4].
            // spec: Docs/RE/formats/animation.md §Keyframe record — 28 bytes: CONFIRMED.
            foreach (var (tx, ty, tz, rx, ry, rz, rw) in keys)
            {
                // translation_x @ sub-offset +0
                // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x @ +0: CONFIRMED.
                WriteF32LE(ms, tx);
                // translation_y @ +4
                WriteF32LE(ms, ty);
                // translation_z @ +8
                WriteF32LE(ms, tz);
                // rotation_x @ +12
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_x @ +12: CONFIRMED.
                WriteF32LE(ms, rx);
                // rotation_y @ +16
                WriteF32LE(ms, ry);
                // rotation_z @ +20
                WriteF32LE(ms, rz);
                // rotation_w @ +24 (scalar W last — XYZW order)
                // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_w @ +24: CONFIRMED.
                WriteF32LE(ms, rw);
            }
        }

        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parse_Header_IdAndName()
    {
        // spec: Docs/RE/formats/animation.md §Header layout — id_a, id_b, name: CONFIRMED.
        byte[] data = BuildMot(idA: 10, idB: 20, name: "RunForward", frameCount: 30, tracks: []);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;

        Assert.Equal(10u, clip.IdA);
        Assert.Equal(20u, clip.IdB);
        Assert.Equal("RunForward", clip.Name);
    }

    [Fact]
    public void Parse_FrameCount_RoundTrips()
    {
        // spec: Docs/RE/formats/animation.md §Header layout — frame_count u32 LE: CONFIRMED.
        // Clip duration = frame_count × 0.1.
        // spec: Docs/RE/formats/animation.md §Timing — "duration = frame_count × 0.1": CONFIRMED.
        byte[] data = BuildMot(idA: 1, idB: 2, name: "Idle", frameCount: 100, tracks: []);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;

        Assert.Equal(100u, clip.FrameCount);
    }

    [Fact]
    public void Parse_ZeroTracks_EmptyArray()
    {
        // spec: Docs/RE/formats/animation.md §Track count — "u32 LE track_count": CONFIRMED.
        byte[] data = BuildMot(idA: 1, idB: 2, name: "NoTracks", frameCount: 10, tracks: []);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;

        Assert.Empty(clip.Tracks);
    }

    [Fact]
    public void Parse_TrackBoneId_ExtractedFromLowByte()
    {
        // bone_id = low byte of track_descriptor.
        // spec: Docs/RE/formats/animation.md §Per-track record — "Low byte = bone_id": CONFIRMED.
        // Upper 3 bytes purpose UNVERIFIED; we set them to 0x010200 to verify masking.
        uint trackDescriptor = 0x01020003u; // bone_id = 3, high bytes = 0x010200
        byte[] data = BuildMot(idA: 1, idB: 2, name: "T", frameCount: 1,
            tracks: [(trackDescriptor, [])]);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;

        Assert.Single(clip.Tracks);
        Assert.Equal((byte)3, clip.Tracks[0].BoneId);
        Assert.Equal(0x010200u, clip.Tracks[0].TrackDescriptorHigh24);
    }

    [Fact]
    public void Parse_KeyframeStride_Is28Bytes()
    {
        // Verify that each keyframe contributes exactly 28 bytes to the fixture.
        // Build a track with 1 key and a track with 2 keys; the difference in file size
        // must be exactly 28 bytes.
        // spec: Docs/RE/formats/animation.md §Keyframe record — "Keyframe stride: 28 bytes": CONFIRMED.
        var oneKey = new (float, float, float, float, float, float, float)[]
            { (1f, 2f, 3f, 0f, 0f, 0f, 1f) };
        var twoKeys = new (float, float, float, float, float, float, float)[]
            { (1f, 2f, 3f, 0f, 0f, 0f, 1f), (4f, 5f, 6f, 0f, 0f, 0f, 1f) };

        byte[] with1 = BuildMot(1, 2, "T", 2, [(0u, oneKey)]);
        byte[] with2 = BuildMot(1, 2, "T", 2, [(0u, twoKeys)]);

        Assert.Equal(28, with2.Length - with1.Length);
    }

    [Fact]
    public void Parse_Keyframe_TranslationAndRotation()
    {
        // spec: Docs/RE/formats/animation.md §Keyframe record — translation_x/y/z @ +0/4/8: CONFIRMED.
        // spec: Docs/RE/formats/animation.md §Keyframe record — rotation_x/y/z/w @ +12/16/20/24: CONFIRMED.
        var key = (tx: 10f, ty: 20f, tz: 30f, rx: 0.1f, ry: 0.2f, rz: 0.3f, rw: 0.9f);
        byte[] data = BuildMot(1, 2, "K", 1, [(trackDescriptor: 5u, keys: [key])]);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;
        Keyframe kf = clip.Tracks[0].Keyframes[0];

        Assert.Equal(new Vec3(10f, 20f, 30f), kf.Translation);
        Assert.Equal(0.1f, kf.Rotation.X, precision: 6);
        Assert.Equal(0.2f, kf.Rotation.Y, precision: 6);
        Assert.Equal(0.3f, kf.Rotation.Z, precision: 6);
        Assert.Equal(0.9f, kf.Rotation.W, precision: 6);
    }

    [Fact]
    public void Parse_MultipleTracksMultipleKeyframes()
    {
        // spec: Docs/RE/formats/animation.md §Track array layout — multiple tracks: CONFIRMED.
        var track0Keys = new (float, float, float, float, float, float, float)[]
        {
            (1f, 0f, 0f, 0f, 0f, 0f, 1f),
            (2f, 0f, 0f, 0f, 0f, 0f, 1f),
        };
        var track1Keys = new (float, float, float, float, float, float, float)[]
        {
            (0f, 5f, 0f, 0f, 0f, 0f, 1f),
        };

        byte[] data = BuildMot(1, 2, "Multi", 20,
            [(trackDescriptor: 0u, keys: track0Keys), (trackDescriptor: 1u, keys: track1Keys)]);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;

        Assert.Equal(2, clip.Tracks.Length);
        Assert.Equal(2, clip.Tracks[0].Keyframes.Length);
        Assert.Single(clip.Tracks[1].Keyframes);
        Assert.Equal(new Vec3(1f, 0f, 0f), clip.Tracks[0].Keyframes[0].Translation);
        Assert.Equal(new Vec3(2f, 0f, 0f), clip.Tracks[0].Keyframes[1].Translation);
        Assert.Equal(new Vec3(0f, 5f, 0f), clip.Tracks[1].Keyframes[0].Translation);
    }

    [Fact]
    public void Parse_BaniMagic_ReturnsNull()
    {
        // 11 of 3,891 .mot files begin with ASCII "BANI" (42 41 4E 49).
        // The shipping client loader has no magic-check branch and fails on them.
        // A faithful parser must detect BANI and return null rather than crashing.
        // spec: Docs/RE/formats/animation.md §BANI variant —
        //   "A parser MUST sniff the first 4 bytes and route BANI files separately": CONFIRMED.
        // spec: Docs/RE/formats/animation.md §BANI variant — loader rejection: SAMPLE-VERIFIED.
        byte[] baniFile = [(byte)'B', (byte)'A', (byte)'N', (byte)'I', 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF];

        AnimationClip? result = AnimationParser.Parse(baniFile.AsSpan());

        Assert.Null(result); // BANI files are dead data: skip-and-return-null, not crash.
    }

    [Fact]
    public void IsBaniVariant_BaniBytes_ReturnsTrue()
    {
        // spec: Docs/RE/formats/animation.md §BANI variant — magic "BANI" (42 41 4E 49): SAMPLE-VERIFIED.
        byte[] baniBytes = [(byte)'B', (byte)'A', (byte)'N', (byte)'I', 0x00];
        Assert.True(AnimationParser.IsBaniVariant(baniBytes.AsSpan()));
    }

    [Fact]
    public void IsBaniVariant_StandardBytes_ReturnsFalse()
    {
        // Standard .mot begins with id_a (first 4 bytes are a u32 LE, not "BANI").
        byte[] stdBytes = [0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00];
        Assert.False(AnimationParser.IsBaniVariant(stdBytes.AsSpan()));
    }

    [Fact]
    public void Parse_OversizedClip_TrailingBytesIgnored()
    {
        // The spec documents one standard-variant clip with ~+48,719 trailing bytes past the track array.
        // The parser must read the header+tracks and stop — trailing bytes are not an error.
        // spec: Docs/RE/formats/animation.md §Oversized standard clip —
        //   "read the header, then track_count tracks, then stop — tolerate a positive residual": CONFIRMED.
        byte[] clip = BuildMot(1, 2, "Oversized", 10, []);
        // Append 64 trailing bytes — simulates trailing garbage.
        byte[] withTrailing = new byte[clip.Length + 64];
        clip.CopyTo(withTrailing, 0);

        AnimationClip? result = AnimationParser.Parse(withTrailing.AsSpan());

        Assert.NotNull(result);
        Assert.Equal(1u, result!.IdA);
    }

    [Fact]
    public void Parse_TruncatedBuffer_ThrowsInvalidData()
    {
        // Structural validation: truncated buffer must throw rather than read out of bounds.
        byte[] full = BuildMot(1, 2, "Trunc", 5,
            [(0u, [(1f, 2f, 3f, 0f, 0f, 0f, 1f), (4f, 5f, 6f, 0f, 0f, 0f, 1f)])]);

        // Cut buffer midway through the second keyframe.
        byte[] truncated = full[..(full.Length - 10)];

        Assert.Throws<InvalidDataException>(() => AnimationParser.Parse(truncated.AsSpan()));
    }

    [Fact]
    public void Parse_ReadOnlyMemory_Overload()
    {
        byte[] data = BuildMot(99, 88, "Mem", 1, []);

        AnimationClip clip = AnimationParser.Parse(new ReadOnlyMemory<byte>(data))!;

        Assert.Equal(99u, clip.IdA);
        Assert.Equal("Mem", clip.Name);
    }

    [Fact]
    public void Parse_RotationQuaternion_XyzwOrder_WIsLast()
    {
        // The quaternion is stored X,Y,Z,W on disk — W (scalar) at sub-offset +24.
        // spec: Docs/RE/formats/animation.md §Keyframe record — "rotation_w @ +24 (scalar W last)": CONFIRMED.
        var key = (tx: 0f, ty: 0f, tz: 0f, rx: 0.5f, ry: -0.5f, rz: 0.5f, rw: 0.5f);
        byte[] data = BuildMot(1, 2, "Q", 1, [(0u, [key])]);

        AnimationClip clip = AnimationParser.Parse(data.AsSpan())!;
        Quat q = clip.Tracks[0].Keyframes[0].Rotation;

        Assert.Equal(0.5f, q.X, precision: 6);
        Assert.Equal(-0.5f, q.Y, precision: 6);
        Assert.Equal(0.5f, q.Z, precision: 6);
        Assert.Equal(0.5f, q.W, precision: 6);
    }
}