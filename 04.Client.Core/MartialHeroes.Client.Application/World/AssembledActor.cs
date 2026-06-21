namespace MartialHeroes.Client.Application.World;

// =============================================================================
// AssembledActor — the neutral actor descriptor emitted by the ActorComposer.
//
// Pure CLR. NO Godot type, NO ArrayMesh — layer-05 (Phase 6) builds the mesh +
// Skeleton3D from this descriptor. It carries:
//   - the skeleton key (id_b) + model class
//   - the baked inverse-bind influences (localPos/localNormal — the ANIMATABLE
//     payload) per base+minor influence
//   - the bone bind list (parent-relative locals + bind-world + base_id) so
//     layer-05 can build a Skeleton3D rest pose
//   - the action→clip (MotionClipIds) and action→SFX (SfxEventIds) tables
//   - the equipment GID list + per-part (mesh_gid / tex_id / slot / hand-bone)
//   - the spawn placement { WorldX, WorldZ, Yaw } — NO baked Y (re-sampled from
//     terrain per frame — assembly_graph §1)
//   - an IsInvisible flag (id_b 0 / variant 3 sentinel)
//
// spec: Docs/RE/specs/assembly_graph.md §2/§4; Docs/RE/specs/skinning.md §0/§4/§8(e);
//       Docs/RE/formats/actormotion.md; Docs/RE/specs/equipment_visuals.md;
//       Docs/RE/formats/npc_spawns.md (yaw = π/2 − value; Y re-sampled).
// =============================================================================

/// <summary>
///     The neutral, engine-free actor descriptor the <see cref="ActorComposer" /> emits for one spawned
///     actor. Layer-05 (Phase 6) consumes this to build the Godot mesh + skeleton; this layer never
///     references a Godot type.
/// </summary>
/// <remarks>
///     The descriptor is valid even when the mesh-build step is deferred: it carries the skin id, the
///     <c>id_b</c> skeleton key, the motion ids, and the GID list independently of any mesh. spec:
///     Docs/RE/specs/assembly_graph.md §4 ("emit a valid actor descriptor … independent of the
///     mesh-build step").
/// </remarks>
public sealed class AssembledActor
{
    /// <summary>
    ///     The skeleton selector — the skin's <c>id_b</c> used verbatim as the pose-pool key. <c>0</c>
    ///     ⇒ no skeleton (see <see cref="IsInvisible" />). spec: Docs/RE/specs/skinning.md §8(e).
    /// </summary>
    public required int SkinIdB { get; init; }

    /// <summary>
    ///     The appearance/skeleton selector <c>model_class_id = 5·(class + 4·variant) − 24 ∈ {1,11,16,26}</c>
    ///     for players; the catalogue-resolved class for mobs. <c>0</c> = invisible sentinel (variant 3).
    ///     spec: Docs/RE/specs/skinning.md §3.5.2.
    /// </summary>
    public required int ModelClassId { get; init; }

    /// <summary>
    ///     The baked inverse-bind influences (the animatable payload): per influence the vertex index,
    ///     bone id, normalized weight, and the bone-local rest <c>localPos</c>/<c>localNormal</c>.
    ///     Empty when the actor is invisible or has no skin/skeleton. spec: Docs/RE/specs/skinning.md §0/§4.
    /// </summary>
    public required IReadOnlyList<BakedInfluence> BakedInfluences { get; init; }

    /// <summary>
    ///     The bone bind list (parent-relative locals + bind-world + base_id) so layer-05 can build a
    ///     <c>Skeleton3D</c> rest pose. Empty when no skeleton resolved. spec: Docs/RE/specs/skinning.md §3.1/§3.4.
    /// </summary>
    public required SkeletonBindView Skeleton { get; init; }

    /// <summary>
    ///     Whether a skeleton was resolved at all (<c>id_b</c> registered). When <see langword="false" />
    ///     the <see cref="Skeleton" /> is a default and the deform would run unskinned.
    ///     spec: Docs/RE/specs/skinning.md §8(e) (id_b 0 / unregistered ⇒ no skeleton).
    /// </summary>
    public required bool HasSkeleton { get; init; }

    /// <summary>
    ///     The action→<c>.mot</c> clip table (a[1] idle, a[2] walk, a[3] run, a[4] death, a[5] mount-idle,
    ///     a[6] combat-idle). spec: Docs/RE/formats/actormotion.md (motion_ids_a).
    /// </summary>
    public required ActionClipTable MotionClipIds { get; init; }

    /// <summary>
    ///     The action→SFX/FX event-id table (NOT routed through animation). spec:
    ///     Docs/RE/formats/actormotion.md (motion_ids_b = SOUND/EFFECT event ids).
    /// </summary>
    public required ActionEventTable SfxEventIds { get; init; }

    /// <summary>
    ///     The equipment GID list — the per-slot equipment gids the composer iterated (the
    ///     <c>{3,4,6,2,11,14}</c> overlay slots, empty slots skipped). spec: Docs/RE/specs/skinning.md §3.5.4.
    /// </summary>
    public required IReadOnlyList<int> EquipmentGids { get; init; }

