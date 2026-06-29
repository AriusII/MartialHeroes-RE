# Spec: Equipment Attach Render — Per-Frame Weapon Socketing, Sword Trail, and Joint Effects

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **draw-time attachment** of held weapon meshes, sword-trail geometry, and
> per-equipment joint effects to an animated character. It covers the per-frame rigid follow
> compose, the attach-host node layout, the hand bone-id resolution table, the
> `SwordLightEffect` ribbon geometry, and the `JointXEffect` cosmetic FX path.
>
> The **build/data side** of equipment visuals — equip-change rebuild, part-slot layout, GID
> formulas, weapon-glow tier toggler — is owned by `Docs/RE/specs/equipment_visuals.md` and
> is not redefined here. The CPU-LBS deform chain for body parts is owned by
> `Docs/RE/specs/skinning.md`. The frame-level draw orchestration is owned by
> `Docs/RE/specs/render_pipeline.md`. This spec covers the equipment-specific layer between
> the deformed body buffer and the final scene-graph draw: the per-frame rigid follow that
> pins a weapon mesh to a hand bone, the sword-trail ribbon that samples the same bone, and
> the per-slot cosmetic particle effects.
>
> **Two long-open items from `equipment_visuals.md` are resolved here:**
> - `attach_local_quat` default confirmed **identity** (no build-path override).
> - Sword-trail per-frame geometry confirmed as `WeaponTrail_AppendFromBone` →
>   interpolated ribbon, resolving the `effects.md §14.8` open item at the append level.

