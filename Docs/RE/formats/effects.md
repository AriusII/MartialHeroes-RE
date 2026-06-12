# Format: .xeff / .eff  (visual-effects subsystem — particle emitters and primitive shapes)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (parse/load side) and by the client effect runtime
> (Client.Application / Godot presentation, instantiation side). Every offset an engineer cites
> must reference this file.
> Three sub-formats plus a runtime model are described here because they form one logical
> subsystem; each has its own section with an independent status block. The sound-trigger `.eff`
> files that share the same extension are **out of scope** — see `sound_tables.md`.

---

## Status

### Section A — `.xeff` (particle effect descriptor)

| Item | Value |
|------|-------|
| `sample_verified` | **partial** — file header (8 bytes) verified against 3 real samples; element body is parser + runtime-trace analysis only (all available samples have `element_count = 0`) |
| Endianness | Little-endian throughout |
| Magic / signature | None — format identified by file extension and directory only |
| Anti-magic | If `effect_id` at offset 0 equals `0x46464558` the client treats the file as invalid |
| Field semantics | The six per-keyframe / per-static float parameters are now resolved (emission velocity Vec3 + billboard size Vec3); emitter type enum resolved for values 0/1/2; `emitter_subtype` resolved as a resource selector; time units resolved as milliseconds. `field_unknown_a` remains unresolved. See per-field confidence below. |

### Section B — `.eff` effect-object shape (geometry primitive)

| Item | Value |
|------|-------|
| `sample_verified` | **true** — all fields verified against three real samples (`cone.eff`, `rect.eff`, `tringle.eff`); size formula exact for all three |
| Endianness | Little-endian throughout |
| Magic / signature | None — identified by directory path (`data/effect/obj/`) only |

### Section C — Effects runtime (instantiation, particle update, attach, network triggers)

| Item | Value |
|------|-------|
| `sample_verified` | **n/a — behavioral model.** Derived from runtime analysis of an already-named handler/dispatch set plus the three real `.xeff` stub samples. Not a byte format. Confidence is stated per subsection. The model is consistent across all spawn, bind, and tick paths examined; the unresolved items are listed in the open-questions block. |
| Time base | Milliseconds (engine wall clock; see C.1) |

### Section D — `effectscale.xdb` (per-effect scale-override table)

| Item | Value |
|------|-------|
| `sample_verified` | **true** — byte-verified against one 16-byte sample (two records); size formula exact |
| Endianness | Little-endian throughout |

---

## Disambiguation: the `.eff` extension is overloaded

The client uses the `.eff` extension for at least three unrelated formats. Parsers **must** dispatch by directory, not by magic:

| Path pattern | Format | Spec location |
|---|---|---|
| `data/effect/obj/*.eff` | Effect-object geometry shape (Section B below) | This file |
| `data/effect/xeff/*.xeff` | Particle effect descriptor (Section A below) | This file |
| `tool/sound/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/map*/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/effect/particle/particleEmitter.eff` | GPU particle emitter definition (separate subsystem) | Unknown — not analyzed; do not conflate with Section B (see C.7) |

---

# Section A: `.xeff` — Particle Effect Descriptor

## A.1 Identification

- **Extension:** `.xeff`
- **Found in:** `.pak` archive; logical path `data/effect/xeff/<name>.xeff`
- **Magic / signature:** None present
- **Anti-magic:** `effect_id` (bytes 0–3) must NOT equal `0x46464558` (the ASCII string `XEFF` in little-endian byte order); the loader treats that value as a corrupt-file sentinel and aborts
- **Version field:** None
- **Endianness:** Little-endian
- **Discovery:** The effect manager reads a manifest (`data/effect/xeffect.lst`, Section A.4) at boot to register all known `.xeff` paths; individual files are parsed lazily on first spawn (confirmed against the runtime registry — see C.2)

## A.2 File Header

**Confidence: VERIFIED** (parser analysis confirmed against 3 real samples)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `effect_id` | Numeric identifier for this effect. Must not equal `0x46464558`. In observed samples, the value matches the decimal portion of the filename (e.g. filename `343100212.xeff` → id = 343100212), but this correlation is not guaranteed — the id is the lookup key, not a file-name checksum. Two files may share the same `effect_id` (confirmed: `343100112.xeff` and `343100212.xeff` both store id 343100212). |
| 0x04 | 4 | u32 LE | `element_count` | Number of element records that follow immediately. Zero is valid (stub/empty effect). All three available samples have `element_count = 0`. |

Immediately after the 8-byte header, `element_count` element records follow sequentially. There is no additional directory, offset table, or terminator.

## A.3 Element Array

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED, SAMPLE-UNVERIFIED** — stride, field order, and read sequence derived from the parser's allocation formula (`element_count × 104 + 4 bytes`) and from runtime use of each parsed field. No sample with `element_count > 0` was available for byte-level cross-check.

The element array is preceded in memory by a 4-byte count prefix (the same value as `element_count` in the header). On disk the elements follow immediately after the header with no count repeat.

- **Record stride:** 104 bytes (0x68) — confidence: PARSER-CONFIRMED
- **Record count source:** `element_count` field at file offset 0x04

Each element is read as a sequence of variable-length sub-groups. The groups must be parsed in order; there is no seek-based access.

### A.3.1 Group A — Emitter identity (20 bytes, fixed)

