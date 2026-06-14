# Format: .scr  (client-side binary record-table container family)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file with
> `// spec: Docs/RE/formats/scr.md`.
>
> status: sample_verified (small tables surveyed below); family shape sample_verified
> Provenance: harness observation of the maintainer's legally-owned client VFS. No IDA used.

---

## Scope of this spec

This file documents the **`.scr` container family** as a whole and the **small fixed-stride
tables** surveyed during the VFS-DEEP triage of the 44 `.scr` entries under `data/script/`:

- `exp.scr`, `guildcrest.scr`, `guildposname.scr`, `itemeffect.scr`, `chivalry.scr`,
  `dashs.scr`, `helps.scr`, `helps_1.scr`
- `discript.sc` (note: extension is `.sc`, not `.scr`; included here because it is the same
  loader family and was triaged alongside)

The **large catalogues** are documented in their own specs and are NOT duplicated here:

- Item database (`items.scr`, `citems.scr`, `items_extra.do`, `itemscale.scr`) — see
  `formats/items_scr.md`.
- Event/schedule table (`events.scr`) — see `formats/events_scr.md`.
- Stat-curve and other already-resolved catalogues (`exp.scr`, `userlevel.scr`, `userpoint.scr`,
  `users.scr`, `skills.scr`, `mobs.scr`, `npcs.scr`, etc.) are also covered in depth in
  `formats/config_tables.md`. This spec is the **family-level overview plus the small-table
  survey**; where a value here overlaps `config_tables.md`, that file is the more detailed
  authority for the stat curves.

---

## Identification

- **Extension:** `.scr` (one sibling `.sc` is the same loader family)
- **Found in:** the engine VFS (`data.inf` + `data/data.vfs`) under `data/script/` and the
  variant directory `data/script_newserver/`. See `formats/pak.md` for the VFS container itself.
- **Magic / signature:** none — no file-level magic bytes and no version header for any `.scr`
  variant. Confidence: CONFIRMED (44/44 triaged entries lack a magic).
- **Version field:** none at file level.
- **Endianness:** little-endian (all integer and float fields).
- **String encoding:** all text fields are CP949 / EUC-KR (legacy Korean code page),
  null-terminated and zero-padded to a fixed field width. Strings are **embedded inside binary
  records**, not delimiter-separated rows.

> Important triage result: **all 44 `.scr` entries (and the one `.sc` sibling) are binary
> fixed-stride struct tables.** None is plain CP949 line-delimited text — there is **no**
> line-delimited / text-mode `.scr` in the VFS. Files that carry Korean text embed it as
> null-padded fixed-width fields within a binary record, so a line/CSV reader must NOT be used on
> `.scr`. **Re-verified (VFS-DEEP-II):** a fresh head-byte hexdump of a random sample of the `.scr`
> set plus the `.sc` sibling showed non-printable binary at offset 0 on every file — no text
> preamble, no TAB/LF/CRLF line patterns. Status: **CONFIRMED**. (The comma-delimited
> `data/script/items.csv` is a `.csv`, a different format with its own spec
> `formats/items_csv.md`; it is NOT a `.scr`.)

---

## Common structural pattern (family shape)

Every `.scr` (and the `.sc` sibling) in this family shares one loader shape:

- **No file header and no record-count prefix** for the flat-array variants. The loader derives
  the record count as `record_count = file_size / record_stride`.
- **Flat array of fixed-size records**, concatenated with no inter-record separator and no
  trailing terminator (apart from any stride-alignment padding inside each record).
- **Map-keyed insertion:** records are typically inserted into a runtime map/tree keyed on the
  record's first integer field (an id).
- **Embedded CP949 strings** are stored as fixed-width, null-terminated, zero-padded fields.
- **Non-flat variants exist** in the family: some files use a small file header followed by a
  fixed slot count, or a two-level (page + sub-entry) hierarchy, or an in-file offset table
  rather than a uniform stride. These are flagged per file below and in `config_tables.md`.

A parser that assumes a uniform stride for every `.scr` will be wrong for the non-flat members
(`helps.scr`, `helps_1.scr`, `events.scr`, and the trailing-sub-entry catalogues `items.scr` /
`skills.scr`). Always check the per-file note before choosing the stride model.

---

## Family inventory (triage of `data/script/`)

Strides marked **VERIFIED** divide the observed file size exactly and were corroborated by a
repeating record pattern. **CANDIDATE** strides divide the size exactly but carry an unresolved
internal-layout conflict. **(other lane)** files are large catalogues documented elsewhere.

