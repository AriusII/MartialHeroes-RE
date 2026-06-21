# Format: actormotion.txt  (per-actor motion / animation table — 136-byte record)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary code addresses. Consumed by `Assets.Parsers`. Every offset an engineer cites must
> reference this file.
>
> verification: confirmed-by-use-site (slot roles, rate semantics, motion_ids_b consumer) + sample-verified (col2 / motion-id slots) + confirmed (read-order, offsets, key math, rate math)
> ida_reverified: 2026-06-19; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
>
> **CORRECTED CYCLE 7 (ida_anchor 263bd994, 2026-06-20):** B-array slot labelling de-shifted to the
> loader's own indexing — **death effect/sound = b[4] = +0x74** (was mislabelled "b[5]"), dead trailing
> slots = b[5..8] (+0x78..+0x84); both 9-slot arrays share the **element-0-unused** convention
> (consumers start at element 1). A-array column numbers made explicit: the **runtime stand idle =
> a[1] = +0x44 = COLUMN 16** (col15 → a[0] is file-loaded but dead at runtime). The runtime idle lookup
> is keyed by the actor **appearance key** (`5·(class+4·variant)−24`), not col2/skin_class, and the
> motion id resolves through the `motlist.txt` clip registry (id == `.mot` header id_b) — no
> `g{id}.mot` sprintf.
>
> **CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19):** 9-slot direction reading **REFUTED**
> (the two arrays are **action/lifecycle-keyed**, not direction-indexed); `motion_ids_b` =
> **SFX/FX event ids NOT secondary motion** (deliberate spec reversal, the binary won);
> `rate_x` / `rate_y` = **movement speed** (resolves the former rate open-question).
>
> This document resolves the open question on the actormotion column semantics (formerly OQ-5):
> the full 136-byte (0x88) per-record layout, the int-vs-float column types, the two 9-element
> motion-id sub-arrays, the per-frame rate fields, and the computed map key.
>
> **Re-confronted on build `263bd994` against the real VFS sample (`data.vfs`):** the IDB loader
> **fully confirms** this doc's read-order and offset map (this is the more precise of the two
> committed docs for the in-memory record), and the sample corroborates the join columns. No layout
> drift. The slot ROLES below are now **confirmed by their runtime use-sites** (each slot is read at a
> fixed offset chosen by the actor's action/lifecycle state — idle, walk, run, death, spawn, footstep —
> never by a facing/direction value).
>
> **Cross-doc naming:** both this doc and `formats/animation.md` use **`motion_ids_a` /
> `motion_ids_b`** for the two 9-slot arrays. Note `formats/animation.md` still describes
> `motion_ids_b` as "secondary motion" ids — that reading is **superseded here** (see the
> `motion_ids_b` correction below); the binary's consumers feed `motion_ids_b` to the sound/effect
> routers, never to the animation mixer.

## Identification

- **Logical path:** `data/char/actormotion.txt`
- **Found in:** the `.pak` archive / loose client tree (see `formats/pak.md`)
- **Container family:** a **dual text/binary table**. The file as shipped is a whitespace-delimited
  **text** table, but the parser is written against a reader that can also consume a binary form of
  the same records (see below). The int/float typing below is the **authoritative column type**
  regardless of which form is read.
- **Endianness (binary form):** little-endian.
- **Encoding (text form):** CP949 (Korean) — consistent with all other game text tables.

## Reader model — dual text / binary (CONFIRMED)

The table is opened in a mode that selects a **per-field text-or-binary reader**. Two field readers
are used, both branching on a "text mode" flag on the file object:

- **Integer field reader:** in text mode, read the next whitespace-delimited token and parse it as a
  signed decimal integer; in binary mode, read a raw 4-byte little-endian value. Used for the **int
  columns** below.
- **Float field reader:** in text mode, read the next whitespace-delimited token and parse it as a
  floating-point value; in binary mode, read a raw 4-byte little-endian value. Used for the **float
  columns** below.

