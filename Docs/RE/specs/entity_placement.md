---
status: static-hypothesis
sample_verified: false
subsystems: [world_systems, actor_placement, terrain, static_object_placement]
ida_anchor: "(f61f66a9 build — differs from committed anchor 263bd994; see provenance note)"
evidence: [static-ida]
verification: "[static-hypothesis] — recovered 2026-06-26 from a session whose live IDB build differs
              from the pinned anchor; behaviors build-stable, exact offsets pending build reconciliation."
conflicts: refines terrain.md §5.4a triangle-selection rule; no contradiction with the plane-math result
---

# Spec: World Entity Placement — Actor Grounding and Static Object Placement

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> `Client.Domain` / `Client.Application` (actor position and movement logic) and by the Godot
> layer-05 for entity placement decisions.
> Citing engineers: `// spec: Docs/RE/specs/entity_placement.md`.
>
> **Provenance note [static-hypothesis, 2026-06-26].** All facts in this spec were recovered from
> a static analysis session whose live IDB build SHA (f61f66a9...) differs from the committed
> spec anchor (263bd994). Recovered behaviors are build-stable across client builds. Exact struct
> field offsets listed in §6 are from the f61f66a9 build and are pending reconciliation against
> the pinned anchor; treat them as [static-hypothesis] until a re-validator confirms them against
> the 263bd994 build.
>
> **Authoritative cross-references:** `Docs/RE/formats/terrain.md §5.4a` (per-triangle plane
> interpolation — CODE-CONFIRMED, CYCLE 12), `Docs/RE/formats/terrain.md §5.4` (block-1
> heights are direct world-space Y), `Docs/RE/formats/terrain.md §5.5` (block-2 normals are
> shading-only), `Docs/RE/formats/terrain.md §5.7` (block-4 governs UV flip / load-time quad
> split), `Docs/RE/specs/skinning.md §7` (Y-up world-placement / heading convention).

---

## 1. Overview

World entities — the local player, remote players, mobs, and NPCs — are placed vertically on the
terrain surface by a single public ground-height sampler. The sampler resolves an absolute
world-space Y coordinate from a world (X, Z) query. The entity's model origin is set to that Y with
no additional vertical bias; the entity stands upright in world space regardless of slope.

The seven load-bearing facts:

1. One public entry — `Terrain_SampleAtWorldXZ` — resolves ground Y from world (X, Z) and returns
   a scalar Y plus a boolean valid flag. It is the sole grounding function for all actor placement.
2. The height math is **per-triangle plane interpolation**, confirming `Docs/RE/formats/terrain.md §5.4a`:
   `Y = -(d + nx·X + nz·Z) / ny` for the plane of the containing triangle.
3. **Triangle selection (refinement of §5.4a implementation note):** the runtime sampler does NOT
   read the block-4 direction byte to pick the diagonal. It iterates the stored face list, applies
   an axis-aligned bounding-box reject followed by a 2D point-in-triangle containment test (XZ
   plane), and keeps the **maximum Y** over all containing triangles.
4. Terrain Y is **absolute world-space Y**. No per-cell base elevation or offset is added in the
   actor-placement path. The only additive term is a data-driven facing-Y offset applied exclusively
   to mounted or vehicle actors.
5. Sampled Y is written **directly** as the actor's position-Y; there is no pelvis or half-height
   offset. The model origin equals the ground contact point — feet on terrain.
6. The sampler returns **Y only** — no surface normal. Actors are oriented by a yaw heading
   quaternion and therefore stand **world-up (upright)** regardless of slope. Terrain normals
   (block-2) are shading-only and are never passed to the actor orientation path.
7. Y is **re-sampled on every position change**: spawn/teleport/explicit position-set, idle-motion
   entry, and per-frame for the local player after horizontal movement — except when the actor's
   motion-state field indicates an airborne or vehicle state (§3.3).

When entities float, sink, or fail to sit on the ground in the port, the prime suspects in order
of probability are: (3) wrong triangle pick — using a fixed diagonal or bilinear fallback instead
of containment + max-Y; (5) adding a half-height or pelvis offset; (4) adding or omitting a cell
elevation base; or sampling Y once at spawn rather than continuously per-frame.

---

## 2. The ground-height sampler — `Terrain_SampleAtWorldXZ`

[static-hypothesis — recovered 2026-06-26; behaviors build-stable, exact offsets pending reconciliation]

### 2.1 Entry-point behavior

`Terrain_SampleAtWorldXZ` accepts world (X, Z) and an output float pointer. Before any triangle
test it initialises the output to the sentinel value **-FLT_MAX** (the most-negative representable
IEEE 754 single, approximately −3.403×10³⁸).

