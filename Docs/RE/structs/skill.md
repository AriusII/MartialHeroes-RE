# Skill structures — wire and runtime layouts (clean-room spec)

Neutral, offset-model of the skill / hotbar structures the legacy client used. Promoted from a
dirty-room note; rewritten, no decompiler identifiers, no binary addresses. Design input for the
**network-protocol-engineer** (decoding the skill packets) and the **domain engineer** (skill model,
hotbar, and a `SkillType` enum).

> **Confidence tags:** `CONFIRMED` = cross-checked at multiple sites; `LIKELY` = single consistent
> site; `UNVERIFIED` = inferred / boundary not pinned.

---

## Runtime hotbar tables

The skill hotbar state is held in two parallel runtime arrays:

| Array          | Element / stride                | Capacity | Meaning |
|----------------|---------------------------------|----------|---------|
| skill-id array | int32 per slot, **8-byte stride** (2 ints per slot) | 240 slots (0..239) | Skill id assigned to each hotbar slot. Each slot occupies 2 ints; only the first is written by the hotbar-set path. |
| slot-points array | int16 (rank/points) per slot, parallel | 240 slots | Skill point / rank per slot; mirrors the skill actor's rank field. |

The second int of each 8-byte skill-id pair is **not** written by any analyzed handler — purpose
UNVERIFIED (possibly a skill sub-id or a cooldown mirror). The domain hotbar model should reserve
the pair but only depend on the first int as the skill id.

Hotbar capacity is **240 slots (0xF0)**; the slot index is a `uint8` in the range 0..239.

Update rule (from the hotbar-set push, opcode 5/33):

```
slot_index   = packet hotbar_slot (u8, must be < 240)
skill_id     = packet skill_id (int32)
skill_points = packet skill_points (int16)

hotbar.skill_id[slot_index]    = skill_id
hotbar.slot_points[slot_index] = skill_points
```

---

## SkillHotbarSlotSet — 20-byte wire packet (opcode 5/33)

Authoritative hotbar-slot update push (server → client).

| Offset | Size | Type    | Field          | Conf       | Meaning |
|--------|------|---------|----------------|------------|---------|
| +0x00  | 4    | int32   | `sort`         | LIKELY     | Actor sort (category in the low byte). |
| +0x04  | 4    | int32   | `actor_id`     | LIKELY     | Actor id (identity check vs. local player). |
| +0x08  | 1    | uint8   | `hotbar_slot`  | CONFIRMED  | Hotbar slot index (0..239). |
| +0x09  | 3    | char[3] | `pad`          | LIKELY     | Alignment. |
| +0x0C  | 4    | int32   | `skill_id`     | CONFIRMED  | Skill id to assign to this slot. |
| +0x10  | 2    | int16   | `skill_points` | CONFIRMED  | Skill point allocation / rank for this skill. |
| +0x12  | 2    | char[2] | `pad_end`      | LIKELY     | Padding to 20 bytes. |

Total: 20 bytes. (Field widths sum to 20.)

---

## SkillHotbarAssignResult — 24-byte wire packet (opcode 4/41)

Result of a client-initiated hotbar assignment (server → client).

| Offset | Size | Type    | Field             | Conf       | Meaning |
|--------|------|---------|-------------------|------------|---------|
| +0x00  | 4    | uint32  | `header`          | LIKELY     | Packet prefix (value 1 expected). |
| +0x04  | 4    | uint32  | `actor_id`        | LIKELY     | Actor id. |
| +0x08  | 1    | uint8   | `gate`            | CONFIRMED  | `1` = apply/ok; `0` = error. |
| +0x09  | 1    | uint8   | `result_code`     | LIKELY     | Error reason (1..8) → localized error strings. |
| +0x0A  | 2    | char[2] | `pad`             | LIKELY     | Alignment. |
| +0x0C  | 4    | int32   | `hotbar_slot_echo`| LIKELY     | Echo of the requested hotbar slot. |
| +0x10  | 4    | int32   | `skill_id_echo`   | LIKELY     | Echo of the requested skill id. |
| +0x14  | 4    | uint32  | `skill_point_pool`| LIKELY     | Remaining skill points after assignment. |

Total: 24 bytes. Behaviour: `gate==1` → look up the slot's skill, refresh hotbar + skill UI;
`gate==0` → clear the slot and show the error string.

---

## SkillPointUpdate — variable-length wire packet (opcode 4/150), minimum 16 bytes

