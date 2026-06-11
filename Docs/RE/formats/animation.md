# Format: .mot  (skeletal animation clip)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: hypothesis
> sample_verified: false

---

## Identification

- **Extension:** `.mot`
- **Found in:** `.pak` / VFS archive (see `formats/pak.md`); logical path prefix `data/char/mot/`
- **Magic / signature:** none identified — file begins immediately with the header fields below.
  Both a binary mode and a text mode appear to be supported at runtime (see §Binary / Text duality
  under Header layout); the binary mode is the primary format documented here.
- **Endianness:** little-endian throughout (all multi-byte integers and floats are LE).
- **Confidence:** CONFIRMED (endianness and binary structure); sample_verified: false

---

## Discovery and catalogue files

Three supporting text files (plain ASCII, one entry per line) control how the engine finds and
pre-loads clips:

| File | Role | Confidence |
|------|------|------------|
| `data/char/motlist.txt` | Manifest — each line is a bare filename; the engine prepends `data/char/mot/` at load time to form the VFS path. | CONFIRMED |
| `data/char/actormotion.txt` | Maps actor / visual identifiers to motion catalogue IDs. Column layout is UNVERIFIED (no parser characterized in the available notes). | UNVERIFIED (existence confirmed; column layout unknown) |
| `data/motion.cache` | Pre-load ID cache. Wire layout: `[u32 count][count × u32 motion_catalogue_id]`. The engine opens this file through a direct OS file call (not the VFS) and uses the IDs to prime the in-memory clip map, triggering eager full-load for the listed IDs. | CONFIRMED (wire layout); magic / versioning UNVERIFIED |

---

## Header layout

The header is read in two passes (see §Two-stage loading below). Both passes read the same
four fields in the same order. All fields are binary little-endian unless the text-mode flag is
active (see §Binary / Text duality).

| Rel. offset | Size | Type    | Field         | Notes                                                                                                      | Confidence |
|------------:|-----:|---------|---------------|------------------------------------------------------------------------------------------------------------|------------|
| 0           | 4    | u32 LE  | `id_a`        | First numeric identifier. Purpose and relationship to `id_b` are UNVERIFIED (see §Known unknowns).        | CONFIRMED  |
| 4           | 4    | u32 LE  | `id_b`        | Second numeric identifier. Used as the catalogue lookup key for this clip (see §Clip catalogue, below).    | CONFIRMED  |
| 8           | var  | LenStr  | `name`        | Clip name string. Wire format: see §LenStr encoding. Not retained in memory after the first loading pass.  | CONFIRMED (field present and consumed); wire format UNVERIFIED |
| 8+N         | 4    | u32 LE  | `frame_count` | Raw frame count. Clip duration in seconds = `frame_count × 0.1` (fixed 10 fps rate; see §Timing).        | CONFIRMED  |

`N` is the total byte width of the `name` LenStr field (4-byte length prefix + body length). Because
`name` is variable-width, subsequent fields have no fixed absolute offset.

### LenStr encoding

> The `name` field in `.mot` uses the same `LenStr` encoding that `.skn` and `.bnd` use (see
> `formats/mesh.md`, §String encoding). The wire format is a 4-byte u32 LE length prefix followed
> by exactly `length` bytes of string body with no null terminator on disk. The `.mot` LenStr
> wire format is UNVERIFIED by sample; implementors should apply the same 4-byte prefix convention
> documented in `formats/mesh.md` and flag any mismatch as a defect to investigate.

### Binary / Text duality

The header reader supports two modes, selected by a runtime flag on the file object. In text mode
the same four fields are parsed as whitespace-separated decimal ASCII integers rather than binary
words. Whether text-mode `.mot` files share the `.mot` extension or use a different name is
UNVERIFIED. This spec documents binary mode only; parsers should implement binary mode first and
treat text mode as a deferred concern.

---

## Two-stage loading

The engine loads a `.mot` file in two sequential passes. Both are relevant to parsing because they
read partially overlapping regions.

| Stage | What is read | Purpose |
|-------|-------------|---------|
| **Stage 1 — header only** | Fields `id_a`, `id_b`, `name`, `frame_count`. Derives `duration_seconds = frame_count × 0.1` and stores it. File is kept open (or re-opened in Stage 2). | Register the clip in the catalogue map; mark as partially loaded. |
| **Stage 2 — full data** | Re-reads the four header fields, then continues with `track_count` and all track / keyframe data. | Populate the bone-track array; mark clip as fully loaded. |

