# Spec: Effects System — Runtime Instantiation, Update, Attachment, and Triggers

> Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers. Promoted from dirty-room runtime analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> Client.Application (effect dispatch) and Assets.Parsers (descriptor loading). Every field
> offset an engineer cites must reference this file or `formats/effects.md`.

---

## Status

| Item | Value |
|------|-------|
| Confidence model | CODE-CONFIRMED = behavior seen in a traced runtime call path; SAMPLE-VERIFIED = confirmed against real `.xeff` or binary samples; HIGH = consistent across multiple traced sites; PLAUSIBLE = structurally inferred but not traced; UNRESOLVED = unknown. |
| Time base | Milliseconds from the engine wall clock (system timer, `ms` throughout). See §1. |
| Class hierarchy | Recovered from RTTI strings and constructor call chains. See §2. |
| Boot sequence | CODE-CONFIRMED; eight manifest files loaded in a fixed order. See §3. |
| Object pools | CODE-CONFIRMED for three pools; fourth pool type UNRESOLVED. See §4. |
| Spawn pathways | CODE-CONFIRMED for all three instance kinds. See §5. |
| Per-frame tick | CODE-CONFIRMED (central dispatch function traced). See §8. |
| Trigger table | CODE-CONFIRMED (all hard-coded effect IDs traced to network handlers). See §7. |
| Bone attachment | CODE-CONFIRMED for field layout; UNRESOLVED for `bone_source` table internals. See §9. |
| Damage-number renderer | CODE-CONFIRMED for init; geometry detail not fully traced. See §10. |
| Particle sub-system | CODE-CONFIRMED for spawn path; record layout PLAUSIBLE only. See §11. |
| SwordLight sub-system | CODE-CONFIRMED for boot and descriptor layout. See §12. |
| Skill-cast effect chain | CODE-CONFIRMED but CAPTURE-UNVERIFIED — the network half (opcode `5/52`, action codes) is read statically from the binary; no live capture has confirmed the on-wire action-code values. See §15. |
| Campaign-5 runtime deep-dive | 104-byte element CONFIRMED; emitter dispatch + keyframe math CONFIRMED; bone-attach overlay CONFIRMED; blend-state and draw-order internals PLAUSIBLE; AnimCatalog weapon-slot meaning PLAUSIBLE. See §17. |

---

## 1. Time Base

**Confidence: CODE-CONFIRMED.**

All effect timing uses the engine's wall-clock millisecond counter (a system timer equivalent to `timeGetTime()`). Every spawn path records the clock value at spawn time, and every per-frame tick subtracts that from the current clock to obtain elapsed milliseconds. All fields in `.xeff` files (`anim_stride`, `anim_base_time`) and all per-instance expiry deadlines are expressed in **milliseconds**.

---

## 2. Class Hierarchy

