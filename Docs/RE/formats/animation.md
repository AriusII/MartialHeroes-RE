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
| `.mot` corpus is real, not stubs (3877/3891 are real clips) | Resolved | SAMPLE-VERIFIED (full census of 3,891 files) |
| `.mot` BANI-magic variant (11 files; different header) | Header fully recovered | SAMPLE-VERIFIED (all header fields cross-checked on 3 of 11 files; body layout PROPOSED) |
| `.mot` track / keyframe layout | Resolved | CONFIRMED (parser-derived; corpus census confirms full-payload clips exist) |
| LenStr 4-byte u32 LE prefix | Resolved | CONFIRMED (sample-verified) |
| `track_descriptor` upper-3-byte padding | Resolved | CONFIRMED (sample-verified) |
| `id_a` vs `id_b` roles (load key vs runtime clip handle) | Resolved | CONFIRMED |
| Wrap / loop is runtime-only (no on-disk flag) | Resolved | CONFIRMED |
| `actormotion.txt` record layout (offsets + read order) | Resolved | CONFIRMED (parser-derived) |
| `actormotion.txt` per-column semantic names (cols 3–14) | Proposed | PROPOSED (offsets confirmed; field meanings inferred) |
| `actormotion.txt` col15 = idle `.mot` `id_a` | Resolved | CONFIRMED (sample-verified — 89.2% hit rate, §`actormotion.txt` layout) |
| `actormotion.txt` declared count (1084) vs parsed rows (1080) | Documented | SAMPLE-VERIFIED discrepancy (§`actormotion.txt` layout) |
| Animation mixer two-list architecture | Resolved | CONFIRMED |
| Mixer sync-phase mechanism + 1.5× rate constant | Resolved | CONFIRMED |
| Per-bone weighted-average accumulation (lerp/slerp) | Resolved | CONFIRMED |
| Default layer playback speed constant (≈1.575) | Documented | UNVERIFIED (constant confirmed; full semantics open) |
| Skinning / deform pipeline that consumes `.mot` (LBS, inverse-bind, pose composition) | Cross-referenced | see `specs/skinning.md` |
| BANI standard-loader rejection (parse-error on all 11 files) | Resolved | SAMPLE-VERIFIED + CODE-CONFIRMED — BANI files are non-loadable by the shipping client |

Open items are tracked in §Known unknowns. This spec documents the on-disk `.mot` binary
(the only artifact an `Assets.Parsers` engineer must implement) plus the runtime animation
model as informative background for `Client.Application`. The runtime model describes
behaviour and constants only; it does not expose any in-memory object layout. The **math that
deforms a skinned mesh using a sampled `.mot` pose** (linear-blend skinning, inverse-bind bake,
pose composition, quaternion/handedness conventions) is documented in `specs/skinning.md`.

---

## Corpus census (sample-verified)

> Earlier revisions of this spec were written against only **three stub clips** (both counts zero)
> and gave the impression that `.mot` files are all stubs. A full census of all **3,891** `.mot` files
> in the validated VFS corrects that: the corpus is overwhelmingly **real, full-payload clips**.

| Status | Count | Share |
|--------|------:|------:|
| Total `.mot` files | 3,891 | 100% |
| Real clips (`frame_count > 0` AND `track_count > 0`) | 3,877 | 99.7% |
| Stub clips (`frame_count == 0` or `track_count == 0`) | 3 | 0.08% |
| BANI-magic variant (separate header — see §BANI variant) | 11 | 0.28% |

**Distribution highlights (real clips):**

- **Frame count** is dominated by short loops: ~59% of real clips have 11–50 frames (1.1–5.0 s at
  10 fps), consistent with combat/idle cycles. A long tail reaches into the hundreds and (rarely)
  thousands of frames.
- **Track count** confirms these are full-body skeletal clips: ~67% of real clips have **51+ bone
  tracks**. The three known stubs are outliers, not representative.
- A few very-long, low-track clips (e.g. ~7,650 frames with only 2 tracks) are cutscene / special
  motions (root + one bone), not character body clips.

**Implication:** character skinning is **not** blocked by missing animations — the assets are present
and plentiful. The deform/skinning math that consumes them is specified in `specs/skinning.md`.

---

## Identification