Because the shipped file is text, each "field" is one whitespace-delimited **column**; the parser
reads a fixed number of columns per row in a fixed order. The int-vs-float distinction below is the
authoritative column type (integer columns go through integer parsing, float columns through
floating-point parsing).

## File structure

```
count            (one leading integer column: number of records that follow)
record[0]        (a fixed sequence of columns, see "Per-record layout")
record[1]
...
record[count-1]
```

- **Record count source:** a single leading integer column at the top of the file (read before the
  record loop). It is the count of records, not a byte size.
- **Declared vs parsed count (SAMPLE-VERIFIED, build `263bd994`):** the line-1 count declares **1084**
  records, but the production parser yields **1080** parsed rows (the sample file has exactly 1080
  non-blank record lines, 1081 total lines). The 4-row difference is benign — blank/structural
  separator lines silently skipped by the parser's record guard. Parsers should **not** trust the
  declared count as the exact iteration bound and should tolerate skipped/blank lines.
- **Column count is uniform:** every data row has exactly **33** whitespace-delimited columns
  (SAMPLE-VERIFIED across the whole file).
- **In-memory record size:** each record is materialised into a **136-byte (0x88)** structure.
- **Records are inserted into an ordered map** keyed by the computed `motion_key` field (record
  offset 0x00); see "Computed lookup key".

## Per-record layout (in-memory record, 136 bytes / 0x88)

The table below is in **record-offset order**. The **read-order column** notes which text column
supplies each field — note that the read order interleaves two fields (the two divisors at +0x28 /
+0x2C) earlier in the column stream than their record-offset position, so the literal text-column
index is not strictly ascending with record offset. The two leading columns of every record
(`col0`, `col1` below) are **key inputs**: they are consumed to compute the `motion_key` field and
are **not** stored verbatim as their own record fields.

Key-input columns (consumed, not stored as standalone fields):

| Read column | Role | Notes |
|------------|------|-------|
| col0 | category / direction selector | used as `(uint8)(col0 + 1)` to index a per-category base table on the parse object |
| col1 | intra-category offset | added to the base-table lookup to form `motion_key` |

Stored record fields:

> The **Read column** below is the literal text-column index that supplies each field, re-confirmed
> operand-for-operand against the IDB loader on build `263bd994`. Note the two divisors (col4, col6)
> are read **interleaved early** — col4 lands at +0x28 and col6 at +0x2C even though they appear in the
> column stream before col5/col7. Every destination offset is exact.

