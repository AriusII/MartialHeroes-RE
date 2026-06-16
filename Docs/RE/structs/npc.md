# NPC / Monster struct, spawn record, and mobs.scr linkage (clean-room spec)

> **Verification banner.** verification: **confirmed** for the loader-exercised facts (the 488-byte
> `mobs.scr` record stride, the u16 `mob_id` primary key at +0x00, the +0xF8 u64 HP qword with its
> load-time `+= 10` mutation, the +0x144 `mob_type` boss discriminator `== 11` and its second lookup
> map); **static-hypothesis** for every other `mobs.scr` field (on-disk/sample inferences the loader
> never reads) and for the `npc.arr` spawn record carried from `formats/npc_spawns.md`;
> **capture/debugger-pending** for the in-game HP *scale factor* that converts the raw `base_max_hp`
> template input to the displayed HP pool (distinct from the offset/mutation, which are confirmed).
> ida_reverified: 2026-06-16 · ida_anchor: 263bd994 · evidence: [static-ida] · conflicts: none
> (the loader **confirms** §1.3's resolution of the prior `config_tables.md §2.9` +0xF4/+0xF8
> "level/spawn-timer" mislabel — see §3.2).

Neutral, offset-model description of the legacy client's monster/NPC data: the **mob-template
record** parsed from `data/script/mobs.scr`, the **per-map spawn record** parsed from
`data/map<NNN>/npc<NNN>.arr`, and the foreign-key **linkage** between them. Promoted from
dirty-room notes; **rewritten** — no decompiler identifiers, no binary addresses. Every field
claim below is backed by an observable fact (a decoded byte range in a sample file, or a
file-format fact such as a fixed record stride), never by a code location.

This document is the design input for:

- the **assets-parser-engineer** decoding `mobs.scr` records and `npc.arr` records into managed
  template / spawn structs;
- the **domain engineer** consuming mob level, HP, type (normal vs. boss), and the spawn placement
  (id + world XZ + facing) to populate world entities.

> **Two related but distinct artifacts are documented here.**
>
> 1. **`mobs.scr` mob-template record** — a 488-byte fixed-stride on-disk record, one per distinct
>    mob/NPC type. This is the primary new contribution of this document.
> 2. **`npc.arr` spawn record** — a 28-byte fixed-stride on-disk record, one per placed instance per
>    map. The *file-format* aspects (container, record-count derivation, in-memory sentinel slot)
>    are owned by `Docs/RE/formats/npc_spawns.md`; this document gives the **struct/field view** and
>    its **linkage** to the template, and does not restate the container mechanics.
>
> All offsets in this document are **relative to the start of the record** (struct-relative). They
> are never binary addresses. Where a wider on-disk table is cited, the fact used is its fixed
> record stride, which is an observable file-format property.

---

## Status header

| Aspect | State |
|---|---|
| `mobs.scr` record stride (488 bytes) | **confirmed** (control-flow) — the loader divides file size by 488, allocates `488 × count`, and steps the record cursor by 488; cross-checked sample 1 950 536 ÷ 488 = exactly 3 997 records, zero remainder. |
| `mobs.scr` record count / id range | **confirmed** (control-flow) — `count = file_size / 488`; record `mob_id` ranges 11 .. 31 157 in the sample (sequential ≈10-step for normal mobs; boss ids cluster at 14 000+). |
| `mobs.scr` primary key = u16 `mob_id` at +0x00 | **confirmed** (control-flow) — the loader inserts each record into a key→record map keyed on the u16 at +0x00. |
| `mobs.scr` boss discriminator (+0x144 `== 11`) | **confirmed** (control-flow) — the loader tests the u8 at +0x144 and, when `== 11`, inserts the record into a second boss/elite-only lookup map. Sample distribution across all 3 997 records: 3 749 normal (0), 125 boss/elite (11), 2 special (12), remainder spread across small sub-type values 2..10. |
| `mobs.scr` HP qword (+0xF8 u64, load-time `+= 10`) | **confirmed** (control-flow) — the loader reads/mutates a 64-bit value at +0xF8 and adds 10 to it on load. The *offset and mutation* are confirmed; the in-game HP **scale factor** is **capture/debugger-pending** (see §1.3). |
| `mobs.scr` core fields (`mob_id`, HP qword offset, boss type byte) | **confirmed** (control-flow) — the loader exercises exactly three offsets: +0x00 (key), +0xF8 (HP qword +=10), +0x144 (boss type). |
| `mobs.scr` other fields (names, level, style, model, drop, scaling) | **sample_verified / static-hypothesis** — decoded consistently from on-disk sample bytes, but the loader never reads them; no consumer re-confirmed this pass. |
| `mobs.scr` combat / drop / scaling fields | **partial** — values decode cleanly and correlate with level / boss status, but the exact formula consuming them is not pinned. |
| `npc.arr` record stride (28 bytes) | **sample_verified** — owned by `formats/npc_spawns.md`; the two 28-byte samples decode to exactly one record each. |
| `npc.arr` core fields (`mob_id`, world XZ, spawn_type) | **confirmed** — see `formats/npc_spawns.md`. |
| `npc.arr` ↔ `mobs.scr` linkage | **partial** — the linkage mechanism (spawn `mob_id` keys the template table) is established, but the two available spawn samples reference an id (10118) not present in the sample `mobs.scr`, which points to a sibling `npc.scr` table (see open questions). |
| String encoding | **confirmed** — all in-record name strings are **CP949 / EUC-KR**, NUL-terminated. |
| Field byte boundaries overall | **partial / draft** for every field tagged below as `PARTIAL` / `UNVERIFIED`; these are static / sample inferences and need either a live capture or a non-zero sample before an engineer hard-codes semantics. |

