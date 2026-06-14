# Actor entity layout (clean-room spec)

Neutral, capture-informed offset model of the legacy client's in-world entity (the "Actor"
object). Promoted from dirty-room notes; **rewritten** — no decompiler identifiers, no binary
addresses. This document is the design input for the **domain engineer** (the `Actor` model in
`Client.Domain`) and the **network-protocol-engineer** (decoding the embedded `SpawnDescriptor`
carried by 5/3 CharSpawn, 5/1 ActorSpawnExtended, and 3/1 CharacterList list records).

All offsets are expressed **relative to the start of the struct** (the Actor object, or the
SpawnDescriptor, as labelled). They are never binary addresses. A few cross-struct facts are
referenced as field offsets (e.g. SD +0x3C) because that is how the wire/struct layout is
specified — those are layout facts, not code locations.

## Status header

| Aspect | State |
|---|---|
| Overall struct size | **sample_verified** — total object size 0x748 bytes (1864 dec); single heap object per in-world entity, natural 4-byte alignment (not packed). |
| Embedded `SpawnDescriptor` | **sample_verified** — 0x370 bytes (880 dec), copied verbatim from the wire on spawn, inlined at Actor +0x74. |
| Coordinate type | **confirmed** — world positions are IEEE-754 32-bit `float`; world Y is always 0. |
| Identity / vitals / position fields | **confirmed** — exercised by multiple handlers (spawn, vitals, movement, skill). |
| `level` byte boundary | **draft / unverified** — see open question 1. |
| Equipment / buff / stat block (SD +0xD4, ~600 B) | **draft** — only three interior points located; treat as opaque. |
| Stat-slot table | **confirmed-as-external** — it is **not** an Actor field; see "Stat-slot table" below. |

Confidence per field is given inline in each table (`confirmed`, `high`, `partial`, `draft`).
The full open list is at the end.

> **What is pinned vs. what needs a capture.** Identity (`id`, `sort`), the live vitals
> (`current_hp` / `current_mp` / `current_stamina`), the spawn/live position floats, yaw, the
> target/alive/PK/combat flags, and the lifecycle state are pinned and implementable now. The
> exact `level` byte boundary, the 600-byte equipment/buff block interior, and a handful of
> `partial`/`draft` auxiliary fields still need a real CharSpawn / 5-53 vitals capture to confirm.

---

## Object model overview

The Actor is a single heap object of **0x748 bytes** with the following top-level regions:

| Range | Size | Region |
|---|---|---|
| +0x00 .. +0x73 | 0x74 | Engine header: object-type pointer slot + inherited scene-graph/transform base fields. |
| +0x74 .. +0x3E3 | 0x370 | **Embedded `SpawnDescriptor`** — copied verbatim from the spawn packet body. |
| +0x3E4 .. +0x747 | 0x364 | Actor-specific live state: visibility/transform, network position state, vitals mirrors, animation mixer, name string slots, target/flag state, AoE data. |

The first 4-byte slot at +0x00 is the object-type pointer (a C++ virtual-table pointer). The
client identifies an Actor by the run-time type name string `Actor`. Engineers re-implementing
in C# do **not** reproduce this pointer; it exists only because the legacy object is a C++ class.

When the descriptor is copied into a live Actor on spawn, **`actor_offset = SD_offset + 0x74`**.
For example the descriptor's `current_hp` (SD +0x3C) becomes the Actor's `current_hp` at +0xB0,
and the descriptor's world-X float (SD +0x4C) becomes the Actor's spawn world-X at +0xC0.

---

## Coordinate type — definitive design input

**World-space positions are IEEE-754 single-precision `float`, NOT fixed-point.**

The legacy client stores and interpolates actor positions as 32-bit floats throughout: spawn
origin, last-received network position, live interpolated position, interpolation target, and the
facing yaw (fed into a quaternion) are all `float`. Movement is XZ-plane only; **world Y is
effectively always 0** — the server never sends Y and the client forces it to 0 on spawn and on
movement.

Evidence (neutral facts, not code):
- The 5/13 ActorMovementUpdate handler receives and stores two `float` position components
  (X, Z) and two `float` destination components.
- CharSpawn extracts the world X / world Z out of the descriptor as `float` and seeds the live
  position with them.
- `last_packet_pos`, `world_pos`, `move_target`, and `interp_pos` are all `float[3]` arrays.
- The facing yaw is a `float` consumed by a "set quaternion from Y axis-angle" routine.
- The per-frame distance/cull check operates on two `float`-vector pointers.

