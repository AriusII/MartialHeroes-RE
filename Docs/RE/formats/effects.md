# Format: .xeff / .eff  (visual-effects subsystem — particle emitters and primitive shapes)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Promoted from dirty-room notes under EU Software Directive 2009/24/EC Art. 6, solely to
> achieve interoperability. No decompiler output and no binary addresses appear below.
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
| `sample_verified` | **partial** — 32-byte file header VERIFIED against multiple real samples including byte-math proof on `331120721.xeff`; element body and sub-effect block layout CONFIRMED by sample byte-walkthrough; alpha/scale curve layout CONFIRMED; track header CONFIRMED; keyframe frame-0 special case CONFIRMED |
| Endianness | Little-endian throughout |
| Magic / signature | None — format identified by file extension and directory only |
| Anti-magic | If `effect_id` at offset 0 equals `0x46464558` the client treats the file as invalid |
| Field semantics | The six per-keyframe / per-static float parameters are now resolved (emission velocity Vec3 + billboard size Vec3); emitter type enum resolved for values 0/1/2; `resource_id` resolved as a resource selector; time units resolved as milliseconds. `field_unknown_a` remains unresolved. See per-field confidence below. |

**HEADER CONFLICT RESOLVED (2026-06-12):** The prior revision of this spec described an 8-byte file header (`effect_id` + `element_count`). Sample byte-walkthrough of real `.xeff` files with non-zero element counts confirms the header is **32 bytes**, not 8. The extra 24 bytes contain a `type_flag` field, 16 bytes of zero padding, and a leading `entry_count` field for the first sub-effect block (at `0x1C`). The prior 8-byte description was based on zero-element stub samples where everything after `effect_id` and `element_count` read as zero; in those stubs, the 32-byte region still exists but the fields are meaningless. All parser engineers must treat the header as 32 bytes. See A.2 for the corrected layout.

### Section B — `.eff` effect-object shape (geometry primitive)

| Item | Value |
|------|-------|
| `sample_verified` | **true** — all fields verified against three real samples (`cone.eff`, `rect.eff`, `tringle.eff`); size formula exact for all three |
| Endianness | Little-endian throughout |
| Magic / signature | None — identified by directory path (`data/effect/obj/`) only |

### Section C — Effects runtime (instantiation, particle update, attach, network triggers)

| Item | Value |
|------|-------|
| `sample_verified` | **n/a — behavioral model.** Derived from runtime analysis of an already-named handler/dispatch set plus real `.xeff` sample files. Not a byte format. Confidence is stated per subsection. The model is consistent across all spawn, bind, and tick paths examined; the unresolved items are listed in the open-questions block. |
| Time base | Milliseconds (engine wall clock; see C.1) |

### Section D — `effectscale.xdb` (per-effect scale-override table)

| Item | Value |
|------|-------|
| `sample_verified` | **true** — byte-verified against one 16-byte sample (two records); size formula exact |
| Endianness | Little-endian throughout |

### Section E — `particleEmitter.eff` (GPU particle emitter descriptor table)

| Item | Value |
|------|-------|
| `sample_verified` | **PLAUSIBLE** — file header uint16 and record stride hypothesis derived from sample byte observation; internal field layout is UNVERIFIED |
| Endianness | Little-endian |
| Note | This is a distinct `.eff` sub-type at `data/effect/particle/particleEmitter.eff`; must NOT be parsed with the Section B geometry parser |

---

## Disambiguation: the `.eff` extension is overloaded

The client uses the `.eff` extension for at least three unrelated formats. Parsers **must** dispatch by directory, not by magic:

| Path pattern | Format | Spec location |
|---|---|---|
| `data/effect/obj/*.eff` | Effect-object geometry shape (Section B below) | This file |
| `data/effect/xeff/*.xeff` | Particle effect descriptor (Section A below) | This file |
| `tool/sound/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/map*/soundtable*.eff` | Sound-trigger event table | `sound_tables.md` |
| `data/effect/particle/particleEmitter.eff` | GPU particle emitter definition (Section E below) | This file |

---

# Section A: `.xeff` — Particle Effect Descriptor

## A.1 Identification

- **Extension:** `.xeff`
- **Found in:** `.pak` archive; logical path `data/effect/xeff/<name>.xeff`
- **Magic / signature:** None present
- **Anti-magic:** `effect_id` (bytes 0–3) must NOT equal `0x46464558` (the ASCII string `XEFF` in little-endian byte order); the loader treats that value as a corrupt-file sentinel and aborts
- **Version field:** None
- **Endianness:** Little-endian
- **Discovery:** The effect manager reads a manifest (`data/effect/xeffect.lst`, Section A.5) at boot to register all known `.xeff` paths; individual files are parsed lazily on first spawn (confirmed against the runtime registry — see C.2)
- **VFS census (SAMPLE-VERIFIED):** 3,584 files total; all under `data/effect/xeff/`. Total uncompressed size approximately 124 MB. Average file size ~34 KB. Of these, 8 are stubs (`sub_effect_count` = 0) and 3,576 are non-empty. 984 files have 9-digit numeric stems; 2,600 have non-numeric names. Across the full corpus, 47 distinct `effect_id` values are shared by more than one file (duplicated ids).