- **Extension:** `.mot`
- **Found in:** `.pak` / VFS archive (see `formats/pak.md`); logical path prefix `data/char/mot/`
- **Magic / signature:** **two variants exist in the corpus.**
  - **Standard variant (3,880 of 3,891 files):** no magic — the file begins immediately with the
    header fields below (`id_a` at offset 0). This is the primary format documented in this spec.
  - **BANI variant (11 files):** begins with the 4-byte ASCII magic `"BANI"` (`42 41 4E 49`) and uses
    a different header layout before the name string. See §BANI variant. **A parser MUST sniff the
    first 4 bytes** and route BANI files separately — the standard loader in the shipping client does
    NOT detect the magic, causing a parse failure on all 11 files (see §BANI variant — loader rejection).
  - Both a binary mode and a text mode appear to be supported at runtime for the standard variant
    (see §Binary / Text duality under Header layout); the binary mode is the primary format here.
- **Endianness:** little-endian throughout (all multi-byte integers and floats are LE).
- **Confidence:** CONFIRMED; sample_verified: true (standard header sample-verified; corpus census of
  3,891 files; BANI header fully recovered across 3 cross-checked samples)

---

## Discovery and catalogue files

Three supporting text files (plain ASCII, one entry per line) control how the engine finds and
pre-loads clips:

| File | Role | Confidence |
|------|------|------------|
| `data/char/motlist.txt` | Manifest / registry index — each line is a bare on-disk filename; the engine prepends `data/char/mot/` to form the VFS path and registers the clip in a numeric-id keyed motion registry (see *Motion id registry* below). | CONFIRMED |
| `data/char/actormotion.txt` | Maps actor / visual identifiers to motion catalogue IDs. Tabular, count-prefixed, 33 columns per record; layout characterized in §`actormotion.txt` layout. | CONFIRMED (record layout, parser-derived); col15 idle mapping sample-verified; per-column semantics for cols 3–14 PROPOSED |
| `data/motion.cache` | Pre-load ID cache. Wire layout: `[u32 count][count × u32 motion_catalogue_id]`. The engine opens this file through a direct OS file call (not the VFS) and uses the IDs to prime the in-memory clip map, triggering eager full-load for the listed IDs. | CONFIRMED (wire layout); size-math cross-checked (see §motion.cache / effect.cache size-math); magic / versioning UNVERIFIED |

The 9-digit motion IDs stored in `actormotion.txt` (see §`actormotion.txt` layout) are the same
values as the `.mot` per-file `id_a` field, which matches the numeric component of the `.mot`
filename. `actormotion.txt` is therefore the actor-facing table that names, per actor class and
motion slot, which `.mot` clip to play.

### Motion id registry (CODE-CONFIRMED)

`.mot` clips are resolved by **numeric id through a registry**, not by formatting a `g%d.mot`
filename at play time. At boot the engine reads `data/char/motlist.txt` line by line; for each line
it prepends the directory prefix `data/char/mot/` to the listed on-disk filename and registers the
resulting clip under a **numeric motion id** taken from the leading digits of the filename (the same
name-prefix-as-id convention the texture list files use; see `formats/texture.md`). After the list is
registered, the pre-load id cache (`data/motion.cache`) drives eager full-load of the listed ids.

