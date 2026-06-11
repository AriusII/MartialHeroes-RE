# Format: .scr / .do / .ini / .xdb  (client-side configuration and data catalogues)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true (exp.scr, userlevel.scr, userpoint.scr, users.scr, mobs.scr,
>                        citems.scr, skillneedset.scr, warstoneinfo.scr, oblist.scr,
>                        textcommand.do, emoticon.do, msginfo.do, items_extra.do,
>                        items.csv — all confirmed against real sample bytes)

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
- **Variable-length variants** (items.scr, skills.scr): after the fixed main record, the loader
  reads `N × 8` trailing sub-entries, where `N` is a `u8` count stored at a fixed offset within
  the main record (see per-file tables below).
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
| `data/script/userlevel.scr` | 60 | none | CONFIRMED | Base stat values per level |
| `data/script/userpoint.scr` | 32 | none | CONFIRMED | Stat allocation curve |
| `data/script/users.scr` | 496 (4 × 124-byte class blocks) | none | CONFIRMED | Character class stat grid |
| `data/script/items.scr` | 548 | N × 8 B | CONFIRMED | Item catalogue (binary) |
| `data/script/skills.scr` | 1504 | N × 8 B | CONFIRMED | Skill catalogue |
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
| `data/script/npc.scr` | UNVERIFIED | — | UNVERIFIED | NPC sub-table |
| `data/script/mapsetting.scr` | UNVERIFIED | — | UNVERIFIED | Map settings |
| `data/script/quests.scr` | UNVERIFIED | — | UNVERIFIED | Quest definitions |
| `data/script/products.scr` | UNVERIFIED | — | UNVERIFIED | Crafting recipes |
| `data/script/events.scr` | UNVERIFIED | — | UNVERIFIED | Event table |
| `data/script/helps.scr` | UNVERIFIED | — | UNVERIFIED | Help text |
| `data/item/items_extra.do` | 48 | none | CONFIRMED | Item extended / 3D-attachment data |
| `data/script/textcommand.do` | 52 | none | CONFIRMED | Chat command definitions |
| `data/script/emoticon.do` | 40 | none | CONFIRMED | Emoticon sprite-sheet definitions |
| `data/script/msginfo.do` | 128 | none | CONFIRMED | In-game popup message strings |
| `data/script/errorinfo.do` | 108 (0x6C) | none | CONFIRMED (stride only) | Error message strings |
| `data/script/monkma.do` and class-stance variants | 166 (0xA6) | none | CONFIRMED (stride only) | Per-class stance/move tables |

**Additional .scr files observed in the engine string table (loaders not traced):**
`nicktofame.scr`, `guildcrest.scr`, `letters.scr`, `chivalry.scr`,
`upgradeitems.scr`, `repair.scr`, `productrandname.scr`, `productcollect.scr`,
`system_control.scr`, `tiphelp.scr`, `playtime_reward.scr`.

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

### 2.4 userlevel.scr — Base stat values per level (stride: 60 bytes, 300 records)

**Wave-7 blocker: RESOLVED. Sample verified: 300 records.**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Level index, 1-based (map key) | Sequential 1..300 | CONFIRMED |
| +2 | 2 | u16 | Always zero | Reserved; zero in all 300 records | CONFIRMED (value=0) |
| +4 | 4 | u32 | Step-count field A | L1..L11=0; L12..L23=(1 as two u16); L24..L35=2; L36..L144=2; L145..L300=0 (resets) | CONFIRMED (transitions); UNVERIFIED (semantic) |
| +8 | 4 | u32 | Step-count field B | L1..L11=0; L12=2; L24=3; L36=4; L145..L300=0 (resets) | CONFIRMED (transitions); UNVERIFIED (semantic) |
| +12 | 4 | f32 | Stat-scale positive group [0] | L1..L35=1.0; L36..L300=3.0 | CONFIRMED |
| +16 | 4 | f32 | Stat-scale positive group [1] | Matches group [0] in all observed records | CONFIRMED |
| +20 | 4 | f32 | Stat-scale positive group [2] | Matches group [0] | CONFIRMED |
| +24 | 4 | f32 | Stat-scale positive group [3] | Matches group [0] | CONFIRMED |
| +28 | 4 | f32 | Stat-scale negative group [0] | L1..L35=−1.0; L36..L300=−2.0 | CONFIRMED |
| +32 | 4 | f32 | Stat-scale negative group [1] | Matches negative group [0] | CONFIRMED |
| +36 | 4 | f32 | Stat-scale negative group [2] | Matches negative group [0] | CONFIRMED |
| +40 | 4 | f32 | Stat-scale negative group [3] | Matches negative group [0] | CONFIRMED |
| +44 | 16 | 4×f32 | Reserved group | All 0.0 in all records | CONFIRMED (all zero) |