## A.2 File Header

**Confidence: VERIFIED** (corrected from prior 8-byte description; byte-math verified on `331120721.xeff` — 621 bytes total with no residual; multiple additional samples cross-checked)

The file header is **32 bytes** (0x20).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `effect_id` | Numeric identifier for this effect. Must not equal `0x46464558`. For numeric-named files the value matches the decimal filename (e.g. `331110711.xeff` → id = 331110711); SAMPLE-VERIFIED on 5 files. Across the full corpus, 47 `effect_id` values are shared by more than one file (SAMPLE-VERIFIED); resolution rule when loading is UNRESOLVED — see Open Questions. |
| 0x04 | 4 | u32 LE | `sub_effect_count` | Number of sub-effect blocks in the file. Observed range: 1 to 16. Zero is valid (stub/empty effect); 8 stub files observed in the full VFS. Formerly labelled `element_count` in the prior 8-byte spec — semantically the same field. |
| 0x08 | 4 | u32 LE | `type_flag` | Observed values: 1 and 2. Possible emitter class (1 = particle/skill, 2 = environment). Semantics UNVERIFIED; do not branch on this value until confirmed. |
| 0x0C | 16 | u8[16] | `reserved` | Zero in all samples. Padding / reserved; read and discard. |
| 0x1C | 4 | u32 LE | `first_entry_count` | Entry count for the first sub-effect block's name table. This is the `entry_count` field that also opens the first sub-effect data block immediately after the header. It is present in the header as a convenience; parsers that read the sub-effect blocks sequentially will encounter it again at the start of block 0. Observed range: 1–41. SAMPLE-VERIFIED. |

Immediately after the 32-byte header, `sub_effect_count` sub-effect blocks follow sequentially. There is no additional directory or offset table.

**File-size formula (single sub-effect, N entries):**

```
32                           header
+ N × 64                     name table
+ (4 + N × 4)               curve 1 (alpha)
+ (4 + curve2_count × 4)    curve 2 (scale_x); count may differ from N
+ (4 + curve3_count × 4)    curve 3 (scale_y)
+ (4 + curve4_count × 4)    curve 4 (scale_z)
+ 13                         track header
+ 9 × 4                      frame 0 (no index prefix)
+ (N − 1) × (4 + 9 × 4)    frames 1 to N-1 (each has a u32 index prefix)
```

**Byte-math verification:** `331120721.xeff` (1 sub-effect, N=5 entries, 621 bytes) was fully parsed with zero residual bytes. See A.3 for the sub-effect block layout.

## A.3 Naming Convention for Skill Effects

Numeric-named `.xeff` files (984 of 3,584 by filename) follow a 9-digit scheme `[CCC][SSS][AB][N]`. Of these 984 files, the effect_ids span 940 unique values; the remaining 44 files carry duplicated ids (contributing to the corpus-wide total of 47 duplicate effect_ids). SAMPLE-VERIFIED from full-corpus census.

| Digit group | Width | Meaning | Observed values |
|---|---|---|---|
| `CCC` | 3 | Character class identifier | 311, 331–334, 341, 343–346, 350, 352, 360, 361, 371, 380, 390 |
| `SSS` | 3 | Skill group within class | 110, 120, 130, 310, 320, 330, and others |
| `A` | 1 | Animation sequence group | 7 = skill-cast (observed) |
| `B` | 1 | Effect variant | 1 = primary, 2 = secondary |
| `N` | 1 | Sub-effect index | 1 or 2 |

The 17 observed `[CCC]` prefix values span the ranges: 311 (PC class A), 331–334 (PC classes B–E), 341 and 343–346 (PC classes F–J), 350 and 352 (PC classes K–L), 360–361 (mob/generic), 371 (PvP), 380 and 390 (additional classes). This aligns with the hard-coded trigger effect IDs: the `31xxxxxxx` range covers PC/character effects, `35xxxxxxx` general combat/hit effects, `36xxxxxxx` mob effects, `37xxxxxxx` PvP effects. Named (non-numeric) files (2,600 of 3,584) are environment, map, mob, and item effects; top name prefixes include `mo`, `mob`, `ef`, `do`, `spear`, `bow`, `bisu`. The digit-group decomposition is PLAUSIBLE from pattern observation; no manifest confirms the semantic meaning of each group.

## A.4 Sub-Effect Block Structure

**Confidence: CONFIRMED by sample byte-walkthrough.** Each sub-effect block consists of four sequential parts: a name table, a curve section (four passes), a track header, and a keyframe array. The block begins with its own `entry_count` u32.

### A.4.1 Name table

`entry_count × 64` bytes. Each slot is a 64-byte null-padded ASCII base name (texture or mesh name, no path prefix, no extension). Slot `i` names the texture for keyframe `i`. The client resolves the full path as `data/effect/texture/<name>.tga`. Korean names are CP949-encoded.