> **Verification banner**
> - **verification:** *static-confirmed* for the per-frame compose math, attach-host node
>   layout and wiring, bone-id stamp column table, `userjoint.txt` loader shape,
>   `SwordLightEffect` class structure and attach path, `WeaponTrail_AppendFromBone`
>   per-frame append (gated on owner state), and `JointXEffect` slot iteration. Items
>   explicitly tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg`
>   session before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the per-frame rigid-follow compose, attach-host
>   node layout, bone-id resolution column table, `StaticSkin` render-node structure, and
>   sword-trail ring-buffer append. Items tagged `[debugger-confirm]` are NON-BLOCKING.
> - **evidence:** [static-ida]
> - **cross-links:**
>   `Docs/RE/specs/equipment_visuals.md` (equip rebuild, part-slot table, GID formulas,
>   hand-bone resolution mechanism — the build/data side this spec extends at draw time);
>   `Docs/RE/specs/skinning.md` (bone addressing, 88-byte stride resolver, quaternion
>   conventions, CPU-LBS deform chain);
>   `Docs/RE/specs/character_rendering.md` (per-part cel draw loop, merged dynamic VB,
>   draw-order context);
>   `Docs/RE/specs/effects.md §12 / §17.6` (sword-light descriptors and class — this spec
>   adds the per-frame bone-sample and append step);
>   `Docs/RE/formats/mesh.md` (`.skn` / `.bnd` format, `id_b` ↔ skeleton).

---

## Status

| Item | Confidence |
|---|---|
| Per-frame attach type — rigid single-bone follow, not skinned deform | CONFIRMED |
| Per-frame compose: weapon world position = hand-bone world origin (local pos forcibly zeroed) | CONFIRMED |
| Per-frame compose: weapon world orientation = `bone_world_quat ⊗ attach_local_quat` | CONFIRMED |
| `attach_local_quat` = host+20 (xyzw), default **identity (0,0,0,1)**, no build-path override | CONFIRMED — resolves `equipment_visuals.md §8` OPEN item |
| Grip placed by weapon `.skn` attach-set scaled by the `Visual+100` scalar | CONFIRMED |
| Weapon mesh = **separate child `GDrawable`**, NOT merged into the shared body dynamic VB | CONFIRMED |
| Body parts share ONE merged dynamic VB (32 bytes/vertex, CPU deform — LBS / rigid) | CONFIRMED |
| Sword-trail per-frame: `WeaponTrail_AppendFromBone` — bone-world sample → interpolated ribbon | CONFIRMED — resolves `effects.md §14.8` open item |
| Sword trail gated on owner actor state == 5 | CONFIRMED |
| `SwordLightEffect` is a `GGeometry`-derived class; ring buffer 242 × 24-byte segments; bone-id at +5960 (default 44) | CONFIRMED |
| Hand-bone column table: columns 902/903 (alt-attack main/off), 904/905 (normal main/off) in `ActorVisual` singleton | CONFIRMED |
| ROW = `(Visual+108) mod 40` | CONFIRMED |
| `userjoint.txt` loader: 41 rows, 4 i32 per row, 16-byte stride, byte columns +3608/+3612/+3616/+3620 | CONFIRMED |
| `JointXEffect` spawned per worn slot (20 slots, skip slot 8) | CONFIRMED |
| Per-row concrete bone-id values in `userjoint.txt` | [debugger-confirm] — data-driven |
| Sword-light descriptor+16 runtime hand-selector field layout | [debugger-confirm] |
| Trail ribbon blend state (additive vs. alpha), fade duration, interpolation math | [debugger-confirm] |
| Weapon-before-body draw order in scene-graph child traversal | [debugger-confirm] |
| Secondary attach point purpose at `Visual+1408` | [debugger-confirm] |
| Actor state == 5 semantics (attack-swing assumed) | [debugger-confirm] |

---

## 1. Subsystem scope

The equipment attach render subsystem runs **each frame** as part of the per-visual deform and
compose pass (`SkinSet_DeformAndUpload`). It is distinct from the equip-change rebuild
documented in `equipment_visuals.md`: the rebuild installs the host nodes and selects bone ids
at part-change time; this spec covers what happens at draw time once those nodes are in place.

Three systems attach to a character bone at draw time:

1. **Weapon mesh** — a `StaticSkin` held-item mesh pinned to the hand bone via a lightweight
   attach-host node. The weapon draws as a separate child `GDrawable` of the actor visual; it
   is not merged into the shared body vertex buffer.
2. **Sword-light trail** — a `SwordLightEffect` (`GGeometry`-derived) that samples the same
   hand bone each frame and appends an interpolated ribbon segment, active only during the
   attack-swing actor state.
3. **Per-slot joint effects** — `JointXEffect` cosmetic particle effects spawned per worn part
   slot at equip time; a parallel system keyed by part-actor id, not by a bone pin.

All three are installed by the build-time weapon attach routine
(`ActorVisual_AttachHandWeaponNodes`) and then operated per-frame by `SkinSet_DeformAndUpload`
and `WeaponTrail_AppendFromBone` respectively.

---

## 2. Three draw-time attach paths — overview

| Path | Attach type | Per-frame action | Draw node |
|---|---|---|---|
| Weapon mesh (`StaticSkin`) | Rigid host node pinned to hand bone | `AttachPoint_ComposeWorld_A` — rigid follow | Separate child `GDrawable` of actor visual |
| Sword-light trail (`SwordLightEffect`) | Bone-id stamp at build time | `WeaponTrail_AppendFromBone` — bone-world sample → ribbon append | `GGeometry`-derived child drawable |
| Joint effects (`JointXEffect`) | Spawned per worn slot at equip time | (no per-frame bone query — particle system) | Particle effect child of actor visual |

---

## 3. Build-time weapon attach (`ActorVisual_AttachHandWeaponNodes`)

### 3.1 Entry point and callers

`ActorVisual_AttachHandWeaponNodes` is the build-time builder for the weapon rigid-attach
path. It is called whenever the weapon attachment must be re-established. Known callers, each
triggering a full weapon re-attach:

- `ActorVisual_RebindLocalPlayerParts` — local-player parts rebind (see `equipment_visuals.md`).
- `ActorVisual_ApplyAttackModeAndRefreshJointFx` — on every attack-mode flip (also re-stamps
  the bone-id, §6.1).
- The per-equipment composition refresh routine — drives a full equip refresh then re-attaches
  weapon nodes and joint effects.

### 3.2 Player-kind gate

The full rigid-attach path (host node + `StaticSkin`) runs only when `Visual+96 == 1` (player
kind). When this condition is false, only the mob sword-light attach path runs and the
function returns immediately. No host node is built for mobs via this entry point; mob
sword-light uses an alternative descriptor lookup keyed by `Visual+108` rather than by weapon
item id.

### 3.3 Appearance base and item-subtype index shift

The builder computes the appearance base index into `Visual+108` (primary appearance/skin-anim
index) and `Visual+112` via `ActorVisual_ComputeAppearanceIdB`. It then looks up the weapon
item-actor by part-actor id (read from `Visual+316`), reads `item_subtype` at item-actor
`+136`, and adds a class-specific shift to `Visual+108`. The shifted value becomes the ROW
basis for bone-id resolution (§6.1).

| `item_subtype` value(s) | Shift added to `Visual+108` |
|---|---|
| 1, 4, 10 | +2 |
| 2, 5, 8, 11 | +3 |
| 3, 6, 9, 12 | +4 |
| 7 | +2, then triggers attack-mode refresh + joint-fx + sword-light attach |
| 45 | +1 (only when alt-attack mode active: `Visual+964 == 1`) |
| > 45 | no shift |

Before the shift is applied, the builder also constructs the slot-14 character-skin
weapon-family deform part (`ActorVisual_BuildStaticSkinNodeAndSelectCelFrame`) — the
holster/sheath character-skin variant documented in `equipment_visuals.md §5`. The semantics
of this variant are unchanged OPEN from that spec.

### 3.4 Host node and `StaticSkin` construction

If the weapon has a `StaticSkin` record (`ItemStaticSkin_LookupById`):

1. Build a `StaticSkinDesc` from the item-actor (`StaticSkinDesc_BuildForActor`).
2. Allocate a `StaticSkin` object and an attach-host node (`WeaponAttachHostNode_Ctor`; see
   §5 for the host node layout).
3. Bind the skin to its pose (`StaticSkin_BindToPose`): resolves the bind pose from
   `BindPosePool` keyed by the skin's `id_b` (stored at skin+108, refcounted) and the mesh
   from `ItemStaticSkinRegistry` keyed by the skin's mesh key (stored at skin+148; fallback
   key `221111013`).
4. Wire the host: `host+0 = Visual+1300` (shared skeleton root); `host+4 = StaticSkin`;
   `host+36 = 2` (main-hand flag; see §5).
5. Set `skin+112 = owner (Visual)`; `skin+144 = Visual+100` (the scalar import scale).
6. Insert the host into the bone-attach list at `Visual+1376` via `BoneAttachList_PushBack`.
7. Register the `StaticSkin` render node as a child `GDrawable` of the actor visual via the
   scene-graph child registration helper (which calls `Diamond_GDrawable__SetParent`).

The sword-light effects for the weapon are installed by `ActorVisual_AttachSwordLightEffects`
(§10.2).

### 3.5 Off-hand weapon

When the weapon skin's **bind class** (read at skin object +8) equals **3**, the builder
additionally constructs a second host node + `StaticSkin` for the off-hand. The main-hand host
receives `host+36 = 2`; the off-hand host receives `host+36 = 1`.

### 3.6 Bone-id stamp

After all host nodes are inserted into the bone-attach list, the builder (and
`ActorVisual_ApplyAttackModeAndRefreshJointFx` on every attack-mode flip) walks the list and
stamps `host+68` with the resolved bone id. See §6 for the full column selection formula.

---

## 4. Per-frame draw-time compose (`SkinSet_DeformAndUpload`)

`SkinSet_DeformAndUpload` is the per-frame entry point for one actor visual. It runs the
merged-body deform and then composes all attached weapon transforms. The Visual object
contains four relevant list regions:

### 4.1 Visual list layout

| List / slot | Visual offsets (base / begin / end) | Contents | Per-frame action |
|---|---|---|---|
| Merged deform list | `+1360 / +1364 / +1368` | Body, head, and face skinned parts | Deform → one shared dynamic VB (32 bytes/vertex) |
| Bone-attach list | `+1376 / +1380 / +1384` | Weapon host node(s) (main/off-hand) | `AttachPoint_ComposeWorld_A` — rigid follow |
| Sword-light list | `+1392 / +1396 / +1400` | `SwordLightEffect`(s) | `WeaponTrail_AppendFromBone` — ribbon segment append |
| Secondary attach | `+1408` (single pointer) | One standalone attach-point struct | `AttachPoint_ComposeWorld_B` — rigid follow |

### 4.2 Ordered per-frame steps

1. If the combined vertex cursor (`Visual+1272`) and the dynamic vertex buffer (`Visual+1264`)
   are both present: **Lock** the dynamic VB (VB vtable byte +44 = Lock).
2. **Deform the merged body pool** — iterate the deform list (begin `Visual+1364`, end
   `Visual+1368`); per part, run the deform selected by the mode word at `Visual+1768`:
   - `0` = LBS (`Skin_DeformLBS`)
   - `1` = rigid-major (`Skin_DeformRigidMajor`)
   - `2` = rigid-owned (`Skin_DeformRigidOwned`)

   Then copy 32-byte vertices into the locked VB at `32 × part.vertexBase` (part record +120),
   count = `32 × pose.boneVertCount` (resolved from part record +108). All body, head, and face
   parts share this one dynamic VB.
3. **Unlock** the VB (VB vtable byte +48 = Unlock).
4. **Compose attach-point worlds** — iterate the bone-attach list (begin `Visual+1380`, end
   `Visual+1384`); per host call `AttachPoint_ComposeWorld_A(host, Visual+100)` (see §7).
5. **Append sword-trail segments** — iterate the sword-light list (begin `Visual+1396`, end
   `Visual+1400`); per effect call `WeaponTrail_AppendFromBone(effect)` (see §10.3).
6. **Secondary attach point** — if `Visual+1408` is non-null, call
   `AttachPoint_ComposeWorld_B(it, Visual+100)` (see §8).

---

## 5. Attach-host node layout

The attach-host node is a **non-polymorphic transform struct** — it has no vtable, no
draw methods, and no scene-graph polymorphism of its own. Its sole purpose is to carry one
hand-bone world transform for a held `StaticSkin`. Allocated by `WeaponAttachHostNode_Ctor`.

| Offset | Size | Type | Field / role | Default (constructor) |
|---:|---:|---|---|---|
| +0 | 4 | ptr | **Pose root** — set to `Visual+1300` (shared skeleton root) | 0 |
| +4 | 4 | ptr | **`StaticSkin`** (the held weapon mesh) | 0 |
| +8 / +12 / +16 | 12 | f32×3 | **Local position scratch** — forcibly zeroed at the start of every per-frame compose | 0, 0, 0 |
| +20 / +24 / +28 / +32 | 16 | f32×4 | **`attach_local_quat`** (xyzw, scalar-last Hamilton) — local rotation | **0, 0, 0, 1 (identity)** — no build-path override confirmed |
| +36 | 1 | u8 | **Hand flag** — `2` = main-hand, `1` = off-hand | 0 (stamped at build) |
| +40 / +44 / +48 | 12 | f32×3 | **Cached world position** (output, written per frame) | — |
| +52 / +56 / +60 / +64 | 16 | f32×4 | **Cached world quaternion** (xyzw, output, written per frame) | 0, 0, 0, 1 |
| +68 | 4 | i32 | **Resolved bone id** — the hand bone this host follows | 0 (stamped by §3.6 / §6.1) |

The `attach_local_quat` at +20 defaults to **identity** from the constructor. The build path
(`StaticSkinDesc_BuildForActor`) writes no override into this field. Whether a runtime path
(for example `.skn` attach metadata) ever writes a non-identity value is `[debugger-confirm]`.

---

## 6. Hand bone-id resolution

### 6.1 Column selection formula

After host node(s) are inserted into the bone-attach list, the builder (and
`ActorVisual_ApplyAttackModeAndRefreshJointFx` on each attack-mode flip) walks the list and
stamps `host+68` by reading the `ActorVisual` singleton (`ActorVisualGlobal_GetSingleton`).
The singleton is a flat i32 array. Selection:

```
ROW    = (Visual+108) mod 40

