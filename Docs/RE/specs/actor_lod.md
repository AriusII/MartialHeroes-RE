# Spec: actor_lod — actor level-of-detail and culling subsystem

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Covers the draw-distance cull, frustum bounding-sphere cull,
> off-screen skinning skip, and skinning-quality LOD applied every frame to all in-world
> actors in `doida.exe`. Cross-ref: `Docs/RE/specs/skinning.md` (deform-mode semantics),
> `Docs/RE/specs/render_pipeline.md` (scene-graph traversal and cull-bin context),
> `Docs/RE/specs/character_rendering.md` (actor draw call chain).

<!--
verification:
  confirmed:
    - actor LOD exists as a skinning-quality (deformation) LOD combined with an XZ
      draw-distance cull, a frustum bounding-sphere cull, and an off-screen skinning skip;
      there is NO polygon-count mesh LOD and NO billboard or impostor representation
    - draw-distance cull: XZ-plane squared distance from camera eye ≤ 1,000,000
      (radius = 1000 world units); applied independently in both the per-actor
      render-submit function (Actor__VFunc_06) and the per-actor update/deform function
      (Actor__VFunc_02); actors beyond 1000 units XZ are not submitted, not
      pose-evaluated, not skinned, and not drawn that frame
    - frustum bounding-sphere cull (Diamond_Sphere_ClassifyVsFrustum, sphere centred at
      actor world origin) executes before the distance test in Actor__VFunc_06;
      in-range but off-screen actors are not submitted and do not have their
      visible-this-frame flag set
    - a per-actor visible-this-frame flag (actor core sub-object, field offset +1358,
      one byte) is written by Actor__VFunc_06 (cleared at entry, set on successful
      submit) and read by Actor__VFunc_02 to gate CPU skinning; an in-range but
      frustum-culled actor therefore skips SkinSet_DeformAndUpload for that frame
    - skinning-quality LOD is enabled by a per-actor LOD-enable flag at actor struct
      offset +0x6EC; the actor constructor sets it to 1 for every actor; the only
      code that clears it to 0 is SelectWindow_BuildZoomPreviewActor and
      SelectWindow_SpawnPreviewLineup (character-select preview actors only)
    - the deform-mode word at actor struct offset +0x6E8 is reselected every frame
      by Actor__VFunc_02 using full 3D squared distance from the camera eye; three bands:
        below 10,000 (< 100 world units)          -> mode 0: Skin_DeformLBS (full LBS)
        10,000 to < 20,000 (100 to ~141.4 units)  -> mode 1: Skin_DeformRigidMajor
        20,000 and above (>= ~141.4 units)         -> mode 2: Skin_DeformRigidOwned
      the deform-mode word is written only by the actor constructor (initialises to 0)
      and by the distance-band reselection; no authored or content-driven mode path exists
    - AnimMixer_BuildPose (clip-clock advance and keyframe sampling) is called in
      Actor__VFunc_02 before the distance/cull gate and runs every frame for every
      ticked actor regardless of XZ distance, frustum status, or visible flag; only
      Actor_EvaluatePoseWithRootMotion, SkinSet_DeformAndUpload, and the draw call
      are gated
    - LOD selection input is squared distance only: XZ-plane squared distance
      (Actor_DistanceSqXZ) for the draw-distance cull; full 3D squared distance for
      the skinning-band selection; no screen-size or projected-area metric appears
      anywhere in the actor path
    - all actor kinds (local player, remote players, NPCs, mobs) are Actor instances
      sharing one vtable; Actor__VFunc_06 and Actor__VFunc_02 are the same slots for
      all of them; no kind-specific LOD variant exists
    - string search for "lod", "billboard", "impostor", "imposter" across the whole
      binary image returns zero hits; Diamond_GDrawable_SelectRenderGroup selects a
      render group by bitmask match against the actor render-group word (actor offset
      +0x500), not by a geometric LOD level
    - SkinSet_DeformAndUpload locks a dynamic vertex buffer, dispatches the deform
      routine selected by the mode word, writes 32-byte-per-vertex deformed data,
      then unlocks; vertex format, draw call, and material binding are identical
      across all three deformation bands — only the deform routine differs
    - Character_DrawSkinnedCelShaded (multi-stage cel-shaded draw) is the
      character-select / UI preview draw path (callers in SelectWindow panels), not
      the in-world actor draw; it is unaffected by the LOD system
  static-hypothesis: []
  capture/debugger-pending:
    - exact intra-frame ordering of the render-submit traversal (sets visible flag)
      vs the update/deform traversal (reads visible flag): mechanism confirmed statically;
      whether both run same-frame or with one-frame lag requires live confirmation via
      the actor-manager per-frame tick (ActorManager__VFunc_02) [debugger-confirm]
    - whether any runtime path (e.g. a graphics-quality option) can clear the
      LOD-enable flag at actor +0x6EC for in-world actors; no such path found
      statically; confirm under a live session [debugger-confirm]
    - visual confirmation in a populated scene that actors at 100–141.4 world units
      and beyond render with visibly stiffer joint deformation relative to close range
      [capture-confirm]
  ida_reverified: 2026-06-28    # deep-3d wave 10: full LOD/cull pass — vtable slot
    coverage (Actor__VFunc_06 / Actor__VFunc_02), distance-constant recovery,
    skinning-band enumeration, LOD-enable and deform-mode writer enumeration,
    string sweep for lod/billboard/impostor, actor-manager traversal context
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: PARTIAL — all static facts resolved; three [debugger-confirm] /
    [capture-confirm] items remain; the port can proceed from the recovered constants
    with those uncertainties flagged
  evidence: [static-ida]
  conflicts: none identified
