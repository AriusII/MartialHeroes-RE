# Format: .eff / .wlk / .run / .bgm / .bge  (per-map sound event and music schedule tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> verification: sample-verified
> ida_reverified: 2026-06-21
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
> reverify_2026-06-21: STATIC-IDA + REAL-SAMPLE re-run CONFIRMS this spec on every load-bearing point
>            (size 13312, stride 48, count 256, read 0x3000, two loaders, tod_enable hour gate with
>            3600 divisor + +0x04 base, +0x20 X / +0x28 Z / +0x2C radius EFF-only, null sentinel,
>            mud byte mapping, cat 0/6 + boundary 5 → 2d/3d, join key id → <dir>/<id>.ogg). Three
>            refinements applied: (R1) the 1024-byte trailer is positively a `u32[256]` per-slot
>            present-flag table (not "unresolved padding"); (R3) the `+8 kind` / `+9 flag` / `+10 name`
>            view is the RUNTIME index-node layout, NOT an on-disk overlay — there is NO on-disk field
>            overlap, so the prior per-file-type ambiguity is withdrawn; (R2) +0x24 NOT-pos_y is
>            doubly corroborated — the EFF play path substitutes the LOCAL PLAYER's Y, never reading
>            +0x24. (R4) the `data/effect/obj` geometry-.eff path string was not found in this run's
>            string set — Section 6 rests on prior samples; flagged for re-check.
> conflicts: C1 — VFS census shows 301 soundtable entries (bge=61, others=60), one extra `.bge`,
>            so "≈300 / five-per-area" is softened to "≈301, one extra .bge" (NOT a layout conflict).
>            C2 (CYCLE 7) — the 0x3000 value is the LOADER-READ record-array extent = the full
>            meaningful file body (256 × 48 = 12288), NOT a "trailer". The 1024 bytes AFTER 0x3000
>            are the loader-ignored trailing region. Prior text calling 0x3000-related content a
>            "trailer" is corrected: 0x3000 = whole record-array size; the trailer is the 0x3000..0x33FF
>            region. The hour-of-day semantics of the +4 24-byte field are RESOLVED this cycle (was DBG-pending).
>
> status: sample_verified
> sample_verified: true  (file size, entry count, stride, entry field layout, and audio container
>                         formats all confirmed against real sample files — 2026-06-11;
>                         on-disk 48-byte record stride two-witness-confirmed — 2026-06-15;
>                         RE-CONFIRMED two-witness on build 263bd994 — 2026-06-16: the per-area
>                         loader advances 48 bytes per record across 256 records and reads exactly
>                         12288 bytes (0x3000), leaving a 1024-byte unread trailing region. The stride-48
>                         reading is now independently corroborated on BOTH witnesses — the loader's
>                         per-record advance AND field-coherence on a populated `.eff` table: at stride
>                         48 all 154 active records yield coherent world-space f32 position/radius, while
>                         stride 52 shreds those fields. The 52-byte reading is refuted by both
>                         witnesses.)
> cycle7_note: 2026-06-20 (IDB SHA 263bd994, CYCLE 7) — TWO loaders re-confirm that 0x3000 (12288)
>              is the full record-array body (256 × 48), NOT a "trailer": a global loader reads FIVE
>              per-map soundtable files each as a fixed 0x3000 blob; a per-scene loader reads three
>              (.bgm/.bge/.eff) and iterates exactly 256 records at the 48-byte (12-dword) stride.
>              The 0x3000..0x33FF region (1024 bytes) is the loader-ignored trailing region (since
>              re-verified as a `u32[256]` present-flag table — see Known unknown #1). The +4
>              24-byte field is RESOLVED as the per-hour-of-day enable bitmap (hour-gating). The
>              former "+10 name vs +0x20.. param overlap" caveat is WITHDRAWN: `+8 kind`/`+9 flag`/
>              `+10 name` is the RUNTIME index-node layout, not an on-disk overlay (see Per-record layout).

---

## CRITICAL DISAMBIGUATION — .eff is NOT a single-purpose format

The `.eff` extension is **reused for two unrelated file types** in this engine, distinguished
only by VFS directory path:

| Path pattern | File type |
|---|---|
| `data/map*/soundtable*.eff` | Per-map sound trigger schedule table (256 records × 48 B read by the loader, then a 1024-byte unread `u32[256]` present-flag trailer; same layout as .wlk/.run/.bgm/.bge) |
| `data/effect/obj/*.eff` | 3D triangle mesh collision / area shape (variable length, starts with a u32 triangle count) |

An engineer must never assume a `.eff` file is a sound table without first confirming the
VFS path prefix. Do NOT open `data/effect/obj/*.eff` as a sound table. See Section 6 for
the geometry shape variant.

The `.xeff` extension (used by the particle/visual-effect system) is a completely separate
third format and is unrelated to either `.eff` variant above.

> **CYCLE 7 explicit statement (do not conflate).** The `soundtable*.eff` documented in THIS file
> is the **SOUND-effect table** — a per-map 48-byte-stride table of triggered point-source SOUND
> events (3D positional audio cues). It is **DISTINCT** from the **visual-effect** `.eff`
> (`data/effect/obj/*.eff`, the triangle-mesh collision/area shapes — Section 6) and from the
> particle `.xeff`. The visual-effect formats are owned by `formats/effects.md` / a future
> `formats/eff_geometry.md`, **not** by this spec. Never merge the sound-effect table with the
> visual-effect `.eff`: same extension, unrelated formats, disambiguated solely by VFS path prefix.

---

## Identification

- **Extensions:** `.wlk`, `.run`, `.bgm`, `.bge`, `.eff` (sound table variant only)
- **Found in:**
  - `data/map<id>/soundtable<id>.<ext>` — runtime (in-game map variant). `<id>` is a
    zero-padded 3-digit area number matching the owning cell's `.mud` path (e.g. `map001`,
    `soundtable001.bgm` for area 1; `map000` / `soundtable000.*` for the global / lobby area).
  - `tool/sound/soundtable<id>.<ext>` — editor / tool variant
