# Format: .txt (bulk CP949 data tables — census + per-table column specs)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every loader an engineer writes for a
> table described here must cite `// spec: Docs/RE/formats/text_tables.md`.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> **Scope.** This document is the catalogue for the broad `.txt` population inside the VFS that is
> not already given its own dedicated format doc. It does three things:
> 1. **Census** of the full `.txt` population by directory/pattern (§1).
> 2. **Pointers, not duplicates,** for the `.txt` tables already specified elsewhere — these are
>    listed once with their covering spec so engineers go to the authoritative file (§2).
> 3. **Full per-table column specs** for the previously-undocumented standalone tables (§3+),
>    with delimiter, column order, encoding, and a confidence tag per fact.
>
> **Encoding (whole document):** all string fields are **CP949** (EUC-KR superset, the legacy
> Korean code page). Numeric and keyword fields are the ASCII subset of CP949. Line endings are
> **CRLF** unless a table is explicitly noted as LF-only. There is no magic number and no version
> field — every file is identified by its VFS path. Endianness is not applicable (text format).

---

## Identification

- **Extension:** `.txt`
- **Found in:** inside the VFS archive (`data.inf` + `data/data.vfs`); see `formats/pak.md` for
  VFS lookup. Files are identified by logical path, never by content magic.
- **Total `.txt` population observed:** 619 files in the production VFS census.
- **Three broad classes:** (a) already-specced manifests/tables — §2; (b) human-readable companions
  to the sky/environment `.bin` family — §4 / cross-ref `environment_bins.md`; (c) standalone tables
  documented for the first time here — §3, §5.

---

## 1. Census by directory / pattern

| Directory                | Pattern(s)                                                              | Approx. count | Where specified |
|--------------------------|------------------------------------------------------------------------|--------------:|-----------------|
| `data/char/`             | actormotion, bindlist, emoticon, motlist, sameemoticon, skin, skinlist, tex*list, userjoint, temp | 13 | §2 (specced) + §5 (char sub-files) |
| `data/cursor/`           | curse.txt, cursechat.txt                                                | 2 | §3.1 |
| `data/effect/`           | bmplist, itemjointeff, itemswordlight, map001..map300, mobjointeff, mobswordlight, texture/bgtexture, totalmugong, xeffect, xobj | ~38 | §2 (specced) + §3.2 + §3.6 |
| `data/item/`             | effectlist, skinlist, texturelist, temp                                 | 4 | §2 + §3.3 + §5 |
| `data/mapNNN/dat/`       | log.txt (areas 000–047, 201–205)                                        | 16 | §3.4 (inert) |
| `data/script/`           | angerlevel, eventuser, product, userlist                               | 4 | §3.5 |
| `data/sky/dat/`          | cloud_cycle*, clouddome*, fog*, light*, map_option*, material*, point_light*, stardome*, weather*, wind* | ~280 | §4 (companions to `environment_bins.md`) |
| `data/sky/lensflare.txt` | lensflare                                                              | 1 | §3.7 |
| `data/sky/map/`          | cloud_cycle*, clouddome*, cloudpattern*, fog*, light*, light_map*, map*, map_option*, material*, point_light*, stardome*, weather*, wind* | ~100 | §4.x |
| `data/test_local.txt`    | (root)                                                                  | 1 | §3.8 (dev-only) |
| `data/ui/...`            | uitex.txt, skillicon.txt, crestlist.txt                                 | 3 | §2 (specced) |

Counts are approximate where a sub-family spans many per-area/per-day variants; they reconcile to
the 619-file total. The bulk of the population (~380 files) is the sky/environment companion set
(§4), which mirrors the already-byte-confirmed `.bin` family in `environment_bins.md`.

> **Census reconciliation (VFS-DEEP-II):** the residual `.txt`/`.csv`/`.scr` text-surface sweep
> confirmed the 619-file `.txt` total above is complete — no previously-uncounted directory or hidden
> sub-family was found, and every path resolves against this census. The one **net-new format** found
> in that sweep is `data/script/items.csv` (the only `.csv` in the VFS) — documented in its own spec
> `formats/items_csv.md`, not here.

---

## 2. Already-specced `.txt` files — go to the covering spec (do NOT re-implement here)

These tables have authoritative documentation elsewhere. They are listed once so a loader engineer
knows the canonical source. **Do not re-document them in this file.**

