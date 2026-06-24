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

> **verification:** sample-verified (two-witness) — every load-bearing `.xeff` / `particleEmitter.eff` /
> `effectscale.xdb` claim re-confirmed against the live loader (control flow + operands) AND a byte-walk
> of real VFS entries; three `.xeff` files (1 / 11 / 68 sub-effect blocks) parse to RESIDUAL = 0, the
> particleEmitter walk reaches its `num_frames == 0` tail over 146 variable-length entries, and every
> manifest / `.xdb` size formula is byte-exact. The Section B `.eff` geometry shape and Section F
> link-tables remain at their prior sample-verified status (not re-walked this pass).
> **ida_reverified:** 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20); the `particleEmitter.eff` 52-byte sub-record ROLES resolved to CODE-CONFIRMED on 2026-06-21 (ASSET-FIDELITY, §E.2.2 / §E.2.4); a static re-walk of the `.xeff` body loader on 2026-06-21 re-confirmed every Section A claim and upgraded two mechanisms to CODE-CONFIRMED — the animated keyframe count is the element count of the already-built texture-handle vector (== `tex_count`, A.4.4), and the boot-path `xeffect.lst` reader reads the documented `u32 count` + 30-byte records end-to-end (A.9). No structural change. CYCLE 12 Block B (2026-06-22, IDB SHA 263bd994): weapon bone-slot attach confirmed — when `bone_name_mode = 1` (CATALOG mode), the AnimCatalog resolves bone ids for slots 902..905; which slot maps to which hand/weapon is DEFERRED (debugger-pending). Recorded in §A.16.2. CYCLE 12 Block C (2026-06-24, IDB SHA 263bd994): byte-walk of a 4th `.xeff` file (34,000 B, 8 blocks) and all 146 `particleEmitter.eff` entries both returned RESIDUAL = 0, independently re-confirming the structural models. ONE CORRECTION applied: the `xeffect.lst` 30-byte name record includes the `.xeff` extension (3625 of 3669 records end in `.xeff`); the loader concatenates `data/effect/xeff/` + record-name verbatim — it does NOT append an extension. The prior "without extension" claim (A.9) is corrected. `xobj.lst` (effect/xobj.lst, 32 records, 34-byte stride, 1,092 bytes) added to §A.11 as a newly confirmed structured companion list.
> **ida_anchor:** 263bd994
> **evidence:** [static-ida, vfs-sample]
> **conflicts:** NONE structural — the loader and the real sample contradict no committed claim on this
> build. Two carried items: (1) the on-disk `effectscale.xdb` sample on this build holds different
> *record values* than the values recorded in §D (a newer build's table) — the **layout is identical**,
> so this is NOT a conflict; (2) the `.fx` terrain-layer `type_tag == group_count` claim is OUT-OF-LANE
> (owned by `terrain_layers.md`) and was NOT re-walked here — see the Terrain FX section, status
> capture/debugger-pending → terrain.

---

## Status

### Section A — `.xeff` (particle effect descriptor)

| Item | Value |
|------|-------|
| `sample_verified` | **true (two-witness, build 263bd994, 2026-06-16)** — 8-byte file header, 24-byte element fixed head, name table, four-pass curve section, 9-byte track header, and uniform 40-byte keyframe stride ALL re-confirmed against the live body parser AND a byte-walk of `331120721.xeff` (621 B, 1 block), `zone_sel_u.xeff` (26,947 B, 11 blocks), and `char_select-u.xeff` (75,372 B, 68 blocks) — **all three parse to RESIDUAL = 0**. The earlier "partial" tag (header verified, body still being walked) is upgraded: the body is now byte-exact on three files spanning the full block-count range. |
| Endianness | Little-endian throughout |
| Magic / signature | None — format identified by file extension and directory only |
| Anti-magic | If `effect_id` at offset 0 equals `0x46464558` the client treats the file as invalid |
| Field semantics | The six per-keyframe / per-static float parameters are now resolved (emission velocity Vec3 + billboard size Vec3); emitter type enum resolved for values 0/1/2; `resource_id` resolved as a resource selector; time units resolved as milliseconds. `field_unknown_a` remains unresolved. See per-field confidence below. |

**HEADER CORRECTED (2026-06-14):** The file header is **8 bytes** (`effect_id` u32 + `sub_effect_count` u32). A prior revision wrongly described a 32-byte header by treating the leading fields of the first sub-effect block as header members. The value at file offset `0x08` is **not** a header field: it is the first element's `emitter_type` (the block starts immediately after the 8-byte header). All parser engineers must treat the header as 8 bytes and parse every sub-effect block — including block 0 — with the same element read sequence. See A.2 for the corrected layout and the Correction history note at the end of Section A.

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
| `loader_confirmed` | **SAMPLE-VERIFIED (two-witness, build 263bd994) — variable-length entry sequence** (loader read-order, byte-symmetric with the writer, AND a byte-walk of the real `particleEmitter.eff`, 116,652 B: **146 variable-length entries walked to a clean `num_frames == 0`/EOF tail with no desync**). The file is a SEQUENCE of variable-length entries — each a 28-byte entry header + (`num_frames` × 52-byte sub-record) + 64-byte trailing texture name — and the read loop terminates when an entry header's `num_frames` is 0 (or fewer than 28 bytes remain). There is NO file-level magic check (the first dword is data — `entry_id = 10001`, the value the old flat model misread as "magic 0x2711"). The earlier "16-byte header + 2,243 × 52-byte flat records" model is REFUTED (see E.0 Correction). |
| Endianness | Little-endian |
| Field semantics | Entry-header fields CONFIRMED/HIGH (`entry_id`, `num_frames` = live particle count, `sprite_size_x/y`, `max_particles`, trailing texture name). The 52-byte sub-record is **fully typed AND its roles are now CODE-CONFIRMED** (2026-06-21): 4 × u16 timers/size (`life_bonus`, `lifetime`, `spawn_delay`, `size_init`), an RGBA8 colour quad at +0x08, position xyz + `size_rate` (f32), four signed colour-rate i16, and velocity xyz + `velocity_damp` (f32). A sub-record is a per-particle spawn+Euler-integration descriptor (NOT a keyframe). See §E.2.2 / §E.2.4. |
| Note | This is a distinct `.eff` sub-type at `data/effect/particle/particleEmitter.eff` (VFS-lowercased `particleemitter.eff`); must NOT be parsed with the Section B geometry parser. Entry-record selection is by **raw `entry_id` equality** to a `.xeff` element's `resource_id` (no `−10000` subtraction) — see E.4 and A.4.0. |

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

**Confidence: VERIFIED** (byte-math verified on `331120721.xeff` — 621 bytes total with no residual; multiple additional samples cross-checked, including high-block-count files)

The file header is **8 bytes** (0x08). It is exactly two unsigned 32-bit fields; there is no
`type_flag`, no reserved padding, and no block-0 entry count in the header. Everything from file
offset `0x08` onward is the first sub-effect block, parsed by the element read sequence in A.4.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `effect_id` | Numeric identifier for this effect. Must not equal `0x46464558`. For numeric-named files the value matches the decimal filename (e.g. `331110711.xeff` → id = 331110711); SAMPLE-VERIFIED on 5 files. **The runtime keys on THIS header value, never on the decimal filename** — the matching numeric filename is an authoring convention, not re-derived at spawn (see C.2, the Option B verdict). Across the full corpus, 47 `effect_id` values are shared by more than one file (SAMPLE-VERIFIED); when two or more files share an id the rule is **FIRST-WINS** (CYCLE 7): the first file listed in `xeffect.lst` for that id is registered and kept, and every later same-id file is **rejected at insert** (logged as a "same effect" diagnostic, its freshly-built descriptor destroyed) and is unreachable at runtime — NOT last-wins, no override, no bucket, no composite key. See §C.2 and Open Question #7 (CLOSED). |
| 0x04 | 4 | u32 LE | `sub_effect_count` | Number of sub-effect blocks in the file. Observed range: 0 to 68 (`char_select-u.xeff` = 68). Zero is valid (stub/empty effect); 8 stub files observed in the full VFS. |

Immediately after the 8-byte header, `sub_effect_count` sub-effect blocks follow sequentially, each
parsed by the same element read sequence (A.4). There is no additional directory or offset table.

**RETIRED — the former `type_flag` field at offset `0x08`:** earlier revisions labelled the four
bytes at file offset `0x08` as a header `type_flag` taking values 1 or 2, and described a 16-byte
reserved region plus a `first_entry_count` at `0x1C`. That labelling is **wrong**. Those bytes are
the leading fields of sub-effect block 0 (which begins at `0x08`), and the value at `0x08` is that
block's `emitter_type` (A.4 / A.12): `1` = mesh-particle element, `2` = directional-billboard
element. It is a per-element category, not a file-level mode, and it is **not** a tagged union keyed
by a header tag. The bytes that were called `reserved`/`first_entry_count` are block 0's own
`resource_id`, `anim_flag`, element flags, and `tex_count`. The name `type_flag` is retired; use
element `emitter_type` (A.12). See the Correction history note at the end of Section A.

**File-size formula (single sub-effect, N entries):**

```
8                            header (effect_id + sub_effect_count)
+ element fixed head         see A.4 (emitter_type, resource_id, anim_flag, flags, dword, tex_count)
+ N × 64                     name table
+ (4 + N × 4)               curve 1 (alpha)
+ (4 + curve2_count × 4)    curve 2 (scale_x); count may differ from N
+ (4 + curve3_count × 4)    curve 3 (scale_y)
+ (4 + curve4_count × 4)    curve 4 (scale_z)
+ 9                          track header (anim_loop + anim_stride + anim_base_time)
+ N × (4 + 9 × 4)           N keyframes, each = u32 index prefix + 9 × f32 = 40 B (frame 0 included)
```

**Byte-math verification:** `331120721.xeff` (1 sub-effect, N=5 entries, 621 bytes) parses with zero
residual bytes under the 8-byte-header reading. The total file size is unchanged versus the old
32-byte-header reading — only the field LABELS for bytes `0x08`–`0x1F` differ — which is why the
prior residual check still passed. See A.4 for the sub-effect block layout.

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

**Confidence: CONFIRMED by sample byte-walkthrough (2026-06-14 revision).**

**Every** sub-effect block — including block 0 — is parsed by one and the same read sequence: a
24-byte **element fixed head**, then a variable body of four sequential parts (a name table of
`tex_count × 64` bytes, a curve section of four passes, a 9-byte track header, and a keyframe
array). Block 0 starts at file offset `0x08` (immediately after the 8-byte file header); block `k+1`
starts immediately where block `k` ended. There is **no** offset table, and there is **no** distinct
per-block prefix separate from the element's own fields — the bytes that an earlier revision called a
"24-byte block prefix" for blocks 1..N-1 are simply the first 24 bytes (the fixed head) of every
element. Block 0 is not special.

### A.4.0 Element fixed head (24 bytes)

These are the leading fields of every sub-effect block, read in this on-disk order. (See A.5 for how
they map to the in-memory dword order, which differs.)

| Element offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0x00 | 4 | u32 LE | `emitter_type` | 0 = billboard, 1 = mesh-particle, 2 = directional billboard (A.12). For block 0 this is the value at file offset `0x08` formerly mislabelled `type_flag`. |
| +0x04 | 4 | u32 LE | `resource_id` | Dispatch gate, NOT an index base: `< 10000` = direct index into the shared mesh table (24-byte stride, §A.11 / §E.4); `≥ 10000` = GPU particle id selected by **raw equality** (NO `−10000` subtraction) against the `particleEmitter.eff` entry map (Section E.4). |
| +0x08 | 4 | u32 LE | `anim_flag` | Consumed as a boolean (value ≠ 0) by the element constructor. |
| +0x0C | 4 | u32 LE | `field_unknown_a` | Bulk-read, not field-decoded; no read-site isolated. Keep as an opaque dword. Semantics **DBG-pending** (live-debugger confirmation required) — do NOT invent a meaning. |
| +0x10 | 4 | u32 LE | `element_dword2` | Written ahead of `tex_count` in the in-memory record (reverse of file order); bulk-read, not field-decoded. Keep as an opaque dword. Role beyond that ordering is **DBG-pending** (live-debugger confirmation required) — do NOT invent a meaning. |
| +0x14 | 4 | u32 LE | `tex_count` | This element's entry count; drives the name table, the keyframe count, and (low byte) the UV-scroll flags (A.13). For block 0 this is the value at file offset `0x1C` formerly mislabelled `first_entry_count`. Observed range across samples: 1–41. |

CONFIRMED by byte-walk of `zone_sel_u.xeff` (11 sub-effects, block-0 `tex_count` = 20),
`char_select-u.xeff` (68 sub-effects, block-0 `tex_count` = 2), and `331120721.xeff` (1 sub-effect,
`tex_count` = 5). Disproof of the old "reserved = zero" claim: `311100014.xeff` carries a non-zero
byte in the region the old layout called `reserved`, confirming that region is live element data, not
padding.

### A.4.1 Name table

`tex_count × 64` bytes (where `tex_count` is the element fixed-head field at element offset +0x14, A.4.0). Each slot is a 64-byte null-padded ASCII base name (texture or mesh name, no path prefix, no extension). Slot `i` names the texture for keyframe `i`. The client resolves the full path as `data/effect/texture/<name>.tga`. Korean names are CP949-encoded.

| Field | Size per slot | Type | Notes |
|---|---|---|---|
| `tex_name[i]` | 64 | char[64] | Null-padded; 64 bytes total per entry including null bytes |

### A.4.2 Curve section (four passes)

Follows the name table. Contains exactly four consecutive float-curve arrays, each prefixed by its own count:

| Pass | Loader-level role | Count source | Notes |
|------|-------------------|--------------|-------|
| A (pass 1) | Single-float inverse/alpha track | own u32 prefix | CONFIRMED: each value stored as `1.0 − file_value` (see A.6). This is a one-float-per-keyframe track, distinct from the Vec3 curve below. |
| 2 | Vec3 curve, component +0 | own u32 prefix | CONFIRMED layout: fills component +0 of each Vec3 entry (stride 12). Loader assigns NO colour/scale meaning. |
| 3 | Vec3 curve, component +4 | own u32 prefix | CONFIRMED layout: fills component +4 of each Vec3 entry. |
| 4 | Vec3 curve, component +8 | own u32 prefix | CONFIRMED layout: fills component +8 of each Vec3 entry. |

Each pass reads `u32 curve_count` then `curve_count × f32`.

**CURVE SEMANTICS DOWNGRADED TO DBG-PENDING (CAMPAIGN VFS-MASTERY — two-witness: loader read-order + black-box byte-walk).** At the loader level, passes 2/3/4 are NOT three independent named channels: they are one **generic component-major Vec3 curve**. The loader walks the same destination Vec3 array three times — pass 2 writes component `+0` of each Vec3, pass 3 writes component `+4`, pass 4 writes component `+8` (Vec3 stride 12). **The loader assigns NO colour or scale meaning to these three components**; it simply scatters the three passes into the three lanes of a Vec3 array. Pass A (formerly "pass 1") is a separate single-float track, stored inverted as `1.0 − file_value` (A.6).

The earlier reading that labelled passes 2/3/4 as the per-keyframe **DIFFUSE R/G/B multiplier** is a **render-side interpretation**, not parser-provable: it describes how the render path consumes the Vec3 lanes, not what the file loader knows. That interpretation is therefore **DBG-pending** — the RGB-vs-XYZ (colour vs scale) semantic of the three Vec3 components must be confirmed against the live render path before it can be promoted as CONFIRMED.

What IS settled at the loader level (CONFIRMED, two-witness):
- The curve section is exactly four count-prefixed `f32` arrays.
- Passes 2/3/4 land as the three components of a component-major Vec3 array (stride 12), in component order +0 / +4 / +8.
- The loader attaches no colour/scale meaning; that is a downstream (render) concern.
- A per-pass count may differ from `tex_count`; shorter passes leave the unfilled lanes at their
  default. **Concrete witness (SAMPLE-VERIFIED, build 263bd994):** in `char_select-u.xeff` block 67
  the four pass counts are `alpha / pass2 / pass3 / pass4 = 9 / 9 / 4 / 4` — the alpha track and
  pass 2 carry 9 entries while passes 3 and 4 carry only 4, proving the counts genuinely vary
  within a single block. A parser must read each pass's own `u32` count prefix and never assume the
  four counts are equal or equal to `tex_count`.

The render-side observations that motivated the "diffuse RGB" reading (e.g. distinct per-effect triplets folded into the sprite's per-vertex diffuse) are retained as **render-domain notes for the presentation lane**, tagged DBG-pending here; do not promote them as a loader fact. The element's real SIZE/scale is the keyframe `size_x/y/z` floats (positions 4–6 of the 9-float keyframe, A.4.4), independent of this curve section. The in-memory model's `ScaleX/Y/Z` fields therefore hold this Vec3 curve regardless of whether its render meaning turns out to be colour or scale (a field-naming follow-up).

### A.4.3 Track header (9 bytes, fixed)

Follows the curve section. The track header is **9 bytes** in BOTH the static and the animated
path; the loader branches on the 1-byte `anim_loop` field but the header byte count is identical on
either branch.

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---:|------|-------|-------|-----------|
| +0 | 1 | u8 | `anim_loop` | Non-zero enables the animated (multi-keyframe) path; zero selects the single static-state entry. Observed value: `0x01` in all samples. | CONFIRMED |
| +1 | 4 | u32 LE | `anim_stride` | Duration of one animation frame in milliseconds. **Per-block and clearly variable** — observed values include 67 ms (`zone_sel_u`, `char_select-u` block 0), 100 ms and 140 ms (other `char_select-u` blocks), and 469 ms / 871 ms (earlier samples). The observed list is a distribution sample, NOT an enumeration of legal values. | CONFIRMED |
| +5 | 4 | u32 LE | `anim_base_time` | Base time offset in milliseconds. **Commonly 0, but a non-zero value IS used in the wild** — `331120721.xeff` carries `anim_base_time = 469 ms` while `zone_sel_u` / `char_select-u` carry 0. (Earlier revisions said "0 in all samples"; that absolute is dropped — treat the field as ordinarily 0 with non-zero values legal and observed.) | CONFIRMED |

Derived value (not in file): `total_time = entry_count × anim_stride + anim_base_time` (milliseconds). The loader stores this in the in-memory descriptor.

**Correction (CAMPAIGN VFS-MASTERY — two-witness: loader + black-box):** an earlier revision
described this header as **13 bytes**, inserting a 4-byte `unknown_constant` (value `67`) at
position +1. That 4-byte field **does not exist** — no read-site consumes it, and the two-witness
review (loader read-order plus black-box byte-walk) found no header member there. The four bytes
that were mislabelled `unknown_constant` are in fact the **first keyframe's u32 index prefix**
(A.4.4): the keyframe array begins immediately after this 9-byte header, and its frame-0 entry
carries its own index prefix like every other frame. The `unknown_constant` field is therefore
**deleted** from this spec. The header is 9 bytes on both the static and animated branch; parsers
must not read a fourth header dword.

### A.4.4 Keyframe array

Follows the track header. In the **animated** path (`anim_loop` ≠ 0, A.4.3) it contains `tex_count` keyframe entries; in the **static** path (`anim_loop` = 0) it contains a single static-state entry whose size depends on the element's `emitter_type` (see A.4.6).

**Every animated keyframe carries a u32 index prefix (CORRECTED):** each of the `tex_count`
keyframe entries in the animated path is `u32 kf_index` + 9 × f32 = **40 bytes**, INCLUDING
keyframe 0. An earlier revision wrongly described keyframe 0 as a special 36-byte case with no
index prefix; that "missing" 4-byte prefix was being absorbed into a phantom 13-byte track header
(the deleted `unknown_constant`, A.4.3). With the header corrected to 9 bytes, keyframe 0 is a
normal 40-byte entry. Confidence: CONFIRMED (CAMPAIGN VFS-MASTERY two-witness: loader read-order +
black-box byte-walk).

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

**Confidence: CONFIRMED** for the uniform 40-byte keyframe stride (every animated frame, frame 0
included, has the u32 index prefix) and the 9-float count; **SAMPLE-VERIFIED (partial)** for
individual field semantics.

**Animated keyframe-count source (CODE-CONFIRMED, static re-walk):** in the animated path the loader
does NOT re-read a keyframe count from disk. It takes the count from the **element count of the
texture-handle vector it has already built** while reading the name table (A.4.1) — i.e. the number
of resolved texture handles — which by construction equals `tex_count` (A.4.0). So "`tex_count`
keyframes" and "one keyframe per resolved texture handle" are the same number; a faithful parser may
read exactly `tex_count` 40-byte entries.

### A.4.6 The one real `emitter_type`-dependent branch (NOT a header tagged union)

The shape of the keyframe / static-state section depends on the track header `anim_loop` byte
(A.4.3) and, only in the static case, on the element's own `emitter_type` (A.4.0 / A.12). This is the
single place where `emitter_type` changes how many bytes are read — it is a per-element
micro-variation, **not** a file-level tagged union and **not** keyed by any header field.

| `anim_loop` | `emitter_type` | Section read |
|---:|---:|---|
| ≠ 0 (animated) | any | `tex_count` frames, each `u32 index + 9 × f32` = 40 B (frame 0 included — it is NOT a special no-index case; see A.4.4 correction). Last 3 floats are Euler degrees → quaternion. |
| 0 (static) | 0 or 1 | one entry: 6 × f32 (velocity Vec3 + size Vec3), 24 B, no rotation read. |
| 0 (static) | 2 | one entry: 6 × f32 **plus 3 extra f32 Euler rotation** (→ quaternion), 36 B read. |

So the only `emitter_type` size difference is **+12 bytes (3 Euler floats) in the static branch when
`emitter_type` = 2** (directional billboard). This is exactly the behaviour described in A.12 and is
the real meaning of what an earlier revision saw as a header "type_flag = 1 vs 2": values 1 and 2 are
just element 0's `emitter_type`. Confidence: CONFIRMED (parser read-order plus sample byte-walk
agree).

### A.4.5 Multi-sub-effect files

For `sub_effect_count > 1`, sub-effects follow sequentially, and EVERY block — block 0 included — is parsed with the same element read sequence (24-byte fixed head A.4.0, then the variable body). There is no distinct per-block prefix and no offset table; block boundaries are found purely by sequential parsing. CONFIRMED (2026-06-14) against `zone_sel_u.xeff` (11 blocks) and `char_select-u.xeff` (68 blocks).

## A.5 In-Memory Element Layout (104 bytes, 26 dwords)

**Confidence: PARSER-CONFIRMED + RUNTIME-TRACED (no element-bearing sample verification)**

This is the in-memory layout the loader builds; it is provided so an engineer understands the runtime's view. **File read order differs from in-memory dword order** for `tex_count` and `field_unknown_a` (the loader writes them in reverse relative to file order). On disk, fields appear strictly in the read order given in A.4.

| Dword | Byte offset | Type | Field | Source / Notes |
|---|---|---|---|---|
| [0] | +0x00 | u32 | `emitter_type` | from element's emitter class |
| [1] | +0x04 | u32 | `resource_id` | < 10000 = mesh index; ≥ 10000 = particle id |
| [2] | +0x08 | u8 bool | `anim_flag` | low byte; upper 3 bytes unused |
| [3] | +0x0C | u32 | `field_unknown_a` (`element_flags`) | Bulk-read; no semantic read-site found. Semantics **DBG-pending** — keep opaque, do not assign a meaning |
| [4] | +0x10 | u32 | `tex_count` | drives keyframe count; low byte also tested as UV-scroll flags (A.10) |
| [5..7] | +0x14..+0x1C | ptr×3 | texture-handle vector | resolved texture handles (no file bytes) |
| [8] | +0x20 | u32 | `alpha_key_count` | curve-pass-1 count |
| [9..11] | +0x24..+0x2C | ptr×3 | alpha-key vector (f32) | stored as `1.0 − file_value` |
| [12] | +0x30 | u32 | (constructor-zeroed; no confirmed write) | likely padding/reserved |
| [13] | +0x34 | u32 | `vec3_curve_key_count` | high-water mark of the three component-curve counts (passes 2/3/4, A.4.2) |
| [14..16] | +0x38..+0x40 | ptr×3 | component-major Vec3 curve vector | curve passes 2/3/4 scattered into Vec3 lanes +0/+4/+8 (A.4.2). Loader assigns no colour/scale meaning; the RGB-vs-XYZ render semantic is DBG-pending. |
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

**Confidence: SAMPLE-VERIFIED** (build 263bd994: `entry_count = 3669`, record stride 30; the size
formula `4 + 3669 × 30 = 110,074` matches the on-disk file size exactly). The boot loader reads the
`u32` count, then `count` 30-byte NUL-padded name records. The full path is built as a verbatim
concatenation of the directory prefix and the record name: `data/effect/xeff/<name>` — no extension
is appended by the loader. The record name already carries the extension in 3,625 of 3,669 records
(typically `.xeff`); 44 records lack it (likely directory entries or alternate-handled stems).
(The 85-entry surplus over the 3,584 `.xeff` files in the VFS is recorded in Open Questions
— stale manifest rows or names served from a secondary archive; the structure itself is exact.)

**Boot-path reader confirmed (CODE-CONFIRMED, static re-walk):** the manifest reader actually invoked
on the boot path does read this exact `u32 count` + `count × 30-byte record` structure (despite an
internal "headerless" working name that is a misnomer — it is NOT headerless, it reads the leading
count). Per record it concatenates the directory prefix with the record name to form
`data/effect/xeff/<name>`, opens the file, takes the `.xeff` header's first u32 as the `effect_id`,
and inserts the descriptor into the registry keyed on that id (FIRST-WINS on a duplicate — see §C.2).
The end-to-end manifest → file → registry chain is therefore confirmed; the A.9 layout above is exact.
A second, alternate manifest reader exists (it reads the count through a different disk-read helper but
parses the same 30-byte records and performs the same registry insert); it is NOT on the traced boot
path and resolves to the identical on-disk structure, so it imposes no separate layout.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | Number of effect name records. SAMPLE-VERIFIED = 3669 on this build. |
| 0x04 | `entry_count × 30` | Record[] | `entries[]` | One 30-byte record per effect |

**Name record (30 bytes, zero-padded):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 30 | char[30] | `name` | Filename including extension (typically `.xeff`), no path prefix. Full path: `data/effect/xeff/<name>` (verbatim, no extension appended by the loader). **CORRECTED (CYCLE 12 Block C):** the prior "without extension" claim was wrong — 3,625 of 3,669 on-disk records carry the `.xeff` extension; the loader never appends one. CP949 for Korean. |

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

### A.11.1 Companion: `xobj.lst` — Primitive Mesh Boot Manifest

**Confidence: SAMPLE-VERIFIED** (build 263bd994: `entry_count = 32`, record stride 34; the size
formula `4 + 32 × 34 = 1,092` matches the on-disk file size exactly).

- **Path:** `data/effect/xobj.lst`
- **Endianness:** Little-endian
- **Layout:** `u32 count` prefix + `count × 34-byte` NUL-padded name records — the same counted-array
  pattern as `xeffect.lst` (A.9) and `bmplist.lst` (A.10), but with a 34-byte record stride.
- **Role:** boot manifest enumerating the `.xobj` ASCII primitive meshes; each record provides a base
  name resolved to the `.xobj` file path. Populates the shared mesh table used when `resource_id < 10000`.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 LE | `entry_count` | Number of mesh name records. SAMPLE-VERIFIED = 32 on this build. |
| 0x04 | `entry_count × 34` | Record[] | `entries[]` | One 34-byte record per mesh |

**Name record (34 bytes, zero-padded):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0 | 34 | char[34] | `name` | Base name (numeric/control-char stems observed in sample). Full path resolution pending — likely `data/effect/xobj/<name>` by analogy with sibling manifests. CP949 for Korean. |

## A.12 Enumerations / Flags

### `emitter_type` (u32 in element in-memory layout)

| Value | Meaning | Confidence |
|------:|---------|------------|
| `0` | Billboard sprite — flat camera-facing quad | CONFIRMED |
| `1` | Mesh-particle object — per-vertex transform of shared mesh | CONFIRMED |
| `2` | Directional billboard — camera-facing quad with fixed 90° Y pre-rotation; reads extra rotation in static-state branch (A.4.6) | CONFIRMED |
| `20` | Parse-inert at load — the value `20` is read and stored but changes nothing in the parse path. Its **render meaning is DBG-pending** (needs live-debugger confirmation); do not assign one. | parse: CONFIRMED inert · render: DBG-pending |
| other | Behaviour undetermined | UNRESOLVED |

**Parse-time effect of `emitter_type` (CAMPAIGN VFS-MASTERY — two-witness: loader + black-box).**
Only the value `2` changes how the file is parsed: it gates the +12-byte extra Euler-rotation read
in the **static** branch (A.4.6). Every other `emitter_type` value — including `0`, `1`, and `20` —
is **parse-inert**: read and retained, but it does not alter the byte layout the loader consumes.
In particular `emitter_type == 20` does NOT gate any branch in the parser; its only effect is at
render time, which was not traced and is therefore **DBG-pending** (live-debugger confirmation
required). A faithful parser stores the value verbatim and branches on it nowhere.

The four bytes at file offset `0x08` — formerly mislabelled a header `type_flag` — are the
`emitter_type` of sub-effect block 0, drawn from this same enum (A.2 / A.4.0). The "type_flag = 1 vs
2" behavioural difference is exactly the static-branch rotation read in A.4.6.

## A.13 UV-Scroll Render Flags via `tex_count` Low Byte (MEDIUM confidence)

At runtime, bits 0 and 1 of the low byte of the in-memory `tex_count` dword are tested as UV-scroll flags:
- bit 0 set → scroll U by `phase_ms mod 5000 / 5000.0` (5-second loop)
- bit 1 set → scroll V by the same ratio

This appears to be an intentional dual use of the frame-count field; intent unverified. Confidence: MEDIUM.

## A.14 Named Constants

| Name | Value | Context |
|------|------:|---------|
| `XEFF_HEADER_SIZE` | 8 (0x08) | File header byte count: `effect_id` u32 + `sub_effect_count` u32. (Corrected 2026-06-14 from a wrong 32-byte value; the extra 24 bytes were block 0's element fixed head, A.4.0.) |
| `XEFF_ELEMENT_FIXED_HEAD` | 24 (0x18) | Per-element on-disk fixed head: emitter_type + resource_id + anim_flag + flags + dword + tex_count (A.4.0). |
| `XEFF_ELEMENT_STRIDE` | 104 (0x68) | In-memory element record size |
| `XEFF_KEYFRAME_STRIDE` | 44 (0x2C) | In-memory keyframe entry size (animated branch) |
| `XEFF_STATIC_STRIDE` | 48 (0x30) | In-memory static-state allocation |
| `XEFF_TEX_NAME_LEN` | 64 (0x40) | Bytes per texture name entry in name table |
| `XEFF_LST_NAME_LEN` | 30 (0x1E) | Bytes per record in `xeffect.lst` and `bmplist.lst` |
| `XEFF_TRACK_HEADER_SIZE` | 9 | Bytes: 1 (anim_loop) + 4 (anim_stride) + 4 (anim_base_time). Identical on the static and animated branch. (Corrected from a wrong 13 — there is no 4-byte header field at +1; those four bytes are keyframe 0's u32 index prefix, A.4.3 / A.4.4.) |
| `XEFF_KEYFRAME_ONDISK_STRIDE` | 40 (0x28) | On-disk size of one animated keyframe entry: u32 index prefix + 9 × f32 = 40 B, for EVERY frame including frame 0 (A.4.4). Distinct from the 44-byte in-memory keyframe stride. |
| `XEFF_EMITTER_BILLBOARD` | 0 | `emitter_type`: flat billboard sprite |
| `XEFF_EMITTER_MESH` | 1 | `emitter_type`: mesh-particle object |
| `XEFF_EMITTER_DIRECTIONAL` | 2 | `emitter_type`: directional billboard |
| `XEFF_RESOURCE_PARTICLE_THRESHOLD` | 10000 | `resource_id ≥ this` → GPU particle descriptor id |
| `XEFF_UV_SCROLL_PERIOD_MS` | 5000 | Loop period for UV-scroll flags |
| `XEFF_INVALID_MAGIC` | 0x46464558 | `effect_id` sentinel; file invalid if header equals this |
| `XEFF_TIME_UNIT` | milliseconds | Engine wall-clock unit for all timing fields |

## A.15 Front-end scene VFX mapping (effect id → file)

> **Confidence: SAMPLE-VERIFIED** (file presence, header `effect_id` / `sub_effect_count`, and
> manifest listing observed in the real VFS). This is purely the front-end **id → file** mapping;
> the `.xeff` byte format itself is already specified in Sections A.1–A.14. Added for the
> front-end (login / PIN / server-list / char-select) scene lane.

The front-end screens use named `.xeff` files in the dedicated **`380xxxxxxx` front-end UI effect
id range** (server-list / zone-select) plus a high-sub-effect-count char-select effect:

| Front-end scene | VFS path | `effect_id` | `sub_effect_count` | Notes |
|---|---|---:|---:|---|
| Server-list / zone-select | `data/effect/xeff/zone_sel_u.xeff` | 380000000 | 11 | Listed in `xeffect.txt` / `xeffect.lst` (A.5, A.9). |
| Server-list / zone-select (variant 2) | `data/effect/xeff/zone_sel2-u.xeff` | 380000001 | 11 | Second variant; same size class as the first. |
| Char-select | `data/effect/xeff/char_select-u.xeff` | 380003000 | **68** | Rich particle effect (68 sub-effects — the highest front-end count). |

**Login / PIN scenes have NO `.xeff` VFX (CONFIRMED ABSENT).** A VFS census found no `.xeff` file
whose name contains `login`, `pin`, `title`, `intro`, `server`, or `menu`. The login and PIN
screens' animated elements are delivered through DDS sprite art and scripted UI, **not** through
the particle effect system. (The pre-login "red ribbon" intro crawl is a positional DDS scroll, not
an `.xeff` — see `specs/intro_sequence.md`.)

**Parser caveat RESOLVED (2026-06-14):** the high-`sub_effect_count` front-end files
(`char_select-u.xeff` at 68 sub-effects, block-0 `tex_count` = 2; `zone_sel_u.xeff` /
`zone_sel2-u.xeff` at 11 sub-effects, block-0 `tex_count` = 20) previously failed the `.xeff` parser.
Root cause confirmed: the header is 8 bytes and EVERY sub-effect block (block 0 included) is parsed
by the same element read sequence — a 24-byte fixed head (A.4.0) then the variable body. Each block's
entry count is its own `tex_count` at element offset +0x14; there is no header `first_entry_count` and
no distinct per-block prefix. Parser fixed 2026-06-14; all regression fixtures for both files pass.

Char-class selection within char-select additionally uses 16 `guildmaster_{d|j|mo|mu}_{jung|sa}{05|06}.xeff`
files (4 classes × 2 levels × 2 event types); these are standard `.xeff` (Section A) and need no
special mapping beyond their filenames.

## A.16 In-Memory Runtime Element Struct (104 bytes / 0x68) — Campaign-5

> **Confidence: 104-byte size CONFIRMED; ctor/setup-written field offsets CONFIRMED; a few pad
> dwords PLAUSIBLE; the `.xeff` on-disk → this-struct field mapping is UNVERIFIED (out of this
> lane — see note at the end).** Added Campaign 5, Lane 1 (effects runtime). This documents only
> the **in-memory runtime element** that the live effect system ticks; the on-disk `.xeff` byte
> layout is in Sections A.1–A.15 and is owned by the format lane. Behavioural semantics
> (emitter dispatch, keyframe sampling, bone attachment, blending, draw order) are in
> `specs/effects.md §17`. Engineers cite this table as `// spec: Docs/RE/formats/effects.md §A.16`.

This is the single fixed-size object the runtime allocates for every live effect, regardless of
subtype. Its size (104 bytes / 0x68) is **CONFIRMED** independently by the fixed-block pool
allocator, which batch-allocates `104 × N` bytes and strides the block in 104-byte units to build a
free-list. The polymorphic family (`XEffect` base, `UserXEffect`, `JointXEffect`, `MapXEffect`) is
selected by the virtual-dispatch pointer at +0x00 plus the **type tag** at +0x04. This in-memory
struct is distinct from the on-disk per-element layout in §A.5 (the §A.5 table is the loader's
per-component descriptor element; this §A.16 table is the per-instance runtime object).

### A.16.1 Base / `UserXEffect` element (all subtypes share this layout)

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | ptr | `vtable` | Class identity (base / User / Joint / Map). | CONFIRMED |
| +0x04 | 4 | i32 | `type_tag` | 1 = `UserXEffect`, 4 = `JointXEffect` (set by the derived constructor); base leaves it unset; `MapXEffect` sets its own. | CONFIRMED |
| +0x08 | 1 | u8 | `alive` | Active flag; cleared to stop the effect (lifetime expiry, lost attach, soft-stop). | CONFIRMED |
| +0x09 | 1 | u8 | `particles_built` | Set once first-tick init has built the per-element particle/draw objects. | CONFIRMED |
| +0x0C | 4 | ptr | `descriptor` | Pointer to the shared parsed `.xeff` descriptor (`CoreXEffect`); 0 = invalid / lookup failed (discard before active list). | CONFIRMED |
| +0x10 | 12 | f32[3] | `world_pos` | Current effect origin (x, y, z); copied from the anchor actor or bone each tick. | CONFIRMED |
| +0x1C | 16 | f32[4] | `world_rot` | Current effect orientation quaternion (x, y, z, w); copied from the anchor actor or bone each tick. | CONFIRMED |
| +0x2C | 4 | f32 | `pad_0` | Zeroed by the base constructor; no tick read observed. | PLAUSIBLE |
| +0x30 | 4 | f32 | `pad_1` | Zeroed; no tick read observed. | PLAUSIBLE |
| +0x34 | 4 | f32 | `pad_2` | Zeroed; no tick read observed. | PLAUSIBLE |
| +0x38 | 1 | u8 | `flagA` | Base constructor sets 0; semantics unresolved. | PLAUSIBLE |
| +0x3C | 1 | u8 | `loop_flag` | Set from the setup argument; when set, the period-expiry kill is bypassed (effect loops). Low byte of a dword whose non-zero-ness also indicates a valid lookup result (see §6.2 note in `specs/effects.md`). | CONFIRMED |
| +0x40 | 4 | u32 | `start_ms` | Spawn time stamp = `delay_arg + clock_ms`; per-tick update is skipped while `now < start_ms`. | CONFIRMED |
| +0x44 | 1 | u8 | `visible` | "Drawn this frame" flag; cleared while before the start time. | CONFIRMED |
| +0x45 | 1 | u8 | `in_range` | Cleared when distance-culled vs the local player. | CONFIRMED |
| +0x48 | 4 | f32 | `effect_scale` | Per-instance scale; multiplied into every component extent each tick. = descriptor base scale × spawn `effectscale` argument. The **descriptor base scale** is itself the `effectscale.xdb` override value where one exists (it REPLACES the ctor default `1.0` at parse — see Section D), else `1.0`. | CONFIRMED |
| +0x4C | 4 | f32 | `y_offset` | Height offset added to the anchor actor's Y when placing the effect. | CONFIRMED |
| +0x50 | 4 | f32 | `time_rate` | Elapsed-ms multiplier: `local_ms = (now − start_ms) × time_rate`. | CONFIRMED |
| +0x54 | 4 | i32 | `anchor_id` | Actor sort/id used to look up the anchor actor (`UserXEffect`). | CONFIRMED |
| +0x58 | 1 | u8 | `anchor_sub` | Actor sub-id / kind selector for the anchor lookup. | CONFIRMED |
| +0x5C | 4 | i32 | `target_id` | Secondary actor (the "target", for line/beam effects between two actors). | CONFIRMED |
| +0x60 | 1 | u8 | `target_sub` | Sub-id for the target actor lookup. | CONFIRMED |
| +0x64 | 1 | u8 | `tail` | `UserXEffect`: a miscellaneous byte. `JointXEffect`: re-used as the orientation-source selector (see A.16.2). | CONFIRMED |

**Stride / size:** 104 bytes (0x68), CONFIRMED. There is one element per live effect; the count is
not stored in the element — instances live on the manager's active list (see `specs/effects.md §5.2`).

### A.16.2 `JointXEffect` overlay of the attachment region (+0x58 .. +0x64)

For a `JointXEffect` (type tag 4) the bytes from +0x58 onward are re-purposed as a bone-attachment
descriptor. This overlay replaces the `anchor_sub` / `target_id` / `target_sub` / `tail` reading
above:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x58 | 4 | i32 | `bone_actor_id` | Actor whose skeleton owns the bone. | CONFIRMED |
| +0x5C | 1 | u8 | `bone_actor_sub` | Actor sub-id for the bone-owner lookup. | CONFIRMED |
| +0x5D | 1 | u8 | `bone_name_mode` | 0 = use the explicit bone id at +0x60; 1 = look the bone up through the AnimCatalog by weapon/hand slot (see `specs/effects.md §17.4`). | CONFIRMED |
| +0x60 | 4 | i32 | `bone_id` | Explicit bone id (used when `bone_name_mode` = 0). | CONFIRMED |
| +0x64 | 1 | u8 | `rot_source` | Orientation source: 1 = the bone's own world rotation; 2 = the actor's facing quaternion. | CONFIRMED |

The runtime copies the resolved bone's world translation into `world_pos` (+0x10) and a quaternion
into `world_rot` (+0x1C) each tick. The full bone-resolution behaviour, including the AnimCatalog
weapon-hand slot lookup, is specified in `specs/effects.md §17.4`; the bone world-transform layout is
owned by `specs/skinning.md`.

> **CYCLE 12 Block B (IDB SHA 263bd994, 2026-06-22) — weapon bone-slot attach CONFIRMED.**
> When `bone_name_mode` = 1 (`XEFF_BONE_MODE_CATALOG`), the AnimCatalog lookup resolves bone ids
> for **weapon attachment slots 902, 903, 904, and 905**. These four slots are the confirmed
> range used for weapon bone-attachment; the lookup is CODE-CONFIRMED by the binary's AnimCatalog
> dispatch path. **Which slot maps to which hand/weapon position** (e.g. right-hand vs left-hand
> vs off-hand vs two-hand) is **DEFERRED — debugger-pending** (requires a live-session register
> read to observe which slot id is passed for each weapon-type equip). Do not hard-code a
> slot→hand mapping until the debugger pass confirms it. Cite: `// spec: Docs/RE/formats/effects.md §A.16.2`.

### A.16.3 Named constants (Campaign-5 runtime element)

| Name | Value | Context |
|------|------:|---------|
| `XEFF_RUNTIME_ELEMENT_SIZE` | 104 (0x68) | In-memory runtime effect-instance object size (CONFIRMED via the pool allocator). |
| `XEFF_TYPE_TAG_USER` | 1 | `type_tag` (+0x04) value for `UserXEffect`. |
| `XEFF_TYPE_TAG_JOINT` | 4 | `type_tag` (+0x04) value for `JointXEffect`. |
| `XEFF_BONE_MODE_EXPLICIT` | 0 | `bone_name_mode`: use explicit bone id. |
| `XEFF_BONE_MODE_CATALOG` | 1 | `bone_name_mode`: AnimCatalog weapon-slot lookup. |
| `XEFF_ROT_SOURCE_BONE` | 1 | `rot_source`: use the bone's own world rotation. |
| `XEFF_ROT_SOURCE_ACTOR` | 2 | `rot_source`: use the actor's facing quaternion. |

> **Note — `.xeff` on-disk → runtime-element mapping is UNVERIFIED / pending.** The byte layout of
> the on-disk `.xeff` descriptor that ultimately feeds this runtime element (the file parser side)
> was not walked in the runtime lane. The runtime descriptor offsets the tick reads (loaded flag,
> last-use timestamp, period, component count, component array base) are behavioural details kept in
> `specs/effects.md §17`; how `.xeff` file bytes map into them is **owned by the format/struct lane**
> and remains UNVERIFIED here. Do not infer the on-disk layout from this in-memory table — the two
> differ in field order, and several fields here (texture handles, resolved descriptor pointer) have
> no on-disk counterpart. The authoritative on-disk `.xeff` layout is Sections A.1–A.15.


## A.17 Correction history (Section A header / block framing)

To keep this spec honest and traceable, the header-size revisions are recorded here:

- **Earliest revision:** described an 8-byte header (`effect_id` + `element_count`). Correct on size,
  but the block layout was not yet walked.
- **2026-06-12 revision:** "corrected" the header to **32 bytes**, inventing a header `type_flag`
  (offset 0x08), a 16-byte `reserved` region (0x0C), and a `first_entry_count` (0x1C). This was a
  **mislabelling**: those 24 bytes are the leading fields (the fixed head, A.4.0) of sub-effect block
  0, which begins immediately after the 8-byte header. The "all-zero reserved" reading came from
  sampling only files whose block-0 `resource_id` / `anim_flag` / flags happened to be zero.
- **2026-06-14 correction (this revision):** header restored to **8 bytes**. The value at offset
  0x08 is element 0's `emitter_type` (1 = mesh-particle, 2 = directional billboard), NOT a header
  tag — and it is NOT a tagged union; the only `emitter_type`-dependent size change is the +12-byte
  static-branch rotation for `emitter_type` = 2 (A.4.6). The name `type_flag` is retired in favour of
  element `emitter_type` (A.12). The total file size is unchanged across all three readings — only
  the field labels for bytes 0x08–0x1F differ — so prior residual checks were not invalidated.

> **Engineer note:** parse the `.xeff` header as exactly 8 bytes, then parse every sub-effect block
> (block 0 included) with the A.4.0 fixed head + A.4 body sequence; never read a `type_flag`,
> `reserved`, or `first_entry_count` header field. Cite `// spec: Docs/RE/formats/effects.md` on
> every offset.

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

## C.2 Descriptor registry and lazy load — `effect_id` → file resolution (Option B, CONFIRMED)

**Confidence: CONFIRMED.**

The runtime **always** resolves a spawn's `effect_id` through the boot-populated registry — a sorted
map / red-black tree keyed by the raw numeric `effect_id`. The verdict on the resolution path:

- **Option A — numeric-name sprintf at spawn: REJECTED.** The client never builds a path like
  `data/effect/xeff/{id}.xeff` from a decimal id at spawn time. There is no integer-to-`.xeff` format
  string; the only `.xeff` path-builder runs at boot and concatenates a *name* (directory prefix + a
  list name), never a decimal id.
- **Option B — registry lookup keyed by `effect_id`: CONFIRMED as the sole runtime path.** The
  spawn key is the raw numeric `effect_id`, with no arithmetic transform.
- **Option C — hybrid / numeric-name fallback: NONE.** On a registry miss the resolver returns
  invalid (0); no numeric-name fallback open is attempted.

This sharpens A.2: a numeric filename *coincides* with the header `effect_id` by authoring
convention, but the runtime keys purely on the `.xeff` header's first u32 (`effect_id`), never
re-deriving the filename.

**Boot population (CONFIRMED):** `xeffect.lst` is `u32 count` then `count × 30-byte NUL-padded ASCII
name` (A.9). Per record the boot loader builds `data/effect/xeff/<name>` — a **NAME concat, not an id
format** — opens the file, **reads the first u32 of the header as the `effect_id`**, and inserts the
descriptor into the tree keyed by that header id. The `effect_id` therefore comes from the FILE
HEADER, not from list order or record index. The `.lst` provides only `name → path`.

**Duplicate-id tie-break — FIRST-WINS / reject-on-insert (CONFIRMED, CYCLE 7).** The registry is a
no-overwrite ordered map (red-black tree) keyed by the bare 4-byte `effect_id`, with exactly one node
per id. At each insert the loader checks whether a node with that id already exists: if none exists it
inserts the descriptor; if one already exists it **does NOT insert** — it logs a "same effect"
diagnostic and immediately destroys the newly built descriptor, leaving the original node untouched.
Because records are processed in `xeffect.lst` order, **the first file listed for an id wins** and
every later same-id file is rejected and made unreachable at runtime. This is NOT last-wins, there is
no override, and there is no list/bucket of multiple records per id. (Earlier revisions guessed
"second may silently overwrite first, untraced"; that guess was wrong — the loader does the
opposite.)

- **Lazy load:** a spawn request looks up the descriptor by `effect_id`. If the descriptor's "loaded"
  flag is clear, the registry lazy-parses the `.xeff` file (reopening the **boot-stored path string**,
  not any numeric-format path), resolves textures, and stamps the descriptor loaded. A failed lookup
  abandons the spawn.
- **Consumer side of the tie-break:** the spawn resolver uses the same id-keyed lookup as the boot
  insert. Because the map holds exactly one node per `effect_id`, every spawn can only ever resolve to
  the single first-wins descriptor; a later-listed duplicate's bytes never enter the tree and are
  unreachable.

For the complete boot loading sequence, object pools, and spawn factory details, see
`specs/effects.md §3` (Boot Loading), `specs/effects.md §15.1` (the spawn-side resolver), and
`specs/effects.md §4` (Pool Allocation).

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

**Confidence: SAMPLE-VERIFIED (two-witness, build 263bd994)** — byte-exact against a 16-byte,
two-record sample (`16 = 2 × 8`, size formula exact), AND the loader + application site re-walked.

- **Path:** `data/script/effectscale.xdb`
- **Endianness:** Little-endian
- **Magic / signature:** None
- **Layout:** flat array of fixed 8-byte records, no header, no count prefix; record count = `file_size / 8`
- **In-memory form:** the loader builds a map / red-black tree keyed by `effect_id`, each node holding the `f32 scale` at node-relative `+0x04`. Lookup is by `effect_id`.

**Record (8 bytes):**

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| +0x00 | 4 | u32 LE | `effect_id` | Matches a `.xeff` `effect_id` (Section A.2) |
| +0x04 | 4 | f32 LE | `scale` | Scale multiplier for that effect |

**Observed sample records.** Earlier recorded: `(353100201 → 3.0)`, `(353100202 → 2.0)`. The
build-263bd994 sample holds **different content** — `(360021705 → 3.0)`, `(360021706 → 2.0)` — i.e.
a newer build's table with the **identical 8-byte layout and size formula**. This content difference
is a per-build table edit, **NOT a structural conflict**: the format holds on both builds.

**Application (RESOLVED, two-witness — supersedes the prior "unconfirmed application site").** The
override is applied **at parse time, inside the `.xeff` body parser** (the §A lazy-parse path),
**before** the descriptor is marked loaded: the parser looks up the descriptor's `effect_id` in this
table and, on a hit, **REPLACES** the descriptor's base-scale field (the ctor default of `1.0`,
§A.16 / the descriptor base) with the table's `scale` value. It does **NOT** stack / multiply with
the base-scale — it overwrites it. The per-instance spawn `effectscale` argument (§A.16 `+0x48`
`effect_scale`) then multiplies *that* resolved base-scale at spawn. So the scale chain is:
`effectscale.xdb scale` (replaces the `1.0` ctor default at parse) `×` per-instance spawn argument
(at runtime). A miss leaves the `1.0` ctor default in place.

---

# Section E: `particleEmitter.eff` — GPU Particle Emitter Descriptor Table

**Confidence: file structure SAMPLE-VERIFIED (two-witness, build 263bd994) — the variable-length
entry sequence is established from the loader read-order, byte-symmetric with the writer, AND
re-confirmed by a byte-walk of the real `particleEmitter.eff` (116,652 B): 146 entries walked to a
clean tail with no desync. Entry-header fields CONFIRMED/HIGH; the 52-byte sub-record inner fields
remain UNRESOLVED except one colour-like quad (DBG-pending).**

> **Sample-walk witness (SAMPLE-VERIFIED, build 263bd994):** entry 0 carries `entry_id = 10001`,
> `num_frames = 10`, `sprite_size = (64.0, 1.0)`, `max_particles = 1`; entries 1.. continue with
> contiguous ids `10002, 10003, …`. Each entry body of `28 + num_frames × 52 + 64` bytes consumes
> the file exactly across all 146 entries — the variable-length model is byte-exact.

## E.0 CORRECTION (Campaign 5B) — the flat-table model is RETIRED

> A prior revision described this file as a **flat** `16-byte header (u32 magic 0x2711 + u32
> group_count + two f32) + 2,243 × 52-byte records`, with the stride "anchored by a sequential u16
> at record +0x10". **That model was derived from sample bytes alone and is REFUTED by the loader.**
> It is retired here, in the same honest style as A.17.

The real loader reads a **sequence of variable-length entries**, not a flat record array, and
performs **no file-level magic check**. Each misread "header field" maps to a real field of the
**first entry**, as follows:

| Old (WRONG) reading | Actual meaning |
|---|---|
| `u32 magic = 0x2711` at file 0x00 | The first entry's `entry_id` (= 10001 decimal — a GPU-particle id, which by authoring live in the `≥ 10000` space). It is data, not a signature; the loader never compares this dword to any constant. |
| `u32 group_count = 10` at file 0x04 | The first entry's `num_frames` (its sub-record count and the loop terminator). |
| two `f32` at 0x08 / 0x0C | The first entry's `sprite_size_x` / `sprite_size_y`. |
| "sequential u16 at record +0x10" | A within-entry sub-record field. The "+1 per 52 bytes" pattern only holds inside one entry's contiguous sub-record run; it is NOT a file-global record index, and the 2,243-record / flat-stride reconciliation was coincidental. |

A parser built on the flat model desyncs the moment the file holds more than one entry or an entry
whose `num_frames` differs from the first. Use the variable-length entry model below.

## E.1 Identification

- **Path:** `data/effect/particle/particleEmitter.eff` (the VFS normalises this to lowercase
  `data/effect/particle/particleemitter.eff`; the logical name is `particleEmitter.eff`)
- **Magic / signature:** **None.** The loader performs no magic comparison; the file is identified by
  its fixed path only.
- **Endianness:** Little-endian
- **Disambiguation:** This is NOT the same format as the Section B geometry `.eff` files. Dispatch by
  directory path, not by extension alone. This on-disk table feeds the GPU particle sub-system that
  `resource_id ≥ 10000` `.xeff` elements bridge to (§A.16, E.4).

## E.2 File layout — variable-length entry sequence (CONFIRMED)

```
File = Entry[0] Entry[1] ... Entry[k]   (read in order; no count prefix, no directory)

Entry = 28-byte entry header
      + num_frames × 52-byte sub-record       (num_frames from the entry header)
      + 64-byte trailing texture name
```

- **Termination:** the read loop stops when it reads an entry header whose `num_frames` is **0**
  (a sentinel/terminator entry), or when fewer than 28 bytes remain in the file (tail guard).
- **Entry count source:** NOT stored. Derived only by walking entries until the `num_frames == 0`
  terminator. Entries are keyed and looked up by `entry_id` (a sorted / red-black map keyed on the
  header's first u32), not by sequence index.
- **No file header:** there is no file-level header, no count field, and no magic preceding the
  first entry. Entry 0 begins at file offset 0x00.

### E.2.1 Entry header (28 bytes / 0x1C)

The loader reads exactly 28 bytes into a zeroed block, then overwrites the last two dwords with
runtime pointers (the resolved texture handle and the allocated sub-record array pointer). Those two
dwords' **on-disk values are ignored at load** — the authoritative texture source is the 64-byte
trailing name (E.2.3).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 LE | `entry_id` | Map key for this entry. A GPU-particle id in the `≥ 10000` space (first observed entry = 10001). Selected at spawn by raw equality to a `.xeff` element's `resource_id` (E.4). | CONFIRMED |
| 0x04 | 4 | u32 LE | `num_frames` | Sub-record count for this entry AND the loop terminator (`0` ends the read loop). Drives the 52-byte sub-record array length. **This is also the live particle count** — the per-particle rings and the simulation loop are sized by `num_frames`, NOT by `max_particles` (0x10); see the note below. | CONFIRMED |
| 0x08 | 4 | f32 LE | `sprite_size_x` | Per-emitter sprite size; fed to the GPU particle buffer's sprite-size setter. "x then y" axis naming is HIGH, not CONFIRMED (the setter's internal axis assignment was not traced). | HIGH |
| 0x0C | 4 | f32 LE | `sprite_size_y` | Second sprite-size float; fed to the same sprite-size setter. | HIGH |
| 0x10 | 4 | u32 LE | `max_particles` | Carried into the runtime emitter object and asserted non-zero by the loader, but on the render path the per-particle rings and the simulation loop are sized by `num_frames` (0x04), not by this field — in an observed entry the two differ (num_frames = 10, max_particles = 1). Whether `max_particles` ever bounds the vertex/index-buffer capacity at runtime is **DBG-pending**. | HIGH (carried + asserted; ring sizing uses num_frames) |
| 0x14 | 4 | u32 LE | `tex_handle_slot` | **OVERWRITTEN at load** with the resolved texture handle (from the trailing 64-byte name). The on-disk value is a saved slot id and is **ignored on load**; the trailing name is authoritative. | MEDIUM (disk value unused) |
| 0x18 | 4 | u32 LE | `subrecord_array_ptr` | **OVERWRITTEN at load** with the pointer to the newly-allocated 52-byte sub-record array. The on-disk value is a stale/placeholder pointer, never consumed. | MEDIUM (disk value unused) |

- **Mandatory-field assert:** the loader asserts that the resolved texture handle, `max_particles`,
  and the sub-record array pointer are all present (non-zero) — confirming those three are required
  for a valid entry. (The live ring/loop count is `num_frames`, not `max_particles`; size the rings
  by `num_frames` and merely carry `max_particles`.)
- **Engineer note:** treat the two trailing dwords (0x14, 0x18) as *ignored on read*. Resolve the
  texture from the 64-byte name; do not consume the on-disk slot/pointer.
- **Cross-witness (CYCLE 7):** the entry-header field set (`entry_id`, the two sprite-size f32, and
  `max_particles`) is independently corroborated by the `tool/effect/particle_%d.txt` text serializer
  sibling, which reads the same header fields in the same order before its 52-byte sub-record loop.

### E.2.2 Sub-record (52 bytes / 0x34) — per-particle / per-frame descriptor

`num_frames` of these follow each entry header, immediately before the 64-byte texture name. The
52-byte stride is CONFIRMED (the loader allocates `num_frames × 52`, and a sibling tool-preview path
strides by 52). **FIELD WIDTHS/TYPES FULLY RESOLVED (CYCLE 7).** The two prior "opaque slice" rows
are retired: the full 52 bytes decompose exactly into 19 typed fields — 4 × u16, a 4-byte RGBA8
colour quad at +0x08, 4 × f32, 4 × u16, then 4 × f32 — totalling `8 + 4 + 16 + 8 + 16 = 52` bytes.

**Width witness.** The binary loader itself only **bulk-copies** the raw `52 × num_frames` bytes — it
does not field-decode the sub-record — so the field widths could not be read from the loader alone.
They are instead proven by an **in-tool TEXT serializer sibling**: an editor parser of the same
record struct, fed from `tool/effect/particle_%d.txt`, reads each field one at a time with an
explicit width and storage cast (single bytes for the colour quad, `u16` for the integer fields,
`f32` for the float fields). That sibling parser is the authoritative width/type map; it is the same
record struct that ships in binary form inside `particleEmitter.eff`.

**Roles RESOLVED (CODE-CONFIRMED, 2026-06-21 — superseding the earlier "roles DBG-pending" note).**
A static read of the per-particle simulation kernel (the spawn routine and the per-tick integrator)
shows exactly how each field is consumed, so every field's role is now CODE-CONFIRMED. The key
structural finding: **a sub-record is NOT an interpolated keyframe — it is a per-particle
spawn-and-integration descriptor.** `num_frames` is the **particle count** (one particle per
sub-record), not a timeline length. Each particle is initialised from its sub-record (spawn delay,
lifetime, initial position/size/colour/velocity) and then advanced every fixed simulation step by
constant per-second rates (size rate, four signed colour rates, a velocity damping factor) via
stepwise Euler integration — there is no lerp/slerp over the array. The two f32 quads are therefore
**position xyz + size-rate** (+0x0C) and **velocity xyz + damping** (+0x24), and the four 16-bit
values at +0x1C..+0x22 are **signed per-second colour deltas**, not indices.

| Rec offset | Size | Type | Field | Notes | Confidence |
|-----------:|-----:|------|-------|-------|------------|
| +0x00 | 2 | u16 LE | `life_bonus` | Added once to the active-lifetime at init (`life += this`). | CODE-CONFIRMED |
| +0x02 | 2 | u16 LE | `lifetime` | Base active-lifetime set at (re)spawn; counts down each step. | CODE-CONFIRMED |
| +0x04 | 2 | u16 LE | `spawn_delay` | Initial / respawn delay set at (re)spawn; counts down, then the particle spawns. | CODE-CONFIRMED |
| +0x06 | 2 | u16 LE | `size_init` | Initial particle size, copied to the render state at spawn. | CODE-CONFIRMED |
| +0x08 | 1 | u8 | `color_r` | Initial red of the RGBA8 quad, copied to the render state at spawn. Default constructor zeroes it. | CONFIRMED |
| +0x09 | 1 | u8 | `color_g` | Initial green. Default constructor zeroes it. | CONFIRMED |
| +0x0A | 1 | u8 | `color_b` | Initial blue. Default constructor zeroes it. | CONFIRMED |
| +0x0B | 1 | u8 | `color_a` | **Initial alpha** (genuine alpha, NOT an "active = 0xFF" sentinel). Default constructor sets it to `0xFF`, giving a default of opaque black RGBA `(0, 0, 0, 255)`. | CONFIRMED |
| +0x0C | 4 | f32 LE | `spawn_pos_x` | Initial position X; the spawn position is offset by the emitter origin. | CODE-CONFIRMED |
| +0x10 | 4 | f32 LE | `spawn_pos_y` | Initial position Y. | CODE-CONFIRMED |
| +0x14 | 4 | f32 LE | `spawn_pos_z` | Initial position Z. | CODE-CONFIRMED |
| +0x18 | 4 | f32 LE | `size_rate` | Size growth per second: `size += this × dt` each step. | CODE-CONFIRMED |
| +0x1C | 2 | i16 LE | `color_r_rate` | **Signed** red change per second: `r += this × dt`. | CODE-CONFIRMED |
| +0x1E | 2 | i16 LE | `color_g_rate` | Signed green change per second. | CODE-CONFIRMED |
| +0x20 | 2 | i16 LE | `color_b_rate` | Signed blue change per second. | CODE-CONFIRMED |
| +0x22 | 2 | i16 LE | `color_a_rate` | Signed alpha change per second (alpha is then scaled by the global brightness option, §E.2.4). | CODE-CONFIRMED |
| +0x24 | 4 | f32 LE | `velocity_x` | Initial velocity X (units/second). | CODE-CONFIRMED |
| +0x28 | 4 | f32 LE | `velocity_y` | Initial velocity Y. | CODE-CONFIRMED |
| +0x2C | 4 | f32 LE | `velocity_z` | Initial velocity Z. | CODE-CONFIRMED |
| +0x30 | 4 | f32 LE | `velocity_damp` | Velocity scale applied each active step when non-zero (`velocity *= this`) — drag/acceleration. | CODE-CONFIRMED |

- **Sub-record stride:** 52 bytes (CONFIRMED).
- **Sub-record count source:** the entry header's `num_frames` (CONFIRMED).
- **RGBA8 colour quad at +0x08 (CONFIRMED).** Four single bytes `r, g, b, a`; the high byte is a
  genuine alpha channel, upgraded from MEDIUM to CONFIRMED this cycle. The default constructor proves
  it: it zeroes `r`/`g`/`b` and sets `a = 0xFF` (default opaque black). It is NOT an "active = 0xFF"
  flag.
- **Per-particle simulation (CODE-CONFIRMED).** At spawn, a particle's render state is set from
  `size_init`, the colour quad, and `spawn_pos` (offset by the emitter origin), and its velocity from
  the `velocity` triple; `lifetime`/`spawn_delay` arm its timers. Each fixed simulation step (~67 ms;
  see `specs/effects.md §11`): if `velocity_damp ≠ 0` the velocity is scaled by it, then position +=
  velocity × dt, size += `size_rate` × dt, and each colour channel += its signed rate × dt, with alpha
  finally scaled by the global brightness option (§E.2.4). When a particle's lifetime expires it
  respawns from the same sub-record (unless the effect is one-shot). A faithful re-implementation runs
  exactly this stepwise integration — it must NOT interpolate between sub-records.
- **Citation:** engineers cite this layout as `// spec: Docs/RE/formats/effects.md §E.2.2`.

### E.2.3 Trailing texture name (64 bytes / 0x40)

| Sub-offset | Size | Type | Field | Notes | Confidence |
|-----------:|-----:|------|-------|-------|------------|
| +0x00 | 64 | char[64] | `texture_name` | Read immediately after the sub-record array; NUL-padded ASCII/CP949. **The stored string is the FULL texture path, used verbatim** (loader-resolved, CAMPAIGN VFS-MASTERY): the texture manager looks it up by an **exact string compare** — no directory prefix is prepended and no extension is stripped or appended. The resolved handle is written back into the entry header's `tex_handle_slot` (0x14). A name that does not resolve falls back to a default texture slot. | CONFIRMED (loader-resolved) |

### E.2.4 Global brightness modulation of alpha (CODE-CONFIRMED)

After a particle's alpha rate is accumulated each step, the resulting alpha is scaled by a global
display-brightness factor derived from the client brightness option: the factor is `0.05 + 0.95 ×
(brightness_option / 100)`, clamped so that the lowest brightness setting drives the factor to 0. The
`0.05` floor is a fixed constant. This is the GPU-particle expression of the same "alpha scaled by a
global brightness option" behaviour noted for the `.xeff` path (`specs/effects.md §17.3`), now pinned
to this sub-system with the exact floor. A faithful re-implementation applies this alpha scale after
the per-step colour-rate accumulation, before the colour is written to the vertex diffuse.

## E.3 Known unknowns (Section E)

- **52-byte sub-record — WIDTHS AND ROLES RESOLVED (roles CODE-CONFIRMED 2026-06-21; see §E.2.2).**
  All 19 fields are now typed (4 × u16 + RGBA8 quad + 4 × f32 + 4 × u16 + 4 × f32) — the two prior
  "opaque slice" runs are fully decomposed (E.2.2), proven by the `tool/effect/particle_%d.txt` text
  serializer. The colour quad at +0x08 is CONFIRMED RGBA8 (genuine alpha). What remains open is the
  **semantic role** of the 18 non-colour fields: they are likely per-frame velocity / size / time /
  index by analogy to §A.16, but that is NOT proven and is **DBG-pending** (live-debugger
  confirmation against the runtime particle simulation required). Do not invent these roles.
- **`sprite_size_x` / `sprite_size_y` axis mapping.** CONFIRMED to feed the sprite-size setter; the
  "x then y" vs "width/height" naming is HIGH, not CONFIRMED.
- **On-disk values of the two overwritten dwords (0x14, 0x18).** Ignored on load; what the writer
  stores there (a live slot id vs a stale pointer) is MEDIUM and irrelevant to a faithful reader.

## E.4 Runtime selection — raw-id map lookup (CONFIRMED; CORRECTS the prior `−10000` guess)

A `.xeff` element whose `resource_id ≥ 10000` (Section A.4.0 / A.12) selects an entry in this table
**by raw `entry_id` equality**:

```
selected entry = the particleEmitter entry whose entry_id == resource_id   (raw, NO subtraction)
```

The bridge passes the `resource_id` **verbatim** as the map key. It does **NOT** compute
`resource_id − 10000`. The `≥ 10000` test is a **dispatch gate** (GPU-particle table vs the shared
mesh table), **not an index base**. This RETIRES the prior "record index = `resource_id − 10000`"
guess.

- A map **miss** (no entry with that `entry_id`) silently produces no particle system — the effect
  element renders nothing.
- The entry_ids stored in `particleEmitter.eff` are themselves authored in the `≥ 10000` space (which
  is why the first entry's id is 10001 — the value the flat model misread as "magic 0x2711").
- If a given file happened to store contiguous ids `10000, 10001, 10002, …`, then `index = id − 10000`
  would coincidentally work — but the client does a keyed map lookup, so a faithful port MUST key by
  raw `entry_id`, never subtract.

**Mesh-emitter counterpart (`resource_id < 10000`):** the same dispatch gate routes `resource_id`
below 10000 to a **shared mesh table** indexed by **direct index** at a **24-byte stride** (mesh
emitters, `emitter_type == 1`, §A.4.0 / §A.11). That shared mesh table is populated from the
`.xobj` ASCII files (§A.11). So the two arms are: `< 10000` → mesh table by direct 24-byte-stride
index; `≥ 10000` → this particle table by raw-id map lookup.

## E.5 Cross-reference

This file feeds the separate GPU particle sub-system (`ParticleEffectManager`). For the runtime
behavior of that sub-system (boot load sequence, spawn validation, GPU particle-buffer geometry), see
`specs/effects.md §11`. The `.xeff` → particle bridge and the `resource_id` dispatch gate are in
`specs/effects.md §17.2`. Engineers cite this section as `// spec: Docs/RE/formats/effects.md §E`.

---

# Section F: Effect-Link Tables — Gameplay ID → Effect ID

> **Confidence: SAMPLE-VERIFIED** for record structure, delimiter, encoding, and column positions
> of all three tables; **PLAUSIBLE** for individual column semantics. These are CP949 tab-delimited
> text tables (not binary) that wire gameplay identifiers (item slots, mob types, mugong/skill
> classes) to effect ids consumed by the effect registry (Section C.2). All three are confirmed
> present in the VFS and were measured by reading the real archive (added 2026-06-13).

## F.1 Identification and VFS Locations

All three tables, plus the related sword-light tables, live under `data/effect/` (NOT
`data/script/`, which the Cross-References list previously implied for some of them — corrected
2026-06-13; only `effectscale.xdb` is under `data/script/`).

| File | VFS path | Size | Records | Role |
|------|----------|-----:|--------:|------|
| `itemjointeff.txt` | `data/effect/itemjointeff.txt` | 550,065 B | 18,580 | Item slot → xeff effect id (`390xxxxxx`) |
| `mobjointeff.txt` | `data/effect/mobjointeff.txt` | 9,306 B | 414 | Mob type + slot → effect id (`360xxxxxx`) |
| `totalmugong.txt` | `data/effect/totalmugong.txt` | 11,692 B | 484 | Mugong (skill) class + slot → two effect ids (`12xxxxxxx`, `82xxxxxxx`) |
| `itemswordlight.txt` | `data/effect/itemswordlight.txt` | 145,350 B | 3,614 | Item id → sword-light blade texture/length |
| `mobswordlight.txt` | `data/effect/mobswordlight.txt` | 1,585 B | 39 | Mob type → sword-light blade texture/length |

**Common encoding (SAMPLE-VERIFIED):** CP949 text, TAB-delimited columns, CRLF line endings. The
first line of each file is a single integer giving the data-row count; data rows follow. (Sample
bytes were pure ASCII, but the files are CP949 because Korean names may appear in other rows.)

## F.2 `itemjointeff.txt` — Item Slot → Effect ID

**Confidence: SAMPLE-VERIFIED** structure; **PLAUSIBLE** semantics. Effect-id presence in the VFS
is **SAMPLE-VERIFIED** — col1 values resolve directly to `.xeff` filenames.

- **Line 1:** record count. Observed: 18,580.
- **Data rows:** six tab-separated columns.

| Column | Type | Observed values | Meaning (proposed) |
|--------|------|-----------------|--------------------|
| col0 | u32 | 1, 2, 3, … 200, 201, … 301 | Item slot / equip-position id |
| col1 | u32 | 390000011, 390000021, … (range `390000001`–`390000305`) | Effect id in the xeff registry |
| col2 | u32 | 0 | Flag (constant 0 in sample) |
| col3 | u32 | 0 or 1 | Flag (PLAUSIBLE: two-handed / off-hand applicability) |
| col4 | u32 | 1 | Flag (constant 1 in sample) |
| col5 | u32 | 2 | Field (constant 2 in sample) |

**Key cross-link (SAMPLE-VERIFIED VFS presence):** col1 ids in the `390xxxxxx` range match numeric
`.xeff` filenames directly, e.g. `390000011` → `data/effect/xeff/390000011.xeff` (file presence
CONFIRMED in the VFS). This range is the wearable/item-equip effect group (25 numeric files,
Section A.3). For this range the filename-equals-`effect_id` convention (Section A.2) holds, so the
registry can resolve these ids either by filename or by the `.xeff` header's `effect_id` field.

## F.3 `mobjointeff.txt` — Mob Type + Slot → Effect ID

**Confidence: SAMPLE-VERIFIED** structure; **PLAUSIBLE** semantics. **The col2 effect ids are
HEADER `effect_id` values, NOT filenames** (SAMPLE-VERIFIED: no matching numeric `.xeff` file
exists in the VFS).

- **Line 1:** record count. Observed: 414.
- **Data rows:** six tab-separated columns.

| Column | Type | Observed values | Meaning (proposed) |
|--------|------|-----------------|--------------------|
| col0 | u32 | 1 | Mob-type index (all 1 in sample) |
| col1 | u32 | 1, 2, 3, … | Mob slot / skill slot within the type |
| col2 | u32 | 0, 360000605, 360001405, 360001705, 360001805, 360002005, 360002105, 360002205, 360002305 | Effect id, primary (0 = none) |
| col3 | u32 | 0, 4, 6, 9, 19 | Secondary index or bone-attachment slot (PLAUSIBLE) |
| col4 | u32 | 1 | Flag (constant 1 in sample) |
| col5 | u32 | 1 | Flag (constant 1 in sample) |

**Resolution note (SAMPLE-VERIFIED):** col2 ids lie in the `360xxxxxx` mob-effect range, but the
VFS contains **no** file named e.g. `data/effect/xeff/360000605.xeff` (checked). These ids must
therefore be resolved by the **`effect_id` header field** inside `.xeff` files, not by filename —
i.e. through the effect registry's id keying (Section C.2), not a path lookup.

## F.4 `totalmugong.txt` — Mugong (Skill) Class + Slot → Two Effect IDs

**Confidence: SAMPLE-VERIFIED** structure; **PLAUSIBLE** semantics. **Neither effect-id column
matches any `.xeff` filename** (SAMPLE-VERIFIED) — both must be resolved by the `effect_id` header
field through the registry.

- **Line 1:** record count. Observed: 484.
- **Data rows:** four tab-separated columns.

| Column | Type | Observed values | Meaning (proposed) |
|--------|------|-----------------|--------------------|
| col0 | u32 | 0, 1, 2, 3, … | Mugong (skill) class index |
| col1 | u32 | 1, 2, 3, 4, … | Skill slot within the class |
| col2 | u32 | 0, 121100040, 121113040, … (`12xxxxxxx` range) | Primary effect id (0 = no effect) |
| col3 | u32 | 0, 821100003, 821400003, … (`82xxxxxxx` range) | Secondary effect id (0 = no effect) |

**Resolution note (SAMPLE-VERIFIED):** the col2 (`12xxxxxxx`) and col3 (`82xxxxxxx`) ids do **not**
correspond to any numeric `.xeff` filename in the VFS. The registry must resolve them by the
`effect_id` field stored in the `.xeff` header (Section C.2), not by constructing a path from the
numeric id. This is the strongest VFS-confirmed case that the effect registry is keyed on the
header `effect_id`, independent of filename, and it generalises the resolution rule used by
`mobjointeff.txt` (F.3).

This table is also a runtime data source for a separate timing-precise martial-arts SFX overlay
(loaded into an effect-manager map keyed by an animation-frame slot); that audio behaviour is out
of scope for this format spec — see `specs/effects.md`. Here it is documented purely as an
effect-id link table.

## F.5 Companion Sword-Light Tables (overview)

`itemswordlight.txt` (3,614 rows) and `mobswordlight.txt` (39 rows) share the same CP949 / TAB /
CRLF convention and a leading record-count line. They map an item id (col0, `items.csv` id domain)
or a mob type (col0) to a **blade-trail texture name** (final string column, resolving to
`data/effect/texture/<name>.tga`, the same texture path as `.xeff` name-table entries) plus a
floating-point blade length/offset column and several still-unresolved flag columns. These feed the
sword-light renderer, which is a distinct sub-system; their full column semantics are PLAUSIBLE
only and the renderer itself is specified in `specs/effects.md`. They are listed here for
completeness as members of the effect-link family.

## F.6 Effect-ID Resolution Rule (summary)

| Source table | Effect-id range | Matches `.xeff` filename? | Registry resolves by |
|--------------|-----------------|---------------------------|----------------------|
| `itemjointeff.txt` | `390xxxxxx` | **Yes** (SAMPLE-VERIFIED) | filename or header `effect_id` (equivalent for this range) |
| `mobjointeff.txt` | `360xxxxxx` | **No** (SAMPLE-VERIFIED) | header `effect_id` field only |
| `totalmugong.txt` | `12xxxxxxx`, `82xxxxxxx` | **No** (SAMPLE-VERIFIED) | header `effect_id` field only |

The practical consequence for an implementer: the effect registry **must** index `.xeff`
descriptors by their header `effect_id` (Section A.2) and resolve link-table ids against that
index. A filename-only resolver would succeed for `itemjointeff.txt` but silently fail for
`mobjointeff.txt` and `totalmugong.txt`. The reverse mismatch — duplicate `effect_id` values shared
by multiple files (Section A.2; 47 such ids) — interacts with this rule: the registry keeps exactly
the **first** file listed in `xeffect.lst` for each id (FIRST-WINS / reject-on-insert, §A.2 / §C.2,
Open Question #7 CLOSED), so a link-table id that collides resolves to that single winning descriptor.

## F.7 Loader-schema confirmation (Campaign 5B) — text token tables, key resolution

> **Confidence: loader read-order CONFIRMED.** This subsection refines the per-table schemas of F.2,
> F.3, and F.4 against the actual readers, and replaces the speculative fixed-binary / column models
> where they conflicted. The three tables share a **text, whitespace/token-delimited** reader (the
> same mixed text/binary disk reader used elsewhere): every field is parsed as an integer or float
> **token**, not a fixed byte offset. So the "columns" of F.2–F.4 are *logical tokens per record*.
> The first token of each file is a **record count** (an int). Two distinct joint-effect managers
> exist — call them the **item manager** and the **mob manager** — and the load-bearing fact is
> which file populates which, and which key is catalog-resolved.

### F.7.1 Manager wiring (CONFIRMED)

- `itemjointeff.txt` populates the **item joint-effect manager**.
- `mobjointeff.txt` populates the **mob joint-effect manager**.
- (If any prose elsewhere implies the reverse, this is authoritative — they are not interchangeable.)

### F.7.2 Key resolution (CONFIRMED, load-bearing)

| Table | Outer key | Catalog-resolved? |
|---|---|---|
| `itemjointeff.txt` | the **raw** group id (first token) | **No** — keyed directly on the raw group id. |
| `mobjointeff.txt` | `offset + AnimCatalog[(class + 1)]` | **Yes** — outer key is catalog-resolved from the `(class, offset)` token pair. |
| `totalmugong.txt` | `field2 + AnimCatalog[field1 + 1]` | **Yes** — the stored key is catalog-resolved. |

`AnimCatalog` here is the animation-catalog singleton (the same catalog used for skeleton / weapon-hand
resolution elsewhere). Its **internal array layout is UNRESOLVED** (only the `[index]` access shape is
known), so a faithful port must reproduce the catalog first to compute the mob/mugong keys exactly.

### F.7.3 Per-record token schemas (CONFIRMED)

- **`totalmugong.txt`** — `count` (int), then per record **4 int tokens**: `field1` (the class used as
  `AnimCatalog[field1 + 1]`), `field2` (the offset added to the catalog base), and two further ints
  stored in the node. The stored key is `field2 + AnimCatalog[field1 + 1]`.
- **`itemjointeff.txt`** — `count` (int), then per record **6 tokens**: `group_key` (int, the raw
  outer key), `effect_id` (int; **`effect_id == 0` skips the record entirely**), two further ints, one
  **float** token, and a final int stored as a single byte. Inner node = `{effect_id, int, int, float,
  byte}`.
- **`mobjointeff.txt`** — `count` (int), then per record **6 tokens**: a `(class, offset)` int pair that
  forms the catalog-resolved outer key (F.7.2), then an inner node of `{int, int, float, byte}`.

These supersede the F.2–F.4 "u32 column" descriptions where they differ: the fields are text tokens,
the joint tables carry an explicit float token and a byte-stored final int, and the mob/mugong outer
keys are catalog-resolved (not raw). The F.6 resolution rule (registry keyed by header `effect_id`)
is unchanged and reinforced.

---

## Terrain FX Layer Formats — Cross-Format Deltas

> **Status (build 263bd994, 2026-06-16): OUT-OF-LANE — re-confirmation deferred to the terrain lane.**
> The `.fx1`–`.fx7` layer loaders are NOT reachable from any `.fx` / `fx%d` / `%s.fx` format string in
> the binary; the per-cell base path is built by the **terrain** subsystem (which appends the layer
> extension), not the effect subsystem. The `.fx` `type_tag == group_count` / per-group-header correction
> recorded below was **CODE-CONFIRMED + 595-file census SAMPLE-VERIFIED in a prior campaign**, but it was
> **NOT re-walked in this (effects/shaders) lane** to avoid asserting a layout outside this lane's
> two-witness scope. For build 263bd994 this claim is therefore **[capture/debugger-pending → terrain]**:
> authoritative ownership stays with `terrain_layers.md` + the terrain analyst. The notes below are
> retained as the prior-campaign record; do NOT treat them as re-verified on this build.

The per-cell terrain layer mesh files (`.fx1` through `.fx7`) are documented in full in
`Docs/RE/formats/terrain_layers.md §Section 1`. That file is authoritative for the FX format
byte layouts; do NOT duplicate its tables here.

This section records only **new sample observations** from the June 2026 black-box pass that
extend or correct the `terrain_layers.md` record:

### FX2 Header Field[3]: CONFLICT RESOLVED (corrected 2026-06-13)

`terrain_layers.md §1.6` states that the `render_state` field at header offset `0x0C` has value
`15 (0x0F)` for FX2, identical to FX1. The June 2026 black-box pass observed **value 50** in
one FX2 sample (`d001x9990z10000.fx2`). These two values contradicted each other.

**(corrected 2026-06-13: the conflict is now RESOLVED — the field is per-group and VARIABLE, not
constant.)** Two independent lines of evidence agree:

1. **Structural (CODE-CONFIRMED).** A trace of the FX1 and FX2 binary loaders shows the byte at
   file offset `0x0C` is NOT a file-level header field at all. It is the third u32 of a **20-byte
   per-group header** (see the new "FX1/FX2 corrected binary layout" subsection below). The file
   begins with a 4-byte `group_count`, after which each group carries its own 20-byte header; the
   field formerly read as the constant `render_state` is the per-group field at group-relative
   offset `+0x08`. Its value is not mandated to be 15.
2. **Corpus census (SAMPLE-VERIFIED, 595 FX2 files).** The field takes **seven** distinct values
   across the full FX2 corpus. The modal value is 50 (0x32) at 80.2% of files; value 15 (0x0F)
   appears in only 35 files (5.9%). Full distribution:

   | Value | Hex | FX2 file count | Fraction |
   |------:|-----|---------------:|---------:|
   | 0  | 0x0000 | 5   | 0.8% |
   | 10 | 0x000A | 17  | 2.9% |
   | 15 | 0x000F | 35  | 5.9% |
   | 20 | 0x0014 | 17  | 2.9% |
   | 30 | 0x001E | 22  | 3.7% |
   | 40 | 0x0028 | 22  | 3.7% |
   | 50 | 0x0032 | 477 | 80.2% |

Both the IDA-traced value (15) and the June 2026 single-sample value (50) are valid observations
of the same variable field in different cells. The earlier committed claim — that FX2 field[3]
equals `15`, "same as FX1" — is **WRONG** and is corrected here.

**The same variability holds for every other FX layer measured** (SAMPLE-VERIFIED):

| Extension | Files measured | Distinct field[3] values | Modal value (share) | Committed-spec value | Spec value's share |
|-----------|---------------:|-------------------------:|---------------------|---------------------:|-------------------:|
| FX1 | 226 | 13 | 0 (22.6%) | 15 | 35 files / 15.5% |
| FX2 | 595 | 7  | 50 (80.2%) | 15 | 35 files / 5.9% |
| FX3 | 160 | 7  | 400 (36%) | 5 | 24 files / 15% |
| FX5 | 89  | 6  | 300 (~63%) | — | — |
| FX6 | 6   | 1  | 0 (100%) | — | — |
| FX7 | 2   | 1  | 0x42F73439 = f32 123.602 | — | — (distinct header; see below) |

FX1 distinct values span `{0, 2, 15, 20, 30, 60, 70, 100, 110, 118, 120, 300, 400}`; FX5 values
are larger, `{100, 118, 120, 300, 400, 450}`. In every case the value the prior spec cited as a
constant is a **minority** observation, not the mode.

**Plausible interpretation (PLAUSIBLE — not confirmed by a render-path trace):** the field is a
**LOD / render-distance threshold in world units**, not a render-state enum or a format-version
constant. Supporting evidence: the values are sparse round integers; larger FX types (FX5,
multi-section large overlays) carry systematically larger values (100–450) than small FX types
(FX2, small quad overlays, mode 50); FX6 is uniformly 0 (possibly "no LOD / always render"). A
render-state-index reading is also consistent with the small discrete value set. The field is
neither a binary flag (too many values), a format-version constant (it varies within a single
extension), nor padding (the non-zero values are clearly intentional). For FX7 the bytes at
`0x0C` decode as an f32 (123.602), not a uint, consistent with FX7's separate header layout.

Engineering guidance: do NOT treat this field as a constant 15. Read and expose it per group; do
not branch on its value until the interpretation is confirmed by a render-path trace.

### FX1/FX2 Corrected Binary Layout (corrected 2026-06-13)

> **CODE-CONFIRMED** layout and size formulae; CAPTURE-UNVERIFIED is not applicable (this is a
> file format, not a wire protocol). Field-A/B/C semantics remain UNRESOLVED.

The prior FX1/FX2 description (in `terrain_layers.md §1.6`) treated the file as opening with a
**24-byte constant header** of the form
`[type_tag=1][unknown_1=1][unknown_2=0][render_state=15][mesh_count][index_count]`. A trace of the
two binary loaders (the FX1 layer loader and the FX2 layer loader) shows this was a structural
**misidentification (corrected 2026-06-13)**. The real layout is:

- The file begins with a **4-byte `group_count`** at file offset `0x00` (the outer loop count).
- Each of `group_count` groups then carries a **20-byte per-group header**, immediately followed
  by that group's vertex array and index array.
- There is no constant 24-byte file header and no single file-level `render_state` field.

**Per-group header (20 bytes):**

| Group-relative offset | File offset (group 0) | Size | Type | Field | Prior (WRONG) label | Notes |
|----------------------:|----------------------:|-----:|------|-------|---------------------|-------|
| +0x00 | 0x04 | 4 | u32 LE | `field_A` | `unknown_1 = 1` | Observed 1 in samples; VARIABLE across corpus; semantics UNRESOLVED |
| +0x04 | 0x08 | 4 | u32 LE | `field_B` | `unknown_2 = 0` | Observed 0 in samples; semantics UNRESOLVED |
| +0x08 | 0x0C | 4 | u32 LE | `field_C` | `render_state = 15` | Per-group, VARIABLE (see field[3] census above); semantics UNRESOLVED |
| +0x0C | 0x10 | 4 | u32 LE | `vert_count` | `mesh_count` | Number of vertex records in this group |
| +0x10 | 0x14 | 4 | u32 LE | `idx_count` | `index_count` | Number of u16 indices in this group |

Note the layout consequence: what the prior spec called `type_tag` at file offset `0x00` is the
`group_count` outer loop count, and the per-group fields begin at file offset `0x04`. The
"field[3]" referenced throughout the census above is `field_C` (group-relative `+0x08`,
file offset `0x0C` for group 0).

**(corrected 2026-06-13: the `type_tag` "constant = 1" claim is also WRONG.)** The field at file
offset `0x00` — now identified as `group_count` — is likewise VARIABLE, not the constant `1` the
prior description implied. Across the corpus, FX1 file offset `0x00` takes 27 distinct values
(range 1–61) and FX2 takes 11 distinct values (range 1–16). Value 1 is the most common but far
from the only value. PLAUSIBLE reading: this is a genuine per-file group count (a cell may carry
several overlay mesh groups), which also re-explains why a "type_tag" appeared to vary.

**Vertex and index arrays (per group, immediately after the 20-byte header):**

- `vert_count` vertex records, then `idx_count` u16 indices.
- **Vertex stride is 36 bytes for FX1** (`VF_36`) and **44 bytes for FX2** (`VF_44` = `VF_36`
  plus an extra 8 bytes carrying a second UV pair). This is the structural difference between the
  two extensions.
- Indices are a flat u16 triangle list.

**File-size formula (CODE-CONFIRMED, byte-exact on samples):**

```
FX1: total = 4 + group_count × (20 + vert_count × 36 + idx_count × 2)
FX2: total = 4 + group_count × (20 + vert_count × 44 + idx_count × 2)
```

Worked examples with zero residual bytes:
- FX1 `group_count=1, vert_count=3, idx_count=3`: `4 + (20 + 3×36 + 3×2) = 4 + 20 + 108 + 6 = 138 B`.
- FX2 `group_count=1, vert_count=3, idx_count=3`: `4 + (20 + 3×44 + 3×2) = 4 + 20 + 132 + 6 = 162 B`.

`terrain_layers.md §1.6` should be corrected to match this group-based layout; this file records
the correction. The semantic meaning of `field_A`, `field_B`, and `field_C` is stored in the
per-group in-memory record but no downstream consumer reading them for dispatch was traced — they
remain UNRESOLVED (see Open Questions).

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

1. **`field_unknown_a` / `element_flags` (element/in-memory +0x0C)** — bulk-read, no semantic read-site isolated. **DBG-pending** (live-debugger confirmation required). Do not assign a meaning.
2. **`type_flag` — RETIRED (2026-06-14).** The value at file offset 0x08 is NOT a header field; it is sub-effect block 0's `emitter_type` (values 1 = mesh-particle, 2 = directional billboard; A.2 / A.4.0 / A.12). No open question remains.
3. **`unknown_constant = 67` — REFUTED and DELETED (CAMPAIGN VFS-MASTERY).** No read-site consumes a 4-byte field at track-header offset +1, and the two-witness review found no such header member. The four bytes are keyframe 0's u32 index prefix (A.4.3 / A.4.4). The track header is 9 bytes; the field no longer appears in this spec. No open question remains.
4. **`emitter_type` values beyond 0, 1, 2 (e.g. `20`)** — parse-inert (only `== 2` gates the static-branch rotation read, A.4.6 / A.12); the **render meaning is DBG-pending** (live-debugger confirmation required). Do not branch on these values in the parser and do not assign a render meaning.
5. **`tex_count` low byte as UV-scroll flags (A.13)** — MEDIUM confidence; dual use of frame count as flags is unusual.
6. **In-memory dword[19] dual use (`start_time_or_total`)** — parser writes `total_time`; tick reads it as the spawn timestamp. Likely overwritten at spawn; UNCONFIRMED.
7. **`effect_id` duplicate resolution — RESOLVED / CLOSED (FIRST-WINS, CYCLE 7).** 47 `effect_id`
   values are shared by more than one file (SAMPLE-VERIFIED). The tie-break is **first-wins /
   reject-on-insert**: the registry is a no-overwrite ordered map keyed by the bare 4-byte
   `effect_id` (one node per id), so the first `.xeff` listed in `xeffect.lst` for an id is registered
   and kept, while every later same-id file is rejected at insert (logged "same effect", its
   descriptor destroyed) and is unreachable at runtime. It is NOT last-wins; there is no override and
   no bucket/list; the key is the bare scalar `effect_id`, not a composite. The earlier guess that
   "the second-registered file may silently overwrite the first" was wrong. See §A.2 and §C.2. No open
   question remains.
8. **9-digit naming scheme `[CCC][SSS][AB][N]`** — PLAUSIBLE from pattern observation; no manifest confirms the digit-group semantics.
9. **Keyframe index prefix — RESOLVED (CAMPAIGN VFS-MASTERY).** The earlier "frame 0 has no index prefix (36 B)" reading is corrected: every animated keyframe, frame 0 included, is `u32 index + 9 × f32` = 40 B (A.4.4). The parser reads `tex_count` uniform 40-byte entries with no frame-0 special case. No open question remains.
10. **High-`sub_effect_count` front-end files — RESOLVED (2026-06-14).** `char_select-u.xeff` (68 sub-effects, block-0 `tex_count` = 2) and `zone_sel_u.xeff` / `zone_sel2-u.xeff` (11 sub-effects, block-0 `tex_count` = 20) now parse successfully. Root cause was a mislabelled header: the header is 8 bytes and EVERY block (block 0 included) is parsed by the same element read sequence — a 24-byte fixed head (A.4.0) then the variable body — with each block's entry count being its own `tex_count` at element offset +0x14. There is no header `first_entry_count` and no distinct per-block prefix.
11. **Anti-magic sentinel `0x46464558` (A.1, A.14) — carry-forward.** The CYCLE 12 Block C static re-walk of the boot loader and the register-from-file helper did NOT encounter this constant as a code immediate — the helper simply stores the first u32 as the `effect_id` without any sentinel comparison. The comparison, if present, must reside in the lazy body parser (not traced this pass). Status: NOT RE-CONFIRMED this session. The prior CONFIRMED status (§A.1, §A.14) is carried forward from earlier sessions. Scheduled for re-confirmation in the next body-parser trace.
12. **`xobj.lst` name resolution** — the 34-byte records carry numeric/control-char stems in the build 263bd994 sample; the full path built from each record name is presumed to be `data/effect/xobj/<name>` by analogy with the sibling `.lst` files, but the boot loader read-site for `xobj.lst` was not traced this pass. **DBG-pending** (body-parser / boot-path trace required).

## From `.eff` geometry (Section B)

1. **Parser function for `.eff` obj files** — no dedicated loading function identified by string reference.
2. **Maximum `vert_count` limits** — a related mesh parser warns above 3072 vertices; whether this parser enforces a similar limit is unknown.
3. **`tex_u` / `tex_v` purpose** — present, but whether the effect pipeline binds a texture to these shapes or UVs are unused is unknown.

## From the runtime (Section C)

1. **A fourth and possibly a sixth effect pool** — torn down at shutdown alongside the three known instance pools; class names not identified.
2. **Bone-source action tables (modes 1/2)** — layout and source (skill table? animation events?) unconfirmed.
3. **5/139 template fields and 5/14 `param1`/`param2`** — meaning not resolved.

## From `effectscale.xdb` (Section D)

1. **Application site — RESOLVED (two-witness, build 263bd994).** The override is applied at parse
   time inside the `.xeff` body parser, before the descriptor is marked loaded, and it **REPLACES**
   (overwrites) the descriptor base-scale ctor default of `1.0` — it does NOT stack/multiply with it.
   The per-instance spawn `effectscale` argument (§A.16 `+0x48`) then multiplies the resolved base.
   No open question remains. (See Section D.)

## From `particleEmitter.eff` (Section E)

1. **52-byte sub-record — widths RESOLVED (CYCLE 7), roles DBG-pending.** All 19 fields are now typed
   (4 × u16 + RGBA8 quad at +0x08 + 4 × f32 + 4 × u16 + 4 × f32), proven by the
   `tool/effect/particle_%d.txt` text-serializer sibling; the colour quad is CONFIRMED RGBA8 with a
   genuine alpha byte (E.2.2). The remaining gap is the **semantic role** of the 18 non-colour fields,
   which needs live-debugger confirmation against the runtime particle simulation and must not be
   invented. This is the primary remaining gap.
2. **Entry-header sprite-size axis mapping** — the two header f32 at 0x08/0x0C feed the sprite-size setter (CONFIRMED), but "x then y" naming is HIGH only.
3. **(RESOLVED, Campaign 5B)** The prior "flat 16-byte header + 2,243×52-byte records" model and the "record index = `resource_id − 10000`" guess are both RETIRED. The file is a variable-length entry sequence terminated by `num_frames == 0`, keyed by raw `entry_id`; selection is by raw-id map lookup against a `.xeff` element's `resource_id` (E.0, E.2, E.4).

## From terrain FX deltas

1. **FX2 field[3] conflict (15 vs 50)** — RESOLVED 2026-06-13: the field is per-group and VARIABLE
   (7 values across 595 FX2 files; mode 50, not 15). Remaining open: the **semantic meaning** of
   `field_A`/`field_B`/`field_C` in the per-group header. The LOD / render-distance-threshold
   reading (PLAUSIBLE) is consistent with the corpus statistics but is not confirmed by a
   render-path trace — no downstream consumer reading these fields for dispatch was located.
2. **FX field[0] (`group_count`) semantics** — corrected 2026-06-13 from "constant = 1" to a
   variable per-file count (FX1: 27 distinct values; FX2: 11). Whether it is strictly a mesh-group
   count or also selects a rendering sub-path is PLAUSIBLE only.
3. **FX7 full field layout** — only the position/normal offset divergence from FX1–FX5 is confirmed;
   complete header and vertex format mapping awaits a parser trace. Its field at `0x0C` decodes as
   an f32 (123.602), not a uint, in both (byte-identical) FX7 files.
4. **FX4 single-instance prevalence** — only one FX4 file exists in the full VFS (43,347 files). The
   reason for this is unknown; it may be a deprecated or experimental format.

## From effect-link tables (Section F)

1. **Effect-id resolution path** — col2/col3 ids of `mobjointeff.txt` and `totalmugong.txt` have no
   matching `.xeff` filename, so the registry must resolve them by the header `effect_id` field
   (Section C.2). The exact lookup code path was not traced; SAMPLE-VERIFIED only that the filenames
   do not exist for these ranges. How a `12xxxxxxx`/`82xxxxxxx`/`360xxxxxx` id finds its descriptor
   (linear scan of `effect_id` fields vs a secondary index) is the most important open linkage gap.
2. **`itemjointeff.txt` col2–col5** — constant in the sample (0/0-1/1/2); their flag meanings (e.g.
   col3 as a two-handed / off-hand applicability flag) are PLAUSIBLE only.
3. **`mobjointeff.txt` col3** — values 4, 6, 9, 19; possibly a secondary effect index or bone slot.
4. **`totalmugong.txt` col0** — whether the mugong class index aligns with the `[CCC]` prefix of the
   9-digit `.xeff` naming scheme (Section A.3) is unconfirmed.
5. **`xeffect.lst` vs VFS count** — the manifest records 3,669 name entries but only 3,584 `.xeff`
   files exist (85-entry surplus). May be stale manifest rows or names served from a secondary
   archive.

---

# Cross-References

- **Related formats:** `pak.md` (container), `sound_tables.md` (the other `.eff` variant), `mesh.md` (shares 32-byte vertex record convention), `terrain_layers.md` (FX layer formats; authoritative for `.fx1`–`.fx7` byte layouts)
- **Related runtime spec:** `specs/effects.md` — the authoritative effects system behavioral spec (boot manifests, object pools, trigger dispatch table, per-frame tick math, bone attachment, damage-number renderer, sword-light sub-system)
- **Related specs:** `specs/combat.md` (server-authoritative damage; effects here are presentation-only), `specs/skinning.md` (bone hierarchy used by bone-attached effects)
- **Companion binary manifests:** `xeffect.lst` (3,669 records × 30 bytes, §A.9), `bmplist.lst` (1,526 records × 30 bytes, §A.10), `xobj.lst` (32 records × 34 bytes, §A.11.1) — all at `data/effect/`; see `specs/effects.md §3` for boot loading sequence
- **Companion plain-text tables:** `totalmugong.txt`, `itemjointeff.txt`, `mobjointeff.txt`, `itemswordlight.txt`, `mobswordlight.txt` — documented in **Section F** above (all under `data/effect/`, corrected 2026-06-13 from the prior `data/script/` implication; only `effectscale.xdb` is under `data/script/`)
- **Companion ASCII format:** `.xobj` files in `data/effect/xobj/` — plain-text primitive meshes
- **Companion binary tables:** `effectscale.xdb` (Section D), `particleEmitter.eff` (Section E)
- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