- **Table inventory (VFS census, re-counted on the real sample 2026-06-16):** **≈301** per-area
  binary tables, NOT a clean five-per-area count. The actual extension split is `.bge` = 61 and
  `.bgm` / `.eff` / `.run` / `.wlk` = 60 each (one extra `.bge` table — an area carries a `.bge`
  without the matching other four, or a `map000` / global duplicate). The `map000` global set is
  included in this total. Every table is binary (never CP949 text) and every table is exactly
  13312 bytes. — confidence: SAMPLE-VERIFIED. (The earlier "five-per-area × ~60 = ~300" phrasing is
  softened to "≈301, one extra .bge" — this is CONFLICT C1, an inventory note only, NOT a layout
  conflict; the format is unaffected.)
- **Magic / signature:** none — no file-level magic or version header; first byte of a
  well-formed file is the low byte of the `sound_entry_id` u32 at record index 0
- **Endianness:** little-endian (confirmed: f32 1.0 stored as `00 00 80 3F`)
- **File size:** fixed 13312 bytes (0x3400) — confirmed across 12 runtime samples (2026-06-11),
  re-confirmed across all tables by VFS census (2026-06-14), and RE-CONFIRMED on build 263bd994:
  **301/301** soundtable entries in the real VFS sample are exactly 13312 bytes (size histogram
  `{13312: 301}`). Of this, the loader reads only the first 12288 bytes (0x3000); see File layout.

---

## Semantic mapping of the five sound-table extensions

All five files for a given map share identical binary structure and identical size. They
differ only in the category of sound event they carry:

| Extension | Semantic role | Terrain-cell index byte(s) |
|---|---|---|
| `.wlk` | Walk footstep sounds | PLAUSIBLY mud-cell byte at cell offset +0 (see `mud.md` bytes-0/1 hypothesis; UNVERIFIED) |
| `.run` | Run footstep sounds | PLAUSIBLY mud-cell byte at cell offset +1 (see `mud.md` bytes-0/1 hypothesis; UNVERIFIED) |
| `.bgm` | Background music zones | Mud-cell byte at cell offset +2 |
| `.bge` | Looped ambient sound effects | Mud-cell bytes at cell offsets +3 and +4 (up to 2 simultaneous per cell) |
| `.eff` (sound table) | Triggered point-source sound events | Mud-cell bytes at cell offsets +5, +6, and +7 (up to 3 simultaneous per cell) |

The `.wlk` and `.run` files in all available samples contain only null records (sound_entry_id
= 0 in every slot). The terrain-cell indexing mechanism for footstep tables is confirmed to
exist but its index source in the `.mud` tile was not directly observed; the leading two mud
bytes are the leading hypothesis — see the cross-reference to `mud.md`.

---

## File layout

### Overall structure — loader stride reconciliation (two-witness)

A 2026-06-15 two-witness reconciliation (a static loader reading plus an independent black-box
file measurement) settled the record stride, and it was RE-CONFIRMED on build 263bd994
(2026-06-16) with a stronger evidentiary base. The loader advances **48 bytes (0x30)** per record
and iterates over **256 records**, reading exactly **12288 bytes (0x3000 = 256 × 48)** from the
start of the file. The remaining **1024 bytes** of the fixed 13312-byte file form a trailer that
the loader never reads.

This **corrects** an earlier (2026-06-14) on-disk reading that proposed a uniform 52-byte stride
with no trailer (256 × 52 = 13312). That 52-byte figure is wrong: the loader's per-record advance
is 48 bytes, not 52, and the final 1024 bytes are a separate unread region rather than a 4-byte
per-record tail. Both readings reconcile to the same 13312-byte total file size, and both agree on
the field layout of the first 0x2C bytes of every record (see the per-record table below). The
48-byte record stride plus the 1024-byte unread trailer is the authoritative layout for a parser.

> **The clean 52-division is the exact trap.** `13312 % 52 = 0` (clean) but `13312 % 48 = 16`
> (a remainder), so a black-box-only reading is *tempted* by 52. Two independent witnesses overrule
> it: (1) the loader's per-record advance is unambiguously 48 bytes per iteration over a fixed 256
> iterations, and (2) reading a populated table (`soundtable001.eff`, 154 active records) at stride
> 48 makes **100% of the active records** produce coherent world-space f32 X / Z / radius, whereas
> reading at stride 52 misaligns the fields (only ~40–50% of 208 misaligned slots stay plausible).
> So **both witnesses independently pick 48**; the 52-byte reading is REFUTED.

