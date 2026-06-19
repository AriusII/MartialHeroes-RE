---
verification: confirmed
ida_reverified: 2026-06-19
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: 4/48 record array (8 × 28-byte records starting at body+24) overruns the 236-byte copied body by 12 bytes — flagged UNVERIFIED (either the routed frame carries more than 236 bytes, fewer than 8 records actually populate, or the in-memory walk stride differs from the wire); 4/56 secondary-array byte base (the +1040 region) is a dword-vs-byte-pointer static-hypothesis; 4/71 sub-records past +52 are mapped relative to the consumer's copied struct, not 1:1-proven against wire offsets; 5/73 (SmsgQuestComplete vs SmsgGuildWarInfoUpdate) name is INCONCLUSIVE from body shape — not tabled as a struct here
---

# Structured S2C packet-body DTOs (clean-room offset tables)

Neutral, rewritten **C-struct-style offset tables** for the large server→client (S2C) panel
snapshots that have a recovered internal layout. Promoted from dirty-room body-shape notes;
**rewritten** — no decompiler identifiers, no binary addresses. This file is the offset-table
backing for the IDB struct typing of the packet-body DTOs.

> **Scope and authority.** This file presents each body as a **named C-struct-style offset table**
> (the layout view used by the IDB type system). The **wire-level field view** — directions, opcode
> ids, value semantics, CP949 notes — is owned by the corresponding `packets/*.yaml` spec and the
> `opcodes.md` catalogue; **cross-referenced by name, not duplicated here.** The 880-byte
> SpawnDescriptor embedded in the 4/4 actor record is owned by `structs/spawn_descriptor.md` (not
> re-derived here).

**Offset convention.** Unless a table says otherwise, all offsets are **payload-relative** — relative
to the start of the body, i.e. immediately after the 8-byte frame header
`[u32 size][u16 major][u16 minor]`. Record-relative offsets are labelled per-record. The body sizes
below are the fixed copy lengths the handlers read off the wire and are **CONFIRMED** unless marked.

**Confidence vocabulary.** `CONFIRMED` = control-flow / disassembly confirmed (a fixed copy length,
an explicit constant-bounded loop, or a disasm-pinned stride proves it). `UNVERIFIED` = hypothesis
only; do not hard-code. **VALUE semantics of individual bytes are PENDING throughout** — these tables
claim section boundaries, strides, counts, and record geometry only.

Cross-references: `packets/*.yaml` (per-opcode wire specs), `opcodes.md`,
`specs/handlers.md`, `structs/spawn_descriptor.md` (the 880-byte descriptor),
`structs/net_handler.md` (the dispatch tables these bodies route through).

---

## 1. 4/65 — Guild full-sync snapshot (body = 1812 bytes / 0x714, CONFIRMED)

A header/scalar region (≈ +0 .. +59) followed by **seven parallel 50-entry member arrays** walked
by a single 50-trip loop. The full-sync path is the structured one (a subtype byte at +8 selects
full-sync vs guild-leave). The seven arrays are dense from +60 to the exact body end (+1812); no
trailing bytes. Wire-level view: `packets/4-65_*.yaml`.

### Header / scalar region (payload-relative; VALUE semantics PENDING)

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `subtype` | CONFIRMED | Selector: 1 = full-sync (the structured path), else = guild-leave. |
| +0x0A | ≤18 | char[] | `guild_title` | CONFIRMED (region) | Guild title/name, CP949, NUL-terminated; region ends by +28. |
| +0x1C | 2 | uint16 | `guild_id` | CONFIRMED | Guild id. |
| +0x1E | 1 | uint8 | `guild_rank_byte` | CONFIRMED | Guild rank / grade byte. |
| +0x20 | 4 | uint32 | `field_count_level` | CONFIRMED | Guild count/level field. Meaning PENDING. |
| +0x24 | 4 | uint32 | `points_low` | CONFIRMED | Guild points/rank low (tier-compared with `points_hi`). |
| +0x28 | 4 | uint32 | `points_hi` | CONFIRMED | Guild points/rank hi pair. |
| +0x2C | 8 | uint64 | `guild_funds` | CONFIRMED | Guild funds / gold (64-bit, thousands-grouped on display). |
| +0x34 | 1 | uint8 | `notice_flag` | CONFIRMED | Guild flag / notice byte. |

The header occupies roughly +0 .. +60; the parallel arrays begin at +60.

### The 7 parallel 50-entry member arrays (single 50-trip loop, CONFIRMED)

Each array is `count = 50`. The single fused loop advances all seven with independent per-array
strides. Roles are PENDING (hypothesised below).