mode   = alt-attack   when (Visual+964 == 1) OR (Visual+1800 != 0)
         normal        otherwise

                    main-hand (host+36 == 2)        off-hand (host+36 == 1)
alt-attack mode  →  singleton[4·ROW + 902]           singleton[4·ROW + 903]
normal mode      →  singleton[4·ROW + 904]           singleton[4·ROW + 905]

host+68 = singleton[selected column]
```

Columns 902/903/904/905 form a 41-row × 4-column i32 table within the flat singleton array.
The per-row concrete bone-id values are data-driven (read from `data/char/userjoint.txt` at
boot) and are `[debugger-confirm]` — the selection mechanism is confirmed, not the values.

### 6.2 `userjoint.txt` loader

The `ActorVisual` singleton table is populated at boot by `UserJointTable_LoadFromTxt`, called
from `CharManifest_LoadAll`. File format:

- Header: one i32 `count`.
- Per entry: one i32 `row_index` (loop breaks when `row_index >= 41`, so valid indices are
  0..40), then 4 × i32 bone-id values.
- Runtime layout in the singleton: columns 902/903/904/905 at byte offsets
  `+3608 / +3612 / +3616 / +3620` respectively, at stride `16 × row_index` bytes per row.
- File: `data/char/userjoint.txt` (VFS-resident).

---

## 7. Per-frame rigid follow (`AttachPoint_ComposeWorld_A`)

Called per host node in step 4 of §4.2, with arguments `(host, s)` where `s = Visual+100`
(the actor's scalar import scale). Runs only when `host+4` (the `StaticSkin`) is non-null.

Let `bone` = result of `Pose_ResolveBoneByIdOffset(host+0, host+68)` — the 88-byte-stride
bone resolver documented in `skinning.md §3.2`. Fields used: `bone+56` = world translation
(vec3); `bone+68` = world quaternion (xyzw, scalar-last Hamilton, active rotation,
parent-on-left — matching `skinning.md` conventions).

Compose algorithm:

1. Zero `host.local_pos` (+8/+12/+16 ← 0, 0, 0).
2. `skin.world_quat` = `bone.world_quat ⊗ host.attach_local_quat` (Quat_Mul, parent-on-left).
3. `skin.world_pos` = rotate `host.local_pos` by `skin.world_quat` (= rotates the zero vector → still zero).
4. `skin.world_pos` += `bone.world_trans` → **weapon world position = hand-bone world origin**.
5. Place the weapon `.skn` attach-set: for each grip record,
   `grip_world = skin.world_quat · (record_local_offset · s) + skin.world_pos`
   via `AttachSet_PlaceAllByHostNode`.
6. Write cached outputs: `host+40..+48 ← skin.world_pos`; `host+52..+64 ← skin.world_quat`.

**Net result:** weapon world position equals the hand-bone's world origin (positional offset is
permanently zero); weapon world orientation equals `bone_world_quat ⊗ attach_local_quat`, with
`attach_local_quat` defaulting to identity. The grip geometry is laid out by the weapon `.skn`
attach-set, scaled by the single scalar `s`.

---

## 8. Secondary attach point (`AttachPoint_ComposeWorld_B`)

Called in step 6 of §4.2 on the standalone attach-point struct at `Visual+1408`. The compose
math is byte-for-byte identical to §7. The standalone struct has different field positions:

| Role | Offset in secondary struct |
|---|---|
| Pose root | +4 |
| `StaticSkin` | +8 |
| Bone id | +12 |
| Local quaternion | +28 |
| Cached world position output | +44 |
| Cached world quaternion output | +56 |

The purpose of this secondary attach point (mount socket, back-mount slot, or other role) is
`[debugger-confirm]` — the struct and its compose path are confirmed; who installs it and for
what slot is not isolated in this static pass.

---

## 9. Weapon mesh (`StaticSkin`)

### 9.1 Bind path (`StaticSkin_BindToPose`)

- `skin+144 = scale` (later overwritten with `Visual+100` by the caller).
- `skin+100 = key`.
- Resolves the **bind pose** from `BindPosePool` keyed by `skin+108` (`id_b`, refcounted).
- Resolves the **mesh** from `ItemStaticSkinRegistry` keyed by the skin's mesh key at `skin+148`
  (fallback key `221111013`).
- The weapon carries its own small bind/grip skeleton (used by `AttachSet_PlaceAllByHostNode`),
  while the host node at §5 pins it to the character's hand bone.

### 9.2 Render node (`StaticSkin_BuildRenderNode`)

Builds a self-contained D3D9 draw node: 9 geometry, index, and UV `DynArray` stream buffers
inserted into a `KeyedNodeList` under stream-key slots 0, 1, 2, 4, 8, 16, 32, 64, and 128.
Texture slot identifier: **`"gtex"`**.

### 9.3 Scene-graph placement

The weapon render node is registered as a **separate child `GDrawable`** of the actor visual
via `Diamond_GDrawable__SetParent`. It is not merged into the body's shared dynamic vertex
buffer. The off-hand weapon (when present) is a second independent child drawable.

The precise traversal order of weapon child drawables relative to the body mesh submesh
submissions is the scene-graph child-list order at draw time and is `[debugger-confirm]`.

---

## 10. Sword-light trail (`SwordLightEffect`)

### 10.1 Class summary

`SwordLightEffect` is a **`GGeometry`-derived class** (the constructor calls the `GGeometry`
base constructor and then installs its own vtable). Total object size approximately 5,964 bytes.

Key layout fields:

| Offset | Role |
|---|---|
| +104 | Owner pointer (actor visual) |
| +120..+127 | Head/tail RGBA gradient bytes (start and end colour defaults: all channels −1) |
| +136 | Fixed ring buffer — 242 × 24-byte trail segments |
| +100, +108, +116 | Ring head, tail, and count (zeroed when trail resets) |
| +5944 / +5948 / +5952 | vec3 scratch |
| +5956 | 1.0f constant |
| +5960 | **Bone id** (default 44; stamped at attach time from the hand-bone column table) |

### 10.2 Attach path (`ActorVisual_AttachSwordLightEffects`)

Called by `ActorVisual_AttachHandWeaponNodes` (and by the mob attach routine for non-player
actors, which uses a descriptor lookup keyed by `Visual+108` instead of by weapon item id).

For player-kind actors: reads the weapon part-actor id at `Visual+316`, looks up the
sword-light descriptor list via `SwordLightManager_GetSingleton` and
`SwordLightManager_FindItemDescriptors` (keyed by weapon item id), and iterates the
descriptors. Per descriptor:

1. Construct a `SwordLightEffect` instance; set `+104 = owner (Visual)`.
2. Initialize from the descriptor via `SwordLightEffect_InitFromDescriptor`.
3. Read the **hand selector at descriptor+16** and stamp `effect+5960` (bone id):

| Descriptor+16 value | Meaning | Bone-id source |
|---|---|---|
| 1 | Main-hand trail | `singleton[4·(Visual+108 mod 40) + 902]` |
| 2 | Off-hand trail | `singleton[4·(Visual+108 mod 40) + 903]` |
| 3 | Dual (both hands) | Build main trail → col 902; build a second effect → col 903 |

4. Register as a child `GDrawable` of the actor visual.
5. Push into the sword-light list at `Visual+1392` via the list push helper.

The runtime layout of the descriptor object (field names, the relationship between
descriptor+16 and the text-table position in `data/effect/itemjointeff.txt`) is
`[debugger-confirm]` — the field offset and hand-selector value meanings are confirmed,
not the full descriptor schema.

### 10.3 Per-frame ribbon append (`WeaponTrail_AppendFromBone`)

Called per `SwordLightEffect` in step 5 of §4.2.

- **State gate:** if `owner+1420` (actor state field) is **not equal to 5**, reset the ring
  buffer (`+100 = 0`, `+108 = 0`, `+116 = 0`) and return — the trail is suppressed outside
  the attack-swing state. Actor state 5 is assumed to be the attack-swing state;
  `[debugger-confirm]` the live semantic.
- **Bone sample:** resolve `bone` via `Pose_ResolveBoneByIdOffset(owner+1300, effect+5960)`.
  Read `bone+56..+64` (world translation, vec3) and `bone+68..+80` (world quaternion, xyzw).
- **Append:** call `ActorTrail_AppendInterpolatedSegment(effect, tx, ty, tz, q0..q3)` which
  appends an interpolated ribbon segment between the previous and current bone-world sample
  into the 242-segment ring buffer.

The ring-buffer interpolation algorithm, exact ribbon vertex layout per segment (24 bytes each),
fade duration, segment-count semantics, and D3D9 blend state (additive vs. alpha) are
`[debugger-confirm]`.

---

## 11. Per-part joint effects (`JointXEffect`)

`JointXEffect` cosmetic particle effects are a **parallel system** independent from the rigid
weapon mesh and the sword-trail. They are spawned at equip time (not per-frame bone-sampled)
via the per-equipment composition refresh routine, which calls
`JointXEffectManagerA_SpawnForActorKey` for each worn part slot.

- Iteration: 20 part slots at `Visual+204` (16-byte records), **skipping slot 8**.
- Per slot: calls `JointXEffectManagerA_SpawnForActorKey` on the player-kind joint-effect
  manager singleton, keyed by part-actor id + actor key (`Visual+92`) + player-kind
  (`Visual+96`).
- For non-player actors (`Visual+96 != 1`): uses the other-actor joint-effect manager
  singleton and resolves an appearance key via `Appearance_ResolveKey`.
- Descriptor tables loaded at boot by `EffectManager_LoadBmplistAndManifests` from
  `data/effect/itemjointeff.txt` (player) and `data/effect/mobjointeff.txt` (mob).

---

## 12. Render-state and D3D9 pass recipe

The three equip-attach systems interact with the D3D9 device as follows. This section
describes roles and state ownership; the cel draw render-state recipe is documented in
`Docs/RE/specs/character_rendering.md §5`.

### 12.1 Merged body dynamic VB (body, head, face parts)

| Attribute | Value |
|---|---|
| Vertex stride | 32 bytes |
| VB type | Dynamic, CPU-written per frame |
| VB lock | VB vtable byte +44 = Lock |
| VB unlock | VB vtable byte +48 = Unlock |
| Deform mode | Selected by `Visual+1768`: 0 = LBS, 1 = rigid-major, 2 = rigid-owned |
| Lock/unlock count | Once per actor per frame; all body parts share the one VB |

### 12.2 Weapon mesh (`StaticSkin`)

The weapon render node draws as a separate child `GDrawable` — it is **not** part of the
merged body VB. It carries its own stream buffers (§9.2) and its texture is bound to the
`"gtex"` slot. Its world transform is set by `AttachPoint_ComposeWorld_A` (§7). Draw order
relative to the body mesh is scene-graph child-list order; `[debugger-confirm]` the on-screen
sequencing.

### 12.3 Sword-light trail (`SwordLightEffect`)

The trail is a `GGeometry`-derived renderable and is drawn in-pipeline as its own geometry.
Per-frame it reads the hand-bone world transform and appends a ribbon segment into the
242-entry ring buffer. Head-to-tail RGBA alpha gradient is configurable via the descriptor;
defaults: all channels −1. The blend state (additive vs. alpha-blend) is plausibly additive
per `effects.md §17.6` and is `[debugger-confirm]`.

---

## 13. Open items and [debugger-confirm]

### 13.1 [debugger-confirm] items

Static-confirmed hypotheses requiring a live `?ext=dbg` session before treating them as
implementation facts. All are NON-BLOCKING for the core port work.

| # | Item | What to confirm |
|---|---|---|
| 1 | Concrete bone-id values in `userjoint.txt` | The column-selection formula and loader format are confirmed; read the VFS file or query the live `ActorVisualGlobal` singleton after boot to obtain the per-(row, column) bone-id integers. |
| 2 | Sword-light descriptor+16 runtime hand-selector field | The field offset and values 1/2/3 are confirmed; the runtime descriptor object layout (vs. the text-table float at that position, per `effects.md §12.2`) is not expanded in this static pass. |
| 3 | Trail ribbon blend state, fade, interpolation math | The ring buffer (242 × 24-byte segments), append gate (state == 5), and bone-world sample are confirmed. The per-segment vertex layout, D3D9 blend state, and fade parameters require a live read. |
| 4 | Actor state == 5 semantics | Assumed to be the attack-swing state; confirm the state-machine value live. |
| 5 | Weapon vs. body draw order | The weapon is a separate child `GDrawable` parented to the actor visual; the on-screen traversal order relative to body submesh submissions is child-list order — confirm live. |
| 6 | Secondary attach point purpose | `AttachPoint_ComposeWorld_B` is structurally confirmed; who installs the struct at `Visual+1408` and its semantic role (mount, back-mount, other) was not traced in this static pass. |
| 7 | `attach_local_quat` runtime override | Default identity is confirmed with no static build-path override. Confirm live that no runtime path (e.g. `.skn` attach metadata) ever writes a non-identity value. |

### 13.2 Open questions (escalated to RE domain)

| # | Open item |
|---|---|
| 1 | `userjoint.txt` concrete bone-id values per (appearance row, attack-mode × hand column) — read the VFS file or query the live singleton. |
| 2 | Full runtime descriptor object layout for `SwordLightEffect` descriptors, especially the relationship between the binary object fields and the `data/effect/itemjointeff.txt` text columns. |
| 3 | Slot-14 character-skin weapon-family deform part semantics (holster/sheath?) — carried forward as OPEN from `equipment_visuals.md §5`. |

---

## 14. Cross-references

| Spec | Relationship |
|---|---|
| `Docs/RE/specs/equipment_visuals.md` | Build/data side of equipment rendering — equip rebuild, part-slot table, GID formulas, weapon-glow tier toggler. This spec is the per-frame draw-time complement. |
| `Docs/RE/specs/skinning.md` | Bone addressing (88-byte stride resolver), quaternion conventions (xyzw, Hamilton, parent-on-left, active rotation), CPU-LBS deform chain. §7 of this spec uses the bone resolver directly. |
| `Docs/RE/specs/character_rendering.md` | Per-part cel draw loop, merged dynamic VB context, D3D9 render-state recipe. The merged VB described in §4.2 of this spec is the same object drawn by `character_rendering.md §3.1`. |
| `Docs/RE/specs/effects.md §12 / §17.6` | Sword-light descriptor format and `SwordLightEffect` class documentation; §10 of this spec adds the per-frame bone-sample and ribbon-append step, resolving the `effects.md §14.8` open item. |
| `Docs/RE/specs/render_pipeline.md` | Frame-level draw orchestration, pass order, cull-set submission. `SkinSet_DeformAndUpload` runs within the cull-drawable draw that this spec calls out. |
| `Docs/RE/formats/mesh.md` | `.skn` / `.bnd` format, `id_b` ↔ skeleton, attach-set record layout. The grip attach-set placed by `AttachSet_PlaceAllByHostNode` (§7) follows the `.skn` attach-set schema. |
