using System.Runtime.CompilerServices;

namespace MartialHeroes.Client.Application.World;

// =============================================================================
// ActorComposer — the engine-free actor bake (assembly_graph §2/§4).
//
// From an IActorAssemblySource + a spawn (a SpawnDescriptorReader span OR a
// neutral .arr-derived ActorSpawn), emits a neutral AssembledActor:
//   1. Identity → model class (skinning §3.5.2):
//        player (class,variant) → 5·(class + 4·variant) − 24 ∈ {1,11,16,26}
//        (variant 3 → 0 = invisible sentinel); mob → caller-resolved class.
//   2. Skeleton VERBATIM by id_b (skinning §8(e)): pose_pool[id_b], no g{N}.bnd
//        formatting, id_b 0/unregistered → no skeleton (invisible).
//   3. The inverse-bind bake (skinning §0/§4 — the animatable headline):
//        invQ = conj(bindWorldQuat);
//        localPos    = invQ ⊗ (restPos − bindWorldTrans);   // subtract THEN rotate
//        localNormal = invQ ⊗ restNormal;                   // rotation only
//        per-vertex weights normalized to sum 1.0; drop < 0.01 then renormalize;
//        bone resolved base-relative bone_array[bone_id − base_id], OOR skipped.
//   4. Equipment overlay (skinning §3.5 + equipment_visuals): iterate the fixed
//        slot list {3,4,6,2,11,14}; catalog_key = gid_reduced + 1e9·(slot +
//        100·model_class_id) (weapon slot 14 base-1000 reduction, others base-100);
//        weapon attaches to a hand bone (dual-hand off-hand flag).
//   5. Motion: emit MotionClipIds (action→.mot) + SfxEventIds (action→SFX). SFX
//        is NOT routed through animation.
//
// The model_class_id → concrete .bnd value-edge and the categoryBase[] table are
// PENDING a live debugger (skinning §3.5.5) — they are NEVER invented here; the
// caller supplies the resolved id_b and the equipment gids.
//
// Engine-free: float Vec3/Quat helpers below — NO Godot.Quaternion/Vector3.
// =============================================================================

/// <summary>
///     A neutral, engine-free spawn input the offline port builds from an <c>.arr</c> record (or the
///     caller derives from the wire 880-byte descriptor — see
///     <see cref="ActorComposer.Compose(in ActorSpawn, IActorAssemblySource)" />
///     and the <see cref="System.ReadOnlySpan{T}" /> overload). It carries the identity inputs the
///     composer needs that are NOT byte-decoded here.
/// </summary>
/// <remarks>
///     The <see cref="SkinIdB" />, <see cref="ModelClassId" />, and <see cref="MotionKey" /> are
///     caller-resolved because their value-edges (the <c>model_class_id → .bnd</c> map and the
///     <c>categoryBase[]</c> table) are pending a live debugger and must not be invented. spec:
///     Docs/RE/specs/skinning.md §3.5.5.
/// </remarks>
public readonly struct ActorSpawn
{
    /// <summary>
    ///     For a player: the internal class index (1..4). For a mob: ignored (use <see cref="MotionKey" />).
    ///     spec: Docs/RE/specs/skinning.md §3.5.2; Docs/RE/structs/spawn_descriptor.md (+0x34 internal_class).
    /// </summary>
    public int PlayerClass { get; init; }

    /// <summary>
    ///     For a player: the appearance variant (variant 3 ⇒ invisible sentinel). For a mob: ignored.
    ///     spec: Docs/RE/specs/skinning.md §3.5.2; Docs/RE/structs/spawn_descriptor.md (+0x2C appearance_variant).
    /// </summary>
    public int AppearanceVariant { get; init; }

    /// <summary>
    ///     <see langword="true" /> for a player (compute <c>model_class_id</c> from class+variant);
    ///     <see langword="false" /> for a mob (use the caller-resolved <see cref="ModelClassId" />).
    ///     spec: Docs/RE/specs/assembly_graph.md §2 (Identity → model class).
    /// </summary>
    public bool IsPlayer { get; init; }

    /// <summary>
    ///     The caller-resolved <c>model_class_id</c> for a mob (reached through the mobs.scr → catalogue
    ///     indirection; NOT computed here). Ignored for players (computed from class+variant).
    ///     spec: Docs/RE/specs/skinning.md §8(e) (mob indirection); §3.5.5 (value-edge caller-supplied).
    /// </summary>
    public int ModelClassId { get; init; }

    /// <summary>
    ///     The skin's <c>id_b</c> (skeleton selector — the verbatim pose-pool key, §8(e)). Caller-resolved
    ///     from the base skin (slot 3) header. <c>0</c> ⇒ no skeleton (invisible). spec: skinning.md §8(e).
    /// </summary>
    public int SkinIdB { get; init; }

    /// <summary>
    ///     The base body skin mesh GID (slot 3 — there is no separate base mesh; §3.5.1). The composer
    ///     bakes the inverse-bind for this mesh. spec: Docs/RE/specs/skinning.md §3.5.1/§3.5.3.
    /// </summary>
    public int BaseMeshGid { get; init; }

    /// <summary>
    ///     The actormotion catalogue key (record +0x00 <c>motion_key</c>) for the motion table lookup.
    ///     spec: Docs/RE/formats/actormotion.md (computed lookup key).
    /// </summary>
    public int MotionKey { get; init; }

    /// <summary>The world-space X spawn origin. spec: Docs/RE/formats/npc_spawns.md (+0x04 world_x).</summary>
    public float WorldX { get; init; }

    /// <summary>The world-space Z spawn origin. spec: Docs/RE/formats/npc_spawns.md (+0x08 world_z).</summary>
    public float WorldZ { get; init; }

    /// <summary>
    ///     The spawn yaw (radians). For an <c>.arr</c>-derived NPC the caller passes
    ///     <c>π/2 − facing</c> here; the wire path passes the actor yaw directly. spec:
    ///     Docs/RE/formats/npc_spawns.md (+0x12 facing; runtime applies <c>π/2 − value</c>).
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    ///     The per-slot equipment GIDs in the fixed overlay-slot order <c>{3,4,6,2,11,14}</c>; a slot's
    ///     GID of <c>0</c> means "empty" (skipped). spec: Docs/RE/specs/skinning.md §3.5.4;
    ///     Docs/RE/structs/spawn_descriptor.md (+0x58 equip_ref_table leading dword = worn-item id).
    /// </summary>
    public EquipmentGidSet EquipmentGids { get; init; }
}