**Confidence: CODE-CONFIRMED** (recovered from RTTI strings in the binary's read-only data section and from constructor call chains).

```
XEffect                  — abstract base for all live effect instances
├── UserXEffect          — free-floating or actor-relative timed effect
├── JointXEffect         — bone-socket-attached effect; follows an actor's skeleton
└── MapXEffect           — world-space positioned effect with a full quaternion orientation

CoreXEffect              — the file-backed parsed record; holds id, element array, timing data;
                           lazily parsed on first spawn (not a live effect instance)
MapXEffectManager        — per-area manager that owns and ticks map-anchored ambient effects

ParticleEffectManager    — separate sub-system; activated by XEffect elements whose
                           resource_id ≥ 10000 (see §11)
SwordLightManager        — weapon-trail / sword-glow sub-system; entirely separate from the
                           XEffect class family (see §12)
```

The `CoreXEffect` record (in-memory size: 84 bytes) holds the parsed descriptor for one `.xeff` file. It is NOT a renderable object. Key fields within the `CoreXEffect` object:

| Byte offset within object | Type | Field | Confidence |
|---:|---|---|---|
| +0x20 | u8 | `exists_flag` — 1 if the file was found in the VFS at boot; 0 if not | CODE-CONFIRMED |
| +0x21 | u8 | `loaded_flag` — 0 until the first lazy parse; 1 thereafter | CODE-CONFIRMED |
| +0x34 | u32 | `effect_id` — from the `.xeff` file header byte 0 | CODE-CONFIRMED |
| +0x38 | u32 | `element_count` — from the `.xeff` file header byte 4 | CODE-CONFIRMED |
| +0x3C | u32 | `total_time` — derived: `tex_count × anim_stride + anim_base_time` (ms) | CODE-CONFIRMED |
| +0x40 | f32 | `scale_default` — initialized to 1.0 at construction | CODE-CONFIRMED |
| +0x44 | ptr | `element_array` — heap-allocated after lazy parse | CODE-CONFIRMED |
| +0x48 | u32 | `xobj_ref` — mesh handle for `.xobj` mesh-particle effects | CODE-CONFIRMED |

---

## 3. Boot Loading Pipeline

**Confidence: CODE-CONFIRMED** (boot sequence traced to a single top-level asset-thread caller).

The effect manager initializes from eight source files in the following order. All files reside under `data/effect/` unless a different path is given. All read operations use the same mixed binary/text reader that handles both little-endian `uint32` and ASCII decimal integers from the same stream — do NOT assume pure-binary or pure-text for these files.

### 3.1 Boot sequence

**Step 1: `data/effect/bmplist.lst` — texture name pool**

Format: `u32 entry_count` then `entry_count × 30-byte` null-padded ASCII name records. Each name is expanded to `data/effect/texture/<name>` and registered as a texture slot in the effect manager's texture vector. See also `formats/effects.md §A.10` for the binary layout.

**Step 2: `data/effect/xobj.lst` — ASCII primitive mesh manifest**

Format: `u32 entry_count` then per-record `(u32 id, char[30] name)`. Registers each `.xobj` primitive mesh by id for later lookup when an effect element's `emitter_type` is 1 (mesh-particle). The `.xobj` files themselves are plain text (see `formats/effects.md §A.11`).

**Step 3: `data/effect/xeffect.lst` — effect name registry**

Format: `u32 entry_count` then `entry_count × 30-byte` name records. For each entry, the manager opens the corresponding `.xeff` file, reads only the first `u32` (the `effect_id`), creates a `CoreXEffect` stub with the id and path, and inserts it into the manager's sorted id-keyed map. The full element parse is **deferred** to the first spawn request for that id (lazy load). See `formats/effects.md §A.9` for the binary layout.

**Step 4: `EffectCache_LoadIDs`** — warms an LRU pre-cache with frequently used effect ids so the first spawn of common effects (e.g. hit sparks) does not pay the lazy-parse cost on the first frame.

**Step 5: `data/effect/totalmugong.txt` — martial-arts skill sound overlay table**

Format (text token table, CORRECTED Campaign 5B): `int count` then per-record **4 int tokens**. The stored key is the catalog-resolved value `field2 + AnimCatalog[field1 + 1]`. Links martial-arts skill animation-catalog slots to sound cues. See §13 and `formats/effects.md §F.7` for the per-token schema.

**Step 6: `data/effect/itemjointeff.txt` — item joint-effect binding table**

Format (text token table, CORRECTED Campaign 5B): `int count` then per-record **6 tokens**: `group_key`, `effect_id` (`== 0` skips the record), two ints, one **float**, and a final int stored as a byte. Populates the **item joint-effect manager**, keyed on the **raw** `group_key` (NOT catalog-resolved). Used to auto-attach bone-socket effects to items when equipped or spawned. See §9.2 for the per-spawn use and `formats/effects.md §F.7` for the schema.

**Step 7: `data/effect/mobjointeff.txt` — mob joint-effect binding table**

Text token table (CORRECTED Campaign 5B): `int count` then per-record **6 tokens** — a `(class, offset)` key pair plus an inner `{int, int, float, byte}` node. Populates the **mob joint-effect manager** (the manager OTHER than the item one in Step 6), keyed on the **catalog-resolved** value `offset + AnimCatalog[(class + 1)]`. The `AnimCatalog` internal layout is UNRESOLVED. See `formats/effects.md §F.7`.

**Step 8a: `data/effect/itemswordlight.txt` — item weapon-trail descriptors**

Format: `u32 count` then per-record `(u32 item_id, f32 color_r, f32 color_g, f32 color_b, f32 offset_x, f32 offset_y, f32 offset_z, u8 enabled_flag, char[] texture_name)`. Texture name is resolved via the texture manager by name. Mapped keyed by `item_id`. See §12.

**Step 8b: `data/effect/mobswordlight.txt` — mob weapon-trail descriptors**

Same format as `itemswordlight.txt`. Mapped keyed by `mob_id`. See §12.

---

## 4. Object Pool Allocation

**Confidence: CODE-CONFIRMED** for three pools; fourth pool class **UNRESOLVED**.

Each live-effect class allocates from a fixed-size object pool. Objects are returned to the pool (not freed) on expiry; the destructor path returns the object to the pool only if setup fails (effect ID not found). Objects that successfully enter the active list are returned on expiry or when the list is flushed.

| Effect class | Pool | Notes |
|---|---|---|
| `MapXEffect` | Pool A | World-space effects |
| `UserXEffect` | Pool B | Free-floating / actor-relative effects |
| `JointXEffect` | Pool C | Bone-attached effects |
| (Unknown class) | Pool D | Type unresolved; torn down at shutdown alongside the three known pools. Candidates: a local-player-only subtype or an absolute-world subtype. |

An additional singleton (`SwordLightManager`) is also torn down at the same shutdown point but belongs to the separate sword-light sub-system (§12).

The `UserXEffect` pool (Pool B) is a **fixed-block free-list pool**: when its free-list is empty it batch-allocates a block of `N` elements at the fixed element stride (104 bytes; see §17.1) and links them into the free-list, so the element size is structurally load-bearing for the pool. CONFIRMED.

---

## 5. Spawn Pathways

**Confidence: CODE-CONFIRMED.**

Two factory functions are responsible for all spawns:

- **UserXEffect factory** — pool-allocates a `UserXEffect`, sets up via a timed-setup function, inserts into the active list if setup succeeds.
- **MapXEffect factory** — pool-allocates a `MapXEffect`, sets up via a world-position-and-quaternion setup function.

Both factories are called from over 53 combined call sites, covering all network handler triggers and the per-bone equip path. The `JointXEffect` spawns via two dedicated entry points described in §9.

### 5.1 Lazy-load resolution

All three spawn paths call the same **lazy-load resolver**. The resolver:

1. Looks up the requested `effect_id` in the manager's sorted map.
2. If not found → writes an invalid flag into the new instance object, calls its destructor (returns to pool without entering the active list), and the spawn is silently abandoned.
3. If found but `CoreXEffect.loaded_flag` is clear → calls the load callback via the virtual dispatch table (slot 1). This triggers the full element parse: reads all sub-effect elements, resolves textures, loads alpha and scale curves, and reads the keyframe array into heap-allocated arrays within the `CoreXEffect`.
4. On success → records the load timestamp in the `CoreXEffect` and returns the descriptor pointer.

### 5.2 Active-list management

Active effect objects are stored in a linked list rooted at the effect manager. Insertion happens at the end of every successful factory call. On each draw frame, the manager iterates the list, calls the tick/dispatch function for each live instance, and removes expired instances (lifetime exhausted or descriptor pointer null). Removed instances are returned to their pool.

---

## 6. In-Memory Instance Layouts

**Confidence: CODE-CONFIRMED for the listed fields; HIGH for the remaining marked fields.**

All three concrete instance types share the base `XEffect` layout and extend it. Offsets are within the live in-memory instance object. These are NOT on-disk formats.

### 6.1 Shared base fields (all instance types)

| Offset | Type | Field | Confidence |
|---:|---|---|---|
| +0x00 | ptr | Virtual dispatch table pointer | CODE-CONFIRMED |
| +0x0C | u32 | Descriptor pointer / validity flag (0 = invalid, non-zero = `CoreXEffect*`) | CODE-CONFIRMED |
| +0x3C | u8 | Loop flag — non-zero = looping effect | CODE-CONFIRMED |
| +0x40 | u32 | Spawn timestamp (ms) = `delay_ms + clock_at_spawn` | CODE-CONFIRMED |
| +0x48 | f32 | Effective lifetime (ms) = `CoreXEffect.scale_default × caller_scale` | CODE-CONFIRMED |
| +0x50 | u32 or f32 | Time-scale / playback-speed factor | CODE-CONFIRMED |

### 6.2 `UserXEffect` — additional fields

**Confidence: CODE-CONFIRMED.**

| Offset | Type | Field | Notes |
|---:|---|---|---|
| +0x54 | u32 | Actor sort-id or world-space handle (`a3` at setup) | |
| +0x58 | u8 | Actor sort byte — distinguishes PC (1) from mob (2) and others | |
| +0x5C | u32 | Actor id (`a5` at setup) | |
| +0x60 | u8 | Flag parameter (`a6` at setup) — semantics unresolved | |

Note: the layout note in the dirty source observes that offset `+0x3C` is shared between the `loop_flag` (u8 low byte) and what may be a larger u32 lookup-result field. The safe read is to treat `+0x3C` as a u32 where the low byte encodes the loop flag, and the non-zero-ness of the whole dword is the validity indicator for the lookup result.

### 6.3 `JointXEffect` — additional fields

**Confidence: CODE-CONFIRMED.** See also §9 for the attachment resolution path.

| Offset | Type | Field | Notes |
|---:|---|---|---|
| +0x54 | u32 | `actor_sort_id` — sort-id of the target actor | |
| +0x58 | u8 | `sub_id` — sub-actor identifier (e.g. distinguishes mount from rider) | |
| +0x59 | u8 | `bone_source_enum` — how to locate the bone; see §9 | |
| +0x5C | u32 | `bone_id_or_hint` — explicit bone index when `bone_source_enum` = 0 | |
| +0x60 | u8 | `quat_source_enum` — which orientation quaternion to use; see §9 | |
| +0x64 | u32 | Color / render parameter (`a11` at setup) — semantics unresolved | |

### 6.4 `MapXEffect` — additional fields

**Confidence: CODE-CONFIRMED.**

| Offset | Type | Field | Notes |
|---:|---|---|---|
| +0x10 | f32 | `world_pos_x` | World-space X |
| +0x14 | f32 | `world_pos_y` | World-space Y |
| +0x18 | f32 | `world_pos_z` | World-space Z |
| +0x1C | f32[4] | `world_rot_quat` — XYZW quaternion, copied at spawn | |
| +0x50 | u32 | Color / render parameter — semantics unresolved | |

---

## 7. Gameplay Trigger Dispatch Table

**Confidence: CODE-CONFIRMED** — all entries traced to network message handlers and internal event dispatch sites.

Two factory functions cover all dispatches. The table below lists every confirmed trigger site. Effect IDs are exact decimal values; no addresses appear below.

| Event | Network handler / source | Effect ID | Instance class | Condition |
|---|---|---|---|---|
| PC spawn | `CharSpawn` handler | **310000001** | `UserXEffect` | actor sort = PC |
| Mob spawn | `CharSpawn` handler | **360000001** | `UserXEffect` | actor sort = mob |
| Level-up | `LevelUp` handler | **310000002** | `UserXEffect` | — |
| Death (PvP kill) | `CharDeath` handler | **350000010** | `UserXEffect` | death type = PvP |
| Death (PvE kill) | `CharDeath` handler | **360000003** | `UserXEffect` | death type = PvE |
| Attack hit (actor class range A) | `AttackEffect` handler | **350000026** | `UserXEffect` | actor id in class range A |
| Attack hit (actor class range B) | `AttackEffect` handler | **350000021** | `UserXEffect` | actor id in class range B |
| Attack hit (actor type flag A set) | `AttackEffect` handler | **350000021** | `UserXEffect` | actor internal flag A non-zero |
| Attack hit (actor type flag B set) | `AttackEffect` handler | **350000022** | `UserXEffect` | actor internal flag B non-zero |
| Skill cast (inner event 1001) | `AttackEffect` handler | from actor's cast-skill field | `UserXEffect` or `MapXEffect` | inner event = 1001 |
| (corrected 2026-06-13: this `AttackEffect` row is the post-contact **hit-burst** leg, fired on the `AttackEffect` message when the actor template's inner-event field reads 1001 — `MapXEffect` spawned at the caster position from the template's effect-id field. It is NOT the cast-channel looping effect, which is driven separately by the `ActorSkillAction` (`5/52`) handler from the per-skill record; see §15.) | | | | |
| Actor state (class range A) | `ActorStateEvent` handler | **350000026** | `UserXEffect` | class-id range A |
| Actor state (class ranges B/C) | `ActorStateEvent` handler | **350000021** | `UserXEffect` | class-id ranges B/C |
| Buff applied (inner event 1023) | `ActorStateEvent` handler | **350000021** | `UserXEffect` | inner event = 1023 |
| Item use successful (class range A) | `ItemUseEffect` response | **350000026** | `UserXEffect` | class range A |
| Item use successful (class range B) | `ItemUseEffect` response | **350000021** | `UserXEffect` | class range B |
| Item use secondary effect | `ItemUseEffect` response | **350000022** | `UserXEffect` | secondary condition |
| Trade state toggle | `TradeStateToggle` handler | **350000063** | `UserXEffect` | — |
| PvP death — stand phase | `PvpDeathFx` handler | **371003701** | `UserXEffect` | op code = 1 |
| PvP death — fall phase | `PvpDeathFx` handler | **371003702** | `UserXEffect` | op code = 6 |
| Animation cycle end / visual-slot | Visual cycle-end event callback | from actor visual-slot table at `visual_id + 1` | `UserXEffect` | inner event 1021; effect id looked up from the actor's visual-slot byte |
| Actor spawn, per-bone equip effects | Per-bone equip-slot loop | from `itemjointeff.txt` binding | `JointXEffect` | each equipped-item entry in the binding list |

### Hard-coded effect ID reference table

| Symbolic name | Decimal value | Role |
|---|---|---|
| `EFFECT_ID_PC_SPAWN` | 310000001 | PC character appearance effect |
| `EFFECT_ID_MOB_SPAWN` | 360000001 | Mob appearance effect |
| `EFFECT_ID_LEVELUP` | 310000002 | Level-up radiance |
| `EFFECT_ID_DEATH_PVP` | 350000010 | PvP kill death effect |
| `EFFECT_ID_DEATH_PVE` | 360000003 | PvE kill death effect |
| `EFFECT_ID_ATTACK_HIT_A` | 350000021 | Generic hit spark (primary type) |
| `EFFECT_ID_ATTACK_HIT_B` | 350000022 | Hit spark (secondary type) |
| `EFFECT_ID_ATTACK_CAST` | 350000026 | Skill cast burst |
| `EFFECT_ID_TRADE_TOGGLE` | 350000063 | Trade aura toggle |
| `EFFECT_ID_PVP_STAND` | 371003701 | PvP death — stand-phase effect |
| `EFFECT_ID_PVP_FALL` | 371003702 | PvP death — fall-phase effect |

---

## 8. Per-Frame Tick Math

**Confidence: CODE-CONFIRMED** (central tick and dispatch function traced). The function processes the active effect list each draw frame.

### 8.1 Position in the render loop

Effects are drawn during the transparent-overlay phase of the render pipeline, after opaque geometry. The tick-and-dispatch function is the central per-frame update; it iterates all entries in the active list.

### 8.2 Per-element update sequence

For each active element within a live instance:

1. **Gate.** Skip if the active flag is clear.

2. **First-tick initialisation.** On the element's first active frame, allocate per-element runtime particle buffers. Elements whose `resource_id ≥ 10000` also bridge to the particle sub-system (§11); the particle spawn is triggered at this point.

3. **Elapsed phase.**
   ```
   elapsed_ms = (current_clock_ms - spawn_timestamp_ms) × time_scale_factor
   phase_ms   = elapsed_ms mod (loop_period_ms)
   ```
   The loop period is the element's `total_time` when the loop flag is set; otherwise the effect expires after `lifetime_ms`.

4. **Proximity cull.** If the squared distance from the local player to the effect origin exceeds the render-distance threshold, skip geometry build for this instance. The active flag is NOT cleared; the instance remains alive.

5. **Frame index selection.**
   ```
   frame_index = elapsed_ms / anim_stride_ms        (unsigned integer division)
   active_kf   = frame_index mod tex_count
   ```
   Where `tex_count` is the element's keyframe count. If `anim_loop` is clear (single play), the element holds on the last frame and then expires.

6. **Keyframe interpolation.** The keyframe interpolation function lerps between `keyframes[active_kf]` and `keyframes[(active_kf + 1) mod tex_count]` using the fractional part of the elapsed-time-to-stride ratio, producing:
   - A blended `velocity` Vec3 (emission direction and displacement).
   - A blended `size` Vec3 (billboard half-extents or mesh scale).
   - A blended `alpha` float (already converted from the stored inverted value to opacity in `[0, 1]`).

   See §17.3 for the full per-channel sampling rule (linear lerp for scalar/vector channels, slerp for rotation, stepped for the sprite-frame index).

7. **Orientation.** The active orientation quaternion is fetched:
   - For `MapXEffect`: the stored `world_rot_quat` field.
   - For `JointXEffect`: resolved from the bound actor's bone each tick (see §9).
   - For `UserXEffect`: identity, or a stored orientation if a world quaternion was provided at spawn.

8. **World position.**
   ```
   world_pos = effect_origin + rotate(orientation_quat, velocity) × effective_scale
   ```
   `effective_scale` = `CoreXEffect.scale_default × caller_scale × effectscale.xdb override (if present)`.

9. **Geometry build by emitter type.**

   | `emitter_type` | Geometry |
   |---|---|
   | 0 — Billboard | Camera-facing quad; `half_width = −0.5 × size_x × scale`; `half_height = −0.5 × size_y × scale`; four corner vertices from `world_pos`. |
   | 1 — Mesh-particle | Vertices from the `.xobj` mesh (selected by `resource_id`); each vertex position scaled by `(size_x, size_y, size_z)` and rotated by the orientation quaternion. |
   | 2 — Directional billboard | Camera-facing quad with an additional 90° Y pre-rotation applied before the camera-facing transform. Reads an extra rotation value from the static-state branch. |

   The runtime CPU geometry-build branch is described more fully in §17.2 (billboard / oriented-quad / mesh, plus the `resource_id` CPU-mesh-vs-GPU-particle split).

10. **Vertex alpha.** `vertex_color.alpha = round(255 × blended_alpha_opacity)`.

11. **UV scroll** (conditional). If bit 0 of the low byte of `tex_count` is set:
    ```
    U_offset += (phase_ms mod 5000) / 5000.0       (5-second loop)
    ```
    If bit 1 is set, the same formula applies to the V coordinate. Confidence: MEDIUM (dual use of the frame-count field as a flags byte is unusual; the behavior is consistent with a render test but the intent is unverified).

12. **Submit.** The built geometry is submitted to the render pipeline's transparent draw queue. No per-particle back-to-front depth sorting is performed; the pipeline relies on alpha-smearing for approximate transparency layering. See §17.7 for the submission model (effects push their per-frame drawables into the shared queue and do not keep a private sort).

### 8.3 Expiry and cleanup

An instance expires when any of the following is true:
- `current_clock_ms > spawn_timestamp_ms + lifetime_ms` (lifetime exhausted).
- The descriptor pointer in the instance is null (effect not found at spawn, flagged during lazy-load).
- The `JointXEffect`'s bound actor is no longer in the actor manager (actor despawned).

Expired instances are unlinked from the active list and returned to their pool.

---

## 9. `JointXEffect` Bone Attachment

**Confidence: CODE-CONFIRMED** for all field values; **UNRESOLVED** for `bone_source_enum` modes 1 and 2 (the action-table contents are not recovered).

### 9.1 Attachment fields (from §6.3)

| Field | Offset | Meaning |
|---|---|---|
| `actor_sort_id` | +0x54 (u32) | Sort-id of the target actor; used to look up the actor in the `ActorManager`. |
| `sub_id` | +0x58 (u8) | Sub-actor identifier, e.g. distinguishes a mount from its rider. |
| `bone_source_enum` | +0x59 (u8) | Controls how to resolve the bone index. See enum below. |
| `bone_id_or_hint` | +0x5C (u32) | The bone index (when `bone_source_enum` = 0), or a lookup hint (modes 1 and 2). |
| `quat_source_enum` | +0x60 (u8) | Controls which orientation quaternion is used. See enum below. |

**`bone_source_enum` values:**

| Value | Meaning | Confidence |
|---|---|---|
| 0 | Explicit bone index — use `bone_id_or_hint` directly. | CODE-CONFIRMED |
| 1 | Look up bone from action table A (table contents unrecovered). | UNRESOLVED |
| 2 | Look up bone from action table B (table contents unrecovered). | UNRESOLVED |

**`quat_source_enum` values:**

| Value | Meaning | Confidence |
|---|---|---|
| 1 | Use the bone's own world-space quaternion for the effect orientation. | CODE-CONFIRMED |
| 2 | Use the actor's root transform quaternion for the effect orientation. | CODE-CONFIRMED |

> Campaign-5 refinement (§17.4) further resolves the `bone_source_enum` non-zero case as a lookup through the AnimCatalog keyed by the actor's model/visual class (modulo 40) plus one of four weapon-hand slot offsets; which slot means which hand/weapon remains PLAUSIBLE only.

### 9.2 Per-tick bone resolution

On every tick, a `JointXEffect` executes:

1. Look up the bound actor by `actor_sort_id` in the `ActorManager`. If not found, expire the instance.
2. Resolve the target bone index via `bone_source_enum`:
   - Mode 0: use `bone_id_or_hint` directly.
   - Modes 1/2: table lookup (UNRESOLVED).
3. Read the bone's world-space position → this becomes the effect origin.
4. Read the bone's world-space quaternion (mode 1) or the actor root quaternion (mode 2) → this becomes the orientation quaternion for the velocity rotation in §8.2 step 8.

### 9.3 Two spawn entry points

**Entry point A** — called from the visual cycle-end event path. Iterates a per-actor table of up to 8 equip-slot effect entries; spawns one `JointXEffect` per slot. Each in-memory table entry: `[effect_id u32][bone_id u32][scale f32][flag u8]`.

**Entry point B** — called from equip and actor-spawn paths. Iterates the `itemjointeff.txt` / `mobjointeff.txt` binding list for the given actor id; spawns one `JointXEffect` per record. Each record in those files: `[actor_id u32][effect_id u32][bone_id u32][bone_source u32][scale f32][flag u8]`.

---

## 10. Damage-Number Renderer

**Confidence: CODE-CONFIRMED** for initialisation. Geometry emission detail not fully traced.

The damage-number renderer is a dedicated sub-system initialised at boot (before the first frame). It is separate from the `XEffect` and `ParticleEffectManager` systems.

### 10.1 Digit sprite array

- 12 animation frames are allocated; each frame is a 32-byte record. The 12 frames represent the ten decimal digits (0–9) plus two additional glyphs (critical modifier and miss label).
- Three textures are loaded at boot:
  - `data/effect/tex/att-font.dds` — normal hit digit font.
  - `data/effect/tex/cri-font.dds` — critical hit digit font.
  - `data/effect/tex/miss.tga` — miss label.

### 10.2 Vertex buffer

A dedicated Direct3D vertex buffer is created at boot with:
- Vertex stride: 96 bytes.
- Capacity: 520 vertices.
- Usage flags: `322` (a D3D9 `D3DUSAGE_DYNAMIC | D3DUSAGE_WRITEONLY` combination; value is the literal constant from the binary).

The vertex buffer is written each frame when damage numbers are active. The format of the 96-byte vertex record within this buffer is **UNVERIFIED** — the geometry build path was not fully traced in this analysis pass.

---

## 11. `ParticleEffectManager` Sub-System

**Confidence: CODE-CONFIRMED** for the spawn path, the validation logic, and the `particleEmitter.eff` file structure (variable-length entry sequence). See `formats/effects.md §E` for the on-disk layout.

The `ParticleEffectManager` is a completely separate sub-system from the `XEffect` class family. It handles true particle systems (fire, smoke, burst emitters) as opposed to the billboard/mesh-primitive emitters in `.xeff`.

### 11.1 Boot load

At boot the manager lazily loads `data/effect/particle/particleEmitter.eff` the first time a GPU-particle element (`.xeff` `resource_id >= 10000`) is spawned. The file is a **sequence of variable-length entries**, each a 28-byte entry header + (`num_frames` x 52-byte sub-record) + 64-byte trailing texture name; the read loop terminates when an entry header's `num_frames` is 0. There is **no file-level magic** and no flat record count. Each entry is inserted into a sorted map keyed by its `entry_id` (the entry header's first u32). See `formats/effects.md §E.2` for the full layout. (CORRECTED Campaign 5B from the retired "16-byte header + 2,243 x 52-byte flat records" model.)