The first five u32 values in each element. Read order is given below; note that read order (file order) differs from the in-memory dword index for two of these fields (see A.10).

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 4 | u32 LE | `emitter_type` | Geometry / rendering class. `0` = billboard sprite; `1` = animated mesh-particle object; `2` = directional billboard (reads extra rotation in Branch B). See A.8. Confidence: CONFIRMED for 0/1/2 |
| 2 | 4 | u32 LE | `resource_id` | Resource selector (formerly `emitter_subtype`). Interpretation depends on value range: `resource_id < 10000` → 0-based index into the engine's shared mesh table (selects the emitter's mesh shape); `resource_id >= 10000` → identifier of a GPU particle-emitter descriptor (the runtime hands this to the separate particle subsystem, see C.7). Confidence: HIGH |
| 3 | 4 | u32 LE | `anim_flag` | Non-zero enables the animated (multi-keyframe) path. Stored as a boolean byte in the in-memory struct (upper 3 bytes of the dword are unused). Confidence: PARSER-CONFIRMED |
| 4 | 4 | u32 LE | `tex_count` | Number of texture name entries in the texture sub-array (Group B). Also drives the keyframe array length when `anim_loop != 0`. **The low byte of this value is additionally inspected as a render flag at runtime** (see A.9, MEDIUM confidence — treat as a caution, not a guarantee). Confidence: PARSER-CONFIRMED |
| 5 | 4 | u32 LE | `field_unknown_a` | Fifth consecutive u32 in the file; stored in the in-memory element at dword[3] (byte +0x0C). The element constructor zero-initializes that slot and the parser overwrites it with this file value. **No semantic read-site was isolated at runtime.** Purpose UNRESOLVED — candidate roles are render/blend flags, a packed color/tint, or reserved padding. Do NOT assign a meaning until a sample with a non-zero value is observed in context. Confidence: PARSER-CONFIRMED (field exists), semantics UNRESOLVED. Provisional neutral name: `element_flags`. |

### A.3.2 Group B — Texture sub-array (variable length)

Immediately follows Group A. Length = `tex_count × 64` bytes.

| Count | Byte width | Type | Field | Notes / Confidence |
|---|---:|------|-------|--------------------|
| `tex_count` | 64 | char[64] ASCII | `tex_name[i]` | Null-padded ASCII base name (no path prefix, no extension). The client resolves the full path as `data/effect/texture/<tex_name>.tga` and resolves each name to a texture handle through the shared texture manager. The resolved handles are stored in the element's texture-slot vector. If `tex_count = 0` this group is empty (zero bytes). Names that contain Korean text are encoded **CP949** (do not assume UTF-8 or pure ASCII when decoding). Confidence: CONFIRMED (path-resolution behavior), SAMPLE-UNVERIFIED for actual name data |

## A.3.3 Group C — Alpha keyframes (variable length)

Follows Group B.

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 4 | u32 LE | `alpha_key_count` | Number of alpha keyframe entries. Confidence: PARSER-CONFIRMED |
| 2 … N+1 | 4 each | f32 LE | `alpha_key[i]` | Per-keyframe alpha value. **Stored inverted**: the client loads each value as `1.0 − file_value`. So a file value of `0.0` means fully opaque and `1.0` means fully transparent; the in-memory value is opacity (0.0 = transparent, 1.0 = opaque). At render time the opacity is multiplied by 255 to produce the per-vertex alpha byte. Confidence: CONFIRMED (load arithmetic and render use both confirmed) |

### A.3.4 Group D — Scale / float channels (variable length, 3 passes)

Follows Group C. The three passes drive the X, Y, and Z scale channels respectively, each packed as a 3-component float triplet in the in-memory keyframe struct. Each pass reads its own count field. If a later pass's count exceeds an earlier pass's count, the additional slots are zero-padded in memory.

| Pass | Reads | Type | Field | Notes / Confidence |
|---:|---|------|-------|--------------------|
| 1 | 1 × u32 LE | — | `scale_count_x` | Entry count for channel X (first float in each triplet). Confidence: PARSER-CONFIRMED |
| 1 | `scale_count_x` × f32 LE | f32 | `scale_x[i]` | X-scale keyframe values (written at byte +0 inside each 12-byte triplet). Confidence: PARSER-CONFIRMED, semantics MEDIUM |
| 2 | 1 × u32 LE | — | `scale_count_y` | Entry count for channel Y (second float in each triplet). Confidence: PARSER-CONFIRMED |
| 2 | `scale_count_y` × f32 LE | f32 | `scale_y[i]` | Y-scale keyframe values. Written at byte +4 inside each 12-byte triplet. Confidence: PARSER-CONFIRMED |
| 3 | 1 × u32 LE | — | `scale_count_z` | Entry count for channel Z (third float in each triplet). Confidence: PARSER-CONFIRMED |
| 3 | `scale_count_z` × f32 LE | f32 | `scale_z[i]` | Z-scale keyframe values. Written at byte +8 inside each 12-byte triplet. Confidence: PARSER-CONFIRMED |

The scale vector is resized once to the largest of the three counts; passes that run short leave their channel zero-padded in the already-resized slots. At render time this per-axis scale channel multiplies the per-vertex position (along with the billboard-size Vec3 from Group F).

### A.3.5 Group E — Animation timing (9 bytes, fixed)

Follows Group D.

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 1 | u8 | `anim_loop` | Read as a **single byte** (not a u32). Non-zero selects the multi-keyframe branch (Group F Branch A); zero selects the static-state branch (Group F Branch B). Confidence: CONFIRMED |
| 2 | 4 | u32 LE | `anim_stride` | Duration of one animation frame, in **milliseconds**. Confidence: CONFIRMED (unit confirmed against the engine ms clock; see C.1) |
| 3 | 4 | u32 LE | `anim_base_time` | Base time offset added to the per-frame accumulation, in **milliseconds**. Typically 0. Confidence: CONFIRMED |

Derived value (not in file): `total_time = tex_count × anim_stride + anim_base_time` (milliseconds). The loader stores this in the in-memory descriptor and aborts the entire file parse if `total_time == 0` AND `element_count == 0`. At runtime, the elapsed-time accumulator is taken modulo this loop period to produce the current loop phase (see C.5).

### A.3.6 Group F — Keyframe / static-state block (branched on `anim_loop`)

#### Branch A: `anim_loop != 0` — animated keyframe array

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED, SAMPLE-UNVERIFIED**

Keyframe count is equal to `tex_count` (the texture sub-array count drives the frame count). Each keyframe is 44 bytes in memory; 10 values are read from file per keyframe. The six float parameters (reads 2–7) are now resolved (see A.3.7).