It then:

- Converts world XZ to a `(mapX, mapZ)` cell coordinate using the formula in
  `Docs/RE/formats/terrain.md §2` (multiplier `1/1024 = 0.0009765625`, with the negative-axis
  correction for coordinates below zero).
- Resolves the currently-loaded cell via the 3×3 streaming ring.
- Selects one of two inner per-cell evaluators based on a terrain-manager mode flag:
  - Mode A (flag non-zero): evaluates the base triangle face-list only.
  - Mode B (flag zero): evaluates an upper/mass-terrain patch grid first, then the base face-list;
    takes the maximum Y between the two paths.
- **Boundary-seam handling:** when the query point falls exactly on a cell boundary (world coordinate
  exactly divisible by 1024), the coordinate is nudged inward by **1.1 world units** and the
  adjacent cell is also resolved and sampled, ensuring a seam-coincident query still finds a triangle.
- Returns true if and only if the output was updated away from the -FLT_MAX sentinel; returns false
  (leaving the output at -FLT_MAX) when no cell is loaded or no triangle contained the query point.

### 2.2 Per-cell evaluators

Both inner evaluators share a common structure:

- A small retry loop of up to two passes. If the first pass finds no containing triangle, the query
  point is nudged by a small XZ delta `(0.1, 0.08)` and the test is retried. This handles
  edge-degenerate or boundary cases.
- Conversion of world XZ to cell-local coordinates: `lx = X − cellOriginX`, `lz = Z − cellOriginZ`,
  where `cellOriginX = (mapX − 10000) × 1024` and `cellOriginZ = (mapZ − 10000) × 1024`. The cell
  origin is used for patch-index arithmetic only — it is **never added to Y**.
- A 16×16 patch-grid lookup: `pcol = clamp(floor(lx / 64), 0, 15)`, `prow = clamp(floor(lz / 64), 0, 15)`
  (scale factor `1/64 = 0.015625`). Each patch covers a 64 × 64 world-unit region (a 4×4 quad block
  from the 64×64 `.ted` quad grid).

**Base-terrain evaluator.** Iterates every triangle face record in the patch's face list. For each
face it calls the per-face evaluator (§2.3) and keeps the maximum returned Y. The face list is a
pre-built array of triangle descriptors populated by the `.ted` loader at cell-load time.

**Upper/mass-terrain evaluator.** Operates on a denser sub-quad patch structure. It selects a
sub-quad by comparing the query X and Z against stored vertex threshold values, determines the
containing triangle of the two triangles within that sub-quad using a 2D point-in-triangle test,
builds or retrieves the cached plane, solves for Y (identical to §2.3), and updates the running
maximum if greater.

### 2.3 Per-face plane evaluator

Each triangle face record holds an axis-aligned bounding box in XZ, three vertex positions,
a cached plane normal and constant, and a flat-Y cache marker. Evaluation:

1. **AABB reject.** If the query (X, Z) is outside the face's `[minX, maxX] × [minZ, maxZ]`, the
   face is skipped with no contribution.

2. **2D point-in-triangle test (XZ plane).** The standard 3-edge-sign test: compute the signed
   cross-product of the query point against each of the triangle's three edges in the XZ plane.
   The point is inside-or-on-edge when all three cross-products have consistent sign, or when any
   cross-product is exactly zero (on-edge case). Only inside-or-on-edge query points proceed.

3. **Plane Y.** The plane normal `(nx, ny, nz)` and constant `d` are lazily cached from the three
   stored vertex positions. Y is solved by the plane equation:

   ```
   Y = -(d + nx·X + nz·Z) / ny
   ```

   This is the standard solution of `nx·X + ny·Y + nz·Z + d = 0` for Y.
   The plane is stored as a unit-normal plane (the builder normalizes the cross-product normal before
   computing d). Normalization does not change the Y solution — it cancels in both numerator and
   denominator — but it is what the binary stores.

4. The returned Y is compared against the running maximum across all faces. If greater, the maximum
   is updated. The sampler's final output is this maximum-Y over all geometrically-containing faces
   in the patch.

**Face record layout** [static-hypothesis, offsets from f61f66a9 build, pending reconciliation]:

| Field index (f32 units from record base) | Name | Description |
|---:|---|---|
| 0–3 | XZ AABB | `minX, minZ, maxX, maxZ` — AABB reject |
| 4–6 | Vertex V0 | World-space `(x, y, z)` |
| 7–9 | Vertex V1 | World-space `(x, y, z)` |
| 10–12 | Vertex V2 | World-space `(x, y, z)` |
| 13–15 | Plane normal | `(nx, ny, nz)` — unit-length; lazily built from V0/V1/V2 |
| 16 | Plane constant | `d` satisfying `nx·x + ny·y + nz·z + d = 0` for any point on the plane |
| 17 | Flat-Y cache | Non-zero: a precomputed constant Y is used directly (degenerate / flat-face fast path); zero: the plane path §2.3 step 3 runs |

### 2.4 Plane builder

When the plane cache is uninitialised (detected by a negative sentinel in the stored ny component),
the builder constructs the unit-normal plane from the three face vertices. It computes two edge
vectors from the vertices, takes their cross product (`nx, ny, nz`), normalizes the result using
`Vector_NormalizeInPlaceReturnLength`, and sets `d = -dot(normal, V0)` using `Vec3_Dot`. If the
cross product has zero length (degenerate face), the builder returns failure and the face is skipped.

---

## 3. Y re-sampling triggers and state gating

[static-hypothesis — recovered 2026-06-26; behaviors build-stable, exact offsets pending reconciliation]

### 3.1 The actor grounding wrapper

A single internal grounding function drives all actor vertical placement from `Terrain_SampleAtWorldXZ`.
Given an actor, it:

- Reads the actor's current world X and Z from the actor's position fields (+0x40, +0x48).
- Calls `Terrain_SampleAtWorldXZ` and stores the returned ground Y in a scratch field on the actor
  (+0xA0).
- Conditionally applies a mount/vehicle Y offset on top of the sampled Y (§4.2).
- Writes the resolved Y directly to the actor's position-Y field (+0x44) and to a mirror field (+0xA4).
  No half-height, pelvis, or bounding-sphere-radius term is ever added.

If the actor's cell is not yet loaded, the sampler returns the -FLT_MAX sentinel and the actor's
position-Y is set to that sentinel until the streaming ring loads the cell. This is the expected
source of a one-frame Y-pop on initial spawn.

### 3.2 Re-sampling call sites

| Trigger | When Y is re-sampled |
|---|---|
| **Spawn / teleport / explicit position-set** | `SceneNode_SetPositionRecursive` writes XYZ then re-grounds Y; it recurses into attached child nodes (mount + rider), grounding each independently. |
| **Per-frame local-player update** | The local-player per-frame update integrates horizontal movement (applying the yaw heading quaternion to the movement delta), then calls the grounding wrapper. |
| **Idle-motion entry / refresh** | Several idle-motion entry and refresh paths re-ground Y when an actor enters or replays its idle cycle. |
| **Default-idle and motion-by-kind apply** | `Actor_PlayDefaultIdleCycle` and the visual idle-motion-apply paths re-ground Y on each (re)pose. |
| **Spawn via buff (e.g. mirror-clone buff)** | Buff-driven clone spawn paths ground each spawned clone independently. |

### 3.3 State gating — when the per-frame ground snap is skipped

The per-frame ground snap for the local player is **state-gated**. When the actor's motion-state
field (class+1420) holds a value in the set **{2, 3, 4, 17}**, the per-frame grounding call is
skipped entirely. These values correspond to airborne / jump / fall / vehicle states (the exact
semantic of each numeric value is an open question — see §7.1). In those states, vertical position
is owned by the corresponding jump/fall/vehicle logic, not by the terrain sampler.

State value 8 (an idle-snap variant) takes a distinct branch that still invokes the grounding wrapper.

---

## 4. Per-cell base elevation and mount offset

[static-hypothesis — recovered 2026-06-26; behaviors build-stable]

### 4.1 No per-cell base elevation

The face record vertices carry **absolute world-space Y coordinates**. Block-1 heightmap floats are
direct world Y, per `Docs/RE/formats/terrain.md §5.4`; they carry no implicit cell-origin bias. The
plane solve therefore already returns an absolute world Y. The cell-local origin used for patch-index
arithmetic is a pure XZ offset and is never added to the Y result. For a normal grounded actor, the
sampler output is used directly with no additional elevation term.

### 4.2 Mount/vehicle Y offset (mounted actors only)

The only additive Y term is applied when an actor has an attached mount or vehicle. In that case, a
data-driven Y offset — keyed by the actor's current facing index into the vehicle's Xdb record — is
added to the sampled ground Y so the rider's feet land correctly on top of the vehicle geometry.
Normal (unmounted) actors receive zero additive offset beyond the plain sampled Y.

---

## 5. Actor anchor and standing orientation