| VFS path | Stride / model | Record count | Embedded CP949 | Confidence | Role |
|---|---|---|---|---|---|
| `data/script/exp.scr` | 20 B flat | 300 | no | VERIFIED | Level → XP table |
| `data/script/guildcrest.scr` | 20 B flat | 1300 | no | VERIFIED | Guild-crest sprite-sheet crop coords |
| `data/script/guildposname.scr` | 88 B flat | 9 | yes | VERIFIED | Guild position (rank) name table |
| `data/script/itemeffect.scr` | 4 B flat (u32 list) | 793 | no | VERIFIED | Item-effect type-code list |
| `data/script/chivalry.scr` | 24 B flat | 16 | yes | VERIFIED (stride) | Chivalry/honor rank records |
| `data/script/dashs.scr` | 199 B flat | 28 | yes | CANDIDATE (layout conflict) | Dash/evasion skill table |
| `data/script/helps.scr` | hierarchical (page + sub-entries) | see note | yes | CANDIDATE (48 vs 64 conflict) | In-game help text |
| `data/script/helps_1.scr` | header + title + 64 B entries | 1 + 1 + ≤20 | yes | CANDIDATE | Help chapter index |
| `data/script/discript.sc` | 68 B flat | 33 | unknown | VERIFIED (stride) | District/zone descriptor table |
| `data/script/autoquestion_cl.scr` | 92 B flat | 1300 | — | SAMPLE-VERIFIED | Anti-bot quiz pool (see config_tables.md) |
| `data/script/mapsetting.scr` | 84 B flat | 52 | yes | SAMPLE-VERIFIED | Per-zone map settings (see config_tables.md) |
| `data/script/npc.scr` | 404 B flat | 2510 | yes | SAMPLE-VERIFIED | NPC description-text table (see config_tables.md) |
| `data/script/quests.scr` | 3720 B sparse | 488 slots / 122 used | yes | SAMPLE-VERIFIED | Quest templates (see config_tables.md) |
| `data/script/statue.scr` | 36 B flat | — | no | (config_tables.md) | Statue/landmark table |
| `data/script/setitemname.scr` | 36 B flat | — | yes | (config_tables.md) | Set-item name table |
| `data/script/skillcategory.scr` | 564 B flat | — | yes | (config_tables.md) | Skill category table |
| `data/script/skillneedset.scr` | 4 B flat | 22 | no | (config_tables.md) | Skill prerequisite edges |
| `data/script/viplevels.scr` | 92 B flat | — | no | (config_tables.md) | VIP level table |
| `data/script/warstoneinfo.scr` | 40 B flat | 1 | yes | (config_tables.md) | War-stone region info |
| `data/script/oblist.scr` | 12 B flat | 1 | no | (config_tables.md) | Object list |
| `data/script/itemscale.scr` | 8 B flat | — | no | (items_scr.md) | Item scale/sizing table |
| `data/script/skills.scr` | 1504 B + N×8 trailing | ~194 real | yes | (config_tables.md) | Skill database |
| `data/script/mobs.scr` | 488 B flat | 3997 | yes | (config_tables.md) | Mob stat table |
| `data/script/mobsitem.scr` | flat (large) | — | yes | UNVERIFIED (stride) | Mob drop-item table |
| `data/script/npcs.scr` | 1916 B flat | — | yes | (config_tables.md) | NPC stat/placement table |
| `data/script/minds.scr` | flat | — | yes | UNVERIFIED (stride) | Mind/inner-skill table |
| `data/script/letters.scr` | flat | — | yes | UNVERIFIED (stride) | Letter/mail templates |
| `data/script/nicktofame.scr` | flat | — | yes | UNVERIFIED (stride) | Nickname-to-fame table |
| `data/script/productrandname.scr` | flat | — | yes | UNVERIFIED (stride) | Random craft name table |
| `data/script/products.scr` | 212 B flat | — | yes | SAMPLE-VERIFIED (stride only) | Crafting product table |
| `data/script/productcollect.scr` | flat (u32 arrays) | — | no | UNVERIFIED (stride) | Crafting/collection recipe table |
| `data/script/playtime_reward.scr` | small struct | — | no | UNVERIFIED (layout) | Play-time reward table |
| `data/script/repair.scr` | small struct | — | no | UNVERIFIED (layout) | Repair cost/parameter table |
| `data/script/system_control.scr` | u32/f32 pairs | — | no | UNVERIFIED (layout) | System control parameters |
| `data/script/tiphelp.scr` | flat | — | yes | UNVERIFIED (stride) | Loading-screen tip text |
| `data/script/tutor.scr` | 1660 B flat | — | yes | (config_tables.md) | Tutorial step table |
| `data/script/upgradeitems.scr` | flat (float arrays, large) | — | no | UNVERIFIED (stride) | Item upgrade recipe table |
| `data/script/userlevel.scr` | 60 B flat | 300 | no | (config_tables.md) | Per-level stat-scaling curve |
| `data/script/userpoint.scr` | 32 B flat | 301 | no | (config_tables.md) | Stat-point allocation curve |
| `data/script/users.scr` | 496 B (4×124 blocks) | 4 classes | yes | (config_tables.md) | Per-class stat-ratio grid |
| `data/script/userpoint*/userpoint.scr` variants | — | — | — | — | (as above) |
| `data/script/citems.scr` | 1052 B flat | 512 | yes | (other lane → items_scr.md) | Billing/premium item catalogue |
| `data/script/items.scr` | 548 B + N×8 trailing | — | yes | (other lane → items_scr.md) | Full item database |
| `data/script/events.scr` | indexed (offset table) | — | yes | (other lane → events_scr.md) | Event schedule/data table |
| `data/script_newserver/items.scr` | item-db variant | — | yes | (other lane → items_scr.md) | New-server item database |
| `data/script_newserver/npcs.scr` | npc-table variant | — | yes | (config_tables.md) | New-server NPC table |