At runtime a motion id (a 9-digit value equal to the clip's `id_a`, sourced from `actormotion.txt`)
is looked up in this registry to obtain the clip; the on-disk file it maps to is whatever
`motlist.txt` named for that id. The filenames happen to follow `g{id}.mot`, but that is the naming
convention on disk, not a runtime template — the resolution is id -> registry entry.

<!-- source: _dirty/campaign5/character-appearance-assembly.md -->

---

## Header layout

The header is read in two passes (see §Two-stage loading below). Both passes read the same
four fields in the same order. All fields are binary little-endian unless the text-mode flag is
active (see §Binary / Text duality). **This layout applies to the standard variant only**; the
BANI variant has a different header (see §BANI variant).

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

**CONFIRMED sample-verified:** in all reference samples the four bytes at offset 8 decode as
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
| 0                          | 4    | u32 LE | `track_count` | Number of `BoneTrack` records that follow. One track per animated bone. A value of zero is valid (stub clip). Real full-body clips carry tens of tracks (~67% have 51+); a clip may have fewer tracks than the skeleton has bones — unanimated bones are simply absent and stay at their bind pose. | CONFIRMED |

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

**Pose semantics (cross-reference):** a sampled keyframe is a **local replacement pose** for the
named bone, not an additive delta — it overwrites the bone's animated local translation/rotation.
Animation drives **rotation on child bones and translation on the root only**; a child bone's local
translation is held at its bind-pose value each frame. The full pose-composition and deform math is
specified in `specs/skinning.md` (§6 keyframe sampling, §5 deform).

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
  this is intentional design or a latent defect in the original client is UNVERIFIED. An importer
  may reproduce the raw-seconds alpha for bit-faithful legacy motion, or renormalize `alpha /= 0.1`
  for smooth playback — this choice is discussed for the Godot path in `specs/skinning.md` §8(c).

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
numeric — no bone name string is stored in the `.mot` file. The bone is resolved by **ID offset**
(`bone_array[bone_id − base_id]`), not by array position; see `specs/skinning.md` §3 and
`formats/mesh.md` §Bone addressing.

At runtime, if a `bone_id` in the `.mot` track array has no matching `self_id` in the loaded
`.bnd` skeleton, that track is silently skipped for that frame. Parsers and importers should
treat an unmatched `bone_id` as a non-fatal warning. (This is normal: a clip commonly animates
fewer bones than the skeleton has — e.g. a 80-track clip on an 84-bone skeleton.)

---

## BANI variant

> **Format deviation — header fully recovered; body layout partially characterised.**
> Of the 3,891 `.mot` files, **11 begin with a 4-byte ASCII magic `"BANI"`** (`42 41 4E 49`)
> instead of the bare standard header. The standard loader in the shipping client has **no
> magic-check branch** and cannot load these files — all 11 produce parse errors and are
> effectively dead data in the VFS. See §BANI variant — loader rejection for details.

The 11 BANI files all reside in `data/char/mot/`. They fall in two numeric ID bands:

- `g170350513.mot` through `g170350515.mot` (3 files, `version = 1`)
- `g170576814.mot` through `g170948714.mot` (8 files, `version` values 1 and 3)

They account for 0.28% of the `.mot` corpus. They are plausibly a specific character class's
animations exported from a newer or different pipeline that was never integrated into the shipping
client loader.

### BANI header layout (SAMPLE-VERIFIED — 3 of 11 files cross-checked)

The header is variable-length due to the embedded LenStr name field. All fields listed below are
**SAMPLE-VERIFIED** except where noted.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u8[4] | `magic` | ASCII `"BANI"` (`42 41 4E 49`). Identifies the variant. | SAMPLE-VERIFIED |
| 0x04 | 4 | u32 LE | `version` | Sub-format variant selector. Observed values: **1** (files g170350513–515 and part of the 170576xxx group) and **3** (one or more files in the 170577xxx / 170948xxx group). Whether version affects post-header payload layout is unknown. | SAMPLE-VERIFIED |
| 0x08 | 4 | u32 LE | `anim_id` | Numeric animation identifier. Matches the decimal numeric suffix of the filename (same role as standard `id_a` but at offset 8 rather than offset 0). | SAMPLE-VERIFIED |
| 0x0C | 4 | u32 LE | `unknown_field` | Constant value **7830** (0x1E96) across all 11 observed files. Candidate interpretations: skeleton/rig group ID, animation set category, or build-tool export revision. See §unknown_field note. | SAMPLE-VERIFIED (value); PROPOSED (interpretation) |
| 0x10 | 4 | u32 LE | `name_len` | Byte length of the embedded name string (LenStr 4-byte u32 LE prefix, same encoding as the standard variant). Observed value 11 in the cross-checked samples. | SAMPLE-VERIFIED |
| 0x14 | N | u8[N] | `name` | ASCII name string of `name_len` bytes; no NUL terminator on disk. Encodes the animation identifier string, e.g. `"g170350513"` (11 bytes). Same relative-path convention as the standard variant. | SAMPLE-VERIFIED |
| 0x14+N | 4 | u32 LE | `frame_count` | Total number of keyframes. Observed values: 28, 29, 38 across the three cross-checked samples. | SAMPLE-VERIFIED |
| 0x18+N | 4 | u32 LE | `track_count` | Number of bone tracks. **Constant 52 across all 11 files** (confirmed from the census scan). Consistent with a shared 52-bone skeleton rig. | SAMPLE-VERIFIED |
| 0x1C+N | variable | — | payload | Per-track and per-frame data. Layout not yet decoded; see §BANI payload note. | NOT YET DECODED |

After `track_count` the payload begins. Its structure is not confirmed. Identity-rotation float
values appear in positions consistent with the standard keyframe encoding (translation XYZ +
quaternion XYZW, 28 bytes per keyframe), but the exact arrangement of track descriptors and
keyframe blocks has not been characterised.

### Confirmed all-samples constants

Two field values are consistent across every one of the 11 BANI files (confirmed from the full
file census):

| Field | Constant value | Interpretation |
|-------|---------------|----------------|
| `unknown_field` | 7830 (0x1E96) | Candidate: skeleton group / rig set ID; links these 11 files to a common 52-bone hierarchy. PROPOSED — requires cross-reference to the skeleton catalogue to confirm. |
| `track_count` | 52 | Matches the bone count of a specific skeleton rig. All 11 files animate the same skeleton. |

### Cross-checked sample summary

| File | `version` | `anim_id` | `unknown_field` | `name` | `frame_count` | `track_count` |
|------|:---------:|----------:|:--------------:|--------|:------------:|:-------------:|
| `g170350513.mot` | 1 | 170350513 | 7830 | `g170350513` | 28 | 52 |
| `g170350514.mot` | 1 | 170350514 | 7830 | `g170350514` | 29 | 52 |
| `g170350515.mot` | 1 | 170350515 | 7830 | `g170350515` | 38 | 52 |

### BANI variant — loader rejection (SAMPLE-VERIFIED + CODE-CONFIRMED)

> **Conclusion: the shipping client cannot load BANI files. All 11 are dead/unused data.**

The standard loader (`CoreMot_LoadFromFile_Header`) reads the first four bytes as a u32 LE
`id_a` field with no magic check. When fed a BANI file:

1. The four magic bytes `42 41 4E 49` are read as a bogus `id_a` value.
2. The u32 at offset 4 (the `version` field, value 1 or 3) is read as `id_b`.
3. The u32 at offset 8 (the `anim_id` field, e.g. 170350513) is read as `name_len`.
4. The loader attempts to read 170 million bytes as a name string — far exceeding any real file
   size — causing an immediate parse failure.

Parse errors on all 11 BANI files were confirmed by the animscan census tool. No separate BANI
loader branch was found in the binary after an exhaustive search. These files are not referenced
at runtime by any code path in the shipping client.

**Implication for Assets.Parsers:**

1. **Sniff the first 4 bytes.** If they equal `42 41 4E 49` (ASCII `"BANI"`), skip or log and
   continue — do not attempt standard header parsing.
2. BANI files may be safely excluded from the production animation catalogue. If future
   completeness requires them, a dedicated BANI parser may be built from the header table above;
   the payload layout must be decoded separately before that parser is considered production-ready.
3. Mark any BANI file in the catalogue as `NonLoadable` in the parser output — do not surface them
   to `Client.Application` as valid clips.

### BANI payload note

After the `track_count` field, the payload structure is not yet decoded. Based on the confirmed
`frame_count` and `track_count` values, the expected content is:
- Per-track bone index / channel type descriptors
- Per-frame quaternion/translation samples for each track

Whether per-track data uses f32 or f16 quantization, and the exact packing order (interleaved
vs. planar), is unknown. Decoding this payload is a deferred task; it is not required for the
`Assets.Parsers` implementation because BANI files are non-loadable by the shipping client and
should be skipped.

---

## Clip catalogue

The engine maintains a runtime map from a u32 key to the loaded clip object. Two distinct numeric
identities are involved, and they are used at different stages — this distinction was previously a
known unknown and is now resolved.

**CONFIRMED:**

- **`id_b` is the load-time catalogue key.** It is passed as the lookup key when registering a clip
  into the runtime clip map during loading. It is shared across all clips belonging to the same
  actor motion set (all three earlier reference samples carried `id_b = 7741`, confirming grouping
  semantics). This is the key the loader uses to find clips by group / set.
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

> **In-memory record size and lookup key (CODE-CONFIRMED).** Each parsed row is stored as a
> fixed **136-byte (0x88)** in-memory record. The record is filed under a lookup key derived from
> the first two columns: `key = col1 + categoryBase[col0]`, where `col0` is a small category /
> actor-group selector and `categoryBase[]` is a per-category base-offset table held on the catalogue
> object (the **same** base-offset table the skin catalogue uses — see `specs/skinning.md`). This
> turns a locally numbered row id into a globally unique motion-set key. **Column 2 (`id_b` /
> SkinClassId)** is the per-actor skeleton/skin selector. The 18 motion-id columns are stored as
> **two runs of 9** (a primary run and a secondary run), and two derived per-frame-rate fields are
> computed from the cycle-duration and frame-count columns (see *Derived fields* below). The exact
> contents of `categoryBase[]` are **UNVERIFIED** (a live array dump is needed); the record size,
> key shape, column-2 role, and 9+9 motion-id split are CODE-CONFIRMED.
>
> <!-- source: _dirty/campaign5/character-appearance-assembly.md -->
> <!-- pending live-debugger value-edge: actormotion/skin catalogue categoryBase[] array contents -->
The byte offsets below are the offsets within the engine's in-memory record (a fixed 136-byte
record) into which each column is parsed; they are stable and parser-derived. Per-column **semantic
names for columns 3–14 are PROPOSED** — the read order, types, and offsets are confirmed, but the
meanings are inferred from sample values and usage and should be cross-checked before being relied
upon. **Column 15 (the idle motion) is now sample-verified** (see §col15 below).

