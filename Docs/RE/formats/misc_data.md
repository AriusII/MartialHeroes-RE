# Format: misc_data  (miscellaneous script and data files: .xdb / .mi / .tol / .ion / .sc)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.

<!--
verification: sample-verified (every documented format re-decoded against the real VFS on build
              263bd994 — strides divide file sizes with zero remainder, CP949 text decodes
              coherently); a handful of field semantics remain capture/debugger-pending;
              re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: (RESOLVED, CYCLE 7) mobinfo.mi field6 (was portrait_res_3) — RESOLVED: mobinfo.mi is
           DEAD in build 263bd994 (confirmed not read — no loader, no path literal, not in the
           boot data-table corpus pointer table, not compiled in as a static array), so field6
           has no consumer read-site to pin. The portrait_res_3 label is withdrawn; field6 is a
           4-byte u32/i32 LE with 0xFFFFFFFF = -1 = "none". See §2.
-->

> **status: sample_verified** — every format below was re-confronted with the real VFS mount
> (43 347 entries) on build `263bd994`: `actor_size.xdb`, `buff_icon_position.xdb`,
> `effectscale.xdb`, `mobinfo.mi`, `.tol`, `descript.ion`, `discript.sc`, `msg.xdb`,
> `mapsetting.scr`, and `regiontableNNN.bin` all re-decoded cleanly (strides divide their file
> sizes with zero remainder; CP949 names decode coherently). The previously-carried `mobinfo.mi`
> `field6` (was `portrait_res_3`) conflict is now **RESOLVED**: `mobinfo.mi` is **DEAD in this build**
> (confirmed not read), so `field6` has no consumer read-site and the `portrait_res_3` label is
> withdrawn (§2). Two undocumented `.xdb` variants present in the VFS are now covered:
> `creature_item.xdb` (§8) and `vehicle.xdb` (§9). See per-section confidence notes.

This document covers the miscellaneous script and data file types that share no common wire format
but are all small script/data assets extracted from the `.pak` / VFS container. They are grouped
here because none warrants a standalone spec file. Each section is self-contained.

---

## Section 1 — `.xdb` Script Data Binary files

### 1.1 Identification (all `.xdb` sub-variants)

- **Extension:** `.xdb`
- **Found in:** `.pak` archive; logical path: `data/script/*.xdb`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.
- **Record count:** Derived from file size divided by the per-variant stride (no stored count).
  The file size must be an exact multiple of the stride; any remainder is an error.

**`.xdb` VFS census (build `263bd994`, SAMPLE-VERIFIED):** six `.xdb` files live under
`data/script/`. Each is a headerless flat array; the stride differs per variant and is the only way
to know how to read a given file (the parser must already know which variant it is loading).

| Path | Size (bytes) | Stride | Records | Section |
|---|---:|---:|---:|---|
| `data/script/actor_size.xdb` | 180 | 12 | 15 | §1.2 |
| `data/script/buff_icon_position.xdb` | 1 608 | 12 | 134 | §1.3 |
| `data/script/effectscale.xdb` | 16 | 8 | 2 | §1.4 |
| `data/script/creature_item.xdb` | 44 208 | 48 | 921 | §9 |
| `data/script/vehicle.xdb` | 3 016 | 52 | 58 | §10 |
| `data/script/msg.xdb` | 1 364 304 | 516 | 2 644 | §6 |

All six strides divide their file sizes with zero remainder. The first three plus `msg.xdb` carry
full documented layouts; `creature_item.xdb` and `vehicle.xdb` (§9, §10) carry head-only layouts
recovered in the 2026-06-16 re-verification pass and are graded accordingly.

---

### 1.2 `actor_size.xdb` — Per-actor-class scale override table

**sample_verified: true**

**Role:** Maps an integer actor-class ID to two floating-point scale factors (XZ-plane scale and
Y-axis scale). Used at runtime to resize actor meshes per class.

**Record stride:** 12 bytes. Record count = `file_size / 12`.

#### Record layout (12 bytes)

| Offset | Size | Type  | Field          | Notes                                      | Confidence |
|-------:|-----:|-------|----------------|--------------------------------------------|------------|
| 0      | 4    | u32LE | actor_class_id | Sequential 0-based actor class identifier  | HIGH       |
| 4      | 4    | f32LE | scale_xz       | XZ-plane (body width) scale; observed range: 0.10 – 2.00 | HIGH |
| 8      | 4    | f32LE | scale_y        | Y-axis (body height) scale; observed range: 1.00 – 1.50 | HIGH |

**Notes:**
- An identity record (scale_xz = 1.0, scale_y = 1.0) is valid and common.
- The stride 12 is confirmed by exact file-size division: a known sample of 180 bytes yields
  exactly 15 records with no remainder.
- The record key (`actor_class_id`) is used as a lookup key in a runtime associative table keyed
  by the first u32 of each record; this applies to all `.xdb` variants (see §1.5).

---

### 1.3 `buff_icon_position.xdb` — Buff-effect icon atlas coordinates

**sample_verified: true** (record stride and field roles); **CODE-CONFIRMED** (record layout and
resolver behaviour — confirmed from the icon-position lookup routine).

**Role:** Maps a buff-effect integer ID to the pixel origin of its icon cell within a single shared
UI sprite-atlas texture. The atlas is `data/ui/skillicon/stateicon.dds` (a 512 × 512 DXT2 texture);
the same atlas serves every buff and state. The per-buff `(atlas_x, atlas_y)` pair is **stored data**
read from this file, not a position derived from a grid formula and not a per-buff texture file.

**Record stride:** 12 bytes. Record count = `file_size / 12`.

#### Record layout (12 bytes)

| Offset | Size | Type  | Field   | Notes                                                        | Confidence |
|-------:|-----:|-------|---------|--------------------------------------------------------------|------------|
| 0      | 4    | u32LE | buff_id | Buff-effect identifier (the lookup key); non-sequential; range 1 – 1103 observed | CODE-CONFIRMED |
| 4      | 4    | i32LE | atlas_x | Pixel X of the icon cell's top-left corner within `stateicon.dds` | CODE-CONFIRMED |
| 8      | 4    | i32LE | atlas_y | Pixel Y of the icon cell's top-left corner within `stateicon.dds` | CODE-CONFIRMED |

> **(corrected 2026-06-13: `atlas_x` / `atlas_y` are signed `i32LE`, not `u32LE`; the resolver
> returns them as a signed coordinate pair. The earlier "25 × 25 cells, 1-based grid formula"
> description was wrong — cell size is class-dependent (23 × 23 or 25 × 25, see §1.6) and the
> coordinates are raw stored pixel values read from this file, never inferred from a grid stride.)**

**Atlas model:**
- The icon source is a single shared atlas, `data/ui/skillicon/stateicon.dds` (512 × 512, DXT2).
  There is no per-buff texture file and no separate sheet-plus-cell-index addressing.
- Each active buff blits one cell from that atlas at the per-buff `(atlas_x, atlas_y)` read from
  this table. The cell's width and height are not stored here; they are fixed by buff class at
  render time (23 × 23 for `buff_id` ≤ 80, 25 × 25 for `buff_id` > 80 — see §1.6).
- The parser must treat `atlas_x` and `atlas_y` as raw pixel values and never infer them from a
  formula. Some coordinates fall off any regular 25-pixel grid (e.g. 250, 251, 276, 304 …),
  confirming the values are authored data, not computed.

**Notes:**
- The stride 12 is confirmed by exact file-size division: a known sample of 1608 bytes yields
  exactly 134 records with no remainder.
- The record key (`buff_id`) is used as a lookup key in a runtime red-black tree (see §1.5). The
  lookup routine searches the tree by `buff_id` and returns the `(atlas_x, atlas_y)` pair from the
  record (record offsets +4 and +8), or `(0, 0)` when the ID is absent.
- This table is the missing catalogue for `stateicon.dds`: that atlas ships with no companion text
  or descriptor file, so the `(atlas_x, atlas_y)` mapping lives entirely in this `.xdb`.
- For the HUD buff bar that consumes this table and the wire packet that drives it, see §1.6.

---

### 1.4 `effectscale.xdb` — Per-effect-object scale table

**sample_verified: true**

**Role:** Maps a large game-object resource ID to a floating-point scale multiplier applied to the
associated particle or effect mesh at runtime.

**Record stride:** 8 bytes. Record count = `file_size / 8`.

#### Record layout (8 bytes)

| Offset | Size | Type  | Field     | Notes                                              | Confidence |
|-------:|-----:|-------|-----------|----------------------------------------------------|------------|
| 0      | 4    | u32LE | object_id | Large non-sequential resource identifier           | HIGH       |
| 4      | 4    | f32LE | scale     | Float scale multiplier; observed values: 2.0, 3.0 | HIGH       |

**Notes:**
- The stride 8 is confirmed by exact file-size division: a known sample of 16 bytes yields exactly
  2 records with no remainder. The sample is small; assume more records exist in production builds.
- The record key (`object_id`) is used as a lookup key in a runtime red-black tree (see §1.5).
- Resource IDs in the observed sample are large values in the range of 3.6 × 10^8 and appear
  sequentially allocated.

---

### 1.5 Cross-variant notes for all `.xdb` files

- No magic bytes, no version, no stored record count. The stride is the identifying characteristic
  of each named variant; the parser must already know which variant it is loading.