/// <summary>
///     The per-slot equipment GIDs for the six fixed overlay slots <c>{3,4,6,2,11,14}</c>, indexed by
///     the slot's position in that fixed list (0..5). spec: Docs/RE/specs/skinning.md §3.5.4.
/// </summary>
[InlineArray(SlotCount)]
public struct EquipmentGidSet
{
    /// <summary>The number of fixed overlay slots <c>{3,4,6,2,11,14}</c>. spec: skinning.md §3.5.4.</summary>
    public const int SlotCount = 6;

    private int _slot0;
}

/// <summary>
///     The engine-free actor bake. Composes a neutral <see cref="AssembledActor" /> from an
///     <see cref="IActorAssemblySource" /> + a spawn. spec: Docs/RE/specs/assembly_graph.md §2/§4.
/// </summary>
public sealed class ActorComposer
{
    /// <summary>
    ///     The weapon overlay slot. Uses the base-1000 reduction and attaches to a hand bone.
    ///     spec: Docs/RE/specs/skinning.md §3.5.4; Docs/RE/specs/equipment_visuals.md §1.1/§5.
    /// </summary>
    private const int WeaponSlot = 14; // spec: equipment_visuals.md §1.1 (slot 14 = weapon)

    /// <summary>
    ///     The appearance variant whose <c>model_class_id</c> resolves to <c>0</c> = invisible / no-mesh.
    ///     spec: Docs/RE/specs/skinning.md §3.5.2 ("variant == 3 resolves to 0 … invisible actor").
    /// </summary>
    private const int InvisibleVariant = 3; // spec: skinning.md §3.5.2

    /// <summary>
    ///     The per-vertex weight floor: influences below this are dropped, then survivors renormalized.
    ///     spec: Docs/RE/specs/skinning.md §5.1/§5.2 (keep test <c>weight ≥ ~0.01</c>).
    /// </summary>
    private const float MinWeight = 0.01f; // spec: skinning.md §5.1 (0.0099999998 keep test)

    /// <summary>
    ///     The fixed overlay-slot list iterated per equip rebuild, in attach order. Slot 3 = body, 14 =
    ///     weapon. spec: Docs/RE/specs/skinning.md §3.5.4; Docs/RE/specs/equipment_visuals.md §1.1.
    /// </summary>
    private static readonly int[] OverlaySlots = [3, 4, 6, 2, 11, 14];

    private readonly IActorAssemblySource _source;