Confidence per field is given inline in each table (`CONFIRMED`, `PARTIAL`, `UNVERIFIED`). The full
open list is at the end.

> **What is pinned vs. what needs more evidence.** The two record strides, the id/level/style/type
> fields, the CP949 name and region strings, the normal-vs-boss discriminator, the HP qword, the
> model id, and the spawn placement (`mob_id` + world XZ) are pinned and implementable now.
> Everything tagged `PARTIAL` / `UNVERIFIED` — most of the interior combat / scaling / drop fields,
> the exact `+0xF4` field identity, and the trailing reserved spans — still needs a live combat /
> spawn capture, or a sample with non-zero data, before its semantics are trusted.

---

## 1. mobs.scr — mob-template record (488 bytes, 0x1E8 stride)

### 1.1 File and record framing (facts)

| Property | Value |
|---|---|
| File | `data/script/mobs.scr` |
| Container | Headerless flat array — no magic, no version, no record count in the file. |
| Record stride | **488 bytes (0x1E8)** — **confirmed** (control-flow): the loader divides file size by 488, allocates `488 × count`, and advances the record cursor by 488. Cross-checked: file size ÷ 488 = whole record count, zero remainder. |
| Record count | `floor(file_size / 488)` — **confirmed** (loader division); 3 997 in the sample. |
| Endianness | Little-endian throughout (x86 client). |
| String fields | CP949 / EUC-KR, NUL-terminated, fixed-width slots (read up to the first NUL). |
| Primary key | `mob_id` (u16 at +0x00) — **confirmed** (control-flow): the loader inserts each record into a key→record map keyed on the u16 at +0x00. A second map is built for boss/elite records only (those whose type byte at +0x144 equals 11). |

> **Loader-applied mutation (important for HP) — CONFIRMED (control-flow).** When the file is loaded
> into memory, the loader reads the 64-bit value at +0xF8 and **adds 10 to it** for every record
> (`qword at +0xF8 += 10`, with 248 = 0xF8). The on-disk value at +0xF8 is therefore the base value,
> and the runtime value = on-disk value + 10. A parser that reads `mobs.scr` from disk and wants the
> runtime-equivalent HP base must add 10 to the +0xF8 qword. The offset and the `+= 10` mutation are
> control-flow confirmed; the *displayed-HP scale factor* remains capture/debugger-pending. See §1.3.

### 1.2 Record field table

All offsets are struct-relative (from the start of the 488-byte record). Sample values are quoted
from decoded records (record 0 = id 11, a level-1 normal mob; record 1 = id 12; the boss row = id
14000) to ground each claim.

> **What the loader actually reads (control-flow scope).** The loader exercises exactly **three**
> offsets by control flow: **+0x00** (u16 `mob_id`, the map key), **+0xF8** (the u64 HP qword it
> mutates `+= 10`), and **+0x144** (the u8 `mob_type` boss discriminator). Those three rows are
> **CONFIRMED**. **Every other field below** — `name`, `region_name`, `level`, `style`, the scaling /
> drop / combat fields, `model_id` — is decoded from on-disk **sample bytes** and is consistent
> across many records, but no consumer was re-located this pass; treat the `CONFIRMED` tags on those
> rows as **sample-verified** (strong on-disk evidence), not control-flow confirmed. The `PARTIAL` /
> `UNVERIFIED` tags below are unchanged.