### File structure

| Element | Description |
|---------|-------------|
| Line 1 | Decimal record count. The file declares **1084** records, but the production parser parses **1080** rows (see §Declared vs parsed count). |
| Lines 2…N | One record per line, 33 tab-separated values, parsed in the column order below. |

**Column count is uniform: every data line has exactly 33 columns** — SAMPLE-VERIFIED across the
whole file (no per-line variation).

### Record layout (parser-derived offsets within the 136-byte record)

| Column | Type | Record offset | Proposed field | Notes |
|-------:|------|--------------:|----------------|-------|
| 0  | u32 | (key input) | `group_type` | Actor class / visual group selector. Used together with column 1 to compute the record key (see §Record key). |
| 1  | u32 | (key input) | `row_id` | Row index within the motion set; combined with `group_type` to form the global key. |
| 2  | u32 | +0x04 | `skin_class_id` | **SkinClassId.** Selects the character's skeleton/skin: maps to `data/char/bind/g{id}.bnd` (and the `.skn` whose `id_b` equals this value). A value of **0** means no skeleton (null pointer; login/camera/special actors). SAMPLE-VERIFIED: 95.9% of rows resolve to an existing `.bnd`. |
| 3  | f32 | +0x08 | `cycle_duration_a` | Cycle duration in seconds, set A (e.g. 7.402). PROPOSED meaning. |
| 4  | u32 | +0x28 | `frame_count_a` | Frame count for set A. Clamped to a minimum of 1 (a parsed 0 is forced to 1). PROPOSED meaning. |
| 5  | f32 | +0x0C | `cycle_duration_b` | Cycle duration in seconds, set B (e.g. 16.282). PROPOSED meaning. |
| 6  | u32 | +0x2C | `frame_count_b` | Frame count for set B. Clamped to a minimum of 1. PROPOSED meaning. |
| 7  | u32 | +0x10 | `flags` | Bitfield (purpose UNVERIFIED; sample value 0). PROPOSED meaning. |
| 8  | f32 | +0x14 | `phase_a` | Phase / timing parameter A (e.g. 4.0). PROPOSED meaning. |
| 9  | f32 | +0x18 | `phase_b` | Phase / timing parameter B (e.g. 5.0). PROPOSED meaning. |
| 10 | f32 | +0x1C | `phase_c` | Phase / timing parameter C (e.g. 3.0). PROPOSED meaning. |
| 11 | f32 | +0x20 | `weight_a` | Blend weight A (e.g. 1.0). PROPOSED meaning. |
| 12 | f32 | +0x24 | `weight_b` | Blend weight B (e.g. 8.0). PROPOSED meaning. |
| 13 | f32 | +0x38 | `speed_override_a` | Playback speed override A (e.g. 4.0). PROPOSED meaning. |
| 14 | f32 | +0x3C | `speed_override_b` | Playback speed override B (e.g. 1.0). PROPOSED meaning. |
| 15 | u32 | +0x40 | `idle_motion_id` (`motion_ids_a[0]`) | **Idle / stand motion** — the first of 9 primary motion IDs. A 9-digit clip ID equal to a `.mot` `id_a`; maps to `data/char/mot/g{id}.mot`. Zero = empty slot. **SAMPLE-VERIFIED** (see §col15). |
| 16–23 | u32 ×8 | +0x44 … +0x60 | `motion_ids_a[1..8]` | Remaining primary motion-ID array. Same encoding; zero = empty slot. |
| 24–32 | u32 ×9 | +0x64 … +0x84 | `motion_ids_b[9]` | Secondary motion-ID array, same encoding as the primary array. |

