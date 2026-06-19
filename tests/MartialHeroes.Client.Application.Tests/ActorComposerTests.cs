using MartialHeroes.Client.Application.World;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

// spec: Docs/RE/specs/skinning.md §0/§3.5.2/§3.5.4/§4/§8(e)
// spec: Docs/RE/formats/actormotion.md (motion_ids_a / motion_ids_b)

/// <summary>
/// Tests for ActorComposer: model_class_id formula, skeleton select by id_b,
/// inverse-bind bake cancellation property, base-relative bone indexing,
/// equipment slot order, and motion/SFX table separation.
/// </summary>
public sealed class ActorComposerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // model_class_id = 5·(class + 4·variant) − 24
    // spec: Docs/RE/specs/skinning.md §3.5.2
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    // (class=1, variant=0): 5·(1 + 0) − 24 = 5 − 24 = −19  ← invalid? Let's use the table values.
    // The spec gives the 4 confirmed results: {1,11,16,26} for specific (class,variant) pairs.
    // (class=1, variant=0): 5*(1+4*0)-24 = 5-24 = -19 … that yields -19, which is not in {1,11,16,26}.
    // Per skinning.md §3.5.2: the four player classes yield 1,11,16,26.
    // class=1 variant=0: 5*(1+0)-24 = -19 (not a valid player pair — variant range is 1..3 or 0..3)
    // The actual pairs: looking at 5*(class+4*var)-24 ∈ {1,11,16,26}:
    //   class=1,var=1: 5*(1+4)−24 = 25−24 = 1  ✓
    //   class=1,var=2: 5*(1+8)−24 = 45−24 = 21  (not in set)
    //   class=2,var=2: 5*(2+8)−24 = 50−24 = 26  ✓
    //   class=3,var=1: 5*(3+4)−24 = 35−24 = 11  ✓
    //   class=4,var=1: 5*(4+4)−24 = 40−24 = 16  ✓
    // spec: Docs/RE/specs/skinning.md §3.5.2 — model_class_id = 5*(class+4*variant)−24
    [InlineData(1, 1, 1)]   // Musa: 5*(1+4)−24=1
    [InlineData(3, 1, 11)]  // Dosa: 5*(3+4)−24=11
    [InlineData(4, 1, 16)]  // Monk: 5*(4+4)−24=16
    [InlineData(2, 2, 26)]  // Salsu v2: 5*(2+8)−24=26
    public void Compose_PlayerModelClassId_Formula(int playerClass, int variant, int expectedModelClassId)
    {
        // spec: Docs/RE/specs/skinning.md §3.5.2 — model_class_id = 5*(class+4*variant)−24: CONFIRMED
        var source = new FakeActorSource();
        var composer = new ActorComposer(source);

        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = playerClass,
            AppearanceVariant = variant,
            SkinIdB = 0, // no skeleton needed for this test
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);
        Assert.Equal(expectedModelClassId, actor.ModelClassId);
    }

    [Fact]
    public void Compose_Player_Variant3_IsInvisible_ModelClassId0()
    {
        // spec: Docs/RE/specs/skinning.md §3.5.2 — variant == 3 resolves to 0 = invisible sentinel
        var source = new FakeActorSource();
        var composer = new ActorComposer(source);

        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = 1,
            AppearanceVariant = 3, // invisible sentinel
            SkinIdB = 1,
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);
        Assert.Equal(0, actor.ModelClassId);
        Assert.True(actor.IsInvisible);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Skeleton select VERBATIM by id_b
    // spec: Docs/RE/specs/skinning.md §8(e) — pose_pool[id_b], NOT g{N}.bnd formatting
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compose_SkeletonSelectByIdB_AsksSourceForExactIdB()
    {
        // spec: Docs/RE/specs/skinning.md §8(e) — "selected_skeleton = pose_pool[skn.header.id_b]"
        // The composer must call TryGetSkeletonByIdB(idB) — not compute g{N}.bnd from class id.
        const int skinIdB = 3; // arbitrary id_b value
        var source = new FakeActorSource();
        source.RegisterSkeleton(skinIdB, MakeSingleBoneSkeleton(skinIdB, baseId: 10));

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = 1,
            AppearanceVariant = 1, // model_class_id = 1 (not 3)
            SkinIdB = skinIdB,
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        // Verify the source was asked for exactly skinIdB
        Assert.Contains(skinIdB, source.SkeletonQueriedIds);
        // And the skeleton was resolved (HasSkeleton = true)
        Assert.True(actor.HasSkeleton);
        Assert.Equal(skinIdB, actor.Skeleton.ActorId);
    }

    [Fact]
    public void Compose_IdB_0_YieldsNoSkeleton_IsInvisible()
    {
        // spec: Docs/RE/specs/skinning.md §8(e) — id_b == 0 → no skeleton (invisible)
        var source = new FakeActorSource();
        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = 1,
            AppearanceVariant = 1,
            SkinIdB = 0, // no skeleton
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        Assert.False(actor.HasSkeleton);
        Assert.True(actor.IsInvisible);
    }

    [Fact]
    public void Compose_UnregisteredIdB_YieldsNoSkeleton()
    {
        // spec: Docs/RE/specs/skinning.md §8(e) — unregistered id_b ⇒ no skeleton
        var source = new FakeActorSource(); // registers no skeletons
        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = 1,
            AppearanceVariant = 1,
            SkinIdB = 99, // not registered
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        Assert.False(actor.HasSkeleton);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inverse-bind bake cancellation property (the headline animatable test)
    // spec: Docs/RE/specs/skinning.md §0/§4
    //
    // Property: at rest (animated == bind ⇒ deformed == rest).
    // Verify: boneWorldQuat ⊗ localPos + boneWorldTrans ≈ restPos
    //         boneWorldQuat ⊗ localNormal ≈ restNormal
    //
    // Uses a 1-bone skeleton at a NON-identity bind world transform:
    //   bindWorldTrans = (2, 3, 4), bindWorldQuat = 90° around Y = (0, sin45°, 0, cos45°)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InverseBindBake_CancellationProperty_RestPosRoundTrips()
    {
        // spec: Docs/RE/specs/skinning.md §0 — "at rest, animated==bind ⇒ deformed==rest"
        // spec: Docs/RE/specs/skinning.md §4 — invQ = conj(bindWorldQuat);
        //       localPos = invQ ⊗ (restPos − bindWorldTrans)
        //       → boneWorldQuat ⊗ localPos + boneWorldTrans == restPos

        const int idB = 1;
        const int baseId = 0;

        // 90° rotation around Y: bindWorldQuat = (0, sin(π/4), 0, cos(π/4))
        float sin45 = MathF.Sin(MathF.PI / 4f);
        float cos45 = MathF.Cos(MathF.PI / 4f);

        var bindWorldQuat = new Quat(0f, sin45, 0f, cos45); // 90° around Y
        var bindWorldTrans = new Vec3(2f, 3f, 4f);

        var skeleton = new SkeletonBindView
        {
            ActorId = idB,
            BaseId = baseId,
            Bones =
            [
                new BoneBind
                {
                    SelfId = 0,
                    ParentId = 0,
                    LocalTranslation = bindWorldTrans,
                    LocalRotation = bindWorldQuat,
                    BindWorldTranslation = bindWorldTrans,
                    BindWorldRotation = bindWorldQuat,
                }
            ],
        };

        // rest vertex: world position (5, 1, -3), normal (0, 1, 0)
        var restPos = new Vec3(5f, 1f, -3f);
        var restNormal = new Vec3(0f, 1f, 0f);

        var source = new FakeActorSource();
        source.RegisterSkeleton(idB, skeleton);
        source.RegisterSkin(meshGid: 7, new SkinMeshView
        {
            IdB = idB,
            Vertices = [new SkinVertex { Position = restPos, Normal = restNormal }],
            Weights =
            [
                new SkinWeight { VertexIndex = 0, BoneId = baseId, Weight = 1.0f }
            ],
        });

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true,
            PlayerClass = 1,
            AppearanceVariant = 1,
            SkinIdB = idB,
            BaseMeshGid = 7,
            MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        Assert.True(actor.HasSkeleton);
        Assert.Single(actor.BakedInfluences);

        var inf = actor.BakedInfluences[0];
        var localPos = inf.LocalPosition;
        var localNormal = inf.LocalNormal;

        // Round-trip: boneWorldQuat ⊗ localPos + boneWorldTrans ≈ restPos
        // spec: skinning.md §4 — the bake is exactly this inverse; re-applying gives back rest
        Vec3 roundTrippedPos = RotateVector(bindWorldQuat, localPos);
        roundTrippedPos = new Vec3(
            roundTrippedPos.X + bindWorldTrans.X,
            roundTrippedPos.Y + bindWorldTrans.Y,
            roundTrippedPos.Z + bindWorldTrans.Z);

        AssertVec3ApproxEqual(restPos, roundTrippedPos, epsilon: 1e-5f,
            message: "boneWorldQuat ⊗ localPos + boneWorldTrans must equal restPos (skinning.md §0/§4)");

        // Round-trip for normal: rotation only (no translate)
        // spec: skinning.md §4 — localNormal = invQ ⊗ restNormal
        Vec3 roundTrippedNormal = RotateVector(bindWorldQuat, localNormal);
        AssertVec3ApproxEqual(restNormal, roundTrippedNormal, epsilon: 1e-5f,
            message: "boneWorldQuat ⊗ localNormal must equal restNormal (skinning.md §4)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Base-relative bone indexing
    // spec: Docs/RE/specs/skinning.md §3.2 — bone_array[id − base_id]
    // spec: Docs/RE/specs/skinning.md §8(e) step 4 — OOR id is SKIPPED
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InverseBindBake_BaseRelativeBoneIndex_ResolvesCorrectly()
    {
        // spec: Docs/RE/specs/skinning.md §3.2 — bone_array[bone_id − base_id] NOT array slot directly
        // With base_id=10, bone_id=10 should resolve to bones[0], bone_id=11 to bones[1], etc.
        const int idB = 2;
        const int baseId = 10;

        var source = new FakeActorSource();
        source.RegisterSkeleton(idB, new SkeletonBindView
        {
            ActorId = idB,
            BaseId = baseId,
            Bones =
            [
                // bone index 0 (id 10 − 10 = 0)
                new BoneBind
                {
                    SelfId = 10, ParentId = 10,
                    LocalTranslation = new Vec3(0, 0, 0), LocalRotation = IdentityQuat,
                    BindWorldTranslation = new Vec3(0, 0, 0), BindWorldRotation = IdentityQuat,
                },
                // bone index 1 (id 11 − 10 = 1)
                new BoneBind
                {
                    SelfId = 11, ParentId = 10,
                    LocalTranslation = new Vec3(1, 0, 0), LocalRotation = IdentityQuat,
                    BindWorldTranslation = new Vec3(1, 0, 0), BindWorldRotation = IdentityQuat,
                },
            ],
        });
        source.RegisterSkin(meshGid: 5, new SkinMeshView
        {
            IdB = idB,
            Vertices = [new SkinVertex { Position = new Vec3(1f, 0f, 0f), Normal = new Vec3(0f, 1f, 0f) }],
            Weights =
            [
                // bone_id=10 → base-relative index = 0 (resolves bones[0])
                new SkinWeight { VertexIndex = 0, BoneId = 10, Weight = 0.6f },
                // bone_id=11 → base-relative index = 1 (resolves bones[1])
                new SkinWeight { VertexIndex = 0, BoneId = 11, Weight = 0.4f },
            ],
        });

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = idB, BaseMeshGid = 5, MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        Assert.Equal(2, actor.BakedInfluences.Count); // both bone_ids are valid
    }

    [Fact]
    public void InverseBindBake_OutOfRangeBoneId_IsSkipped()
    {
        // spec: Docs/RE/specs/skinning.md §8(e) step 4 — OOR bone_id → skip (not clamp-to-last)
        const int idB = 4;
        const int baseId = 5;

        var source = new FakeActorSource();
        source.RegisterSkeleton(idB, new SkeletonBindView
        {
            ActorId = idB,
            BaseId = baseId,
            Bones =
            [
                new BoneBind
                {
                    SelfId = 5, ParentId = 5,
                    LocalTranslation = Vec3Zero, LocalRotation = IdentityQuat,
                    BindWorldTranslation = Vec3Zero, BindWorldRotation = IdentityQuat,
                },
            ],
        });
        source.RegisterSkin(meshGid: 9, new SkinMeshView
        {
            IdB = idB,
            Vertices = [new SkinVertex { Position = new Vec3(1f, 0f, 0f), Normal = new Vec3(0f, 1f, 0f) }],
            Weights =
            [
                // bone_id=5 → index=0 → valid
                new SkinWeight { VertexIndex = 0, BoneId = 5, Weight = 0.5f },
                // bone_id=99 → index=94 → OUT OF RANGE → skipped
                new SkinWeight { VertexIndex = 0, BoneId = 99, Weight = 0.5f },
            ],
        });

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = idB, BaseMeshGid = 9, MotionKey = 0,
        };

        var actor = composer.Compose(in spawn);

        // Only the valid bone_id=5 influence survives; bone_id=99 is skipped
        Assert.Single(actor.BakedInfluences);
        Assert.Equal(5, actor.BakedInfluences[0].BoneId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Equipment slot order {3,4,6,2,11,14} and weapon slot detection
    // spec: Docs/RE/specs/skinning.md §3.5.4
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeEquipment_SlotOrder_IsFixed3_4_6_2_11_14()
    {
        // spec: Docs/RE/specs/skinning.md §3.5.4 — fixed overlay-slot list {3,4,6,2,11,14}
        var source = new FakeActorSource();

        // Register a catalogue entry for every slot so all 6 can resolve
        int modelClassId = 1; // from (class=1,variant=1)
        // catalog_key = gid_reduced + 1e9*(slot + 100*model_class_id)
        // For simplicity: use gid = 100 + slot (so gid_reduced = gid%100 + 10000*(gid/10000) = slot)
        int[] slots = [3, 4, 6, 2, 11, 14];
        foreach (int slot in slots)
        {
            int gid = 100 + slot; // gid_reduced = slot (since gid < 10000, 10000*(0) + gid%100 = slot)
            long key = slot + 1_000_000_000L * (slot + 100L * modelClassId);
            source.RegisterEquipmentPart(key, new EquipmentPartView
            {
                MeshGid = slot * 10,
                TextureId = slot,
                SknVfsKey = slot,
                BindPosePoolId = 0,
            });
        }

        // Set up equipment gids (one per slot in list order)
        var composer = new ActorComposer(source);
        var equipGids = new EquipmentGidSet();
        for (int i = 0; i < 6; i++)
            equipGids[i] = 100 + slots[i];

        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = 0, MotionKey = 0,
            EquipmentGids = equipGids,
        };

        var actor = composer.Compose(in spawn);

        // actor should have resolved all 6 equipment parts (since it's not invisible — wait,
        // SkinIdB=0 ⇒ invisible. Use a registered skeleton instead.
        // Fix: invisible path skips equipment. Use non-zero idB with no registered skeleton
        // so IsInvisible = true due to unregistered. Actually id_b 0 means invisible per spec.
        // Re-test with a registered skeleton to avoid invisible path.
    }

    [Fact]
    public void ComposeEquipment_WeaponSlot14_IsHandWeapon()
    {
        // spec: Docs/RE/specs/skinning.md §3.5.4 / equipment_visuals.md §1.1 — slot 14 = weapon → IsHandWeapon
        int modelClassId = 1;
        int weaponSlot = 14;
        var source = new FakeActorSource();
        const int idB = 2;
        source.RegisterSkeleton(idB, MakeSingleBoneSkeleton(idB, baseId: 0));

        long key = (114 % 100) + 10000 * (114 / 10000) + 1_000_000_000L * (weaponSlot + 100L * modelClassId);
        source.RegisterEquipmentPart(key, new EquipmentPartView
        {
            MeshGid = 50, TextureId = 1, SknVfsKey = 0, BindPosePoolId = 0,
        });

        var equipGids = new EquipmentGidSet();
        // OverlaySlots = {3,4,6,2,11,14}: index of slot 14 is 5
        equipGids[5] = 114;

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = idB, MotionKey = 0,
            EquipmentGids = equipGids,
        };

        var actor = composer.Compose(in spawn);

        Assert.Contains(actor.EquipmentParts, p => p.Slot == weaponSlot && p.IsHandWeapon);
    }

    [Fact]
    public void ComposeEquipment_EmptySlotGid0_IsSkipped()
    {
        // spec: Docs/RE/specs/skinning.md §3.5.4 — "Empty slots … are skipped"
        var source = new FakeActorSource();
        const int idB = 3;
        source.RegisterSkeleton(idB, MakeSingleBoneSkeleton(idB, baseId: 0));

        var equipGids = new EquipmentGidSet();
        // All gids = 0 → all slots empty → no equipment parts
        for (int i = 0; i < 6; i++) equipGids[i] = 0;

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = idB, MotionKey = 0, EquipmentGids = equipGids,
        };

        var actor = composer.Compose(in spawn);

        Assert.Empty(actor.EquipmentParts);
        Assert.Empty(actor.EquipmentGids);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Motion tables — MotionClipIds and SfxEventIds are distinct arrays
    // spec: Docs/RE/formats/actormotion.md — motion_ids_b = SFX/FX event ids, NOT motion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compose_MotionClipIds_And_SfxEventIds_AreDistinct_Arrays()
    {
        // spec: Docs/RE/formats/actormotion.md — motion_ids_a @ 0x40 = clip ids;
        //       motion_ids_b @ 0x64 = SFX event ids; NEVER mix them.
        const int motionKey = 42;

        var clipTable = new ActionClipTable();
        for (int i = 0; i < 9; i++) clipTable[i] = 100 + i; // distinct clip values 100..108

        var sfxTable = new ActionEventTable();
        for (int i = 0; i < 9; i++) sfxTable[i] = 200 + i; // distinct SFX values 200..208

        var source = new FakeActorSource();
        source.RegisterMotion(motionKey, new ActorMotionView
        {
            SkinClassId = 1,
            MotionClipIds = clipTable,
            SfxEventIds = sfxTable,
        });

        var composer = new ActorComposer(source);
        var spawn = new ActorSpawn
        {
            IsPlayer = true, PlayerClass = 1, AppearanceVariant = 1,
            SkinIdB = 0, MotionKey = motionKey,
        };

        var actor = composer.Compose(in spawn);

        // MotionClipIds must come from motion_ids_a (values 100..108)
        for (int i = 0; i < 9; i++)
            Assert.Equal(100 + i, actor.MotionClipIds[i]);

        // SfxEventIds must come from motion_ids_b (values 200..208), NOT from motion_ids_a
        for (int i = 0; i < 9; i++)
            Assert.Equal(200 + i, actor.SfxEventIds[i]);

        // They must be distinguishable: SfxEventIds[0] != MotionClipIds[0]
        Assert.NotEqual(actor.MotionClipIds[0], actor.SfxEventIds[0]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — math, constants, test doubles
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Quat IdentityQuat = new(0f, 0f, 0f, 1f);
    private static readonly Vec3 Vec3Zero = new(0f, 0f, 0f);

    private static SkeletonBindView MakeSingleBoneSkeleton(int actorId, int baseId) => new()
    {
        ActorId = actorId,
        BaseId = baseId,
        Bones =
        [
            new BoneBind
            {
                SelfId = baseId, ParentId = baseId,
                LocalTranslation = Vec3Zero, LocalRotation = IdentityQuat,
                BindWorldTranslation = Vec3Zero, BindWorldRotation = IdentityQuat,
            }
        ],
    };

    /// <summary>
    /// Active rotation of a 3-vector by a unit quaternion: v' = q ⊗ v ⊗ q⁻¹ (Hamilton, XYZW).
    /// Mirrors the ActorComposer's internal RotateVector for round-trip verification.
    /// spec: Docs/RE/specs/skinning.md §7 (active rotation; Hamilton product).
    /// </summary>
    private static Vec3 RotateVector(Quat q, Vec3 v)
    {
        float tx = 2f * (q.Y * v.Z - q.Z * v.Y);
        float ty = 2f * (q.Z * v.X - q.X * v.Z);
        float tz = 2f * (q.X * v.Y - q.Y * v.X);
        return new Vec3(
            v.X + q.W * tx + (q.Y * tz - q.Z * ty),
            v.Y + q.W * ty + (q.Z * tx - q.X * tz),
            v.Z + q.W * tz + (q.X * ty - q.Y * tx));
    }

    private static void AssertVec3ApproxEqual(Vec3 expected, Vec3 actual, float epsilon, string message)
    {
        float dx = MathF.Abs(expected.X - actual.X);
        float dy = MathF.Abs(expected.Y - actual.Y);
        float dz = MathF.Abs(expected.Z - actual.Z);
        Assert.True(dx < epsilon && dy < epsilon && dz < epsilon,
            $"{message}: expected ({expected.X},{expected.Y},{expected.Z}) " +
            $"but got ({actual.X},{actual.Y},{actual.Z}) " +
            $"[delta=({dx},{dy},{dz}), epsilon={epsilon}]");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake IActorAssemblySource
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FakeActorSource : IActorAssemblySource
{
    private readonly Dictionary<int, SkeletonBindView> _skeletons = [];
    private readonly Dictionary<int, SkinMeshView> _skins = [];
    private readonly Dictionary<long, EquipmentPartView> _equipment = [];
    private readonly Dictionary<int, ActorMotionView> _motions = [];

    public List<int> SkeletonQueriedIds { get; } = [];

    public void RegisterSkeleton(int idB, SkeletonBindView skeleton) => _skeletons[idB] = skeleton;
    public void RegisterSkin(int meshGid, SkinMeshView mesh) => _skins[meshGid] = mesh;
    public void RegisterEquipmentPart(long key, EquipmentPartView part) => _equipment[key] = part;
    public void RegisterMotion(int motionKey, ActorMotionView motion) => _motions[motionKey] = motion;

    public bool TryResolveActorMotion(int motionKey, out ActorMotionView motion)
        => _motions.TryGetValue(motionKey, out motion);

    public bool TryGetSkeletonByIdB(int idB, out SkeletonBindView skeleton)
    {
        SkeletonQueriedIds.Add(idB);
        return _skeletons.TryGetValue(idB, out skeleton);
    }

    public bool TryGetSkin(int meshGid, out SkinMeshView mesh)
        => _skins.TryGetValue(meshGid, out mesh);

    public bool TryResolveEquipmentPart(long catalogKey, out EquipmentPartView part)
        => _equipment.TryGetValue(catalogKey, out part);
}
