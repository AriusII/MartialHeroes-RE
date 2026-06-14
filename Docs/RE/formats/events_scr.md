# Format: events_scr  (`.scr` script tables: game events, anti-bot captcha, and the `.scr` role index)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> Promoted from black-box VFS harness observation AND neutral static-loader notes under
> EU Software Directive 2009/24/EC Art. 6. No decompiler output and no binary virtual addresses
> appear anywhere in this file.
> C# parsers MUST cite this spec on every offset / magic / stride: `// spec: Docs/RE/formats/events_scr.md`.
>
> status: sample_verified + loader-confirmed. The runtime client loader for `events.scr` has been
> characterized from neutral RE notes (which fields it actually dereferences); the per-field VALUE
> distributions come from a full-file black-box harness pass over 1848 records. Where the two
> disagree on a field's ROLE, the loader is authoritative for "what the client consumes" and the
> harness is authoritative for "what values appear in the blob".

The `.scr` extension is used throughout `data/script/` for a large family of unrelated flat data
tables (no shared wire format). This document fully specifies the two `.scr` tables owned by this
lane — `events.scr` and `autoquestion_cl.scr` — and provides a role-classification index of the
remaining `.scr` files so an engineer knows which table does what before opening one. Tables already
owned by other specs/lanes (`items.scr`, `citems.scr`, `npc.scr`, `quests.scr`, `mapsetting.scr`,
and the `discript.sc` / help / guild text group) are out of scope here and only referenced.

---

## Section 1 — `events.scr` (timed game-event definition table)

**Confidence: CONFIRMED that the client has a runtime loader for this file and that it consumes
only four record fields (see §1.6). Stride and record-count rule CONFIRMED by the loader's own
size-validation. Per-field value distributions SAMPLE-VERIFIED across all 1848 records of one
sample.**

### 1.1 Identification

- **Extension:** `.scr`
- **Found in:** `.pak` / VFS archive; logical path: `data/script/events.scr`. The client opens this
  file by name from the master bulk-asset load pass (the same pass that loads `items.scr`,
  `skills.scr`, `mobs.scr`, `npc.scr`, `quests.scr`, …). It is a normal client-loaded table.
- **Magic / signature:** None. No file-level header, no version field.
- **Endianness:** Little-endian throughout.
- **Encoding:** No text in this file — all fields are numeric / ID-based. A full-record scan of 20
  representative records for printable CP949 runs found none. CONFIRMED all-numeric.
- **Record count source:** Derived from file size divided by stride. No stored count. The client
  loader explicitly validates `file_size % 520 == 0` and sets `record_count = file_size / 520`.
  CONFIRMED. (Different builds carry different record totals: one harness sample divides exactly as
  1848 records; another build sample divides as a smaller record count — both with zero remainder.)
- **Record stride:** 520 bytes (0x208). CONFIRMED by the loader's modulo check.

### 1.2 Container structure

A headerless flat array of fixed-size 520-byte records. Record count = `file_size / 520`.

At load time the client performs a blind bulk copy of the entire file into one heap buffer, then
iterates the records reading ONLY the first u32 of each (`event_id` @0x00) to build an
`event_id → record` lookup map. No other field is parsed at load time. Every other field is read
later, on demand, by the consumers that look a record up by `event_id`. CONFIRMED.

The array may be sparsely populated: in standard records, the array fields (§1.3) are zero-terminated,
so a record with all-zero arrays carries no participants/rewards.

### 1.3 Record layout (standard record, 520 bytes)

Offsets are relative to the start of each record. The **"Client read?"** column is the load-bearing
distinction for an engineer: only the four fields marked CONSUMED are dereferenced by the client at
runtime. Every other field is present in the in-memory blob but is never read — do NOT spend effort
modelling or validating the unread fields beyond skipping over them.

