# Format: actormotion.txt  (per-actor motion / animation table — 136-byte record)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every offset an engineer cites must
> reference this file.
>
> This document resolves the open question on the actormotion column semantics (formerly OQ-5):
> the full 136-byte (0x88) per-record layout, the int-vs-float column types, the two 9-element
> directional sub-arrays, the per-frame rate fields, and the computed map key.

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

| Rec off | Size | Type | Read column | Field (proposed) | Notes | Confidence |
|--------:|-----:|------|------------|------------------|-------|------------|
| 0x00 | 4 | u32 | (computed) | `motion_key` | `= col1 + base_table[(uint8)(col0 + 1)]`; the ordered-map insertion key | HIGH layout / MED meaning |
| 0x04 | 4 | i32 | col2 | `int_a` | integer field (motion / animation id, likely) | HIGH layout / MED meaning |
| 0x08 | 4 | f32 | col3 | `rate_src_x` | numerator of the per-frame rate at +0x30 (a duration or rate) | HIGH layout / MED meaning |
| 0x0C | 4 | f32 | col5 | `rate_src_y` | numerator of the per-frame rate at +0x34 | HIGH layout / MED meaning |
| 0x10 | 4 | i32 | col6 | `int_b` | integer field | HIGH layout / MED meaning |
| 0x14 | 4 | f32 | col7 | `float_c` | float field | HIGH layout / MED meaning |
| 0x18 | 4 | f32 | col8 | `float_d` | float field | HIGH layout / MED meaning |
| 0x1C | 4 | f32 | col9 | `float_e` | float field | HIGH layout / MED meaning |
| 0x20 | 4 | f32 | col10 | `float_f` | float field | HIGH layout / MED meaning |
| 0x24 | 4 | f32 | col11 | `float_g` | float field | HIGH layout / MED meaning |
| 0x28 | 4 | i32 | col4 | `divisor_x` | frame / loop count; **forced to 1 if read as 0** | HIGH |
| 0x2C | 4 | i32 | (paired) | `divisor_y` | frame / loop count; **forced to 1 if read as 0** | HIGH |
| 0x30 | 4 | f32 | (computed) | `rate_x` | `= 15.0 × rate_src_x / divisor_x` (per-frame rate, X) | HIGH |
| 0x34 | 4 | f32 | (computed) | `rate_y` | `= 15.0 × rate_src_y / divisor_y` (per-frame rate, Y) | HIGH |
| 0x38 | 4 | f32 | col12 | `float_h` | float field | HIGH layout / MED meaning |
| 0x3C | 4 | f32 | col13 | `float_i` | float field | HIGH layout / MED meaning |
| 0x40 | 36 | i32[9] | (9 columns) | `dir_array_1` | 9-element integer sub-array #1 (per-direction) | HIGH layout / MED meaning |
| 0x64 | 36 | i32[9] | (9 columns) | `dir_array_2` | 9-element integer sub-array #2 (per-direction) | HIGH layout / MED meaning |

**Total: 0x40 + 0x24 + 0x24 = 0x88 = 136 bytes (exact).**

### Per-frame rate fields (0x30, 0x34) — CONFIRMED math

The two computed fields at 0x30 and 0x34 are derived at parse time, not read from the file:

```
rate_x = 15.0 * rate_src_x / divisor_x        // rate_src_x = field @ 0x08, divisor_x = field @ 0x28
rate_y = 15.0 * rate_src_y / divisor_y        // rate_src_y = field @ 0x0C, divisor_y = field @ 0x2C
```

The constant **15.0** is a frames-per-second base: these fields convert a source magnitude over a
loop/frame count into a per-frame rate at 15 fps. Each divisor is forced to 1 when the source column
parses as 0, guarding against divide-by-zero. The exact physical meaning of `rate_x` / `rate_y`
(e.g. per-frame movement displacement vs. animation advance) is **MED** — a runtime dump of a known
record is the way to settle it.

### The two 9-element directional sub-arrays (0x40, 0x64)

Each sub-array is **nine consecutive integer columns**. Nine is the count of the game's
movement/facing directions: the eight compass directions plus a neutral/centre, i.e. the classic
3×3 directional grid. The record is keyed by direction (`(uint8)(col0 + 1)` selects the category in
the key), which reinforces that both arrays are **per-direction motion-id tables**.

- `dir_array_1` (0x40): the per-direction primary motion / animation index (strong inference).
- `dir_array_2` (0x64): a per-direction secondary index — a transition/blend or alternate motion
  index (weaker inference).

The count being 9 = directions is **HIGH**; which array is "primary" vs. "transition/secondary" is
**LOW** and is a runtime-confirmation candidate.

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

- **Direction index:** the 9-element arrays are indexed `0..8` over the 8 compass directions plus a
  neutral/centre slot (3×3 grid). The exact index-to-compass-direction mapping (which slot is N, NE,
  …, and which is neutral) is not pinned down here — treat slot 8 (or slot 0) as the neutral slot
  pending runtime confirmation.

## Known unknowns

1. **Which 9-element array is primary motion vs. transition/secondary** (LOW).
2. **`int_a` (0x04) meaning** — base motion id vs. a flags word (MED).
3. **`rate_x` / `rate_y` physical meaning** — per-frame movement displacement vs. animation advance
   (MED). The 15 fps base and the divide-by-`divisor` math are CONFIRMED; only the interpretation is
   open.
4. **The float fields `float_c..float_i` (0x14, 0x18, 0x1C, 0x20, 0x24, 0x38, 0x3C)** — individual
   meanings not resolved (MED). Their type (float) is CONFIRMED via the float column reader.
5. **The category base table** that `(uint8)(col0 + 1)` indexes — its source and per-category
   meaning (MED).
6. **Exact direction-slot ordering** within each 9-element array (which index is which compass
   direction and which is neutral).

A runtime dump of a known actor/mob record (the per-record materialisation site and the two
divisor-guard sites) would settle items 1–4 and 6.

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` (how the file bytes are obtained).
- **Asset chains:** the actormotion table is the join point for the **mob → skin** and **idle-motion**
  chains: a `mob_id` / actor maps through this table to a skin class and to a motion id, and the
  motion id resolves to a `data/char/mot/g{id}.mot` file. See the recovered chains documented under
  character skinning/binding (`Docs/RE/specs/skinning.md`) and the motion file format
  (`Docs/RE/formats/animation.md`).
- **Glossary:** see `Docs/RE/names.yaml` (`ActorMotionTable_Parse`, `ActorMotionMap_Insert`).
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).
