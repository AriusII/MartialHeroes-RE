# Format: .xdb (small fixed-stride data tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> C# parsers that decode any table below MUST cite `// spec: Docs/RE/formats/xdb_tables.md`
> on every magic constant, stride, and byte offset.
>
> status: sample_verified
> sample_verified: true — every stride below is confirmed by exact divisibility of the real VFS
>                  file size; the small tables (§1–§4) were decoded in their entirety, and the
>                  large §5 table was scanned in full for the per-column statistics cited.

---

## Identification

- **Extension:** `.xdb`
- **Found in:** the VFS (`data.inf` + `data/data.vfs`), under `data/script/`.
- **Magic / signature:** none. No file-level magic bytes, version header, or record-count prefix.
- **Endianness:** little-endian (all `u32` / `f32` fields).
- **Common structural pattern:** each `.xdb` documented here is a **flat array of fixed-size
  records with no file header**. The record count is derived as `file_size / stride`. There is no
  inter-record padding.

This spec covers the five small `.xdb` tables. The large `msg.xdb` (localised message strings) is
a different, much larger file and is documented separately in `misc_data.md §6` (516-byte stride:
`u32` id + 512-byte CP949 null-terminated string) — it is **not** re-documented here.

### File inventory

| VFS path                              | Size (bytes) | Stride | Record count | Section |
|---------------------------------------|-------------:|-------:|-------------:|---------|
| `data/script/actor_size.xdb`          | 180          | 12     | 15           | §1      |
| `data/script/buff_icon_position.xdb`  | 1,608        | 12     | 134          | §2      |
| `data/script/effectscale.xdb`         | 16           | 8      | 2            | §3      |
| `data/script/vehicle.xdb`             | 3,016        | 52     | 58           | §4      |
| `data/script/creature_item.xdb`       | 44,208       | 48     | 921          | §5      |
| `data/script/msg.xdb`                 | 1,364,304    | 516    | 2,644        | see `misc_data.md §6` |

### Second black-box witness (reconciliation note)

Multiple independent black-box passes (stride arithmetic plus full-record-population scans on the
real VFS) re-derived all five strides and counts and **agree** on the container shape, every
stride, every record count, and the field types. A later full-file scan (every record, not just
the head window) **resolved several questions that earlier head-only readings left open** — these
are landed inline in the affected sections: the `buff_id` continuity question (§2), the finer
field semantics in `vehicle.xdb` (§4), and the reinterpretation of `creature_item.xdb`'s residual
columns (§5). Fields that remain inferred (e.g. which scale factor maps to which axis) keep an
`INFERRED` / `UNVERIFIED` tag.

---

## §1 — actor_size.xdb (actor scale table)

- **Stride:** 12 bytes. **Record count:** 15 (`180 / 12 = 15`, exact). **No file header.**

| Offset | Size | Type   | Field           | Notes                                            | Confidence |
|-------:|-----:|--------|-----------------|--------------------------------------------------|------------|
| +0     | 4    | u32 LE | `actor_kind_id` | Sequential 0-based index (0..14)                 | CONFIRMED  |
| +4     | 4    | f32 LE | `scale_a`       | Horizontal / radial scale (XZ footprint, "girth"). Observed range across all 15 records ≈ 0.10 .. 2.00; deviates from 1.0 in 13 of 15 records. | CONFIRMED (value); INFERRED (horizontal axis) |
| +8     | 4    | f32 LE | `scale_b`       | Vertical / height scale. Observed range ≈ 0.88 .. 1.50; stays near 1.0 in the majority of records. | CONFIRMED (value); INFERRED (vertical axis) |

- **Axis interpretation (INFERRED by value distribution):** `scale_a` (proposed `scale_width`)
  varies over a roughly 20× span (0.10 → 2.00) and is non-unity far more often, which is the
  signature of a horizontal / radial footprint scale. `scale_b` (proposed `scale_height`) is
  near-unity for most actors and stretches only modestly (max ≈ 1.5), the signature of a vertical
  scale. The reference entry (id 0) is `(1.0, 1.0)`. Which factor is which axis remains
  **INFERRED** — confirm against the loader before relying on the axis assignment.
- **Sample-verified:** all 15 records decoded from the full 180-byte file.

---

## §2 — buff_icon_position.xdb (buff icon sprite-sheet positions)

- **Stride:** 12 bytes. **Record count:** 134 (`1,608 / 12 = 134`, exact). **No file header.**