> The strides marked **(config_tables.md)** / **(items_scr.md)** / **(events_scr.md)** are
> authoritatively documented in those specs; they are listed here only so the family inventory is
> complete. Files marked **UNVERIFIED (stride)** were size-and-role triaged only — a parser MUST
> determine and confirm their stride before reading record bodies.

---

## Small-table layouts (surveyed in this spec)

The byte offsets below are **within one record** (record start = 0). All multi-byte integers are
little-endian. CP949 text fields are null-terminated and zero-padded to the stated width.

### exp.scr — Level → XP (stride 20 B, 300 records)

Record count source: `file_size / 20`. No header; record[0] is level 1.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 2 | u16 | Level number (1-based, 1..300) | Sequential; loader validates monotonic order | HIGH |
| +2 | 2 | u16 | Constant 64 (0x0040) | Identical on all records; semantic unknown | HIGH (value); LOW (semantic) |
| +4 | 4 | u32 | XP required to reach this level | Exponential growth curve | HIGH |
| +8 | 8 | u64 | Reserved / zero | Zero in all observed records | LOW |
| +16 | 4 | u32 | Reserved / zero | Zero in all observed records | LOW |

> See `config_tables.md §2.3` for a deeper survey of the same file, where the +8..+19 region is
> further split into secondary/tertiary progression curves. Where the two specs differ in the
> tail-field model, treat `config_tables.md` as the more detailed authority.

### guildcrest.scr — Crest sprite-sheet crop coords (stride 20 B, 1300 records)

Record count source: `file_size / 20`. No header. Pure integer table, no embedded text.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | crest_id (sequential 1..1300) | Map key | HIGH |
| +4 | 4 | u32 | Secondary index | Equals crest_id on observed records; may be an independent index/variant | MED |
| +8 | 4 | u32 | sheet_index (1-based) | Which crest sprite sheet | HIGH |
| +12 | 4 | u32 | x_src (source X pixel offset in sheet) | Steps observed in small increments | HIGH |
| +16 | 4 | u32 | y_src (source Y pixel offset in sheet) | Steps observed in larger increments | HIGH |

Maps each crest id to a 2D crop region in a crest sprite-sheet image. The `sheet_index` allows
crests to span multiple sheets. Whether +4 is a true duplicate of +0 is UNVERIFIED.

### guildposname.scr — Guild position (rank) names (stride 88 B, 9 records)

Record count source: `file_size / 88`. No header. Embeds CP949 rank titles.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | position_id (1..9 sequential) | Map key | HIGH |
| +4 | 4 | u32 | parent_position_id | 0-based predecessor in the rank hierarchy | HIGH |
| +8 | 20 | char[20] | Title name A (CP949, null-padded) | Korean guild rank title | HIGH |
| +28 | 20 | char[20] | Title name B / variant (CP949) | Variant of the rank title | HIGH |
| +48 | 20 | char[20] | Title name C / variant (CP949) | Variant of the rank title | MED |
| +68 | 4 | u32 | Numeric parameter | Small value observed on some records | LOW |
| +72 | 16 | bytes | Unknown trailing region | UNVERIFIED | LOW |

The three name variants per record may encode honorific or gender variants of a single rank. The
numeric parameter at +68 may be a max-member-count, privilege level, or display order — UNVERIFIED.