| Rec off | Size | Type | Read column | Field (proposed) | Notes | Confidence |
|--------:|-----:|------|------------|------------------|-------|------------|
| 0x00 | 4 | u32 | (computed) | `motion_key` | `= col1 + base_table[(uint8)(col0 + 1)]`; the ordered-map insertion key | HIGH layout / MED meaning |
| 0x04 | 4 | i32 | col2 | `int_a` (= `skin_class`) | SkinClassId — actor-to-skeleton key; joins to `data/char/bind/g<skin_class>.bnd`. 0 = null skeleton. **SAMPLE-VERIFIED** (95.8% of rows resolve to an existing `.bnd`). | HIGH layout / HIGH meaning (sample-verified) |
| 0x08 | 4 | f32 | col3 | `rate_src_x` | numerator of the per-frame **movement speed** at +0x30 (raw walk/run speed magnitude before /divisor) | HIGH layout / MED meaning |
| 0x0C | 4 | f32 | col5 | `rate_src_y` | numerator of the per-frame **alt movement speed** at +0x34 | HIGH layout / MED meaning |
| 0x10 | 4 | i32 | col7 | `int_b` | integer field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x14 | 4 | f32 | col8 | `float_c` | float field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x18 | 4 | f32 | col9 | `float_d` | float field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x1C | 4 | f32 | col10 | `float_e` | float field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x20 | 4 | f32 | col11 | `float_f` | float field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x24 | 4 | f32 | col12 | `float_g` | float field — no static read-site off the record; UNRESOLVED | HIGH layout / UNRESOLVED meaning |
| 0x28 | 4 | i32 | col4 | `divisor_x` | per-loop divisor for `rate_x`; **forced to 1 if read as 0**; read interleaved early | HIGH |
| 0x2C | 4 | i32 | col6 | `divisor_y` | per-loop divisor for `rate_y`; **forced to 1 if read as 0**; read interleaved early | HIGH |
| 0x30 | 4 | f32 | (computed) | `rate_x` | `= 15.0 × rate_src_x / divisor_x` — **per-frame MOVEMENT SPEED** (ground displacement per frame @15fps), default locomotion | HIGH |
| 0x34 | 4 | f32 | (computed) | `rate_y` | `= 15.0 × rate_src_y / divisor_y` — **per-frame MOVEMENT SPEED, alt** (mounted / alt-locomotion) | HIGH |
| 0x38 | 4 | f32 | col13 | `float_h` | **locomotion-dust FX descriptor/id** — passed as the FX descriptor to the footfall-dust particle spawn | HIGH layout / MED meaning |
| 0x3C | 4 | f32 | col14 | `float_i` | **locomotion-dust FX SCALE/magnitude** — multiplied by 1.0 (walk) or 0.18 (run) then passed as the FX scale | HIGH layout / MED-HIGH meaning |
| 0x40 | 36 | i32[9] | cols15–23 | `motion_ids_a` | 9-element **action → `.mot` clip-id** lookup. Slots feed the ANIMATION layer; each slot is keyed by actor action/lifecycle state, NOT by direction. See the slot table below. | HIGH layout / HIGH (action→clip, by use-site) |
| 0x64 | 36 | i32[9] | cols24–32 | `motion_ids_b` | 9-element **action → SOUND/EFFECT event-id** lookup (NOT secondary motion). Slots feed the sound/effect routers, never the animation mixer. See the slot table below. | HIGH layout / HIGH (action→event-id, by use-site) |

**Total: 0x40 + 0x24 + 0x24 = 0x88 = 136 bytes (exact).** Every field's destination offset is
re-confirmed against the IDB loader on build `263bd994`; the record fills exactly to +0x84.

> **`col2` = `skin_class` (SkinClassId), SAMPLE-VERIFIED.** Column 2 (record `int_a` @ +0x04) is the
> actor-to-skeleton key. Joining col2 → `data/char/bind/g<skin_class>.bnd` resolves for **1,035 / 1,080
> = 95.8%** of rows on build `263bd994`; the non-resolving rows are **15 with `skin_class = 0`** (null
> skeleton — login/camera/special actors on a different code path) and **29** referencing `.bnd` ids
> absent from the preserved VFS (expected gaps). A value of 0 is a null pointer (no skeleton).
>
> **`motion_ids_a` slots hold `.mot` clip ids, SAMPLE-VERIFIED.** The col15/col16 idle references
> resolve to registered `.mot` clips for **89.1%** of rows. The runtime stand idle reads slot **`a[1]`
> (+0x44, COLUMN 16)**, not the col15-loaded `a[0]` — `a[0]` (+0x40, col15) is a file-source reference
> with no runtime read-site (dead). Resolution is **not** a `g{id}.mot` sprintf: the motion id is a
> lookup key into the `motlist.txt` clip registry (id == `.mot` header id_b) — see the slot table and
> the runtime-idle-lookup note below.
>
> **`motion_ids_b` slots are SOUND/EFFECT event ids, NOT `.mot` motion ids.** Every runtime consumer
> feeds `motion_ids_b` slots to the sound/effect routers (spawn sound, footstep SFX, death effect),
> never to the animation mixer. The earlier sample statistic that "74.5% of all non-zero slots resolve
> to a `.mot`" is a **coincidence of id-namespace overlap** (or `.mot` ids reused as event keys) — it is
> NOT evidence that `motion_ids_b` holds motion clips. The decisive fact is the **consumer**: `b`-slots
> never reach the animation sampler. This supersedes the prior "secondary motion" claim here and in
> `formats/animation.md`.