| File (canonical VFS path)                 | Covering spec                                   | Confidence |
|-------------------------------------------|-------------------------------------------------|------------|
| `data/char/actormotion.txt`               | `formats/actormotion.md`, `formats/animation.md`| CONFIRMED  |
| `data/char/bindlist.txt`                  | `formats/bindlist.md`                            | CONFIRMED  |
| `data/char/skin.txt`                      | `formats/bgtexture_lst.md` (skin chain)         | CONFIRMED  |
| `data/char/motlist.txt`                   | `formats/animation.md`                           | CONFIRMED  |
| `data/map000/texture/bgtexture.txt`       | `formats/bgtexture_lst.md` (text mirror)        | CONFIRMED  |
| `data/effect/texture/bgtexture.txt`       | `formats/bgtexture_lst.md` (text mirror)        | CONFIRMED  |
| `data/ui/uitex.txt`                       | `formats/ui_manifests.md §1`                     | CONFIRMED  |
| `data/ui/skillicon/skillicon.txt`         | `formats/ui_manifests.md §2`                     | CONFIRMED  |
| `data/ui/guildicon/crestlist.txt`         | `formats/ui_manifests.md §3`                     | SAMPLE-VERIFIED |
| `data/item/texturelist.txt`               | `formats/ui_manifests.md §10`                    | CODE-CONFIRMED |
| `data/effect/itemjointeff.txt`            | `formats/effects.md §F.2`                         | CONFIRMED  |
| `data/effect/mobjointeff.txt`             | `formats/effects.md §F.3`                         | CONFIRMED  |
| `data/effect/totalmugong.txt`             | `formats/effects.md §F.4`                         | CONFIRMED  |
| `data/effect/itemswordlight.txt`          | `formats/effects.md §F.5`                         | CONFIRMED  |
| `data/effect/mobswordlight.txt`           | `formats/effects.md §F.6`                         | CONFIRMED  |
| `data/sky/dat/cloud_cycle*.txt`           | `formats/environment_bins.md` (TSV companion)    | CONFIRMED  |

Sky/environment `.bin`-family **companions** (`fog`, `material`, `stardome`, `clouddome`,
`map_option`, `weather`, `wind`, `point_light`, `light`) are covered as a group in §4 of this file
and defer to `formats/environment_bins.md` / `formats/sky.md` for the authoritative colour/option
tables. The `.txt` companions documented here add only the readable column ordering.

---

## 3. Standalone tables (first documented here)

### 3.1 Chat-filter tables — `data/cursor/curse.txt`, `data/cursor/cursechat.txt`

- **Delimiter:** TAB. One pair per line; CRLF line endings.
- **Header:** a `;`-prefixed comment preamble (revision/identification comments), then headerless
  data rows.
- **Encoding:** CP949 (Korean profanity filter; both columns are CP949 strings).

| col# | type   | role                              | confidence |
|------|--------|-----------------------------------|------------|
| 0    | string | bad word to filter                | HIGH       |
| 1    | string | clean replacement to substitute   | HIGH       |

`cursechat.txt` shares the same 2-column schema with a slightly different word set. Consumed by the
chat-filter subsystem at load time; no join to other tables.
**Proposed canonical names:** `curse_filter_table`, `cursechat_filter_table`.
**Verification:** both files observed; schema uniform across visible rows. No IDA cross-check.

---

### 3.2 Effect manifests — `data/effect/xeffect.txt`, `data/effect/xobj.txt`

#### 3.2.1 `xeffect.txt` — count-prefixed `.xeff` filename manifest

- **Line 0:** integer total count.
- **Lines 1..N:** one bare `.xeff` filename per line, no path prefix. ASCII.

| line   | type   | role                                   | confidence |
|--------|--------|----------------------------------------|------------|
| 0      | u32    | total entry count (header line)        | HIGH       |
| 1..N   | string | bare `.xeff` filename (no path prefix) | HIGH       |

**Cross-file join:** the **0-based line index into the filename list** is the `effect_slot_index`
referenced by `itemjointeff.txt` and `mobjointeff.txt`. Binary companion `data/effect/xeffect.lst`
(fixed-width records) is the authoritative parallel manifest — see `formats/effects.md §A.9`. The
`.txt` and `.lst` are parallel views of the same pool; this is the first column spec for the `.txt`.
**Proposed canonical name:** `xeffect_manifest`.
**Verification:** single file; count line matches body row count (schema HIGH, count value SINGLE-SAMPLE).

#### 3.2.2 `xobj.txt` — count-prefixed named `.xobj` manifest

- **Line 0:** integer total count.
- **Lines 1..N:** `{index} ({zero_padded_index})-{name}.xobj` — index, parenthesised zero-padded
  index, dash, then the human-readable name including the extension. ASCII.

| line   | type   | role                                           | confidence |
|--------|--------|------------------------------------------------|------------|
| 0      | u32    | total entry count (header line)                | HIGH       |
| 1..N   | string | `{idx} ({idx_padded})-{name}.xobj` entry line  | HIGH       |

