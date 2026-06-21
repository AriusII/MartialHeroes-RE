# Item structures — wire and runtime layouts (clean-room spec)

> **Verification banner.** verification: **confirmed** for the handler control-flow facts re-pinned
> this pass — the five item-wire opcode→handler **routings** and **body sizes** (4/149 = header + a
> 16-byte record run, 4/12 = 16B, 4/22 = 36B, 4/50 = 32B, 4/19 = 56B), the per-handler **ownership
> gates** and **primary result/chunk/slot byte offsets**, the EquipTable = 20 × 16B array, the bag
> soft cap `40 × (bag_count + 3)`, the equip actor-mirror dual-write, and the 4/12 `to_slot` byte
> position (corrected to a single byte at +0x0C — see §7b); **static-hypothesis** for the item-actor
> stat-grant / set-bonus / combat-grant offsets (§6) and the per-field detail inside the apply
> functions (these live in apply paths that were **not** re-located by control flow this pass — they
> are carried from prior analysis, not re-confirmed); **capture/debugger-pending** for every wire
> **field value semantic** (what a copied byte *means*) and any binding/tradeable bit inside the
> 16-byte slot record. ida_reverified: 2026-06-16 · ida_anchor: 263bd994 · evidence: [static-ida] ·
> conflicts: one low-severity offset refinement — 4/12 `to_slot` is a single byte at **+0x0C**
> (not the hedged +0x0B–0x0C); the "value 15 ⇒ visual rebuild" semantic is unchanged.
>
> **CYCLE 7 re-verification (2026-06-20).** re-verified against doida.exe IDB SHA 263bd994, CYCLE 7
> (2026-06-20). The 4/50 upgrade-result handler was re-read by control flow and its RESULT/REASON
> branch table promoted from `LIKELY` to `CONFIRMED` (§8). The per-slot enchant-progress accumulator
> location was **corrected**: it is a global per-slot array, not slot-record +0x0C (§1a, §8). The
> upgrade-level cap (28), upgrade-process step count (6), and the `upgradeitems.scr` record stride
> (44 B) were pinned (§8, §11). A new item-actor field — +0x7C `event_id` (the events.scr join column)
> — and the special-goods `item_subtype` value 1004 were added (§6e). Item-actor template offsets in
> §6 remain static-hypothesis; wire-field value semantics remain capture/debugger-pending.

Neutral, offset-model of the item-related structures the legacy client used. Promoted from
dirty-room notes; rewritten, no decompiler identifiers, no binary addresses. Design input for the
**network-protocol-engineer** (decoding the item / equip / shop / upgrade packets) and the
**domain engineer** (applying item stat grants, equipment state, durability, and the inventory grid).

