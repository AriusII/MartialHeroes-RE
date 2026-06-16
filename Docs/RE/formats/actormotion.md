# Format: actormotion.txt  (per-actor motion / animation table — 136-byte record)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary code addresses. Consumed by `Assets.Parsers`. Every offset an engineer cites must
> reference this file.
>
> verification: sample-verified (col2 / motion-id slots) + confirmed (read-order, offsets, key math, rate math)
> ida_reverified: 2026-06-16
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
>
> This document resolves the open question on the actormotion column semantics (formerly OQ-5):
> the full 136-byte (0x88) per-record layout, the int-vs-float column types, the two 9-element
> motion-id sub-arrays, the per-frame rate fields, and the computed map key.
>
> **Re-confronted on build `263bd994` against the real VFS sample (`data.vfs`):** the IDB loader
> **fully confirms** this doc's read-order and offset map (this is the more precise of the two
> committed docs for the in-memory record), and the sample corroborates the join columns. No layout
> drift. The one change this pass is a cross-doc **naming reconciliation**: the two 9-slot arrays were
> named `dir_array_1` / `dir_array_2` here and `motion_ids_a` / `motion_ids_b` in
> `formats/animation.md`; both docs now use **`motion_ids_a` / `motion_ids_b`**, with the
> per-direction interpretation of the 9 slots carried explicitly as **PROPOSED**.
>
> **Conflicts carried (this anchor):** which 9-slot array is "primary" vs "transition/secondary", and
> whether the 9 slots map to the 8 compass directions plus a neutral/centre, are PROPOSED (not proven).
> The float-column meanings (`float_c..float_i`) and the `int_b` meaning are layout-confirmed but
> semantically open (`int_a` = `skin_class` is resolved + sample-verified). These are
> runtime/debugger-confirmation candidates.

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
| 0x08 | 4 | f32 | col3 | `rate_src_x` | numerator of the per-frame rate at +0x30 (a duration or rate) | HIGH layout / MED meaning |
| 0x0C | 4 | f32 | col5 | `rate_src_y` | numerator of the per-frame rate at +0x34 | HIGH layout / MED meaning |
| 0x10 | 4 | i32 | col7 | `int_b` | integer field | HIGH layout / MED meaning |
| 0x14 | 4 | f32 | col8 | `float_c` | float field | HIGH layout / MED meaning |
| 0x18 | 4 | f32 | col9 | `float_d` | float field | HIGH layout / MED meaning |
| 0x1C | 4 | f32 | col10 | `float_e` | float field | HIGH layout / MED meaning |
| 0x20 | 4 | f32 | col11 | `float_f` | float field | HIGH layout / MED meaning |
| 0x24 | 4 | f32 | col12 | `float_g` | float field | HIGH layout / MED meaning |
| 0x28 | 4 | i32 | col4 | `divisor_x` | frame / loop count; **forced to 1 if read as 0**; read interleaved early | HIGH |
| 0x2C | 4 | i32 | col6 | `divisor_y` | frame / loop count; **forced to 1 if read as 0**; read interleaved early | HIGH |
| 0x30 | 4 | f32 | (computed) | `rate_x` | `= 15.0 × rate_src_x / divisor_x` (per-frame rate, X) | HIGH |
| 0x34 | 4 | f32 | (computed) | `rate_y` | `= 15.0 × rate_src_y / divisor_y` (per-frame rate, Y) | HIGH |
| 0x38 | 4 | f32 | col13 | `float_h` | float field | HIGH layout / MED meaning |
| 0x3C | 4 | f32 | col14 | `float_i` | float field | HIGH layout / MED meaning |
| 0x40 | 36 | i32[9] | cols15–23 | `motion_ids_a` | 9-element integer motion-id sub-array #1; slots hold `.mot` `id_a` values (slot 0 = idle motion). **SAMPLE-VERIFIED** (col15 idle → `.mot` 89.1%; all non-zero motion slots 74.5%). | HIGH layout / HIGH (slots are `.mot` ids, sample-verified) / per-direction meaning PROPOSED |
| 0x64 | 36 | i32[9] | cols24–32 | `motion_ids_b` | 9-element integer motion-id sub-array #2; same encoding (`.mot` `id_a` values). | HIGH layout / HIGH (slots are `.mot` ids, sample-verified) / per-direction meaning PROPOSED |

**Total: 0x40 + 0x24 + 0x24 = 0x88 = 136 bytes (exact).** Every field's destination offset is
re-confirmed against the IDB loader on build `263bd994`; the record fills exactly to +0x84.

> **`col2` = `skin_class` (SkinClassId), SAMPLE-VERIFIED.** Column 2 (record `int_a` @ +0x04) is the
> actor-to-skeleton key. Joining col2 → `data/char/bind/g<skin_class>.bnd` resolves for **1,035 / 1,080
> = 95.8%** of rows on build `263bd994`; the non-resolving rows are **15 with `skin_class = 0`** (null
> skeleton — login/camera/special actors on a different code path) and **29** referencing `.bnd` ids
> absent from the preserved VFS (expected gaps). A value of 0 is a null pointer (no skeleton).
>
> **The two 9-slot arrays hold `.mot` `id_a` values, SAMPLE-VERIFIED.** `motion_ids_a[0]` (col15) is
> the idle / stand motion and resolves to `data/char/mot/g{id}.mot` for **89.1%** of rows; across the
> whole file **74.5%** of all non-zero slots in both arrays resolve to an existing `.mot` clip
> (a random mapping would resolve ≈0%). This is the same data `formats/animation.md` documents under
> the same `motion_ids_a` / `motion_ids_b` names.

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