**Transition summary:**

| Level range | Step field A | Step field B | Positive float | Negative float |
|---|---|---|---|---|
| L1–L11 | 0 | 0 | 1.0 | −1.0 |
| L12–L23 | 1 (×2) | 2 | 1.0 | −1.0 |
| L24–L35 | 2 (×2) | 3 | 1.0 | −1.0 |
| L36–L144 | 2 (×2) | 4 | 3.0 | −2.0 |
| L145–L300 | 0 | 0 | 3.0 | −2.0 |

The four positive and four negative float slots likely correspond to four stat categories (for
example physical offense, physical defense, magical offense, magical defense), but the mapping
to named game stats is UNVERIFIED. The reset of step fields to zero at L145 while the float
values remain unchanged is UNVERIFIED in meaning.

**Open questions:**
- Names of the four stat categories in the positive/negative groups
- Semantic meaning of the integer step fields at +4 and +8 (phase-gating mechanic?)
- Why step fields reset to zero at L145 while scale floats do not change

---

### 2.5 userpoint.scr — Stat allocation curve (stride: 32 bytes, 301 records)

**Wave-7 blocker: RESOLVED. Sample verified: 301 records, keys 0..300.**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Point-allocation index, 0-based (0..300) | Map key | CONFIRMED |
| +2 | 2 | u16 | Constant = 25; identical in all 301 records | Semantic UNVERIFIED; possibly base stat-point budget or creation constant | CONFIRMED (value); UNVERIFIED (semantic) |
| +4 | 2 | u16 | Stat-group-1 gain at this step | key=0→5; key=300→1000 | CONFIRMED |
| +6 | 2 | u16 | Always zero (alignment pad) | All records | CONFIRMED (value=0) |
| +8 | 2 | u16 | Stat-group-1 cumulative total | Running sum of group-1 gains; verified for first 20 records | CONFIRMED |
| +10 | 2 | u16 | Mostly zero; value 1 in two of 301 records | Rare flag or overflow indicator; UNVERIFIED | UNVERIFIED |
| +12 | 2 | u16 | Stat-group-2 gain at this step | key=0→7; key=1→1; grows at high keys | CONFIRMED |
| +14 | 2 | u16 | Always zero (alignment pad) | All records | CONFIRMED (value=0) |
| +16 | 2 | u16 | Stat-group-2 cumulative total | Sequential pattern confirmed | CONFIRMED |
| +18 | 2 | u16 | Always zero (alignment pad) | All records | CONFIRMED (value=0) |
| +20 | 2 | u16 | Secondary curve low word | key=0..5→0; key=6→282; increments by approximately 3 per step | CONFIRMED |
| +22 | 2 | u16 | Secondary curve high word | key=0..5→0; key=6..9→20; grows slowly | CONFIRMED |
| +24 | 4 | u32 | Tertiary value 1 | key=0..295 mostly 0; key=296→235,000; key=300→255,000 | CONFIRMED |
| +28 | 4 | u32 | Tertiary value 2 | Same pattern as +24; key=300→255,000 | CONFIRMED |

The two stat groups correspond to the two main stat allocation pools. The constant 25 at +2
may be the base stat-point budget awarded at character creation. The semantic mapping of
group-1 and group-2 to named stats (Strength, Defense, Agility, Wisdom, etc.) is UNVERIFIED.