| Read order | Byte count | Type | Field | Notes |
|---:|---:|------|-------|-------|
| 1 | 4 | u32 LE | `kf_index` | Keyframe slot index (0-based frame number) |
| 2 | 4 | f32 LE | `velocity_x` | Emission velocity / displacement, X component (see A.3.7). Confidence: HIGH |
| 3 | 4 | f32 LE | `velocity_y` | Emission velocity / displacement, Y component. Confidence: HIGH |
| 4 | 4 | f32 LE | `velocity_z` | Emission velocity / displacement, Z component. Confidence: HIGH |
| 5 | 4 | f32 LE | `size_x` | Billboard / particle size, X component (width). Confidence: HIGH |
| 6 | 4 | f32 LE | `size_y` | Billboard / particle size, Y component (height). Confidence: HIGH |
| 7 | 4 | f32 LE | `size_z` | Billboard / particle size, Z component (depth / mesh Z scale). Confidence: HIGH |
| 8 | 4 | f32 LE | `kf_rot_x_deg` | Euler X rotation in **degrees**. Converted to a quaternion component at load (see A.4). Confidence: CONFIRMED |
| 9 | 4 | f32 LE | `kf_rot_y_deg` | Euler Y rotation in **degrees**. Same conversion. Confidence: CONFIRMED |
| 10 | 4 | f32 LE | `kf_rot_z_deg` | Euler Z rotation in **degrees**. Same conversion. Confidence: CONFIRMED |

In-memory keyframe layout (44 bytes, stride 0x2C):

| Byte offset | Size | Type | Field | Source |
|---:|---:|------|-------|--------|
| +0x00 | 4 | u32 | `kf_index` | file read 1 |
| +0x04 | 4 | f32 | `velocity_x` | file read 2 |
| +0x08 | 4 | f32 | `velocity_y` | file read 3 |
| +0x0C | 4 | f32 | `velocity_z` | file read 4 |
| +0x10 | 4 | f32 | `size_x` | file read 5 |
| +0x14 | 4 | f32 | `size_y` | file read 6 |
| +0x18 | 4 | f32 | `size_z` | file read 7 |
| +0x1C | 4 | f32 | `rotation_quat_x` | computed from `kf_rot_x_deg` (see A.4) |
| +0x20 | 4 | f32 | `rotation_quat_y` | computed from `kf_rot_y_deg` |
| +0x24 | 4 | f32 | `rotation_quat_z` | computed from `kf_rot_z_deg` |
| +0x28 | 4 | f32 | `rotation_quat_w` | constructor-initialized to 1.0 (identity), then set by the conversion |

The quaternion (bytes +0x1C..+0x2B) is NOT present in the file — only the three degree values are stored; the quaternion is derived during load. The constructor initializes the rotation quaternion to identity `(0, 0, 0, 1)` before conversion.

#### Branch B: `anim_loop == 0` — static state

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED, SAMPLE-UNVERIFIED**

One allocation of 48 bytes (0x30) in memory. Six floats are always read; three additional rotation floats are conditional on `emitter_type == 2`. The six base floats carry the same meaning as their keyframe counterparts.

| Read order | Byte count | Type | Field | Condition | Notes |
|---:|---:|------|-------|-----------|-------|
| 1 | 4 | f32 LE | `static_velocity_x` | always | Emission velocity / displacement X (see A.3.7) |
| 2 | 4 | f32 LE | `static_velocity_y` | always | Emission velocity / displacement Y |
| 3 | 4 | f32 LE | `static_velocity_z` | always | Emission velocity / displacement Z |
| 4 | 4 | f32 LE | `static_size_x` | always | Billboard / particle size X (width) |
| 5 | 4 | f32 LE | `static_size_y` | always | Billboard / particle size Y (height) |
| 6 | 4 | f32 LE | `static_size_z` | always | Billboard / particle size Z (depth / mesh Z scale) |
| 7 | 4 | f32 LE | `static_rot_x_deg` | only if `emitter_type == 2` | Euler X rotation, degrees. Same quaternion conversion as Branch A. Confidence: CONFIRMED |
| 8 | 4 | f32 LE | `static_rot_y_deg` | only if `emitter_type == 2` | Euler Y rotation, degrees. Confidence: CONFIRMED |
| 9 | 4 | f32 LE | `static_rot_z_deg` | only if `emitter_type == 2` | Euler Z rotation, degrees. Confidence: CONFIRMED |

If `emitter_type != 2`, only 24 bytes (reads 1–6) are consumed from the file for this group; the in-memory allocation is still 48 bytes, with the rotation quaternion left at its constructor-initialized identity value.

In-memory static-state layout (48 bytes, stride 0x30):

| Byte offset | Size | Type | Field | Condition |
|---:|---:|------|-------|-----------|
| +0x00 | 4 | u32 | count prefix (always 1) | always |
| +0x04 | 4 | f32 | `velocity_x` | always |
| +0x08 | 4 | f32 | `velocity_y` | always |
| +0x0C | 4 | f32 | `velocity_z` | always |
| +0x10 | 4 | f32 | `size_x` | always |
| +0x14 | 4 | f32 | `size_y` | always |
| +0x18 | 4 | f32 | `size_z` | always |
| +0x1C | 4 | f32 | `rotation_quat_x` | from `static_rot_x_deg` if `emitter_type == 2`, else identity |
| +0x20 | 4 | f32 | `rotation_quat_y` | from `static_rot_y_deg` if `emitter_type == 2`, else identity |
| +0x24 | 4 | f32 | `rotation_quat_z` | from `static_rot_z_deg` if `emitter_type == 2`, else 0 |
| +0x28 | 4 | f32 | `rotation_quat_w` | constructor-initialized to 1.0; set by conversion if `emitter_type == 2` |

### A.3.7 Resolved semantics of the six float parameters

The six floats shared by Branch A keyframes and Branch B static states form two 3-component vectors. Both interpretations are HIGH confidence (parser layout + runtime use), but SAMPLE-UNVERIFIED (no element-bearing sample available).

- **`velocity` (`velocity_x/y/z`, file reads 2–4 / keyframe bytes +0x04..+0x0F):** a 3D direction-plus-magnitude vector for the particle element's displacement from the effect origin. At render time it is rotated by the effect instance's world orientation quaternion, scaled by the instance's effective scale, and added to the world-space origin to produce the particle's world position. The magnitude encodes speed or offset distance. Keyframes are interpolated between adjacent frames.
- **`size` (`size_x/y/z`, file reads 5–7 / keyframe bytes +0x10..+0x1B):** the billboard/particle dimensions in world units. For the billboard rendering path (`emitter_type 0`, and `2`), `size_x` and `size_y` produce the quad's half-extents (each multiplied by ±0.5 to form the four corners around the center). For the mesh path (`emitter_type 1`), all three components multiply the corresponding mesh-vertex coordinate axis. Keyframes are interpolated as a continuous size channel.

