# Skinning & animation pipeline (clean-room spec)

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

> **Headline finding — why the character mesh currently explodes.**
> The legacy renderer is **CPU linear-blend skinning (LBS)** with a **load-time inverse-bind bake**.
> At rest (animation == bind pose) the inverse-bind and the forward bone transform **cancel exactly
> to the identity**, so the rest mesh is reproduced unchanged. The current Godot avatar explodes
> because it skins *without* that cancellation property — either the inverse-bind step is missing,
> the bone-transform multiply order is wrong, or the handedness conversion is applied piecemeal to
> verts but not to bones (or vice-versa). The fix is to reproduce the cancellation, applying ONE
> handedness conversion uniformly to bones + vertices + keyframes. See §8 (Godot import guidance).

| Area | Confidence |
|---|---|
| CPU LBS, no GPU bone palette, no 4×4 matrices in the skinning math | HIGH |
| Inverse-bind baked once at load into per-influence bone-local rest position/normal | HIGH (full deform chain recovered end-to-end) |
| Bind-pose world transform accumulated from parent-relative `.bnd` locals | HIGH |
| Bones addressed by **bone ID** (`id − base_id`), not array position, by both `.skn` weights and `.mot` tracks | HIGH |
| Major/minor influence split + per-vertex normalization to sum 1.0; skip weight < 0.01 | HIGH (code) + SAMPLE-VERIFIED (corpus: min weight 0.010, 1140 multi-weight skins) |
| LBS deform equation (weighted sum of bone-local rest placed by animated bone world transform) | HIGH |
| `.mot` sampling: `floor(t·10)` @ 10 fps, LERP translation, shortest-arc SLERP rotation | HIGH |
| Interpolation alpha is RAW seconds in `[0, 0.1]`, not renormalized to `[0,1]` | HIGH (observed); intentional-vs-defect UNVERIFIED |
| Pose composition: `parentWorld ⊗ bindLocal ⊗ animLocal`; child bones rotate-only; root-only translation | HIGH |
| Quaternion convention: XYZW (scalar W last), Hamilton product, active rotation, parent-on-left | HIGH |
| Native space is left-handed D3D9; NO axis flip inside the skinning math | HIGH |
| Exact Godot quaternion remap under Z-negation | PROPOSED — validate on one sample bone |
| Per-mesh `scale` has a real source (read at attach as `meshScale · nodeScale`) | CONFIRMED — resolves the prior "assumed 1.0" open item |
| Whole deform/bind/mot/world-walk chain re-derived end-to-end | RATIFIED — an independent CAMPAIGN 9 re-derivation reproduced §0–§7 and corrected nothing |

> **Ratification (CAMPAIGN 9).** An independent dirty-room re-derivation read the actual deform,
> bind-pose, `.mot`, and world-walk routines (and their math primitives) from scratch and
> **confirmed this spec is correct** — the deform equation, the quaternion product order
> (parent-on-left, XYZW Hamilton), the active-rotation `q ⊗ v ⊗ q⁻¹` vector transform, the `.mot`
> 10 fps / 28-byte-keyframe / raw-seconds-alpha sampling, the animated world walk
> `parentWorld ⊗ bindLocal ⊗ animLocal`, and the no-axis-flip-inside-the-math finding were all
> reproduced and **nothing was corrected**. The pass additionally resolved the per-mesh `scale`
> source (see §5.3 / §9). Recovered via static RE, CAMPAIGN 9.

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
- **Mode 2 — rigid fast path** using a position-proximity ownership table (single-bone-owned vertices
  transformed once, then copied to merged duplicates).

For a faithful modern re-implementation, **Mode 0 is the one to port**; Modes 1 and 2 are
optimizations of the single-influence case and produce the same result whenever every vertex has
exactly one influence.

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
see `formats/mesh.md` §Vertex table) plus the per-corner UVs from the face table. Render vertices are
**deduplicated by position** (tolerance ≈ 0.001) so shared triangle corners collapse to one skinned
vertex; a corner→unique-vertex index map and a vertex→owner (rigid-merge) table are built at load.

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
`self_id`. This is the single most important indexing fact for the importer:

- A **`.skn` weight's `bone_index` is a bone ID**, not a palette slot or array position.
- A **`.mot` track's `bone_id`** (low byte of the track descriptor) is the same bone ID.

For the recovered sample skeletons `base_id == 0`, so ID equals array index — but the importer **must
not assume** `base_id == 0` in general. Always resolve `bone_array[id − base_id]`.

### 3.3 Composition order (multiply convention)

Hierarchy composition is **child = parent ∘ local, parent on the LEFT** (pre-multiply / row-vector
style). This holds for both the bind-pose world walk (§3.1) and the animated world walk (§6.6).

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
  handle is the actor's skeleton (§3, §8(e)).

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

### 3.5.5 Pending value-edges (do NOT invent)

Two data-driven edges are CODE-CONFIRMED in mechanism but their concrete values await a live-debugger
confirm and must not be invented:

- the **`categoryBase[]`** array contents (the per-category base offsets used in both the catalogue
  key and the actormotion key);
- the concrete **`model_class_id` -> bind-pose handle** mapping (which loaded `.bnd` each of
  `{1, 11, 16, 26}` selects — the data-driven `{1->g1, 26->g2, 11->g3, 16->g4}` edge of §8(e)).

<!-- source: _dirty/campaign5/character-appearance-assembly.md -->
<!-- pending live-debugger value-edges: catalogue categoryBase[] contents; model_class_id -> concrete bind-pose handle -->

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

---

## 5. Weight application — linear blend skinning

### 5.1 The disk weight record's three fields

The 12-byte `.skn` weight record's fields are exactly:

| Disk field | Meaning |
|---|---|
| `vertex_index` | which vertex this influence affects |
| `bone_index` | **bone ID** (resolves the bind/pose bone by `id − base_id`, §3.2) — no indirection table |
| `weight` | scalar influence; records with `weight < 0.01` are dropped at load |

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

> **`scale` has a real source — do NOT assume 1.0 (CONFIRMED, CAMPAIGN 9).** The per-mesh scale is a
> field on the skin object, populated **at attach time** as the product `meshScale · nodeScale` (with
> an optional override factor). It is therefore **generally non-unit** and must be read from the skin
> object, not hard-coded to 1.0. The deform loop and the animated world walk (§6.6) both multiply by
> this same scale (positions only — never normals, never rotations). This resolves the prior §9 open
> item that listed `scale` as "assumed 1.0; setter not traced." An importer that lets the engine skin
> (Godot `Skeleton3D`, §8(a)) must still apply this mesh scale to the rest geometry / node transform;
> dropping it shrinks or inflates the whole character.

### 5.4 Influences per vertex are unbounded by the format

The format imposes no fixed 4-bone cap; the major array contributes one influence and the minor array
contributes the rest, summed for the vertex. Newer skins happen to use a fixed 4-influence convention,
but older skins use a variable count. An importer that must cap to its engine's per-vertex influence
limit **must re-normalize the surviving weights to sum 1.0** after dropping the smallest influences,
to preserve the convex-combination property.

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
   LERP/SLERP by `w_new / (w_acc + w_new)`, the denominator floored at 0.001).
3. **Commit pass:** fold the accumulated sample into each node's **local animated** translation/rotation
   slots, blended against any previously committed value. **Child-bone special case:** for a non-root
   bone with a full parent chain, the local **translation is forced to the bind-pose local
   translation** — i.e. **child bones keep their fixed bind-pose bone length and only rotate**. Only
   the root translates freely.
4. **Cycle-layer pass:** same accumulate-then-commit for the looping clips, using the sync-mode sample
   time where applicable (see `formats/animation.md` §Sync-phase mechanism).
5. **Root + heading:** the root node's world translation is set from the actor's world position; the
   root world rotation folds in the smoothed heading (yaw) quaternion.
6. **World walk:** fill every node's animated world transform from the committed local poses (§6.6).

### 6.3 Child bones rotate only; only the root translates