| Field | Size per slot | Type | Notes |
|---|---|---|---|
| `tex_name[i]` | 64 | char[64] | Null-padded; 64 bytes total per entry including null bytes |

### A.4.2 Curve section (four passes)

Follows the name table. Contains exactly four consecutive float-curve arrays, each prefixed by its own count:

| Pass | Semantic | Count source | Notes |
|------|----------|--------------|-------|
| 1 | Alpha channel | own u32 prefix | CONFIRMED: values in `[0, 1]`; stored as `1.0 − opacity` (see A.6) |
| 2 | Scale X channel | own u32 prefix | CONFIRMED: constant-zero pass observed |
| 3 | Scale Y channel | own u32 prefix | CONFIRMED: opacity fall-off values observed |
| 4 | Scale Z channel | own u32 prefix | CONFIRMED: scale ramp values observed |

Each pass reads `u32 curve_count` then `curve_count × f32`. The four passes map to: `alpha_key_count + alpha_key[i]`, then scale channels X/Y/Z (matching the legacy Group C/D parser specification). The per-pass count may differ from `entry_count`; the scale vector is resized to the largest of the three scale counts, with shorter passes zero-padded.

### A.4.3 Track header (13 bytes, fixed)

Follows the curve section.

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---:|------|-------|-------|-----------|
| +0 | 1 | u8 | `anim_loop` | Non-zero enables animated (multi-keyframe) path. Observed value: `0x01` in all samples. | CONFIRMED |
| +1 | 4 | u32 LE | `unknown_constant` | Value `67` (0x43) observed in all samples. Not identified in prior parser analysis. Candidates: version tag, sub-effect class id, or rendering pipeline selector. Do NOT assign a meaning. | SAMPLE-VERIFIED (value), semantics UNRESOLVED |
| +5 | 4 | u32 LE | `anim_stride` | Duration of one animation frame in milliseconds. Observed: 469 ms, 871 ms. | CONFIRMED |
| +9 | 4 | u32 LE | `anim_base_time` | Base time offset in milliseconds. Observed value: 0 in all samples. | CONFIRMED |

Derived value (not in file): `total_time = entry_count × anim_stride + anim_base_time` (milliseconds). The loader stores this in the in-memory descriptor.

**Note on `unknown_constant = 67`:** the prior parser analysis described a 9-byte Group E reading `(u8 anim_loop, u32 anim_stride, u32 anim_base_time)` = 9 bytes, consistent with 1+4+4 = 9. Sample observation confirms the same 9-byte count with an extra u32 value at position +1. The reconciled reading is that the prior analysis skipped documenting this field because its role was not traced. The field physically exists in the file and must be consumed.

### A.4.4 Keyframe array

Follows the track header. Contains `entry_count` keyframe entries.

**Frame 0 is a special case:** it has NO index prefix — only 9 × f32 (36 bytes). This is CONFIRMED by sample byte-math; the prior spec did not state this explicitly.

**Frames 1 through `entry_count − 1`:** each has `u32 kf_index` + 9 × f32 = 40 bytes.

The 9-float layout per frame is (in file order):

| Position | Type | Field | Notes |
|---|---|---|---|
| 1 | f32 | `velocity_x` | Emission velocity / displacement X (see A.8) |
| 2 | f32 | `velocity_y` | Emission velocity / displacement Y |
| 3 | f32 | `velocity_z` | Emission velocity / displacement Z |
| 4 | f32 | `size_x` | Billboard / particle size X |
| 5 | f32 | `size_y` | Billboard / particle size Y |
| 6 | f32 | `size_z` | Billboard / particle size Z |
| 7 | f32 | `kf_rot_x_deg` | Euler X rotation in degrees; converted to quaternion at load |
| 8 | f32 | `kf_rot_y_deg` | Euler Y rotation in degrees |
| 9 | f32 | `kf_rot_z_deg` | Euler Z rotation in degrees |

**Confidence: CONFIRMED** for the frame-0 no-index rule and the 9-float count; **SAMPLE-VERIFIED (partial)** for individual field semantics.

### A.4.5 Multi-sub-effect files

For `sub_effect_count > 1`, sub-effects follow sequentially. Each subsequent sub-effect begins with its own `entry_count` u32, then its name table and curve+track+keyframe data. Sub-effect boundaries are identified purely by sequential parsing; there is no offset table.

## A.5 In-Memory Element Layout (104 bytes, 26 dwords)

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED (no element-bearing sample verification)**

This is the in-memory layout the loader builds; it is provided so an engineer understands the runtime's view. **File read order differs from in-memory dword order** for `tex_count` and `field_unknown_a` (the loader writes them in reverse relative to file order). On disk, fields appear strictly in the read order given in A.4.

