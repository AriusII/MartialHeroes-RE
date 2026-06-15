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

| VFS path                              | Size (bytes) | Stride | Record count | Section | Runtime consumer |
|---------------------------------------|-------------:|-------:|-------------:|---------|------------------|
| `data/script/actor_size.xdb`          | 180          | 12     | 15           | §1      | **NONE (dead in this build)** |
| `data/script/buff_icon_position.xdb`  | 1,608        | 12     | 134          | §2      | buff-icon renderer |
| `data/script/effectscale.xdb`         | 16           | 8      | 2            | §3      | effect-scale lookup |
| `data/script/vehicle.xdb`             | 3,016        | 52     | 58           | §4      | vehicle / mount catalogue |
| `data/script/creature_item.xdb`       | 44,208       | 48     | 921          | §5      | creature attached-item placement |
| `data/script/msg.xdb`                 | 1,364,304    | 516    | 2,644        | see `misc_data.md §6` | message strings |

### Second black-box witness (reconciliation note)

Multiple independent black-box passes (stride arithmetic plus full-record-population scans on the
real VFS) re-derived all five strides and counts and **agree** on the container shape, every
stride, every record count, and the field types. A later full-file scan (every record, not just
the head window) **resolved several questions that earlier head-only readings left open** — these
are landed inline in the affected sections: the `buff_id` continuity question (§2), the finer
field semantics in `vehicle.xdb` (§4), and the reinterpretation of `creature_item.xdb`'s residual
columns (§5). Fields that remain inferred (e.g. which scale factor maps to which axis) keep an
`INFERRED` / `UNVERIFIED` tag.

The CAMPAIGN VFS-MASTERY two-witness pass (loader trace + black-box statistics) further settled
the runtime-consumption status of several fields below — which tables/fields the shipped client
actually reads versus those that are tool-side authoring data with no runtime reader. Those
verdicts are landed inline and called out per field.

---

## §1 — actor_size.xdb (actor scale table)

> **DEAD IN THIS BUILD — DO NOT LOAD (loader-resolved).** The file is present on disk in the VFS,
> but the shipped client has **zero runtime consumers** for it: no loader resolves or reads
> `actor_size.xdb` anywhere in the executable. A faithful 1:1 port **must NOT load this table** —
> doing so would add behaviour the original never had. The layout below is documented for
> completeness and archival fidelity only; it is not wired into any code path. Actor scaling in
> this build is driven elsewhere, not by this file.
>
> Confidence: **loader-resolved (no consumer)** — the absence of a runtime reader is confirmed by
> the loader-side witness; the on-disk record layout is the black-box witness.

- **Stride:** 12 bytes. **Record count:** 15 (`180 / 12 = 15`, exact). **No file header.**

| Offset | Size | Type   | Field           | Notes                                            | Confidence |
|-------:|-----:|--------|-----------------|--------------------------------------------------|------------|
| +0     | 4    | u32 LE | `actor_kind_id` | Sequential 0-based index (0..14)                 | CONFIRMED (on disk); table unread at runtime |
| +4     | 4    | f32 LE | `scale_a`       | Horizontal / radial scale (XZ footprint, "girth"). Observed range across all 15 records ≈ 0.10 .. 2.00; deviates from 1.0 in 13 of 15 records. | CONFIRMED (value); INFERRED (horizontal axis); table unread at runtime |
| +8     | 4    | f32 LE | `scale_b`       | Vertical / height scale. Observed range ≈ 0.88 .. 1.50; stays near 1.0 in the majority of records. | CONFIRMED (value); INFERRED (vertical axis); table unread at runtime |

- **Axis interpretation (INFERRED by value distribution):** `scale_a` (proposed `scale_width`)
  varies over a roughly 20× span (0.10 → 2.00) and is non-unity far more often, which is the
  signature of a horizontal / radial footprint scale. `scale_b` (proposed `scale_height`) is
  near-unity for most actors and stretches only modestly (max ≈ 1.5), the signature of a vertical
  scale. The reference entry (id 0) is `(1.0, 1.0)`. Which factor is which axis remains
  **INFERRED**. Since the table is **never read at runtime**, this axis question is moot for the
  port — it is recorded only for archival completeness.
- **Sample-verified:** all 15 records decoded from the full 180-byte file.

---

## §2 — buff_icon_position.xdb (buff icon sprite-sheet positions)

- **Stride:** 12 bytes. **Record count:** 134 (`1,608 / 12 = 134`, exact). **No file header.**

