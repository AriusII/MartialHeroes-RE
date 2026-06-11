# Item structures — wire and runtime layouts (clean-room spec)

Neutral, offset-model of the item-related structures the legacy client used. Promoted from a
dirty-room note; rewritten, no decompiler identifiers, no binary addresses. Design input for the
**network-protocol-engineer** (decoding the item / equip / shop packets) and the **domain engineer**
(applying item stat grants and equipment state).

> **Confidence tags:** `CONFIRMED` = cross-checked at multiple sites; `LIKELY` = single consistent
> site; `UNVERIFIED` = inferred / boundary not pinned.

---

## ItemSlotRecord — 16-byte wire/runtime unit

The canonical slot unit. The same 16 bytes are transmitted by the item-panel slot-update path
(opcode 4/149) and stored verbatim in the runtime item-slot array, and the same record shape backs
each equipment slot inside the SpawnDescriptor (see `spawn_descriptor.md`, "Equipment slot array").

| Offset | Size | Type   | Field          | Conf       | Meaning |
|--------|------|--------|----------------|------------|---------|
| +0x00  | 4    | uint32 | `word_0`       | UNVERIFIED | First dword, copied verbatim. Possibly an item template id or flags; not individually dispatched by any analyzed handler. |
| +0x04  | 4    | uint32 | `item_actor_id`| LIKELY     | Actor id of the item-actor instance. Used to resolve the item actor (which carries the stat-grant fields below). |
| +0x08  | 4    | uint32 | `expiry_lo`    | LIKELY     | Low 32 bits of a UNIX `time_t` expiry timestamp. `0` = no expiry (permanent item). |
| +0x0C  | 4    | uint32 | `expiry_hi`    | LIKELY     | High 32 bits of the same expiry `time_t`. `0` for most items. |

`expiry_lo` + `expiry_hi` together form a 64-bit `time_t` consumed by a local-time conversion when
the slot is applied. The actor-id read (+0x04) and the expiry read (+0x08) are confirmed by the
slot-apply path; the first and last dwords are written verbatim but never individually interpreted
by the analyzed handlers, so their semantics remain UNVERIFIED.

> **Per-slot chunk framing (4/149).** In the 4/149 packet each per-slot chunk is preceded by a
> 1-byte **destination slot index** plus a pad byte; the 16-byte `ItemSlotRecord` then follows. The
> slot-index byte selects the destination slot and is **not** copied into the record. The protocol
> engineer should frame each chunk as `{ slot_index:u8, pad:u8, ItemSlotRecord:16B }` and verify the
> exact chunk stride against a capture.

---

## EquipSlotBody — 36-byte item-slot state record (opcode 4/22)

Read as a fixed 36-byte (0x24) body by the single-slot state-ack handler. Distinct from the 16-byte
`ItemSlotRecord`; this carries equip/unequip result plus stat/enchant fields.

| Offset | Size | Type    | Field          | Conf       | Meaning |
|--------|------|---------|----------------|------------|---------|
| +0x00  | 8    | char[8] | `header`       | LIKELY     | Guard / identity prefix bytes. |
| +0x08  | 1    | uint8   | `result`       | LIKELY     | `0` = error, `1` = ok. |
| +0x09  | 1    | uint8   | `pad_9`        | UNVERIFIED | Padding. |
| +0x0A  | 1    | uint8   | `from_slot`    | LIKELY     | Source slot index. |
| +0x0B  | 1    | uint8   | `to_slot`      | LIKELY     | Destination slot index. |
| +0x0C  | 4    | uint32  | `flag_c`       | UNVERIFIED | Flags. |
| +0x10  | 4    | uint32  | `flag_10`      | UNVERIFIED | Flags. |
| +0x14  | 4    | —       | (gap)          | UNVERIFIED | Unmapped 4 bytes (between flag_10 and bonus_1). |
| +0x18  | 4    | int32   | `bonus_field_1`| UNVERIFIED | Bonus / stat field. |
| +0x1C  | 4    | int32   | `bonus_field_2`| UNVERIFIED | Bonus / stat field. |
| +0x20  | 4    | int32   | `bonus_field_3`| UNVERIFIED | Bonus / stat field; possibly durability or enchant level. |

Field names at +0x0C..+0x20 are inferred from local usage only — treat as UNVERIFIED. The
state-ack path feeds this record into the same slot-apply logic used by `ItemSlotRecord`.

---

## EquipItemResult — 16-byte equip/unequip result (opcode 4/12)

Read into a local buffer by the equip-result handler. The visible header is 16 bytes; the apply
path treats the underlying buffer as larger (see "Equip apply fields" note).

| Offset | Size | Type   | Field        | Conf       | Meaning |
|--------|------|--------|--------------|------------|---------|
| +0x00  | 1    | uint8  | `guard`      | LIKELY     | Validity guard; must be non-zero. |
| +0x01  | 3    | —      | (pad)        | UNVERIFIED | Alignment. |
| +0x04  | 4    | uint32 | `actor_sort` | LIKELY     | Actor identity / sort check. |
| +0x08  | 1    | uint8  | `result`     | LIKELY     | `0` = error, `1` = ok, `2` = other. |
| +0x09  | 1    | uint8  | (unused)     | UNVERIFIED | |
| +0x0A  | 1    | uint8  | `from_slot`  | LIKELY     | Source slot index. |
| +0x0B  | 1    | uint8  | `from_sub`   | UNVERIFIED | From-sub-slot or related index. |
| +0x0C  | 1    | uint8  | `to_slot`    | LIKELY     | Destination slot index. Value `15` triggers a visual gear refresh; value `14` is the special weapon slot. |
| +0x0D  | 3    | char[3]| `pad_end`    | UNVERIFIED | Padding to 16 bytes. |

