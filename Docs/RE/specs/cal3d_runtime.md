# Cal3D Runtime — Symbol Index & Pipeline Map

> **Status:** Static recovery (IDA MCP). LBS math fully confirmed in
> `specs/skinning.md`. This document maps the runtime SYMBOLS to the
> confirmed math.
> **IDB anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963

---

## 0. Overview

The doida.exe skeletal animation system is a **CPU Linear-Blend Skinning (LBS)**
pipeline, fully confirmed in `specs/skinning.md`. It does not use standard
Cal3D class names externally — the runtime exposes its state through the engine's
own `CorePose / CoreSkin / CoreActor` naming convention (if RTTI is present),
or has all symbol names stripped.

The pipeline has five confirmed stages:

| Stage | Function | Address | Confirmed in |
|-------|----------|---------|--------------|
| 1. Skeleton load | `BindPose_ParseBndFile` | `0x43009c` | skinning.md §3 |
| 2. Inverse-bind bake | `CoreSkin_LoadFromFile` | `0x43472a` | skinning.md §4 |
| 3. Clip sampling + Slerp | `Track_SampleAtTime` | `0x4029a5` | skinning.md §6.2 |
| 4. World transform build | `Pose_WorldWalk` | `0x437fb6` | skinning.md §6.6 |
| 5. LBS deform loop | `Skin_DeformLBS` | `0x4387fb` | skinning.md §5 |

---

## 1. Key Runtime Constants (confirmed)

| Constant | Value | Meaning |
|----------|-------|---------|
| Pose bone stride | 88 bytes (0x58) | Per-bone runtime record size |
| Bind bone stride | 72 bytes (0x48) | In-memory bind bone record size |
| Keyframe stride | 28 bytes (0x1C) | Per-keyframe record (3×f32 trans + 4×f32 quat) |
| Keyframe rate | 10 fps | Frame index = floor(t × 10) |
| Bone count limit | 255 (u8) | Maximum bones per skeleton |
| Vertex stride | 32 bytes | Position(12) + Normal(12) + UV(8) |
| dt conversion | 0.001f | dt_seconds = ms × 0.001 |

---

## 2. Pose Bone Record Layout (88 bytes, runtime)

```
Offset  Size  Field
+0      4     vtable / padding
+4      4     ptr → parent runtime bone
+8      4     ptr → first child runtime bone
+12     4     ptr → next sibling runtime bone
+16     4     ptr → source bind bone (72-byte in-memory bind record)
+20     4     accumWeight (f32) - running average sampling weight
+24     4     layerWeight (f32) - current layer sample weight
+28     12    local animated translation (3 × f32)
+40     16    local animated quaternion (4 × f32, XYZW)
+56     12    world translation / accum translation sample (overwritten)
+68     16    world quaternion / accum quaternion sample (overwritten)
+84     4     per-node scale (f32)
```

> [!NOTE]
> The bone structure uses union-like field reuse: `+56` and `+68` serve as the temporary accumulated samples (`accumTrans` / `accumQuat`) during sampling and blending, and are subsequently overwritten by the computed world transforms (`boneWorldTrans` / `boneWorldQuat`) during the hierarchy traversal pass (`Pose_WorldWalk`).

---

## 3. LBS Deform Equation (confirmed, §0 skinning.md)

```
vertexWorld(v) = Σ_i  w_i · ( boneWorldQuat_i ⊗ ( localPos_i · scale ) + boneWorldTrans_i )
```

Where `localPos_i` is baked once at load:
```
localPos_i    = bindWorldQuat_i⁻¹ ⊗ ( restModelPos(v) − bindWorldTrans_i )
localNormal_i = bindWorldQuat_i⁻¹ ⊗ restModelNormal(v)
```

- **Quaternion convention:** XYZW (scalar W last), Hamilton product, active rotation
- **Multiply order:** parent on LEFT
- **Axis flip:** NONE inside the math
- **Mode 0** = full LBS; **Mode 1** = rigid major-only; **Mode 2** = rigid fast (owner-proximity)

---

## 4. .mot Keyframe Sampler (confirmed, skinning.md §6)

