using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Mesh.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Bone
{
    public readonly uint SelfId;

    public readonly uint ParentId;

    public readonly Vec3 Translation;

    public readonly Quat Rotation;

    public bool IsRoot => (SelfId & 0xFF) == 0 && (ParentId & 0xFF) == 0;

    public Bone(uint selfId, uint parentId, Vec3 translation, Quat rotation)
    {
        SelfId = selfId;
        ParentId = parentId;
        Translation = translation;
        Rotation = rotation;
    }
}

public sealed class Skeleton
{
    public required uint ActorId { get; init; }

    public required string ActorName { get; init; }

    public required Bone[] Bones { get; init; }
}