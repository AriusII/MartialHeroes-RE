---
verification: static-only (IDB SHA f61f66a9, 2026-06-28) — layouts recovered from static control-flow and operand analysis across the animation runtime cluster; no live debugger session; open questions noted below.
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false      # in-memory C++ object layouts, not packed-file formats
subsystems: [animation, render_pipeline, character]
conflicts: one flagged open (play-time clip-resolve key id_a vs id_b per formats/animation.md — not silently overwritten; see Open Q1)
deep_cartography_deepening: 2026-06-29 — struct B +0x00 key formula corrected to col1+categoryBase[(u8)(col0+1)]; ActorAnimationCycle pool stride confirmed 44 bytes (added end row); actor+0x510 typed as std::map<unsigned int,CoreAnimation*> keyed by id_a; Q1 static bound sharpened toward id_a (still debugger-pending); no other layout changes.
corrected_2026_06_30: keyframe sampler — the first index clamps to key_count-1, but the SECOND interpolation index WRAPS to keyframe 0 at the loop seam (it is NOT clamp-to-last); single-keyframe tracks short-circuit and never wrap. The rotation SLERP is raw-dot (no hemisphere flip), only the exact-antipodal dot<=-1 degenerate case special-cased. See the Keyframe sampler section. No layout change.
mixer_recovered_2026_06_30: per-frame layer math re-read from the binary (action FSM, cycle ramp, sync-phase advance, weighted-sample accumulate, between-pass commit, all shipped start/refresh call sites). Key new facts — action blend-in/out durations are play-call arguments (NOT clip fields/constants) and are 0 at every shipped call site; the commit pass clamps accumulated weight to (1 - committed weight) before folding; the action layer holds output weight 0 and freezes its FSM until the first wrap of its wrap-length window. See the new "Mixer dynamics" section. No layout change.
---

# Struct cluster: animation runtime — `CoreAnimation`, `AnimMixer`, `ActorAnimationCycle`, `ActorAnimationAction`, `AnimCatalog` record

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static analysis under EU Software
> Directive 2009/24/EC Art. 6, solely to document object layouts for clean-room reimplementation.
> **All offsets are byte offsets relative to the start of the named struct** — they are layout offsets,
> NOT memory addresses, and must never be treated as such. Citing engineers:
> `// spec: Docs/RE/structs/anim_runtime.md`.
>
> **Scope:** the render-side animation playback runtime — the loaded-clip object, the per-actor mixer and
> its layer objects, and the AnimCatalog lookup record. This is distinct from the on-disk `.mot` byte
> format (see `Docs/RE/formats/animation.md`) and the deform math (see `Docs/RE/specs/skinning.md`); both
> are cross-referenced and not re-derived here.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow and operand analysis,
> corroborated across multiple call sites; `[static-hypothesis]` = structurally inferred or observed only
> as a zero-init, not independently re-isolated this pass; `[debugger-pending]` = layout confirmed but
> one behavioural detail requires a live `?ext=dbg` read to settle definitively. This is a static-only
> pass — no debugger session was run.

---

## Summary

The renderer drives skeletal animation through an **embedded per-actor mixer** (`AnimMixer`) that owns two
intrusive doubly-linked lists of player layer objects: a CYCLE list for looping motions and an ACTION list
for one-shot fade-in/play/fade-out motions. Each layer holds a pointer to a shared, refcounted loaded-clip
object (`CoreAnimation`), a local-time cursor, a playback-speed scalar, a timing or FSM state, and
current/target blend weights.

Five architectural facts confirmed by this recovery pass:

1. **`CoreAnimation` is 80 bytes.** It is a `GHandle`-derived refcounted heap object carrying a VFS
   source path, two ids (id_a and id_b), frame count–derived duration, a track vector, and two state
   bytes. Track data is loaded lazily on first lookup (lazy Stage-2 load). See `formats/animation.md`
   for the `.mot` on-disk format; `CoreAnimation` is the parsed, live representation.
2. **The `AnimMixer` is embedded in the actor object** at actor field +0x524 (not separately allocated).
   It owns the two layer lists and a shared sync phase/range for phase-locking co-playing looping layers.
3. **Two concrete player layer types:** `ActorAnimationCycle` (looping, sync or free-run) and
   `ActorAnimationAction` (one-shot, 52 bytes). Both advance and sample via direct (non-virtual) calls
   from the mixer; only the destructor slot is exercised virtually on the recovered paths.
4. **The `AnimCatalog`** is a balanced tree mapping an i64 appearance key to a 136-byte per-row record
   holding two 9-element motion-id arrays plus per-record scalars. It is embedded in the
   `ActorVisualGlobal` singleton at singleton field +0x2DAC.
5. **Stand-idle selection is confirmed:** the appearance key at actor +0x6C is looked up in the
   AnimCatalog; motion-kind 0 reads the record's array-A element 1 (record +0x44) as the stand-idle
   motion id. This corroborates `Docs/RE/specs/skinning.md §10` and `Docs/RE/formats/animation.md`
   (column 16 / record +0x44 for stand idle; element 0 / +0x40 is unused at runtime).

