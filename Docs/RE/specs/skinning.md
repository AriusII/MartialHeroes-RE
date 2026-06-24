# Skinning & animation pipeline (clean-room spec)

> **Verification banner.**
> - **verification:** *confirmed* (control-flow-confirmed) for the whole CPU-LBS deform chain, the
>   inverse-bind cancellation property, both bind/animated world walks, the `.mot` keyframe sampler,
>   the major/minor split + per-vertex normalization, the per-mesh and per-node scale sources, and the
>   quaternion conventions (XYZW / Hamilton / active-rotation / parent-on-left) — all re-read from the
>   function bodies this pass and reproduced exactly. *confirmed (static)* — as of CYCLE 1 — for the
>   inverse-bind **bake** itself: the bake pass is now pinned statically (a separate skin-attach pass
>   that runs after the bind world transforms exist and before any deform), and its `(−x,−y,−z,w)`
>   conjugate / subtract-then-rotate order is read directly from the routine — see §4 and the
>   corrected status row below. *confirmed (visual-oracle, 2026-06-21)* for the `.skn` geometry
>   height-axis: the rest-mesh is authored **X-TALL** (native-X height), requiring a **+90°-about-Z
>   importer remap** to stand the avatar up — see the CORRECTED note below and §7/§8(b)/§9. The matrix
>   major-order, the exact Godot quaternion remap under Z-negation, and the three epsilon tests remain
>   *capture/debugger-pending* (the epsilons surface in disassembly as a log-shaped intrinsic compared
>   against 0.001 — almost certainly a decompiler mis-symbol of an absolute-value epsilon clamp at 0.001).
> - **CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19):** inverse-bind bake pinned static
>   (no longer a hypothesis); a `.skn` weight's bone index is a base-relative bone-ID
>   (`bone_array[id − base_id]`), NOT an array slot / palette / track index; skeleton selection is the
>   `.skn` header `id_b` used VERBATIM as the pose-pool key. These three corrections jointly **retire
>   the skinning-explosion debt** — the avatar can now be animated from the static recovery.
> - **CORRECTED 2026-06-21 (visual-oracle) — the `.skn` GEOMETRY height-axis is native X, NOT Y.**
>   Measured against the visual oracle (raw `.skn` rest-mesh bytes via the asset extractor + the
>   displayed-frame AABB through the production deform path), the mesh's height axis runs along **native
>   X**: the raw rest-mesh extent is tall-along-X *before any deform or handedness conversion* (e.g. the
>   g1 player body ≈ X 5.0 > Y 2.4 > Z 1.7, skeleton g1.bnd = 84 bones, clip 84 tracks), and the deformed
>   frame-0 AABB at an identity importer pivot is likewise tall-X (recumbent). The inverse-bind
>   cancellation (INV1) PASSES at the float-noise floor, so this is a **PURE ORIENTATION property of the
>   asset data**, NOT a deform-math or wrong-skeleton bug. **A faithful Godot import MUST apply a
>   +90°-about-Z importer (display-node pivot) stand-up remap** to map native +X height onto Godot +Y
>   (verified: AABB tall-X → tall-Y; screenshot +90° upright, −90° upside-down). This **CORRECTS** the
>   CYCLE-7 "the UP-AXIS is RESOLVED to Y-up / identity import / the correct up-axis conversion is the
>   IDENTITY" claim wherever it appears (this banner, §7, §8(b), §9): the engine's **Y-up is real but for
>   a DIFFERENT quantity** — it is the WORLD-PLACEMENT / HEADING convention (yaw about Y, ground plane XZ,
>   facing from the XZ planar delta), which is KEPT and governs how an actor is placed/turned. The `.skn`
>   geometry height-axis is the separate quantity §7/§9 had left as a `capture/debugger-pending` label,
>   now settled to native X. The "avatar lies on X" is the **as-authored** axis, NOT a port-added spurious
>   rotation — the fix is to **ADD the +90°-Z remap**, never to chase a non-existent upstream rotation to
>   remove. The deform / inverse-bind math (§0/§4/§5) is UNTOUCHED; only the importer orientation remap
>   changes. A future engineer MUST NOT re-zero `UpAxisRemapDeg` on stale "identity" wording.
> - **re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20).** Three previously-open
>   items are now closed at the per-vertex level: **(1) the full per-vertex deform chain is CONFIRMED
>   end-to-end** — the on-disk `.skn` influence packing, the per-vertex weight drop (< 0.01) +
>   normalize-to-1.0, the inverse-bind bake form and its storage slots, the runtime two-pass LBS (major
>   pass WRITES, minor pass ACCUMULATES), right-handed Hamilton (XYZW scalar-last), parent-LEFT
>   pre-multiply bone-world compose, per-mesh scale = 1.0 (uniform override only), and the base-relative
>   `bone_array[id − base_id]` resolve are all confirmed *inside the runtime deform itself*, not just the
>   bake (§0, §4, §5, §7). **(2) two DISTINCT axis quantities (CORRECTED 2026-06-21, visual-oracle).**
>   (2a) The engine's **WORLD-PLACEMENT / HEADING convention is Y-up** (yaw is a pure rotation about Y;
>   the ground plane is XZ; facing is the XZ planar delta; the render forward basis is +Z; the device is
>   left-handed D3D); there is no runtime character root rotation beyond the Y-yaw heading. This governs
>   actor placement/turning and is KEPT. The "−Z world / −X mesh-local" flips are port-side
>   handedness/forward reconciliations that do not touch the placement up axis (§7, §8(b)). (2b) The
>   **`.skn` GEOMETRY height-axis is native X** (visual-oracle-settled) — the rest-mesh is authored
>   X-TALL, so the importer must apply a **+90°-about-Z stand-up remap** (native +X height → Godot +Y).
>   This SUPERSEDES the earlier "up-axis RESOLVED to Y-up / identity import" reading of the asset geometry
>   (see the CORRECTED note above and §7/§8(b)/§9). **(3) the standing-idle column is CORRECTED to
>   column 16** (in-memory record +0x44 = direction array A element 1), keyed by the **appearance key**
>   (not col2/skin_class), resolved via the **motlist.txt clip registry** (not a `g{id}.mot` sprintf) —
>   see §8(e) and §10.
> - **Idle-animation lane (added 2026-06-16; slot/key CORRECTED by CYCLE 7 below):** *confirmed*
>   (control-flow) that the engine feeds the anim mixer real per-frame elapsed time (`dt = ms × 0.001`)
>   and advances each active layer's clock every frame, so the keyframe sampler is never pinned at
>   `t = 0` (`formats/animation.md` §Per-frame clip-time advance); *sample-verified* (production-parser
>   keyframe diff) that the observed human stand idle `g101100001.mot` is **static data** (0/84 animated
>   tracks). ⚠️ The original "col15 / `motion_ids_a[0]`, debugger-pending slot" framing of this bullet is
>   **SUPERSEDED by CYCLE 7**: the runtime stand slot is **column 16 (record +0x44, array A element 1)**,
>   keyed by the **appearance key**, resolved via the **motlist.txt registry** (not `g{id}.mot`), and the
>   slot question is RESOLVED static (col15 / element 0 is statically dead). See §8(e) and §10.
> - **ida_reverified:** 2026-06-24 (audit pass — TWO distinct bone resolvers with different out-of-range behaviour; commit/accumulate denominator guard is a genuine `logf` call, not a decompiler mis-symbol; VB lock budget and bone-id read-width nuances recorded; all prior load-bearing facts re-confirmed with no drift)
> - **spec_corrected:** 2026-06-21 (visual-oracle — `.skn` geometry height-axis = native X; +90°-Z importer remap)
> - **ida_anchor:** 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
> - **evidence:** [static-ida, vfs-sample, visual-oracle]
> - **conflicts:** two resolved against the IDB this pass — (1) the out-of-range bone-id behaviour is a
>   **clamp-to-last-bone** in the engine, NOT a skip (the spec previously implied the engine skips;
>   "skip" is retained only as the *recommended importer hardening*, §8(e)); (2) the child-bone
>   translation lock is **interior-bone-only** (a bone with both a parent and a grandparent and at
>   least one child), narrower than the previous "every non-root bone" phrasing. The core math
>   (deform equation, quaternion order, Hamilton product, active rotation, both world walks, scale
>   source, raw-seconds alpha, 28-byte keyframe, XYZW) was reproduced with **no correction**. A third
>   conflict was resolved against the **visual oracle** on 2026-06-21: the `.skn` geometry height-axis is
>   **native X** (requiring a +90°-Z importer remap), correcting the CYCLE-7 "identity / Y-up import"
>   reading of the asset geometry (the placement/heading Y-up convention is unaffected).

Neutral, data-only model of how the legacy *Martial Heroes* client **deforms and animates** skinned
characters: how the bind pose is built from a `.bnd` skeleton, how the inverse-bind transform is
baked at load, the linear-blend-skinning deform equation, how `.mot` keyframes are sampled and
interpolated, the pose-composition order up the bone hierarchy, and the quaternion / handedness
conventions that make the whole thing internally consistent. Promoted from dirty-room notes and
rewritten in our own words — no decompiler identifiers, no binary addresses.

This document is design input for the **assets-mapping engineer** (`Assets.Mapping`, the bridge to
glTF / Godot `ArrayMesh` + `Skeleton3D`) and the **Godot presentation engineer** (layer 05). It is
the math companion to the container specs:

- `formats/mesh.md` — the on-disk bytes of `.skn` (skinned mesh) and `.bnd` (bind-pose skeleton).
- `formats/animation.md` — the on-disk bytes of `.mot` (skeletal animation clip) plus the runtime
  mixer model.

This spec describes the **math that consumes those bytes**, not the bytes themselves. No on-disk
field is redefined here; where an offset is named it is named only to point at the field already
documented in the container spec.

---

## Status header (read first)

> **Headline finding — the cancellation property is the whole game.**
> The legacy renderer is **CPU linear-blend skinning (LBS)** with a **load-time inverse-bind bake**.
> At rest (animation == bind pose) the inverse-bind and the forward bone transform **cancel exactly
> to the identity**, so the rest mesh is reproduced unchanged. A naïve skinning setup that skins
> *without* that cancellation property — a missing inverse-bind step, a wrong bone-transform multiply
> order, or a handedness conversion applied piecemeal to verts but not to bones (or vice-versa) —
> explodes the mesh. The fix is to reproduce the cancellation, applying ONE handedness conversion
> uniformly to bones + vertices + keyframes. See §8 (Godot import guidance).
>
> **Mesh-explosion status (RETIRED — now from a complete static recovery).** The earlier Godot
> mesh-explosion debt is **retired**, and as of CYCLE 1 the three facts that retire it are all
> **CONFIRMED static** — the avatar can be animated entirely from the static recovery, with no live
> read required: (1) the inverse-bind **bake** is pinned (§4: subtract bind-world translation then
> rotate by the conjugate bind-world quaternion); (2) a `.skn` weight's bone index is a **base-relative
> bone-ID** resolved as `bone_array[id − base_id]` — NOT an array slot, palette index, or track index,
> and the weight id-space is the **same** base-relative space the bind skeleton and `.mot` tracks use
> (§3.2); (3) the deform skeleton is selected by the skin's `id_b` used **verbatim** as the pose-pool
> key (§8(e)). Feeding array positions instead of base-relative IDs, OR pairing the skin with a
> wrong-`id_b` skeleton, is exactly what reproduces the explosion. The port already renders correctly
> via quaternion LBS that preserves the §0 cancellation. The remaining character-animation observation
> — a standing human looks static — is a **separate and faithful** matter: the standing-idle clip the
> recovered chain resolves is genuinely static data (§10), so a frozen standing human is correct for
> that asset, not a skinning/animation defect. The only open animation question is which idle slot the
> live engine selects at runtime (§10, DEBUGGER-PENDING).

| Area | Confidence |
|---|---|
| CPU LBS, no GPU bone palette, no 4×4 matrices in the skinning math | HIGH (re-confirmed CAMPAIGN 10) |
| Inverse-bind **baked into** per-influence bone-local rest position/normal; consumed by the deform with no per-frame inverse | HIGH (deform consumes bone-local rest; the load fields start zeroed → a bake pass populates them) |
| Inverse-bind **bake** (its existence, conjugate form, and subtract-then-rotate order) | **CONFIRMED (static), CYCLE 1** — pinned as a separate skin-attach pass; conjugate `(−x,−y,−z,w)`, subtract bind-world translation then rotate by the inverse bind-world quaternion (supersedes the prior STATIC-HYPOTHESIS) |
| Bind-pose world transform accumulated from parent-relative `.bnd` locals | HIGH (re-confirmed CAMPAIGN 10) |
| Bones addressed by **bone ID** in base-relative ID space (`bone_array[id − base_id]`), NOT an array slot / palette index / track index, by both `.skn` weights and `.mot` tracks | **CONFIRMED (static), CYCLE 1** (the explosion root cause — weight id-space == bind/`.mot` id-space) |
| Skeleton selection: `.skn` header `id_b` passed VERBATIM as the pose-pool key (no `g{N}.bnd` formatting, no slot transform at the resolve site) | **CONFIRMED (static), CYCLE 1** (§8(e)) |
| Runtime pose bone stride **88 bytes**; in-memory bind bone **72 bytes**; bone count is a single **u8** (≤ 255 bones) | HIGH (recovered CAMPAIGN 10 — see §3.4) |
| Major/minor influence split + per-vertex normalization to sum 1.0; drop weight < 0.01 | HIGH (code) + SAMPLE-VERIFIED (corpus: min weight 0.010, 1140 multi-weight skins) |
| LBS deform equation (weighted sum of bone-local rest placed by animated bone world transform) | **CONFIRMED end-to-end at the per-vertex level (CYCLE 7)** — runtime two-pass deform read directly: major pass WRITES, minor pass ACCUMULATES; `vertex_world = Σ wᵢ·(boneWorldQuat·(restPos·scale)+boneWorldTrans)`, normals same without translation |
| `.mot` sampling: `floor(t·10)` @ 10 fps, LERP translation, shortest-arc SLERP rotation, 28-byte keyframe | HIGH (re-confirmed) |
| Per-frame clip time `t` advances (real `dt = ms × 0.001`; mixer ticked every frame; never pinned to 0) | HIGH (control-flow-confirmed — `formats/animation.md` §Per-frame clip-time advance) |
| Standing/stand-still idle clip = actormotion **column 16** (in-memory record +0x44, direction array A element 1), keyed by the **appearance key** (not col2/skin_class), resolved via the **motlist.txt clip registry** (motion id == `.mot` header id_b), NOT a `g{id}.mot` sprintf | **CONFIRMED static (CYCLE 7)** — every motion-kind-0 idle read-site reads record +0x44; record +0x40 (col15, array A element 0) has ZERO read-sites (statically dead). Supersedes the prior "col15 / `motion_ids_a[0]`" claim and the `g{id}.mot` resolution assumption (§8(e), §10) |
| Which idle slot the live engine plays for a standing human | RESOLVED statically to the column-16 stand clip (record +0x44); the live-vs-static slot question is settled by the static read-site evidence (§10) |
| Interpolation alpha is RAW seconds in `[0, 0.1]`, not renormalized to `[0,1]` | HIGH (observed); intentional-vs-defect UNVERIFIED |
| Pose composition: `parentWorld ⊗ bindLocal ⊗ animLocal`; **interior** bones rotate-only; root + leaf/near-root translate | HIGH (lock narrowed to interior bones, CAMPAIGN 10 — §6.3) |
| Quaternion convention: XYZW (scalar W last), Hamilton product, active rotation, parent-on-left | HIGH (re-confirmed) |
| Out-of-range bone id is **clamped to the last bone** (NOT skipped) | HIGH (re-confirmed CAMPAIGN 10 — the engine clamps; "skip" is importer hardening only, §8(e)) |
| NO axis flip inside the skinning math | HIGH — there is no single-axis negation or remap *inside* the deform/bake math; bone space and rest-mesh space are the same native space (§7). This is distinct from the importer orientation knob below |
| `.skn` GEOMETRY height-axis (raw rest-mesh up axis) | **CORRECTED 2026-06-21 (visual-oracle): native X** — the rest-mesh is authored X-TALL, so a faithful import applies a **+90°-about-Z importer remap** (native +X height → Godot +Y). This SUPERSEDES the prior "RESOLVED to Y-up / identity import" reading. It is a pure asset-orientation property (inverse-bind cancellation passes at the float-noise floor); §0/§4/§5 deform math is untouched — only the importer remap changes (§7, §8(b), §9) |
| World-PLACEMENT / HEADING convention | **Y-up (KEPT)** — yaw is a pure rotation about Y, the ground plane is XZ, facing is the XZ planar delta, the render forward basis is +Z, the device is left-handed D3D; no runtime character root rotation beyond the Y-yaw heading. Governs how an actor is placed/turned — a DIFFERENT quantity from the `.skn` geometry height-axis above (§7) |
| Exact Godot quaternion remap under Z-negation | PROPOSED — validate on one sample bone |
| Per-mesh `scale` real source (read at attach as `nodeScale · meshScale · optionalOverride`) | CONFIRMED — resolves the prior "assumed 1.0" open item |
| Per-**node** scale (distinct from per-mesh scale), applied in the animated world walk | HIGH (recovered CAMPAIGN 10 — §6.6) |
| Whole deform/bind/mot/world-walk chain re-derived end-to-end | RATIFIED twice — CAMPAIGN 9 then CAMPAIGN 10 reproduced §0–§7; only the two conflict wordings were corrected |