| # | Field | Base offset | Stride | Count | Element type | Role (PENDING) |
|---|-------|-------------|--------|-------|--------------|----------------|
| 1 | `member_ids` | +60 (+0x3C) | 4 | 50 | uint32 | Member id (passed to the actor composite-key find). |
| 2 | `member_flags_a` | +260 (+0x104) | 1 | 50 | uint8 | Online / state byte. |
| 3 | `member_flags_b` | +1160 (+0x488) | 1 | 50 | uint8 | Second per-member byte flag. |
| 4 | `member_names` | +310 (+0x136) | 17 | 50 | char[17] | Member name, fixed 17-byte CP949 cell (NUL-padded). |
| 5 | `member_points` | +1212 (+0x4BC) | 4 | 50 | int32 | Per-member points/contribution (wire element 32-bit; widened to 64-bit host-side). |
| 6 | `member_array_f` | +1412 (+0x584) | 4 | 50 | uint32 | Per-member dword (re-scanned by a second consumer; rank or login flag). |
| 7 | `member_array_g` | +1612 (+0x64C) | 4 | 50 | uint32 | Per-member dword (login time or second contribution). |

> Array #7 ends at +1612 + 50·4 = +1812 = exact body end (0x714). The names array's fixed 17-byte
> cell is the surest signal it is a name table. Array #5 is read 32-bit then sign-extended to a
> 64-bit slot — the **wire element is 32-bit**.

---

## 2. 4/103 — Guild panel text update (body = 204 bytes / 0xCC, CONFIRMED)

An 8-byte leading region followed by **four fixed 49-byte CP949 text cells** that fill the body
exactly (+155 + 49 = +204; no trailing bytes). Wire-level view: `packets/4-103_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x00 | 8 | bytes | `header` | UNVERIFIED | Leading 8 bytes, not read as text; possibly subtype/flags. |
| +0x08 | 49 | char[49] | `text_line_1` | CONFIRMED | Guild text line 1 (CP949). |
| +0x39 | 49 | char[49] | `text_line_2` | CONFIRMED | Guild text line 2 (CP949). |
| +0x6A | 49 | char[49] | `text_line_3` | CONFIRMED | Guild text line 3 (CP949). |
| +0x9B | 49 | char[49] | `text_line_4` | CONFIRMED | Guild text line 4 (CP949). |

---

## 3. 4/56 — Structured panel / spawn snapshot (body = 1552 bytes / 0x610, CONFIRMED)

Reclassified from a "thin slot" to a structured ~1552-byte snapshot: a leading scalar block (subtype
byte at +8, actor id at +12) plus a **64-entry dual transform table**. Loop guard proves 64 entries
(arrays A and B advance in lockstep). Wire-level view: `packets/4-56_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `subtype` | CONFIRMED | 1 selects the structured population path. |
| +0x0C | 4 | uint32 | `actor_key` | CONFIRMED | Actor id passed to the composite-key find. |
| +0x10 | 1024 | record[64] | `transform_array_a` | CONFIRMED | 64 entries × 16 bytes: 4 × uint32 per slot (per-slot transform/position, x/y/z + 1 word). Byte base +16 is solid (walked as dwords). |
| +0x410 | 512 | record[64] | `transform_array_b` | UNVERIFIED (base) | 64 entries × 8 bytes: 2 × uint32 per slot (secondary pair — rotation/state). Count 64 and stride 8 are CONFIRMED, but the **byte base is a dword-vs-byte-pointer static-hypothesis** (the +1040 region, shown here as +0x410); debugger-confirmable. |

> After the two arrays + scalar tail the block fills to 1552; remaining bytes (name/title strings
> bound from the matched actor) are host-side, not extra wire fields.

---

## 4. 4/71 — Structured panel snapshot (body = 1092 bytes / 0x444, CONFIRMED)

