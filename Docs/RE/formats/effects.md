# Format: .xeff / .eff  (visual-effects subsystem — particle emitters and primitive shapes)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> Two sub-formats are described here because they form one logical subsystem; each has its
> own section with an independent status block. The sound-trigger `.eff` files that share
> the same extension are **out of scope** — see `sound_tables.md`.

---

## Status

### Section A — `.xeff` (particle effect descriptor)

| Item | Value |
|------|-------|
| `sample_verified` | **partial** — file header (8 bytes) verified against 3 real samples; element body is parser-analysis only (all samples have `element_count = 0`) |
| Endianness | Little-endian throughout |
| Magic / signature | None — format identified by file extension and directory only |
| Anti-magic | If `effect_id` at offset 0 equals `0x46464558` the client treats the file as invalid |

### Section B — `.eff` effect-object shape (geometry primitive)

| Item | Value |
|------|-------|
| `sample_verified` | **true** — all fields verified against three real samples (`cone.eff`, `rect.eff`, `tringle.eff`); size formula exact for all three |
| Endianness | Little-endian throughout |
| Magic / signature | None — identified by directory path (`data/effect/obj/`) only |

---

## Disambiguation: the `.eff` extension is overloaded

The client uses the `.eff` extension for at least three unrelated formats. Parsers **must** dispatch by directory, not by magic:

| Path pattern | Format | Spec location |
|---|---|---|
| `data/effect/obj/*.eff` | Effect-object geometry shape (Section B below) | This file |
| `data/effect/xeff/*.xeff` | Particle effect descriptor (Section A below) | This file |
| `tool/sound/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/map*/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/sky/*/particleEmitter.eff` | Particle emitter definition | Unknown — not analyzed; do not conflate with Section B |

---

# Section A: `.xeff` — Particle Effect Descriptor

## A.1 Identification

- **Extension:** `.xeff`
- **Found in:** `.pak` archive; logical path `data/effect/xeff/<name>.xeff`
- **Magic / signature:** None present
- **Anti-magic:** `effect_id` (bytes 0–3) must NOT equal `0x46464558` (the ASCII string `XEFF` in little-endian byte order); the loader treats that value as a corrupt-file sentinel and aborts
- **Version field:** None
- **Endianness:** Little-endian
- **Discovery:** The effect manager reads a manifest (`data/effect/xeffect.lst`, Section A.4) at boot to register all known `.xeff` paths; individual files are parsed lazily on first spawn

## A.2 File Header

**Confidence: VERIFIED** (parser analysis confirmed against 3 real samples)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `effect_id` | Numeric identifier for this effect. Must not equal `0x46464558`. In observed samples, the value matches the decimal portion of the filename (e.g. filename `343100212.xeff` → id = 343100212), but this correlation is not guaranteed — the id is the lookup key, not a file-name checksum. Two files may share the same `effect_id`. |
| 0x04 | 4 | u32 LE | `element_count` | Number of element records that follow immediately. Zero is valid (stub/empty effect). |

Immediately after the 8-byte header, `element_count` element records follow sequentially. There is no additional directory, offset table, or terminator.

## A.3 Element Array

**Confidence: PARSER-CONFIRMED, SAMPLE-UNVERIFIED** — stride and field order derived from the parser's allocation formula (`element_count × 104 + 4 bytes`) and sequential read sequence. No sample with `element_count > 0` was available for byte-level cross-check.

The element array is preceded in memory by a 4-byte count prefix (the same value as `element_count` in the header). On disk the elements follow immediately after the header with no count repeat.

- **Record stride:** 104 bytes (0x68) — confidence: PARSER-CONFIRMED
- **Record count source:** `element_count` field at file offset 0x04

Each element is read as a sequence of variable-length sub-groups. The groups must be parsed in order; there is no seek-based access.

### A.3.1 Group A — Emitter identity (20 bytes, fixed)

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 4 | u32 LE | `emitter_type` | Emitter behavior class. Known value: `2` = directional / oriented emitter. Other values: unconfirmed. Confidence: PARSER-CONFIRMED |
| 2 | 4 | u32 LE | `emitter_subtype` | Secondary type/mode. Semantics: UNRESOLVED. Confidence: PARSER-CONFIRMED |
| 3 | 4 | u32 LE | `anim_flag` | Non-zero enables the animated (multi-keyframe) path. Stored as a boolean byte in the in-memory struct. Confidence: PARSER-CONFIRMED |
| 4 | 4 | u32 LE | `tex_count` | Number of texture name entries in the texture sub-array (Group B). Also controls the keyframe array length when `anim_loop != 0`. Confidence: PARSER-CONFIRMED |
| 5 | 4 | u32 LE | `field_unknown_a` | Fifth consecutive u32; purpose UNRESOLVED. May be a mesh/xobj reference, color pack, or spare parameter. Confidence: PARSER-CONFIRMED (field exists), semantics UNRESOLVED |