These two vectors are the per-particle motion and scale inputs the runtime tick consumes (see C.5).

## A.4 Rotation Encoding Note

All rotation values throughout this format are stored on disk in **degrees** and converted to quaternion form during load. The conversion multiplies by `π / 180` (approximately `0.017453293`), then applies standard half-angle Euler-XYZ decomposition (products of `sin(angle/2)` and `cos(angle/2)` per axis). Parsers should reproduce this: do not store degree values in the parsed struct; store the computed quaternion (or convert to radians as appropriate). The rotation quaternion is initialized to identity `(0, 0, 0, 1)` before conversion.

## A.5 Companion Manifest: `xeffect.lst`

**Confidence: HIGH** (parser structure confirmed; analogy to `bmplist.lst` which uses identical layout and is sample-verified)

The manifest is a flat binary file that boots the effect registry (loaded at startup alongside `bmplist.lst` and the texture directory).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | Number of effect name records |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte record per effect |

**Name record (30 bytes, zero-padded):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] | `name` | Base filename without path prefix and without `.xeff` extension. Full path resolved as `data/effect/xeff/<name>.xeff`. Null-padded to 30 bytes. Korean names are CP949-encoded. |

## A.6 Companion: `bmplist.lst` — Texture Name Pool

**Confidence: VERIFIED** (sample confirmed: 45,784 bytes = 4 + 1526 × 30; first record `.mayaswatches`)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | 1526 in observed sample |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte name record per texture slot |

**Name record (30 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] | `name` | Base name, no extension. Full path: `data/effect/texture/<name>.tga`. Null-padded. CP949 for Korean. |

## A.7 Companion: `.xobj` — ASCII Primitive Meshes

**Confidence: CONFIRMED** (3 sample files verified)

These files in `data/effect/xobj/` are **plain text** (CRLF line endings), NOT binary. Each file begins with integer line counts followed by tab-separated floating-point vertex/UV data rows. They define the emitter shape (plane, cone, triangle fan) referenced by the mesh path (`emitter_type 1`) through the shared mesh table (selected by `resource_id`). This spec does not further detail their text layout. Parsers for `.xobj` must use a text reader, not a binary reader.

## A.8 Enumerations / Flags

### `emitter_type` (u32 at element read 1 / in-memory dword[0])

| Value | Meaning | Rendering path | Confidence |
|------:|---------|----------------|------------|
| `0` | Billboard sprite | Flat camera-facing quad built from `size_x`/`size_y` half-extents; no orientation reads in Branch B | CONFIRMED |
| `1` | Mesh-particle object | Per-vertex transform of a shared mesh selected by `resource_id`; size Vec3 scales each axis | CONFIRMED |
| `2` | Directional billboard | Camera-facing quad like type 0, but applies a fixed 90° Y-axis pre-rotation and reads three extra rotation floats in Branch B (static state) | CONFIRMED |
| other | Behavior undetermined; likely additional primitive types (e.g. ribbon/trail) exist | — | UNRESOLVED |

## A.9 Render flags via `tex_count` low byte (CAUTION — MEDIUM confidence)

At runtime, bits 0 and 1 of the low byte of the in-memory `tex_count` dword are tested as UV-scroll render flags:

- bit 0 set → scroll the texture U coordinate by `phase_ms mod 5000 / 5000.0` (a 5-second looping U scroll)
- bit 1 set → scroll the texture V coordinate by the same ratio

Because the parser writes the frame count (`tex_count`) into this same dword and the element constructor zeroes it, it is unclear whether this is an intentional dual use (frame count low bits doubling as flags) or whether a distinct flag byte was originally overlaid here. Treat this as a **caution**: an implementation that reproduces the UV scroll should gate it on these bits exactly as described, but the design intent is unverified and needs a sample with `element_count > 0`. Confidence: MEDIUM.

## A.10 In-Memory Element Struct Map (104 bytes, 26 dwords)

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED (no element-bearing sample verification)**

This is the in-memory layout the loader builds; it is provided so an engineer understands the runtime's view. **File read order differs from in-memory dword order** for `tex_count` and `field_unknown_a` (the loader writes them in reverse relative to file order). On disk, fields appear strictly in the read order given in A.3.

| Dword | Byte offset | Type | Field | Source / Notes |
|---|---|---|---|---|
| [0] | +0x00 | u32 | `emitter_type` | file read 1 |
| [1] | +0x04 | u32 | `resource_id` | file read 2 (< 10000 = mesh index; ≥ 10000 = particle id) |
| [2] | +0x08 | u8 bool | `anim_flag` | file read 3 (low byte; upper 3 bytes unused) |
| [3] | +0x0C | u32 | `field_unknown_a` (`element_flags`) | file read 5 (after `tex_count`). UNRESOLVED. Constructor zeroes it; no semantic read-site found. |
| [4] | +0x10 | u32 | `tex_count` | file read 4. Low byte also tested as UV-scroll flags (A.9). |
| [5..7] | +0x14..+0x1C | ptr×3 | texture-handle vector | resolved texture handles (no file bytes) |
| [8] | +0x20 | u32 | `alpha_key_count` | Group C count |
| [9..11] | +0x24..+0x2C | ptr×3 | alpha-key vector (f32) | values stored as `1.0 − file_value` |
| [12] | +0x30 | u32 | (constructor-zeroed; no parser write) | likely padding/reserved |
| [13] | +0x34 | u32 | `scale_key_count` | high-water mark of the three Group D counts |
| [14..16] | +0x38..+0x40 | ptr×3 | scale-Vec3 vector | Group D triplets (scale_x/y/z) |
| [17] | +0x44 | u32 | (constructor-zeroed; no confirmed write) | likely padding |
| [18] | +0x48 | u32 | `anim_base_time` | Group E, milliseconds |
| [19] | +0x4C | u32 | `start_time_or_total` | DUAL USE: parser writes the derived `total_time`; at runtime this slot is read as the effect start timestamp. The likely resolution is that a spawn-setup step overwrites it with the spawn timestamp. AMBIGUOUS — see open questions. |
| [20] | +0x50 | u32 | `anim_stride` | Group E, milliseconds per frame |
| [21] | +0x54 | u32 | `total_time` | derived = `tex_count × anim_stride + anim_base_time` |
| [22..24] | +0x58..+0x60 | ptr×3 | keyframe / static-state vector | Branch A (44-byte) or Branch B (48-byte) entries |
| [25] | +0x64 | u32 | (constructor-zeroed) | likely padding |

