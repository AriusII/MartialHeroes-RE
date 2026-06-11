# Format: .mot  (skeletal animation clip)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true

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
| `data/char/actormotion.txt` | Maps actor / visual identifiers to motion catalogue IDs. Column layout is UNVERIFIED (no parser characterized in the available notes). | UNVERIFIED (existence confirmed; column layout unknown) |
| `data/motion.cache` | Pre-load ID cache. Wire layout: `[u32 count][count × u32 motion_catalogue_id]`. The engine opens this file through a direct OS file call (not the VFS) and uses the IDs to prime the in-memory clip map, triggering eager full-load for the listed IDs. | CONFIRMED (wire layout); magic / versioning UNVERIFIED |

---

## Header layout

The header is read in two passes (see §Two-stage loading below). Both passes read the same
four fields in the same order. All fields are binary little-endian unless the text-mode flag is
active (see §Binary / Text duality).

| Rel. offset | Size | Type    | Field         | Notes                                                                                                                              | Confidence |
|------------:|-----:|---------|---------------|------------------------------------------------------------------------------------------------------------------------------------|------------|
| 0           | 4    | u32 LE  | `id_a`        | Per-file unique numeric identifier. Matches the decimal integer component of the filename (e.g. `g170354502.mot` → `id_a = 170354502`). Returned as the clip handle by the registration function; not used as the catalogue lookup key (see §Clip catalogue). | CONFIRMED (sample-verified) |
| 4           | 4    | u32 LE  | `id_b`        | Group / catalogue key. Shared across all clips in the same actor motion set. Used as the key when inserting into the runtime clip map (see §Clip catalogue). | CONFIRMED (sample-verified) |
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
| **Stage 1 — header only** | Fields `id_a`, `id_b`, `name_body` (skipped), `frame_count`. Derives `duration_seconds = frame_count × 0.1` and stores it. File is kept open (or re-opened in Stage 2). | Register the clip in the catalogue map; mark as partially loaded. |
| **Stage 2 — full data** | Re-reads the four header fields, then continues with `track_count` and all track / keyframe data. | Populate the bone-track array; mark clip as fully loaded. |

A parser implementing `Assets.Parsers` should perform the equivalent of both stages in a single
sequential read (read the header, then immediately read the track array).

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

The engine maintains a runtime map from catalogue key (u32) to loaded clip.

**CONFIRMED (sample-verified):**

- **`id_b` is the catalogue key.** It is passed as the lookup key when inserting a clip into the
  runtime actor map. It is shared across all clips belonging to the same actor motion set (all
  three reference samples carry `id_b = 7741`, confirming grouping semantics).
- **`id_a` is the per-file unique identifier.** It matches the decimal integer in the filename
  (e.g. filename `g170354502.mot` → `id_a = 170354502`). It is returned to callers as the clip
  handle after registration. It is NOT used as a catalogue lookup key.

Neither field encodes a format version. Both fields carry semantic identity values assigned at
content-creation time.

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

No enumerated fields or bitflag fields exist in the on-disk `.mot` format. The upper three bytes
of `track_descriptor` (bits 8–31) are reserved padding and carry no enumerated meaning.

---

## Known unknowns

The following aspects are unresolved and must not be assumed by the implementing engineer:

| Item | Status | Impact |
|------|--------|--------|
| `actormotion.txt` column layout | UNVERIFIED — the file exists and is read by the engine, but the column/field structure has not been characterized. | Defer `actormotion.txt` parsing; it is not required to decode `.mot` binary data. |
| Interpolation parameter normalization intent | UNVERIFIED — `alpha` is in raw seconds `[0, 0.1]`, not `[0, 1]`. Matches observed behaviour; whether intentional design or latent defect is unknown. | Implement as observed (raw seconds); document the deviation from standard practice. |
| `motion.cache` magic and versioning | UNVERIFIED — no header magic or version field confirmed; only the `[u32 count][u32[] ids]` layout. | Parse defensively; treat as unversioned. |
| `AnimationPointer` role | UNVERIFIED — appears in the runtime type hierarchy as a possible catalogue reference wrapper; no layout characterized. | No on-disk impact; deferred for runtime layer analysis. |
| `AnimationActionLayer` on-disk / runtime layout | UNVERIFIED — no constructor or field layout characterized in available notes. | Action-layer playback is a runtime concern; on-disk `.mot` binary is unaffected. |
| Variable frame rate (rates other than 10 fps) | UNVERIFIED — only 10 fps observed. | Treat 10 fps as fixed; flag any `frame_count` that produces an unexpected duration. |
| Text-mode `.mot` files in the wild | UNVERIFIED — the binary/text switch exists in the reader code, but no text-mode samples are known to exist. | Implement binary mode only. |
| Non-stub clips (frame_count > 0, track_count > 0) | The three reference samples are stub clips (both counts zero); no full-payload clip has been byte-verified. The track and keyframe layouts are derived from parser analysis rather than raw sample bytes. | The track/keyframe layout tables are CONFIRMED by parser analysis; consider a full-payload sample to close the loop before production. |

The following items from the previous spec revision have been resolved and removed from this table:

| Former item | Resolution |
|-------------|------------|
| LenStr prefix width (1-byte vs 4-byte) | CONFIRMED 4-byte u32 LE prefix — sample-verified in all three reference samples. |
| Upper 3 bytes of `track_descriptor` | CONFIRMED unused padding — parser reads the 4-byte word and extracts only the low byte; bits 8–31 are discarded with no comparison or storage. |
| `id_a` vs `id_b` as catalogue key | CONFIRMED — `id_b` is the catalogue key; `id_a` is the per-file UID matching the filename integer and is returned as the clip handle. |
| Wrap-to-first at clip end | CONFIRMED — no loop flag exists in the `.mot` binary; wrap is a runtime property of `AnimationCycleLayer` (modulo on local time), not of the file. |

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