### A.3.2 Group B — Texture sub-array (variable length)

Immediately follows Group A. Length = `tex_count × 64` bytes.

| Count | Byte width | Type | Field | Notes / Confidence |
|---|---:|------|-------|--------------------|
| `tex_count` | 64 | char[64] ASCII | `tex_name[i]` | Null-padded ASCII base name (no path prefix, no extension). The client resolves the full path as `data/effect/texture/<tex_name>.tga`. If `tex_count = 0` this group is empty (zero bytes). Confidence: PARSER-CONFIRMED (path strings in loader), SAMPLE-UNVERIFIED for actual name data |

### A.3.3 Group C — Alpha keyframes (variable length)

Follows Group B.

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 4 | u32 LE | `alpha_key_count` | Number of alpha keyframe entries. Confidence: PARSER-CONFIRMED |
| 2 … N+1 | 4 each | f32 LE | `alpha_key[i]` | Per-keyframe alpha value. **Stored inverted**: the client loads each value as `1.0 − file_value`, so a file value of `0.0` represents fully opaque and `1.0` represents fully transparent. Confidence: HIGH (parser arithmetic confirmed) |

### A.3.4 Group D — Scale / float channels (variable length, 3 passes)

Follows Group C. The three passes drive the X, Y, and Z scale channels respectively, each packed as a 3-component float triplet in the in-memory keyframe struct. Each pass reads its own count field. If a later pass's count exceeds an earlier pass's count, the additional slots are zero-padded in memory.

| Pass | Reads | Type | Field | Notes / Confidence |
|---:|---|------|-------|--------------------|
| 1 | 1 × u32 LE | — | `scale_count_x` | Entry count for channel X (first float in each triplet). Confidence: PARSER-CONFIRMED |
| 1 | `scale_count_x` × f32 LE | f32 | `scale_x[i]` | X-scale keyframe values. Confidence: PARSER-CONFIRMED, semantics MEDIUM |
| 2 | 1 × u32 LE | — | `scale_count_y` | Entry count for channel Y (second float in each triplet). Confidence: PARSER-CONFIRMED |
| 2 | `scale_count_y` × f32 LE | f32 | `scale_y[i]` | Y-scale keyframe values. Written at byte +4 inside each 12-byte triplet. Confidence: PARSER-CONFIRMED |
| 3 | 1 × u32 LE | — | `scale_count_z` | Entry count for channel Z (third float in each triplet). Confidence: PARSER-CONFIRMED |
| 3 | `scale_count_z` × f32 LE | f32 | `scale_z[i]` | Z-scale keyframe values. Written at byte +8 inside each 12-byte triplet. Confidence: PARSER-CONFIRMED |

### A.3.5 Group E — Animation timing (9 bytes, fixed)

Follows Group D.

| Read order | Byte count | Type | Field | Notes / Confidence |
|---:|---:|------|-------|--------------------|
| 1 | 1 | u8 | `anim_loop` | Read as a **single byte** (not a u32). Non-zero selects the multi-keyframe branch (Group F Branch A); zero selects the static-state branch (Group F Branch B). Confidence: CONFIRMED (single-byte read confirmed) |
| 2 | 4 | u32 LE | `anim_stride` | Per-frame duration. Unit is UNRESOLVED (milliseconds, ticks, or frames). Confidence: PARSER-CONFIRMED, unit UNRESOLVED |
| 3 | 4 | u32 LE | `anim_base_time` | Base time offset added to the per-frame accumulation. Confidence: PARSER-CONFIRMED |

Derived value (not in file): `total_time = tex_count × anim_stride + anim_base_time`. The loader stores this in the in-memory `CoreXEffect` object and aborts the entire file parse if `total_time == 0` AND `element_count == 0`.

### A.3.6 Group F — Keyframe / static-state block (branched on `anim_loop`)

#### Branch A: `anim_loop != 0` — animated keyframe array

**Confidence: PARSER-CONFIRMED, SAMPLE-UNVERIFIED**

Keyframe count is equal to `tex_count` (the texture sub-array count drives the frame count). Each keyframe is 44 bytes in memory; 10 fields are read from file per keyframe:

| Read order | Byte count | Type | Field | Notes |
|---:|---:|------|-------|-------|
| 1 | 4 | u32 LE | `kf_index` | Keyframe slot index (0-based) |
| 2 | 4 | f32 LE | `kf_param_0` | Purpose UNRESOLVED (likely one of: size, speed, spread, lifetime, opacity) |
| 3 | 4 | f32 LE | `kf_param_1` | Purpose UNRESOLVED |
| 4 | 4 | f32 LE | `kf_param_2` | Purpose UNRESOLVED |
| 5 | 4 | f32 LE | `kf_param_3` | Purpose UNRESOLVED |
| 6 | 4 | f32 LE | `kf_param_4` | Purpose UNRESOLVED |
| 7 | 4 | f32 LE | `kf_param_5` | Purpose UNRESOLVED |
| 8 | 4 | f32 LE | `kf_rot_x_deg` | Euler X rotation in **degrees**. At load time, halved (÷2) and converted to quaternion component via `sin(angle/2)`. See Rotation Encoding note. Confidence: CONFIRMED |
| 9 | 4 | f32 LE | `kf_rot_y_deg` | Euler Y rotation in **degrees**. Same conversion. Confidence: CONFIRMED |
| 10 | 4 | f32 LE | `kf_rot_z_deg` | Euler Z rotation in **degrees**. Same conversion. Confidence: CONFIRMED |

After reading, the loader converts the three degree values to a quaternion using standard Euler-XYZ decomposition with half-angles (`sin(deg × π/180 ÷ 2)`, `cos(deg × π/180 ÷ 2)`). The quaternion is stored in the keyframe struct — it is NOT written back to the file.

#### Branch B: `anim_loop == 0` — static state

**Confidence: PARSER-CONFIRMED, SAMPLE-UNVERIFIED**

One allocation of 48 bytes (0x30) in memory. Six floats are always read; three additional rotation floats are conditional on `emitter_type`:

| Read order | Byte count | Type | Field | Condition | Notes |
|---:|---:|------|-------|-----------|-------|
| 1 | 4 | f32 LE | `static_param_0` | always | Purpose UNRESOLVED |
| 2 | 4 | f32 LE | `static_param_1` | always | Purpose UNRESOLVED |
| 3 | 4 | f32 LE | `static_param_2` | always | Purpose UNRESOLVED |
| 4 | 4 | f32 LE | `static_param_3` | always | Purpose UNRESOLVED |
| 5 | 4 | f32 LE | `static_param_4` | always | Purpose UNRESOLVED |
| 6 | 4 | f32 LE | `static_param_5` | always | Purpose UNRESOLVED |
| 7 | 4 | f32 LE | `static_rot_x_deg` | only if `emitter_type == 2` | Euler X rotation, degrees. Same quaternion conversion as Branch A. Confidence: CONFIRMED |
| 8 | 4 | f32 LE | `static_rot_y_deg` | only if `emitter_type == 2` | Euler Y rotation, degrees. Confidence: CONFIRMED |
| 9 | 4 | f32 LE | `static_rot_z_deg` | only if `emitter_type == 2` | Euler Z rotation, degrees. Confidence: CONFIRMED |

If `emitter_type != 2`, only 24 bytes (reads 1–6) are consumed from the file for this group.

## A.4 Rotation Encoding Note

All rotation values throughout this format are stored on disk in **degrees** and converted to quaternion form during load. The conversion multiplies by `π / 180` (approximately `0.017453293`), then applies standard half-angle Euler-XYZ decomposition. Parsers should reproduce this: do not store degree values in the parsed struct; store the computed quaternion or convert to radians as appropriate.

## A.5 Companion Manifest: `xeffect.lst`

**Confidence: HIGH** (parser structure confirmed; no manifest sample available for byte-level check; analogy to `bmplist.lst` which uses identical layout and is sample-verified)

The manifest is a flat binary file that boots the effect registry.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | Number of effect name records |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte record per effect |

**Name record (30 bytes, zero-padded):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] ASCII | `name` | Base filename without path prefix and without `.xeff` extension. Full path resolved as `data/effect/xeff/<name>.xeff`. Null-padded to 30 bytes. |

## A.6 Companion: `bmplist.lst` — Texture Name Pool

**Confidence: VERIFIED** (sample confirmed: 45,784 bytes = 4 + 1526 × 30)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | 1526 in observed sample |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte name record per texture slot |

**Name record (30 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] ASCII | `name` | Base name, no extension. Full path: `data/effect/texture/<name>.tga`. Null-padded. |

## A.7 Companion: `.xobj` — ASCII Primitive Meshes

**Confidence: CONFIRMED** (3 sample files verified)

These files in `data/effect/xobj/` are **plain text** (CRLF line endings), NOT binary. Each file begins with integer line counts followed by tab-separated floating-point vertex/UV data rows. They define the emitter shape (plane, cone, triangle fan). They are loaded by the XObj manager and referenced by the effect system; this spec does not further detail their text layout. Parsers for `.xobj` must use a text reader, not a binary reader.