-->

---

## Status block

| Attribute | Value |
|---|---|
| `status` | **RESOLVED (static)** — actor LOD is a skinning-quality LOD plus distance/frustum cull and off-screen skinning skip; all constants, field offsets, and control flow recovered; three debugger/capture items remain open |
| `binary_analysed` | `doida.exe` (legacy client) — vtable slot coverage (Actor__VFunc_06 / Actor__VFunc_02), distance-constant recovery, skinning-band enumeration, deform-mode and LOD-enable writer enumeration, render-group-word context, string sweep for lod/billboard/impostor |
| `confidence` | **STATIC-CONFIRMED** for all resolved facts; three items carry [debugger-confirm] / [capture-confirm] flags |

---

## 1. Subsystem overview

**What actor LOD is.** `doida.exe` applies a three-level pipeline to every in-world actor every frame:

1. **Draw-distance cull** — actors beyond 1000 world units (XZ plane) are skipped entirely: not submitted, not pose-evaluated, not skinned, not drawn.
2. **Frustum bounding-sphere cull** — actors within draw distance but outside the view frustum are not submitted and skip CPU skinning.
3. **Skinning-quality LOD** — the CPU deformation routine used to fill the actor's dynamic vertex buffer is reselected each frame by full 3D distance into three bands, trading skinning smoothness for cost at range.

**What actor LOD is not.**

- There is **no polygon-count mesh LOD** — the geometry submitted is always the same real indexed skinned mesh regardless of distance.
- There is **no billboard or impostor** — a whole-binary string search for `lod`, `billboard`, `impostor`, and `imposter` returns zero hits; no impostor class, vtable, or RTTI descriptor exists; `Diamond_GDrawable_SelectRenderGroup` selects by render-group bitmask, not by a geometric LOD level.
- **Animation sampling is not distance-throttled** — `AnimMixer_BuildPose` runs before any cull gate.
- The selection input is **squared distance**, not screen size or projected area.

All in-world actor kinds — local player, remote players, NPCs, and mobs — are `Actor` instances sharing the same vtable. No kind-specific LOD variant exists.

---

## 2. Draw-distance cull

The draw-distance gate is an XZ-plane squared-distance comparison against a single global threshold.

| Parameter | Value | Notes |
|---|---|---|
| Distance² threshold | 1,000,000 world units² | XZ plane only — Y component ignored |
| Equivalent radius | 1000 world units | XZ plane |
| Distance helper | `Actor_DistanceSqXZ` — (Δx)² + (Δz)² from camera eye to actor world position | |

The threshold is enforced in **both** actor vtable entry points that carry LOD/cull logic:

- **Actor__VFunc_06 (render-submit):** evaluated after the frustum cull; actors that fail are not submitted and their visible-this-frame flag remains cleared.
- **Actor__VFunc_02 (update/deform):** evaluated before pose evaluation and skinning; actors that fail skip the entire deform body — pose walk, CPU skinning, draw call.

An actor beyond 1000 units XZ is therefore **not drawn, not pose-evaluated, and not skinned** in any given frame.

---

