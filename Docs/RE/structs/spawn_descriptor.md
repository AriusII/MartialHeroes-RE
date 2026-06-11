# SpawnDescriptor — full 880-byte field layout (clean-room spec)

Neutral, offset-model extension of `actor.md`'s SpawnDescriptor section. Promoted from a dirty-room
note; rewritten, no decompiler identifiers, no binary addresses. This document is the design input
for the **network-protocol-engineer** decoding the 880-byte descriptor carried by 5/3 CharSpawn and
the 3/1 CharacterList slot records, and for the **domain engineer** consuming the equipment / stat /
buff sub-fields.

> **Offsets are relative to the start of the SpawnDescriptor (SD).** When the descriptor is copied
> into a live actor on spawn, every field lands at `actor_offset = SD_offset + 0x74` (see
> `actor.md`). This document gives **SD-relative** offsets only.
>
> **Total size: 880 bytes (0x370).** Confirmed by the fixed-size copy the spawn handlers perform.
>
> **Confidence tags:** `CONFIRMED` = cross-checked at multiple use sites; `LIKELY` = single
> consistent use site, plausible; `UNVERIFIED` = inferred / boundary not pinned; needs a capture.

---

## Top-level field table

| SD offset | Size | Type     | Field                 | Conf       | Meaning |
|-----------|------|----------|-----------------------|------------|---------|
| +0x00     | 17   | char[17] | `name`                | CONFIRMED  | NUL-terminated actor name, up to 16 chars + NUL. |
| +0x11     | 3    | —        | (pad)                 | LIKELY     | Alignment to +0x14. |
| +0x14     | 2    | uint16   | `inner_event_code`    | LIKELY     | Internal event code; the value 55 gates a "reset to default motion" visual path. |
| +0x16     | 12   | char[12] | `gap_16`              | UNVERIFIED | Mostly opaque; the last byte acts as a discriminator for clone display-name routing. |
| +0x22     | 2    | uint16   | `clone_name_flag`     | LIKELY     | Non-zero → a player-clone display name is active. |
| +0x24     | 8    | int64    | `current_xp`          | CONFIRMED  | Current experience points (signed 64-bit). |
| +0x2C     | 1    | uint8    | `body_variant`        | LIKELY     | Body / gender model variant (selects a different mesh). |
| +0x2D     | 9    | char[9]  | `gap_2D`              | UNVERIFIED | Padding / unmapped. |
| +0x36     | 2    | uint16   | `gap_36`              | UNVERIFIED | Read by a visual handler; purpose unknown. |
| +0x38     | 1    | uint8    | `level_state`         | UNVERIFIED | A level/state byte read by the vitals path. Its relationship to the `level` u16 below is unresolved — `level` may straddle this byte. See caveat. |
| +0x39     | 1    | uint8    | `secondary_level`           | UNVERIFIED | Secondary level/state byte. |
| +0x3A     | 2    | uint16   | `level`               | LIKELY     | Character level (u16). Read by the spawn handler. Byte boundary unverified vs. +0x38; see caveat. |
| +0x3C     | 4    | uint32   | `current_hp`          | CONFIRMED  | Current hit points. Written by the 5/53 vitals path. |
| +0x40     | 4    | uint32   | `current_mp`          | CONFIRMED  | Current mana / ki. |
| +0x44     | 4    | uint32   | `current_stamina`     | CONFIRMED  | Current stamina. |
| +0x48     | 4    | uint32   | `scenario_state`      | LIKELY     | Internal scenario-state dword; low bytes hold a sub-state. |
| +0x4C     | 4    | float    | `world_x`             | CONFIRMED  | World X spawn origin. Doubles as a move-speed value for one actor sort via a union overlay. |
| +0x50     | 4    | float    | `world_z`             | CONFIRMED  | World Z spawn origin. |
| +0x54     | —    | —        | (equipment slot array)| CONFIRMED  | **128 bytes of equipment slots — see "Equipment slot array" below. Overlaps the +0x54..+0xD4 range.** |
| +0x74     | 2    | uint16   | `server_class`        | LIKELY     | Server-assigned class id (martial-arts style discriminator). |
| +0x76     | 6    | char[6]  | `gap_76`              | UNVERIFIED | Unmapped. |
| +0x7C     | 1    | uint8    | `race_or_skin`        | LIKELY     | Race / skin variant byte. |
| +0x7D     | 67   | char[67] | `visual_data`         | UNVERIFIED | Equipment-visual / appearance region. |
| +0xC0     | 4    | uint32   | `on_hit_item_id`      | LIKELY     | Item actor id for an on-hit effect (weapon reference). |
| +0xC8     | 8    | char[8]  | `gap_C8`              | UNVERIFIED | Unmapped. |
| +0xD0     | 4    | uint32   | `on_hit_mp_item_id`   | LIKELY     | Item actor id for an MP-on-hit effect. |
| +0xD4     | 600  | char[600]| `stat_equip_block`    | partial    | Large equipment / stat / buff block. Confirmed sub-fields below (~40% mapped). |
| +0x32C    | 4    | uint32   | `in_combat_flag`      | CONFIRMED  | Combat state flag; set when the actor enters combat. (Lies inside `stat_equip_block`.) |
| +0x354    | 4    | int32    | `mob_pair_partner_id` | LIKELY     | Paired-mob partner actor id, for mobs that spawn in pairs. (Inside the tail region.) |

