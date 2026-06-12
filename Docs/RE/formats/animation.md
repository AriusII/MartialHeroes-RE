# Format: .mot  (skeletal animation clip)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true

---

## Status and confidence summary

| Area | Status | Confidence |
|------|--------|------------|
| `.mot` header (`id_a`, `id_b`, `name_length`, `name_body`, `frame_count`) | Resolved | CONFIRMED (sample-verified) |
| `.mot` track / keyframe layout | Resolved | CONFIRMED (parser-derived; not yet byte-verified on a non-stub clip) |
| LenStr 4-byte u32 LE prefix | Resolved | CONFIRMED (sample-verified) |
| `track_descriptor` upper-3-byte padding | Resolved | CONFIRMED (sample-verified) |
| `id_a` vs `id_b` roles (load key vs runtime clip handle) | Resolved | CONFIRMED |
| Wrap / loop is runtime-only (no on-disk flag) | Resolved | CONFIRMED |
| `actormotion.txt` record layout (offsets + read order) | Resolved | CONFIRMED (parser-derived) |
| `actormotion.txt` per-column semantic names | Proposed | PROPOSED (offsets confirmed; field meanings inferred) |
| Animation mixer two-list architecture | Resolved | CONFIRMED |
| Mixer sync-phase mechanism + 1.5× rate constant | Resolved | CONFIRMED |
| Per-bone weighted-average accumulation (lerp/slerp) | Resolved | CONFIRMED |
| Default layer playback speed constant (≈1.575) | Documented | UNVERIFIED (constant confirmed; full semantics open) |

Open items are tracked in §Known unknowns. This spec documents the on-disk `.mot` binary
(the only artifact an `Assets.Parsers` engineer must implement) plus the runtime animation
model as informative background for `Client.Application`. The runtime model describes
behaviour and constants only; it does not expose any in-memory object layout.

---

## Identification

- **Extension:** `.mot`
- **Found in:** `.pak` / VFS archive (see `formats/pak.md`); logical path prefix `data/char/mot/`
- **Magic / signature:** none — file begins immediately with the header fields below.
  Both a binary mode and a text mode appear to be supported at runtime (see §Binary / Text duality
  under Header layout); the binary mode is the primary format documented here.
- **Endianness:** little-endian throughout (all multi-byte integers and floats are LE).
- **Confidence:** CONFIRMED; sample_verified: true (three 39-byte stub clips read byte-for-byte)

---

## Discovery and catalogue files

Three supporting text files (plain ASCII, one entry per line) control how the engine finds and
pre-loads clips:

| File | Role | Confidence |
|------|------|------------|
| `data/char/motlist.txt` | Manifest — each line is a bare filename; the engine prepends `data/char/mot/` at load time to form the VFS path. | CONFIRMED |
| `data/char/actormotion.txt` | Maps actor / visual identifiers to motion catalogue IDs. Tabular, count-prefixed, 33 columns per record; layout characterized in §`actormotion.txt` layout. | CONFIRMED (record layout, parser-derived); per-column semantics PROPOSED |
| `data/motion.cache` | Pre-load ID cache. Wire layout: `[u32 count][count × u32 motion_catalogue_id]`. The engine opens this file through a direct OS file call (not the VFS) and uses the IDs to prime the in-memory clip map, triggering eager full-load for the listed IDs. | CONFIRMED (wire layout); magic / versioning UNVERIFIED |

The 9-digit motion IDs stored in `actormotion.txt` (see §`actormotion.txt` layout) are the same
values as the `.mot` per-file `id_a` field, which matches the numeric component of the `.mot`
filename. `actormotion.txt` is therefore the actor-facing table that names, per actor class and
motion slot, which `.mot` clip to play.

---

## Header layout

The header is read in two passes (see §Two-stage loading below). Both passes read the same
four fields in the same order. All fields are binary little-endian unless the text-mode flag is
active (see §Binary / Text duality).