**Open questions:**
- Semantic meaning of constant 25 at +2
- Which named game stats map to group-1 versus group-2
- Meaning of the secondary curve at +20..+23 (possibly martial-energy or a third stat type)
- Meaning of the tertiary values at +24 and +28 (possibly max-cap for a third stat type)
- The rare value 1 at +10 (two records only)

---

### 2.6 users.scr — Character class stat grid (496-byte file, 4 × 124-byte class blocks)

**Wave-7 blocker: RESOLVED. Sample verified: all 4 class blocks confirmed.**

**Corrected structure:** the file is not a single flat blob; it contains four sequential 124-byte
blocks, one per character class. Each block begins with a 4-byte header followed by 30 × f32
values (120 bytes). Total: 4 × 124 = 496 bytes.

**Per-block layout:**

| Offset within block | Size | Type | Field | Notes | Confidence |
|--------------------:|-----:|------|-------|-------|------------|
| +0 | 1 | u8 | Class ID (1..4) | Block 0→1, block 1→2, block 2→3, block 3→4 | CONFIRMED |
| +1 | 3 | — | Padding / header tail | Bytes observed as constant pattern across all 4 blocks; not data | CONFIRMED (present); UNVERIFIED (semantic) |
| +4 | 12 | 3×f32 | Stat group A | Values 3.0, 3.0, 3.0; identical across all 4 classes | CONFIRMED |
| +16 | 16 | 4×f32 | Zero group | All 0.0; all 4 classes | CONFIRMED (all zero) |
| +32 | 4 | f32 | Stat ratio column 1 | 7.0 in all 4 classes | CONFIRMED |
| +36 | 4 | f32 | Stat ratio column 2 | 24.0 in all 4 classes | CONFIRMED |
| +40 | 4 | f32 | Zero | 0.0 in all 4 classes | CONFIRMED |
| +44 | 12 | 3×f32 | Stat ratio repeat | Same (7.0, 24.0, 0.0) pattern; repeated a second and third time through +56 | CONFIRMED |
| +68 | 32 | 8×f32 | Zero group | All 0.0; all 4 classes | CONFIRMED |
| +92 | 32 | 8×f32 | Class-specific ratio group | Mostly 1.0; class-specific deviations (see table below) | CONFIRMED |

**Class-specific deviations in the 8-float group (offsets +92..+120 relative to block start):**

| Class ID | Deviating position | Deviant value | All others |
|---|---|---|---|
| 1 | none | — | 1.0 |
| 2 | float[1] at block+96 | 1.10 | 1.0 |
| 3 | float[2] at block+100=1.15; float[3] at block+104=1.10 | as noted | 1.0 |
| 4 | float[1] at block+96=1.10; float[2] at block+100=1.15 | as noted | 1.0 |

These deviations are consistent with per-class base-stat multipliers (class 2 has a 10% bonus
to one stat category; class 3 has 15% to one category and 10% to another).

**Open questions:**
- Names of the four character classes (Class 1=Monk? Class 2=Musa? etc.)
- Which named game stats map to which float positions in the 8-float group
- The semantic of the header padding bytes at +1..+3
- Relationship to the `(10/A)*B` formula noted in prior loader tracing: `A` and `B` offset
  positions within the block are UNVERIFIED

---

### 2.7 items.scr — Item catalogue (stride: 548 bytes + N × 8 trailing)

**Wave-7 blocker: RESOLVED (stride, category flags, trailing count confirmed).**

Note: `items.csv` (Section 5) provides a much richer human-readable item catalogue. `items.scr`
is the binary runtime form. The two files likely share item IDs but were not cross-verified.