> **CYCLE 7 (2026-06-20, IDB SHA 263bd994) — "0x3000" is the full record-array size, NOT a trailer.**
> Any earlier phrasing that treated the value 0x3000 itself as a "trailer" is **corrected**: 0x3000
> (12288) is exactly the **record-array body** = 256 records × 48 bytes, and it is the **only region
> the loader reads**. Two distinct loaders confirm this on the live binary:
> - a **global / map-set loader** opens **FIVE** per-map soundtable files (`.wlk .run .bgm .bge .eff`)
>   and reads each as a **fixed 0x3000 blob** (zero-clearing each global up front), and
> - a **per-scene loader** opens **three** (`.bgm .bge .eff`) and iterates **exactly 256 records at a
>   48-byte (12-dword) stride** (`record_ptr += 12 dwords; while index < 256`).
>
> Both arrive at 256 × 48 = 0x3000. The **trailer is the 0x3000..0x33FF region** (1024 bytes after the
> array), which the loader never touches — it is *not* the 0x3000 value. The prior "stride 48 (not 52)"
> finding is CONFIRMED; the only correction is terminological: 0x3000 = whole record-array size.

### Two loader entry points (not one)

There are **two** distinct loader routines, both using the same 12288-byte (0x3000) read size per
table; the difference is which extensions they touch:

| Entry point (role) | Extensions opened | Behaviour |
|---|---|---|
| Per-area runtime loader | `.bgm`, `.bge`, `.eff` (3 of 5) | The in-game path. Opens the three from `data/map<aaa>/`, reads 0x3000 into three parallel buffers, then walks 256 records at the 48-byte stride indexing the +0x00 id of each. THIS is the authoritative stride/count witness, and it is the path that drives runtime ambient/BGM/EFF playback. |
| Map-set loader | all five exts | The map-load path. Opens **all five** extensions (both `tool/sound/` and `data/map/` format strings) and reads exactly 0x3000 from each into five fixed global buffers spaced 0x3000 apart. |

This explains why `.wlk` / `.run` are *loaded* (by the map-set loader) but never *indexed* in the
runtime per-area path. The field/stride/count conclusions are identical across both routines.

| Region | Offset | Size (bytes) | Notes |
|---|---:|---:|---|
| Record table (read) | 0 (0x0000) | 12288 (0x3000) | 256 records × **48 bytes** — loader reads this region |
| Present-flag trailer (unread) | 12288 (0x3000) | 1024 (0x0400) | `u32[256]` per-slot present/active flag table — one dword per record index, value 1 where the matching record is populated, else 0. Present in every file; the loader never reads it (almost certainly an authoring/editor artifact). | 
| **Total on disk** | — | **13312 (0x3400)** | Confirmed across 12 runtime samples and ≈301 census tables (301/301 sample-verified on build 263bd994) |

- **Record count source:** fixed at **256** — there is no count field; the loader iterates 256
  times. (12288 / 48 = 256, exact.) — confidence: CONFIRMED
- **Record stride:** **48 bytes**. — confidence: CONFIRMED (two-witness: loader advance + file
  measurement)

### Record index 0 — null sentinel

Record index 0 is the null/disabled sentinel — a terrain cell byte value of 0 means
"no sound assigned", so the slot at index 0 is never the target of a meaningful lookup.

---

## Per-record layout (48 bytes, little-endian throughout)

Confidence levels reflect triangulation of four sources: a static reading of the loader's
record-advance and field accesses, direct observation of 12 runtime samples (2026-06-11), a
256-record field census of `.bgm` / `.bge` / `.eff` tables in area 001 (2026-06-14), and a
build-263bd994 two-witness re-verification (2026-06-16) that read the populated `soundtable001.eff`
table (154 active records) at the 48-byte stride and found every active field coherent.

The record is exactly 48 bytes (offsets +0x00 .. +0x2F). There is no per-record tail beyond
+0x2F; the bytes formerly attributed to a per-record tail belong to the file-level 1024-byte
unread trailer (see File layout).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | `sound_entry_id` | Numeric resource key; 0 = empty/unassigned slot. Active records carry 9-digit decimal values (range ~900000000..999999999; see Sound ID section). | CONFIRMED |
| +0x04 | 24 | u8[24] | `tod_enable[24]` (hour-of-day enable bitmap) | One byte per **in-game hour 0..23**. **CYCLE 7 RESOLVED (control-flow-confirmed):** this is the per-hour-of-day **enable bitmap** that gates whether the row plays at the current hour. At play time the ambient driver computes `hour = time-of-day-ms / 3600` (clamped 0..23) and tests `record[+4 + hour]`; a zero byte suppresses/stops the cue for that hour, non-zero (re)starts it. This is the field formerly called `unlabeled_24` / "DBG-pending mask". All-runtime-0x01 in null/simple samples; area-001 census shows per-record 0x00/0x01 patterns, consistent with per-hour gating. | CONFIRMED structure; **hour-of-day semantics CYCLE 7 control-flow-confirmed** (the 3600 divisor + the +4-base index are explicit in the ambient driver) |
| +0x1C | 4 | f32 | `weight` | Volume / attenuation / blend scalar. `weight == 1.0f` (`00 00 80 3F`) for **all 256 records in every sampled table** (including all 154 active `.eff` records), not just BGM / BGE. Not accessed in the observed runtime playback path. | SAMPLE-VERIFIED type/value; semantic UNVERIFIED |
| +0x20 | 4 | f32 | `pos_x` | World-space X of the 3D source. Populated (non-zero) only in `.eff` (3D) records; 0.0 for BGM / BGE / WLK / RUN. Passed to the DirectSound 3D position as the X argument. | CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED |
| +0x24 | 4 | — | `unlabeled_24` | The loader does NOT read these 4 bytes. The earlier `pos_y` label is incorrect — no read site assigns a meaning to this offset. Two independent witnesses REFUTE a position role: (a) the EFF play path builds the 3D source position from `+0x20` (X), the **local player's current Y** (substituted at play time), and `+0x28` (Z) — it never reads `+0x24` as the Y axis; (b) in the 154 active `.eff` records the sample value at +0x24 is **neither a plausible world-space f32 nor exactly zero** (one active record reads as a tiny f32 denormal / arbitrary integer) — non-coordinate data, not a Y axis. Left unlabeled; role unestablished (possibly an editor id). | NOT-READ by loader; play-path + sample both confirm NOT a position field; meaning UNRESOLVED |
| +0x28 | 4 | f32 | `pos_z` | World-space Z of the 3D source. Populated only in `.eff` records; 0.0 for BGM / BGE. Passed to the DirectSound 3D position as the Z argument. | CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED |
| +0x2C | 4 | f32 | `radius` | Audibility radius of the 3D source (formerly labelled `volume_factor`). Populated only in `.eff` records; 0.0 for BGM / BGE. For the BGM playback path the runtime applies a 0.7 volume scaling at a separate stage. | CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001 |

