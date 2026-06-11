using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One bone from a <c>.bnd</c> bind-pose skeleton record.
/// On-disk the record is 72 bytes; the first 36 bytes are identified fields,
/// and the trailing 36 bytes are uncharacterized.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §BndBone record — 72 bytes on disk, little-endian
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Bone
{
    /// <summary>
    /// Bone's own ID within the skeleton. Only the low byte is observed to be consumed.
    /// spec: Docs/RE/formats/mesh.md §BndBone record — self_id @ +0: MEDIUM confidence.
    /// </summary>
    public readonly uint SelfId;

    /// <summary>
    /// Parent bone ID. Only the low byte is observed to be consumed.
    /// Root bone sentinel value (self-referential or other) is UNVERIFIED.
    /// spec: Docs/RE/formats/mesh.md §BndBone record — parent_id @ +4: MEDIUM confidence.
    /// </summary>
    public readonly uint ParentId;

    /// <summary>
    /// Rest-pose local translation.
    /// spec: Docs/RE/formats/mesh.md §BndBone record — translation @ +8: MEDIUM confidence.
    /// </summary>
    public readonly Vec3 Translation;

    /// <summary>
    /// Rest-pose local rotation quaternion.
    /// Component order (XYZW vs. WXYZ) is UNVERIFIED — no sample available.
    /// spec: Docs/RE/formats/mesh.md §BndBone record — rotation @ +20: MEDIUM confidence.
    /// </summary>
    public readonly Quat Rotation;

    /// <summary>
    /// Trailing 36 bytes of each bone record, entirely uncharacterized.
    /// May contain scale, flags, inverse-bind matrix data, or additional transform data.
    /// spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36 @ +36: UNVERIFIED.
    /// </summary>
    public readonly BoneTrailingBytes Unknown36;

    public Bone(uint selfId, uint parentId, Vec3 translation, Quat rotation, BoneTrailingBytes unknown36)
    {
        SelfId      = selfId;
        ParentId    = parentId;
        Translation = translation;
        Rotation    = rotation;
        Unknown36   = unknown36;
    }
}

/// <summary>
/// Opaque 36-byte block occupying the uncharacterized trailing bytes of a <c>BndBone</c> record.
/// spec: Docs/RE/formats/mesh.md §BndBone record — unknown_36 @ +36: UNVERIFIED.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 36)]
public readonly struct BoneTrailingBytes
{
    // 36 opaque bytes; kept as a fixed-size struct so the array of Bone
    // can be reasoned about without heap boxing.
    // Access the raw bytes via MemoryMarshal if needed upstream.

    private readonly byte _b00;
    private readonly byte _b01;
    private readonly byte _b02;
    private readonly byte _b03;
    private readonly byte _b04;
    private readonly byte _b05;
    private readonly byte _b06;
    private readonly byte _b07;
    private readonly byte _b08;
    private readonly byte _b09;
    private readonly byte _b10;
    private readonly byte _b11;
    private readonly byte _b12;
    private readonly byte _b13;
    private readonly byte _b14;
    private readonly byte _b15;
    private readonly byte _b16;
    private readonly byte _b17;
    private readonly byte _b18;
    private readonly byte _b19;
    private readonly byte _b20;
    private readonly byte _b21;
    private readonly byte _b22;
    private readonly byte _b23;
    private readonly byte _b24;
    private readonly byte _b25;
    private readonly byte _b26;
    private readonly byte _b27;
    private readonly byte _b28;
    private readonly byte _b29;
    private readonly byte _b30;
    private readonly byte _b31;
    private readonly byte _b32;
    private readonly byte _b33;
    private readonly byte _b34;
    private readonly byte _b35;

    public BoneTrailingBytes(ReadOnlySpan<byte> src)
    {
        if (src.Length < 36)
            throw new ArgumentException("BoneTrailingBytes requires exactly 36 bytes.", nameof(src));

        _b00 = src[0];  _b01 = src[1];  _b02 = src[2];  _b03 = src[3];
        _b04 = src[4];  _b05 = src[5];  _b06 = src[6];  _b07 = src[7];
        _b08 = src[8];  _b09 = src[9];  _b10 = src[10]; _b11 = src[11];
        _b12 = src[12]; _b13 = src[13]; _b14 = src[14]; _b15 = src[15];
        _b16 = src[16]; _b17 = src[17]; _b18 = src[18]; _b19 = src[19];
        _b20 = src[20]; _b21 = src[21]; _b22 = src[22]; _b23 = src[23];
        _b24 = src[24]; _b25 = src[25]; _b26 = src[26]; _b27 = src[27];
        _b28 = src[28]; _b29 = src[29]; _b30 = src[30]; _b31 = src[31];
        _b32 = src[32]; _b33 = src[33]; _b34 = src[34]; _b35 = src[35];
    }

    /// <summary>Copies the 36 opaque bytes into a caller-provided span.</summary>
    public void CopyTo(Span<byte> dest)
    {
        if (dest.Length < 36)
            throw new ArgumentException("Destination span must be at least 36 bytes.", nameof(dest));

        dest[0]  = _b00; dest[1]  = _b01; dest[2]  = _b02; dest[3]  = _b03;
        dest[4]  = _b04; dest[5]  = _b05; dest[6]  = _b06; dest[7]  = _b07;
        dest[8]  = _b08; dest[9]  = _b09; dest[10] = _b10; dest[11] = _b11;
        dest[12] = _b12; dest[13] = _b13; dest[14] = _b14; dest[15] = _b15;
        dest[16] = _b16; dest[17] = _b17; dest[18] = _b18; dest[19] = _b19;
        dest[20] = _b20; dest[21] = _b21; dest[22] = _b22; dest[23] = _b23;
        dest[24] = _b24; dest[25] = _b25; dest[26] = _b26; dest[27] = _b27;
        dest[28] = _b28; dest[29] = _b29; dest[30] = _b30; dest[31] = _b31;
        dest[32] = _b32; dest[33] = _b33; dest[34] = _b34; dest[35] = _b35;
    }
}

/// <summary>
/// Neutral decoded result of a <c>.bnd</c> bind-pose skeleton file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Format: .bnd — binary bind-pose skeleton
/// </remarks>
public sealed class Skeleton
{
    /// <summary>
    /// Numeric actor ID for this skeleton.
    /// spec: Docs/RE/formats/mesh.md §Header — actor_id @ +0: CONFIRMED.
    /// </summary>
    public required uint ActorId { get; init; }

    /// <summary>
    /// Actor name from the length-prefixed header string.
    /// spec: Docs/RE/formats/mesh.md §Header — actor_name: CONFIRMED (presence); UNVERIFIED (exact wire encoding).
    /// </summary>
    public required string ActorName { get; init; }

    /// <summary>
    /// All bones in the skeleton, in on-disk order.
    /// spec: Docs/RE/formats/mesh.md §Bone array.
    /// </summary>
    public required Bone[] Bones { get; init; }
}