A parser implementing `Assets.Parsers` should perform the equivalent of both stages in a single
sequential read (read the header, then immediately read the track array).

---

## Track array layout

Immediately following the header (after `frame_count`), Stage 2 data begins with a count field,
then a sequence of variable-length track records.

### Track count

| Rel. offset (after header) | Size | Type   | Field         | Notes                              | Confidence |
|---------------------------:|-----:|--------|---------------|------------------------------------|------------|
| 0                          | 4    | u32 LE | `track_count` | Number of `BoneTrack` records that follow. One track per animated bone. | CONFIRMED |

### Per-track record

Repeated `track_count` times. Each record consists of a fixed 8-byte preamble followed by a
variable-length keyframe block.

| Rel. offset (within track) | Size          | Type    | Field              | Notes                                                                                                   | Confidence |
|---------------------------:|--------------:|---------|--------------------|---------------------------------------------------------------------------------------------------------|------------|
| 0                          | 4             | u32 LE  | `track_descriptor` | Low byte = `bone_id`; upper three bytes purpose UNVERIFIED (see §Known unknowns).                      | CONFIRMED (low byte); upper bytes UNVERIFIED |
| 4                          | 4             | u32 LE  | `key_count`        | Number of keyframes in this track.                                                                      | CONFIRMED |
| 8                          | `key_count × 28` | bytes | `keyframes`     | Inline array of keyframe records, each 28 bytes. See §Keyframe record.                                  | CONFIRMED |

**Track record stride:** variable — `8 + key_count × 28` bytes.

### Keyframe record — 28 bytes, little-endian

Each keyframe encodes one sample in time: a 3-component translation vector and a 4-component
rotation quaternion. There is no scale channel in this format.

| Sub-offset | Size | Type   | Field            | Notes                                            | Confidence |
|-----------:|-----:|--------|------------------|--------------------------------------------------|------------|
| 0          | 4    | f32 LE | `translation_x`  | Local translation X.                             | CONFIRMED |
| 4          | 4    | f32 LE | `translation_y`  | Local translation Y.                             | CONFIRMED |
| 8          | 4    | f32 LE | `translation_z`  | Local translation Z.                             | CONFIRMED |
| 12         | 4    | f32 LE | `rotation_x`     | Quaternion X component.                          | CONFIRMED |
| 16         | 4    | f32 LE | `rotation_y`     | Quaternion Y component.                          | CONFIRMED |
| 20         | 4    | f32 LE | `rotation_z`     | Quaternion Z component.                          | CONFIRMED |
| 24         | 4    | f32 LE | `rotation_w`     | Quaternion W (scalar) component.                 | CONFIRMED |

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
  `sample_index + 1`, clamped to `key_count − 1`. Clamp-to-last behaviour at clip end is
  confirmed; wrap-to-first (loop) behaviour is UNVERIFIED.
- **Interpolation parameter** `alpha` = `t − (sample_index / 10.0)`. This is expressed in raw
  seconds in the range `[0, 0.1]` and is passed directly as the blend factor to both the
  translation and rotation interpolators. It is not re-normalized to `[0, 1]` before use. Whether
  this is intentional design or a latent defect in the original client is UNVERIFIED.

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

The engine maintains a runtime map from catalogue ID (u32) to loaded clip. Clips are indexed by
`id_b`. Whether `id_a` is also used as a key, or whether `id_a` and `id_b` have distinct
semantic roles (e.g. format version vs. content ID), is UNVERIFIED.

---

## Animation mixer — runtime blend model

This section describes the runtime layering model as observed. It is informative for `Assets.Parsers`
and mandatory background for `Client.Application`.

### Two clip lists

The mixer owns two independent lists of active clip layers:

| List name    | Behaviour | Clip type |
|--------------|-----------|-----------|
| **Action list** | One-shot: plays once and expires. | `AnimationActionLayer` |
| **Cycle list**  | Looping: replays continuously until removed. | `AnimationCycleLayer` |

Both lists are processed together each frame. The per-bone accumulation below applies uniformly
across entries from both lists.

### Per-bone weighted accumulation

Each active layer contributes a weight (`f32`) and the sampled (translation, rotation) pair for
each bone it drives. The mixer accumulates these contributions into a single result per bone using
a normalized weighted average:

1. **First contributor** for a bone (accumulated weight is zero): assign translation and rotation
   directly; set accumulated weight to this layer's weight.