**Main record (548 bytes = 0x224):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | ? | u16 or u32 | Item ID | First field; exact width UNVERIFIED | CONFIRMED (position) |
| +0xD2 | 1 | u8 | Sub-type flag | — | CONFIRMED |
| +0xE5 | 1 | u8 | Category flag 1 | Value 1 = weapon | CONFIRMED |
| +0xE6 | 1 | u8 | Category flag 2 | Value 1 = armour | CONFIRMED |
| +0xE7 | 1 | u8 | Category flag 3 | Value 1 = type-11 | CONFIRMED |
| +0xE8 | 1 | u8 | Category flag 4 | Value 1 = type-16 | CONFIRMED |
| +0x220 | 1 | u8 | Trailing entry count N | Upgrade/effect sub-entries | CONFIRMED |
| +0x221 | 3 | — | Alignment padding | Pads to 548-byte stride | CONFIRMED (derived) |
| All other offsets | — | ? | Remaining item fields | UNVERIFIED | UNVERIFIED |

**Trailing upgrade-effect entries (N × 8 bytes, only present when N > 0):**

| Offset within entry | Size | Type | Field | Confidence |
|--------------------:|-----:|------|-------|------------|
| +0 | 8 | ? | All fields UNVERIFIED | UNVERIFIED |

Each 8-byte on-disk entry maps to a 12-byte runtime object; the extra 4 bytes are derived at
load time. Internal field layout UNVERIFIED.

---

### 2.8 skills.scr — Skill catalogue (stride: 1504 bytes + N × 8 trailing)

**Wave-7 blocker: RESOLVED (stride and trailing-count offset confirmed).**

**Main record (1504 bytes = 0x5E0):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 1500 | ? | Main skill data (all fields UNVERIFIED) | UNVERIFIED |
| +0x5DC | 1 | u8 | Trailing entry count N | CONFIRMED |
| +0x5DD | 3 | — | Alignment padding to reach 1504 bytes | CONFIRMED (derived) |

**Trailing sub-entries (N × 8 bytes):** same structural form as items.scr trailing entries; all
fields UNVERIFIED.

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
| Per-class stance `.do` files (`monkma.do`, `monksa.do`, etc.) | 166 bytes (0xA6) | Per-class skill/move tables |

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

## Section 4 — items.csv (text item catalogue)

### 4.1 File metadata

| Property | Value | Confidence |
|---|---|---|
| VFS path | `data/script/items.csv` (probable; confirmed by sample analysis) | CONFIRMED |
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

#### Identity (cols 0–6)

| Col | Field name | Type | Confidence | Notes |
|---|---|---|---|---|
| 0 | `name_cp949` | string | CONFIRMED | Display name, CP949; ` +N` suffix marks enchant level N > 0 |
| 1 | `item_id` | uint32 | CONFIRMED | Unique numeric item ID; increments by 1 per enchant level within a family |
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
correspond to the five primary stats of Martial Heroes.

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

## Known unknowns

### Resolved since last version (wave-7 unblocks)

Items 1, 2, 3, 4, and 15 from the prior version's known-unknowns list are now PARTIALLY
or FULLY RESOLVED:

1. **exp.scr +10..+19** — RESOLVED: three sub-fields identified (+8=reserved zero, +12=secondary
   EXP curve, +16=tertiary EXP curve); semantics of secondary and tertiary curves remain UNVERIFIED.
2. **userlevel.scr field layout** — RESOLVED: eight f32 stat-scale values (four positive, four
   negative) confirmed; step fields at +4/+8 confirmed; stat category names remain UNVERIFIED.
3. **userpoint.scr layout** — FULLY RESOLVED: gain/cumulative pair for two stat groups plus
   secondary/tertiary curve columns fully mapped.
4. **users.scr internal layout** — FULLY RESOLVED: 4 class blocks × 124 bytes; class-specific
   multiplier deviations confirmed.
15. **UNVERIFIED strides** — RESOLVED for: citems.scr, skillneedset.scr, warstoneinfo.scr,
    oblist.scr, statue.scr, setitemname.scr, Tutor.scr, viplevels.scr, itemscale.scr,
    itemeffect.scr, textcommand.do, emoticon.do, msginfo.do.

### Still open