> **Ratification (CAMPAIGN 9, then CAMPAIGN 10).** Two independent dirty-room re-derivations read the
> actual deform, bind-pose, `.mot`, and world-walk routines (and their math primitives) from scratch.
> Both **confirmed this spec's core math is correct** — the deform equation, the quaternion product
> order (parent-on-left, XYZW Hamilton), the active-rotation `q ⊗ v ⊗ q⁻¹` vector transform, the
> `.mot` 10 fps / 28-byte-keyframe / raw-seconds-alpha sampling, the animated world walk
> `parentWorld ⊗ bindLocal ⊗ animLocal`, and the no-axis-flip-inside-the-math finding were all
> reproduced. CAMPAIGN 9 additionally resolved the per-mesh `scale` source (see §5.3 / §9). The
> CAMPAIGN 10 static re-verification reproduced §0–§7 end-to-end and corrected exactly **two**
> wordings — the out-of-range bone-id behaviour (the engine **clamps to the last bone**; the prior
> spec implied a skip, which is retained only as importer hardening, §8(e)) and the translation lock
> (it is **interior-bone-only**, narrower than "every non-root bone", §6.3) — plus surfaced the
> structural facts now recorded in §3.4 (88-byte runtime bone, 72-byte in-memory bind bone, u8 bone
> count, the per-node scale, and the rotate-then-scale literal order in the world walk). Recovered via
> static RE.

Open items are consolidated in §9. Korean strings referenced indirectly (bind/skin names) are
**CP949 / EUC-KR** (no BOM), consistent with `formats/config_tables.md`.

---

## 0. The one load-bearing rule

Per render vertex `v`, the final deformed world position is the weighted sum over its influences `i`:

```
vertexWorld(v) = Σ_i  w_i · ( boneWorldQuat_i ⊗ ( localPos_i · scale ) + boneWorldTrans_i )
```

where each `localPos_i` was **baked once at load** by the inverse-bind:

```
localPos_i    = bindWorldQuat_i⁻¹ ⊗ ( restModelPos(v) − bindWorldTrans_i )
localNormal_i = bindWorldQuat_i⁻¹ ⊗   restModelNormal(v)
```

and each `boneWorld*_i` is rebuilt every frame from the animated bone hierarchy:

```
boneWorldQuat  = parentWorldQuat ⊗ boneLocalQuat            (parent on the LEFT)
boneWorldTrans = parentWorldQuat ⊗ boneLocalTrans + parentWorldTrans
```

There is **no 4×4 matrix** and **no GPU bone palette** in the legacy path. Everything is unit
quaternions and 3-vectors on the CPU; the deformed vertices are uploaded as a dynamic vertex buffer,
and the GPU sees only a single composite world·view·projection matrix for the whole object.

The cancellation property (the reason it does not explode at rest): if the animated bone world
transform equals the bind world transform — which is the case for any bone with no animation track,
i.e. the entire idle pose — then

```
boneWorldQuat ⊗ ( bindWorldQuat⁻¹ ⊗ (p − bindWorldTrans) ) + bindWorldTrans  =  p
```

i.e. the rest position is reproduced exactly. **This identity is what a naïve skinning setup lacks.**

> **CONFIRMED end-to-end at the per-vertex level (CYCLE 7).** Every link of this rule — the on-disk
> influence packing, the per-vertex weight drop (< 0.01) and normalize-to-1.0, the inverse-bind bake
> (and its storage slots, §4), the runtime two-pass Linear Blend Skinning (the MAJOR pass writes the
> destination vertex, the MINOR pass accumulates into it), the right-handed Hamilton quaternion
> operators (XYZW scalar-last), the parent-LEFT pre-multiply bone-world composition, the per-mesh scale
> (uniform, default 1.0), the absence of any single-axis flip or axis remap, and the base-relative
> `bone_array[id − base_id]` bone resolve — was read directly out of the runtime deform routine, not
> merely the load-time bake. The deform chain is fully pinned for a 1:1 port; see §4 (bake), §5 (deform
> loop), §7 (convention dictionary). NOTE: "no axis flip inside the math" is about the *deform math*; it
> is a separate matter from the importer-layer **+90°-about-Z stand-up remap** the `.skn` geometry
> height-axis requires (native-X-tall → Godot +Y) — see §7/§8(b)/§9.

---

## 1. Where skinning happens (CPU, not a shader)

The legacy engine skins on the CPU, once per actor per frame:

1. Build the animated **pose** for the frame: sample every active clip's tracks, blend them per bone,
   commit the blended local pose, then walk the hierarchy root→leaf to produce each bone's animated
   **world** transform (translation + quaternion). (§6)
2. Lock the actor's dynamic vertex buffer and run the **deform loop** (§5): for each skinned mesh, for
   each influence record, transform the baked bone-local rest position by that bone's animated world
   transform, scale by the influence weight, and accumulate into the destination vertex.
3. Upload the deformed vertices and draw with a single world·view·projection matrix.

There is **no vertex-shader skinning path**. The shader only transforms the already-skinned
position/normal/UV by the composite matrix. Consequently the entire bind / weight / hierarchy
convention is fully expressed on the CPU side and is what this spec documents.

The deform loop has three variants selected by a per-actor blend-mode word:

- **Mode 0 — full LBS** (general correct path; documented in §5.3).
- **Mode 1 — rigid, major-influence-only** (each vertex rigidly follows its single dominant bone).
  This path transforms each major vertex rigidly as `boneWorldQuat ⊗ (localPos · scale) +
  boneWorldTrans` with **NO `· weight` factor** — consistent with a single-influence rigid follow
  (one bone owns the vertex outright, so its weight is effectively 1.0).
- **Mode 2 — rigid fast path** using a position-proximity ownership table (single-bone-owned vertices
  transformed once, then copied to merged duplicates).

The per-actor mode word is read from a fixed field on the actor object each frame and dispatches to the
matching deform routine. For a faithful modern re-implementation, **Mode 0 is the one to port**; Modes
1 and 2 are optimizations of the single-influence case and produce the same result whenever every
vertex has exactly one influence.

---

## 2. The vertex stream and the runtime influence record

### 2.1 Render vertex stride

The rest-pose render vertex (and the CPU scratch vertex the deform loop writes into) is **32 bytes**:

| Sub-offset | Size | Field |
|---:|---:|---|
| 0  | 12 | position (3 × f32) |
| 12 | 12 | normal   (3 × f32) |
| 24 | 8  | UV       (2 × f32); the engine stores `v` as `1.0 − v` (D3D convention) |

This 32-byte render vertex is **derived** from the on-disk `.skn` 24-byte vertex (normal-then-position;
see `formats/mesh.md` §Vertex table) plus the per-corner UVs from the face table. Concretely the
loader writes the render position from the on-disk vertex's **last** three floats and the render
normal from its **first** three floats (the disk record is normal-then-position), and stores
`uv.v` as `1.0 − v`. Render vertices are **deduplicated by position** (an absolute-value epsilon of
≈ 0.001 per axis) so shared triangle corners collapse to one skinned vertex; a corner→unique-vertex
index map and a vertex→owner (rigid-merge) table are built at load.

> **Epsilon-test note (partially resolved).** Three epsilon tests in this pipeline:
>
> - The **accumulate-blend denominator floor** (§6.2) and the **commit denominator floor** (§6.2) are
>   now **confirmed to use a genuine `logf` call** (real CRT logarithm function, static-confirmed):
>   the floor branch is taken when `logf(committedWeight + accumWeight) < 0.001`, and the denominator
>   is floored to `0.001` in that branch. The 0.001 floor is real; the guard that selects it is the
>   log of the total accumulated weight. The earlier "almost certainly a decompiler mis-symbol of an
>   absolute-value epsilon clamp" framing for these two floors is **dropped** — the `logf` is genuine.
>   Practical effect: at vanishing accumulated weight the denominator is floored to 0.001, avoiding
>   divide-by-zero; the blending outcome is behaviorally equivalent to an epsilon clamp for typical
>   weights. See §6.2.
>
> - The **per-axis vertex-dedup tolerance here** (this §2.1 test) still surfaces as a log-shaped
>   intrinsic compared against 0.001 and has **not** been re-checked this audit pass. Whether it is
>   also a genuine `logf` or a plain absolute-value compare remains `capture/debugger-pending`; treat
>   it as a 0.001 per-axis absolute-value tolerance until confirmed.

### 2.2 Runtime influence (weight) record

The 12-byte on-disk `.skn` weight record (`vertex_index`, `bone_index`, `weight`; see `formats/mesh.md`
§Weight / skin table) is expanded at load into a **runtime influence record** carrying nine floats:

| Field | Source | Meaning |
|---|---|---|
| `bone_id` | on-disk `bone_index` | addresses the bind / pose bone by **ID** (`id − base_id`, §3) — NOT a palette slot, NOT a track index |
| `vertex_index` | on-disk `vertex_index` | the unique render vertex this influence affects |
| `weight` | on-disk `weight`, then normalized | scalar influence; per-vertex influences sum to 1.0 after load |
| `localPos` (3 × f32) | computed by the inverse-bind bake (§4) | rest position expressed in this bone's local frame |
| `localNormal` (3 × f32) | computed by the inverse-bind bake (§4) | rest normal in this bone's local frame |

Influences are stored in two arrays — a **major** array (one dominant influence per vertex) and a
**minor** array (the remaining influences) — see §5.2. Both array entry types are identical in layout.

> The runtime record is an internal optimization of the legacy CPU path. A modern engine that lets
> its own skeleton skin (Godot `Skeleton3D`) does **not** reproduce `localPos`/`localNormal`; it
> feeds bone weights + indices + rest poses and lets the engine compute the inverse-bind. See §8.

---

## 3. Bind pose and bone hierarchy

### 3.1 `.bnd` transforms are parent-relative (local). The world transform is computed at load.

Each `.bnd` bone stores a **parent-relative** rest translation and rest rotation (quaternion) — see
`formats/mesh.md` §Bone array, fields `local_trans_*` and `local_rot_*`. No world-space transform is
stored on disk. The bind **world** transform is accumulated down the tree at load:

```
# root bone:
worldTrans = localTrans
worldQuat  = localQuat

# every non-root bone:
worldTrans = parentWorldQuat ⊗ localTrans + parentWorldTrans
worldQuat  = parentWorldQuat ⊗ localQuat
```

i.e. rotate the bone's local offset by the parent's world rotation, then add the parent's world
position; compose the parent's world rotation with the bone's local rotation, parent on the left.

### 3.2 Hierarchy is encoded by `parent_id`, and bones are addressed by ID — not array index

Each `.bnd` bone carries a `self_id` and a `parent_id` (low byte meaningful; see `formats/mesh.md`).
The tree is built by linking each bone under the bone whose `self_id` equals its `parent_id`; the root
is the bone with `self_id == parent_id == 0`. An unmatched `parent_id` is a fatal error in the legacy
client.

**Bone lookup is by ID offset, `bone_array[ id − base_id ]`**, where `base_id` is the first bone's
`self_id`. This is the single most important indexing fact for the importer — and the **root cause of
the skinning explosion** (CONFIRMED static, CYCLE 1):

- A **`.skn` weight's `bone_index` is a bone ID in base-relative bone-ID space**, resolved as
  `bone_array[id − base_id]` — NOT an array slot, NOT a palette index, and NOT a track index.
- A **`.mot` track's `bone_id`** (low byte of the track descriptor) is the same bone ID, in the
  **same** base-relative space; the weight id-space, the bind-skeleton id-space, and the `.mot`-track
  id-space are one and the same (all three resolve through the `id − base_id` resolver).
- Feeding **array positions** instead of base-relative bone-IDs, OR pairing the skin with a
  **wrong-`id_b` skeleton** (§8(e)), reproduces the explosion. This retires the skinning-explosion
  debt: with the correct base-relative resolve and the correct `id_b`-selected skeleton, the avatar
  deforms and animates correctly from the static recovery.

> **DEBT#1 (skinning math) — CLOSED, re-confirmed end-to-end (static IDA, 2026-06-21).** A dedicated
> binary-verdict re-walk re-confirmed every load-bearing element from the static recovery — the on-disk
> `.skn` vertex/bone/weight packing, the base-relative bone-ID resolve (`id − base_id`, **not** an array
> index), the inverse-bind bake (conjugate quaternion, subtract-then-rotate, baked per-influence), the
> deform multiply order (active-rotate → add bone world-translation → scale by weight; parent-on-left;
> XYZW Hamilton; right-handed; no axis flip), the `.mot` keyframe sampler, and the pose compose. **Nothing
> in the recovery is unproven.** The **char-select preview path** provides an **independent second witness**:
> the preview's idle-motion apply derives the appearance key from the actor's appearance slot, looks up the
> animation catalogue by that key, and plays the **column-16 stand clip (catalogue record +0x44)** on the
> skeleton selected by the skin's **`id_b` used verbatim** as the pose-pool key — i.e. the exact same
> `id_b`-verbatim skeleton selection and the same col-16 idle that the in-world path uses. So the five
> char-select avatars' skinning + idle is **fully pinned** by two independent read-sites.
>
> **The "mesh explodes" note is OBSOLETE; the "static-upright / recumbent" symptom is the `.skn`
> geometry's native-X height-axis (CORRECTED 2026-06-21, visual-oracle).** Any remaining tooling/agent
> text describing the avatar as *exploding* predates this recovery and refers to a **port-side**
> implementation bug (the classic port causes below), **not** a recovery gap. The separate
> *static-upright / lies-on-X (recumbent)* symptom is now SETTLED: the `.skn` rest-mesh is authored
> **X-TALL** (height along native X), so a faithful import must apply a **+90°-about-Z importer
> stand-up remap** (native +X height → Godot +Y) — this is the as-authored asset orientation, NOT a
> spurious port rotation to remove (§7, §8(b)). The classic explosion causes (all preventable) are:
> feeding Godot bone array indices instead of base-relative IDs; dropping or mis-ordering the
> inverse-bind; applying the handedness flip to vertices but not bones (or vice-versa) instead of one
> uniform conversion to bones+verts+keyframes; pairing the `.skn` with a wrong-`id_b` skeleton; or
> sampling a started clip at a frozen `t = 0`. With the recovery above applied AND the +90°-Z stand-up
> remap, the mesh deforms, stands upright, and animates correctly.

For the recovered sample skeletons `base_id == 0`, so ID equals array index — but the importer **must
not assume** `base_id == 0` in general. Always resolve `bone_array[id − base_id]`.

> **Two distinct resolvers exist with DIFFERENT out-of-range behaviour — use the right one per context.**
>
> - **Runtime pose resolver (88-byte stride) — CLAMPS.** Used by the deform loop (Mode 0/1/2), the
>   `.mot` track binders, and the attach-point composers. When `id − base_id ≥ bone_count` it returns
>   the **last** bone (`pose_bone_base + 88 · bone_count − 88`). Downstream null-guards never fire on
>   a clamp, so an out-of-range `.mot` track id or `.skn` weight id silently drives the last bone.
>   This is a faithful-behaviour fact for the deform/.mot path; an importer should prefer to **skip**
>   such an influence instead (§8(e) step 4).
>
> - **Bake-time bind resolver (72-byte stride) — returns NULL (no clamp).** Used exclusively by the
>   inverse-bind bake (§4) and by the bind-pose hierarchy builder. When `id − base_id ≥ bone_count`
>   it returns NULL. The bake has **no null guard** at the call site, so a `.skn` weight whose bone id
>   falls outside the bind skeleton's id window is a hard fault at bake time, not a silent clamp. An
>   importer must skip such records defensively (§8(e) step 4).
>
> The deform loop reads the influence bone id as a **full 32-bit value** before passing it to the
> runtime resolver. The bake reads the same field as a **single byte**. Both treat the field as a
> small unsigned bone id (the u8 domain of §3.4 — valid bone ids fit in one byte), so there is no
> behavioral difference for well-formed skins; only the read width differs per code path.