---

## Object ownership and lifetime

**Boot-time:** the motlist loader reads `data/char/motlist.txt` line by line, prefixes `data/char/mot/`,
and for each path creates a `CoreAnimation` heap object. The loader reads id_a, id_b, name (discarded),
and frame count to derive duration, then registers the clip into a master registry keyed by id_a and into
a per-skeleton bucket selected by id_b. After the full list, a motion cache file primes a subset of clips
for eager full-data load. The AnimCatalog is populated from `data/char/actormotion.txt` (count-prefixed,
33 fields per record).

**Per-actor:** the mixer is embedded in the actor (not heap-allocated separately). Layer objects are
allocated from small fixed pools and pushed onto the mixer's lists; a layer is deleted via its vtable
destructor slot the frame it expires (an action one-shot finishes, or a cycle's target weight reaches
zero). The mixer never persists expired layers.

**Clip lifetime:** clip objects are refcounted (`GHandle` base). Track data is loaded lazily on first
registry lookup; the clip's last-access timestamp is updated on each lookup for LRU bookkeeping.

**Sharing:** AnimCatalog records and clip objects are global/shared across all actors. Layer objects are
per-actor.

---

## Struct A — `CoreAnimation` (loaded clip object, 80 bytes)

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | ptr | **vtable pointer** | `CoreAnimation` vtable (GHandle-derived). Vtable slot roles: see vtable map below. | confirmed |
| +4 | +0x04 | ~28 | `std::string` | **source VFS path** | MSVC `std::string` containing the VFS path `data/char/mot/<file>`, kept for Stage-2 reopen on lazy full-data load. Spans approximately +0x04..+0x1F (small-string-optimized layout). | confirmed |
| +32 | +0x20 | 1 | u8 | **header-valid flag** | Set to 0 on header open failure. | confirmed |
| +33 | +0x21 | 1 | u8 | **fully-loaded flag** | Set to 1 after the track array is read (Stage-2 complete). Gates lazy load at registry lookup and mixer skip when 0. | confirmed |
| +40 | +0x28 | 4 | u32 | **last-access timestamp (ms)** | Written by the registry lookup on every access. Used for LRU bookkeeping. | confirmed |
| +52 | +0x34 | 4 | ptr | **track vector allocator/proxy** | Internal `std::vector` machinery. | confirmed |
| +56 | +0x38 | 4 | ptr | **track array begin** | Pointer to the first `CoreTrack` element. | confirmed |
| +60 | +0x3C | 4 | ptr | **track array end** | One-past-end of the used track range. | confirmed |
| +64 | +0x40 | 4 | ptr | **track array capacity-end** | Zeroed in constructor. | confirmed |
| +68 | +0x44 | 4 | u32 | **id_a** (file id, first word) | Per-clip unique id equal to the filename number. Used as the leaf-level lookup key at play-time resolve sites. See Open Q1 regarding reconciliation with `formats/animation.md`. | confirmed |
| +72 | +0x48 | 4 | u32 | **id_b** (group id, second word) | Selects the per-skeleton registration bucket at boot-time load. | confirmed |
| +76 | +0x4C | 4 | f32 | **duration (seconds)** | Computed as `frame_count × 0.1` (10 fps). See `Docs/RE/formats/animation.md` for the `.mot` frame-count field. | confirmed |
| +80 | +0x50 | — | — | **end of object (80 bytes)** | | |

> Bytes +0x04..+0x1F overlap the MSVC `std::string` (small-string-optimized layout). The exact byte
> boundary of the SSO buffer vs. internal string fields is not sub-byte-pinned — treat +0x04..+0x1F as
> the path-string region. The gap +0x22..+0x27 was not observed written and is likely string tail / pad.

---

## Struct A1 — `CoreTrack` (runtime per-bone track, 16 bytes)

Pointed to by the `CoreAnimation` track array (struct A field +0x38). Offsets relative to the track
object base.

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | ptr | **track head** | Object header; exact role not fully pinned at this pass. | static-hypothesis |
| +4 | +0x04 | 4 | ptr | **keyframe array base** | Pointer to the first 28-byte keyframe (`vec3` translation + `quat` XYZW). See `Docs/RE/formats/animation.md §keyframe-layout`. | confirmed |
| +8 | +0x08 | 1 | u8 | **bone_id** | Target bone index. Low byte of the `.mot` track descriptor. | confirmed |
| +12 | +0x0C | 4 | u32 | **key_count** | Number of keyframes in the array. | confirmed |

> **Two-view note (Open Q3):** the parse loop uses a temporary 16-byte scratch block with a different
> internal field ordering. The offsets above are the committed runtime offsets used by the sampler and
> pose evaluator — these are the authoritative values for porting. Confirm via the per-element track
> constructor if a precise parse-vs-runtime layout reconciliation is needed.