| Offset | Size | Type | Field (proposed) | Conf | Meaning / sample evidence |
|--------|------|------|------------------|------|---------------------------|
| +0x00 | 2 | u16 | `mob_id` | CONFIRMED | Template primary key. Sample: 11, 12, 21, 22… (≈10-step sequence for normal mobs); boss ids at 14 000+. Cross-referenced by the spawn record's `mob_id`. |
| +0x02 | 17 | bytes[17] | `name` | CONFIRMED | Mob display name, **CP949**, NUL-terminated, fixed 17-byte slot (≤16 chars + NUL). Sample record 0 decodes to a Korean mob name; record 1 to another. |
| +0x13 | 17 | bytes[17] | `region_name` | CONFIRMED | Secondary CP949 NUL-terminated string in a 17-byte slot — a region / zone or sub-type label. Sample record 0 decodes to a Korean place name; the boss row to a longer label. |
| +0x24 | 16 | bytes[16] | (reserved) | UNVERIFIED | All zero in every observed record. Candidate: flags / extra-name / reserved. |
| +0x34 | 4 | i32 | `group_id` | PARTIAL | Small sequential integers. Two distinct value bands observed: low values (1, 2, …) and a higher band (≈379, 380, 381…). The two bands appear to separate factions or spawn-zone groups. No confirmed consumer. Sample: rec0=379, rec1=1, rec2=380. |
| +0x38 | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x3C | 4 | f32 | `attack_period` | PARTIAL | 3.0 in most records; 4.0 in a few (e.g. record 1). Likely a base attack-period / animation-cycle value (seconds). No confirmed consumer. |
| +0x40 | 20 | bytes[20] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x54 | 2 | u16 | `level` | CONFIRMED | Mob level (1..270 observed). Compared against player level for combat eligibility, and against tier thresholds for difficulty banding. Sample: rec0=1, rec2=2, boss=36. |
| +0x56 | 2 | u16 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x58 | 20 | bytes[20] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x6C | 2 | u16 | `style` | CONFIRMED | Targeting-style flag. The value **1** marks the mob as **not targetable by standard attack skills** (a "special style" mob). Zero in all observed sample records. |
| +0x6E | 10 | bytes[10] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x78 | 2 | u16 | `flag_aggressive` | PARTIAL | 1 in most records; 0 for some records (including the boss row) — those may be non-hostile-by-default or specially gated. Candidate: aggression flag. Sample: rec0=1, boss=0. |
| +0x7A | 2 | u16 | `flag_7A` | PARTIAL | 0 in most records; 1 in some high-level records. Purpose unknown. |
| +0x7C | 56 | bytes[56] | (reserved) | UNVERIFIED | All zero in observed records (a wide reserved span). |
| +0xB4 | 4 | i32 | `combat_const_B4` | PARTIAL | Constant 5 in all observed records. Candidate: number of combat phases / action group. |
| +0xB8 | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0xBC | 4 | f32 | `float_BC` | PARTIAL | Constant 1.0 in all observed records. |
| +0xC0 | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0xC4 | 4 | i32 | `stat_tier_lo` | PARTIAL | Constant 10 in observed records. Possibly a lower stat-tier bound. |
| +0xC8 | 4 | i32 | `stat_tier_a` | PARTIAL | Varies (11, 12, 13…) and correlates with mob tier. Appears paired with `stat_tier_b` at +0xD0. |
| +0xCC | 4 | i32 | `stat_tier_mid` | PARTIAL | Constant 10 (same as +0xC4) in observed records. |
| +0xD0 | 4 | i32 | `stat_tier_b` | PARTIAL | Equals `stat_tier_a` (+0xC8) in observed records. |
| +0xD4 | 4 | i32 | `stat_tier_c` | PARTIAL | Equals `stat_tier_a` (+0xC8) in observed records. |
| +0xD8 | 4 | i32 | `level_offset` | PARTIAL | −6 for most normal mobs; +6 for the boss row. Candidate: a level / stat adjustment. |
| +0xDC | 4 | f32 | `float_DC` | PARTIAL | 0.0 for most records; a small positive float for some. Purpose unknown. |
| +0xE0 | 8 | bytes[8] | (reserved) | UNVERIFIED | Usually zero. |
| +0xE8 | 4 | i32 | `xp_weight` | PARTIAL | Varies (5, 9, 6, 8, 14…) and correlates with mob tier. Equals `xp_weight_b` (+0xEC) in all observed records. Candidate: XP / reward weight. |
| +0xEC | 4 | i32 | `xp_weight_b` | PARTIAL | Equals `xp_weight` (+0xE8) in all observed records. |
| +0xF0 | 4 | i32 | (reserved) | UNVERIFIED | Zero in almost all records; non-zero (36) in the boss row, where it equals `level`. |
| +0xF4 | 4 | i32 | `field_F4` | UNVERIFIED | **−1** for many normal mobs; **0** for some; equals `level` for the boss row (36). **This field's identity is contested** — an earlier draft of the config-table spec labelled it "mob level" — but `level` is already confirmed at +0x54, and the **+0xF8 qword is the HP value, control-flow confirmed** (see next row and §1.3). Treat `field_F4` as a *secondary / effective-level or spawn-group key*, **UNVERIFIED**, pending a live HP capture. Sample: rec0=−1, rec1=0, boss=36. |
| +0xF8 | 8 | u64 | `base_max_hp` | **CONFIRMED** (offset + load mutation); scale-factor **capture-pending** | **Base max-HP value, 64-bit.** The loader reads this offset as a 64-bit value (`u64`) and **adds 10 to it on load** (runtime value = on-disk value + 10) — both the offset and the mutation are control-flow confirmed. The high 32 bits are zero in all observed records (HP < 2³²). On-disk sample values are small (rec0=33, rec1=40, boss=40), so a scale factor is very likely applied elsewhere to produce the in-game HP — the *scale factor* itself remains **capture/debugger-pending**; see §1.3 and open question 2. Supersedes the earlier "spawn timer" label for this offset. |
| +0x100 | 4 | u32 | `phys_atk_base` | PARTIAL | Scales with level. Sample: lv1→200, boss→440. Candidate: base physical attack parameter. |
| +0x104 | 4 | u32 | `phys_def_base` | PARTIAL | Scales with level. Sample: lv1→200, lv2→210, boss→810. Candidate: base physical defence parameter. |
| +0x108 | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x10C | 1 | u8 | `dungeon_tier` | PARTIAL | 0 in all sample records. A value greater than 1 gates a per-class dungeon-difficulty level-range check; candidate meaning: 0 = overworld, 1/2/3 = dungeon tiers. |
| +0x10D | 3 | bytes[3] | (reserved) | UNVERIFIED | Zero bytes (alignment after `dungeon_tier`). |
| +0x110 | 24 | f32[6] | `stat_scale_group` | PARTIAL | Six floats, always `[1.0, 1.0, 1.0, 1.0, 0.0, 1.0]` in all observed records. Likely per-category stat multipliers; purpose of the 0.0 entry unverified. |
| +0x128 | 4 | f32 | `variance_lo` | PARTIAL | 0.95 in all observed records (≈ −5% variance). |
| +0x12C | 4 | f32 | `variance_hi` | PARTIAL | 1.05 in all observed records (≈ +5% variance). |
| +0x130 | 4 | f32 | `variance_lo_2` | PARTIAL | 0.95 — second variance pair. |
| +0x134 | 4 | f32 | `variance_hi_2` | PARTIAL | 1.05 — second variance pair. |
| +0x138 | 4 | f32 | `power_scale_a` | CONFIRMED (boss/normal split) | Distinguishes boss from normal. Sample: normal ≈0.10..0.19; boss ≈3.09. Used in mob-power computation. |
| +0x13C | 4 | f32 | `power_scale_b` | CONFIRMED (boss/normal split) | Same pattern as `power_scale_a`. Sample: normal ≈0.13..0.23; boss ≈3.71. |
| +0x144 | 1 | u8 | `mob_type` | **CONFIRMED** (control-flow) | Mob-class discriminator. **0 = normal**, **11 = boss/elite**, **12 = special**; small values 2..10 are sub-types. The loader tests the u8 at +0x144 (324 = 0x144) and, when `== 11`, inserts the record into a second boss/elite-only lookup map — control-flow confirmed. Verified distribution across all 3 997 records: 3 749×0, 125×11, 2×12, remainder spread over 2..10. |
| +0x145 | 3 | bytes[3] | (reserved) | UNVERIFIED | Alignment after the type byte. |
| +0x148 | 4 | f32 | `drop_param_a` | PARTIAL | Normal: 40.0; boss: 35.0. Candidate: base drop probability or reward modifier. |
| +0x14C | 4 | f32 | `drop_param_b` | PARTIAL | Normal: 80.0; boss: 30.0. |
| +0x150 | 4 | u32 | `exp_group_id` | PARTIAL | Varies (0, 41, 51, 61, and a boss-only value). Candidate: EXP-reward group / zone EXP-table key. |
| +0x154 | 4 | u32 | `tier_const_154` | PARTIAL | 45 for the lowest-id records and for bosses; 50 for higher-id records. Candidate: a tier constant or faction tag. |
| +0x158 | 36 | bytes[36] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x17C | 4 | f32 | `float_17C` | PARTIAL | 0.0 for normal; 0.0 for most bosses (one boss had a non-zero value). |
| +0x180 | 16 | f32[4] | (reserved floats) | UNVERIFIED | 0.0 for all observed records (spans +0x180..+0x18F). |
| +0x190 | 4 | f32 | `float_190` | PARTIAL | 0.0 for normal; non-zero (≈40.0) for some bosses. |
| +0x194 | 4 | f32 | `move_speed` | PARTIAL | 2.1 in all observed records. Candidate: base move speed or speed multiplier. |
| +0x198 | 4 | u32 | `model_id` | PARTIAL | 6–9-digit visual / model asset id. Sample: 909001, 900011, 900091, 900021, 900381 (boss). Feeds the animation / mesh catalog lookup. |
| +0x19C | 12 | bytes[12] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1A8 | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1AC | 4 | u32 | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1B0 | 4 | u32 | `stat_budget` | PARTIAL | Normal: 350–402; boss: 300. Candidate: total stat budget / difficulty-scaling cap. Sample rec0=402, boss=300. |
| +0x1B4 | 4 | u32 | `flag_1B4` | PARTIAL | 100 for most records; 10 for a few (e.g. record 1). Candidate: a percentage modifier (100 = normal). |
| +0x1B8 | 8 | bytes[8] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1C0 | 4 | f32 | `aggro_range` | PARTIAL | 50.0 for normal mobs; 360.0 for the boss row. A zero value guards a special-case skill branch. Candidate: aggro / detection range. |
| +0x1C4 | 4 | f32 | `float_1C4` | PARTIAL | Constant 2.8 in all observed records. |
| +0x1C8 | 4 | f32 | `float_1C8` | PARTIAL | Varies with level and boss status (≈8.1 at lv1, ≈23.5 at lv2, ≈21.7 boss). Candidate: density / territory size. |
| +0x1CC | 4 | bytes[4] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1D0 | 2 | u16 | `drop_rate_pct` | PARTIAL | 100 in most records (a 100% base-rate modifier). Copied into a runtime HP-bar / display struct. |
| +0x1D2 | 2 | u16 | `drop_item_id_a` | PARTIAL | 0 or an item-template id. Sample: 548 (rec0), 0 (rec1). Cross-referenced against item-template ids; a match triggers a particle effect on the mob. Candidate: a "special drop" item reference. |
| +0x1D4 | 1 | u8 | `drop_type_a` | PARTIAL | 0 in most records; compared against an item's drop-type / category byte. |
| +0x1D5 | 1 | u8 | (reserved) | UNVERIFIED | Zero. |
| +0x1D6 | 2 | u16 | `flag_1D6` | PARTIAL | 0 or 1. Compared against a value in the item-extra record; may gate a drop condition. |
| +0x1D8 | 8 | bytes[8] | (reserved) | UNVERIFIED | Zero in observed records. |
| +0x1E0 | 2 | u16 | `drop_item_id_b` | PARTIAL | 0 in most records. A second drop-item cross-reference (same logic as `drop_item_id_a`). |
| +0x1E2 | 1 | u8 | `drop_type_b` | PARTIAL | 0 in all observed records. Paired drop-type check with `drop_item_id_b`. |
| +0x1E3 | 5 | bytes[5] | (reserved) | UNVERIFIED | Zero in observed records (trailing bytes to the 488-byte boundary). |