### 11.2 Spawn path

`ParticleEffectManager` is invoked from the tick function in §8 when an `XEffect` element has `resource_id ≥ 10000`. The spawn function receives:
- The `resource_id` **verbatim** as the map key (CORRECTED Campaign 5B: there is **NO** `resource_id − 10000` subtraction). The `≥ 10000` test is a dispatch gate, not an index base.
- A world-space origin (3-float position).

It then:
1. Looks up the entry whose `entry_id == resource_id` (raw-id map lookup; a miss silently abandons the spawn — `formats/effects.md §E.4`).
2. Validates that the record's texture handle is non-null, that the particle count is greater than zero, and that the particle info pointer is non-null.
3. If validation passes, allocates a 60-byte `ParticleEffect` object, constructs it with the texture handle, spread, speed, count, type, and particle-info parameters, then sets the world-space spawn origin.
4. If validation fails, the spawn is silently abandoned.

The `GParticleBuffer` is a Direct3D vertex buffer manager dedicated to particle geometry; it uses a prepare-and-lock mechanism. Error conditions include: "index lock failed" and "vertex lock failed" (confirmed from error strings in the binary read-only data section).

The Campaign-5 pass adds that the start/stop of a GPU-particle component is gated by that component's **active-frame window**: entering the window starts the emitter, leaving it stops/destroys it. See §17.2.