| Offset | Size | Type   | Field   | Conf       | Meaning |
|--------|------|--------|---------|------------|---------|
| +0x00  | 1    | uint8  | `valid` | CONFIRMED  | Must equal 1. |
| +0x01  | 3    | —      | (pad)   | LIKELY     | Alignment to +0x04. |
| +0x04  | 4    | int32  | `idkey` | LIKELY     | Actor-id key, matched against the local player. |
| +0x08  | 4    | uint32 | `mode`  | CONFIRMED  | `1` = set total skill points; `2` = level-up notice. |
| +0x0C  | 4    | uint32 | `value` | CONFIRMED  | `mode==1` → new total skill-point pool. `mode==2` → new character level. |

Minimum 16 bytes; `mode==2` paths read additional level-up data from a runtime singleton (not part
of this packet's fixed prefix). The displayed skill-point pool is capped at 255 (0xFF) **for the UI
string only** — the wire value may exceed 255; do not clamp the protocol value.

---

## ActorSkillAction — skill-action broadcast (opcode 5/52), variable-length

The richest skill handler: drives AoE projectile spawning, visual effects, and hit application.
Only partial fields are recovered; field positions are **not yet pinned** (UNVERIFIED — needs a
dedicated capture-backed pass before this can be specced as a struct).

| Field             | Conf       | Meaning |
|-------------------|------------|---------|
| `actor_id`        | UNVERIFIED | Caster actor id. |
| `skill_id`        | UNVERIFIED | Skill being cast. |
| `target_actor_id` | UNVERIFIED | Primary target actor id. |
| `skill_aoe_count` | UNVERIFIED | AoE hit count; drives a split/clone loop. |
| `world_x`,`world_z`| UNVERIFIED| Cast-origin float coordinates. |

The protocol engineer must not implement 5/52 from this stub; it requires a dedicated pass.

---

## Skill-actor catalog fields

Skill data is held in a **separate skill catalog** loaded from disk, not in the world-actor map.
Skills are looked up by skill id through a catalog lookup, distinct from the general actor lookup.
The hotbar-apply path reads these fields from a skill catalog entry. **Offsets relative to the start
of the skill catalog entry:**

| Offset | Size  | Field           | Conf       | Meaning |
|--------|-------|-----------------|------------|---------|
| +1292 (0x50C) | u16 | `skill_rank`    | LIKELY  | Current skill rank / level. Mirrored into the hotbar slot-points array. |
| +1306 (0x51A) | u16 | `skill_category`| LIKELY  | Skill category / type discriminator. This is the field to back a domain `SkillType` enum. |

`skill_category` drives UI routing: the recovered values `0, 6, 7, 11, 18` route to a
passive/AoE path; other values route to active-skill paths (one of two, depending on a per-skill
activation-mode flag). The exact semantic of each category value is **UNVERIFIED** — the domain
engineer should define the `SkillType` enum from this field but treat the specific value→type
mapping as provisional until pinned against skill data.

A separate per-skill **activation-mode byte** (in a catalog entry resolved by skill id, distinct
from the category above) selects which UI panel slot an active skill populates. Treat as UNVERIFIED.

---

## Skill state in the SpawnDescriptor

Per `spawn_descriptor.md`: `skill_state_word` (SD+0x1F0, u16) is a high-level skill/buff state
discriminator received on spawn — **not** a per-skill cooldown table. Exact semantics UNVERIFIED.

A separate runtime cooldown table is recomputed from the skill catalog; its size and layout are not
recovered (UNVERIFIED) and are out of scope for this spec.

---

## Notes for the domain engineer

1. Skill ids are 32-bit ints; hotbar slots are `uint8` (0..239); the hotbar has 240 slots.
2. The skill-point pool is a `uint32`; the 255 cap is a UI-only display clamp — do not clamp the
   protocol value.
3. Back the `SkillType` enum on `skill_category` (catalog entry +1306, u16); the value→type mapping
   is provisional.
4. Skill actors are catalog entries (disk-loaded), not world actors — model them as a separate
   catalog keyed by skill id.

## Open questions

- 5/52 ActorSkillAction field layout — UNVERIFIED, needs a dedicated capture-backed pass.
- `skill_category` value → `SkillType` mapping — provisional.
- The unused second int of each 8-byte hotbar slot pair — purpose UNVERIFIED.
- Per-skill activation-mode byte semantics — UNVERIFIED.
- Runtime cooldown-table layout — not recovered.
- All offsets are static inferences; no live capture was available.
</content>