### itemeffect.scr — Item-effect type-code list (4 B stride, 793 u32 entries)

Record count source: `file_size / 4`. No header, no per-entry metadata — a flat `u32` array.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| N×4 | 4 | u32 | item_effect_type_code | Mostly sequential with gaps; high-byte family shared across entries | MED |

Appears to be an ordered allow-list / enumeration of valid item-effect type codes the client
references for lookups. The mapping from a code to a concrete effect behaviour is UNVERIFIED and
belongs to the item-system lane (see `formats/items_scr.md`).

### chivalry.scr — Chivalry/honor rank records (stride 24 B, 16 records)

Record count source: `file_size / 24`. No header. Embeds short CP949 syllables.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | rank id / packed key | Looks like a packed enum or hash | MED |
| +4 | 5 | char[5] | Short name A (CP949, null-padded) | 2-byte Korean char + terminator + pad | HIGH |
| +9 | 4 | bytes | Unknown / padding | Zeros observed; may be a numeric sub-field | LOW |
| +13 | 5 | char[5] | Short name B (CP949, null-padded) | As name A | HIGH |
| +18 | 6 | bytes | Unknown / padding | Could be a third short string or a u16+u32 pair | LOW |

The 24-byte stride is clean, but the regions at +9 and +18 are UNVERIFIED — they may be padding,
a numeric field, or additional short name slots.

### dashs.scr — Dash/evasion skill table (stride 199 B, 28 records) — CANDIDATE

Record count source: `file_size / 199` (= 28 exactly). Embeds a CP949 short name and a long
description.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | skill id / hash | Map key candidate | MED |
| +4 | (≥8) | char[] | Short skill name (CP949, null-padded) | Width UNVERIFIED — see caveat | MED |
| ~+? | (≤187) | char[] | Long description (CP949, null-padded) | Begins well past +12; exact offset UNVERIFIED | LOW |

> **STRIDE/LAYOUT CONFLICT — UNVERIFIED.** 199 is the only exact divisor of the file size above
> 4 (other than 4 and 28), so the 199-byte stride is the strongest candidate. However, the long
> description appears roughly 252 bytes from the record start in the observed window, which a
> simple 4 + 8 + 187 layout cannot account for — there is an unexplained gap (the short-name
> field may be far wider than 8 bytes, or there is intermediate binary data between name and
> description). A per-record harness pass at 199-byte intervals is required to confirm the field
> boundaries. Do NOT implement body-field offsets for this file from this spec yet — only the
> id-at-+0 and the candidate stride are usable.

### helps.scr — In-game help text (hierarchical) — CANDIDATE

Record model: **two-level hierarchical**, not a single uniform stride. The first record at the
file start is a chapter/section descriptor; subsequent records are topic entries.

Candidate inner record (treating the body as a flat 48-byte entry):

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | chapter_id / section_id | | MED |
| +4 | 4 | u32 | sub_id / entry_index | | MED |
| +8 | 4 | u32 | flags / reserved | Zero observed | LOW |
| +12 | 4 | u32 | count / param | e.g. child-entry count | MED |
| +16 | 4 | u32 | entry_id / reference | | MED |
| +20 | 4 | u32 | parent_id / reference | | MED |
| +24 | 4 | u32 | reserved | Zero observed | LOW |
| +28 | 1 | u8 | control byte | 1 on chapter records, 0 on others | MED |
| +29 | 19 | char[19] | topic/chapter title (CP949, null-padded) | | HIGH |

> **STRIDE CONFLICT — UNVERIFIED (48 vs 64).** The arithmetic stride 48 divides the file size
> exactly, but the CP949 title strings recur with a 64-byte (0x40) visual spacing in the observed
> head window. The discrepancy is consistent with a 48-byte record plus null fill between titles,
> but it is NOT resolved. A deeper harness pass is needed to confirm whether the true stride is
> 48 or 64. `config_tables.md` models `helps.scr` as a two-level structure (16-byte page header +
> N × 48-byte sub-entries); that hierarchical model and this flat-48 candidate must be
> reconciled before implementation. Treat the stride as UNVERIFIED.

### helps_1.scr — Help chapter index (header + title + 64 B entries) — CANDIDATE

