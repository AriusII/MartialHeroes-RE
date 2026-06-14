# Format: .scr / .do / .ini / .xdb  (client-side configuration and data catalogues)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true (exp.scr, userlevel.scr, userpoint.scr, users.scr, skills.scr,
>                        mobs.scr, citems.scr, skillneedset.scr, warstoneinfo.scr, oblist.scr,
>                        textcommand.do, emoticon.do, msginfo.do, items_extra.do,
>                        items.csv — all confirmed against real sample bytes)
> confirmed: userlevel.scr / userpoint.scr / users.scr record-body columns and the skills.scr
>            field survey are cross-checked against the binary loaders by an analyst; items.scr
>            body fields remain loader-only (no sample) and stay UNVERIFIED where noted.
>
> Wave-8 update (stat curves + item/skill catalogue): the record-body column offsets for the
> four stat-config files (userlevel / userpoint / users / skills) are now resolved against real
> sample bytes, with a layout correction to userpoint.scr (the +8 cumulative is u32, not the
> previously-described u16 + flag byte) and a substantial field survey for skills.scr. items.scr
> disk fields were resolved from the loader path only (no sample available). All Korean text
> fields are CP949 / EUC-KR, null-terminated.

---

## IMPORTANT — Architecture unlock for Client.Domain

The stat curves (EXP per level, level stat bases, stat allocation points) and the item, skill,
mob, and NPC catalogues are **entirely client-side**. They reside as binary `.scr` files under
`data/script/` inside the VFS (`data.inf` + `data/data.vfs`). They are NOT server-side data.
This means all values that Client.Domain was forced to hard-code as `0` (EXP thresholds, base
stats per level, weapon/armour category flags, mob boss flags, etc.) can be recovered by
extracting and parsing these files from the VFS once a sample is available.

---

## Identification

- **Extensions:** `.scr`, `.do`, `.xdb` (binary record files); `.ini` (Windows INI, text);
  `.csv` (comma-separated item catalogue, text)
- **Found in:** `.scr` and `.do` under `data/script/` and `data/item/` in the VFS; `.ini` on
  disk relative to the executable directory; `.xdb` under `data/script/` in the VFS;
  `items.csv` under `data/script/` in the VFS
- **Magic / signature:** none — no file-level magic bytes or version header for any variant
- **Endianness:** little-endian (all binary fields)
- **String encoding:** all string fields are CP949 / EUC-KR (the legacy Korean code page),
  null-terminated; numbers in `.csv` are decimal ASCII

---

## Section 1 — VFS container (data.inf + data/data.vfs)

All `.scr`, `.do`, `.xdb`, and `.csv` files are delivered through the engine's virtual
filesystem. The VFS is documented in full in `formats/pak.md`; only the summary relevant to
these loaders is repeated here.

The TOC index (`data.inf`) provides an entry count and per-file `(dataOffset, dataSize)` pairs.
Lookup is a binary search on the lowercased ASCII filename. Once a file is located, the loader
calls the OS file-read API on the VFS blob.

A configuration key (`vfsmode`) in `game.lua` selects whether the engine reads from the VFS
blob or falls back to direct disk access. The loaders for all formats below use a unified
file-open wrapper that makes this choice transparent; a parser implementation may do the same.

**No compression is applied to any `.scr`, `.do`, `.xdb`, or `.csv` payload in the VFS.**

---

## Section 2 — .scr and .do files (binary record catalogues)

### 2.1 Common structural pattern

All `.scr` and `.do` files share the same loader pattern:

- **No file header, no record-count prefix.** The loader derives record count as:
  `record_count = file_size / record_stride`
- **Flat array of fixed-size records**, concatenated without inter-record padding.
- **Keyed insertion:** records are inserted into a runtime balanced BST (or map) keyed on the
  record's first integer field.
- **Variable-length variants** (items.scr): after the fixed main record, the loader reads
  `N × 8` trailing sub-entries, where `N` is a `u8` count stored at a fixed offset within the
  main record (see per-file tables below). Each 8-byte on-disk sub-entry is expanded to a
  12-byte runtime entry (the extra 4 bytes are filled in at load time).
- **Debug-build padding:** the client was compiled with MSVC debug mode enabled. Unused bytes
  after null terminators in string fields, and structural alignment padding bytes, are filled
  with `0xCC` (the MSVC debug-stack sentinel). These bytes carry no data.

### 2.2 Catalogue file inventory

Stride values marked **CONFIRMED** were established by parser or sample analysis. Strides marked
**UNVERIFIED** were observed as path-builder strings but the corresponding loaders were not
traced.

| VFS path | Stride (bytes) | Trailing entries | Confidence | Role |
|---|---|---|---|---|
| `data/script/exp.scr` | 20 | none | CONFIRMED | EXP required per level |
| `data/script/userlevel.scr` | 60 | none | CONFIRMED | Per-level stat-scaling coefficients |
| `data/script/userpoint.scr` | 32 | none | CONFIRMED | Stat-point allocation budget curve |
| `data/script/users.scr` | 496 (4 × 124-byte class blocks) | none | CONFIRMED | Per-class stat-ratio grid |
| `data/script/items.scr` | 548 | N × 8 B (each → 12 B runtime) | CONFIRMED | Item catalogue (binary) |
| `data/script/skills.scr` | 1504 | none in sample (trailing count byte present but typically 0) | CONFIRMED | Skill catalogue |
| `data/script/skillcategory.scr` | 564 | none | CONFIRMED | Skill category table |
| `data/script/mobs.scr` | 488 | none | CONFIRMED | Mob / monster catalogue |
| `data/script/npcs.scr` | 1916 | none | CONFIRMED | NPC catalogue |
| `data/script/citems.scr` | 1052 (0x41C) | none | CONFIRMED | Billing/premium item catalogue |
| `data/script/skillneedset.scr` | 4 | none | CONFIRMED | Skill prerequisite edges |
| `data/script/warstoneinfo.scr` | 40 (0x28) | none | CONFIRMED | War stone region info |
| `data/script/oblist.scr` | 12 | none | CONFIRMED | Object list (billing-filtered) |
| `data/script/statue.scr` | 36 (0x24) | none | CONFIRMED | Statue table |
| `data/script/setitemname.scr` | 36 (0x24) | none | CONFIRMED | Set-item name table |
| `data/script/Tutor.scr` | 1660 (0x67C) | none | CONFIRMED | Tutorial definition table |
| `data/script/viplevels.scr` | 92 (0x5C) | none | CONFIRMED | VIP level table |
| `data/script/itemscale.scr` | 8 | none | CONFIRMED | Item scale table |
| `data/script/itemeffect.scr` | 4 | none | CONFIRMED | Item effect table |
| `data/script/npc.scr` | 404 (0x194) | none | SAMPLE-VERIFIED | NPC description-text table (see §2.17; distinct from npcs.scr) |
| `data/script/quests.scr` | 3720 (0xE88), sparse | none | SAMPLE-VERIFIED | Quest template catalogue (see §2.17; 488 slots / 122 occupied) |
| `data/script/autoquestion_cl.scr` | 92 (0x5C) | none | SAMPLE-VERIFIED | Anti-bot arithmetic-quiz question pool (see §2.17) |
| `data/script/discript.sc` | 68 (0x44) | none | SAMPLE-VERIFIED | UI context-menu label table (see §2.17) |
| `data/script/products.scr` | 212 (0xD4) | none | SAMPLE-VERIFIED (stride) | Crafting recipe catalogue (id + CP949 name verified; body fields UNVERIFIED) |
| `data/script/mapsetting.scr` | 84 (0x54) | none | SAMPLE-VERIFIED | Per-zone map settings (52 records: zone id + CP949 name + XZ bounds; see §2.17.6) |
| `data/script/events.scr` | variable-length (indexed) | offset-table header | SAMPLE-VERIFIED (header only) | Event table — not a flat array; in-file offset table at +0x64 (see §2.17 note) |
| `data/script/helps.scr` | two-level hierarchical | per-page sub-entries | SAMPLE-VERIFIED (first page) | Help text — outer 16-byte page header + N × 48-byte sub-entries (see §2.17 note) |
| `data/item/items_extra.do` | 48 | none | CONFIRMED | Item extended / 3D-attachment data |
| `data/script/textcommand.do` | 52 | none | CONFIRMED | Chat command definitions |
| `data/script/emoticon.do` | 40 | none | CONFIRMED | Emoticon sprite-sheet definitions |
| `data/script/msginfo.do` | 128 | none | CONFIRMED | In-game popup message strings |
| `data/script/errorinfo.do` | 108 (0x6C) | none | CONFIRMED (stride only) | Error message strings |
| `data/script/monkma.do` and class-stance variants | 116 (0x74) | none | SAMPLE-VERIFIED (12/12 files) + CODE-CONFIRMED layout | Per-class stance skill-hotbar tables — the on-disk source of skill icon (srcX,srcY); **full 116-byte record layout in §3.5**; see also ui_manifests.md §2.7. The earlier 166-byte estimate was wrong: 9 of the 12 files divide by 116 exactly (e.g. musajung.do 34,916 B = 301×116) and the rest leave only a small ignored tail (12–60 B); none divides by 166. |

**Additional .scr files observed in the engine string table (loaders not traced):**
`nicktofame.scr`, `guildcrest.scr`, `letters.scr`, `chivalry.scr`,
`upgradeitems.scr`, `repair.scr`, `productrandname.scr`, `productcollect.scr`,
`system_control.scr`, `tiphelp.scr`, `playtime_reward.scr`.

---

### 2.2a Canonical primary stat order (CONFIRMED)

The five primary character stats appear throughout the runtime and item systems in one fixed,
consecutive order. This order is corroborated from the stat-aggregation pipeline (five
consecutive stat accessors, consecutive buff kinds, and consecutive runtime grant fields):

| Index | Stat | Korean game text |
|---|---|---|
| 0 | STR (Strength) | 힘 |
| 1 | AGI (Agility) | 민 |
| 2 | DEX (Dexterity) | — |
| 3 | INT (Intelligence) | 지 |
| 4 | CON (Constitution) | 체 |

The per-stat **base values are server-supplied at runtime** and are NOT stored in any client
`.scr` file. The `.scr` stat files documented below store **scaling curves and budgets** that
operate on those server-supplied bases, not the bases themselves:

- `userlevel.scr` — per-level scaling coefficients for a stat-tier formula.
- `userpoint.scr` — stat-point allocation budget (points granted per allocation step, and
  cumulative totals).
- `users.scr` — per-class stat-ratio inputs for a `(10 / A) × B` formula grid.

Where this canonical order maps onto the specific float/group positions inside each file is, in
most cases, still UNVERIFIED (the sample data does not vary enough per position to disambiguate).
Those mapping gaps are listed per file.

---

### 2.3 exp.scr — EXP per level (stride: 20 bytes, 300 records)

**Wave-7 blocker: RESOLVED.** The EXP thresholds are client-side.
**Sample verified:** 300 records confirmed.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Level index, 1-based (1..300) | Sequential; loader validates monotonic increment | CONFIRMED |
| +2 | 2 | u16 | Constant value 64 (0x0040) | Identical across all 300 records; semantic UNVERIFIED | CONFIRMED (value); UNVERIFIED (semantic) |
| +4 | 4 | u32 | Primary EXP required to reach the next level | L1=10; L50=112,284,408; plateaus at 1,999,557,415 from approximately L143 onward | CONFIRMED |
| +8 | 4 | u32 | Reserved or high-word extension | Zero in all 300 records | CONFIRMED (always zero) |
| +12 | 4 | u32 | Secondary EXP curve | Zero for L1–L73; L74=1; grows to approximately 4.17 billion at L240 then plateaus; non-monotone at very high levels | CONFIRMED (present); UNVERIFIED (semantic) |
| +16 | 4 | u32 | Tertiary EXP curve | Zero until approximately L186; L200=8; L300=263,880 | CONFIRMED (present); UNVERIFIED (semantic) |

The two non-primary columns (+12 and +16) are **not** cumulative sums of the primary column.
They are likely separate progression curves for secondary advancement systems (inner
cultivation, merit, or prestige). The loader validates sequential level ordering; a non-matching
record aborts the load.

**Open questions:**
- Semantic meaning of the constant 64 at +2 (max skill level? EXP-rate denominator? flags word?)
- Semantic identity of the secondary EXP curve at +12
- Semantic identity of the tertiary EXP curve at +16
- Whether field +8 is a true reserved pad or the high word of a future u64

---

### 2.4 userlevel.scr — Per-level stat-scaling coefficients (stride: 60 bytes, 300 records)

**Wave-7 blocker: RESOLVED. Record body columns RESOLVED. Sample verified: 300 records.**

**Important semantic note:** this file does NOT store per-stat base values (those are
server-supplied; see Section 2.2a). It stores **per-level scaling coefficients** consumed by a
stat-tier formula together with `users.scr` and the `(10 / A) × B` grid (Section 2.6). The
record is a 2-byte level index at +0 followed by a 58-byte body.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Level index, 1-based (map key, 1..300) | Sequential across all 300 records | CONFIRMED |
| +2 | 2 | u16 | Always zero (alignment pad) | Zero in all 300 records | CONFIRMED (value=0) |
| +4 | 2 | u16 | Tier step counter A | L1–L11=0; L12–L23=1; L24–L144=2; L145–L300=0 (resets) | CONFIRMED (value); UNVERIFIED (name) |
| +6 | 2 | u16 | Tier step counter B | Mirrors +4 in all 300 records (same value as +4 everywhere) | CONFIRMED (mirrors +4) |
| +8 | 2 | u16 | Divisor index C | L1–L11=0; L12=2; L24=3; L36–L144=4; L145–L300=0 (resets) | CONFIRMED (value); UNVERIFIED (name) |
| +10 | 2 | u16 | Always zero (alignment pad) | Zero in all 300 records | CONFIRMED (value=0) |
| +12 | 4 | f32 | Positive-scale group [0] | L1–L35=1.0; L36–L300=3.0 | CONFIRMED |
| +16 | 4 | f32 | Positive-scale group [1] | Matches group [0] in all 300 records | CONFIRMED |
| +20 | 4 | f32 | Positive-scale group [2] | Matches group [0] in all records | CONFIRMED |
| +24 | 4 | f32 | Positive-scale group [3] | Matches group [0] in all records | CONFIRMED |
| +28 | 4 | f32 | Negative-scale group [0] | L1–L35=−1.0; L36–L300=−2.0 | CONFIRMED |
| +32 | 4 | f32 | Negative-scale group [1] | Matches negative group [0] in all records | CONFIRMED |
| +36 | 4 | f32 | Negative-scale group [2] | Matches negative group [0] in all records | CONFIRMED |
| +40 | 4 | f32 | Negative-scale group [3] | Matches negative group [0] in all records | CONFIRMED |
| +44 | 4 | f32 | Reserved group [0] | Always 0.0 in all 300 records | CONFIRMED (value=0) |
| +48 | 4 | f32 | Reserved group [1] | Always 0.0 | CONFIRMED (value=0) |
| +52 | 4 | f32 | Reserved group [2] | Always 0.0 | CONFIRMED (value=0) |
| +56 | 4 | f32 | Reserved group [3] | Always 0.0 | CONFIRMED (value=0) |

**SPEC CORRECTION from prior version:** the prior spec described +4 and +8 each as a 4-byte `u32`
step field. The sample resolution shows these are 2-byte `u16` counters with a 2-byte zero pad
between/after them: counter A at +4, its mirror B at +6, divisor index C at +8, and a zero pad
at +10. The reserved group at +44 is four individual f32 slots (all zero), not an opaque 16-byte
blob.

**Transition summary:**

| Level range | Counter A (+4) | Mirror B (+6) | Divisor C (+8) | Positive floats | Negative floats |
|---|---|---|---|---|---|
| L1–L11 | 0 | 0 | 0 | 1.0 | −1.0 |
| L12–L23 | 1 | 1 | 2 | 1.0 | −1.0 |
| L24–L35 | 2 | 2 | 3 | 1.0 | −1.0 |
| L36–L144 | 2 | 2 | 4 | 3.0 | −2.0 |
| L145–L300 | 0 | 0 | 0 | 3.0 | −2.0 |

