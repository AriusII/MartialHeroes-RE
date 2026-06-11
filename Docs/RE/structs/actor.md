# Actor entity layout (clean-room spec)

Neutral, capture-informed offset model of the legacy client's in-world entity. Promoted from a
dirty-room note; rewritten, no decompiler identifiers or addresses. This document is the design
input for the **domain engineer** (the `Actor` model) and the **network-protocol-engineer**
(decoding the embedded `SpawnDescriptor` carried by 5/3 CharSpawn and 3/1 CharacterList).

> **Capture-unverified.** Offsets are static inferences. They are expressed relative to the start
> of each struct (never as binary addresses). Treat them as hypotheses until a capture confirms.

## Coordinate type - definitive design input

**World-space positions are IEEE-754 single-precision `float`, NOT fixed-point.**

The legacy client stores and interpolates actor positions as 32-bit floats throughout: spawn
origin, last-received network position, interpolation target, and the yaw quaternion are all
`float`. Movement is XZ-plane only; **world Y is effectively always 0** (the server never sends Y
and the client forces it to 0 on spawn and movement).

Guidance for the domain engineer:
- The **wire and legacy model is float**. A re-implementation may still choose a deterministic
  `Vector3Fixed` for its own simulation, but if it does so it must convert at the network boundary
  and must not assume the legacy client used fixed-point.
- There is no evidence of any fixed-point scaling factor.

## Actor - base fields the domain model needs

These are the fields the `Actor` domain model requires. Offsets are relative to the start of the
Actor object. Sizes/types as observed; fields the domain layer does not need (scene-graph,
animation-mixer, string slots, render flags) are omitted.

| Field            | Offset | Type   | Meaning |
|------------------|--------|--------|---------|
| `id`             | +0x5C  | int32  | Actor id; the map key. Initialised to 0xFFFFFFFF, set on spawn. |
| `sort`           | +0x60  | uint8  | Entity category discriminator: **1 = PC, 2 = Mob, 3 = NPC**, 4+ = other. |
| `move_speed`     | +0x64  | f32    | Movement speed multiplier. Default 1.0. |
| `current_hp`     | +0xB0  | uint32 | Current hit points. (Mirror of `SpawnDescriptor.current_hp`.) |
| `current_mp`     | +0xB4  | uint32 | Current mana / ki points. |
| `current_stamina`| +0xB8  | uint32 | Current stamina. |
| `world_x`        | +0xC0  | f32    | World X (spawn origin / live position seed). |
| `world_y`        | -      | f32    | Always 0 from the server (not stored as a distinct wire field). |
| `world_z`        | +0xC4  | f32    | World Z. |
| `last_packet_pos`| +0x428 | f32[3] | Last network-received position (x, y, z); used for AoE/skill origin. |
| `move_target`    | +0x450 | f32[3] | Destination position for movement interpolation (x, y=0, z). |
| `yaw`            | +0x4C0 | f32    | Facing; stored as a yaw quaternion built from the movement packet's yaw float. |
| `target_id`      | +0x6E8 | int32  | Id of the current target actor. Default 0. |
| `alive`          | +0x6EC | uint8  | Alive flag. Default 1. |
| `pk_flag`        | +0x704 | uint8  | PK (player-kill) mode flag. Default 0. |
| `combat_flag`    | +0x705 | uint8  | In-combat flag. Default 0. |
| `lifecycle_state`| +0x58C | int32  | Lifecycle / motion state: 0=uninit, 1=refreshing, 2=walk, 3=run, 8=dead/scripted. |

Notes for the domain engineer:
- **`max_hp` / `max_mp` are NOT stored as fields.** They are computed on demand from base stats
  plus equipment bonuses. The vitals path caps `current_hp` against the computed max. The domain
  model should compute max HP/MP from a formula, not expect a wire field.
- The vitals update path writes HP as part of an 8-byte (qword) block; storing `current_hp` as a
  64-bit-capable value is a reasonable forward-compatibility choice.
- `sort` doubles as the entity-type discriminator AND part of the actor-manager composite key
  `(id, sort)`. Lookups use both.

## SpawnDescriptor - the 5/3 CharSpawn payload (and 3/1 list records)

The `SpawnDescriptor` is an 880-byte (0x370) record. It is the body of the 5/3 CharSpawn push and
the first sub-block of each 3/1 CharacterList slot record. Offsets below are **relative to the
start of the SpawnDescriptor**.

| Field             | SD offset | Type   | Meaning |
|-------------------|-----------|--------|---------|
| `name`            | +0x00     | char[17] | Actor name, NUL-terminated UTF-8/ASCII (up to 16 chars + NUL). |
| `current_xp`      | +0x24     | int64  | Current experience points (signed 64-bit). |
| `model_id`        | +0x34     | uint16 | Model / mesh id (for sort==2 mobs, selects the mesh). |
| `level`           | +0x3A     | uint16 | Character level. **(byte boundary unverified - see caveat below.)** |
| `current_hp`      | +0x3C     | uint32 | Current hit points. |
| `current_mp`      | +0x40     | uint32 | Current mana / ki points. |
| `current_stamina` | +0x44     | uint32 | Current stamina. |
| `world_x`         | +0x4C     | f32    | World X. **Confirmed float.** Extracted into the actor's world position. |
| `world_z`         | +0x50     | f32    | World Z. **Confirmed float.** World Y forced to 0 on spawn. |
| `server_class`    | +0x74     | uint16 | Server-assigned class id (maps to a martial-arts style). |
| `race_or_skin`    | +0x7C     | uint8  | Race / skin variant. |
| `on_hit_item_id`  | +0xC0     | uint32 | Item id applied on hit (weapon / accessory reference). |
| `on_hit_mp_item_id`| +0xD0    | uint32 | Item id for an MP-on-hit effect. |
| `in_combat_flag`  | +0x32C    | uint32 | Combat state flag (within the large equipment / stat block). |

The region from +0x7D onward (and the 600-byte block at +0xD4) holds equipment slots, buff
tables, and combat stat bonuses. These are not individually mapped here and need a dedicated pass;
the network engineer should treat them as an opaque blob within the 880-byte descriptor for now.

### When embedded in the Actor

When the descriptor is copied into a live Actor on spawn, its fields land at `actor_offset =
SD_offset + 0x74`. For example the descriptor's `current_hp` (SD +0x3C) is the Actor's
`current_hp` at +0xB0, and the descriptor's `world_x` (SD +0x4C) feeds the Actor's `world_x`
at +0xC0. The vitals push (5/53) updates these in place on the live Actor.

## Unverified / open questions

- **`level` byte boundary.** One path treats `level` as a `u16` at SD +0x3A; another reads a
  level/state *byte* near SD +0x38, suggesting the u16 may straddle a state byte and a level byte.
  Disambiguate with a capture before treating `level` as a clean 16-bit field.
- **World-coordinate offset reconciliation.** A dirty-room note records two views of the spawn
  coordinate offset (descriptor +0x44/+0x48 vs +0x4C/+0x50) from different base points. This spec
  adopts +0x4C/+0x50 (the values actually extracted into the live position), but the two views
  MUST be reconciled against a real CharSpawn capture before an engineer hard-codes an offset.
- **Equipment / buff / stat block** (SD +0xD4, ~600 bytes): unmapped. Needs a dedicated pass.
- All offsets here are static inferences; no live capture was available.
