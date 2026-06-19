namespace MartialHeroes.Client.Application.World;

// =============================================================================
// IActorAssemblySource — the engine-free input port for the ActorComposer.
//
// The composer resolves the actor-bake chain (assembly_graph.md §2) WITHOUT
// depending on a concrete parser (Assets.Parsers) or the VFS. A caller (the
// layer-05 composition root) wires this port to the boot-loaded catalogues:
// the actormotion table, the eager-preloaded bind-pose pool (bindlist.md), the
// skin-mesh cache, and items.scr equipment keys.
//
// All view structs below are pure-BCL (float triples/quads) — NO Godot type.
// The composer bakes the inverse-bind (skinning.md §4) from these inputs and
// emits a neutral AssembledActor; the mesh build is layer-05 (Phase 6).
//
// spec: Docs/RE/specs/assembly_graph.md §2/§4 (ActorComposer contract);
//       Docs/RE/specs/skinning.md §8(e) (verbatim id_b pool key);
//       Docs/RE/formats/actormotion.md (motion record);
//       Docs/RE/formats/bindlist.md (eager preload, actor_id key);
//       Docs/RE/formats/items_scr.md §1.4.2 (+0x80 / +0x84 equipment keys).
// =============================================================================

/// <summary>
/// The input port the <see cref="ActorComposer"/> consumes to resolve the actor-bake chain without
/// a concrete-parser or VFS dependency. A layer-05 caller adapts the boot-loaded catalogues
/// (actormotion, bind-pose pool, skin cache, items.scr) to this interface.
/// </summary>
/// <remarks>
/// Engine-free: every member traffics in neutral CLR view structs (float triples/quads), never a
/// Godot type and never a concrete parser model. spec: Docs/RE/specs/assembly_graph.md §4
/// ("emit a valid actor descriptor … independent of the mesh-build step (which is layer-05)").
/// </remarks>
public interface IActorAssemblySource
{
    /// <summary>
    /// Resolves the actormotion catalogue record for an actor by its computed motion key
    /// (the mob→appearance→catalogue chain; for players the key is derived upstream). The record
    /// carries the skin/skeleton class and the action→clip / action→SFX tables.
    /// </summary>
    /// <param name="motionKey">
    /// The catalogue lookup key (actormotion record +0x00 <c>motion_key</c>). For mobs this is
    /// resolved through the mobs.scr → catalogue indirection; for players it is derived from the
    /// appearance/class. spec: Docs/RE/formats/actormotion.md ("Computed lookup key (record +0x00)");
    /// Docs/RE/specs/assembly_graph.md §2 (Identity → model class).
    /// </param>
    /// <returns><see langword="true"/> if a record was found; otherwise <see langword="false"/>.</returns>
    bool TryResolveActorMotion(int motionKey, out ActorMotionView motion);

    /// <summary>
    /// Looks up the eager-preloaded bind-pose skeleton by the skin's <c>id_b</c> used VERBATIM as the
    /// pool key (no <c>g{N}.bnd</c> formatting, no appearance-slot transform at this site). The pool
    /// is keyed by each preloaded <c>.bnd</c>'s parsed <c>actor_id</c>.
    /// </summary>
    /// <param name="idB">
    /// The skin header <c>id_b</c> (skeleton selector). <c>0</c> / unregistered ⇒ no skeleton.
    /// spec: Docs/RE/specs/skinning.md §8(e) ("selected_skeleton(skn) = pose_pool[ skn.header.id_b ]");
    /// Docs/RE/formats/bindlist.md (eager boot preload, register by parsed actor_id).
    /// </param>
    /// <returns><see langword="true"/> if a skeleton is registered for <paramref name="idB"/>.</returns>
    bool TryGetSkeletonByIdB(int idB, out SkeletonBindView skeleton);

