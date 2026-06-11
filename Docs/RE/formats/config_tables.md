# Format: .scr / .do / .ini / .xdb  (client-side configuration and data catalogues)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: hypothesis
> sample_verified: false

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

- **Extensions:** `.scr`, `.do`, `.xdb` (binary record files); `.ini` (Windows INI, text)
- **Found in:** `.scr` and `.do` under `data/script/` and `data/item/` in the VFS; `.ini` on disk
  relative to the executable directory; `.xdb` under `data/script/` in the VFS
- **Magic / signature:** none — no file-level magic bytes or version header for any variant
- **Endianness:** little-endian (all fields)

---

## Section 1 — VFS container (data.inf + data/data.vfs)

All `.scr`, `.do`, and `.xdb` files are delivered through the engine's virtual filesystem. The
VFS is documented in full in `formats/pak.md`; only the summary relevant to these loaders is
repeated here.

The TOC index (`data.inf`) provides an entry count and per-file `(dataOffset, dataSize)` pairs.
Lookup is a binary search on the lowercased ASCII filename. Once a file is located, the loader
calls `SetFilePointerEx` + `ReadFile` on the VFS blob.

A configuration key (`vfsmode`) in `game.lua` selects whether the engine reads from the VFS blob
or falls back to direct disk access. The loaders for all formats below use a unified file-open
wrapper that makes this choice transparent; a parser implementation may do the same.

**No compression is applied to any `.scr`, `.do`, or `.xdb` payload in the VFS.**

---

## Section 2 — .scr and .do files (binary record catalogues)

### 2.1 Common structural pattern

All `.scr` and `.do` files share the same loader pattern:

- **No file header, no record-count prefix.** The loader derives record count as:
  `record_count = file_size / record_stride`
- **Flat array of fixed-size records**, concatenated without inter-record padding.
- **Keyed insertion:** the first field of every record is a `u16` identifier. The loader inserts
  each record into a runtime map keyed on this value.
- **Variable-length variants** (items.scr, skills.scr): after the fixed main record, the loader
  reads `N × 8` trailing sub-entries, where `N` is a `u8` count stored at a fixed offset within
  the main record (see per-file tables below).

### 2.2 Catalogue file inventory

Stride values marked **CONFIRMED** were established by parser analysis. Strides marked
**UNVERIFIED** were observed as path-builder strings but the corresponding loaders were not
traced.

| VFS path | Stride (bytes) | Trailing entries | Confidence | Role |
|---|---|---|---|---|
| `data/script/exp.scr` | 20 | none | CONFIRMED | EXP required per level |
| `data/script/userlevel.scr` | 60 | none | CONFIRMED | Base stat values per level |
| `data/script/userpoint.scr` | 32 | none | CONFIRMED | Stat allocation curve |
| `data/script/users.scr` | 496 (bulk block) | none | CONFIRMED | Character class stat grid |
| `data/script/items.scr` | 548 | N × 8 B | CONFIRMED | Item catalogue |
| `data/script/skills.scr` | 1504 | N × 8 B | CONFIRMED | Skill catalogue |
| `data/script/skillcategory.scr` | 564 | none | CONFIRMED | Skill category table |
| `data/script/mobs.scr` | 488 | none | CONFIRMED | Mob / monster catalogue |
| `data/script/npcs.scr` | 1916 | none | CONFIRMED | NPC catalogue |
| `data/script/npc.scr` | UNVERIFIED | — | UNVERIFIED | NPC sub-table |
| `data/script/mapsetting.scr` | UNVERIFIED | — | UNVERIFIED | Map settings |
| `data/script/quests.scr` | UNVERIFIED | — | UNVERIFIED | Quest definitions |
| `data/script/products.scr` | UNVERIFIED | — | UNVERIFIED | Crafting recipes |
| `data/script/events.scr` | UNVERIFIED | — | UNVERIFIED | Event table |
| `data/script/helps.scr` | UNVERIFIED | — | UNVERIFIED | Help text |
| `data/item/items_extra.do` | 48 | none | CONFIRMED | Item extended data |

**Additional .scr files observed in the engine string table (loaders not traced):**
`statue.scr`, `warstoneinfo.scr`, `oblist.scr`, `citems.scr`, `setitemname.scr`,
`tiphelp.scr`, `nicktofame.scr`, `guildcrest.scr`, `letters.scr`, `chivalry.scr`,
`upgradeitems.scr`, `repair.scr`, `productrandname.scr`, `productcollect.scr`,
`viplevels.scr`, `skillneedset.scr`, `itemeffect.scr`, `itemscale.scr`,
`playtime_reward.scr`, `system_control.scr`.