> The 28-byte keyframe layout (vec3 translation at +0, quat XYZW at +12) and the 10-fps sampling
> convention are owned by `Docs/RE/formats/animation.md`; they are cross-referenced here because the
> mixer directly consumes them.

---

## Struct B — `AnimCatalog` record (actormotion row, 136 bytes)

One record per row of `data/char/actormotion.txt`. Stored as values in the `AnimCatalog` balanced tree
(a `std::map<i64 appearanceKey, record*>`) embedded in the `ActorVisualGlobal` singleton at singleton
field +0x2DAC. Offsets relative to the record base.

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | i32 | **motion_key** | Global record key inserted into the ordered map. Computed as `col1 + categoryBase[(u8)(col0+1)]` where col0 = category selector and col1 = intra-category offset (same formula as `formats/actormotion.md`; deep-cartography deepening pass f61f66a9 re-confirmed byte-for-order). The high dword of the i64 map key is 0. See Open Q2 for the `categoryBase[]` literal-values gap. | confirmed |
| +4 | +0x04 | 4 | i32 | scalar (file col3) | | confirmed |
| +8 | +0x08 | 4 | f32 | scalar (file col4) | Numerator of derived rate stored at +0x30. | confirmed |
| +12 | +0x0C | 4 | f32 | scalar (file col6) | Numerator of derived rate stored at +0x34. | confirmed |
| +16 | +0x10 | 4 | i32 | scalar (file col8) | | confirmed |
| +20 | +0x14 | 4 | f32 | scalar (file col9) | | confirmed |
| +24 | +0x18 | 4 | f32 | scalar (file col10) | | confirmed |
| +28 | +0x1C | 4 | f32 | scalar (file col11) | | confirmed |
| +32 | +0x20 | 4 | f32 | scalar (file col12) | | confirmed |
| +36 | +0x24 | 4 | f32 | scalar (file col13) | | confirmed |
| +40 | +0x28 | 4 | i32 | **rate denominator A** (file col5) | Forced to 1 if 0. Divisor for derived rate at +0x30. | confirmed |
| +44 | +0x2C | 4 | i32 | **rate denominator B** (file col7) | Forced to 1 if 0. Divisor for derived rate at +0x34. | confirmed |
| +48 | +0x30 | 4 | f32 | **derived rate A** | Computed as `field@+0x08 × 15.0 / field@+0x28`. | confirmed |
| +52 | +0x34 | 4 | f32 | **derived rate B** | Computed as `field@+0x0C × 15.0 / field@+0x2C`. | confirmed |
| +56 | +0x38 | 4 | f32 | scalar (file col14) | | confirmed |
| +60 | +0x3C | 4 | f32 | scalar (file col15-of-file) | | confirmed |
| +64 | +0x40 | 36 | i32[9] | **motion-id array A (9 elements)** | Element 0 (+0x40) unused at runtime. **Element 1 (+0x44) = stand-idle motion id** (motion-kind 0). Element 6 (+0x58) = alt-idle (motion-kind 1). | confirmed |
| +100 | +0x64 | 36 | i32[9] | **motion-id array B (9 elements)** | Element 4 (+0x74) = death effect id. Per `Docs/RE/formats/animation.md`. | confirmed |
| +136 | +0x88 | — | — | **end of record (136 bytes)** | Arrays A and B are each read by a 9-iteration loop. Total: 16 scalars × 4 B + 36 B + 36 B = 136 B. | |

---

## Struct C — `AnimMixer` (embedded in actor at actor +0x524)

The mixer is embedded in the actor object and is not separately heap-allocated. Offsets below are relative
to the mixer base (i.e., `actorBase + 0x524 + offset`).

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | — | **embedded layout head** | Not observed written on recovered paths; treat as embedded-layout filler. | static-hypothesis |
| +4 | +0x04 | 4 | ptr | **back-pointer to actor** | Source of the actor's speed scalar (+0x3FC), pose (+0x514), root transform, and clip registry. | confirmed |
| +8 | +0x08 | 4 | ptr | **ACTION list allocator/proxy** | Internal intrusive-list machinery. | confirmed |
| +12 | +0x0C | 4 | ptr | **ACTION list head/sentinel** | First live node = `*(+0x0C)`. | confirmed |
| +16 | +0x10 | 4 | u32 | **ACTION list size** | Count of live action layers. | confirmed |
| +20 | +0x14 | 4 | ptr | **CYCLE list allocator/proxy** | | confirmed |
| +24 | +0x18 | 4 | ptr | **CYCLE list head/sentinel** | | confirmed |
| +28 | +0x1C | 4 | u32 | **CYCLE list size** | Count of live cycle layers. | confirmed |
| +32 | +0x20 | 4 | f32 | **sync phase** | Advanced each frame by `dt × 1.5`, wrapped modulo the sync range (+0x24). Shared across all sync-mode cycle layers. | confirmed |
| +36 | +0x24 | 4 | f32 | **sync range** | Recomputed each frame as `Σ(weightᵢ × durationᵢ) / Σweightᵢ` over sync-mode cycle layers. Set to 0 when Σweight ≤ 0. | confirmed |
| +40 | +0x28 | 1 | u8 | **unconfirmed byte** | Not observed written; see Open Q4. | static-hypothesis |
| +41 | +0x29 | 1 | u8 | **wrap-occurred flag** | Set when the sync phase wraps a full cycle; triggers footstep sound effect. | confirmed |