[static-hypothesis — recovered 2026-06-26; behaviors build-stable]

### 5.1 Anchor = feet at ground, no pelvis offset

The grounding wrapper writes the sampled (and optionally mount-offset) Y directly as the actor's
position-Y. No half-height, pelvis, center-of-mass, or bounding-sphere-radius term is present. The
**model origin is the ground contact point**: feet are at the terrain surface, and upward model
extent grows above the origin.

Port implication: if the ported skinned mesh has its origin at the pelvis or model center (a common
default for imported characters), every entity will appear to sink by approximately half its height.
The mesh origin must correspond to the feet contact point to match the original.

### 5.2 Orientation = world-up (upright), never terrain-normal aligned

Two independent facts from the binary establish this:

1. **`Terrain_SampleAtWorldXZ` returns a scalar Y only.** Its output parameter is a single float.
   Neither the entry point nor any of its internal evaluators produce or propagate a surface normal.
   The actor placement path therefore has no terrain normal available with which to tilt the actor.
2. **Actor orientation is a yaw quaternion.** Horizontal movement is produced by rotating a forward
   vector by the actor's heading quaternion (see `Quat_SetYawRotationFromAngle`). The root heading
   transform applies only a yaw rotation and an XZ translation offset; it applies no pitch or roll.
   This is consistent with `Docs/RE/specs/skinning.md §7` — Y-up world-placement / heading convention.

Actors therefore stand **upright in world space** regardless of terrain slope. Block-2 terrain
normals (`Docs/RE/formats/terrain.md §5.5`) are for diffuse **shading only** and are never passed
to the actor orientation path.

Port implication: tilting the character model to align with the terrain surface normal diverges from
the original, which keeps all actors strictly vertical.

---

## 6. Actor struct field reference

[static-hypothesis — offsets from f61f66a9 build session; require re-validation against 263bd994 before implementation]

The following fields on the actor object are referenced by the grounding path. All byte offsets are
from the actor object base.

| Byte offset | Field | Role in placement |
|---|---|---|
| +0x40 | World position X | XZ query input to the grounding wrapper |
| +0x44 | World position Y | Written by the grounding wrapper (ground-snapped, no offset) |
| +0x48 | World position Z | XZ query input to the grounding wrapper |
| +0x68, +0x6C, +0x70 | Position mirror X/Y/Z | Kept in sync with +0x40/+0x44/+0x48 |
| +0x84 | Facing quaternion (yaw heading) | Used for horizontal movement integration; no terrain-tilt component |
| +0xA0 | Ground Y scratch | Intermediate: raw sampler output before the mount offset is applied |
| +0xA4 | Resolved Y mirror | Mirror of the final resolved position-Y |
| +0xA8 | Facing index | Keys the vehicle facing-Y-offset table lookup |
| class+0x60 (value 0x17) | Actor kind flag | Identifies the actor as a vehicle actor type |
| class+0x3E0 | Vehicle Xdb id | Resolved to the vehicle data record used for the mount Y offset |
| class+0x704 | Has-mount flag | Non-zero when a mount is attached; gates the mount Y offset path |
| class+1420 | Motion-state field | Values {2,3,4,17} skip per-frame ground snap; value 8 = idle-snap variant |

---

## 7. Open questions and hand-off

The following items were identified during this recovery pass but not resolved.

### 7.1 Motion-state semantics

The state values {2, 3, 4, 17} that gate the per-frame ground snap are identified by value but not
semantically decoded — which specific value corresponds to jump, fall, flight, or vehicle is unknown.
The port needs this mapping to suspend grounding at the correct moments.
Hand to: `re-function-analyst` (actor state machine).

### 7.2 Mesh-build diagonal source

This pass examined the **runtime sampler path only**. Whether the `.ted` loader uses the block-4
direction byte to choose each quad's triangulation diagonal when it builds the face list at cell-load
time was not traced. Both the correct runtime sampler AND the correct load-time triangulation are
required for a faithful port. See `Docs/RE/formats/terrain.md §5.7` for the block-4 flag definition.
Hand to: `re-asset-format-analyst` (terrain loader — `.ted` mesh build pass).

### 7.3 Upper vs base evaluator selection

The terrain-manager mode flag that selects between base-only and upper+base evaluation is identified
but its correspondence to map area types (e.g. normal cells vs. mass/elevated terrain areas) was not
confirmed.
Hand to: `re-function-analyst` (terrain streaming / map-area state).

### 7.4 Build reconciliation

All struct field offsets in §6 are from the f61f66a9 build session and require re-validation against
the committed IDB anchor (263bd994) before being used in implementation. Request a re-validator pass
against the pinned build to confirm or correct each offset.