2. **Subsequent contributors:**
   - `lerp_factor = new_weight / (accumulated_weight + new_weight)`
   - `accumulated_translation = lerp(accumulated_translation, src_translation, lerp_factor)`
   - `accumulated_rotation = slerp(accumulated_rotation, src_rotation, lerp_factor)`
   - `accumulated_weight += new_weight`

This is a proportional normalized blend. There is no additive mode and no layer priority
ordering — all contributors are merged symmetrically by weight.

### Activating a cycle

To start or update a looping clip: provide the catalogue ID and a weight. If a cycle with that
catalogue ID already exists in the list, its weight and blend parameter are updated in place. If
it does not exist and the weight is non-zero, a new cycle entry is created and appended to the
Cycle list.

### Fade-out

Setting a layer's weight to `0.0` initiates a fade-out. The layer is not removed immediately; it
remains in the list with weight zero and is phased out over a duration controlled by a separate
`blend_param` field (units UNVERIFIED). Parsers do not need to model fade-out; this is a runtime
concern for `Client.Application`.

---

## Enumerations / flags

No enumerated fields or bitflag fields have been identified in the on-disk `.mot` format. The
upper three bytes of `track_descriptor` may encode flags or a sub-type, but their meaning is
UNVERIFIED.

---

## Known unknowns

The following aspects are unresolved and must not be assumed by the implementing engineer:

| Item | Status | Impact |
|------|--------|--------|
| `LenStr` wire format in `.mot` (1-byte vs 4-byte prefix) | UNVERIFIED — assumed 4-byte by analogy with `.skn`/`.bnd`; confirm with a sample. | Parser will mis-align all post-name fields if the prefix width is wrong. |
| Upper three bytes of `track_descriptor` | UNVERIFIED — may be zero padding, may encode a channel type or LOD flag. | Parsers should mask to low byte for `bone_id` and store the upper bytes opaquely pending clarification. |
| `id_a` vs `id_b` semantics | UNVERIFIED — which is the external catalogue lookup key and which is an internal format or version identifier is unknown. | Implement catalogue lookup on `id_b` (the best available hypothesis); flag for correction once `actormotion.txt` is parsed. |
| `actormotion.txt` column layout | UNVERIFIED — the file exists and is read by the engine, but the column/field structure has not been characterized. | Defer `actormotion.txt` parsing; it is not required to decode `.mot` binary data. |
| Wrap-to-first at clip end | UNVERIFIED — clamp-to-last at the final keyframe is confirmed; whether a loop flag in the cycle layer causes the time to wrap to 0 is not confirmed in the `.mot` binary itself. | Implement clamp-to-last; looping is controlled by the cycle-layer runtime, not the on-disk clip. |
| Interpolation parameter normalization | UNVERIFIED intent — `alpha` is in raw seconds `[0, 0.1]`, not `[0, 1]`. Matches observed behaviour. Whether this is design or a latent defect is unknown. | Implement as observed (raw seconds); document the deviation from standard practice. |
| `motion.cache` magic and versioning | UNVERIFIED — no header magic or version field confirmed; only the `[u32 count][u32[] ids]` layout. | Parse defensively; treat as unversioned. |
| `AnimationPointer` role | UNVERIFIED — appears in the runtime type hierarchy as a possible catalogue reference wrapper; no layout characterized. | No on-disk impact; deferred for runtime layer analysis. |
| `AnimationActionLayer` on-disk / runtime layout | UNVERIFIED — no constructor or field layout characterized in available notes. | Action-layer playback is a runtime concern; on-disk `.mot` binary is unaffected. |
| Variable frame rate (rates other than 10 fps) | UNVERIFIED — only 10 fps observed. | Treat 10 fps as fixed; flag any `frame_count` that produces an unexpected duration. |
| Text-mode `.mot` files in the wild | UNVERIFIED — the binary/text switch exists in the reader code, but no text-mode samples are known to exist. | Implement binary mode only. |

---

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` — VFS archive that delivers `.mot` files.
- **Skeleton:** `Docs/RE/formats/mesh.md` — `.bnd` bind-pose skeleton; the `self_id` field of
  each bone record is the link target for `bone_id` in `.mot` track records. Also defines the
  `LenStr` encoding (4-byte u32 LE prefix) that is provisionally assumed for `.mot`.
- **Canonical names:** see `Docs/RE/names.yaml`
  (`MotionClip`, `BoneTrack`, `MotionClipManager`, `AnimationMixer`,
  `AnimationCycleLayer`, `AnimationActionLayer`).
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