| Offset | Size | Type   | Field       | Notes                                              | Confidence |
|-------:|-----:|--------|-------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `buff_id`   | Sparse buff-system identifier. Observed min 1, max 1103, with only 134 of that range populated — **non-contiguous**. Index the table by stored `buff_id`, never by row position. | CONFIRMED (non-contiguous) |
| +4     | 4    | u32 LE | `sprite_x`  | Pixel X origin on the buff-icon sprite sheet; advances in ~25-px steps and wraps after 8 icons per row. | CONFIRMED |
| +8     | 4    | u32 LE | `sprite_y`  | Pixel Y origin on the sprite sheet; advances ~25 px when `sprite_x` wraps back to the row start. Not constant. Some tail rows are irregular (suggesting stitched sheets). | CONFIRMED (pixel-Y origin) |

- **`buff_id` continuity — RESOLVED:** the earlier conflict (one head-only reading saw `1..134`
  contiguous, another saw a sparse `1..1103`) is resolved in favour of the **non-contiguous**
  reading. A full scan confirms the head happens to begin contiguously (`1,2,3,…`) but the table
  spans up to 1103 with only 134 slots populated. **`buff_id != row_index`.**
- **`sprite_x` / `sprite_y` grid — RESOLVED:** the layout is a sprite-sheet row-wrap grid:
  `sprite_x` steps by ~25 px for 8 columns, then resets while `sprite_y` advances ~25 px to the
  next row. The third field is therefore the **pixel Y origin**, not a constant page flag. The
  earlier "constant 1" reading was a head-window artefact. No `1024`/`1025` X outliers were present
  in the scanned instance (that earlier note does not hold for this VFS).
- **Sample-verified:** all 134 records decoded.

---

## §3 — effectscale.xdb (effect scale table)

- **Stride:** 8 bytes. **Record count:** 2 (`16 / 8 = 2`, exact). **No file header.**
- **Overall confidence: MEDIUM.** Only 2 records exist in the entire file, so the field layout
  rests on a single 16-byte sample with no internal repetition to cross-check. Stride 8 is the only
  divisor that yields a plausible (`u32` + `f32`) layout, and the two `u32` keys differ by exactly
  1, which supports the layout — but the small population is a real risk. Treat the §3 layout as
  the best black-box hypothesis, not a firm contract.

| Offset | Size | Type   | Field            | Notes                                        | Confidence |
|-------:|-----:|--------|------------------|----------------------------------------------|------------|
| +0     | 4    | u32 LE | `effect_key`     | Large `u32` key. Splits into a high 16 bits that are **shared across both records** (a fixed type-tag) and a low 16 bits that differ by 1 (a per-effect index). | MEDIUM |
| +4     | 4    | f32 LE | `scale_factor`   | Floating-point scale (record 0 = 3.0, record 1 = 2.0) | MEDIUM (value); UNVERIFIED (semantic) |

- **Key structure (NARROWED):** the high 16 bits of `effect_key` are identical in both records,
  consistent with a **table/type-tag in the high half**; the low 16 bits increment by 1, consistent
  with a **per-effect index in the low half**. This narrows the prior "hash vs. compound id"
  question toward a packed compound id, but with only 2 records it stays MEDIUM.
- **Sample-verified:** both records decoded (full file = 16 bytes). **Single-sample risk:** with
  only 2 records there is no in-file periodicity to confirm the field split.

---

## §4 — vehicle.xdb (vehicle / mount catalogue)

- **Stride:** 52 bytes. **Record count:** 58 (`3,016 / 52 = 58`, exact). **No file header.**