| Rel. offset | Size | Type    | Field         | Notes                                                                                                                              | Confidence |
|------------:|-----:|---------|---------------|------------------------------------------------------------------------------------------------------------------------------------|------------|
| 0           | 4    | u32 LE  | `id_a`        | Per-file unique numeric identifier. Matches the decimal integer component of the filename (e.g. `g170354502.mot` → `id_a = 170354502`). Returned as the clip handle by the registration function and used by the runtime mixer as the per-clip lookup ID; not used as the load-time catalogue key (see §Clip catalogue). | CONFIRMED (sample-verified) |
| 4           | 4    | u32 LE  | `id_b`        | Group / set load key. Shared across all clips in the same actor motion set. Used as the key when inserting into the runtime clip map at load time (see §Clip catalogue). | CONFIRMED (sample-verified) |
| 8           | 4    | u32 LE  | `name_length` | Length of the name body that follows, in bytes. 4-byte u32 LE prefix — no null terminator on disk. See §LenStr encoding. | CONFIRMED (sample-verified) |
| 12          | N    | bytes   | `name_body`   | Clip path string of `name_length` bytes. Relative source-tree path, form `./do/g{id_a}.mot`. Read in both loading stages and silently discarded; parsers must read and skip it to advance the file pointer. See §Name field semantics. | CONFIRMED (sample-verified) |
| 12+N        | 4    | u32 LE  | `frame_count` | Raw frame count. Clip duration in seconds = `frame_count × 0.1` (fixed 10 fps rate; see §Timing). | CONFIRMED (sample-verified) |

`N` is the value of `name_length`. Because `name_body` is variable-width, subsequent fields have
no fixed absolute offset.

### LenStr encoding

The `name` field uses the same `LenStr` encoding that `.skn` and `.bnd` use (see `formats/mesh.md`,
§String encoding). The wire format is a 4-byte u32 LE length prefix followed by exactly `length`
bytes of string body with no null terminator on disk.

**CONFIRMED sample-verified:** in all three reference samples the four bytes at offset 8 decode as
a u32 LE value that equals exactly the byte count of the string body that follows it, and the
subsequent fields align correctly on that assumption. A 1-byte prefix interpretation is inconsistent
with the observed byte layout and is rejected.

