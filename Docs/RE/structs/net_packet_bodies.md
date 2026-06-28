---
verification: confirmed
ida_reverified: 2026-06-20
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory - subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
layout: confirmed
value_semantics: capture/debugger-pending
conflicts: 4/48 record-array fit RESOLVED (no overrun — see §7), re-confirmed identical CYCLE 7; 4/56 1552-byte dual-transform shape re-confirmed identical CYCLE 7 (the +0x410 array-B base is now control-flow confirmed, not a static-hypothesis); 4/71 RESOLVED CYCLE 7 — the prior opaque tail past +52 is now broken into real fixed-stride sub-tables (id array, status array, two CP949 name-cell regions, 8-byte pair array, 8 × 80-byte slot records); 5/73 (SmsgQuestComplete vs SmsgGuildWarInfoUpdate) name is INCONCLUSIVE from body shape — not tabled as a struct here; the 5/68 record COUNT was corrected from 20 to 10 and split into wire (columnar, name-stride 17) vs runtime-mirror (slot stride 32) — see §5 and §9; every field VALUE meaning across the embedded body-record structs (§9) is capture/debugger-pending while the LAYOUTS are static-confirmed
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

> **Re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20).** Body shape
> **CONTROL-FLOW CONFIRMED**, fixed 1552 bytes, **no variable tail**. Re-confirmed identical to the
> prior reading, with one upgrade: the secondary array's byte base (+0x410) is now control-flow
> confirmed — the structured-apply path walks a single 64-trip loop whose cursor advances 48 bytes per
> trip (16 from array A + 8 from array B), and the spawn-populate routine copies the whole 0x610 block
> a second time, independently confirming 1552. The "dword-vs-byte-pointer static-hypothesis" caveat
> on the +0x410 base is **retired**. VALUE meanings remain capture/debugger-pending.

Reclassified from a "thin slot" to a structured 1552-byte snapshot: a leading scalar block (subtype
byte at +8, actor key at +12) plus a **64-entry dual transform table** (arrays A and B advance in
lockstep). The subtype byte gates the path: 1 = structured apply (walks the table); 0 = tear down a
UI sub-view. Wire-level view: `packets/4-56_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x000 | 8 | bytes[8] | `header` | CONFIRMED (region) | Leading header region; the subtype gate is the next byte. |
| +0x008 | 1 | uint8 | `subtype` | CONFIRMED | Subtype gate: 1 = structured apply; 0 = tear down a UI sub-view. |
| +0x009 | 3 | bytes[3] | (filler) | CONFIRMED | Filler to the actor key. |
| +0x00C | 4 | uint32 | `actor_key` | CONFIRMED | Actor composite key, passed to the cached by-key actor lookup. |
| +0x010 | 1024 | record[64] | `transform_array_a` | CONFIRMED | 64 entries × 16-byte stride: 4 × uint32 each (per-slot transform/position). Base +0x010 walked as dwords. |
| +0x410 | 512 | record[64] | `transform_array_b` | CONFIRMED | 64 entries × 8-byte stride: 2 × uint32 each (secondary pair, e.g. rotation/state), lockstep with A; ends at +0x610. Base now control-flow confirmed. |

> **Fit:** 8 + 1 + 3 + 4 + 1024 + 512 = **1552** (0x610), block closes exactly.
>
> **Matched-actor name/title are HOST-side, NOT wire bytes.** After the table walk the consumer
> resolves the actor via the +0x0C key and binds name/title strings read from the resolved actor
> object — they are not additional wire bytes. The 1552 bytes are fully accounted for by the header +
> the two arrays; do not invent name/title byte offsets inside the body.

---

## 4. 4/71 — Structured 8-slot relation/party panel snapshot (body = 1092 bytes / 0x444, CONFIRMED)

> **Re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20).** Body shape
> **CONTROL-FLOW CONFIRMED**, fixed 1092 bytes, **no variable tail** (every sub-table is 8-slot,
> fixed-stride — no count-prefixed remainder). **This pass RESOLVES the prior +52 opaque blob into
> real sub-tables.** The panel walker copies the whole 0x444 block verbatim into its destination
> struct (273 contiguous dwords = 1092) and then reads its sub-tables from that copy; the wire layout
> below is recovered by subtracting the constant copy-base offset from each struct read, so every
> sub-table's wire placement is now pinned, not a struct-relative hypothesis. Each sub-table is
> iterated by an 8-trip loop. VALUE meanings remain capture/debugger-pending.

A subtype byte at +8 selects the structured path, then an **8-slot id array**, an **8-slot status
array**, two **8-cell CP949 name-cell regions** (one chosen per slot by a self-vs-other test), an
**8-entry 8-byte pair array**, and an **8-record array** of 80-byte slot records. Two 32-byte gaps
sit between the status array and the name-cell regions; this consumer does not read them. Wire-level
view: `packets/4-71_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x000 | 8 | bytes[8] | `header` | CONFIRMED (region) | Leading header region; the subtype gate is the next byte. |
| +0x008 | 1 | uint8 | `subtype` | CONFIRMED | Subtype gate: tested == 1 to select the structured panel-apply path. |
| +0x009 | 3 | bytes[3] | (filler) | CONFIRMED | Filler to the id array. |
| +0x00C | 32 | uint32[8] | `id_array` | CONFIRMED | Per-slot id array, 8 × uint32 (4-byte stride), +0x0C .. +0x2C; non-zero = active slot; compared to a panel id table and to the local-player id. |
| +0x02C | 8 | uint8[8] | `status_array` | CONFIRMED | Per-slot status byte array, 8 × uint8 (1-byte stride), +0x2C .. +0x34; a per-slot status byte (value == 2 tested). |
| +0x034 | 32 | bytes[32] | (reserved gap 1) | CONFIRMED (region) | Reserved gap; not read by this consumer (no read site between +0x34 and +0x54). |
| +0x054 | 136 | char[17][8] | `name_cells_other` | CONFIRMED | Name-cell region 1: 8 cells × 17-byte CP949 stride; selected when the slot id ≠ local-player id (other). |
| +0x0DC | 32 | bytes[32] | (reserved gap 2) | CONFIRMED (region) | Reserved gap; not read by this consumer (no read site between +0xDC and +0xFC). |
| +0x0FC | 136 | char[17][8] | `name_cells_self` | CONFIRMED | Name-cell region 2: 8 cells × 17-byte CP949 stride; selected when the slot id == local-player id (self). |
| +0x184 | 64 | record[8] (stride 8) | `pair_array` | CONFIRMED | Per-slot 8-byte pair array: 8 entries × 8-byte stride (2 × uint32, read as a qword). |
| +0x1C4 | 640 | record[8] (stride 80) | `slot_record_array` | CONFIRMED | Per-slot record array: 8 records × 80-byte stride (see below); ends at +0x444. |