> **Intrusive list layout:** each node is `{ next @ +0, prev @ +4, payload @ +8 }`. The layer object
> begins at `node + 8`. The sync mechanism phase-locks all sync-mode looping layers over a
> weighted-average clip duration; the 1.5 factor is a global playback rate for the sync path.

---

## Struct D — `ActorAnimationCycle` (looping layer)

Allocated from a small fixed pool; pushed onto the mixer CYCLE list when a looping motion starts.
Offsets relative to the layer object base.

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | ptr | **vtable pointer** | `ActorAnimationCycle` vtable. Only slot 0 (destructor) is exercised on recovered paths. | confirmed |
| +4 | +0x04 | 4 | ptr | **clip pointer** | `CoreAnimation*` — the loaded clip this layer is playing. | confirmed |
| +8 | +0x08 | 4 | u32 | **clip id** | Equals the clip's id_a (struct A +0x44). The layer's identity, matched against a requested motion id. | confirmed |
| +12 | +0x0C | 4 | u32 | **layer-kind tag** | `1` for Cycle. (`ActorAnimationAction` uses `3`.) | confirmed |
| +16 | +0x10 | 4 | u32 | **timing mode** | `1` = sync (sample time derived from mixer sync phase); `2` = free-run (own local time). | confirmed |
| +20 | +0x14 | 4 | f32 | **local clip time** | Used in free-run mode only. Advanced by `+= speed × dt`, fmod-wrapped at clip duration. | confirmed |
| +24 | +0x18 | 4 | f32 | **playback speed** | Default 1.5. Applied to dt before advancing local time in free-run mode. | confirmed |
| +28 | +0x1C | 4 | f32 | **current blend weight** | Ramped toward target weight (+0x24) during blend-in. | confirmed |
| +32 | +0x20 | 4 | f32 | **blend-in remaining time** | Counts down by dt; when ≤ 0 the weight latches to the target. | confirmed |
| +36 | +0x24 | 4 | f32 | **target blend weight** | Set by the start/refresh call. Layer expires and is deleted when this reaches 0. | confirmed |
| +40 | +0x28 | 1 | u8 | **wrap-occurred flag** | Set when free-run local time wraps to the beginning of the clip. | confirmed |
| +41–43 | +0x29–+0x2B | 3 | — | **pad** | Alignment padding to 44-byte pool stride. | confirmed |
| +44 | +0x2C | — | — | **end of object (44 bytes)** | Pool-allocation stride confirmed: `malloc(44 × count)` (deep-cartography deepening pass, f61f66a9). Fields +0x04..+0x28 fill exactly 44 bytes. | |

> **Sample time for evaluation:** sync-mode = `clipDuration × (mixer.phase / mixer.range)`; free-run =
> local time (+0x14). Blend-in ease uses a `logf`-guarded denominator floor of 0.001.

---

## Struct E — `ActorAnimationAction` (one-shot layer, 52 bytes)

Allocated from a small fixed pool; pushed onto the mixer ACTION list by the play-action routine.
Offsets relative to the layer object base.

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | ptr | **vtable pointer** | `ActorAnimationAction` vtable. Only slot 0 (destructor) is exercised on recovered paths. | confirmed |
| +4 | +0x04 | 4 | ptr | **clip pointer** | `CoreAnimation*` — the loaded clip. | confirmed |
| +8 | +0x08 | 4 | u32 | **clip id** | Equals the clip's id_a (struct A +0x44). | confirmed |
| +12 | +0x0C | 4 | u32 | **layer-kind tag** | `3` for Action. | confirmed |
| +16 | +0x10 | 4 | u32 | **playback FSM state** | `3` = blend-in, `4` = full-weight play, `5` = blend-out (transitions to expire). | confirmed |
| +20 | +0x14 | 4 | f32 | **local clip time** | Advanced by `+= speed × dt`; wrapped at the wrap length (+0x2C). | confirmed |
| +24 | +0x18 | 4 | f32 | **playback speed** | Set to `requestedSpeed × 1.5`; default effective 1.5. | confirmed |
| +28 | +0x1C | 4 | f32 | **output blend weight** | Ramped 0 → 1.0 during blend-in (state 3); held 1.0 during full play (state 4); ramped 1.0 → 0 during blend-out (state 5). | confirmed |
| +32 | +0x20 | 4 | f32 | **blend-in duration** | Time (seconds) for the weight ramp up. | confirmed |
| +36 | +0x24 | 4 | f32 | **blend-out duration** | Time (seconds) for the weight ramp down. | confirmed |
| +44 | +0x2C | 4 | f32 | **wrap length** | Clip-length reference for first-cycle wrap detection. | confirmed |
| +48 | +0x30 | 1 | u8 | **wrapped-once flag** | Set when local time has wrapped at least once. | confirmed |
| +52 | +0x34 | — | — | **end of object (52 bytes)** | +0x28 (4 bytes) was not observed written on recovered paths; treated as pad/filler. | |