**Total record size: 488 bytes (0x1E8).** Sample-verified by exact division of the file size.

### 1.3 HP field disambiguation (resolves a prior spec note)

A prior version of the config-table spec (`Docs/RE/formats/config_tables.md §2.9`) split the
+0xF4 / +0xF8 region into "mob level at +0xF4" and "spawn timer at +0xF8". This document **corrects
that** based on independent evidence:

- `level` is already at **+0x54** (an unambiguous u16 compared against player level and tier
  thresholds — a sample inference, distinct from the loader-read offsets).
- The value at **+0xF8** is read by the loader as a 64-bit quantity and surfaced as the mob's
  **MAXHP** in the client (HP-bar / display path). The loader **adds 10 to this qword on load** —
  **control-flow confirmed** (the offset and the `+= 10` mutation are both read straight off the
  loader; 248 = 0xF8).
- The field at **+0xF4** is **−1 for most normal mobs** and **0 for others**, only matching `level`
  for the boss row — behaviour inconsistent with a plain "level" field.

**Resolution adopted here (now loader-confirmed):** +0xF8 = `base_max_hp` (u64, runtime value =
on-disk + 10) — the offset and mutation are CONFIRMED by control flow; +0xF4 = `field_F4`, an
UNVERIFIED secondary / effective-level or spawn-group key.