| Dword | Byte offset | Type | Field | Source / Notes |
|---|---|---|---|---|
| [0] | +0x00 | u32 | `emitter_type` | from element's emitter class |
| [1] | +0x04 | u32 | `resource_id` | < 10000 = mesh index; ≥ 10000 = particle id |
| [2] | +0x08 | u8 bool | `anim_flag` | low byte; upper 3 bytes unused |
| [3] | +0x0C | u32 | `field_unknown_a` (`element_flags`) | UNRESOLVED; no semantic read-site found |
| [4] | +0x10 | u32 | `tex_count` | drives keyframe count; low byte also tested as UV-scroll flags (A.10) |
| [5..7] | +0x14..+0x1C | ptr×3 | texture-handle vector | resolved texture handles (no file bytes) |
| [8] | +0x20 | u32 | `alpha_key_count` | curve-pass-1 count |
| [9..11] | +0x24..+0x2C | ptr×3 | alpha-key vector (f32) | stored as `1.0 − file_value` |
| [12] | +0x30 | u32 | (constructor-zeroed; no confirmed write) | likely padding/reserved |
| [13] | +0x34 | u32 | `scale_key_count` | high-water mark of the three scale-curve counts |
| [14..16] | +0x38..+0x40 | ptr×3 | scale-Vec3 vector | curve passes 2/3/4 triplets |
| [17] | +0x44 | u32 | (constructor-zeroed; no confirmed write) | likely padding |
| [18] | +0x48 | u32 | `anim_base_time` | track header field, milliseconds |
| [19] | +0x4C | u32 | `start_time_or_total` | DUAL USE: parser writes derived `total_time`; at runtime overwritten with spawn timestamp |
| [20] | +0x50 | u32 | `anim_stride` | track header field, milliseconds per frame |
| [21] | +0x54 | u32 | `total_time` | derived = `tex_count × anim_stride + anim_base_time` |
| [22..24] | +0x58..+0x60 | ptr×3 | keyframe / static-state vector | Branch A (44-byte) or Branch B (48-byte) entries |
| [25] | +0x64 | u32 | (constructor-zeroed) | likely padding |

## A.6 Alpha Inversion Convention

Alpha values are stored **inverted**: file value `0.0` means fully opaque; `1.0` means fully transparent. The loader applies `in_memory_value = 1.0 − file_value`. At render time opacity multiplied by 255 gives the per-vertex alpha byte. Confidence: CONFIRMED.

## A.7 Rotation Encoding Note

All rotation values are stored in **degrees** and converted to quaternion form during load. The conversion uses `π / 180`, then standard half-angle Euler-XYZ decomposition. Parsers must store the computed quaternion, not the degree values. The rotation quaternion is initialized to identity `(0, 0, 0, 1)` before conversion.

## A.8 Resolved Semantics of the Six Float Parameters

The six floats form two 3-component vectors. Both are HIGH confidence (parser layout + runtime use), SAMPLE-VERIFIED (partial):

- **`velocity` (`velocity_x/y/z`):** displacement direction-plus-magnitude from the effect origin. At render time it is rotated by the effect instance's world orientation quaternion, scaled by the instance's effective scale, and added to the world-space origin to produce the particle's world position.
- **`size` (`size_x/y/z`):** billboard/particle dimensions in world units. For billboard types, `size_x`/`size_y` produce the quad half-extents (multiplied by ±0.5). For mesh type, all three components scale the mesh vertex axes.

## A.9 Companion Manifest: `xeffect.lst`

**Confidence: HIGH**

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | Number of effect name records |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte record per effect |

**Name record (30 bytes, zero-padded):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] | `name` | Base filename without path prefix and without `.xeff` extension. Full path: `data/effect/xeff/<name>.xeff`. CP949 for Korean. |

## A.10 Companion: `bmplist.lst` — Texture Name Pool

**Confidence: VERIFIED** (45,784 bytes = 4 + 1526 × 30)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | 1526 in observed sample |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte name record per texture slot |

**Name record (30 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] | `name` | Base name, no extension. Full path: `data/effect/texture/<name>.tga`. CP949 for Korean. |

## A.11 Companion: `.xobj` — ASCII Primitive Meshes

**Confidence: CONFIRMED** (3 sample files verified)

These files in `data/effect/xobj/` are **plain text** (CRLF line endings), not binary. Each file begins with integer line counts followed by tab-separated floating-point vertex/UV data rows. They define the emitter shape referenced by `emitter_type == 1` through the shared mesh table (selected by `resource_id`). Parsers for `.xobj` must use a text reader.

## A.12 Enumerations / Flags

### `emitter_type` (u32 in element in-memory layout)

| Value | Meaning | Confidence |
|------:|---------|------------|
| `0` | Billboard sprite — flat camera-facing quad | CONFIRMED |
| `1` | Mesh-particle object — per-vertex transform of shared mesh | CONFIRMED |
| `2` | Directional billboard — camera-facing quad with fixed 90° Y pre-rotation; reads extra rotation in static-state branch | CONFIRMED |
| other | Behavior undetermined | UNRESOLVED |

## A.13 UV-Scroll Render Flags via `tex_count` Low Byte (MEDIUM confidence)

At runtime, bits 0 and 1 of the low byte of the in-memory `tex_count` dword are tested as UV-scroll flags:
- bit 0 set → scroll U by `phase_ms mod 5000 / 5000.0` (5-second loop)
- bit 1 set → scroll V by the same ratio

