---
status: static-hypothesis
sample_verified: false
subsystems: [world_systems, actor_placement, terrain]
ida_anchor: "(f61f66a9 build — differs from committed anchor 263bd994; see provenance note)"
evidence: [static-ida]
verification: "[static-hypothesis] — recovered 2026-06-26 from a session whose live IDB build differs
              from the pinned anchor; behaviors build-stable, exact offsets pending build reconciliation."
conflicts: refines terrain.md §5.4a triangle-selection rule; no contradiction with the plane-math result
---

# Spec: World-Entity Vertical Placement (Terrain Grounding)

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