- Record layout is purely numeric (u32 / f32). No text fields. No CP949 content.
- The first field of each record serves as the lookup key in a runtime associative structure. The
  key is always u32LE at offset 0 within the record.
- File size must be an exact multiple of the stride; if not, the file is malformed.

**Known unknowns:**
- No loader routine located for `actor_size.xdb` or `effectscale.xdb`; path strings were not
  found as string literals in the binary. The strides are confirmed by sample arithmetic alone for
  these two variants.
- Whether additional named `.xdb` variants exist beyond the three documented here is unknown.
- Whether `.xdb` files carry a version across different game patches is unknown (single samples only).

---

### 1.6 Buff/state HUD bar — render model and wire source for `buff_icon_position.xdb`

> **Verification status: CODE-CONFIRMED** (atlas binding, slot count, cell sizes, the per-refresh
> reset, and the wire-packet structure are all confirmed from the buff-window builder, the slot
> setter, and the response-4/102 handler). **CAPTURE-UNVERIFIED:** no packet capture was available,
> so the wire-field *meanings* of the 12-byte active-buff record (beyond `buff_id`) are graded
> PLAUSIBLE, and the duration unit is unconfirmed. The static structure is certain; the live values
> have not been observed against a real capture.

This section documents how `buff_icon_position.xdb` (§1.3) is consumed at runtime: the HUD buff bar
that draws the icons, and the server packet that decides which buffs are active.

**Shared atlas (CODE-CONFIRMED):** The buff-window builder loads `data/ui/skillicon/stateicon.dds`
(512 × 512 DXT2) exactly once and binds that single texture handle to all icon slots. It also loads
two companion sheets for the same window: `data/ui/skillwindow.dds` (window chrome) and
`data/ui/blacksheet.dds` (a solid-fill sheet). The icon-source model is therefore one shared atlas
with per-buff UV offsets, not one texture per buff.

**Slot count and cell sizes (CODE-CONFIRMED):** The buff bar has **30 icon slots**. The slot setter
selects the cell size from the buff class:

| Buff class | `buff_id` range | Cell w × h | Position model |
|---|---|---|---|
| Buff | `buff_id` ≤ 80 | 23 × 23 px | Flowing left-to-right counter; placed in the next free position and the counter advances |
| State / debuff | `buff_id` > 80 | 25 × 25 px | Fixed per-slot screen position |

The boundary value 80 is a literal comparison in the slot setter. Whether 80 is a true semantic
buff-versus-debuff partition in the catalogue, or merely an array-bound guard between two internal
UV tables, is not confirmed (it needs the `buff_id` distribution from a real VFS dump of the table).

**Per-refresh reset (CODE-CONFIRMED):** Each time the bar refreshes, the window first clears and
hides all 30 slots (zeroing each slot's source rectangle and size, clearing its caption, and
resetting the flowing-layout counter), then re-shows and positions only the slots that the incoming
packet marks active. There is no client-side expiry: a buff vanishes only when a later refresh omits
it. Slot assignment is therefore **fully server-driven** — the server owns which buff occupies which
slot and how many stack.

#### Wire source — server-to-client response, major 4 / minor 102 (`SkillWindowStateUpdate`)

> **CODE-CONFIRMED structure / CAPTURE-UNVERIFIED values.** Opcode tuple and payload size are read
> from the dispatch-table install and the handler's fixed-length read; field meanings within the
> stat block are inferred from the caption format-string IDs and read widths (PLAUSIBLE).

The active-buff set is pushed in a single fixed-length response message. One message rebuilds the
entire skill/state window: it fills roughly 20 player-stat text fields, then drives all 30 buff
slots.

- **Opcode:** major 4, minor 102 (S2C response). The handler rebuilds the skill/state window and
  the buff bar. (An older analyst label of this handler as a "quest data update" is stale and was
  discarded.)
- **Payload size:** fixed **476 bytes (0x1DC)**, read in one bounded copy.
- **Layout:** a player stat block occupies roughly the first 116 bytes (level, PvP mode, primary
  stats, experience, hp/mp, an actor id-key, and similar fields formatted into the stat text
  widgets); the active-buff array follows.

**Active-buff array:** **30 records of 12 bytes each.** The record base for slot `i` is at
`payload + 116 + 12*i` (the handler walks the array in 12-byte strides for 30 iterations). 30 × 12
= 360 bytes; 116 + 360 = 476, matching the payload size.

##### Active-buff record (12 bytes, little-endian)

| Offset | Size | Type  | Field        | Notes                                                                 | Confidence |
|-------:|-----:|-------|--------------|------------------------------------------------------------------------|------------|
| +0     | 2    | u16LE | buff_id      | Buff/state catalogue id; `0` marks an empty slot (skipped). `buff_id` ≤ 80 → buff cell (23 px); > 80 → state cell (25 px). Keys into `buff_icon_position.xdb`. | CODE-CONFIRMED (role) |
| +2     | 2    | u16LE | (reserved)   | Not consumed by the slot setter; possibly high bits of the id or padding | PLAUSIBLE |
| +4     | 4    | u32LE | duration     | Remaining-time candidate; stored into the live per-slot duration array. Unit (ms vs s) not determined | PLAUSIBLE |
| +8     | 2    | u16LE | stack_level  | Stack count or buff level; stored into the live per-slot stack array     | PLAUSIBLE |
| +10    | 1    | u8    | flag         | Type/category flag (buff vs debuff vs neutral)                          | PLAUSIBLE |
| +11    | 1    | u8    | (reserved)   | Not read                                                               | PLAUSIBLE |

**Render contract for implementors:**
1. On each 4/102 message, clear and hide all 30 buff slots.
2. For each of the 30 records, if `buff_id == 0`, leave the slot hidden.
3. Otherwise look up `buff_id` in `buff_icon_position.xdb` (§1.3) to obtain `(atlas_x, atlas_y)`,
   choose the cell size by class (23 × 23 for `buff_id` ≤ 80, else 25 × 25), and blit that cell from
   `data/ui/skillicon/stateicon.dds`.
4. Position buff-class icons (≤ 80) with the flowing counter and state-class icons (> 80) at their
   fixed per-slot coordinates.
5. Show the stack count from `stack_level`. Treat `duration` as a candidate countdown source only —
   verify it against a real capture before drawing any on-icon timer (no countdown/sweep render was
   located in the static analysis).

**Variant aura strip (CODE-CONFIRMED that it exists; role PLAUSIBLE):** A separate, smaller 3-slot
readout reads buff ids in the 1000–1002 and 1010–1012 ranges from the **same** `buff_icon_position`
resolver, with 21 × 21 cells at fixed positions. It is distinct from the 30-slot main bar (whose ids
are small, ≤ ~250) and is likely a permanent status/aura indicator (e.g. a PvP/peace or mount-state
strip). The exact HUD element it belongs to is not confirmed.

**Known unknowns (§1.6):**
- Duration rendering: no countdown sweep, timer text, or alpha-ramp draw was located. The `duration`
  field is the strongest remaining-time candidate but its unit (ms vs s) and whether it is rendered
  at all are unconfirmed.
- Record-base precision: the `payload + 116 + 12*i` base should be confirmed against a real 4/102
  capture (verify that the first buff id sits at `payload + 116` and that `payload[112..115]` belong
  to the stat block, not the buff array).
- The `buff_id` ≤ 80 / > 80 boundary as a semantic partition (vs an array bound) is unconfirmed.
- `stack_level` could instead be an icon-variant selector or a magnitude; capture-verify.

---

## Section 2 — `mobinfo.mi` — Monster Info Table (DEAD in build `263bd994` — present on disk, not read)