    /// <summary>
    /// Resolves a skin mesh by its mesh GID (the rest vertices + the 12-byte weight records). The
    /// composer bakes the inverse-bind against the skeleton resolved from this skin's <c>id_b</c>.
    /// </summary>
    /// <param name="meshGid">
    /// The mesh GID (<c>data/char/skin/g{meshGid}.skn</c>). spec: Docs/RE/specs/skinning.md §3.5.3
    /// (skin.txt col4 = mesh gid); Docs/RE/formats/mesh.md (<c>.skn</c> vertices/weights).
    /// </param>
    /// <returns><see langword="true"/> if the skin was found.</returns>
    bool TryGetSkin(int meshGid, out SkinMeshView mesh);

    /// <summary>
    /// Resolves an equipment GID for a fixed visual slot to its part mesh GID, texture id, and the
    /// items.scr asset keys (<c>+0x80</c> mesh selector, <c>+0x84</c> bind-pose pool id).
    /// </summary>
    /// <param name="catalogKey">
    /// The 64-bit catalogue key the overlay draw path recomputes:
    /// <c>gid_reduced + 1e9·(slot + 100·model_class_id)</c>.
    /// spec: Docs/RE/specs/skinning.md §3.5.3/§3.5.4; Docs/RE/specs/equipment_visuals.md §3.2.
    /// </param>
    /// <returns><see langword="true"/> if the part resolved; an empty slot returns <see langword="false"/>.</returns>
    bool TryResolveEquipmentPart(long catalogKey, out EquipmentPartView part);
}

// =============================================================================
// Neutral view structs — the bake's inputs. Pure CLR (float triples/quads).
// =============================================================================

/// <summary>
/// A neutral view of one actormotion catalogue record's animation/skeleton fields.
/// spec: Docs/RE/formats/actormotion.md (136-byte record).
/// </summary>
public readonly struct ActorMotionView
{
    /// <summary>
    /// The actor-to-skeleton key (<c>int_a</c> = <c>skin_class</c>, record +0x04). For mobs this is
    /// the skeleton's <c>id_b</c> reached through the catalogue indirection; <c>0</c> = null skeleton.
    /// spec: Docs/RE/formats/actormotion.md (col2 = skin_class); Docs/RE/specs/skinning.md §8(e).
    /// </summary>
    public required int SkinClassId { get; init; }

    /// <summary>
    /// The action→<c>.mot</c> clip table (<c>motion_ids_a</c>, record +0x40, 9 slots:
    /// [0] idle file-source, [1] idle, [2] walk, [3] run, [4] death, [5] mount-idle, [6] combat-idle).
    /// Feeds the ANIMATION layer. spec: Docs/RE/formats/actormotion.md (motion_ids_a slot table).
    /// </summary>
    public required ActionClipTable MotionClipIds { get; init; }

    /// <summary>
    /// The action→SFX/FX event-id table (<c>motion_ids_b</c>, record +0x64, 9 slots). These are
    /// SOUND/EFFECT event ids, NOT motion — they must NOT be routed through the animation sampler.
    /// spec: Docs/RE/formats/actormotion.md ("motion_ids_b … SOUND/EFFECT event ids NOT motion").
    /// </summary>
    public required ActionEventTable SfxEventIds { get; init; }
}

/// <summary>
/// The 9-slot <c>motion_ids_a</c> action→<c>.mot</c> clip table (action/lifecycle keyed, NOT
/// direction). spec: Docs/RE/formats/actormotion.md (motion_ids_a, +0x40, 9 × i32).
/// </summary>
[System.Runtime.CompilerServices.InlineArray(SlotCount)]
public struct ActionClipTable
{
    /// <summary>Number of slots in <c>motion_ids_a</c>. spec: Docs/RE/formats/actormotion.md (i32[9]).</summary>
    public const int SlotCount = 9;

    private int _slot0;
}

/// <summary>
/// The 9-slot <c>motion_ids_b</c> action→SOUND/EFFECT event-id table (NOT motion).
/// spec: Docs/RE/formats/actormotion.md (motion_ids_b, +0x64, 9 × i32).
/// </summary>
[System.Runtime.CompilerServices.InlineArray(SlotCount)]
public struct ActionEventTable
{
    /// <summary>Number of slots in <c>motion_ids_b</c>. spec: Docs/RE/formats/actormotion.md (i32[9]).</summary>
    public const int SlotCount = 9;