---

## 8. Static Decorative Object Placement (Mass Objects and FX Layers)

[static-hypothesis — recovered 2026-06-28; load-bearing items flagged `[debugger-confirm]`/`[capture-confirm]` for a G2 pass; see §8.9 for open items]

This section documents the runtime placement model for static world-dressing geometry: buildings and
props loaded from `.bud` files ("mass objects") and the animated decorative geometry layers loaded
from `.fx1`–`.fx7` files. On-disk formats for both families are documented separately —
`Docs/RE/formats/terrain_scene.md` covers the `.map` text descriptor and `.bud` building geometry;
`Docs/RE/formats/effects.md` covers the `.fx1`–`.fx7` per-cell layer formats. This section covers
only the **runtime placement model**: spatial indexing, culling, level-of-detail, draw batching, and
wind-sway animation. None of this material appears in the format specs or in §1–§7 above.

Actor spawn descriptors (`.arr` records) are the one exception to the no-transform convention
described in §8.1 — they carry a per-instance position and heading quaternion and are covered in
`Docs/RE/formats/npc_spawns.md` and `Docs/RE/structs/spawn_descriptor.md`, cross-referenced only here.

---

### 8.1 No per-instance transform — geometry is absolute world-space

Static dressing objects carry no per-instance world transformation matrix. Buildings, props, and FX
layer geometry store their vertex coordinates as **absolute world-space positions baked offline by the
map author**. Each object's position, rotation, and scale are embedded in the vertex stream at
content-creation time. The runtime never multiplies a per-object transformation matrix.

The draw path collects visible objects by concatenating their vertex data verbatim into a shared
upload buffer, rebasing only the index values by the accumulated vertex count. No matrix multiply
occurs anywhere in the gather-or-draw sequence. This is consistent with the terrain geometry
convention in `Docs/RE/formats/terrain.md` (world-space Y in the vertex stream) and with actor
grounding (§1–§5), where the runtime places actors on top of pre-baked terrain geometry.

---

### 8.2 Two static-object families

Both families share the world-space-vertex convention and are drawn as real indexed triangle lists
(D3DPT_TRIANGLELIST). There is no camera-facing billboard path for world dressing; billboards are
confined to sky, particles, and effect sprites.

**Mass objects** (from `.bud` via the `BUILDING` section of the `.map` descriptor): static triangle
meshes — buildings, large props, and non-animated decorative geometry. Each object is represented by
a 116-byte in-memory record (§8.4). Vertex format: XYZ position (12 bytes) + unit normal (12 bytes)
+ one UV set (8 bytes) = 32 bytes per vertex; FVF `0x112`.

**FX layers** (`FX1`–`FX7`, from `.fx1`–`.fx7`): per-cell animated decorative geometry (foliage,
banners, water surfaces, wind-swayed dressing). Each layer group carries a base vertex buffer (the
disk-loaded positions) and a separate working vertex buffer (a copy updated each animated frame by
the wind-sway oscillator, §8.7). FX vertex formats differ by layer; FX6 uses 32-byte verts, FX1
disk verts are 36 bytes. Per-layer vertex and file format details are in `Docs/RE/formats/effects.md`.

---

### 8.3 Spatial indexing — 16×16 grid per terrain cell

At cell-load time, `Map_BuildMassObjectGrid` partitions each terrain cell into a **16×16 = 256-square
grid** of **64 × 64 world-unit squares**. The grid origin XZ aligns to the cell descriptor origin.

Each mass object is binned into every grid square whose XZ extent overlaps the object's world-space
AABB. The overlap test is a four-comparison axis-aligned bounding-box check: a square spanning
`[X₀, X₀+64] × [Z₀, Z₀+64]` overlaps an object whose AABB spans `[minX, maxX] × [minZ, maxZ]`
when `X₀ ≤ maxX` and `minX ≤ X₀+64` and `Z₀ ≤ maxZ` and `minZ ≤ Z₀+64`. On overlap, the object
reference is appended to that square's growable list.

Two height arrays of 256 entries each track the per-square min and max Y extent of all objects binned
into each square; these are consumed by the terrain/ground system. The square index is `row × 16 + col`.

`Map_BuildMassObjectGrid` also remaps each object's section-local texture selector (1-based integer
from the `.bud` record) to a global texture-pool id using the `TEXTURES` table from the `.map`
`BUILDING` section, validating the remapped id against the pool size. Out-of-range selectors are
clamped to 1 with an error-log entry.

---