Reclassified from a "thin slot" to a structured ~1092-byte snapshot: a subtype byte at +8 plus an
**8-entry id array** at +12 and an **8-entry status array** at +44, followed by further fixed-stride
sub-records. Wire-level view: `packets/4-71_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `subtype` | CONFIRMED | Tested == 1 to select the structured path. |
| +0x0C | 32 | uint32[8] | `id_array` | CONFIRMED | 8 entries × 4 bytes (+12 .. +44); non-zero = active slot; compared to a panel id table and to the local-player key. |
| +0x2C | 8 | uint8[8] | `status_array` | CONFIRMED | 8 entries × 1 byte (+44 .. +52); a per-slot status byte (value == 2 checked). |
| +0x34 | (rest) | bytes | `sub_records` | UNVERIFIED | The remainder past +52 feeds further fixed-stride sub-tables (80-byte slot records and 17-byte CP949 name cells) inside the consumer's destination struct. Their precise **wire** offsets are a static-hypothesis (the consumer indexes its own copied struct, so wire-vs-struct mapping past +52 is not 1:1-proven). |

> What is proven: total = 1092, `subtype` @ +8, `id_array` @ +12 (×8, stride 4), `status_array` @
> +44 (×8, stride 1). The sub-records past +52 are UNVERIFIED in their wire placement.

---

## 5. 5/68 — Quest-list snapshot (body = 452 bytes / 0x1C4, CONFIRMED)

Three header bytes, two 10-byte state/flag vectors, then **parallel** quest-id and quest-name arrays
(20 entries each — ids fully precede names, not interleaved). Fills the body exactly (name array ends
at +452). Wire-level view: `packets/5-68_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `header_flag_a` | CONFIRMED | Header byte A. |
| +0x09 | 1 | uint8 | `header_flag_b` | CONFIRMED | Header byte B. |
| +0x0A | 1 | uint8 | `header_flag_c` | CONFIRMED | Header byte C. |
| +0x0B | 10 | uint8[10] | `state_vector_a` | CONFIRMED | 10-byte state/flag vector A (likely category/tab summary). |
| +0x15 | 10 | uint8[10] | `state_vector_b` | CONFIRMED | 10-byte state/flag vector B. |
| +0x20 | 80 | uint32[20] | `quest_ids` | CONFIRMED | 20 quest-id dwords (parallel array, precedes names). |
| +0x70 | 340 | char[20][17] | `quest_names` | CONFIRMED | 20 entries × 17-byte CP949 cell (NUL forced at entry+16). Ends at +452. |

---

## 6. 5/77 — Rank-progress bulk panel (body = 400 bytes / 0x190, CONFIRMED)

A leading actor key, then **two CP949 record arrays** (8 entries and 12 entries, 17-byte stride each)
each followed by a parallel per-entry flag byte-vector, plus a single title string. Forwarded only
when the addressed actor is the local player. Wire-level view: `packets/5-77_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x04 | 4 | uint32 | `actor_key` | CONFIRMED | Actor composite key (used by the composite-key find). |
| +0x08 | 136 | char[8][17] | `names_a` | CONFIRMED | Record array A: 8 entries × 17-byte CP949 cell. |
| +0x90 | 8 | uint8[8] | `flags_a` | CONFIRMED | Per-entry flags for array A. |
| +0x98 | 17 | char[17] | `title_string` | CONFIRMED | Title/name string (CP949). |
| +0xA9 | 1 | uint8 | `title_flag` | CONFIRMED | Label-state flag. |
| +0xAA | 204 | char[12][17] | `names_b` | CONFIRMED | Record array B: 12 entries × 17-byte CP949 cell. |
| +0x176 | 12 | uint8[12] | `flags_b` | CONFIRMED | Per-entry flags for array B. |
| +0x182 | 14 | bytes | (trailing) | UNVERIFIED | Tail bytes +386 .. +399 not touched by the consumer; trailing pad or additional fields PENDING. |

---

## 7. 4/48 — Rank-progress event (body = 236 bytes / 0xEC, CONFIRMED size) — record overrun UNVERIFIED

Two routing bytes at +8/+9, then a record array handed to the panel at body+12. The record array is
**8 interleaved 28-byte records starting at body+24** (id + 18-byte CP949 name + two int16). Wire-level
view: `packets/4-48_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `route_byte` | CONFIRMED | Non-zero → detail path. |
| +0x09 | 1 | uint8 | `subselect_byte` | CONFIRMED | 0/1 — picks UI list entry. |
| +0x0C | — | — | (record-array base hand-off) | CONFIRMED | Panel parser is handed body+12 (first 12 bytes are a header consumed upstream). |
| +0x18 | 224 | record[8] (stride 28) | `record_array` | CONFIRMED (stride/count) / **UNVERIFIED (fit)** | 8 records × 28 bytes starting at +24. **The array end (body+248) overruns the copied 236-byte body by 12 bytes** — see open question. |

### Per-record layout (record-relative; record base E = body+24 + i·28)

| Rec offset | Size | Type | Field | Confidence | Notes |
|------------|------|------|-------|------------|-------|
| E+0x00 | 4 | uint32 | `presence_id` | CONFIRMED | Presence/id key; zero ⇒ empty slot (blank labels). |
| E+0x04 | 18 | char[18] | `name` | CONFIRMED | CP949 name string. |
| E+0x16 | 2 | int16 | `value_a` | CONFIRMED | Numeric field A. |
| E+0x18 | 2 | int16 | `value_b` | CONFIRMED | Numeric field B. |