### Per-frame rate fields (0x30, 0x34) — CONFIRMED math

The two computed fields at 0x30 and 0x34 are derived at parse time, not read from the file:

```
rate_x = 15.0 * rate_src_x / divisor_x        // rate_src_x = field @ 0x08, divisor_x = field @ 0x28
rate_y = 15.0 * rate_src_y / divisor_y        // rate_src_y = field @ 0x0C, divisor_y = field @ 0x2C
```

The constant **15.0** is a frames-per-second base: these fields convert a source magnitude over a
loop/frame count into a per-frame rate at 15 fps. Each divisor is forced to 1 when the source column
parses as 0, guarding against divide-by-zero.

**`rate_x` / `rate_y` are per-frame MOVEMENT SPEED, CONFIRMED (resolves the former open question).**
The move-speed resolver reads `rate_x` (default locomotion) or `rate_y` (alternate / mounted
locomotion, selected by the actor's locomotion flag) and writes the result into the actor's per-frame
velocity field, after a buff/slow modifier and a max against other speed sources (mount speed, status
overrides). So these fields are the **ground displacement per frame** the locomotion uses — **NOT** an
animation playback rate. The `.mot` clip's own playback speed is governed inside the `.mot` sampler,
not by these record fields. `rate_x` = default (locomotion flag clear); `rate_y` = alternate
(mounted / alt-locomotion).

### The two 9-element sub-arrays (0x40, 0x64) — action/lifecycle keyed, NOT direction-indexed

Both sub-arrays are **nine consecutive integer columns**, but they are **two different kinds of
array** and **neither is indexed by movement direction**. Every runtime consumer reads a **fixed slot
index** chosen by the actor's **action / lifecycle state** (idle, walk, run, death, spawn, footstep) —
there is no `array[direction]` access site anywhere in the actor-visual code.

- **`motion_ids_a` (0x40) → `.mot` clip ids (animation layer).** The slots feed the animation
  layer (the `.mot` clip the actor plays for each action).

  | Slot | Rec off | Column | Action / lifecycle state | Meaning | Confidence |
  |----:|------|----|----|----|----|
  | a[0] | +0x40 | col15 | (file-source) | idle reference loaded from file; runtime idle reads a[1] not a[0] — **dead at runtime** (no read-site of +0x40) | MED (no direct runtime read of +0x40) |
  | a[1] | +0x44 | col16 | idle / stand | **default stand idle** (the idle the runtime actually uses) | HIGH |
  | a[2] | +0x48 | col17 | walk | walk clip | HIGH |
  | a[3] | +0x4C | col18 | run | run clip | HIGH |
  | a[4] | +0x50 | col19 | death / knockdown | death clip | HIGH |
  | a[5] | +0x54 | col20 | mount-follow default idle | mount-follow idle / default-idle cycle | HIGH |
  | a[6] | +0x58 | col21 | idle ALT (motion-kind byte == 1) | combat-stance / alternate idle | HIGH |
  | a[7] | +0x5C | col22 | — | no static consumer | OPEN-RISK |
  | a[8] | +0x60 | col23 | — | no static consumer | OPEN-RISK |

  **KEY POINT: the runtime stand idle is COLUMN 16 (record +0x44, a[1]), NOT column 15.** The loader
  fills col15 into a[0] (+0x40), which has **zero runtime read-sites** — every motion-kind-0 (stand)
  idle path reads a[1] (+0x44, col16). Same element-0-unused convention as the B-array (element 0
  unused/padding, first consumed element is element 1).

  **Runtime idle lookup KEY + resolution (CONFIRMED).** The play-time idle lookup is keyed by the
  actor's **appearance key** (for a player, `key = 5·(class + 4·variant) − 24`), looked up in the
  AnimCatalog ordered map — **NOT** by col2/`skin_class`. col2/`skin_class` selects the `.bnd`
  skeleton and *builds the catalog record's `motion_key` field* (record +0x00) at load time; it is not
  the key the idle dispatchers pass at runtime. Once the record is fetched, the motion-kind byte on the
  actor selects the slot (stand idle = a[1] = +0x44 = col16). The a[1] motion id then resolves through
  the **`motlist.txt` clip registry** — the motion id is an ordered-map lookup key into a registry
  pre-populated from `motlist.txt` (each line prefixed `data/char/mot/`), where the id matches the
  `.mot` file's **header id_b**. There is **no `g{id}.mot` sprintf** (the only `g%d` asset format string
  is the skin path `data/char/skin/g%d.skn`). See `formats/animation.md` (the `.mot` registry / header
  id_b join) and `specs/skinning.md` (the full idle-selection chain).