| Offset | Size | Type | Field | Client read? | Notes / observed values | Confidence |
|-------:|-----:|------|-------|--------------|-------------------------|------------|
| 0x00 | 4 | u32LE | `event_id` | **CONSUMED** (primary key) | Unique non-zero identifier. Map key at load; lookup argument for every consumer. Observed range 10551–31704; two allocation epochs — see §1.4. | CONFIRMED (loader) / HIGH (values) |
| 0x04 | 2 | u16LE | `event_type` | not read | Observed value 1 in all standard records (1 in the 5 anomalous records, see §1.5). | SAMPLE-VERIFIED (single value) |
| 0x06 | 2 | u16LE | `day_count` | not read | Observed 7 in most records; 1 in the 5 anomalous records. Likely "active days per cycle". | SAMPLE-VERIFIED |
| 0x08 | 68 | u8[68] | `reserved_a` | **not read** | Zero in all 1843 standard records. NON-ZERO structured block only in the 5 anomalous records (§1.5). The client loader and all consumers ignore this region entirely. | CONFIRMED not-consumed / HIGH (values) |
| 0x4C | 4 | u32LE | `level_min` | not read | Observed 100 in standard records, 0 in anomalous records. Plausibly minimum eligible level, but NO consumer dereferences it in the recovered client paths. | SAMPLE-VERIFIED value / CONFIRMED not-consumed |
| 0x50 | 4 | u32LE | `level_max` | not read | Observed 1000 in standard records, 0 in anomalous records. Same status as `level_min`. | SAMPLE-VERIFIED value / CONFIRMED not-consumed |
| 0x54 | 2 | u16LE | `day_window_start` | **not read** | Inclusive start day of an eligible window within a 144-day cycle (see §1.4); 0 = no day restriction. Present in the blob but NOT dereferenced by the client. | HIGH (values) / CONFIRMED not-consumed |
| 0x56 | 2 | u16LE | `day_window_end` | **not read** | Inclusive end day; pairs with `day_window_start` in 12-day windows over a 144-day cycle (§1.4). Not dereferenced by the client. | HIGH (values) / CONFIRMED not-consumed |
| 0x58 | 2 | u16LE | `pad_58` | not read | Always 0 across all 1848 records. Padding. | HIGH |
| 0x5A | 2 | u16LE | `sub_flag_a` | not read | 0 or 1; value 1 in 1179/1848 records. All four sub-flags (0x5A–0x60) are always equal within a record. Not dereferenced by the client. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x5C | 2 | u16LE | `sub_flag_b` | not read | Identical distribution to `sub_flag_a`; equals it in every record. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x5E | 2 | u16LE | `sub_flag_c` | not read | Identical distribution to `sub_flag_a`. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x60 | 2 | u16LE | `sub_flag_d` | not read | Identical distribution to `sub_flag_a`. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x62 | 2 | u8[2] | `pad_62` | not read | Always 0. Padding. | HIGH |
| 0x64 | 2 | u16LE | `mode_flag` | **CONSUMED** | LIVE display/eligibility mode flag. One consumer branches on `== 1`, another branches on `== 0`, to switch display modes. Observed 1 in 1233/1848 records, 0 otherwise. Earlier drafts mislabeled this region as reserved/padding — it is an actively read field. See §1.6. | CONFIRMED consumed (loader) / PLAUSIBLE (exact semantics) |
| 0x66 | 2 | u8[2] | `pad_66` | not read | Always 0. Padding. | HIGH |
| 0x68 | 200 | u32LE[50] | `rate_array` | **CONSUMED** | Fixed slot of up to 50 u32 entries (0x68–0x12F), zero-terminated (first zero slot ends the populated range). The client consumes each value as a **rate/percentage**: it divides by 1,000,000 and displays the result as a `%` against the associated actor in `actor_array`. NOT an ID array. Observed raw values span small integers, 100k–500k, and 500k–1,000,000 — consistent with fractional rates scaled by 1e6. DISJOINT from `actor_array` value space. See §1.6 / §1.7. | CONFIRMED consumed (loader) / HIGH (÷1e6 rate role) |
| 0x130 | 208 | u32LE[52] | `actor_array` | **CONSUMED** | Fixed slot of up to 52 u32 entries (0x130–0x1FF), zero-terminated (iteration stops at the first non-positive/zero slot). The client iterates these and resolves each as an **actor ID**. Values use the 9-digit ID namespace shared with `items.scr` / `citems.scr` (e.g. 213010002, 215010101; one consumer compares an entry against a 9-digit constant). Pairs positionally with `rate_array` (slot N rate ↔ slot N actor). | CONFIRMED consumed (loader) / HIGH |
| 0x200 | 8 | u8[8] | `record_trailer` | **not read** | Fixed 8-byte tail. In standard records it is the constant byte pattern decoding as three u16 = 1 followed by u16 = 0; in the 5 anomalous records it is all-zero. NOT a checksum (identical across all standard records regardless of content). Never dereferenced by the client. | CONFIRMED not-consumed / HIGH (constant) |