**Cross-file join:** provides the named `.xobj` geometry shapes used by `.xeff` elements.
**Proposed canonical name:** `xobj_manifest`. **Verification:** single file.

---

### 3.3 `data/item/effectlist.txt` — item effect-texture list

- **Structure:** one bare `.dds` filename per line. No header, no count. ASCII.

| col# | type   | role                                                   | confidence |
|------|--------|--------------------------------------------------------|------------|
| 0    | string | bare DDS filename (item effect texture, no path prefix)| HIGH       |

**Cross-file join:** the 0-based line index maps to the `effect_frame_id` used by the item
rendering system; line count determines the pool size (no explicit count header).
**Proposed canonical name:** `item_effect_texture_list`. **Verification:** single file.

---

### 3.4 `data/mapNNN/dat/log.txt` (16 files) — INERT BUILD ARTIFACT

Plain-text error logs dumped by the map/collision pre-processor during asset compilation. Contents
are English error messages embedding absolute developer filesystem paths (with one CP949 Korean
path component). **Not a runtime data table** — no column structure, not load-bearing. The engine
does not consume them; they were packed into the VFS as-is during compilation.
**Proposed treatment:** document as inert build artifact; **no parser to be written.**

---

### 3.5 `data/script/` tables

#### 3.5.1 `angerlevel.txt`

- **Delimiter:** TAB; 3 columns; CRLF. No header row (first row is data). All values integer.

| col# | type | role                                            | confidence |
|------|------|-------------------------------------------------|------------|
| 0    | u32  | level index (1-based, sequential)               | HIGH       |
| 1    | u32  | accumulated anger/aggro threshold at this level | HIGH (proposed role) |
| 2    | u32  | tier/band value (small int, slow growth)        | MEDIUM (proposed role) |

**Cross-file join:** keyed by level index; consumed by the NPC/mob aggro/anger system (see
`specs/combat.md`). **Proposed canonical name:** `anger_level_table`. **Verification:** single file;
column 2 semantics UNVERIFIED.

#### 3.5.2 `eventuser.txt`

- **Structure:** one ASCII username per line; **LF-only** line endings; no header, no columns.
- **Content:** specific player account names.
- **Classification:** runtime event whitelist / blocklist keyed by an event or GM system — not a
  general lookup table. **Proposed canonical name:** `event_user_whitelist`. **Verification:** single file.

#### 3.5.3 `product.txt` — NPC crafting recipe

- **Format:** keyword-block (`PRODUCT CREATE BEGIN … PRODUCT CREATE END`) with labelled fields;
  field values TAB-delimited. CP949 (Korean item names in quoted strings).

| field    | type        | role                                      | confidence |
|----------|-------------|-------------------------------------------|------------|
| ID       | u32         | NPC id this recipe belongs to             | HIGH       |
| STUFF    | string list | raw-material item names (quoted, CP949)   | HIGH       |
| PRODUCT  | string      | result item name (quoted, CP949)          | HIGH       |

**Cross-file join:** string-keyed by CP949 item name (no numeric item id observed) — weak join.
**Proposed canonical name:** `npc_product_recipe`. **Verification:** single file, single record.

#### 3.5.4 `userlist.txt`

- **Structure:** line 0 = integer count, then one username per line. Minimal GM/event registry;
  same purpose as `eventuser.txt` but very short (likely a dev/test remnant). Runtime relevance
  UNVERIFIED. **Verification:** single file, single entry.

> **Note on `data/script/items.csv`.** The only `.csv` in the VFS lives in this directory but is a
> distinct format (comma-delimited, LF-only, ~33 MB, two parser hazards). It has its own spec —
> see `formats/items_csv.md`. Do not parse it with the TAB-table loaders above.

---

### 3.6 `data/effect/bmplist.txt` — TGA texture manifest (alternating-line model CONFIRMED)

- **Structure:** **CONFIRMED** alternating-line model. Line 0 is the runtime entry count; then for
  each entry, a `.tga` filename line (odd) followed by an integer line (even). The integer on each
  even line is a **sequential 0-based ordinal** (0, 1, 2, …) — it enumerates the entry, it is **not**
  an independent lookup key; the entry's slot identity is carried by its position. ASCII.

| line          | type   | role                                                          | confidence |
|---------------|--------|--------------------------------------------------------------|------------|
| 0             | u32    | runtime entry count (the usable pool size)                   | HIGH       |
| 2k+1 (k≥0)    | string | bare `.tga` texture filename (no path prefix)                | HIGH       |
| 2k+2 (k≥0)    | u32    | sequential entry ordinal (0-based, equals k)                 | HIGH (ordinal); LOW (any deeper semantic) |