```
frame_index = floor(t × 10)          // 10 fps
alpha       = (t × 10) − frame_index // raw seconds in [0, 0.1]

kf_a = track_keyframes[frame_index]      // 28-byte record
kf_b = track_keyframes[frame_index + 1]  // next frame

local_trans = LERP(kf_a.trans, kf_b.trans, alpha)
local_quat  = SLERP(kf_a.quat, kf_b.quat, alpha)  // shortest-arc
```

---

## 5. Skeleton / Skin Asset Paths

| Asset | Path pattern | Key field |
|-------|-------------|-----------|
| Skeleton | `data/char/skin/g{actor_id}.bnd` | `actor_id` (1..4 for players) |
| Skinned mesh | `data/char/skin/g{mesh_gid}.skn` | `id_b` = skeleton actor_id |
| Animation clip | via `motlist.txt` | `id_b` = skeleton actor_id |
| Skin registry | `data/char/skin.txt` | appearance catalogue |
| Motion registry | `data/char/actormotion.txt` | appearance key |
| Skeleton list | `data/char/bindlist.txt` | preloads g1..g4.bnd |

---

## 6. model_class_id → Skeleton Mapping (VFS-pinned)

| model_class_id | Class | Skeleton | Bones | Idle clip |
|----------------|-------|----------|-------|-----------|
| 1  | Musa  | g1.bnd | 84 | g111100010.mot |
| 26 | Salsu | g2.bnd | 87 | g112200010.mot |
| 11 | Dosa  | g3.bnd | 82 | g111300010.mot |
| 16 | Monk  | g4.bnd | 89 | g111400010.mot |

```
model_class_id = 5 * (class + 4 * variant) - 24
```

---

## 7. Runtime Symbol Index

The following table indexes the recovered animation and skinning functions in `doida.exe` (pinned to imagebase `0x400000`):

| Function Name | Address | Status | Notes |
|---------------|---------|--------|-------|
| `Track_SampleAtTime` | `0x4029a5` | CONFIRMED | Samples animation track; calls Vec3_Lerp / Quat_Slerp |
| `SkinSet_DeformAndUpload` | `0x40d1c3` | CONFIRMED | Locks VB, triggers deform per mode (LBS/rigid), copies to D3D VB, unlocks |
| `Pose_ResolveBoneByIdOffset` | `0x41e3fb` | CONFIRMED | Stride-88 bone resolver; caps/clamps out-of-bounds bones |
| `AnimActionLayer_AdvanceState` | `0x41ec7c` | CONFIRMED | Advances action-layer timeline & handles action FSM |
| `AnimCycleLayer_AdvanceBlend` | `0x41ede0` | CONFIRMED | Advances cycle-layer timelines & eases blending factor |
| `AnimMixer_BuildPose` | `0x41f8f1` | CONFIRMED | Top-level frame mixer update: ticks anim timelines & builds pose |
| `CoreMot_LoadFullData` | `0x42f839` | CONFIRMED | Decodes `.mot` file format from VFS Stream into track keyframes |
| `CoreSkin_LoadFromFile` | `0x43472a` | CONFIRMED | Loads `.skn` file, sets up submeshes, deduplicates vertices, and maps weights |
| `Pose_WorldWalk` | `0x437fb6` | CONFIRMED | Root-to-leaf skeleton hierarchy walk: builds boneWorldTrans / boneWorldQuat |
| `PoseBone_ResetSampleAccumulator` | `0x43802d` | CONFIRMED | Clears bone weight accumulators (`accumWeight` and `layerWeight`) |
| `Pose_LinkRuntimeHierarchy` | `0x43803b` | CONFIRMED | Establishes parent/child and sibling pointers in the runtime bone tree |
| `PoseNode_CommitWithChildLock` | `0x4385ba` | CONFIRMED | Blends running weight samples; normalizes translation & quats per bone |
| `Pose_BuildFromBindPose` | `0x4386f3` | CONFIRMED | Instantiates runtime pose skeleton from static bind pose |
| `Pose_CommitBoneSamples` | `0x4387d6` | CONFIRMED | Iterates skeleton bones to normalize and finalize sample blending |
| `Skin_DeformLBS` | `0x4387fb` | CONFIRMED | Runs CPU Linear-Blend Skinning loops (major pass = overwrite, minor pass = add) |
| `Skin_DeformRigidMajor` | `0x438a2a` | CONFIRMED | Runs rigid fast-path: deforms vertices using dominant major bone influence only |
| `Skin_DeformRigidOwned` | `0x438afc` | CONFIRMED | Proximity-based optimization: deforms "owner" vertices, copies to duplicate seam verts |