**Record stride: 48 bytes. CONFIRMED.** (256 × 48 = 12288 = bytes read by the loader; the
remaining 1024 bytes are the unread file trailer.)

> **The `+8 kind` / `+9 flag` / `+10 name` view is the RUNTIME INDEX NODE, NOT an on-disk overlay
> (re-verification — supersedes the earlier "field-overlap" caveat).** The on-disk record is
> unambiguously `{ u32 id @+0x00; u8 tod_enable[24] @+0x04; f32 weight @+0x1C; f32 pos_x @+0x20;
> (+0x24 not read); f32 pos_z @+0x28; f32 radius @+0x2C }` — sample-verified at stride 48, with **no
> kind, flag, or name field anywhere in the on-disk bytes**. The `+8 sound_kind` / `+9 flag` /
> `+10 name` layout instead describes a **runtime-constructed index node** the per-area loader builds
> for each non-null record: it allocates a fresh 48-byte (0x30) node and populates it as
> `{ id @+0x00; kind byte @+0x08; 0 @+0x09; name string (≤31 bytes) @+0x0A; owner @+0x2C; 0 @+0x29 }`,
> then inserts it into a sorted by-id collection. The `id` is copied from the on-disk record's
> `+0x00`; the **`kind` byte is a HARDCODED category** assigned at index time (0 for the `.bgm`/`.bge`
> arrays, 6 for the `.eff` array — see Sound ID section); the **`name` is the strncpy ARGUMENT** (a
> runtime tag passed to the indexer), not bytes read from the file. The coincidence that the runtime
> node is also 48 bytes is unrelated to the on-disk record size.
>
> Consequently there is **no on-disk field overlap and no per-file-type ambiguity**: the `+8 kind` /
> `+9 flag` / `+10 name` offsets are valid *only* for the in-memory index node, and the runtime parser
> should read the on-disk record exactly as the field table above (24-byte `tod_enable` + the f32
> params). The earlier "SAMPLE/DBG-pending: +10 name vs +0x20 params, mutually exclusive per file
> type?" question is **withdrawn** — it rested on treating the runtime node layout as a possible
> alternate on-disk reading, which the binary disproves.
>
> The **`sound_kind` value table** itself (the `SOUND_KIND` enum the runtime `kind` byte holds) is
> documented in `specs/sound.md §9` (value 1 reserved/NONE; `SOUND_SKILL` = 11). The runtime node's
> `+9 flag` byte's precise meaning (loop vs one-shot vs sub-channel) remains **unestablished**.

> Field-naming reconciliation: an earlier spec named offsets +0x20 / +0x28 / +0x2C as
> `pos_x` / `pos_z` / `volume_factor` and +0x24 as `unknown_36` (later mislabelled `pos_y`). The
> 2026-06-14 EFF field census resolved the runtime-read axes as +0x20 = X (read), +0x28 = Z
> (read), +0x2C = radius (read). The two-witness loader reading confirms +0x24 is NOT read by the
> loader on any path, so it carries no recovered position meaning — it is left unlabeled. These
> read fields are only populated by `.eff` (3D) records; BGM / BGE records leave them zero.

### Byte-level field map (quick reference)

```
[+0x00..+0x03]  sound_entry_id   u32 LE    (0 = null/unassigned)
[+0x04..+0x1B]  tod_enable       u8 × 24   (per-HOUR-OF-DAY enable bitmap, hour 0..23; CYCLE 7 control-flow-confirmed)
[+0x1C..+0x1F]  weight           f32 LE    (1.0f for BGM/BGE; blend/attenuation scalar)
[+0x20..+0x23]  pos_x            f32 LE    (3D world X; read; EFF records only, else 0.0)
[+0x24..+0x27]  unlabeled_24     4 bytes   (NOT read by the loader; meaning unresolved)
[+0x28..+0x2B]  pos_z            f32 LE    (3D world Z; read; EFF records only, else 0.0)
[+0x2C..+0x2F]  radius           f32 LE    (3D audibility radius; read; EFF records only, else 0.0)
--- end of 48-byte record; next record begins at +0x30 ---
```

> The `+8 sound_kind` (SOUND_KIND enum) / `+9 flag` / `+10.. name` triple is the layout of the
> RUNTIME index node the loader builds per record (NOT an on-disk overlay — see the caveat above);
> the on-disk record has no such fields.