**Cross-file join:** text companion to `data/effect/bmplist.lst` (fixed-width binary, see
`formats/effects.md §A.10`). Binary companion shape: a `u32` little-endian count at offset 0, then a
flat array of fixed **30-byte** records (null-padded ASCII filename per record). The `.lst` size
reconciles exactly as `4 + 30 × record_count`.

> **Count discrepancy (`.txt` vs `.lst`) — new finding.** The `.txt` reports a runtime entry count
> that is **8 fewer** than the record count stored in the binary `.lst`. The `.lst` therefore carries
> 8 additional records (likely internal/development entries) absent from the `.txt` manifest. A
> loader that uses the `.txt` count as the authoritative pool size will not see the 8 extra `.lst`
> records; one that uses the `.lst` count will find 8 records with no `.txt` manifest line. The
> `.txt` count is the **runtime-usable** size; the discrepancy is believed intentional. IMPACT: LOW
> (these are particle-effect textures; the extra entries are unlikely to be referenced by name from
> gameplay code). The exact numeric counts are recorded in the dirty provenance note; they are
> deliberately not transcribed here.

**Proposed canonical name:** `bmp_texture_manifest`.
**Verification:** single file; alternating pattern and count-discrepancy CONFIRMED by harness sweep.

---

### 3.7 `data/sky/lensflare.txt` — lens-flare spot definitions

- **Format:** keyword-block (`SPOT N BEGIN … SPOT END`) with labelled fields. ASCII.

| field      | type | role                                                     | confidence |
|------------|------|----------------------------------------------------------|------------|
| TEXTURE_ID | u32  | index into the lens-flare texture pool                   | HIGH       |
| RADIUS     | float| screen-space radius (fraction of screen height)          | HIGH       |
| POSITION   | float| position along the sun-to-centre axis (0 = sun, 1 = centre) | HIGH    |
| COLOR      | u8×4 | RGBA tint for this flare spot                             | HIGH (type), MEDIUM (channel order) |

**Cross-file join:** `TEXTURE_ID` references `data/sky/texture/lensflare{N}.dds` (see
`formats/environment_bins.md`). **Proposed canonical name:** `sky_lensflare_def`.
**Verification:** single file; channel order of `COLOR` UNVERIFIED.

---

### 3.8 `data/test_local.txt` — dev-only respawn/time table

- **Format:** keyword-block records (`MAP BEGIN … MAP END`) with labelled fields. CP949.

| field     | type    | role                                            | confidence |
|-----------|---------|-------------------------------------------------|------------|
| MAP_ID    | u32     | area/zone id                                    | HIGH       |
| MAP_DAY   | u32     | starting day offset                             | HIGH       |
| MAP_SEC   | u32     | starting time in seconds (21600 = 06:00)        | HIGH       |
| MAP_SPEED | u32     | time-acceleration factor                        | HIGH       |
| LOCATION  | float×2 | spawn XZ world coordinates                      | HIGH       |

**Classification:** test/debug table (filename prefix `test_local` indicates dev-only usage); likely
not loaded by the shipping client. **Verification:** single file, multiple MAP records observed.

---

## 4. Sky / environment companions (`data/sky/dat/`, `data/sky/map/`)

The `data/sky/` `.txt` files are **human-readable companions** to the binary sky/environment family.
**The authoritative byte/colour/option tables live in `formats/environment_bins.md`** (sample-verified
against real archive bytes) and `formats/sky.md` (parser read-order). This section documents only the
readable column ordering of the `.txt` companions so an engineer who prefers the text form can parse
it. Where a column meaning is defined in `environment_bins.md`, that file wins.

All companions are TAB-separated, CRLF, CP949 (ASCII subset for the numeric/keyword data). Most
sub-families are **time-keyed** (rows labelled `HH:MM` or `{day} Day`) and end each data row with an
`end` sentinel token.

### 4.1 `map_option{N}.txt` — sky/option flags (key-value)

One key-value pair per line, TAB between key and value, with optional inline `#` comments. Companion
to `map_option{N}.bin` (see `environment_bins.md §B.1`, authoritative).

| field        | type | values             | meaning                    | confidence |
|--------------|------|--------------------|----------------------------|------------|
| MOVE_DUNGEON | u8   | 0 = field, 1 = dungeon | area type selector     | HIGH       |
| SIGHT_FIX    | u32  | 0 = free, 1–1000   | max camera sight distance  | HIGH       |
| LENSFLARE    | u8   | 0/1                | enable lens flare          | HIGH       |
| STARDOME     | u8   | 0/1                | enable star-dome render    | HIGH       |
| CLOUDDOME    | u8   | 0/1                | enable cloud-dome render   | HIGH       |
| SUN          | u8   | 0/1                | render sun object          | HIGH       |
| MOON         | u8   | 0/1                | render moon object         | HIGH       |
| SKYBOX       | u8   | 0/1                | render skybox cube         | HIGH       |
| MAPHIDE      | u8   | 0/1                | hide minimap for this area | HIGH       |