> **Fit:** 8 + 1 + 3 + 32 + 8 + 32 + 136 + 32 + 136 + 64 + 640 = **1092** (0x444), block closes
> exactly.

### Per slot-record layout (80-byte stride = 5 sub-cells × 16 bytes; sub-cell offsets sub-cell-relative)

Each 80-byte record is **5 sub-cells × 16 bytes**. A sub-cell is applied to the panel only when its
`presence_guard` dword (sub-cell +4) is non-zero; when live, the full 16-byte sub-cell is copied.

| Sub-cell offset | Size | Type | Field | Confidence | Notes |
|-----------------|------|------|-------|------------|-------|
| +0x00 | 4 | uint32 | `key_or_type` | CONFIRMED | Sub-cell key / type. |
| +0x04 | 4 | uint32 | `presence_guard` | CONFIRMED | Non-zero ⇒ the sub-cell is live and gets applied. |
| +0x08 | 4 | uint32 | `payload_a` | CONFIRMED | Payload dword A. |
| +0x0C | 4 | uint32 | `payload_b` | CONFIRMED | Payload dword B. |

> Sub-cell total = 16; 5 sub-cells = 80 per record; 8 records = 640.
>
> **Selection logic (no value invention).** For each of the 8 slots the walker resolves the slot's id
> to a record index (≤ 8), then, depending on whether the slot id matches the local player's id, binds
> either `name_cells_self` (+0x0FC) or `name_cells_other` (+0x054) plus the 8-byte pair, and copies
> the live 16-byte sub-cells of that slot's 80-byte record into the panel.

**VALUE meanings (capture/debugger-pending):** the per-slot id/status value meanings (status == 2 is a
state test of unknown semantics); the sub-cell key/payload dword meanings; whether the two 32-byte
reserved gaps (+0x34, +0xDC) carry server data unread by this particular consumer (no read site here —
treated as reserved until a capture shows otherwise).

---

## 5. 5/68 — Quest-list snapshot (body = 452 bytes / 0x1C4, CONFIRMED) — COUNT 10, columnar