**Caveat (open question 2) — capture/debugger-pending:** the on-disk HP base values are small (33–40).
They are almost certainly multiplied by a scale factor (a candidate is one of the `power_scale_*`
floats at +0x138 / +0x13C, or the level / attack / defence parameters) to produce the in-game HP
pool. A live combat or CharSpawn / vitals capture for a mob of known HP is required to pin the exact
scaling. The *offset and the `+= 10` load mutation* are control-flow confirmed; the *scale factor* is
the only HP-related fact still pending. Until then, treat `base_max_hp` as a raw template input, not
the displayed HP.

### 1.4 Normal vs. boss/elite (type byte +0x144 == 11)

Boss/elite records (`mob_type` == 11; 125 of them in the sample) are inserted into a second,
boss-only lookup map in addition to the main key→record map. The clearest numeric tells separating a
boss from a normal mob in the sample:

| Field | Offset | Normal (sample) | Boss (id 14000) |
|---|---|---|---|
| `power_scale_a` | +0x138 | ≈0.10 .. 0.19 | ≈3.09 |
| `power_scale_b` | +0x13C | ≈0.13 .. 0.23 | ≈3.71 |
| `drop_param_a` | +0x148 | 40.0 | 35.0 |
| `drop_param_b` | +0x14C | 80.0 | 30.0 |
| `aggro_range` | +0x1C0 | 50.0 | 360.0 |
| `phys_atk_base` | +0x100 | 200 (lv1) | 440 |
| `phys_def_base` | +0x104 | 200 (lv1) | 810 |