The four positive and four negative float slots are scaling coefficients applied to four stat
categories. Because all four positions carry the same value in every observed record (all 1.0 or
all 3.0; negative all −1.0 or all −2.0), the sample cannot distinguish whether the four positions
are four named primary stats or four compound combat categories (for example physical offense,
physical defense, magical offense, magical defense). The step/divisor counters at +4/+6/+8 act as
indices into the `(10 / A) × B` grid built from `users.scr` (Section 2.6): when divisor C = 0
(phases 1 and 5) the grid lookup is skipped via a divide-by-zero guard; when C = 2/3/4 the
formula yields scaling factors 15.0 / 10.0 / 7.5 respectively using the B input 3.0 from
`users.scr`.

**Open questions:**
- Named-stat (STR/AGI/DEX/INT/CON) mapping for each of the four float positions
- Whether the four groups are individual stats or compound combat categories
- Why the step/divisor counters reset to zero at L145 while the float values do not change

---

### 2.5 userpoint.scr — Stat-point allocation budget curve (stride: 32 bytes, 301 records)

**Wave-7 blocker: RESOLVED. Record body columns RESOLVED with LAYOUT CORRECTION.
Sample verified: 301 records, keys 0..300.**

**SPEC CORRECTION from prior version (important):** the prior spec described +8 as a `u16`
cumulative and +10 as a rare `u16` flag. The sample proves the cumulative total at +8 exceeds
65,535 at high keys (it reaches ~65,960 around key 285), so **the field at +8 is a `u32` spanning
+8..+11**. The apparent "flag" at +10 was simply the high byte of that `u32`. Reading +8 as a
`u32` reproduces the running sum of the per-step gain at +4 across all 301 records exactly. The
group-2 cumulative at +16 is likewise a `u32` (its values stay within `u16` range in the sample,
but the field width is 4 bytes to mirror group 1). The +18 / +14 alignment pads from the prior
spec are subsumed by the wider cumulative fields.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Allocation step index, 0-based (0..300) | Map key; sequential | CONFIRMED |
| +2 | 2 | u16 | Constant = 25 in all 301 records | Semantic UNVERIFIED (creation budget? per-level cap?) | CONFIRMED (value); UNVERIFIED (semantic) |
| +4 | 2 | u16 | Stat-group-1 gain this step | key=0→5; key=1–284→3; key=285–300→1000 | CONFIRMED |
| +6 | 2 | u16 | Always zero (alignment pad) | Zero in all 301 records | CONFIRMED (value=0) |
| +8 | 4 | u32 | Stat-group-1 cumulative total (running sum of +4) | Verified 301/301, including overflow past 65,535 | CONFIRMED |
| +12 | 2 | u16 | Stat-group-2 gain this step | key=0→7; key=9→2; key=300→300 | CONFIRMED |
| +14 | 2 | u16 | Always zero (alignment pad) | Zero in all 301 records | CONFIRMED (value=0) |
| +16 | 4 | u32 | Stat-group-2 cumulative total (running sum of +12) | Verified 301/301; reaches ~38,941 at key=300 | CONFIRMED |
| +20 | 2 | u16 | Secondary curve — low word | key=0–5→0; key=6→282; increments ~3/step; plateaus near key≈150 at 696 | CONFIRMED |
| +22 | 2 | u16 | Secondary curve — high word | key=0–5→0; key=6→20; grows slowly; plateaus near 56 by key≈150 | CONFIRMED |
| +24 | 4 | u32 | Tertiary value 1 | key=0–295→0; key=296→235,000; key=300→255,000; equals +28 in all checked records | CONFIRMED |
| +28 | 4 | u32 | Tertiary value 2 | Equals +24 in all checked records | CONFIRMED |

**Semantic interpretation:**

- **Stat-group-1** (gain at +4, cumulative at +8): points allocated per step for the first stat
  pool; the cumulative tracks total points available at a given allocation level. The very large
  gains at keys 285–300 (1000/step) are high-level stat-point bursts.
- **Stat-group-2** (gain at +12, cumulative at +16): the same pattern for a second pool with
  different scaling (starts at 7, smaller increments).
- **Constant 25 at +2** — UNVERIFIED. Candidate meanings: base stat points granted at character
  creation, an early-curve per-level allocation cap, or a legacy formula constant.
- **Secondary curve at +20/+22** — a 32-bit value pair encoding a secondary allocation curve
  (possibly martial energy: 내공 / 내력, or a third stat type). Zero for keys 0–5, begins at
  key 6, plateaus around key 150 with no further growth to key 300.
- **Tertiary values at +24/+28** — identical in all checked records; begin at key 296 and rise by
  5,000/step to 255,000 at key 300. Likely a high-level cap bonus or prestige reward pool.

**Open questions:**
- Which named stat (STR/AGI/DEX/INT/CON) maps to group-1 vs group-2
- Semantic of constant 25 at +2
- Whether the secondary curve at +20/+22 is a martial-energy (내공 / 내력) budget
- Whether the tertiary values at +24/+28 are a third stat pool or a max-cap schedule
- Meaning of the large gains at keys 285–300 (post-standard-level reward bursts?)

---

### 2.6 users.scr — Per-class stat-ratio grid (496-byte file, 4 × 124-byte class blocks)

**Wave-7 blocker: RESOLVED. Record body columns RESOLVED. Sample verified: all 4 class blocks.**

**Structure:** the file is four sequential 124-byte blocks, one per character class. Each block
is a 4-byte header followed by 30 × f32 values (120 bytes). Total: 4 × 124 = 496 bytes. These
floats are the inputs to a `(10 / A) × B` formula grid evaluated after the file loads (the
`(7.0, 24.0, 0.0)` triplets are `B` inputs; the `A` divisors come from a separate runtime table
not stored in this file).

**Per-block layout:**

| Offset within block | Size | Type | Field | Notes | Confidence |
|--------------------:|-----:|------|-------|-------|------------|
| +0 | 1 | u8 | Class ID (1..4) | Block 0→1, 1→2, 2→3, 3→4; see class-name table below | CONFIRMED |
| +1 | 1 | u8 | Constant 0x13 (19) in all 4 blocks | Semantic UNVERIFIED (count or version marker?) | CONFIRMED (value); UNVERIFIED (semantic) |
| +2 | 1 | u8 | Constant 0x43 (67) in all 4 blocks | Semantic UNVERIFIED | CONFIRMED (value); UNVERIFIED (semantic) |
| +3 | 1 | u8 | Always zero (header pad) | Zero in all 4 blocks | CONFIRMED (value=0) |
| +4 | 12 | 3×f32 | Stat weight triplet A = (3.0, 3.0, 3.0) | Identical across all 4 classes | CONFIRMED |
| +16 | 20 | 5×f32 | Zero group | All 0.0; all 4 classes | CONFIRMED (value=0) |
| +36 | 4 | f32 | Ratio A = 7.0 | Same in all 4 classes; `B`-input #1 | CONFIRMED |
| +40 | 4 | f32 | Ratio B = 24.0 | Same in all 4 classes; `B`-input #1 | CONFIRMED |
| +44 | 4 | f32 | Ratio C = 0.0 | Same in all 4 classes; `B`-input #1 | CONFIRMED |
| +48 | 12 | 3×f32 | Second (7.0, 24.0, 0.0) triplet | `B`-input #2; same in all 4 classes | CONFIRMED |
| +60 | 12 | 3×f32 | Third (7.0, 24.0, 0.0) triplet | `B`-input #3; same in all 4 classes | CONFIRMED |
| +72 | 20 | 5×f32 | Zero group | All 0.0; all 4 classes | CONFIRMED (value=0) |
| +92 | 32 | 8×f32 | Class-specific multiplier group | Mostly 1.0; per-class deviations (table below) | CONFIRMED |

**SPEC CORRECTION from prior version:** the prior spec listed the block header as a 1-byte class
ID at +0 followed by an opaque 3-byte pad. The two constant bytes at +1 (0x13 / 19) and +2
(0x43 / 67) are now documented explicitly; only +3 is a true zero pad. The prior spec also placed
the class-specific deviations on the wrong float positions; the corrected positions are below.

**Class names (CONFIRMED):**

| Class ID | Korean | Romanisation |
|---|---|---|
| 1 | 무사 (武士) | Musa (warrior) |
| 2 | 자객 (刺客) | Jagaek (assassin) |
| 3 | 도사 (道士) | Dosa (mystic/Taoist) |
| 4 | 승려 (僧侶) | Seungnyeo (monk) |

> Note: the `items.csv` class-restriction flags (Section 4) name the four equip-class lines as
> Musa / Jager / Dosa / Gungnyo. The `users.scr` class names above come from the stat-grid loader
> and use 승려 (monk) for class 4. The discrepancy in the class-4 label (monk vs. archer) between
> the two sources is an open question (see open list).

**Class-specific multiplier group (block offsets +92..+123, eight f32 at indices [22..29]):**

| Float index | Byte offset | Class 1 (무사) | Class 2 (자객) | Class 3 (도사) | Class 4 (승려) |
|---|---|---|---|---|---|
| [22] | +92 | 1.0 | 1.0 | 1.0 | 1.0 |
| [23] | +96 | 1.0 | **1.10** | 1.0 | 1.0 |
| [24] | +100 | 1.0 | 1.0 | **1.15** | **1.10** |
| [25] | +104 | 1.0 | 1.0 | **1.10** | **1.15** |
| [26] | +108 | 1.0 | 1.0 | 1.0 | 1.0 |
| [27] | +112 | 1.0 | 1.0 | 1.0 | 1.0 |
| [28] | +116 | 1.0 | 1.0 | 1.0 | 1.0 |
| [29] | +120 | 1.0 | 1.0 | 1.0 | 1.0 |

These eight multipliers are per-class stat-growth modifiers: class 2 has a 10% bonus at [23];
class 3 has 15% at [24] and 10% at [25]; class 4 has 10% at [24] and 15% at [25]. Positions
[26..29] are 1.0 across all classes. Which named stat maps to each of [22..29] is UNVERIFIED
because only 2–4 positions deviate from 1.0.

**Open questions:**
- Which named stat maps to each of the 8 multiplier positions [22..29]
- Semantic of the header constants 0x13 (19) and 0x43 (67)
- Class-4 label discrepancy: `users.scr` calls it 승려 (monk); `items.csv` calls the fourth
  equip class Gungnyo (궁녀, archer)
- The runtime source of the `A` divisor table feeding the `(10 / A) × B` grid (populated outside
  this file)

---

### 2.7 items.scr — Item catalogue (stride: 548 bytes + N × 8 trailing)

**Wave-7 blocker: RESOLVED (stride, category flags, trailing count). Body fields PARTIALLY
RESOLVED from the loader path only — NO sample file was available, so individual field offsets
between named anchors remain UNVERIFIED.**

Note: `items.csv` (Section 4) provides a much richer human-readable item catalogue. `items.scr`
is the binary runtime form. The two files share item IDs (the CSV→SCR correspondence is assumed
one-to-one in row order), but they were not cross-verified field by field. The item unique ID is
at SCR record offset **+0x34** (i32, little-endian), which corresponds to CSV column 1
(`item_id`).

**Main record (548 bytes = 0x224):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | ? | ? | First record field (likely a leading ID or header word) | Not individually resolved | UNVERIFIED |
| +0x34 (52) | 4 | i32 | Item unique ID | Maps to items.csv col 1 (`item_id`) | CONFIRMED |
| +0x4C (76) | 4 | u32 | Item category / class key | Used as the lookup key into the animation catalogue | CONFIRMED (presence); UNVERIFIED (name) |
| +0x98 (152) | 4 | i32 | Primary key field (item model or animation ID) | Passed into the catalogue dispatcher | CONFIRMED (presence); UNVERIFIED (name) |
| +0x9C (156) | 4 | i32 | Secondary key field | Passed into the catalogue dispatcher | CONFIRMED (presence); UNVERIFIED (name) |
| +0xD2 (210) | 1 | u8 | Type / category discriminator | A value of 14 bypasses an ID-modulo routing check | CONFIRMED (presence); UNVERIFIED (name) |
| +0xE5 (229) | 1 | u8 | Category flag 1 | If 1 → dispatch code 1 (weapon-type item) | CONFIRMED |
| +0xE6 (230) | 1 | u8 | Category flag 2 | If 1 → dispatch code 26 (armour-type item) | CONFIRMED |
| +0xE7 (231) | 1 | u8 | Category flag 3 | If 1 → dispatch code 11 (other category) | CONFIRMED |
| +0xE8 (232) | 1 | u8 | Category flag 4 | If 1 → dispatch code 16 (other category) | CONFIRMED |
| +0x220 (544) | 1 | u8 | Trailing entry count N | Controls the variable upgrade section (N × 8 bytes) | CONFIRMED |
| +0x221 (545) | 3 | — | Alignment padding to the 548-byte stride | Derived from record size | CONFIRMED (derived) |
| All other offsets | — | ? | Remaining item fields (name/description/stat grants) | No sample; not resolved | UNVERIFIED |

The four flag bytes at +0xE5..+0xE8 select an item category for dispatch into the animation
catalogue (code 0 when all four are zero). Exactly one flag is set for categorised items. The
dispatcher composes a registration key from the type discriminator at +0xD2, the dispatch code,
and a base value, then registers the item's animation reference; the registration confirms that
`items.scr` populates the engine's item-animation catalogue.

The runtime item object is 0x228 = 552 bytes (the 548-byte disk record copied verbatim, plus a
4-byte pointer to the expanded upgrade-entry list).

**Trailing upgrade-effect entries (N × 8 bytes on disk, only present when N > 0):**

Each 8-byte on-disk entry expands to a 12-byte runtime entry:

| On-disk bytes | Size | Type | Runtime field | Confidence |
|---|---|---|---|---|
| [0..1] | 2 | u16 | Effect type code | CONFIRMED |
| [2..3] | 2 | i16 | Effect magnitude (sign-extended to i32 at runtime) | CONFIRMED |
| [4..5] | 2 | u16 | Level threshold (level at which the upgrade activates) | CONFIRMED |
| [6] | 1 | u8 | Upgrade sub-byte | CONFIRMED |
| [7] | 1 | — | On-disk trailing byte (unused / pad) | UNVERIFIED |

The observed maximum for N in the loader is ≤ 19.

**Open questions:**
- Width and encoding of the leading field at +0x00 (and whether +0x34 is the only ID field)
- Field layout in the large unresolved regions (+0x00..+0x33, +0x35..+0x4B, +0x4D..+0x97,
  +0x9D..+0xD1, +0xE9..+0x21F)
- Where the item name and description text reside in the 548-byte record (CP949 expected, by
  analogy with every other catalogue file, but unconfirmed without a sample)
- How the on-disk record encodes the stat-grant values that populate the runtime item object
  (STR/AGI/DEX/INT/CON/HP/MP)

---

### 2.8 skills.scr — Skill catalogue (stride: 1504 bytes, ~194 real records)

**Wave-7 blocker: RESOLVED (stride). Record body field survey SUBSTANTIALLY RESOLVED.
Sample verified: 1504-byte stride; 194 real skill definitions detected (the remaining slots in
the sample are zero-padded or hold misaligned/garbage data and should be filtered out).**

Each record defines one skill across all of its levels (it is not one record per skill level).
A real record is distinguished from garbage by a plausible skill ID (< 10,000,000) and a
plausible category index (< 300). String fields are CP949, null-terminated.