| Offset | Size | Type   | Field       | Notes                                              | Confidence |
|-------:|-----:|--------|-------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `buff_id`   | Sparse buff-system identifier. Observed min 1, max 1103, with only 134 of that range populated — **non-contiguous**. Index the table by stored `buff_id`, never by row position. | CONFIRMED (non-contiguous) |
| +4     | 4    | u32 LE | `sprite_x`  | Pixel X origin on the buff-icon sprite sheet; advances in steps matching the cell origin spacing and wraps after 8 icons per row. | CONFIRMED |
| +8     | 4    | u32 LE | `sprite_y`  | Pixel Y origin on the sprite sheet; advances by the cell origin spacing when `sprite_x` wraps back to the row start. Not constant. Some tail rows are irregular (suggesting stitched sheets). The value **401** appears as a deliberate data-side **blank-tile convention** (see below), not a code sentinel. | CONFIRMED-variable |

- **`buff_id` continuity — RESOLVED:** the earlier conflict (one head-only reading saw `1..134`
  contiguous, another saw a sparse `1..1103`) is resolved in favour of the **non-contiguous**
  reading. A full scan confirms the head happens to begin contiguously (`1,2,3,…`) but the table
  spans up to 1103 with only 134 slots populated. **`buff_id != row_index`.**
- **`sprite_x` / `sprite_y` grid — RESOLVED:** the layout is a sprite-sheet row-wrap grid:
  `sprite_x` steps by the cell origin spacing for 8 columns, then resets while `sprite_y` advances
  by the same spacing to the next row. The third field is therefore the **pixel Y origin**, not a
  constant page flag. The earlier "constant 1" reading was a head-window artefact.
- **`sprite_y = 401` is a data-side blank-tile convention (CONFIRMED-variable):** the value `401`
  in `sprite_y` is **not** a code sentinel — no loader branch tests for it. It is a convention
  written into the data to point the entry at a deliberately empty (blank) tile on the sprite
  sheet, i.e. a "no icon here" placeholder expressed purely in the source data. The renderer reads
  it as an ordinary pixel-Y origin and draws the (blank) cell at that position; nothing special
  happens in code. A faithful port must treat `401` as just another Y origin and must **not** add a
  hard-coded sentinel check.
- **Sprite-sheet cell geometry (CONFIRMED-variable):** the **render cell is 21×21 pixels** (the
  drawn icon footprint), while **27** is the **origin spacing** between successive cells (the stride
  between one `sprite_x`/`sprite_y` origin and the next). The 21×21 draw cell sits inside the
  27-unit origin grid (the difference is the inter-cell gutter). Use 21×21 for the blit/quad size
  and 27 for stepping between origins; do not conflate the two.
- **Sample-verified:** all 134 records decoded.

---

## §3 — effectscale.xdb (effect scale table)

- **Stride:** 8 bytes. **Record count:** 2 (`16 / 8 = 2`, exact). **No file header.**
- **Field semantics CONFIRMED via the loader witness:** `scale_factor` is a **per-effect overall
  size multiplier** — a uniform scale applied to the whole effect when it is spawned. The earlier
  "single-sample risk" caveat is superseded for the *meaning* of the field: the loader-side witness
  confirms the consumer multiplies the effect's size by this factor. The narrow 2-record population
  remains a structural caveat only (no in-file periodicity to cross-check the `effect_key` split),
  but the column's purpose is now settled.

| Offset | Size | Type   | Field            | Notes                                        | Confidence |
|-------:|-----:|--------|------------------|----------------------------------------------|------------|
| +0     | 4    | u32 LE | `effect_key`     | Large `u32` key. Splits into a high 16 bits that are **shared across both records** (a fixed type-tag) and a low 16 bits that differ by 1 (a per-effect index). | MEDIUM |
| +4     | 4    | f32 LE | `scale_factor`   | **Per-effect overall size multiplier** — a uniform scale applied to the entire effect on spawn (record 0 = 3.0, record 1 = 2.0). | CONFIRMED |

- **`scale_factor` semantic (CONFIRMED):** the loader witness shows this float is consumed as the
  effect's overall size multiplier (uniform scale of the whole effect), keyed by `effect_key`. The
  port should look up the effect by key and multiply its size by this factor.
- **Key structure (NARROWED):** the high 16 bits of `effect_key` are identical in both records,
  consistent with a **table/type-tag in the high half**; the low 16 bits increment by 1, consistent
  with a **per-effect index in the low half**. This narrows the prior "hash vs. compound id"
  question toward a packed compound id, but with only 2 records the *split* stays MEDIUM.
- **Sample-verified:** both records decoded (full file = 16 bytes). **Single-sample structural
  caveat:** with only 2 records there is no in-file periodicity to confirm the `effect_key` field
  split (the `scale_factor` purpose is nonetheless confirmed by the consumer).

---

## §4 — vehicle.xdb (vehicle / mount catalogue)

- **Stride:** 52 bytes. **Record count:** 58 (`3,016 / 52 = 58`, exact). **No file header.**