    private int _slot0;
}

/// <summary>
/// A neutral bind-pose skeleton view: the per-bone parent-relative locals plus the load-computed
/// bind-WORLD transforms the inverse-bind bake consumes, plus the base-relative id resolver inputs.
/// spec: Docs/RE/specs/skinning.md §3.1/§3.2/§3.4.
/// </summary>
public readonly struct SkeletonBindView
{
    /// <summary>
    /// The skeleton's <c>actor_id</c> (the pool key == the skin's <c>id_b</c>).
    /// spec: Docs/RE/formats/bindlist.md (registered by parsed actor_id).
    /// </summary>
    public required int ActorId { get; init; }

    /// <summary>
    /// The first bone's <c>self_id</c> — the base of the base-relative bone-ID resolver
    /// (<c>bone_array[id − base_id]</c>). spec: Docs/RE/specs/skinning.md §3.2.
    /// </summary>
    public required int BaseId { get; init; }

    /// <summary>The bones, in array order (resolved by <c>id − BaseId</c>). spec: skinning.md §3.2.</summary>
    public required IReadOnlyList<BoneBind> Bones { get; init; }
}

/// <summary>
/// One bind-pose bone: the parent-relative local trans/quat (for a layer-05 <c>Skeleton3D</c> rest
/// pose) plus the load-computed bind-WORLD trans/quat (the inverse-bind bake source).
/// spec: Docs/RE/specs/skinning.md §3.1 (world walk) / §3.4 (in-memory bind bone).
/// </summary>
public readonly struct BoneBind
{
    /// <summary>This bone's <c>self_id</c>. spec: Docs/RE/specs/skinning.md §3.2.</summary>
    public required int SelfId { get; init; }

    /// <summary>The parent bone's <c>self_id</c> (root: <c>self_id == parent_id == 0</c>). spec: skinning.md §3.2.</summary>
    public required int ParentId { get; init; }

    /// <summary>Parent-relative local translation (3 × f32). spec: Docs/RE/specs/skinning.md §3.1.</summary>
    public required Vec3 LocalTranslation { get; init; }

    /// <summary>Parent-relative local rotation (XYZW). spec: Docs/RE/specs/skinning.md §3.1.</summary>
    public required Quat LocalRotation { get; init; }

    /// <summary>Bind-WORLD translation (load-computed by the bind-world walk). spec: skinning.md §3.1/§3.4.</summary>
    public required Vec3 BindWorldTranslation { get; init; }

    /// <summary>Bind-WORLD rotation (XYZW, load-computed). spec: Docs/RE/specs/skinning.md §3.1/§3.4.</summary>
    public required Quat BindWorldRotation { get; init; }
}

/// <summary>
/// A neutral skinned-mesh view: rest vertices (position+normal) and the 12-byte weight influence
/// records (<c>vertex_index</c>, <c>bone_index</c> = bone ID, <c>weight</c>).
/// spec: Docs/RE/specs/skinning.md §2 (32-byte render vertex, 12-byte weight); Docs/RE/formats/mesh.md.
/// </summary>
public readonly struct SkinMeshView
{
    /// <summary>
    /// The skin header <c>id_b</c> (skeleton selector — the verbatim pool key, §8(e)). <c>0</c> ⇒
    /// no skeleton. spec: Docs/RE/specs/skinning.md §8(e); Docs/RE/formats/mesh.md (.skn header).
    /// </summary>
    public required int IdB { get; init; }

    /// <summary>The rest-pose render vertices (position + normal). spec: Docs/RE/specs/skinning.md §2.1.</summary>
    public required IReadOnlyList<SkinVertex> Vertices { get; init; }

    /// <summary>The 12-byte weight influence records. spec: Docs/RE/specs/skinning.md §2.2 / §5.1.</summary>
    public required IReadOnlyList<SkinWeight> Weights { get; init; }
}

