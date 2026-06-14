# Format: .xdb (small fixed-stride data tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true — every stride below is confirmed by exact divisibility of the real VFS
>                  file size, and the listed records were decoded from the file's head window.

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

---

## §1 — actor_size.xdb (actor scale table)

- **Stride:** 12 bytes. **Record count:** 15 (`180 / 12 = 15`, exact). **No file header.**

| Offset | Size | Type   | Field           | Notes                                            | Confidence |
|-------:|-----:|--------|-----------------|--------------------------------------------------|------------|
| +0     | 4    | u32 LE | `actor_kind_id` | Sequential 0-based index (0..14)                 | CONFIRMED  |
| +4     | 4    | f32 LE | `scale_a`       | First scale factor; reference entry (id 0) = 1.0 | CONFIRMED (value); UNVERIFIED (axis) |
| +8     | 4    | f32 LE | `scale_b`       | Second scale factor; reference entry (id 0) = 1.0 | CONFIRMED (value); UNVERIFIED (axis) |

- The two scale factors are a horizontal/radial vs. vertical pair (proposed names
  `scale_width` / `scale_height`). Which factor maps to which axis is **UNVERIFIED** without the
  loader. The reasoning for the proposed direction: id 0 → (1.0, 1.0) is the unscaled reference;
  one entry reads (2.0, 1.0), i.e. a double on the first factor with the second held at 1.0,
  consistent with the first factor being a horizontal/width scale.
- **Sample-verified:** all 15 records decoded from the full 180-byte file.

---

## §2 — buff_icon_position.xdb (buff icon sprite-sheet positions)

- **Stride:** 12 bytes. **Record count:** 134 (`1,608 / 12 = 134`, exact). **No file header.**

| Offset | Size | Type   | Field                | Notes                                              | Confidence |
|-------:|-----:|--------|----------------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `buff_id`            | 1-based sequential (1..134)                         | CONFIRMED  |
| +4     | 4    | u32 LE | `sprite_x`           | Pixel X origin on the buff-icon sprite sheet; advances in constant steps of 25 px | CONFIRMED (pattern) |
| +8     | 4    | u32 LE | `sprite_row_or_page` | Observed `1` in every checked record; likely a sheet row or page index | CONFIRMED (value=1); UNVERIFIED (semantic) |

- The `sprite_x` progression is `1, 26, 51, 76, 101, …` (a constant +25 px step), consistent with
  a 25-px-wide icon cell grid. The relation `sprite_x = (buff_id - 1) * 25 + 1` holds for the
  head records.
- **Sample-verified:** head records decoded (first window); full count (134) confirmed by exact
  stride divisibility.

---

## §3 — effectscale.xdb (effect scale table)

- **Stride:** 8 bytes. **Record count:** 2 (`16 / 8 = 2`, exact). **No file header.**

| Offset | Size | Type   | Field            | Notes                                        | Confidence |
|-------:|-----:|--------|------------------|----------------------------------------------|------------|
| +0     | 4    | u32 LE | `effect_key`     | Large `u32` key (not a small sequential int); the two records' keys differ by 1 | CONFIRMED |
| +4     | 4    | f32 LE | `scale_factor`   | Floating-point scale (record 0 = 3.0, record 1 = 2.0) | CONFIRMED (value); UNVERIFIED (semantic) |

- The two keys are large and sequential-by-1; they may be a hash or a compound id (effect type +
  subtype packed into a `u32`). This is the smallest `.xdb` in the VFS — exactly 2 entries.
- **Sample-verified:** both records decoded (full file = 16 bytes).

---

## §4 — vehicle.xdb (vehicle / mount catalogue)

- **Stride:** 52 bytes. **Record count:** 58 (`3,016 / 52 = 58`, exact). **No file header.**

| Offset | Size | Type      | Field          | Notes                                              | Confidence |
|-------:|-----:|-----------|----------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE    | `vehicle_id`   | Sequential 1-based (1..58)                          | CONFIRMED  |
| +4     | 4    | u32 LE    | `item_id`      | Item id in `items.scr`; consecutive block (id 1 → 3108, id 2 → 3109, …) | CONFIRMED |
| +8     | 8    | u8[8]     | `unknown_8b`   | Identical 8-byte run across the head records. **UNVERIFIED** — could be two `u32` sub-fields with fixed values in this build, or a serialized handle / debug artifact. Do not rely on it. | UNVERIFIED |
| +16    | 36   | u8[36]    | `zero_region`  | All zero in the head records; likely multiple typed fields that are zero for the base vehicle entries | CONFIRMED (value=0 in head); UNVERIFIED (layout) |