This is the rule from step 3 restated because it matters for the importer: animation drives **rotation**
on child bones and **translation only on the root**. A child bone's local translation is held at its
bind-pose value every frame. An importer should therefore feed **rotation tracks for child bones and a
position track for the root only**, to match legacy behaviour.

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
worldTrans = parentWorldQuat ⊗ ( localAnimTrans · scale ) + parentWorldTrans
worldQuat  = ( parentWorldQuat ⊗ bindLocalQuat ) ⊗ localAnimQuat
```

So per node the rotation is `parentWorld ⊗ bindLocal ⊗ animLocal`, and the translation is the parent's
world rotation applied to the (scaled) animated local translation plus the parent's world position
(rotate-then-translate). These `worldTrans` / `worldQuat` are exactly the `boneWorldTrans` /
`boneWorldQuat` the deform loop (§5.3) reads.

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
| Render space | **D3D9 left-handed**; **no axis flip inside the skinning math** |
| Vertex stream | 32 bytes: pos[0..11], normal[12..23], uv[24..31]; `uv.v` stored as `1.0 − v` |

**No axis negation or mirroring happens inside the skinning math.** The known project conventions
(world negates Z; `.skn` mesh-local geometry negates X) are **importer-layer transforms**, not engine
internals. Bone space and rest-mesh space are the same native left-handed space — that is precisely why
the inverse-bind and the forward transform cancel. §8 explains how to bridge this to Godot without
breaking the cancellation.

---

## 8. Godot import guidance

The legacy math lives entirely in the engine's native **left-handed** space and is internally
consistent. Godot is **right-handed, Y-up**. Four things must be honoured.

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

### (b) Unify the project's two ad-hoc flips into ONE handedness conversion

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

> Provenance: promoted from a dirty-room root-cause note kept under
> `Docs/RE/_dirty/campaign4/charselect3d/class4-shatter-mot.md` (gitignored). Asset byte facts are
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

1. Parse the base `.skn` and read its `id_b` (`formats/mesh.md` §Header). Resolve the deform skeleton as
   `data/char/bind/g{id_b}.bnd`, **per class** — never a single shared rig hard-coded across all classes.
2. Select the idle clip from `actormotion.txt` keyed by `skin_class == id_b` (col2 → col16), **per
   class** — never a single shared idle clip. Each clip's track count equals its rig's bone count.
3. Skin **every** overlay part onto that **same** `id_b`-selected skeleton (all of a class's overlays
   carry the class's `id_b`).
4. **Defensive guard:** in the track→bone binder, **SKIP (do not clamp)** any clip track whose `bone_id`
   falls outside `[base_id, base_id + bone_count)` of the bound skeleton (unmatched `bone_id` is a
   non-fatal skip — `formats/animation.md` §Bone-track linkage). Clamping the index into range would
   still drive the wrong bone; the track must be dropped, not redirected.
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
| Exact Godot quaternion remap under Z-negation | PROPOSED — `(x,y,z,w) → (−x,−y,z,w)` is the expected mapping but must be checked against one real bone rotation | Get it wrong and the rig twists; validate before mass import |
| Per-mesh `scale` (§5.3, §6.6) | RESOLVED (CAMPAIGN 9) — the scale is a real skin-object field set at attach as `meshScale · nodeScale` (× optional override); it is generally non-unit | The importer **must read and apply** the mesh scale to positions (not normals, not rotations); do NOT assume 1.0 |
| Faithful vs. renormalized interpolation alpha (§6.1) | PROPOSED choice — both are documented; pick one per project taste | Affects playback feel, not correctness; document the choice |
| `actormotion.txt` columns 3–14 semantics | PROPOSED — offsets/types confirmed, meanings inferred (see `formats/animation.md` §`actormotion.txt` layout) | Not needed to deform; do not branch on these until confirmed |
| Multi-bone character `.skn`/`.bnd` byte-level cross-check of the inverse-bind bake | PARTIALLY VERIFIED — corpus confirms multi-weight skins exist (§5.2); the bake math is code-recovered, not yet byte-validated end-to-end on a real character | Validate against the §8(d) player trio; assert the cancellation invariant |
| Which skeleton the original char-create preview pairs with class 4 (§8(e)) | PLAUSIBLE (disk-implied: the class-4 skin's own `id_b` selects the 89-bone rig) — to be ratified against the live original | The recovered fix resolves rig + clip from the skin's `id_b` per class; the live ratification only confirms the original makes the same per-class choice |

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