> **FSM transition:** state 3 ramps weight = `localTime / blend-in-duration` (logf-guarded denominator);
> at blend-in end transitions to state 4 (weight 1.0); when `localTime ≥ clipDuration − blend-out-duration`
> transitions to state 5; state 5 ramps weight = `(clipDuration − localTime) / blend-out-duration` and
> returns expired at `clipDuration`.

---

## Struct F — per-bone sample accumulator (overlay on the 88-byte runtime pose bone)

These offsets are within the 88-byte runtime pose-bone object. See `Docs/RE/specs/skinning.md §3.4` for
the complete pose-bone field map. The mixer uses these slots as transient per-frame accumulators; they
are zeroed by the pose-reset step at the start of `Actor_EvaluatePoseForRender`.

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +24 | +0x18 | 4 | f32 | **accumulated weight** | Running sum of applied layer weights this frame (Σwᵢ). | confirmed |
| +56 | +0x38 | 12 | f32×3 | **accumulated translation** | Running weighted-average translation (LERP accumulator). | confirmed |
| +68 | +0x44 | 16 | f32×4 | **accumulated quaternion XYZW** | Running weighted-average rotation (SLERP accumulator). | confirmed |

> **Blend rule:** the first sample is assigned directly. Thereafter the blend fraction is
> `f = wNew / (wAccumulated + wNew)` with the denominator floored at 0.001 via a logf guard, then
> translation LERP and quaternion SLERP by `f`, followed by `wAccumulated += wNew`. A commit step
> (`Pose_CommitBoneSamples`) finalizes the accumulator into the bone's committed animated local pose
> between the ACTION pass and the CYCLE pass, and again after.

---

## Relevant actor field offsets (observed at animation runtime sites)

These are field offsets within the actor object, observed at animation runtime call sites. They are
included for cross-referencing context; the authoritative actor struct table is a separate struct lane.

| Actor field offset | Hex | Role |
|-------------------:|-----|------|
| +108 | +0x6C | appearance key (i64 AnimCatalog lookup key, low dword) |
| +168 | +0xA8 | facing/direction (u16) |
| +964 | +0x3C4 | motion-kind byte (0 = stand idle, 1 = alt idle) |
| +1020 | +0x3FC | playback speed (f32, default 1.5) |
| +1064 | +0x428 | root world translation (vec3) |
| +1076 | +0x434 | root world quaternion |
| +1288 | +0x508 | delta-ms accumulator (computed each draw frame) |
| +1292 | +0x50C | last-drawn-ms timestamp |
| +1296 | +0x510 | per-actor clip registry (`std::map<unsigned int, CoreAnimation*>`, keyed by id_a — deep-cartography f61f66a9; see Q1) |
| +1300 | +0x514 | runtime pose object (88-byte bone array) |
| +1316 | +0x524 | **embedded AnimMixer** (struct C above) |
| +1420 | +0x58C | anim-state byte |

---

## Virtual method table slot-role maps

### `CoreAnimation` (clip object) vtable

Only two slots are exercised on recovered paths.

| Slot | Conceptual name | Description |
|-----:|-----------------|-------------|
| 0 | **scalar-deleting destructor** | Standard MSVC pattern. Invoked on failed clip creation. |
| 1 | **ensure fully loaded** | Returns a success bool. Runs the Stage-2 full-data track-array load when the fully-loaded byte (struct A +0x21) is clear. Invoked by the registry lookup on every play-time access. |

### `ActorAnimationCycle` / `ActorAnimationAction` (player layers) shared vtable shape

Only one slot is exercised on recovered paths.

| Slot | Conceptual name | Description |
|-----:|-----------------|-------------|
| 0 | **scalar-deleting destructor** | Standard MSVC pattern. Invoked the frame the layer expires and is erased from the mixer list. |

> The per-frame advance, blend, and sample operations are **not virtual** — the mixer dispatches directly
> to the concrete cycle or action advance routine based on which list the layer resides in. There is no
> per-layer "update" vtable slot to map.

---

## Per-frame path

The following call sequence drives animation each frame. Canonical role names are used throughout; all
binary addresses are stripped per firewall rules.

**Draw-time path** (`Character_DrawSkinnedCelShaded`):

1. **Compute dt.** Read `battleClock.ms` and `actor.lastDrawnMs(+0x50C)`; compute `delta_ms`; store
   into `actor.deltaMs(+0x508)`; write `lastDrawnMs` back; `dt = delta_ms × 0.001` seconds.
