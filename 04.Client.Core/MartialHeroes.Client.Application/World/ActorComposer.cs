using System.Runtime.CompilerServices;

namespace MartialHeroes.Client.Application.World;


public readonly struct ActorSpawn
{
    public int PlayerClass { get; init; }

    public int AppearanceVariant { get; init; }

    public bool IsPlayer { get; init; }

    public int ModelClassId { get; init; }

    public int SkinIdB { get; init; }

    public int BaseMeshGid { get; init; }

    public int MotionKey { get; init; }

    public float WorldX { get; init; }

    public float WorldZ { get; init; }

    public float Yaw { get; init; }

    public EquipmentGidSet EquipmentGids { get; init; }
}

[InlineArray(SlotCount)]
public struct EquipmentGidSet
{
    public const int SlotCount = 6;

    private int _slot0;
}

public sealed class ActorComposer
{
    private const int WeaponSlot = 14;

    private const int InvisibleVariant = 3;

    private const float MinWeight = 0.01f;

    private static readonly int[] OverlaySlots = [3, 4, 6, 2, 11, 14];

    private readonly IActorAssemblySource _source;

    public ActorComposer(IActorAssemblySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public AssembledActor Compose(ReadOnlySpan<byte> descriptor, in ActorSpawn identity)
    {
        var reader = new SpawnDescriptorReader(descriptor);
        var spawn = identity with
        {
            WorldX = reader.ReadWorldX(),
            WorldZ = reader.ReadWorldZ()
        };
        return Compose(in spawn, _source);
    }

    public AssembledActor Compose(in ActorSpawn spawn)
    {
        return Compose(in spawn, _source);
    }

    private static AssembledActor Compose(in ActorSpawn spawn, IActorAssemblySource source)
    {
        int modelClassId;
        bool invisible;
        if (spawn.IsPlayer)
        {
            if (spawn.AppearanceVariant == InvisibleVariant)
            {
                modelClassId = 0;
                invisible = true;
            }
            else
            {
                modelClassId = 5 * (spawn.PlayerClass + 4 * spawn.AppearanceVariant) - 24;
                invisible = false;
            }
        }
        else
        {
            modelClassId = spawn.ModelClassId;
            invisible = modelClassId == 0;
        }

        ActionClipTable clips = default;
        ActionEventTable sfx = default;
        if (source.TryResolveActorMotion(spawn.MotionKey, out var motion))
        {
            clips = motion.MotionClipIds;
            sfx = motion.SfxEventIds;
        }

        SkeletonBindView skeleton;
        var hasSkeleton = false;
        if (!invisible && spawn.SkinIdB != 0 &&
            source.TryGetSkeletonByIdB(spawn.SkinIdB, out var resolved))
        {
            skeleton = resolved;
            hasSkeleton = true;
        }
        else
        {
            skeleton = EmptySkeleton(spawn.SkinIdB);
            invisible = invisible || spawn.SkinIdB == 0;
        }

        var baked = new List<BakedInfluence>();
        if (hasSkeleton && source.TryGetSkin(spawn.BaseMeshGid, out var baseSkin)) BakeSkin(baseSkin, skeleton, baked);

        var equipParts = new List<EquipmentPart>();
        var equipGids = new List<int>();
        if (!invisible) ComposeEquipment(spawn.EquipmentGids, modelClassId, source, equipParts, equipGids);

        return new AssembledActor
        {
            SkinIdB = spawn.SkinIdB,
            ModelClassId = modelClassId,
            BakedInfluences = baked,
            Skeleton = skeleton,
            HasSkeleton = hasSkeleton,
            MotionClipIds = clips,
            SfxEventIds = sfx,
            EquipmentGids = equipGids,
            EquipmentParts = equipParts,
            WorldX = spawn.WorldX,
            WorldZ = spawn.WorldZ,
            Yaw = spawn.Yaw,
            IsInvisible = invisible
        };
    }


    private static void BakeSkin(
        in SkinMeshView skin, in SkeletonBindView skeleton, List<BakedInfluence> baked)
    {
        var vertexTotals = new Dictionary<int, float>();
        var weights = skin.Weights;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i];
            if (w.Weight < MinWeight) continue;

            vertexTotals.TryGetValue(w.VertexIndex, out var total);
            vertexTotals[w.VertexIndex] = total + w.Weight;
        }

        var verts = skin.Vertices;
        var bones = skeleton.Bones;
        var baseId = skeleton.BaseId;

        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i];
            if (w.Weight < MinWeight) continue;

            var boneIndex = w.BoneId - baseId;
            if ((uint)boneIndex >=
                (uint)bones.Count) continue;

            if ((uint)w.VertexIndex >=
                (uint)verts.Count) continue;

            var total = vertexTotals[w.VertexIndex];
            if (total <= 0f)
                continue;

            var weight = w.Weight / total;

            var bone = bones[boneIndex];
            var vertex = verts[w.VertexIndex];

            var invQ = Conjugate(bone.BindWorldRotation);

            var localPos = RotateVector(invQ, Subtract(vertex.Position, bone.BindWorldTranslation));

            var localNormal = RotateVector(invQ, vertex.Normal);

            baked.Add(new BakedInfluence
            {
                VertexIndex = w.VertexIndex,
                BoneId = w.BoneId,
                Weight = weight,
                LocalPosition = localPos,
                LocalNormal = localNormal
            });
        }
    }


    private static void ComposeEquipment(
        EquipmentGidSet gids,
        int modelClassId,
        IActorAssemblySource source,
        List<EquipmentPart> parts,
        List<int> emittedGids)
    {
        var bodyOnly = modelClassId > 1000;

        for (var i = 0; i < OverlaySlots.Length; i++)
        {
            var slot = OverlaySlots[i];
            if (bodyOnly && slot != 3) continue;
            var equipGid = gids[i];
            if (equipGid == 0) continue;

            var gidReduced = ReduceGid(equipGid, slot);

            var catalogKey = gidReduced + 1_000_000_000L * (slot + 100L * modelClassId);

            if (!source.TryResolveEquipmentPart(catalogKey,
                    out var part))
                continue;

            var isWeapon = slot == WeaponSlot;
            emittedGids.Add(equipGid);
            parts.Add(new EquipmentPart
            {
                Slot = slot,
                EquipmentGid = equipGid,
                MeshGid = part.MeshGid,
                TextureId = part.TextureId,
                IsHandWeapon = isWeapon,
                IsOffHand = false
            });
        }
    }

    private static int ReduceGid(int gid, int slot)
    {
        _ = slot;
        return 10000 * (gid / 10000) + gid % 100;
    }

    private static SkeletonBindView EmptySkeleton(int idB)
    {
        return new SkeletonBindView
        {
            ActorId = idB,
            BaseId = 0,
            Bones = []
        };
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quat Conjugate(Quat q)
    {
        return new Quat(-q.X, -q.Y, -q.Z, q.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vec3 Subtract(Vec3 a, Vec3 b)
    {
        return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vec3 RotateVector(Quat q, Vec3 v)
    {
        var tx = 2f * (q.Y * v.Z - q.Z * v.Y);
        var ty = 2f * (q.Z * v.X - q.X * v.Z);
        var tz = 2f * (q.X * v.Y - q.Y * v.X);

        var rx = v.X + q.W * tx + (q.Y * tz - q.Z * ty);
        var ry = v.Y + q.W * ty + (q.Z * tx - q.X * tz);
        var rz = v.Z + q.W * tz + (q.X * ty - q.Y * tx);
        return new Vec3(rx, ry, rz);
    }
}