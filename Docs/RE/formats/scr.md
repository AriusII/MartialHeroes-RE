# Format: .scr  (client-side binary record-table container family)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file with
> `// spec: Docs/RE/formats/scr.md`.
>
> status: sample-verified (small tables + strides surveyed below corroborated against the real VFS);
>   loader-control-flow facts: confirmed; genuinely ambiguous body fields: capture/debugger-pending
> ida_reverified: 2026-06-16
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
> conflicts: helps_1.scr per-entry 4-byte prefix UNRESOLVED (sample-probe-pending); helps.scr
>   two-level body layout WITHIN the confirmed 48-byte frame still UNVERIFIED.
> Provenance: two-witness pass — neutral static-loader control-flow notes (build 263bd994) plus
>   black-box harness observation of the maintainer's legally-owned client VFS (43,347 entries).

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
  records**, not delimiter-separated rows. The embedded text is **single-byte/CP949 (NOT UCS-2 /
  UTF-16)** across every triaged member, including the wide `npcs.scr` records (see the per-file
  note below). Confidence: CONFIRMED.

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
- **Partial trailing record (defensive EOF handling, may or may not trigger):** the loader is
  written to stop at the last *whole* record — it attempts the next read, gets a short read, and
  breaks. If `file_size` happens not to be an exact multiple of `record_stride`, the leftover
  sub-stride bytes are an **unconsumed partial final record**, never a header, a terminator, or a
  second format. This is defensive handling and **does not imply that any shipping VFS file
  actually carries such a tail** — on the analysed VFS the catalogues all divide exactly (see
  `npcs.scr` below, where the formerly-claimed 732-byte tail is REFUTED for this build). A
  faithful parser should consume only whole records and tolerate (discard) any sub-stride tail if
  one is ever encountered.

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
| `data/script/dashs.scr` | 199 B flat | 28 | yes | SAMPLE-VERIFIED (stride; body UNVERIFIED) | Dash/evasion skill table |
| `data/script/helps.scr` | 48 B flat (two-level reading within one frame) | 1378 | yes | SAMPLE-VERIFIED (stride 48; 64 refuted) | In-game help text |
| `data/script/helps_1.scr` | 16 B header + 20 B title + N×64 B entries | 1 + 1 + 20 | yes | SAMPLE-VERIFIED (geometry; per-entry prefix UNRESOLVED) | Help chapter index |
| `data/script/discript.sc` | 68 B flat | 33 | yes | VERIFIED (stride) | UI context-menu label table |
| `data/script/autoquestion_cl.scr` | 92 B flat | 1300 | — | SAMPLE-VERIFIED | Anti-bot quiz pool (see config_tables.md) |
| `data/script/mapsetting.scr` | 84 B flat | 52 | yes | SAMPLE-VERIFIED | Per-zone map settings (see config_tables.md) |
| `data/script/npc.scr` | 404 B flat | 2510 | yes | CONFIRMED (two-witness) | Keyed CP949 string table (see config_tables.md §2.17.3) |
| `data/script/quests.scr` | 3720 B sparse | 488 slots / 122 used | yes | SAMPLE-VERIFIED | Quest templates (see config_tables.md) |
| `data/script/statue.scr` | 36 B flat | 430 | no | SAMPLE-VERIFIED (count) | Statue/landmark table (see config_tables.md) |
| `data/script/setitemname.scr` | 36 B flat | 61 | yes | SAMPLE-VERIFIED (count) | Set-item name table (see config_tables.md) |
| `data/script/skillcategory.scr` | 564 B flat | 17 | yes | SAMPLE-VERIFIED (stride + count) | Skill category table (see config_tables.md) |
| `data/script/skillneedset.scr` | 4 B flat | 22 | no | SAMPLE-VERIFIED (count) | Skill prerequisite edges (see config_tables.md) |
| `data/script/viplevels.scr` | 92 B flat | 9 | no | SAMPLE-VERIFIED (stride + count) | VIP level table (see config_tables.md) |
| `data/script/warstoneinfo.scr` | 40 B flat | 1 | yes | SAMPLE-VERIFIED (single record) | War-stone region info (see config_tables.md) |
| `data/script/oblist.scr` | 12 B flat | 1 | no | SAMPLE-VERIFIED (single record) | Object list (see config_tables.md) |
| `data/script/itemscale.scr` | 8 B flat | — | no | (items_scr.md) | Item scale/sizing table |
| `data/script/skills.scr` | 1504 B + N×8 trailing | ~194 real | yes | (config_tables.md) | Skill database |
| `data/script/mobs.scr` | 488 B flat | 3997 | yes | (config_tables.md) | Mob stat table |
| `data/script/mobsitem.scr` | flat (large) | — | yes | UNVERIFIED (stride) | Mob drop-item table |
| `data/script/npcs.scr` | 1916 B flat (no tail on this VFS) | 812 | yes | CONFIRMED (stride + count) | NPC stat/placement table (see config_tables.md + note below) |
| `data/script/minds.scr` | flat | — | yes | UNVERIFIED (stride) | Mind/inner-skill table |
| `data/script/letters.scr` | flat | — | yes | UNVERIFIED (stride) | Letter/mail templates |
| `data/script/nicktofame.scr` | flat | — | yes | UNVERIFIED (stride) | Nickname-to-fame table |
| `data/script/productrandname.scr` | flat | — | yes | UNVERIFIED (stride) | Random craft name table |
| `data/script/products.scr` | 212 B flat | — | yes | SAMPLE-VERIFIED (stride only) | Crafting product table |
| `data/script/productcollect.scr` | flat (u32 arrays) | — | no | UNVERIFIED (stride) | Crafting/collection recipe table |
| `data/script/playtime_reward.scr` | 32 B flat | 5 | no | SAMPLE-VERIFIED (stride + count) | Play-time reward table |
| `data/script/repair.scr` | small struct | — | no | UNVERIFIED (layout) | Repair cost/parameter table |
| `data/script/system_control.scr` | 8 B flat (u32/f32 pairs) | 114 | no | SAMPLE-VERIFIED (stride + count) | System control parameters |
| `data/script/tiphelp.scr` | flat | — | yes | UNVERIFIED (stride) | Loading-screen tip text |
| `data/script/tutor.scr` | 1660 B flat | 86 | yes | SAMPLE-VERIFIED (count) | Tutorial step table (see config_tables.md) |
| `data/script/upgradeitems.scr` | flat (float arrays, large) | — | no | UNVERIFIED (stride) | Item upgrade recipe table |
| `data/script/userlevel.scr` | 60 B flat | 300 | no | SAMPLE-VERIFIED (stride + count) | Per-level stat-scaling curve (see config_tables.md; the 120/150 reading is REFUTED) |
| `data/script/userpoint.scr` | 32 B flat | 301 | no | (config_tables.md) | Stat-point allocation curve |
| `data/script/users.scr` | 496 B single structure (no record stride) | 1 blob (4 class windows) | yes | (config_tables.md) | Per-class stat-ratio grid |
| `data/script/userpoint*/userpoint.scr` variants | — | — | — | — | (as above) |
| `data/script/citems.scr` | 1052 B flat | 512 | yes | (other lane → items_scr.md) | Billing/premium item catalogue |
| `data/script/items.scr` | 548 B + N×8 trailing | 90,937 | yes | (other lane → items_scr.md) | Full item database |
| `data/script/events.scr` | indexed (offset table) | — | yes | (other lane → events_scr.md) | Event schedule/data table |
| `data/script_newserver/items.scr` | item-db variant | — | yes | (other lane → items_scr.md) | New-server item database |
| `data/script_newserver/npcs.scr` | npc-table variant | — | yes | (config_tables.md) | New-server NPC table |

