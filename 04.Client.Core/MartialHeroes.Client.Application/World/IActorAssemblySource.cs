using System.Runtime.CompilerServices;

namespace MartialHeroes.Client.Application.World;


public interface IActorAssemblySource
{
    bool TryResolveActorMotion(int motionKey, out ActorMotionView motion);

    bool TryGetSkeletonByIdB(int idB, out SkeletonBindView skeleton);

    bool TryGetSkin(int meshGid, out SkinMeshView mesh);

    bool TryResolveEquipmentPart(long catalogKey, out EquipmentPartView part);
}


public readonly struct ActorMotionView
{
    public required int SkinClassId { get; init; }

    public required ActionClipTable MotionClipIds { get; init; }

    public required ActionEventTable SfxEventIds { get; init; }
}

[InlineArray(SlotCount)]
public struct ActionClipTable
{
    public const int SlotCount = 9;

    private int _slot0;
}

[InlineArray(SlotCount)]
public struct ActionEventTable
{
    public const int SlotCount = 9;

    private int _slot0;
}

public readonly struct SkeletonBindView
{
    public required int ActorId { get; init; }

    public required int BaseId { get; init; }

    public required IReadOnlyList<BoneBind> Bones { get; init; }
}

public readonly struct BoneBind
{
    public required int SelfId { get; init; }

    public required int ParentId { get; init; }

    public required Vec3 LocalTranslation { get; init; }

    public required Quat LocalRotation { get; init; }

    public required Vec3 BindWorldTranslation { get; init; }

    public required Quat BindWorldRotation { get; init; }
}

public readonly struct SkinMeshView
{
    public required int IdB { get; init; }

    public required IReadOnlyList<SkinVertex> Vertices { get; init; }

    public required IReadOnlyList<SkinWeight> Weights { get; init; }
}

public readonly struct SkinVertex
{
    public required Vec3 Position { get; init; }

    public required Vec3 Normal { get; init; }
}

public readonly struct SkinWeight
{
    public required int VertexIndex { get; init; }

    public required int BoneId { get; init; }

    public required float Weight { get; init; }
}

public readonly struct EquipmentPartView
{
    public required int MeshGid { get; init; }

    public required int TextureId { get; init; }

    public required int SknVfsKey { get; init; }

    public required int BindPosePoolId { get; init; }
}


public readonly record struct Vec3(float X, float Y, float Z);

public readonly record struct Quat(float X, float Y, float Z, float W);