## A.11 Named Constants

| Name | Value | Context |
|------|------:|---------|
| `XEFF_ELEMENT_STRIDE` | 104 (0x68) | In-memory element record size; also determines per-element file read budget |
| `XEFF_KEYFRAME_STRIDE` | 44 (0x2C) | In-memory keyframe entry size (Branch A) |
| `XEFF_STATIC_STRIDE` | 48 (0x30) | In-memory static-state allocation (Branch B) |
| `XEFF_TEX_NAME_LEN` | 64 (0x40) | Bytes per texture name entry in Group B sub-array |
| `XEFF_LST_NAME_LEN` | 30 (0x1E) | Bytes per record in `xeffect.lst` and `bmplist.lst` |
| `XEFF_EMITTER_BILLBOARD` | 0 | `emitter_type`: flat billboard sprite |
| `XEFF_EMITTER_MESH` | 1 | `emitter_type`: mesh-particle object |
| `XEFF_EMITTER_DIRECTIONAL` | 2 | `emitter_type`: directional billboard (reads extra rotation in Branch B) |
| `XEFF_RESOURCE_PARTICLE_THRESHOLD` | 10000 | `resource_id ≥ this` → GPU particle descriptor id; below → shared mesh-table index |
| `XEFF_UV_SCROLL_PERIOD_MS` | 5000 | Loop period for the UV-scroll render flags (A.9) |
| `XEFF_INVALID_MAGIC` | 0x46464558 | `effect_id` sentinel; file is invalid if header equals this |
| `XEFF_TIME_UNIT` | milliseconds | Engine wall-clock unit for all `.xeff` timing fields |

---

# Section B: `.eff` Effect-Object Shape

## B.1 Identification

- **Extension:** `.eff`
- **Found in:** `.pak` archive; logical path `data/effect/obj/<name>.eff`
- **Disambiguation key:** Directory `data/effect/obj/` exclusively. Do NOT use this layout for `.eff` files in sound or particle paths.
- **Magic / signature:** None
- **Version field:** None
- **Endianness:** Little-endian

## B.2 File Layout Overview

The file consists of exactly two sections in sequence with no padding, no footer, and no checksum.

**File size formula (exact, verified):**

```
total_bytes = 4 + (index_count × 2) + 4 + (vert_count × 32)
```

## B.3 Index Section

**Confidence: VERIFIED** — all three samples satisfy the formula with zero residual bytes.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `index_count` | Total number of u16 index values stored. Always divisible by 3 — pure indexed triangle list. Divide by 3 to get face/triangle count. |
| 0x04 | `index_count × 2` | u16 LE[] | `indices[]` | Flat vertex-index array. Consumed in consecutive groups of three; each triplet defines one triangle. No triangle strips, fans, or degenerate primitives observed. Indices are 0-based references into the vertex array. |

## B.4 Vertex Section

Immediately follows the last index byte at file offset `4 + (index_count × 2)`. The `vert_count` field at that position may be at a non-4-byte-aligned address if `index_count × 2` is not a multiple of 4. No padding is inserted. Parsers must read `vert_count` at `4 + index_count × 2` regardless of address alignment.

**Confidence: VERIFIED**

| Offset from section start | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 4 | u32 LE | `vert_count` | Number of 32-byte vertex records that follow |
| +4 | `vert_count × 32` | VertexRecord[] | `verts[]` | One record per vertex |

### B.4.1 VertexRecord (32 bytes, stride = 0x20)

**Confidence: VERIFIED** (geometry coherent for rect and cone; see normal-encoding note for tringle exporter artifact)

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 4 | f32 LE | `pos_x` | World-space X position |
| +4 | 4 | f32 LE | `pos_y` | World-space Y position |
| +8 | 4 | f32 LE | `pos_z` | World-space Z position |
| +12 | 4 | f32 LE | `normal_x` | Vertex normal X component |
| +16 | 4 | f32 LE | `normal_y` | Vertex normal Y component |
| +20 | 4 | f32 LE | `normal_z` | Vertex normal Z component |
| +24 | 4 | f32 LE | `tex_u` | Texture coordinate U (horizontal, range 0–1 in observed data) |
| +28 | 4 | f32 LE | `tex_v` | Texture coordinate V (vertical, range 0–1 in observed data) |

The 32-byte-per-vertex layout is consistent with a shared engine convention: the same field order and stride appear in a separate mesh-loading path for the BUD terrain format (confirmed from parser analysis). Parsers may treat this as an engine-wide vertex record type.

## B.5 Normal Encoding Note

In two of the three verified samples (`cone.eff`, `rect.eff`) all vertex normals are unit vectors (magnitude = 1.0). In `tringle.eff` the stored normal bytes are bit-for-bit identical to the position bytes, yielding a non-unit magnitude of approximately 1.16. This is assessed as an **exporter bug** (position data written into the normal slot), not a format variant. Parsers must tolerate non-unit normals without aborting. Normalizing at load time is recommended for rendering use.

## B.6 Primitive Type

All observed files define a pure **indexed triangle list**: every three consecutive indices in `indices[]` define one triangle. `index_count` is always a multiple of 3. No strips, fans, or degenerate primitives observed.

## B.7 Texture and Material Binding

These files contain geometry only — no texture filename, no material ID, and no color data. Texture and material binding are assumed to be handled externally by the effect subsystem that references a given shape file.

## B.8 Sample Measurements (reference — not format-defining)

Three samples were measured; all satisfy the file-size formula exactly.

| File | File size | `index_count` | Triangle count | `vert_count` | `vert_count` field offset |
|------|----------:|---:|---:|---:|---:|
| `cone.eff` | 496 B | 36 | 12 | 13 | 0x4C |
| `rect.eff` | 148 B | 6 | 2 | 4 | 0x10 |
| `tringle.eff` | 110 B | 3 | 1 | 3 | 0x0A (not 4-byte aligned) |

---

# Section C: Effects Runtime — Instantiation, Update, Attachment, Triggers