---

## 12. `SwordLightManager` Sub-System

**Confidence: CODE-CONFIRMED** for boot loading and descriptor record layout.

The `SwordLightManager` renders glowing weapon trails as geometric ribbons. It is completely separate from the `XEffect` family; it does NOT use `.xeff` descriptors.

### 12.1 Boot load

The manager is initialised at the same time as the main effect manager (same boot thread). It loads:
- `data/effect/itemswordlight.txt` — item-based weapon-trail descriptors.
- `data/effect/mobswordlight.txt` — mob-based weapon-trail descriptors.

### 12.2 Descriptor record format

Each record in both files (loaded as text, field-by-field):

| Field index | Type | Field | Notes |
|---|---|---|---|
| 1 | u32 | `item_id` or `mob_id` | Key for lookup |
| 2 | f32 | `color_r` | Trail colour red channel (0.0–1.0) |
| 3 | f32 | `color_g` | Trail colour green channel |
| 4 | f32 | `color_b` | Trail colour blue channel |
| 5 | f32 | `offset_x` | Offset from weapon-attachment bone, X |
| 6 | f32 | `offset_y` | Offset from weapon-attachment bone, Y |
| 7 | f32 | `offset_z` | Offset from weapon-attachment bone, Z |
| 8 | u8 | `enabled_flag` | Non-zero enables the trail |
| 9 | char[] | `texture_name` | ASCII name resolved via the texture manager |

**Confidence: CODE-CONFIRMED** — field order traced from the loading function argument sequence.

### 12.3 Runtime behaviour

The sword-light trail is rendered as a geometric ribbon attached to the actor's weapon bone. The per-frame trail geometry build (the ribbon construction from bone position, offset, color, and texture) was **not fully traced** in this analysis pass. See Open Questions §14, item 8.

The Campaign-5 pass identifies the runtime renderable as a dedicated `SwordLightEffect` class (separate from the `.txt` descriptor tables above) carrying a paired start/end RGBA gradient; see §17.6.

---

## 13. Companion Manifest File Format Summary

All eight boot manifests reside in `data/effect/`. The read mechanism is a mixed binary/text reader that handles both little-endian `u32` and ASCII decimal integers from the same stream.