> **Equip apply fields.** The apply path consumes the record using these positions (for protocol
> reference only; treat as the apply contract, not a second wire layout): `from_slot` at +0x0A,
> `to_slot` at +0x0B–0x0C (dispatched as `0` = unequip, `1` = equip, `2` = equip-swap), and a dword
> pair near +0x1C/+0x20 used as a dirty/expiry check (both `0` = not expired). The exact byte
> positions beyond the 16-byte header are UNVERIFIED and should be pinned against a capture.

---

## NpcBuy / inventory-acquire ack (opcode 4/19) — 56-byte fixed body

| Offset | Size | Type    | Field            | Conf       | Meaning |
|--------|------|---------|------------------|------------|---------|
| +0x00  | 4    | char[4] | `header`         | LIKELY     | Packet prefix. |
| +0x04  | 4    | int32   | `actor_id`       | LIKELY     | Target actor id. |
| +0x08  | 4    | int32   | `gold_lo`        | LIKELY     | Gold cost (low) or amount. |
| +0x0C  | 4    | int32   | `gold_hi`        | UNVERIFIED | Gold cost (high) or a secondary currency. |
| +0x10  | 1    | uint8   | `result`         | LIKELY     | `0` = error, `1` = ok. |
| +0x11  | 1    | uint8   | `reason_code`    | LIKELY     | Error reason; selects a localized error string. |
| +0x12  | 1    | uint8   | `bag_slot_index` | LIKELY     | Destination bag slot. |
| +0x13  | 13   | char[13]| `gap_13`         | UNVERIFIED | Unmapped to +0x20. |
| +0x20  | 4    | uint32  | `repair_val_1`   | UNVERIFIED | Repair-related value; non-zero shows a repair-completion countdown. |
| +0x24  | 4    | uint32  | `repair_val_2`   | UNVERIFIED | Repair-related value. |
| +0x28  | 4    | int32   | `item_quad_a`    | UNVERIFIED | Item descriptor word A. |
| +0x2C  | 4    | int32   | `item_quad_b`    | LIKELY     | Item descriptor word B = item actor id. |
| +0x30  | 4    | int32   | `item_quad_c`    | UNVERIFIED | Item descriptor word C = quantity / stack. |
| +0x34  | 4    | int32   | `item_quad_d`    | UNVERIFIED | Item descriptor word D. |

Behaviour: `result==0 && reason_code==1` → show item-shop-expired message. `result==1` with a valid
`item_quad_b` actor id → apply the item to `bag_slot_index`. Non-zero `repair_val_*` → show a
repair-completion countdown UI.

---

## Item-actor stat-grant fields (in the item actor's layout)

When an item is instanced as an actor, its actor layout carries direct stat-grant fields. These are
the fields the player's stat formula (`stats.md`) sums over every worn item (skipping slot 8).
**Offsets are relative to the start of the item actor's layout**, not the player's.

| Item-actor offset | Field            | Conf      | Meaning |
|-------------------|------------------|-----------|---------|
| +248 (0x0F8)      | `stat_grant_str` | CONFIRMED | STR bonus granted by this item. |
| +260 (0x104)      | `stat_grant_agi` | CONFIRMED | AGI bonus. |
| +272 (0x110)      | `stat_grant_dex` | CONFIRMED | DEX bonus. |
| +284 (0x11C)      | `stat_grant_int` | CONFIRMED | INT bonus. |
| +296 (0x128)      | `stat_grant_con` | CONFIRMED | CON bonus. |
| +312 (0x138)      | `stat_grant_hp`  | CONFIRMED | Flat HP bonus. |
| +328 (0x148)      | `stat_grant_mp`  | CONFIRMED | Flat MP bonus. |

### Set-bonus fields (in the item actor's layout)

The set-bonus accumulator (`stats.md`, "all-or-nothing rule") reads additional per-pair fields from
each item actor when matching and applying a set. **Item-actor-relative offsets:**

| Item-actor offset | Conf      | Role |
|-------------------|-----------|------|
| +244 (0x0F4), u16 | CONFIRMED | Set-type id used to match items into a set. |
| +252 / +256      | CONFIRMED | A set-bonus output pair. |
| +264 / +268      | CONFIRMED | A set-bonus output pair. |
| +276 / +280      | CONFIRMED | A set-bonus output pair. |
| +288 / +292      | CONFIRMED | A set-bonus output pair. |
| +300 / +304      | CONFIRMED | A set-bonus output pair. |
| +316 / +320      | CONFIRMED | A set-bonus output pair. |
| +332 / +336      | CONFIRMED | A set-bonus output pair. |
| +344 / +346, u16 | CONFIRMED | A set-bonus output pair (16-bit). |
| +424 / +436      | CONFIRMED | Primary set-bonus outputs (mapped to STR/DEX set bonus in the formula). |
| +512 (0x200)     | CONFIRMED | Item flag dword; non-zero triggers the set-bonus lookup for this item. |

The semantic stat-to-field mapping of the output pairs (beyond the +424/+436 STR/DEX pair) is
**UNVERIFIED**; they are confirmed as set-bonus output slots by access pattern but not individually
labelled. The domain engineer should treat them as a per-stat set-bonus vector keyed off the
set-type id at +244, and pin the exact stat assignment against item data when available.

---

## Open questions

- `word_0` and `expiry` byte boundaries in `ItemSlotRecord` — verbatim-copied, semantics
  UNVERIFIED; pin against a 4/149 capture.
- `EquipSlotBody` +0x0C..+0x20 field meanings — inferred only.
- `EquipItemResult` fields beyond the 16-byte header (the dirty-check dword pair) — boundaries
  UNVERIFIED.
- Per-pair stat assignment of the item-actor set-bonus output fields — UNVERIFIED.
- All offsets are static inferences; no live capture was available.
</content>