**Main record (1504 bytes = 0x5E0):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Skill ID (map key) | Examples: 11, 12, 13, 21…, 300131–300136 | CONFIRMED |
| +4 | 4 | u32 | Skill category index | Class-skill values 150–153 (one per class); 154–158 mixed/universal; other ranges for combo/chain tiers (see table below) | CONFIRMED |
| +8 | ≤24 | char[] | Skill name (CP949, null-terminated) | Longest real name 17 bytes; buffer always zero by +32; buffer width is 24 or 32 (UNVERIFIED which) | CONFIRMED (name at +8); CONFIRMED (buffer ends ≤ +31) |
| +32..+515 | 484 | mixed | Integer/sparse field region | Mostly zero in real records; only +260 and +516 resolved within this region | PARTIALLY RESOLVED |
| +260 | 4 | u32 | Constant 0x30000000 in all real records | Likely an internal type/version marker | CONFIRMED (value); UNVERIFIED (meaning) |
| +516 | 4 | u32 | Class flag = (class ID << 16) | Class 1=0x00010000, 2=0x00020000, 3=0x00030000, 4=0x00040000 | CONFIRMED |
| +520 | 1 | u8 | Skill type / tier byte | 0=passive/unknown, 1=movement-base, 2=tier-3, 3=tier-3/5 chain, 4=tier-2, 5=tier-5 chain, 6=tier-6 chain (see table below) | CONFIRMED |
| +521 | ≤512 | char[] | Long description text (CP949, null-terminated) | Confirmed for all checked records; descriptions up to 52+ bytes | CONFIRMED |
| +1032 | ≤8+ | char[] | Short description / action label (CP949) | Text begins shortly after +1032 (≈+1033); buffer width UNVERIFIED | CONFIRMED (presence); PARTIALLY CONFIRMED (exact offset) |
| +1072 | 4 | u32 | Constant 0x00003000 (12288) in all real records | Unresolved | CONFIRMED (value); UNVERIFIED (meaning) |
| +1116 | 4 | u32 | Skill chain reference [0] | Decimal-digit composite (e.g. 141100041 for skill 11, slot 0); encoding schema UNVERIFIED | CONFIRMED (presence); UNVERIFIED (encoding) |
| +1120 | 4 | u32 | Skill chain reference [1] | Same pattern | CONFIRMED (presence) |
| +1124 | 4 | u32 | Skill chain reference [2] | Same pattern | CONFIRMED (presence) |
| +1128 | 4 | u32 | Skill chain reference [3] | Same pattern | CONFIRMED (presence) |
| +1132 | 4 | u32 | Skill chain reference [4] | Same pattern | CONFIRMED (presence) |
| +1136 | 4 | u32 | Skill chain reference [5] | Different decimal prefix (3xxxxxxxx vs 1xxxxxxxx), e.g. 341100111 | CONFIRMED (presence); UNVERIFIED (encoding) |
| +1176 | 4 | f32 | Constant 1.0 in all real records | Meaning UNVERIFIED | CONFIRMED (value); UNVERIFIED (meaning) |
| +1180 | 4 | u32 | Skill chain reference [6] | Third prefix type (8xxxxxxxx), e.g. 841100111 | CONFIRMED (presence) |
| +1280 | 4 | u32 | Prerequisite / parent skill ID | 0 for base skills; e.g. skill 13 → 11 | CONFIRMED (pattern) |
| +1292 | 2 | u16 | Skill-point (SP) cost to learn | Per-tier values 4, 8, 12 | CONFIRMED (pattern) |
| +1294 | 2 | u16 | Next-in-chain reference or 0 | e.g. skill 11 → 13; some records hold a composite ID | CONFIRMED (pattern); UNVERIFIED (composite encoding) |
| +1296 | 4 | u32 | Second chain / upgrade-path reference | Single ID or composite chain ID | CONFIRMED (presence); UNVERIFIED (encoding) |
| +1300 | 4 | u32 | Third chain / upgrade-path reference | Same as +1296 | CONFIRMED (presence); UNVERIFIED (encoding) |
| +1304 | 2 | u16 | Motion / animation index A | Increments 20, 46, 72 (by 26) across skill tiers | CONFIRMED (pattern); UNVERIFIED (name) |
| +1306 | 2 | u16 | Constant 7 in all checked records | Meaning UNVERIFIED | CONFIRMED (value); UNVERIFIED (meaning) |
| +1328 | 4 | u32 | Constant 0x00010000 (65536) in all real records | Meaning UNVERIFIED | CONFIRMED (value); UNVERIFIED (meaning) |
| +1372 | 4 | u32 | Cooldown (seconds) | 8–16 for movement skills; 0 for passive skills | CONFIRMED (plausible range) |
| +1412 | 4 | f32 | Range (game units) | 30.0–40.0 for movement skills; 0.0 for passive skills | CONFIRMED (plausible range) |
| +1496 | 4 | u32 | Tail field | Sparse | UNVERIFIED |
| +1500 | 4 | — | Tail bytes incl. trailing count byte | A trailing-count byte exists in the loader but is 0 for the sampled real records (no trailing 8-byte sub-entries observed) | PARTIALLY CONFIRMED |

**SPEC CORRECTION from prior version:** the prior spec listed the entire 1500-byte body as
UNVERIFIED with only a trailing count byte. The body is now substantially mapped (name, category,
class flag, type byte, descriptions, SP cost, cooldown, range, chain references). The trailing
`N × 8` sub-entry mechanism is the same shape as items.scr. Most records carry zero trailing
sub-entries, but at least some do not: **the campaign-4 harness confirms `skills.scr` is NOT a
flat array.** The real file size does not divide evenly by 1504 — the 1504-byte figure is the
**fixed part only**, and a small number of 8-byte trailing sub-entries (on the order of dozens
across the whole file) account for the remainder. The correct record model is therefore
`stride = 1504 fixed + N × 8 trailing` per record, identical in shape to `items.scr`, where `N`
is 0 for most records. A parser must read the per-record trailing count and advance by
`1504 + N × 8`; it cannot assume a uniform 1504-byte stride.

**Skill category index (+4):**

| Value | Meaning |
|---|---|
| 150 | Class 1 (무사 / warrior) skills |
| 151 | Class 2 (자객 / assassin) skills |
| 152 | Class 3 (도사 / Taoist) skills |
| 153 | Class 4 (승려 / monk) skills |
| 154–158 | Mixed / universal (e.g. 심법, 환마술) |
| 80–106 | Higher-tier combination skill sub-types |
| 47–51 | Advanced skill-chain sub-types |

**Skill type / tier byte (+520):**

| Value | Examples (Korean) | Interpretation |
|---|---|---|
| 0x00 | 환마술, passive IDs | Passive / no-movement / unknown |
| 0x01 | 경공 (lightfoot), 심법 (simbeop) | Tier 1 / base skill |
| 0x02 | 비선행공 | Tier 3 skill |
| 0x03 | (3rd/5th chain) | Tier 3/5 chain |
| 0x04 | 초상비 | Tier 2 skill |
| 0x05 | (5th chain) | Tier 5 chain |
| 0x06 | (6th chain) | Tier 6 chain |

The chain-reference fields at +1116..+1136 and +1180 encode multiple sub-fields in **decimal
digit groups** (not packed binary). The leading digit varies by class and chain type (1xx for
base class chains, 3xx for combo chains, 8xx for an alternate chain set). The full decode schema
is UNVERIFIED.

**Open questions:**
- Exact name-buffer width at +8 (24 or 32 bytes)
- Field layout of the integer region between +32 and +515 (only +260 and +516 resolved)
- Short-description field boundary and buffer width near +1032/+1033
- Whether +1292 is strictly "SP cost to learn" and how it disambiguates from max level
- Full decimal-composite encoding of the chain references (+1116..+1136, +1180, +1294)
- Whether +1296/+1300 are prerequisite or successor chain IDs
- Meaning of motion index at +1304 and the constant 7 at +1306
- Tail fields between +1496 and +1503

---

### 2.9 mobs.scr — Mob catalogue (stride: 488 bytes, 3,997 records)

**Wave-7 blocker: RESOLVED. Sample verified: 3,997 records.**

**SPEC CORRECTION from prior version:** the field previously described as an `i64` at +248 is
two separate fields: an `i32` mob-level at +244 and a `u32` spawn timer at +248.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Mob ID (map key) | First 10 records: 11, 12, 21, 22, 31 … (sequential by 10 for low IDs); boss range 14,000+ | CONFIRMED |
| +2 | ≤17 | char[] | Primary mob name | CP949/EUC-KR, null-terminated; buffer ≤17 bytes (16 chars + NUL); longest observed null at offset +18 | CONFIRMED |
| +19 | ≤19 | char[] | Secondary mob name (category or region label?) | CP949, null-terminated; buffer ≤19 bytes; common substring shared across many mobs; semantic UNVERIFIED | CONFIRMED (presence); UNVERIFIED (semantic) |
| +2..+51 | 50 | mixed | Additional name/tag fields | Region between primary name end and +52; mixed zero and text; UNVERIFIED | UNVERIFIED |
| +52 | 2 | u16 | Unknown field | Observed value 379 (0x017B) in first record; other records UNVERIFIED | UNVERIFIED |
| +60 | 4 | f32 | Constant 3.0 in all observed records | Value/semantic UNVERIFIED | CONFIRMED (value); UNVERIFIED (name) |
| +188 | 4 | f32 | Constant 1.0 in all observed records | UNVERIFIED | UNVERIFIED |
| +244 | 4 | i32 | Mob level | −1 = not set; 0 = trivial; boss range 36..46 for ID range 14000–14009; regular mobs 87..249 for high-ID range | CONFIRMED (boss validation path) |
| +248 | 4 | u32 | Spawn timer in seconds | Range 33..41,006 in sample; boss default ≈ 40 s | CONFIRMED (plausible range) |
| +252 | ? | ? | Unknown fields | UNVERIFIED | UNVERIFIED |
| +272 | 24 | 6×f32 | All 1.0 | Observed in first record | CONFIRMED (value); UNVERIFIED (name) |
| +296 | 4 | f32 | Variance/spread low | 0.95 in first record | UNVERIFIED |
| +300 | 4 | f32 | Variance/spread high | 1.05 in first record | UNVERIFIED |
| +304 | 4 | f32 | Variance low (second pair?) | 0.95 | UNVERIFIED |
| +308 | 4 | f32 | Variance high (second pair?) | 1.05 | UNVERIFIED |
| +316 | 4 | f32 | Scale factor — normal vs. boss differs | 0.19 for normal mobs; 3.09 for bosses | CONFIRMED (boss differentiation) |
| +320 | 4 | f32 | Scale factor B — normal vs. boss differs | 0.23 for normal; 3.71 for bosses | CONFIRMED (boss differentiation) |
| +324 | 1 | u8 | Mob type | 0=normal (3,749 records); 2–10=sub-types (106 records total); 11=boss/elite (125 records); 12=special (2 records) | CONFIRMED |
| +325 | 3 | ? | Fields adjacent to type byte | UNVERIFIED | UNVERIFIED |
| +328 | 4 | f32 | Float — normal=40.0, boss=35.0 | UNVERIFIED name | UNVERIFIED |
| +332 | 4 | f32 | Float — normal=80.0, boss=30.0 | UNVERIFIED name | UNVERIFIED |
| +396 | 4 | f32 | Float — 0.0 for normal; 40.0 for bosses only | UNVERIFIED | UNVERIFIED |
| +400 | 4 | f32 | Constant 2.10 across normal and boss | UNVERIFIED | UNVERIFIED |
| +444 | 4 | f32 | Float — normal=50.0, boss=360.0 | Possibly max HP scale factor | UNVERIFIED |
| +448 | 4 | f32 | Constant 2.80 | UNVERIFIED | UNVERIFIED |
| +452 | 4 | f32 | Float — normal=8.09, boss=21.67 | UNVERIFIED | UNVERIFIED |
| Remaining bytes | ? | ? | Fields from +456 to end of 488-byte record | UNVERIFIED | UNVERIFIED |

Boss-type mobs (type byte = 11) are inserted into a separate runtime index in addition to the
main map. Mobs with mob-level = −1 may be special scripted or data-anomaly entries.

---

### 2.10 npcs.scr — NPC catalogue (stride: 1916 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 2 | u16 | NPC ID (map key) | CONFIRMED |
| +2 | 1914 | ? | Name(s) and other fields | UNVERIFIED |

The loader allocates a 957-element `u16` array per record on the stack, suggesting that the
record body contains text encoded as 2-byte characters (UCS-2 or CP949/EUC-KR). The exact
character encoding and field layout are UNVERIFIED.

---

### 2.11 citems.scr — Billing/premium item catalogue (stride: 1052 bytes, 512 records)

**Sample verified: 512 records (375 normal items + 137 billing/premium items).**

Billing filter: records with item ID >= 100,000 are only inserted into the runtime map when the
billing-system flag is active. Records with item ID < 100,000 are always loaded.

String fields are CP949/EUC-KR encoded, null-terminated, zero-padded. Description blocks that
have no text often contain a single `#` byte (0x23) as a placeholder sentinel.

**Main record (1052 bytes = 0x41C):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Item ID (map key) | < 100,000 = normal; >= 100,000 = billing/premium | CONFIRMED |
| +4 | ≤64 | char[] | Item name (CP949) | Null-terminated; max observed name ≈ 22 bytes | CONFIRMED |
| +52 | 4 | u32 | Flag word | Sparse; value 0x04000000 on some items; others 0 | UNVERIFIED |
| +56 | 2 | u16 | Cost / point value | Range 100–8,350; consistent with in-game point costs | UNVERIFIED (name) |
| +58 | 2 | — | Padding | UNVERIFIED |
| +60 | 1 | u8 | Category/type byte | Observed values 4, 5, 6; most records = 6 | CONFIRMED (presence); UNVERIFIED (name) |
| +72 | 4 | u32 | Handle/pool reference | Incrementing value across sequential records; likely a runtime allocation tag | UNVERIFIED |
| +76 | 4 | u32 | Stack quantity / lot size | Values 1, 5, 10, 12; consistent with typical MMO stack sizes | UNVERIFIED |
| +77..+227 | 151 | ? | Fields between +76 and description blocks | UNVERIFIED | UNVERIFIED |
| +228 | 81 | char[81] | Description block 0 (CP949) | Text present in most records | CONFIRMED |
| +309 | 81 | char[81] | Description block 1 (CP949) | Text in many records | CONFIRMED |
| +390 | 81 | char[81] | Description block 2 (CP949) | Text in some records | CONFIRMED |
| +471 | 81 | char[81] | Description block 3 (CP949) | Text in some records | CONFIRMED |
| +552 | 81 | char[81] | Description block 4 (CP949) | Often contains `#` placeholder only | CONFIRMED (structure) |
| +633 | 81 | char[81] | Description block 5 (CP949) | Often `#` or empty | CONFIRMED (structure) |
| +714 | 81 | char[81] | Description block 6 (CP949) | Largely empty/placeholder in sample | CONFIRMED (structure); UNVERIFIED (usage) |
| +795 | 81 | char[81] | Description block 7 (CP949) | Same | CONFIRMED (structure) |
| +876 | 81 | char[81] | Description block 8 (CP949) | Same | CONFIRMED (structure) |
| +957 | 81 | char[81] | Description block 9 (CP949) | Max 17 non-null bytes observed | CONFIRMED (structure) |
| +1038 | 2 | ? | Unanalysed field | UNVERIFIED | UNVERIFIED |
| +1040 | 4 | u32 | Tail field 0 | Values 0 or 1; many records = 1 | UNVERIFIED |
| +1044 | 4 | u32 | Tail field 1 | Values 0 or 2; many records = 2 | UNVERIFIED |
| +1048 | 4 | u32 | Always zero | All checked records | CONFIRMED (value=0) |

Ten × 81-byte description blocks cover offsets +228 through +1037 (10 × 81 = 810 bytes).
Blocks 6–9 are largely empty in the sample and may represent unused language slots.

**Open questions:**
- Names and semantics of fields +4 through +51
- Meaning of the flag at +52
- Semantics of cost field at +56 and category byte at +60
- Semantics of pool-reference at +72 and lot-size at +76
- Purpose of the ten 81-byte description blocks (tooltip sections? multiple language slots?)
- Whether the `#` sentinel is the engine's null-description marker
- Purpose of tail fields at +1040 and +1044

---

### 2.12 skillneedset.scr — Skill prerequisite edges (stride: 4 bytes, 22 records)

**Sample verified: 22 records.**

Each record encodes one directed prerequisite edge in the skill tree.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Prerequisite skill ID | Must be unlocked before the dependent skill | CONFIRMED |
| +2 | 2 | u16 | Dependent skill ID | Cannot be unlocked until the prerequisite is held | CONFIRMED |

Sample edges form a directed prerequisite tree in the skill ID range 300–334.

---

### 2.13 warstoneinfo.scr — War stone region data (stride: 40 bytes, 1 record in sample)

**Sample verified: 1 record.** A diagnostic log emits the record as `id, name, field_04, field_32, field_36`.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Record ID (map key) | Sample record = 1 | CONFIRMED |
| +4 | 4 | u32 | field_04 | Sample = 1; semantic UNVERIFIED | CONFIRMED (present); UNVERIFIED (name) |
| +8 | 4 | u32 | field_08 | Sample = 107; UNVERIFIED | UNVERIFIED |
| +12 | 20 | char[20] | Name string (CP949) | Null-terminated; sample = 6-byte Korean text | CONFIRMED |
| +32 | 4 | u32 | field_32 | Sample = 156; semantic UNVERIFIED | CONFIRMED (present); UNVERIFIED (name) |
| +36 | 4 | u32 | field_36 | Sample = 249; semantic UNVERIFIED | CONFIRMED (present); UNVERIFIED (name) |

---

### 2.14 oblist.scr — Object list (stride: 12 bytes, 1 record in sample)

**Sample verified: 1 record.**