| File | Record schema | Keyed by |
|---|---|---|
| `bmplist.lst` | `u32 count` + `count × char[30]` name | Sequential index (slot number) |
| `xeffect.lst` | `u32 count` + `count × char[30]` name | Sequential index; id from file header |
| `xobj.lst` | `u32 count` + `count × (u32 id, char[30] name)` | `id` → xobj path |
| `totalmugong.txt` | `int count` + `count × 4 int tokens` (text-mode) | `field2 + AnimCatalog[field1 + 1]` (catalog-resolved) |
| `itemjointeff.txt` | `int count` + `count × 6 tokens`: `group_key, effect_id, int, int, float, byte` (text-mode; `effect_id == 0` skips) → **item manager** | raw `group_key` (NOT catalog-resolved) |
| `mobjointeff.txt` | `int count` + `count × 6 tokens`: `class, offset` key pair + inner `{int, int, float, byte}` (text-mode) → **mob manager** | `offset + AnimCatalog[(class + 1)]` (catalog-resolved) |
| `itemswordlight.txt` | `u32 count` + per-record: `(u32 item_id, f32 r, f32 g, f32 b, f32 ox, f32 oy, f32 oz, u8 enabled, char[] tex_name)` | `item_id` |
| `mobswordlight.txt` | Same schema as `itemswordlight.txt` | `mob_id` |

**Schema correction (Campaign 5B, CONFIRMED loader read-order).** These three are **text,
whitespace/token-delimited** tables, NOT fixed binary — each field is parsed as an int or float token,
and each file leads with an int record count. Load-bearing facts:

- `itemjointeff.txt` populates the **item joint-effect manager**, keyed on the **raw** `group_key`.
  Its record carries an explicit **float** token and a final int stored as a single byte; an
  `effect_id == 0` skips the record.
- `mobjointeff.txt` populates the **mob joint-effect manager** (the OTHER manager — they are not
  interchangeable), keyed on the **catalog-resolved** key `offset + AnimCatalog[(class + 1)]`.
- `totalmugong.txt` stores the catalog-resolved key `field2 + AnimCatalog[field1 + 1]` (4 int tokens
  per record).

The `AnimCatalog` singleton's internal array layout is **UNRESOLVED** (only its `[index]` access shape
is known) — a faithful port must reproduce the catalog to compute the mob/mugong keys exactly. See
`formats/effects.md §F.7` for the per-token schemas.

---

## 14. Open Questions

1. **Fourth (and possibly sixth) effect pool class.** The fourth pool type was not identified from the traced class hierarchy; possible candidates are a local-player-only effect type or an absolute-world effect type. A search from the fourth pool's allocator function would recover the constructor and class name.

2. **`bone_source_enum` modes 1 and 2 (action tables A and B).** The tables themselves were not located or traced. They are likely per-action effect-slot tables inside the actor structure, possibly linked to animation event descriptors. Cross-reference with `specs/skinning.md` and the animation catalog for the bone-mapping tables. The Campaign-5 pass (§17.4) narrows this to an AnimCatalog weapon-hand slot lookup but does not resolve which slot means which hand/weapon — still open.

3. **Per-frame bone world-position read offset.** The byte offset of the bone's world-space position within the actor's skeleton transform array was not isolated in this pass. Engineers implementing bone attachment should reference `specs/skinning.md` for the confirmed bone-world-transform layout. The Campaign-5 pass (§17.4) observes the runtime reads the bone world translation from the bone matrix's translation row and a quaternion adjacent to it; reconcile the exact slot indices with the skinning lane.

4. **Visual-slot visual cycle event (`inner_event` 1021).** The effect id for this trigger is looked up from the actor's visual-slot table at index `visual_id + 1` (where `visual_id` is a byte within the actor object at `+0x96`). The table structure and the relationship between the visual slot and the texture manager's index are not fully mapped.

5. **`MapXEffectManager` persistent map-effect list.** When a player enters a new area, the `MapXEffectManager` loads area-specific ambient effects. The format of the per-area map-effect manifest (if any), and how it differs from `xeffect.lst`, was not traced in this pass.

6. **Active-list iteration structure.** Whether the active list is an intrusive linked list, an STL-style list, or a custom pool array was not confirmed. The list is iterated by the per-frame tick dispatcher; its structure is relevant for the reimplementation.

7. **`totalmugong.txt` record semantics.** The loader reads `class_idx` as a 0-based index into the animation catalog's martial-arts class table. The `timing_offset` and the two sound ids are stored but their read-back path and relationship to skill-cast sound timing were not traced beyond the loader itself.

8. **SwordLight trail geometry generation.** The per-frame ribbon construction — transforming bone position + color-offset → screen-space ribbon vertices — was not traced in this pass. The trail width, fade duration, and vertex count are entirely UNVERIFIED. The Campaign-5 pass (§17.6) adds the head/tail alpha-gradient bytes and the in-pipeline draw, but the full ribbon vertex generation and its exact blend state (additive vs alpha) remain UNVERIFIED.

9. **`effectscale.xdb` application site.** The per-effect scale override table (`formats/effects.md §D`) is loaded at boot. Whether the override multiplier is applied in addition to, or instead of, `CoreXEffect.scale_default` during the effective-scale computation in §8.2 step 8 was not confirmed.

10. **Effect cleanup on area transition.** When the player enters a new area, it is unknown whether the effect manager flushes all active `UserXEffect` and `JointXEffect` objects, or only the `MapXEffect` objects. The area-transition call chain was not traced past the `EffectCache_SaveIDs` call at shutdown.

11. **AoE fan-out (action code 0xCC) per-sub-actor effects.** The `5/52` AoE action code places sub-actors in a ring (position placement is documented), but whether each sub-actor in the ring receives its own cast-channel effect spawn — and if so, whether each re-emits a 0xC8-style enable for its own instance — was not confirmed in this pass. PLAUSIBLE: each ring instance re-emits its own enable; UNRESOLVED. See §15.3.

12. **Frame-precise cast-SFX consumer (`totalmugong.txt`).** The cast chain fires a single one-shot sound at cast-enable from the skill record's `cast_sfx_id` (byte offset 1180; §15.2). Separately, `totalmugong.txt` (§5 step 5, §13) builds a timing-keyed sound-overlay map (the effect manager's `class_idx`-keyed table) intended to fire **frame-synchronised** sounds during the cast animation. The runtime consumer that reads that map and triggers those frame-precise overlays was **not** traced — it is unknown which subsystem reads it, and whether a single cast uses both the one-shot `cast_sfx_id` and the `totalmugong.txt` overlays, or whether they are mutually exclusive. This extends open question §14 item 7.

13. **emitter_type enum completeness.** The Campaign-5 runtime pass observed only three CPU geometry branches (billboard / oriented-quad / mesh, §17.2). If the `.xeff` format defines additional named emitter kinds, they collapse into the "else" mesh branch at runtime — UNVERIFIED that this is the full set. The on-disk enum is `formats/effects.md §A.12`.

14. **Global transparent-pass sort.** §17.7 confirms effects submit drawables into the shared queue rather than self-sorting, but the global depth/transparent sort itself was not walked — UNVERIFIED whether effects are forced into a late/transparent bucket or sorted purely by material/blend state.

---

## 15. Skill-Cast Effect Chain (cast-channel leg)

**Confidence: CODE-CONFIRMED for the resolution chain and spawn/teardown behaviour; CAPTURE-UNVERIFIED for the network half.** The action-code values and the `5/52` message layout are read statically from the client; no live capture has yet confirmed the on-wire values. Treat the protocol facts here as static-truth-only until a capture exists.

This section documents the **cast-channel** effect — the looping aura/glow that plays while a martial-arts skill is being channelled. It is distinct from, and coexists with, the post-contact **hit-burst** effect in §7 (the `AttackEffect` hardcoded-id path). The two fire on different events and from different data sources.

### 15.1 Trigger and resolution chain

The cast-channel effect is driven by the **actor skill-action** server message (opcode `5/52`, `SmsgActorSkillAction`; see `opcodes.md` and `packets/5-52_actor_skill_action.yaml`). That message carries a server-supplied `skill_id` and an **action code** byte that selects cast-enable vs. cast-disable behaviour (see §15.3).

On cast-enable, the handler resolves the effect id **directly from the per-skill record** — there is **no separate skill-id → effect-id lookup table**. The effect-id → descriptor step is **Option B (registry lookup), CONFIRMED**: the runtime ALWAYS resolves through the boot-populated sorted registry keyed by the raw numeric `effect_id`; it **never** builds a numeric `data/effect/xeff/{id}.xeff` path at spawn (no integer-to-`.xeff` format string exists). The registry is populated at boot from `xeffect.lst` (`u32 count` + 30-byte NUL-padded names): per record the loader concatenates `data/effect/xeff/<name>` — a **name** concat, not an id format — opens the file, and reads the **first u32 of the header as the `effect_id`** (the registry key comes from the FILE HEADER, not the list order). On a miss the resolver returns invalid (0); there is **no** numeric-name fallback open. See `formats/effects.md §C.2`.