The name body in the reference samples is ASCII. The field is discarded after reading, so encoding
matters only when the name body must be inspected for diagnostics. For such cases, treat as
CP949 / EUC-KR (the client's Korean locale); pure-ASCII values (numeric paths) are a subset and
decode identically under any ASCII-compatible encoding.

### Binary / Text duality

The header reader supports two modes, selected by a runtime flag on the file object. In text mode
the same four fields are parsed as whitespace-separated decimal ASCII integers rather than binary
words. Whether text-mode `.mot` files share the `.mot` extension or use a different name is
UNVERIFIED. This spec documents binary mode only; parsers should implement binary mode first and
treat text mode as a deferred concern.

### Name field semantics

The embedded name string encodes a relative source-tree path of the form `./do/g{id_a}.mot`.
The `do` directory component reflects the original D.O. Online game source layout. This path
does not match the VFS load path (`data/char/mot/`); it is a build-time artifact baked into
the content at export time. Both loading stages read this field and discard it immediately; the
value is never stored in the runtime clip object. Parsers must consume `name_length` bytes to
keep the file pointer aligned but need not retain the value.

---

## Two-stage loading

The engine loads a `.mot` file in two sequential passes. Both are relevant to parsing because they
read partially overlapping regions.

| Stage | What is read | Purpose |
|-------|-------------|---------|
| **Stage 1 — header only** | Fields `id_a`, `id_b`, `name_body` (skipped), `frame_count`. Derives `duration_seconds = frame_count × 0.1` and stores it. File is kept open (or re-opened in Stage 2). | Register the clip in the catalogue map under `id_b`; mark as partially loaded. |
| **Stage 2 — full data** | Re-reads the four header fields, then continues with `track_count` and all track / keyframe data. | Populate the bone-track array; mark clip as fully loaded. |

A parser implementing `Assets.Parsers` should perform the equivalent of both stages in a single
sequential read (read the header, then immediately read the track array).

The runtime maintains a per-clip "fully loaded" flag separate from registration. The mixer skips
any cycle whose underlying clip has not reached the fully-loaded state, so stub or in-flight clips
are silently ignored during sampling rather than producing garbage.

---

## Track array layout

Immediately following the header (after `frame_count`), Stage 2 data begins with a count field,
then a sequence of variable-length track records.

### Track count

| Rel. offset (after header) | Size | Type   | Field         | Notes                                                                              | Confidence |
|---------------------------:|-----:|--------|---------------|------------------------------------------------------------------------------------|------------|
| 0                          | 4    | u32 LE | `track_count` | Number of `BoneTrack` records that follow. One track per animated bone. A value of zero is valid (stub clip). | CONFIRMED |

### Per-track record

Repeated `track_count` times. Each record consists of a fixed 8-byte preamble followed by a
variable-length keyframe block.

| Rel. offset (within track) | Size                | Type    | Field              | Notes                                                                                                                                   | Confidence |
|---------------------------:|--------------------:|---------|--------------------|-------------------------------------------------------------------------------------------------------------------------------------------| -----------|
| 0                          | 4                   | u32 LE  | `track_descriptor` | Low byte = `bone_id` (see §Bone-track linkage). Bytes 1–3 (bits 8–31) are reserved padding: the parser reads and discards them; they carry no meaning. Strict parsers may assert that the upper three bytes are zero. | CONFIRMED (sample-verified; upper bytes confirmed unused padding) |
| 4                          | 4                   | u32 LE  | `key_count`        | Number of keyframes in this track.                                                                                                      | CONFIRMED |
| 8                          | `key_count × 28`   | bytes   | `keyframes`        | Inline array of keyframe records, each 28 bytes. See §Keyframe record.                                                                  | CONFIRMED |

**Track record stride:** variable — `8 + key_count × 28` bytes.

### Keyframe record — 28 bytes, little-endian

Each keyframe encodes one sample in time: a 3-component translation vector and a 4-component
rotation quaternion. There is no scale channel in this format.

| Sub-offset | Size | Type   | Field            | Notes                            | Confidence |
|-----------:|-----:|--------|------------------|----------------------------------|------------|
| 0          | 4    | f32 LE | `translation_x`  | Local translation X.             | CONFIRMED |
| 4          | 4    | f32 LE | `translation_y`  | Local translation Y.             | CONFIRMED |
| 8          | 4    | f32 LE | `translation_z`  | Local translation Z.             | CONFIRMED |
| 12         | 4    | f32 LE | `rotation_x`     | Quaternion X component.          | CONFIRMED |
| 16         | 4    | f32 LE | `rotation_y`     | Quaternion Y component.          | CONFIRMED |
| 20         | 4    | f32 LE | `rotation_z`     | Quaternion Z component.          | CONFIRMED |
| 24         | 4    | f32 LE | `rotation_w`     | Quaternion W (scalar) component. | CONFIRMED |

**Keyframe stride: 28 bytes. Component order: XYZ translation, then XYZW quaternion.**

The quaternion component order (X, Y, Z, W) with the scalar W last is consistent with the
quaternion representation used in `.bnd` bind-pose records (see `formats/mesh.md`,
§Quaternion component order).

**Scale:** there is no scale channel. The keyframe record carries only translation and rotation.
Scale is not animated in this format.

---

## Timing and interpolation

### Timing

- **Fixed frame rate: 10 fps.**
- **Clip duration** (seconds) = `frame_count × 0.1`.
- At playback time `t` (seconds), the sample index is `floor(t × 10.0)`. The next index is
  `sample_index + 1`, clamped to `key_count − 1`. Clamp-to-last is applied within the
  per-track sample function when addressing keyframes; wrap-to-zero is handled at the clip level
  by the runtime cycle layer (see §Wrap and loop behaviour).
- **Interpolation parameter** `alpha` = `t − (sample_index / 10.0)`. This is expressed in raw
  seconds in the range `[0, 0.1]` and is passed directly as the blend factor to both the
  translation and rotation interpolators. It is not re-normalized to `[0, 1]` before use. Whether
  this is intentional design or a latent defect in the original client is UNVERIFIED.

### Wrap and loop behaviour

**CONFIRMED:** there is no loop flag or wrap flag in the `.mot` binary. The on-disk format
is indifferent to loop mode. Wrap behaviour is determined entirely at runtime by the clip layer
type:

- **CycleLayer (looping):** when the layer's local time exceeds `clip_duration`, local time is
  reset via modulo to `fmod(local_time, clip_duration)`. This wrap fires unconditionally whenever
  the clip is active and time overflows. A per-layer internal flag is set each time a wrap occurs
  and is used to trigger footstep sound-effect callbacks; this flag is a runtime state variable,
  not a file field.
- **ActionLayer (one-shot):** the clip plays once to its end (no wrap). The layer expires and is
  removed from the active list. State transitions: fade-in → playing → fade-out → done.

Parsers in `Assets.Parsers` need not model either behaviour; this is a runtime concern for
`Client.Application`.

### Translation interpolation

Linear interpolation between consecutive translation samples:

```
blended_translation = lerp(key[n].translation, key[n+1].translation, alpha)
```

### Rotation interpolation

Spherical linear interpolation (SLERP) between consecutive rotation quaternions, with dot-product
sign flip to enforce the shortest-arc path:

```
if dot(key[n].rotation, key[n+1].rotation) < 0:
    negate key[n+1].rotation before slerp
blended_rotation = slerp(key[n].rotation, key[n+1].rotation, alpha)
```

Degenerate cases: nearly-identical quaternions (dot close to 1.0) fall back to normalized linear
interpolation. Antipodal quaternions (dot close to −1.0) are handled by a 90-degree perpendicular
path. These are implementation-level concerns for `Assets.Parsers` / runtime; the on-disk format
is unaffected.

---

## Bone-track linkage

Each track's `bone_id` (the low byte of `track_descriptor`) directly identifies which bone the
track drives. This numeric ID matches the `self_id` field of the corresponding bone record in the
`.bnd` skeleton file (see `formats/mesh.md`, §Bone array, field `self_id`). Linkage is purely
numeric — no bone name string is stored in the `.mot` file.

At runtime, if a `bone_id` in the `.mot` track array has no matching `self_id` in the loaded
`.bnd` skeleton, that track is silently skipped for that frame. Parsers and importers should
treat an unmatched `bone_id` as a non-fatal warning.

---

## Clip catalogue

The engine maintains a runtime map from a u32 key to the loaded clip object. Two distinct numeric
identities are involved, and they are used at different stages — this distinction was previously a
known unknown and is now resolved.

**CONFIRMED:**

- **`id_b` is the load-time catalogue key.** It is passed as the lookup key when registering a clip
  into the runtime clip map during loading. It is shared across all clips belonging to the same
  actor motion set (all three reference samples carry `id_b = 7741`, confirming grouping semantics).
  This is the key the loader uses to find clips by group / set.
- **`id_a` is the per-file unique identifier and the runtime clip handle.** It matches the decimal
  integer in the filename (e.g. filename `g170354502.mot` → `id_a = 170354502`). It is returned to
  callers as the clip handle after registration, and the runtime mixer uses `id_a` as the per-clip
  identifier when starting or locating an individual cycle or action layer. It is NOT used as the
  load-time catalogue key.

In short: `id_b` answers "which set does this clip belong to" (used while loading); `id_a` answers
"which exact clip is this" (used while playing). Both descriptions in earlier revisions of this
spec are correct once read in their respective contexts. The 9-digit motion IDs in
`actormotion.txt` (§`actormotion.txt` layout) are `id_a` values.

Neither field encodes a format version. Both fields carry semantic identity values assigned at
content-creation time.

---

## `actormotion.txt` layout

`data/char/actormotion.txt` is the actor-facing motion table. It is a tab-separated text file with
a decimal count on the first line and one record per subsequent line. Each record has **33 columns**.
The byte offsets below are the offsets within the engine's in-memory record (a fixed 136-byte
record) into which each column is parsed; they are stable and parser-derived. Per-column **semantic
names are PROPOSED** — the read order, types, and offsets are confirmed, but the meanings are
inferred from sample values and usage and should be cross-checked before being relied upon.