### col2 → `.bnd` coverage (sample-verified)

Mapping col2 (`skin_class_id`) → `data/char/bind/g{id}.bnd` resolves for **1,036 / 1,080 = 95.9%** of
rows. The 44 misses break down as: ~28 rows with `skin_class_id = 0` (null skeleton — login/camera/
special actors that take a different code path; treat 0 as a null pointer, no skeleton), and the
remainder referencing `.bnd` ids absent from the preserved VFS (expected gaps). **This confirms col2
is the SkinClassId**, the actor-to-skeleton key.

### col15 → idle `.mot` coverage (sample-verified)

Mapping col15 (`idle_motion_id`) → `data/char/mot/g{id}.mot` yields an **89.2% hit rate** (963 of 1,080
rows; 56 rows have a zero id = intentionally empty slot; 61 reference clips absent from the preserved
VFS). A random mapping would hit near 0%, so this **empirically confirms col15 is an idle `.mot`
`id_a`** — previously PROPOSED, now CONFIRMED (sample-verified). The IDs in cols 15+ are confirmed
`.mot` `id_a` references.

### Declared vs parsed count (discrepancy noted)

The file's line-1 count declares **1084** records, but the production parser yields **1080** parsed
rows — a difference of **4 rows**. The 4 missing rows are most likely blank/structural separator lines
silently skipped by the parser's `actorClassId` parse guard. This is a benign declared-vs-parsed
mismatch, not a layout error; parsers should not trust the declared count as the exact iteration bound
and should tolerate skipped/blank lines. SAMPLE-VERIFIED.

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