Guidance for the domain engineer:
- The **wire and legacy model is float**. A re-implementation may still choose a deterministic
  `Vector3Fixed` for its own simulation, but it must convert at the network boundary and must not
  assume the legacy client used fixed-point. There is no evidence of any fixed-point scaling
  factor; do not use `Vector3Fixed` for these wire coordinates.

---

## Actor — base fields the domain model needs (quick reference)

These are the fields the `Actor` domain model requires. Offsets are relative to the start of the
Actor object. Fields the domain layer does not need (scene-graph node, animation mixer, string
slots, render flags) are summarized later but omitted here.

| Field            | Offset | Type   | Confidence | Meaning |
|------------------|--------|--------|------------|---------|
| `id`             | +0x5C  | int32  | confirmed  | Actor id; the map key. Initialised to 0xFFFFFFFF, set on spawn. |
| `sort`           | +0x60  | uint8  | confirmed  | Entity category discriminator: **1 = PC, 2 = Mob, 3 = NPC**, other values for further categories. |
| `move_speed`     | +0x64  | f32    | confirmed  | Movement speed multiplier. Default 1.0. |
| `scale_factor`   | +0x68  | f32    | high       | Visual scale multiplier. Default 1.0. |
| `model_class_id` | +0x6C  | int32  | confirmed  | Resolved visual/mesh class id (looked up from the model template for mobs). |
| `current_hp`     | +0xB0  | uint32 | confirmed  | Current hit points. (Mirror of `SpawnDescriptor.current_hp`.) |
| `current_mp`     | +0xB4  | uint32 | confirmed  | Current mana / ki points. |
| `current_stamina`| +0xB8  | uint32 | confirmed  | Current stamina. |
| `world_x`        | +0xC0  | f32    | confirmed  | Live world X (seeded from spawn origin). |
| `world_y`        | —      | f32    | confirmed  | Always 0 from the server (not stored as a distinct wire field). |
| `world_z`        | +0xC4  | f32    | confirmed  | Live world Z. |
| `last_packet_pos`| +0x428 | f32[3] | confirmed  | Last network-received position (x, y, z); used as AoE / skill origin. |
| `move_target`    | +0x450 | f32[3] | confirmed  | Destination position for movement interpolation (x, y=0, z). |
| `interp_pos`     | +0x47C | f32[3] | confirmed  | Smoothed position used during interpolation. |
| `yaw`            | +0x4C0 | f32    | confirmed  | Facing; fed into a yaw quaternion built from the movement packet's yaw float. |
| `lifecycle_state`| +0x58C | int32  | confirmed  | Lifecycle / motion state (see enum below). |
| `target_id`      | +0x6E8 | int32  | confirmed  | Id of the current target actor. Default 0. |
| `alive`          | +0x6EC | uint8  | confirmed  | Alive flag. Default 1. |
| `pk_flag`        | +0x704 | uint8  | confirmed  | PK (player-kill) mode flag. Default 0. |
| `combat_flag`    | +0x705 | uint8  | confirmed  | In-combat flag. Default 0. |

`lifecycle_state` (+0x58C) enumerated values (observed in spawn / movement / skill gates):

| Value | Meaning |
|---|---|
| 0 | Uninitialised. |
| 1 | Refreshing / active (live, accepting updates). |
| 2 | Walk. |
| 3 | Run. |
| 8 | Dead / scripted. |

Notes for the domain engineer:
- **`max_hp` / `max_mp` are NOT stored as fields.** They are computed on demand for the **local
  player only**, from base stats plus equipment plus auras (see `stats.md`). The vitals path caps
  `current_hp` against the computed max. Remote actors carry only `current_hp` / `current_mp` as
  sent by the server. The domain model should compute max HP/MP from a formula, never expect a
  wire field.
- The vitals update path writes HP as part of an 8-byte (qword) block alongside MP; storing
  `current_hp` as a 64-bit-capable value is a reasonable forward-compatibility choice.
- `sort` doubles as the entity-type discriminator AND part of the actor-manager composite key
  `(id, sort)`. Lookups use both.

---

## Full Actor field table

Offsets relative to the start of the Actor object. "—" in the confidence column for padding rows
means the gap exists for natural 4-byte alignment. Fields inside the embedded SpawnDescriptor
(+0x74 .. +0x3E3) are detailed in their own table below.

### Engine header and identity (+0x00 .. +0x73)

