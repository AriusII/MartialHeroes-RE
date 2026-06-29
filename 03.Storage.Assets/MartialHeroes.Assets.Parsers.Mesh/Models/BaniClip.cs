namespace MartialHeroes.Assets.Parsers.Mesh.Models;

public sealed class BaniClip
{
    public required uint Version { get; init; }

    public required uint AnimId { get; init; }

    public required uint RigGroupId { get; init; }

    public required string Name { get; init; }

    public required uint FrameCount { get; init; }

    public required AnimationTrack[] Tracks { get; init; }
}