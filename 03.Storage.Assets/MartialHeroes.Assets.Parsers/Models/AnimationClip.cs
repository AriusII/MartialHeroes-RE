namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One keyframe sample from a <c>.mot</c> animation track.
/// Carries a translation vector and a rotation quaternion. No scale channel exists in this format.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/animation.md §Keyframe record — 28 bytes, little-endian.
/// Keyframe stride: 28 bytes. Component order: XYZ translation, then XYZW quaternion.
/// CONFIRMED (field presence and order); sample_verified: false.
/// </remarks>
public readonly record struct Keyframe
{
    /// <summary>
    /// Local-space translation at this keyframe.
    /// spec: Docs/RE/formats/animation.md §Keyframe record — translation_x/y/z @ sub-offset 0/4/8: CONFIRMED.
    /// </summary>
    public required Vec3 Translation { get; init; }

    /// <summary>
    /// Local-space rotation quaternion at this keyframe (XYZW component order, scalar W last).
    /// spec: Docs/RE/formats/animation.md §Keyframe record — rotation_x/y/z/w @ sub-offset 12/16/20/24: CONFIRMED.
    /// </summary>
    public required Quat Rotation { get; init; }
}

/// <summary>
/// One bone track from a <c>.mot</c> animation file.
/// Drives a single bone identified by <see cref="BoneId"/> (low byte of the on-disk
/// <c>track_descriptor</c> u32).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/animation.md §Per-track record.
/// </remarks>
public sealed class AnimationTrack
{
    /// <summary>
    /// Bone this track drives. Matches <c>Bone.SelfId</c> (low byte) in the skeleton.
    /// spec: Docs/RE/formats/animation.md §Per-track record — track_descriptor low byte = bone_id: CONFIRMED.
    /// </summary>
    public required byte BoneId { get; init; }

    /// <summary>
    /// Upper three bytes of the on-disk <c>track_descriptor</c> u32.
    /// Purpose UNVERIFIED; stored opaquely pending clarification.
    /// spec: Docs/RE/formats/animation.md §Per-track record — upper bytes UNVERIFIED.
    /// </summary>
    public required uint TrackDescriptorHigh24 { get; init; }

    /// <summary>
    /// All keyframe samples for this track, in on-disk order.
    /// spec: Docs/RE/formats/animation.md §Per-track record — key_count × 28-byte keyframes: CONFIRMED.
    /// </summary>
    public required Keyframe[] Keyframes { get; init; }
}

/// <summary>
/// Decoded result of a <c>.mot</c> binary skeletal animation clip.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/animation.md §Header layout.
/// Fixed frame rate: 10 fps. Duration (seconds) = FrameCount × 0.1.
/// spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
/// </remarks>
public sealed class AnimationClip
{
    /// <summary>
    /// First numeric identifier from the header.
    /// spec: Docs/RE/formats/animation.md §Header layout — id_a @ offset 0, u32 LE: CONFIRMED.
    /// Semantic role vs. id_b is UNVERIFIED.
    /// </summary>
    public required uint IdA { get; init; }

    /// <summary>
    /// Second numeric identifier. Used as catalogue lookup key.
    /// spec: Docs/RE/formats/animation.md §Header layout — id_b @ offset 4, u32 LE: CONFIRMED.
    /// </summary>
    public required uint IdB { get; init; }

    /// <summary>
    /// Clip name string from the header LenStr field.
    /// spec: Docs/RE/formats/animation.md §Header layout — name LenStr @ offset 8: CONFIRMED (field present).
    /// Wire format (4-byte prefix) is UNVERIFIED — assumed by analogy with .skn/.bnd;
    /// see formats/animation.md §LenStr encoding.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Raw frame count from the header.
    /// Clip duration in seconds = FrameCount × 0.1 (10 fps fixed).
    /// spec: Docs/RE/formats/animation.md §Header layout — frame_count u32 LE: CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §Timing — duration = frame_count × 0.1: CONFIRMED.
    /// </summary>
    public required uint FrameCount { get; init; }

    /// <summary>
    /// All bone tracks for this clip, in on-disk order.
    /// spec: Docs/RE/formats/animation.md §Track array layout — track_count u32 LE: CONFIRMED.
    /// </summary>
    public required AnimationTrack[] Tracks { get; init; }
}