After the 256th record the file carries a 1024-byte `u32[256]` present-flag trailer the loader never
reads (see File layout, Known unknown #1).

---

## Resolution chain — `.mud` tile → soundtable record → leaf audio

The `.mud` per-cell ambient-sound zone grid stores byte indices that select records in these
tables. The mud byte value is used as a **direct 0-based row index** into the table whose
extension matches the byte's role. A mud byte of 0 selects record index 0 (the null sentinel)
and plays nothing. (Cross-map census: max bgmZoneId = 10, max bgeAmbientId = 20, max effId = 154,
all well under the 256 records per table — SAMPLE-VERIFIED across 1578 `.mud` files.)

```
.mud tile byte +2 (bgmZoneId = N)
  └─► data/map<AAA>/soundtable<AAA>.bgm  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/2d/{sound_entry_id}.ogg          (BGM = 2D / stereo)

.mud tile bytes +3, +4 (bgeAmbientId0/1 = N)
  └─► data/map<AAA>/soundtable<AAA>.bge  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/2d/{sound_entry_id}.ogg          (BGE ambient = 2D)

.mud tile bytes +5, +6, +7 (effId0/1/2 = N)
  └─► data/map<AAA>/soundtable<AAA>.eff  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/3d/{sound_entry_id}.ogg          (EFF = 3D positional)
              3D source position = record +0x20 (X), the LOCAL PLAYER's current Y (substituted at
                                   play time), +0x28 (Z) as f32 (note: record +0x24 is NOT read)
              audibility radius  = record +0x2C as f32

.mud tile byte +0  (PLAUSIBLE walk index — UNVERIFIED)
  └─► data/map<AAA>/soundtable<AAA>.wlk  record[N] (0-based) → footstep walk sound

.mud tile byte +1  (PLAUSIBLE run index — UNVERIFIED)
  └─► data/map<AAA>/soundtable<AAA>.run  record[N] (0-based) → footstep run sound
```

`<AAA>` is the zero-padded 3-digit area number from the `.mud` path (e.g. `map001` for area 1).
The 0-based direct-index rule is SAMPLE-VERIFIED for BGM / BGE / EFF across the full area-001 mud
census (77 files) and globally corroborated by the cross-map census. The `.wlk` / `.run` index
source (mud bytes +0 / +1) is PLAUSIBLE only — see `mud.md` and the IDA cross-check request below.

---

## Sound ID semantics

Active `sound_entry_id` values observed in samples are 9-digit decimal integers in the range
~900000000 .. 999999999:

- 910022000
- 910033000
- 910034000
- 910053002
- 920100200

All share a high byte of 0x36 (decimal 54). The pattern is consistent with a Korean MMORPG
resource catalog key of the form `<type> × 10^8 + <sequential_id>`, where the upper digits
encode asset category. The `sound_entry_id` integer is formatted as a plain decimal string
(no zero-padding, no prefix) to form the filename stem.

**The audio directory is NOT universal — it depends on the table extension** (SAMPLE-VERIFIED +
CODE-CONFIRMED; cross-reference `specs/sound.md §13`):

| Table extension | Audio directory | Confidence |
|---|---|---|
| `.bgm` | `data/sound/2d/` | SAMPLE-VERIFIED: `.bgm` IDs confirmed present under `data/sound/2d/` |
| `.bge` | `data/sound/2d/` | SAMPLE-VERIFIED (2026-06-14): BGE IDs confirmed present under `data/sound/2d/` |
| `.eff` (sound table) | `data/sound/3d/` | SAMPLE-VERIFIED: `.eff` sound table IDs confirmed present under `data/sound/3d/` |
| `.wlk` | UNDETERMINED | All sampled `.wlk` records are null (sound_entry_id = 0); directory unverifiable |
| `.run` | UNDETERMINED | All sampled `.run` records are null; directory unverifiable |

The directory selection (`data/sound/2d/` vs `data/sound/3d/`) is encoded in the table
extension, not in the `sound_entry_id` value itself. The engine selects the directory via an
internal per-extension **category code**, not merely a per-extension string. The mechanism is:

- At index time the per-area loader assigns each record an explicit **category code** by table:
  **category 0** for the `.bgm` and `.bge` arrays, **category 6** for the `.eff` array. This code
  is stored in the resulting sound descriptor (descriptor byte +0x08).
- At resolve time the descriptor's category byte is tested against the constant **5**: a value
  **< 5 → 2D** (`data/sound/2d/`, internal type code **1**); a value **≥ 5 → 3D**
  (`data/sound/3d/`, internal type code **3**). Category 0 (bgm/bge) and category 6 (eff)
  straddle the `< 5` boundary exactly as the observed 2D/3D outcome requires.
- The open primitive receives `(sound_entry_id, type_code, dir_string)` and builds
  `<dir_string><sound_entry_id>.ogg`; type_code 1 = 2D, 3 = 3D.

This is firmer than a bare "extension → directory" rule: the boundary constant is 5, the concrete
category codes are 0 (bgm/bge) and 6 (eff), and the open-primitive type codes are 1 (2D) and
3 (3D). — confidence: SAMPLE-VERIFIED (EFF records carry non-zero 3D position/radius → 3D;
BGM / BGE leave them zero → 2D, matching the assigned categories).

For the `.bgm` case, the IDs in the range `910xxxxx` map to BGM tracks (stereo, 44100 Hz Vorbis)
which reside exclusively in `data/sound/2d/`. For the `.eff` sound table case, the IDs map to short
3D positional effects stored in `data/sound/3d/`.

How the engine resolves IDs that correspond to `.wav` files rather than `.ogg` files is
not determined; the principal path-construction code uses `.ogg` unconditionally. WAV files
in `data/sound/3d/` may be loaded through a different call path.

---

## Terrain-cell indexing (summary; full chain above)

The terrain system stores a per-tile record in `.mud` format (see `mud.md`). The sound table is
addressed by bytes within the mud tile:

| Extension | Mud-tile byte offset(s) used as record index | Max simultaneous sounds per tile | Confidence |
|---|---|---|---|
| `.bgm` | +2 | 1 | CONFIRMED |
| `.bge` | +3 and +4 (separate indices) | 2 | CONFIRMED |
| `.eff` (sound table) | +5, +6, +7 (separate indices) | 3 | CONFIRMED |
| `.wlk` | +0 (PLAUSIBLE) | 1 (presumed) | PLAUSIBLE / UNVERIFIED |
| `.run` | +1 (PLAUSIBLE) | 1 (presumed) | PLAUSIBLE / UNVERIFIED |

A mud byte value of 0 selects record index 0 (the null sentinel). The runtime checks the mud
byte before performing any table lookup and skips the slot if the byte is zero.

---

## Section 6 — data/effect/obj/*.eff — 3D geometry shape format

Files under `data/effect/obj/` that carry the `.eff` extension are **triangle mesh
collision/area shapes**, not sound tables. They are structurally and semantically unrelated
to the per-map sound table format described above.

These files carry NO file-level magic. Their format begins immediately with a triangle count:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 4 | u32 LE | `triangle_count` | Number of triangles in the mesh | CONFIRMED from samples |
| 4 | variable | u16[] | index array | `triangle_count × 3 × u16` for triangle list topology | CONFIRMED (cone, rect); ambiguous for tringle.eff |
| after indices | variable | f32[] | vertex position array | `vertex_count × 3 × f32` (XYZ per vertex) | CONFIRMED (cone, rect) |

Observed samples: `cone.eff` (496 bytes, 36 triangles, 23 vertices), `rect.eff` (148 bytes,
6 triangles, 9 vertices), `tringle.eff` (110 bytes — index/vertex boundary ambiguous; may
use a triangle-strip variant with fewer indices). The full encoding variant for `tringle.eff`
is UNRESOLVED.

A dedicated `formats/eff_geometry.md` spec should be produced once the geometry-shape
loader is traced.

> Path-string note (re-verification): the literal `data/effect/obj` path string was **not present in
> the string set examined during the 2026-06-21 re-run** (the geometry-shape variant rests on prior
> samples, not on a string witness in that build). This is not a contradiction — the variant is
> sample-verified from `cone.eff` / `rect.eff` — but the path prefix is unverified from strings in
> this build; re-confirm it when Section 6 is next revised.

---

## Section 7 — Audio container formats (.ogg and .wav)

Sound files referenced by `sound_entry_id` values in these tables are stored under
`data/sound/2d/` and `data/sound/3d/` as **standard, unmodified audio containers**. No
proprietary header, no encryption, and no additional framing wraps these files.

### 7.1 Ogg Vorbis (.ogg)

- **Magic:** ASCII `OggS` at file offset 0 (bytes `4F 67 67 53`) — standard Ogg capture pattern
- **Format:** Standard Ogg Vorbis as defined by RFC 3533 and the Vorbis I specification.
  Directly decodable by any Ogg Vorbis decoder without preprocessing.
- **Observed codec parameters (all three samples are identical):**
  - Channels: 1 (mono)
  - Sample rate: 22050 Hz
  - Nominal bitrate: 16000 bps
  - Encoder: Xiph.Org libVorbis I, build 2002-07-17
  - No embedded comment tags
- **VFS path pattern:** `data/sound/3d/<sound_entry_id>.ogg` (3D positional — `.eff` table
  IDs); `data/sound/2d/<sound_entry_id>.ogg` (2D non-positional — `.bgm` / `.bge` table IDs; 178
  `.ogg` files confirmed in `data/sound/2d/`, SAMPLE-VERIFIED)
- **Filename scheme:** plain decimal integer stem matching `sound_entry_id`, no zero-padding
  (e.g. `841390513.ogg`)
- **sample_verified: true** — three `.ogg` samples confirmed from `data/sound/3d/`; directory
  `data/sound/2d/` additionally SAMPLE-VERIFIED (178 files, including stereo 44100 Hz BGM tracks)
- **Max decoded PCM size:** the engine allocates a 512 KiB (524288 bytes) decode buffer.
  At 22050 Hz mono 16-bit, this accommodates approximately 11.9 seconds of audio. The engine
  logs an error if decoded PCM exceeds this limit.

### 7.2 RIFF/WAVE PCM (.wav)

- **Magic:** ASCII `RIFF` at file offset 0 (bytes `52 49 46 46`), form type `WAVE`
  at offset 8 (bytes `57 41 56 45`) — standard RIFF container
- **Format:** Standard RIFF/WAVE PCM (WAVE_FORMAT_PCM, audio_format = 1). Directly
  playable by any standard WAV decoder without preprocessing.
- **Observed codec parameters (one sample):**
  - Channels: 1 (mono)
  - Sample rate: 22050 Hz
  - Bit depth: 16-bit signed little-endian PCM
  - Byte rate: 44100 bytes/s
  - Duration: approximately 1.07 seconds
  - No `fact` chunk; `fmt` chunk is exactly 16 bytes (no WAVEFORMATEX extension)
- **VFS path pattern:** `data/sound/3d/<sound_entry_id>.wav` (one sample observed in the 3D
  directory; 2D directory .wav presence UNVERIFIED)
- **sample_verified: true** — one `.wav` sample confirmed from `data/sound/3d/`

### 7.3 Engine loading notes

The engine selects the `data/sound/2d/` or `data/sound/3d/` directory based on a type byte
in the internal sound descriptor object. When the VFS is mounted, audio data is loaded from
the VFS buffer and decoded using Ogg Vorbis callbacks (`ov_open_callbacks`). When the VFS is
not mounted (editor / development mode), the engine falls back to direct filesystem `fopen`.
3D sounds use DirectSound3D (`IDirectSound3DBuffer`) for spatialisation; 2D sounds do not.

A streaming playback path exists for long audio (BGM tracks), which re-opens the Ogg stream
in place rather than fully decoding it. No streaming-specific file format differences apply;
the `.ogg` container format is identical.

---

## .xeff files (visual/particle effects — separate format, not this spec)

`.xeff` files are the particle and visual effect format. They are entirely distinct from all
`.eff` variants above.

- **VFS path:** `data/effect/xeff/<name>.xeff`; an index file `data/effect/xeffect.lst`
  enumerates available assets
- **Magic:** ASCII `XEFF` at file offset 0 — confirmed from an engine error string
- **Status:** documented in `formats/effects.md`

---

## Known unknowns

1. **1024-byte unread trailer — IDENTIFIED as `u32[256]` present-flag table** — the final 1024 bytes
   (file offset 0x3000 .. 0x33FF) are present in every file but the loader never reads them. The
   trailing region is positively a **256-entry `u32` per-slot present/active flag table**: one dword
   per record index, value `1` at the index of each populated record and `0` elsewhere. On the
   populated `.bgm` / `.eff` samples the only non-zero entry is index `[1]` (= the single populated
   record at index 1); on the all-null `.bge` / `.run` / `.wlk` samples it is entirely zero. (This
   supersedes the earlier "unresolved padding / single non-zero byte" reading: the non-zero content
   is a structured u32 present-flag, not stray bytes.) It is almost certainly an authoring/editor
   artifact — the runtime loader never touches it, so a faithful runtime parser may skip it. Its only
   remaining open point is whether non-1 values ever appear (all observed non-zero entries are 1).

2. **`unlabeled_24` at +0x24** — these 4 bytes are NOT read by the loader on any path. The earlier
   `pos_y` / `unknown_36` labels are withdrawn. A position role is REFUTED by two witnesses:
   (a) the EFF 3D play path constructs the source position from `+0x20` (X), the **local player's
   current Y** (substituted at play time), and `+0x28` (Z), never reading `+0x24` as the Y axis; and
   (b) the sample value at +0x24 in the 154 active `.eff` records is neither a plausible world-space
   f32 nor zero → non-coordinate data, not a Y axis. No recovered meaning is assigned (possibly an
   editor-side id). Candidate for an IDA cross-check only if a consumer is ever found.

3. **`weight` at +0x1C semantic** — `1.0f` in **all 256 records of every sampled table** (BGM, BGE,
   and all 154 active `.eff` records), not just BGM / BGE; not accessed in the observed runtime path.
   Likely a blend weight, priority, or attenuation scalar. Naming tentative.

4. **`tod_enable` per-byte semantics (formerly `hour_schedule`)** — **CYCLE 7 RESOLVED**
   (control-flow-confirmed): the 24-byte field at +0x04 is the **per-hour-of-day enable bitmap**.
   The ambient driver computes `hour = time-of-day-ms / 3600` (clamped 0..23) and tests
   `record[+0x04 + hour]`; a zero byte suppresses the cue for that hour, non-zero (re)starts it.
   The 3600 divisor and the +0x04 base index are explicit in the driver. The all-0x01 runtime
   samples mean "enabled every hour"; the varying area-001 patterns are per-hour gating. No longer
   DBG-pending. (A faithful runtime parser should expose this as a 24-entry hour gate.)

5. **EFF 3D position units** — whether `pos_x` / `pos_z` and `radius` use the same world-space
   unit scale as the terrain grid is not established; verified populated only in EFF records of one
   area (001).

6. **`.wlk` / `.run` index source** — the mapping from the `.mud` tile to a `.wlk` / `.run`
   record index is UNVERIFIED. The leading hypothesis is mud byte +0 (walk) and +1 (run); see
   `mud.md`. All sampled `.wlk` / `.run` tables are entirely null, so the leaf audio directory is
   also undetermined. IDA / debugger cross-check requested.

7. **sound_entry_id integer encoding** — whether the 9-digit decimal values represent a direct
   catalog key, a CRC32, or a composite type+sequence integer is not established.

8. **sound_entry_id → filename resolution for .wav** — the engine path-construction code uses a
   `.ogg` format string unconditionally in the observed path. How `.wav` files are resolved is
   UNVERIFIED.

9. **Per-map .eff soundtable byte-level sample** — RESOLVED (2026-06-14): the area-001 `.eff`
   table was field-censused (256 records), confirming the read 3D-position axes plus radius.

10. **`data/effect/obj/tringle.eff` encoding variant** — triangle-strip vs triangle-list index
    scheme is ambiguous from size alone. Requires a trace of the effect-geometry loader.

11. **Runtime index-node `+9 flag` byte semantics** — the per-area loader builds an in-memory index
    node per record carrying a small flag byte at node offset +9 (loop vs one-shot vs sub-channel).
    Its precise meaning is unestablished. (This byte lives in the runtime-constructed index node, NOT
    the on-disk record — see the field-map caveat. It is unrelated to the on-disk `tod_enable` span.)

12. **`+10` name vs `+0x20/+0x28/+0x2C` overlap — WITHDRAWN (no on-disk overlap exists).** The earlier
    open question — whether a NUL-terminated `name` string at +10 and the f32 3D params at
    +0x20/+0x28/+0x2C are mutually exclusive per file-type — rested on reading the `+8/+9/+10` triple
    as a possible alternate ON-DISK record layout. The binary disproves that: `+8 kind` / `+9 flag` /
    `+10 name` belong to the RUNTIME index node, and the on-disk record carries the `tod_enable`
    schedule + f32 params with no name field. There is therefore no on-disk overlap to settle. (Item
    closed; see the field-map caveat for the runtime-node vs on-disk distinction.)

13. **Router category-5 vs the 0..4 cap (CYCLE 7, cross-ref `specs/sound.md §2.1`)** — the
    actor-anchored 3D event SFX path uses a play category argument of 5, above the 0..4 multi-voice
    cap; whether it shares the same numeric category space or belongs to a distinct router overload is
    **DBG-pending**. (This is a playback-routing question owned by `specs/sound.md`, noted here only
    because the `.eff` 3D records feed that path.)

---

## Provenance note — stride correction (preserved for audit)

The original (2026-06-11) interpretation, recovered from runtime access-pattern analysis and 12
all-2D runtime samples, described the file as a 256 × 48 = 12288-byte record table followed by a
1024-byte trailing region. A later (2026-06-14) black-box file measurement proposed a uniform
52-byte stride with no trailer (256 × 52 = 13312). The 2026-06-15 two-witness reconciliation — a
static reading of the loader's per-record advance together with an independent file measurement —
settled the matter in favour of the original split: the loader advances **48 bytes** per record,
iterates **256 records**, reads **12288 bytes (0x3000)**, and leaves a **1024-byte trailer
unread**. The 52-byte reading is therefore withdrawn, and the per-record `tail_unknown` it implied
does not exist. The first 0x2C bytes of every record are unaffected by the correction.

A 2026-06-16 re-verification on build 263bd994 RE-CONFIRMED this at tier [sample-verified] with a
stronger evidentiary base. The stride-48 reading now rests on **two independent proofs**: (1) the
per-area loader's per-record advance (48 bytes per iteration over a fixed 256 iterations), and
(2) **field coherence on a populated table** — reading `soundtable001.eff` (154 active records) at
stride 48 makes 100% of the active records produce coherent world-space f32 X / Z / radius, whereas
stride 52 misaligns them. Both witnesses independently select 48; the clean `13312 % 52 = 0`
division (vs the `13312 % 48 = 16` remainder) is the precise trap that made 52 tempting from file
size alone. All structural claims (size 13312, stride 48, count 256, read 12288, trailer 1024, the
five active field offsets, the null sentinel, the category split) re-confirmed TRUE on this build.

---

## Cross-references

- Source of the index bytes into these tables (per-cell ambient-sound zone grid): `formats/mud.md`
- Effect geometry shape variant: `data/effect/obj/*.eff` — a future `formats/eff_geometry.md`
- Visual/particle effects: `data/effect/xeff/` — `formats/effects.md` (magic "XEFF")
- VFS container layout: `Docs/RE/formats/pak.md`
- Sound-subsystem behaviour (directory resolution, streaming): `Docs/RE/specs/sound.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`

---

## Names flagged for names.yaml (orchestrator to record)

- Formats: `soundtable_bgm`, `soundtable_bge`, `soundtable_eff`, `soundtable_wlk`,
  `soundtable_run` (per-area sound index tables)
- Struct: `SoundTableRecord` (48 bytes): `sound_entry_id` (u32 @+0x00), `hour_schedule` (u8[24]
  @+0x04), `weight` (f32 @+0x1C), `pos_x`/`pos_z` (f32 @+0x20/+0x28, EFF only), `radius`
  (f32 @+0x2C, EFF only), `unlabeled_24` (4 bytes @+0x24, not read by the loader)
- Trailer: `SoundTablePresentFlags` (`u32[256]` @ file offset 0x3000, loader-ignored present/active
  flag per record index)
- Constants: `SOUNDTABLE_FILE_SIZE = 13312`, `SOUNDTABLE_RECORD_COUNT = 256`,
  `SOUNDTABLE_RECORD_STRIDE = 48`, `SOUNDTABLE_READ_SIZE = 12288`,
  `SOUNDTABLE_TRAILER_SIZE = 1024`
- Category constants (2D/3D directory split): `SOUND_CAT_2D_MAX = 5` (descriptor cat byte `< 5` →
  2D, `≥ 5` → 3D), `SOUND_CAT_BGM_BGE = 0`, `SOUND_CAT_EFF = 6`, `SOUND_TYPE_2D = 1`,
  `SOUND_TYPE_3D = 3` (open-primitive type codes)