**sample_verified: true** (on-disk header and stride); **DEAD in this build** (no loader / no path
literal / not in the boot data-table corpus / not compiled in — confirmed not read, see "Runtime
status" below); **hypothesis** (field semantics — moot, no consumer to confirm them)

> **Runtime status (CONFIRMED not read, CYCLE 7, build `263bd994`):** the file
> `data/ui/mobinfo.mi` is **present on disk** in the 43,347-entry VFS with its documented container
> shape, but the client has **no code that opens it**. Exhaustive static search (four ways — the
> string index, a case-insensitive regex over the string store, a raw ASCII byte scan, and a
> UTF-16LE wide byte scan) returns **zero** hits for any `mobinfo` path or name; the filename is
> **not** in the boot data-table corpus's filename pointer table; and the table is **not** compiled
> into the binary as a static array (byte scans for its on-disk record signature return zero). This
> **upgrades** the prior "appears not to read it via a path" verdict to a hard **"confirmed not
> read."** "Present" ≠ "read". The mob data the client **does** read comes from
> `data/script/mobs.scr` (loaded at boot) plus `msg.xdb` (mob name / portrait strings, §6) — **not**
> from `mobinfo.mi`. The on-disk shape documented below stands as a sample-true record of the file,
> but nothing in this build consumes it.

- **Extension:** `.mi`
- **Found in:** `.pak` archive; logical path: `data/ui/mobinfo.mi`
- **Magic / signature:** None observed.
- **Endianness:** Little-endian.
- **Version field:** None observed.

**Role:** UI mob information table. For each mob class in the game database, stores references to
portrait icon resources and to string-table entries used to render the mob's name in the UI.

### File layout

The file begins with a 4-byte header count, followed immediately by that many 28-byte records.
Total file size = 4 + (`count` × 28).

#### Header (4 bytes)

| Offset | Size | Type  | Field | Notes                              | Confidence |
|-------:|-----:|-------|-------|------------------------------------|------------|
| 0      | 4    | u32LE | count | Number of records that follow      | HIGH       |

#### Per-record layout (28 bytes — 7 × u32LE)

| Offset | Size | Type  | Field          | Notes                                                      | Confidence |
|-------:|-----:|-------|----------------|------------------------------------------------------------|------------|
| 0      | 4    | u32LE | mob_class_id   | Mob class type identifier; range 101 – 121 in known sample | HIGH       |
| 4      | 4    | u32LE | name_str_id    | String-table reference for the mob's primary display name; 0xFFFFFFFF = none | PARTIAL |
| 8      | 4    | u32LE | alt_name_str_id | String-table reference for an alternate name (e.g. longer title); 0xFFFFFFFF = none | PARTIAL |
| 12     | 4    | u32LE | icon_index     | UI sprite index for this mob's icon; range 55 – 173 observed | HIGH     |
| 16     | 4    | u32LE | portrait_res_1 | Resource ID for the primary portrait image; 0xFFFFFFFF = none | PARTIAL |
| 20     | 4    | u32LE | portrait_res_2 | Resource ID for the hover/alternate portrait frame; 0xFFFFFFFF = none | PARTIAL |
| 24     | 4    | u32/i32 LE | field6 (was `portrait_res_3`) | **Role MOOT — file is DEAD (no read-site).** 4-byte LE; `0xFFFFFFFF` = -1 = "none / not present" sentinel; small populated values (e.g. 99, 103) read as an optional small id/index (HYPOTHESIS, unconfirmable without a reader). The `portrait_res_3` label is withdrawn. See note below. | DEAD (no consumer) |

**Sentinel:** 0xFFFFFFFF indicates "not present" for the optional reference fields
(`name_str_id`, `alt_name_str_id`, `portrait_res_1`, `portrait_res_2`). It is also one of the
values seen in `field6`, but `field6`'s overall role is unresolved (next note).

> **RESOLVED (CYCLE 7) — `field6` (offset +24, was `portrait_res_3`):** the field's role is **MOOT
> in this build** because there is **no consumer read-site** to pin it — `mobinfo.mi` is DEAD (see
> the Runtime status note above: no loader, no path literal, not in the boot data-table corpus
> pointer table, not compiled in). On the on-disk shape: the field is **4 bytes, u32/i32 LE**;
> `0xFFFFFFFF` = -1 is the "none / not present" sentinel (the re-verification sample shows record 0 =
> 103, record 1 = 99, record 2 = 0xFFFFFFFF). The small populated values (99, 103) read as an
> **optional small id/index** (HYPOTHESIS, unconfirmable without a reader). The earlier
> `portrait_res_3` label is **withdrawn**: the small two/three-digit values are inconsistent with the
> large ~5 080 000-range adjacent resource IDs that `portrait_res_1` / `portrait_res_2` hold in the
> same records, so this field is **not** a third portrait resource ID. With zero read-sites there is
> no consumer behaviour to disambiguate "small index" from "small id" from "small category"; the
> distinction is unresolvable from this binary.

### Field notes

**String-table references (`name_str_id`, `alt_name_str_id`):**
- Observed `name_str_id` values cluster in the range 0x4E03 – 0x4E45, suggesting a dedicated
  string-table sector at 0x4E00. The sector or table these IDs index is not confirmed in the binary.
- Observed `alt_name_str_id` values fall in the range 20000 – 20037. The dual reference may
  distinguish a short name from a long/title name; this is inferred, not confirmed.
- No CP949 text is stored directly in this file; names are resolved at runtime via the string table.
- String IDs in this file resolve against `data/script/msg.xdb` (see §6 of this document).

**Portrait resource IDs (`portrait_res_1` / `portrait_res_2`):**
- Large 32-bit values. A pattern of the form `(group × 1_000_000) + index` is consistent with the
  observed values (e.g. values in the 5,080,000 range with adjacent IDs differing by 1 – 3).
  This encoding formula is unconfirmed.
- In the re-verification sample, `portrait_res_1` and `portrait_res_2` are **adjacent IDs**
  (`portrait_res_2 = portrait_res_1 + 1`), consistent with sequential resource allocation per mob
  (e.g. `5 079 876` / `5 079 877`; `5 013 620` / `5 013 621`; `5 099 990` / `5 099 991`).
- `field6` (offset +24) does **not** follow this pattern (small values 99 / 103) — see the
  RESOLVED note above; it is not a third portrait resource (label withdrawn). Its role is moot in
  any case, as nothing reads this file.

**Size verification (SAMPLE-VERIFIED, build `263bd994`):** the single VFS instance at
`data/ui/mobinfo.mi` is **592 bytes**: a 4-byte header `count = 21` (`0x15`) followed by 21 × 28-byte
records (`4 + 21 × 28 = 592` exactly). The container shape — present-on-disk, 21 records, 28-byte
stride of 7 × u32 — is sample-verified. (The runtime **confirmed does not read** this file in build
`263bd994` — no loader, no path literal, not in the boot data-table corpus, not compiled in; see the
Runtime status note at the top of §2. The on-disk shape stands regardless.)

### Known unknowns

- **The file is DEAD in this build (CONFIRMED not read).** No loader routine, no path/name string
  literal (exhaustive static search — string index, regex, raw ASCII byte scan, UTF-16LE wide scan,
  all zero), not in the boot data-table corpus's filename pointer table, and not compiled in as a
  static array. The container shape (count + 28-byte records) is **sample-verified**; the runtime
  consumption path is **absent** — nothing opens it. This is no longer an open question.
- The exact string-table structure that `name_str_id` / `alt_name_str_id` index is not documented
  here; see §6 of this document (`msg.xdb`) for the resolved record format.
- **`field6` (offset +24) role is MOOT** — the file has no read-site, so there is no consumer to
  pin its meaning. On disk it is a 4-byte u32/i32 LE with `0xFFFFFFFF` = -1 = none; the small values
  99 / 103 read as an optional small id/index (HYPOTHESIS only). The `portrait_res_3` label is
  withdrawn. See the RESOLVED note in the record layout above.
- The portrait resource ID encoding (group × 1e6 + index vs another scheme) for
  `portrait_res_1` / `portrait_res_2` is unconfirmed.
- Whether files with `mob_class_id` ranges outside 101 – 121 exist is unknown (single sample).

---

## Section 3 — `.tol` — Terrain Tile Obstacle / Collision Layer

**sample_verified: true** (header layout and tile-grid stride); **hypothesis** (world-origin semantics)

- **Extension:** `.tol`
- **Found in:** `.pak` archive; logical path pattern: `data/map<NNN>/<NNN>.tol`
- **Magic / signature:** None. The file begins directly with the 16-byte header.
- **Endianness:** Little-endian (header fields).
- **Version field:** None observed.

**Role:** Per-map tile walkability bitmap. Each byte encodes the obstacle state of one world tile.
Used for pathfinding and movement validation. Grid dimensions are always powers of two.

### File layout

A 16-byte header is immediately followed by a flat `width_tiles × height_tiles` byte array.
Total file size = 16 + (`width_tiles` × `height_tiles`).

#### Header (16 bytes — 4 × u32LE)

| Offset | Size | Type  | Field          | Notes                                                         | Confidence |
|-------:|-----:|-------|----------------|---------------------------------------------------------------|------------|
| 0      | 4    | u32LE | world_origin_x | World-space X origin of the grid; see world-origin note below | PARTIAL    |
| 4      | 4    | u32LE | world_origin_y | World-space Y origin of the grid; see world-origin note below | PARTIAL    |
| 8      | 4    | u32LE | width_tiles    | Grid width in tiles; must be a power of 2                     | HIGH       |
| 12     | 4    | u32LE | height_tiles   | Grid height in tiles; must be a power of 2                    | HIGH       |

**World-origin note (PARTIAL confidence):** The `world_origin_x` value observed in samples divides
exactly by 256. For example, a value of 23552 / 256 = 92, and a value of 8192 / 256 = 32, both
yield integer tile-column offsets. This suggests `world_origin_x = tile_column_offset × 256`,
where 256 is a per-world-unit scale factor (the number of sub-tile units in one tile). The
corresponding formula for absolute world tile column of grid cell (i) is:
`tile_col = world_origin_x / 256 + i`. The constant 256 is inferred from data divisibility; the
game engine's internal tile-size constant is not confirmed.

#### Tile data (immediately after header)

| Offset | Size                     | Type      | Field     | Notes                              | Confidence |
|-------:|--------------------------:|-----------|-----------|------------------------------------|------------|
| 16     | `width_tiles × height_tiles` | u8[W×H] | tile_grid | Row-major; index = `y × W + x`    | HIGH (row-major assumed; not loader-confirmed) |

Each byte encodes one tile:
- `0` — walkable (no obstacle)
- `1` — blocked (obstacle present)

Only values 0 and 1 have been observed. Any other value should be treated as blocked by a
defensive parser.

**Size verification (SAMPLE-VERIFIED, build `263bd994`):** three `.tol` files live in the VFS
(`data/map009/009.tol`, `data/map013/013.tol`, `data/map100/100.tol`).
- A 2048 × 2048 map: 16 + 4 194 304 = 4 194 320 bytes — confirmed against `map009` **and** `map013`
  (both exactly 4 194 320).
- A 256 × 256 map: 16 + 65 536 = 65 552 bytes — confirmed against `map100`.
- Header re-decode bears out the layout and the ×256 world-origin divisibility:
  `map009` reads `world_origin_x = 8192` (8192 / 256 = 32), `world_origin_y = 57344` (57344 / 256 =
  224), `width = height = 2048`; `map100` reads `world_origin_x = 23552` (23552 / 256 = 92),
  `world_origin_y = 55296` (55296 / 256 = 216), `width = height = 256`. Both origins are exact
  multiples of 256, consistent with the `tile_col = world_origin_x / 256 + i` model below.

### Known unknowns

- No loader routine located in the binary; the `.tol` path was not found as a string literal in the binary.
  Format is confirmed by sample arithmetic alone.
- `world_origin_x` / `world_origin_y` units: the ×256 sub-tile scale factor is inferred from
  data divisibility; the game engine's tile-size constant is not confirmed from source.
- Row-major tile ordering (`y × W + x`) is standard and consistent with the sample, but has not
  been cross-checked against the engine's pathfinding code.
- Whether `width_tiles` and `height_tiles` are strictly required to be powers of two, or whether
  this is merely an observed property of the sample set, is not confirmed.

---

## Section 4 — `descript.ion` — Texture Description / Directory File

**sample_verified: true** (record structure + sizing, build `263bd994`); **UNVERIFIED**
(field1 / field2 semantics). The single VFS instance is **29 bytes = exactly one record**, and the
record decodes end-to-end against the structure below: a `.tga` texture filename, an ASCII space
separator, two u32LE values (each ~30–32 MB, consistent with pak byte offsets but unconfirmed), a
`name_length` byte that equals the filename length, and a CRLF terminator —
`len(filename) + 1 + 4 + 4 + 1 + 2 = 29`, exact. The multi-record question (below) remains open.

- **Extension:** `.ion`
- **Found in:** `.pak` archive; logical path: `data/effect/texture/descript.ion`
- **Magic / signature:** None. The file begins directly with the first record.
- **Endianness:** Little-endian (binary fields).
- **Version field:** None observed.

**Role:** Associates a texture filename with binary metadata — at minimum, the filename's byte
length and two 32-bit values whose semantics are not yet confirmed (candidates: pak data offset
and end-offset/size, or CRC32 and size). The format is a text/binary hybrid using Windows line
endings.

### Record layout (variable length per record)

Records are delimited by CR LF (0x0D 0x0A). Each record has the following fixed structure:

```
<filename> SP <u32LE:field1> <u32LE:field2> <u8:name_length> CRLF
```

| Component      | Type       | Size        | Notes                                           | Confidence |
|----------------|------------|-------------|--------------------------------------------------|------------|
| filename       | ASCII text | variable    | Texture filename including extension (e.g. `.tga`); no null terminator within the record | HIGH |
| separator      | literal    | 1 byte      | ASCII space (0x20)                               | HIGH       |
| field1         | u32LE      | 4 bytes     | Large 32-bit value; semantics unconfirmed — candidate: pak offset | MEDIUM |
| field2         | u32LE      | 4 bytes     | Large 32-bit value; semantics unconfirmed — candidate: pak end-offset or file size | MEDIUM |
| name_length    | u8         | 1 byte      | Byte length of `filename`; confirmed equal to `len(filename)` in the single known sample | HIGH |
| line terminator | literal   | 2 bytes     | CR LF (0x0D 0x0A)                               | HIGH       |

**Total record size:** `len(filename) + 1 + 4 + 4 + 1 + 2 = len(filename) + 12` bytes.

**Parsing algorithm (single pass):**
1. Read bytes until SP (0x20) — this is the filename.
2. Read the next 4 bytes as u32LE → `field1`.
3. Read the next 4 bytes as u32LE → `field2`.
4. Read the next 1 byte as u8 → `name_length`.
5. Read the next 2 bytes and verify they are CR LF.
6. Assert `name_length == len(filename)` as a consistency check.
7. Repeat from step 1 until end of file.

**Text encoding:** ASCII filenames confirmed in the single available sample. Whether filenames can
contain CP949 characters is unknown; treat as ASCII unless evidence to the contrary emerges.

**Field1 / field2 interpretations (UNVERIFIED):**
- Candidate A: pak archive byte offset (field1 = start, field2 = end or size). The sampled record
  reads `field1 ≈ 30.4 MB` and `field2 ≈ 32.6 MB` with `field1 < field2`, which fits a
  start/end-offset pair for a late entry in a large archive — but it is not confirmed.
- Candidate B: one field is a CRC32 checksum, one is a file size. Not confirmed.
- Windows FILETIME encoding was tested and rejected (values do not map to plausible dates).

### Known unknowns

- Only a single-record sample is available. Whether files with multiple records exist in production
  and whether they all follow the same record structure is unknown.
- `field1` and `field2` semantics are unconfirmed.
- Whether the filename field can contain non-ASCII (CP949) characters is unknown.
- No loader routine located in the binary; the path was not found as a string literal in the binary.

---

## Section 5 — `discript.sc` — UI Context-Menu Label Table

**sample_verified: true**

- **Extension:** `.sc`
- **Found in:** `.pak` archive; logical path: `data/script/discript.sc`
- **Magic / signature:** None. The file begins directly with the first record.
- **Endianness:** Little-endian (u32 fields).
- **Version field:** None observed.

**Role (CONFIRMED — two-witness: loader + black-box):** Right-click context-menu label table for
the UI. Maps integer IDs to the CP949 menu-item display name shown in pop-up/context menus and (for
hotkey-capable windows) to a keyboard shortcut string. Covers party commands, currency labels, UI
window names, and guild/faction actions. The context-menu label loader walks this file as a flat
array of 68-byte records.

> **Corrected (CONFIRMED): these are UI context-menu labels, not "district/zone" names.** An
> earlier reading treated `discript.sc` as a district/region table; that reading is refuted. The
> consumer is the context-menu label loader, and the records carry menu-item captions plus optional
> hotkey strings, with no world-geometry or zone-bounds fields. (Zone bounding boxes and zone names
> live in `mapsetting.scr`, §7.1 — a distinct file with a 84-byte stride.)

### File layout

Flat array of fixed 68-byte records. No header. Record count = `file_size / 68`. The file size
must be an exact multiple of 68; any remainder is an error.

**Size verification (SAMPLE-VERIFIED, build `263bd994`):** the VFS instance at
`data/script/discript.sc` is **2 244 bytes = exactly 33 records** (2 244 / 68 = 33.0). The first two
records re-confirm the layout and the corrected role: both have `category = 3` (party command), the
30-byte `display_name` decodes cleanly as CP949 party-action labels ("Join Party" / "Leave Party"),
and the `keyboard_shortcut` field is the no-shortcut placeholder (`'0'` then NUL bytes) for these
non-hotkey records. The party-action content is decisive evidence that these are UI context-menu
labels, not district/zone names.

#### Per-record layout (68 bytes = 0x44)

| Offset | Size | Type     | Field        | Notes                                                      | Confidence |
|-------:|-----:|----------|--------------|------------------------------------------------------------|------------|
| 0      | 4    | u32LE    | descriptor_id | Unique integer identifier; see ID ranges below            | HIGH       |
| 4      | 4    | u32LE    | category     | Category code; see enumeration below                       | HIGH       |
| 8      | 30   | char[30] | display_name | CP949-encoded Korean display string, null-padded to 30 bytes | HIGH     |
| 38     | 3    | char[3]  | keyboard_shortcut | ASCII keyboard shortcut notation, null-padded to 3 bytes; see encoding below | HIGH |
| 41     | 27   | u8[27]   | reserved     | All zero in the known sample; purpose unknown             | LOW        |

**Total:** 4 + 4 + 30 + 3 + 27 = 68 bytes.

### Enumerations

#### `category` values

| Value | Meaning                                    | Confidence |
|------:|--------------------------------------------|------------|
| 3     | Party command                              | HIGH       |
| 102   | Hotkey window (has keyboard shortcut)      | HIGH       |
| 103   | Currency label                             | HIGH       |
| 105   | Faction / guild action                     | HIGH       |

Other values may exist in production builds; treat unknown category values as reserved.

#### `descriptor_id` ranges (observed)

| Range       | Apparent group         |
|------------|------------------------|
| 8 – 12     | Party commands         |
| 70 – 72    | Currency labels        |
| 88 – 89    | Unknown                |
| 4000 – 4013 | Hotkey window names   |
| 4500 – 4508 | Guild/faction actions |

#### `keyboard_shortcut` encoding

- For records with `category = 102` (hotkey windows): three ASCII bytes in the form `(X)`, where
  `X` is the letter key. Examples: `(C)` = character info, `(P)` = party, `(S)` = skills,
  `(I)` = inventory, `(M)` = map.
- For all other categories: byte at offset 38 = 0x30 (`'0'`), bytes at offsets 39 – 40 = 0x00.
  The `'0'` byte is a placeholder indicating no keyboard shortcut.

### Text encoding

The `display_name` field is encoded in **CP949 (code page 949 / EUC-KR)**. The field is
null-padded to exactly 30 bytes; a string shorter than 30 bytes is followed by one or more
0x00 bytes. Korean text occupies 2 bytes per character in CP949; a 30-byte field holds at most
15 Korean characters.

Parsers must treat the field as a byte buffer and decode it to Unicode (UTF-16 or UTF-8) using
CP949 before display or comparison. Never treat this field as ASCII.

### Known unknowns

- The 27-byte `reserved` region at offset 41 is entirely zero in the known sample; its purpose,
  if any, is not known.
- Whether additional `category` values beyond 3, 102, 103, 105 exist in production builds.
- Whether additional `descriptor_id` ranges exist beyond those listed above.

---

## Section 6 — `msg.xdb` — UI Message Catalogue

> **Verification status: CODE-CONFIRMED** (parser read-sequence, record stride, lookup model,
> and fill-byte convention confirmed from the loader routine). **SAMPLE-VERIFIED** (record count,
> fill byte, representative string content, and ID range groupings confirmed from direct
> harness observation of the real VFS at `data/script/msg.xdb`). All claims below are graded
> individually; previously SAMPLE-UNVERIFIED items are now resolved.

- **Extension:** `.xdb` (shares extension with §1 variants; distinguished by its VFS path)
- **Found in:** `.pak` archive; logical path: `data/script/msg.xdb`
- **Magic / signature:** None. No file-level header of any kind.
- **Endianness:** Little-endian throughout.
- **Version field:** None.
- **File size (SAMPLE-VERIFIED):** 1,364,304 bytes.

**Role:** Startup binary string database that maps u32 integer IDs to CP949-encoded UI caption
strings. All visible UI text (error messages, state names, quest text, menu labels, and similar
captions) is fetched from this catalogue at runtime by numeric ID. The file is loaded once at
engine startup, before any map or asset is loaded. It is a startup asset, not a per-area asset.

### File layout

The file is a flat, headerless array of fixed-size records. There is no stored record count; the
count is derived at load time from the file size:

```
record_count = file_size / 516       (integer division; file must be an exact multiple of 516)
```

Any remainder implies a malformed file.

**Record count (SAMPLE-VERIFIED):** 1,364,304 / 516 = **2,644 records exactly**.

### Record layout (516 bytes = 0x204)

| Offset | Size | Type      | Field | Notes                                                              | Confidence |
|-------:|-----:|-----------|-------|--------------------------------------------------------------------|------------|
| 0x000  | 4    | u32LE     | id    | Unique message identifier; used as the runtime lookup key          | CODE-CONFIRMED |
| 0x004  | 512  | u8[512]   | text  | CP949-encoded string, NUL-terminated within the buffer; bytes after the NUL through +515 are filled with **0xEE** (not 0x00) | CODE-CONFIRMED + SAMPLE-VERIFIED |

**Total record size: 4 + 512 = 516 bytes.**

> **Fill-byte clarification (SAMPLE-VERIFIED):** The padding bytes after the NUL terminator are
> filled with **0xEE**, not 0x00. This is a deliberate non-null sentinel that distinguishes padding
> from short strings. A parser reading the `text` field must stop at the **first 0x00 byte**, not
> the first 0xEE byte. A record whose entire 516 bytes consist of 0xEE (all padding, no NUL, no
> non-zero id) is an empty/reserved slot and should be silently skipped.

### Filled and empty record counts (SAMPLE-VERIFIED)

| Category | Count |
|---|---|
| Total record slots | 2,644 |
| Filled slots (non-zero id, text present) | 2,633 |
| Empty slots (entirely 0xEE — reserved/unused) | 11 |

### Text encoding

All string content is encoded in **CP949** (Windows code page 949, the Korean EUC-KR superset).
The `text` field is NUL-terminated within its 512-byte buffer. The maximum usable string length
before the NUL is 511 bytes (511 CP949 code units; multi-byte Korean characters each occupy 2
bytes, so the maximum is 255 Korean characters per message). Bytes after the NUL are 0xEE fill
and must be ignored.

Parsers must register `CodePagesEncodingProvider` and decode the buffer with
`Encoding.GetEncoding(949)` before passing strings to any display layer.

### Format-string conventions (SAMPLE-VERIFIED)

Message strings may contain the following substitution conventions. A parser should not strip
or transform these — pass the raw CP949 string to the display layer, which performs substitution:

| Convention | Meaning | Example message |
|---|---|---|
| `%s` | String argument (player name, item name) | `%s님이 입장하셨습니다` |
| `%d` | Integer argument (count, level, duration) | `%d시간`, `%d분`, `%d초` |
| `%I64d` | 64-bit integer (large gold amounts) | `%I64d 금` |
| `%1%`, `%2%`, `%3%` | Positional substitution (year, month, day, etc.) | `만료 기간 : %1%년 %2%월 %3%일 %4%:%5%` |
| Embedded CRLF | Multi-line message body | Help text and long dialog strings contain `\r\n` within the string field |

The positional `%N%` convention is a custom formatter, not standard printf. The engine's
mechanism for this substitution is not documented here; the exact implementation is UNKNOWN
(see Open questions §6.8).

### Load and lookup model (CODE-CONFIRMED)

The client performs a single bulk read of the entire file into a heap buffer, then iterates all
records and inserts each one into a global **red-black tree** keyed on the u32 `id` field. The
tree is accessed at runtime via a lookup function that accepts an integer `id` and returns a
pointer to the 516-byte record; the string payload begins at byte offset 4 within that record.

The tree is keyed on `id` only; there is no secondary index by string content. Lookups are by
numeric ID exclusively.

**Load sequence:**
1. Open `data/script/msg.xdb` read-only.
2. Query file size; compute `record_count = file_size / 516`.
3. Allocate a heap buffer of `516 × record_count` bytes.
4. Read all records in a single bulk read into the buffer.
5. For each record: check whether the record is an empty slot (all 0xEE — skip it); otherwise
   read `id` (u32LE at record base), insert `(id → record_ptr)` into the global red-black tree.
6. Close the file. The buffer remains live for the process lifetime.

**Lookup contract for implementors:**
- Do NOT assume `id == slot_index + 1`. The `id` stored at record +0x000 is the authoritative
  identifier; slots are allocated in groups but are NOT sequentially numbered.
- For an empty slot check: if all 516 bytes are 0xEE, skip. In practice, checking that
  `id != 0` and that the byte at +0x004 is not 0xEE is sufficient.
- The runtime dict is keyed by `id`; build it as `Dictionary<uint, string>` using the `id` field
  as key and the CP949-decoded text as value.

### ID range groupings (SAMPLE-VERIFIED from representative slot sampling)

The `id` values are non-contiguous and non-sequential. They are allocated in semantic groups with
large numeric gaps between groups. The table below lists the confirmed groups from direct record
observation:

| Slot range | ID range | Representative CP949 texts | Apparent group |
|---|---|---|---|
| 0–3 | 1–4 | `%s금`, `%d은`, `%d동`, `엽전` | Currency format strings (gold/silver/copper/yang) |
| 4–10 | 101–107 | `확인`, `취소`, `닫기`, `수령`, `검색`, `이전`, `다음` | Common UI button labels (OK, Cancel, Close, Receive, Search, Prev, Next) |
| 11–18 | 201–208 | `%d시간`, `%d분`, `%d초`, `%d시`, `오전`, `오후`, `%d일`, `남은 시간: %s` | Time display format strings (hour/minute/second/AM/PM/day/remaining) |
| 19–22 | 501–504 | `기간 연장`, `폐기`, `노점`, `정리` | Item shop/stall actions (Extend, Destroy, Stall, Organize) |
| 23–34 | 901–912 | `을`, `를`, `은`, `는`, `이`, `가`, `에`, `굴림체`, `굴림`, `바탕체`, `궁서체`, `korean` | Korean postposition suffixes and font name strings |
| 35–63 | ~913–940 | Map/camera/combat/trade system messages | General in-game system messages |
| 64–157 | ~940–1057 | Warning dialogs, trade, item time-limit messages | In-game warning and transaction messages |
| 158–~300 | ~1058–1300 | NPC dialog, party, alliance, server text | Multiplayer and social messages |
| ~300–400 | ~1300–1400 | Server names, legal/privacy text | Server list and terms-of-service text |
| ~400–600 | ~1400–1600 | Server status, stall actions, help text | Server/stall/UI system |
| ~600–700 | ~1600–1700 | Combat/trade/system continuation | Mixed in-game messages |
| ~700–800 | ~1700–1800 | Data server errors, item enhancement, help text | Error messages and help overlay |
| ~800–900 | ~1800–1900 | Item enchant help text (multi-line), quest data | Item help and quest strings |
| ~900–1000 | ~1900–2000 | Mob kill notifications, level-up, stat strings | Combat and progression feedback |
| ~1000–1100 | ~2000–2100 | Item stat labels (attack, gear attribute strings) | Item attribute labels |
| ~1100–1200 | ~2100–2200 | Money format (`%I64d 금`), elder/title strings | Economy and rank titles |
| ~1200–1400 | ~2200–2400 | Class and rank names, NPC names | Class taxonomy and NPC names |
| ~1400–1500 | ~2400–2500 | Required stat labels, buff window captions | Stat UI labels |
| ~1500–1700 | ~2500–2700 | Alignment/faction text, skill book names | Faction and skill content |
| ~1700–1800 | ~2700–2800 | Skill book names, weapon names | Item names |
| ~1800–1900 | ~2800–2900 | Equipment equip messages, character death messages | Action feedback |
| ~1900–2000 | ~2900–3000 | Death dialogs, dungeon restriction messages | Death/respawn/area |
| ~2000–2100 | ~3000–3100 | Restriction messages, production item dialogs | Area restriction and crafting |
| ~2100–2200 | ~3100–3200 | Mode/production text | Status messages |
| ~2200–2300 | ~3200–3300 | Server error strings (energy-system errors) | System errors |
| ~2300–2400 | ~3300–3400 | Charm slot/soul orb messages | Equipment system |
| ~2400–2500 | ~3400–3500 | Lottery messages, buff selection | Event and buff system |
| ~2500–2566 | ~3500–3566 | Buff format strings (`%d분간 %d 상승`), NPC error messages | Buff and NPC system |
| ~2567–2580 | ~3567–3580 | Death by monster/player, war announcements | Combat and war system |
| ~2580–2609 | ~3580–3609 | Job change item, map move, logout, CAPTCHA/question system | Class change, navigation, bot-prevention |
| ~2610–2625 | ~3610–3625 | Auto-war, peace declaration, war mode | PvP/peace system |
| ~2626–2643 | ~3626–3643 | Item time extension, appearance change cosmetics | Item system extension |

> **ID vs. slot index (critical):** the `id` stored in each record is the authoritative
> identifier. A record at slot 0 has `id = 1`; a record at slot 4 has `id = 101`; there is
> no formula relating slot index to `id`. Always read the `id` field from the record itself.
> The estimated maximum `id` is approximately **3,643** (derived from the last occupied slot).

### Confirmed specific message IDs (combined CODE-CONFIRMED + SAMPLE-VERIFIED)

The following IDs are pinned by both call-site analysis and direct sample observation:

| msg_id | CP949 text | Translation | Confidence |
|---|---|---|---|
| 1 | `%s금` | Gold: %s | SAMPLE-VERIFIED |
| 2 | `%d은` | Silver: %d | SAMPLE-VERIFIED |
| 3 | `%d동` | Copper: %d | SAMPLE-VERIFIED |
| 4 | `엽전` | Yang (currency unit) | SAMPLE-VERIFIED |
| 101 | `확인` | OK / Confirm | SAMPLE-VERIFIED |
| 102 | `취소` | Cancel | SAMPLE-VERIFIED |
| 103 | `닫기` | Close | SAMPLE-VERIFIED |
| 104 | `수령` | Receive | SAMPLE-VERIFIED |
| 105 | `검색` | Search | SAMPLE-VERIFIED |
| 106 | `이전` | Previous | SAMPLE-VERIFIED |
| 107 | `다음` | Next | SAMPLE-VERIFIED |
| 201 | `%d시간` | %d hour(s) | SAMPLE-VERIFIED |
| 202 | `%d분` | %d minute(s) | SAMPLE-VERIFIED |
| 203 | `%d초` | %d second(s) | SAMPLE-VERIFIED |
| 204 | `%d시` | %d o'clock | SAMPLE-VERIFIED |
| 205 | `오전` | AM | SAMPLE-VERIFIED |
| 206 | `오후` | PM | SAMPLE-VERIFIED |
| 207 | `%d일` | %d day(s) | SAMPLE-VERIFIED |
| 208 | `남은 시간: %s` | Remaining time: %s | SAMPLE-VERIFIED |
| 501 | `기간 연장` | Extend (duration) | SAMPLE-VERIFIED |
| 502 | `폐기` | Destroy | SAMPLE-VERIFIED |
| 503 | `노점` | Stall | SAMPLE-VERIFIED |
| 504 | `정리` | Organize | SAMPLE-VERIFIED |
| 901 | `을` | Korean postposition (object marker, after consonant) | SAMPLE-VERIFIED |
| 902 | `를` | Korean postposition (object marker, after vowel) | SAMPLE-VERIFIED |
| 903 | `은` | Korean postposition (topic marker, after consonant) | SAMPLE-VERIFIED |
| 904 | `는` | Korean postposition (topic marker, after vowel) | SAMPLE-VERIFIED |
| 905 | `이` | Korean postposition (subject marker, after consonant) | SAMPLE-VERIFIED |
| 906 | `가` | Korean postposition (subject marker, after vowel) | SAMPLE-VERIFIED |
| 907 | `에` | Korean postposition (location/direction) | SAMPLE-VERIFIED |
| 908 | `굴림체` | Font name: Gulim bold | SAMPLE-VERIFIED |
| 909 | `굴림` | Font name: Gulim | SAMPLE-VERIFIED |
| 910 | `바탕체` | Font name: Batang | SAMPLE-VERIFIED |
| 911 | `궁서체` | Font name: Gungseo | SAMPLE-VERIFIED |
| 912 | `korean` | Font/locale identifier | SAMPLE-VERIFIED |
| 200–212 | (error messages) | Character create/rename error messages | CODE-CONFIRMED |
| 4025–4028 | (login errors) | Login error toast messages | CODE-CONFIRMED |
| 9001 + N | (state names) | Scene/state name strings | CODE-CONFIRMED |

### Cross-reference

This catalogue is consumed by the UI system documented in `Docs/RE/specs/ui_system.md`. Mob
name and alternate-name string IDs stored in `mobinfo.mi` (§2 of this document) resolve against
this same catalogue. UI window button labels, currency display, time formatting, font selection,
and Korean postposition injection all draw from this file.

### Open questions for §6

1. **Positional substitution mechanism (`%1%`/`%2%`/`%3%`):** the engine's implementation for
   positional argument insertion is not documented. Whether it uses a custom formatter or an
   sprintf-style call with numbered arguments is unknown.
2. **Maximum `id` value:** the highest `id` in the file was not systematically extracted across
   all 2,644 slots. The estimated maximum is approximately 3,643 based on the last occupied slot
   grouping. The exact max is required for bounds-checking a lookup array if an array is preferred
   over a dictionary.
3. **Empty slot detection edge case:** the check "all 516 bytes are 0xEE" may be overly strict if
   any padding variant uses 0x00 after the NUL. The confirmed fill byte is 0xEE; whether any
   partial-0xEE/partial-0x00 padding exists in the 11 empty slots is not exhaustively confirmed.
4. **ID stability across patches:** whether the same `id` values map to the same strings in
   earlier or later client versions is unknown. The mapping documented here is from the single
   analysed VFS version.

---

## Section 7 — Map zone tables (`mapsetting.scr`, `regiontableNNN.bin`)

> **Verification status: SAMPLE-VERIFIED.** Both formats were decoded by direct observation of the
> real VFS (no decompiler involved): the strides divide their file sizes exactly with zero
> remainder, and the CP949 name fields and numeric ranges decode coherently across multiple
> records and multiple area instances. No loader routine was traced, so a few field *semantics*
> (the bounding-box ordering, the fog-density float, the sub-zone coordinate pair) are graded
> PLAUSIBLE even though the layout itself is SAMPLE-VERIFIED.

These two files describe playable zones for the world-map / minimap system. `mapsetting.scr` is a
single global table of zone bounding boxes and fog settings; `regiontableNNN.bin` is a per-area
table of sub-zone landmark labels with world coordinates. Both are flat arrays of fixed records.

Related minimap art (not part of these tables) lives under `data/ui/`: a single world-map panel
texture `data/ui/map/map1.dds` (512 × 512 DXT2) shared across all areas, a PvP overview
`data/ui/broodwarmap.dds` (1024 × 1024 DXT2), a compass icon `data/ui/direction.dds` (16 × 16 RGBA),
and a player-marker `data/ui/map_userpoint.tga` (64 × 64). No per-area baked minimap bitmaps exist
in the VFS; the in-game minimap is assumed to be generated at runtime from terrain data rather than
loaded from images.

### 7.1 `mapsetting.scr` — Zone bounding-box and fog table

**sample_verified: true**

- **Extension:** `.scr`
- **Found in:** `.pak` archive; logical path: `data/script/mapsetting.scr`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.

**Role:** One record per playable zone. Stores the zone's integer id, its CP949 display name, an
axis-aligned world-space bounding box (used to test which zone a world position falls in and to map
world coordinates onto the shared world-map panel), and a per-zone fog density.

#### File layout

Flat array of fixed 84-byte records. No header. Record count = `file_size / 84`. The file size must
be an exact multiple of 84.

**Size verification:** a known sample of 4,368 bytes yields exactly **52 records** with no
remainder. Zone ids run roughly sequentially (1, 2, 3, …) with gaps and a few high ids (100, 203,
204, 205, 208, 300), so the array index is not the zone id — always read the `zone_id` field.

#### Per-record layout (84 bytes = 0x54)

| Offset | Size | Type      | Field        | Notes                                                                 | Confidence |
|-------:|-----:|-----------|--------------|------------------------------------------------------------------------|------------|
| 0x00   | 4    | i32LE     | zone_id      | Zone identifier and lookup key; non-contiguous (gaps), not the array index | SAMPLE-VERIFIED |
| 0x04   | 36   | char[36]  | zone_name    | CP949-encoded zone name, NUL-terminated within the field               | SAMPLE-VERIFIED |
| 0x28   | 4    | i32LE     | world_min_x  | World-space X lower bound of the zone's bounding box                   | PLAUSIBLE  |
| 0x2C   | 4    | i32LE     | world_min_z  | World-space Z lower bound                                              | PLAUSIBLE  |
| 0x30   | 4    | i32LE     | world_max_x  | World-space X upper bound                                              | PLAUSIBLE  |
| 0x34   | 4    | i32LE     | world_max_z  | World-space Z upper bound                                              | PLAUSIBLE  |
| 0x38   | 4    | i32LE     | flags_a      | Packed flags; `0x012C0001` in 50 of 52 records (two exceptions hold `0x012C0000`) | UNKNOWN |
| 0x3C   | 4    | i32LE     | flags_b      | Usually `0x00000001`; one record holds `0x00000000`                   | UNKNOWN    |
| 0x40   | 4    | f32LE     | fog_density  | Per-zone fog density; observed values 1.30 (interior), 1.50 (rare), 1.70 (outdoor) | PLAUSIBLE |
| 0x44   | 4    | i32LE     | unknown_0x44 | First record = 1, all others = 0                                      | UNKNOWN    |
| 0x48   | 4    | i32LE     | unknown_0x48 | Typically 0 or -1                                                     | UNKNOWN    |
| 0x4C   | 4    | i32LE     | unknown_0x4C | High byte constant `0x64` (= 100), low 24 bits vary (e.g. `0x64000007`, `0x64000002`, `0x64001200`); candidate: packed minimap scale + flags | UNKNOWN |
| 0x50   | 4    | i32LE     | unknown_0x50 | Always 0 in all 52 observed records                                   | UNKNOWN    |

**Total:** 4 + 36 + 16 + 8 + 4 + 16 = 84 bytes.

**Field notes:**
- The four bounding-box fields form a `(min_x, min_z, max_x, max_z)` box: the min pair precedes the
  max pair and the observed values bracket plausibly (e.g. `(-10240, -7168)` min / `(5120, 10240)`
  max for the first zone). The exact axis assignment and whether these are inclusive bounds is
  inferred from the value ranges, not from a loader. World units match the terrain coordinate scale
  documented elsewhere (cells of 1024 units).
- `fog_density` reads as a float that clusters at three physically meaningful values; indoor/cave
  zones tend to 1.30 and outdoor zones to 1.70.
- The `zone_name` field decodes cleanly as CP949 (e.g. the first three zones are `하왕관`, `염무진`,
  `사해주`). One record has an empty name. Decode the field with `Encoding.GetEncoding(949)`; never
  treat it as ASCII.

**Cross-reference:** zone names are stored directly in this file, not in `msg.xdb` (§6). The map
transfer / region UI strings (a map-move countdown, a "no location data" cancel message, a
quick-move label, and a "Region" column header) live in `msg.xdb` in the 73001–73007 and 18503 id
range; server/realm names occupy the 5001–5040 range. Those are caption strings only and do not
carry zone geometry.

**Known unknowns (§7.1):**
- The packed meaning of `flags_a` (`0x012C0001`) and the two exception records.
- Whether `unknown_0x4C`'s `0x64` high byte is a 1:100 minimap scale factor packed with flags.
- The exact inclusivity / axis convention of the bounding box (PLAUSIBLE, not loader-confirmed).
- A few apparent duplicate or drifted rows in the 52-record sample suggest version drift in the
  table; whether other client versions carry a different record count is unknown.

### 7.2 `regiontableNNN.bin` — Per-area sub-zone label table

**sample_verified: true** (stride and name field); **PLAUSIBLE** (coordinate fields)

- **Extension:** `.bin`
- **Found in:** `.pak` archive; logical path pattern: `data/map<NNN>/regiontable<NNN>.bin`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.

**Role:** One record per named sub-zone / landmark within an area. Stores a world XZ centre point
and a CP949 label, used to place sub-zone name captions on the world map.

#### File layout

Flat array of fixed 32-byte records. No header. Record count = `file_size / 32`.

**Size verification (SAMPLE-VERIFIED, build `263bd994`):** the VFS holds **60** `regiontable*.bin`
files (one per area). The area-1 instance was re-decoded this pass: 1 664 bytes = exactly
**52 records** (1 664 / 32 = 52.0). Record 1 reads `center_x = -1574.0`, `center_z = 2698.0` (both
inside the area-1 bounding box from `mapsetting.scr`) with `sub_zone_name` decoding cleanly as a
CP949 landmark name; record 0 is all-zero (the empty / second-sub-type case noted below). The
area-1/2/3 instances are each 1 664 bytes (52 records), consistent across instances.

#### Per-record layout (32 bytes = 0x20)

| Offset | Size | Type      | Field          | Notes                                                         | Confidence |
|-------:|-----:|-----------|----------------|---------------------------------------------------------------|------------|
| 0x00   | 4    | f32LE     | center_x       | World-space X of the sub-zone label                          | PLAUSIBLE  |
| 0x04   | 4    | f32LE     | center_z       | World-space Z of the sub-zone label                          | PLAUSIBLE  |
| 0x08   | 8    | u8[8]     | unknown_0x08   | Zero in all observed records                                  | UNKNOWN    |
| 0x10   | 16   | char[16]  | sub_zone_name  | CP949-encoded landmark name, NUL-terminated within the field | PLAUSIBLE  |

**Total:** 4 + 4 + 8 + 16 = 32 bytes.

**Field notes:**
- The CP949 `sub_zone_name` field decodes cleanly into Korean landmark names across multiple
  records and area files (e.g. for area 1: `폐어촌`, `구룡부`, `무암촌`; for area 2: `녹영초산곡`,
  `남소사`, `적릉`). Decode with `Encoding.GetEncoding(949)`.
- The `(center_x, center_z)` floats land in the world-unit ranges of the matching `mapsetting.scr`
  bounding box for that area, which is the basis for grading them PLAUSIBLE.
- **Two sub-types under one stride (open):** some records read as garbage floats at offset 0 while
  still carrying coherent CP949 text at offset 0x10, suggesting two record sub-types share the
  32-byte stride — one carrying a coordinate plus a name, and one whose first 8 bytes hold something
  other than a usable coordinate (an unused anchor, or a name placed at a different offset). The
  discriminator between the two sub-types has not been resolved; a defensive parser should validate
  that `center_x` / `center_z` fall within the area bounding box before trusting them as coordinates.

**Character-select special case:** `data/map000/regiontable000.bin` begins with the CP949 string
`캐릭터선택창` ("Character Select Window") padded to 1,664 bytes. Area 0 (`map000`) is the
character-select / lobby zone, not a world area, so its coordinates are dummy/zero.

#### Companion files (out of scope here, noted for completeness)

The per-area directory also contains `region<NNN>.bin` and `map<NNN>.bin` files whose layouts are
not yet decoded:
- `region<NNN>.bin` — size varies by area (32 bytes for `map000`, 4,096 / 1,680 / 1,776 bytes for
  later areas); structure undecoded. Possibly a polygonal boundary or entry-link list, distinct from
  the fixed-size `regiontableNNN.bin` label table.
- `map<NNN>.bin` — a fixed 520-byte per-area file, almost entirely zero (in `map000.bin` only the
  byte at offset 4 is non-zero, value `0x10`); structure undecoded.

**Known unknowns (§7.2):**
- The two-sub-type discriminator in `regiontableNNN.bin`.
- The eight bytes at offset 0x08 (zero in all samples).
- The layouts of the companion `region<NNN>.bin` and `map<NNN>.bin` files.

## Section 8 — `chatfilter` — ABSENT from this build

> **Verification status: CONFIRMED absent (two-witness: loader + black-box; re-confirmed on build
> `263bd994`, 2026-06-16 — a VFS scan returns zero entries matching `chatfilter`).**

There is **no `chatfilter` asset format in this client build.** The only trace of a chat-filter
feature is a single type-name string belonging to a class identifier in the binary's runtime
type metadata. There is **no VFS path, no file extension, and no loader routine** that reads any
such file: a black-box scan of the asset tree finds no matching asset, and no consumer reads one.

Accordingly, **this document deliberately does not model a `chatfilter` format** — there is no
layout, no stride, and no record structure to describe. An implementor must not invent one. If a
chat word-filter behaviour is ever required, it has to be sourced elsewhere (a server-side rule
set or a later client build), because the analysed build ships none.

---

## Section 9 — `creature_item.xdb` — Creature-to-item binding table

> **Verification status: sample_verified** (stride + record count, build `263bd994`);
> **static-hypothesis** (field roles — head-only, single-pass; no loader traced). Newly covered in
> the 2026-06-16 re-verification pass; previously undocumented.

- **Extension:** `.xdb` (a fourth flat-array `.xdb` variant; distinguished by its VFS path)
- **Found in:** VFS; logical path: `data/script/creature_item.xdb`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.

**Role (HYPOTHESIS):** binds a large creature/object id to an item reference plus a 3D extent
(a capture/pickup bounding volume or per-creature drop region). The role is inferred from the field
shapes only and is not loader-confirmed.

### File layout

Flat array of fixed 48-byte records. No header. Record count = `file_size / 48`. The file size must
be an exact multiple of 48; any remainder is an error.

**Size verification (SAMPLE-VERIFIED):** the VFS instance is **44 208 bytes = exactly 921 records**
(44 208 / 48 = 921.0).

#### Per-record layout (48 bytes — head-only, single-pass)

| Offset | Size | Type   | Field        | Notes                                                              | Confidence |
|-------:|-----:|--------|--------------|--------------------------------------------------------------------|------------|
| 0      | 4    | u32LE  | creature_id  | Large non-sequential creature/object id (the lookup key)           | sample-verified (shape) |
| 4      | 4    | u32LE  | item_ref     | Item / reference id (small values observed)                        | static-hypothesis |
| 8      | 4    | f32LE  | extent_a     | First of a set of f32 extent values; same across head records      | static-hypothesis |
| 12     | 4    | f32LE  | extent_b     | f32 extent                                                          | static-hypothesis |
| 16     | 4    | f32LE  | extent_c     | f32 extent                                                          | static-hypothesis |
| 20     | 4    | f32LE  | extent_d     | f32 extent                                                          | static-hypothesis |
| 24     | 4    | f32LE  | extent_e     | f32 extent (repeats `extent_a` in head records)                    | static-hypothesis |
| 28     | 4    | f32LE  | extent_f     | f32 extent (repeats `extent_b` in head records)                    | static-hypothesis |
| 32     | 4    | f32LE  | extent_g     | f32 extent (a height/scale-like value)                             | static-hypothesis |
| 36     | 4    | u32LE  | field9       | Zero in head records (flag / padding)                              | static-hypothesis |
| 40     | 4    | u32LE  | field10      | Small packed value (a u32 reading ~256, or a u16 pair)             | static-hypothesis |
| 44     | 4    | u32LE  | field11      | Small value (~100; count / tier candidate)                         | static-hypothesis |

**Notes:**
- Across the inspected head records, the seven f32 fields (+8…+32) are constant while only
  `creature_id` (+0), `item_ref` (+4), and the two trailing small fields (+40, +44) vary — i.e. the
  per-record identity is `(creature_id, item_ref)` and the f32 block is a shared geometric template.
- Field roles are head-only inferences with no loader trace; treat the whole record beyond the
  stride/count as **static-hypothesis**. The first u32 (`creature_id`) keys the table per the
  cross-variant rule in §1.5.

---

## Section 10 — `vehicle.xdb` — Vehicle registry table

> **Verification status: sample_verified** (stride + record count, build `263bd994`);
> **static-hypothesis** (field roles — head-only, single-pass; no loader traced). Newly covered in
> the 2026-06-16 re-verification pass; previously undocumented.

- **Extension:** `.xdb` (a fifth flat-array `.xdb` variant; distinguished by its VFS path)
- **Found in:** VFS; logical path: `data/script/vehicle.xdb`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.

**Role (HYPOTHESIS):** a registry of ride / mount / vehicle entries, each binding a sequential
vehicle id to a resource id, followed by a per-file constant stamp and a large reserved tail.
Inferred from field shapes only; not loader-confirmed.

### File layout

Flat array of fixed 52-byte records. No header. Record count = `file_size / 52`. The file size must
be an exact multiple of 52; any remainder is an error.

**Size verification (SAMPLE-VERIFIED):** the VFS instance is **3 016 bytes = exactly 58 records**
(3 016 / 52 = 58.0).

#### Per-record layout (52 bytes — head-only, single-pass)

| Offset | Size | Type    | Field        | Notes                                                              | Confidence |
|-------:|-----:|---------|--------------|--------------------------------------------------------------------|------------|
| 0      | 4    | u32LE   | vehicle_id   | Sequential vehicle id (1, 2, …; the lookup key)                    | sample-verified (shape) |
| 4      | 4    | u32LE   | resource_id  | Resource / type id; increments by 1 in step with `vehicle_id`      | sample-verified (shape) |
| 8      | 8    | u8[8]   | const_stamp  | **Byte-for-byte identical across all sampled records** — a file-level constant (hash / version stamp), not per-record data | sample-verified (constant) |
| 16     | 36   | u8[36]  | reserved     | All zero in the sampled records; role unknown                      | static-hypothesis |

**Notes:**
- The 8-byte block at +8 is **constant across every inspected record**, so it is best read as a
  per-file stamp baked into each record rather than meaningful per-record data; a parser should not
  interpret it as `vehicle_id`-varying content.
- `vehicle_id` and `resource_id` both increment by 1 across the head records (a dense, sequentially
  allocated table).
- Beyond the stride/count and the constant stamp, the field roles are head-only inferences with no
  loader trace; grade them **static-hypothesis**. The first u32 (`vehicle_id`) keys the table per
  the cross-variant rule in §1.5.

---

## Cross-format summary

| Format          | Header          | Stride | Count source          | Text encoding | Loader confirmed |
|-----------------|-----------------|--------|-----------------------|---------------|---------------|
| `actor_size.xdb` | none           | 12 B   | `file_size / 12` = 15 records | none      | stride only   |
| `buff_icon_position.xdb` | none  | 12 B   | `file_size / 12` = 134 records | none     | YES (loader + stride) |
| `effectscale.xdb` | none          | 8 B    | `file_size / 8` = 2 records | none        | stride only   |
| `creature_item.xdb` | none        | 48 B   | `file_size / 48` = 921 records | none (numeric) | NO (head-only; sample-verified stride) |
| `vehicle.xdb`   | none            | 52 B   | `file_size / 52` = 58 records | none (numeric) | NO (head-only; sample-verified stride) |
| `mobinfo.mi`    | 4-byte u32 count | 28 B  | stored `count` field (= 21) | none (refs only) | NO — DEAD (confirmed not read; present on disk, no loader) |
| `.tol`          | 16-byte header  | 1 B/tile | `width × height`    | none          | NO            |
| `descript.ion`  | none            | variable | until EOF (CRLF delimited) | ASCII  | NO            |
| `discript.sc`   | none            | 68 B   | `file_size / 68`      | CP949 (display_name) | YES (context-menu label loader + stride) |
| `msg.xdb`       | none            | 516 B  | `file_size / 516` = 2,644 records | CP949 (text, 0xEE fill) | YES (loader + stride + content SAMPLE-VERIFIED) |
| `mapsetting.scr` | none           | 84 B   | `file_size / 84` = 52 records | CP949 (zone_name) | NO (sample-verified stride) |
| `regiontableNNN.bin` | none       | 32 B   | `file_size / 32` = 52 records | CP949 (sub_zone_name) | NO (sample-verified stride) |

## Known unknowns (cross-format)

- Whether any `.xdb` variant can vary in stride across game patches.
- **`mobinfo.mi` `field6` (offset +24, was `portrait_res_3`)** — role MOOT: the file is DEAD in this
  build (confirmed not read), so there is no consumer to pin the field. On disk it is a 4-byte
  u32/i32 LE with `0xFFFFFFFF` = -1 = none; small values (99, 103) read as an optional small
  id/index (HYPOTHESIS); the `portrait_res_3` label is withdrawn (§2). RESOLVED, CYCLE 7.
- Whether `mobinfo.mi` files covering mob class IDs outside the range 101 – 121 exist (academic —
  the file is not read in this build).
- The full field semantics of `creature_item.xdb` (§9) and `vehicle.xdb` (§10) — stride and count
  are sample-verified, but the per-record field roles are head-only inferences with no loader trace.
- The semantics of `descript.ion` `field1` / `field2` — pak offset vs checksum vs size (the sampled
  record reads `field1 < field2`, both ~30–32 MB).
- Whether `descript.ion` can have multiple records; the only VFS instance is a single 29-byte record.
- The world-origin sub-tile scale factor (256) in `.tol` files is inferred, not confirmed from
  the engine's internal constants.
- The `reserved` 27-byte region in `discript.sc` records.
- The positional-substitution mechanism for `%1%`/`%2%`/`%3%` format strings in `msg.xdb`.
- The exact maximum `id` value in `msg.xdb` (estimated ~3,643; not exhaustively verified).
- The duration-field unit and on-icon countdown rendering for the buff bar (§1.6); the whole
  buff-bar wire path (response 4/102) is CODE-CONFIRMED but CAPTURE-UNVERIFIED.
- The packed `flags_a` / `unknown_0x4C` semantics in `mapsetting.scr` (§7.1) and the two-sub-type
  discriminator in `regiontableNNN.bin` (§7.2).

## Cross-references

- Related formats: `pak.md` (archive container that holds all of the above), `terrain.md` (`.tol`
  is a terrain companion file; `.ted` terrain data is the likely runtime minimap source),
  `texture.md` (`.ion` references `.tga` texture filenames).
- `msg.xdb` consumers: `Docs/RE/specs/ui_system.md` (caption lookup by numeric ID),
  `mobinfo.mi` §2 (mob name string IDs reference this catalogue),
  `formats/ui_manifests.md` §8 (widget-to-caption binding reference).
- Buff bar (§1.6): `buff_icon_position.xdb` (§1.3) supplies the per-buff atlas coordinates;
  the shared atlas `data/ui/skillicon/stateicon.dds` and the 30-slot bar are described in
  `formats/ui_manifests.md` (UI atlas catalogue); the driving response 4/102
  `SkillWindowStateUpdate` is packet-spec material for `Docs/RE/packets/`.
- Map zone tables (§7): the world-map / minimap art (`data/ui/map/map1.dds`,
  `data/ui/broodwarmap.dds`, `data/ui/direction.dds`, `data/ui/map_userpoint.tga`) is catalogued in
  `formats/ui_manifests.md`; world-coordinate conventions are documented in `terrain.md`.
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md`. §5 `discript.sc` role correction and §8
  `chatfilter`-absent finding promoted under CAMPAIGN VFS-MASTERY (two-witness: loader +
  black-box).