- The `item_id` values are consecutive (3108+), so vehicles map to a contiguous block of item ids.
- **Sample-verified:** head records decoded for `vehicle_id` and `item_id`; full count (58)
  confirmed by exact divisibility. The +8 8-byte field and the +16 region are **UNVERIFIED**.

---

## §5 — creature_item.xdb (creature attached-item table)

- **Stride:** 48 bytes. **Record count:** 921 (`44,208 / 48 = 921`, exact). **No file header.**

| Offset | Size | Type   | Field             | Notes                                                       | Confidence |
|-------:|-----:|--------|-------------------|-------------------------------------------------------------|------------|
| +0     | 4    | u32 LE | `creature_key`    | Large `u32` key, sequential-by-1; a compound id             | CONFIRMED (pattern) |
| +4     | 4    | u32 LE | `item_id`         | Attached item id (e.g. 3001)                                | CONFIRMED  |
| +8     | 4    | f32 LE | `attach_f0`       | First of six attachment floats; negative model-local offset | CONFIRMED (present); UNVERIFIED (axis) |
| +12    | 4    | f32 LE | `attach_f1`       | Second attachment float                                     | CONFIRMED (present); UNVERIFIED (axis) |
| +16    | 4    | f32 LE | `attach_f2`       | Third attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +20    | 4    | f32 LE | `attach_f3`       | Fourth attachment float                                     | CONFIRMED (present); UNVERIFIED (axis) |
| +24    | 4    | f32 LE | `attach_f4`       | Fifth attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +28    | 4    | f32 LE | `attach_f5`       | Sixth attachment float                                      | CONFIRMED (present); UNVERIFIED (axis) |
| +32    | 4    | f32 LE | `scale_or_radius` | Notably larger than the six attachment floats (head value 8.0); may be a scale or collision radius | CONFIRMED (present); UNVERIFIED (semantic) |
| +36    | 4    | u32 LE | `unknown_u1`      | Zero in the head records                                    | CONFIRMED (value=0 in head); UNVERIFIED |
| +40    | 1    | u8     | `flag_0`          | Zero in the head records                                    | UNVERIFIED |
| +41    | 1    | u8     | `flag_1`          | `1` in every head record                                    | UNVERIFIED |
| +42    | 1    | u8     | `flag_2`          | Alternates 0/1 across consecutive head records (toggle — e.g. left/right or main/off-hand slot) | UNVERIFIED |
| +43    | 1    | u8     | `flag_3`          | Zero in the head records                                    | UNVERIFIED |
| +44    | 4    | u32 LE | `probability`     | `100` (`0x64`) in every head record; likely a drop/attach probability in integer percent | CONFIRMED (value); UNVERIFIED (semantic) |

- The six floats at +8..+31 likely encode a 3D attachment transform (a position plus a rotation,
  or a position plus a second point / bounding extent) for the item rendered on the creature. The
  negative values are consistent with model-local offsets. **The mapping of each float to a named
  axis is UNVERIFIED** — do not assume an order.
- **Sample-verified:** head records decoded; full count (921) confirmed by exact stride
  divisibility.

---

## Known unknowns (all five tables)

- `actor_size.xdb`: which scale factor is which axis (width vs. height).
- `buff_icon_position.xdb`: semantic of the third field (`sprite_row_or_page`, constant 1 in
  the sample).
- `effectscale.xdb`: the internal structure of the large `effect_key` (hash vs. packed compound),
  and the semantic of `scale_factor`.
- `vehicle.xdb`: the 8-byte field at +8 (its real type/meaning), and the field layout inside the
  36-byte zero region at +16 (all zero only in the head records).
- `creature_item.xdb`: the axis mapping of the six attachment floats; the meaning of
  `scale_or_radius` at +32; the per-bit meaning of the four flag bytes at +40..+43; and whether
  `probability` at +44 is truly a percent.

---

## Cross-references

- Related formats: `config_tables.md` (the sibling `.scr` / `.do` catalogues; `vehicle.xdb` and
  `creature_item.xdb` reference `item_id`s that live in `items.scr` / `citems.scr`),
  `misc_data.md §6` (the large `msg.xdb` message-string table), `effects.md` (effect keys used by
  `effectscale.xdb`).
- Glossary: see `Docs/RE/names.yaml` (proposed: `ActorSizeXdb`, `BuffIconPositionXdb`,
  `EffectScaleXdb`, `VehicleXdb`, `CreatureItemXdb` and their record fields).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).