## 3. Frustum bounding-sphere cull and off-screen skinning skip

### 3.1 Frustum cull

`Actor__VFunc_06` calls `Diamond_Sphere_ClassifyVsFrustum` on a bounding sphere centred at the actor world origin before the distance test. Actors within draw distance but off-screen fail this test and are not submitted.

### 3.2 Visible-this-frame flag

A single per-actor flag records whether the actor was submitted during the current frame's render-submit traversal. It is located in the actor's core sub-object at field offset +1358 (one byte).

| Operation | Actor function |
|---|---|
| Cleared (= 0) at frame start | `Actor__VFunc_06` (clears at entry) |
| Set (= 1) on successful submit | `Actor__VFunc_06` (after both cull tests pass) |
| Read to gate CPU skinning | `Actor__VFunc_02` (before `Actor_EvaluatePoseWithRootMotion` + `SkinSet_DeformAndUpload`) |

The mechanism — write-then-read across the two traversal calls — is confirmed statically. Whether both traversals run within the same frame or with one frame of lag is [debugger-confirm]; see §8.

### 3.3 Off-screen skinning skip

An in-range actor whose visible flag is cleared (frustum-culled or not yet submitted this frame) skips CPU skinning entirely. `SkinSet_DeformAndUpload` is not called and the actor's dynamic vertex buffer is not updated that frame. This is the principal cost saving in dense scenes where many actors are in range but off-screen.

---

## 4. Skinning-quality LOD — three deformation bands

### 4.1 Control fields

| Field | Actor struct offset | Default | Set by |
|---|---|---|---|
| LOD-enable flag | +0x6EC | 1 (enabled) | Actor constructor sets 1 for every actor; `SelectWindow_BuildZoomPreviewActor` and `SelectWindow_SpawnPreviewLineup` set 0 for character-select preview actors only |
| Deform-mode word | +0x6E8 | 0 | Actor constructor initialises to 0; distance-band reselection in `Actor__VFunc_02` updates it each frame |

### 4.2 Distance bands

When the LOD-enable flag is set, `Actor__VFunc_02` reselects the deform-mode word every frame using the **full 3D squared distance** from the camera eye to the actor world position.

| 3D distance² range | Distance range | Deform mode | Routine |
|---|---|---|---|
| < 10,000 | < 100 world units | 0 | `Skin_DeformLBS` — full per-vertex linear-blend skinning |
| 10,000 to < 20,000 | 100 to ~141.4 world units | 1 | `Skin_DeformRigidMajor` — each vertex rigidly follows its single dominant influence bone |
| ≥ 20,000 | ≥ ~141.4 world units | 2 | `Skin_DeformRigidOwned` — rigid owned/fast path |

Cross-ref: `Docs/RE/specs/skinning.md §1` for the per-mode deform semantics (LBS weighted sum, rigid-major single-bone, rigid-owned single-owner fast path).

### 4.3 Semantic effect

Rigid modes 1 and 2 follow one dominant bone per vertex with no per-vertex weight blend. They match full LBS only where a vertex has exactly one skinning influence; at joints with multiple influences the deformation is stiffer. This is a **deformation-quality LOD** — the mesh topology and vertex count are unchanged across all three bands; only the deform routine and therefore the resulting vertex positions in the dynamic vertex buffer differ.

### 4.4 Character-select preview exception

`SelectWindow_BuildZoomPreviewActor` and `SelectWindow_SpawnPreviewLineup` clear the LOD-enable flag (actor +0x6EC = 0) for their preview actors, forcing full LBS at all distances. These are the **only** write sites that ever set the flag to 0. No static evidence was found of a runtime graphics-quality option clearing it for in-world actors; see §8.

---

## 5. Animation sampling is not distance-gated

`AnimMixer_BuildPose` — the clip-clock advance and keyframe sampler for all active clips on the actor — is called in `Actor__VFunc_02` **before** the distance/cull gate. It runs every frame for every ticked actor regardless of XZ distance, frustum result, or the visible-this-frame flag.

Only the subsequent steps are gated:

| Step | Gated by |
|---|---|
| `Actor_EvaluatePoseWithRootMotion` (pose-to-world walk) | Visible flag + distance cull |
| `SkinSet_DeformAndUpload` (CPU deform + VB upload) | Visible flag + distance cull |
| Actor draw vtable slot | Visible flag + distance cull |