```
skill_id (from the 5/52 message)
  → skill-catalog lookup (id-keyed map of loaded skill records)
  → skill record  (the in-memory copy of one data/script/skills.scr record)
  → cast_effect_id  =  skill record field at byte offset 1136
                       (indexed by the caster's visual-class byte; see §15.2)
  → CoreXEffect lazy-load resolver (§5.1), keyed by numeric effect_id
  → CoreXEffect descriptor  (lazy-parsed by reopening the boot-stored path string for that id; the path was built at boot from the xeffect.lst NAME, never re-derived from the decimal id)
  → looping UserXEffect spawn via the UserXEffect factory (§5)
```

The effect id is embedded **verbatim** in the skill record; it is the same numeric id that names the `.xeff` file on disk (the 9-digit `.xeff` filename is the decimal form of the effect id — see `formats/effects.md §A.2`).

**Confidence: CODE-CONFIRMED** (two independent action-code handlers read the same byte offset 1136); the `.xeff`-filename = decimal(effect_id) mapping is SAMPLE-VERIFIED elsewhere (`formats/effects.md §A.2`), not re-verified in this pass.

### 15.2 The three sibling fields in the skill record

The cast chain reads three sibling u32 fields from the in-memory skill record (the 1504-byte structure copied from one `data/script/skills.scr` row). All three are indexed by the caster's **visual-class byte** (the actor's resolved visual/mesh class id; the field at offset 1136 etc. is element 0 of a parallel array, with the visual-class byte added as the array index). For the default visual class (0) the offsets are exactly as listed.

| Skill-record byte offset | Field (role) | Consumed for | Confidence |
|---:|---|---|---|
| **1116** | `cast_motion_base` — base id of the cast animation | Combined with the caster's class and gender into a final animation id, then played on the actor's animation mixer when the cast begins. | CODE-CONFIRMED |
| **1136** | `cast_effect_id` — the cast-channel effect id | Resolved through the CoreXEffect registry and spawned as a looping `UserXEffect` (§15.1, §15.4). | CODE-CONFIRMED |
| **1180** | `cast_sfx_id` — the cast-start sound id | Played once as a one-shot sound cue (sound kind 11) when the cast begins. | CODE-CONFIRMED |

> The skill-record layout itself (these offsets) is authoritatively owned by `structs/skill.md` and `formats/config_tables.md`, which previously listed +1116 / +1136 / +1180 as un-decoded chain-reference composites (`ChainRef[0]` / `ChainRef[5]` / `ChainRef[7]`). This section **resolves the field meaning** for the three offsets the cast chain consumes; the prefix families observed there (`3xxxxxxx` at +1136, `8xxxxxxx` at +1180) match the effect-id and sound-id families used elsewhere in this spec, corroborating the resolution. The byte layout stays in `structs/skill.md`; do not re-list it here.

**Array note:** `cast_motion_base` and `cast_effect_id` are parallel arrays indexed by the visual-class byte. The array extents (how many visual classes a single skill carries) are not independently confirmed; at minimum the index expression is consistent with adjacent parallel arrays covering several classes. PLAUSIBLE only — see §14 open items.

### 15.3 Action codes (cast lifecycle)

The `5/52` handler branches on an action-code byte. The cast-channel effect is bound to the enable/disable pair:

| Action code | Phase | Effect behaviour | Confidence |
|---|---|---|---|
| **0xC8** | Cast-enable | Spawn the looping `UserXEffect` from `cast_effect_id` (§15.4); play the cast animation from `cast_motion_base`; fire the `cast_sfx_id` one-shot; set the actor's action-lock slot to the active skill id. | CODE-CONFIRMED |
| **0xC9** | Cast-disable | Soft-stop the running cast effect by `cast_effect_id` (§15.5); reset the animation; clear the action-lock slot. | CODE-CONFIRMED |
| **0xCA** | Secondary enable | Sets a secondary action-lock; no cast-channel effect is spawned on this code. | CODE-CONFIRMED |
| **0xCB** | Secondary disable | Soft-stops the cast effect by the same `cast_effect_id` (byte offset 1136), mirroring the 0xC9 teardown. | CODE-CONFIRMED |
| **0xCC** | AoE fan-out | Places sub-actors in a ring (position placement only; per-sub-actor effect spawning is UNRESOLVED — see §14). | UNRESOLVED (effect behaviour) |

**Confidence: CODE-CONFIRMED** for the 0xC8 / 0xC9 / 0xCB effect coupling (the same effect id, read at byte offset 1136, is used to both start and stop the instance). **CAPTURE-UNVERIFIED** for the action-code values themselves.

### 15.4 Cast effect is a looping, actor-anchored `UserXEffect` (NOT bone-attached)

On cast-enable, the resolved `cast_effect_id` is spawned through the **`UserXEffect` factory** (§5), not the `JointXEffect` factory. The key spawn properties:

- **Looping** — the loop flag is set, so the effect repeats its keyframe animation indefinitely until it is explicitly stopped (it does not expire on a lifetime timer).
- **Actor-anchored, not bone-attached** — the effect is bound to the caster by the actor's sort-id + id pair (the `UserXEffect` actor-relative anchor of §6.2), so the effect origin follows the caster's **world position**. It does **not** track a specific skeleton bone; it is a `UserXEffect`, not a `JointXEffect`.
- **Default transform** — spawned at scale 1.0 and time-scale 1.0 with no extra anchor offset.

This is why the cast aura sits at the character's body/feet origin and follows them as they move during the channel, rather than riding a hand or weapon bone. Weapon-trail effects (the `SwordLight` sub-system, §12, and the `JointXEffect` equip bindings, §9) are a separate system and run independently of the cast channel.

**Confidence: CODE-CONFIRMED.**

### 15.5 Cast teardown is a soft-stop

Cast-disable (action codes 0xC9 / 0xCB) does **not** free the effect with a fade-out callback. Instead it performs a **soft-stop**: the active `UserXEffect` list is walked for an instance whose descriptor effect id equals `cast_effect_id` **and** whose anchored actor sort-id + id pair match the caster, and that instance's active flag is cleared. The instance is then removed by the per-frame tick on the following frame (§5.2, §8.3).

Because there is no fade-out, the visual close of the cast effect is whatever the descriptor's keyframe animation happens to be showing when the active flag is cleared. The same soft-stop routine is also used to cancel the cast effect on an animation-cycle-end / idle transition (so an interrupted or naturally ending cast removes its aura the same way).

**Confidence: CODE-CONFIRMED.**

### 15.6 Relationship to the hit-burst leg (§7)

The two skill-related effect legs are independent and may both be live during a single skill use:

| | Cast-channel leg (this section) | Hit-burst leg (§7) |
|---|---|---|
| Source message | `5/52` `SmsgActorSkillAction`, action code 0xC8/0xC9/0xCB | `AttackEffect` message |
| Effect-id source | Per-skill record field at byte offset 1136 (varies by skill) | Hardcoded id ranges in the handler (always a small fixed set, e.g. 350000021 / 350000022 / 350000026) |
| When it fires | At cast-start, removed at cast-end | On contact / post-hit |
| Lifetime | Looping until soft-stopped | One-shot (plays out and expires) |
| Per-skill identity | Yes — each skill carries its own cast effect id | No — the wire payload's id range selects from the fixed set |

**Confidence: CODE-CONFIRMED** for the distinction.

---

## 17. Campaign-5 Runtime Deep-Dive — XEffect / EffectManager Element Model

> Added Campaign 5, Lane 1 (effects runtime). Promoted from dirty-room runtime analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This section refines
> and extends the earlier runtime sections (§5–§9) with the per-element dispatch model, the precise
> keyframe-sampling math, the bone-attach overlay, the blending model, and the
> draw-order/submission model. It does NOT restate the boot sequence (§3), pool teardown (§4), or
> trigger table (§7). The companion in-memory struct table is `formats/effects.md §A.16`.
> Engineers cite this section as `// spec: Docs/RE/specs/effects.md §17`.

### 17.1 One polymorphic 104-byte runtime element

