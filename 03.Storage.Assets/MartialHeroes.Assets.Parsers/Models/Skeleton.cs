using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One bone from a <c>.bnd</c> bind-pose skeleton record.
/// The on-disk record is exactly 36 bytes — all fields are fully characterized.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §Bone array — "The on-disk record is 36 bytes." CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §Bone array — CORRECTION:
///   "The 72-byte figure is the in-memory object size … The on-disk record is 36 bytes.
///    There is no uncharacterized trailing region on disk." CONFIRMED.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Bone
{
    /// <summary>
    /// Bone's own ID within the skeleton. On-disk: full u32 LE; only the low byte is consumed
    /// by the original client. Root bone sentinel: self_id == 0 and parent_id == 0.
    /// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — self_id @ +0: CONFIRMED.
    /// </summary>
    public readonly uint SelfId;

    /// <summary>
    /// Parent bone ID. On-disk: full u32 LE; only the low byte is consumed.
    /// For the root bone this field holds the same value as self_id (both zero).
    /// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — parent_id @ +4: CONFIRMED.
    /// </summary>
    public readonly uint ParentId;

    /// <summary>
    /// Rest-pose local translation (X, Y, Z).
    /// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_translation @ +8: CONFIRMED.
    /// </summary>
    public readonly Vec3 Translation;

    /// <summary>
    /// Rest-pose local rotation quaternion in XYZW component order (scalar W last).
    /// spec: Docs/RE/formats/mesh.md §BndBone on-disk record — local_rotation @ +20 (XYZW): CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §Quaternion component order:
    ///   "stored as four consecutive floats in XYZW order: X at sub-offset +20, Y at +24, Z at +28,
    ///    W (scalar) at +32." CONFIRMED.
    /// </summary>
    public readonly Quat Rotation;

    /// <summary>
    /// Whether this bone is the root of the skeleton.
    /// spec: Docs/RE/formats/mesh.md §Root bone sentinel:
    ///   "self_id == 0 and parent_id == 0 (both low bytes are zero)." CONFIRMED.
    /// </summary>
    public bool IsRoot => (SelfId & 0xFF) == 0 && (ParentId & 0xFF) == 0;

    public Bone(uint selfId, uint parentId, Vec3 translation, Quat rotation)
    {
        SelfId      = selfId;
        ParentId    = parentId;
        Translation = translation;
        Rotation    = rotation;
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
    /// spec: Docs/RE/formats/mesh.md §Header — actor_name LenStr: CONFIRMED.
    /// </summary>
    public required string ActorName { get; init; }

    /// <summary>
    /// All bones in the skeleton, in on-disk order.
    /// spec: Docs/RE/formats/mesh.md §Bone array.
    /// </summary>
    public required Bone[] Bones { get; init; }
}