### 1.5 Confirmed-field quick reference (mobs.scr)

| Offset | Field | Type | Confidence |
|--------|-------|------|------------|
| +0x00 | `mob_id` | u16 | **CONFIRMED** (loader key) |
| +0x02 | `name` | bytes[17] (CP949) | sample_verified (loader does not read) |
| +0x13 | `region_name` | bytes[17] (CP949) | sample_verified (loader does not read) |
| +0x54 | `level` | u16 | sample_verified (loader does not read) |
| +0x6C | `style` | u16 | sample_verified (==1 ⇒ not targetable by standard attacks) |
| +0xF8 | `base_max_hp` | u64 | **CONFIRMED** offset + `+= 10` load mutation; scale factor capture-pending |
| +0x138 | `power_scale_a` | f32 | sample_verified (boss/normal split) |
| +0x13C | `power_scale_b` | f32 | sample_verified (boss/normal split) |
| +0x144 | `mob_type` | u8 | **CONFIRMED** (loader boss discriminator: 0=normal, 11=boss, 12=special) |
| +0x198 | `model_id` | u32 | sample_verified (pattern visual id; loader does not read) |

---

## 2. npc.arr — spawn record (28 bytes, 0x1C stride)

> **The file-format spec for `.arr` is `Docs/RE/formats/npc_spawns.md`** — that document owns the
> container mechanics (headerless flat array, `record_count = floor(file_size / 28)`, the prepended
> all-zero sentinel slot 0, the 16-byte map-000 anomaly). This section gives only the **struct/field
> view** needed to populate a spawned entity, and is intentionally consistent with that spec.

### 2.1 Record field table (struct-relative)