**Confidence: 104-byte size CONFIRMED.** Every live effect instance is a single fixed-size object
of **104 bytes (0x68)**, regardless of subtype. The size is independently confirmed by the pool
allocator, which batch-allocates blocks of `104 × N` bytes and walks them in 104-byte strides to
build its free-list. The polymorphic family selects behaviour through a virtual-dispatch-table
pointer at the element's start plus a numeric **type tag** stored at byte offset +0x04:

| Class (neutral name) | type tag (+0x04) | Role |
|---|---:|---|
| `XEffect` (base) | (none set by the base) | Abstract base; zeroes defaults at construction |
| `UserXEffect` | 1 | World / actor-anchored timed effect (also the looping cast-channel, §15.4) |
| `JointXEffect` | 4 | Bone-attached effect; samples a skeleton bone each tick (§17.4) |
| `MapXEffect` | (set by its own constructor) | Map / world-placed effect |

`SwordLightEffect` (§17.6) is a **separate, much larger class** and is NOT a member of this
104-byte family.

The byte-level field table for the 104-byte element (including the `JointXEffect` overlay of the
attachment region) is the authoritative struct in `formats/effects.md §A.16`. This section describes
only behaviour. The shared-field offsets above and in §6 are consistent with that table.

### 17.2 Per-element emitter dispatch (billboard / oriented-quad / mesh, and the GPU split)

**Confidence: emitter dispatch CONFIRMED; exact enum names PLAUSIBLE (named by behaviour).**

Each parsed descriptor (the shared `CoreXEffect`, referenced by the element's descriptor pointer)
owns a vector of **components** (the emitter elements). Each component carries an `emitter_type`
selector and a `resource_id`. Two orthogonal choices drive how a component renders:

**(a) emitter_type — the geometry style** (a three-way branch on the component's first field):

| emitter_type (runtime branch) | Geometry built per frame |
|---|---|
| 0 | **Screen-facing billboard quad.** Four corners at ±0.5·size_x / ±0.5·size_y in the camera's billboard basis (the camera-config view-matrix right/up vectors), each corner rotated by the element's world-orientation quaternion, then translated to the world position; per-vertex diffuse colour stamped. |
| 1 | **Axis-rotated / oriented quad.** Same quad, but a facing direction is first derived from the sampled rotation (Euler→direction) and an extra fixed 90° rotation about Y is applied. Used for ground-aligned or velocity-aligned sprites. Iterates the source mesh vertex list applying the rotation. |
| else (mesh) | **Mesh particle.** Take the shared mesh's vertices (24-byte stride: position Vec3, UV/normal, packed diffuse), scale each by the sampled size and the instance's effective scale, rotate by the element quaternion, translate to world, and stamp diffuse colour. |

> Reconciliation with the on-disk enum: `formats/effects.md §A.12` lists the **file** `emitter_type`
> as 0 = billboard, 1 = mesh, 2 = directional. This section describes the **runtime CPU geometry
> branch** (billboard / oriented-quad / else-mesh). Treat the format spec as authoritative for
> parse-time enum decoding and verify the geometry path against a sample at implementation time
> where the meaning of value 1 vs the "else" branch differs (PLAUSIBLE reconciliation).

**(b) resource_id — the CPU-mesh vs GPU-particle split** (CONFIRMED):

- `resource_id < 10000` → **CPU mesh particle** (the shared mesh table, "XObj"). The per-frame CPU
  vertex transform above applies.
- `resource_id ≥ 10000` → **GPU particle** ("XPObj"), handed to the separate particle manager
  (§11). The CPU vertex transform is bypassed entirely; the runtime only positions the emitter and
  starts/stops it on the component's active-frame window.

This `< 10000 / ≥ 10000` rule is the primary "billboard/mesh vs GPU-particle" switch and matches the
`XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000` constant in `formats/effects.md §A.14`.

**Per-component active-frame window (CONFIRMED):** each component carries a `[start_frame,
end_frame]` window in the same time units as the local phase. A component is skipped while the phase
is outside its window; for GPU-particle components, leaving the window stops/destroys the emitter.

### 17.3 Keyframe / curve sampling — piecewise-linear with slerp rotation

**Confidence: CONFIRMED** (the sampling math was fully traced).

Per component, per frame, the runtime samples each animated channel by **linear interpolation in
time**, except rotation (which uses **slerp**) and the sprite-frame index (which is **stepped**).

Given the local phase time `t` (the elapsed-and-wrapped time after the loop-period modulo and the
per-component window), and a per-channel frame stride `dt` and frame base time `t0`:

```
i      = floor((t - t0) / dt)          // integer key index
i_next = i + 1
frac   = ((t - t0) mod dt) / dt        // fractional position in [0, 1)
```

The index is **clamped to the channel's key count** (the last key holds past the end). Each channel
is then sampled as:

| Channel | Sampling rule |
|---|---|
| Sprite / texture frame index | **Stepped (no interpolation):** `frame = keys[min(i, count − 1)]`. Selects the animated sub-image. |
| Alpha (scalar) | **Linear:** `alpha = keys[i]·(1 − frac) + keys[i_next]·frac`; defaults to 1.0 when no keys. |
| Colour (RGB Vec3) | **Linear, component-wise**; defaults to (1, 1, 1) when no keys. |
| Velocity / position-delta (Vec3) | **Linear.** |
| Size / half-extents (Vec3) | **Linear.** |
| Rotation (quaternion) | **Slerp** between adjacent keys. |

So: **scale, colour, alpha, position, and velocity are straight piecewise-linear lerps between
adjacent keys; rotation is slerp; the discrete sprite-frame channel is stepped.** There is no spline
/ curve table — only linear sampling with frame-index stepping for the sprite channel.

**Texture scroll (independent of keyframes):** a per-component render flag byte enables a continuous
texture scroll on top of the keyframes — if its low bit is set, each vertex's U texture coordinate is
offset by `(phase mod 5000) / 5000` (a five-second loop); the next bit does the same for V. This
matches the UV-scroll behaviour and period in `formats/effects.md §A.13` / `XEFF_UV_SCROLL_PERIOD_MS
= 5000`, and refines §8.2 step 11. CONFIRMED.

**Vertex colour packing (CONFIRMED):** the sampled colour and alpha are written to each vertex as a
packed diffuse value — red/green/blue from the colour channel times 255, alpha from the alpha channel
times 255 — with the alpha additionally scaled for mesh particles by a global brightness/option
factor (a configurable brightness setting blended with a base constant). This is ordinary per-vertex
alpha modulation, not an inversion flag (the on-disk **storage** of alpha is inverted; see §17.5 and
`formats/effects.md §A.6`).

### 17.4 `JointXEffect` bone attachment (overlay of the attachment region)

**Confidence: bone-attach mechanism CONFIRMED; AnimCatalog weapon-slot meaning PLAUSIBLE.**

A `JointXEffect` (type tag 4) re-purposes the element's actor-anchor region (the bytes from +0x58
onward) as a **bone-attachment descriptor** — see the overlay table in `formats/effects.md §A.16`,
and the field-level view in §6.3 / §9.1. Each tick:

1. **Resolve the owning actor** from the stored `(bone_actor_id, bone_actor_sub)` pair via the actor
   manager. If the actor is gone, kill the effect.
2. **Resolve the bone** by the bone-source mode byte:
   - **Mode 0 (explicit):** use the stored explicit bone id directly.
   - **Mode 1 (catalog lookup):** pick a bone id from the **AnimCatalog**, keyed by the actor's
     model/visual class wrapped modulo 40, plus a weapon-hand slot offset (one of four sibling slot
     indices) chosen by a sub-mode and an actor-descriptor flag byte. This is the weapon-hand /
     handedness selector (left/right hand, primary/secondary weapon). The four slot indices and the
     class-wrap are CONFIRMED from the code; **which slot means which hand/weapon is PLAUSIBLE only**
     and must be reconciled with the skinning / AnimCatalog lane.
   Then fetch the bone object from the actor's live pose by that bone id.
3. **Read the bone world transform.** The runtime copies the bone's **world translation** into the
   element's world position. For orientation it copies a **quaternion** (not re-derived from the
   matrix) chosen by an orientation-source byte:
   - source = 1 → the **bone's own world rotation**;
   - source = 2 → the **actor's facing** quaternion.
   The bone's world translation is read from the bone-matrix translation row (the matrix is row-major
   with the translation in its last row), and the bone quaternion sits adjacent to that translation in
   the bone struct.