### 8.4 Mass-object in-memory record — 116 bytes

Built by `Bud_LoadBuildingBlob` from the `.bud` binary; AABB and draw-distance budget computed by
`BudObject_ComputeAABBAndBudget`; geometric-LOD buffer allocated by `BudObject_BuildLodBuffer` for
LOD-class objects. A load-time warning is emitted (non-fatal; object is retained) when a single
object exceeds 3 072 vertices.

| Byte offset | Type | Field | Notes |
|---:|---|---|---|
| +0x00 | u32 | `tex_id` | Disk: 1-based section-local selector; remapped in-place to global texture-pool id by `Map_BuildMassObjectGrid` |
| +0x04 | ptr | base vertex array | 32-byte verts, absolute world-space XYZ |
| +0x08 | u32 | vertex count | Non-fatal warn if > 3 072 |
| +0x0C | ptr | index array | u16 indices |
| +0x10 | u32 | index count | |
| +0x14 | f32 | AABB minX | |
| +0x18 | f32 | AABB minY | |
| +0x1C | f32 | AABB minZ | |
| +0x20 | f32 | AABB maxX | |
| +0x24 | f32 | AABB maxY | |
| +0x28 | f32 | AABB maxZ | |
| +0x30 | u32 | vertex byte size | `32 × vertex_count` |
| +0x34 | u8 | type byte | From the `.bud` per-object header |
| +0x35 | u8 | skip flag | Non-zero: object excluded from batching this frame |
| +0x3C | u8 | stage byte | 0 = near; 1 = far; set per-frame by cull logic (§8.5) |
| +0x3D | u8 | cull flag | 1 = beyond draw distance; 0 = visible |
| +0x40 | f32 | draw-distance² budget | Size-keyed; see §8.5 draw-distance table |
| +0x44 | u8 | batch-dirty flag | Batch-state bookkeeping |
| +0x46 | u8 | LOD class byte | From the per-texture LOD-class table; classes 10–14 and 20–24 trigger geometric-LOD buffer; else 1 |
| +0x48 | ptr | geometric-LOD vertex buffer | Allocated for LOD-class objects; starts as a copy of the base buffer |
| +0x54–+0x60 | f32×3 | LOD step / params | Filled only for LOD-class objects |
| +0x68–+0x70 | f32×3 | centroid | Midpoint of two corner vertices built by `BudObject_BuildLodBuffer`; world-space |

AABB degenerate-axis fix: when any AABB axis has `min == max`, `max` is increased by 2.0 world units
before the budget computation, ensuring a non-zero-volume bounding box.

---

### 8.5 Per-frame culling and near/far stage assignment

`BuildingTree_CullAndDraw` performs per-frame culling and near/far classification of all mass objects
in the visible grid squares. Camera eye XYZ is read from the terrain-manager view block.

For each non-skipped object in a visible grid square:

1. AABB centre is computed as the midpoint of `AABBmin` and `AABBmax`.
2. XZ distance-squared is computed between the camera position and the AABB centre (Y ignored).
3. If `distSq > budget (+0x40)`: cull flag (+0x3D) = 1; object is skipped this frame.
4. If `distSq ≤ budget`: cull flag = 0, and **stage byte (+0x3C) = 1 when `distSq > 0.7 × budget`,
   else 0**. Stage 0 = "near" (texture-batched draw); stage 1 = "far" (per-object projected-texture draw).

#### Size-keyed draw-distance budget

`BudObject_ComputeAABBAndBudget` assigns each object a squared-distance draw budget based on a size
metric derived from its world AABB:

```
size_metric = max( 0.6 × XZ_diagonal(AABB), AABB_height )
```

where `XZ_diagonal` is the distance from `AABBmin` to `AABBmax` and `AABB_height` is
`AABBmaxY − AABBminY`.

| Size metric `s` | Squared budget stored at +0x40 | Effective draw radius |
|---:|---:|---:|
| s < 8 | 90 000 | 300 world units |
| 8 ≤ s < 16 | 250 000 | 500 world units |
| 16 ≤ s < 32 | 1 000 000 | 1 000 world units |
| 32 ≤ s < 64 | 2 250 000 | 1 500 world units |
| s ≥ 64 | 3 240 000 | 1 800 world units |

Small props vanish at 300 world units; large buildings remain visible at up to 1 800 world units.

#### Per-texture LOD class

A per-texture LOD-class table inside the terrain manager (indexed by global texture-pool id) maps
each texture to a LOD class byte that controls whether `BudObject_BuildLodBuffer` allocates a
geometric-LOD vertex buffer. Classes 10–14 and 20–24 trigger buffer allocation; any other class
yields byte value 1 (no LOD buffer). The subdivision factor is `2 << (class − base)`:

| Class | Subdivision factor |
|---|---|
| 10 or 20 | 2 |
| 11 or 21 | 4 |
| 12 or 22 | 8 |
| 13 or 23 | 16 |
| 14 or 24 | 32 |

Which draw path substitutes the LOD buffer at runtime, and how the LOD-class table is populated, are
open questions (§8.9.1).

---

### 8.6 Texture-batched draw

#### Near-stage mass objects (stage 0)

Near objects are drawn by batching geometry per texture into a shared upload buffer, flushed to the
GPU via `DrawIndexedPrimitiveUP` (D3DPT_TRIANGLELIST) when a ceiling is reached or when the texture
changes.

Batch collection: for each non-culled, non-skipped, stage-0 object in the per-square linked list:
- Append the object's base vertex array verbatim (memcpy, `32 × vertex_count` bytes) to the upload
  vertex buffer.
- Append indices rebased by the running vertex count: `dst[i] = accumulated_vertex_offset + src[i]`.

Flush thresholds (mass objects): flush when adding the next object would bring the total index count
to ≥ 24 576 or total vertex count to ≥ 6 144. A single object exceeding these limits is drawn alone.

Flush action (`BuildingSection_DrawIndexed`): bind the object's texture (only when the texture changes
since the last flush — a texture change forces an immediate flush of the in-progress batch). Then
issue `DrawIndexedPrimitiveUP` with the accumulated buffer. Batching is therefore per-texture.

#### Far-stage mass objects (stage 1)

Far objects (stage byte = 1) are drawn one per `DrawIndexedPrimitiveUP` call (no batching), under a
texture-matrix render-state branch. This branch enables a projected-texture / texture-coordinate
generation mode on a secondary texture stage, applied as a per-object push/pop around each far draw.

#### FX layer draw

FX batching follows the same texture-batched `DrawIndexedPrimitiveUP` pattern with smaller flush
thresholds: index total ≥ 3 072 or vertex total ≥ 1 024 triggers a flush. Vertex emit per group is
either the animated working buffer (when the sway accumulator fired this frame, §8.7) or a static
memcpy of the cached working buffer (between recompute frames).

---

### 8.7 FX wind-sway animation

FX layer groups carry a wind-sway oscillator that displaces each group's entire geometry rigidly
along a fixed authored direction vector. The sway pattern is a triangle wave with a slightly
asymmetric velocity reversal. Animation is driven by an environment-time accumulator that throttles
the recompute rate.

#### Accumulator throttle

The FX6 layer driver accumulates an environment time delta each frame:

```
accumulator += per_frame_environment_time_delta
```

When `accumulator ≥ 50`: `timeScale = accumulator × 1×10⁻⁶`; the animated vertex emit runs for
each visible group; then `accumulator` resets to 0. When `accumulator < 50`, the cached working
buffer from the last animated frame is reused without recompute (static memcpy emit). Wind-sway is
therefore recomputed only a few times per second. `[debugger-confirm]` — the unit of the environment
time delta is an open item (§8.9.4).

#### Per-group animated emit

For each FX group when the accumulator fires:

1. **Advance phase:** `phase += rate × timeScale × phaseVelocity`
2. **Bounce (triangle wave):**
   - If `phase > upperBound`: in normal mode (`one_shot_flag == 0`) set `phaseVelocity = -96.0` and
     clamp `phase = upperBound`; in one-shot mode, reset `phase = 0`.
   - If `phase < lowerBound`: in normal mode set `phaseVelocity = +100.0` and clamp
     `phase = lowerBound`; in one-shot mode, reset `phase = 0`.
   - The asymmetric reversal values (−96.0 / +100.0) produce a slightly skewed triangle wave.
3. **Rigid translate:** `displacement = swayDirection × phase`. For each vertex in the group, only
   the XYZ position triple is modified: `working[i].xyz = base[i].xyz + displacement`. Normal and UV
   fields retain their load-time copy. The entire group translates as one rigid block — there is no
   per-vertex weighting, no rotation, and no vertex bending. A foliage patch, banner, or water sheet
   rocks uniformly along one direction.
4. The updated working buffer is memcopied into the draw batch.

#### FX group record layout (FX6 variant)

[static-hypothesis; FX1 in-memory layout differs — see `Docs/RE/formats/effects.md` for per-layer
on-disk format and vertex stride details]