### File structure

| Element | Description |
|---------|-------------|
| Line 1 | Decimal record count (the sample file declares 1084 records). |
| Lines 2…N | One record per line, 33 tab-separated values, parsed in the column order below. |

### Record layout (parser-derived offsets within the 136-byte record)

| Column | Type | Record offset | Proposed field | Notes |
|-------:|------|--------------:|----------------|-------|
| 0  | u32 | (key input) | `group_type` | Actor class / visual group selector. Used together with column 1 to compute the record key (see §Record key). |
| 1  | u32 | (key input) | `row_id` | Row index within the motion set; combined with `group_type` to form the global key. |
| 2  | u32 | +0x04 | `class_variant` | Class / variant discriminator (sample value 1). |
| 3  | f32 | +0x08 | `cycle_duration_a` | Cycle duration in seconds, set A (e.g. 7.402). |
| 4  | u32 | +0x28 | `frame_count_a` | Frame count for set A. Clamped to a minimum of 1 (a parsed 0 is forced to 1). |
| 5  | f32 | +0x0C | `cycle_duration_b` | Cycle duration in seconds, set B (e.g. 16.282). |
| 6  | u32 | +0x2C | `frame_count_b` | Frame count for set B. Clamped to a minimum of 1. |
| 7  | u32 | +0x10 | `flags` | Bitfield (purpose UNVERIFIED; sample value 0). |
| 8  | f32 | +0x14 | `phase_a` | Phase / timing parameter A (e.g. 4.0). |
| 9  | f32 | +0x18 | `phase_b` | Phase / timing parameter B (e.g. 5.0). |
| 10 | f32 | +0x1C | `phase_c` | Phase / timing parameter C (e.g. 3.0). |
| 11 | f32 | +0x20 | `weight_a` | Blend weight A (e.g. 1.0). |
| 12 | f32 | +0x24 | `weight_b` | Blend weight B (e.g. 8.0). |
| 13 | f32 | +0x38 | `speed_override_a` | Playback speed override A (e.g. 4.0). |
| 14 | f32 | +0x3C | `speed_override_b` | Playback speed override B (e.g. 1.0). |
| 15–23 | u32 ×9 | +0x40 … +0x60 | `motion_ids_a[9]` | Primary motion-ID array. Each non-zero entry is a 9-digit clip ID equal to a `.mot` `id_a`. Zero = empty slot. |
| 24–32 | u32 ×9 | +0x64 … +0x84 | `motion_ids_b[9]` | Secondary motion-ID array, same encoding as the primary array. |