---

## 8. 4/4 — Area entity snapshot: per-tag actor record (892 bytes / 0x37C, CONFIRMED)

The 4/4 stream is a 17-byte area header followed by a tag-dispatched record loop (tag 0 = terminator).
The structured per-tag record this file tables is the **ID-FIRST 892-byte actor record** used by tags
1/2/3 (player / mob / npc). The section framing, header, and the smaller tag records (tag-4 ground
item 24 B, tag-6 guild name 36 B, tag-9 title 24 B) are owned by `specs/handlers.md §10` and
`packets/4-4_*.yaml`; only the 892-byte actor-record frame is tabled here because it is the one with a
recovered internal C-struct shape.

**892 = 8 (prefix) + 880 (descriptor) + 4 (trailer).** The 880-byte core is the `SpawnDescriptor` —
see `structs/spawn_descriptor.md` (not re-derived here). The record is **ID-FIRST**: the first wire
field is the entity id-key.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x000 | 4 | uint32 | `prefix_entity_id` | CONFIRMED (offset) / meaning likely | On-wire prefix entity id-key (the lookup key; stamped into the live actor). **ID-FIRST.** |
| +0x004 | 1 | uint8 | `prefix_variant` | CONFIRMED (offset) / meaning UNVERIFIED | Variant/kind byte (gates a == 5 visual path). |
| +0x005 | 1 | uint8 | `prefix_byte_05` | CONFIRMED (offset) / meaning UNVERIFIED | Stamped into a live-actor slot. |
| +0x006 | 2 | bytes | (prefix pad) | UNVERIFIED | Not individually referenced; likely padding. |
| +0x008 | 880 | obj | `descriptor` | CONFIRMED (size) | The 880-byte `SpawnDescriptor` core (begins at on-wire +8). Owned by `structs/spawn_descriptor.md`. |
| +0x378 | 1 | uint8 | `trailer_byte_378` | CONFIRMED (offset) / meaning UNVERIFIED | Stamped into a live-actor slot. |
| +0x379 | 1 | bytes | (trailer pad) | UNVERIFIED | Likely padding. |
| +0x37A | 1 | uint8 | `trailer_byte_37A` | CONFIRMED (offset) / meaning UNVERIFIED | When set, drives a live-actor timer-slot to 60. |
| +0x37B | 1 | bytes | (trailer pad) | UNVERIFIED | Pad to the 892-byte total. |

> **Cross-frame note.** 4/4 tags 1/2/3 = 892. The 5/3 CharSpawn (908) and 5/1 ActorSpawnExtended
> (912) frame the **same** 880-byte descriptor with their own prefix/trailer — they are different
> wire frames around one descriptor core, not conflicting descriptor sizes
> (`structs/spawn_descriptor.md`).

---

## Open questions (UNVERIFIED / PENDING)

1. **4/48 record overrun (UNVERIFIED fit).** The 8 × 28-byte record array ends at body+248 vs the
   236-byte copied body — a 12-byte overrun. Reconcile against a live read: either the routed frame
   carries more than 236 bytes, fewer than 8 records populate, or the in-memory walk stride differs
   from the wire stride. Stride 28 and count 8 are CONFIRMED; the body-vs-array fit is UNVERIFIED.
2. **4/56 secondary-array byte base (UNVERIFIED).** The 64-entry, 8-byte-stride `transform_array_b`
   base is a dword-vs-byte-pointer static-hypothesis (the +1040 region); count and stride are
   CONFIRMED.
3. **4/71 sub-records past +52 (UNVERIFIED).** Mapped relative to the consumer's copied struct, not
   1:1-proven against wire offsets.
4. **5/77 trailing 14 bytes (PENDING).** Bytes +386 .. +399 are untouched by the consumer — trailing
   pad or additional fields.
5. **5/73 name dispute (out of scope here).** SmsgQuestComplete vs SmsgGuildWarInfoUpdate is
   INCONCLUSIVE from body shape (a verdict header + a 10-row name+number table fits either; mild,
   non-decisive lean toward an info/ranking-table reading). Its body is verbatim-copied into a
   mirror struct, so its inner array's wire offset is not wire-loop-pinned — it is **not tabled as a
   struct DTO here**; see `packets/5-73_*.yaml` and the name-dispute owner.
6. **VALUE semantics PENDING throughout.** Every per-byte meaning (which flag is active, what the
   numeric fields represent) needs the protocol-value pass and/or a live capture; these tables claim
   boundaries, strides, counts, and record geometry only.