> Behavioral model, not a byte format. It tells an engineer how a parsed `.xeff` descriptor
> (Section A) becomes a live, ticking, on-screen effect, how it attaches to actors and bones, how
> it expires and is culled, and which network messages cause one to spawn. Wire field offsets are
> stated as plain facts; instance-struct offsets (`+0xNN`) are runtime layout, not file layout.

## C.1 Time base

**Confidence: CONFIRMED.** All effect timing uses the engine's millisecond wall clock (a single global clock derived from the operating-system multimedia timer, optionally scaled by a global time-scale factor where `1.0` = realtime). Every spawn path records the clock value at spawn as the effect's start timestamp; every tick subtracts that from the current clock to get elapsed milliseconds. Consequently all `.xeff` timing fields (`anim_stride`, `anim_base_time`, derived `total_time`) and the per-instance expiry deadline are in **milliseconds**.

## C.2 Descriptor registry and lazy load

**Confidence: CONFIRMED.**

- A single global **effect registry** holds parsed descriptors keyed by numeric `effect_id` (a sorted map). It is populated at boot by reading `xeffect.lst`, `bmplist.lst`, and the texture directory (Section A.5/A.6).
- **Lazy load:** a spawn request looks up the descriptor by `effect_id`. If the descriptor's "loaded" flag is clear, the registry parses the `.xeff` file from disk on the spot (the parser of Section A), resolves its textures, then stamps the descriptor as loaded. A failed lookup returns nothing and the spawn is abandoned.
- Two descriptor-level fields read at lookup time:
  - a **loop-period** value used by the tick as the modulus for the loop phase;
  - a **base-scale** value that every spawn path multiplies into the instance's effective scale (so `instance_scale = descriptor_base_scale × caller_scale`).
- Confirms the Section A statement that two filenames may map to one `effect_id`; the duplicate-id resolution rule remains unknown.

## C.3 Instance memory layout (runtime, shared across the XEffect family)

**Confidence: CONFIRMED for the marked fields; MEDIUM/HIGH where noted.** Offsets are within the live instance object, cross-checked across all spawn, bind, and tick paths. This is runtime state, not the on-disk format.

| Offset | Type | Field (neutral) | Confidence |
|---:|---|---|---|
| +0x00 | ptr | vtable / class identity | CONFIRMED |
| +0x08 | u8 | active flag (tick is a no-op when 0) | CONFIRMED |
| +0x09 | u8 | warmup-done flag (first non-warm tick runs per-element init) | HIGH |
| +0x0C | ptr | **descriptor pointer** (0 ⇒ invalid ⇒ instance discarded before joining the active list) | CONFIRMED |
| +0x10..+0x1B | Vec3 f32 | world position (placed effect: from spawn args; attached effect: actor/bone-derived each tick) | CONFIRMED |
| +0x1C..+0x2B | Quat f32 | world orientation | CONFIRMED |
| +0x3C | u8 | loop flag (from the spawn loop argument) | CONFIRMED |
| +0x40 | u32 | **expiry timestamp = spawn_clock + lifetime_ms** | CONFIRMED |
| +0x44 | u8 | "elapsed-this-frame" flag (set by tick) | HIGH |
| +0x45 | u8 | "drew-this-frame" flag (set/cleared by tick) | HIGH |
| +0x48 | f32 | **effective scale = descriptor base-scale × caller scale** | CONFIRMED |
| +0x50 | u32/f32 | time-scale / playback-speed factor (from spawn arg) | CONFIRMED |
| +0x54 | u32 | bound actor handle / sort id (actor-anchored and bone variants) | CONFIRMED |
| +0x58 | u32 | actor handle (bone variant); anchor-mode byte (actor-anchored variant) | CONFIRMED |
| +0x5C | u8 | actor sub-id / slot byte (bone variant) | CONFIRMED |
| +0x5D | u8 | **bone-source mode** (bone variant): 0 = explicit bone id; 1, 2 = derive bone id from an action table | HIGH |
| +0x60 | u32 | **bone id / hint** (bone variant) | CONFIRMED |
| +0x64 | u8 | **anchor mode** (bone variant): 1 = use the bone's own orientation; 2 = use the actor-root orientation | HIGH |
| +0x30 | ptr | per-element runtime particle-buffer array (allocated on first tick) | CONFIRMED |

(Field names map to the three subclasses described in C.4. The byte offsets above are decimal-to-hex conversions of the runtime layout; an engineer reimplementing the runtime should treat them as the relative order/grouping of state, not as a fixed ABI to reproduce verbatim.)

## C.4 Spawn pathways (three instance kinds, one descriptor registry)

**Confidence: CONFIRMED.** Three instance kinds share the descriptor registry but differ in how they obtain their world transform. Each has its own object pool and one primary spawn entry point. Each spawn: pool-allocates an instance; resolves the descriptor by `effect_id` (lazy-loading on first use); if the descriptor is missing, destroys the instance immediately (it never joins the active list); otherwise writes the instance fields below and inserts it into that kind's active list. Effective scale is always `descriptor_base_scale × caller_scale`; expiry is always `spawn_clock + lifetime_ms`.

| Instance kind | Transform source | Key spawn arguments | Notes |
|---|---|---|---|
| **Actor-anchored, timed** | Follows a target actor; lives for a fixed duration | `effect_id`, actor handle, anchor mode/param, loop flag, scale, time-scale | The workhorse path; by far the most call sites (attack/death/level-up/item-use handlers and the actor visual/motion system all use it). |
| **World-placed** | Fixed world position + orientation quaternion supplied at spawn | `effect_id`, position Vec3, orientation quaternion, loop flag, scale, time-scale | Used for ground/positional FX and the character-select scene builder. |
| **Bone-attached** | Resolves a skeleton bone's world transform every tick | `effect_id`, actor handle, sub-id, bone-source mode, bone id, anchor mode, scale, time-scale | See C.6. Spawned in groups via a table that maps an effect-group key to a list of (bone, flags) sub-entries. |

**Weapon-trail example (bone-attached group):** an actor's weapon/effect-state index maps to a fixed base `effect_id` and fires a bone-attached effect group. The id table is contiguous (`390000xx1`, with the state index encoded in the middle digits), confirming that visual effect ids are assigned in numeric ranges by purpose. This is presentation-only.