For reference (sample-derived, illustrative): `group_type = 0`, `row_id = 1`, `skin_class_id = 1`,
`cycle_duration_a = 7.402`, `frame_count_a = 16`, `cycle_duration_b = 16.282`, `frame_count_b = 11`,
`flags = 0`, `phase_a/b/c = 4 / 5 / 3`, `weight_a/b = 1 / 8`, `speed_override_a/b = 4 / 1`,
`motion_ids_a` = several non-zero 9-digit IDs followed by zeros, `motion_ids_b` = a few non-zero
9-digit IDs followed by zeros.

> A sibling table, `data/char/skin.txt`, uses a related but different 6-column key-and-identity
> layout and belongs to the skin / bind catalogue, not the motion catalogue. It is out of scope
> for this format and noted here only to prevent confusion.

---

## motion.cache / effect.cache size-math

`data/motion.cache` and `data/effect.cache` are read through a **direct OS file call** (not the VFS),
so they are absent from the VFS itself. They were not byte-observed in the validated install, but the
recorded file sizes cross-check the documented `[u32 count][count × u32 id]` layout exactly:

| File | Size | Layout check | Implied count |
|------|-----:|--------------|--------------:|
| `motion.cache` | 44 B | `4 + count × 4` → `count = (44 − 4) / 4` | 10 IDs |
| `effect.cache` | 72 B | `4 + count × 4` → `count = (72 − 4) / 4` | 17 IDs |

Both sizes are **exactly** consistent with the documented wire layout — an indirect confirmation
without reading the files. Magic / versioning remains UNVERIFIED.

---

## Animation mixer — runtime blend model