| Offset | Size | Type   | Field            | Confidence | Meaning |
|--------|------|--------|------------------|------------|---------|
| +0x00  | 4    | ptr    | (type pointer)   | confirmed  | C++ virtual-table pointer. Not reproduced in a managed re-implementation. |
| +0x04  | 88   | bytes  | (engine base)    | confirmed  | Inherited scene-graph / transform base: child list, bounding sphere, render mask word, transform link, plus a scene-node name string set to a fixed literal in the constructor. Opaque to the domain model. |
| +0x5C  | 4    | int32  | `id`             | confirmed  | Numeric actor id and map key. Init 0xFFFFFFFF, set on spawn. |
| +0x60  | 1    | uint8  | `sort`           | confirmed  | Entity category: 1=PC, 2=Mob, 3=NPC, other values for further categories. |
| +0x61  | 3    | —      | (pad)            | —          | Alignment. |
| +0x64  | 4    | f32    | `move_speed`     | confirmed  | Movement speed multiplier. Init 1.0. |
| +0x68  | 4    | f32    | `scale_factor`   | high       | Visual scale multiplier. Init 1.0. |
| +0x6C  | 4    | int32  | `model_class_id` | confirmed  | Resolved visual/mesh class id. For mobs derived from the model template keyed by the descriptor's model id. |
| +0x70  | 4    | bytes  | (gap)            | low        | Unverified pad between `model_class_id` and the embedded descriptor. |

### Embedded SpawnDescriptor (+0x74 .. +0x3E3)

See the dedicated SpawnDescriptor table below.

### Live state — scene/visibility and transform (+0x3E4 .. +0x41F)

| Offset | Size | Type   | Field             | Confidence | Meaning |
|--------|------|--------|-------------------|------------|---------|
| +0x3E4 | 1    | uint8  | `visible`         | confirmed  | Render visibility flag. 0 in constructor, set to 1 by the spawn factory after insertion. |
| +0x3E5 | 3    | —      | (pad)             | —          | Alignment. |
| +0x3E8 | 64   | bytes  | `xform_node`      | confirmed  | Scene-graph transform node (the 64-byte transform sub-object). A derived facing angle is cached inside it. Opaque to the domain model. |
| +0x3FC | 4    | f32    | `anim_speed`      | partial    | Animation playback speed (1.0 default, occasionally overridden by a snapshot update). |
| +0x414 | 1    | uint8  | `anim_speed_flag` | partial    | Enables the custom animation speed above. |

### Live state — network position (+0x428 .. +0x4FF)

| Offset | Size | Type   | Field             | Confidence | Meaning |
|--------|------|--------|-------------------|------------|---------|
| +0x428 | 12   | f32[3] | `last_packet_pos` | confirmed  | Last network-received position (x, y, z). Written from movement packets; used as AoE / skill origin. |
| +0x434 | 12   | f32[3] | `last_packet_rot` | confirmed  | Last network-received rotation (3 floats), written alongside `last_packet_pos`. |
| +0x440 | 4    | int32  | (gap)             | partial    | Unknown int; 0 in constructor, preserved across respawn. |
| +0x444 | 12   | f32[3] | `world_pos`       | confirmed  | Live interpolated world position (x, y, z); seeded from the spawn world floats and interpolated each frame. |
| +0x450 | 12   | f32[3] | `move_target`     | confirmed  | Movement-interpolation destination (x, y=0, z), set by the movement handler. |
| +0x45C | 8    | bytes  | (gap)             | low        | Unverified. |
| +0x464 | 4    | f32    | (aux target)      | low        | Possible secondary Z target used in angle math. Unverified. |
| +0x47C | 12   | f32[3] | `interp_pos`      | confirmed  | Position used during smoothing/lerp; reset to `world_pos` on respawn. |
| +0x488 | 4    | int32  | `last_state_ms`   | confirmed  | Millisecond timestamp of last state change. |
| +0x48C | 52   | bytes  | (rotation block)  | partial    | Rotation/transform working area that contains the yaw float below at byte +52. |
| +0x4C0 | 4    | f32    | `yaw`             | confirmed  | Facing yaw (float), fed into a "set quaternion from Y axis-angle" call. Written from the movement packet's yaw field. |
| +0x4C4 | 36   | bytes  | (rotation tail)   | low        | Tail of the rotation block. |
| +0x4E8 | 4    | uint32 | `prev_hp`         | confirmed  | Previous-HP mirror, written just before an HP update so the party panel can diff HP changes. |
| +0x4EC | 4    | uint32 | `prev_mp`         | confirmed  | Previous-MP mirror. |
| +0x4F0 | 16   | bytes  | (gap)             | partial    | Several interior dwords zeroed in the constructor. |

### Live state — update control and animation mixer (+0x500 .. +0x5FB)