## C.5 Per-frame tick (particle update / emit / draw)

**Confidence: HIGH (behavioral; SAMPLE-UNVERIFIED for element-body field mapping).** Each kind's active list is walked each frame; for each live instance:

1. **Gate:** do nothing if the instance's active flag is clear or the descriptor is not marked ready.
2. **First-tick init:** on the first real tick, allocate the per-element runtime particle buffers. For elements whose `resource_id ≥ 10000`, this is also where the bridge to the separate GPU particle subsystem fires (see C.7).
3. **Elapsed phase:** `elapsed = now − start`; multiply by the instance time-scale; take it **modulo the descriptor's loop period** to obtain the current loop phase (milliseconds within one loop).
4. **Proximity cull:** if the local player exists and the squared distance from the player to the effect's world position exceeds a render-distance threshold, abort this instance's draw for the frame (no quads built). This is the primary culling mechanism.
5. **Element loop:** for each descriptor element, select the active keyframe by the phase, compute per-vertex RGBA (each channel float × 255; alpha from the inverted-alpha channel of A.3.3), and build geometry by `emitter_type`:
   - **type 0 (billboard):** a camera-facing quad whose half-extents come from `size_x`/`size_y` (each ±0.5), positioned relative to the cached camera basis.
   - **type 2 (directional billboard):** the same quad with a fixed 90° pre-rotation about the Y axis.
   - **type 1 (mesh):** transform each vertex of the mesh selected by `resource_id`, scaling each axis by the corresponding `size` component.
   - Per vertex the pipeline is: scale (by the `size` Vec3 and the Group D scale channel) → rotate by the instance orientation quaternion → translate by the instance world position → write color. The `velocity` Vec3 (A.3.7) contributes the per-particle displacement that is rotated and scaled into the world position.
   - **UV scroll:** if the element's UV-scroll bits are set (A.9), offset U and/or V by `phase mod 5000 / 5000.0`.
6. Submit the built geometry for drawing. The camera/render context is cached once per frame and reused for all billboard orientation.

The actor-anchored kind uses a tick variant that additionally re-derives the instance transform from its target actor before building geometry.

## C.6 Attach to actor / bone

**Confidence: HIGH.** The bone-attached kind resolves its transform every tick rather than storing a fixed one:

1. Look up the bound actor by its stored handle; skip if the actor is gone.
2. Fetch the actor's current animation pose. Determine the target bone: directly from the stored bone id when bone-source mode is 0, or via an action table when it is 1 or 2 (the action-table source is unconfirmed — see open questions).
3. Read that bone's world transform and write the instance's world position and orientation from it. The anchor mode selects whether the bone's own orientation or the actor-root orientation is applied.
4. Then run the standard particle build (C.5).