> **Confidence tags:** `CONFIRMED` = cross-checked at multiple sites or control-flow-confirmed (a
> dispatch slot, a handler's fixed read size, or a handler read offset); `LIKELY` = single consistent
> site; `UNVERIFIED` = inferred / boundary not pinned. The wire-packet **routings, body sizes, and the
> primary handler read offsets** are control-flow confirmed this pass; the wire-field **value
> semantics** were not checked against a live capture and stay capture/debugger-pending pending a
> `sample_verified` pass.
>
> **Text encoding.** All Korean text fields the client renders (item names, tooltip strings,
> localized error reasons) are stored as **CP949** (extended EUC-KR) byte sequences, never UTF-16
> on the wire. Treat every name/text blob as a CP949 byte buffer.

---

## 1. ItemSlotRecord — 16-byte wire/runtime unit (shared by all containers)

The canonical slot unit. The same 16 bytes are transmitted by the item-panel slot-update path
(opcode 4/149), stored verbatim in every runtime item container (equipment, bag, drag/drop
staging), and back each equipment slot inside the SpawnDescriptor (see `spawn_descriptor.md`,
"Equipment slot array"). Equipment, bag, and staging containers all use this identical record.

| Offset | Size | Type   | Field             | Conf       | Meaning |
|--------|------|--------|-------------------|------------|---------|
| +0x00  | 1    | uint8  | `flags_a`         | UNVERIFIED | First byte of the leading dword; copied verbatim from the server slot. May encode an item-category or server-side flag. Not individually interpreted by any analyzed handler. |
| +0x01  | 1    | uint8  | `flags_b`         | UNVERIFIED | Second byte of the leading dword; copied verbatim. Semantics unknown. |
| +0x02  | 1    | uint8  | `client_flag_src` | LIKELY     | **Client-local, not wire data.** Zeroed on every normal slot write; set non-zero only during drag/drop staging to mark the move source context. The server never sends this byte. |
| +0x03  | 1    | uint8  | `client_flag_dst` | LIKELY     | **Client-local, not wire data.** Zeroed on every normal slot write; set during drag/drop staging to mark the destination context. |
| +0x04  | 4    | uint32 | `item_actor_id`   | CONFIRMED  | Id of the item's runtime actor instance (which carries the stat-grant/template fields in §6). **`0` = empty slot.** Every consumer resolves the item by this id. |
| +0x08  | 4    | uint32 | `qty_or_expiry_lo`| CONFIRMED  | Overloaded by item type — see §1a. Stackable: current stack count. Timed: low 32 bits of a UNIX `time_t` expiry. |
| +0x0C  | 4    | uint32 | `enchant_or_expiry_hi`| CONFIRMED | Overloaded by item type — see §1a. Timed: high 32 bits of the same `time_t`. **Not** the upgrade accumulator (corrected CYCLE 7): the per-slot enchant-progress accumulator lives in a separate global per-slot array, see §1a and §8; the 4/50 upgrade-result handler does not write this slot-record field. |

The +0x04 actor-id read and the +0x08/+0x0C dual-use are confirmed across many consumers. The two
leading bytes (`flags_a`/`flags_b`) are written verbatim but never individually dispatched, so their
meaning stays UNVERIFIED. Note: an **earlier promotion of this record** labelled +0x04 as
`item_actor_id` and +0x08/+0x0C as a single `expiry_lo`/`expiry_hi` pair; that is the *timed-item*
case of the overload now documented fully in §1a. The earlier single-dword label for the leading
four bytes at +0x00 is superseded by the `flags_a`/`flags_b`/client-flag breakdown above.

### 1a. Dual-use of +0x08 / +0x0C by item type — CONFIRMED

The +0x08/+0x0C pair is interpreted differently depending on the item's actor template
(stackability and upgradeability are template properties, see §6):

| Item type                | +0x08 meaning                          | +0x0C meaning |
|--------------------------|----------------------------------------|---------------|
| Stackable                | `quantity` (uint32, range 1..1000)     | (overload free; not the upgrade accumulator) |
| Timed / expiring         | `expiry_lo` — low 32 bits of a 64-bit UNIX `time_t` | `expiry_hi` — high 32 bits of the same `time_t` |
| Non-stack permanent gear | (unused / 0)                           | (unused / 0; not the upgrade accumulator) |

- **Stack cap = 1000** (CONFIRMED constant). Two same-template stackable items merge only if the
  combined count stays ≤ 1000.
- A 64-bit `time_t` (lo at +0x08, hi at +0x0C) is consumed by a local-time conversion when the slot
  is applied; `0` = no expiry (permanent item).

#### Enchant-progress accumulator — a global per-slot array, NOT slot-record +0x0C — CONFIRMED (CYCLE 7)

An earlier promotion placed the upgrade/enchant-progress accumulator inside this 16-byte slot record
(at +0x0C). **Corrected this pass:** the accumulator the item-upgrade pipeline advances and clears is a
**global, flat, per-slot array of 32-bit integers** (one `int32` per upgrade slot, 4-byte stride),
**outside** the `ItemSlotRecord`. The 4/50 upgrade-result handler (§8) is the writer:

- on **partial progress** it adds the server-supplied delta into `accumulator[slot_index]`;
- on **success** it resets `accumulator[slot_index]` to 0.

A **sibling global per-slot array** runs in parallel — one entry per upgrade slot, holding the
**active/displayed item actor id** for that slot (resolved through the actor manager when the upgrade
panel reads it). Role of the accumulator array is `CONFIRMED`; role of the sibling id array is `LIKELY`
(active-item id per upgrade slot).

The slot-record field at +0x0C therefore stays a separate, overloaded `enchant_or_expiry_hi` field
(timed-item expiry high dword); the 4/50 handler never writes it.

### 1b. Per-slot chunk framing (4/149)

In the 4/149 packet each per-slot region is a contiguous run of 16-byte records preceded by header
fields that select the destination container and starting slot. The framing is **not** an interleaved
`{slot_index, pad, record}` per chunk; rather a header gives `chunk_type`, `start_index`, and `count`,
then `count` records follow back-to-back. See §7 for the full 4/149 header layout. (This corrects an
earlier note that described a per-chunk `{slot_index:u8, pad:u8, record:16B}` stride.)

---

## 2. Inventory container model — the four runtime arrays

The client holds the item world in four flat, fixed-stride arrays. All four use the 16-byte
`ItemSlotRecord` (§1) **except** the quick-use bar, which has its own 8-byte slot. The equipment
and bag arrays use **non-overlapping index spaces** and are entirely separate tables.

| Container        | Stride | Capacity | Conf       | Role |
|------------------|--------|----------|------------|------|
| **EquipTable**   | 16 B   | 20 slots (index 0..19) | CONFIRMED | The player's currently equipped items. Embedded inside the local-player SpawnDescriptor and mirrored onto the local player actor (see §3). |
| **BagTable**     | 16 B   | up to 240 slots (index 0..239) | CONFIRMED | The carry/bag inventory. Active size is a soft cap derived from bag count; absolute cap 240 (see §4). |
| **StagingTable** | 16 B   | small workspace | LIKELY | Client-only scratch copy of slot records built during drag/drop move validation. Never sent to the server. |
| **QuickUseSlots**| 8 B    | UNVERIFIED count (likely 10–12) | LIKELY | Hotbar / quick-use bindings (see §5). |

---

## 3. EquipTable — 20-slot equipment array — CONFIRMED

The equipment array is **embedded inside the local-player SpawnDescriptor** at SD-relative offset
+0x54 (see `spawn_descriptor.md`, "Equipment slot array"), 20 records × 16 bytes = 320 bytes, and
is **mirrored** onto the local-player actor at a second location so two views stay in sync. Equip/
unequip apply paths write **both** copies simultaneously; bulk re-sync paths copy the actor mirror
back into the descriptor copy.

### 3a. Slot index semantics

| Index | Role | Conf | Meaning |
|-------|------|------|---------|
| 0..13 | General gear slots | LIKELY | Specific body-part assignment per index is **UNVERIFIED** (head / chest / legs / hands / feet / belt / rings / neck / etc. not yet mapped). Slot 0 is the first gear position. |
| 8     | Special slot | CONFIRMED | **Excluded from the worn-stat sum** (the stat formula in `stats.md` skips slot 8) and excluded from the "equip-onto-another-slot" rejection guard. Its category is UNVERIFIED (candidate: a non-stat visual / quest / charm placeholder). |
| 14    | Weapon slot | CONFIRMED | Equipping here drives the weapon-model / weapon-glow visual path (uses the item actor's weapon-effect grade, §6c). The currently-equipped weapon's actor id is tracked in a dedicated global read before the glow dispatch. |
| 15    | Appearance slot | CONFIRMED | Writing here triggers a **full gear visual rebuild** of the character. |
| 16..19| Additional gear slots | LIKELY | Within the 20-slot bound; exact body-part assignment UNVERIFIED. |

> **Engineer note.** Model EquipTable as a fixed 20-entry array of `ItemSlotRecord`. Treat slots
> 8, 14, 15 as the three with confirmed special behaviour; leave the rest as generic gear pending a
> slot-type table.

### 3b. Equipment-table update triggers (behaviour, for the protocol engineer)

- **4/149, equip chunk** (`chunk_type == 0`): writes `count` records into EquipTable starting at
  `start_index`, bound-checked against the 20-slot limit. This is the authoritative server push.
- **Equip / unequip / equip-swap apply** (local apply of a server result): writes both the
  descriptor copy and the actor mirror at `16 * slot`.
- **After NPC sell ack** and **after a periodic tick-table refresh**: re-sync all 20 slots by copying
  the actor mirror back into the descriptor copy.

---

## 4. BagTable — carry inventory (≤ 240 slots) — CONFIRMED

A separate flat array of 16-byte records for the carry/bag inventory.

| Property | Value | Conf | Meaning |
|----------|-------|------|---------|
| Stride | 16 bytes | CONFIRMED | `16 * slot_index` throughout. |
| Soft cap (active size) | `40 * (bag_count + 3)` | CONFIRMED | The number of usable slots, driven by how many extra bags the player owns. |
| Hard cap | 240 slots | CONFIRMED | Any slot index ≥ 240 (0xF0) yields an empty record; never addressable. |
| Page size | 40 slots | CONFIRMED | One bag container = one 40-slot page. |
| Base pages | 3 | CONFIRMED | With no extra bags the player has 3 × 40 = 120 slots. |

`bag_count` is a single byte carried inside the SpawnDescriptor (see `spawn_descriptor.md`). Mapping:

| `bag_count` | Active soft cap | Total pages |
|-------------|-----------------|-------------|
| 0 | 120 | 3 × 40 |
| 1 | 160 | 4 × 40 |
| 2 | 200 | 5 × 40 |
| 3 | 240 | 6 × 40 (= hard cap) |

Bag counts above 3 would exceed the hard cap and were not observed.

### 4a. Bag update triggers (behaviour)

- **4/149, inventory chunk** (`chunk_type == 1`): writes `count` records into BagTable starting at
  `start_index`, bound-checked against the active soft cap.
- **4/153 slot refresh**: rewrites a range of bag slots; a sentinel `start_index` of 0xFF means
  "clear all".
- **Item acquire / buy**: writes all four dwords of a slot and zeroes the two client-flag bytes
  (+0x02/+0x03).
- **Item consume**: decrements `qty_or_expiry_lo` (+0x08) by 1.
- **Stack merge**: adds a delta into `qty_or_expiry_lo` (+0x08), capped at 1000.
- **Item upgrade** (4/50): on success, overwrites all four dwords of the slot record and resets the
  slot's entry in the **global enchant-progress accumulator array** (§1a) to 0; on partial progress,
  adds the server delta into that global accumulator entry (the slot record is not replaced). See §8.

---

## 5. QuickUseSlots — hotbar bindings (8-byte slot) — LIKELY

| Offset | Size | Type   | Field        | Conf       | Meaning |
|--------|------|--------|--------------|------------|---------|
| +0x00  | 4    | uint32 | `occupancy`  | LIKELY     | Non-zero = the quick-use slot is bound/occupied. |
| +0x04  | 4    | uint32 | `secondary`  | UNVERIFIED | Purpose unconfirmed; candidate is an item-actor id or a cooldown/timer reference. |

The quick-use validator checks `occupancy != 0` for a slot. C2S 2/17 carries `(quick_slot, mode,
item_or_target)`; the matching 4/17 ack updates this array (the ack write path is not yet traced).
The **total slot count is UNVERIFIED** (likely 10–12 from hotbar context).

---

## 6. Item-actor layout — template region (static, per-item-type)

When an item is instanced as an actor, its layout opens with a **template region of 548 bytes
(0x224)** loaded verbatim from the matching binary item record, followed by a **runtime region**
(pointers, UI linkages, the upgrade staging buffer). The full template struct is 552 bytes
(0x228); the trailing 4 bytes are likely a pointer to an upgrade-effect list. **All stat-grant,
set-bonus, combat-grant, and set-control fields below fall entirely within the 548-byte template
copy** (CONFIRMED). Offsets are **relative to the start of the item actor's layout**, not the
player's.

> **Tiering note for all of §6 (this pass).** The §6 item-actor stat-grant / set-bonus / combat-grant
> / set-control / weapon-grade offsets below were **NOT re-located by control flow this pass** — the
> worn-item stat-recompute loop and the per-item read sites were not cleanly isolated (a naive
> displacement scan returned false positives in unrelated code). They are **carried from prior
> analysis** (`stats.md`) and remain **static-hypothesis** here; the `CONFIRMED` tags on the §6 rows
> reflect that *prior* cross-checking, **not** a control-flow re-confirmation in this campaign. Do not
> upgrade them on the strength of this pass. The §6 *region size* facts (548/552 bytes) are
> consistent with the prior analysis but were likewise not re-pinned this pass.

### 6a. Direct stat-grant fields — CONFIRMED

The fields the player's stat formula (`stats.md`) sums over every worn item (skipping slot 8):

| Item-actor offset | Size | Type | Field            | Conf      | Meaning |
|-------------------|------|------|------------------|-----------|---------|
| +248 (0x0F8)      | 4 | int32 | `stat_grant_str` | CONFIRMED | STR bonus granted by this item. |
| +260 (0x104)      | 4 | int32 | `stat_grant_agi` | CONFIRMED | AGI bonus. |
| +272 (0x110)      | 4 | int32 | `stat_grant_dex` | CONFIRMED | DEX bonus. |
| +284 (0x11C)      | 4 | int32 | `stat_grant_int` | CONFIRMED | INT bonus. |
| +296 (0x128)      | 4 | int32 | `stat_grant_con` | CONFIRMED | CON bonus. |
| +312 (0x138)      | 4 | int32 | `stat_grant_hp`  | CONFIRMED | Flat HP bonus. |
| +328 (0x148)      | 4 | int32 | `stat_grant_mp`  | CONFIRMED | Flat MP bonus. |

### 6b. Combat-grant fields — CONFIRMED

Newly promoted attack/defense grants, read by the per-worn-item attack and defense accumulators
(same loop that sums the stat grants, skipping slot 8):

| Item-actor offset | Size | Type | Field           | Conf       | Meaning |
|-------------------|------|------|-----------------|------------|---------|
| +396 (0x18C)      | 4 | int32 | `atk_bonus_min` | CONFIRMED (label LIKELY) | Attack bonus, read first into the running attack total. |
| +420 (0x1A4)      | 4 | int32 | `atk_bonus_max` | CONFIRMED (label LIKELY) | Attack bonus, added into the same running attack total. |
| +432 (0x1B0)      | 4 | int32 | `def_bonus`     | CONFIRMED  | Defense bonus, summed by the defense accumulator. |

> **Min/max attack caveat.** Both attack fields are summed into a single running attack-power total,
> and the `min`/`max` labels are inferred from developer debug labels (which list a "min damage"
> term before a "max damage" term). Whether +396 is strictly minimum and +420 strictly maximum at
> the per-item level is **UNVERIFIED** and should be cross-checked against the binary item-record
> field ordering. The summing behaviour itself is CONFIRMED.

### 6c. Set-bonus fields — CONFIRMED access, partial labelling

The set-bonus accumulator (`stats.md`, "all-or-nothing rule") reads a per-pair output vector from
each item actor when matching and applying a set. Each pair is a **piece output / full-set output**
couple (the distributor produces both a per-piece value and a full-set value). All pairs below are
CONFIRMED as set-bonus output slots by access pattern; the **per-stat semantic assignment** of each
pair is CONFIRMED only for the attack/defense pair and LIKELY-by-position for the rest.

| Item-actor offset | Size | Stat (by position) | Conf | Role |
|-------------------|------|--------------------|------|------|
| +244 (0x0F4)      | 2 (u16) | — | CONFIRMED | `set_type_id`: set discriminator used to match items into a set (non-zero ⇒ set member). |
| +252 / +256 (0x0FC/0x100) | 4 / 4 | STR | LIKELY-position | Set-bonus piece/full pair (paired with `stat_grant_str`). |
| +264 / +268 (0x108/0x10C) | 4 / 4 | AGI | LIKELY-position | Set-bonus piece/full pair. |
| +276 / +280 (0x114/0x118) | 4 / 4 | DEX | LIKELY-position | Set-bonus piece/full pair. |
| +288 / +292 (0x120/0x124) | 4 / 4 | INT | LIKELY-position | Set-bonus piece/full pair. |
| +300 / +304 (0x12C/0x130) | 4 / 4 | CON | LIKELY-position | Set-bonus piece/full pair. |
| +316 / +320 (0x13C/0x140) | 4 / 4 | HP  | LIKELY-position | Set-bonus piece/full pair. |
| +332 / +336 (0x14C/0x150) | 4 / 4 | MP  | LIKELY-position | Set-bonus piece/full pair. |
| +342 / +346 (0x156/0x15A) | 2 / 2 (i16) | UNVERIFIED | CONFIRMED access | The 10th and final output pair, read as **16-bit** values (the only sub-32-bit pair). Candidate stat: a critical or stamina/chi value. Stat role UNVERIFIED. |
| +424 / +436 (0x1A8/0x1B4) | 4 / 4 | ATK / DEF | CONFIRMED | Attack and defense set-bonus pair (the attack/defense accumulators consume these directly). |
| +428 / +440 (0x1AC/0x1B8) | 4 / 4 | ATK-full / DEF-full | CONFIRMED | Full-set counterpart of the attack/defense pair. |

### 6d. Set-control fields — CONFIRMED

| Item-actor offset | Size | Type | Field               | Conf | Meaning |
|-------------------|------|------|---------------------|------|---------|
| +512 (0x200)      | 4 | uint32 | `set_flag`          | CONFIRMED | Non-zero ⇒ this item participates in a set; the set-bonus lookup is skipped when 0. |
| +516 (0x204)      | 4 | uint32 | `set_required_count`| CONFIRMED | Number of matching set pieces that must be worn for the full-set bonus to activate. |

### 6e. Miscellaneous item-actor fields — CONFIRMED

| Item-actor offset | Size | Type | Field                 | Conf | Meaning |
|-------------------|------|------|-----------------------|------|---------|
| +124 (0x07C)      | 4 | uint32 | `event_id`            | CONFIRMED | **events.scr join column** (added CYCLE 7). The item-actor / `items.scr` record carries a 32-bit `event_id` here that joins to the `events.scr` table by that table's primary key (events record +0x00, exact-match lookup). Resolved when the goods/cash-shop info path looks up the item's bound event. `0` = no bound event. See `formats/events_scr.md` for the events side. (The +0x7C field is within the 548-byte template copied verbatim from the on-disk `items.scr` record.) |
| +136 (0x088)      | 2 | uint16 | `item_subtype`        | CONFIRMED | Item sub-category. Value **62** = socketing stone (special handling in slot-clear); value **1001** = a special NPC-guard subtype (suppressed from sell display); value **1004** = a special-goods subtype gated by the cash-shop / goods info path (added CYCLE 7). The full subtype code table is data-driven (an equippable range plus discrete special codes). |
| +184 (0x0B8)      | 1 | uint8  | `stackable_flag` / `durability_enabled` | CONFIRMED | **Dual-role byte at the same offset.** Non-zero gates two systems: it marks the item as **stackable** (stacks merge if same template and combined count ≤ 1000) **and** marks the item type as participating in the **durability/wear system** (see §6f). Different code paths read this same byte for the two purposes. |
| +231 (0x0E7)      | 1 | uint8  | `weapon_effect_grade` | CONFIRMED | Weapon visual-glow tier and tooltip enchant display (see §6g). |

> **Note on the +184 dual role.** The stackability check and the durability-participation check read
> the **same byte**. Treat it as a single "active item-behaviour" flag whose non-zero state turns on
> both merge-on-pickup and wear-tracking. Whether stackable and durability-bearing item types are
> mutually exclusive in the data (so the byte never has to mean both at once for one item) is
> UNVERIFIED; the safe model is one byte read by two subsystems.

### 6f. Runtime durability — a parallel slot-indexed array, NOT on the item actor — CONFIRMED

The item actor does **not** store a `current_durability` counter. Wear is tracked in a separate flat
**32-bit per-equipment-slot array**, indexed by the equipment slot index (not by item id):

- A wear-tick operation subtracts a delta from the slot's counter and returns the new value.
- Worn to 0 ⇒ the slot-clear path fires (the item is destroyed/cleared from the equipment slot).
- Only slots whose item actor has `durability_enabled != 0` (the +184 byte) participate; when 0, the
  expiry check bypasses durability and the slot is cleared by the normal expiry path instead.

The maximum-durability **baseline** is a static template value carried in the binary item record
(not in the 16-byte slot record and not at a confirmed item-actor offset this pass). Durability is
therefore **server-authoritative on the baseline** and **client-tracked on the running counter**.

### 6g. Weapon-effect grade — CONFIRMED

The byte at item-actor +231 is the weapon's visual-glow tier and tooltip enchant display:

- **Effective display range 1..9** selects the weapon glow tier. **0 = no effect.**
- Encoded values **101..109 normalize to 1..9** (the client subtracts 100 before using them); values
  ≥ 100 are otherwise treated as a special/clear case.
- The grade is also rendered in the item tooltip via a localized format string, and is compared
  against a global threshold byte to pick the tooltip color (e.g. green at or above the threshold).
- The relationship between this runtime grade (range 0..9) and the static `enchant_level` column in
  the binary item record (range 0..28) is **UNVERIFIED** — the grade may be the enchant level
  directly, or a derived tier (e.g. a divided/bucketed value). Needs a data cross-reference.

---

## 7. EquipSlotBody, EquipItemResult, and the panel-slot packet (4/22, 4/12, 4/149)

### 7a. EquipSlotBody — 36-byte item-slot state record (opcode 4/22)

Read as a **fixed 36-byte (0x24) body** by the single-slot state-ack handler — the handler's read
size is **control-flow confirmed** at 0x24. Distinct from the 16-byte `ItemSlotRecord`; carries
equip/unequip result plus stat/enchant fields.

| Offset | Size | Type    | Field          | Conf       | Meaning |
|--------|------|---------|----------------|------------|---------|
| +0x00  | 8    | char[8] | `header`       | LIKELY     | Guard / identity prefix bytes. |
| +0x08  | 1    | uint8   | `result`       | CONFIRMED (offset) | Read by the handler at +0x08: `0` = dismiss/error notice; `1` = apply (feeds the slot-apply path). The +0x08 read is control-flow confirmed; the code set stays capture-pending. |
| +0x09  | 1    | uint8   | `pad_9`        | UNVERIFIED | Padding. |
| +0x0A  | 1    | uint8   | `from_slot`    | LIKELY     | Source slot index. |
| +0x0B  | 1    | uint8   | `to_slot`      | LIKELY     | Destination slot index. |
| +0x0C  | 4    | uint32  | `flag_c`       | UNVERIFIED | Flags. |
| +0x10  | 4    | uint32  | `flag_10`      | UNVERIFIED | Flags. |
| +0x14  | 4    | —       | (gap)          | UNVERIFIED | Unmapped 4 bytes. |
| +0x18  | 4    | int32   | `bonus_field_1`| UNVERIFIED | Bonus / stat field. |
| +0x1C  | 4    | int32   | `bonus_field_2`| UNVERIFIED | Bonus / stat field. |
| +0x20  | 4    | int32   | `bonus_field_3`| UNVERIFIED | Bonus / stat field; possibly durability or enchant level. |

Field names at +0x0C..+0x20 are inferred from local usage only — treat as UNVERIFIED; they are
consumed **inside the slot-state apply function** (not re-located by control flow this pass), not in
the ack handler. The state-ack handler gates on +0x08 then feeds this record into the same slot-apply
logic used by `ItemSlotRecord`.

### 7b. EquipItemResult — 16-byte equip/unequip result (opcode 4/12)

Read into a local buffer by the equip-result handler as a **fixed 16-byte body** (the handler's read
size is control-flow confirmed at 16 / 0x10). The apply path treats the underlying buffer as larger
(see the apply note).

| Offset | Size | Type   | Field        | Conf       | Meaning |
|--------|------|--------|--------------|------------|---------|
| +0x00  | 1    | uint8  | `guard`      | LIKELY     | Validity guard; must be non-zero. |
| +0x01  | 3    | —      | (pad)        | UNVERIFIED | Alignment. |
| +0x04  | 4    | uint32 | `actor_sort` | LIKELY     | Actor identity / sort check. |
| +0x08  | 1    | uint8  | `result`     | CONFIRMED (offset) | Read by the handler at +0x08: `0` = error → error UI; `1` = ok → apply; `2` = other. The +0x08 read is control-flow confirmed; the exact code set stays capture-pending. |
| +0x09  | 1    | uint8  | (unused)     | UNVERIFIED | |
| +0x0A  | 1    | uint8  | `from_slot`  | LIKELY     | Source slot index. |
| +0x0B  | 1    | uint8  | `from_sub`   | UNVERIFIED | From-sub-slot or related index. |
| +0x0C  | 1    | uint8  | `to_slot`    | **CONFIRMED** (offset + the `== 15` test) | **Single byte at +0x0C** (corrected this pass from the prior "+0x0B–0x0C" hedge). The result handler reads this one byte and tests it `== 15` to drive the **full visual gear rebuild**; value `14` is the special weapon slot. The +0x0C read and the `== 15` rebuild trigger are control-flow confirmed. |
| +0x0D  | 3    | char[3]| `pad_end`    | UNVERIFIED | Padding to 16 bytes. |

> **Equip apply fields.** The result handler reads `to_slot` as a **single byte at +0x0C** (the
> `== 15` visual-rebuild test) — this is control-flow confirmed. The deeper apply path (the
> equip-apply function, not the result handler, and **not re-located by control flow this pass**)
> additionally consumes the record dispatching the move type (`0` = unequip, `1` = equip, `2` =
> equip-swap) and reads a dword pair near +0x1C/+0x20 as a dirty/expiry check (both `0` = not
> expired). Those deeper apply byte positions are **static-hypothesis** (carried, not re-pinned) and
> should be pinned against a capture; only the +0x0C `to_slot` read is confirmed here.

### 7c. Item-panel slot-update packet (opcode 4/149) — header + record run

The authoritative server push for both equipment and bag contents. A fixed header selects the
container and slot range, then a back-to-back run of 16-byte `ItemSlotRecord`s follows. The header
guard, the ownership gate, the `chunk_type` two-way branch, the `(start_index, count)` loop bounds,
and the 16-byte record-run framing are all **control-flow confirmed** in the handler this pass.

| Offset | Size | Type  | Field         | Conf      | Meaning |
|--------|------|-------|---------------|-----------|---------|
| +0x00  | 1    | uint8 | `guard`       | CONFIRMED | Handler gate: `!= 1 → return`. |
| +0x04  | 4    | int32 | `local_id_key`| CONFIRMED | Ownership gate: compared against the local-player id key; mismatch → return. |
| +0x08  | 1    | uint8 | `chunk_type`  | CONFIRMED | Two-way handler branch: `0` = equipment table, `1` = bag/inventory table. |
| +0x09  | 1    | uint8 | `start_index` | CONFIRMED | First destination slot to write (loop base). |
| +0x0A  | 1    | uint8 | `count`       | CONFIRMED | Number of 16-byte records that follow (loop bound). |
| +0x0C  | 16×`count` | — | `records`     | CONFIRMED | `count` × `ItemSlotRecord` (§1), written back-to-back (4-dword copy per slot, 16-byte stride). |

Destination and bound-check by `chunk_type`:

- `chunk_type == 0` (equipment): records land in EquipTable at `start_index`, bound-checked against
  the 20-slot limit.
- `chunk_type == 1` (inventory): records land in BagTable at `start_index`, bound-checked against the
  active soft cap `40 * (bag_count + 3)`.

Gap bytes +0x01..+0x03 and +0x0B are unmapped (UNVERIFIED); pin the exact header stride against a
capture before committing the packet YAML.

---

## 8. Item upgrade-result packet (opcode 4/50) — 32-byte fixed body

The server response to an item-upgrade attempt. Read as a **fixed 32-byte body** — the handler's read
size is **control-flow confirmed** at 0x20. On success it replaces the target slot's record and resets
that slot's entry in the **global enchant-progress accumulator array** (§1a); on partial progress it
adds the delta into that global accumulator entry. The **RESULT / REASON branch table is now
control-flow CONFIRMED** (CYCLE 7) — promoted from the earlier hedged "reason 1/2/3/4" note; the field
offsets the handler itself reads (RESULT +0x08, REASON +0x09, `slot_index` +0x0B, `enchant_delta`
+0x1C) are all control-flow confirmed. The new-record dwords (+0x10..) are consumed inside the
**upgrade-apply function** (not re-located this pass) — treat those as static-hypothesis.

| Offset | Size | Type   | Field          | Conf       | Meaning |
|--------|------|--------|----------------|------------|---------|
| +0x00  | 8    | char[8]| `header`       | LIKELY     | Guard / actor-identity prefix bytes. |
| +0x08  | 1    | uint8  | `result`       | CONFIRMED  | Read by the handler at +0x08: `1` = success; `0` = not-success (branch on `reason`). Control-flow confirmed. |
| +0x09  | 1    | uint8  | `reason`       | CONFIRMED  | Read by the handler at +0x09. See the RESULT/REASON branch table below. Control-flow confirmed. |
| +0x0A  | 1    | uint8  | `pad_a`        | UNVERIFIED | Not individually decoded. |
| +0x0B  | 1    | uint8  | `slot_index`   | CONFIRMED  | Upgrade slot index; indexes the global enchant accumulator array (and its sibling id array). Control-flow confirmed. |
| +0x0C  | 4    | uint32 | `pad_c`        | UNVERIFIED | Not individually decoded; possibly extra info. |
| +0x10  | 4    | uint32 | `new_flags`    | LIKELY     | On success: replaces the slot's leading dword (+0x00..+0x03). |
| +0x14  | 4    | uint32 | `new_actor_id` | LIKELY     | On success: new item actor id; replaces slot +0x04. |
| +0x18  | 4    | uint32 | `new_qty`      | LIKELY     | On success: new quantity / `expiry_lo`; replaces slot +0x08. |
| +0x1C  | 4    | uint32 | `enchant_delta`| CONFIRMED  | On partial progress (REASON 2/3): added into the slot's **global** enchant accumulator entry (§1a). The +0x1C read is control-flow confirmed. |

**RESULT / REASON branch table — CONFIRMED (control flow):**

| `result` (+0x08) | `reason` (+0x09) | Outcome | Client action |
|---|---|---|---|
| 1 | (any) | **SUCCESS** | Reset the slot's global accumulator entry to 0, apply the new record, play the **success cue 862100101**. |
| 0 | 1 | **FAIL (generic)** | Generic-fail apply path; play the **fail/progress cue 862100102**. |
| 0 | 2 | **PARTIAL PROGRESS (tier A)** | Add `enchant_delta` (+0x1C) into the slot's global accumulator entry; advance the gauge; play cue 862100102. |
| 0 | 3 | **PARTIAL PROGRESS (tier B)** | Add `enchant_delta` (+0x1C) into the slot's global accumulator entry; advance the gauge; play cue 862100102. |
| 0 | 4 | **FAIL (alternate)** | A distinct apply path (the candidate item-break / downgrade branch); play cue 862100102. |

Sound cues: success = **862100101**; fail / partial-progress = **862100102** (CONFIRMED constants).

> **REASON 4 semantic.** REASON 4 takes a different apply path from the generic REASON 1 fail and is
> the candidate **item-break / downgrade** branch; the binary names neither failure path, so the exact
> "break vs downgrade" gameplay distinction is **RUNTIME-ONLY** (capture-pending). Both REASON 1 and
> REASON 4 are failure paths.

**Upgrade caps & the recipe table (CONFIRMED, CYCLE 7):**

- **Upgrade-level cap = 28.** The upgrade UI loads 28 distinct per-level art assets (a `weap_made01`
  … `weap_made28` family), so there are 28 upgrade levels. This is consistent with the static
  `enchant_level` column range 0..28 on the binary item record; the relationship between that column
  and the runtime weapon-glow grade 1..9 (§6g) remains UNVERIFIED.
- **Upgrade process = 6 steps.** The upgrade-process panel state machine exits when its current-step
  counter reaches **≥ 6**, so the flow has 6 process steps.
- **`upgradeitems.scr` recipe table — record stride = 44 bytes (0x2C).** Loaded into a **doubly-linked
  list** (not a flat array), keyed on **record +0x04** (primary lookup key, likely the base item id)
  and **record +0x08** (secondary key, likely a material / second item id); the recipe **result id** is
  returned from **record +0x00**. The payload region **+0x0C..+0x2B** holds success-rate floats and
  step parameters; the exact field-by-field layout is **UNVERIFIED**.
- **Per-recipe success rates are RUNTIME-ONLY (server-authored).** The upgrade panel's animated
  "step gauge rate" is a **time-based UI fill** (elapsed time since the step started, against a cached
  duration), **not** the success probability — do not treat the animated gauge as the success rate.

Behaviour summary: `result == 1` ⇒ overwrite all four dwords of `slot_index`'s record and reset its
global accumulator entry. `result == 0 && reason ∈ {2,3}` ⇒ add `enchant_delta` into the global
accumulator entry (no record replacement). `result == 0 && reason ∈ {1,4}` ⇒ failure paths
(REASON 4 = candidate break/downgrade); play the fail cue and show the localized message.

---

## 9. NpcBuy / inventory-acquire ack (opcode 4/19) — 56-byte fixed body

Read as a **fixed 56-byte body** — the handler's read size is **control-flow confirmed** at 0x38.
The `result` gate at +0x10 and the `reason_code` test at +0x11 are confirmed in the handler; the
descriptor words and repair fields are consumed inside the **npc-buy apply path / string-format
branch** (not re-located by control flow this pass) — treat them as static-hypothesis.

| Offset | Size | Type    | Field            | Conf       | Meaning |
|--------|------|---------|------------------|------------|---------|
| +0x00  | 4    | char[4] | `header`         | LIKELY     | Packet prefix. |
| +0x04  | 4    | int32   | `actor_id`       | LIKELY     | Target actor id. |
| +0x08  | 4    | int32   | `gold_lo`        | LIKELY     | Gold cost (low) or amount. |
| +0x0C  | 4    | int32   | `gold_hi`        | UNVERIFIED | Gold cost (high) or a secondary currency. |
| +0x10  | 1    | uint8   | `result`         | CONFIRMED (offset) | Read by the handler at +0x10: `== 1` → apply (npc-buy apply path); `== 0` → error branch. The +0x10 read is control-flow confirmed; the code set stays capture-pending. |
| +0x11  | 1    | uint8   | `reason_code`    | CONFIRMED (offset) | Read by the handler at +0x11: `== 1` selects the "item-shop-expired" localized message (a fixed message-db id); other values select other error strings. The +0x11 read is control-flow confirmed. |
| +0x12  | 1    | uint8   | `bag_slot_index` | LIKELY     | Destination bag slot (consumed in the apply path). |
| +0x13  | 13   | char[13]| `gap_13`         | UNVERIFIED | Unmapped to +0x20. |
| +0x20  | 4    | uint32  | `repair_val_1`   | UNVERIFIED | Repair-related value; non-zero shows a repair-completion countdown. |
| +0x24  | 4    | uint32  | `repair_val_2`   | UNVERIFIED | Repair-related value. |
| +0x28  | 4    | int32   | `item_quad_a`    | UNVERIFIED | Item descriptor word A. |
| +0x2C  | 4    | int32   | `item_quad_b`    | LIKELY     | Item descriptor word B = item actor id. |
| +0x30  | 4    | int32   | `item_quad_c`    | UNVERIFIED | Item descriptor word C = quantity / stack. |
| +0x34  | 4    | int32   | `item_quad_d`    | UNVERIFIED | Item descriptor word D. |

Behaviour (control-flow confirmed gates; value detail capture-pending): `reason_code == 1` → show the
item-shop-expired message. `result == 1` with a valid `item_quad_b` actor id → apply the item to
`bag_slot_index`. Non-zero `repair_val_*` → show a repair-completion countdown UI.

---

## 10. Upgrade staging buffer (item-actor runtime region) — LIKELY

Beyond the 548-byte template region, the item actor's runtime region holds an upgrade staging
workspace used to hold a pending upgrade transaction before the server confirms it:

| Item-actor offset | Size | Type    | Field                   | Conf   | Meaning |
|-------------------|------|---------|-------------------------|--------|---------|
| +1604 (0x644)     | 20   | u8[20]  | `upgrade_staging`       | LIKELY | Receives five 4-byte fields from the equip-swap / upgrade apply path; later consumed by the upgrade-result handler. |
| +2592 (0xA20)     | 1    | uint8   | `upgrade_pending_commit`| LIKELY | Non-zero ⇒ an upgrade transaction is staged and awaiting server confirmation. |

These are runtime-only (not part of the static template copy) and are not transmitted; they exist so
the engineer understands the client's pending-upgrade state machine.

---

## 11. Constants summary

| Constant | Value | Conf | Meaning |
|----------|-------|------|---------|
| Stack cap | 1000 | CONFIRMED | Max count per stackable slot. |
| Equip slot count | 20 | CONFIRMED | EquipTable indices 0..19. |
| Bag page size | 40 | CONFIRMED | Slots per bag container. |
| Base bag pages | 3 | CONFIRMED | Default before extra bags (120 slots). |
| Max extra bags | 3 | CONFIRMED | Drives the soft cap up to the hard cap. |
| Bag hard cap | 240 (0xF0) | CONFIRMED | Absolute slot ceiling. |
| Item-actor template region | 548 bytes (0x224) | CONFIRMED | Static per-item-type data copied from the binary item record. |
| Item-actor full template struct | 552 bytes (0x228) | CONFIRMED | Template region + a trailing pointer dword. |
| Weapon-effect grade display range | 1..9 (0 = none) | CONFIRMED | Glow tier; values 101..109 normalize to 1..9. |
| Socketing-stone subtype | 62 | CONFIRMED | `item_subtype` discriminator. |
| Special NPC-guard subtype | 1001 | CONFIRMED | Suppressed from sell display. |
| Special-goods subtype | 1004 | CONFIRMED | `item_subtype` gated by the cash-shop / goods info path (added CYCLE 7). |
| Upgrade-level cap | 28 | CONFIRMED | 28 distinct per-level art assets (`weap_made01`…`weap_made28`); upgrade levels 1..28 (added CYCLE 7). |
| Upgrade-process step count | 6 | CONFIRMED | Upgrade-process panel exits when current step ≥ 6 (added CYCLE 7). |
| `upgradeitems.scr` record stride | 44 bytes (0x2C) | CONFIRMED | Recipe record; doubly-linked list keyed on +0x04 / +0x08, result id at +0x00; payload +0x0C..+0x2B = success-rate floats + step params (field-by-field UNVERIFIED) (added CYCLE 7). |
| Upgrade success cue | 862100101 | CONFIRMED | 4/50 RESULT 1 sound id (added CYCLE 7). |
| Upgrade fail / partial-progress cue | 862100102 | CONFIRMED | 4/50 RESULT 0 sound id (added CYCLE 7). |

---

## 12. Open questions

- **`flags_a` / `flags_b`** (ItemSlotRecord +0x00/+0x01) — copied verbatim, never individually
  dispatched; candidate meanings (item category / binding state / wear-restriction class) UNVERIFIED.
- **Binding state** (bound / unbound / tradeable) — not confirmed as any bit inside the 16-byte slot
  record; may be computed from item-actor template fields rather than stored in the slot. UNVERIFIED.
- **Equipment body-part assignment** for slots 0–7, 9–13, 16–19 — undecoded; need a slot-type table.
- **Slot 8 category** — confirmed excluded from stat-sum and equip-guard, but the category it
  represents is UNVERIFIED.
- **QuickUseSlots** — `secondary` field (+0x04) purpose and total slot count both UNVERIFIED; the
  4/17 ack write path is untraced.
- **+342/+346 set-bonus i16 pair** — confirmed as the 10th set-bonus output pair but the stat it
  feeds (critical / stamina-chi / other) is UNVERIFIED.
- **Per-stat set-bonus assignment** — only the attack/defense pair (+424/+436, +428/+440) is
  semantically confirmed; the STR/AGI/DEX/INT/CON/HP/MP pairs are assigned by position (LIKELY) and
  need the remaining set-distributor callers traced to confirm each slot → stat mapping.
- **`atk_bonus_min` vs `atk_bonus_max`** — both sum into one attack total; the strict min/max
  distinction at the item level needs a binary item-record field-order cross-check.
- **`weapon_effect_grade` ↔ static enchant level** — the mapping between the runtime 0..9 grade and
  the 0..28 static enchant-level column is UNVERIFIED (direct vs. derived).
- **`item_subtype` full code table** — only values 62 and 1001 are confirmed from analysis; the full
  equippable-range / special-code table is data-driven and unverified by direct cross-reference.
- **Durability max baseline** — lives in the binary item record, not at a confirmed item-actor
  offset; the running counter lives in a separate slot-indexed array (§6f).
- **EquipSlotBody +0x0C..+0x20** and **EquipItemResult fields beyond the 16-byte header** — inferred
  only; boundaries UNVERIFIED.
- **4/149 header gap bytes** (+0x01..+0x03, +0x0B) — the load-bearing header offsets (guard +0x00,
  id key +0x04, chunk_type +0x08, start_index +0x09, count +0x0A) and the 16-byte record-run framing
  are now control-flow confirmed; only the unmapped gap bytes need a capture.
- **Cash-shop / warehouse containers** — no distinct slot arrays found; likely separate opcode
  families. UNVERIFIED.
- **Tiering this pass:** the five item-wire **routings, body sizes, and primary handler read offsets**
  are control-flow confirmed (no addresses retained); the §6 item-actor template offsets are
  static-hypothesis carried from prior analysis (not re-located this pass); every wire-field **value
  semantic** and any binding/tradeable bit remain **capture/debugger-pending** — no live capture was
  available this campaign (`sample_verified` not yet set for the wire bodies).
</content>
</invoke>