> **Corrected this pass (binary wins, W5.1).** The earlier reading of "20 entries each" is
> superseded: the record **count is 10**, and the body is a **columnar (struct-of-arrays)** layout,
> not an array-of-structs. There are TWO distinct strides that must not be conflated — the **wire
> CP949-name stride is 17 bytes**, while the client's **runtime quest-log mirror slot is 32 bytes**
> (value @ slot+8, name @ slot+12). The 32-byte stride is the RUNTIME mirror, NOT on the wire. The
> per-record embedded-struct view (wire `QuestListWireRecord` vs runtime `QuestLogMirrorSlot`) is
> tabled in §9.5. Wire-level view: `packets/5-68_*.yaml`.

Three-byte header region around +0x08, then **three parallel column arrays** (byte-A column from
+0x0B, byte-B column from +0x15, value-word column from +0x20) and a **name column from +0x70 at
17-byte stride**, all `count = 10`. The columns fill the 452-byte body.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x08 | 1 | uint8 | `panel_open_flag` | CONFIRMED | Snapshotted before/after the copy; a transition opens/closes the quest panel. |
| +0x09 | 1 | uint8 | `mirror_flag_a` | CONFIRMED | Header byte (copied to a mirror flag). |
| +0x0A | 1 | uint8 | `mirror_flag_b` | CONFIRMED | Header byte (copied to a mirror flag). |
| +0x0B | 10 (×10, stride 1) | uint8[10] | `per_quest_byte_a` | CONFIRMED | One byte per record (quest id / icon class). Column, count 10. |
| +0x15 | 10 (×10, stride 1) | uint8[10] | `per_quest_byte_b` | CONFIRMED | One byte per record (progress / state). Column, count 10. |
| +0x20 | 40 (×10, stride 4) | uint32[10] | `per_quest_value_word` | CONFIRMED | One dword per record. Column, count 10 (NOT 20). |
| +0x70 | 170 (×10, **stride 17**) | char[17][10] | `per_quest_name` | CONFIRMED | 10 CP949 name cells packed **17 bytes apart** (NUL-padded). WIRE name stride = 17. |

> **Strides, pinned to their sides.** WIRE name stride = **17** (10 names from +0x70). RUNTIME mirror
> slot stride = **32** (the client's stored quest-log slot; value @ slot+8, CP949 name @ slot+12) —
> a runtime structure, NOT on the wire. See §9.5.

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

## 7. 4/48 — Rank-progress event (body = 236 bytes / 0xEC, CONFIRMED) — fit RESOLVED (no overrun)

> **Re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20).** Body shape
> **CONTROL-FLOW CONFIRMED**, fixed 236 bytes, **no variable tail, no overrun** — re-confirmed
> **identical** to the prior reading: 12-byte header (8-byte leading region + route byte @ +8 +
> sub-select byte @ +9 + 2-byte filler) then 8 × 28-byte records based at body +0x0C. The handler reads
> exactly 0xEC and the record walker loops exactly 8 times over a 28-byte stride. VALUE meanings remain
> capture/debugger-pending.

> **Corrected (binary wins, W3 ledger).** The earlier "12-byte overrun" is **REFUTED** —
> there is no overrun. The handler reads exactly 236 (0xEC). The consumer forwards `block + 12` to
> the record walker, so the record base is **body +0x0C** (not +0x18), with stride 28 and count 8:
> `12 + 8·28 = 236` exactly. The prior reading double-counted the 12-byte header. Corrected layout:
> 12-byte header + 8 × 28-byte records.

Two routing bytes at +8/+9, then a record array handed to the panel at `body + 12` (the record base).
Each record is id + 18-byte CP949 name + two int16. Wire-level view: `packets/4-48_*.yaml`.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x00 | 8 | bytes[8] | `header` | CONFIRMED (region) | Leading header region, precedes the route bytes. |
| +0x08 | 1 | uint8 | `route_byte` | CONFIRMED | Non-zero → detail/record path. |
| +0x09 | 1 | uint8 | `subselect_byte` | CONFIRMED | 0/1 — picks one of two UI-list targets. |
| +0x0A | 2 | bytes[2] | (filler) | CONFIRMED | Filler to the record-array base. |
| +0x0C | 224 | record[8] (stride 28) | `record_array` | CONFIRMED (base/stride/count/fit) | Record base = body +0x0C; 8 records × 28 bytes. `8 + 1 + 1 + 2 + 224 = 236` — fills the body exactly, no overrun. |