1. **exp.scr +2 constant 64** — is this a max-level constant, EXP-rate denominator, or a flags word?
2. **exp.scr secondary curve at +12** — semantic identity (inner cultivation? merit? prestige?)
3. **exp.scr tertiary curve at +16** — semantic identity
4. **userlevel.scr step fields at +4 and +8** — what phase-transition do they encode? Why do they reset to zero at L145 while float values do not?
5. **userlevel.scr stat category names** — which of the four named game stats maps to positive group [0], [1], [2], [3]?
6. **userpoint.scr constant 25 at +2** — what does 25 represent in the allocator?
7. **userpoint.scr secondary curve at +20** — martial-energy curve or a third stat type?
8. **userpoint.scr tertiary values at +24/+28** — max-cap or something else?
9. **users.scr class IDs 1..4 vs. 3 playable classes** — is one class unused/NPC-only?
10. **users.scr header bytes +1..+3** — VTable pointer fragment or a magic marker?
11. **items.scr ID field width** — u16 or u32 at offset +0?
12. **items.scr main record body** — fields from +0 to +0xD1 and from +0xE9 to +0x21F entirely UNVERIFIED.
13. **skills.scr main record body** — 1,500 bytes of skill data before the trailing count byte.
14. **mobs.scr gap fields** — contiguous regions between +52 and +243 and between +253 and +271 fully UNVERIFIED; regions from +325 to +395 and +401 to +443 and +449 to +487 UNVERIFIED.
15. **mobs.scr secondary name at +19** — is this the zone/region name or a sub-type label?
16. **mobs.scr level field −1** — special scripted mobs or data anomalies?
17. **npcs.scr** — character encoding (UCS-2 vs. CP949) and all field names.
18. **citems.scr fields +4..+51** — names and semantics of fields between item name and flag word.
19. **citems.scr description blocks 6–9 (largely empty)** — unused language slots?
20. **citems.scr `#` placeholder** — is 0x23 the engine's null-description sentinel?
21. **citems.scr tail fields at +1040 and +1044** — meaning of values 0/1 and 0/2.
22. **oblist.scr fields field_00 and field_04** — role of the first two u32 values.
23. **warstoneinfo.scr fields field_04, field_08, field_32, field_36** — semantic names.
24. **emoticon.do flag at +4** — basic vs. premium emoticons? static vs. animated?
25. **emoticon.do action link at +12** — confirmed cross-table link to textcommand.do?
26. **emoticon.do fields at +16, +24, +28** — frame timing or sprite-sheet coordinates?
27. **msginfo.do dialog flag at +4** — "requires confirmation"? Only one data point.
28. **items_extra.do fields at +8 and +12** — bone indices? Secondary lookup indices?
29. **items_extra.do field at +40** — fourth rotation component or animation blend parameter?
30. **items_extra.do rarity_tier at +44** — no loader field name confirmed; inferred from value range.
31. **items_extra.do category top-byte scheme** — enum names for top-byte values 0x0B–0x11.
32. **items_extra.do sentinel records at 0x7FFFFFFF** — sub-table boundary or merge artifact?
33. **items.csv cols 20, 21** — adjacent to max_stack; role unclear.
34. **items.csv cols 115, 116** — large/signed values; possibly internal checksums or pre-computed.
35. **All .xdb files** — record stride, key scheme, and field layout entirely UNVERIFIED.
36. **option.ini / panel.ini / combo.ini / TSIDX.ini keys** — not traced.
37. **INI field at read-order index +18** — key name and purpose UNVERIFIED.
38. **npc.scr stride** — independent file or sub-table of npcs.scr; not traced.
39. **UNVERIFIED stride .scr files** — mapsetting, quests, products, events, helps, plus those listed as UNVERIFIED in section 2.2.

---

## Cross-references

- VFS container layout: `Docs/RE/formats/pak.md`
- Sound event tables sharing the VFS path convention: `Docs/RE/formats/sound_tables.md`
- Mob type byte (value 11 = boss) relates to the spawn descriptor: `Docs/RE/structs/spawn_descriptor.md`
- Item category flags relate to: `Docs/RE/structs/item.md`
- Skill catalogue relates to: `Docs/RE/structs/skill.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