This appears to be an intentional dual use of the frame-count field; intent unverified. Confidence: MEDIUM.

## A.14 Named Constants

| Name | Value | Context |
|------|------:|---------|
| `XEFF_HEADER_SIZE` | 32 (0x20) | File header byte count (CORRECTED from prior 8-byte value) |
| `XEFF_ELEMENT_STRIDE` | 104 (0x68) | In-memory element record size |
| `XEFF_KEYFRAME_STRIDE` | 44 (0x2C) | In-memory keyframe entry size (animated branch) |
| `XEFF_STATIC_STRIDE` | 48 (0x30) | In-memory static-state allocation |
| `XEFF_TEX_NAME_LEN` | 64 (0x40) | Bytes per texture name entry in name table |
| `XEFF_LST_NAME_LEN` | 30 (0x1E) | Bytes per record in `xeffect.lst` and `bmplist.lst` |
| `XEFF_TRACK_HEADER_SIZE` | 13 | Bytes: 1 (anim_loop) + 4 (unknown_constant) + 4 (anim_stride) + 4 (anim_base_time) |
| `XEFF_EMITTER_BILLBOARD` | 0 | `emitter_type`: flat billboard sprite |
| `XEFF_EMITTER_MESH` | 1 | `emitter_type`: mesh-particle object |
| `XEFF_EMITTER_DIRECTIONAL` | 2 | `emitter_type`: directional billboard |
| `XEFF_RESOURCE_PARTICLE_THRESHOLD` | 10000 | `resource_id ≥ this` → GPU particle descriptor id |
| `XEFF_UV_SCROLL_PERIOD_MS` | 5000 | Loop period for UV-scroll flags |
| `XEFF_INVALID_MAGIC` | 0x46464558 | `effect_id` sentinel; file invalid if header equals this |
| `XEFF_TIME_UNIT` | milliseconds | Engine wall-clock unit for all timing fields |
| `XEFF_TRACK_UNKNOWN_CONSTANT` | 67 (0x43) | Constant u32 at track header offset +1; purpose UNRESOLVED |

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
| 0x00 | 4 | u32 LE | `index_count` | Total number of u16 index values stored. Always divisible by 3 — pure indexed triangle list. |
| 0x04 | `index_count × 2` | u16 LE[] | `indices[]` | Flat vertex-index array; consecutive groups of three form triangles. |

## B.4 Vertex Section

Immediately follows the last index byte at file offset `4 + (index_count × 2)`. May be at a non-4-byte-aligned address. No padding is inserted.

**Confidence: VERIFIED**

| Offset from section start | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 4 | u32 LE | `vert_count` | Number of 32-byte vertex records that follow |
| +4 | `vert_count × 32` | VertexRecord[] | `verts[]` | One record per vertex |

### B.4.1 VertexRecord (32 bytes, stride = 0x20)

**Confidence: VERIFIED**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 4 | f32 LE | `pos_x` | World-space X |
| +4 | 4 | f32 LE | `pos_y` | World-space Y |
| +8 | 4 | f32 LE | `pos_z` | World-space Z |
| +12 | 4 | f32 LE | `normal_x` | Vertex normal X |
| +16 | 4 | f32 LE | `normal_y` | Vertex normal Y |
| +20 | 4 | f32 LE | `normal_z` | Vertex normal Z |
| +24 | 4 | f32 LE | `tex_u` | Texture U, range 0–1 in observed data |
| +28 | 4 | f32 LE | `tex_v` | Texture V, range 0–1 in observed data |

## B.5 Normal Encoding Note

In `cone.eff` and `rect.eff` all vertex normals are unit vectors. In `tringle.eff` the normal bytes are bit-for-bit identical to the position bytes (non-unit magnitude ~1.16) — assessed as an exporter bug. Parsers must tolerate non-unit normals without aborting.

## B.6 Sample Measurements

| File | File size | `index_count` | `vert_count` |
|------|----------:|---:|---:|
| `cone.eff` | 496 B | 36 | 13 |
| `rect.eff` | 148 B | 6 | 4 |
| `tringle.eff` | 110 B | 3 | 3 |

---

# Section C: Effects Runtime — Instantiation, Update, Attachment, Triggers

> Behavioral model, not a byte format. Tells an engineer how a parsed `.xeff` descriptor becomes
> a live, ticking, on-screen effect, how it attaches to actors and bones, how it expires and is
> culled, and which network messages cause one to spawn. This section is the file-format
> companion to `specs/effects.md`, which contains the full runtime system specification including
> the boot sequence, object pools, trigger dispatch table, and per-frame tick math.

## C.1 Time base

**Confidence: CONFIRMED.** All effect timing uses the engine's millisecond wall clock. Every spawn path records the clock value at spawn; every tick subtracts that from the current clock to get elapsed milliseconds. All `.xeff` timing fields (`anim_stride`, `anim_base_time`, derived `total_time`) and per-instance expiry deadlines are in **milliseconds**.

## C.2 Descriptor registry and lazy load

**Confidence: CONFIRMED.**