### Per-record layout (record-relative; record base E = body+0x0C + i·28)

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

## 9. Embedded packet-body record structs (W5.1)

The structs in this section are the **inner record layouts** carried inside packet bodies — the
discriminated union, the per-record strides, and the wire-vs-runtime width splits that the §1–§8
panel tables referenced but did not expand. These are **layout-confirmed (static-ida)**; every field
**VALUE meaning is `[capture/debugger-pending]`**. Proposed canonical names are used in prose only
(they are flagged for `names.yaml`, not applied here).

All offsets are **body-relative** (wire byte 0 = first byte after the 8-byte
`[u32 size][u16 major][u16 minor]` header) unless a row is explicitly marked a **runtime mirror**
offset.

### 9.1. 2/80 — `CmsgGuildRequestBody` — 48-byte DISCRIMINATED UNION (C2S)

The 48-byte (0x30) body is **an overlay, not a flat record**: a common front, then a per-selector
reinterpretation of the same buffer keyed by the +0x00 selector byte. Callers `memset(rec, 0, 0x30)`
then write the selector and only the fields the active selector uses. (This **corrects** the earlier
"single 48-byte opaque fixed record" reading — it is a tagged union.)

**Common front (all selectors):**

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x00 | 1 | uint8 | `Selector` | CONFIRMED | Op discriminator. Observed values: 0 = map-pin / area-select · 1 = guild create-cost commit · 2 = guild cost-quote · 3,4,5,7,8 = guild list/info sub-ops (sub-ops 6/7/0xC/0xD/0xE use the +0x1C sub-selector). Enum semantics `[capture/debugger-pending]`. |
| +0x01 | 3 | bytes[3] | (pad) | CONFIRMED | Zero-filled. |
| +0x04 | 4 | uint32 | `ContextId` | CONFIRMED | Window / guild context handle; written on nearly every selector. `[capture/debugger-pending]`. |

**Selector-specific overlay (bytes +0x08 .. +0x2F — per-selector reinterpretations, not all live at once):**

| Offset | Size | Type | Field (per active selector) | Confidence | Notes |
|--------|------|------|-----------------------------|------------|-------|
| +0x08 | 4 | uint32 | `PrimaryValue` / `PtrOrId` | CONFIRMED | Selector 0: map-pin id. Selector 2: a computed cost. Selectors 5/0xA: an internal list/completion handle. `[capture/debugger-pending]`. |
| +0x0C | 1 | uint8 | `SubOpA` | CONFIRMED | Selector 9 sets this to a donate/contribute sub-op marker. `[capture/debugger-pending]`. |
| +0x0E | 17 | char[17] | `NameCellA` (CP949) | CONFIRMED | Selector 9: local player's name, NUL-padded width-17 CP949 cell. |
| +0x1C | 1 | uint8 | `SubSelector` | CONFIRMED | Name-op discriminator for the name-bearing selectors (add target / variant / other name ops). `[capture/debugger-pending]`. |
| +0x1D | 17 | char[17] | `NameCellB` (CP949) | CONFIRMED | Target / guild name for name-op selectors, width-17 CP949 cell. |
| +0x20 | 8 | int64 | `AmountValue` | CONFIRMED | Selector 9: a donation amount; occupies the qword slot at +0x20. `[capture/debugger-pending]`. |
| +0x28 | 4 | uint32 | `TailValueA` | CONFIRMED | Selector 9: a secondary amount/handle. `[capture/debugger-pending]`. |
| +0x2C | 4 | uint32 | `TailValueB` | CONFIRMED | Selectors 1 and 3: a window/list handle (high half of the qword at +0x28). `[capture/debugger-pending]`. |

> **Union span.** Front +0x00..+0x07 common; the name-op overlay reaches +0x1D + 17 = +0x2E; the
> value-op overlay reaches +0x2C + 4 = +0x30 (48). Both fit exactly within the 48-byte body.

### 9.2. 2/75 — `CmsgRosterAssembleBody` — 80-byte resident panel sub-struct (C2S)