**Additional .do files observed (loaders not traced):**
`data/script/textcommand.do`, `emoticon.do`, `msginfo.do`, `errorinfo.do`;
`data/script/monkma.do`, `monksa.do`, `monkjung.do` and wizard / assassin / musa
variants (per-class skill / move tables).

### 2.3 exp.scr — EXP per level (stride: 20 bytes)

**Wave-7 blocker: RESOLVED.** The EXP thresholds are client-side.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 2 | u16 | Level index, 1-based | CONFIRMED |
| +2 | 4 | u32 | EXP column 0 | CONFIRMED |
| +6 | 4 | u32 | EXP column 1 | CONFIRMED |
| +10 | 10 | ? | Remaining fields | UNVERIFIED |

The loader validates that the level index in each successive record matches the expected
sequential counter (1, 2, 3 ...). A mismatch aborts the load. The two EXP columns feed
separate runtime ladders; which one is "EXP to next level" and which is a cumulative total
is UNVERIFIED pending sample inspection.

### 2.4 userlevel.scr — Base stat values per level (stride: 60 bytes)

**Wave-7 blocker: RESOLVED (stride and key confirmed).**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 2 | u16 | Level index (map key) | CONFIRMED |
| +2 | 58 | ? | Stat base values for this level | UNVERIFIED |

Total: 60 bytes. The stat field names, their order, and whether they are `u16`, `u32`, or
`float` are UNVERIFIED pending sample extraction.

### 2.5 userpoint.scr — Stat allocation curve (stride: 32 bytes)

**Wave-7 blocker: RESOLVED (stride and key confirmed).**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 2 | u16 | Point index (map key) | CONFIRMED |
| +2 | 30 | ? | Curve values | UNVERIFIED |

Total: 32 bytes. Internal layout UNVERIFIED.

### 2.6 users.scr — Character class stat grid (496-byte bulk block)

**Wave-7 blocker: RESOLVED (bulk size confirmed).**

The entire file is read as a single 496-byte block. The engine recomputes a floating-point
stat-ratio grid from this data using the formula `(10 / A) * B`. The grid is indexed as
`grid[3 * j + 3 * i + k]`, implying three character classes, N stat categories, and 3
sub-values per category. The exact mapping of `A` and `B` to offsets within the block is
UNVERIFIED.

| Offset | Size | Notes | Confidence |
|-------:|-----:|-------|------------|
| +0 | 496 | Entire block, internal layout UNVERIFIED | CONFIRMED (size only) |

### 2.7 items.scr — Item catalogue (stride: 548 bytes + N × 8 trailing)

**Wave-7 blocker: RESOLVED (stride, category flags, trailing count confirmed).**

**Main record (548 bytes = 0x224):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | ? | u16 or u32 | Item ID | First field; exact size UNVERIFIED | CONFIRMED (position) |
| +0xD2 | 1 | u8 | Sub-type flag | — | CONFIRMED |
| +0xE5 | 1 | u8 | Category flag 1 | Value `1` = weapon | CONFIRMED |
| +0xE6 | 1 | u8 | Category flag 2 | Value `1` = armour | CONFIRMED |
| +0xE7 | 1 | u8 | Category flag 3 | Value `1` = type-11 | CONFIRMED |
| +0xE8 | 1 | u8 | Category flag 4 | Value `1` = type-16 | CONFIRMED |
| +0x220 | 1 | u8 | Trailing entry count N | Upgrade / effect sub-entries | CONFIRMED |
| +0x221 | 3 | — | Alignment padding | To reach 548-byte stride | CONFIRMED (derived) |
| All other offsets | — | ? | Internal item fields | UNVERIFIED | UNVERIFIED |

**Trailing upgrade-effect entries (N × 8 bytes, present only when N > 0):**

| Offset within entry | Size | Type | Field | Confidence |
|--------------------:|-----:|------|-------|------------|
| +0 | 8 | ? | All fields UNVERIFIED | UNVERIFIED |

Each 8-byte on-disk entry maps to a 12-byte runtime object; the extra 4 bytes are derived at
load time. Internal field layout UNVERIFIED.

### 2.8 skills.scr — Skill catalogue (stride: 1504 bytes + N × 8 trailing)

**Wave-7 blocker: RESOLVED (stride and trailing-count offset confirmed).**

**Main record (1504 bytes = 0x5E0):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 1500 | ? | Main skill data (all fields UNVERIFIED) | UNVERIFIED |
| +0x5E0 | 1 | u8 | Trailing entry count N | CONFIRMED |
| +0x5E1 | 3 | — | Alignment padding to reach 1504 | CONFIRMED (derived) |

**Trailing sub-entries (N × 8 bytes):** same structure as items.scr trailing entries; all fields
UNVERIFIED.