A variant under `data/sky/map/` named `map{N}.txt` uses the same key-value schema with a slightly
different field-name set (`DUNGEON` in place of `MOVE_DUNGEON`/`SIGHT_FIX`) — likely an older format.

### 4.2 `fog{N}.txt` — time-keyed fog table

Row label `HH:MM`, then three integer components, then `end`. Companion to `fog{N}.bin`.

| col# | type   | role                                | confidence |
|------|--------|-------------------------------------|------------|
| 0    | string | time label `HH:MM`                  | HIGH       |
| 1–3  | u32    | fog colour/density components       | HIGH       |
| last | string | `end` sentinel                      | HIGH       |

**Proposed canonical name:** `sky_fog_table`.

### 4.3 `wind{N}.txt` — wind-zone table

Header lines `WIND COUNT {n}` and `WIND OBJECT {n}` give the record count, then a column-label line,
then one data row per wind zone. Companion to `wind{N}.bin`.

| col# | type   | role                          | confidence |
|------|--------|-------------------------------|------------|
| 0    | float  | wind speed (1–10)             | HIGH       |
| 1    | float  | wind init offset (0–2048)     | HIGH       |
| 2    | float  | wind delay (1–20)             | HIGH       |
| 3    | u32    | texture index (0 = none)      | HIGH       |
| last | string | `end` sentinel                | HIGH       |

**Record count source:** the `WIND COUNT` header line. **Proposed canonical name:** `sky_wind_table`.

### 4.4 `point_light{N}.txt` — point-light table

Header lines `POINT LIGHT VIEW {dist} end` (view cull distance) and `POINT LIGHT COUNT {n} end`
(record count), a column-label line, then one data row per light. Companion to `point_light{N}.bin`.

| col# | type     | role                              | confidence |
|------|----------|-----------------------------------|------------|
| 0–2  | u8/float | AMBIENT r, g, b                   | HIGH       |
| 3–5  | u8/float | DIFFUSE r, g, b                   | HIGH       |
| 6–8  | u8/float | SPECULAR r, g, b                  | HIGH       |
| 9    | float    | X world position                  | HIGH       |
| 10   | float    | Y world position                  | HIGH       |
| 11   | float    | Z world position                  | HIGH       |
| 12   | float    | light range                       | HIGH       |
| 13   | u8       | Always (1 = always-on)            | HIGH       |
| 14   | u8       | Swing (0 = no swing animation)    | HIGH       |
| last | string   | `end` sentinel                    | HIGH       |

**Record count source:** the `POINT LIGHT COUNT` header line. **Proposed canonical name:** `sky_point_light_table`.

### 4.5 `weather{N}.txt` / `weather{N}_rain.txt` — day×hour weather grid

Column-header row labels `0 Hour`..`23 Hour`; data rows `{day} Day` + 24 integer values + `end`
(0 = clear; non-zero = precipitation-type index). Companion to `weather{N}.bin`.

**`_rain` variant — same schema CONFIRMED.** The `weather{N}_rain.txt` files decode with the **exact
same structure** as the base `weather{N}.txt`: same delimiter, same column-header row, same row count,
same `end` sentinel. The `_rain` suffix selects different precipitation-type indices within the same
grid — it does **not** add fields. The small file-size difference between a base file and its `_rain`
sibling is explained by larger integer values (more digits) in non-zero cells, not by extra columns.
This promotes the previously-UNVERIFIED `_rain` note to **CONFIRMED**.

| col# | type   | role                                          | confidence |
|------|--------|-----------------------------------------------|------------|
| 0    | string | `{day} Day` row label                         | HIGH       |
| 1–24 | u32    | per-hour precipitation-type index (0 = clear) | HIGH       |
| last | string | `end` sentinel                                | HIGH       |

**Proposed canonical name:** `sky_weather_grid` (shared by base and `_rain` variants).

### 4.6 `cloud_cycle{N}.txt` — day-keyed cloud cycle

Row label `{day} Day`, then a cloud-speed value and per-time-slot cloud texture indices, terminated
by `end`. Already noted as a TSV companion in `environment_bins.md` (authoritative); the column
breakdown here is: 1 label column + 7 data columns + `end` sentinel.

### 4.7 Wide colour-grid companions — `light{N}.txt`, `material{N}.txt`, `clouddome{N}.txt`, `stardome{N}.txt`