- **`motion_ids_b` (0x64) → SOUND/EFFECT event ids (sound/effect routers), NOT motion.** The slots
  feed the sound/effect routers, never the animation mixer.

  | Slot | Rec off | Action / lifecycle state | Meaning | Confidence |
  |----:|------|----|----|----|
  | b[0] | +0x64 | — | no static consumer | OPEN-RISK |
  | b[1] | +0x68 | spawn | spawn sound/event id (sound category 11) | HIGH |
  | b[2] | +0x6C | walk | walk footstep SFX id (category 7) | HIGH |
  | b[3] | +0x70 | run | run footstep SFX id (category 8) | HIGH |
  | b[4] | +0x74 | death | death effect/sound id | MED-HIGH |
  | b[5..8] | +0x78..+0x84 | — | no static consumer | OPEN-RISK |

  By the loader's own array indexing (b[0] = +0x64), the death-effect slot is **b[4] at +0x74** —
  not "b[5]". This is the SAME element-0-unused convention as the A-array: in **both** 9-slot arrays
  element 0 is unused/padding and the runtime consumers start at element 1.

  (The walk-vs-run footstep choice is gated by an actor locomotion flag and only fires while the actor
  is in a motion state.)

**The "9 directions" reading is REFUTED.** Nine matching the 8 compass directions plus a centre is a
coincidence; there is no direction-indexed access. Do **not** implement a direction-to-slot mapping,
and do **not** route `motion_ids_b` slots through the animation sampler.

## Computed lookup key (record +0x00)

```
motion_key = col1 + base_table[(uint8)(col0 + 1)]
```

where `base_table` is a per-category base-offset table that already exists on the parse object before
the actormotion records are read (`base_table` is indexed by `(uint8)(col0 + 1)`). The semantics:

- `col0` selects a **category** (its `+1` indexes the base table),
- `col1` is the **intra-category offset**,
- their sum is the **global motion key** under which the record is inserted into an ordered
  (balanced-tree) map.

Lookups elsewhere in the client retrieve a motion record by this same `motion_key`. The mechanics of
the key (sum of an intra-category offset and a per-category base) are **HIGH**; the meaning of the
category base table is **MED**.

## Enumerations / flags

- **`motion_ids_a` slot index = actor action / lifecycle state (CONFIRMED by use-site).** The 9-element
  array is indexed by a FIXED slot chosen by the actor's action: a[1] idle, a[2] walk, a[3] run, a[4]
  death, a[5] mount-idle, a[6] combat-idle; a[0] is a file-source idle reference (runtime idle reads
  a[1]); a[7]/a[8] have no static consumer. Slots feed the animation layer as `.mot` clip ids.
- **`motion_ids_b` slot index = actor action, value = SOUND/EFFECT event id (CONFIRMED by use-site).**
  b[1] spawn-sound (category 11), b[2] walk-footstep SFX (category 7), b[3] run-footstep SFX
  (category 8), b[4] death effect/sound; b[0]/b[5..8] have no static consumer. Slots feed the
  sound/effect routers — never the animation mixer. (By the loader's indexing b[0] = +0x64, so the
  death slot at +0x74 is **b[4]**, not "b[5]" — the same element-0-unused convention as the A-array.)
- **The "9 directions" reading is REFUTED** — there is no direction-indexed access to either array.