4. **Distance-cull** versus the local player, then drive every component exactly as in §17.2 / §17.3,
   anchored to the bone transform instead of an actor root. A configurable height offset is added to
   the anchor position.

**Godot-bridge facts:** the attach point is a **bone WORLD transform sampled from the live skeleton
pose** (the same pose pool the skinning lane recovered), addressed **by bone id**, optionally resolved
through the AnimCatalog weapon-slot map. Because the position and orientation are copied from the live
actor/bone each tick, the effect inherits the actor's world convention unchanged; the project's
world-Z-negation rule (`WorldCoordinates.ToGodot`) applies to the sampled position as-is. Billboard
quads, by contrast, are built in the **camera basis**, so their up axis / handedness follow the camera
config, not the mesh-local X-negate rule. See `specs/skinning.md` for the authoritative bone
world-transform layout, and §9 for the earlier field-level description of this attachment.

### 17.5 Blending model — alpha vs additive, and the "alpha-invert" question

**Confidence: alpha storage inversion CONFIRMED; additive-vs-alpha blend-state PLAUSIBLE.**

Two distinct things were found, and they should not be conflated:

- **Per-particle alpha** of the billboard / mesh path is the sampled alpha channel (§17.3), written
  into the vertex diffuse alpha and modulated by the global brightness option. This is ordinary alpha.
  The **on-disk** alpha keys are stored **inverted** (file 0.0 = opaque, 1.0 = transparent; the loader
  applies `1 − value`) — see `formats/effects.md §A.6`. There is no per-effect "invert" boolean in the
  runtime.
- **The actual blend state** (additive vs alpha-blend, alpha-test / alpha-ref) is NOT hard-coded by
  the effect code. It is carried by each drawable's own material/render-state and applied by the shared
  (Diamond) render layer that owns the blend modes. So whether a given effect renders additively or as
  straight alpha is a property of its drawable's material, decided downstream, not by the effect tick.
  PLAUSIBLE.

So "alpha-invert" is best understood as the **inverted on-disk storage of the alpha curve** (§A.6),
not a runtime invert flag. The visual "inverted/additive glow" look of many effects comes from the
drawable's additive blend state plus the alpha-modulated diffuse, not from a boolean named "invert".

### 17.6 `SwordLightEffect` — the weapon-swing trail (separate class)

**Confidence: start/end alpha gradient bytes CONFIRMED; additive blend PLAUSIBLE.**

The weapon-swing light trail is a **separate effect class**, NOT the 104-byte XEffect. It derives
from the engine's geometry-drawable family and is a much larger object (on the order of a few
kilobytes) that holds a fixed-capacity sub-array of trail segments. Its colour model is a **paired
start/end RGBA gradient** along the swing: a start RGB with a start alpha byte (default fully opaque)
and an end RGB with an end alpha byte. The trail therefore fades from a **head alpha to a tail alpha**
along its length — this gradient is the designer-set "swing alpha" property (a named serialized
property read by an in-editor property dialog). The trail is drawn **in-pipeline** as part of its
owning geometry's draw (a direct indexed-primitive draw, position+diffuse vertex format, 24-byte
stride), with no separate effects pass.

This is the concrete meaning of "alpha-invert" for the sword trail: a **head→tail alpha gradient**
plus an (PLAUSIBLE) additive blend state, not a boolean invert. This `SwordLightEffect` class is
distinct from, and runs independently of, both the `XEffect` family and the `SwordLight` descriptor
text tables of §12 (the tables feed it; this class renders it).

### 17.7 Effect draw order — submission into the shared drawable queue (no private sort)

**Confidence: the per-frame submit call is CONFIRMED; the absence of a private sort is observed
(PLAUSIBLE→CONFIRMED); the global transparent-pass sort itself was NOT walked (UNVERIFIED).**

Effects do **not** maintain their own depth-sorted draw list. Each tick, after a component's geometry
is transformed, the runtime calls that component drawable's "submit" virtual-dispatch slot, pushing
the mesh/GPU-particle drawable into the engine's **normal (Diamond) drawable queue**. Ordering and the
transparent pass are then handled by the generic scene/draw manager, keyed by each drawable's
material/blend render-state — not by the effect code.

- **GPU-particle components** (`resource_id ≥ 10000`) are handed to the particle manager (§11);
  start/stop is driven by the component's active-frame window (enter window = start, leave window =
  stop/destroy). The particle manager owns their draw.
- **`SwordLightEffect`** (§17.6) is drawn in-pipeline as part of its owning geometry's draw — no
  separate effects pass.

Net: effects register their per-frame drawables into the shared queue; transparency/additive ordering
is the render layer's job via per-drawable blend state. Whether effects are forced into a
late/transparent bucket or sorted purely by material is **UNVERIFIED** — the global sort was not fully
walked this pass. This refines §8.2 step 12.

### 17.8 Confidence summary for this section

| Fact | Confidence |
|---|---|
| 104-byte element size; type tag at +0x04 selecting the subtype | CONFIRMED |
| Emitter dispatch (billboard / oriented-quad / mesh) and the `< 10000` CPU-mesh vs `≥ 10000` GPU split | CONFIRMED |
| Per-component active-frame window gating | CONFIRMED |
| Keyframe sampling: linear lerp for scale/colour/alpha/position/velocity, slerp for rotation, stepped sprite-frame index | CONFIRMED |
| UV scroll on a 5000 ms loop via a per-component flag | CONFIRMED |
| `JointXEffect` bone attachment by bone id; world translation + quaternion copied from the live pose | CONFIRMED |
| AnimCatalog weapon-hand slot meaning (which slot = which hand/weapon) | PLAUSIBLE |
| On-disk alpha stored inverted (`1 − value`) | CONFIRMED |
| Additive-vs-alpha blend state lives on the drawable material, not hard-coded by the effect code | PLAUSIBLE |
| `SwordLightEffect` head→tail start/end alpha gradient bytes | CONFIRMED |
| `SwordLightEffect` additive blend specifically | PLAUSIBLE |
| Effects submit drawables into the shared queue with no private depth sort | PLAUSIBLE→CONFIRMED |
| Global transparent-pass sort / late-bucket forcing | UNVERIFIED |
| `.xeff` on-disk → descriptor field mapping | UNVERIFIED (out of this lane; pending — see `formats/effects.md §A.16` note) |

---

## 16. Cross-References

- **Binary format specs:** `formats/effects.md` — the authoritative `.xeff` and `.eff` format specs, including the 32-byte `.xeff` header (§A.2), sub-effect block structure (§A.4), in-memory element layout (§A.5), the Campaign-5 runtime element struct (§A.16), and the `particleEmitter.eff` layout (§E).
- **Scale override table:** `formats/effects.md §D` (`effectscale.xdb`).
- **Terrain FX layer formats:** `formats/terrain_layers.md §Section 1` — authoritative for `.fx1`–`.fx7` byte layouts; do NOT look here for those.
- **Bone and skeleton layout:** `specs/skinning.md` — bone world-transform format required for §9.3 step 3 and §17.4.
- **Combat:** `specs/combat.md` — server-authoritative damage values; the effects system is presentation-only and has no authority over damage numbers.
- **Actor struct:** `structs/actor.md` — actor field layout referenced by `actor_sort_id` lookup and `visual_slot_byte`.
- **Skill record (cast chain, §15):** `structs/skill.md` and `formats/config_tables.md §2.8` — authoritative byte layout of the `skills.scr` record. They own the +1116 / +1136 / +1180 offsets that §15 resolves to `cast_motion_base` / `cast_effect_id` / `cast_sfx_id`.
- **Skill subsystem behaviour:** `specs/skills.md` — skill catalog load, cost/cooldown, and per-rank rows.
- **Cast network message (§15):** `opcodes.md` (opcode `5/52` `SmsgActorSkillAction`) and `packets/5-52_actor_skill_action.yaml` — the message that drives the cast-channel action codes (capture-unverified).
- **Cast-effect `.xeff` files:** `formats/effects.md §A.2` — the `.xeff` filename = decimal(effect_id) mapping the cast chain relies on. The FX1/FX2 terrain layer byte layout is **not** here; see `formats/terrain_layers.md §Section 1`.
- **Glossary:** see `Docs/RE/names.yaml`.
- **Provenance:** see `Docs/RE/journal.md`.