| Offset | Size | Type   | Field          | Notes                                              | Confidence |
|-------:|-----:|--------|----------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `vehicle_id`   | Sequential 1-based (1..58)                          | CONFIRMED  |
| +4     | 4    | u32 LE | `item_id`      | Item id in `items.scr`; early records form a consecutive block (id 1 → 3108, id 2 → 3109, …); later records are non-consecutive | CONFIRMED |
| +8     | 4    | u32 LE | `tag_a`        | **Tool-side / editor metadata — NOT consumed at runtime (REFUTED as a runtime field).** Takes 3 distinct values across the 58 records (one dominant block of 16, one block of ~40, and a 2-record special family), so it reads like a vehicle-family discriminator, but the loader witness shows **no runtime consumer reads this column**. It exists only as table-tool authoring metadata. | REFUTED as runtime field (tool-side only) |
| +12    | 4    | u32 LE | `tag_b`        | **Constant `0x1575A3E4` in all 58 records.** Carries no per-vehicle information — a table-type stamp (table-name hash or compile-time constant inserted by the table tool). Proposed name `table_stamp`. | CONFIRMED (constant) |
| +16    | 4    | f32 LE | `param_0`      | Rider mount-point X offset (lateral). Range ≈ 0.0 .. 4.0; zero for most records (zero = use the model's own default mount point). | HIGH (present); INFERRED (X offset) |
| +20    | 4    | f32 LE | `param_1`      | **Always 0.0 across all 58 records** — a constrained / unused axis (consistent with a fixed vertical component if mount points share a height). | CONFIRMED (always 0); INFERRED (constrained axis) |
| +24    | 4    | f32 LE | `param_2`      | Rider mount-point Z offset (forward/back). Range ≈ -4.0 .. +2.5 (signed). Zero for most records. | HIGH (present); INFERRED (Z offset) |
| +28    | 4    | f32 LE | `param_3`      | Range ≈ 0.0 .. +5.0; likely a bounding / scale parameter. Zero for most records. | INFERRED |
| +32    | 4    | f32 LE | `param_4`      | 0.0 except in the 2-record special family, where it is a large negative offset (≈ -22 / -33) — specific to that family's geometry. | INFERRED |
| +36    | 16   | f32[4] | `param_5..8`   | 0.0 for almost all records; the last few records carry small positive values (≈ 1.5 .. 2.0), possibly collision / attachment dimensions for late-table mounts. | UNVERIFIED |

- **`tag_a` is tool-side, not a runtime field (REFUTED):** although the three distinct `tag_a`
  values partition the table into what looks like three vehicle families, the loader witness shows
  **no runtime consumer reads `tag_a`**. It is authoring/editor metadata only. A faithful port must
  **not** branch on `tag_a` or treat it as a live discriminator — the family grouping it implies is
  not used by the shipped client. (Engineers may parse and ignore the column, or skip it entirely.)
- **Rider mount-point reading (INFERRED):** the float params behave like a rider attachment
  offset — `param_0` lateral (X), `param_1` always zero (the constrained axis), `param_2`
  forward/back (Z) — with the all-zero records using the model's built-in default mount point.
  Treat the offset axes as INFERRED until the loader confirms.
- **Sample-verified:** all 58 records decoded for the column statistics above.

---

## §5 — creature_item.xdb (creature attached-item table)

- **Stride:** 48 bytes. **Record count:** 921 (`44,208 / 48 = 921`, exact). **No file header.**
- **Attachment-offset reading CONFIRMED (two-witness):** the per-record floats place the attached
  item relative to the creature in the **facing frame** as **XZ offset pairs with Y forced to 0** —
  they are **NOT** a bone reference. See the dedicated note below the table.

| Offset | Size | Type   | Field             | Notes                                                       | Confidence |
|-------:|-----:|--------|-------------------|-------------------------------------------------------------|------------|
| +0     | 4    | u32 LE | `creature_key`    | Large `u32` key. Runs of consecutive variants share sequential keys, but different creature families occupy disjoint key blocks (large gaps in the key space — ~3,175-value span for 921 records). A compound entity-type + index id. | CONFIRMED (block-sequential, gapped) |
| +4     | 4    | u32 LE | `item_id`         | Attached item id (e.g. 3001)                                | CONFIRMED  |
| +8     | 4    | f32 LE | `attach_x0`       | X component of the first XZ offset pair, in the creature's facing frame | CONFIRMED (XZ-facing offset) |
| +12    | 4    | f32 LE | `attach_z0`       | Z component of the first XZ offset pair (Y is forced to 0)  | CONFIRMED (XZ-facing offset) |
| +16    | 4    | f32 LE | `attach_x1`       | X component of the second XZ offset pair                    | CONFIRMED (XZ-facing offset) |
| +20    | 4    | f32 LE | `attach_z1`       | Z component of the second XZ offset pair (Y forced to 0)    | CONFIRMED (XZ-facing offset) |
| +24    | 4    | f32 LE | `attach_x2`       | X component of the third XZ offset pair                     | CONFIRMED (XZ-facing offset) |
| +28    | 4    | f32 LE | `attach_z2`       | Z component of the third XZ offset pair (Y forced to 0)     | CONFIRMED (XZ-facing offset) |
| +32    | 4    | f32 LE | `scale_or_radius` | Takes only **two values across all 921 records: 3.0 or 8.0.** The narrow two-level spread points to a **collision-sphere radius** (small creature = 3.0, large creature = 8.0) rather than a free-form scale. Proposed name `collision_radius`. | CONFIRMED (two values); INFERRED (collision radius) |
| +36    | 4    | f32 LE | `attach_probability_f32` | A **float**, not a constant zero. 716 of 921 records are 0.0; the remaining 205 carry clean fractional values (0.1, 0.2, 0.3, 0.4, 0.7, 0.8, 1.0, 1.8, 2.0, 2.2). The clean-fraction distribution is the signature of a sub-probability / multiplier float (0.0 = use default). | CONFIRMED (f32, non-zero in 205/921); INFERRED (sub-probability) |
| +40    | 1    | u8     | `flag_0`          | Binary flag. Values across the table: ~272 zero, ~649 one. Likely a primary-vs-secondary attachment-slot flag. | CONFIRMED (independent u8 flag) |
| +41    | 1    | u8     | `flag_1`          | Binary flag, **almost always 1** (916 of 921). Likely an is-active / is-visible flag. | CONFIRMED (independent u8 flag) |
| +42    | 1    | u8     | `flag_2`          | Binary flag, nearly always 1 (910 of 921). Likely paired with `flag_1` (is-enabled). | CONFIRMED (independent u8 flag) |
| +43    | 1    | u8     | `flag_3`          | Binary flag, mostly 0 (824 of 921); set to 1 in ~97 records. Likely a has-extended-data marker (does NOT directly correlate with the 205 non-zero `attach_probability_f32` records, so it encodes something distinct). | CONFIRMED (independent u8 flag) |
| +44    | 4    | u32 LE | `probability`     | **Constant `100` (`0x64`) in all 921 records.** Integer-percent probability; effectively a fixed 100% for this table. | CONFIRMED (constant 100) |

- **Attachment floats are XZ offset pairs in the facing frame (CONFIRMED, two-witness):** the six
  floats at +8..+31 are **three XZ offset pairs** — each pair is a horizontal offset (X then Z)
  expressed in the creature's **facing frame** (the local frame oriented by the creature's heading),
  with the **Y component forced to 0** (the offsets sit on the creature's horizontal plane). They
  are **NOT** a bone index / bone reference and **NOT** a packed rotation. The port should apply
  each pair as `(x, 0, z)` rotated into world space by the creature's facing, never look up a bone.
  This replaces the earlier "UNVERIFIED axis / unknown 3D transform" reading.
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
- **Sample-verified:** all 921 records decoded for the per-column statistics above.

---

## Known unknowns (all five tables)

- `actor_size.xdb`: **no runtime consumer (dead in this build)** — the on-disk layout is recorded
  for archival completeness only; which scale factor is which axis is moot for the port.
- `buff_icon_position.xdb`: the buff-icon texture path (not encoded in this file); whether any
  later VFS revision adds rows beyond the 134 observed. (`sprite_y = 401` blank-tile convention and
  the 21×21 cell / 27 origin-spacing geometry are now CONFIRMED-variable.)
- `effectscale.xdb`: confirmation of the high16=type-tag / low16=index split of `effect_key`
  (NARROWED but the table holds only 2 records). The `scale_factor` purpose (per-effect overall
  size multiplier) is now CONFIRMED.
- `vehicle.xdb`: `tag_a` is now known to be tool-side metadata (not a runtime field); the exact
  axis assignment of the rider-offset floats (INFERRED); the semantic of `param_3` and the tail
  `param_5..8` values remain open.
- `creature_item.xdb`: the six attachment floats are now CONFIRMED as three XZ offset pairs in the
  facing frame (Y forced 0). Final confirmation that `scale_or_radius` (only 3.0/8.0) is a collision
  radius; the exact meaning of each of the four flag bytes; whether `attach_probability_f32` is a
  drop sub-probability or a variant scale, remain open.

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
- Provenance: see `Docs/RE/journal.md`. CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
  `actor_size.xdb` dead/unread (do not load); `effectscale.scale_factor` = per-effect overall size
  multiplier (CONFIRMED); `vehicle.tag_a` tool-side metadata, not a runtime field (REFUTED as
  runtime); `creature_item` six floats = three XZ offset pairs in the facing frame, Y forced 0,
  not a bone (CONFIRMED); `buff_icon_position.sprite_y = 401` data-side blank-tile convention,
  render cell 21×21, origin spacing 27 (CONFIRMED-variable).