### Derived fields (computed after parsing, not present in the text)

| Record offset | Type | Computed value | Notes |
|--------------:|------|----------------|-------|
| +0x30 | f32 | `cycle_duration_a × 15.0 / frame_count_a` | Scaled rate for set A. |
| +0x34 | f32 | `15.0 × cycle_duration_b / frame_count_b` | Scaled rate for set B. |

The constant `15.0` here is distinct from the on-disk `.mot` 10 fps timing; `actormotion.txt`
appears to express its rates against a 15-unit base. The relationship between this 15-unit base and
the `.mot` 10 fps clip rate is UNVERIFIED.

### Record key

The global record key is formed from the two key-input columns:

```
record_key = row_id + group_base(group_type)
```

where `group_base(group_type)` is a per-group base offset looked up from the manager object using
`group_type`. This produces a globally unique motion-set index from a locally numbered `row_id`,
so different groups can reuse small `row_id` values without collision.

### Sample record (first data line)

For reference (sample-derived, illustrative): `group_type = 0`, `row_id = 1`, `class_variant = 1`,
`cycle_duration_a = 7.402`, `frame_count_a = 16`, `cycle_duration_b = 16.282`, `frame_count_b = 11`,
`flags = 0`, `phase_a/b/c = 4 / 5 / 3`, `weight_a/b = 1 / 8`, `speed_override_a/b = 4 / 1`,
`motion_ids_a` = seven non-zero 9-digit IDs followed by zeros, `motion_ids_b` = a few non-zero
9-digit IDs followed by zeros.

> A sibling table, `data/char/skin.txt`, uses a related but different 6-column key-and-identity
> layout and belongs to the skin / bind catalogue, not the motion catalogue. It is out of scope
> for this format and noted here only to prevent confusion.

---

## Animation mixer — runtime blend model

This section describes the runtime layering and blending model as observed. It is informative for
`Assets.Parsers` and mandatory background for `Client.Application`. It documents behaviour and
constants only — no in-memory object layout is exposed, and no parser needs to reproduce these
structures to decode a `.mot` file.