| Offset | Size | Type   | Field                 | Confidence | Meaning |
|--------|------|--------|-----------------------|------------|---------|
| +0x500 | 4    | int32  | (aux)                 | partial    | Init 0; purpose unverified. |
| +0x504 | 1    | uint8  | `enabled`             | confirmed  | Global update-enable flag. Init 1. |
| +0x505 | 3    | —      | (pad)                 | —          | Alignment. |
| +0x508 | 4    | int32  | (gap)                 | low        | Unknown. |
| +0x50C | 4    | int32  | `last_anim_change_ms` | confirmed  | Millisecond timestamp of last animation state change. Init to current ms in constructor. |
| +0x510 | 4    | int32  | (gap)                 | partial    | Zeroed in constructor. |
| +0x514 | 16   | bytes  | (gap)                 | partial    | Pre-mixer scratch (four dwords zeroed). |
| +0x524 | 48   | bytes  | `mixer_head`          | confirmed  | Animation-mixer list/queue head. Opaque to the domain model. |
| +0x554 | 56   | bytes  | (mixer state)         | partial    | Animation-mixer state fields. `lifecycle_state` sits at byte +56 of this region (= +0x58C). |
| +0x588 | 4    | int32  | `anim_cycle_timer`    | partial    | Timer (set to 60 by the vitals handler for the pair-state path). Distinct role unverified. |
| +0x58C | 4    | int32  | `lifecycle_state`     | confirmed  | Lifecycle / motion state enum (0/1/2/3/8 — see table above). Heavily used as a gate in skill and movement handlers. |
| +0x590 | 4    | int32  | `dirty_flag`          | confirmed  | Set to 1 in constructor; non-zero means the actor still needs refresh and is not yet accepting network updates. Checked as a precondition in the skill handler. |
| +0x594 | 16   | bytes  | (gap)                 | partial    | Interior dwords zeroed in constructor. |
| +0x5A4 | 4    | int32  | (gap)                 | partial    | Zeroed in constructor. |
| +0x5A8 | 4    | int32  | (gap)                 | partial    | Zeroed in constructor. |
| +0x5AC | 4    | f32    | `anim_blend_t`        | confirmed  | Animation blend factor. Init 1.0. |
| +0x5B0 | 12   | bytes  | (gap)                 | partial    | Interior: a millisecond timestamp at +4. |
| +0x5BC | 4    | int32  | (gap)                 | partial    | Init -1. |
| +0x5C0 | 4    | int32  | (gap)                 | partial    | Init 0. |
| +0x5C4 | 4    | int32  | `respawn_state`       | partial    | Respawn / warp state during zone transitions. |
| +0x5C8 | 1    | uint8  | `respawn_flag`        | partial    | Respawn flag (set from the movement-predict path). |
| +0x5C9 | 3    | —      | (pad)                 | —          | Alignment. |
| +0x5CC | 12   | bytes  | (gap)                 | partial    | Interior: a millisecond timestamp at +0. |
| +0x5D8 | 12   | bytes  | `anim_state`          | confirmed  | Animation state machine (3 dwords). The dword at +8 (= +0x5E0) is checked == 0 as a movement-permission precondition. |
| +0x5E4 | 12   | bytes  | `event_outlet`        | confirmed  | Event-outlet / callback chain. Opaque to the domain model. |
| +0x5F0 | 8    | bytes  | (gap)                 | partial    | Two dwords zeroed in constructor. |
| +0x5F8 | 4    | int32  | `anim_tag`            | confirmed  | Animation tag. Init 0. |

### Live state — display-name string slots (+0x5FC .. +0x6E3)

These are C++ `std::string` objects (32 bytes each in this build). They hold display text; the
domain model should treat them as engine-side and decode the wire `name` field itself.

| Offset | Size | Type        | Field        | Confidence | Meaning |
|--------|------|-------------|--------------|------------|---------|
| +0x5FC | 28   | bytes       | (gap)        | low        | Unverified. |
| +0x618 | 32   | std::string | `str_slot1`  | confirmed  | String slot. Purpose unverified. |
| +0x638 | 32   | std::string | `str_slot2`  | confirmed  | String slot. |
| +0x658 | 32   | std::string | `str_slot3`  | confirmed  | String slot; assigned a name in one snapshot path. |
| +0x678 | 32   | std::string | `name`       | confirmed  | Actor display name (CP949 / EUC-KR decoded). The decoded copy of the wire name. |
| +0x694 | 4    | int32       | `rank_class_state` | confirmed | Display-state gate written on spawn (PC branch) and on snapshot update; compared against small constants to choose a display state. |
| +0x698 | 4    | bytes       | (gap)        | partial    | Zeroed / cleared in constructor and on one snapshot branch. |
| +0x69C | 32   | std::string | `str_slot5`  | confirmed  | String slot. |
| +0x6BC | 4    | bytes       | (pad)        | partial    | Padding. |
| +0x6C0 | 32   | std::string | `str_slot6`  | confirmed  | String slot (guild / title auxiliary text). |
| +0x6E0 | 4    | bytes       | (pad)        | partial    | Padding. |