### 2.9 mobs.scr — Mob catalogue (stride: 488 bytes)

**Wave-7 blocker: RESOLVED (stride, ID field, and boss-type byte confirmed).**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Mob ID (map key) | — | CONFIRMED |
| +2 | 246 | ? | Fields between ID and qword field | UNVERIFIED | UNVERIFIED |
| +248 | 8 | i64 | Timer / uptime field | Engine adds 10 on load (spawn timer?) | CONFIRMED (offset) |
| +256 | 68 | ? | Fields between qword and type byte | UNVERIFIED | UNVERIFIED |
| +324 | 1 | u8 | Mob type | Value `11` = boss / elite | CONFIRMED |
| +325 | 163 | ? | Remaining fields | UNVERIFIED | UNVERIFIED |

Total: 488 bytes. Boss-type mobs (type byte = 11) are inserted into a separate runtime index
in addition to the main map.

### 2.10 npcs.scr — NPC catalogue (stride: 1916 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 2 | u16 | NPC ID (map key) | CONFIRMED |
| +2 | 1914 | ? | Name(s) and other fields | UNVERIFIED |

The loader allocates a 957-element `u16` array per record on the stack, suggesting that the
record body contains text encoded as 2-byte characters (UCS-2 or EUC-KR). The exact character
encoding and field layout are UNVERIFIED.

### 2.11 items_extra.do — Item extended data (stride: 48 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0 | 48 | ? | All fields UNVERIFIED | UNVERIFIED |

### 2.12 .xdb files — Runtime sub-system tables

Binary table files; confirmed paths in the engine string table:

- `data/script/buff_icon_position.xdb`
- `data/script/actor_size.xdb`
- `data/script/vehicle.xdb`
- `data/script/creature_item.xdb`
- `data/script/effectscale.xdb`

Loaders were not traced. Record stride, field layout, and key scheme are all UNVERIFIED.

---

## Section 3 — .ini files (client configuration, disk-only)

### 3.1 Location and storage

The five INI files are stored **on disk** in the executable directory, not inside the VFS.
They are created with the hidden file attribute. Their paths are assembled at startup.

| Slot | Filename | Purpose | Confidence |
|---|---|---|---|
| 1 | `DoOption.ini` | Main graphics / sound / UI options | CONFIRMED |
| 2 | `option.ini` | Secondary options | UNVERIFIED |
| 3 | `panel.ini` | UI panel layout state | UNVERIFIED (inferred) |
| 4 | `combo.ini` | Combo / hotkey bindings | UNVERIFIED (inferred) |
| 5 | `TSIDX.ini` | Tab / session index | UNVERIFIED |

All five are read via the Windows INI API (`GetPrivateProfileIntA` /
`GetPrivateProfileStringA`). A parser need not implement binary INI parsing; the Windows API or
any standard INI library suffices.

### 3.2 DoOption.ini — Section [DO_OPTION]

Single section. All keys are integers except `OPTION_ID` which is a string.