- **Record count source:** `file_size / 520` (loader-validated `% 520 == 0`).
- **Record stride:** 520 bytes.

### 1.4 Field-pair semantics (day window and ID allocation)

These describe observed VALUE structure. They are useful for understanding the data but are NOT
required to parse what the client consumes — the client does not read the day-window fields.

- **Day window (`day_window_start` @0x54, `day_window_end` @0x56):** when non-zero, the pair encodes
  an inclusive day range within a repeating 144-day cycle. Observed combinations partition the cycle
  into twelve consecutive 12-day windows (1–12, 13–24, …, 133–144), plus a full-cycle variant
  (start = 1, end = 144) and a no-restriction state (both 0). 13 distinct pair combinations seen
  across all 1848 records. HIGH (deterministic across the sample).
- **`event_id` allocation epochs:** the early ~144 records use a step of 10 between consecutive IDs;
  after a single large jump, the later ~1700 records use a step of 1 (dense consecutive IDs). The
  early step-10 epoch's record count aligns with the 144-day cycle length. SAMPLE-VERIFIED.

### 1.5 Anomalous 5-record sub-type (event_ids 30133–30137)

Exactly five records carry a DIFFERENT sub-layout. They have `event_type = 1`, `day_count = 1`,
`level_min = level_max = 0`, all day-window/sub-flag fields zero, empty `rate_array` and
`actor_array`, and a ZEROED `record_trailer`. Their distinguishing data lives in the `reserved_a`
region @0x08, which for these records holds a structured block of u32 values rather than zeros:

- The first u32 (@0x08) is a large round value (observed 500,000 or 1,000,000) — plausibly a
  gold / currency threshold.
- The second u32 (@0x0C) is a small even value (observed 20, 50, 100, 150, 200) — plausibly a
  percentage or step.
- Further u32 slots (@0x10–0x20) hold packed values that may decode as paired u16 sub-fields.

This sub-type's exact semantics are UNVERIFIED (no loader path consumes these records' @0x08 block —
the client never reads `reserved_a`). An engineer should treat these five records as data-present but
client-unconsumed, identical in handling to standard records (only `event_id` is indexed; their
`rate_array`/`actor_array` are empty so consumers find nothing to display). SAMPLE-VERIFIED layout /
UNVERIFIED semantics.

### 1.6 What the client actually consumes (authoritative parse contract)

The runtime loader and its consumers dereference exactly four record fields. An `Assets.Parsers`
implementation that reproduces client behaviour needs ONLY these:

1. **`event_id` (u32 @0x00)** — read at load to build the `event_id → record` map; the lookup key
   for every consumer.
2. **`mode_flag` (u16 @0x64)** — a display/eligibility mode selector; consumers branch on `== 0` vs
   `== 1`.
3. **`rate_array` (u32[50] @0x68)** — zero-terminated; each entry divided by 1,000,000 and shown as a
   percentage against the positionally-paired actor.
4. **`actor_array` (u32[52] @0x130)** — zero-terminated (stop at first zero/non-positive); each entry
   is a 9-digit actor ID resolved against the actor/item namespace.