These are very wide, time-keyed colour/brightness grids (named column groups such as AMBIENT /
DIFFUSE / SPECULAR / EMISSIVE r,g,b, plus POWER, character-light, star brightness). They are the
text mirrors of the corresponding `.bin` files; **`environment_bins.md` is authoritative** for the
colour values. Exact total column counts for these wide tables are UNVERIFIED at the column level
from the census head and should be confirmed against `environment_bins.md` before a text-form loader
is written — preferring the `.bin` form is recommended.

### 4.8 `data/sky/map/` unique sub-families

The `data/sky/map/` directory holds world-map / lobby-backdrop sky data. Most sub-families share the
`data/sky/dat/` schemas above. Three are unique to this directory:

- **`cloudpattern{area}_{N}.txt`** — key-value lines: `CLOUD_SPEED` (comma-separated speeds per cloud
  layer), `CLOUD1` (comma-separated texture indices, count ~30), `CLOUD2` (comma-separated indices,
  count ~60). Global cloud pattern parameters. **Proposed canonical name:** `sky_cloudpattern`.
- **`light_map{N}.txt`** — keyword-block (`LIGHT n BEGIN … LIGHT n END`) point-light definitions with
  labelled fields `Ambient`/`Diffuse`/`Specular` (float×3 RGB each), `Position` (float×3 XYZ),
  `Range` (float), `Always` (u8), `Swing` (u8). Distinct from `point_light{N}.txt` (which uses the
  tabular `POINT LIGHT COUNT` form). **Proposed canonical name:** `sky_light_map`.
- **`map{N}.txt`** — see §4.1 (key-value option set, older field names). One outlier file in this set
  is far larger than the others and does **not** match the small key-value schema; that specific file
  is SAMPLE-UNVERIFIED (see Known Unknowns).

---

## 5. `data/char/` sub-files

| File                              | Structure                                                          | Status |
|-----------------------------------|-------------------------------------------------------------------|--------|
| `data/char/skinlist.txt`          | one bare `.skn` filename per line; no header, no count             | MANIFEST |
| `data/item/skinlist.txt`          | one bare `.skn` filename per line (item skin filenames)            | MANIFEST |
| `data/char/tex10241024list.txt`   | one bare `.png` filename per line (char textures, 1024×1024)       | MANIFEST |
| `data/char/tex512512list.txt`     | one bare `.png` filename per line (512×512)                        | MANIFEST |
| `data/char/tex256256list.txt`     | one bare `.png` filename per line (256×256)                        | MANIFEST |
| `data/char/tex256512list.txt`     | one bare `.png` filename per line (256×512)                        | MANIFEST |
| `data/char/sameemoticon.txt`      | TAB; 2 columns (emote_id + CP949 trigger-string); no header, no count prefix; many aliases → one emote | CONFIRMED (§5.3) |
| `data/char/temp.txt`, `data/item/temp.txt` | dev-tool file listings inside the directory                | INERT BUILD ARTIFACT |

The `*list.txt` manifests follow the same bare-filename-per-line pattern as `motlist.txt` (see
`formats/animation.md`); the engine prepends the appropriate directory prefix at load.

### 5.1 `data/char/emoticon.txt` — emote table (12-column schema CONFIRMED)

- **Delimiter:** TAB; CRLF; CP949.
- **Line 0 is a count integer** (the number of data rows) — **not** a column-header row. Data starts
  at line 1. This is the same `{count}\r\n{data rows…}` count-prefix pattern as `actormotion.txt`
  and `effect/mapNNN.txt`. A parser that treats line 0 as a header row (or as data) will misalign
  the table.
- **Column count: 12, CONFIRMED** (resolves the prior "column count UNVERIFIED" note). Each row is
  one emote: 4 leading fields, then 8 per-class-group animation ids.

| col#  | type   | role                                                                  | confidence |
|-------|--------|----------------------------------------------------------------------|------------|
| 0     | u32    | `emote_id` (0-based, sequential)                                      | HIGH       |
| 1     | string | `emote_name` — emote command / label (CP949)                         | HIGH       |
| 2     | u32    | `enter_state` — `emote_id` of a state that must be active first (0 = any) | HIGH (type); MEDIUM (exact semantic) |
| 3     | u32    | `next_state` — `emote_id` to transition into (0 = none / self-loop)  | HIGH (type); MEDIUM (exact semantic) |
| 4     | u32    | `anim_id` for class group 1 (9-digit animation id; 0 = not applicable) | HIGH     |
| 5     | u32    | `anim_id` for class group 2                                           | HIGH       |
| 6     | u32    | `anim_id` for class group 3                                           | HIGH       |
| 7     | u32    | `anim_id` for class group 4                                           | HIGH       |
| 8     | u32    | `anim_id` for class group 5                                           | HIGH       |
| 9     | u32    | `anim_id` for class group 6                                           | HIGH       |
| 10    | u32    | `anim_id` for class group 7                                           | HIGH       |
| 11    | u32    | `anim_id` for class group 8                                           | HIGH       |