> Note on overlap: `rank_class_state` (+0x694) sits inside this region's address range but is a
> plain int32, not part of a string object. It is listed here because it is written from the same
> spawn / snapshot paths as the name slots.

### Live state — title, target, flags, AoE (+0x6E4 .. +0x747)

| Offset | Size | Type   | Field              | Confidence | Meaning |
|--------|------|--------|--------------------|------------|---------|
| +0x6E4 | 4    | uint32 | `title_slot`       | confirmed  | Title / rank index (written from a snapshot byte, stored as a dword), passed to the title-display update. |
| +0x6E8 | 4    | int32  | `target_id`        | confirmed  | Current target actor id. Init 0. |
| +0x6EC | 1    | uint8  | `alive`            | confirmed  | Alive flag. Init 1. |
| +0x6ED | 3    | —      | (pad)              | —          | Alignment. |
| +0x6F0 | 4    | int32  | (gap)              | partial    | Init 0. |
| +0x6F4 | 4    | ptr    | `companion_ptr`    | partial    | Pointer to a companion / pair sub-object; used to propagate animation-cycle and lifecycle updates to a paired actor. Allocation path unverified. |
| +0x6F8 | 1    | uint8  | `flag_byte`        | confirmed  | Generic state-flags byte. Init 0. |
| +0x6F9 | 3    | —      | (pad)              | —          | Alignment. |
| +0x6FC | 4    | int32  | (gap)              | low        | Unknown. |
| +0x700 | 4    | ptr    | `npc_interact_obj` | confirmed  | Pointer to an NPC-interaction sub-object, present for `sort==3` (NPC) only; null for PC and mob. Has a periodic (about 13-second) update. |
| +0x704 | 1    | uint8  | `pk_flag`          | confirmed  | PK (player-kill) mode flag. Init 0. |
| +0x705 | 1    | uint8  | `combat_flag`      | confirmed  | In-combat flag. Init 0. Checked before companion propagation. |
| +0x706 | 2    | —      | (pad)              | —          | Alignment. |
| +0x708 | 4    | int32  | `lock_state`       | confirmed  | Lock / interaction state. A zero value is a precondition for movement being allowed. |
| +0x70C | 32   | bytes  | (gap)              | partial    | Interior: an AoE-member-count dword at +0x728 used by a split-clone loop. |
| +0x72C | 12   | bytes  | (gap)              | partial    | Several interior bytes zeroed in constructor. |
| +0x734 | 1    | uint8  | `anim_var_a`       | partial    | Animation-variant byte written from snapshot updates and propagated to the companion. Semantics unverified. |
| +0x735 | 3    | —      | (pad)              | low        | Alignment. |
| +0x738 | 4    | int32  | `world_state_server` | partial  | Server-supplied world / zone state value (written by the game-tick config handler). |
| +0x73C | 4    | int32  | (gap)              | low        | Unknown. |
| +0x740 | 4    | int32  | `spawn_extra`      | partial    | Written on snapshot for both PC and mob actors from a trailing snapshot byte. Semantics unverified. |
| +0x744 | 4    | bytes  | (pad)              | low        | Trailing bytes to the 0x748 boundary. |

---

## Embedded SpawnDescriptor (the 5/3 CharSpawn payload and 3/1 list records)

The `SpawnDescriptor` is an **880-byte (0x370)** record. It is the body of the 5/3 CharSpawn push
(and 5/1 ActorSpawnExtended) and the first sub-block of each 3/1 CharacterList slot record. The
whole block is copied verbatim from the wire into the Actor at +0x74. Offsets below are
**relative to the start of the SpawnDescriptor**; the parenthesised "Actor +" column gives the
absolute Actor offset (= SD offset + 0x74) for the fields the live Actor uses directly.