The actor-anchored kind attaches more loosely (it follows the actor's position/sort handle, with an anchor mode/param), without per-bone pose resolution.

Actors also keep an **effect-slot array** (16-byte slots, indexed by slot number) for persistent effects placed on them by the network (see C.8.2). Spawn/respawn/visual-change network messages re-instantiate an actor's full effect set; a central "apply effect group to actor" routine drives this, iterating the actor's effect slots.

## C.7 Lifetime, expiry, and the separate particle subsystem

**Confidence: CONFIRMED (lifetime/cull); CONFIRMED-existence, NOT-ANALYZED (particle subsystem).**

- **Lifetime / expiry:** every spawn writes `expiry = spawn_clock + lifetime_ms` (absolute millisecond deadline). A timed-removal helper compares elapsed time against the instance's duration and, when exceeded, invokes the instance's cull/destroy path. Combined with the per-frame proximity cull (C.5 step 4), this governs effect teardown. Instances whose descriptor lookup failed are destroyed before ever joining an active list.
- **Separate GPU particle subsystem:** a distinct particle system (its own manager and GPU vertex/index buffer, sourced from `data/effect/particle/particleEmitter.eff` and `tool/effect/particle_*.txt`) is reached from the tick for elements with `resource_id ≥ 10000`. Its `particleEmitter.eff` file format is the one Section's disambiguation table marks "not analyzed." **It is not specced here — do not conflate it with the Section B `.eff` geometry format.**

## C.8 Network triggers (combat / skills / item use)

Two server→client messages are the primary network triggers for visual effects; a third (item-use) reuses the same spawn API. These are **presentation-only**: no gameplay state changes here (damage and combat resolution are server-authoritative — see `specs/combat.md`). Wire layouts below are stated as byte facts; promote them into `packets/` and `opcodes.md` via the protocol-spec-author (they are out of scope for this format file but are recorded here for the runtime model).

### C.8.1 Attack/hit effect (transient) — opcode group 5/139

**Confidence: CONFIRMED layout, HIGH behavior.** Fixed 12-byte payload:

| Offset | Size | Type | Field |
|---:|---:|------|-------|
| +0x00 | 4 | u32 LE | source sort id |
| +0x04 | 4 | u32 LE | source actor id |
| +0x08 | 4 | u32 LE | attack/effect id |

Behavior: resolve the caster actor (by sort id + actor id) and an effect template (by the attack/effect id); if either is missing, do nothing. Then map the numeric attack/effect id (by range) to one or more fixed visual effect ids plus a sound id, and spawn them attached to the caster (actor-anchored or, for some templates, world-placed at the caster's bone-anchor position). This is a **fire-and-forget hit-flash + SFX broadcast**; it changes no gameplay state.

### C.8.2 Combat-effect instance spawn (persistent / placed) — opcode group 5/14

**Confidence: CONFIRMED layout, HIGH behavior.** Fixed 48-byte payload (key fields):

| Offset | Size | Type | Field |
|---:|---:|------|-------|
| +0x00 | 4 | u32 LE | source sort id |
| +0x04 | 4 | u32 LE | source actor id |
| +0x08 | 1 | i8 | mode |
| +0x09 | 1 | u8 | slot |
| +0x0C | 4 | i32 LE | effect id |
| +0x14 | 4 | i32 LE | param 1 (meaning unresolved) |
| +0x18 | 4 | i32 LE | param 2 (meaning unresolved) |
| +0x1C | 4 | f32 LE | world X |
| +0x20 | 4 | f32 LE | world Z |
| +0x2C | 1 | u8 | notice flag |

Behavior: convert world (X, Z) to a map grid cell; if `mode == -1`, substitute a default generic effect id. Spawn a **world/ground combat-effect entity** at the grid position (tagged differently for NPC sources). When the bound actor exists and `mode == 0`, store the effect into the actor's effect-slot array at the given slot (16-byte slots) and refresh the actor's visuals. Certain effect-id ranges trigger a sound; `notice_flag == 1` (gated by a one-shot global) shows an on-screen notice from the string table. This is the **persistent / area combat-effect** placement, contrasted with 5/139's transient hit FX.

### C.8.3 Item-use effect — opcode group 4/139

**Confidence: HIGH.** Reuses the same actor-anchored / world-placed spawn API as above to play an item-use visual; covered functionally by the same model.

---

# Section D: `effectscale.xdb` — Per-Effect Scale Override Table

**Confidence: VERIFIED** (byte-exact against one 16-byte, two-record sample; size formula exact). The consuming code path (which spawn step applies the override) was not located — see open questions.

- **Path:** `data/script/effectscale.xdb`
- **Endianness:** Little-endian
- **Magic / signature:** None
- **Layout:** a flat array of fixed 8-byte records, no header and no count prefix; record count = `file_size / 8`.

**Record (8 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0x00 | 4 | u32 LE | `effect_id` | Matches a `.xeff` `effect_id` (Section A.2) |
| +0x04 | 4 | f32 LE | `scale` | Scale multiplier for that effect |

Observed sample records: `(353100201 → 3.0)`, `(353100202 → 2.0)`.

This is a per-effect-id scale-override table that augments the descriptor base-scale (C.2). Whether the runtime multiplies it in addition to, or instead of, the descriptor base-scale is unconfirmed (the application site was not isolated).

---

# Shared: Known Unknowns and Open Questions

## From `.xeff` (Section A)

1. **`field_unknown_a` / `element_flags` (in-memory dword[3], byte +0x0C)** — the fifth u32 read from each element. No semantic runtime read-site was isolated. Candidate roles: render/blend flags, packed color/tint, or reserved padding (the constructor zeroes it). Confidence: LOW. Needs a sample with `element_count > 0` and a non-zero value at this slot. Do not assign a meaning.
2. **`emitter_type` values beyond 0, 1, 2** — behavior and any additional conditional reads are unknown (ribbon/trail/other primitives may exist).
3. **`tex_count` low byte as UV-scroll flags (A.9)** — observed at runtime but the dual use of the frame count as flags is unusual; intent is unverified (MEDIUM). Needs an element-bearing sample.
4. **In-memory dword[19] dual use (`start_time_or_total`, byte +0x4C)** — the parser writes the derived `total_time` there, while the tick reads it as the effect start timestamp; the likely resolution (spawn-time overwrite with the spawn clock) is unconfirmed (MEDIUM). Needs a spawn-path trace or live sample.
5. **In-memory dwords [12] and [17] (bytes +0x30, +0x44)** — constructor-zeroed with no observed parser write or runtime read; likely padding/reserved. Unconfirmed.
6. **`effect_id` duplicate resolution** — two filenames may map to one `effect_id`; the resolution rule (last-wins / first-wins / alias) is unknown.
7. **Whether `xeffect.lst` is pre-sorted by `effect_id`** — the registry is a sorted map in memory; whether the file is pre-sorted or sorted on load is unconfirmed.
8. **Element-body float channels: byte-level verification** — the 104-byte stride and the field semantics (velocity Vec3, size Vec3) are parser + runtime-trace results; no sample with `element_count > 0` exists, so they remain SAMPLE-UNVERIFIED.
9. **Scale-channel (Group D) vs. size Vec3 interaction** — both feed per-vertex scaling at render time; their exact combination order is described but not byte-verified.

## From `.eff` effect-object (Section B)

1. **Parser function for `.eff` obj files** — no dedicated loading function was identified by string reference; the file may be read by a shared binary-mesh routine. Behavior for malformed files (mismatched `vert_count`, wrong size) is unknown.
2. **Maximum `index_count` / `vert_count` limits** — a related mesh parser warns above 3072 vertices; whether the `.eff` obj loader enforces a similar limit is unknown.
3. **Coordinate system handedness** — `rect.eff` normals all point −X; right- vs left-handed world space for effect volumes is undetermined from these files alone.
4. **`tex_u` / `tex_v` purpose** — present, but whether the effect pipeline binds a texture to these shapes or the UVs are unused baked output is unknown.
5. **`particleEmitter.eff` in `data/effect/particle/`** — a different `.eff` layout; not analyzed; must not be parsed with this spec.

## From the runtime (Section C)

1. **Per-frame tick driver hookup** — the tick virtuals are walked over each kind's active list, but the exact render-loop call site that iterates the effect managers each frame was not pinned (one hop short of `game_loop`).
2. **A fourth effect pool** — torn down alongside the three known instance pools at shutdown; its instance kind was not identified.
3. **Bone-source action tables (modes 1/2)** — the two action tables consulted to derive a bone id when bone-source mode is 1 or 2 were referenced but their layout/source (skill table? animation events?) is unconfirmed.
4. **5/139 template fields and 5/14 `param1`/`param2`** — their precise meaning (path/trail target, secondary actor, duration, etc.) is not resolved.

## From `effectscale.xdb` (Section D)

1. **Application site** — the table is byte-confirmed, but which spawn step multiplies by it (and whether it stacks with or replaces the descriptor base-scale) was not located.

---

# Cross-References

- **Related formats:** `pak.md` (container), `sound_tables.md` (the other `.eff` variant — sound triggers), `mesh.md` (shares 32-byte vertex record convention), `terrain.md` (BUD path also uses 32-byte vertex layout)
- **Related runtime/specs:** `structs/actor.md` (actor effect-slot array, `actor + 0xCC + 16×slot`), `specs/combat.md` (server-authoritative damage; effects here are presentation-only), `specs/skills.md`, `opcodes.md` (5/139, 5/14, 4/139)
- **Companion plain-text files:** `xeffect.lst`, `bmplist.lst` — manifest/pool files loaded at boot
- **Companion ASCII format:** `.xobj` files in `data/effect/xobj/` — plain-text primitive meshes; not binary
- **Companion binary tables:** `effectscale.xdb` (Section D)
- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