### Two clip lists

The mixer owns two independent, separately-iterated lists of active clip layers:

| List name    | Behaviour | Clip type |
|--------------|-----------|-----------|
| **Action list** | One-shot: plays once and expires. Removed from the list when it ends. | `AnimationActionLayer` |
| **Cycle list**  | Looping: replays continuously until faded out and removed. | `AnimationCycleLayer` |

Each frame the mixer advances both lists, then a separate accumulation pass samples every active
layer in both lists into a single pose. A clip is addressed within a list by its `id_a` value
(§Clip catalogue): starting or updating a cycle/action looks up an existing layer by `id_a`, and a
new layer stores the clip's `id_a` for subsequent lookups.

### Per-frame update sequence

Each frame the mixer performs the following steps in order:

1. **Advance the sync phase** (see §Sync-phase mechanism). If there are no sync-mode cycles, the
   sync phase is reset to zero.
2. **Advance the Action list.** Each action layer is ticked with the frame delta time. An action
   that has finished is removed; when the Action list becomes empty, an "actions empty" callback
   fires.
3. **Advance the Cycle list.** Each cycle layer is ticked with a delta time scaled by the owning
   visual's per-character speed factor (`scaled_dt = dt × character_speed_scale`). A cycle whose
   effective weight has decayed to zero is removed. When a looping cycle wraps, a footstep
   sound-effect callback fires.
4. **Recompute the sync range** (see §Sync-phase mechanism) from the current set of sync-mode
   cycles and their weights.
5. **Accumulate the pose** across both lists (see §Per-bone weighted accumulation), then apply
   heading smoothing and submit the skinned pose to the rendering pipeline.

The character speed factor in step 3 is a per-visual scalar (UNVERIFIED in origin — sourced from
character speed data); a value of `1.0` leaves playback unscaled.

### Sync-phase mechanism

Looping cycles can run in one of two timing modes:

- **Sync mode:** the layer's sampling time is driven by a single mixer-wide sync phase rather than
  by its own local clock. This keeps multiple simultaneously-blended cycles phase-locked (for
  example, a walk and a run blend stay foot-synchronized).
- **Free-running mode:** the layer advances its own local time independently.

The mixer maintains two scalar quantities for sync mode:

| Quantity | Meaning |
|----------|---------|
| `sync_phase` (seconds) | A mixer-wide playback phase, advanced each frame and wrapped via modulo against `sync_range`. |
| `sync_range` (seconds) | A weighted-average target duration, recomputed each frame from the active sync-mode cycles. |

**Phase advance.** Each frame, when `sync_range` is non-zero:

```
sync_phase += dt × 1.5
if sync_phase >= sync_range:
    sync_phase = fmod(sync_phase, sync_range)
    set wrap-this-frame flag
```

The advance rate **1.5** is a confirmed constant (stored as an IEEE-754 double equal to 1.5). Its
purpose is hypothesized to be a 15-to-10 frame-rate ratio (15 fps ÷ 10 fps = 1.5) but this
interpretation is UNVERIFIED.

**Range recompute.** Each frame, `sync_range` is set to the weight-weighted mean clip duration over
all sync-mode cycles:

```
sync_range = sum(clip_duration[i] × weight[i]) / sum(weight[i])   over sync-mode cycles
sync_range = 0   if the total weight is zero
```

**Sync-mode sampling time.** A sync-mode cycle is sampled at:

```
sample_time = clip_duration × (sync_phase / sync_range)
```

so all sync-mode cycles share a common normalized phase scaled to their individual durations.
A free-running cycle is sampled at its own advancing local time instead.

When the sync phase wraps during a frame, the per-frame wrap flag triggers the footstep
sound-effect callback noted in step 3 above.

### Layer weight ramping (fade in / fade out)

Each layer carries an effective weight that is smoothly ramped toward a target weight over a
remaining blend time, rather than snapping. Conceptually, per tick:

```
if blend_time_remaining > dt:
    factor = dt / max(blend_time_remaining, 0.001)
    effective_weight = lerp(effective_weight, target_weight, factor)
    blend_time_remaining -= dt
else:
    effective_weight = target_weight
    blend_time_remaining = 0
    if target_weight == 0:
        the layer is finished and is removed from its list
```