| SD offset | Actor + | Size | Type     | Field             | Confidence | Meaning |
|-----------|---------|------|----------|-------------------|------------|---------|
| +0x00     | +0x74   | 17   | bytes[17]| `name`            | confirmed  | Actor name, NUL-terminated, **CP949 / EUC-KR** encoded (up to 16 bytes + NUL). |
| +0x11     | +0x85   | 3    | —        | (pad)             | —          | Alignment after name. |
| +0x14     | +0x88   | 2    | uint16   | `inner_event_code`| high       | Internal event / class code; compared against a fixed constant when resetting the default motion. |
| +0x16     | +0x8A   | 12   | bytes    | (gap)             | low        | Mostly unverified region up to the discriminator below. |
| +0x22     | +0x96   | 1    | uint8    | `name_clone_discriminator` | high | Display-name discriminator for the player-clone path (distinguishes same-named clones). Also feeds the slot-14 visible-gear catalog key as its high decimal digit. |
| +0x23     | +0x97   | 1    | bytes    | (gap)             | low        | Alignment up to SD +0x24. |
| +0x24     | +0x98   | 8    | int64    | `current_xp`      | high       | Current experience points (signed 64-bit). |
| +0x2C     | +0xA0   | 1    | uint8    | `appearance_variant` | confirmed | Body / gender appearance variant. The **`variant` argument** of the model-class formula (see SpawnDescriptor notes). For a list/spawn character the **server supplies it**; the create form only seeds it for the class carousel. |
| +0x2D     | +0xA1   | 7    | bytes    | (gap)             | low        | Remainder up to SD +0x34. |
| +0x34     | +0xA8   | 2    | uint16   | `internal_class`  | confirmed  | Internal class word. **For PCs it is the class id `{1,2,3,4}`** (1 Musa, 2 Salsu, 3 Dosa, 4 Monk) and is the **primary skeleton/appearance driver** -- the `class` argument of the model-class formula. For mobs the same field keys the model-template lookup. (Earlier drafts called this `model_id`.) |
| +0x36     | +0xAA   | 2    | uint16   | `anim_class_word` | partial    | Read in the PC spawn branch as a name-assignment gate; may encode class or animation variant. |
| +0x38     | +0xAC   | 1    | uint8    | `state_byte`      | confirmed  | Level/state byte, written by the 5/53 vitals handler from a packet byte. |
| +0x39     | +0xAD   | 1    | uint8    | `sub_level_byte`  | confirmed  | Second level/state byte, written by the 5/53 vitals handler from the next packet byte. |
| +0x3A     | +0xAE   | 2    | uint16   | `level`           | **draft**  | Character level. **May straddle the two state bytes above** — see open question 1. Do not hard-code as a clean u16 without a capture. |
| +0x3C     | +0xB0   | 4    | uint32   | `current_hp`      | confirmed  | Current hit points. Written by the 5/53 vitals handler; sometimes written as a qword pair with `current_mp`. |
| +0x40     | +0xB4   | 4    | uint32   | `current_mp`      | confirmed  | Current mana / ki points. |
| +0x44     | +0xB8   | 4    | uint32   | `current_stamina` | confirmed  | Current stamina (capped by the vitals path). |
| +0x48     | +0xBC   | 4    | uint32   | `scenario_state`  | partial    | Four bytes immediately before the world coordinates. Meaning unverified; present in the wire data. See open question 5. |
| +0x4C     | +0xC0   | 4    | f32      | `world_x`         | confirmed  | World X (float). Extracted into the live position on spawn. |
| +0x50     | +0xC4   | 4    | f32      | `world_z`         | confirmed  | World Z (float). World Y forced to 0 on spawn. |
| +0x54     | +0xC8   | 4    | bytes    | (gap)             | partial    | Tail of the coordinate region; one path reuses part of it as a move-speed override for a special sort. |
| +0x58     | +0xCC   | 320  | slot[20] | `equip_ref_table` | confirmed  | Visible-gear / equipment reference table: 20 entries of 16 bytes each. The **leading 4-byte dword of each entry is the part gid**. The renderer attaches **overlay slots {3,4,6,2,11,14}**, mapping each entry's gid to `data/char/skin/g{gid}.skn` to layer the worn outfit. The remaining 12 bytes per entry are unverified. Spans SD +0x58 .. SD +0x197. |
| +0x74     | +0xE8   | 2    | uint16   | `server_class`    | high       | Server-assigned class id (maps to a martial-arts style). |
| +0x76     | +0xEA   | 6    | bytes    | (gap)             | low        | Unverified padding. |
| +0x7C     | +0xF0   | 1    | uint8    | `race_or_skin`    | high       | Race / skin variant identifier. |
| +0x7D     | +0xF1   | 67   | bytes    | (gap)             | low        | Equipment / visual data region; needs a dedicated pass. |
| +0xC0     | +0x134  | 4    | uint32   | `on_hit_item_id`  | high       | Item-actor id applied on hit (weapon / accessory reference). |
| +0xC4     | +0x138  | 12   | bytes    | (gap)             | partial    | An interior dword feeds the max-HP equipment-bonus accumulator. |
| +0xD0     | +0x144  | 4    | uint32   | `on_hit_mp_item_id`| high      | Item id for an MP-on-hit effect. |
| +0xD4     | +0x148  | 600  | bytes    | `equip_stat_buff_block` | **draft** | Large equipment / buff / stat block. Treat as opaque (see interior notes and open question 4). Spans SD +0xD4 .. SD +0x32B. |
| +0x194    | +0x208  | 4    | int32    | `aura_pct_value`  | confirmed  | Aura percentage value (divided by 100 to get a multiplier) used in the max-HP / max-MP formulas. Interior of the buff block. |
| +0x1AA    | +0x21E  | 1    | uint8    | `buff_kind`       | confirmed  | Aura / buff type discriminator: 1 = HP aura, 2 = MP aura. Interior of the buff block. |
| +0x1F0    | +0x264  | 2    | uint16   | `skill_state_word`| partial    | Skill state word used for state gating in the skill handler. Interior of the buff block. |
| +0x2EE    | +0x362  | 1    | uint8    | `motion_state_byte` | high     | Passed by the PC spawn branch into a motion-setup helper; on a mob fast-path a `0xFF` sentinel triggers a move-speed / animation-tag shortcut. Interior of the buff block. |
| +0x32C    | +0x3A0  | 4    | uint32   | `in_combat_flag`  | high       | Combat state flag. |
| +0x330    | +0x3A4  | 64   | bytes    | `sd_tail`         | partial    | Tail of the descriptor. A partner / pair-actor id is stored at SD +0x354 (Actor +0x3C8) for mob pair-state tracking. |
| +0x35C    | +0x3D0  | 4    | int32    | `world_state`     | partial    | Read by the local-player-status handler to gate a disconnect notification. Semantics unverified. |
| +0x36F    | +0x3E3  | 1    | byte     | (descriptor end)  | confirmed  | Last byte of the SpawnDescriptor. |