**Animation-id encoding.** Each 9-digit id encodes its class group in its leading digits; the 8
columns split into two character families (four class groups each), consistent with the skin-class
id patterns seen in `actormotion.txt`. Emotes that do not apply to a given group store `0` in that
column.

**State-machine columns (2/3).** Columns 2 and 3 form a simple emote state machine: an emote with a
non-zero `enter_state` may only play from that state and exits to `next_state`. Role is HIGH-confidence
from observed value patterns; the **exact** transition semantics still want an IDA cross-check.

**Proposed canonical name:** `emoticon_table`; columns `emote_id`, `emote_name`, `enter_state`,
`next_state`, `anim_id_{group}` (×8). **Verification:** single file; 12-column shape CONFIRMED across
all visible rows. IDA cross-check of cols 2/3 semantics pending.

### 5.2 `data/char/userjoint.txt` — joint table (5 columns; count-prefixed)

- **Delimiter:** TAB; CRLF; CP949 (content is pure ASCII / integers).
- **Line 0 is a count integer** (the number of joint records) — not a column header. Data starts at
  line 1.
- **Column count: 5, CONFIRMED** (a 1-based index column + 4 value columns).

| col# | type | role                                                       | confidence |
|------|------|------------------------------------------------------------|------------|
| 0    | u32  | `joint_index` (1-based, sequential)                        | HIGH       |
| 1    | u32  | primary value (small int; recurs in runs across joint groups; likely a bone index) | MEDIUM |
| 2    | u32  | secondary value (small int; recurs alongside col 1)        | MEDIUM     |
| 3    | u32  | tertiary value (sparse non-zero; likely a secondary attachment/rotation slot) | MEDIUM (sparse) |
| 4    | u32  | quaternary value (usually 0; sometimes duplicates col 2)   | LOW        |

**Pattern observation.** Consecutive joint records share the same (col 1, col 2) pair in runs, which
reads like a (bone_index, attachment) pair carried per joint group; many records are all-zero in
cols 1–4 (apparently unused joint slots). **Whether col 1 is a bone index or a pixel/coordinate
offset — and the meaning of col 4 — remains UNVERIFIED and needs an IDA cross-check.**

**Proposed canonical name:** `user_joint_table`. **Verification:** single file; 5-column shape
CONFIRMED; column 1–4 semantics UNVERIFIED.

### 5.3 `data/char/sameemoticon.txt` — emote alias table (CONFIRMED)

- **Delimiter:** TAB; CRLF; CP949 (Korean alias strings).
- **No header and no count prefix** — row 0 is data. A parser that expects a leading count line (as
  `emoticon.txt` has) will misparse this file.
- **Column count: 2, CONFIRMED.**

| col# | type   | role                                                   | confidence |
|------|--------|--------------------------------------------------------|------------|
| 0    | u32    | `emote_id` — references `emoticon.txt` col 0           | HIGH       |
| 1    | string | typed-text alias that triggers this emote (CP949)      | HIGH       |

Multiple rows may share one `emote_id`, giving a many-aliases-to-one-emote mapping.
**Cross-file join:** col 0 → `emoticon.txt` `emote_id` (§5.1).
**Proposed canonical name:** `same_emoticon_alias_table`. **Verification:** single file; schema uniform.

---

## 6. `data/effect/map{NNN}.txt` (17 files) — area effect placement (load-bearing)

- **Paths:** `data/effect/map001.txt` … `data/effect/map300.txt` (not all area IDs present).
- **Delimiter:** TAB; line 0 = record count; data rows TAB-separated; CRLF; CP949 (ASCII data).

| col# | type  | role                                            | confidence |
|------|-------|-------------------------------------------------|------------|
| 0 (line 0) | u32 | record count (header line)                  | HIGH       |
| 1    | u32   | effect_id (9-digit, `370xxxxxx` range)          | HIGH       |
| 2    | float | world X position                                | HIGH       |
| 3    | float | world Y position                                | HIGH       |
| 4    | float | world Z position                                | HIGH       |
| 5    | float | scale factor                                    | HIGH       |
| 6    | u32   | likely time-of-day gate start                   | MEDIUM (UNVERIFIED) |
| 7    | u32   | likely time-of-day gate end                     | MEDIUM (UNVERIFIED) |