Records are only inserted into the runtime map when a region/server billing-state flag matches
the value stored in the third field.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | field_00 | Sample = 1; possibly entry count or object ID | UNVERIFIED |
| +4 | 4 | u32 | field_04 | Sample = 1; possibly region code | UNVERIFIED |
| +8 | 4 | u32 | Billing-state filter key | Must match the server's region flag to insert record | CONFIRMED (filter role) |

---

### 2.15 statue.scr, setitemname.scr — Stride-confirmed, no field analysis

**Confirmed stride:** 36 bytes (0x24). No sample data available for field-level analysis.

`setitemname.scr` contains at least one null-terminated string field inside the 36 bytes
(confirmed by the loader calling a string-length function on the record buffer).

`Tutor.scr` (stride 1660 bytes, 0x67C) has a debug log format of `TUTOR ID[%d] DESC[%s]`,
indicating a `u32` ID at +0 followed by a description string beginning at or near +4.

---

### 2.16 Additional stride-confirmed files (no field analysis)

These strides are confirmed; no sample bytes available for field-level breakdown.

| File | Stride | Notes |
|---|---|---|
| `data/script/viplevels.scr` | 92 bytes (0x5C) | VIP level table |
| `data/script/itemscale.scr` | 8 bytes | Item scale table |
| `data/script/itemeffect.scr` | 4 bytes | Item effect table |
| `data/script/errorinfo.do` | 108 bytes (0x6C) | Error message strings |
| Per-class stance `.do` files (`monkma.do`, `monksa.do`, etc.) | 116 bytes (0x74) — corrected from the earlier 166-byte estimate; SAMPLE-VERIFIED 12/12 | Per-class skill-hotbar tables incl. skill icon (srcX,srcY) at +0x18/+0x1C; full layout in §3.5 (ui_manifests.md §2.7) |

---

### 2.17 Quest-data family (quests.scr, autoquestion_cl.scr, npc.scr, discript.sc)

**Sample verified (2026-06-13) via VFS byte inspection (no IDA).** These four files back the
quest / NPC-dialog subsystem; the behavioural model that consumes them is in
`specs/quests.md`. The behavioural roles are summarised there; the byte layouts are here.

All strings CP949 / EUC-KR, null-terminated. All integers little-endian.

#### 2.17.1 quests.scr — quest template catalogue (stride: 3720 bytes, 488 slots, 122 occupied)

**SPEC CORRECTION:** the §2.2 inventory previously listed `quests.scr` as UNVERIFIED, and a
prior static inference put the stride at 4960 bytes (~366 quests). The real sample measures a
**3720-byte (0xE88) stride, 488 slots, 122 occupied** (366 empty). This is a **sparse** flat
array, not index-keyed: a slot is empty when its leading `u16` quest id is 0, and the runtime
keys a map on the non-zero quest ids (range 1..617).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x000 | 2 | u16 | Quest id (map key) | 1..617; 0 = empty slot | SAMPLE-VERIFIED |
| +0x002 | ≤32 | char[] | Quest name (CP949) | Null-terminated; in a ~62-byte name buffer ending by ~+0x3F | SAMPLE-VERIFIED |
| +0x040 | 6 | u8[6] | Step / objective code bytes | Three distinct patterns (e.g. `05 06 07 08 09 0A`); 100% occupancy; index/template meaning UNVERIFIED | SAMPLE-VERIFIED (presence); UNVERIFIED (semantic) |
| +0x046 | 14 | — | Zero pad | Zero in all occupied records | SAMPLE-VERIFIED (value=0) |
| +0x054 | 4 | u32 | Chain/category reference A | Decimal-digit composite `730 R QQQQQ 04` (R = region/category, QQQQQ = quest id) | SAMPLE-VERIFIED (pattern) |
| +0x058 | 4 | u32 | Chain/category reference B | Always `A + 1` (the `…05` sibling) | SAMPLE-VERIFIED |
| +0x064 | 4 | u32 | Quest type or step count | Constant value 2 across all 122 occupied records; semantic UNVERIFIED | SAMPLE-VERIFIED (value); UNVERIFIED (semantic) |
| +0x068 onward | var | char[] | Objective / step description text (CP949) | One or more strings; occupancy tapers across later steps | SAMPLE-VERIFIED (presence) |
| +0x0E4, +0x1D4, +0x248, +0x2C4, +0x338, … +0xE78 | 4 ea | u32 | Sub-section markers (value 48 / 0x30) | 100% occupancy at regular spacing (≈0xF0=240 B for the first group, then ≈0x7C=124 B); implies embedded fixed-stride objective/reward sub-records | SAMPLE-VERIFIED (value=48) |

**Structural note.** The recurring `u32 = 48` at fixed spacing is the strongest structural
signal in the record: it is consistent with embedded **48-byte sub-records** (quest steps,
each likely carrying objective NPC id, kill/collect targets, reward item id and EXP). The
sub-record contents are not yet field-decoded. The composite at +0x054/+0x058 mirrors the
decimal-composite chain-reference scheme seen in `skills.scr` (§2.8).

#### 2.17.2 autoquestion_cl.scr — anti-bot quiz pool (stride: 92 bytes, 1300 records)

Sequential `u32` question id at +0; the file stores the **display text** of simple
Korean-language arithmetic (multiplication) questions for human-verification. The numeric
answer is checked server-side and is not stored on the client.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | Question id (map key) | Sequential 1..1300 | SAMPLE-VERIFIED |
| +0x04 | ≤28 | char[] | Question text (CP949) | Null-terminated; e.g. "(number) 과 (number)(을)를 곱한 결과는?" | SAMPLE-VERIFIED |
| ~+0x20 | var | char[] | Answer-hint text (CP949) | "정답을 숫자로 입력해주세요" ("enter the answer as a number") | SAMPLE-VERIFIED |
| tail | — | — | Zero pad to the 92-byte stride | — | SAMPLE-VERIFIED |

#### 2.17.3 npc.scr — NPC description-text table (stride: 404 bytes, 2510 records)

**SPEC CORRECTION:** §2.2 previously listed `npc.scr` as UNVERIFIED. The sample confirms a
**404-byte (0x194) stride, 2510 records**, sequential `u32` id at +0 mirrored at +4. This file
holds the multi-paragraph **NPC class/archetype description text** (and is the dialogue-body
store referenced by the quest dialogs); it is distinct from the `npcs.scr` catalogue (stride
1916, §2.10). The key linking the two id spaces is UNVERIFIED.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x000 | 4 | u32 | NPC class/descriptor id (map key) | Sequential 1..2510 | SAMPLE-VERIFIED |
| +0x004 | 4 | u32 | Id mirror | Equals +0x000 in all records (possible second lookup key) | SAMPLE-VERIFIED |
| +0x008 | 8 | — | Reserved | Zero in observed records | SAMPLE-VERIFIED (value=0) |
| +0x014 | ≤36 | char[] | Description paragraph 0 (CP949) | First archetype paragraph | SAMPLE-VERIFIED |
| +0x050 | ≤28 | char[] | Description paragraph 1 (CP949) | Second paragraph | SAMPLE-VERIFIED |
| +0x090 | ≤28 | char[] | Description paragraph 2 (CP949) | Third paragraph | SAMPLE-VERIFIED |
| +0x0D0 | 4 | u32 | Sub-section marker (value 48 / 0x30) | 100% occupancy | SAMPLE-VERIFIED (value=48) |
| +0x110 | 4 | u32 | Sub-section marker (value 48) | 100% occupancy | SAMPLE-VERIFIED (value=48) |
| +0x150 | 4 | u32 | Sub-section marker (value 48) | 100% occupancy | SAMPLE-VERIFIED (value=48) |
| other | — | — | Mostly zero / sparse | — | UNVERIFIED |

The three `48`-markers at +0xD0 / +0x110 / +0x150 (spacing 0x40 = 64) mirror the `quests.scr`
sub-record pattern: each 404-byte record carries three 64-byte description "pages".

#### 2.17.4 discript.sc — UI context-menu labels (stride: 68 bytes, 33 records)

A small table of right-click / context-menu item labels, referenced from quest and
interaction UI menus (party/guild actions, currency names, window toggles, school actions).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | Menu item id (map key) | e.g. 8..12 (party), 70..73 (currency), 0xF2A0+ (window toggles), 0x1194+ (school) | SAMPLE-VERIFIED |
| +0x04 | 4 | u32 | Category / type code | e.g. 3 = party action, 67 = shop item, 70/102 = hotkey/function-key labels | SAMPLE-VERIFIED |
| +0x08 | ≤24 | char[] | Label text (CP949) | e.g. "무리 권유" (party invite), "협행창" (quest window), "행낭창" (inventory) | SAMPLE-VERIFIED |
| +0x1C | ≤20 | char[] | Secondary text / ASCII shortcut | For hotkey items: "(F)", "(I)", … | SAMPLE-VERIFIED |
| ~+0x26 | 4 | u32 | Sub-section marker (value 48) | Consistent with the family's 48-marker convention | SAMPLE-VERIFIED (value=48) |
| tail | — | — | Zero pad to the 68-byte stride | — | SAMPLE-VERIFIED |

#### 2.17.5 Related quest-data files (stride/structure only, this pass)

| File | Size / structure | Notes |
|---|---|---|
| `products.scr` | stride 212 (0xD4), 9092 records | `u32` recipe id @ +0, CP949 name @ +4; 188-byte body (ingredients/output) UNVERIFIED |
| `events.scr` | variable-length, indexed | File header with a `u16` event-category count and an in-file `u32` offset table at +0x64..+0xA7 pointing to ~15 variable-length event-record bodies; record content not decoded |
| `helps.scr` | two-level hierarchical | Outer 16-byte page header (`page_id`, `section_id`, reserved, `entry_count`) + `entry_count` × 48-byte sub-entries; first page sample-verified, full page-walk UNVERIFIED |
| `tiphelp.scr` | variable-length | Loading-screen tip records; `u32` body-size + tip-count header then CP949 tip text; not flat-stride |
| `dashs.scr` / `minds.scr` | stride 796 / 808 | Movement-skill / inner-cultivation skill reference tables; `u32` id @ +0, CP949 name; 7 / 9 records |

#### 2.17.6 mapsetting.scr — per-zone map settings (stride: 84 bytes, 52 records)

**SPEC CORRECTION:** §2.2 previously listed `mapsetting.scr` as UNVERIFIED. The campaign-4
harness confirms an **84-byte (0x54) stride with 52 records** by exact divisibility
(`4,368 / 84 = 52`, exact). This table holds one record per playable zone: a zone id, a CP949
zone name, and the zone's XZ world bounds. The same 84-byte stride is already assumed by the
minimap scan tooling described in `misc_data.md`.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Zone id (map key) | One record per zone | SAMPLE-VERIFIED |
| +4 | ? | char[] | Zone name (CP949, null-terminated) | Inline name buffer within the 84-byte record | SAMPLE-VERIFIED (presence) |
| body | 16 | 4×f32 | XZ world bounds (min/max along X and Z) | The zone's extent; exact float order within the body is UNVERIFIED | SAMPLE-VERIFIED (presence); UNVERIFIED (exact offsets) |
| tail | — | — | Remaining/zeroed fields to the 84-byte stride | — | UNVERIFIED |

- **Record count source:** `file_size / 84` (no file header, flat array — the common §2.1 pattern).
- The byte offsets of the name buffer and the four bound floats inside the 84-byte body are not
  individually pinned in this pass; only the zone-id-at-+0, the CP949 name, and the presence of
  the XZ bounds are sample-verified. An engineer should treat the inner-offset layout as UNVERIFIED.

---

---

## Section 3 — .do files (detailed layouts)

### 3.1 textcommand.do — Chat command definitions (stride: 52 bytes, 28 records)

**Sample verified: all 28 records decoded and confirmed.**

All `.do` loaders share the common structural pattern of Section 2.1. BST is keyed on the
first `u32` field. Name-lookup also supports a secondary string search on the `name_buf` field.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Command ID (BST key) | Integer dispatch key; observed ranges: 1–11 (panel/toggle), 12–17 (social), 18–29 (emote/action), 50–52 (guild) | CONFIRMED |
| +4 | 36 | char[36] | Command name (CP949) | Null-terminated; `/` is first character (e.g. `/무리`, `/전음`); remainder zero-padded (with debug 0xCC fill after the NUL) | CONFIRMED |
| +40 | 4 | — | Alignment padding | Contains debug 0xCC fill; no data field | CONFIRMED (pad) |
| +44 | 1 | u8 | Argument flag | 0 = command takes no target argument; 1 = command takes a player-name argument | CONFIRMED (value pattern); UNVERIFIED (exact semantic) |
| +45 | 3 | — | Alignment padding | Debug 0xCC fill | CONFIRMED (pad) |
| +48 | 4 | u32 | Sub-command ID | Non-zero for emote/action commands (encodes the emote action code); zero for commands where argument flag = 1 | CONFIRMED (value pattern); UNVERIFIED (exact semantic) |

**Sample command table (selected records):**

| Command ID | Name | Arg flag | Sub ID | Translation |
|---|---|---|---|---|
| 1 | /무리 | 0 | 0 | party |
| 4 | /전음 | 0 | 0 | whisper |
| 7 | /노점개설 | 0 | 0 | open stall |
| 8 | /무리초대 | 1 | 0 | invite to party |
| 9 | /무리추방 | 1 | 0 | kick from party |
| 13 | /비무신청 | 1 | 0 | duel request |
| 14 | /친구등록 | 1 | 0 | add friend |
| 18 | /줍기 | 0 | 2 | pick up |
| 19 | /일어서기 | 0 | 14 | stand up |
| 20 | /앉기 | 0 | 13 | sit down |
| 21 | /자리비움 | 0 | 4 | AFK |
| 22 | /인사 | 0 | 5 | bow |
| 23 | /포권 | 0 | 6 | martial salute |
| 24 | /웃기 | 0 | 7 | laugh |
| 25 | /환호 | 0 | 8 | cheer |
| 26 | /울기 | 0 | 9 | cry |
| 27 | /거절 | 0 | 10 | refuse |
| 28 | /방향표현 | 0 | 11 | direction emote |
| 29 | /항복 | 0 | 12 | surrender |
| 50 | /문파가입 | 1 | 0 | guild join |
| 51 | /문파탈퇴 | 0 | 0 | guild leave |
| 52 | /문파강퇴 | 1 | 0 | guild kick |

**Open questions:**
- Exact semantic of the argument flag at +44 (0/1 inferred from data pattern)
- The sub-command ID integer space (2=pick_up, 5=bow, etc. inferred; not traced through the emote catalog)

---

### 3.2 emoticon.do — Emoticon sprite definitions (stride: 40 bytes, 21 records)

**Sample verified: all 21 records confirmed.**

BST uses dual indexing: primary key on `emote_id`, secondary key on `secondary_key`.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Emote ID (primary BST key) | Sequential 1..21 | CONFIRMED |
| +4 | 1 | u8 | Category flag | 0 for records 1–9; 1 for records 10–21; semantic UNVERIFIED | CONFIRMED (value pattern); UNVERIFIED (semantic) |
| +5 | 3 | — | Alignment padding | Debug 0xCC fill | CONFIRMED (pad) |
| +8 | 4 | u32 | Secondary key (secondary BST key) | Sequential 0..20; confirmed by dual-insert into secondary index | CONFIRMED |
| +12 | 4 | u32 | Action link field | For records with flag=0: always 0; for flag=1 records: matches `sub_command_id` values from `textcommand.do`, strongly suggesting a cross-table link | CONFIRMED (value pattern); UNVERIFIED (name) |
| +16 | 4 | u32 | Frame count or frame delay | 10 for first 9 records and some later records; 160 for others; alternates by record tier | UNVERIFIED |
| +20 | 4 | u32 | Sprite Y origin (pixels) | Increments in fixed steps correlated with sprite_h progression; consistent with sprite-sheet row coordinate | CONFIRMED (pattern); UNVERIFIED (name) |
| +24 | 4 | u32 | Sprite X sub-offset | Mostly 0; small integers (23, 46, 69 …) for later records; increments by 23 | UNVERIFIED |
| +28 | 4 | u32 | Always zero in sample (except last record = 23) | UNVERIFIED | UNVERIFIED |
| +32 | 4 | u32 | Sprite column offset (pixels) | Alternates 0 / 87 across records; likely encodes left-strip vs. right-strip within a two-column sprite sheet | CONFIRMED (pattern); UNVERIFIED (name) |
| +36 | 4 | u32 | Sprite cell height (pixels) | Increases per emote tier: 46, 59, 72, 85, 98, 111, 124, 137, 150, 163, 176 | CONFIRMED (pattern); UNVERIFIED (name) |