## A.8 Enumerations / Flags

### `emitter_type` (u32 at element byte 0)

| Value | Meaning | Confidence |
|------:|---------|------------|
| `2` | Directional / oriented emitter — causes the loader to read the three rotation floats in Branch B | CONFIRMED |
| other | Behavior undetermined; rotation reads in Branch B skipped | UNRESOLVED |

## A.9 Named Constants

| Name | Value | Context |
|------|------:|---------|
| `XEFF_ELEMENT_STRIDE` | 104 (0x68) | In-memory element record size; also determines per-element file read budget |
| `XEFF_KEYFRAME_STRIDE` | 44 (0x2C) | In-memory keyframe entry size (Branch A) |
| `XEFF_STATIC_STRIDE` | 48 (0x30) | In-memory static-state allocation (Branch B) |
| `XEFF_TEX_NAME_LEN` | 64 (0x40) | Bytes per texture name entry in Group B sub-array |
| `XEFF_LST_NAME_LEN` | 30 (0x1E) | Bytes per record in `xeffect.lst` and `bmplist.lst` |
| `XEFF_EMITTER_DIRECTIONAL` | 2 | Known `emitter_type` value: directional emitter |
| `XEFF_INVALID_MAGIC` | 0x46464558 | `effect_id` sentinel; file is invalid if header equals this |

---

# Section B: `.eff` Effect-Object Shape

## B.1 Identification

- **Extension:** `.eff`
- **Found in:** `.pak` archive; logical path `data/effect/obj/<name>.eff`
- **Disambiguation key:** Directory `data/effect/obj/` exclusively. Do NOT use this layout for `.eff` files in sound or sky/particle paths.
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

# Shared: Known Unknowns and Open Questions

## From `.xeff`

1. **`kf_param_0` through `kf_param_5` / `static_param_0` through `static_param_5`** — six float parameters per keyframe/static state whose semantics are entirely unresolved. Likely candidates include particle size, speed, spread angle, lifetime, and emission rate, but no assignment is confirmed.
2. **`field_unknown_a` (Group A, read 5)** — the fifth u32 in each element's identity group; could be an xobj mesh reference index, a packed color, or a spare parameter. Do not guess.
3. **`emitter_type` values other than `2`** — behavior and whether they conditionally read additional file bytes is unknown.
4. **`anim_stride` / `anim_base_time` time unit** — milliseconds, game ticks, or frames; not determinable from parser analysis alone.
5. **Whether `xeffect.lst` is sorted by `effect_id`** — the manager uses an apparent sorted map in memory, but whether the file must be pre-sorted or the manager sorts on load is unconfirmed.
6. **`effect_id` uniqueness** — observed samples show two filenames mapping to the same `effect_id`; the resolution rule (last-wins, first-wins, or alias table) is unknown.
7. **Element record actual byte budget** — the stride of 104 bytes is derived from the allocation formula; the exact mapping of all 26 dwords within that struct to the variable-length file reads has not been byte-verified.

## From `.eff` effect-object

1. **Parser function for `.eff` obj files** — no dedicated loading function was identified in the client by string reference. The file may be read by a shared binary-mesh routine not easily located. Parser behavior for malformed files (mismatched `vert_count`, wrong file size) is unknown.
2. **Maximum `index_count` / `vert_count` limits** — a related mesh parser enforces a warning threshold of 3072 vertices; it is unknown whether the `.eff` obj loader has a similar limit.
3. **Coordinate system handedness** — the `rect.eff` sample has all normals pointing in the −X direction; whether the engine uses right-handed or left-handed world space for effect volumes is not determined from these files alone.
4. **`tex_u` / `tex_v` purpose** — UV coordinates are present but whether the effect pipeline actually binds a texture to these shapes or the UV data is unused baked output is unknown.
5. **`particleEmitter.eff` in `data/sky/`** — this path also uses `.eff` and is confirmed to be a different layout. It has not been analyzed; it must not be parsed with this spec.

---

# Cross-References

- **Related formats:** `pak.md` (container), `sound_tables.md` (the other `.eff` variant — sound triggers), `mesh.md` (shares 32-byte vertex record convention), `terrain.md` (BUD path also uses 32-byte vertex layout)
- **Companion plain-text files:** `xeffect.lst`, `bmplist.lst`, `xobj.lst` — manifest/pool files loaded at boot
- **Companion ASCII format:** `.xobj` files in `data/effect/xobj/` — plain-text primitive meshes; not binary
- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