### 3.3 Composition order (multiply convention)

Hierarchy composition is **child = parent ∘ local, parent on the LEFT** (pre-multiply / row-vector
style). This holds for both the bind-pose world walk (§3.1) and the animated world walk (§6.6).

### 3.4 In-memory layouts (runtime — distinct from the on-disk `formats/mesh.md` records)

These are the **runtime** structures the skinning math walks; the on-disk `.bnd`/`.skn` byte layouts
are owned by `formats/mesh.md` and are NOT redefined here. An importer that bakes scale or pose into
its own skeleton needs these to map fields correctly.

- **Runtime pose bone stride = 88 bytes.** The runtime pose allocates `bone_count · 88` bytes and the
  ID-offset resolver strides by 88 (so the resolver address is `pose_bone_base + 88 · (id − base_id)`,
  §3.2). The runtime bone field map (byte offsets within the 88-byte record):
  - **+16** — back-pointer to its source bind bone.
  - **+28..+39** — local **animated** translation (3 × f32).
  - **+40..+55** — local **animated** quaternion (4 × f32, XYZW).
  - **+56..+67** — **world** translation (3 × f32) — the `boneWorldTrans` the deform reads.
  - **+68..+83** — **world** quaternion (4 × f32, XYZW) — the `boneWorldQuat` the deform reads.
  - **+84** — per-**node** scale (f32), applied in the animated world walk (§6.6).
  - During a mixer pass an accumulator overlay aliases the same 88-byte record (a running accumulated
    weight, accumulated translation/quaternion, a committed weight, and a blend fraction); these are
    transient mixer scratch, not persisted pose state.
- **In-memory bind bone = 72 bytes.** The bind-pose load strides the source bind bone by 72 bytes
  while copying locals. The in-memory bind bone carries a parent pointer, a child/sibling link, the
  parent-relative local translation and local quaternion (XYZW), and the **computed** world
  translation and world quaternion appended (the world slots are filled by the bind-pose world walk
  §3.1, not stored on disk). The on-disk bind record is the smaller `formats/mesh.md` number; the
  72-byte figure is the in-memory bone with its world slots appended.
- **Bone count is a single byte (u8).** The skeleton's bone count is read as an unsigned 8-bit value
  and the build loop iterates a u8 — so a skeleton holds **at most 255 bones**. The §8(d) sample rigs
  (82–89 bones) are well under the cap. Importers should treat the bone count as 8-bit.

---

## 3.5 Character appearance assembly — one shared skeleton + up to 6 overlay parts (CODE-CONFIRMED)

> Provenance: promoted from a dirty-room appearance-assembly note (gitignored). The catalogue
> population, overlay-slot set, and `model_class_id` formula are **CODE-CONFIRMED**; the two
> data-driven value-edges noted at the end remain pending a live-debugger confirm.

A rendered character — whether an in-world actor or a char-select preview — is **one shared
skeleton** carrying a **fixed set of overlay `.skn` parts**, one per visible-gear slot, each skinned
onto that single skeleton (§3, §4, §5) and textured via a per-part texture id (`formats/texture.md`).
There is **no monolithic body mesh**: the body is itself an overlay part. Everything resolves through
one in-memory **appearance catalogue** (a gid -> visual map) plus three registry caches loaded once at
boot — skin, texture (`formats/texture.md`), and motion (`formats/animation.md`).

### 3.5.1 The body is overlay slot 3 — there is no separate base mesh

A full character is composed of **up to six overlay parts**, attached in a fixed slot order. Slot 3
is the **body** (the torso/legs mesh, the `202`/"b" family); it is attached through the **same**
overlay path as every other part, not loaded as a distinct base mesh. `skin.txt` is consumed to
**build the catalogue**, not to scan a body row at draw time; at draw time the body is just slot 3 of
the uniform overlay set.

| Slot id | Outfit family | Meaning | Present on |
|--------:|---------------|---------|-----------|
| 3 | `202` ("b") | **BODY** (torso/legs) | every actor; sole slot in the reduced high-tier composition |
| 4 | `203` ("p") | layer "p" | every actor |
| 6 | `206` ("s") | layer "s" | every actor |
| 2 | `209` ("a") | layer "a" | every actor |
| 11 | (head / hair / face family) | head overlay | every actor |
| 14 | (weapon) | WEAPON, hand-attached | **local player only** |

Other actors omit slot 14 (weapon); above a per-character skin-level threshold read from the
catalogue, the local-player path binds **only slot 3** (a reduced high-tier composition). All of a
character's overlay parts are authored against the **same skeleton** (the class's `id_b` rig — §8(e));
the cancellation invariant of §0 holds only when every part, the deform skeleton, and the played clip
share that one `id_b`.

### 3.5.2 model_class_id (the appearance/skeleton selector)

The class + variant of a player resolve to a single integer `model_class_id` (also the value a
`.skn` carries as its `id_b`):

```
model_class_id = 5 * (class + 4 * variant) - 24            in {1, 11, 16, 26}
```

- `class` is the internal class index (1..4); `variant` is the appearance variant.
- `variant == 3` resolves to `0`, which means an **invisible actor** (no mesh) — a reserved sentinel.
- `model_class_id` selects the visual record in the appearance catalogue, whose bound bind-pose
  handle is the actor's skeleton (§3, §8(e)). Note this slot transform is an **upstream** appearance
  decision (it chooses *which* `.skn`/`id_b` an actor uses); it is **not** re-applied at the
  skin-load / skeleton-resolve site, where the skin's `id_b` is the verbatim pool key (§8(e)).

### 3.5.3 The appearance catalogue is populated from skin.txt (CODE-CONFIRMED)