- A single global **effect registry** holds parsed descriptors keyed by numeric `effect_id` (a sorted map). Populated at boot by reading `xeffect.lst`, `bmplist.lst`, and the texture directory.
- **Lazy load:** a spawn request looks up the descriptor by `effect_id`. If the descriptor's "loaded" flag is clear, the registry parses the `.xeff` file on the spot, resolves textures, stamps the descriptor as loaded. Failed lookup abandons the spawn.

For the complete boot loading sequence, object pools, and spawn factory details, see `specs/effects.md §3` (Boot Loading) and `specs/effects.md §4` (Pool Allocation).

## C.3 Instance memory layout (runtime, shared across the XEffect family)

**Confidence: CONFIRMED for the marked fields.** Offsets are within the live instance object, not on-disk format. Full spawn pathway details are in `specs/effects.md §5`.

| Offset | Type | Field (neutral) | Confidence |
|---:|---|---|---|
| +0x00 | ptr | vtable / class identity | CONFIRMED |
| +0x08 | u8 | active flag | CONFIRMED |
| +0x0C | ptr | descriptor pointer (0 ⇒ invalid ⇒ discard before active list) | CONFIRMED |
| +0x10..+0x1B | Vec3 f32 | world position | CONFIRMED |
| +0x1C..+0x2B | Quat f32 | world orientation | CONFIRMED |
| +0x3C | u8 | loop flag | CONFIRMED |
| +0x40 | u32 | expiry timestamp = spawn_clock + lifetime_ms | CONFIRMED |
| +0x48 | f32 | effective scale = descriptor base-scale × caller scale | CONFIRMED |
| +0x50 | u32/f32 | time-scale / playback-speed factor | CONFIRMED |
| +0x54 | u32 | bound actor sort id (actor-anchored and bone variants) | CONFIRMED |
| +0x5C | u8 | actor sub-id (bone variant) | CONFIRMED |
| +0x5D | u8 | bone-source mode: 0 = explicit bone id; 1, 2 = action table | HIGH |
| +0x60 | u32 | bone id / hint (bone variant) | CONFIRMED |
| +0x64 | u8 | anchor mode: 1 = bone orientation; 2 = actor-root orientation | HIGH |

## C.4 Spawn pathways

**Confidence: CONFIRMED.** Three instance kinds share the descriptor registry:

| Instance kind | Transform source |
|---|---|
| Actor-anchored, timed | Follows a target actor for a fixed duration |
| World-placed | Fixed world position + orientation quaternion at spawn |
| Bone-attached | Resolves a skeleton bone's world transform every tick |

## C.5 Per-frame tick (particle update / emit / draw)

**Confidence: HIGH.** Each kind's active list is walked each frame; for each live instance:

1. Gate: skip if active flag is clear.
2. First-tick init: allocate per-element runtime particle buffers. Elements with `resource_id ≥ 10000` also bridge to the separate GPU particle subsystem (Section E).
3. Elapsed phase: `elapsed = now − start`; multiply by time-scale; take modulo the loop period.
4. Proximity cull: if the squared distance to the local player exceeds the render-distance threshold, skip this instance's geometry build.
5. Element loop: select the active keyframe, compute per-vertex RGBA, build geometry by `emitter_type` (billboard quad for types 0/2; mesh transform for type 1). The `velocity` Vec3 contributes the per-particle displacement (rotated by instance orientation quaternion, scaled, added to world origin). The `size` Vec3 drives billboard half-extents or mesh-axis scaling. UV-scroll flags (A.13) offset texture coordinates.
6. Submit geometry for drawing.

See `specs/effects.md §8` for the full tick math including elapsed-to-frame-index computation, keyframe interpolation, and UV scroll formula.

## C.6 Bone attachment

**Confidence: HIGH.** See `specs/effects.md §9` for full bone-attachment field layout. Per-tick:

1. Look up the bound actor; skip if gone.
2. Fetch the actor's current animation pose at the target bone (bone-source mode 0 = direct bone id; modes 1/2 = action-table lookup, UNCONFIRMED).
3. Write the bone's world position and orientation into the instance; then run the standard particle build.

## C.7 Network triggers

For the full trigger dispatch table (all hard-coded effect IDs, network handler mappings, and the visual-cycle trigger path), see `specs/effects.md §7`. The format-file records two network payload layouts for completeness:

### C.7.1 Attack/hit effect — opcode group 5/139

Fixed 12-byte payload:

| Offset | Size | Type | Field |
|---:|---:|------|-------|
| +0x00 | 4 | u32 LE | source sort id |
| +0x04 | 4 | u32 LE | source actor id |
| +0x08 | 4 | u32 LE | attack/effect id |

### C.7.2 Combat-effect instance spawn — opcode group 5/14

Fixed 48-byte payload (key fields):

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

---

# Section D: `effectscale.xdb` — Per-Effect Scale Override Table

**Confidence: VERIFIED** — byte-exact against one 16-byte, two-record sample; size formula exact.