    /// <summary>Creates the composer over an actor-assembly source port.</summary>
    public ActorComposer(IActorAssemblySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>
    ///     Composes an <see cref="AssembledActor" /> from a wire 880-byte spawn descriptor span plus the
    ///     caller-resolved identity inputs that are not byte-decoded by
    ///     <see cref="SpawnDescriptorReader" />. The descriptor supplies world X/Z; the caller supplies
    ///     the resolved <c>id_b</c> / <c>model_class_id</c> / equipment gids (value-edges pending a
    ///     debugger — §3.5.5). spec: Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    /// <param name="descriptor">The 880-byte spawn descriptor span (5/3 CharSpawn inner block).</param>
    /// <param name="identity">
    ///     The caller-resolved identity inputs (<c>id_b</c>, <c>model_class_id</c>, motion key, equipment
    ///     gids); its world placement fields are ignored — they come from the descriptor.
    /// </param>
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

    /// <summary>
    ///     Composes an <see cref="AssembledActor" /> from a neutral spawn input (the offline port builds
    ///     this from an <c>.arr</c> record + the visual catalogue). spec: Docs/RE/specs/assembly_graph.md §4
    ///     ("accept a spawn descriptor the offline port builds from .arr OR the wire").
    /// </summary>
    public AssembledActor Compose(in ActorSpawn spawn)
    {
        return Compose(in spawn, _source);
    }

    private static AssembledActor Compose(in ActorSpawn spawn, IActorAssemblySource source)
    {
        // ── 1. Identity → model class ────────────────────────────────────────
        // Player: 5·(class + 4·variant) − 24 ∈ {1,11,16,26}; variant 3 ⇒ 0 = invisible.
        // Mob: caller-resolved (mobs.scr → catalogue indirection — NOT a literal g{skin_class}.bnd).
        // spec: skinning.md §3.5.2; §8(e).
        int modelClassId;
        bool invisible;
        if (spawn.IsPlayer)
        {
            if (spawn.AppearanceVariant == InvisibleVariant)
            {
                modelClassId = 0; // spec: skinning.md §3.5.2 (variant 3 ⇒ 0 = invisible)
                invisible = true;
            }
            else
            {
                modelClassId = 5 * (spawn.PlayerClass + 4 * spawn.AppearanceVariant) - 24; // spec: skinning.md §3.5.2
                invisible = false;
            }
        }
        else
        {
            modelClassId = spawn.ModelClassId; // spec: skinning.md §8(e) (mob catalogue indirection)
            invisible = modelClassId == 0;
        }

        // ── 5. Motion (clips + SFX) ──────────────────────────────────────────
        // Emitted regardless of mesh resolution; SfxEventIds is NOT routed through animation.
        // spec: actormotion.md (motion_ids_a action→.mot; motion_ids_b SFX/FX event ids).
        ActionClipTable clips = default;
        ActionEventTable sfx = default;
        if (source.TryResolveActorMotion(spawn.MotionKey, out var motion))
        {
            clips = motion.MotionClipIds;
            sfx = motion.SfxEventIds;
        }

        // ── 2. Skeleton select VERBATIM by id_b ──────────────────────────────
        // pose_pool[id_b]; id_b 0 / unregistered ⇒ no skeleton (invisible/unskinned).
        // spec: skinning.md §8(e); bindlist.md (verbatim id_b key).
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
            invisible = invisible || spawn.SkinIdB == 0; // id_b 0 ⇒ no mesh (spec: skinning.md §8(e))
        }

        // ── 3. The inverse-bind bake (the animatable payload) ────────────────
        var baked = new List<BakedInfluence>();
        if (hasSkeleton && source.TryGetSkin(spawn.BaseMeshGid, out var baseSkin)) BakeSkin(baseSkin, skeleton, baked);

        // ── 4. Equipment overlay ─────────────────────────────────────────────
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

    // -------------------------------------------------------------------------
    // The inverse-bind bake. spec: skinning.md §0 / §4 / §3.2 / §5.2.
    // -------------------------------------------------------------------------

    private static void BakeSkin(
        in SkinMeshView skin, in SkeletonBindView skeleton, List<BakedInfluence> baked)
    {
        // Per-vertex weight normalization (drop < 0.01, then renormalize to sum 1.0).
        // spec: skinning.md §5.2 ("Normalizes the influence weights so they sum to 1.0";
        //       drop weight < 0.01 then renormalize — §5.4 importer rule).
        var vertexTotals = new Dictionary<int, float>();
        var weights = skin.Weights;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i];
            if (w.Weight < MinWeight) continue; // spec: skinning.md §5.1 (records below ~0.01 are dropped at load)

            vertexTotals.TryGetValue(w.VertexIndex, out var total);
            vertexTotals[w.VertexIndex] = total + w.Weight;
        }