The 80-byte (0x50) body is a **resident HUD-panel sub-struct copied verbatim** at the send path (a
20-dword block copy out of a panel object's live buffer), **not** field-assembled at send time. Only
one field is send-path-confirmed; the interior is **opaque-pending**.

| Offset | Size | Type | Field | Confidence | Notes |
|--------|------|------|-------|------------|-------|
| +0x00 | 4 | uint32 | `record_head` | UNVERIFIED · `[capture/debugger-pending]` | First dword of the resident record (snapshot start). |
| +0x04 | 1 | uint8 | `arm_flag` | CONFIRMED (write of 1) · `[capture/debugger-pending]` | One send path stamps this to **1** immediately before the verbatim copy — a "record armed/submitted" flag. The only field-confirmed interior field. |
| +0x05 | 75 | bytes[75] | `resident_remainder` | UNVERIFIED · `[capture/debugger-pending]` | The rest of the 80-byte panel sub-struct; mixes ids and CP949 width-17 name cells, but the exact split is **not recoverable from the send path** — built incrementally by the panel's list-fill / name-entry paths. |

> **Interior opaque-pending.** 4 + 1 + 75 = 80 (reconciles). Recovering the split needs a panel-fill
> trace or a live read of the panel object's resident-record window while the roster panel is
> populated. Only the +0x04 arm-flag and the verbatim-80 size are firm.

### 9.3. 5/52 — `SkillActionTargetRecord` — 36-byte stride (S2C, inside SmsgActorSkillAction)

The 5/52 body is `24-byte header + 36 × N target records` (TargetCount at header +0x14, ≤ 40). The
per-target record (record-relative offsets, **36-byte stride**):

| Rec offset | Size | Type | Field | Confidence | Notes |
|------------|------|------|-------|------------|-------|
| +0x00 | 1 | uint8 | `TargetSort` | CONFIRMED | Actor sort sub-key (composite-key lookup arg). |
| +0x01 | 3 | bytes[3] | (pad) | CONFIRMED | |
| +0x04 | 4 | uint32 | `TargetId` | CONFIRMED · `[capture/debugger-pending]` | Composite-key entity id (lookup arg). |
| +0x08 | 12 | bytes[12] | (opaque) | UNVERIFIED · `[capture/debugger-pending]` | Bounded opaque body; not field-read by the damage loop. |
| +0x14 | 4 | uint32 | `DamageSum_low` | CONFIRMED | Low dword of the damage accumulator. |
| +0x18 | 4 | uint32 | `DamageSum_high` | CONFIRMED | High dword. **+0x14/+0x18 form ONE signed 64-bit accumulator** (explicit low/high add-with-carry across the 36-byte record walk). |
| +0x1C | 8 | bytes[8] | (tail) | UNVERIFIED · `[capture/debugger-pending]` | Bounded opaque tail within the 36-byte stride. |

> **Damage offset note.** The 64-bit quantity is at rec **+0x14 (low) / +0x18 (high)**, NOT the older
> +0x10/+0x14 reading (rec +0x10 is never read by this handler). Whether the 64-bit value is literal
> damage vs another signed magnitude is `[capture/debugger-pending]`.

### 9.4. 5/31 — `BuffEntry` — wire 12-byte triple, runtime stride 12 (S2C, inside SmsgBuffSlotUpdate)

The 5/31 body is a fixed 56 bytes (0x38) carrying a header (entity keys + slot index) and one buff
descriptor. The `{code, value, extra}` triple has **different widths on the wire vs in the runtime
store** — state both:

**Wire triple (inside the 56-byte body):**

| Body offset | Size | Type | Field | Confidence | Notes |
|-------------|------|------|-------|------------|-------|
| +0x0C | 2 | uint16 | `effect_code` | CONFIRMED | The **code**; widened to u32 on store. (Code 44 on a non-local actor = remove/despawn that actor.) |
| +0x0E | 2 | bytes[2] | (pad) | CONFIRMED | Gap between the u16 code and the u32 value. |
| +0x10 | 4 | uint32 | `effect_value` | CONFIRMED · `[capture/debugger-pending]` | The **value**; 0 ⇒ clear, non-zero ⇒ set/active. |
| +0x14 | 4 | uint32 | `effect_extra` | CONFIRMED · `[capture/debugger-pending]` | The **extra** (duration / remaining). |

**Runtime stored entry (the record written into the buff arrays, stride 12 — NOT the wire layout):**
`{ uint32 code (widened from the wire u16), uint32 value, uint32 extra }` — three contiguous dwords,
**stride 12**, written at `12 × slot_index`.

> **Widths, pinned to their sides.** Wire = `{u16 code @+0x0C, pad, u32 value @+0x10, u32 extra
> @+0x14}` (the wire triple spans +0x0C..+0x17). Runtime entry = `{u32 code, u32 value, u32 extra}`
> stride 12 (the code is widened to a full dword in the store).

### 9.5. 5/68 — `QuestListWireRecord` vs `QuestLogMirrorSlot` (S2C, inside SmsgQuestList)

5/68 carries **10 quest records** (count = **10**, corrected from the earlier "20"). The two sides
are DIFFERENT structures and must be kept separate:

**(a) WIRE side — columnar (struct-of-arrays).** There is no contiguous per-record struct on the
wire; the data is **three parallel column arrays + a name column**, all count 10 (full column
offsets in §5):

| Column | Body offset | Stride | Count | Type | Belongs to |
|--------|-------------|--------|-------|------|-----------|
| byte A | +0x0B | 1 | 10 | uint8 | wire |
| byte B | +0x15 | 1 | 10 | uint8 | wire |
| value word | +0x20 | 4 | 10 | uint32 | wire |
| **name** | +0x70 | **17** (CP949) | 10 | char[17] | wire |

→ **WIRE CP949-name stride = 17 bytes.**

**(b) RUNTIME mirror — `QuestLogMirrorSlot`, 32-byte slot (NOT on the wire).** The client stores each
quest as a 32-byte slot; count 10:

| Slot offset | Size | Type | Field | Belongs to |
|-------------|------|------|-------|-----------|
| slot +0x08 | 4 | uint32 | value word | runtime mirror |
| slot +0x0C | 17 | char[17] | quest name (CP949) | runtime mirror |
| slot +0x00..+0x1F | 32 | — | **runtime mirror slot stride = 32** | runtime mirror |

> **Do not conflate the strides.** WIRE name stride = **17**; RUNTIME mirror slot stride = **32**.
> Count is **10**, not 20. The 32-byte slot is a runtime structure, never on the wire.

### 9.6. 5/57 — `TrackedPanelSlotRecord` — 24-byte body (S2C, inside SmsgTrackedPanelSlotUpdate)

The 5/57 body is a fixed 24 bytes (0x18), a single tracked-panel slot descriptor. The applier decodes
it field-by-field starting at +0x08 (the leading 8 bytes are a header this applier does not field-read):

| Body offset | Size | Type | Field | Confidence | Notes |
|-------------|------|------|-------|------------|-------|
| +0x00 | 8 | bytes[8] | (header) | UNVERIFIED | Not field-read by the applier (likely entity keys consumed upstream). |
| +0x08 | 1 | uint8 | `Op` | CONFIRMED · `[capture/debugger-pending]` | ==1 ⇒ set/update slot · ==0 ⇒ clear/remove slot. |
| +0x09 | 1 | uint8 | `SlotIndex` | CONFIRMED | The panel slot index (the slot multiplier). |
| +0x0A | 1 | uint8 | `ByteA` | CONFIRMED · `[capture/debugger-pending]` | |
| +0x0B | 1 | uint8 | `ByteB` | CONFIRMED · `[capture/debugger-pending]` | |
| +0x0C | 2 | int16 | `Value16` | CONFIRMED · `[capture/debugger-pending]` | Read signed 16-bit, stored widened to a dword. |
| +0x0E | 2 | bytes[2] | (pad) | UNVERIFIED | Gap between the i16 and the next dword field. |
| +0x10 | 4 | int32 | `Value32A` | CONFIRMED · `[capture/debugger-pending]` | Stored into a parallel runtime table. |
| +0x14 | 4 | uint32 | `Value32B` | CONFIRMED · `[capture/debugger-pending]` | |

> **Destination table stride is RUNTIME.** The tracked-panel destination table uses a **48-byte slot
> stride** (`48 × SlotIndex`, with a parallel region at `48 × (SlotIndex + 21)`). That 48-byte stride
> is the runtime panel table, NOT the wire — the wire record is the 24-byte descriptor above. Last
> consumed field +0x14 ends at +0x18 (24) — reconciles. The set branch (Op==1) reads
> +0x09/+0x0A/+0x0B/+0x0C/+0x10/+0x14; the clear branch (Op==0) reads only +0x09 (SlotIndex).

### 9.7. PvP-state singleton interior (S2C — 5/89 / 5/90 / 5/91 / 5/92 / 5/94)

The five PvP messages share **one resident singleton object** that aggregates them at non-overlapping
offset bands. **All five setters field-DECODE their wire blocks** (a correction to an earlier
"interior opaque-forward / wholesale copy" reading — they are NOT opaque blits). The tables below are
the **WIRE body layouts**; the singleton scatter offsets are noted as **runtime mirrors** (mark them
runtime, not wire). The `PvpStateSingleton` regions do not overlap, confirming one shared object.

**5/90 `PvpCountersBlock` — 32-byte body:**

| Body offset | Size | Type | Field | Mirror (runtime) | Confidence |
|-------------|------|------|-------|------------------|------------|
| +0x00 | 1 | uint8 | sort/kind | → singleton +176 | CONFIRMED · `[capture/debugger-pending]` |
| +0x04 | 4 | uint32 | id | → +180 | CONFIRMED · `[capture/debugger-pending]` |
| +0x08 | 4 | uint32 | counter A | → +184 | CONFIRMED · `[capture/debugger-pending]` |
| +0x0C | 4 | uint32 | counter B | → +188 | CONFIRMED · `[capture/debugger-pending]` |
| +0x10 | 4 | uint32 | counter C | → +192 | CONFIRMED · `[capture/debugger-pending]` |
| +0x14 | 4 | uint32 | counter D | → +196 | CONFIRMED · `[capture/debugger-pending]` |
| +0x18 | 4 | uint32 | counter E | → +200 | CONFIRMED · `[capture/debugger-pending]` |
| +0x1C | 4 | uint32 | value F | → +204 | CONFIRMED · `[capture/debugger-pending]` |

Total 0x20 (32), all 8 fields consumed.

**5/91 `PvpScoreBlock` — 124-byte body (header + 6-entry CP949 name array stride 17):**

| Body offset | Size | Type | Field | Mirror (runtime) | Confidence |
|-------------|------|------|-------|------------------|------------|
| +0x00 | 1 | uint8 | sort/kind | → +208 | CONFIRMED · `[capture/debugger-pending]` |
| +0x04 | 4 | uint32 | id | → +212 | CONFIRMED · `[capture/debugger-pending]` |
| +0x08 | 1 | uint8 | byte A | → +216 | CONFIRMED · `[capture/debugger-pending]` |
| +0x09 | 1 | uint8 | byte B | → +217 | CONFIRMED · `[capture/debugger-pending]` |
| +0x0C | 4 | uint32 | value A | → +220 | CONFIRMED · `[capture/debugger-pending]` |
| +0x10 | 4 | uint32 | value B | → +224 | CONFIRMED · `[capture/debugger-pending]` |
| +0x14 | 17 (×6, **stride 17**) | char[17][6] | NameCell[0..5] (CP949) | → +228, 6 cells stride 17 | CONFIRMED |

Total 0x14 + 6×17 = +0x76 (118) + pad = 124 (0x7C).

**5/89 `PvpRevengeRosterBlock` — 188-byte body (header + 10-name roster stride 17 + property byte arrays):**

| Body offset | Size | Type | Field | Mirror (runtime) | Confidence |
|-------------|------|------|-------|------------------|------------|
| +0x00 | 1 | uint8 | revenge_actor_sort | → +64 | CONFIRMED |
| +0x01 | 3 | bytes[3] | (pad) | | CONFIRMED |
| +0x04 | 4 | uint32 | revenge_actor_id | → +68 | CONFIRMED · `[capture/debugger-pending]` |
| +0x08 | 1 | uint8 | revenge_team | → +73 | CONFIRMED · `[capture/debugger-pending]` |
| +0x09 | 1 | uint8 | revenge_team_guild | → +72 | CONFIRMED · `[capture/debugger-pending]` |
| +0x0A | 17 (×10, **stride 17**) | char[17][10] | RosterName[0..9] (CP949) | → +332, 10 cells stride 17 | CONFIRMED |
| +0xB4 | 1 (×2, stride 1) | uint8[2] | DEFAULT_PROPERTY_MAX[0..1] | → +502 | CONFIRMED · `[capture/debugger-pending]` |
| +0xB6 | 1 (×4, stride 1) | uint8[4] | RANDOM_PROPERTY_MAX[0..3] | → +504 | CONFIRMED · `[capture/debugger-pending]` |

Total 0x0A + 10×17 = +0xB4 (180); + 2 + 4 = +0xBA (186) + pad = 188 (0xBC).

**5/92 `PvpRequestNoticeBlock` — 40-byte body:**

| Body offset | Size | Type | Field | Mirror (runtime) | Confidence |
|-------------|------|------|-------|------------------|------------|
| +0x00 | 1 | uint8 | revenge_actor_sort | → +88 | CONFIRMED |
| +0x01 | 3 | bytes[3] | (pad) | | CONFIRMED |
| +0x04 | 4 | uint32 | revenge_actor_id | → +92 | CONFIRMED · `[capture/debugger-pending]` |
| +0x08 | 4 | uint32 | value A | → +96 (also a HUD panel field) | CONFIRMED · `[capture/debugger-pending]` |
| +0x0C | 4 | uint32 | value B | → +100 | CONFIRMED · `[capture/debugger-pending]` |
| +0x10 | 4 | uint32 | target_id | → +104 (am-I-the-target test) | CONFIRMED · `[capture/debugger-pending]` |
| +0x14 | 17 | char[17] | NameCell (CP949) | → +108 | CONFIRMED |

Total 0x14 + 17 = +0x25 (37) + pad = 40 (0x28).

**5/94 `PvpVoteResultBlock` — 16-byte body:**

| Body offset | Size | Type | Field | Mirror (runtime) | Confidence |
|-------------|------|------|-------|------------------|------------|
| +0x00 | 1 | uint8 | actor_sort | → +144 | CONFIRMED |
| +0x01 | 3 | bytes[3] | (pad) | | CONFIRMED |
| +0x04 | 4 | uint32 | actor_id | → +148 | CONFIRMED · `[capture/debugger-pending]` |
| +0x08 | 1 | uint8 | vote_side flag | → +152 (==1 selects "this side") | CONFIRMED · `[capture/debugger-pending]` |
| +0x09 | 1 | uint8 | byte B | → +153 | CONFIRMED · `[capture/debugger-pending]` |
| +0x0A | 1 | uint8 | byte C | → +154 | CONFIRMED · `[capture/debugger-pending]` |
| +0x0B | 1 | bytes[1] | (pad) | | CONFIRMED |
| +0x0C | 4 | uint32 | vote_result code | → +156 (drives a win/lose/in-progress notice) | CONFIRMED · `[capture/debugger-pending]` |

Total 0x0C + 4 = 16, all fields consumed.

> **Singleton scatter offsets are RUNTIME mirrors.** The `→ +N` notes above are positions inside the
> shared `PvpStateSingleton`, not wire offsets — they are recorded for audit but **must not be
> promoted as wire fields.** The bands are: revenge-roster @+64..+73 + 10 names @+332 + property
> arrays @+502/+504 (5/89); request/notice @+88..+108 (5/92); counters @+176..+204 (5/90); score
> @+208..+245 (5/91); vote @+144..+156 (5/94) — non-overlapping, confirming one shared object. All
> five WIRE layouts are static-confirmed; the field VALUE meanings remain `[capture/debugger-pending]`.

---

## Open questions (UNVERIFIED / PENDING)

1. **4/48 record fit — RESOLVED (no overrun).** The record base is body +0x0C (not +0x18); `12 +
   8·28 = 236` fills the 236-byte body exactly. The earlier "12-byte overrun" double-counted the
   12-byte header. Base/stride/count/fit all CONFIRMED — see §7.
2. **4/56 secondary-array byte base — RESOLVED (CYCLE 7).** The 64-entry, 8-byte-stride
   `transform_array_b` base at +0x410 is now control-flow confirmed (single 64-trip loop, cursor += 48
   per trip; whole 0x610 block independently re-copied). The prior dword-vs-byte-pointer caveat is
   retired. See §3.
3. **4/71 sub-records past +52 — RESOLVED (CYCLE 7).** The prior opaque blob is broken into real
   fixed-stride sub-tables (id array, status array, two CP949 name-cell regions, 8-byte pair array,
   8 × 80-byte slot records of 5 × 16-byte sub-cells); the verbatim-copy base lets every wire offset be
   pinned. See §4. (Two 32-byte reserved gaps at +0x34/+0xDC are unread by this consumer — reserved
   until a capture shows otherwise.)
4. **5/77 trailing 14 bytes (PENDING).** Bytes +386 .. +399 are untouched by the consumer — trailing
   pad or additional fields.
5. **5/73 name — RESOLVED to `SmsgQuestComplete`** (binary-confirmed in W4; the committed
   `SmsgGuildWarInfoUpdate` reading is refuted). Its body is verbatim-copied into a mirror struct, so
   its inner array's wire offset is not wire-loop-pinned — it is **not tabled as a struct DTO here**;
   see `packets/5-73_*.yaml` and `opcodes.md`.
6. **VALUE semantics PENDING throughout.** Every per-byte meaning (which flag is active, what the
   numeric fields represent) needs the protocol-value pass and/or a live capture; these tables claim
   boundaries, strides, counts, and record geometry only.