This section describes the runtime layering and blending model as observed. It is informative for
`Assets.Parsers` and mandatory background for `Client.Application`. It documents behaviour and
constants only — no in-memory object layout is exposed, and no parser needs to reproduce these
structures to decode a `.mot` file. The downstream **deform** of a skinned mesh using the accumulated
pose (linear-blend skinning, inverse-bind, pose composition) is specified in `specs/skinning.md`.

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
concern and has no bearing on the `.mot` file format. The composition into final bone world
transforms and the deform that follows are specified in `specs/skinning.md`.

### Activating a cycle

To start or update a looping clip: provide the clip's `id_a`, a target weight, and a blend time. If
a cycle with that `id_a` already exists in the Cycle list, its target weight and blend time are
updated in place. If it does not exist and the requested weight is non-zero, a new cycle layer is
created and appended to the Cycle list. Stopping all layers sets every layer's target weight to
`0.0`, letting the weight-ramp logic fade them out and remove them.

---

## Enumerations / flags

No enumerated fields or bitflag fields exist in the on-disk standard `.mot` format. The upper three
bytes of `track_descriptor` (bits 8–31) are reserved padding and carry no enumerated meaning. The
`flags` column in `actormotion.txt` (§`actormotion.txt` layout, column 7) is a bitfield whose bit
assignments are UNVERIFIED. The BANI variant's `version` field (offset 4) takes the discrete values
1 and 3 (§BANI variant) but its full enumeration and effect on payload layout are not characterized.

---

## Known unknowns

The following aspects are unresolved and must not be assumed by the implementing engineer:

| Item | Status | Impact |
|------|--------|--------|
| BANI variant payload layout (post `track_count`) | NOT YET DECODED — magic, version, anim_id, unknown_field, name LenStr, frame_count, and track_count are all sample-verified; the per-track and per-keyframe structure that follows is not characterised. | Sniff and skip BANI files in the standard parser; do not rely on BANI clips for production. If future decode is needed, the payload is expected to be similar to the standard keyframe encoding but this is unconfirmed. |
| BANI `unknown_field = 7830` interpretation | PROPOSED — constant across all 11 files; candidate is a skeleton group / rig set ID linking these files to the 52-bone hierarchy. Cross-reference with the skeleton catalogue required to confirm. | Carry the value through; do not branch on it. |
| BANI `version` effect on payload | UNVERIFIED — values 1 and 3 observed; whether they imply different post-header layouts is unknown. | Treat as informational metadata until payload is decoded. |
| `actormotion.txt` per-column semantics (cols 3–14) | UNVERIFIED — record layout, types, and offsets are confirmed; the proposed names (`phase_*`, `weight_*`, `speed_override_*`, `flags` bit meanings) are inferred from sample values. Cols 2 and 15 are now sample-verified and removed from this caveat. | Parse by offset and type; treat cols 3–14 field names as provisional until cross-checked against the actor controller. |
| `actormotion.txt` 15-unit rate base | UNVERIFIED — derived rate fields use a `15.0` base; relationship to the `.mot` 10 fps clip rate is unknown. | Compute the derived rates as specified; do not assume the bases are interchangeable. |
| Default layer speed constant (≈1.575) | UNVERIFIED — a confirmed float literal set at layer construction; its interaction with `actormotion.txt` `speed_override` columns is unknown. | Document the constant; do not hard-wire playback speed to it until its role is confirmed. |
| Sync-phase 1.5× rate rationale | UNVERIFIED — the 1.5 multiplier is a confirmed constant; the "15 fps / 10 fps" interpretation is a hypothesis. | Implement the constant as-is; do not depend on the hypothesized rationale. |
| Character speed scale factor source | UNVERIFIED — the per-visual `scaled_dt` factor is sourced from character speed data not yet characterized. | Treat as a runtime input (default 1.0); not needed to decode `.mot` binaries. |
| `flags` bitfield meanings (`actormotion.txt` column 7) | UNVERIFIED — the column is a u32 bitfield; individual bits are uncharacterized (sample value 0). | Carry the value through; do not branch on undocumented bits. |
| Interpolation parameter normalization intent | UNVERIFIED — `alpha` is in raw seconds `[0, 0.1]`, not `[0, 1]`. Matches observed behaviour; whether intentional design or latent defect is unknown. | Implement as observed (raw seconds); document the deviation — see `specs/skinning.md` §8(c) for the Godot faithful-vs-smoothed choice. |
| `motion.cache` magic and versioning | UNVERIFIED — no header magic or version field confirmed; only the `[u32 count][u32[] ids]` layout (cross-checked by size math, §motion.cache / effect.cache size-math). | Parse defensively; treat as unversioned. |
| Variable frame rate (rates other than 10 fps) | UNVERIFIED — only 10 fps observed for `.mot` clips. | Treat 10 fps as fixed; flag any `frame_count` that produces an unexpected duration. |
| Text-mode `.mot` files in the wild | UNVERIFIED — the binary/text switch exists in the reader code, but no text-mode samples are known to exist. | Implement binary mode only. |