- **Path:** `data/script/effectscale.xdb`
- **Endianness:** Little-endian
- **Magic / signature:** None
- **Layout:** flat array of fixed 8-byte records, no header, no count prefix; record count = `file_size / 8`

**Record (8 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0x00 | 4 | u32 LE | `effect_id` | Matches a `.xeff` `effect_id` (Section A.2) |
| +0x04 | 4 | f32 LE | `scale` | Scale multiplier for that effect |

Observed sample records: `(353100201 → 3.0)`, `(353100202 → 2.0)`.

This is a per-effect-id scale-override table that augments the descriptor base-scale. Whether the runtime multiplies it in addition to, or instead of, the descriptor base-scale is unconfirmed (application site not isolated).

---

# Section E: `particleEmitter.eff` — GPU Particle Emitter Descriptor Table

**Confidence: PLAUSIBLE** (file-level header byte SAMPLE-VERIFIED; internal record layout is UNVERIFIED)

## E.1 Identification

- **Path:** `data/effect/particle/particleEmitter.eff`
- **Magic:** None
- **Endianness:** Little-endian
- **File size:** 116,652 bytes (observed)
- **Disambiguation:** This is NOT the same format as the Section B geometry `.eff` files. Must be dispatched by directory path, not extension alone.

## E.2 File layout

**Header (at file offset 0x00):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 2 | u16 LE | `record_count` | Observed value: 10,001 (0x2711) | SAMPLE-VERIFIED |
| 0x02 | 2 | u16 LE | _unknown_ | Observed value; purpose not confirmed | UNVERIFIED |

**Record array:** begins at offset 0x20 (hypothesis). Stride is **48 bytes** per record (PLAUSIBLE from byte pattern observation; not fully confirmed). Each record encodes one particle-type descriptor (`emit_file` entry): texture handle, particle count, and a particle info pointer. At runtime, `resource_id ≥ 10000` in a `.xeff` element selects a record from this table by its index.

The exact field layout within each 48-byte record is **UNVERIFIED**. The following fields are hypothesized from parser analysis:

| Slot | Type | Field | Confidence |
|---|---|---|---|
| texture handle | ptr or u32 | resolved DDS/TGA texture | UNVERIFIED |
| particle_count | u32 | number of particles to emit | UNVERIFIED |
| particle_info | ptr | pointer to a particle descriptor sub-record | UNVERIFIED |
| remaining ~28 bytes | unknown | pre-baked state or padding | UNVERIFIED |

**Do not implement a parser for this format until the field layout is confirmed** from an IDA trace of the particle-system constructor arguments.

## E.3 Cross-reference

This file feeds the separate GPU particle sub-system (`ParticleEffectManager`). For the runtime behavior of that sub-system (boot load sequence, spawn validation, `GParticleBuffer` geometry), see `specs/effects.md §11`.

---

## Terrain FX Layer Formats — Cross-Format Deltas

The per-cell terrain layer mesh files (`.fx1` through `.fx7`) are documented in full in
`Docs/RE/formats/terrain_layers.md §Section 1`. That file is authoritative for the FX format
byte layouts; do NOT duplicate its tables here.

This section records only **new sample observations** from the June 2026 black-box pass that
extend or correct the `terrain_layers.md` record:

### FX2 Header Field[3]: CONFLICT FLAGGED

`terrain_layers.md §1.6` states that the `render_state` field at header offset `0x0C` has value
`15 (0x0F)` for FX2, identical to FX1. The June 2026 black-box pass observed **value 50** in
one FX2 sample (`d001x9990z10000.fx2`). These two values contradict each other.

Resolution status: **UNRESOLVED**. Possible explanations:
- The field is variable (encodes a per-file texture-slot or render-mode index) and was
  coincidentally 15 in the IDA-analyzed sample.
- The IDA sample and the black-box sample come from different map areas with different rendering setups.
- The field meaning is different from `render_state` (it may be a texture ID, not a constant).

Engineering guidance: do NOT treat this field as a constant 15. Read and expose it; do not branch on its value until the conflict is resolved.

### FX4: Confirmed Structurally Identical to FX3/FX5

`terrain_layers.md §1.10` lists FX4 as `sample_verified: false` with "UNKNOWN" vertex format.

The June 2026 black-box pass analyzed the single known FX4 file (`d001x9997z10002.fx4`, 1,076 bytes) and found:
- Header matches the FX3/FX5 48-byte pattern (same constant field values at the same positions).
- Unit-magnitude normal vector confirmed at header offset `0x10`.
- World-position floats confirmed at body offset `0x30`.
- Vertex format is **VF_36** (same as FX3 and FX5) — SAMPLE-VERIFIED.

**Update `terrain_layers.md` recommendation:** FX4 should be marked `sample_verified: true` for its header and vertex format. Only one instance exists in the VFS (map001, one cell), so this is a low-prevalence format variant.

### FX7: Confirmed Distinct Header Layout

`terrain_layers.md §1.10` lists FX7 as `sample_verified: false` with "UNKNOWN" format.

The June 2026 black-box pass analyzed both known FX7 files (both exactly 35,202 bytes and byte-identical). Key observations:
- The header does **not** follow the FX1–FX5 pattern. World-space position floats appear at header byte **0x08** (not at the offset used by FX3/FX5).
- A pair of unit-magnitude floats appears at offsets `0x14` and `0x1C` (possible dual normal or orientation pair).
- Both files are byte-identical — consistent with a shared template rather than per-cell content.

**Update `terrain_layers.md` recommendation:** FX7 has a **distinct header layout** from all other FX layers. The position/normal field offsets are confirmed different. Vertex format and full header field mapping are UNVERIFIED — a dedicated parser trace is needed.

---

# Shared: Known Unknowns and Open Questions

## From `.xeff` (Section A)

1. **`field_unknown_a` / `element_flags` (in-memory byte +0x0C)** — no semantic runtime read-site isolated. Confidence: LOW. Do not assign a meaning.
2. **`type_flag` (header offset 0x08)** — values 1 and 2 observed; semantics not confirmed.
3. **`unknown_constant = 67` in track header** — value confirmed in two samples; purpose UNRESOLVED. Do not branch on it.
4. **`emitter_type` values beyond 0, 1, 2** — behavior and conditional reads unknown.
5. **`tex_count` low byte as UV-scroll flags (A.13)** — MEDIUM confidence; dual use of frame count as flags is unusual.
6. **In-memory dword[19] dual use (`start_time_or_total`)** — parser writes `total_time`; tick reads it as the spawn timestamp. Likely overwritten at spawn; UNCONFIRMED.
7. **`effect_id` duplicate resolution** — 47 `effect_id` values are shared by more than one file (SAMPLE-VERIFIED). The resolution rule when two files in the sorted map share an id is UNRESOLVED. The second-registered file may silently overwrite the first; no tie-break logic was traced.
8. **9-digit naming scheme `[CCC][SSS][AB][N]`** — PLAUSIBLE from pattern observation; no manifest confirms the digit-group semantics.
9. **Frame-0 no-index rule** — CONFIRMED in samples; whether the parser has a conditional or always reads N×(index+9f) starting from frame 1 while treating frame 0 as pure 9f is not traced from the code path.

## From `.eff` geometry (Section B)

1. **Parser function for `.eff` obj files** — no dedicated loading function identified by string reference.
2. **Maximum `vert_count` limits** — a related mesh parser warns above 3072 vertices; whether this parser enforces a similar limit is unknown.
3. **`tex_u` / `tex_v` purpose** — present, but whether the effect pipeline binds a texture to these shapes or UVs are unused is unknown.

## From the runtime (Section C)

1. **A fourth and possibly a sixth effect pool** — torn down at shutdown alongside the three known instance pools; class names not identified.
2. **Bone-source action tables (modes 1/2)** — layout and source (skill table? animation events?) unconfirmed.
3. **5/139 template fields and 5/14 `param1`/`param2`** — meaning not resolved.

## From `effectscale.xdb` (Section D)

1. **Application site** — which spawn step multiplies by it, and whether it stacks with or replaces descriptor base-scale, was not located.

## From `particleEmitter.eff` (Section E)

1. **Record layout** — the 48-byte stride is PLAUSIBLE but not confirmed; field offsets within each record are entirely UNVERIFIED.
2. **Record count vs `record_count` header field** — the `u16` value 10,001 may be a record count or an id-space size; no out-of-range check was traced.

## From terrain FX deltas

1. **FX2 field[3] conflict (15 vs 50)** — the discrepancy between the IDA-traced value (15) and the sample-observed value (50) is unresolved. Read the field but do not treat it as a constant.
2. **FX7 full field layout** — only the position/normal offset divergence from FX1–FX5 is confirmed; complete header and vertex format mapping awaits a parser trace.
3. **FX4 single-instance prevalence** — only one FX4 file exists in the full VFS (43,347 files). The reason for this is unknown; it may be a deprecated or experimental format.

---

# Cross-References

- **Related formats:** `pak.md` (container), `sound_tables.md` (the other `.eff` variant), `mesh.md` (shares 32-byte vertex record convention), `terrain_layers.md` (FX layer formats; authoritative for `.fx1`–`.fx7` byte layouts)
- **Related runtime spec:** `specs/effects.md` — the authoritative effects system behavioral spec (boot manifests, object pools, trigger dispatch table, per-frame tick math, bone attachment, damage-number renderer, sword-light sub-system)
- **Related specs:** `specs/combat.md` (server-authoritative damage; effects here are presentation-only), `specs/skinning.md` (bone hierarchy used by bone-attached effects)
- **Companion plain-text files:** `xeffect.lst`, `bmplist.lst`, `totalmugong.txt`, `itemjointeff.txt`, `mobjointeff.txt`, `itemswordlight.txt`, `mobswordlight.txt` — boot manifests (see `specs/effects.md §3`)
- **Companion ASCII format:** `.xobj` files in `data/effect/xobj/` — plain-text primitive meshes
- **Companion binary tables:** `effectscale.xdb` (Section D), `particleEmitter.eff` (Section E)
- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