| Offset | Size | Type | Field | Conf | Meaning / sample evidence |
|--------|------|------|-------|------|---------------------------|
| +0x00 | 2 | u16 | `mob_id` | CONFIRMED | Template key → `mobs.scr` (or `npc.scr`) `mob_id`. Both 28-byte samples: 10118. |
| +0x02 | 2 | u16 | `field_02` | UNVERIFIED | 67 in both 28-byte samples. Candidate: level override / sub-type / faction. No confirmed consumer. |
| +0x04 | 4 | f32 | `world_x` | CONFIRMED | World-space X. Samples: 24836.0, 25278.0. |
| +0x08 | 4 | f32 | `world_z` | CONFIRMED | World-space Z. Samples: 64680.0, 39052.0. Y (height) is not stored; the terrain system supplies ground height. |
| +0x0C | 4 | f32 | `rotation_y` | PARTIAL | Facing yaw in radians, Y-axis. Samples: 1.541043 (≈π/2), 1.680247. Sample-inference only — no confirmed consumer reading this offset by name. |
| +0x10 | 4 | u32 | `spawn_type` | CONFIRMED | Spawn-group / modifier. Value 7 = elite/boss modifier (a +10% bonus branch); also matched against an in-scene actor's spawn-group id. Both 28-byte samples: 0 (standard). |
| +0x14 | 4 | u32 | `unknown_14` | UNVERIFIED | Zero in all 28-byte samples. Candidate: respawn delay (s) or max live count. |
| +0x18 | 4 | u32 | `unknown_18` | UNVERIFIED | Zero in all 28-byte samples. Candidate: spawn radius or group size. |

**Total record size: 28 bytes (0x1C).** Sample-verified; stride owned by `formats/npc_spawns.md`.

### 2.2 spawn_type known values

| Value | Meaning |
|------:|---------|
| 0 | Standard spawn — no modifier (both samples). |
| 7 | Elite / boss modifier — spawn-matching code applies a +10% bonus (110 vs. baseline 100). |

All other values are unobserved in available samples.

---

## 3. Linkage — npc.arr `mob_id` → mobs.scr template

The spawn record's `mob_id` (+0x00) is the **foreign key** into the mob-template table. The
template lookup map itself is **control-flow confirmed**: the `mobs.scr` loader builds a key→record
map keyed by the u16 `mob_id` at +0x00 (a second boss-only map is built for type-11 records). At map
load the runtime resolves each spawn's `mob_id` through that template lookup, retrieving the 488-byte
template record used for name display, level, HP-bar, mob type (normal/boss), model id, and combat
eligibility (the *resolution-at-spawn* step is consistent but was not itself re-traced this pass —
static-hypothesis). The resolved record drives:

- the actor's resolved model / mesh class id (from `model_id` at template +0x198);
- the displayed name (template `name` at +0x02, CP949);
- the level / type used in combat-eligibility and HP-bar logic.

> **Caveat on the available samples.** Both 28-byte `npc.arr` samples reference `mob_id` 10118, which
> is **not present** in the sample `mobs.scr` (whose ids top out at 31 157 but do not include 10118 in
> the contiguous low band). The same lookup table is populated from whichever mob/NPC `.scr` file was
> loaded; ids in this range are expected to live in a sibling **`npc.scr`** table. `npc.scr`'s record
> stride and layout are **not yet established** (a separate analysis target). Engineers should treat
> the linkage mechanism (spawn id → template record) as the same for both `.scr` sources, and not
> assume every spawn id resolves inside `mobs.scr` alone. See open question 5.

---

## 4. Sibling tables (for context — not specified here)

The dirty-room recon places `mobs.scr` alongside several sibling NPC/quest tables loaded by the same
subsystem. They are noted here only so an engineer understands the scope boundary; their layouts are
separate analysis targets and are **not** specified by this document:

- **`npc.scr`** — fixed-stride NPC-definition records carrying several CP949 string sub-fields
  (name + dialog/label slots). Stride not yet established. Likely the home of spawn ids (such as
  10118) that do not resolve inside `mobs.scr`.
- **`npcs.scr`** — a larger sibling/variant NPC table.
- Quest / tutorial tables (`quests.scr`, `Tutor.scr`) — unrelated to the mob/spawn structs here.

---

## 5. Proposed canonical names (flag for names.yaml; not applied here)

> Names are proposed only. This document does not edit `names.yaml`.

**mobs.scr template record (`MobTemplateRecord`, 488 bytes):**
`mob_id` (+0x00), `name` (+0x02, CP949), `region_name` (+0x13, CP949), `level` (+0x54),
`style` (+0x6C), `base_max_hp` (+0xF8, u64, runtime += 10), `power_scale_a` (+0x138),
`power_scale_b` (+0x13C), `mob_type` (+0x144), `model_id` (+0x198), `aggro_range` (+0x1C0),
`drop_item_id_a` (+0x1D2), `drop_item_id_b` (+0x1E0).