/// <summary>
/// One rest-pose render vertex: model-space position + normal (the bake's geometry inputs).
/// spec: Docs/RE/specs/skinning.md §2.1 (32-byte render vertex; UV omitted — bake reads pos/normal).
/// </summary>
public readonly struct SkinVertex
{
    /// <summary>Model-space rest position. spec: Docs/RE/specs/skinning.md §2.1 (pos[0..11]).</summary>
    public required Vec3 Position { get; init; }

    /// <summary>Model-space rest normal. spec: Docs/RE/specs/skinning.md §2.1 (normal[12..23]).</summary>
    public required Vec3 Normal { get; init; }
}

/// <summary>
/// One 12-byte skin weight influence record. <c>BoneId</c> resolves the bind/pose bone by
/// <c>id − base_id</c> (NOT an array slot / palette / track index — §3.2).
/// spec: Docs/RE/specs/skinning.md §5.1 (12-byte weight record).
/// </summary>
public readonly struct SkinWeight
{
    /// <summary>The unique render vertex this influence affects. spec: skinning.md §2.2.</summary>
    public required int VertexIndex { get; init; }

    /// <summary>The bone ID in base-relative bone-ID space (<c>id − base_id</c>). spec: skinning.md §3.2.</summary>
    public required int BoneId { get; init; }

    /// <summary>The scalar influence weight (records with <c>weight &lt; ~0.01</c> are dropped). spec: skinning.md §5.1/§5.2.</summary>
    public required float Weight { get; init; }
}

/// <summary>
/// A resolved equipment part: its mesh GID, texture id, and the items.scr asset keys.
/// spec: Docs/RE/specs/skinning.md §3.5.3 (mesh_gid, tex_id); Docs/RE/formats/items_scr.md §1.4.2.
/// </summary>
public readonly struct EquipmentPartView
{
    /// <summary>The part mesh GID (<c>data/char/skin/g{MeshGid}.skn</c>). spec: skinning.md §3.5.3 (col4).</summary>
    public required int MeshGid { get; init; }

    /// <summary>The part texture id (<c>tex_id</c>). spec: Docs/RE/specs/skinning.md §3.5.3 (col5).</summary>
    public required int TextureId { get; init; }

    /// <summary>
    /// items.scr <c>+0x80</c> mesh-selector key (<c>data/char/skin/g{SknVfsKey}.skn</c>), or <c>0</c>
    /// if unset. spec: Docs/RE/formats/items_scr.md §1.4.2 (model_ref_key).
    /// </summary>
    public required int SknVfsKey { get; init; }

    /// <summary>
    /// items.scr <c>+0x84</c> bind-pose pool id (an id into the preloaded skeleton pool), or <c>0</c>.
    /// spec: Docs/RE/formats/items_scr.md §1.4.2 (anim_ref_key).
    /// </summary>
    public required int BindPosePoolId { get; init; }
}

// =============================================================================
// Float math view types — pure CLR, no Godot. The legacy engine's skinning math
// is float quaternions + 3-vectors (skinning.md §0/§7), so the bake geometry is
// float-precision, NOT the deterministic Q16.16 Vector3Fixed (gameplay trajectory).
// spec: Docs/RE/specs/skinning.md §7 (XYZW unit quaternions + 3-vectors, no 4×4).
// =============================================================================

/// <summary>A neutral float 3-vector (no Godot dependency). spec: Docs/RE/specs/skinning.md §7.</summary>
public readonly record struct Vec3(float X, float Y, float Z);

/// <summary>
/// A neutral XYZW unit quaternion (scalar W last, Hamilton convention). spec: skinning.md §7
/// (XYZW component order; Hamilton product; active rotation <c>q ⊗ v ⊗ q⁻¹</c>).
/// </summary>
public readonly record struct Quat(float X, float Y, float Z, float W);