All other fields (`event_type`, `day_count`, `reserved_a`, `level_min`, `level_max`,
`day_window_*`, `sub_flag_*`, all `pad_*`, `record_trailer`) are present in the on-disk/in-memory
record but are NEVER read by the client. A faithful parser may expose them for documentation but must
not gate any behaviour on them.

### 1.7 Semantic role

Each record defines one timed game event: an `event_id` key, a `mode_flag` that selects how the event
panel is displayed, and two positionally-paired fixed-capacity arrays — `rate_array` (per-slot
contribution/drop rate, value ÷ 1e6 = percentage) and `actor_array` (the 9-digit actor/item ID each
rate applies to). The remaining numeric fields (day window, level bounds, sub-flags) describe the
event in the data but are not exercised by this client build.

### 1.8 Known unknowns (events.scr)

- The exact meaning of `mode_flag` @0x64 (which two display modes `0`/`1` select) is PLAUSIBLE only;
  confirming the branch under live data is an optional debugger task.
- Whether `rate_array` slot N is strictly positionally paired with `actor_array` slot N (versus some
  other association) is HIGH-confidence but not byte-proven; an engineer should assume positional
  pairing and verify against rendered output.
- The semantics of the 5 anomalous records' @0x08 sub-block (gold threshold + percentage steps +
  packed flags) are UNVERIFIED — no client path consumes them.
- Why `sub_flag_a..d` appear as four lockstep copies, and the role of the day-window fields, are
  UNVERIFIED — and moot for parsing, since the client reads none of them.
- `event_type` and `day_count` were observed with a near-single value each; whether other values
  occur in other game versions is unknown (and also client-unconsumed).

---

## Section 2 — `autoquestion_cl.scr` (NPC anti-bot captcha Q&A table)

**Status correction (CONFIRMED): the client has NO runtime loader for this file.** The captcha
question/prompt the client displays is supplied at runtime by the server (network packet), not loaded
from a client-side `.scr`. The byte layout below remains valid as a description of the file as it
exists in the VFS, but an `Assets.Parsers` implementation does NOT need to parse it to reproduce
client behaviour — the client never opens it.

**sample_verified: stride confirmed across all 1300 records; string decode confirmed CP949 for the
first 5 records; field-slot layout verified at sample level.**

### 2.1 Identification

- **Extension:** `.scr`
- **Found in:** `.pak` / VFS archive; logical path: `data/script/autoquestion_cl.scr`. Present in the
  VFS but NOT referenced by any client load path (see §2.4). CONFIRMED absent from the client's
  `.scr` loader table; no `autoquestion`/`autoq`/`captcha` path or string exists in the client.
- **Magic / signature:** None. No file-level header, no version field.
- **Endianness:** Little-endian for the numeric ID field.
- **Encoding:** CP949 (EUC-KR), null-terminated, for the two text fields. Register the code page once
  at startup (`Encoding.GetEncoding(949)`) per project convention.
- **Record count source:** file size / stride. A known sample yields exactly 1300 records,
  sequentially numbered, densely packed, no trailer.
- **Record stride:** 92 bytes (0x5C).

### 2.2 Record layout (92 bytes)

The `_cl` suffix would suggest a client-side table, but in this build it is NOT client-read (§2.4).
It holds only display text; no numeric answer is present.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32LE | `question_id` | 1-based sequential identifier (1, 2, 3, …). | HIGH |
| 0x04 | 84 | char[] (CP949) | `text_block` | An 84-byte block (0x04–0x57) holding two consecutive null-terminated CP949 strings: the question text, then the answer-prompt instruction text. See split note below. | HIGH |
| 0x58 | 4 | u8[4] | `record_padding` | Zero padding before the next record. | HIGH |

**Split within `text_block`:** the question text starts at 0x04 and is null-terminated; the
answer-prompt string begins immediately after that null and is itself null-terminated; the remainder
of the 84-byte block is zero-filled. The boundary between the two strings is data-dependent (the
position of the first null), not a fixed offset. A parser must read the first null-terminated string
from 0x04, then read the second null-terminated string starting at the byte after that terminator,
both bounded by the end of the 84-byte block.