2. **`AnimMixer_BuildPose(mixer at actor+0x524, dt)`:**
   a. Advance sync phase: `mixer.phase(+0x20) += dt × 1.5`, wrap modulo `mixer.range(+0x24)`;
      set wrap flag.
   b. ACTION pass: advance each action layer via `AnimActionLayer_AdvanceState(layer, dt)`; delete
      layers that return expired.
   c. CYCLE pass: advance each cycle layer via `AnimCycleLayer_AdvanceBlend(layer, dt × actor.speed(+0x3FC))`;
      ramp current weight toward target; for sync-mode layers accumulate Σweight and Σ(weight × clipDuration);
      fire footstep sound effect on wrap; delete expired layers.
   d. Recompute `mixer.range = Σ(weight × duration) / Σweight` for next frame.
3. **`Actor_EvaluatePoseForRender`:**
   - Reset all bone accumulators (`Pose_ResetBoneAccumulators` on the pose at actor+0x514).
   - ACTION pass: for each loaded action layer, for each clip track, resolve the pose bone by bone_id,
     call `Track_SampleAtTime(track, layer.localTime(+0x14), &trans, &quat)`, then
     `Bone_AccumulateWeightedSample(bone, layer.weight(+0x1C), trans, quat)`; then commit.
   - CYCLE pass: sample time = sync-mode (`clipDuration × mixer.phase / mixer.range`) or free-run
     local time; same per-track sample and per-bone weighted accumulate; commit.
   - Root compose: copy `actor.rootWorldTranslation(+0x428)` into the pose root; compose root world
     quaternion from `actor.rootQuat(+0x434)` and the root-bone local quaternion.
   - `Pose_WorldWalk` builds each bone's world transform (parent-on-left). See
     `Docs/RE/specs/skinning.md`.
4. **`SkinSet_DeformAndUpload`:** CPU linear-blend skin deform into the dynamic vertex buffer and
   upload to the D3D9 device.

**Update-time path:** the actor per-frame update virtual also calls `AnimMixer_BuildPose` followed by
`Actor_EvaluatePoseWithRootMotion` — the root-motion-aware variant used for gameplay movement (emits a
position delta) vs the draw-time render-only variant above.

**Keyframe sampler** (confirms `Docs/RE/formats/animation.md`):
`idx = floor(t × 10)`, **clamp the first index to `key_count − 1`; the second index `idx+1` WRAPS to
keyframe 0 at the loop seam** (it is NOT clamped to `key_count − 1`; corrected 2026-06-30) — so a track
with `key_count ≤ frame_count` interpolates `keyframe[last] → keyframe[0]` over its final 0.1 s window
(raw-seconds alpha in `[0, 0.1)`), while a single-keyframe track short-circuits to that one key and
never wraps;
alpha = `t − idx × 0.1` (raw seconds, **not** renormalized to [0, 1]),
translation via `Vec3_Lerp`, rotation via `Quat_Slerp` (**raw-dot, no hemisphere flip; only the
exact-antipodal `dot ≤ −1` degenerate case is special-cased** — corrected 2026-06-30; the Godot port
keeps a shortest-arc SLERP and must NOT be changed toward the binary, see
`Docs/RE/specs/skinning.md` §6.1).

**Idle-clip selection** (`ActorVisual_ApplyIdleMotionByKind`):
1. Read `actor.appearanceKey(+0x6C)`.
2. `AnimCatalog_LookupByKey(ActorVisualGlobal, key)` → 136-byte record from the tree at singleton +0x2DAC.
3. Read `actor.motionKind(+0x3C4)`: kind 0 → `record+0x44` (array A element 1, stand idle);
   kind 1 → `record+0x58` (array A element 6, alt idle); other kinds → global per-direction motion
   tables on the singleton indexed by `actor.direction(+0xA8)`.
4. `MotClipList_SampleByTime(mixer, motionId, weight=1.0, blendIn=0.0)` — starts or refreshes the
   cycle layer; sets `actor.animState(+0x58C) = 1`; defaults `actor.speed(+0x3FC) = 1.5`.

---

## Mixer dynamics — exact per-frame layer math (recovered 2026-06-30)

A binary re-read of the action-layer advance, the cycle-layer advance, the weighted-sample accumulate,
the between-pass commit, and every shipped play-action / start-cycle call site sharpened the following.
Struct-relative offsets only; no behaviour below changes any layout in the tables above.