> The strides marked **(config_tables.md)** / **(items_scr.md)** / **(events_scr.md)** are
> authoritatively documented in those specs; they are listed here only so the family inventory is
> complete. Files marked **UNVERIFIED (stride)** were size-and-role triaged only — a parser MUST
> determine and confirm their stride before reading record bodies.

---

## npcs.scr — NPC stat/placement table (stride 1916 B, flat)

> Stride, count, and tail-handling were established by a two-witness pass (loader behaviour plus
> black-box file-geometry observation over the full file). Field-level body layout remains in
> `config_tables.md`'s lane; this section pins only the stride, the count, the (refuted) tail, and
> the text encoding.

- **Record stride:** 1916 bytes (0x77C). Confidence: **CONFIRMED** (loader stride and file
  geometry agree).
- **Record count source:** `record_count = file_size / 1916`. On the analysed VFS this is
  **812 records exactly** — `812 × 1916` reconciles the file size with **zero remainder**.
  Confidence: **CONFIRMED**.
- **No partial trailing record on this VFS (REFUTES the earlier "732-byte tail").** An earlier
  revision claimed the file carried an unconsumed 732-byte partial final record (and used it to
  argue a "new-server variant"). The full-file black-box walk **refutes** that for this build: the
  file size is an **exact** multiple of 1916, so there is **no** sub-stride remainder to discard.
  - The loader still *contains* defensive short-read handling (it would stop at the last whole
    record if a future/alternate build's file were not stride-aligned), but on this VFS that path
    never fires. A parser must **not** hard-code a 732-byte tail, **not** treat any trailing bytes
    as a record/header/alternate layout, and **not** revive the "new-server variant" reading
    (which remains REFUTED). Confidence: **CONFIRMED** (the 732-byte tail is REFUTED for this
    build; the defensive short-read path is real but inert here).
- **Embedded text encoding:** the CP949/EUC-KR single-byte legacy Korean code page, null-terminated
  and zero-padded — the same encoding as the rest of the family. The body text is **NOT** UCS-2 /
  UTF-16 wide characters. Any earlier UCS-2 reading is corrected to CP949. Confidence: **CONFIRMED**.

The full field breakdown of the 1916-byte record body is the authority of `config_tables.md §2.10`;
this section governs only the stride, the count, the tail rule, and the encoding so a parser does
not mis-size records or mis-decode the strings.

---

## citems.scr — Billing/premium item catalogue (stride 1052 B, 512 records)

> Field-level body layout is the authority of `items_scr.md` (item-system lane). Pinned here only
> for the key/index field clarified by the two-witness pass.

- **Field at +0 = `item_id`.** This leading u32 serves as **both the map key and the cash-shop /
  billing filter** for the record — the loader resolves a billing/premium item by this id, and the
  same value also drives the billing-system inclusion filter (records with id ≥ a billing
  threshold are only inserted when the billing system is active). It is not a separate "row number"
  distinct from a key; the one field plays both roles. Confidence: **loader-resolved** (settled by
  observed loader behaviour, not a static guess).

See `formats/items_scr.md` for the remaining `citems.scr` body fields.

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
| +68 | 4 | u32 | Numeric parameter | Observed = 10 (decimal) on record 0; non-zero. Semantic UNVERIFIED | MED (value) / LOW (semantic) |
| +72 | 16 | bytes | Unknown trailing region | All zeros on records 0–1 | LOW |

The three name variants per record may encode honorific or gender variants of a single rank. The
numeric parameter at +68 is now observed as **10** on the first record (it is non-zero, not the
formerly-suspected always-small/often-zero value); it may be a max-member-count, privilege level, or
display order — semantic still UNVERIFIED. The trailing 16-byte region at +72 reads all-zero on the
observed records.

### itemeffect.scr — Item-effect type-code list (4 B stride, 793 u32 entries)

Record count source: `file_size / 4`. No header, no per-entry metadata — a flat `u32` array.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| N×4 | 4 | u32 | item_effect_type_code | First 8 entries are tightly sequential with **no gaps**, sharing one high-byte family; gaps may appear deeper in the file | MED |

Appears to be an ordered allow-list / enumeration of valid item-effect type codes the client
references for lookups. The first 8 entries form a gap-free run within a single high-byte family
(the earlier "mostly sequential with gaps" reading is narrowed: the head is gap-free; whether gaps
occur later in the 793-entry file is UNVERIFIED). The mapping from a code to a concrete effect
behaviour is UNVERIFIED and belongs to the item-system lane (see `formats/items_scr.md`).

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

### dashs.scr — Dash/evasion skill table (stride 199 B, 28 records) — stride SAMPLE-VERIFIED

Record count source: `file_size / 199` (= 28 exactly). Embeds a CP949 short name and a long
description.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | skill id / hash | Map key candidate | MED |
| +4 | (≥8) | char[] | Short skill name (CP949, null-padded) | Begins immediately at +4; width UNVERIFIED — see caveat | MED |
| ~+? | (≤187) | char[] | Long description (CP949, null-padded) | Begins well past +12; exact offset UNVERIFIED | LOW |

> **STRIDE RESOLVED (sample-verified); BODY LAYOUT still UNVERIFIED.** The file size is an exact
> multiple of 199 (`size / 199 = 28` remainder 0), and 199 is the only clean divisor above 4. The
> competing **796 B / 7-record** reading from `config_tables.md §2.17.5` is **REFUTED** by VFS
> geometry — `size / 796` does NOT divide evenly (it leaves a 796-byte remainder, i.e. it is not an
> integer count). The formerly-unreconciled stride conflict is therefore **closed in favour of
> 199 B / 28 records** (`config_tables.md §2.17.5` should be corrected to match). The id at +0 and a
> CP949 short name beginning at +4 are observed; however, the long description appears roughly 252
> bytes from the record start in the observed window, which a simple 4 + 8 + 187 layout cannot
> account for — there is an unexplained gap (the short-name field may be far wider than 8 bytes, or
> there is intermediate binary data between name and description). A per-record harness pass at
> 199-byte intervals is still required to confirm the internal field boundaries. Do NOT implement
> body-field offsets beyond the id-at-+0 from this spec yet; the stride (199) is the only
> body-independent fact that is fully settled.

### helps.scr — In-game help text (48 B flat frame, two-level reading) — stride SAMPLE-VERIFIED

Record model: a uniform **48-byte record frame** read in a **two-level** way — the first record at
the file start is a chapter/section descriptor; subsequent records are topic entries. Both kinds
share the same 48-byte stride. Record count = `file_size / 48` = 1378 records.

Inner record (uniform 48-byte frame):

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | chapter_id / section_id | First of seven leading u32 fields (+0..+27) | MED |
| +4 | 4 | u32 | sub_id / entry_index | | MED |
| +8 | 4 | u32 | flags / reserved | Zero observed | LOW |
| +12 | 4 | u32 | count / param | e.g. child-entry count | MED |
| +16 | 4 | u32 | entry_id / reference | | MED |
| +20 | 4 | u32 | parent_id / reference | | MED |
| +24 | 4 | u32 | reserved | Zero observed | LOW |
| +28 | 1 | u8 | control byte | 1 on chapter records, 0 on topic records | MED |
| +29 | 19 | char[19] | topic/chapter title (CP949, null-padded) | On a topic entry the CP949 title sits at the same +29 within its own 48-byte frame | HIGH |

> **STRIDE RESOLVED (sample-verified): 48 B; the 64-byte reading is REFUTED.** The arithmetic stride
> 48 divides the file size exactly (`size / 48 = 1378` records, remainder 0). The competing 64-byte
> candidate is **arithmetically impossible** — `size / 64` is **not** an integer (it lands on a
> fractional record count), so 64 cannot be the record stride. The "64-byte title spacing" seen in
> earlier head-window passes was a misread of the inter-title byte distance, not the record stride.
> The earlier "48 vs 64 conflict" is therefore **closed in favour of 48 B**. The two-level reading
> (`config_tables.md`'s 16-byte page header + N × 48-byte sub-entries model) is preserved, but every
> record shares the single 48-byte frame — there is no separate page-header stride. The internal
> field roles within the 48-byte frame (the seven leading u32s) remain UNVERIFIED beyond the
> control byte at +28 and the CP949 title at +29.

### helps_1.scr — Help chapter index (header + title + 64 B entries) — geometry SAMPLE-VERIFIED

Record model: a small file header, then a title record, then a run of fixed 64-byte content
entries (not a single uniform stride). The total size reconciles **exactly** as
`16 (header) + 20 (title) + 20 × 64 (entries)` = the observed file size (sample-verified, remainder 0).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | record id (= 1) | Header field | HIGH |
| +4 | 4 | u32 | reserved / zero | | LOW |
| +8 | 4 | u32 | declared total slots | Sample declares 100 but only ~20 entries present | MED |
| +12 | 4 | u32 | reserved / zero | | LOW |
| +16 | 20 | char[20] | Title record (CP949, null-padded) | Zero-fill completes the 20-byte field | HIGH |
| +36 | 64 | record | Content entry: optional 4-byte prefix + CP949 title + null pad | Repeats for each of the 20 entries — see prefix conflict below | HIGH (frame) / UNRESOLVED (prefix) |

The header/title/entry geometry is sample-verified (16 + 20 + 20×64 = file size, exact). The
declared-total field (value 100) vs the 20 entries actually present is UNVERIFIED — the file may be
sparse with null slots, or the field may be a different quantity (e.g. a pixel height).

> **OPEN CONFLICT — per-entry 4-byte prefix (UNRESOLVED, sample-probe-pending).** The earlier model
> claimed every 64-byte content entry begins with a fixed 4-byte prefix (an index back-reference or
> flags) followed by the CP949 title. The harness pass does **not** confirm this consistently:
> - Content entry 0 begins with 4 zero bytes, and the CP949 title starts at entry+4.
> - Content entry 1 appears to begin **directly** with CP949 title bytes — no 4 leading zero bytes.
>
> This is **not resolved**. Either (a) the 4 leading bytes are a per-slot sparse/index indicator
> that happens to be zero on entry 0 and non-zero (CP949-looking) on entry 1, or (b) entry 0 is a
> special first slot and regular entries carry no prefix. A faithful parser should read **64 raw
> bytes per content entry** and treat the first 4 bytes as a possible sparse/index field (value 0 or
> not) with the remainder a CP949 null-terminated title — but MUST NOT hard-assume a uniform 4-byte
> prefix until a deeper per-entry harness pass settles it. Carried as an explicit open conflict.

### discript.sc — UI context-menu label table (stride 68 B, 33 records) — CONFIRMED

Record count source: `file_size / 68` (confirmed by the production parser: stride 68, 33 records,
zero tail). Note the extension is `.sc`, not `.scr`.

> **Role CONFIRMED (two-witness: loader + black-box): these are UI right-click / context-menu
> labels, NOT "district/zone" descriptors.** The consumer is the context-menu label loader; each
> record carries an id, a category code, a CP949 menu-item caption and (for hotkey-capable windows)
> an ASCII keyboard-shortcut string. The earlier "district/zone descriptor" reading is REFUTED —
> there are no world-geometry or zone-bounds fields here (zone bounding boxes live in
> `mapsetting.scr`). The **full record field layout, the `category` enumeration, the
> `descriptor_id` ranges, and the keyboard-shortcut encoding** are documented in
> `formats/misc_data.md §5` (the single authority for the `.sc` record body); only the id-at-+0
> and the 68-byte stride are pinned here.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | record_id (map key) | Sparse id space (party/currency/window/guild groups); not 1-based | HIGH |
| +4 | 64 | bytes | Remaining record body | Field layout is the authority of `misc_data.md §5` | see misc_data.md |

---

## Known unknowns

- **dashs.scr** body layout: the **199-byte stride is now SAMPLE-VERIFIED** (28 records, exact) and
  the competing 796-byte reading is REFUTED — the stride conflict is closed. What remains UNVERIFIED
  is only the internal body field sequence: the long-description offset cannot be explained by a
  simple 4 + 8 + 187 layout (unexplained gap). `config_tables.md §2.17.5` should be corrected to the
  199-byte stride.
- **helps.scr** stride: **RESOLVED — 48 B (sample-verified, 1378 records); the 64-byte reading is
  arithmetically impossible** and REFUTED. The two-level (chapter header + topic entries) reading is
  preserved within the single 48-byte frame. The internal field roles of the seven leading u32s
  (within the 48-byte frame) remain UNVERIFIED.
- **helps_1.scr** geometry (16 + 20 + 20×64) is SAMPLE-VERIFIED. Two items stay open: the
  declared-total field (100) vs the 20 entries present (sparse vs alternate meaning, UNVERIFIED), and
  the **per-entry 4-byte prefix (UNRESOLVED open conflict)** — entry 0 carries 4 leading zero bytes
  while entry 1 appears to start directly with CP949 title bytes; a deeper per-entry harness pass is
  pending.
- **chivalry.scr** regions at +9 and +18 — padding, numeric, or extra name slots, UNVERIFIED.
- **guildposname.scr** numeric parameter at +68 and trailing region at +72 — UNVERIFIED.
- **guildcrest.scr** field at +4 — duplicate of crest_id or independent index/variant, UNVERIFIED.
- **exp.scr** constant 64 at +2 and the tail fields at +8..+19 — semantic UNVERIFIED here; see
  `config_tables.md` for the deeper (secondary/tertiary curve) model.
- **itemeffect.scr** code→effect mapping — UNVERIFIED, belongs to the item-system lane.
- **discript.sc** body bytes +4..+67 — the field layout is the authority of `misc_data.md §5`
  (role CONFIRMED as context-menu labels; the 27-byte reserved tail there is UNVERIFIED).
- **npcs.scr** body field layout (within the confirmed 1916-byte record) — the authority is
  `config_tables.md §2.10`; the stride (1916), the count (812), the absence of a tail on this
  build, and the CP949 encoding are CONFIRMED here.
- **citems.scr** body fields beyond `item_id` at +0 — UNVERIFIED here; the authority is
  `items_scr.md`.
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
    `itemeffect.scr` semantics, `items_extra.do`).
  - `formats/misc_data.md` — the `.sc` / `.xdb` / `.mi` small-data family; the **authority for the
    `discript.sc` record body** (§5).
  - `formats/events_scr.md` — event/schedule table (`events.scr`, indexed offset-table model).
    *(big-table home; create if absent)*
  - `formats/pak.md` — the VFS container that delivers all `.scr` files.
- Proposed canonical names (flag for `names.yaml`, orchestrator-owned): `exp_level_record` (20 B),
  `guild_crest_record` (20 B), `guild_pos_record` (88 B), `item_effect_id_entry` (4 B),
  `chivalry_rank_record` (24 B), `dash_skill_record` (199 B), `help_entry_record` (48 B),
  `help_chapter_header` (16 B), `district_desc_record` (68 B), `npcs_stat_record` (1916 B).
- Glossary: see Docs/RE/names.yaml
- Provenance: see Docs/RE/journal.md (add an entry for this spec).
  - CAMPAIGN VFS-MASTERY (two-witness: loader + black-box): `npcs.scr` stride 1916 / **812 records
    exactly** CONFIRMED; the formerly-claimed **732-byte partial trailing record is REFUTED for
    this VFS** (file size is an exact multiple of 1916; the loader's defensive short-read path
    exists but is inert here; the "new-server variant" reading stays REFUTED); `npcs.scr` body text
    CP949 (UCS-2 reading corrected); `citems.scr` +0 = `item_id` dual key / cash-shop filter
    (loader-resolved); `discript.sc` role = UI context-menu labels (district/zone reading REFUTED),
    record body authority moved to `misc_data.md §5`.
  - CAMPAIGN 10 / D9 (ida_reverified 2026-06-16, ida_anchor 263bd994; two-witness: static loader +
    VFS sample): family-level shape re-confirmed (no magic, LE, CP949, count = size/stride, no tail
    fires) across the 44 `.scr` entries [sample-verified]. **Three conflicts resolved:** `dashs.scr`
    stride = **199 B / 28 records** (796 B REFUTED); `helps.scr` stride = **48 B / 1378 records**
    (64 B arithmetically impossible — REFUTED); `userlevel.scr` = **60 B / 300 records** (the 120 B /
    150-record estimate is REFUTED). **Six record counts newly sample-verified:** `statue.scr` 430,
    `skillcategory.scr` 17, `tutor.scr` 86, `playtime_reward.scr` 5, `viplevels.scr` 9,
    `setitemname.scr` 61 (plus `system_control.scr` 114 and `oblist.scr`/`warstoneinfo.scr` single
    records confirmed). `guildposname.scr` +68 observed = 10 (semantic still UNVERIFIED).
    `itemeffect.scr` head 8 entries gap-free. **Open:** `helps_1.scr` per-entry 4-byte prefix
    UNRESOLVED (entry 0 has it, entry 1 appears not to) — carried as a sample-probe-pending conflict.