`data/char/skin.txt` is parsed once at boot into the appearance catalogue. Each row carries (in disk
order) an appearance group id, a class id, and two further catalogue digits, followed by the two
identity columns the draw path needs: **column 4 = the mesh gid** (`g{gid}.skn` is the part's mesh)
and **column 5 = the texture id** (`tex_id`, looked up in the texture registry, `formats/texture.md`).
The payload stored under each catalogue key is the `(mesh_gid, tex_id)` pair.

Each row is filed under a 64-bit catalogue key. The same key shape is reconstructed by the overlay
draw path from `(slot, model_class_id, reduced_gid)`:

```
catalog_key = gid_reduced + 1e9 * ( slot + 100 * model_class_id )
```

so a part registered from skin.txt under a given `(class, slot, reduced gid)` is found again at draw
time by recomputing the same key. The `model_class_id` term in the key is itself built from a
per-appearance-group base offset table (`categoryBase[]`, indexed by the appearance group id) held on
the catalogue object — the **same** base-offset table the actormotion table uses
(`formats/animation.md`). The exact contents of `categoryBase[]` are **UNVERIFIED**.

### 3.5.4 Overlay attach order and the reduced gid

The factory iterates the fixed slot list `{3, 4, 6, 2, 11, 14}` and, per slot, reads the slot's gid
from the actor's equipment table, computes the catalogue key above, looks up the `(mesh_gid, tex_id)`
payload, loads/caches the `.skn` mesh by gid, skins its geometry onto the shared skeleton (§4, §5),
and binds the texture by `tex_id` (`formats/texture.md`). The `reduced_gid` term differs by slot
family: the weapon slot (14) uses a wider reduction with a base-1000 slot multiplier, while the other
slots use a base-100 multiplier — both are deterministic reductions of the slot gid. Empty slots
(e.g. no head overlay, no weapon) resolve to no node and are skipped.

### 3.5.5 Data value-edges — PINNED from the real client VFS (no debugger needed)

> **Provenance.** The mechanism (catalogue key formula, `model_class_id`-keyed lookup) stays
> CODE-CONFIRMED. The concrete value-edges below were **pinned by direct observation of the real
> client VFS** (`05.Presentation/.../clientdata/` — `.bnd`/`.skn`/`.mot` decode headers + the CP949
> `skin.txt`/`actormotion.txt`/`bindlist.txt`/`motlist.txt` tables), without a debugger. Every asset
> was verified to EXIST in the VFS (existence + size; no payload). The earlier "await a live-debugger
> confirm / do not invent" framing is SUPERSEDED for the `model_class_id → bind-pose` edge.

**`model_class_id` → bind-pose skeleton (PINNED, VFS-verified).** Only the four player rigs
`g1..g4.bnd` exist (`g11.bnd`/`g16.bnd` are CONFIRMED ABSENT from the VFS); each is preloaded by name
from `bindlist.txt` and filed under its parsed `actor_id`. The mapping resolves through the verbatim
`id_b` rule (§8(e)) and is confirmed end-to-end by each body `.skn`'s decoded `id_b`:

| `model_class_id` | class (internal 1..4 / role) | skeleton (`actor_id`, bones) | body `.skn` (slot 3, `id_b`) | idle clip (REAL, 30f) |
|---:|---|---|---|---|
| 1  | 1 / Musa (`M_musa`)  | `g1.bnd` (`actor_id 1`, 84 bones) | `g202110001.skn` (`id_b=1`) | `g111100010.mot` (84 tracks) |
| 26 | 2 / Salsu (`salsu`)  | `g2.bnd` (`actor_id 2`, 87 bones) | `g202220001.skn` (`id_b=2`) | `g112200010.mot` (87 tracks) |
| 11 | 3 / Dosa (`dosa`)    | `g3.bnd` (`actor_id 3`, 82 bones) | `g202130001.skn` (`id_b=3`) | `g111300010.mot` (82 tracks) |
| 16 | 4 / Monk (`Monk`)    | `g4.bnd` (`actor_id 4`, 89 bones) | `g202140001.skn` (`id_b=4`) | `g111400010.mot` (89 tracks) |

So the long-documented `{1→g1, 26→g2, 11→g3, 16→g4}` edge is now **PINNED from VFS data**: each body
skin's `id_b` (1/2/3/4) equals the target skeleton's `actor_id`, and the per-class idle clip's track
count equals that skeleton's bone count (84/87/82/89 — exact), so the trio is self-consistent.

**`skin.txt` body chain (PINNED).** `skin.txt` is tab-delimited (line 1 = row count `1352`). Columns
(0-based): `[0]` appearance group, `[1]` class key = `model_class_id` (the value filed under the
catalogue, ∈ `{1, 11, 16, 26, …}` for players), `[2]` catalogue slot, `[3]` a flag (0 on body rows),
`[4]` **mesh gid** (`g{gid}.skn`), `[5]` **tex id**. The `group 0` body rows (catalogue slot 3) are
exactly the four `202{1..}0001` meshes tabulated above (e.g. `0\t1\t3\t0\t202110001\t402110001`).

**Catalogue slot column ≠ engine equip-slot index.** The `skin.txt` catalogue-slot column (`[2]`)
takes values `{0, 3, 4, 6, 11, 40}` in the data, with these family bindings (VFS-observed): slot 3 →
`202` (body "b"), slot 4 → `203` ("p"), slot 6 → `209` ("a"), slot 11 → `206` ("s"), slot 0 → `210`/`273`
(head/face), slot 40 → `218` (face/anim-class — the §3.6.2 build-#8 family). This is **distinct** from
the engine equip-table slot list `{3, 4, 6, 2, 11, 14}` of §3.5.1/§3.6.2 — the equip-slot indices are
mapped into the catalogue key via the formula `catalog_key = gid_reduced + 1e9·(slot + 100·model_class_id)`
(§3.5.3), and there is **no catalogue-slot value `2`** in `skin.txt` (the equip family `209`/"a" is
filed under catalogue slot 6). A port must key the catalogue by the formula's `slot`, not assume the
two slot numberings coincide.

**`categoryBase[]` array contents — STILL a value-edge (do NOT invent).** The per-appearance-group
base-offset table held on the catalogue object (used in both the catalogue key and the actormotion key)
is **not** recoverable from the table data alone — its contents are computed/held in code and not
emitted to any observed text table. This edge remains open and **must not be invented**; route to RE if
needed. The `group` column (`skin.txt[0]`) is observed to take values `0, 1, 2, …` (multiple appearance
groups per class), which is the index `categoryBase[]` is keyed by — but the offset *values* are not in
the VFS text tables.

<!-- VFS-PINNED: model_class_id->bnd {1->g1,26->g2,11->g3,16->g4} (id_b verbatim); body skin chain; idle clips. STILL OPEN: categoryBase[] offset values (in-code, not in tables) -->

---

## 3.6 Char-select 3D actor assembly — complete build chain (CODE-CONFIRMED, static)

> **Provenance.** Promoted and rewritten from a dirty-room static-analysis note (`_dirty/`,
> gitignored). Every element enumerated here was read from the function bodies of the relevant
> routines in `doida.exe` IDB SHA 263bd994 during the CYCLE 10 / 2026-06-21 pass. The note
> contains no Hex-Rays text; addresses and raw offset constants exist only in the gitignored
> dirty note and do not appear here. Promotion target is this section only; world-placement
> coordinates and window-level call sequences are owned by `specs/frontend_scenes.md §3.x`.
> See §3.6.7 for the consolidated open-item list for this section.
>
> **Relationship to §3.5.** Section §3.5 documents the **general** character appearance assembly
> (the shared skeleton + overlay part model, the appearance catalogue, the `model_class_id`
> formula). This section documents the **char-select-specific** two-path actor build — which
> part driver runs on each path, the exact deform-part slot lists, the weapon rigid-attach,
> and the idle-apply mechanics. The §3.5 facts (skeleton, catalogue, `model_class_id` formula,
> slot family map) apply here without modification.
>
> **Update (static, 2026-06-22).** A dedicated static re-walk of the lineup two-pass build and
> the slot-23 secondary builders **resolved** the former OPEN-1 (lineup weapon), OPEN-4 (slot-23
> builds #6/#7 same-or-different), and the primary OPEN-6 (the "horrible assembly" / double-build
> suspect). The verdict: the second pass fully **tears down** the first pass's skin list before
> building, so there is **no double-attach and no in-place overwrite** — PASS 2 always wins, and the
> append-only linker means parts never replace each other within a pass. The "double-build →
> exploding/duplicated mesh" hypothesis is statically REFUTED. See §3.6.2 for the full verdict and
> the surviving low-priority [OPEN@DEBUGGER] residuals. Still STATIC ONLY — no live debugger this
> campaign; items requiring a live read are tagged [OPEN@DEBUGGER].

### 3.6.0 Two distinct preview paths — do not conflate

The char-select scene builds 3D preview actors via two separate entry points, each with a
different purpose, part driver, and equip source. Both paths call the shared actor factory
`ActorManager_SpawnActorFromDescriptor` (mode arg 1 = player), but they differ in everything
that follows:

| Path | Purpose | Actors per build | Equip source | Notes |
|------|---------|-----------------|-------------|-------|
| **Roster lineup** | The 5-slot row of existing characters | Up to 5 (slots 0..4) | Server-supplied equip table from the slot's spawn descriptor | Part driver `ActorVisual_RebuildLineupParts` runs after the factory; see §3.6.2 |
| **Zoom / single preview** | Enlarged view of the just-created or selected character | 1 | Synthesised descriptor with 4 per-class **preview-only starter equip ids** injected (see §3.6.1) | Factory's `ActorVisual_RebindLocalPlayerParts` driver runs and is not overridden |

The two actor sets are **mutually exclusive at runtime**: the lineup build tears down the prior
zoom actor, and the zoom build tears down all 5 lineup actors before spawning its single actor.
A 1:1 port must replicate this teardown sequence or stale ghost actors accumulate.

> **RESOLVED static (was OPEN-1) — an EQUIPPED lineup slot shows NO weapon from PASS 1.** The
> factory's PASS-1 in-world driver attaches weapon nodes, but the equipped-slot PASS-2 driver
> (`ActorVisual_RebuildLineupParts`) begins with the full skin-list teardown (it destroys and
> clears the entire list built by PASS 1 — see §3.6.2) and then does NOT re-invoke the weapon-node
> builder. So PASS 1's weapon does not survive: the equipped-lineup final list holds only the
> mantissa-zeroed deform parts, no weapon. (The **unequipped** PASS-2 fallback re-runs the in-world
> driver, which DOES rebuild weapon nodes, so an unequipped lineup actor can carry the in-world
> driver's weapon.) **[OPEN@DEBUGGER]** corroborate by enumerating the skin/node list of an equipped
> lineup actor after PASS 2 — low priority, the teardown is statically unambiguous.

### 3.6.1 Zoom-path synthesised descriptor and preview-only equip ids

The zoom build constructs a local 880-byte descriptor (zeroed), copies three appearance words
from the select window's internal state, and injects **4 preview-only starter equip ids** per
class via a class-switch. These ids exist solely for this preview actor; they are not the
character's real equipment and must not be used for in-world spawning.

The class selector and the four injected equip-slot ids (using the slot-family numbering from
§3.5.1: slots 3/4/6/2 = families 202/"b" / 203/"p" / 206/"s" / 209/"a") per class are:

| Class (server-supplied class selector, 1..4) | Slot 3 (body) | Slot 4 ("p") | Slot 6 ("s") | Slot 2 ("a") |
|------|------------|------------|------------|------------|
| 1 | 202110003 | 203110002 | 206110002 | 209110001 |
| 2 | 202220003 | 203220002 | 206220002 | 209220001 |
| 3 | 202130003 | 203130002 | 206130002 | 209130001 |
| 4 | 202140003 | 203140002 | 206140002 | 209140001 |

A body-family byte is also set per class (classes 1, 3, and 4 set it to 1; class 2 sets it to
2). The zoom actor is then placed at a single anchor offset (documented in
`specs/frontend_scenes.md §3.7`) and given its idle via the standard in-world idle path (§3.6.5).

> **OPEN (§3.6 OPEN-2):** the class-2 body-family byte value (2 vs. 1 for every other class)
> is unexplained. The most plausible interpretation is a gender or body-family marker specific
> to class 2 (Salsu), but this is not confirmed in the binary. Do not hard-code an assumption;
> carry as OPEN pending a live read or a cross-reference to the body-family byte's consumer.

### 3.6.2 Deform-part enumeration — two drivers with different part sets

A char-select preview actor is a set of independently-loaded deform `.skn` parts each bound to
the shared actor pose (§3.5.1). Two part drivers exist with different part counts and catalogue
key sources.

#### Roster-lineup driver (working name: `ActorVisual_RebuildLineupParts`; name pending `names.yaml`) — 8 deform-part builds

This driver runs as a second pass after the factory (§3.6.3), on lineup actors only. It opens
with a **high-tier collapse guard**: when the actor's `model_class_id` exceeds a per-session
threshold read from the global visual object, only the body slot (slot 3) is built. Below the
threshold it builds the following 8 deform parts in order:

| Build # | Slot | Key source | Part identity |
|---------|------|-----------|--------------|
| 1 | 3 | `(slot, model_class_id)`, reduced-gid mantissa zeroed | **Body** (family 202/"b") default for this `model_class_id` |
| 2 | 4 | `(slot, model_class_id)`, mantissa zeroed | Family 203/"p" default |
| 3 | 6 | `(slot, model_class_id)`, mantissa zeroed | Family 206/"s" default |
| 4 | 2 | `(slot, model_class_id)`, mantissa zeroed | Family 209/"a" default |
| 5 | 11 | `(slot, model_class_id)`, mantissa zeroed | Head / hair / face default |
| 6 | (slot-23 family) | Secondary appearance field (`actor.secondaryAppearanceId`) | Secondary deform part, slot-23-family key seed |
| 7 | (slot-23 family) | `actor.secondaryAppearanceId` + `model_class_id` + variant/class terms | Secondary deform part (variant-tinted), same family as #6 |
| 8 | (slot-40 family) | Face/anim-class field from the actor descriptor (`desc+offset0x36`) + `model_class_id` | Face/anim-class part |

Parts #1–#5 use the default `(slot, model_class_id)` catalogue key with the reduced-gid
mantissa zeroed — they produce the default appearance for the appearance class, ignoring any
equip-mantissa on the server descriptor. Parts #6–#8 key off two additional actor fields
(`secondaryAppearanceId` and `desc+0x36` respectively) whose exact semantic meanings are not
yet confirmed (see OPEN-3 below). Each of #6, #7, and #8 is conditional on a catalogue hit;
a miss silently skips that part.

> **OPEN (§3.6 OPEN-3):** the semantic meanings of the actor fields driving builds #6 (secondary
> appearance id), #7 (secondary + variant/class), and #8 (face/anim-class field at
> `desc+offset0x36`) are unverified. Only their offsets and use are confirmed. Candidates
> include face sub-mesh id, head decoration, hair variant, or sub-class. Do not invent the
> semantics; carry as OPEN pending AnimCatalog data or a live read.

> **RESOLVED static (was OPEN-4) — builds #6 and #7 request DIFFERENT catalogue keys; legitimate
> multi-part, NOT a redundant double-build.** The two slot-23-family secondary builders run as an
> adjacent pair on the same actor, build #6 then build #7. Each computes its own 64-bit AnimCatalog
> lookup key by a **different formula reading different actor fields**, then attaches the single skin
> part its key resolves to (or nothing, on a catalogue miss). The keys cannot collide:
> - **build #6** computes its key with one **fixed secondary sub-id additive tag**, applied to a
>   single appearance field on the actor;
> - **build #7** computes its key with a **different additive tag** and a branch that, in its main
>   case, derives the multiplicand from the actor's class/variant slot fields via the familiar
>   `5·(class + 4·variant) − 24` `model_class_id` form (§3.5.2) plus a distinct additive tag.
>
> Because #6's and #7's additive tags differ (and #7's multiplicand differs in its slot-id branch),
> the two keys resolve to **distinct catalogue entries** — two different skin parts (e.g. a face base
> and a face-detail / variant-tinted sub-mesh), not the same part built twice. Each is independently
> conditional on a catalogue hit, so a real run attaches 0, 1, or 2 of them. Both builders share the
> same scaffolding (a lazily-cached per-builder pointer to the shared visual singleton, a find-then-get
> double *lookup* of the same key — benign intra-routine idiom, not a second part — and a single
> `ActorVisual_AttachSkinPart` with a 0.0 scale override). Treat as legitimate multi-part assembly.
> The precise **semantics** of the driving actor fields remain OPEN-3; only that #6 ≠ #7 is now settled.
> A live-debugger read of the two concrete key values on an equipped actor would byte-prove
> non-collision, but the static additive-tag delta already settles it. **[OPEN@DEBUGGER]** byte-prove
> the two resolved record ids on a live actor (low priority — non-collision is statically proven).
> Independently corroborated by the char-select per-slot static walk
> (`frontend_scenes.md` §11.5h cross-ref): the two slot-23-family secondary builders carry distinct
> additive tags / multiplicands, confirming a legitimate two-part attach rather than a double-build.

> **OPEN (§3.6 OPEN-5):** the concrete value of the high-tier collapse threshold is data-driven
> (read from the global visual object at runtime), not a static constant. It is not recovered.
> Do not invent a value; the effect — only slot 3 is built above the threshold — is confirmed.

#### In-world / factory part driver (working name: `ActorVisual_RebindLocalPlayerParts`; name pending `names.yaml`) — 6 deform parts + face builders + weapon

This driver runs inside the shared actor factory (§3.6.3) for both the lineup and zoom paths.
The same high-tier collapse guard applies. Below the threshold it builds via the
equip-mantissa-carrying per-part builder (working names: `ActorVisual_BuildPart` /
`ActorVisual_AttachSkinPart`; names pending `names.yaml` — see §3.6.4) in slot order:

**Slots `{3, 4, 6, 2, 11, 14}`** — six deform parts — then two additional face-part builders
(one for the equip-table entry 0 / face slot, one for the `desc+0x36`-keyed face part), and
finally the weapon rigid-attach builder (`ActorVisual_AttachHandWeaponNodes`, §3.6.4).

The per-part catalogue key for slots other than 14 uses the formula from §3.5.3:
`catalog_key = gid_reduced + 1e9 * (slot + 100 * model_class_id)`. The weapon slot (14) uses
a wider reduction with a base-1000 slot multiplier; see `specs/equipment_visuals.md §3`.

There is also a near-identical sibling driver used in the high-tier case that builds only slot 3.

**The two-pass build on the lineup path — RESOLVED static (no double-attach).** Every lineup
actor is built in **two sequential passes**, and the verdict that matters for a 1:1 port is that
they do **NOT** co-reside — the second pass tears the first one down before it builds, so the
final skin list holds **exactly one pass's parts**:

- **PASS 1 — inside the factory, at spawn (always the in-world driver).** The factory runs
  `ActorVisual_RebindLocalPlayerParts` (equip-mantissa-carrying, slots `{3,4,6,2,11,14}` + two
  face parts + weapon nodes), appending each part onto the actor's (initially empty) skin list.
  This driver does **not** tear down — it only appends — which is correct at PASS 1 because the
  list starts empty.
- **PASS 2 — back in the lineup loop, after the factory returns.** An equip-walk over the actor's
  20-entry equip table selects ONE PASS-2 driver: an **equipped** slot (the first equip entry that
  resolves to a live part-actor with a qualifying part-flag) runs `ActorVisual_RebuildLineupParts`
  (the mantissa-zeroed 8-part build, §3.6.2 above); an **unequipped** slot (all 20 entries miss)
  runs the fallback driver that simply re-runs `ActorVisual_RebindLocalPlayerParts`.

> **RECONCILE — the equip-validity scan polarity, and EQUIPPED lineup slots CAN show a weapon
> (binary-won, counter-check IDB SHA 263bd994, static-only).** A static re-walk of the lineup loop's
> per-slot equip-validity scan pins the two-way branch precisely, and reverses the polarity above:
> the scan walks the 20 equip-table entries and, for each that names a live actor, tests a per-class
> flag at `*(byte)(class@actor+168 + foundActor+204)`. The branch is:
> - **ALL 20 entries valid** (scan completes without an "invalid" break) → tear down PASS-1, then
>   **re-run the equip-driven in-world driver `ActorVisual_RebindLocalPlayerParts`** (slots
>   `{3,4,6,2,11,14}` + weapon). The fully-valid-equip lineup actor therefore looks **exactly like the
>   in-world actor and CAN show a weapon** (slot 14).
> - **ANY entry invalid** (early break) → tear down PASS-1, then build the **mantissa-zeroed default
>   driver** (slots `{3,4,6,2,11}`, the gid term = 0, keyed purely by `(slot, model_class_id)`) plus
>   the secondary/face builders — **NO weapon (no slot 14) in this branch.**
>
> This **reverses** the "equipped slot runs the mantissa-zeroed lineup driver; unequipped re-runs the
> in-world driver" framing in this subsection and in the table/notes below: it is the **fully-valid-equip**
> slot that re-runs the weapon-bearing in-world driver, and the **default (any-invalid)** slot that runs
> the mantissa-zeroed, weapon-less driver. Consequently the **"equipped lineup slot shows no weapon"
> open item is revised** — an equipped lineup slot whose 20 equip entries are all valid goes through the
> WEAPON-bearing driver, so it can show a weapon; the no-weapon outcome is specific to the **default
> (invalid-equip)** branch. Both branches still tear PASS-1 down first, so the no-double-attach verdict
> is unaffected. A faithful port must reproduce **both** branches and the equip-validity scan that
> selects between them — using the in-world (gid-bearing, weapon) key on the fully-valid branch and the
> mantissa-zeroed `1e9·(slot + 100·model_class_id)` key on the default branch. The exact byte semantic
> of the per-class equip-validity flag (what makes an entry "valid") is the only residual here —
> mechanism confirmed, meaning **debugger-pending** (do not invent).
- **The decisive fact: BOTH PASS-2 drivers begin with a full skin-list teardown.** Each PASS-2
  driver first calls the shared skin-list teardown (`ActorVisual_TeardownSkinList`; name pending
  `names.yaml`), which iterates the entire current skin list, decrements each bound pose's refcount,
  detaches and virtual-destroys every skin object, frees the pooled dynamic vertex/index buffers,
  and **clears the skin-list vector to empty** (and resets the pooled vertex/index counts to 0).
  Only then does it append its own parts onto the now-empty list.

**The per-part linker is APPEND-ONLY.** Each successful attach calls a list linker that wires the
skin's pose/owner/pooled-base-index, bumps the pooled vertex/index counts, and does a vector
`push_back` of the new skin entry — there is **no search-by-slot, no key compare, no
replace-in-place**. Within a single pass parts therefore accumulate (they never overwrite each
other); across passes the only thing that prevents accumulation is the PASS-2 teardown above.

**Verdict (RESOLVES the former OPEN-6 "horrible assembly" suspect):**

1. **Order is fixed** — PASS 1 = the factory in-world driver (at spawn), then PASS 2 = the lineup
   driver (equipped) or the re-run of the in-world driver (unequipped), after the factory returns.
2. **No part is attached twice in the final list, and no part is overwritten in place.** PASS 2
   destroys PASS 1's entire skin list before appending its own parts. The earlier
   "double-build → duplicated / overwriting / exploding mesh" hypothesis is **statically REFUTED**
   — the mesh is not doubled by this two-pass structure.
3. **PASS 2 wins, unconditionally**, because it tears PASS 1 down first. For an **equipped** lineup
   slot the winner is the lineup driver's **mantissa-zeroed default-body** 8-part build; for an
   **unequipped** slot the winner is the second run of the in-world (equip-mantissa) driver.

**1:1 port guidance.** A faithful port may either (a) reproduce the two passes literally with a
full skin-list teardown between them, OR equivalently (b) build **only the PASS-2 set** directly
(the lineup driver's parts for an equipped slot, the in-world driver's parts for an unequipped
slot), since PASS 1 is always discarded.

> **Fidelity consequence for EQUIPPED lineup slots (a faithful behaviour, NOT a bug to fix).**
> *(Polarity caveat: per the RECONCILE note above, the mantissa-zeroed lineup driver is the **default
> (any-invalid-equip)** branch; a slot whose 20 equip entries are ALL valid instead re-runs the
> weapon-bearing in-world driver and shows its real equip meshes. The paragraph below describes the
> **default-branch** outcome — read "equipped lineup slot" here as a slot taking the mantissa-zeroed
> default driver.)*
> The winning PASS-2 build for a default-branch lineup slot is the lineup driver, which keys parts
> `#1–#5` with the **mantissa-zeroed `(slot, model_class_id)`** catalogue key — i.e. it drops the
> equip-item gid mantissa and resolves the **default body for the appearance class**. So even when
> a roster character IS equipped, the lineup row shows the **default-body silhouette** for its
> `(slot, model_class_id)`, discarding the equip-item meshes that PASS 1 built with the equip
> mantissa. The equip-walk uses equipment only to DECIDE which PASS-2 driver runs; the lineup
> driver itself then ignores the equip mantissa. A 1:1 lineup port should render the default-body
> parts for equipped slots, matching this — it is the as-built behaviour, not a defect. (The zoom /
> single-preview path never runs PASS 2, so it keeps the factory's equip-mantissa parts — its
> synthetic starter gear shows; §3.6.0.)
>
> **OPEN@DEBUGGER (residual, do NOT guess — none affects the no-double-attach verdict):**
> (1) the COUNT of *successful* attaches per pass (each attach is gated on an AnimCatalog hit, and
> the catalogue is VFS/data-driven, not in the binary) — breakpoint the per-part attach routine on
> a char-select with an equipped slot and count the skin-list entries after PASS 2 settles;
> (2) corroborate the skin list is empty immediately after the teardown returns at PASS-2 entry
> (the static read is unambiguous; listed for completeness);
> (3) the exact semantic of the equip-walk part-flag byte that decides equipped-vs-fallback (it
> does not affect the duplication verdict either way).

### 3.6.3 Shared actor factory — call sequence

`ActorManager_SpawnActorFromDescriptor` (mode 1 = player) is the single convergence point for
all char-select preview actors (and all in-world spawns). For the char-select paths it:

1. Allocates the actor object and copies the 880-byte descriptor into it.
2. Resolves the **appearance key** (`model_class_id = 5·(class + 4·variant) − 24`; variant 3
   resolves to 0 — the invisible sentinel, no mesh rendered). See §3.5.2.
3. Looks up the **AnimCatalog record** by appearance key. A miss or empty part list destroys
   the actor — no preview for that slot.
4. From the catalog record, acquires the **bind-pose handle** (the actor's deform skeleton) and
   sets the base motion-rate scalar and the mesh import-scale base.
5. Runs player-kind name/relation fixups (same-named-clone display rebuild).
6. Runs the in-world part driver `ActorVisual_RebindLocalPlayerParts` (§3.6.2 above) and
   applies the idle motion (§3.6.5).

The lineup path then additionally runs `ActorVisual_RebuildLineupParts` as a second pass
(§3.6.2), producing the double-build described there.

### 3.6.4 Per-part attach mechanics (`ActorVisual_AttachSkinPart`)

For each deform part (slots other than the weapon), the per-part attach routine:

1. Resolves the catalogue key to a `(skinId, poseId)` pair.
2. Fetches the `.skn` from the skin cache (path `data/char/skin/g{skinId}.skn`), loading and
   running the inverse-bind bake (§4) if not already cached.
3. Allocates a deform scratch vertex buffer (`32 · vertCount` bytes) and memcpys the 32-byte
   rest vertices into it.
4. Binds the skin part to its pose skeleton by `poseId` via the bind-pose pool (`id_b` verbatim
   key — §8(e)).
5. Computes the per-mesh scale as `nodeScale · meshScale` (× optional gid-scale override if
   non-zero).
6. Links the skin into the actor's skin list (see §3.5.4) and adds its vertex/index counts to
   the actor's pooled totals.

The vertex stride is 32 bytes (position vec3 + normal vec3 + uv2; §2.1). The deform mode
(LBS / rigid-major / rigid-fast) is a per-actor word that dispatches the deform variant each
frame (§1, §5.3).

**Weapon rigid-attach (working name: `ActorVisual_AttachHandWeaponNodes`; name pending `names.yaml`).** The weapon is a **static
(rigid) item skin attached to a hand bone**, not a deform skin. This builder runs only for
player-kind actors via the factory's in-world part driver. It:

1. Reads the weapon item-actor id from the actor's weapon slot.
2. Shifts a bone-index field by a per-subtype offset (the shift selects the hand-bone attach
   node for the weapon sub-type: two-handed sword vs. bow vs. staff, etc.).
3. Looks up the weapon item skin record and, if found, builds a `StaticSkin` node (not a
   deform `Skin`) attached to the selected hand-bone node.
4. For dual-weapon items (identified by a weapon-bind-class field == 3), builds a **second**
   StaticSkin node: one for the main hand (node flag 2) and one for the off hand (node flag 1).
5. Assigns each weapon node's bone-matrix index from attack-mode tables keyed by the
   actor-mode columns; flag 2 (main hand) and flag 1 (off hand) select different table rows.

The concrete hand bone-id is **debugger-pending** (see `specs/equipment_visuals.md §3` and its
`DBG-PENDING` note). Do not hard-code a bone id; resolve it through the table mechanism above.

### 3.6.5 Idle motion application on preview actors

Two idle entry points apply to preview actors; both resolve the same catalogue slot.

**Factory / in-world path (`ActorVisual_ApplyIdleMotionByKind`).** For motion-kind 0
(stand/idle), reads the AnimCatalog record at **+0x44 (direction array A element 1 = column 16
of actormotion.txt)** and applies that clip at rate 1.0. The idle clip is resolved through
the `motlist.txt` clip registry (the column-16 value is a motion id equal to a `.mot` file's
header `id_b`). See §10 for the full standing-idle analysis. The lookup key is the actor's
stored **appearance key** (`model_class_id`), not the skin-class / `id_b` field from
`actormotion.txt` column 2.

**Lineup-direct path (working name: `Actor_ApplyIdleMotion_Direct`; name pending `names.yaml`).** Recomputes a **normalised lineup idle key** by
bucketing the `model_class_id` into 5-wide families: `key' = 5·((model_class_id % 40 − 1)/5) + 1`.
Looks up the catalog by `key'` and applies **record +0x44** (the same column-16 stand slot)
at rate 1.0. The effect is that lineup actors from the same class family share one idle bucket.

Both paths read the same column-16 catalogue slot. Record +0x40 (column 15, array A element 0)
has no read-sites and is statically dead — see §10.

> **OPEN (§3.6 OPEN-7):** the normalized key `5·((model_class_id % 40 − 1)/5) + 1` used on the
> lineup path differs from the raw `model_class_id` used on the zoom/in-world path. Whether
> the two expressions select the same catalogue record (and therefore the same `.mot` clip) for
> the four starter classes (whose `model_class_id` values are 1, 26, 11, and 16) is unverified.
> Both read column 16, so the bucket difference only matters if bucketing produces a different
> row from a raw lookup. Carry as OPEN pending a live check or an AnimCatalog data dump.

**Scale applied before idle on the lineup path.** The lineup driver multiplies the actor's
base motion-rate scalar (`actor.motionRateBase`, set from `catalogRecord.motionRateBase`) by
a fixed multiplier of **3.0** before calling the idle applicator. The zoom/in-world path uses
the catalog value without this multiplier.

> **Note for the port.** The 3.0 multiplier and the scale literal 72.0 written to the actor's
> scale fields by the select window are **legacy engine-unit artefacts** tied to the importer
> mesh scale and the catalog-record scale fields. A Godot port must reconcile against the
> actual `.skn`/`.bnd` import scale rather than transplanting those literals.

### 3.6.6 Skeleton selection in the char-select context

Two skeleton-selection events occur per preview actor and must not be confused:

1. **The actor pose skeleton** (the shared deform skeleton all parts bind into) is selected by
   the AnimCatalog record's bind-pose handle, keyed by the appearance key `model_class_id`
   (§3.6.3 step 3–4). This is one of the four `g1..g4.bnd` player rigs, reached through the
   catalog indirection, not by a literal `g{model_class_id}.bnd` path format.
2. **Each loaded `.skn`'s own skeleton for the inverse-bind bake**: when a part's `.skn` is
   loaded, the bake routine reads the `.skn` header's `id_b` field, resolves the matching
   preloaded skeleton by that `id_b` used verbatim as the pool key (§8(e)), and bakes the
   inverse-bind against that skeleton's world transforms (§4). For a consistent character all
   parts' `.skn` `id_b` must match the actor pose skeleton's `id_b`; a mismatch is the
   class-mismatch shatter described in §8(e).

> **RESOLVED (§3.6 OPEN-8) — VFS-PINNED.** The concrete `model_class_id → bind-pose handle`
> mapping for the four player values `{1, 11, 16, 26}` is **pinned from the real client VFS**
> (§3.5.5): `1→g1.bnd` (84 bones), `26→g2.bnd` (87), `11→g3.bnd` (82), `16→g4.bnd` (89). It is
> confirmed by each body `.skn`'s decoded `id_b` (which equals the target skeleton's `actor_id`)
> and by each per-class idle clip's track count exactly matching the skeleton's bone count. The
> four `g1..g4.bnd` are preloaded by name from `bindlist.txt`; `g11.bnd`/`g16.bnd` are CONFIRMED
> ABSENT from the VFS (so the appearance-slot values `11`/`16` are NOT filenames — they resolve to
> `g3`/`g4` through the `id_b` verbatim rule). No live read was needed.

### 3.6.7 Open-item summary for §3.6

| Id | Item | Impact |
|----|------|--------|
| ~~OPEN-1~~ **RESOLVED** | Whether lineup actors show weapons | **RESOLVED static (§3.6.2):** an EQUIPPED lineup slot shows NO weapon — its PASS-2 driver tears the whole list down (incl. PASS-1 weapon) and omits the weapon-node builder; an UNEQUIPPED slot's PASS-2 fallback re-runs the in-world driver and rebuilds weapon nodes. Low-priority [OPEN@DEBUGGER] corroboration only |
| OPEN-2 | Class-2 body-family byte = 2 (vs. 1 for classes 1/3/4) in the zoom synthesised descriptor | May affect which body-family mesh is loaded for the zoom class-2 actor; unexplained |
| OPEN-3 | Semantic meaning of `secondaryAppearanceId` (builds #6/#7) and `desc+0x36` (build #8) | Needed to correctly populate these actor fields on spawn; currently offsets/use are confirmed but semantics are not |
| ~~OPEN-4~~ **RESOLVED** | Builds #6 and #7 — same or different skin ids? | **RESOLVED static (§3.6.2):** DIFFERENT keys — each builder uses a different additive tag (and #7 a different multiplicand branch), so the two resolve to distinct catalogue entries (two sub-meshes), NOT a redundant build. Both are needed in a 1:1 port. Low-priority [OPEN@DEBUGGER] byte-proof only |
| OPEN-5 | Concrete value of the high-tier collapse threshold | Needed to reproduce the body-only fallback on actors above the threshold |
| ~~OPEN-6~~ **RESOLVED** | Two-pass build on the lineup: which build's parts win for an equipped slot? | **RESOLVED static (§3.6.2):** PASS 2 tears PASS 1 fully down first (shared teardown clears the skin-list vector), so NO part is attached twice and nothing is overwritten in place — the "double-build / exploding mesh" hypothesis is REFUTED. PASS 2 wins: an equipped lineup slot shows the lineup driver's mantissa-zeroed **default body** (discarding PASS 1's equip-item parts — faithful, not a bug). Residual [OPEN@DEBUGGER]: count of *successful* (catalogue-hit) attaches per pass only |
| OPEN-7 | Lineup normalized idle key `5·((model_class_id%40−1)/5)+1` vs. zoom raw `model_class_id` — same catalogue row for the four starters? | If they differ, lineup actors may idle differently than zoom; carry until an AnimCatalog row dump or live check |
| ~~OPEN-8~~ **RESOLVED (VFS-PINNED)** | Concrete `model_class_id → bind-pose handle` data values for `{1, 11, 16, 26}` | **PINNED from the real client VFS (§3.5.5):** `1→g1.bnd` (84 bones), `26→g2.bnd` (87), `11→g3.bnd` (82), `16→g4.bnd` (89); confirmed via each body `.skn`'s decoded `id_b` (= skeleton `actor_id`) and matching idle-clip track counts. `g11.bnd`/`g16.bnd` confirmed ABSENT. No live read needed |

---

## 4. Inverse bind — computed at load, never stored

There is **no inverse-bind matrix on disk.** It is baked once per skin, immediately after the skin
loads and its bind pose is resolved, into each influence record's `localPos` / `localNormal` fields.

For every major and minor influence (which carries a `bone_id` and a `vertex_index`):

```
bone      = bindPose.boneById(influence.bone_id)        # the 36-byte-on-disk bind bone
restPos   = renderVertex[influence.vertex_index].position    # model-space rest position
restNorm  = renderVertex[influence.vertex_index].normal      # model-space rest normal
invQ      = inverse(bone.bindWorldQuat)                  # unit-quat conjugate: negate X,Y,Z; keep W

influence.localPos    = invQ ⊗ ( restPos − bone.bindWorldTrans )    # position: subtract then rotate
influence.localNormal = invQ ⊗   restNorm                          # normal: rotation only (no translate)
```

`inverse(q)` for a unit quaternion is its conjugate `(−x, −y, −z, w)` (the legacy code divides all four
components by the squared magnitude, which is 1 for a unit quaternion, then negates X/Y/Z and keeps W).

This is the textbook **offset (inverse-bind) transform** `B⁻¹`: it expresses a model-space rest vertex
in the bone's local frame, so the animated bone world transform can re-place it. Because it is baked
into `localPos`, the per-frame deform never touches the bind pose again — it needs only the animated
bone world transform. The cancellation in §0 is the direct consequence.

> **The bake is a SEPARATE pass, not part of the `.skn` load — PINNED STATIC (CONFIRMED, CYCLE 1).**
> At the end of `.skn` parsing the influence records' `localPos` (+12) and `localNormal` (+24) fields
> are **zero-initialised** — the load builds the 9-float influence (bone id, vertex index, weight) and
> the influence-record default constructor sets the bone id and vertex index to a sentinel and zeroes
> the seven floats, so the bake's target fields are provably zero at skin-load. The inverse-bind bake
> is a **distinct pass invoked at skin-attach**, immediately after the `.skn` is parsed and its
> matching bind pose is resolved by the skin's `id_b` (§8(e)). It runs **after** the skeleton's bind
> **world** transforms exist (built at `.bnd` load by the bind-world walk, §3.1) and **before** the
> first deform (which reads `localPos`/`localNormal` as already bone-local, with no per-frame inverse).
>
> This pass — its existence, location, and math — is now **CONFIRMED static** (no longer a hypothesis,
> no debugger needed). It loops the MAJOR influence array first then the MINOR array (same body),
> stride 36 bytes per record, and per influence reads the bone id as a **single byte** and the vertex
> index, resolves the bind bone via the **72-byte bake-time bind resolver** (§3.2 — returns NULL
> out-of-range, no clamp; the bake has no null guard at the call site), reads the 32-byte render vertex's position
> (first three floats) and normal (next three floats), and writes the two baked fields exactly as the
> equations above:
> - `localPos` = subtract the bind-world translation from the rest position **first**, then rotate by
>   the inverse (conjugate) bind-world quaternion — `invQ ⊗ (restPos − bindWorldTrans)`;
> - `localNormal` = rotate the rest normal by the same inverse quaternion only — `invQ ⊗ restNorm`
>   (no translation term).
> The inverse is the **unit-quaternion conjugate** `(−x,−y,−z,w)` (the routine divides all four
> components by the squared magnitude — unity for a unit quaternion — then negates X/Y/Z and keeps W).
> The whole bake is quaternion-based: only 3-vector subtract, unit-quat inverse, and active
> quaternion-rotate primitives are called — **no 4×4 matrices anywhere**. The bind-world source is the
> static bind-world walk (parent-on-left: `worldTrans = parentQuat ⊗ localTrans + parentTrans`,
> `worldQuat = parentQuat ⊗ localQuat`) with **no animation term**, so the bake is taken against the
> rest/bind pose directly (see the frame-0 cross-check below). This is the textbook offset transform
> `B⁻¹`, and it produces exactly the §0 cancellation. (CYCLE 1, static.) **CYCLE 7 update:** the bake's
> rest pos/normal are stored per influence at the in-memory influence record's `localPos` slot (+0x0C)
> and `localNormal` slot (+0x18) — i.e. the bake folds the inverse-bind into the per-influence rest
> geometry rather than keeping a separate inverse-bind matrix array; this is the same quantity glTF
> stores as `inverseBindMatrix · vertex_bind`. **CORRECTED 2026-06-21 (visual-oracle):** there is no
> axis flip *inside* the bake/deform math, but the `.skn` rest geometry is authored **X-TALL** (native-X
> height-axis), so a faithful import must add a **+90°-about-Z importer stand-up remap** (native +X
> height → Godot +Y) — applied uniformly to the rest geometry, bones and keyframes so the §0
> cancellation survives the change of basis (§7, §8(b)). The bake math itself is unchanged.

> **Frame-0 vs rest cross-check (CONFIRMED static).** The skin-matrix bake reads the **bind/rest** pose
> directly — there is **no** mixer / animation-sample / clip-advance step on the skin-attach path
> before the bake, and the bone world transforms it reads are the bind-world slots written by the
> animation-free bind-world walk. The campaign-9c finding that a **pivot/AABB** is computed from an
> *animated frame-0* is a **separate, port-side preview-centering** concern (the char-create / Godot
> preview pivot), operating on a different quantity entirely — **no contradiction**: the *skin-matrix
> bake* uses the rest pose; the *preview pivot* reads an animated frame-0 AABB.

---

## 5. Weight application — linear blend skinning

### 5.1 The disk weight record's three fields

The 12-byte `.skn` weight record's fields are exactly:

| Disk field | Meaning |
|---|---|
| `vertex_index` | which vertex this influence affects |
| `bone_index` | **bone ID** (resolves the bind/pose bone by `id − base_id`, §3.2) — no indirection table |
| `weight` | scalar influence; a record is **kept** when `weight ≥ ~0.01` (the keep test is `weight ≥ 0.0099999998`), so records below ~0.01 are dropped at load |

### 5.2 Major / minor split + per-vertex normalization

At load, all influences are grouped by their (deduplicated) unique vertex. For each vertex the engine:

1. **Normalizes** the influence weights so they sum to 1.0. A zero total weight is a fatal assertion
   in the legacy client (so the per-vertex sum is always exactly 1.0 after this step).
2. Picks the **largest-weight influence as the MAJOR** entry for that vertex; routes the remaining
   influences to the **MINOR** array.

The major/minor split exists purely so the deform loop can *overwrite* the destination on the first
(major) influence and *accumulate* on subsequent (minor) influences, avoiding a separate clear pass.

**Corpus confirmation (SAMPLE-VERIFIED):** 1140 of 2786 `.skn` files are multi-bone / multi-weight.
Observed minimum weight is exactly **0.010** (records at 0.010 are kept; the skip threshold is below
0.010). Weight/vertex ratios range from ~2.6 (variable influence count) up to exactly 4.0 (a fixed
4-influence convention in newer skins). Both styles are multi-bone; neither is rigid. See
`formats/mesh.md` §Weight / skin table.

### 5.3 The deform loop (Mode 0 — full LBS)

For each influence record, look up the animated pose bone by `bone_id` and read its animated world
transform (`boneWorldQuat`, `boneWorldTrans`):

```
placed_pos    = boneWorldQuat ⊗ ( influence.localPos · scale ) + boneWorldTrans
placed_normal = boneWorldQuat ⊗   influence.localNormal

# MAJOR pass — OVERWRITE the destination vertex:
dst.position = placed_pos    · influence.weight
dst.normal   = placed_normal · influence.weight

# MINOR pass — ACCUMULATE into the destination vertex:
dst.position += placed_pos    · influence.weight
dst.normal   += placed_normal · influence.weight
```

Because weights are normalized at load, the weighted sum is convex. `scale` (a uniform per-mesh
scalar) multiplies the bone-local position **before** rotation; the quaternion-vector product is the
active rotation `q ⊗ v ⊗ q⁻¹` for a unit quaternion.

> **`scale` has a real source — do NOT assume 1.0 (CONFIRMED, CAMPAIGN 9, re-confirmed CAMPAIGN 10).**
> The per-mesh scale is a field on the skin object, populated **at attach time** as the product
> `nodeScale · meshScale`, then multiplied by an **optional override factor when that override is
> non-zero**. It is therefore **generally non-unit** and must be read from the skin object, not
> hard-coded to 1.0. The deform loop multiplies the bone-local position by this per-mesh scale
> (positions only — never normals, never rotations). This resolves the prior §9 open item that listed
> `scale` as "assumed 1.0; setter not traced." An importer that lets the engine skin (Godot
> `Skeleton3D`, §8(a)) must still apply this mesh scale to the rest geometry / node transform;
> dropping it shrinks or inflates the whole character.
>
> **There are TWO distinct scales — do not conflate them.** This per-**mesh** scale (a field on the
> skin object) multiplies the skinned vertex positions in the deform loop (§5.3). A separate
> per-**node** scale lives on each runtime pose bone (§3.4, the +84 field) and multiplies the bone's
> animated **local translation** in the world walk (§6.6). Both are uniform scalars but they apply to
> different quantities; an importer that bakes scale into its skeleton must apply the per-node scale in
> the bone world transform and the per-mesh scale to the skinned positions.
>
> **VB lock budget (port detail).** The deform upload routine locks the dynamic vertex buffer with a
> size cap and copies exactly **32 × vertCount** bytes per skin part into the locked region. This
> confirms the 32-byte upload vertex stride and bounds the per-skin vertex count: a skin part may not
> exceed the lock cap in total deformed bytes. A faithful port that manages its own CPU-side vertex
> buffer should budget accordingly; the cap is a structural constraint of the legacy upload path, not
> an artifact to work around.

### 5.4 Influences per vertex are unbounded by the format

The format imposes no fixed 4-bone cap; the major array contributes one influence and the minor array
contributes the rest, summed for the vertex. Newer skins happen to use a fixed 4-influence convention,
but older skins use a variable count. An importer that must cap to its engine's per-vertex influence
limit **must re-normalize the surviving weights to sum 1.0** after dropping the smallest influences,
to preserve the convex-combination property.

> **Port rule for Godot `Skeleton3D`'s 4-bone-per-vertex cap (CONFIRMED CYCLE 7 — implementable).**
> The legacy loader already normalizes each vertex's surviving influences to Σ = 1.0 across **all** of
> them (major + every minor), so a 4-cap port must **re-normalize after capping**:
> 1. For each vertex, collect its influences (1 major + n minor), each `{boneId, weight}` from the same
>    `id_b` skin.
> 2. **Sort by weight descending and keep the top 4.** (Every vertex has exactly one major influence —
>    the dominant bone — which is always among the kept four.)
> 3. **Re-normalize the kept weights to sum 1.0** (divide each by their kept-weight total).
> 4. Pack into Godot's `ARRAY_BONES` / `ARRAY_WEIGHTS` (4 per vertex). Map each `boneId` to the
>    `Skeleton3D` bone index via the base-relative scheme (build the bone order so `bone index == id −
>    baseId`, §3.2). A vertex with fewer than 4 influences pads the unused slots with weight 0.
> Skipping the re-normalize after capping (relying on the engine's original Σ = 1.0) is wrong, because
> dropping the smallest minor influences removes weight that must be redistributed to the survivors.

---

## 6. `.mot` keyframe sampling and pose composition

This section describes how a frame's animated pose is produced. The on-disk `.mot` track/keyframe
layout (8-byte track preamble + 28-byte keyframes, translation XYZ then quaternion XYZW) lives in
`formats/animation.md` §Track array layout; it is not repeated here.

### 6.1 Per-track sampling

At clip time `t` (seconds), a track is sampled as:

```
n     = floor(t · 10.0)                       # fixed 10 fps; duration = frame_count · 0.1
nNext = (n < key_count − 1) ? n + 1 : n        # clamp to the last key within a track
alpha = t − n / 10.0                           # RAW seconds in [0, 0.1] — NOT renormalized to [0,1]
trans = lerp ( key[n].translation, key[nNext].translation, alpha )
rot   = slerp( key[n].rotation,    key[nNext].rotation,    alpha )   # shortest-arc
```

- Translation is **linear interpolation**.
- Rotation is **shortest-arc SLERP**: if `dot(a, b) < 0`, negate `b` before SLERP; near-identical
  quaternions fall back to normalized LERP, antipodal quaternions to a perpendicular path.
- **The `alpha` quirk:** the blend factor is raw seconds (≤ 0.1), **not** normalized to `[0, 1]`. This
  makes legacy playback effectively near-keyframe-snapped. To reproduce legacy motion bit-for-bit,
  replicate the raw-seconds alpha; for smooth motion, renormalize `alpha /= 0.1`. This is a documented
  deviation (faithful vs. smoothed). See §8(c) and `formats/animation.md` §Timing.

### 6.2 Mixer → per-bone accumulation → commit (overview)

Each frame the mixer builds the animated pose in passes:

1. **Reset** every pose node's per-pass accumulators.
2. **Action-layer pass:** sample each one-shot clip's tracks and blend each sample into the matching
   bone with a running normalized weighted average (first contributor assigns; later contributors
   LERP/SLERP by `w_new / (w_acc + w_new)`, the denominator floored at 0.001 — see note below on
   the `logf` guard that selects the floor branch).
3. **Commit pass:** fold the accumulated sample into each node's **local animated** translation/rotation
   slots, blended against any previously committed value. **Interior-bone translation lock:** the local
   **translation is forced to the bind-pose local translation** only for an **interior** bone — one
   that has **both a parent AND a grandparent AND at least one child**. Such interior bones keep their
   fixed bind-pose bone length and only rotate. The **root**, the root's **direct children**, and the
   **leaf** bones instead take the blended (LERP'd) accumulator translation. (This is narrower than the
   prior phrasing "every non-root bone is locked" — see §6.3.) The first contributor assigns; later
   contributors blend by `w_new / (w_acc + w_new)` with the denominator floored at 0.001 — same `logf`
   guard as the accumulate pass.

> **The 0.001 denominator floor uses a genuine `logf` guard (static-confirmed).** The floor branch is
> taken when `logf(committedWeight + accumWeight) < 0.001`; in that branch the denominator is forced to
> `0.001` and `frac = accumWeight / 0.001`. Otherwise `frac = accumWeight / (committedWeight + accumWeight)`.
> The 0.001 floor value is real. The earlier "almost certainly a decompiler mis-symbol of an
> absolute-value epsilon clamp" framing for these two floors is **dropped** — the logarithm call is a
> genuine CRT function. For a 1:1 port: the guard fires only at vanishing accumulated weight, so the
> practical effect (prevent divide-by-zero on the first contributor) is unchanged regardless of whether
> the port reproduces the `logf` guard or uses a simpler `< 0.001` direct compare on the sum — but the
> faithful behaviour is the `logf` form. (The §2.1 vertex-dedup epsilon has not been re-checked and
> remains capture/debugger-pending separately.)
4. **Cycle-layer pass:** same accumulate-then-commit for the looping clips, using the sync-mode sample
   time where applicable (computed as `key_count · clip_field / sync_denominator` when the clip's sync
   flag is set, else a stored clip time; detail owned by `formats/animation.md` §Sync-phase mechanism).
5. **Root + heading:** the root node's world translation is set from the actor's world position; the
   root world rotation folds in the smoothed heading (yaw) quaternion.
6. **World walk:** fill every node's animated world transform from the committed local poses (§6.6).

### 6.3 The translation lock is INTERIOR-bone-only (the practical rule is unchanged)

This is the rule from step 3 restated because it matters for the importer. The exact legacy condition
is a **three-way interior test**: a bone's animated local translation is forced to its bind-pose local
translation **only when it has a parent, a grandparent, AND at least one child**. The root, the root's
direct children, and leaf bones take the blended accumulator translation instead. The prior phrasing
"child bones keep their fixed bind-pose bone length; only the root translates" had the right intent but
was slightly over-broad — strictly it is *interior* bones that are locked.

**The practical importer rule is unchanged:** feed **rotation tracks for non-root bones and a position
track for the root only**, to match legacy behaviour. This is safe because the on-disk idle clips carry
a (non-zero) translation on nearly every child track that the engine ignores for the locked interior
set, and the leaf / near-root translations come from an accumulator that was itself seeded at the
bind-pose local translation (so for an importer that does not author per-child translation tracks, the
interior-vs-leaf distinction does not change the fed data). Applying the stored child translations
verbatim would stretch bone lengths — see §8(e) step 5.

### 6.4 Keyframes are local replacement poses, not additive deltas

The sampled (translation, rotation) of a track **replaces** the node's local animated pose — it is not
added on top as a delta. The committed local quaternion is *initialized* to the bind local quaternion,
so a bone with no track keeps the bind local pose and the world walk reproduces the bind world pose
(no explosion at rest).

### 6.5 Quaternion handedness in composition

The animated rotation is applied as a **right (post) multiply** on top of the bind-local rotation in
the world walk (§6.6). At rest this still yields the bind world pose because the committed local quat
equals the bind local quat.

### 6.6 Animated world walk

After the commit passes, the hierarchy is walked from the root's children outward to fill each node's
animated world transform from the committed local poses:

```
worldTrans = ( parentWorldQuat ⊗ localAnimTrans ) · nodeScale + parentWorldTrans
worldQuat  = ( parentWorldQuat ⊗ bindLocalQuat ) ⊗ localAnimQuat
```

So per node the rotation is `parentWorld ⊗ bindLocal ⊗ animLocal`, and the translation is the parent's
world rotation applied to the animated local translation, then scaled by this node's per-node scale
(§3.4, the +84 field), then offset by the parent's world position. These `worldTrans` / `worldQuat` are
exactly the `boneWorldTrans` / `boneWorldQuat` the deform loop (§5.3) reads.

> **Literal order: rotate THEN scale (no functional conflict).** The legacy world walk rotates the
> local animated translation by the parent world quaternion **first**, then multiplies by the per-node
> scale, then adds the parent world translation — i.e. `rotate → scale → translate`. The deform loop
> (§5.3), by contrast, scales the bone-local position **before** rotating (`scale → rotate`). Because
> both scales are **uniform scalars**, scale and rotation commute, so `parentWorldQuat ⊗ (t · scale)`
> and `(parentWorldQuat ⊗ t) · scale` produce the identical result — the two literal orderings are
> mathematically equivalent. This is recorded for fidelity, not because it changes any value.

---

## 7. Quaternion and handedness conventions (the convention dictionary)

| Aspect | Convention |
|---|---|
| Primitive | unit quaternions + 3-vectors; **no 4×4 matrices** in the skinning math |
| Quaternion component order | **XYZW** — X, Y, Z first, scalar **W last** — on disk and in memory |
| Quaternion product | **Hamilton product** |
| Vector rotation | **active rotation** `v' = q ⊗ v ⊗ q⁻¹` (unit-quat form) |
| Quaternion inverse | conjugate `(−x, −y, −z, w)` for a unit quaternion |
| Hierarchy composition | **child = parent ⊗ local**, parent on the LEFT (pre-multiply / row-vector style) |
| Animated rotation | applied as a **right (post) multiply** delta: `parentWorld ⊗ bindLocal ⊗ animLocal` |
| Deform | LBS: `Σ w·(qWorld ⊗ (localPos·scale) + tWorld)`; normal `Σ w·(qWorld ⊗ localNormal)` |
| Inverse-bind | `localPos = qBindWorld⁻¹ ⊗ (modelPos − tBindWorld)`, baked at load |
| Render space (engine internal) | **D3D9 left-handed, +Z-forward** (CONFIRMED CYCLE 7); **no axis flip inside the skinning math** |
| World-PLACEMENT / HEADING up axis | **Y-up (KEPT)** — yaw is a pure rotation about Y, the ground plane is XZ, facing is the XZ planar delta; the engine places/turns actors about Y. This governs world placement, NOT the `.skn` geometry height-axis below |
| `.skn` GEOMETRY height-axis | **native X (CORRECTED 2026-06-21, visual-oracle)** — the rest-mesh is authored X-TALL; a faithful Godot import applies a **+90°-about-Z importer (display-node) stand-up remap** to map native +X height onto Godot +Y. This SUPERSEDES the prior "Y-up / identity import" reading of the asset geometry. It is a pure asset-orientation property (the inverse-bind cancellation passes at the float-noise floor); the deform math is untouched. See §8(b), §9 |
| Vertex stream | 32 bytes: pos[0..11], normal[12..23], uv[24..31]; `uv.v` stored as `1.0 − v` |

**No axis negation or mirroring happens inside the skinning math.** The known project conventions
(world negates Z; `.skn` mesh-local geometry negates X) are **importer-layer transforms**, not engine
internals. Bone space and rest-mesh space are the same native space — that is precisely why the
inverse-bind and the forward transform cancel. Separately, that native space is **X-TALL** for the
`.skn` geometry (height along native X), so the importer adds a **+90°-about-Z stand-up remap** on top
of the handedness/forward reconciliation; both are importer-layer, both must be applied **uniformly**
to bones + verts + keyframes so the cancellation survives. §8 explains how to bridge this to Godot
without breaking the cancellation.

> **The two up-axis quantities — do NOT conflate them (CORRECTED 2026-06-21, visual-oracle).**
> The engine's own placement/heading transforms are genuinely **Y-up**, and that is KEPT: a "turn to
> face" is a pure quaternion rotation **about Y** (the heading quaternion is `{0, sin(θ/2), 0,
> cos(θ/2)}`), facing is computed from the **XZ planar delta only** (the Y coordinate is height and is
> unused in facing), the render walk's forward basis vector is hard-coded **+Z**, and the device
> pipeline is a **left-handed** look-at. There is **no runtime character root rotation** other than the
> Y-yaw heading. **That Y-up is the WORLD-PLACEMENT / HEADING convention — how an actor is positioned
> and turned in the world.** It is a **different quantity** from the `.skn` GEOMETRY height-axis, which
> the visual oracle has now settled to **native X**: the raw rest-mesh is tall-along-X *before any
> deform or handedness conversion* (e.g. the g1 player body ≈ X 5.0 > Y 2.4 > Z 1.7), and the deformed
> frame-0 AABB at an identity importer pivot is likewise recumbent (tall-X). Because the inverse-bind
> cancellation passes at the float-noise floor, this is a **pure orientation property of the asset
> bytes**, not a deform-math or wrong-skeleton bug. Therefore a faithful import applies a **+90°-about-Z
> importer (display-node pivot) stand-up remap** to map native +X height onto Godot +Y (verified: AABB
> tall-X → tall-Y; screenshot +90° upright, −90° upside-down). This SUPERSEDES the CYCLE-7 "the up-axis
> is RESOLVED to Y-up / identity import / correct conversion = IDENTITY" wording for the asset geometry.
> The documented "−Z world" and "−X mesh-local" rules remain **port-side** left-handed→right-handed +
> forward-axis reconciliations (they convert the original's LH +Z-forward space to Godot's RH
> −Z-forward space); they are separate from, and applied alongside, the +90°-Z stand-up remap. The
> "avatar lies on X" is the **as-authored** geometry orientation — the fix is to **ADD** the +90°-Z
> remap, NOT to remove a non-existent upstream rotation. See §8(b) and the "avatar lies on X"
> diagnostic there. A future engineer MUST NOT re-zero `UpAxisRemapDeg` on stale "identity" wording.

---

## 8. Godot import guidance

The legacy math lives entirely in the engine's native **left-handed, +Z-forward** space and is
internally consistent. Godot is **right-handed, Y-up, −Z-forward**. The engine's world-PLACEMENT /
HEADING convention is Y-up (yaw about Y) and is reconciled by the handedness/forward conversion below;
SEPARATELY, the `.skn` GEOMETRY is authored **X-TALL** (native-X height-axis, CORRECTED 2026-06-21 by
the visual oracle), so the character import must also apply a **+90°-about-Z stand-up remap** to bring
native +X height onto Godot +Y. Five things must be honoured.

### (a) Preserve the bind / inverse-bind cancellation — this is what stops the explosion

The mesh explodes when the inverse-bind and the forward bone transform do **not** cancel at rest. To
guarantee cancellation in Godot, choose **one** of these two equivalent strategies and do not mix them:

- **Engine-skinned (recommended):** build a Godot `Skeleton3D` whose **rest** bone poses are the
  `.bnd` parent-relative local trans/quat (Godot wants parent-relative rest poses — exactly what `.bnd`
  stores). Feed the mesh **model-space rest vertices** plus per-vertex bone **IDs + weights**, and let
  Godot compute the inverse-bind from the rest pose. **Do NOT also pre-bake the legacy `localPos` into
  the vertices** — that double-applies the inverse-bind and explodes the mesh. The legacy `localPos`
  is the CPU shortcut, not something to port.
- **Manually-skinned (legacy parity):** reproduce §4 + §5 exactly on the CPU. Only do this if engine
  skinning cannot represent the rig.

In both cases the invariant to assert during bring-up: **with the idle/bind pose loaded and no clip
playing, the deformed mesh must equal the rest mesh.** If it does not, the cancellation is broken —
fix that before touching animation.

### (b) Unify the handedness conversion AND apply the +90°-about-Z geometry stand-up remap

The project today applies two separate negations: world geometry negates Z
(`Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,−z)`), and `.skn` mesh-local geometry negates X.
Applied piecemeal to a rig, these mirror the skin relative to the skeleton and break skinning.

**Pick one handedness conversion — the world Z-negate — and apply it uniformly to bone bind
translations, mesh vertex positions, AND keyframe translations.** Then drop the ad-hoc per-asset X/Z
flips for skinned characters. A Z-negation is a handedness flip; under it a quaternion `(x,y,z,w)` maps
to `(−x,−y,z,w)` (negate the two components orthogonal to the un-flipped Z axis). **Validate this exact
quaternion remap against a single sample bone rotation rather than assuming it** (§9 open item). The
key requirement is *uniformity*: bones, verts, and keyframes all undergo the same conversion, so the
cancellation property survives the change of basis.

**On top of the handedness conversion, apply the +90°-about-Z importer stand-up remap (CORRECTED
2026-06-21, visual-oracle).** The `.skn` geometry is authored X-TALL (height along native X), so the
character import MUST rotate it +90° about Z (at the display-node / importer pivot) to map native +X
height onto Godot +Y. Like the handedness conversion, apply this remap **uniformly** to bones + verts +
keyframes so the §0 cancellation survives. The world-PLACEMENT / HEADING up axis stays Y (yaw about Y)
— that convention is unaffected; the +90°-Z remap is purely the asset-geometry stand-up.

> **The "avatar lies on X (recumbent)" symptom is the as-authored geometry axis — ADD the +90°-about-Z
> remap; do NOT chase a rotation to remove (CORRECTED 2026-06-21, visual-oracle).** The `.skn` rest-mesh
> is authored X-TALL (§7), so at an identity importer pivot the avatar lies on its side (recumbent along
> X) — this is the **native** orientation of the asset bytes, NOT a port-added spurious rotation. **The
> fix is to ADD a +90°-about-Z importer (display-node) stand-up remap**, mapping native +X height onto
> Godot +Y. Do NOT search for and remove a non-existent upstream Z-up→Y-up rotation; the earlier "set
> the importer up-axis to identity / remove the spurious −90°-about-X" guidance is SUPERSEDED — that
> path leaves the avatar recumbent. (Screenshot evidence: +90°-Z is upright, −90°-Z is upside-down.)
>
> **Deformed-pose AABB verification.** Bake the bind pose (or a single idle `.mot` frame) through the
> parser-true math, compute the deformed-vertex AABB, and check which axis spans the figure's height:
> - **CORRECT (with the +90°-about-Z stand-up remap applied):** the AABB's largest extent is along
>   **Y** (a tall standing figure), small along X and Z; the head is at max-Y and the feet at min-Y.
> - **WRONG (the current "lies on X", identity importer pivot):** the AABB's largest extent is along
>   **X** — the figure is laid down. This is the signature of the as-authored native-X height-axis with
>   the +90°-Z stand-up remap **missing** (NOT a spurious −90°-about-X to remove).
> Run the check with the +90°-Z remap applied; that case must produce the tall-along-Y AABB. (The bake
> AABB itself is already sane and the inverse-bind cancellation is at the float-noise floor, per CYCLE 1
> — the orientation knob is the importer +90°-Z stand-up remap only, not the deform math.)

### (c) The raw-seconds interpolation alpha (faithful vs. renormalized)

Legacy clips sample with `alpha` in raw seconds `[0, 0.1]` (§6.1), which yields near-keyframe-snapped
playback. For Godot tracks you may either:

- **Faithful:** reproduce the raw-seconds alpha (set track interpolation so the effective blend factor
  matches `t − n/10`), accepting the snappy legacy look; or
- **Smoothed (recommended for a modern look):** renormalize `alpha /= 0.1` (i.e. use Godot's normal
  `[0,1]` interpolation between keys at 10 fps). Document whichever you choose; this is a known,
  intentional deviation, not a bug to fix silently.

Either way, keyframes map directly to Godot bone tracks: a **rotation** track per child bone and a
**position** track for the root only (§6.3), keyed at 10 fps.

### (d) Canonical test specimens

Validate end-to-end against these recovered trios (paths are VFS logical paths; the originals are
user-supplied and gitignored):

**Player (base warrior "M_musa"):**

| Role | Path | Key metrics |
|---|---|---|
| Skeleton | `data/char/bind/g1.bnd` | 84 bones |
| Skin | `data/char/skin/g202110010.skn` | `id_b = 1`, 475 verts, multi-weight |
| Motion | `data/char/mot/g170399907.mot` | 501 frames, 80 tracks (long clip; tracks 80 < bones 84 is normal — unmatched bones are idle) |

A shorter alternative idle for the same rig is `data/char/mot/g101100001.mot` (3 frames, 84 tracks).

**Mob:**

| Role | Path | Key metrics |
|---|---|---|
| Skeleton | `data/char/bind/g2048.bnd` | 45 bones |
| Skin | `data/char/skin/g219000630.skn` | `id_b = 2048`, 933 verts, multi-weight |
| Motion | `data/char/mot/g182006900.mot` | 120 frames, 45 tracks (track count exactly matches bone count — ideal for byte-level verification) |

These trios link through the recovered mappings: a `.skn`'s `id_b` selects the `.bnd` (the `id_b ↔ bnd`
relationship is a confirmed bijection across all 349 ids — see `formats/mesh.md`), and a clip's track
`bone_id`s address that skeleton's bones by ID (§3.2).

### (e) Rig/clip identity: select skeleton AND clip by the skin's `id_b` (the class/rig-mismatch shatter)

> Provenance: promoted (rewritten, not copied) from a dirty-room root-cause note. Asset byte facts are
> **SAMPLE-VERIFIED** against the real client VFS; the resolution mechanism is **CODE-CONFIRMED**
> (prior). The cross-link to the char-create preview is `frontend_scenes.md` §3.7.5 (preview-character
> assets for the four starter classes).

This subsection states the identity invariant that ties a skinned mesh, its skeleton, and its played
clip together. Ignoring it produces the **clean-at-rest / shatter-on-play** failure observed on the
char-create preview for the Monk class, while the Warrior class rendered correctly.

#### The invariant: one skin is authored against exactly one skeleton, named by its `id_b`

A skinned mesh's bind-local vertex offsets (§4, the inverse-bind bake) are baked against **one specific
skeleton's rest pose**. That skeleton is identified by the skin's own `id_b` (the SkinClassId, the
skeleton selector — `formats/mesh.md` §Header). The cancellation property of §0 holds **only** when all
three of the following are the same `id_b` rig:

- the **deform skeleton** that supplies the animated bone world transforms,
- the **inverse-bind bake** that produced each influence's `localPos` / `localNormal`, and
- the **played clip**, whose track `bone_id`s address that same skeleton.

#### How the engine actually selects the skeleton: verbatim `id_b` against an eager-preloaded pool (CONFIRMED static, CYCLE 1)

The runtime resolves the deform skeleton through a single, transform-free lookup, populated once at
boot:

- **Eager preload at boot.** Every `.bnd` named in `bindlist.txt` is physically opened, parsed into a
  pose object, and inserted into a shared **pose pool** during the boot data-table corpus load — not
  lazily on actor spawn, not registered by name for deferred load. (All ~349 listed skeletons are
  parsed up front; only the four `g1..g4.bnd` players plus mob/NPC rigs are listed.) Each pose is
  filed in the pool under the **`actor_id` parsed from that `.bnd`'s header** (its offset-0 field).
- **Verbatim `id_b` key.** When a `.skn` loads, the loader reads the header's second identity field
  (`id_b`) and passes it **verbatim** as the pool key. There is **no** arithmetic between the read and
  the lookup at this site: no `g{N}.bnd` path formatting, no `5·(class + 4·variant) − 24` appearance
  slot transform, no use of the header `SkinClassId` as the lookup key. The selected skeleton is simply
  the registered pose whose **`actor_id == id_b`**; an `id_b` of `0` (or any unregistered value)
  resolves to **no skeleton** (the deform then runs without a rig — the reserved invisible/no-mesh
  case).

So the operative runtime rule is: `selected_skeleton(skn) = pose_pool[ skn.header.id_b ]`, where the
pool key is each preloaded `.bnd`'s parsed `actor_id`. The familiar `g{SkinClassId}.bnd`-for-`{1,2,3,4}`
convenience rule and the `{1,11,16,26}` appearance-slot encoding both remain correct as **chain
documentation**, but they live one level **upstream** (they decide *which* `.skn`/`id_b` an actor
wears); they are **not** re-applied as a second remap inside the pool lookup. The four player rigs
resolve cleanly precisely because each class's `.skn` `id_b` already equals the `actor_id` of its
intended `g{n}.bnd` (`g1.bnd` parses to `actor_id 1`, and so on) — so the verbatim-key rule and the
`g{id_b}.bnd` convenience rule agree for players.

**Mobs reach the same pool indirectly.** A mob is NOT a literal `g{skin_class}.bnd`. The mob's record
carries an appearance value that is combined with a per-category base offset into an **appearance key**;
that key indexes the animation catalogue (the actormotion map); the catalogue record yields a
model-class-id key; that key resolves a visual/skin record in the same character visual registry the
preloaded skeletons live in; and the visual record holds the **bind-pose handle** from which the
runtime pose is built. The skeleton a mob ends up with is therefore still one of the bind poses
preloaded **by name** from `bindlist.txt`, reached through the catalogue indirection rather than a
filename. (The concrete `model_class_id → loaded `.bnd`` value-edges and the per-category base-offset
table contents remain value-edges pending a live read — do not invent them; see §3.5.5.)

Concretely the engine-intended matched trio per class is:

| Skin `id_b` | Skeleton | Idle clip (actormotion col2 == `id_b` → col16) | Tracks = bones |
|---:|---|---|---:|
| 1 | `data/char/bind/g1.bnd` (84 bones) | `data/char/mot/g111100010.mot` | 84 = 84 |
| 2 | `data/char/bind/g2.bnd` (87 bones) | `data/char/mot/g112200010.mot` | 87 = 87 |
| 3 | `data/char/bind/g3.bnd` (82 bones) | `data/char/mot/g111300010.mot` | 82 = 82 |
| 4 | `data/char/bind/g4.bnd` (89 bones) | `data/char/mot/g111400010.mot` | 89 = 89 |

The four creatable classes therefore do **NOT** share one rig. The Warrior base mesh carries `id_b = 1`
(its skeleton has 84 bones); the Monk base mesh carries `id_b = 4` (its skeleton has 89 bones). Each
class's overlay parts carry the same `id_b` as that class's base mesh.

#### Why bone IDs are NOT portable across skeletons

Two skeletons can share the bone-**ID** range `0..N` and yet be **different skeletons**: the same ID
denotes a **different physical joint** on each. Comparing the 84-bone and 89-bone rigs above over their
shared ID range:

- rest **translation** differs on almost every shared bone (max difference on the order of several model
  units — different limb lengths),
- rest **rotation** differs on a large majority of shared bones (up to a fully opposite orientation),
- **parent** differs on roughly half of the shared bones — the hierarchy is re-topologised (a bone may
  hang under a completely different parent on the other rig),
- the larger rig also has extra appendage bones at the top of the ID range that simply do not exist on
  the smaller rig.

Because of this, a clip or a skinned mesh authored against rig A **must never** be applied to rig B,
**even when the IDs "fit"** within B's window. The IDs fitting is necessary but not sufficient — the
joints they name are physically different.

#### The clean-at-rest / shatter-on-play diagnostic fingerprint

When a mesh is bound to the **wrong** same-ID-range skeleton and an idle clip from the wrong rig is
played:

- **At rest** (no clip, or every track at its bind value) the inverse-bind and the forward bone
  transform still cancel (§0), so the rest mesh is reproduced **cleanly** — there is no visible defect.
- **The instant a clip rotates bones off bind**, each vertex is rotated about the **wrong joint up a
  wrong parent chain**, and the mesh **shatters**.

This precise signature — correct at rest, exploding only once animation moves bones off bind — is the
fingerprint of a **rig substitution**. It is NOT a defect in the skinning math of §0–§7, and it is NOT
a track→bone-ID overflow (the wrong clip's IDs happened to fit the wrong rig's window). The matched
class renders correctly purely because its shared default choice coincided with its own `id_b` rig.

#### Importer invariant (implementers MUST follow)

1. Parse the base `.skn` and read its `id_b` (`formats/mesh.md` §Header). Resolve the deform skeleton by
   **looking the `id_b` up verbatim** in the skeleton pool keyed by each `.bnd`'s parsed `actor_id`
   (CONFIRMED static, CYCLE 1 — see "How the engine actually selects the skeleton" above) — **per
   class**, never a single shared rig hard-coded across all classes. For the four players this pool
   lookup is equivalent to loading `data/char/bind/g{id_b}.bnd` (each `g{n}.bnd` parses to
   `actor_id n`), so an importer may use that filename convenience for `id_b ∈ {1,2,3,4}`; for mobs the
   skeleton is reached through the animation-catalogue indirection onto the same preloaded pool, NOT a
   literal `g{skin_class}.bnd` filename.
2. Select the standing idle clip from `actormotion.txt` **column 16** (in-memory record +0x44 =
   direction array A element 1), looked up by the actor's resolved **appearance key**
   `5·(class + 4·variant) − 24`, NOT by col2/skin_class. **Resolve the clip through the motlist.txt clip
   registry**, NOT a `g{id}.mot` sprintf: the `.mot` files are pre-registered from `motlist.txt` lines
   (each prefixed `data/char/mot/`), and the column-16 value is a **motion id** that equals a `.mot`
   file's header `id_b` — the registry lookup key. (col2/skin_class is the `.bnd` skeleton selector and
   contributes to building the catalogue record's key; it is NOT the runtime idle lookup key. Record
   +0x40 = column 15 = array A element 0 is statically DEAD — it has no read-site — so any "idle =
   col15 / `motion_ids_a[0]`" claim is the off-by-one to avoid; the first *consumed* A-array element is
   element 1 = column 16.) Each clip's track count equals its rig's bone count. **Per class** — never a
   single shared idle clip.
3. Skin **every** overlay part onto that **same** `id_b`-selected skeleton (all of a class's overlays
   carry the class's `id_b`). Apply the **+90°-about-Z stand-up remap** (§8(b)) uniformly across all
   parts + bones + keyframes so the geometry stands upright (native-X height → Godot +Y) without
   breaking the §0 cancellation.
4. **Defensive guard (importer hardening — NOT legacy parity).** Two distinct resolvers handle
   out-of-range ids differently (§3.2):
   - The **runtime 88-byte resolver** (used by the deform loop and `.mot` track binders) **CLAMPS** to
     the last bone — when `id − base_id ≥ bone_count` it returns `pose_bone_base + 88 · bone_count − 88`.
     The downstream non-null guard never fires on a clamp, so an out-of-range `.mot` track id drives the
     last bone and an out-of-range `.skn` weight id at deform time binds the last bone.
   - The **bake-time 72-byte bind resolver** (used by the inverse-bind bake, §4) returns **NULL** with
     no clamp when `id − base_id ≥ bone_count`. The bake has no null guard at the call site — a `.skn`
     weight whose bone id is out of range is a hard fault at bake time.
   Both outcomes are wrong for a mismatched rig; the rig-identity invariant is what actually matters.
   For a robust importer **recommend SKIPPING** any track/weight whose `bone_id` falls outside
   `[base_id, base_id + bone_count)` rather than reproducing either legacy behaviour — a skipped
   influence is inert, whereas a clamp piles geometry onto one bone and a NULL dereference faults the
   bake. Treat "skip" as the recommended hardening (`formats/animation.md` §Bone-track linkage).
5. **Honour §6.3 (secondary hazard, not the cause):** idle clips store a **non-zero translation on
   nearly every child track on disk**, which the legacy engine ignores — child bones rotate only and
   keep their bind-pose local translation; only the root translates. Feed **rotation tracks for child
   bones and a position track for the root only**. Applying the stored child translations stretches bone
   lengths and can itself shatter the mesh — this would affect every class, so it is a separate fidelity
   requirement that must be respected once the rig/clip identity above is correct.

Bring-up assertion: after step 1–3 the trio is self-consistent for **all** classes, so the cancellation
invariant of §8(a) holds at rest and the animation reproduces correctly. If a class is clean at rest but
shatters on its idle, re-check that its rig and clip were resolved from that class's own `id_b` and not a
shared default. This is the recovered cause of the char-create preview shatter cross-referenced in
`frontend_scenes.md` §3.7.5.

---

## 9. Open items

| Item | Status | Impact |
|---|---|---|
| Inverse-bind **bake** existence + conjugate/order (§4) | RESOLVED (CONFIRMED static, CYCLE 1) — the bake is pinned as a separate skin-attach pass: conjugate `(−x,−y,−z,w)`, subtract bind-world translation then rotate by the inverse bind-world quaternion, against the rest/bind world pose, normal rotation-only, quaternion-based (no matrices). No longer a hypothesis and no debugger needed | None — the math is settled; importers implement §4 directly |
| Skeleton-selection key + skeleton preload (§8(e)) | RESOLVED (CONFIRMED static, CYCLE 1) — the `.skn` `id_b` is the verbatim pose-pool key (no `g{N}.bnd` formatting / slot transform at the resolve site); all listed `.bnd` are eager-preloaded at boot keyed by parsed `actor_id`; mobs reach the same pool via the animation-catalogue indirection | None for the resolve mechanism; the concrete `model_class_id → loaded `.bnd`` value-edge + the per-category base-offset table remain value-edges (§3.5.5) |
| Exact Godot quaternion remap under Z-negation | PROPOSED — `(x,y,z,w) → (−x,−y,z,w)` is the expected mapping but must be checked against one real bone rotation | Get it wrong and the rig twists; validate before mass import |
| `.skn` GEOMETRY height-axis + importer stand-up remap | **CORRECTED 2026-06-21 (visual-oracle): native X → +90°-about-Z importer remap.** The rest-mesh is authored X-TALL (height along native X), measured against the visual oracle (raw rest-mesh bytes tall-along-X *pre-deform*, e.g. g1 body ≈ X 5.0 > Y 2.4 > Z 1.7; deformed frame-0 AABB recumbent at an identity pivot). It is a pure asset-orientation property — the inverse-bind cancellation passes at the float-noise floor — so a faithful import applies a **+90°-about-Z importer (display-node) stand-up remap** (native +X height → Godot +Y), applied uniformly to bones+verts+keyframes. SUPERSEDES the prior "RESOLVED to Y-up / identity import" reading. The world-PLACEMENT / HEADING up axis stays Y (yaw about Y), a separate quantity that is unaffected | The "avatar lies on X" symptom is the as-authored geometry axis — **ADD** the +90°-Z stand-up remap (§8(b)); do NOT remove a non-existent upstream rotation. Verify with the deformed-pose AABB (tall along Y with the remap applied). A future engineer MUST NOT re-zero `UpAxisRemapDeg` on stale "identity" wording |
| Epsilon tests — accumulate / commit denominator floors (§6.2) | **RESOLVED (static-confirmed)** — both floors use a **genuine `logf` call** (real CRT logarithm): the floor branch is taken when `logf(committedWeight + accumWeight) < 0.001`; denominator is forced to `0.001` in that branch. The "decompiler mis-symbol of an abs-value clamp" framing is dropped. See §6.2 note | No importer impact in practice (floor fires only at vanishing weight); a faithful port reproduces the `logf` form |
| Epsilon test — per-axis vertex-dedup tolerance (§2.1) | CAPTURE/DEBUGGER-PENDING — surfaces as a log-shaped intrinsic compared against 0.001; not re-checked this audit pass; whether it is a genuine `logf` or a plain absolute-value compare is open | Treat as a 0.001 per-axis absolute-value tolerance until confirmed; no importer impact |
| Per-mesh + per-node `scale` (§3.4, §5.3, §6.6) | RESOLVED (CAMPAIGN 9, re-confirmed CAMPAIGN 10) — per-mesh scale is a skin-object field set at attach as `nodeScale · meshScale` (× optional non-zero override); a separate per-node scale lives at runtime bone +84; both generally non-unit | The importer **must read and apply** the per-mesh scale to positions and the per-node scale to bone-local translation (not normals, not rotations); do NOT assume 1.0 |
| Faithful vs. renormalized interpolation alpha (§6.1) | PROPOSED choice — both are documented; pick one per project taste | Affects playback feel, not correctness; document the choice |
| `actormotion.txt` columns 3–14 semantics | PROPOSED — offsets/types confirmed, meanings inferred (see `formats/animation.md` §`actormotion.txt` layout) | Not needed to deform; do not branch on these until confirmed |
| Multi-bone character `.skn`/`.bnd` byte-level cross-check of the inverse-bind bake | PARTIALLY VERIFIED — corpus confirms multi-weight skins exist (§5.2); the bake math is code-recovered, not yet byte-validated end-to-end on a real character | Validate against the §8(d) player trio; assert the cancellation invariant |
| Which skeleton the original char-create preview pairs with class 4 (§8(e)) | PLAUSIBLE (disk-implied: the class-4 skin's own `id_b` selects the 89-bone rig) — to be ratified against the live original | The recovered fix resolves rig + clip from the skin's `id_b` per class; the live ratification only confirms the original makes the same per-class choice |
| Runtime standing-idle slot selection (§10) | **RESOLVED static (CYCLE 7)** — the standing/stand-still idle clip (motion-kind 0) = actormotion **column 16** (record +0x44, direction array A element 1), read by every motion-kind-0 idle path; record +0x40 (col15, element 0) has ZERO read-sites (statically dead). Keyed by the appearance key, resolved via the motlist.txt registry (motion id == `.mot` header id_b), NOT a `g{id}.mot` sprintf. The live-vs-static slot question is settled by the static read-site evidence | Select column 16 (not col15) for the stand idle; resolve via the registry, not a filename sprintf. Whether that clip's *data* animates is a per-asset matter (§10) |

---

## 10. The standing human idle is static DATA — faithful, not a missing-animation defect

> Provenance: promoted from two dirty-room lanes (gitignored) that jointly settled the long-standing
> "character idle is flat/static" observation — one read the engine sampler/advance math, the other
> diffed the keyframes of the real `.mot` through the production parser over the maintainer's own VFS
> sample. **Engine math: control-flow-confirmed. Keyframe diff: sample-verified.** The runtime
> slot-selection question is now **RESOLVED static (CYCLE 7)** — see the correction note below.

> **CORRECTION — the standing-idle SLOT is column 16, not column 15 (CONFIRMED static, CYCLE 7).**
> The runtime stand/idle clip (motion-kind 0) is **direction array A element 1 = in-memory record +0x44
> = actormotion.txt column 16**, looked up by the actor's **appearance key** `5·(class + 4·variant) − 24`
> (NOT by col2/skin_class). Every motion-kind-0 idle read-site reads record +0x44; **record +0x40
> (column 15, array A element 0) has ZERO read-sites — it is statically dead for clip selection.** This
> is the same element-0-unused off-by-one already proven for the direction-B array (consumers start at
> element 1). The earlier "col15 / `motion_ids_a[0]`" attribution below is therefore the off-by-one to
> avoid; read every "col15 / `motion_ids_a[0]` stand idle" in this section as **column 16 / array A
> element 1**. Resolution is via the **motlist.txt clip registry** (the column-16 value is a motion id
> that equals a `.mot` file's header `id_b`, the registry key), **not** a `g{id}.mot` sprintf — no
> `g%d.mot` format string exists in the binary (the only `g%d` asset format is `data/char/skin/g%d.skn`,
> a SKIN path). The "static stand pose is faithful, not a defect" conclusion below still holds for
> whichever clip the column-16 slot resolves to; only the slot/key/resolution mechanics are corrected.

This section resolves the framing of the character-idle observation. A standing human in the port
looks frozen while mobs animate. That is **not** a parser bug, a missing-animation defect, or a
residue of the (now retired) mesh-explosion debt. It is the **faithful** result of the static data the
recovered idle chain resolves (whichever clip the column-16 stand slot resolves to is rendered as-is).

### 10.1 The engine DOES animate a looping idle — it never pins time (CONFIRMED)

The engine feeds the animation mixer **real elapsed time every frame** and advances every active
layer's clock; the per-track sampler therefore always sees a moving `t` for an active layer. There is
no code path that samples a started, weighted layer at a fixed `t = 0`. The math (10 fps `floor(t·10)`
frame pair, raw-seconds alpha, LERP translation + shortest-arc SLERP rotation) is exactly §6.1; the
per-frame `dt = ms × 0.001` source and the cycle-layer time advance (free-run `local_time += rate·dt`
with modulo wrap, or sync-mode `t = duration × phase/range`) are documented in
`formats/animation.md` §Per-frame clip-time advance and §Wrap and loop behaviour. **Consequence:** a
short looping idle in the original is *alive* exactly when its keyframes differ — the engine cannot
produce a flat result from a clip whose keyframes carry motion.

### 10.2 The observed human stand idle's keyframes do NOT differ — it is static data (SAMPLE-VERIFIED)

> **Slot/key corrected (CYCLE 7):** the runtime stand slot is **column 16 (record +0x44, array A
> element 1)** keyed by the **appearance key**, resolved via the **motlist.txt registry** (motion id ==
> `.mot` header id_b) — see the §10 correction note. The "col2 → col15 → `g{id}.mot`" chain below is the
> superseded attribution; the keyframe-diff observation of a static stand `.mot` stands on its own as a
> per-asset finding.

A keyframe diff of the observed human stand clip (`data/char/mot/g101100001.mot`) through the production
parser shows it is a **fixed stand pose**:

- `frame_count = 3`, `track_count = 84`, with 3 keyframes on **all** 84 tracks (a full-shape clip,
  not a stub);
- **0 of 84 tracks animate** — maximum translation delta is exactly **0.0** on every bone, and the
  single non-zero rotation delta is **≈1.0e-6** (one bone, at the float-noise floor).

So every frame samples the same pose; the clip pins the skeleton to one held stand pose. The full
keyframe-diff table and positive controls (mob clips and other human slots that animate strongly under
the identical metric, proving the metric detects motion) are in `formats/animation.md`
§Static idle clips.

### 10.3 The rig HAS animated idle content — just not in the observed stand slot

Other slots in the same human direction-A array **do** animate: a `peace`-tagged slot is a subtle
breathing/idle-sway loop (51 of 84 bones move), and a combat slot moves strongly. So animated idle
content exists for the human rig; the observed stand slot is specifically the **static stand snapshot**.
A visible breathing idle in the port would require selecting a **different**, animated slot. Note the
sibling idle slots recovered alongside the stand idle: the default-idle-cycle / state-8 idle is array A
element 5 (record +0x54, column 20), and the alt-idle (motion-kind 1) is array A element 6 (record
+0x58, column 21) — distinct columns from the motion-kind-0 stand idle at column 16.

#### 10.3.1 Standing-idle slot selection — RESOLVED static (CYCLE 7): motion-kind 0 → column 16

> **RESOLVED static (CYCLE 7).** An actor carries a client-side **motion-kind word** (a small per-actor
> state field) that selects which catalogue slot supplies the idle clip. Reading every motion-kind-0
> (stand) branch directly: the **stand/idle case reads the catalogue record at +0x44 = direction array
> A element 1 = actormotion column 16**, and applies that motion id to the actor's animation mixer. The
> alt-idle case (motion-kind 1) reads +0x58 (element 6, column 21); the default-idle-cycle / state-8
> idle reads +0x54 (element 5, column 20). The lookup is keyed by the actor's stored **appearance key**
> `5·(class + 4·variant) − 24`, not by col2/skin_class (skin_class selects the `.bnd` skeleton and
> builds the catalogue record's key — it is not the runtime idle lookup key).
>
> **Record +0x40 (column 15, array A element 0) has ZERO read-sites — it is statically dead for clip
> selection.** A reg+stack scan of every catalogue-getter call-site shows no record read at +0x40. So
> the live-vs-static slot question is **settled by the static read-site evidence**: for a plain standing
> human the engine plays the **column-16** stand clip. (The prior "6-case, value-mapping
> debugger-pending" framing is superseded — the stand slot is pinned statically; whether the
> *column-16 clip's data* animates is a separate per-asset matter, §10.2.)

### 10.4 What is settled vs. open

| Question | Verdict |
|----------|---------|
| Does the engine advance clip time for a looping idle? | YES — real per-frame `dt`, never pinned to 0 (CONFIRMED). |
| Which slot is the runtime stand/idle clip? | **Column 16 (record +0x44, direction array A element 1), keyed by the appearance key — RESOLVED static, CYCLE 7.** Record +0x40 (col15, element 0) is statically dead (no read-site). |
| How is the stand idle clip resolved to a file? | Via the **motlist.txt clip registry** (the column-16 value is a motion id == `.mot` header id_b, the registry key) — **NOT** a `g{id}.mot` sprintf (no such format string exists). |
| Is the observed human stand `.mot` static data? | YES — 0/84 animated tracks, a fixed held pose (SAMPLE-VERIFIED, per-asset). |
| Is a frozen standing human in the port a bug? | NO — it is **faithful** to the static stand asset. The mesh renders correctly via quaternion LBS (§0 cancellation holds) once the +90°-about-Z geometry stand-up remap is applied (§8(b)); the mesh-explosion debt is **retired**. |
| Which standing-idle slot does the **live engine** select at runtime? | **RESOLVED static (CYCLE 7)** — the column-16 stand clip; settled by the static read-site evidence (no debugger needed). |

### 10.5 Guidance for the port

- Select the stand idle from **column 16** (record +0x44, direction array A element 1) keyed by the
  appearance key, and resolve it through the **motlist.txt registry** (motion id == `.mot` header id_b)
  — **not** column 15 and **not** a `g{id}.mot` sprintf.
- Render that idle clip **faithfully**: if its data is a static stand, a static stand is correct for
  that asset. Do **not** synthesize a breathing idle or treat the flat result as a defect to "fix" in
  the parser/mixer.
- The importer must still drive the active clip's clock with real per-frame `dt` and wrap at clip end
  (§6, `formats/animation.md` §Per-frame clip-time advance) so that **animated** clips (mobs, combat,
  the animated sibling idle slots) play correctly — the static look must come from the data, never from
  a port that fails to advance `t`.
- If a breathing standing idle is desired as a presentation choice, select an animated direction-A slot
  (e.g. the `peace`-tagged slot, §10.3) rather than the column-16 stand snapshot — a deliberate
  presentation choice, not a parity requirement.

---

## Cross-references

- **Skinned mesh / skeleton bytes:** `formats/mesh.md` (`.skn` weights, `.bnd` bones, quaternion XYZW
  order, the multi-bone notes added alongside this spec).
- **Animation clip bytes + mixer:** `formats/animation.md` (`.mot` tracks/keyframes, 10 fps timing,
  raw-seconds alpha, the BANI variant, `actormotion.txt`).
- **Container:** `formats/pak.md` — VFS archive that delivers `.skn` / `.bnd` / `.mot`.
- **Char-create preview (rig/clip identity, §8(e)):** `frontend_scenes.md` §3.7.5 (preview-character
  assets for the four starter classes).
- **Canonical names:** see `Docs/RE/names.yaml` (`SkinFile`, `BindPoseFile`, `BndBone`, `MotionClip`,
  `BoneTrack`, `Keyframe`, `AnimationMixer`).
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