### The two 9-element motion-id sub-arrays (0x40, 0x64)

Each sub-array is **nine consecutive integer columns**. The slots demonstrably hold **`.mot` `id_a`
values** (sample-verified — see the SAMPLE-VERIFIED note above; 74.5% of all non-zero slots resolve to
a real clip, a random mapping ≈0%), so both arrays are **motion-id tables**. `motion_ids_a[0]` (col15)
is the idle / stand motion.

- `motion_ids_a` (0x40): the primary motion-id array (slot 0 = idle).
- `motion_ids_b` (0x64): a secondary motion-id array — candidate transition/blend or alternate
  motion ids; same encoding.

**Per-direction interpretation (PROPOSED, not proven).** Nine is also the count of the game's
movement/facing directions (eight compass directions plus a neutral/centre, the classic 3×3 grid),
and the record key uses a category selector (`(uint8)(col0 + 1)`), so a plausible reading is that each
slot index corresponds to a direction. This per-direction framing is **PROPOSED** — it is an
interpretation of the slot index only and has not been confirmed against the runtime consumer. Which
array is "primary" vs "transition/secondary" is likewise **LOW** confidence and a
runtime-confirmation candidate. Treat both arrays as flat 9-element `.mot`-id arrays for parsing; do
not rely on a direction-to-slot mapping.

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

- **Motion-id slot index (per-direction reading PROPOSED):** the 9-element `motion_ids_a` /
  `motion_ids_b` arrays are indexed `0..8`. Slot 0 of `motion_ids_a` is the idle / stand motion
  (sample-verified). A **PROPOSED** reading maps the 9 slots to the 8 compass directions plus a
  neutral/centre slot (3×3 grid); the exact index-to-direction mapping (which slot is N, NE, …, and
  which is neutral) is **not** pinned down and is not proven. Treat the arrays as flat `.mot`-id
  arrays; defer any direction-to-slot mapping to runtime confirmation.

## Known unknowns

1. **Which 9-element array is primary motion vs. transition/secondary** (LOW). Both are confirmed to
   hold `.mot` `id_a` values (sample-verified); only their relative role is open.
2. **`int_b` (0x10) meaning** — an integer field whose role is unresolved (MED). (`int_a` at 0x04 is
   now resolved: it is `skin_class` / SkinClassId — sample-verified.)
3. **`rate_x` / `rate_y` physical meaning** — per-frame movement displacement vs. animation advance
   (MED). The 15 fps base and the divide-by-`divisor` math are CONFIRMED; only the interpretation is
   open.
4. **The float fields `float_c..float_i` (0x14, 0x18, 0x1C, 0x20, 0x24, 0x38, 0x3C)** — individual
   meanings not resolved (MED). Their type (float) is CONFIRMED via the float column reader.
5. **The category base table** that `(uint8)(col0 + 1)` indexes — its source and per-category
   meaning (MED).
6. **Per-direction interpretation of the 9-slot motion-id arrays (PROPOSED, unproven)** — whether the
   slot index corresponds to the 8 compass directions plus a neutral/centre, and the exact
   slot-to-direction ordering. The slots hold `.mot` ids; the direction framing is an unverified
   interpretation of the index.

A runtime dump of a known actor/mob record (the per-record materialisation site and the two
divisor-guard sites) would settle items 1–4 and 6.

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` (how the file bytes are obtained).
- **Skeleton registry:** `Docs/RE/formats/bindlist.md` — the authoritative list of registered
  `.bnd` skeletons. The `skin_class` column here joins to `data/char/bind/g<skin_class>.bnd`,
  whose validity is the membership test in `bindlist.txt`. There is NO computed `g{N}.bnd`
  numeric rule; registration is by explicit list / `IdB` join.
- **Asset chains:** the actormotion table is the join point for the **mob → skin** and **idle-motion**
  chains: a `mob_id` / actor maps through this table to a skin class and to a motion id, and the
  motion id resolves to a `data/char/mot/g{id}.mot` file. See the recovered chains documented under
  character skinning/binding (`Docs/RE/specs/skinning.md`) and the motion file format
  (`Docs/RE/formats/animation.md`).
- **Motion file format (naming reconciled):** `Docs/RE/formats/animation.md` documents the same two
  9-slot motion-id arrays under the **same `motion_ids_a` / `motion_ids_b`** names (§`actormotion.txt`
  layout). Both docs agree on layout, offsets, and the sample-verified `.mot`-id slot content; the
  per-direction reading of the slot index is carried as PROPOSED in both.
- **Glossary:** see `Docs/RE/names.yaml` (`ActorMotionTable_Parse`, `ActorMotionMap_Insert`). The
  loader re-affirmed on build `263bd994` is `ActorMotionTable_LoadFromTxt` (33-col → 136-byte record),
  using the shared `AssetStream_ReadInt32Field` / `AssetStream_ReadFloatField` dual text/binary field
  readers and inserting into the ordered map keyed by `motion_key`.
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).