    /// <summary>
    ///     The resolved equipment overlay parts (mesh_gid / tex_id / slot / hand-bone). All parts share
    ///     the one <see cref="SkinIdB" /> skeleton. spec: Docs/RE/specs/equipment_visuals.md §3/§4/§5.
    /// </summary>
    public required IReadOnlyList<EquipmentPart> EquipmentParts { get; init; }

    /// <summary>The world-space X spawn origin. spec: Docs/RE/structs/spawn_descriptor.md (+0x4C world_x).</summary>
    public required float WorldX { get; init; }

    /// <summary>The world-space Z spawn origin. spec: Docs/RE/structs/spawn_descriptor.md (+0x50 world_z).</summary>
    public required float WorldZ { get; init; }

    /// <summary>
    ///     The spawn yaw (radians). For an <c>.arr</c>-derived NPC spawn the caller applies
    ///     <c>π/2 − facing</c> before constructing this; the wire path supplies the actor yaw directly.
    ///     spec: Docs/RE/formats/npc_spawns.md (+0x12 facing; runtime applies <c>π/2 − value</c>).
    /// </summary>
    public required float Yaw { get; init; }

    /// <summary>
    ///     <see langword="true" /> when the actor has no visible mesh — the model-class invisible sentinel
    ///     (variant 3 ⇒ model_class_id 0) or an <c>id_b</c> of 0 / unregistered skeleton.
    ///     spec: Docs/RE/specs/skinning.md §3.5.2 (variant 3 ⇒ 0 = invisible); §8(e) (id_b 0 ⇒ no skeleton).
    /// </summary>
    public required bool IsInvisible { get; init; }

    // NOTE: NO baked WorldY. The ground Y is re-sampled from the terrain manager every frame
    // (sentinel when the cell is not yet streamed; a later frame re-snaps). Layer-05 owns the
    // per-frame Y sample. spec: Docs/RE/specs/assembly_graph.md §1; Docs/RE/formats/npc_spawns.md
    // ("Ground-Y is re-sampled from terrain every frame").
}

/// <summary>
///     One baked inverse-bind influence — the animatable payload. The <c>localPos</c>/<c>localNormal</c>
///     were baked once by <c>invQ ⊗ (restPos − bindWorldTrans)</c> / <c>invQ ⊗ restNormal</c> so the
///     per-frame deform never touches the bind pose. spec: Docs/RE/specs/skinning.md §0/§4.
/// </summary>
public readonly struct BakedInfluence
{
    /// <summary>The unique render vertex this influence affects. spec: Docs/RE/specs/skinning.md §2.2.</summary>
    public required int VertexIndex { get; init; }

    /// <summary>The bone ID (base-relative; resolves <c>bone_array[id − base_id]</c>). spec: skinning.md §3.2.</summary>
    public required int BoneId { get; init; }

    /// <summary>The normalized weight (per-vertex influences sum to 1.0; &lt; 0.01 dropped). spec: skinning.md §5.2.</summary>
    public required float Weight { get; init; }

    /// <summary>Rest position in this bone's local frame (baked). spec: Docs/RE/specs/skinning.md §4.</summary>
    public required Vec3 LocalPosition { get; init; }

    /// <summary>Rest normal in this bone's local frame (rotation only, baked). spec: Docs/RE/specs/skinning.md §4.</summary>
    public required Vec3 LocalNormal { get; init; }
}

/// <summary>
///     One resolved equipment overlay part. spec: Docs/RE/specs/skinning.md §3.5.4;
///     Docs/RE/specs/equipment_visuals.md §3/§5.
/// </summary>
public readonly struct EquipmentPart
{
    /// <summary>The fixed visual slot id (one of <c>{3,4,6,2,11,14}</c>; 14 = weapon). spec: skinning.md §3.5.4.</summary>
    public required int Slot { get; init; }

    /// <summary>The original equipment GID for this slot (pre-reduction). spec: skinning.md §3.5.4.</summary>
    public required int EquipmentGid { get; init; }

    /// <summary>The resolved part mesh GID. spec: Docs/RE/specs/skinning.md §3.5.3.</summary>
    public required int MeshGid { get; init; }

    /// <summary>The resolved part texture id. spec: Docs/RE/specs/skinning.md §3.5.3.</summary>
    public required int TextureId { get; init; }

    /// <summary>
    ///     <see langword="true" /> for the weapon slot (14), which attaches to a hand bone rather than
    ///     skinning across the body. spec: Docs/RE/specs/equipment_visuals.md §5 (weapon → hand bone).
    /// </summary>
    public required bool IsHandWeapon { get; init; }

    /// <summary>
    ///     <see langword="true" /> for the off-hand node of a dual / two-piece weapon (skin bind class 3).
    ///     Off-hand carries node flag 1; the main-hand node flag 2. spec: Docs/RE/specs/equipment_visuals.md §5.1.
    /// </summary>
    public required bool IsOffHand { get; init; }
}