The following items from previous spec revisions have been resolved and removed from the
unknowns table:

| Former item | Resolution |
|-------------|------------|
| "All `.mot` samples are stubs" | RESOLVED — a full census of 3,891 files shows 3,877 (99.7%) are real full-payload clips; only 3 are stubs and 11 are the BANI variant (§Corpus census). The track/keyframe layout is exercised by the corpus, not just by parser inference. |
| LenStr prefix width (1-byte vs 4-byte) | CONFIRMED 4-byte u32 LE prefix — sample-verified. |
| Upper 3 bytes of `track_descriptor` | CONFIRMED unused padding — parser reads the 4-byte word and extracts only the low byte; bits 8–31 are discarded with no comparison or storage. |
| `id_a` vs `id_b` as catalogue key | CONFIRMED — `id_b` is the load-time catalogue / group key; `id_a` is the per-file UID matching the filename integer, returned as the clip handle and used by the runtime mixer as the per-clip lookup ID. |
| Wrap-to-first at clip end | CONFIRMED — no loop flag exists in the `.mot` binary; wrap is a runtime property of `AnimationCycleLayer` (modulo on local time), not of the file. |
| `actormotion.txt` column layout | CONFIRMED (record layout) — 33 columns, count-prefixed, parsed into a 136-byte record; offsets and types documented in §`actormotion.txt` layout. Per-column semantic names for cols 3–14 remain PROPOSED; col2 (SkinClassId) and col15 (idle motion) are now sample-verified. |
| `actormotion.txt` col15 = idle motion | CONFIRMED (sample-verified) — col15 → `data/char/mot/g{id}.mot` hits 89.2% of rows (§col15). |
| `.mot` magic: "no magic, starts with id_a" | CORRECTED — true for 3,880 standard-variant files, but 11 files use the `"BANI"` magic with a different header (§BANI variant). Parsers must sniff the first 4 bytes. |
| Animation mixer runtime blend model (provisional) | CONFIRMED — two-list architecture, sync-phase mechanism with 1.5× constant, weight ramping with 0.001 s floor, and per-bone normalized weighted-average accumulation (order-dependent for ≥3 layers) documented in §Animation mixer — runtime blend model. |
| BANI files loadable by shipping client | CONFIRMED NEGATIVE (SAMPLE-VERIFIED + CODE-CONFIRMED) — the standard loader has no magic-check branch; all 11 BANI files produce parse errors and are dead/unused data. Parsers must sniff and skip them. |

---

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` — VFS archive that delivers `.mot` files.
- **Skeleton / skinned mesh:** `Docs/RE/formats/mesh.md` — `.bnd` bind-pose skeleton and `.skn` skin;
  the `self_id` field of each bone record is the link target for `bone_id` in `.mot` track records.
  Also defines the `LenStr` encoding (4-byte u32 LE prefix) confirmed for `.mot`.
- **Deform / skinning math:** `Docs/RE/specs/skinning.md` — how a sampled `.mot` pose is composed up
  the bone hierarchy and used to deform a skinned mesh (linear-blend skinning, inverse-bind bake,
  quaternion/handedness conventions, Godot import guidance, canonical test specimens).
- **Canonical names:** see `Docs/RE/names.yaml`
  (`MotionClip`, `BoneTrack`, `Keyframe`, `MotionClipManager`, `AnimationMixer`,
  `AnimationCycleLayer`, `AnimationActionLayer`; proposed `BaniMotClip`, `bani_magic`,
  `bani_version`).
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