### Notes on the SpawnDescriptor for the network engineer

- **CP949 names.** The `name` field at SD +0x00 is **CP949 / EUC-KR**, NUL-terminated, max 16
  bytes plus terminator. Decode with CP949, not UTF-8/ASCII (earlier drafts said UTF-8 — that is
  corrected here).
- **Vitals (`current_hp` / `current_mp` / `current_stamina`)** at SD +0x3C / +0x40 / +0x44 are
  the live values the 5/53 vitals push updates in place on the Actor; the 5/3 spawn seeds them.
- **World coordinates are the floats at SD +0x4C / +0x50** (not the four bytes at SD +0x48,
  whose role is unverified). World Y is always 0.
- **`level` is not yet a clean field** — see open question 1. Until a capture confirms the byte
  boundary, prefer reading the level/state bytes at SD +0x38 / +0x39 explicitly rather than a
  u16 at SD +0x3A.
- **The descriptor drives the rendered appearance** (for an existing PC, a 3/1 list slot, or a
  5/3 spawn). The two inputs are `internal_class` (SD +0x34, `{1,2,3,4}`) and `appearance_variant`
  (SD +0x2C). The client derives a model-class id from them:
  `model_class_id = 5 * (internal_class + 4 * appearance_variant) - 24`, which yields an IdB in
  `{1, 11, 16, 26}` for the four starter classes. That IdB selects the catalog skeleton (one of
  g1..g4 via the visual catalog — there is **no** literal `g{n}.bnd` filename computed). The
  visible outfit is then layered from the `equip_ref_table` (SD +0x58): the renderer attaches
  overlay slots `{3, 4, 6, 2, 11, 14}`, mapping each entry's leading gid to
  `data/char/skin/g{gid}.skn`. A list/spawn character therefore renders its REAL appearance from
  the server-supplied descriptor, never from the create-scene's hardcoded carousel gids.
- The **equipment reference table** (SD +0x58, 20×16 bytes) and the **600-byte equipment/buff
  block** (SD +0xD4) are largely opaque. Only the three interior points listed (`aura_pct_value`,
  `buff_kind`, `skill_state_word`) and the per-slot item-id dword in the equip table are located.
  Engineers should treat the rest as a reserved blob inside the 880-byte descriptor.

---

## Stat-slot table — NOT an Actor field (correction)

The local player's per-slot stat-bonus table (the equipment/buff stat slots consumed by the
max-HP / max-MP and primary-stat formulas) is **not** stored inside the Actor object. It lives in
a separate fixed region of the client's global data segment, indexed independently of the Actor
layout. Any earlier note placing a "stat-slot table at Actor +0x680" is **incorrect** — that
offset was measured relative to the global *pointer variable*, not relative to the Actor instance.

Slot-entry shape (each entry is 12 bytes = six 16-bit words):