**Open questions:**
- Semantic of the category flag at +4 (basic vs. premium emoticons? static vs. animated?)
- Whether the action link at +12 is confirmed as a cross-table foreign key into textcommand.do
- Exact roles of fields at +16, +24, and +28 (frame timing and sprite origin)

---

### 3.3 msginfo.do — In-game popup messages (stride: 128 bytes, 14 records in sample)

**Sample verified: all 14 records decoded (CP949 confirmed).**

The table is sparse; the client looks up messages by numeric ID. Gaps in observed IDs (6–12,
16–19, 21–99) indicate unused message slots.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Message ID (BST key) | Not sequential; observed: 0–5, 13–15, 20, 100–103 | CONFIRMED |
| +4 | 4 | u32 | Dialog flag | 0 for 13 of 14 records; 1 for `msg_id=103` (the one confirmation-dialog record); semantic UNVERIFIED | CONFIRMED (value pattern); UNVERIFIED (semantic) |
| +8 | 60 | char[60] | Text line 1 (CP949) | Null-terminated, zero-padded to 60 bytes | CONFIRMED |
| +68 | 60 | char[60] | Text line 2 (CP949) | Null-terminated, zero-padded; most records end with `\n` (0x0A) | CONFIRMED |

**Sample decoded messages (CP949 → English summary):**

| Message ID | Flag | Line 1 | Line 2 |
|---|---|---|---|
| 0 | 0 | Please try again next time | — |
| 1 | 0 | Your stat points have been reset | Please reallocate them |
| 2 | 0 | Congratulations! | Your rank has increased |
| 3 | 0 | Congratulations! | You have gained experience |
| 4 | 0 | Congratulations! You have obtained gold | Please check the ground |
| 5 | 0 | Congratulations! An item has been delivered | Please check the ground |
| 13 | 0 | You feel power surging through your muscles | Your attack has greatly increased |
| 14 | 0 | Your body becomes hard as steel | Your defense has greatly increased |
| 15 | 0 | The chi within you begins to circulate faster | Your HP recovery rate has increased |
| 20 | 0 | Your mind is becoming clearer | EXP is doubled for 1 hour |
| 100 | 0 | You can now receive your official class | Find your master and change class |
| 101 | 0 | From now on, weaker mobs give less EXP | Stronger mobs give more EXP |
| 102 | 0 | You have obtained a cultivation item | Right-click to learn the cultivation |
| 103 | 1 | You can still select more items | Are you sure you want to give up items? |

**Open questions:**
- Exact semantic of the dialog flag at +4 (value 1 on the confirmation dialog only; likely "requires confirmation" or "modal dialog" — only one data point)
- Whether the trailing `\n` in text_line2 is rendered as a newline or stripped by the engine

---

### 3.4 items_extra.do — Item 3D attachment and scale data (stride: 48 bytes, 90,866 records)

**Sample verified. The loader's attachment code independently confirmed `attach_pos_xyz` and
`attach_rot_*_deg` fields. `anim_scale` fallback literal confirms its position as offset +4.**

Record count: 90,866 active records plus 16 sentinel records at ID = 2,147,483,647 (INT32_MAX).
The sentinel records appear mid-file (not at EOF), likely marking a sub-table boundary.

**Item ID structure:** the top byte of the 32-bit item ID encodes an item category.

| Top byte | Record count in sample |
|---|---|
| 11 (0x0B) | 11,052 |
| 12 (0x0C) | 56,850 |
| 13 (0x0D) | 5,599 |
| 14 (0x0E) | 2,640 |
| 15 (0x0F) | 1,729 |
| 16 (0x10) | 12,037 |
| 17 (0x11) | 943 |
| 127 (0x7F) | 16 (sentinel) |

**Record layout (48 bytes):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | Item ID (BST key) | Top byte encodes category; 0x7FFFFFFF = sentinel | CONFIRMED |
| +4 | 4 | f32 | Animation speed scale | 1.0 = normal; engine substitutes 1.0 when the item ID is not found in this table | CONFIRMED |
| +8 | 4 | i32 | Attachment field A | Range 0..3; added to a bone-position component in the 3D-attachment calculation | CONFIRMED (range); UNVERIFIED (name) |
| +12 | 4 | i32 | Attachment field B | Range 8..48; added to a second bone-position component in the same calculation | CONFIRMED (range); UNVERIFIED (name) |
| +16 | 4 | i32 | Weapon bone attachment X offset | Local space; cast to float at usage | CONFIRMED |
| +20 | 4 | i32 | Weapon bone attachment Y offset | Local space; cast to float at usage | CONFIRMED |
| +24 | 4 | i32 | Weapon bone attachment Z offset | Local space; cast to float at usage | CONFIRMED |
| +28 | 4 | i32 | Rotation around X axis (degrees) | Multiplied by π/180 at usage | CONFIRMED |
| +32 | 4 | i32 | Rotation around Y axis (degrees) | Multiplied by π/180 at usage | CONFIRMED |
| +36 | 4 | i32 | Rotation around Z axis (degrees) | Multiplied by π/180 at usage | CONFIRMED |
| +40 | 4 | i32 | Fourth rotation component or secondary animation parameter | Dominant value 180; range −185..+300; correlates with rarity tier; no direct caller confirmed at this offset | CONFIRMED (range); UNVERIFIED (name) |
| +44 | 4 | u32 | Rarity tier | Observed values {0, 1, 2, 3, 4, 5}; INFERRED as rarity/quality grade — no loader field name confirmed | CONFIRMED (range); INFERRED (semantic) |

**Open questions:**
- Semantic names of `Attachment field A` (+8) and `Attachment field B` (+12) (possibly bone indices or secondary lookup indices)
- Whether the field at +40 is a fourth rotation component (degrees) or an animation blend parameter
- Exact semantics of `rarity_tier` (+44)
- The category scheme encoded by the top byte of the item ID (category enum names unknown)
- Purpose of the 16 sentinel records at ID 0x7FFFFFFF (sub-table separator?)

---

### 3.5 Per-class stance `.do` skill-hotbar table (stride: 116 bytes / 0x74)

**Code-confirmed (static analysis), CAPTURE-UNVERIFIED, plus sample-verified census (2026-06-13).**
The 12 per-class stance `.do` files (`data/script/musajung.do` and 11 siblings) are the
**authoritative on-disk source** of the per-skill UI hotbar slot layout, including the skill
icon source coordinates `(iconSrcX, iconSrcY)`. Each file covers one `(job × stance)` path. The
file-to-class mapping and the icon-source role are described in `ui_manifests.md §2.7`; this
section gives the **complete 116-byte record layout** that §2.7 previously left as a 72-byte
"unmapped" tail.

**Correction (2026-06-13).** Earlier notes (and `ui_manifests.md §2.7`) described `+0x14` as a
`u16` and the icon fields `+0x18 / +0x1C` as `i16`. The engine's hotbar-slot widget builder reads
**all** multi-byte fields from `+0x08` onward as 32-bit `u32` (each field is moved as a DWORD; the
small on-disk values simply leave the top two bytes zero). The icon coordinates are therefore
**unsigned `u32`** with values in the range 0..489, **not** signed `i16`. The sample census reads
the same fields as 16-bit-valued and confirms the upper two bytes of every such field are
near-universally zero (96–97% zero, occasional uninitialised-memory garbage), so the on-disk and
in-engine views agree: a 16-bit value stored in a 4-byte slot.

**Important — this record carries NO game-mechanic data.** It is a pure UI widget descriptor:
screen position, icon atlas UV, a secondary level/rank-bar UV pair, and up to three optional
overlay-sprite descriptors. Level requirement, MP/SP cost, cooldown, range, and target mode are
**not** in the `.do` record; those live in `skills.scr` (§2.8) and are reached via the join
described below. (The §2.16 inventory still lists this file family at 116 bytes; the row there
remains correct.)

**Sample-verified census (3,312 records across all 12 files):** the 116-byte stride is confirmed;
9 of the 12 files divide by 116 exactly, the other 3 leave a small ignored tail (12/40/60 bytes).
The icon coordinates at `+0x18 / +0x1C` resolve to valid 23-px-cell grid positions for 93.9% of
records (the remainder are empty/unused skill slots). All odd-aligned 2-byte positions across the
whole record (the upper halves of the `u32` fields) are 96–97% zero — i.e. padding.

**The earlier "72 vs 76 bytes at +0x28" discrepancy is resolved.** `116 − 0x28 = 76` bytes, and
those 76 bytes (`+0x28..+0x73`) are a **UI overlay-sprite descriptor block**: three optional badge
sprites that decorate the hotbar slot, each gated by its own presence flag. The block is fully
accounted for below; there is no remaining unmapped region.

#### Record layout (116 bytes / 0x74, little-endian, naturally 4-byte aligned)