- **Record count source:** file size / 92 (no stored count).
- **Record stride:** 92 bytes.

### 2.3 Semantic role

An NPC auto-question (captcha) table for anti-bot / anti-AFK-farm checks: an NPC poses a simple
arithmetic question (Korean word-form) and the player types the numeric answer. The first text string
is the question; the second is a constant UI instruction. In THIS client build the file is unused —
the displayed question is server-pushed (see §2.4).

### 2.4 Client integration (CONFIRMED via neutral loader notes)

- **No client loader exists for `autoquestion_cl.scr`.** It is absent from the master `.scr` loader
  table; no path string, path-builder, or filename for it appears anywhere in the client. The only
  related artefact is a UI panel class (`AutoQuestionPanel`) that displays the captcha — with NO
  associated data-file load path. CONFIRMED.
- **The correct answer is NOT stored in this file**, and — because the client never even reads the
  file — the client cannot grade the answer locally. The captcha question/prompt is delivered to the
  panel at runtime via a server packet; the client only displays it and submits the player's input;
  the server validates. A faithful client must source the captcha text from the wire, not from this
  VFS table, and must not attempt local grading. CONFIRMED.
- The second text string was observed constant across the first 5 records only (the answer-prompt
  instruction); SAMPLE-VERIFIED.

---

## Section 3 — Role-classification index of the remaining `.scr` tables

This is a routing aid, NOT a byte-layout spec. It tells an engineer what each `.scr` table is for
and how reliably that role was determined, so a parser is written against the right table. Strides
listed here are first-pass divisor estimates, NOT confirmed layouts; treat every stride in this
table as UNVERIFIED until promoted into its own spec section. The role guesses derive from CP949
name decoding, first-field patterns, and clean file-size divisors.

Out of scope (owned elsewhere): `items.scr`, `citems.scr`, `npc.scr`, `quests.scr`,
`mapsetting.scr`, and the `discript.sc` / help / guild-text group.

