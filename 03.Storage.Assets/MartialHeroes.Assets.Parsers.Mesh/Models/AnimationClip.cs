using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Mesh.Models;

public readonly record struct Keyframe
{
    public required Vec3 Translation { get; init; }

    public required Quat Rotation { get; init; }
}

public sealed class AnimationTrack
{
    public required byte BoneId { get; init; }

    public required uint TrackDescriptorHigh24 { get; init; }

    public required Keyframe[] Keyframes { get; init; }
}

public sealed class AnimationClip
{
    public required uint IdA { get; init; }

    public required uint IdB { get; init; }

    public required string Name { get; init; }

    public required uint FrameCount { get; init; }

    public required AnimationTrack[] Tracks { get; init; }
}