**Action one-shot FSM (struct E) — what drives the blend-in/out durations.** The blend-in duration
(layer +0x20) and blend-out duration (layer +0x24) are **not** clip fields and **not** engine
constants — they are **arguments passed to the play-action routine and stored verbatim on the layer at
start**. The same start routine sets clip pointer (+0x04), clip id = clip id_a (+0x08), layer-kind tag
3 (+0x0C), FSM state 3 (+0x10), local time 0 (+0x14), playback speed = requestedSpeedFactor × 1.5
(+0x18), output weight 0 (+0x1C), wrap length = a caller argument (+0x2C), and wrapped-once flag 0
(+0x30). **Every shipped call site passes blend-in = 0 and blend-out = 0** — the death/knockdown
motion, the combat/skill action dispatcher, and the effect-driven motion path were all checked and all
pass 0 for both durations; only the **wrap length** is ever data-driven (one path reads it from a
per-actor table at actor +0x4C4 indexed by a motion selector; the others pass 0). The requested speed
factor observed is 1.0 (death) or 1.5 (combat), giving an effective layer speed of 1.5 or 2.25.
**Consequence for the port:** with zero blend durations the FSM steps state 3 → 4 on the first advance
(output weight latches straight to 1.0) and 4 → 5 → expire across the final frame, i.e. shipped
one-shot actions are a hard 0↔1 switch with **no visible cross-fade**. The ramp machinery is genuine
but the shipped data never exercises it; the logf-guarded 0.001 denominator floor exists precisely to
keep the `localTime / duration` division finite when a duration is 0.

**Action FSM per-frame (confirms struct E).** Each advance: local time (+0x14) += speed (+0x18) × dt.
Before the first wrap (local time < wrap length AND wrapped-once flag clear) the **output weight is
forced to 0 and the FSM is frozen** for that frame; on the first crossing of the wrap length the
wrapped-once flag (+0x30) is set and the wrap length is subtracted from local time (clamped ≥ 0).
State 3 (blend-in): if blend-in duration ≤ local time → state 4 and weight = 1.0; else weight =
local time / blend-in duration (denominator floored to 0.001 when its logf < 0.001). State 4 → state 5
when local time ≥ clip duration − blend-out duration. State 5 (blend-out): if clip duration ≤ local
time → return expired (layer deleted this frame); else weight = (clip duration − local time) /
blend-out duration (same 0.001 floor). With a zero wrap length the pre-first-wrap gate is a no-op on
frame 1.

**Cycle looping-layer weight ramp (confirms struct D).** Start/refresh sets target weight (+0x24) and
blend-in remaining time (+0x20) from the start call's weight and blend-in arguments; a refresh of an
already-live layer (matched by clip id at +0x08) rewrites **only** those two fields. Each advance, when
dt < blend-in remaining: weight (+0x1C) = (1 − f)·weight + target·f with f = dt / (blend-in remaining)
(the remaining time floored to 0.001 when its logf < 0.001), then blend-in remaining −= dt. When
dt ≥ blend-in remaining: weight latches to the target weight and the remaining time latches to the
target as well; **if the target weight is 0 the layer expires** (deleted this frame). Free-run timing
(mode 2): local time (+0x14) += speed (+0x18) × dt, fmod-wrapped at clip duration, wrap flag (+0x28)
set on wrap. Sync timing (mode 1): the layer carries no own clock; its sample time is computed in the
evaluator as clip duration × mixer sync phase / mixer sync range.

**Sync-phase advance (confirms struct C).** Each frame, before the layer passes: clear the mixer wrap
flag (+0x29); then if the sync range (+0x24) is 0 set the sync phase (+0x20) to 0, else phase += dt × 1.5
and fmod-wrap at the range, setting the mixer wrap flag on a full wrap (which fires the footstep sound
effect once per wrap for sync-mode cycle layers). After the cycle pass the range is recomputed as
Σ(weightᵢ × clip durationᵢ) / Σ weightᵢ over **sync-mode cycle layers only**, set to 0 when Σ weight ≤ 0.

**Per-bone weighted-sample accumulate (confirms struct F).** First contributor: assign the sampled
translation (accumulator +0x38) and quaternion (+0x44) directly and set accumulated weight (+0x18) =
wNew. Later contributors: f = wNew / (accumulated weight + wNew), denominator floored to 0.001 when
logf(accumulated weight + wNew) < 0.001; LERP translation by f, SLERP quaternion by f, then accumulated
weight += wNew.

**Between-pass commit (confirms struct F; cross-note skinning §6.2–6.3).** Run once after the action
pass and again after the cycle pass, over every pose bone. Per bone: **first clamp the pass's
accumulated weight (+0x18) to at most (1 − committed weight)** before folding. If accumulated weight > 0:
when committed weight is 0 (first/only layer) copy the accumulator translation/quaternion straight into
the committed local-animated slots; when committed weight ≠ 0 (second-or-later layer) blend by
f = accumulated / (committed + accumulated) (floored to 0.001 under the same logf guard) — and for an
**interior bone** (one with a parent, a grandparent, and at least one child) the translation is instead
forced to its bind-local translation (rotate-only), while non-interior bones LERP translation;
quaternion always SLERP. Finally committed weight += accumulated and the accumulator weight resets to 0.