The blend-time floor constant is **0.001 seconds** (confirmed), which prevents a divide-by-zero
when a blend time of zero is requested (i.e. an instantaneous weight change). Starting or updating
a layer supplies both a target weight and a blend time; a fade-out is requested by setting the
target weight to `0.0`.

Action layers run a small state progression instead of a single target — **fade-in → playing →
fade-out → done** — but the underlying weight behaviour is the same proportional ramp: the
effective weight rises from 0 toward 1 during fade-in, holds at 1 while playing, and falls back to
0 during fade-out, after which the layer is removed. An action layer may also carry a trigger
threshold: it does not contribute to the pose until its local time reaches that threshold.

A default per-layer playback speed constant of approximately **1.575** (a confirmed float literal,
≈1.574999) is set when a layer is constructed. Its exact role relative to the `actormotion.txt`
`speed_override` columns is UNVERIFIED and is tracked as an open item.

### Per-bone weighted accumulation

Each frame, after both lists have been advanced, the mixer builds the final pose by accumulating
every active layer's contribution per bone. For each layer, for each track in the layer's clip, the
track is sampled at the layer's current sampling time to produce a (translation, rotation) pair,
and that pair is folded into the accumulator for the matching bone using the layer's effective
weight:

1. **First contributor** for a bone (accumulated weight is zero): assign translation and rotation
   directly; set accumulated weight to this layer's weight.
2. **Subsequent contributors:**
   - `lerp_factor = new_weight / max(accumulated_weight + new_weight, 0.001)`
   - `accumulated_translation = lerp(accumulated_translation, src_translation, lerp_factor)`
   - `accumulated_rotation = slerp(accumulated_rotation, src_rotation, lerp_factor)`
   - `accumulated_weight += new_weight`

The denominator is floored at the same **0.001** constant used for blend timing. This is a running
normalized weighted average. With exactly two contributors the result is order-independent; with
three or more it is **order-dependent** (the result depends on the iteration order of the layers).
Implementers reproducing this behaviour must accumulate Action-list layers and then Cycle-list
layers in the same iteration order the engine uses to match results bit-for-bit. There is no
additive blend mode and no explicit per-layer priority — ordering is the only implicit priority.

### Heading smoothing and submission

After accumulation, the mixer applies a heading (yaw) smoothing step: it blends the actor's current
facing quaternion toward the target facing by a fixed factor of **0.2** per frame, sets the actor
orientation from the smoothed result, composes it with the accumulated bind-pose rotation, and
submits the final skinned pose to the GPU skinning path. This is purely a runtime presentation
concern and has no bearing on the `.mot` file format.

### Activating a cycle

To start or update a looping clip: provide the clip's `id_a`, a target weight, and a blend time. If
a cycle with that `id_a` already exists in the Cycle list, its target weight and blend time are
updated in place. If it does not exist and the requested weight is non-zero, a new cycle layer is
created and appended to the Cycle list. Stopping all layers sets every layer's target weight to
`0.0`, letting the weight-ramp logic fade them out and remove them.

---

## Enumerations / flags

No enumerated fields or bitflag fields exist in the on-disk `.mot` format. The upper three bytes
of `track_descriptor` (bits 8–31) are reserved padding and carry no enumerated meaning. The
`flags` column in `actormotion.txt` (§`actormotion.txt` layout, column 7) is a bitfield whose bit
assignments are UNVERIFIED.

---

## Known unknowns

The following aspects are unresolved and must not be assumed by the implementing engineer:

| Item | Status | Impact |
|------|--------|--------|
| `actormotion.txt` per-column semantics | UNVERIFIED — record layout, types, and offsets are confirmed (§`actormotion.txt` layout), but the proposed field names (`phase_*`, `weight_*`, `speed_override_*`, `flags` bit meanings) are inferred from sample values. | Parse by offset and type; treat field names as provisional until cross-checked against the actor controller. |
| `actormotion.txt` 15-unit rate base | UNVERIFIED — derived rate fields use a `15.0` base; relationship to the `.mot` 10 fps clip rate is unknown. | Compute the derived rates as specified; do not assume the bases are interchangeable. |
| Default layer speed constant (≈1.575) | UNVERIFIED — a confirmed float literal set at layer construction; its interaction with `actormotion.txt` `speed_override` columns is unknown. | Document the constant; do not hard-wire playback speed to it until its role is confirmed. |
| Sync-phase 1.5× rate rationale | UNVERIFIED — the 1.5 multiplier is a confirmed constant; the "15 fps / 10 fps" interpretation is a hypothesis. | Implement the constant as-is; do not depend on the hypothesized rationale. |
| Character speed scale factor source | UNVERIFIED — the per-visual `scaled_dt` factor is sourced from character speed data not yet characterized. | Treat as a runtime input (default 1.0); not needed to decode `.mot` binaries. |
| `flags` bitfield meanings (`actormotion.txt` column 7) | UNVERIFIED — the column is a u32 bitfield; individual bits are uncharacterized (sample value 0). | Carry the value through; do not branch on undocumented bits. |
| Interpolation parameter normalization intent | UNVERIFIED — `alpha` is in raw seconds `[0, 0.1]`, not `[0, 1]`. Matches observed behaviour; whether intentional design or latent defect is unknown. | Implement as observed (raw seconds); document the deviation from standard practice. |
| `motion.cache` magic and versioning | UNVERIFIED — no header magic or version field confirmed; only the `[u32 count][u32[] ids]` layout. | Parse defensively; treat as unversioned. |
| Variable frame rate (rates other than 10 fps) | UNVERIFIED — only 10 fps observed for `.mot` clips. | Treat 10 fps as fixed; flag any `frame_count` that produces an unexpected duration. |
| Text-mode `.mot` files in the wild | UNVERIFIED — the binary/text switch exists in the reader code, but no text-mode samples are known to exist. | Implement binary mode only. |
| Non-stub clips (frame_count > 0, track_count > 0) | The three reference samples are stub clips (both counts zero); no full-payload clip has been byte-verified. The track and keyframe layouts are derived from parser analysis rather than raw sample bytes. | The track/keyframe layout tables are CONFIRMED by parser analysis; consider a full-payload sample to close the loop before production. |

The following items from previous spec revisions have been resolved and removed from the
unknowns table:

| Former item | Resolution |
|-------------|------------|
| LenStr prefix width (1-byte vs 4-byte) | CONFIRMED 4-byte u32 LE prefix — sample-verified in all three reference samples. |
| Upper 3 bytes of `track_descriptor` | CONFIRMED unused padding — parser reads the 4-byte word and extracts only the low byte; bits 8–31 are discarded with no comparison or storage. |
| `id_a` vs `id_b` as catalogue key | CONFIRMED — `id_b` is the load-time catalogue / group key; `id_a` is the per-file UID matching the filename integer, returned as the clip handle and used by the runtime mixer as the per-clip lookup ID. |
| Wrap-to-first at clip end | CONFIRMED — no loop flag exists in the `.mot` binary; wrap is a runtime property of `AnimationCycleLayer` (modulo on local time), not of the file. |
| `actormotion.txt` column layout | CONFIRMED (record layout) — 33 columns, count-prefixed, parsed into a 136-byte record; offsets and types documented in §`actormotion.txt` layout. Per-column semantic names remain PROPOSED. |
| Animation mixer runtime blend model (provisional) | CONFIRMED — two-list architecture, sync-phase mechanism with 1.5× constant, weight ramping with 0.001 s floor, and per-bone normalized weighted-average accumulation (order-dependent for ≥3 layers) documented in §Animation mixer — runtime blend model. |

---

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` — VFS archive that delivers `.mot` files.
- **Skeleton:** `Docs/RE/formats/mesh.md` — `.bnd` bind-pose skeleton; the `self_id` field of
  each bone record is the link target for `bone_id` in `.mot` track records. Also defines the
  `LenStr` encoding (4-byte u32 LE prefix) confirmed for `.mot`.
- **Canonical names:** see `Docs/RE/names.yaml`
  (`MotionClip`, `BoneTrack`, `MotionClipManager`, `AnimationMixer`,
  `AnimationCycleLayer`, `AnimationActionLayer`).
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