Record model: a small file header, then a title record, then a run of fixed 64-byte content
entries (not a single uniform stride). Observed total size reconciles as
`16 (header) + 20 (title) + 20 × 64 (entries)`.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | record id (= 1) | | MED |
| +4 | 4 | u32 | reserved / zero | | LOW |
| +8 | 4 | u32 | declared total slots | Sample declares 100 but only ~20 populated | MED |
| +12 | 4 | u32 | reserved / zero | | LOW |
| +16 | 20 | char[20] | Title record (CP949, null-padded) | | HIGH |
| +36 | 64 | record | Content entry: 4-byte prefix + CP949 title + null pad | Repeats for each entry | HIGH |

The declared-total field (value 100) vs the ~20 populated entries is UNVERIFIED — the file may be
sparse with null slots, or the field may be a different quantity (e.g. a pixel height). The 4-byte
prefix in each content entry may be an index back-reference or a flags field.

### discript.sc — District/zone descriptor table (stride 68 B, 33 records)

Record count source: `file_size / 68` (confirmed by the production parser: stride 68, 33 records,
zero tail). Note the extension is `.sc`, not `.scr`.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | record_id | Sparse id space; first ids begin at 8 and jump (not 1-based) | HIGH |
| +4 | 64 | bytes | Remaining record body | Field layout UNVERIFIED — not yet probed | LOW |

Only the id at +0 and the 68-byte stride are verified. The body (bytes +4..+67) has not been
probed and must NOT be guessed; whether it contains CP949 text is UNKNOWN.

> `config_tables.md §2.17` also lists `discript.sc` at stride 68 with the role "UI context-menu
> label table". The two role guesses (district/zone descriptor vs UI context-menu labels) are not
> reconciled — treat the role as UNVERIFIED.

---

## Known unknowns

- **dashs.scr** body layout: the 199-byte stride is a CANDIDATE only; the long-description offset
  cannot be explained by a simple field sequence (unexplained gap). UNVERIFIED.
- **helps.scr** stride: arithmetic 48 vs observed 64-byte title spacing — CONFLICT, UNVERIFIED;
  also the flat-48 model vs the hierarchical page-header model in `config_tables.md` must be
  reconciled.
- **helps_1.scr** declared-total field (100) vs ~20 populated entries — sparse vs alternate
  meaning, UNVERIFIED.
- **chivalry.scr** regions at +9 and +18 — padding, numeric, or extra name slots, UNVERIFIED.
- **guildposname.scr** numeric parameter at +68 and trailing region at +72 — UNVERIFIED.
- **guildcrest.scr** field at +4 — duplicate of crest_id or independent index/variant, UNVERIFIED.
- **exp.scr** constant 64 at +2 and the tail fields at +8..+19 — semantic UNVERIFIED here; see
  `config_tables.md` for the deeper (secondary/tertiary curve) model.
- **itemeffect.scr** code→effect mapping — UNVERIFIED, belongs to the item-system lane.
- **discript.sc** body bytes +4..+67 and its true role — UNVERIFIED (not probed).
- **UNVERIFIED-stride family members** (`mobsitem.scr`, `minds.scr`, `letters.scr`,
  `nicktofame.scr`, `productrandname.scr`, `productcollect.scr`, `playtime_reward.scr`,
  `repair.scr`, `system_control.scr`, `tiphelp.scr`, `upgradeitems.scr`): stride/layout not yet
  determined — a parser must confirm before reading bodies.

## Cross-references

- Related specs:
  - `formats/config_tables.md` — deeper survey of the stat-curve and large catalogue `.scr`
    files (`exp`, `userlevel`, `userpoint`, `users`, `skills`, `mobs`, `npcs`, `npc`, `quests`,
    `autoquestion_cl`, `mapsetting`, `setitemname`, `statue`, `viplevels`, `warstoneinfo`,
    `oblist`, `skillneedset`, `tutor`, and the `.do`/`.xdb` siblings).
  - `formats/items_scr.md` — item database family (`items.scr`, `citems.scr`, `itemscale.scr`,
    `itemeffect.scr` semantics, `items_extra.do`). *(big-table home; create if absent)*
  - `formats/events_scr.md` — event/schedule table (`events.scr`, indexed offset-table model).
    *(big-table home; create if absent)*
  - `formats/pak.md` — the VFS container that delivers all `.scr` files.
- Proposed canonical names (flag for `names.yaml`, orchestrator-owned): `exp_level_record` (20 B),
  `guild_crest_record` (20 B), `guild_pos_record` (88 B), `item_effect_id_entry` (4 B),
  `chivalry_rank_record` (24 B), `dash_skill_record` (199 B), `help_entry_record` (48 B),
  `help_chapter_header` (16 B), `district_desc_record` (68 B).
- Glossary: see Docs/RE/names.yaml
- Provenance: see Docs/RE/journal.md (add an entry for this spec)