| Offset | Hex | Size | Type | Field | Notes | Confidence |
|-------:|----:|-----:|------|-------|-------|------------|
| +0   | 00 | 4 | u32 | `instanceKey` | Large sequential skill-instance id; primary map key (first `musajung.do` record = 131101011 / 0x07D07153). Joined to `skills.scr` client-side via the skill-catalogue lookup; see below | CODE-CONFIRMED (CAPTURE-UNVERIFIED) + SAMPLE-VERIFIED |
| +4   | 04 | 1 | u8  | `stanceFilter` | Stance/sex discriminator byte, compared against the active stance value; non-matching records are skipped when the hotbar is built. Compared as `< 3` in the dispatch | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +5   | 05 | 3 | — | _pad | Alignment padding; never individually read | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +8   | 08 | 4 | u32 | `slotIndex` | Sequential slot identifier 0,1,2,…; secondary map key | CODE-CONFIRMED (CAPTURE-UNVERIFIED) + SAMPLE-VERIFIED |
| +12  | 0C | 4 | u32 | `classStanceRef` | `(job × stance)` discriminator used to resolve the skill-icon DDS sheet. Sample-verified values (census): {1001, 1002, 1004, 1005, 1007, 1008, 1010, 1011} in the eight "jung/sa" files; the four "ma"-path files carry small integers (0/12/19/21/23/…) instead of a 4-digit ref — an anomaly (see open list) | CODE-CONFIRMED (8 files) + SAMPLE-VERIFIED; PLAUSIBLE (4 "ma" files) |
| +16  | 10 | 4 | u32 | `widgetPosX` | Screen X base (pixels) of the hotbar slot widget group | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +20  | 14 | 4 | u32 | `widgetPosY_raw` | Screen Y raw (pixels); the engine subtracts 92 before use (`screenY = widgetPosY_raw − 92`) (corrected 2026-06-13: read as u32, not the previously-noted u16) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +24  | 18 | 4 | u32 | `iconSrcX` | Skill icon atlas left edge (pixels); 0..489 on a 23-px grid (authored data, not a formula) (corrected 2026-06-13: u32, not i16) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) + SAMPLE-VERIFIED |
| +28  | 1C | 4 | u32 | `iconSrcY` | Skill icon atlas top edge (pixels); 0..489, same 23-px grid (corrected 2026-06-13: u32, not i16) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) + SAMPLE-VERIFIED |
| +32  | 20 | 4 | u32 | `levelBarSrcX` | Secondary level/rank-bar UV pair, X. Region is an 87×13-px horizontal strip on the same skill-icon sheet (corrected 2026-06-13: u32) | CODE-CONFIRMED access (CAPTURE-UNVERIFIED); semantic PLAUSIBLE |
| +36  | 24 | 4 | u32 | `levelBarSrcY` | Secondary level/rank-bar UV pair, Y (87×13 strip) | CODE-CONFIRMED access (CAPTURE-UNVERIFIED); semantic PLAUSIBLE |
| +40  | 28 | 1 | u8 | `hasOverlay0` | Presence flag: 1 = overlay badge 0 is drawn | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +41  | 29 | 1 | u8 | `hasOverlay1` | Presence flag: overlay badge 1 | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +42  | 2A | 1 | u8 | `hasOverlay2` | Presence flag: overlay badge 2 | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +43  | 2B | 1 | — | _pad | Alignment padding; never read (not a 4th flag) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +44  | 2C | 4 | u32 | `overlay0_dx` | Overlay 0 X pixel offset from `widgetPosX` (`screenX = overlay0_dx + widgetPosX + 110`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +48  | 30 | 4 | u32 | `overlay1_dx` | Overlay 1 X offset (`screenX = overlay1_dx + widgetPosX + 143`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +52  | 34 | 4 | u32 | `overlay2_dx` | Overlay 2 X offset (`screenX = overlay2_dx + widgetPosX + 98`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +56  | 38 | 4 | u32 | `overlay0_dy` | Overlay 0 Y pixel offset (`screenY = widgetPosY_raw − overlay0_dy − 92 − 5`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +60  | 3C | 4 | u32 | `overlay1_dy` | Overlay 1 Y offset (`screenY = overlay1_dy + widgetPosY_raw − 92 + 10`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +64  | 40 | 4 | u32 | `overlay2_dy` | Overlay 2 Y offset (`screenY = overlay2_dy + widgetPosY_raw − 92 + 49`) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +68  | 44 | 4 | u32 | `overlay0_srcX` | Overlay 0 atlas source X (pixels) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +72  | 48 | 4 | u32 | `overlay0_srcY` | Overlay 0 atlas source Y | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +76  | 4C | 4 | u32 | `overlay1_srcX` | Overlay 1 atlas source X | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +80  | 50 | 4 | u32 | `overlay1_srcY` | Overlay 1 atlas source Y | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +84  | 54 | 4 | u32 | `overlay2_srcX` | Overlay 2 atlas source X | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +88  | 58 | 4 | u32 | `overlay2_srcY` | Overlay 2 atlas source Y | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +92  | 5C | 4 | u32 | `overlay0_w` | Overlay 0 width (pixels) | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +96  | 60 | 4 | u32 | `overlay0_h` | Overlay 0 height | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +100 | 64 | 4 | u32 | `overlay1_w` | Overlay 1 width | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +104 | 68 | 4 | u32 | `overlay1_h` | Overlay 1 height | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +108 | 6C | 4 | u32 | `overlay2_w` | Overlay 2 width | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| +112 | 70 | 4 | u32 | `overlay2_h` | Overlay 2 height | CODE-CONFIRMED (CAPTURE-UNVERIFIED) |
| **116** | **74** | — | | **[end of record]** | Total = 116 bytes = 0x74 | |

Every byte position 0x00..0x73 is accounted for: 29 data fields plus 4 padding bytes (one
3-byte pad at +0x05 and one 1-byte pad at +0x2B). The overlay block (`+0x28..+0x73`) is the three
optional badge descriptors — each a presence flag plus a `{dx, dy, srcX, srcY, w, h}` group whose
six components are interleaved across the block (all `u32`). The three different X anchor offsets
(110 / 143 / 98 from `widgetPosX`) place the badges at fixed, visually distinct positions around
the main 23×23 icon cell. No string constant in the binary names the badges; their identity
(e.g. cooldown / cost / chain-rank indicator) is PLAUSIBLE but UNVERIFIED.

#### Census-derived hypotheses for the overlay block (PLAUSIBLE only)

The sample census found three statistically prominent fields inside the overlay block whose value
distributions hint at skill-parameter meaning rather than pure UI geometry. These are **PLAUSIBLE**
and not reconciled with the code-confirmed overlay interpretation above; record them as candidate
secondary meanings only:

- `+0x3C` low `u16`: values 5 (88 occurrences) and 10 (54) dominate — PLAUSIBLE `skillKind`/category enum.
- `+0x68` low `u16`: value 13 dominates (69 occurrences), then 21/23/26 — PLAUSIBLE `levelReq` (minimum level to unlock).
- `+0x28` low `u16`: power-of-two-ish values (256, 1024, 1792, 3072; one record shows 196608 = 0x30000) — PLAUSIBLE bit-flag/count.

The census also observed `classStanceRef`-valued echoes ({1001, …}) at `+0x2C / +0x30 / +0x34 /
+0x40`, possibly prerequisite/cross-reference links (PLAUSIBLE). The census float scan found **no
IEEE-754 floats anywhere in the record** (SAMPLE-VERIFIED negative): all fields are integer.

#### The `instanceKey` → `skills.scr` join (and the c2s 2/52 wire field)

The `.do` record enters the skill-use path only as two keys, both used **client-side**:

1. `instanceKey` (+0x00) → skill-catalogue lookup → the `skills.scr` runtime object that supplies
   the actual combat parameters (range, cost, cooldown, target mode).
2. `classStanceRef` (+0x0C) → skill-icon DDS sheet path (e.g. 1001 → the `musajung` icon sheet).

**CRITICAL — the wire field in the UseSkill packet (opcode 2/52) is the hotbar SLOT INDEX (a
`u8`), NOT the `.do` `instanceKey`.** When the player activates a hotbar slot, the client sends the
slot index byte; the server resolves the skill from the slot contents (it mirrors the client's
hotbar). The `instanceKey` never appears on the wire — its only role is the client-side join into
`skills.scr`. The runtime hotbar holds one `(instanceKey, slot-points/rank)` pair per slot; slot
index 0xFF in the 2/52 packet denotes the basic attack. (This is a static / CODE-CONFIRMED protocol
fact and is **CAPTURE-UNVERIFIED**; it must be confirmed against a real capture before any wire
implementation depends on it. See the skill-use spec and `opcodes.md`.)

**Open questions (this record):**
- `classStanceRef` for the four "ma"-path files: why these carry small integers (0/12/19/21/23/…)
  instead of 4-digit refs — whether a second record type is interleaved or "ma" uses a different
  encoding. The 4-digit refs 1003 / 1006 / 1009 / 1012 do NOT appear as the primary `classStanceRef`
  in those files (sample-verified negative).
- Whether the `+0x3C` / `+0x68` / `+0x28` census candidates (`skillKind` / `levelReq` / flags) are
  real secondary meanings or coincidental, given the code-confirmed overlay-block interpretation.
- The identity of the three overlay badges and of the 87×13 level/rank strip at `+0x20/+0x24`.
- Whether the `.do` `instanceKey` equals the `skills.scr` skill id (+0x00) directly or via a transform.
- The tail bytes ignored by the loader (`musama` 40, `assasinma` 60, `monkma` 12): authoring
  artefact or footer (cosmetic; UNRESOLVED).

---

## Section 4 — items.csv (text item catalogue)

### 4.1 File metadata

| Property | Value | Confidence |
|---|---|---|
| VFS path | `data/script/items.csv` (the human-editable source that compiles to `items.scr`) | CONFIRMED |
| File size | approximately 33.1 MB | CONFIRMED |
| Total rows | 89,712 | CONFIRMED |
| Columns per row | 139 (fixed, 0-based indices 0..138) | CONFIRMED |
| Encoding | CP949 / EUC-KR (no BOM) | CONFIRMED |
| Delimiter | comma (`,`) | CONFIRMED |
| Quoting | RFC 4180 double-quote quoting used when a field value contains a comma | CONFIRMED |
| Line endings | LF (`\n`); `\\` inside description text represents an in-game newline | CONFIRMED |
| Header row | none — row 0 is the first data record | CONFIRMED |

A naive split on `,` will produce incorrect column counts when description fields (col2) contain
embedded commas. A correct RFC 4180 CSV parser is required.

**Relationship to items.scr:** the CSV is the human-editable source; `items.scr` (Section 2.7) is
the compiled binary form the client actually loads at runtime. The CSV→SCR correspondence is
assumed one-to-one in row order. CSV column 1 (`item_id`) is stored in the SCR record at offset
+0x34 (i32, little-endian). A separate compiled consumable catalogue (`citems.scr`, Section 2.11)
uses a different record format and loader and is not derived from this CSV.

### 4.2 Enchant and pricing rules

Items with the same base appear as sequential rows differentiated by a ` +N` suffix in col0.
The sell price (col16) follows the rule: `price_N = base_price × 2^N`.

This rule has been verified for enchant levels +0 through +5 on multiple weapon and armour
families.

The Korean name system encodes socket type via the second syllable group of the item name:

| Syllable suffix | Active socket column |
|---|---|
| 건 (乾) | col49 |
| 감 (坎) | col52 |
| 간 (艮) | col58 |
| 곤 (坤) | col55 |

For a +0 item, the active socket column holds value 1; for +N it holds N+1 (or a computed
bonus value for very high-tier items).

### 4.3 Column index table

The catalogue has 139 columns (indices 0..138). Most non-zero data is concentrated in the
identity, flag, requirement, enchant/socket, stat-bonus, and float-rate blocks below; a large
fraction of the high-index columns are zero in nearly every row (marked `reserved_*`). Stat
identities for the requirement and bonus blocks are corroborated against the canonical primary
stat order in Section 2.2a, but per-column stat mapping inside the requirement block carries
the caveat in the open list.

#### Identity (cols 0–6)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 0 | `name_cp949` | string | CONFIRMED | Display name, CP949; ` +N` suffix marks enchant level N > 0 |
| 1 | `item_id` | uint32 | CONFIRMED | Unique numeric item ID; increments by 1 per enchant level within a family; stored at items.scr +0x34 |
| 2 | `description_cp949` | string | CONFIRMED | Tooltip/lore text, CP949; `\\` = in-game line break; RFC 4180 quoted when contains comma |
| 3 | `linked_item_id` | uint32 | HIGH | For lottery ticket items (col6=1004): prize item ID; zero for all other items |
| 4 | `base_ref_id` | uint32 | HIGH | Base/template item ID; stable across enchant levels of a family; likely NPC shop or crafting template reference |
| 5 | `secondary_ref_id` | uint32 | HIGH | Secondary reference ID; varies by item family; possibly shop category or drop-table reference |
| 6 | `item_subtype` | uint32 | CONFIRMED | Item category code (see subtype table §4.7) |

#### Flags and meta (cols 7–18)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 7 | `bonus_flag_A` | uint8 | PARTIAL | Mostly 0; value 5 on high-tier sets; 1/2/3 on specific families; possibly set-bonus group ID |
| 8 | `bonus_flag_B` | uint8 | PARTIAL | Companion to col7; values 1–5; appears alongside col7 non-zero |
| 9 | `reserved_09` | uint8 | HIGH | Zero in all 89,712 rows; reserved |
| 10 | `enhancement_size` | uint8 | HIGH | Non-zero only for enhancement stones/hammers; size class values 5, 11, 16, 18, 20, 32; zero for equip/consumable |
| 11 | `gem_slot_grade` | uint8 | PARTIAL | Non-zero on socket gems and some tier-restricted items; values 1–15; likely gem grade or socket tier |
| 12 | `reserved_12` | uint8 | HIGH | Zero in all rows; reserved |
| 13 | `equip_slot_flag` | uint8 | HIGH | Secondary equip-slot indicator: 0=none/hat, 3=body armour, 4=lower armour, 5=inner armour, 15=two-handed(?), 40=ranged weapon slot |
| 14 | `unknown_14` | uint8 | UNVERIFIED | Very rare non-zero (values 30, 32, 42, 46, 50); possibly drop modifier |
| 15 | `unknown_15` | uint8 | UNVERIFIED | Very rare non-zero (1, 2, 3) |
| 16 | `sell_price` | uint32 | CONFIRMED | NPC sell price in game currency; doubles per enchant level; value 1 for non-saleable items |
| 17 | `npc_purchaseable` | uint8 | HIGH | 1 = available for direct NPC purchase; 0 = drop/craft only |
| 18 | `enabled` | uint8 | CONFIRMED | 1 = live item visible to players; 0 = internal/test item |

#### Stacking, tier, durability (cols 19–23)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 19 | `max_stack` | uint16 | CONFIRMED | 1 = non-stackable; 20 = standard consumable stack; other values for special stacking |
| 20 | `unknown_20` | uint8 | PARTIAL | Almost always 1; rarely 20 on special tools; role unclear |
| 21 | `unknown_21` | uint8 | PARTIAL | Almost always 1; very rarely 2 or 20 |
| 22 | `item_tier_rank` | uint16 | CONFIRMED | Item tier/grade; constant within an enchant family; higher = better tier; 1 for consumables (see tier table §4.8) |
| 23 | `max_durability` | uint16 | HIGH | 300 for all equippable gear; 200 for mount items; 1 for consumables |

#### Required stats (cols 24–28)

These columns encode the minimum character stat values required to equip the item. They
correspond to the five primary stats of Martial Heroes. **Mapping caveat:** the requirement
block is presented here in the column order observed in the sample (24..28). The canonical
runtime primary-stat order is STR/AGI/DEX/INT/CON (Section 2.2a); whether the CSV requirement
columns follow that same order or a CSV-specific order is not yet confirmed (see open list).

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 24 | `req_STR` | uint16 | HIGH | Required 힘 (Strength) |
| 25 | `req_CON` | uint16 | HIGH | Required 체 (Constitution) |
| 26 | `req_AGI` | uint16 | HIGH | Required 민 (Agility) |
| 27 | `req_INT` | uint16 | HIGH | Required 지 (Intelligence) |
| 28 | `req_CHI` | uint16 | HIGH | Required 기/내공 (Chi/Internal Force) |

Armour requires equal values in all five stats. Weapons require asymmetric values per weapon
class (see weapon stat matrix §4.10). Consumables have all five at 0.

#### Class restriction flags (cols 29–34)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 29 | `class_yi` | uint8 | HIGH | 1 = 의(義) weapon class (blade/axe/spear — 도/부/창); also 1 for all-class items |
| 30 | `class_ye` | uint8 | HIGH | 1 = 예(禮) weapon class (flying blade/claw/bow — 비/조/궁); also 1 for all-class |
| 31 | `class_in` | uint8 | HIGH | 1 = 인(仁) weapon class (staff/fan/sword — 장/선/검); also 1 for all-class |
| 32 | `class_ji` | uint8 | HIGH | 1 = 지(智) weapon class (rod/wheel/chain-hammer — 봉/륜/추); also 1 for all-class |
| 33 | `class_extra_E` | uint8 | PARTIAL | Rare; set on enhancement tools and universal items |
| 34 | `class_extra_F` | uint8 | UNVERIFIED | Very rare; values 1 or 2 |

For class-specific weapons exactly one of cols 29–32 is 1. For armour, accessories, and
consumables all four (cols 29–32) are 1.

#### Sparse / reserved (cols 35–46)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 35 | `reserved_35` | uint8 | HIGH | Always 0 |
| 36 | `unknown_36` | uint32 | UNVERIFIED | Extremely rare non-zero (800,000; 50,000,000; 47,637) |
| 37 | `reserved_37` | uint8 | HIGH | Always 0 |
| 38 | `unknown_38` | uint8 | PARTIAL | Rare non-zero: value 2 (55 occurrences), value 1 (6 occurrences); possible flag |
| 39–42 | `unknown_39_42` | uint8 | UNVERIFIED | Very sparse; likely internal flags |
| 43 | `is_advanced_tier` | uint8 | HIGH | 1 for items with item_tier_rank >= 120; likely controls advanced-stat display mode |
| 44–46 | `reserved_44_46` | uint8 | HIGH | Always 0 |

#### Enchant and socket block (cols 47–63)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 47 | `enchant_level` | uint8 | CONFIRMED | Current enchant level (0 = base); matches ` +N` in the item name |
| 48 | `gem_power` | uint8 | CONFIRMED | For socketing gem items: gem strength value (1–47+); 0 for equippable gear |
| 49 | `socket_gen` | uint16 | HIGH | Socket 건(乾) slot value; for 건-type socket: enchant_level+1 (low tier) or base_bonus+5×enchant (high tier); 0 if different socket type |
| 50 | `unknown_50` | uint8 | UNVERIFIED | Very sparse; values 1–3 |
| 51 | `unknown_51` | uint32 | UNVERIFIED | Very sparse; large values 20,800–89,000; possibly crafting cost |
| 52 | `socket_gam` | uint16 | HIGH | Socket 감(坎) slot value; same encoding as col49 |
| 53 | `unknown_53` | uint8 | UNVERIFIED | Very sparse |
| 54 | `unknown_54` | uint32 | UNVERIFIED | Very sparse; large values matching col51 |
| 55 | `socket_gon` | uint16 | HIGH | Socket 곤(坤) slot value |
| 56 | `unknown_56` | uint8 | UNVERIFIED | Very sparse |
| 57 | `unknown_57` | uint32 | UNVERIFIED | Very sparse |
| 58 | `socket_gan` | uint16 | HIGH | Socket 간(艮) slot value |
| 59–63 | `unknown_59_63` | mixed | UNVERIFIED | Very sparse; col63 had one non-zero value of 600 |

#### Bonus stat block A (cols 64–74)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 64 | `bonus_atk` | uint32 | HIGH | Attack power bonus; confirmed by red gem (+1,000 attack → col64=1,000) |
| 65 | `bonus_HP` | uint32 | CONFIRMED | HP bonus; confirmed by black gem; increases with enchant level for armour |
| 66 | `bonus_unknown_66` | uint32 | UNVERIFIED | Sparse; values 400–7,000 |
| 67 | `bonus_unknown_67` | uint32 | UNVERIFIED | Very sparse |
| 68 | `bonus_ext_atk` | uint32 | HIGH | External/physical attack bonus; confirmed by blue gem (col68=1,000) |
| 69 | `bonus_unknown_69` | uint32 | PARTIAL | Small values 30–941; likely a secondary stat bonus |
| 70 | `bonus_unknown_70` | uint32 | UNVERIFIED | Sparse; values match col66 distribution |
| 71–73 | `reserved_71_73` | uint8 | HIGH | Always 0 |
| 74 | `unknown_74` | uint8 | UNVERIFIED | Very sparse (values 1–3) |

#### Float rate block (cols 75–83)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 75 | `attack_speed` | float32 | HIGH | Attack speed coefficient for weapons; range 0.26–0.37; increases slightly per enchant level; 0 for non-weapons |
| 76 | `crit_or_ring_bonus` | float32 | PARTIAL | Non-zero only for ring-type accessories (col6=21); values 0.02–0.68; possibly critical-strike rate |
| 77 | `unknown_77` | uint8 | UNVERIFIED | Very sparse (values 1–3) |
| 78 | `dodge_rate` | float32 | HIGH | Dodge/evasion rate coefficient; non-zero for armour items; range 0.08–0.37; increases with enchant level |
| 79 | `unknown_79` | float32 | UNVERIFIED | Extremely sparse; always 0.02 when present |
| 80 | `unknown_80` | float32 | PARTIAL | Sparse; values 0.01–0.03; appears on high-tier armour; possibly damage reduction coefficient |
| 81 | `bonus_rate_81` | float32 | PARTIAL | Values 0.01–0.11; appears on mount/accessory items; possibly movement speed bonus rate |
| 82 | `unknown_82` | float32 | UNVERIFIED | Very sparse |
| 83 | `unknown_83` | uint8 | UNVERIFIED | Very sparse (values 1–3) |

#### Bonus stat block B (cols 84–99)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 84 | `bonus_chi` | uint32 | HIGH | Chi/internal-force bonus; confirmed by green gem (col84=200) |
| 85 | `weapon_stat_A` | uint32 | HIGH | First weapon attack component; large value for high-tier weapons; increases per enchant; likely base internal-force attack |
| 86 | `weapon_stat_B` | uint32 | HIGH | Second weapon attack component; approximately 10–15% of col85; likely attack-variance component |
| 87 | `min_attack` | uint32 | CONFIRMED | Minimum attack damage; increases per enchant level; for max-tier at +12: 584,321 |
| 88 | `reserved_88` | uint8 | HIGH | Always 0 |
| 89 | `unknown_89` | uint8 | UNVERIFIED | Very sparse (values 1–3) |
| 90 | `max_attack` | uint32 | CONFIRMED | Maximum attack damage; always greater than col87 |
| 91 | `reserved_91` | uint8 | HIGH | Always 0 |
| 92 | `unknown_92` | uint8 | UNVERIFIED | Very sparse (values 1–3) |
| 93 | `bonus_defense_A` | uint32 | HIGH | Defense bonus A; confirmed by black gem (col93=300); 0 for non-armour |
| 94 | `phys_defense` | uint32 | CONFIRMED | Physical defense for armour; increases with enchant level; also non-zero for high-tier weapons as secondary stat |
| 95 | `weapon_stat_C` | uint32 | HIGH | Third weapon stat component; appears at high tiers; approximately 50% of col85 |
| 96 | `armor_defense` | uint32 | HIGH | Primary armour defense value; confirmed by yellow gem (col96=150) |
| 97 | `bonus_stat_97` | uint32 | PARTIAL | Non-zero for boots and high-tier armour/weapons; for boots: values 4, 9, 14, 19 by socket tier; possibly movement speed bonus |
| 98 | `weapon_stat_D` | uint32 | PARTIAL | Present on very high-tier weapons; values approximately 5,000–7,000 |
| 99 | `bonus_stat_99` | uint32 | UNVERIFIED | Sparse; values 200–250 |

#### Secondary effects block (cols 100–116)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 100 | `special_power` | float32 | PARTIAL | Item-type-specific power value; for spirit-bead items: defense value; for armour with extra sockets: socket count (1–5); for spirit-gem items: percentage (3.3, 4.4, etc.) |
| 101 | `bonus_stat_101` | uint32 | PARTIAL | Sparse; values 3–200; appears alongside col100 on special items |
| 102 | `socket_count` | uint8 | PARTIAL | Socket slot count; values 1–8; non-zero on socketed high-tier equips and crafted items |
| 103–111 | `reserved_103_111` | uint8 | HIGH | Always 0 in sample; reserved |
| 112 | `duration_minutes` | uint32 | CONFIRMED | Duration for time-limited items in minutes; 4,320=3 days; 10,080=7 days; 14,400=10 days; 21,600=15 days; 30,240=21 days; 43,200=30 days; 0=permanent |
| 113 | `expire_mode` | uint8 | HIGH | Expiry mode: 255 = standard timed expiry; 0 = permanent |
| 114 | `unknown_114` | uint8 | UNVERIFIED | Very sparse; values 1–45 |
| 115 | `unknown_115` | int32 | UNVERIFIED | Sparse; large signed values including negatives; possibly pre-computed or internal |
| 116 | `unknown_116` | uint32 | UNVERIFIED | Sparse; large values; correlates with col115 presence |

#### Model / visual IDs (cols 117–118)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 117 | `model_set_id` | uint16 | HIGH | Item visual model set ID; 301–334=weapon model families; 200–209=armour model families; 1–11=accessory models; 300+=gem/stone models; 0=no model (consumable) |
| 118 | `model_type` | uint8 | HIGH | Model type class: 11=weapon, 10=armour, 2=ring/badge, 3=gem/stone, 4=bracelet, 5=ring, 322/330=special (skill books) |

#### Consumable and special item block (cols 119–138)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 119 | `consumable_value` | uint32 | HIGH | For gem stones: gem bonus value (matches col64/col65/col84/col96 per gem type); for mounts: speed value |
| 120 | `is_consumable` | uint8 | HIGH | 1 = item has a use/consume effect; 0 = no direct use |
| 121 | `unknown_121` | mixed | UNVERIFIED | Sparse; some item ref IDs |
| 122 | `unknown_122` | uint8 | UNVERIFIED | Sparse; values 1, 10, 20 |
| 123 | `unknown_123` | uint16 | UNVERIFIED | Very sparse; values 12–500 |
| 124–125 | `reserved_124_125` | uint8 | HIGH | Always 0 |
| 126 | `unknown_126` | uint32 | UNVERIFIED | Sparse; values 3,104–14,622; possibly item link IDs |
| 127 | `gem_category` | uint8 | HIGH | For socketing stones (col6=62): 1=attack(red), 2=ext-attack(blue), 3=chi(green), 4=HP(black), 5=defense(yellow) |
| 128 | `equippable_flag` | uint8 | HIGH | 1 = item can be equipped or socketed; also 1 for special mount tokens |
| 129 | `has_effect` | uint8 | HIGH | 1 = item has a secondary effect or buffering mechanic |
| 130 | `effect_type` | uint8 | PARTIAL | Effect type code: 13=HP restore, 17=secondary potion type, 18=tertiary potion type, 48=antidote |
| 131 | `effect_strength` | uint16 | PARTIAL | Effect potency (HP restore amount, duration, etc.) |
| 132 | `use_level_req` | uint8 | PARTIAL | Level/rank required to use the consumable |
| 133–135 | `unknown_133_135` | uint8 | UNVERIFIED | Very sparse |
| 136–138 | `reserved_136_138` | uint8 | HIGH | Always 0 |

### 4.4 Gem stone → stat column mapping (sample-verified)

| Gem name | col127 gem_category | Primary stat column | Notes |
|---|---|---|---|
| 블랙 스톤 (Black) | 4 | col93 (`bonus_defense_A`) | Also stored in col119 |
| 옐로우 스톤 (Yellow) | 5 | col96 (`armor_defense`) | Also stored in col119 |
| 레드 스톤 (Red) | 1 | col64 (`bonus_atk`) | Also stored in col119 |
| 그린 스톤 (Green) | 3 | col84 (`bonus_chi`) | Also stored in col119 |
| 블루 스톤 (Blue) | 2 | col68 (`bonus_ext_atk`) | Also stored in col119 |

### 4.5 Equip slot reference (col13 / col19 disambiguation)

| col13 value | Slot |
|---|---|
| 0 | Head (hat / 현천관) |
| 1 | Neck / Amulet (령 type) |
| 2 | Chest (현천굉 type) |
| 3 | Body armour (견의 — heavy outer) |
| 4 | Lower armour (경의 — light outer) |
| 5 | Inner armour (내갑) |
| 6 | Wrist (완) |
| 7 | Weapon (all 12 weapon subtypes) |
| 9 | Ring (환) |
| 11 | Feet (장화 — boots) |
| 12 | Pet slot (신수 / mount pet) |
| 13 | Mount tab (보패) |
| 14 | Skill slot (무공비급 — skill books) |
| 15 | Mount skill slot |
| 16 | Gem socket (주/보주 gem beads) |
| 20 | Non-equippable (consumables, scrolls, materials) |

### 4.6 Item subtype (col6) reference

| col6 | Korean name | Description |
|---|---|---|
| 1 | 도 (刀) | Blade |
| 2 | 부 (斧) | Twin axe |
| 3 | 창 (槍) | Spear |
| 4 | 비 (飛) | Flying blade |
| 5 | 조 (爪) | Claw |
| 6 | 궁 (弓) | Bow |
| 7 | 장 (杖) | Staff |
| 8 | 선 (扇) | Fan |
| 9 | 검 (劍) | Straight sword |
| 10 | 봉 (棒) | Rod/club |
| 11 | 륜 (輪) | Wheel |
| 12 | 추 (錘) | Chain hammer |
| 13 | 견의 | Heavy outer armour |
| 14 | 경의 | Light outer armour |
| 15 | 내갑 | Inner armour |
| 16 | 현천굉 | Chest armour (special) |
| 17 | 장화 | Boots |
| 18 | 현천관 | Headgear |
| 20 | 령 | Amulet / token |
| 21 | 환 | Ring |
| 22 | 완 | Wrist bracelet |
| 23 | 보패 | Mount token |
| 44 | — | Enhancement material / misc tool |
| 47 | 주 / 보주 | Socketing gem bead |
| 52 | 변신구 | Disguise item |
| 53 | 마패 | Mount token (horse) |
| 60 | 강화석 | Enhancement stone |
| 62 | 스톤 | Socketing stone (Black/Yellow/Red/Green/Blue) |
| 1001 | 탕/약/수 | Potion / medicine |
| 1002 | 무공비급 | Skill book |
| 1003 | 환원부 / 이동부 | Teleport scroll / area warp |
| 1004 | 복표 | Lottery ticket |
| 1006 | 전이수 | Teleport potion |
| 1008 | 파황지첩 | Area entry scroll |
| 1010 | 제작서 / 비급 | Crafting recipe scroll |
| 1029 | 지천부 | Special area entry scroll (power-level gated) |
| Large IDs | — | Complex system items (pet books, skill scrolls, etc.) |

### 4.7 Item tier rank (col22) reference

| col22 | Observed tier |
|---|---|
| 1 | Consumables, non-equip, tools |
| 36 | Low-tier accessories |
| 60 | Low-tier weapons |
| 70 | Internal test weapons |
| 80 | Low stat-requirement gear |
| 87 | Standard-req armour pieces |
| 95 | Higher-stat armour pieces |
| 120 | Internal test weapons |
| 144 | 태산 (Taisan) — normal tier |
| 152 | 독고 (Dokgo) tier 1 |
| 166 | 독고 극렬 (Dokgo Extreme) |
| 180–220 | Elite tiers |
| 230–240 | Near-endgame tiers |
| 245 | Max tier (귀살천명 Gwisalcheonmyeong) |

### 4.8 Weapon stat requirements matrix (Taisan tier, col22=144)

| Weapon (col6) | col29 ui | col30 ye | col31 in | col32 ji | col24 STR | col25 CON | col26 AGI | col27 INT | col28 CHI |
|---|---|---|---|---|---|---|---|---|---|
| 도 blade (1) | 1 | 0 | 0 | 0 | 134 | 0 | 111 | 0 | 74 |
| 부 axe (2) | 1 | 0 | 0 | 0 | 134 | 74 | 111 | 0 | 0 |
| 창 spear (3) | 1 | 0 | 0 | 0 | 134 | 0 | 111 | 74 | 0 |
| 비 flying (4) | 0 | 1 | 0 | 0 | 74 | 134 | 0 | 0 | 111 |
| 조 claw (5) | 0 | 1 | 0 | 0 | 74 | 0 | 134 | 74 | 0 |
| 궁 bow (6) | 0 | 1 | 0 | 0 | 74 | 0 | 134 | 0 | 74 |
| 장 staff (7) | 0 | 0 | 1 | 0 | 0 | 0 | 111 | 134 | 74 |
| 선 fan (8) | 0 | 0 | 1 | 0 | 0 | 74 | 111 | 134 | 0 |
| 검 sword (9) | 0 | 0 | 1 | 0 | 74 | 0 | 111 | 134 | 0 |
| 봉 rod (10) | 0 | 0 | 0 | 1 | 74 | 111 | 0 | 134 | 0 |
| 륜 wheel (11) | 0 | 0 | 0 | 1 | 0 | 111 | 0 | 134 | 74 |
| 추 hammer (12) | 0 | 0 | 0 | 1 | 0 | 111 | 74 | 134 | 0 |
| Armour (13–18) | 1 | 1 | 1 | 1 | 80 | 80 | 80 | 80 | 80 |

Values are for Taisan tier (col22=144). Higher tiers scale proportionally.

---

## Section 5 — .ini files (client configuration, disk-only)

### 5.1 Location and storage

The five INI files are stored **on disk** in the executable directory, not inside the VFS.
They are created with the hidden file attribute.

| Slot | Filename | Purpose | Confidence |
|---|---|---|---|
| 1 | `DoOption.ini` | Main graphics / sound / UI options | CONFIRMED |
| 2 | `option.ini` | Secondary options | UNVERIFIED |
| 3 | `panel.ini` | UI panel layout state | UNVERIFIED (inferred) |
| 4 | `combo.ini` | Combo / hotkey bindings | UNVERIFIED (inferred) |
| 5 | `TSIDX.ini` | Tab / session index | UNVERIFIED |

All five are read via the Windows INI API. A parser need not implement binary INI parsing;
the Windows API or any standard INI library suffices.

**Plaintext-config note — "do.ini ships encrypted" is RESOLVED as a false premise (2026-06-13,
CODE-CONFIRMED, CAPTURE-UNVERIFIED).** `DoOption.ini` and its four sibling INIs are **plaintext**:
they are read and written through the standard Windows profile API (the `GetPrivateProfileInt` /
`GetPrivateProfileString` / `WritePrivateProfileString` family) with **no byte-level transform of
any kind**. There is **no file cipher**. The only obfuscation applied is cosmetic — the path
builder sets `FILE_ATTRIBUTE_HIDDEN` on all five INIs, so they are merely hidden from a default
Explorer view (this is almost certainly what created the "encrypted/obfuscated" impression).

The same conclusion holds for the `.do` and `.scr` **data** files: they are plaintext fixed-record
binary streams read through the same unencrypted disk/VFS reader as every `.txt` and `.csv` — no
XOR, no rolling key, no table substitution on the file path. The only per-byte step in the text-mode
read path is CR/LF newline normalisation, not a cipher.

The binary does contain two genuine cipher routines, but **neither touches config or data files**:
the **network packet cipher** (an XOR + bit-rotation transform applied only to outbound packet
buffers — documented separately by the network-crypto firewall, not here) and an **anti-cheat
in-memory string obfuscator** (a runtime-only decode of sensitive API/DLL name strings held in
process memory, so a memory scan does not reveal them; never reads or writes a file). Both are
out of scope for the config/asset spec and are mentioned only to disambiguate them from any
supposed file cipher.

Privacy note for the clean re-implementation: `OPTION_ID` (the saved login/account identifier) is
persisted in clear text inside the hidden `DoOption.ini`; a faithful re-implementation should
decide deliberately whether to keep that behaviour.

### 5.2 DoOption.ini — Section [DO_OPTION]

Single section. All keys are integers except `OPTION_ID` which is a string.

| Key | Value type | Default | Valid range | Notes | Confidence |
|---|---|---|---|---|---|
| `OPTION_WIDTH` | int | 1024 | 800–1920 | Screen width, pixels | CONFIRMED |
| `OPTION_HEIGHT` | int | 768 | 600–1200 | Screen height, pixels | CONFIRMED |
| `OPTION_COLORBIT` | int | 32 | 16 or 32 | Colour depth | CONFIRMED |
| `OPTION_LANG` | int | 1 | 1–3 | Language selection | CONFIRMED |
| `OPTION_VIEW_CHAR` | int | 1 | 0/1 | Character render enable | CONFIRMED |
| `OPTION_VIEW_BACK` | int | 1 | 0/1 | Background render enable | CONFIRMED |
| `OPTION_GROUND` | int | 1 | 0/1 | Ground render enable | CONFIRMED |
| `OPTION_SKY` | int | 1 | 0/1 | Sky render enable | CONFIRMED |
| `OPTION_WEATHER` | int | 1 | 0/1 | Weather system enable | CONFIRMED |
| `OPTION_WATER` | int | 1 | 0/1 | Water render enable | CONFIRMED |
| `OPTION_SHADOW` | int | 1 | 0/1 | Shadow render enable | CONFIRMED |
| `OPTION_DMGTEXT` | int | 1 | 0/1 | Damage floating text enable | CONFIRMED |
| `OPTION_TEX_CHAR` | int | 1 | 0/1 | Character texture quality | CONFIRMED |
| `OPTION_TEX_MOB` | int | 1 | 0/1 | Mob texture quality | CONFIRMED |
| `OPTION_TEX_ITEM` | int | 1 | 0/1 | Item texture quality | CONFIRMED |
| `OPTION_TEX_ETC` | int | 1 | 0/1 | Misc texture quality | CONFIRMED |
| `OPTION_SOUND_CHAR` | int | 1 | 0/1 | Character sound enable | CONFIRMED |
| `OPTION_SOUND_MOB` | int | — | 0/1 | Mob sound enable | CONFIRMED (key); default UNVERIFIED |
| `OPTION_SOUND_TERRAIN` | int | 1 | 0/1 | Terrain sound enable | CONFIRMED |
| `OPTION_SOUND_MUSIC` | int | 1 | 0/1 | Music enable | CONFIRMED |
| `OPTION_SCREENMODE` | int | 0 | 0/1 | Fullscreen mode | CONFIRMED |
| `OPTION_SOUNDVOL_CHAR` | int | 100 | 0–100 | Character SFX volume | CONFIRMED |
| `OPTION_SOUNDVOL_MOB` | int | 100 | 0–100 | Mob SFX volume | CONFIRMED |
| `OPTION_SOUNDVOL_BACK` | int | 100 | 0–100 | Background SFX volume | CONFIRMED |
| `OPTION_SOUNDBOL_MUSIC` | int | 100 | 0–100 | Music volume (key typo: `BOL` not `VOL`) | CONFIRMED |
| `OPTION_EFFECT` | int | 100 | 0–100 | Effect quality/density | CONFIRMED |
| `OPTION_BRIGHT` | int | 100 | 0–100 | Brightness | CONFIRMED |
| `OPTION_STALL_NOTIFY` | int | 0 | 0/1 | Stall notification | CONFIRMED |
| `OPTION_WHISPER_NOTIFY` | int | 0 | 0/1 | Whisper notification | CONFIRMED |
| `OPTION_FORCE_NOTIFY` | int | 0 | 0/1 | Force notification | CONFIRMED |
| `OPTION_ID` | string | (empty) | max 15 chars | Character/account ID for auto-login | CONFIRMED |

One field in the runtime options struct (read-order index +18) is read by the loader but its
key name is UNVERIFIED. `OPTION_SCREENMODE` occupies read-order index +30 (the read order is
non-contiguous).

**Clamp-range refinement (2026-06-13, CODE-CONFIRMED, CAPTURE-UNVERIFIED).** The `[DO_OPTION]`
section holds **31 keys** (the rows above plus the read-order-index quirks below). A fresh read of
the loader's per-key range clamps refines several ranges that were over-narrowed in the table
above; where these differ, the loader is authoritative:

- The render/view toggles (`OPTION_VIEW_CHAR`, `OPTION_VIEW_BACK`, `OPTION_GROUND`, `OPTION_SKY`,
  `OPTION_WEATHER`, `OPTION_WATER`, `OPTION_SHADOW`, `OPTION_DMGTEXT`) clamp to **1..3** (a 3-level
  quality/visibility selector), not a 0/1 boolean; out-of-range values reset to 1.
- The texture-quality keys (`OPTION_TEX_CHAR`, `OPTION_TEX_MOB`, `OPTION_TEX_ITEM`,
  `OPTION_TEX_ETC`) clamp to **1..5**, reset to 1 out of range.
- The sound on/off keys (`OPTION_SOUND_CHAR`, `OPTION_SOUND_MOB`, `OPTION_SOUND_TERRAIN`,
  `OPTION_SOUND_MUSIC`) clamp to **1..2**; a value of 2 ("off") additionally forces the matching
  `OPTION_SOUNDVOL_*` volume field to 0.
- The four `OPTION_SOUNDVOL_*` keys (including the `…SOUNDBOL_MUSIC` typo'd key) clamp to **0..100**.
- `OPTION_SCREENMODE` clamps to **0..2** (not 0/1); values > 2 reset to 0.
- `OPTION_EFFECT` and `OPTION_BRIGHT` clamp to **1..100**.
- The three `*_NOTIFY` keys clamp to **0..1** (>= 2 resets to 0).
- `OPTION_ID` (string) is stored only when non-empty and shorter than 16 characters (a 17-byte
  buffer); read with the string profile API, written back verbatim — see the plaintext-config and
  privacy notes in §5.1.
- One read-order slot (index +12) is **forced to 1 by the constructor and is not read from the
  INI** at all; this accounts for a gap between the key count and the read-order indices.

### 5.3 Other INI files

Keys for `option.ini`, `panel.ini`, `combo.ini`, and `TSIDX.ini` were not traced. Inferred
purposes from the filenames are noted in section 5.1 but are UNVERIFIED.

---

## Section 6 — CVersion data file (.dat, version manifest only)

The `.dat` extension as used by this engine refers exclusively to a **client version / patch
manifest**. It is NOT a game-data catalogue format.

| Field | Size | Type | Notes | Confidence |
|---|---|---|---|---|
| Records | file_size / 4 | u32 each | One `u32` per record, read until EOF | CONFIRMED |

A valid version manifest must contain at least 7 records; fewer triggers a load error. This
file is read at character-selection, not during game-data catalogue loading. It has no
relevance to the `.scr` / `.do` pipeline.

---

## Section 7 — game.ver (binary client-version source file)

**Sample verified** (single instance; VFS read). A small fixed-size binary file that supplies the
client version number consumed by the enter-game token. It is the same shape family as the
Section 6 version manifest (a small flat array of `u32` with a 7-element minimum), but it lives at
a fixed VFS path and exactly one field is consumed by the login/enter token formula.

### Identification

- **VFS path:** `data/cursor/game.ver`
- **Size:** exactly **28 bytes**
- **Format:** flat array of **7 x `u32`, little-endian** (no header, no magic, no record-count
  prefix - the field count is fixed at 7)
- **VFS note:** the archive carries **two index entries for this path** at consecutive offsets;
  both describe the same 28 identical bytes. The second is a redundant shadow entry (a duplicate
  TOC row, not a second distinct file). A parser may resolve either entry; both yield the same
  payload. See `formats/pak.md` for VFS lookup behaviour.

### Layout (7 x u32 LE, 28 bytes)

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | field0 | Unknown; possibly a format/version tag | UNVERIFIED (value present) |
| 0x04 | 4 | u32 | field1 | Unknown | UNVERIFIED |
| 0x08 | 4 | u32 | field2 | Unknown | UNVERIFIED |
| 0x0C | 4 | u32 | field3 | Unknown; possibly a build number | UNVERIFIED |
| 0x10 | 4 | u32 | field4 | Unknown | UNVERIFIED |
| 0x14 | 4 | u32 | **version_source** (field5) | **The value consumed by the enter-game version token.** | CONFIRMED (role) |
| 0x18 | 4 | u32 | field6 | Unknown | UNVERIFIED |

### Version token derivation (CONFIRMED math; cross-referenced)

The enter-game / login version token is computed from the single field at offset **0x14**:

```
version_token = 10 x version_source + 9      // version_source = u32 at offset 0x14
```

With the observed `version_source = 2114`, the token is `10 x 2114 + 9 = 21149`, matching the
enter-game token documented in the protocol / front-end specs. Only `version_source` (offset 0x14)
feeds this formula; the remaining six `u32` are not used by it.

**Record count source:** fixed at 7 (file is always 28 bytes). **Record stride:** 4 bytes per field.

---

## Known unknowns

### Resolved since last version (wave-7 and wave-8 unblocks)

1. **exp.scr +10..+19** — RESOLVED: three sub-fields identified (+8=reserved zero, +12=secondary
   EXP curve, +16=tertiary EXP curve); semantics of secondary and tertiary curves remain UNVERIFIED.
2. **userlevel.scr field layout** — RESOLVED (wave-8): 15 field positions documented, including
   the corrected u16 counters at +4/+6/+8, the zero pads at +2/+10, the eight scale floats, and
   four individual reserved f32 slots. Stat category names remain UNVERIFIED.
3. **userpoint.scr layout** — RESOLVED (wave-8) with a CORRECTION: the +8 cumulative is a `u32`
   (not u16 + flag byte); group-2 cumulative at +16 is a `u32`; gain/cumulative pairs and the
   secondary/tertiary curve columns are fully mapped.
4. **users.scr internal layout** — RESOLVED (wave-8): four 124-byte class blocks; the header
   constants 0x13/0x43 documented; the class-specific multiplier deviations corrected to float
   indices [23], [24], [25]; class names (무사/자객/도사/승려) added.
5. **skills.scr main record body** — RESOLVED (wave-8) from a 1504-byte sample: skill ID,
   category, name, class flag, type byte, descriptions, SP cost, cooldown, range, and chain
   references mapped. Large integer sub-regions and the chain-reference encoding remain open.
6. **items.scr item-ID offset** — RESOLVED: item ID is at +0x34 (i32) and maps to items.csv col 1.
7. **UNVERIFIED strides** — RESOLVED for: citems.scr, skillneedset.scr, warstoneinfo.scr,
   oblist.scr, statue.scr, setitemname.scr, Tutor.scr, viplevels.scr, itemscale.scr,
   itemeffect.scr, textcommand.do, emoticon.do, msginfo.do.
8. **Quest-data family** — RESOLVED (2026-06-13, sample): `quests.scr` (3720 B, sparse,
   488/122 — corrected from the 4960 B estimate), `npc.scr` (404 B, 2510 — description text),
   `autoquestion_cl.scr` (92 B, 1300 — anti-bot quiz), `discript.sc` (68 B, 33 — UI menu
   labels), `products.scr` (212 B stride). See §2.17. The behavioural model is in
   `specs/quests.md`. `events.scr` / `helps.scr` / `tiphelp.scr` structure outlined (not
   flat-stride); their record bodies remain UNVERIFIED.
9. **Per-class stance `.do` record body** — RESOLVED (2026-06-13, code-confirmed + sample): the
   full 116-byte (0x74) layout is mapped in §3.5 — all 29 fields plus 4 padding bytes, including
   the previously-"unmapped" `+0x28..+0x73` overlay-sprite descriptor block. Corrections applied:
   icon coordinates and `widgetPosY_raw` are `u32` (not `i16`/`u16`); the "72 vs 76 bytes @ +0x28"
   discrepancy is resolved (76 bytes = three optional overlay-badge descriptors). The c2s 2/52
   UseSkill wire field is the hotbar **slot index** (`u8`), not the `.do` `instanceKey`.
10. **"do.ini ships encrypted"** — RESOLVED (2026-06-13): FALSE premise. `DoOption.ini` and the
   four sibling INIs, and the `.do`/`.scr` data files, are all **plaintext** read/written via the
   Windows profile API and an uncrypted disk/VFS reader; the only obfuscation is
   `FILE_ATTRIBUTE_HIDDEN` on the five INIs. The binary's two real ciphers (network packet
   XOR/ROL, anti-cheat in-memory string obfuscation) are not file-related. See §5.1.

### Still open

1. **exp.scr +2 constant 64** — max-level constant, EXP-rate denominator, or flags word?
2. **exp.scr secondary curve at +12** — semantic identity (inner cultivation? merit? prestige?)
3. **exp.scr tertiary curve at +16** — semantic identity
4. **userlevel.scr counters at +4/+6/+8** — what phase do they encode? Why reset to zero at L145
   while the scale floats do not change?
5. **userlevel.scr stat category names** — which named stat maps to each of the four float positions?
6. **userpoint.scr constant 25 at +2** — what does 25 represent in the allocator?
7. **userpoint.scr group-1 vs group-2** — which named stat maps to each pool?
8. **userpoint.scr secondary curve at +20/+22** — martial-energy (내공/내력) budget or a third stat?
9. **userpoint.scr tertiary values at +24/+28** — third stat pool or a max-cap schedule?
10. **userpoint.scr large gains at keys 285–300** — post-standard-level reward bursts?
11. **users.scr 8-multiplier-to-stat mapping** ([22..29] at +92..+123).
12. **users.scr header constants 0x13/0x43** — counts or a version marker?
13. **users.scr class-4 label discrepancy** — `users.scr` calls it 승려 (monk); `items.csv` calls
    the fourth equip class Gungnyo (궁녀, archer). Reconcile which is canonical.
14. **users.scr A-divisor table source** — populated outside this file; trace its origin.
15. **items.scr leading field width** at +0x00 (and whether +0x34 is the only ID field).
16. **items.scr unresolved body regions** — +0x00..+0x33, +0x35..+0x4B, +0x4D..+0x97,
    +0x9D..+0xD1, +0xE9..+0x21F (no sample available).
17. **items.scr name/description location** — where the CP949 text fields sit inside the 548-byte
    record (no sample).
18. **items.scr stat-grant encoding** — how the disk record stores the STR/AGI/DEX/INT/CON/HP/MP
    grants that populate the runtime item object.
19. **skills.scr name-buffer width** at +8 (24 or 32 bytes).
20. **skills.scr integer region +32..+515** — only +260 and +516 resolved.
21. **skills.scr chain-reference encoding** — decimal-composite decode for +1116..+1136, +1180, +1294.
22. **skills.scr +1296/+1300** — prerequisite or successor chain IDs?
23. **skills.scr motion index +1304 / constant 7 at +1306** — meaning.
24. **skills.scr tail fields** +1496..+1503.
25. **mobs.scr gap fields** — +52..+243, +253..+271, +325..+395, +401..+443, +449..+487 UNVERIFIED.
26. **mobs.scr secondary name at +19** — zone/region name or sub-type label?
27. **mobs.scr level field −1** — special scripted mobs or data anomalies?
28. **npcs.scr** — character encoding (UCS-2 vs CP949) and all field names.
29. **citems.scr fields +4..+51** — names and semantics between item name and flag word.
30. **citems.scr description blocks 6–9** — unused language slots?
31. **citems.scr `#` placeholder** — is 0x23 the engine's null-description sentinel?
32. **citems.scr tail fields at +1040 and +1044** — meaning of values 0/1 and 0/2.
33. **oblist.scr field_00 / field_04** — role of the first two u32 values.
34. **warstoneinfo.scr field_04/field_08/field_32/field_36** — semantic names.
35. **emoticon.do flag at +4** — basic vs premium? static vs animated?
36. **emoticon.do action link at +12** — confirmed cross-table link to textcommand.do?
37. **emoticon.do fields at +16/+24/+28** — frame timing or sprite-sheet coordinates?
38. **msginfo.do dialog flag at +4** — "requires confirmation"? Only one data point.
39. **items_extra.do fields at +8 and +12** — bone indices? secondary lookup indices?
40. **items_extra.do field at +40** — fourth rotation component or animation blend parameter?
41. **items_extra.do rarity_tier at +44** — no loader field name confirmed; inferred from range.
42. **items_extra.do category top-byte scheme** — enum names for top-byte values 0x0B–0x11.
43. **items_extra.do sentinel records at 0x7FFFFFFF** — sub-table boundary or merge artifact?
44. **items.csv requirement-column stat order (cols 24–28)** — does the CSV follow the canonical
    runtime order STR/AGI/DEX/INT/CON (Section 2.2a), or a CSV-specific order? Currently the table
    labels them STR/CON/AGI/INT/CHI from sample patterning; this needs a definitive cross-check.
45. **items.csv cols 20, 21** — adjacent to max_stack; role unclear.
46. **items.csv cols 115, 116** — large/signed values; possibly internal checksums or pre-computed.
47. **All .xdb files** — record stride, key scheme, and field layout entirely UNVERIFIED.
48. **option.ini / panel.ini / combo.ini / TSIDX.ini keys** — not traced.
49. **INI field at read-order index +18** — key name and purpose UNVERIFIED.
50. **npc.scr ↔ npcs.scr link** — `npc.scr` (404 B, 2510 description records, §2.17.3) and
    `npcs.scr` (1916 B, 812 catalogue records, §2.10) are now both stride-resolved, but the key
    that links the 1..2510 descriptor id space to the catalogue NPC ids is UNVERIFIED.
51. **quests.scr undecoded fields** — the six step-code bytes at +0x040, the contents of the
    embedded 48-byte sub-records (objective/reward), and the constant 2 at +0x064 (§2.17.1).
52. **quests.scr `abandonable` flag byte** — `specs/quests.md` §15.4 reads an `abandonable`
    flag whose runtime offset is inconsistent with the 3720-byte stride; reconcile against the
    sample record.
53. **events.scr / helps.scr / tiphelp.scr record bodies** — structure outlined in §2.17.5;
    the variable-length record contents are not decoded.
54. **Remaining UNVERIFIED-stride .scr files** — mapsetting, plus those listed as UNVERIFIED in
    section 2.2.
55. **Stance `.do` `classStanceRef` in the four "ma" files** — why `musama`/`assasinma`/`wizardma`/
    `monkma` carry small integers at +0x0C instead of 4-digit refs (1003/1006/1009/1012), and
    whether a second record type is interleaved (§3.5).
56. **Stance `.do` overlay block secondary meaning** — whether the census candidates `+0x3C`
    (`skillKind`?), `+0x68` (`levelReq`?), and `+0x28` (flags?) are real fields or coincidental,
    and the identity of the three overlay badges and the 87×13 level/rank strip at +0x20/+0x24 (§3.5).
57. **Stance `.do` `instanceKey` ↔ `skills.scr` skill id** — whether the join key is identity or a
    transform; needs cross-check against `skills.scr` +0x00 (§3.5, §2.8).
59. **game.ver fields 0-4 and 6** - only `version_source` at offset 0x14 has a confirmed
    role (the enter-game token). The other six `u32` (0x00, 0x04, 0x08, 0x0C, 0x10, 0x18)
    have no traced consumer; 0x0C is a build-number candidate. See Section 7.
58. **c2s 2/52 slot-index claim is CAPTURE-UNVERIFIED** — the protocol fact that the UseSkill packet
    sends the hotbar slot index (`u8`), not the instanceKey, is static-confirmed only and must be
    validated against a real capture before any wire code relies on it (§3.5).

---

## Cross-references

- VFS container layout: `Docs/RE/formats/pak.md`
- Sound event tables sharing the VFS path convention: `Docs/RE/formats/sound_tables.md`
- Mob type byte (value 11 = boss) relates to the spawn descriptor: `Docs/RE/structs/spawn_descriptor.md`
- Item category flags relate to: `Docs/RE/structs/item.md`
- Skill catalogue relates to: `Docs/RE/structs/skill.md`
- Skill icon source coordinates and stance `.do` file-to-class mapping: `Docs/RE/formats/ui_manifests.md` §2.7 (§3.5 here gives the full `.do` record layout)
- UseSkill (c2s 2/52) opcode and the skill-use path: `Docs/RE/opcodes.md`, `Docs/RE/specs/skill_system.md`
- `game.ver` version token feeds the enter-game flow: `Docs/RE/specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