Animation state therefore advances at full rate even for actors near the 1000-unit draw boundary or momentarily off-screen.

---

## 6. LOD selection inputs

| LOD stage | Distance metric | Notes |
|---|---|---|
| Draw-distance cull | XZ-plane squared distance — `Actor_DistanceSqXZ` | Y component excluded; camera eye from the global view-matrix state block |
| Skinning-band selection | Full 3D squared distance | Computed by a dedicated 3D squared-distance helper; camera eye from the same view-matrix block |

No screen-size or projected-area metric exists anywhere in the actor LOD path. The camera eye coordinates are drawn from the global render state / view-matrix block.

---

## 7. Render-state note

There is no LOD-specific D3D9 render state. `SkinSet_DeformAndUpload` locks the actor's dynamic vertex buffer, dispatches the deform routine selected by the deform-mode word (§4.2), writes 32-byte-per-vertex deformed data for all parts, then unlocks. The actor's per-part draw vtable slot then binds per-part texture states and issues the indexed draw call against the D3D9 device.

**Vertex format (32 B per vertex), draw call count, and material binding are identical across all three deformation bands.** Only the deform routine and the resulting vertex positions differ.

The `Character_DrawSkinnedCelShaded` multi-stage cel-shaded path (cel/toon ramp + glow post-process) is the character-select and UI preview draw, not the in-world actor draw. It is called from the SelectWindow panel renderers and is unaffected by the LOD-enable flag or the deform-mode bands. See `Docs/RE/specs/render_pipeline.md` for the full scene-graph traversal and cull-bin context.

---

## 8. Open items

| Item | Flag | Notes |
|---|---|---|
| Intra-frame ordering of render-submit vs update/deform traversal | [debugger-confirm] | The visible-this-frame flag is written by the render-submit pass and read by the update pass; the mechanism is clear statically; whether both passes occur within the same frame or with one frame of lag requires live confirmation via the actor-manager per-frame tick (`ActorManager__VFunc_02`) |
| Runtime option clearing the LOD-enable flag for in-world actors | [debugger-confirm] | No static path found; confirm under a live debug session that no graphics-quality option sets actor +0x6EC = 0 for in-world actors |
| Visible stiffer skinning at 100+ world units | [capture-confirm] | The band boundaries (100 u / 141.4 u) are well within the 1000-unit cull radius; confirm visually in a populated scene that actors beyond 100 units render with stiffer joint deformation relative to close range |

---

## 9. Shadow path (partial — not traced this pass)

`Actor_DrawPartsForShadow` and `ActorShadow_DrawBlobQuads` use `Actor_IsRelevantForLocalView` for relevance gating, but a distinct shadow draw-distance gate was not traced in this pass. The mechanism likely reuses the same frustum and relevance criteria. This is an open hand-off to the RE domain (re-function-analyst, shadow-draw pass) if per-shadow-distance accuracy is required by the port.

---

## 10. Reimplementation guidance

| Parameter | Value | Notes |
|---|---|---|
| Draw-distance cull radius | 1000 world units | XZ plane only; Y ignored for the cull comparison |
| Skinning-quality band 0 boundary | 100 world units (3D) | Full LBS below; rigid-major at or above |
| Skinning-quality band 1 boundary | ~141.4 world units (3D) | Rigid-major below; rigid-owned at or above |
| LOD selection input | Squared distance (XZ for cull, 3D for bands) | No screen-size metric |
| Animation sampling | Always full-rate; not gated by distance or visibility | |
| Geometry | Always the same mesh at all distances | No mesh swap, no billboard, no impostor |
| LOD-enable flag | Default 1 for all in-world actors | Clear to 0 for character-select preview actors only |

The Godot port should implement:
- An XZ-plane distance check gating draw, pose evaluation, and skinning at 1000 units.
- A frustum/visibility test gating submission (Godot's built-in `VisibleOnScreenNotifier3D` or visibility layer can serve this role).
- A distance-driven deformation quality selection equivalent to the three-band scheme for any CPU-skinned actor path; GPU-skinned actors do not have the same per-vertex cost tradeoff and may use a simplified LOD policy.
- Animation playback at full rate, independent of distance or visibility.

See `Docs/RE/specs/skinning.md` for the deform-mode semantics (`Skin_DeformLBS`, `Skin_DeformRigidMajor`, `Skin_DeformRigidOwned`) the Godot deformer must replicate for fidelity near the band boundaries.