| Offset | Size | Type   | Field          | Notes                                              | Confidence |
|-------:|-----:|--------|----------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `vehicle_id`   | Sequential 1-based (1..58)                          | CONFIRMED  |
| +4     | 4    | u32 LE | `item_id`      | Item id in `items.scr`; early records form a consecutive block (id 1 → 3108, id 2 → 3109, …); later records are non-consecutive | CONFIRMED |
| +8     | 4    | u32 LE | `tag_a`        | Vehicle-family discriminator. Takes 3 distinct values across the 58 records (one dominant block of 16, one block of ~40, and a 2-record special family). Likely a category / family hash. | HIGH (3-family discriminator); INFERRED (family semantic) |
| +12    | 4    | u32 LE | `tag_b`        | **Constant `0x1575A3E4` in all 58 records.** Carries no per-vehicle information — a table-type stamp (table-name hash or compile-time constant inserted by the table tool). Proposed name `table_stamp`. | CONFIRMED (constant) |
| +16    | 4    | f32 LE | `param_0`      | Rider mount-point X offset (lateral). Range ≈ 0.0 .. 4.0; zero for most records (zero = use the model's own default mount point). | HIGH (present); INFERRED (X offset) |
| +20    | 4    | f32 LE | `param_1`      | **Always 0.0 across all 58 records** — a constrained / unused axis (consistent with a fixed vertical component if mount points share a height). | CONFIRMED (always 0); INFERRED (constrained axis) |
| +24    | 4    | f32 LE | `param_2`      | Rider mount-point Z offset (forward/back). Range ≈ -4.0 .. +2.5 (signed). Zero for most records. | HIGH (present); INFERRED (Z offset) |
| +28    | 4    | f32 LE | `param_3`      | Range ≈ 0.0 .. +5.0; likely a bounding / scale parameter. Zero for most records. | INFERRED |
| +32    | 4    | f32 LE | `param_4`      | 0.0 except in the 2-record special family (`tag_a` = the rare value), where it is a large negative offset (≈ -22 / -33) — specific to that family's geometry. | INFERRED |
| +36    | 16   | f32[4] | `param_5..8`   | 0.0 for almost all records; the last few records carry small positive values (≈ 1.5 .. 2.0), possibly collision / attachment dimensions for late-table mounts. | UNVERIFIED |

- **`tag_a` families (HIGH):** the three distinct `tag_a` values partition the table into three
  vehicle families. The dominant block of 16 records maps to a dense `item_id` run (3108..3123);
  the largest block (~40 records) covers the remaining mounts; a 2-record family (rare value)
  carries the special `param_4` geometry offset. The **meaning** of each family (horse tier,
  flying mount, wagon, …) is INFERRED, not confirmed.
- **Rider mount-point reading (INFERRED):** the float params behave like a rider attachment
  offset — `param_0` lateral (X), `param_1` always zero (the constrained axis), `param_2`
  forward/back (Z) — with the all-zero records using the model's built-in default mount point.
  Treat the offset axes as INFERRED until the loader confirms.
- **Sample-verified:** all 58 records decoded for the column statistics above.

---

## §5 — creature_item.xdb (creature attached-item table)

- **Stride:** 48 bytes. **Record count:** 921 (`44,208 / 48 = 921`, exact). **No file header.**

| Offset | Size | Type   | Field             | Notes                                                       | Confidence |
|-------:|-----:|--------|-------------------|-------------------------------------------------------------|------------|
| +0     | 4    | u32 LE | `creature_key`    | Large `u32` key. Runs of consecutive variants share sequential keys, but different creature families occupy disjoint key blocks (large gaps in the key space — ~3,175-value span for 921 records). A compound entity-type + index id. | CONFIRMED (block-sequential, gapped) |
| +4     | 4    | u32 LE | `item_id`         | Attached item id (e.g. 3001)                                | CONFIRMED  |
| +8     | 4    | f32 LE | `attach_f0`       | First of six attachment floats; negative model-local offset | CONFIRMED (present); UNVERIFIED (axis) |
| +12    | 4    | f32 LE | `attach_f1`       | Second attachment float                                     | CONFIRMED (present); UNVERIFIED (axis) |
| +16    | 4    | f32 LE | `attach_f2`       | Third attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +20    | 4    | f32 LE | `attach_f3`       | Fourth attachment float                                     | CONFIRMED (present); UNVERIFIED (axis) |
| +24    | 4    | f32 LE | `attach_f4`       | Fifth attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +28    | 4    | f32 LE | `attach_f5`       | Sixth attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +32    | 4    | f32 LE | `scale_or_radius` | Takes only **two values across all 921 records: 3.0 or 8.0.** The narrow two-level spread points to a **collision-sphere radius** (small creature = 3.0, large creature = 8.0) rather than a free-form scale. Proposed name `collision_radius`. | CONFIRMED (two values); INFERRED (collision radius) |
| +36    | 4    | f32 LE | `attach_probability_f32` | A **float**, not a constant zero. 716 of 921 records are 0.0; the remaining 205 carry clean fractional values (0.1, 0.2, 0.3, 0.4, 0.7, 0.8, 1.0, 1.8, 2.0, 2.2). The clean-fraction distribution is the signature of a sub-probability / multiplier float (0.0 = use default). | CONFIRMED (f32, non-zero in 205/921); INFERRED (sub-probability) |
| +40    | 1    | u8     | `flag_0`          | Binary flag. Values across the table: ~272 zero, ~649 one. Likely a primary-vs-secondary attachment-slot flag. | CONFIRMED (independent u8 flag) |
| +41    | 1    | u8     | `flag_1`          | Binary flag, **almost always 1** (916 of 921). Likely an is-active / is-visible flag. | CONFIRMED (independent u8 flag) |
| +42    | 1    | u8     | `flag_2`          | Binary flag, nearly always 1 (910 of 921). Likely paired with `flag_1` (is-enabled). | CONFIRMED (independent u8 flag) |
| +43    | 1    | u8     | `flag_3`          | Binary flag, mostly 0 (824 of 921); set to 1 in ~97 records. Likely a has-extended-data marker (does NOT directly correlate with the 205 non-zero `attach_probability_f32` records, so it encodes something distinct). | CONFIRMED (independent u8 flag) |
| +44    | 4    | u32 LE | `probability`     | **Constant `100` (`0x64`) in all 921 records.** Integer-percent probability; effectively a fixed 100% for this table. | CONFIRMED (constant 100) |

- **Four independent `u8` flags at +40..+43 — RESOLVED:** the earlier "4 `u8` flags vs. 1 packed
  `u32`" ambiguity is settled in favour of **four independent `u8` flags**. Each byte has its own
  full-table distribution (272/649, 916/5, 910/11, 824/97); a single packed `u32` would force the
  four bytes to move together, which the data does not show.
- **`attach_probability_f32` at +36 — RESOLVED:** previously read as "always zero in the head"
  and tagged unknown; the full scan shows it is a float that is non-zero in 205 of 921 records with
  clean decimal values, so it is a real per-entry float (sub-probability or variant scale), not an
  error/zero field.
- **Tail records (different family):** the last few records show all four flags set to 1 and a
  non-default `attach_probability_f32` (e.g. 1.0), consistent with a distinct creature family
  (e.g. bosses / rare spawns). Field types are unchanged.
- The six floats at +8..+31 likely encode a 3D attachment transform (a position plus rotation, or
  a position plus a second point / extent) for the item rendered on the creature. The mapping of
  each float to a named axis is **UNVERIFIED** — do not assume an order.
- **Sample-verified:** all 921 records decoded for the per-column statistics above.

---

## Known unknowns (all five tables)

- `actor_size.xdb`: which scale factor is which axis is INFERRED (scale_a ≈ horizontal/radial,
  scale_b ≈ vertical) by distribution but not confirmed against the loader.
- `buff_icon_position.xdb`: the buff-icon texture path (not encoded in this file); whether any
  later VFS revision adds rows beyond the 134 observed.
- `effectscale.xdb`: confirmation of the high16=type-tag / low16=index split (NARROWED but the
  table holds only 2 records). The semantic of `scale_factor`. Whole-table confidence MEDIUM.
- `vehicle.xdb`: the concrete meaning of each `tag_a` family; the exact axis assignment of the
  rider-offset floats (INFERRED); the semantic of `param_3` and the tail `param_5..8` values.
- `creature_item.xdb`: the axis mapping of the six attachment floats; final confirmation that
  `scale_or_radius` (only 3.0/8.0) is a collision radius; the exact meaning of each of the four
  flag bytes; whether `attach_probability_f32` is a drop sub-probability or a variant scale.

---

## Cross-references

- Related formats: `config_tables.md` (the sibling `.scr` / `.do` catalogues; `vehicle.xdb` and
  `creature_item.xdb` reference `item_id`s that live in `items.scr` / `citems.scr`),
  `misc_data.md §6` (the large `msg.xdb` message-string table), `effects.md` (effect keys used by
  `effectscale.xdb`).
- Glossary: see `Docs/RE/names.yaml` (proposed: `ActorSizeXdb`, `BuffIconPositionXdb`,
  `EffectScaleXdb`, `VehicleXdb`, `CreatureItemXdb` and their record fields, including
  `VehicleXdb.tableStamp`, `CreatureItemXdb.collisionRadius`,
  `CreatureItemXdb.attachProbability`).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).
