namespace MartialHeroes.Client.Application.World;


public sealed class AssembledActor
{
    public required int SkinIdB { get; init; }

    public required int ModelClassId { get; init; }

    public required IReadOnlyList<BakedInfluence> BakedInfluences { get; init; }

    public required SkeletonBindView Skeleton { get; init; }

    public required bool HasSkeleton { get; init; }

    public required ActionClipTable MotionClipIds { get; init; }

    public required ActionEventTable SfxEventIds { get; init; }

    public required IReadOnlyList<int> EquipmentGids { get; init; }

    public required IReadOnlyList<EquipmentPart> EquipmentParts { get; init; }

    public required float WorldX { get; init; }

    public required float WorldZ { get; init; }

    public required float Yaw { get; init; }

    public required bool IsInvisible { get; init; }

}

public readonly struct BakedInfluence
{
    public required int VertexIndex { get; init; }

    public required int BoneId { get; init; }

    public required float Weight { get; init; }

    public required Vec3 LocalPosition { get; init; }

    public required Vec3 LocalNormal { get; init; }
}

public readonly struct EquipmentPart
{
    public required int Slot { get; init; }

    public required int EquipmentGid { get; init; }

    public required int MeshGid { get; init; }

    public required int TextureId { get; init; }

    public required bool IsHandWeapon { get; init; }

    public required bool IsOffHand { get; init; }
}