| Bytes | Type  | Meaning |
|-------|-------|---------|
| [0..1]| int16 | Slot id. |
| [2..7]| —     | Unknown / padding (three int16). |
| [8..9]| int16 | Value (the stat-bonus amount). |
| [10..11]| int16 | Unknown. |

Representative slot ids observed in the formulas (see `stats.md` for the formula itself):

| Slot id | Meaning |
|---------|---------|
| 7       | Equipment HP bonus. |
| 2, 3, 9 | Equipment MP bonus (multiple slots). |
| 70 / 71 / 72 / 73 / 74 | STR / AGI / DEX / INT / CON equipment bonus. |
| 81      | HP-percentage buff slot. |
| 93      | Shared buff/debuff applied to all stats. |
| 43, 46, 49, 131 | Status-effect slots checked in the movement-permission path. |

Domain-engineer guidance: model the stat-slot table as a separate keyed collection (slot id →
value) owned by the player/stat subsystem, **not** as bytes inside the `Actor` struct. This keeps
the `Actor` layout faithful and avoids a phantom field.

---

## Actor lookup / container (context, not a struct)

The client keeps all in-world actors in a flat id-keyed map plus a secondary index bucketed by
`sort`. The primary access path is "look up actor by id" (the single busiest entity accessor in
the client); a secondary path looks up by the composite `(id, sort)` key. Spawn handlers insert
into this map and write the wire fields into the new Actor; despawn handlers erase from it. The
re-implementation should mirror this with an id-keyed dictionary plus, if needed, a per-sort
index — it does not need to reproduce the legacy container's internal node layout.

---

## Open questions

1. **`level` byte boundary (draft).** `state_byte` (SD +0x38) and `sub_level_byte` (SD +0x39) are
   confirmed written by the 5/53 vitals handler. A `level` u16 is also labelled at SD +0x3A. The
   u16 may overlap the two state bytes, or `level` may sit cleanly at SD +0x3A..+0x3B. **Resolve
   with a real 5/53 vitals capture for a character of known level before treating `level` as a
   clean 16-bit field.**
2. **`equip_ref_table` interior (SD +0x58 .. +0x197).** 20 entries × 16 bytes. The leading 4-byte
   dword per entry is **confirmed** as the visible-gear **part gid**; the renderer attaches
   **overlay slots {3,4,6,2,11,14}** from this table (each gid -> `data/char/skin/g{gid}.skn`).
   The remaining 12 bytes per entry (item state / visual override / enhancement data) are
   unverified.
3. **Appearance region (SD +0x2C .. +0x37).** `appearance_variant` (SD +0x2C, the `variant` arg)
   and `internal_class` (SD +0x34, the `class` arg, `{1,2,3,4}` for PCs) are confirmed as the
   model-class formula inputs (see SpawnDescriptor notes). Still open: the 7 bytes between
   (SD +0x2D..+0x33) and the role of `anim_class_word` (SD +0x36) need pattern analysis plus a
   capture.
4. **`equip_stat_buff_block` (SD +0xD4 .. +0x32B, 600 bytes).** Only three interior points are
   located (`aura_pct_value`, `buff_kind`, `skill_state_word`). The per-item equipment stat
   offsets referenced by the vitals formulas belong to *item* objects, not the player descriptor;
   the player-side buff-block layout is not individually mapped. Needs a dedicated pass.
5. **`scenario_state` (SD +0x48 .. +0x4B).** The four bytes immediately before the world
   coordinates are present in the wire data but their meaning is unverified.
6. **`companion_ptr` (+0x6F4).** Existence confirmed via indirect use; the allocation path was
   not traced. May be the same object as `npc_interact_obj` (+0x700) or a distinct pair system.
7. **AoE region (+0x70C, 32 bytes).** An AoE-member-count dword at +0x728 is confirmed; the rest
   of the region (likely a small array of AoE target references or an AoE state machine) is not
   individually mapped.
8. **`anim_var_a` (+0x734), `world_state_server` (+0x738), `spawn_extra` (+0x740).** All written
   from the network but their precise protocol roles need capture correlation.
9. **`max_hp` / `max_mp` are confirmed NOT stored.** Computed on demand (local player only) from
   stats + equipment + auras. Remote actors carry only `current_hp` / `current_mp` from the wire.
10. **Capture coverage.** Field *offsets and sizes* are recovered from static layout evidence and
    handler behaviour, not from a live capture. The position floats, the vitals fields, identity,
    and the lifecycle enum are exercised by multiple handlers and are high-confidence; everything
    marked `partial` / `draft` should be re-checked against a real CharSpawn / 5-53 capture before
    an engineer hard-codes it.