## Known unknowns

1. **`motion_ids_a[0]` (+0x40) runtime role** — the loader fills it (col15 idle reference) but the
   runtime idle path reads `a[1]` (+0x44). `a[0]` may be a file source the bake copies into `a[1]`, or
   consumed by a preview/portrait path; no static read of +0x40 confirmed (OPEN-RISK).
2. **`motion_ids_a[7]`, `a[8]` (+0x5C, +0x60)** — no static fixed-offset consumer; possibly dead, or
   read only by a code path not statically reachable from the actor-visual cluster (OPEN-RISK).
3. **`motion_ids_b[0]` (+0x64) and `b[5..8]` (+0x78..+0x84)** — no static consumer (OPEN-RISK). `b[4]`
   (+0x74) death effect/sound is MED-HIGH (one consumer found). Note both 9-slot arrays share the
   element-0-unused convention: b[0] (+0x64) is dead, consumers start at b[1].
4. **`int_b` (0x10) meaning** — integer field with no static read-site off the record; unresolved.
   (`int_a` at 0x04 is resolved: `skin_class` / SkinClassId — sample-verified.)
5. **The float fields `float_c..float_g` (0x14, 0x18, 0x1C, 0x20, 0x24)** — no static read-site off the
   record; individual meanings unresolved (do NOT assign meanings). Their type (float) is CONFIRMED via
   the float column reader. (`float_h`/`float_i` at 0x38/0x3C are resolved: the locomotion-dust FX
   descriptor and FX scale — see the layout table.)
6. **The category base table** that `(uint8)(col0 + 1)` indexes — its source and per-category
   meaning (MED).

> **Resolved this cycle (no longer open):** the `rate_x`/`rate_y` physical meaning (= per-frame
> movement speed, not animation advance) and the 9-slot direction reading (refuted — action/lifecycle
> keyed). A runtime dump of a known actor/mob record would settle the remaining OPEN-RISK slots
> (items 1–3).

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` (how the file bytes are obtained).
- **Skeleton registry:** `Docs/RE/formats/bindlist.md` — the authoritative list of registered
  `.bnd` skeletons. The `skin_class` column here joins to `data/char/bind/g<skin_class>.bnd`,
  whose validity is the membership test in `bindlist.txt`. There is NO computed `g{N}.bnd`
  numeric rule; registration is by explicit list / `IdB` join.
- **Asset chains:** the actormotion table is the join point for the **mob → skin** and **idle-motion**
  chains: a `mob_id` / actor maps through this table to a skin class and to a motion id, and the
  motion id resolves through the **`motlist.txt` clip registry** (motion id == `.mot` header id_b;
  files enumerated under `data/char/mot/`) — **not** a `g{id}.mot` sprintf. See the recovered chains
  documented under character skinning/binding (`Docs/RE/specs/skinning.md`) and the motion file format
  (`Docs/RE/formats/animation.md`).
- **Motion file format (naming reconciled, semantics CORRECTED here):** `Docs/RE/formats/animation.md`
  documents the same two arrays under the **same `motion_ids_a` / `motion_ids_b`** names. The docs agree
  on layout and offsets, but `animation.md` describes `motion_ids_b` as "secondary motion" ids — that
  reading is **superseded** here: by their runtime consumers, `motion_ids_b` slots are SOUND/EFFECT
  event ids, not `.mot` motion ids. The slot index is **action/lifecycle-keyed**, not direction-indexed
  (the PROPOSED per-direction reading is refuted). `animation.md` should be reconciled to match.
- **Glossary:** see `Docs/RE/names.yaml` (`ActorMotionTable_Parse`, `ActorMotionMap_Insert`). The
  loader re-affirmed on build `263bd994` is `ActorMotionTable_LoadFromTxt` (33-col → 136-byte record),
  using the shared `AssetStream_ReadInt32Field` / `AssetStream_ReadFloatField` dual text/binary field
  readers and inserting into the ordered map keyed by `motion_key`.
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).