| Filename | Text/Binary | Stride / records (estimate) | Role guess | Confidence |
|----------|-------------|-----------------------------|------------|------------|
| `mobs.scr` | binary + CP949 | stride 488 | Monster definition table (mob_id, name, stats, AI params). | HIGH |
| `mobsitem.scr` | binary + CP949 | variable (UNVERIFIED) | Mob drop / loot table (per-mob item lists). Shares the `mobs.scr` name header; likely variable-length records. | HIGH (role) / UNVERIFIED (stride) |
| `skills.scr` | binary + CP949 | stride 280 | Skill definition table (skill_id, name, stats, class tags). | MEDIUM |
| `npcs.scr` | binary + CP949 | UNVERIFIED | NPC stat / profile table (shop + AI info), distinct from `npc.scr` dialogues. | HIGH (role) |
| `products.scr` | binary + CP949 | UNVERIFIED | Crafting product / recipe catalog (craftable item names). | HIGH (role) |
| `productcollect.scr` | binary | UNVERIFIED | Product ingredient / material requirement join table (product_id → ingredients). | MEDIUM |
| `productrandname.scr` | binary + CP949 | small table | Random-craft affix (prefix/suffix) name table. | MEDIUM |
| `upgradeitems.scr` | binary | variable (UNVERIFIED) | Item upgrade / enhancement recipe table (materials + success-rate floats per step). Likely variable-length records. | HIGH (role) / UNVERIFIED (stride) |
| `itemscale.scr` | binary | stride ~8 (u32 + f32 pairs) | Item visual-scale parameter table (scale per item_id). | HIGH |
| `userlevel.scr` | binary | stride 120, ~150 records | Per-character-level stat / multiplier table. | HIGH |
| `userpoint.scr` | binary | UNVERIFIED | Loyalty / point-reward threshold table. | MEDIUM |
| `viplevels.scr` | binary | UNVERIFIED | VIP tier definition (thresholds + benefits); very small file. | HIGH (role) |
| `nicktofame.scr` | binary + CP949 | UNVERIFIED | Title / fame-rank name table (rank → title). | HIGH (role) |
| `minds.scr` | binary + CP949 | UNVERIFIED | Mind-technique / inner-cultivation definition table. | HIGH (role) |
| `setitemname.scr` | binary + CP949 | small table | Set-item group name table. | HIGH (role) |
| `skillcategory.scr` | binary + CP949 | UNVERIFIED | Skill-category / skill-tree name table. | HIGH (role) |
| `skillneedset.scr` | binary | stride ~8 (u16 pairs) | Skill prerequisite-link table (skill-ID requirement pairs). | MEDIUM |
| `statue.scr` | binary | UNVERIFIED | World statue / monument definition (ID + coordinates). | MEDIUM |
| `tiphelp.scr` | binary + CP949 | UNVERIFIED | Loading-screen tip / help text table. | HIGH (role) |
| `tutor.scr` | binary + CP949 | UNVERIFIED | Tutorial step / narrative text table. | HIGH (role) |
| `letters.scr` | binary + CP949 | UNVERIFIED | In-game mail / letter template table. | HIGH (role) |
| `warstoneinfo.scr` | binary + CP949 | small (~5 records) | War-stone / warshard item info lookup. | MEDIUM |
| `system_control.scr` | binary | stride 8, ~114 entries | Global game-rate table (id + f32 multiplier pairs). | HIGH |
| `repair.scr` | binary | UNVERIFIED | Item repair-cost / durability table; small file. | MEDIUM |
| `oblist.scr` | binary | single 12-byte record | Object list / global limit or enable flag; purpose unclear. | LOW |
| `playtime_reward.scr` | binary | stride ~32 | Play-time reward table (online-time thresholds → item rewards). | HIGH |
| `users.scr` | binary | UNVERIFIED | Account-type definition table; purpose unclear, possibly unused. | LOW |
| `script_newserver/items.scr` | binary + CP949 | same schema as `items.scr` | "New server" config override of the items table (same format, different data). | HIGH (role) |
| `script_newserver/npcs.scr` | binary + CP949 | same schema as `npcs.scr` | "New server" config override of the NPC table. | HIGH (role) |

---

## Cross-references

- **`actor_array` IDs** in `events.scr` use the 9-digit item/actor-ID namespace shared with
  `items.scr` / `citems.scr` (owned by another lane) — resolve referenced actors/items there.
- **`rate_array`** carries scaled rates (raw value ÷ 1,000,000 = percentage), NOT IDs — do not route
  its values through the item-ID resolver.
- **Mob / skill / NPC roles** in the §3 index feed the recovered actor chains; see
  `Docs/RE/formats/actormotion.md`, `Docs/RE/formats/npc_spawns.md`, and `Docs/RE/specs/`.
- **CP949 handling** convention: see project notes (register `CodePagesEncodingProvider`, use code
  page 949) — same as `Docs/RE/formats/misc_data.md`.
- Glossary: see `Docs/RE/names.yaml` (candidate names for `names.yaml`: `event_id`, `mode_flag`,
  `rate_array`, `actor_array`, `day_window_start`, `day_window_end`, `sub_flag_a..d`, `event_type`,
  `day_count`, `level_min`, `level_max`, `record_trailer`, `question_id`, `question_text`,
  `answer_prompt`). NOTE the renames in this revision: former `flag_b`/`flag_c` → `day_window_start`/
  `day_window_end`; former `flag_e..h` → `sub_flag_a..d`; former `ids_array_a` → `rate_array`; former
  `ids_array_b` → `actor_array`; former `record_trailer` retained; the live u16 @0x64 (previously
  inside `reserved_b`/padding) → `mode_flag`.
- Provenance: see `Docs/RE/journal.md` (a provenance entry pairs this spec).