> **Note on `world_x` / `world_z` overlap with the equipment array.** `actor.md` adopts +0x4C/+0x50
> as the live world coordinates, and the equipment slot array begins at +0x54 (immediately after the
> two coordinate floats). These do not conflict: the coordinates occupy +0x4C..+0x54, equipment
> begins at +0x54. The older +0x44/+0x48 coordinate view from a different base point is superseded.

### Caveat — `level` byte boundary

Two read paths disagree on whether `level` is a clean u16 at +0x3A or a value straddling the
state byte(s) at +0x38/+0x39. Disambiguate against a real CharSpawn capture before hard-coding a
16-bit `level` field. Until then treat +0x38..+0x3B as a 4-byte level/state group.

---

## Equipment slot array (SD+0x54 .. SD+0xD4)

The equipment slots occupy a **128-byte region: 8 records × 16 bytes**, beginning at SD+0x54. This
is confirmed by the item-panel slot-update path (opcode 4/149, chunk-type 0 = equipment): it writes
into this region with a 16-byte stride and bounds the write at SD+0xD4 (`0x54 + 8*0x10 = 0xD4`).

| Slot | SD offset | Size | Type           | Field           |
|------|-----------|------|----------------|-----------------|
| 0    | +0x54     | 16   | ItemSlotRecord | `equip_slot[0]` |
| 1    | +0x64     | 16   | ItemSlotRecord | `equip_slot[1]` |
| 2    | +0x74     | 16   | ItemSlotRecord | `equip_slot[2]` |
| 3    | +0x84     | 16   | ItemSlotRecord | `equip_slot[3]` |
| 4    | +0x94     | 16   | ItemSlotRecord | `equip_slot[4]` |
| 5    | +0xA4     | 16   | ItemSlotRecord | `equip_slot[5]` |
| 6    | +0xB4     | 16   | ItemSlotRecord | `equip_slot[6]` |
| 7    | +0xC4     | 16   | ItemSlotRecord | `equip_slot[7]` |

`ItemSlotRecord` is the 16-byte wire/runtime unit specified in `item.md`. Confidence: **CONFIRMED**
for the array bounds and stride; the per-field interpretation of each record carries `item.md`'s
own (partly UNVERIFIED) confidence.

> **Caution — overlap with named fields.** The `server_class` (+0x74), `race_or_skin` (+0x7C),
> `visual_data` (+0x7D), `on_hit_item_id` (+0xC0), and `on_hit_mp_item_id` (+0xD0) fields above fall
> **inside** this 0x54..0xD4 range. The two views (named scalar fields vs. an 8-slot equipment array)
> were recovered from different code paths and have **not** been reconciled. Do not assume both
> interpretations are simultaneously valid byte-for-byte. A CharSpawn capture is required to settle
> which path actually populates +0x54..+0xD4 on the wire. Treat the equipment-array view as the
> authoritative layout for the 4/149 slot-update path, and the named fields as the spawn-time view,
> pending reconciliation.

---

## stat_equip_block sub-fields (SD+0xD4 .. SD+0x32B, 600 bytes)

Only confirmed sub-offsets are listed; the block is roughly 40% mapped. All offsets are
SD-relative.

| SD offset | Size | Type   | Field              | Conf       | Meaning |
|-----------|------|--------|--------------------|------------|---------|
| +0x194    | 4    | int32  | `buff_value`       | LIKELY     | Buff magnitude. |
| +0x1AA    | 1    | uint8  | `buff_kind`        | LIKELY     | Buff-category discriminator (selects the buff effect / aura kind). |
| +0x1F0    | 2    | uint16 | `skill_state_word` | LIKELY     | Skill / buff state flags received on spawn. A high-level state discriminator, not a per-skill cooldown. |
| +0x305    | 1    | uint8  | `bag_slots_count`  | LIKELY     | Number of bag slots; used by the inventory path to bound the inventory-slot array. |
| +0x32C    | 4    | uint32 | `in_combat_flag`   | CONFIRMED  | Combat state flag (also listed at top level). |

> The remaining ~60% of `stat_equip_block` holds equipment / stat / buff tables that are not yet
> individually mapped. The network engineer should treat the unmapped span as an opaque blob inside
> the 880-byte descriptor. Note that, per the stat-formula analysis (`stats.md`), the player's stat
> bonuses are **not** read from this block directly — they come from the separately-instanced item
> actors referenced by the equipment id table. This block carries the raw equipment-set definition
> (item ids + slot assignments) the server sends on spawn, plus buff/combat state.

---

## Tail region (SD+0x330 .. SD+0x36F, 64 bytes)

Mostly unmapped. One confirmed sub-field:

| SD offset | Size | Type  | Field                 | Conf   | Meaning |
|-----------|------|-------|-----------------------|--------|---------|
| +0x354    | 4    | int32 | `mob_pair_partner_id` | LIKELY | Partner actor id for paired-mob spawns. |

---

## Open questions

- **`level` byte boundary** (+0x38 vs +0x3A) — needs a CharSpawn capture.
- **Equipment-array vs. named-scalar overlap** in +0x54..+0xD4 — two unreconciled views; needs a
  capture to determine which path populates the bytes on the wire.
- **`stat_equip_block`** — ~60% unmapped; needs a dedicated pass with capture evidence.
- **World Y** — forced to 0 by the server; whether SD+0x54 ever carries a non-zero Y in the older
  view is moot once the equipment-array view is confirmed.
- All offsets are static inferences; no live capture was available to confirm byte boundaries.
</content>
