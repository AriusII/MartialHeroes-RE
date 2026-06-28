---
verification: confirmed (static IDA, IDB SHA f61f66a9, 2026-06-28; no debugger pass — see open items for [debugger-confirm] fields)
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [world_systems, render_pipeline]
conflicts: corrects mesh.md §"In-memory BudObject" — the upper half (+70..+115) is a wind-sway animation block, NOT geometric LOD; renames lod_class/lod_vertex_buffer/lod_step/lod_factor/aux fields to their true roles; corrects the +20 AABB-seed reading; adds missing fields +44/+53/+68/+69/+76/+80/+84/+88/+100. The mesh.md §Known Unknowns items "LOD/decimated buffer construction" and "lod_class source table" are resolved by this spec.
deep_cartography_pass: 2026-06-29 — centroid +104..+112 corrected from AABB midpoint to vertex-buffer normal-slot formula; +53/+60/+68 reclassified as dead flags (no reader in full foliage/mass-object cluster of build f61f66a9); sway oscillator confirmed as linear triangle-wave ping-pong (no trig); family-2 sway amp confirmed DIVIDE; dispatch table conditions corrected for B/C entries; weather/time gate and env-accumulator added to §Draw dispatch; open items updated accordingly.
---

# Structs: BudObject — runtime mass-object / foliage instance (116 bytes)

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> The on-disk `.bud` file format is in `formats/mesh.md §.bud`. Citing engineers:
> `// spec: Docs/RE/structs/bud_object.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow + operand analysis,
> corroborated at multiple call sites; `[unverified]` = field exists (ctor/clear evidence) but its
> role was not traced to a reader in the examined cluster; `[debugger-confirm]` = static hypothesis
> requiring a live debugger pass to settle the exact formula or value. `[sample-verified]` does not
> apply here — this is in-memory heap layout, not a packed-file format.

---

## Summary

`BudObject` is the **116-byte runtime instance** of one mass-object sub-mesh loaded from a `.bud`
container. It is a **plain C struct with no virtual table and no RTTI** — offset 0 is a plain
`int` (`tex_id`, constructor default −1), not a vtable pointer. Polymorphic draw behaviour is
achieved by the `anim_class` byte (+70) selecting a free draw function, not by virtual dispatch.

The `.bud` loader allocates an array of `object_count` such structs (with a 4-byte count prefix
ahead of element 0), default-constructs each, then reads disk data into them. `.bud` stores
**both static buildings and animated foliage**; `BudObject` is the shared runtime record for both
categories.

**Critical correction to `formats/mesh.md`.** The upper half of the struct (offsets +70..+115),
previously labelled a geometric LOD block in the committed mesh.md, is in fact a **per-frame
procedural wind-sway vertex-animation block**:

- The field named `lod_vertex_buffer` (+72) is a **full-size** scratch vertex buffer
  (`32 × vertex_count` bytes, exact copy of the source), rebuilt every frame with per-vertex sway
  displacement applied. No vertex reduction or decimation occurs anywhere.
- The field named `lod_class` (+70) is an **animation-style class byte** that selects the sway
  variant, not a geometric LOD level. Values 10–14 and 20–24 each activate a distinct sway path;
  all other values (e.g. 1) leave the object static with no scratch buffer.
- The sway system is **sound-reactive** (the sway animators query `AmbientSoundManager` each frame
  to scale oscillation intensity) and **weather/time-gated** (the foliage master accumulates an
  environment delta-time counter and suppresses sway below a threshold, and disables it entirely in
  one weather state).

The field at +60 (previously named `far_lod_flag`, then `near_far_flag`) is now **`flag_60`** —
reclassified as role-unverified because no read-site for this byte was found in the complete
foliage/mass-object cluster of build f61f66a9. A separate static-building draw path may be the
consumer (see §Full offset table and §Open items).

---

## Object ownership and lifetime

- **Allocation.** The `.bud` loader allocates a contiguous block of `object_count × 116 + 4`
  bytes: the first 4 bytes hold `object_count`, and element 0 begins at offset +4. The owning
  loader record stores the array base and the count as its own fields.
- **Construction.** `BudObject_Ctor` default-constructs each element, setting numeric fields to
  their initial values (see offset table). The matching `BudObject_Clear` frees the three heap
  buffers (`vertex_buffer` at +4, `index_buffer` at +12, and `anim_vertex_buffer` at +72) and
  re-zeroes most fields; it doubles as a reset/clear path and as a destructor.
- **Per-element heap buffers.** During load, `vertex_buffer` (+4) and `index_buffer` (+12) are
  each separately allocated heap blocks (32-byte vertices; u16 indices), each with a 4-byte count
  prefix.
- **Grid-build pass.** After loading, a post-load grid-build step (a) remaps `tex_id` (+0) from
  the `.bud` local 1-based index to a global `bgtexture.lst` slot, (b) bins each object into a
  16×16 grid of 64-unit cells by its world-space AABB (also updating per-grid-cell min/max Y
  height), and (c) calls `BudObject_InitSwayBuffer` per object to set `anim_class` (+70) and
  allocate the +72 scratch buffer if the class falls in a sway range.
- **Render-list bucketing.** `Cells_BucketByCategory` then buckets the loaded grid-cell nodes
  (not individual objects) into four singly-linked render lists by a per-cell category byte: value
  2 → building draw list; values 10–14 → sway-family-A draw list; values 20–24 → sway-family-B
  draw list; all other values → the default static draw list. The value domains are the same as
  those of `anim_class` (+70).
- **Lifetime.** Objects live for the duration of the loaded cell set and are cleared and freed on
  cell unload.

---

## Full offset table (all 116 bytes)

Byte offsets are relative to the start of the `BudObject` instance. Constructor defaults are the
values written by `BudObject_Ctor`; where `BudObject_Clear` differs from the constructor, both
are noted.

| Byte offset | Size | Type | Field | Notes / default | Confidence |
|------------:|-----:|------|-------|-----------------|------------|
| +0  | 4 | int  | `tex_id` | On load, the `.bud` local 1-based texture index; overwritten at grid-build with the global `bgtexture.lst` slot. **Offset 0 is a plain `int`, NOT a vtable pointer.** Default: −1. | confirmed |
| +4  | 4 | ptr  | `vertex_buffer` | Heap pointer to the source 32-byte vertex array (`pos 12 B + normal 12 B + uv 8 B`). Default: null. | confirmed |
| +8  | 4 | u32  | `vertex_count` | Number of vertices in this object. Default: 0. | confirmed |
| +12 | 4 | ptr  | `index_buffer` | Heap pointer to the u16 triangle-list index array. Default: null. | confirmed |
| +16 | 4 | u32  | `index_count` | Number of u16 indices (= 3 × triangle count). Default: 0. | confirmed |
| +20 | 4 | f32  | `aabb_min.x` | World-space AABB minimum X. Computed by `BudObject_ComputeAABBAndBudget`. The constructor seeds the entire AABB block via a shared 6-float helper as an **inverted-AABB seed** (min = +1 per axis, max = −1 per axis) — NOT a pick-ray segment as earlier noted in mesh.md. Seed: +1.0. | confirmed |
| +24 | 4 | f32  | `aabb_min.y` | AABB minimum Y. Seed: +1.0. | confirmed |
| +28 | 4 | f32  | `aabb_min.z` | AABB minimum Z. Seed: +1.0. | confirmed |
| +32 | 4 | f32  | `aabb_max.x` | AABB maximum X. Degenerate-axis correction: if min == max on any axis, max is increased by 2.0. Seed: −1.0. | confirmed |
| +36 | 4 | f32  | `aabb_max.y` | AABB maximum Y. Seed: −1.0. | confirmed |
| +40 | 4 | f32  | `aabb_max.z` | AABB maximum Z. Seed: −1.0. | confirmed |
| +44 | 4 | u32  | `reserved_44` | Zeroed by both constructor and clear; no read-site observed in the examined cluster. Reserved/unused scratch. Default: 0. | unverified role |
| +48 | 4 | u32  | `vertex_byte_size` | Cached byte count of the full vertex buffer, equal to `32 × vertex_count`. Set by `BudObject_ComputeAABBAndBudget`; used by all per-frame draw paths as the memcpy size. Default: 0 (set at load). | confirmed |
| +52 | 1 | u8   | `type` | Object type/category tag read from disk. The loader does not branch on it; role is unconfirmed (possible collision class or sub-mesh kind tag consumed by another system). Default: from disk. | confirmed read; unverified semantics |
| +53 | 1 | u8   | `flag_53` | Zeroed by the constructor; no read-site or non-zero write-site identified in the complete examined cluster (loaders, grid, sway-init, all three sway animators, all four draw batchers, draw-master, frame-cull, GPU-draw, copy paths, height-query, bucketer). The draw batchers gate skip behaviour on `culled_flag` (+61) only. The `skip`-role attribution carried in prior revisions is unverified for build f61f66a9; reclassified pending identification of a reader. Default: 0. | unverified role |
| +54 | 2 | —    | *(padding)* | Alignment gap to the next dword boundary. | confirmed |
| +56 | 4 | f32  | `dist_sq` | Per-frame cached squared XZ distance from the AABB midpoint to the camera position. Default: 0. | confirmed |
| +60 | 1 | u8   | `flag_60` | Zeroed by the constructor; no read-site or non-zero write-site identified in the complete foliage/mass-object cluster. The frame-cull path (draw-master) writes `dist_sq` (+56) and `culled_flag` (+61) only — it does not write this byte. The `near_far`-partition role (0.7 × budget split) described in prior revisions is unverified for build f61f66a9 in the foliage cluster; a separate static-building draw path outside this cluster may be the consumer. Previously named `far_lod_flag` in mesh.md, then `near_far_flag`. Default: 0. | unverified role |
| +61 | 1 | u8   | `culled_flag` | Per-frame: set when `dist_sq > budget`; the object is skipped entirely that frame. Default: 0. | confirmed |
| +62 | 2 | —    | *(padding)* | Alignment gap to the next dword boundary. | confirmed |
| +64 | 4 | f32  | `budget` | Squared draw-distance cull threshold, tiered by AABB extent (see §AABB and draw-distance budget). Default (constructor): 1,000,000. | confirmed |
| +68 | 1 | u8   | `flag_68` | Zeroed by the constructor; no read-site or non-zero write-site identified in the complete examined cluster. No deferred-registration path was observed touching this field in the loaders, grid, sway-init, animators, batchers, draw-master, or cull paths. The `register`-role attribution carried in prior revisions is unverified for build f61f66a9; reclassified pending identification of a reader. Default: 0. | unverified role |
| +69 | 1 | u8   | `flag_69` | Byte flag zeroed by the constructor; paired with +68. Not read in the examined build/foliage draw cluster; role unconfirmed. Default: 0. | unverified role |
| +70 | 1 | u8   | `anim_class` | **Animation-style class byte (NOT geometric LOD).** Assigned by `BudObject_InitSwayBuffer` from a `TerrainManager` per-texture table indexed by the global `tex_id`. Values 10–14 → sway family 1; 20–24 → sway family 2; all others (e.g. 1) → static, no sway buffer. Previously named `lod_class` in mesh.md. Default: set at grid-build (not written by constructor). | confirmed |
| +71 | 1 | —    | *(padding)* | Alignment gap to the next dword boundary. | confirmed |
| +72 | 4 | ptr  | `anim_vertex_buffer` | **Full-size scratch vertex buffer** (`32 × vertex_count` bytes), allocated by `BudObject_InitSwayBuffer` when `anim_class` is in the range 10–14 or 20–24. Rebuilt every frame with sway displacement applied. NOT a decimated/LOD buffer. Previously named `lod_vertex_buffer` in mesh.md. Default: null. | confirmed |
| +76 | 4 | f32  | `sway_phase` | Wind-sway oscillation phase accumulator; advanced each frame by the sway animators. Default (constructor and clear): 0.0. | confirmed |
| +80 | 4 | f32  | `sway_dir` | Sway direction/velocity sign; toggles between +100.0 and −100.0 as the phase reaches its bounds. Default (constructor and clear): +100.0. | confirmed |
| +84 | 4 | f32  | `sway_min` | Lower phase bound (negative); computed as `−(sway_amp × 0.1)`. Reset to 0.0 at the start of the sway-init path, then driven by the animators. Effective initial value: 0.0 (see note below). | confirmed |
| +88 | 4 | f32  | `sway_max` | Upper phase bound (positive); computed as `sway_amp × 0.1`. Reset to 0.0 at sway-init, then driven by the animators. Effective initial value: 0.0. | confirmed |
| +92 | 4 | f32  | `sway_amp` | Sway amplitude and step parameter. **A value of 0.0 means "no animation"** — all three sway animators skip the vertex displacement and fall back to a plain copy when this is 0.0. Computed by `BudObject_InitSwayBuffer` from the object's vertical AABB extent. Previously named `lod_step` in mesh.md. Default (constructor and clear): 0.0. | confirmed |
| +96 | 4 | f32  | `sway_vfactor` | Sway vertical multiplier applied to the Y displacement component. Class-dependent (example values: 0.3 or 0.5). Set by `BudObject_InitSwayBuffer`; not written by the constructor. Previously named `lod_factor` in mesh.md. | confirmed |
| +100 | 4 | f32  | `sway_intensity` | Sway intensity / damping factor. Updated each frame: grows toward the `AmbientSoundManager`-driven target; decays otherwise (toward 1.5 or toward 2.0, depending on variant). **Constructor default: 1.0; clear/reset default: 1.5.** Previously noted only as "aux 1.0" in mesh.md. | confirmed |
| +104 | 4 | f32  | `centroid.x` | **Corrected from prior revision.** Computed by `BudObject_InitSwayBuffer` as `0.5 × (vtx[0].normal.x + vtx[2].normal.x)` — half the sum of the X component of the 12-byte normal-slot field (bytes 12..23 of the 32-byte per-vertex record) for vertices 0 and 2 of the raw source vertex buffer. This is NOT 0.5 × AABB midpoint X as stated in earlier revisions. Used by `BudObject_SwayAnimate_A` as a horizontal sway-amplitude multiplier. The semantic interpretation of these normal-slot vectors in foliage meshes is an open question (see §Open items item 5). Not written by the constructor. | confirmed |
| +108 | 4 | f32  | `centroid.y` | `0.5 × (vtx[0].normal.y + vtx[2].normal.y)` — Y component of the same normal-slot pair computation. Not directly used by sway animators in the examined paths. Not written by the constructor. | confirmed |
| +112 | 4 | f32  | `centroid.z` | `0.5 × (vtx[0].normal.z + vtx[2].normal.z)` — Z component of the same normal-slot pair computation. Used by `BudObject_SwayAnimate_A` as a horizontal sway-amplitude multiplier alongside `centroid.x`. Not written by the constructor. | confirmed |

**Total: 116 bytes.** Padding bytes at +54–+55, +62–+63, and +71. No trailing padding; the struct ends exactly at offset +115.

> **Note on `sway_min` / `sway_max` (+84/+88).** These fields are not explicitly written by
> `BudObject_Ctor`; the allocated block covers them and in practice they are zero at construction.
> The sway-init path resets them to 0.0 before computing their driven values. Treat their effective
> initial value as 0.0.

> **Note on `sway_intensity` (+100) dual default.** The constructor writes 1.0; the clear/reset
> path writes 1.5. Both are valid resets at different lifecycle points. A port should apply 1.0 on
> construction and 1.5 on reset/recycle.

### Padding summary

| Bytes | Reason |
|-------|--------|
| +54–+55 | Alignment after `type` (+52) and `flag_53` (+53) |
| +62–+63 | Alignment after `flag_60` (+60) and `culled_flag` (+61) |
| +71 | Alignment after `anim_class` (+70) |

---

## AABB and draw-distance budget

`BudObject_ComputeAABBAndBudget` walks all vertices in `vertex_buffer` to compute the world-space
AABB at +20..+43, then assigns `budget` (+64) by a tiered rule based on
`max(0.6 × diagonal, height)`, where `height = aabb_max.y − aabb_min.y` and `diagonal` is the
XZ diagonal length.

| Size threshold (metric value) | Budget (squared draw distance) |
|------------------------------:|-------------------------------:|
| < 8   | 90,000 |
| < 16  | 250,000 |
| < 32  | 1,000,000 |
| < 64  | 2,250,000 |
| ≥ 64  | 3,240,000 |

`vertex_byte_size` (+48) is set to `32 × vertex_count` by the same function and cached for all
per-frame draw memcpy calls.

---

## Wind-sway animation system

`BudObject_InitSwayBuffer` is called once per object during the post-load grid-build pass. It:

1. Queries a per-texture animation-class byte from a `TerrainManager` table indexed by the
   object's global `tex_id` and writes the result into `anim_class` (+70).
2. If the class falls in the range 10–14 or 20–24, allocates a full-size scratch buffer
   (`32 × vertex_count` bytes) and stores its pointer in `anim_vertex_buffer` (+72). Static
   objects (class outside both ranges) receive no scratch buffer and `anim_vertex_buffer` stays null.
3. Computes `sway_amp` (+92) from the object's vertical AABB extent, and `sway_vfactor` (+96)
   from the class value. Resets `sway_min` and `sway_max` to 0.0.
4. Computes `centroid` (+104..+112) as `0.5 × (vtx[0].normal + vtx[2].normal)` componentwise
   (half-sum of the normal-slot vectors of vertices 0 and 2 of the raw source vertex buffer) — NOT
   the AABB midpoint (corrected in deep-cartography pass, 2026-06-29).

Three sway-animation variants exist — `BudObject_SwayAnimate_A`, `BudObject_SwayAnimate_B`, and
`BudObject_SwayAnimate_C` — each selected by the draw dispatcher (see §Draw dispatch). Every
variant follows the same structural pattern:

1. Copies `vertex_buffer` into `anim_vertex_buffer` (using `vertex_byte_size` bytes).
2. Applies a per-vertex displacement driven by `sway_phase`, `sway_dir`, `sway_amp`,
   `sway_vfactor`, `sway_intensity`, and `centroid`.
3. Advances `sway_phase` and toggles `sway_dir` when the phase reaches `sway_min` or `sway_max`.
4. Scales `sway_intensity` toward the `AmbientSoundManager`-reported target each frame.
5. Early-exits to a plain copy (no displacement) when `sway_amp` is 0.0.

**Oscillator type — static-confirmed (deep-cartography pass, 2026-06-29).** The phase oscillator
is a **linear triangle-wave ping-pong** — there is no trigonometric call in any of the three
variants. `sway_phase` is advanced each call by `tick × a3 × sway_dir` (variants A and B) or
a scaled form (variant C), where `tick` is a per-family global counter that increments each call
and wraps to 0 after 4 (A/B) or 5 (C) steps, and `a3` is the env-time-gate scalar supplied by
`Foliage_DrawMaster` (accumulated environment time × 1e-6). When the phase crosses `sway_min` or
`sway_max`, the bound is recomputed as `±(tick × sway_amp × 0.1)` (A/B) or `±(tick × 0.05 ×
sway_amp)` (C), and `sway_dir` flips between +100.0 and −100.0. The initial bounds (`sway_min`,
`sway_max`) are both 0.0; they are driven open by the first animator call.

**Per-variant displacement (static-confirmed):**

- **`BudObject_SwayAnimate_A` (sound-reactive; sway family 1, primary weather state):** displaces
  the **X** coordinate of the first few vertices by `centroid.x × sway_phase` and the **Z**
  coordinate by `centroid.z × sway_phase`; displaces **Y** by a sound-modulated term that
  combines `sway_intensity`, `sway_amp`, the `AmbientSoundManager`-reported signed distance
  (`sndDir`) and source value (`sndVal`). An additional vertex is included on the
  `vertex_count == 8` path.
- **`BudObject_SwayAnimate_B` (sound-independent; sway family 1, alternate weather state):**
  displaces the **Z** coordinate of the first few vertices by `vtx[0].normal.y × sway_phase`
  (the Y component of the normal-slot vector of vertex 0 used as a scalar multiplier). No
  `AmbientSoundManager` query. A `vertex_count == 8` path includes a fourth vertex.
- **`BudObject_SwayAnimate_C` (sound-reactive; sway family 2):** loops over **all** `vertex_count`
  vertices, displacing each by the normal-slot vector of vertex 0 scaled by phase:
  `Δx = vtx[0].normal.x × phase`, `Δy = vtx[0].normal.y × phase × 0.7`,
  `Δz = vtx[0].normal.z × phase × 1.5`. A whole-mesh normal-driven sway.

**Sound reactivity (static-confirmed).** Variants A and C query `AmbientSoundManager` each frame.
The manager holds an array of sound sources (24-byte records: position and value). The query finds
the nearest source within ±100 world units of the object's XZ position. On a hit, `sway_intensity`
grows toward the source's value; on a miss it decays: variant A targets 1.5 (decay step 0.05),
variant C targets 2.0 (decay step 0.1). Variant B performs no sound query.

**Residual debugger need (tightened).** The oscillator structure, per-variant displacement axes,
and sound modulation path are now fully static-confirmed. The only remaining unknowns are three
**runtime scalar values** to reproduce exact swayed vertex positions at a given frame: (a) the
gate scalar `a3` (accumulated env-time × 1e-6) at the sampled frame; (b) the live
`AmbientSoundManager` source-array contents (`sndDir`, `sndVal`) for the relevant foliage cell;
(c) the global per-family tick counter state. With those three values, the output vertex set is
fully determined by the static formula. Confirm by breakpointing one sway animator on an active
foliage cell and diffing source vs `anim_vertex_buffer`. This is a fidelity validation item, not
a structural blocker.

**Weather and time gating.** `Foliage_DrawMaster` accumulates an environment delta-time counter
and invokes the sway animators only when the counter reaches a threshold. It disables sway entirely
in one specific weather state, making foliage appear still during that condition.

---

## Draw dispatch — no vtable, no RTTI

`BudObject` has no virtual table and no RTTI. `BudObject_Ctor` never writes a vtable pointer at
offset 0 — that offset holds `tex_id`, a plain `int`. Polymorphic draw behaviour is selected by the
`anim_class` byte (+70) and the current weather state, dispatching to one of four free functions:

| Draw function | Animation function invoked | Condition |
|---------------|---------------------------|-----------|
| `Foliage_StaticDraw` | `BudObject_CopyVerts` (plain copy) | `anim_class` outside 10–14 and 20–24, or `sway_amp` == 0.0 |
| `Foliage_SwayDraw_A` | `BudObject_SwayAnimate_A` | Sway family 1 (class 10–14), weather state 1 |
| `Foliage_SwayDraw_B` | `BudObject_SwayAnimate_C` | Sway family 2 (class 20–24), weather state 1 |
| `Foliage_SwayDraw_C` | `BudObject_SwayAnimate_B` | Sway family 1 (class 10–14), any weather state other than 1 or 3 |

> **Note on prior table:** earlier revisions had the conditions for `Foliage_SwayDraw_B` and
> `Foliage_SwayDraw_C` swapped. Corrected by the deep-cartography pass (2026-06-29) based on
> static analysis of the weather-state routing in `Foliage_DrawMaster`.

`Foliage_DrawMaster` orchestrates the sway lists: it applies the environment delta-time gate and
weather-state check, then walks the sway-family-1 and sway-family-2 render lists, invoking the
appropriate draw function for each object node. When weather state is non-1 non-3, family-2 objects
receive a plain copy (no sway) rather than a sway animator.

**Weather-state routing and env-time gate (static-confirmed).** `Foliage_DrawMaster` applies two
gates before invoking any sway animator each frame:

1. An environment delta-time accumulator is advanced by the engine's per-frame env-time delta.
   Sway animators are invoked only when this accumulator reaches a threshold of **50** (integer
   compare). The time-scalar `a3` supplied to the animators equals the accumulated value × **1e-6**.
   The accumulator resets to 0 after each sway pass. Between sway passes, the per-frame XZ
   distance-and-cull pass still runs for all objects on both lists.
2. The weather/time-of-day state is read from field **+24** of the environment singleton:
   - **State 3:** sway is frozen — the accumulator is not advanced; no sway animator runs that
     frame.
   - **State 1:** family-1 objects (class 10–14) run `BudObject_SwayAnimate_A`; family-2 objects
     (class 20–24) run `BudObject_SwayAnimate_C`.
   - **Any other non-3 state:** family-1 objects run `BudObject_SwayAnimate_B`; family-2 objects
     receive a plain copy (no oscillation that frame).

The integer-to-weather-condition mapping for the state field is not yet resolved from static
analysis (see §Open items item 4).

---

## Open items

1. **`type` byte (+52) — semantics `[unverified]`.** Read from disk and stored in memory; no
   reader of this field was observed in the examined building/foliage cluster. Likely a content or
   collision-class tag consumed by a separate system (collision, sub-mesh classification). Tracing
   non-cluster readers of +52 would resolve this.
2. **Dead flags and unverified fields — role `[unverified]`.** The following fields are zeroed by
   the constructor and have no confirmed read-site or non-zero write-site in the complete examined
   cluster: `flag_53` (+53), `flag_60` (+60), `flag_68` (+68), `reserved_44` (+44), `flag_69`
   (+69). The skip/near-far-partition/register roles attributed to +53/+60/+68 in prior revisions
   are not present in the foliage/mass-object cluster of build f61f66a9. A reader may exist in an
   unexamined static-building draw path. Confirm by tracing readers of these fields binary-wide.
3. **Runtime sway scalars — `[debugger-confirm]` (tightened).** The sway oscillator and per-variant
   displacement formulas are now fully static-confirmed (see §Wind-sway animation system). The only
   remaining debugger need is three runtime numeric values: (a) the accumulated env-time gate scalar
   `a3` at the sampled frame; (b) live `AmbientSoundManager` source-array contents (`sndDir`,
   `sndVal`) for the relevant foliage cell; (c) the global per-family tick counter state. This is a
   fidelity validation item, not a structural blocker.
4. **Weather state integer mapping — `[debugger-confirm]`.** The weather/time-of-day state field
   (+24 of the environment singleton) has three behaviorally distinct values (3 = freeze sway;
   1 = primary sway variant set; all other non-3 = alternate set). The mapping of each integer to a
   named in-game weather or time-of-day condition requires a live read of the field under known
   game weather. (Down from "gate field unidentified" to "gate field + three branch values located;
   semantic label per value needed".)
5. **`centroid` / normal-slot semantics.** The fields at +104..+112 are computed as half the sum of
   the normal-slot vectors of vertices 0 and 2 of the raw source vertex buffer, then used as
   horizontal sway-amplitude multipliers by `BudObject_SwayAnimate_A`. Whether the 12-byte "normal"
   field of sway foliage meshes holds a conventional shading normal or a repurposed per-vertex sway
   weight/anchor vector cannot be determined from loader code alone. Inspect a real `.bud` foliage
   sample to check whether these bytes are unit-length normals or sway-specific data.
6. **No VFS sample verification.** All facts are derived from static decompilation; no `.bud` file
   has been extracted and walked for this struct pass. The on-disk format is in
   `formats/mesh.md §.bud` (also sample-unverified). Extracting a `.bud` from a foliage-bearing
   map cell and tracing the sway path live would provide two-witness confirmation.

---

## Cross-references

- On-disk `.bud` file format and texture binding chain: `formats/mesh.md §.bud`.
- Terrain cell streaming and the grid-build pass: `specs/terrain-streaming.md`.
- TerrainManager per-texture table (source of `anim_class`): `structs/terrain-manager.md`.
- Mass-object grid cell-slot identities: `structs/terrain-manager.md §The 9 per-cell render sub-manager slots`.
- Render pipeline context: `specs/render_pipeline.md`.
- Canonical names: `Docs/RE/names.yaml` (proposed additions: `BudObject` struct; fields `tex_id`,
  `vertex_buffer`, `vertex_count`, `index_buffer`, `index_count`, `aabb_min`, `aabb_max`,
  `vertex_byte_size`, `type`, `flag_53`, `dist_sq`, `flag_60`, `culled_flag`, `budget`,
  `flag_68`, `anim_class`, `anim_vertex_buffer`, `sway_phase`, `sway_dir`, `sway_min`,
  `sway_max`, `sway_amp`, `sway_vfactor`, `sway_intensity`, `centroid`; functions
  `BudObject_Ctor`, `BudObject_Clear`, `BudObject_ComputeAABBAndBudget`,
  `BudObject_InitSwayBuffer` (rename from prior `BudObject_BuildLodBuffer`),
  `BudObject_SwayAnimate_A`, `BudObject_SwayAnimate_B`, `BudObject_SwayAnimate_C`,
  `BudObject_CopyVerts`, `Foliage_DrawMaster`, `Foliage_StaticDraw`,
  `Foliage_SwayDraw_A`, `Foliage_SwayDraw_B`, `Foliage_SwayDraw_C`,
  `Cells_BucketByCategory`, `MassObjectGrid_SampleCellMaxHeight`,
  `MassObject_SamplePlaneHeightXZ`).
- Provenance: `Docs/RE/journal.md`.