**Idle ↔ action transition trigger (per-frame).** The action pass deletes any action layer whose
advance returns expired; the cycle pass deletes any cycle layer whose target weight has reached 0. When
the action list **becomes empty on a frame on which it was non-empty** (the last action just expired)
the mixer calls the idle/state tick, which re-applies the looping idle cycle for the actor's current
motion-kind — this is the action-finished → idle hand-off. Conversely a gameplay event (attack, skill,
death) calls the play-action routine, pushing a new action layer onto the action list; while any action
layer is live the action pass commits first and the idle cycle's contribution is blended underneath it
by the weighted-average commit.

---

## Open questions

The following items remain unresolved from static analysis. They are flagged for `re-validator` to
confirm via a live `?ext=dbg` debugger session. **Do not silently resolve these by spec overwrite.**

**Q1 — Play-time clip resolve key: id_a vs id_b `[debugger-pending]`**
At the resolve sites the motion id from the AnimCatalog record is passed to the clip registry lookup
on `actor+0x510`, whose leaf tree is keyed by **clip id_a** (struct A +0x44). The per-skeleton
registration **bucket** is selected by **id_b** at boot-time. `Docs/RE/formats/animation.md` (CYCLE-7)
states the play-time resolve is id_b-keyed. These may be reconcilable (id_b selects the bucket, id_a
addresses the leaf within it) or may be a genuine conflict requiring a spec correction. A live debugger
session watching the registry lookup is required. **This spec does not overwrite `formats/animation.md`**
— both descriptions stand until confirmed against the binary (ground truth wins).

**Sharpened static bound (deep-cartography pass, f61f66a9):** The per-actor clip registry at
`actor+0x510` is a `std::map<unsigned int, CoreAnimation*>` (tree comparison via unsigned int
lower_bound). `MotClipList_SampleByTime` (a) first tests an already-active mixer layer by
`layer+0x08 == motion_id`; (b) on miss calls the per-actor registry lookup on actor+0x510; (c) on
success sets `layer+0x08 = clip+0x44` (= id_a). For the refresh-match at (a) to ever succeed on a
subsequent frame, `motion_id` must equal `clip+0x44` (id_a). This is strong static evidence that the
**per-actor registry is id_a-keyed** and the actormotion array-A ids occupy the id_a namespace —
contradicting the CYCLE-7 `formats/animation.md` claim. However, the two-registry architecture
(boot-time id_b-keyed master + per-actor id_a-keyed) may reconcile both. A live read of the
`motion_id` value at the lookup site versus `clip+0x44` and `clip+0x48` is the definitive test.
**Neither spec is overwritten; escalated to `re-validator` for a single `?ext=dbg` runtime read.**

**Q2 — `categoryBase[]` values `[static-hypothesis]`**
The `categoryBase[]` array (indexed by `(u8)(col1+1)`) that contributes to the AnimCatalog map key
at record +0x00 is computed in code, not in the text tables. Its contents are not recoverable from
data files alone. This gap is also noted in `Docs/RE/specs/skinning.md §3.5.5`.

**Q3 — CoreTrack two-view discrepancy `[static-hypothesis]`**
The parse loop uses a temporary 16-byte scratch block with a different field ordering than the
committed runtime track object used by the sampler and evaluator. Struct A1 above reflects the runtime
(authoritative) offsets. Confirm via the per-element track constructor if a precise parse-to-runtime
reconciliation is needed.

**Q4 — Mixer +0x00 / +0x28 `[static-hypothesis]`**
The mixer head dword (+0x00) and the byte at +0x28 were not observed written on the recovered paths.
+0x00 is treated as embedded-layout filler; +0x28 as unconfirmed pad. Confirm with a struct-recovery
pass over the actor constructor if needed.

**Q5 — Raw-seconds alpha `[debugger-pending]`**
The keyframe interpolation alpha is raw seconds in [0, 0.1] (not renormalized to [0, 1]), confirmed
in the static sampler analysis and matching `formats/animation.md`. Whether this is intentional or a
legacy quirk has not been confirmed via a live session. Relevant for porting fidelity.

**Q6 — No live confirmation performed**
This entire recovery pass is static only. A `?ext=dbg` breakpoint at `AnimMixer_BuildPose` or the
pose evaluator on a real character would confirm: mixer embed offset 0x524 in the actor, the dt value,
sync phase/range evolution under normal play, and Q1's resolve key against ground truth. Escalated to
`re-validator`.

---

## Cross-references

- On-disk `.mot` binary format and keyframe layout: `Docs/RE/formats/animation.md`
- Skin deform math, pose-bone full field map, and world-walk chain: `Docs/RE/specs/skinning.md`
- Appearance key, skin class, and AnimCatalog key construction: `Docs/RE/specs/skinning.md §3.5.5`
- Per-frame render dispatch and draw call chain: `Docs/RE/specs/render_pipeline.md`
- Entity placement and actor roster: `Docs/RE/specs/entity_placement.md`
- Camera projection struct: `Docs/RE/structs/perspective_camera.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
