# Format: .xdb (small fixed-stride data tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> C# parsers that decode any table below MUST cite `// spec: Docs/RE/formats/xdb_tables.md`
> on every magic constant, stride, and byte offset.
>
> ```
> verification: sample-verified            # every stride/count below confirmed by exact divisibility of the real VFS file size; head records byte-decoded on build 263bd994; loaders/consumers re-read 2026-06-21; all facts re-confirmed 2026-06-24
> ida_reverified: 2026-06-24
> ida_reverified: 2026-06-27 (CYCLE 14 re-anchor: 1 fact re-confirmed SAME; 0 corrected)
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> evidence: [static-ida, vfs-sample]
> confidence: HIGH on all load-bearing facts (loader sequence, strides/counts, field types, actor_size never-loaded verdict) as of 2026-06-24 re-verification pass
> conflicts: buff_icon_position origin spacing CORRECTED 27 -> 25 (sample shows step 25 on both axes); 21x21 draw-cell needs the sprite sheet to adjudicate
> CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19): creature_item.xdb RELABELLED = creature held-item VISUAL attachment keyed by creature_key (NOT a loot/drop table); vehicle.xdb keyed by vehicle_id (mount-visual + per-facing seat-Y); both §4/§5 DBG-pending runtime-use notes RESOLVED to STATIC-CONFIRMED.
> CORRECTED 2026-06-21: (a) loader mechanism = FOUR distinct, individually-named per-format loaders called DIRECTLY in sequence from the boot data-table thread (NOT an indirect loader-pointer dispatch table); (b) actor_size.xdb is FULLY DEAD — its path constant sits in the data-side pointer table but is NEVER passed to any loader (no actor_size loader exists), so it is never opened at runtime, sharper than the prior "opened but result unused" verdict; (c) vehicle per-facing seat-Y floats pinned to byte offsets +0x24/+0x28/+0x2C/+0x30 (facing 1..4).
> REVERIFIED 2026-06-24 (ida_anchor 263bd994, static-only): full re-check of all five small tables against both the on-disk samples and the IDA static analysis. Result: NO corrections. Every stride, count, field type, documented constant (vehicle tag_b constant, creature_item +32 ∈ {3.0,8.0}, +44=100, buff sprite_x step 25, sprite_y=401 blank-tile), loader sequence (four direct named loaders: effectscale/creature_item/vehicle/buff_icon_position), and actor_size never-loaded verdict confirmed exact. Confidence raised to HIGH on all load-bearing loader-mechanism and sample-verified facts.
> ```
>
> status: sample_verified
> sample_verified: true — every stride below is confirmed by exact divisibility of the real VFS
>                  file size; the small tables (§1–§4) were decoded in their entirety, and the
>                  large §5 table was scanned in full for the per-column statistics cited.
>
> **CAMPAIGN 10 Block D re-verify (build 263bd994, 2026-06-16, two-witness: static loader read + VFS
> sample).** All six `.xdb` files present at their documented sizes; all stride arithmetic re-confirmed
> [sample-verified] (`actor_size` 180/12=15, `buff_icon_position` 1,608/12=134, `effectscale` 16/8=2,
> `vehicle` 3,016/52=58, `creature_item` 44,208/48=921, `msg` 1,364,304/516=2,644). Head records
> byte-decoded and re-confirmed for every table. **One correction landed:** the `buff_icon_position`
> origin spacing is **25, not 27** (§2) — the sample shows the `sprite_x`/`sprite_y` origins step by 25
> on both axes; the earlier "27" was an unverified estimate.
>
> **2026-06-21 loader-mechanism re-read (static, build 263bd994) — supersedes the Block-D loader note.**
> The earlier "shared boot data-table corpus loader (an indirect per-format loader-pointer table
> dispatched at boot)" wording is **withdrawn**. The binary shows the boot data-table thread calls
> **four distinct, individually-named per-format loaders DIRECTLY, in sequence** (no indirect
> function-pointer dispatch table): `effectscale.xdb`, `creature_item.xdb`, `vehicle.xdb`,
> `buff_icon_position.xdb`. `msg.xdb` alone is loaded separately from the main-window startup path (the
> login-scene transition; see `misc_data.md §6` / `msg_xdb.md`). **`actor_size.xdb` has NO loader at
> all** — its path string is present only as a data-side pointer-table slot and is never passed to any
> open/load call, so it is **never opened at runtime in this build** (a sharper verdict than "opened
> but result unused"). All strides/counts/field offsets/field semantics re-confirmed against the
> loaders, the named consumers, and the real VFS sample. No addresses or decompiler output crossed the
> firewall.

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
| `data/script/actor_size.xdb`          | 180          | 12     | 15           | §1      | **NONE (never loaded in this build)** |
| `data/script/buff_icon_position.xdb`  | 1,608        | 12     | 134          | §2      | buff-icon renderer |
| `data/script/effectscale.xdb`         | 16           | 8      | 2            | §3      | effect-scale lookup |
| `data/script/vehicle.xdb`             | 3,016        | 52     | 58           | §4      | mount/seat catalogue (keyed by vehicle_id) |
| `data/script/creature_item.xdb`       | 44,208       | 48     | 921          | §5      | creature held-item visual attachment (keyed by creature_key) |
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

## Loaders & read algorithm

> **2026-06-21 re-read (static, build 263bd994).** This section replaces the prior "indirect
> per-format loader-pointer table dispatched at boot" framing.

### Who loads what, and from where

- **Four small tables — boot data-table thread, direct calls.** A dedicated boot data-table loading
  thread loads dozens of `.scr` / `.txt` / `.xdb` catalogues in a fixed sequence; at the **end** of
  that sequence it calls **four distinct, individually-named per-format loaders DIRECTLY** — ordinary
  direct calls, NOT an indirect dispatch through a function-pointer table — in this order:
  `effectscale.xdb`, then `creature_item.xdb`, then `vehicle.xdb`, then `buff_icon_position.xdb`.
  Each path is taken from a contiguous `char*` path-pointer table in the data segment (one slot per
  `data/script/*.xdb` string).
- **`msg.xdb` — main-window startup path.** Loaded once by its own loader during the transition into
  the login scene (the WinMain scene-state machine), **not** from the boot data-table thread. See
  `misc_data.md §6` (the large message-string table).
- **`actor_size.xdb` — never loaded.** Its `data/script/actor_size.xdb` path string is present in the
  data-side path-pointer table, but the slot is **never passed to any open/load call** and **no
  actor_size loader function exists**. The file is therefore **never opened at runtime in this
  build** (the string carries a single data reference — the pointer-table slot itself — and no code
  reference). This is the sharper successor to the earlier "opened but result unused" verdict.

### Common read algorithm (identical across all five live loaders)

The five live loaders (`msg.xdb` plus the four small tables) share one template; there is **no
per-field decode, no transform, and no endianness swap** — the on-disk bytes ARE the in-memory
record layout (little-endian):

1. **Open** the file by VFS path (a disk-file open-for-read; guarded by an is-good check).
2. **Derive the count:** `count = file_size / stride`, an integer divide where `stride` is the
   per-format constant (8 / 12 / 48 / 52 / 516). The divide is exact for every shipped file
   (remainder 0). (`effectscale` uses a right-shift by 3 for its `/8`.)
3. **Reserve / clear** the destination record buffer and reset the per-table index map for `count`
   entries.
4. **Bulk-read the whole file in one shot** into a flat record buffer (`stride * count` bytes). On a
   read failure the loader logs an "error read file" diagnostic and continues to the close and index
   steps — it does not abort early. [static-hypothesis — 2026-06-26: re-derived from a session whose
   IDB build differs from the pinned anchor 263bd994; the prior pinned-anchor text recorded "bails" —
   pending build reconciliation.] On success the buffer holds `count` back-to-back fixed-stride records.
5. **Close** the file.
6. **Index by first dword:** loop `i = 0 .. count-1`, take `key = first u32 of record i` (the field
   at record offset `+0`), and insert `key -> &record[i]` into that table's per-table ordered map
   (a balanced binary tree). The map **value is the record pointer**; lookups return that pointer, or
   0 on a miss.

So the only validation is is-good / read-ok; everything downstream keys on the record's `+0` dword.

---

## §1 — actor_size.xdb (actor scale table)

> **NEVER LOADED IN THIS BUILD — there is no loader for this file at all (CORRECTED 2026-06-21).**
> The file is present on disk in the VFS, and its `data/script/actor_size.xdb` path string is present
> in the data-side path-pointer table, **but the slot is never passed to any open/load call and no
> actor_size loader function exists.** So the byte verdict is **"path constant present in the data
> table, but the file is NEVER OPENED at runtime"** — sharper than, and superseding, the prior
> "opened at boot, but the parsed result is consumed by nothing" verdict. (The earlier reading came
> from a campaign that assumed actor_size shared the small-table corpus loader; the 2026-06-21
> loader-mechanism re-read shows the corpus thread calls only four named loaders directly —
> effectscale / creature_item / vehicle / buff_icon_position — and actor_size is **not** among them.)
>
> **[static-hypothesis — 2026-06-26] Physical pointer-array slot ordering:** the data segment holds a
> contiguous array of exactly five `char*` path pointers in this physical order — slot 1: effect-scale,
> slot 2: creature-item, slot 3: vehicle, slot 4: actor-size, slot 5: buff-icon-position. The boot
> data-table corpus loader calls slots 1, 2, 3, and then 5 in sequence, stepping over slot 4
> (actor-size) entirely — it is never pushed and never passed to any open/load call. Independently,
> the actor-size path string has exactly one reference in the whole image: that orphaned pointer-array
> slot, and the slot itself has no code reference. This makes the "never loaded" verdict precise: the
> slot is present between the vehicle and buff-icon-position slots (likely an authoring remnant) but
> is deliberately skipped at the call site. (Recovered 2026-06-26 from a session whose IDB build
> differs from the pinned anchor 263bd994; behaviors are build-stable, exact offsets pending build
> reconciliation.)
>
> Net guidance for the port: actor scaling in this build is driven elsewhere, not by this file, and a
> faithful 1:1 port need not load it at all. The layout below is documented for completeness and
> archival fidelity.
>
> Confidence: **never-loaded CONFIRMED (no loader exists; the path string has only the pointer-table
> data reference, no code reference)** — the on-disk record layout is the black-box witness (record 0
> = id 0, scale (1.0, 1.0), SAMPLE-VERIFIED on build 263bd994).

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
| +4     | 4    | u32 LE | `sprite_x`  | Pixel X origin on the buff-icon sprite sheet; advances in steps of the cell origin spacing (**25**, sample-verified) and wraps after 8 icons per row. Sample: `1, 26, 51, 76, 101, 126, 151, 176`, then wraps to `1`. | CONFIRMED (step 25, sample-verified) |
| +8     | 4    | u32 LE | `sprite_y`  | Pixel Y origin on the sprite sheet; advances by the cell origin spacing (**25**, sample-verified) when `sprite_x` wraps back to the row start (row 0 → row 1 = `1` → `26`). Not constant. Some tail rows are irregular (suggesting stitched sheets). The value **401** appears as a deliberate data-side **blank-tile convention** (see below), not a code sentinel. | CONFIRMED-variable |

- **`buff_id` continuity — RESOLVED:** the earlier conflict (one head-only reading saw `1..134`
  contiguous, another saw a sparse `1..1103`) is resolved in favour of the **non-contiguous**
  reading. A full scan confirms the head happens to begin contiguously (`1,2,3,…`) but the table
  spans up to 1103 with only 134 slots populated. **`buff_id != row_index`.**
- **`sprite_x` / `sprite_y` grid — RESOLVED:** the layout is a sprite-sheet row-wrap grid:
  `sprite_x` steps by the cell origin spacing for 8 columns, then resets while `sprite_y` advances
  by the same spacing to the next row. The third field is therefore the **pixel Y origin**, not a
  constant page flag. The earlier "constant 1" reading was a head-window artefact. **Origin spacing
  = 25 (CORRECTED, sample-verified):** the VFS sample shows the origins step by **25** on both axes
  (`sprite_x`: 1→26→51→76→101→…, diff 25; `sprite_y`: row 0→row 1 = 1→26, diff 25). The earlier "27"
  estimate is **REFUTED** by the sample — use 25 for the origin grid step.
- **`sprite_y = 401` is a data-side blank-tile convention (CONFIRMED-variable):** the value `401`
  in `sprite_y` is **not** a code sentinel — no loader branch tests for it. It is a convention
  written into the data to point the entry at a deliberately empty (blank) tile on the sprite
  sheet, i.e. a "no icon here" placeholder expressed purely in the source data. The renderer reads
  it as an ordinary pixel-Y origin and draws the (blank) cell at that position; nothing special
  happens in code. A faithful port must treat `401` as just another Y origin and must **not** add a
  hard-coded sentinel check.
- **Sprite-sheet cell geometry (origin spacing CONFIRMED 25; draw-cell sprite-sheet-pending):** the
  **origin spacing** between successive cells (the stride between one `sprite_x`/`sprite_y` origin and
  the next) is **25** on both axes — SAMPLE-VERIFIED from the actual byte steps (the prior "27" is
  REFUTED). The **draw-cell size** (the drawn icon footprint, previously stated as 21×21) **cannot be
  adjudicated from the table data alone** — it depends on the buff-icon sprite-sheet dimensions, which
  this file does not encode. With a 25-unit origin step, a 21×21 draw cell would imply a 4-pixel
  inter-cell gutter (25 − 21 = 4), which is plausible but UNVERIFIED. Treat the **25 origin step** as
  the confirmed stepping value and **flag the 21×21 draw cell as needing the sprite sheet to confirm**;
  do not conflate the origin step (25) with the blit/quad size (sprite-sheet-pending).
- **Sample-verified:** all 134 records decoded; origin step 25 verified from the head rows.

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
- **Sample-verified:** both records decoded (full file = 16 bytes). Build 263bd994 byte-confirmation:
  record 0 `effect_key` high-16 = low-16 base, `scale_factor = 3.0`; record 1 `effect_key` = record 0
  + 1 in the low half, `scale_factor = 2.0`; the high 16 bits are identical across both records and
  the low 16 differ by exactly 1 — re-confirming the high16=type-tag / low16=index reading at the
  byte level. **Single-sample structural caveat:** with only 2 records there is no in-file periodicity
  to confirm the `effect_key` field split as a general rule (the `scale_factor` purpose is nonetheless
  confirmed by the consumer).

---

## §4 — vehicle.xdb (mount / seat catalogue, keyed by vehicle_id)

> **CYCLE 1 (build 263bd994, 2026-06-19) — runtime use RESOLVED to STATIC-CONFIRMED (supersedes the
> prior "DBG-pending runtime use" note; the prior reading is kept below for history).** This table IS
> consumed at runtime, keyed by `vehicle_id` (record `+0`), on the **mounted-vehicle path**. Static
> decompilation of the consumers settled it — no debugger needed. Two runtime consumers, both reached
> when an actor is riding a vehicle and its visual/position is being updated:
> 1. **Mount-attachment refresh.** When the ridden target is a vehicle-kind actor and an attach-active
>    predicate holds, the row is looked up by `vehicle_id` and the rider's **mount visual is spawned**
>    from that row.
> 2. **Per-frame rider Y-placement.** After sampling terrain height under the actor, the mount-rider
>    branch looks up the row by `vehicle_id` and **adds a per-facing seat-height offset** read from the
>    record's trailing float array — one float per cardinal facing (facing indices 1..4; other facings
>    contribute 0). This lifts the rider to the correct seat height for the current heading.
>
> The `vehicle_id` key is the small sequential 1-based id (record `+0`), taken from the rider/mount
> actor when mounting/seating. `tag_a` (`+8`) and `tag_b` (`+12`) are **not** read by either consumer
> (consistent with the prior note — `tag_a` is not a lookup key; this does not over-claim it dead).
> This is a mount/seat catalogue, **not** a loot/drop table.
>
> Confidence: **CONFIRMED** (both consumers decompiled; the map key is the record's `+0` `vehicle_id`).

- **Stride:** 52 bytes. **Record count:** 58 (`3,016 / 52 = 58`, exact). **No file header.**

| Offset | Size | Type   | Field          | Notes                                              | Confidence |
|-------:|-----:|--------|----------------|----------------------------------------------------|------------|
| +0     | 4    | u32 LE | `vehicle_id`   | Sequential 1-based (1..58). **The runtime lookup key** — taken from the rider/mount actor when mounting/seating; resolves the row that supplies the mount visual and the per-facing seat-Y offset. | CONFIRMED (lookup key) |
| +4     | 4    | u32 LE | `item_id`      | Item id in `items.scr`; early records form a consecutive block (id 1 → 3108, id 2 → 3109, …); later records are non-consecutive | CONFIRMED |
| +8     | 4    | u32 LE | `tag_a`        | Takes 3 distinct values across the 58 records (one dominant block of 16, one block of ~40, and a 2-record special family), so it reads like a vehicle-family discriminator. **Not read by either mount-path consumer** (CYCLE 1, static-confirmed): the two runtime consumers touch only the `+0` key and the trailing per-facing float array, so `tag_a` is not a lookup key and is not consumed on the mount/seat path. It remains opaque metadata with no recovered runtime reader; parse it but do NOT branch on it in the port. (This does not over-claim the column dead — only that the two recovered consumers do not read it.) | Not read by mount-path consumers (CYCLE 1, static-confirmed) |
| +12    | 4    | u32 LE | `tag_b`        | **Constant `0x1575A3E4` in all 58 records.** Carries no per-vehicle information — a table-type stamp (table-name hash or compile-time constant inserted by the table tool). Proposed name `table_stamp`. | CONFIRMED (constant) |
| +16    | 4    | f32 LE | `param_0`      | Rider mount-point X offset (lateral). Range ≈ 0.0 .. 4.0; zero for most records (zero = use the model's own default mount point). | HIGH (present); INFERRED (X offset) |
| +20    | 4    | f32 LE | `param_1`      | **Always 0.0 across all 58 records** — a constrained / unused axis (consistent with a fixed vertical component if mount points share a height). | CONFIRMED (always 0); INFERRED (constrained axis) |
| +24    | 4    | f32 LE | `param_2`      | Rider mount-point Z offset (forward/back). Range ≈ -4.0 .. +2.5 (signed). Zero for most records. | HIGH (present); INFERRED (Z offset) |
| +28    | 4    | f32 LE | `param_3`      | Range ≈ 0.0 .. +5.0; likely a bounding / scale parameter. Zero for most records. | INFERRED |
| +32    | 4    | f32 LE | `param_4`      | 0.0 except in the 2-record special family, where it is a large negative offset (≈ -22 / -33) — specific to that family's geometry. | INFERRED |
| +36    | 16   | f32[4] | `param_5..8`   | 0.0 for almost all records; the last few records carry small positive values (≈ 1.5 .. 2.0), possibly collision / attachment dimensions for late-table mounts. | UNVERIFIED |

- **`tag_a` not read by the mount-path consumers (CYCLE 1, static-confirmed):** the three distinct
  `tag_a` values partition the table into what looks like three vehicle families, but neither of the
  two recovered runtime consumers reads this column — they touch only the `+0` `vehicle_id` key and
  the per-facing trailing float array. A faithful port should parse the column but **not** branch on
  it as a live discriminator; it carries no value consumed by the mount/seat path. (No over-claim of
  "dead" — only that the recovered consumers ignore it.)
- **Per-facing seat-Y offset (CYCLE 1, CONFIRMED from the consumer side; offsets pinned 2026-06-21):**
  the per-frame rider Y-placement consumer indexes the record's **trailing float array** by the
  rider's cardinal facing (facing 1..4; other facings contribute 0) and adds that float to the
  rider's world Y after the terrain-height sample — i.e. the trailing floats are **per-facing
  seat-height offsets** that lift the rider to the correct seat height for the current heading.
  **Exact float slots (2026-06-21 consumer re-read):** the consumer reads the record-as-float array
  at index `facing + 8`, so for facing 1..4 it reads float indices **9, 10, 11, 12** = byte offsets
  **+0x24, +0x28, +0x2C, +0x30** — i.e. the four floats this spec labels `param_2`, `param_3`,
  `param_4`, and the first slot of `param_5..8`. (So the leading-float "mount-point offset"
  interpretation below and the per-facing seat-Y reading overlap on `param_2..4`; on the seat-Y path
  the float chosen depends only on the current facing.) This settles the meaning of the trailing
  block on the seat-Y path; the earlier free-form "mount-point offset" reading below is the prior
  interpretation, kept for history.
- **Rider mount-point reading (prior INFERRED interpretation, kept for history):** the early float
  params were read as a rider attachment offset — `param_0` lateral (X), `param_1` always zero (the
  constrained axis), `param_2` forward/back (Z) — with the all-zero records using the model's
  built-in default mount point. The CONFIRMED seat-Y reading above supersedes the offset-axis guess
  for the **trailing** floats consumed per-facing; the exact axis assignment of the leading floats
  stays INFERRED.
- **Sample-verified:** all 58 records decoded for the column statistics above.

---

## §5 — creature_item.xdb (creature held-item VISUAL attachment, keyed by creature_key)

> **CYCLE 1 (build 263bd994, 2026-06-19) — RELABEL (binary-won spec reversal). This is a creature
> held/worn-item VISUAL attachment table — it places an item visual ON the creature. It is NOT a
> loot/drop table.** Any prior "drop" / "loot" / "drop sub-probability" framing is withdrawn; the
> consumer-side decompilation shows a visual-attachment + cadence-gate use, never a drop roll. The
> formerly DBG-pending consumer note is RESOLVED to **STATIC-CONFIRMED** — this table HAS live runtime
> consumers (no debugger needed). Two consumers, both on the creature attached-item path:
> 1. **Spawn-attach.** At creature spawn / appearance refresh the row is looked up by `creature_key`
>    and a visual item — the row's `item_id` (`+4`) — is **spawned and attached to the creature**. It
>    is positioned by the **three XZ offset pairs (`+8..+31`), each as `(x, 0, z)` rotated into world
>    space by the creature's facing** (Y forced to 0), with the row's visual-scale field (`+36`)
>    carried into the spawned descriptor. A state branch selects which of the second/third XZ pairs is
>    used.
> 2. **Per-tick gate.** Every tick the attached creature-item re-validates its pickup/effect cadence,
>    gating on the row's flag bytes (`+40`, `+43`) and using the `+44` column as a millisecond tick
>    interval, driving the pickup-validate / effect-fire branch. So `+40..+44` are **gate flags + a
>    cadence value**, NOT a draw/drop probability.
>
> **Key rule:** `creature_key` (record `+0`), read from the creature actor's **appearance / visual-key
> field**, indexes the lookup — at spawn and per-tick. It is the creature's visual/appearance id, not a
> mob/drop id.
>
> Confidence: **CONFIRMED** (both consumers decompiled; the map key is the record's `+0` `creature_key`,
> and the floats are read exactly as the §5 layout predicts).

- **Stride:** 48 bytes. **Record count:** 921 (`44,208 / 48 = 921`, exact). **No file header.**
- **Attachment-offset reading CONFIRMED (now from the consumer side too):** the per-record floats
  place the attached item relative to the creature in the **facing frame** as **XZ offset pairs with
  Y forced to 0** — they are **NOT** a bone reference. The spawn-attach consumer applies them exactly
  this way. See the dedicated note below the table.

| Offset | Size | Type   | Field             | Notes                                                       | Confidence |
|-------:|-----:|--------|-------------------|-------------------------------------------------------------|------------|
| +0     | 4    | u32 LE | `creature_key`    | Large `u32` key. Runs of consecutive variants share sequential keys, but different creature families occupy disjoint key blocks (large gaps in the key space — ~3,175-value span for 921 records). A compound entity-type + index id. **The runtime lookup key** — read from the creature actor's appearance / visual-key field at spawn and per-tick to resolve the attached-item row. | CONFIRMED (lookup key; block-sequential, gapped) |
| +4     | 4    | u32 LE | `item_id`         | Attached item id (e.g. 3001)                                | CONFIRMED  |
| +8     | 4    | f32 LE | `attach_x0`       | X component of the first XZ offset pair, in the creature's facing frame | CONFIRMED (XZ-facing offset) |
| +12    | 4    | f32 LE | `attach_z0`       | Z component of the first XZ offset pair (Y is forced to 0)  | CONFIRMED (XZ-facing offset) |
| +16    | 4    | f32 LE | `attach_x1`       | X component of the second XZ offset pair                    | CONFIRMED (XZ-facing offset) |
| +20    | 4    | f32 LE | `attach_z1`       | Z component of the second XZ offset pair (Y forced to 0)    | CONFIRMED (XZ-facing offset) |
| +24    | 4    | f32 LE | `attach_x2`       | X component of the third XZ offset pair                     | CONFIRMED (XZ-facing offset) |
| +28    | 4    | f32 LE | `attach_z2`       | Z component of the third XZ offset pair (Y forced to 0)     | CONFIRMED (XZ-facing offset) |
| +32    | 4    | f32 LE | `scale_or_radius` | Takes only **two values across all 921 records: 3.0 or 8.0.** The narrow two-level spread points to a **collision-sphere radius** (small creature = 3.0, large creature = 8.0) rather than a free-form scale. Proposed name `collision_radius`. | CONFIRMED (two values); INFERRED (collision radius) |
| +36    | 4    | f32 LE | `visual_scale`    | A **float**, not a constant zero. 716 of 921 records are 0.0; the remaining 205 carry clean fractional values (0.1, 0.2, 0.3, 0.4, 0.7, 0.8, 1.0, 1.8, 2.0, 2.2). **Consumer-confirmed:** the spawn-attach consumer copies this field into the spawned visual item's descriptor — it is a per-entry visual-scale / size field for the attached item (0.0 = use default), **not** a drop sub-probability. (Proposed name `visual_scale`; earlier `attach_probability_f32` framing withdrawn.) | CONFIRMED (f32, non-zero in 205/921; copied into the spawned visual) |
| +40    | 1    | u8     | `flag_0`          | Binary flag. Values across the table: ~272 zero, ~649 one. **Consumer-confirmed as a per-tick gate flag** — the per-tick consumer tests this byte when validating the attachment's pickup/effect cadence. | CONFIRMED (independent u8 gate flag) |
| +41    | 1    | u8     | `flag_1`          | Binary flag, **almost always 1** (916 of 921). Likely an is-active / is-visible flag. | CONFIRMED (independent u8 flag) |
| +42    | 1    | u8     | `flag_2`          | Binary flag, nearly always 1 (910 of 921). Likely paired with `flag_1` (is-enabled). | CONFIRMED (independent u8 flag) |
| +43    | 1    | u8     | `flag_3`          | Binary flag, mostly 0 (824 of 921); set to 1 in ~97 records. **Consumer-confirmed as a per-tick gate flag** — the per-tick consumer tests this byte alongside `+40` when gating the pickup/effect cadence. (Does NOT directly correlate with the 205 non-zero `visual_scale` records, so it encodes something distinct.) | CONFIRMED (independent u8 gate flag) |
| +44    | 4    | u32 LE | `tick_interval`   | **Constant `100` (`0x64`) in all 921 records.** **Consumer-confirmed as a millisecond tick-interval value** — the per-tick consumer uses this column as a cadence interval that gates how often the attachment re-validates its pickup/effect, **not** as a draw/drop probability percentage. (Proposed name `tick_interval`; earlier `probability` framing withdrawn.) | CONFIRMED (constant 100; consumed as a cadence interval) |

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
  four bytes to move together, which the data does not show. **CYCLE 1:** the per-tick consumer reads
  `+40` and `+43` as **pickup/effect cadence gate flags** (see the §5 header note).
- **`visual_scale` at +36 — RESOLVED (CYCLE 1, consumer-confirmed):** previously read as "always
  zero in the head" and tagged unknown, then as a candidate sub-probability; the full scan shows it
  is a float non-zero in 205 of 921 records with clean decimal values, and the spawn-attach consumer
  copies it into the spawned visual item's descriptor — so it is a per-entry **visual-scale / size**
  field, **not** a drop sub-probability. The earlier "sub-probability" reading is withdrawn.
- **`+44` is a cadence interval, not a probability (CYCLE 1, consumer-confirmed):** the constant-100
  column at `+44` is consumed by the per-tick gate as a **millisecond tick-interval** value (the
  cadence at which the attachment re-validates pickup/effect), not an integer-percent drop
  probability. The earlier "100% probability" reading is withdrawn.
- **Tail records (different family):** the last few records show all four flags set to 1 and a
  non-default `visual_scale` (e.g. 1.0), consistent with a distinct creature family (e.g. bosses /
  rare spawns). Field types are unchanged.
- **Sample-verified:** all 921 records decoded for the per-column statistics above.

---

## Known unknowns (all five tables)

- `actor_size.xdb`: **never loaded in this build** (no loader function; the path string sits in the
  data-side pointer table but is never passed to an open/load call) — the on-disk layout is recorded
  for archival completeness only; which scale factor is which axis is moot for the port.
- `buff_icon_position.xdb`: the buff-icon texture path (not encoded in this file); whether any
  later VFS revision adds rows beyond the 134 observed; the **draw-cell size** (provisionally 21×21)
  needs the sprite sheet to adjudicate — only the **origin spacing of 25** is sample-verified. (The
  `sprite_y = 401` blank-tile convention is CONFIRMED-variable; the prior "27 origin spacing" is
  REFUTED → 25.)
- `effectscale.xdb`: confirmation of the high16=type-tag / low16=index split of `effect_key`
  (NARROWED but the table holds only 2 records). The `scale_factor` purpose (per-effect overall
  size multiplier) is now CONFIRMED.
- **Small-table key-comparison signedness** (all four small tables — `effectscale`,
  `buff_icon_position`, `vehicle`, `creature_item`): the ordered-map **unsigned** key comparison was
  confirmed in the 2026-06-26 static pass for `msg.xdb` only; the corresponding signedness of each
  small-table ordered map was not individually re-checked in that pass. The small-table keys are
  non-negative ids in practice, so the signed/unsigned distinction is unlikely to cause mis-ordering,
  but it remains **UNVERIFIED** per the 2026-06-26 pass and is worth confirming if a port encounters
  a key with the high bit set. [static-hypothesis — 2026-06-26]
- `vehicle.xdb`: runtime use is now **STATIC-CONFIRMED** (CYCLE 1) — keyed by `vehicle_id`, consumed
  on the mount path (mount-visual spawn + per-facing seat-Y, the latter read from float indices 9..12
  = byte offsets +0x24/+0x28/+0x2C/+0x30 for facing 1..4). `tag_a` is **not read by either consumer**
  (not a key; carries no value on the mount/seat path) — no live-debugger pass needed for that
  verdict. Open: the exact axis assignment of the *leading* rider-offset floats (INFERRED); the
  semantic of `param_3` and any non-seat-Y use of the leading floats.
- `creature_item.xdb`: **RELABELLED (CYCLE 1) as a creature held-item VISUAL attachment table, not a
  loot/drop table** — STATIC-CONFIRMED consumers (spawn-attach + per-tick gate), keyed by
  `creature_key` from the creature actor's appearance field. The six attachment floats are CONFIRMED
  as three XZ offset pairs in the facing frame (Y forced 0); `+36` is a visual-scale field (copied
  into the spawned visual) and `+44` is a millisecond cadence interval — neither is a drop
  probability. Open: final confirmation that `scale_or_radius` (only 3.0/8.0) is a collision radius;
  the exact meaning of `flag_1`/`flag_2`.

---

## Cross-references

- Related formats: `config_tables.md` (the sibling `.scr` / `.do` catalogues; `vehicle.xdb` and
  `creature_item.xdb` reference `item_id`s that live in `items.scr` / `citems.scr`),
  `misc_data.md §6` (the large `msg.xdb` message-string table), `effects.md` (effect keys used by
  `effectscale.xdb`).
- Glossary: see `Docs/RE/names.yaml` (proposed: `ActorSizeXdb`, `BuffIconPositionXdb`,
  `EffectScaleXdb`, `VehicleXdb`, `CreatureItemXdb` and their record fields, including
  `VehicleXdb.tableStamp`, `CreatureItemXdb.collisionRadius`,
  `CreatureItemXdb.visualScale` (the `+36` field, formerly proposed `attachProbability`),
  `CreatureItemXdb.tickInterval` (the constant-100 `+44` field, formerly proposed `probability`)).
- **Runtime linkages (named consumers, 2026-06-21).** Each live table is indexed by its record `+0`
  dword and looked up on a specific path:
  - `effectscale.xdb`: looked up by `effect_key` from the lazy `.xeff` parse path; the looked-up
    `scale_factor` **overwrites** (does not stack with) the `.xeff` base-scale field at parse time
    (ctor default 1.0). Join key = `effect_key` → the `.xeff` effect being parsed (see `effects.md`).
  - `buff_icon_position.xdb`: looked up by `buff_id` from the buff/status icon panels (every UI panel
    that draws a buff-icon strip); returns the `(sprite_x, sprite_y)` pixel origin. Join key =
    `buff_id`.
  - `vehicle.xdb`: looked up by `vehicle_id` on the mount path — a mount-attachment refresh spawns the
    mount visual from the row (`item_id` `+4` → `items.scr`), and a per-frame rider Y-placement adds
    the per-facing seat-Y float (indices 9..12). Join key = `vehicle_id`; secondary `item_id` →
    `items.scr`.
  - `creature_item.xdb`: looked up by `creature_key` (the creature actor's appearance / visual-key
    field) at spawn (spawn-attach) and per-tick (cadence gate). Join key = `creature_key`; secondary
    `item_id` `+4` → `items.scr` / `citems.scr`.
  - `msg.xdb`: looked up by `msg_id`; returns the 512-byte CP949 caption/format template (see
    `misc_data.md §6`).
- Provenance: see `Docs/RE/journal.md`. CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):
  `effectscale.scale_factor` = per-effect overall size multiplier (CONFIRMED); `creature_item` six
  floats = three XZ offset pairs in the facing frame, Y forced 0, not a bone (CONFIRMED);
  `buff_icon_position.sprite_y = 401` data-side blank-tile convention. (NOTE: the VFS-MASTERY
  `actor_size.xdb` "result-unused via the boot corpus loader" reading is SUPERSEDED by the 2026-06-21
  re-read below — actor_size has no loader at all and is never opened.)
  **CAMPAIGN 10 Block D (build 263bd994, 2026-06-16):** all six strides/counts re-confirmed
  [sample-verified]; `buff_icon_position` origin spacing CORRECTED **27 → 25** (sample byte-steps),
  the 21×21 draw cell flagged sprite-sheet-pending.
  **2026-06-21 loader-mechanism + consumer re-read (static, build 263bd994) — supersedes the Block-D
  loader note:** the four small tables (`effectscale`, `creature_item`, `vehicle`,
  `buff_icon_position`) load through **four distinct, individually-named per-format loaders called
  DIRECTLY in sequence** from the boot data-table thread — **not** an indirect loader-pointer dispatch
  table. `msg.xdb` is loaded separately from the main-window startup path (the login-scene transition;
  see `misc_data.md §6` / `msg_xdb.md`). **`actor_size.xdb` is NEVER loaded** — no loader exists and
  its path string is never passed to an open/load call (data-side pointer-table slot only). The
  vehicle per-facing seat-Y floats are pinned to byte offsets +0x24/+0x28/+0x2C/+0x30 (facing 1..4).
  **CYCLE 1 (build 263bd994, 2026-06-19) — xdb runtime linkage (supersedes the VFS-MASTERY
  `vehicle.tag_a` DBG-pending note):** both formerly-pending runtime-use notes RESOLVED to
  STATIC-CONFIRMED by decompiling the consumers — no debugger needed. `vehicle.xdb` IS consumed at
  runtime keyed by `vehicle_id` (record `+0`) on the mount path: a mount-attachment refresh spawns the
  mount visual from the looked-up row, and a per-frame rider Y-placement adds a per-facing seat-height
  offset from the record's trailing float array (one float per cardinal facing, indices 1..4). `tag_a`
  (`+8`)/`tag_b` (`+12`) are not read by either consumer (not keys). **`creature_item.xdb` RELABELLED:
  it is a creature held-item VISUAL attachment table, NOT a loot/drop table** — keyed by `creature_key`
  (record `+0`) read from the creature actor's appearance/visual-key field, consumed at creature spawn
  (spawn-attach: the row's `item_id` `+4` is attached as a visual, placed by the three facing-frame XZ
  offset pairs `+8..+31` with the `+36` visual-scale) and per-tick (a flag/cadence gate on `+40`,
  `+43`, and the `+44` millisecond tick-interval). The earlier "drop / drop sub-probability /
  100%-probability" framing is withdrawn.
  **2026-06-26 [static-hypothesis — session IDB build differs from pinned anchor 263bd994; behaviors
  build-stable, exact offsets pending build reconciliation]:** all five live loaders re-decompiled
  directly (not from prior IDB comments) — no corrections to strides, counts, field types, loader
  call sequence, or actor_size never-loaded verdict. Two sharper additions integrated above: (a) the
  physical pointer-array layout — five-slot contiguous array with actor_size occupying slot 4 between
  vehicle and buff-icon-position, stepped over at the call site, path string carrying exactly one data
  reference and no code reference; (b) the bulk-read error path continues to close/index rather than
  aborting early (hypothesis correction to prior "bails" wording — pending pinned-anchor
  reconciliation). New known unknown: the ordered-map key-comparison signedness of the four small
  tables was not individually re-verified in this pass (msg.xdb unsigned ordering confirmed only). No
  addresses or decompiler output crossed the firewall.