**npc.arr spawn record (`NpcSpawnRecord`, 28 bytes):**
`mob_id` (+0x00), `field_02` (+0x02), `world_x` (+0x04), `world_z` (+0x08),
`rotation_y` (+0x0C), `spawn_type` (+0x10), `unknown_14` (+0x14), `unknown_18` (+0x18).
(Several already exist in `names.yaml` via `formats/npc_spawns.md`; reuse those.)

---

## 6. Open questions

1. **`field_F4` (mobs.scr +0xF4) identity.** i32 that is −1 / 0 for most normal mobs but equals
   `level` for the boss row. Not a plain "level" (that is +0x54) and not HP (that is +0xF8). Needs a
   live HP / spawn capture to settle (secondary level vs. spawn-group key vs. effective level).
2. **`base_max_hp` scaling (mobs.scr +0xF8).** Raw on-disk base values are small (33–40); the in-game
   HP pool is almost certainly the base (runtime += 10) multiplied by a scale factor — a candidate is
   `power_scale_a/b` (+0x138 / +0x13C) or the level / atk / def parameters. Requires a known-HP mob
   capture to pin the formula.
3. **`group_id` (mobs.scr +0x34).** Two value bands (low 1..N; high ≈379..N). Faction id, content
   grouping, or spawn-zone key? No confirmed consumer.
4. **Combat / reward field group (mobs.scr +0xB4..+0xF0, +0x100/+0x104, +0xE8/+0xEC).** Values decode
   and correlate with tier, but the exact combat / XP formula consuming them is untraced.
5. **Spawn id 10118 → `npc.scr`.** Both `.arr` samples reference an id absent from the sample
   `mobs.scr`; it is expected in a sibling `npc.scr` whose stride/layout is not yet established. A
   dedicated `npc.scr` pass is needed before mixed NPC+mob spawns can be fully resolved.
6. **`npc.arr` `field_02` (= 67), `unknown_14`, `unknown_18`.** No confirmed consumers; `field_02`
   constant 67 and the two unknowns are zero in all available samples. Needs a sample with non-zero
   data (or an editor/tool format reference).
7. **`npc.arr` `rotation_y` (+0x0C).** Yaw interpretation rests on sample bytes clustering near π/2;
   no confirmed consumer reading this offset by name. Verify against a non-default-facing sample.
8. **mobs.scr drop cross-references (+0x1D2 / +0x1E0 and their type bytes).** The drop-item ids and
   type bytes are cross-checked against item templates at runtime, but the exact drop-condition logic
   and the meaning of `drop_param_a/b` (+0x148 / +0x14C) and `drop_rate_pct` (+0x1D0) are untraced.
9. **Wide reserved spans (mobs.scr +0x7C..+0xB3, +0x158..+0x17B, and the tail +0x1D8..+0x1E7).**
   Entirely zero in all observed records; may be true reserved padding or fields that are non-zero
   only in records not present in the sample.
10. **Capture coverage.** All field semantics are recovered from on-disk sample bytes and static
    evidence, not from a live capture. The strides, ids, names, level/style/type, and the
    normal/boss split are sample-verified and implementable now; everything tagged `PARTIAL` /
    `UNVERIFIED` should be re-checked against a live capture or a wider sample set before an engineer
    hard-codes its semantics.

---

## 7. Cross-references

| Document | Relationship |
|---|---|
| `Docs/RE/formats/npc_spawns.md` | Authoritative **file-format** spec for `.arr` (container, record-count, sentinel slot, anomaly). This struct doc defers all container mechanics to it. |
| `Docs/RE/formats/config_tables.md` | General fixed-record `.scr` catalogue; §2.9 covers `mobs.scr` at a higher level. §1.3 here resolves its +0xF4/+0xF8 "level/spawn-timer" note. |
| `Docs/RE/structs/actor.md` | Live in-world entity; `sort == 2` (Mob) / `3` (NPC) actors are populated from a spawn record + template resolved as in §3. |
| `Docs/RE/structs/spawn_descriptor.md` | The 880-byte wire descriptor for player/actor spawns (a different path from the disk-side mob template here). |
| `Docs/RE/structs/item.md` | Item templates referenced by the mobs.scr drop-item cross-reference fields. |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