---

## 8. RTTI Symbols Map

The following key animation and skinning RTTI symbols are present in `doida.exe`:

| Class Name | VTable | Typeinfo | Notes |
|------------|--------|----------|-------|
| `CoreActor` | `0x720b4c` | `0x78e144` | Actor state wrapper class |
| `CoreActorManager` | `0x720b54` | `0x78e15c` | Manager class for actors |
| `CoreAnimation` | `0x720bd0` | `0x78e498` | Loaded animation track wrapper class |
| `CoreAnimationManager` | `0x720be4` | `0x78e578` | Manager class for loaded animations |
| `CorePose` | `0x720bf0` | `0x78e8d0` | Skeletal pose instance container class |
| `CorePoseManager` | `0x720bf8` | `0x78e8e8` | Manager class for skeletal poses |
| `CoreSkin` | `0x720c10` | `0x78e92c` | Mesh mesh-descriptor class |
| `SkinWeight@CoreSkin` | `0x720c34` | `0x78e964` | Subclass representing vertex skin weight mapping |
| `CoreSkinManager` | `0x720c90` | `0x78ea38` | Manager class for loaded mesh skins |
| `CoreTrack` | None (Inlined) | `0x78e480` | Animation track containing keyframes |
| `Pose::Joint` | `0x759c44` (VTable) | `0x78f4e0` | Runtime bone node class (equivalent to `CalBone`) |
| `ActorAnimationMixer` | None | `0x78da6c` | Engine mixer logic (equivalent to `CalMixer`) |
| `Actor` | None | `0x78db84` | Main animated entity class (equivalent to `CalModel`) |

---

## 9. GPU Upload Pipeline (Confirmed)

1. **Locking:** The engine calls `IDirect3DVertexBuffer9::Lock` (vtable offset `44`) on the dynamic vertex buffer pointer `*(actor + 1264)` with `D3DLOCK_DISCARD` (`0x2000`) flags, obtaining a write-only pointer `v9` to GPU-mapped memory.
2. **Deformation Mode Dispatch:** The engine checks the deform mode word at `actor + 1768`:
   - Mode `0` (LBS): Calls `Skin_DeformLBS` to run the Linear-Blend Skinning calculations.
   - Mode `1` (Rigid Major): Calls `Skin_DeformRigidMajor` to deform vertices rigidly using only the single dominant major bone.
   - Mode `2` (Rigid Owned): Calls `Skin_DeformRigidOwned` to run the proximity-table optimization (only unique "owner" vertices are deformed; duplicates duplicate-copy their values).
3. **GPU Copy:** The deformed vertex positions, normals, and UVs (total stride of `32` bytes: position `12` bytes, normal `12` bytes, UV `8` bytes) are copied via `memcpy` from the submesh's in-memory scratch-buffer pointer (submesh field at offset `+116`) into the locked Direct3D buffer at the submesh's base-vertex offset (the 32-bit field at `+120`, multiplied by the `32`-byte stride).
4. **Unlocking:** The engine calls `IDirect3DVertexBuffer9::Unlock` (vtable offset `48`) on the vertex buffer, finalizing the upload.
5. **Attachpoint Compose:** Attachment slots (for weapons, helmets, shields) are updated down the newly computed hierarchy by calling `AttachPoint_ComposeWorld_A` and `AttachPoint_ComposeWorld_B` to position equipment models in world space.

---

## 10. Open Items

All key structural and mathematical aspects of the Cal3D animation and skinning runtime have been successfully reversed and mapped. No critical items remain open.

---

## 11. Cross-references

- `specs/skinning.md` — full LBS math, bone layouts, .mot format, all confirmed
- `formats/mesh.md` — on-disk .bnd/.skn byte layouts
- `formats/animation.md` — on-disk .mot byte layout + mixer model
- `specs/equipment_visuals.md` — weapon rigid attach, slot families
- `specs/scene_graph.md` — GGeode/GGeometry draw nodes that own deform skins