| Key | Value type | Default | Valid range | Notes | Confidence |
|---|---|---|---|---|---|
| `OPTION_WIDTH` | int | 1024 | 800 – 1920 | Screen width, pixels | CONFIRMED |
| `OPTION_HEIGHT` | int | 768 | 600 – 1200 | Screen height, pixels | CONFIRMED |
| `OPTION_COLORBIT` | int | 32 | 16 or 32 | Colour depth | CONFIRMED |
| `OPTION_LANG` | int | 1 | 1 – 3 | Language selection | CONFIRMED |
| `OPTION_VIEW_CHAR` | int | 1 | 0 / 1 | Character render enable | CONFIRMED |
| `OPTION_VIEW_BACK` | int | 1 | 0 / 1 | Background render enable | CONFIRMED |
| `OPTION_GROUND` | int | 1 | 0 / 1 | Ground render enable | CONFIRMED |
| `OPTION_SKY` | int | 1 | 0 / 1 | Sky render enable | CONFIRMED |
| `OPTION_WEATHER` | int | 1 | 0 / 1 | Weather system enable | CONFIRMED |
| `OPTION_WATER` | int | 1 | 0 / 1 | Water render enable | CONFIRMED |
| `OPTION_SHADOW` | int | 1 | 0 / 1 | Shadow render enable | CONFIRMED |
| `OPTION_DMGTEXT` | int | 1 | 0 / 1 | Damage floating text enable | CONFIRMED |
| `OPTION_TEX_CHAR` | int | 1 | 0 / 1 | Character texture quality | CONFIRMED |
| `OPTION_TEX_MOB` | int | 1 | 0 / 1 | Mob texture quality | CONFIRMED |
| `OPTION_TEX_ITEM` | int | 1 | 0 / 1 | Item texture quality | CONFIRMED |
| `OPTION_TEX_ETC` | int | 1 | 0 / 1 | Misc texture quality | CONFIRMED |
| `OPTION_SOUND_CHAR` | int | 1 | 0 / 1 | Character sound enable | CONFIRMED |
| `OPTION_SOUND_MOB` | int | — | 0 / 1 | Mob sound enable | CONFIRMED (key); default UNVERIFIED |
| `OPTION_SOUND_TERRAIN` | int | 1 | 0 / 1 | Terrain sound enable | CONFIRMED |
| `OPTION_SOUND_MUSIC` | int | 1 | 0 / 1 | Music enable | CONFIRMED |
| `OPTION_SCREENMODE` | int | 0 | 0 / 1 | Fullscreen mode | CONFIRMED |
| `OPTION_SOUNDVOL_CHAR` | int | 100 | 0 – 100 | Character SFX volume | CONFIRMED |
| `OPTION_SOUNDVOL_MOB` | int | 100 | 0 – 100 | Mob SFX volume | CONFIRMED |
| `OPTION_SOUNDVOL_BACK` | int | 100 | 0 – 100 | Background SFX volume | CONFIRMED |
| `OPTION_SOUNDBOL_MUSIC` | int | 100 | 0 – 100 | Music volume (note: key typo `BOL` not `VOL`) | CONFIRMED |
| `OPTION_EFFECT` | int | 100 | 0 – 100 | Effect quality / density | CONFIRMED |
| `OPTION_BRIGHT` | int | 100 | 0 – 100 | Brightness | CONFIRMED |
| `OPTION_STALL_NOTIFY` | int | 0 | 0 / 1 | Stall notification | CONFIRMED |
| `OPTION_WHISPER_NOTIFY` | int | 0 | 0 / 1 | Whisper notification | CONFIRMED |
| `OPTION_FORCE_NOTIFY` | int | 0 | 0 / 1 | Force notification | CONFIRMED |
| `OPTION_ID` | string | (empty) | max 15 chars | Character / account ID for auto-login | CONFIRMED |

One field in the runtime options struct (index position +18 in read order) is read by the
loader but its key name is UNVERIFIED. `OPTION_SCREENMODE` occupies field index +30 (the read
order is non-contiguous).

### 3.3 Other INI files

Keys for `option.ini`, `panel.ini`, `combo.ini`, and `TSIDX.ini` were not traced in this
session. Inferred purposes from the filenames are noted in section 3.1 but are UNVERIFIED.

---

## Section 4 — CVersion data file (.dat, version manifest only)

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

1. **exp.scr bytes +10 through +19** — meaning of the remaining 10 bytes in each 20-byte record.
2. **userlevel.scr field layout** — the 58 bytes after the `u16` level key: field names, types,
   and order for all stat bases.
3. **userpoint.scr field layout** — the 30 bytes after the `u16` key: what stat curve data is
   stored and how it maps to the runtime allocator.
4. **users.scr internal layout** — which bytes are `A` and `B` in the `(10/A)*B` grid formula;
   how the three character classes are distinguished.
5. **items.scr ID field width** — the item ID at offset +0 is the first field, but whether it
   is a `u16` or `u32` is UNVERIFIED.
6. **items.scr fields from +0 to +0xD1 and from +0xE9 to +0x21F** — the majority of the item
   record body.
7. **skills.scr body** — the 1500 bytes of main skill data before the trailing count byte.
8. **mobs.scr** — 246 bytes between the mob ID and the timer field, and 163 bytes after the
   type byte.
9. **npcs.scr** — character encoding of the name data (UCS-2 vs EUC-KR) and all other fields.
10. **items_extra.do** — entire 48-byte record layout.
11. **All .xdb files** — record stride, key scheme, and field layout.
12. **option.ini / panel.ini / combo.ini / TSIDX.ini keys** — loaders not traced.
13. **INI field at read-order index +18** — key name and purpose UNVERIFIED.
14. **npc.scr** — whether it is a sub-table of npcs.scr or independent; stride UNVERIFIED.
15. **UNVERIFIED stride .scr files** — all files listed as UNVERIFIED in section 2.2.

---

## Cross-references

- VFS container layout: `Docs/RE/formats/pak.md`
- Sound event tables sharing the VFS path convention: `Docs/RE/formats/sound_tables.md`
- Mob type byte (value 11 = boss) relates to the spawn descriptor: `Docs/RE/structs/spawn_descriptor.md`
- Item category flags relate to: `Docs/RE/structs/item.md`
- Skill catalogue relates to: `Docs/RE/structs/skill.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