        var verts = skin.Vertices;
        var bones = skeleton.Bones;
        var baseId = skeleton.BaseId;

        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i];
            if (w.Weight < MinWeight) continue; // dropped (spec: skinning.md §5.1)

            // Base-relative bone-ID resolve: bone_array[bone_id − base_id] — NOT array slot / palette /
            // track index (the explosion root cause). An out-of-range id is SKIPPED (importer hardening,
            // §8(e) step 4) — NOT clamped to the last bone (the faithful legacy behaviour) — to keep a
            // mismatched influence inert rather than dragging the mesh onto one bone.
            // spec: skinning.md §3.2 / §8(e) step 4.
            var boneIndex = w.BoneId - baseId;
            if ((uint)boneIndex >=
                (uint)bones.Count) continue; // out-of-range → skip (spec: skinning.md §8(e) step 4 importer hardening)

            if ((uint)w.VertexIndex >=
                (uint)verts.Count) continue; // defensive: a weight pointing past the vertex array is inert

            var total = vertexTotals[w.VertexIndex];
            if (total <= 0f)
                continue; // a zero-total vertex contributes nothing (spec: §5.2 zero-total is fatal in legacy)

            var weight = w.Weight / total; // renormalize to the surviving sum (spec: skinning.md §5.2/§5.4)

            var bone = bones[boneIndex];
            var vertex = verts[w.VertexIndex];

            // invQ = conj(bindWorldQuat) = (−x,−y,−z,w) for a unit quaternion. spec: skinning.md §4/§7.
            var invQ = Conjugate(bone.BindWorldRotation);

            // localPos = invQ ⊗ (restPos − bindWorldTrans)  (subtract THEN rotate). spec: skinning.md §4.
            var localPos = RotateVector(invQ, Subtract(vertex.Position, bone.BindWorldTranslation));

            // localNormal = invQ ⊗ restNormal  (rotation only, no translate). spec: skinning.md §4.
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

    // -------------------------------------------------------------------------
    // Equipment overlay. spec: skinning.md §3.5.4; equipment_visuals.md §3/§5.
    // -------------------------------------------------------------------------

    private static void ComposeEquipment(
        EquipmentGidSet gids,
        int modelClassId,
        IActorAssemblySource source,
        List<EquipmentPart> parts,
        List<int> emittedGids)
    {
        // Slot-gate the binary applies (ActorVisual_RebindLocalPlayerParts): when the appearance key
        // exceeds the player/mob boundary, only slot 3 (the body) is rebuilt; otherwise the full
        // {3,4,6,2,11,14} set. The boundary corresponds to categoryBase[3] = 1000 (player keys
        // {1,11,16,26} <= 1000 → full set; mob keys > 1000 → slot 3 only). This matches the existing
        // EquipOverlayResolver.RunsOverlayResolution(1000) threshold so the composer converges with the
        // binary. spec: equipment_visuals.md §1.1/§3.4 (threshold 1000; full vs reduced).
        var bodyOnly = modelClassId > 1000; // > categoryBase[3] → slot 3 only (mob/general)

        for (var i = 0; i < OverlaySlots.Length; i++)
        {
            var slot = OverlaySlots[i];
            if (bodyOnly && slot != 3) continue; // reduced rebuild set {3} above the boundary
            var equipGid = gids[i];
            if (equipGid == 0) continue; // empty slot → no node (spec: skinning.md §3.5.4 "Empty slots … are skipped")

            // reduced gid: weapon slot 14 uses the wider base-1000 reduction; others base-100.
            // spec: skinning.md §3.5.4; equipment_visuals.md §3.1/§3.2.
            var gidReduced = ReduceGid(equipGid, slot);

            // catalog_key = gid_reduced + 1e9·(slot + 100·model_class_id). spec: skinning.md §3.5.3.
            var catalogKey = gidReduced + 1_000_000_000L * (slot + 100L * modelClassId);

            if (!source.TryResolveEquipmentPart(catalogKey,
                    out var part))
                continue; // a catalogue miss yields null mesh pointers (spec: equipment_visuals.md §3.2)

            var isWeapon = slot == WeaponSlot; // spec: equipment_visuals.md §1.1 (slot 14 = weapon)
            emittedGids.Add(equipGid);
            parts.Add(new EquipmentPart
            {
                Slot = slot,
                EquipmentGid = equipGid,
                MeshGid = part.MeshGid,
                TextureId = part.TextureId,
                IsHandWeapon = isWeapon, // weapon attaches to a hand bone (spec: equipment_visuals.md §5)
                IsOffHand = false // main-hand node; dual-hand off-hand is a second node (§5.1)
            });
        }
    }

    /// <summary>
    ///     The per-slot GID reduction. Non-weapon slots {3,4,6,2,11}:
    ///     <c>gid_reduced = 10000·(gid/10000) + gid%100</c>. The weapon (slot 14) digit-packing is
    ///     UPSTREAM/caller-supplied — the binary's ActorVisual_BuildPart weapon branch builds the weapon
    ///     gid from per-character appearance digits as
    ///     <c>gid_reduced = 1000·(faceA + 10·(mobid + 10·(b + 10·(gid/1000000)))) + gid%1000</c>
    ///     (faceA = +0x22, b = +0x34) BEFORE this composer sees it; at this level the slot's equipment gid
    ///     is already the resolved weapon gid, so the shared non-weapon reduction is applied for the
    ///     catalogue key and the <c>slot</c> term (14) in §3.5.3 keeps the weapon key distinct.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.1 (weapon digit-packing is upstream of the catalogue key)
    ///     / §3.2 (non-weapon reduction); Docs/RE/specs/skinning.md §3.5.4.
    /// </summary>
    private static int ReduceGid(int gid, int slot)
    {
        // Non-weapon parts: gid = 10000·(part/10000) + part%100. spec: equipment_visuals.md §3.2.
        // Weapon (slot 14): the base-1000 digit formula keys on appearance fields the composer does not
        // own here (the weapon GID is built from per-character appearance digits + the weapon part-id —
        // equipment_visuals.md §3.1). At this composer's level the slot's equipment gid is already the
        // resolved weapon gid, so we apply the shared non-weapon reduction for the catalogue key; the
        // weapon-specific appearance-digit packing is an upstream concern (the caller resolves the
        // weapon gid). The catalogue key still differs from non-weapon slots via the `slot` term (14)
        // in §3.5.3. spec: equipment_visuals.md §3.1 (weapon digit math is upstream of the catalogue key).
        _ = slot; // slot drives the catalogue key (§3.5.3), not the reduction shape at this level
        return 10000 * (gid / 10000) + gid % 100; // spec: equipment_visuals.md §3.2
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

    // -------------------------------------------------------------------------
    // Float quaternion / vector math — XYZW, Hamilton, active rotation, no Godot.
    // spec: skinning.md §7 (XYZW; Hamilton product; active rotation q ⊗ v ⊗ q⁻¹;
    //       inverse = conjugate (−x,−y,−z,w) for a unit quaternion).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     The unit-quaternion conjugate <c>(−x, −y, −z, w)</c> = the inverse for a unit quaternion.
    ///     spec: Docs/RE/specs/skinning.md §4/§7.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quat Conjugate(Quat q)
    {
        return new Quat(-q.X, -q.Y, -q.Z, q.W);
    }

    /// <summary>Vector subtraction. spec: Docs/RE/specs/skinning.md §4 (restPos − bindWorldTrans).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vec3 Subtract(Vec3 a, Vec3 b)
    {
        return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>
    ///     Active rotation of a 3-vector by a unit quaternion: <c>v' = q ⊗ v ⊗ q⁻¹</c> (Hamilton, XYZW).
    ///     Computed via the standard expansion <c>v + 2·q.w·(qv × v) + 2·(qv × (qv × v))</c>, where
    ///     <c>qv = (q.x, q.y, q.z)</c> — equivalent to the full triple product for a unit quaternion.
    ///     spec: Docs/RE/specs/skinning.md §7 (active rotation; Hamilton product).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vec3 RotateVector(Quat q, Vec3 v)
    {
        // t = 2 · (qv × v)
        var tx = 2f * (q.Y * v.Z - q.Z * v.Y);
        var ty = 2f * (q.Z * v.X - q.X * v.Z);
        var tz = 2f * (q.X * v.Y - q.Y * v.X);

        // v' = v + q.w · t + qv × t
        var rx = v.X + q.W * tx + (q.Y * tz - q.Z * ty);
        var ry = v.Y + q.W * ty + (q.Z * tx - q.X * tz);
        var rz = v.Z + q.W * tz + (q.X * ty - q.Y * tx);
        return new Vec3(rx, ry, rz);
    }
}