| Byte offset | Type | Field | Notes |
|---:|---|---|---|
| +0x04–+0x0C | f32×3 | sway direction (vx, vy, vz) | Fixed world-space displacement axis; authored per group |
| +0x10 | i32 | phase rate | Per-frame phase increment multiplier |
| +0x18 | i32 | one-shot mode flag | Non-zero: phase resets to 0 at a bound rather than reversing velocity |
| +0x1C | u32 | vertex count | |
| +0x20 | u32 | index count | |
| +0x24 | ptr | base vertex array | Read-only source; 32-byte verts for FX6 |
| +0x2C | ptr | working vertex array | Animated destination; memcopied into draw batch |
| +0x30–+0x38 | f32×3 | AABB centre input | Used for cull-distance computation |
| +0x4C | u32 | working byte size | `32 × vertex_count` |
| +0x50 | f32 | phase (accumulated) | Current oscillation value |
| +0x54 | f32 | phase velocity / sign | Reversed at bounds; initial absolute value ≈ 96–100 |
| +0x58 | f32 | phase lower bound | Authored per group |
| +0x5C | f32 | phase upper bound | Authored per group |
| +0x60 | f32 | cull distance² | Per-group draw-distance budget |
| +0x64 | u8 | cull flag | 1 = beyond draw distance this frame |

---

### 8.8 Per-frame pass order (world opaque pass)

Within the world opaque render pass (`RenderPass_WorldTerrainAndBuildings`), static dressing draws
in this order:

1. Terrain streaming ring tick (cell stability check).
2. World opaque material state block: sets up a 3-texture-stage opaque material (fog enabled,
   alpha-test enabled, alpha-blend disabled, CCW cull mode, bilinear min/mag/mip filters). The exact
   D3DTSS and D3DSAMP enum values for a faithful Godot material port require debugger confirmation
   against the target GPU (§8.9.3). `[debugger-confirm]`
3. **Near mass objects:** `BuildingTree_CullAndDraw` — cull + near/far classification, then
   texture-batched draw of stage-0 objects.
4. **FX layers:** animate (throttled) + cull + texture-batched draw. FX6 and FX7 are driven directly
   in this pass branch; FX1–FX5 are driven from render-globals layer slots via their own draw
   routines.
5. **Far-blend decals** (`BuildingFar_CullAndDraw`): ground/terrain blend-cell quads — this is the
   ground-blend pass, not a mass-object placement concern; noted to avoid mis-scoping.
6. **Ground layers** (`TerrainGround_DrawAllLayers`).
7. **Far mass objects:** per-object draw of stage-1 objects under the texture-matrix /
   projected-texture render-state branch.

---

### 8.9 Open questions and hand-offs

#### 8.9.1 Geometric-LOD buffer consumption

`BudObject_BuildLodBuffer` allocates a LOD vertex buffer for LOD-class objects (classes 10–14 and
20–24), and the LOD-class byte table inside the terrain manager governs which objects qualify. The
near/far draw paths in this pass reference the base vertex buffer. Which draw path (or LOD-distance
gate) substitutes the LOD buffer at runtime, the subdivision/decimation fill algorithm, and how the
LOD-class byte table is populated (from `bgtexture.lst` kind bytes or another config) were not traced
in this pass.
Hand to: `re-asset-format-analyst` / `re-struct-analyst` (terrain manager LOD table).

#### 8.9.2 FX layer to content-type mapping

FX1–FX7 are seven distinct decorative layers with different vertex formats and distinct per-cell
corpus sizes. From static analysis alone it is not possible to determine which layer represents grass,
banners, water, or other content types — the sway direction and phase bounds are per-group authored
data. Confirmation requires inspection of rendered scenes or VFS samples. `[capture-confirm]`
Hand to: `re-asset-format-analyst`.

#### 8.9.3 Render-state enum values for the Godot port

The exact D3DTSS and D3DSAMP enum values for the world-opaque material block and the far-building
texture-matrix branch are partially recovered from static analysis. A debugger-confirmation pass
against the running client on the target GPU is needed to translate these faithfully into Godot
material and sampler node state. `[debugger-confirm]`
Hand to: material / render-state analyst (pilot the maintainer's live `?ext=dbg` session — never
`dbg_start`).

#### 8.9.4 Wind-sway accumulator unit

The environment time delta accumulated per frame (§8.7 throttle, threshold ≥ 50) controls the
recompute cadence. The unit of that delta (milliseconds, arbitrary ticks, or another scale) was not
confirmed by static analysis; it governs how frequently wind-sway recomputes in real time.
`[debugger-confirm]`
Hand to: `re-validator` (read the accumulator in motion during a live debugger session).