**Record count source:** the line-0 count value.
**Cross-file join:** `effect_id` → `xeffect.txt` (slot index) → `.xeff` file.
**Classification:** per-area ambient effect placement manifest — load-bearing for area ambient
effects. Referenced in `specs/effect-scheduling.md §2` and `specs/frontend_scenes.md §3.6.5`.
**Proposed canonical name:** `area_effect_placement`.
**Verification:** 17 files; schema consistent across the largest (422 entries) and shorter files.
Columns 6/7 semantics UNVERIFIED — IDA cross-check pending.

---

## 7. Enumerations / flags

- `map_option{N}.txt` / `map{N}.txt` boolean fields: `0` = off / disabled, `1` = on / enabled (per
  field meaning in §4.1).
- `MOVE_DUNGEON`: `0` = field area, `1` = dungeon area.
- `point_light` / `light_map` `Always`: `1` = always-on, `0` = conditional. `Swing`: `0` = no swing
  animation, non-zero = swing enabled.
- `weather{N}.txt` / `weather{N}_rain.txt` grid cells: `0` = clear, non-zero = precipitation-type index.
- `emoticon.txt` animation-id columns: `0` = emote not applicable to that class group.

---

## 8. Known unknowns

- **`effect/map{N}.txt` columns 6/7** — observed integer values consistent with a time-of-day gate
  (start/end), but field names unconfirmed. IMPACT: HIGH (load-bearing file). Needs IDA cross-check.
- **`emoticon.txt` state-machine semantics (cols 2/3)** — column count is now CONFIRMED at 12 and the
  state-machine role is HIGH-confidence, but the exact `enter_state`/`next_state` transition rules
  want an IDA cross-check. IMPACT: MEDIUM.
- **`userjoint.txt` columns 1–4 semantics** — 5-column shape CONFIRMED; whether col 1 is a bone index
  or an offset, and the meaning of col 4, are UNVERIFIED. IMPACT: LOW.
- **`data/sky/map/map{N}.txt` outlier file** — one file is far larger than the others and does not
  match the small key-value schema; SAMPLE-UNVERIFIED. IMPACT: LOW.
- **Wide colour-grid companions** (`light`, `material`, `clouddome`, `stardome`) — exact column
  counts not resolved from the census head; defer to `environment_bins.md`. IMPACT: LOW–MEDIUM.
- **`product.txt` join semantics** — string-keyed by CP949 name; no numeric item id confirmed. IMPACT: LOW.
- **`lensflare.txt` COLOR channel order** — RGBA vs ARGB ordering UNVERIFIED. IMPACT: LOW.
- **`bmplist.txt` ↔ `.lst` count discrepancy** — the binary `.lst` carries 8 records the `.txt` does
  not list; believed intentional (dev entries). IMPACT: LOW.

> Resolved this sweep (no longer unknown): `emoticon.txt` column count (now 12), `userjoint.txt`
> column count (now 5), `bmplist.txt` line model (alternating, even-line = sequential ordinal), and
> `weather{N}_rain.txt` schema (identical to the base `weather{N}.txt`).

No IDA cross-check was performed for this census (black-box lane). An IDA analyst should confirm
`emoticon.txt` state-machine semantics, `userjoint.txt` joint-to-bone mapping, and `effect/map{N}.txt`
columns 6/7.

---

## 9. Inert build artifacts (no parser required)

These files were packed into the VFS during asset compilation and are not consumed at runtime:
`data/mapNNN/dat/log.txt` (16 files, §3.4), `data/char/temp.txt`, `data/item/temp.txt`, and the
`test_local.txt` dev table (§3.8) is dev-only. A faithful loader does not need to parse them.

---

## 10. Cross-references

- Already-specced `.txt`: `formats/ui_manifests.md`, `formats/effects.md`, `formats/actormotion.md`,
  `formats/animation.md`, `formats/bindlist.md`, `formats/bgtexture_lst.md`.
- The only `.csv`: `formats/items_csv.md` (`data/script/items.csv` — separate format, two hazards).
- `.scr` family (binary-only): `formats/scr.md`, `formats/config_tables.md`, `formats/items_scr.md`.
- Sky/environment authoritative tables: `formats/environment_bins.md`, `formats/sky.md`.
- Effect scheduling / placement consumers: `specs/effect-scheduling.md`, `specs/frontend_scenes.md`.
- Aggro/anger consumer: `specs/combat.md`.
- VFS lookup: `formats/pak.md`.
- Glossary: see `Docs/RE/names.yaml` (proposed names listed per-section above; orchestrator-owned).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).

> **Engineering note:** every C# loader for a table described above must cite
> `// spec: Docs/RE/formats/text_tables.md` on the magic constants / column indices it hard-codes.
