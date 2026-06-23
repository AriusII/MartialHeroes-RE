# Actor entity layout (clean-room spec)

> **Verification banner.**
> - **confirmed** — Actor size (0x748), embedded-descriptor size (0x370) and inlining offset
>   (Actor +0x74), `actor = SD + 0x74`, the identity / sort / position / vitals-mirror offsets,
>   the lifecycle enum, the equip-id table layout (20×16 @ Actor +0xCC), and the spatial cell
>   index (Actor +0x3EC) are **control-flow confirmed** in the spawn / respawn / spawn-extended
>   handlers.
> - **static-hypothesis** — single-site inferences (the `yaw` exact byte, the preview-path float
>   reuse of +0x488/+0x48C, the `current_hp/mp/stamina` Actor offsets derived purely by the
>   +0x74 rule).
> - **capture/debugger-pending** — every *wire VALUE meaning* (what a received byte signifies),
>   the live HP/MP/yaw/move-target writes (driven by the runtime-table-dispatched 5/53 vitals and
>   5/13 movement handlers, whose dispatch table is null at static time), and the role of the
>   `partial`/`draft` auxiliary fields.
> - **ida_reverified:** 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
>   **ida_anchor:** 263bd994  **evidence:** [static-ida]
> - **CYCLE 7 (2026-06-20):** added the 30-slot buff-slot array at Actor +0x208 (520), the
>   buff-related actor state fields it drives (+1013, +1420 enrichment, +1764, +1828, +1832, +1836,
>   +1837, +1838), and the death-state fields (+1424 alive semantics, +1420 value 8, +1480 death
>   timestamp). `specs/buffs.md` is the authority for the buff model. The earlier C7 "locked battle
>   target id at Actor +444" reading has been **resolved (re-verified in IDA, CYCLE 7)**: there is
>   **no** actor-side +444 battle-target field — the locked battle target lives on the
>   **battle-controller singleton** (controller +9 = target id dword, controller +40 = target sort
>   byte), distinct from the actor's UI/current `target_id` at +0x6E8. See `specs/combat.md` and the
>   settled note at item 12.
> - **conflicts:** no hard conflicts; two soft divergences carried as open items — (a) the `level`
>   byte boundary (clean u16 @ SD +0x3A for the display path vs. straddling the SD +0x38/+0x39
>   state bytes on the wire), (b) the +0x488/+0x48C region used as `last_state_ms` (int32) in one
>   path and as a 70.0f float pair in the preview path, and (c) the SpawnDescriptor equipment
>   view at SD +0x54 (8×16) vs. SD +0x58 (20×16) — see open questions.

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
| Overall struct size | **confirmed** — total object size 0x748 bytes (1864 dec); the shared spawn factory allocates a 0x748-byte object then runs the Actor constructor. Single heap object per in-world entity, natural 4-byte alignment (not packed). |
| Embedded `SpawnDescriptor` | **confirmed** — 0x370 bytes (880 dec), copied verbatim from the wire on spawn by a fixed-size 0x370 byte-copy, inlined at Actor +0x74. |
| Coordinate type | **confirmed** — world positions are IEEE-754 32-bit `float`; world Y is always 0 (the spawn handler stores X, forces Y = 0.0, stores Z). |
| Identity / vitals-mirror / position offsets | **confirmed** — exercised by multiple handlers (spawn, respawn, spawn-extended). Live *wire* writes from the 5/53 vitals and 5/13 movement handlers are runtime-table-dispatched (not statically reachable) → their value semantics are **capture/debugger-pending**. |
| `level` byte boundary | **static-hypothesis** — the char-select *display* path reads a clean u16 at SD +0x3A; the *wire* boundary vs. the SD +0x38/+0x39 state bytes is **capture-pending**. See open question 1. |
| Equipment-id table (Actor +0xCC = SD +0x58) | **confirmed** — 20 entries × 16 bytes; each entry's leading dword is a worn-item actor id, walked 20× with a 16-byte stride in both the live spawn and the preview lineup. |
| Equipment / buff / stat block (SD +0xD4, ~600 B) | **partial** — opaque blob; only a handful of interior points located (one new byte at SD +0x304 mapped this pass). Treat as reserved. |
| Local-player Actor pointer | **confirmed** — a single client global (the busiest entity reference in the client), **not** an Actor field. See "Local-player global slot" below. |
| Spatial cell index | **confirmed** — a per-actor cached grid-cell handle at Actor +0x3EC, computed from world X/Z. See the live-state table. |
| Stat-slot table | **confirmed-as-external** — it is **not** an Actor field; see "Stat-slot table" below. |
| 4/4 area-actor wire record | **confirmed** — the 892-byte on-area-entry spawn carrier (8-byte prefix + 880-byte descriptor + 4-byte trailer); prefix has NO sort dword (sort = the tag byte); composite key = (ActorId @+0, tag-byte sort). Control-flow + counter-confirmed CYCLE 12 / Phase 3. See the "4/4 area-entity-snapshot actor record" section. |

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

**The local player is referenced by a single client global, not by an Actor field.** A dedicated
global pointer holds the local player's Actor (see "Local-player global slot" below); the spawn and
respawn handlers compare an Actor against that global to choose self-only branches (e.g. the
self-spawn FX, and "skip recreating my own position"). Re-implementations should keep a single
`LocalPlayerActor` reference on the side rather than searching the actor map each frame.

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
| `sort`           | +0x60  | uint8  | confirmed  | Entity category discriminator: **1 = PC, 2 = Mob, 3 = NPC**. The spawn-factory switch also handles special sorts **15** and **17** (see the field-table note); the default branch covers the rest. |
| `move_speed`     | +0x64  | f32    | confirmed  | Movement speed multiplier. Default 1.0. |
| `scale_factor`   | +0x68  | f32    | confirmed  | Visual scale multiplier. Default 1.0 (the constructor writes the 1.0f bit pattern). |
| `model_class_id` | +0x6C  | int32  | confirmed  | Resolved visual/mesh class id (looked up from the model template for mobs). |
| `equip_ref_table`| +0xCC  | slot[20] | confirmed | 20 entries × 16 bytes; each entry's leading dword is a worn-item actor id. = SD +0x58. See the SpawnDescriptor table. |
| `cell_index`     | +0x3EC | int32  | confirmed  | Cached spatial grid-cell handle, resolved from the live world X/Z. NOT a coordinate. |
| `buff_slots`     | +0x208 | slot[30] | confirmed | 30 buff slots × 12 bytes; the in-world status (buff/debuff) table. See the buff-slot table below and **`specs/buffs.md`** (the authority for the model). |
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
| 1 | Refreshing / active (live, accepting updates); also the idle/normal action-state. |
| 2 | Walk. |
| 3 | Run. |
| 8 | **Dead / knockdown** — the canonical "is this actor dead" test (the respawn countdown re-checks `lifecycle_state != 8` to abort). The buff dispatch treats value 8 as a **protected** state it never overwrites. Set by the death-motion routine alongside `alive = 0` and the death timestamp (`+0x5C8`). See the death-state note below and `specs/buffs.md §3.3`. |
| 11 / 12 / 13 | **Buff-driven transform / stance poses** — written by the buff effect-kind dispatch (buff_id 43 → 11, 46 → 12, 131 → 13; cleared back to 1 by the cleanse path). See the buff-slot table below and `specs/buffs.md §3.1`. |

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
| +0x60  | 1    | uint8  | `sort`           | confirmed  | Entity category / spawn-kind. The spawn-factory switch enumerates **1 = PC**, **3 = NPC** (allocates a 0x50-byte NPC-interaction sub-object), and two special sorts **15** and **17** (15 reuses `world_x` @ +0xC0 as a move-speed override; 17, with a `0xFF` sentinel at +0x372, reuses `world_x`/`world_z` as move-speed + an animation tag). **2 = Mob** and all other categories fall through the default branch. |
| +0x61  | 3    | —      | (pad)            | —          | Alignment. |
| +0x64  | 4    | f32    | `move_speed`     | confirmed  | Movement speed multiplier. Init 1.0. |
| +0x68  | 4    | f32    | `scale_factor`   | confirmed  | Visual scale multiplier. Init 1.0 (constructor writes the 1.0f bit pattern). |
| +0x6C  | 4    | int32  | `model_class_id` | confirmed  | Resolved visual/mesh class id. The spawn factory resolves it from `(sort, internal_class @ SD +0x34, appearance_variant @ SD +0x2C)`; for mobs the same `internal_class` keys the model-template lookup. |
| +0x70  | 4    | bytes  | (gap)            | low        | Unverified pad between `model_class_id` and the embedded descriptor. |

### Embedded SpawnDescriptor (+0x74 .. +0x3E3)

See the dedicated SpawnDescriptor table below.

### Live state — scene/visibility and transform (+0x3E4 .. +0x41F)

| Offset | Size | Type   | Field             | Confidence | Meaning |
|--------|------|--------|-------------------|------------|---------|
| +0x3E4 | 1    | uint8  | `visible`         | confirmed  | Render visibility flag. 0 in constructor, set to 1 by the spawn factory after insertion. |
| +0x3E5 | 3    | —      | (pad)             | —          | Alignment. |
| +0x3E8 | 4    | ptr    | `xform_node`      | confirmed  | Scene-graph transform sub-object base. The spawn handler drives it (recursive set-position from the live world floats). Opaque to the domain model. |
| +0x3EC | 4    | int32  | `cell_index`      | confirmed  | **Spatial grid-cell handle** — a cached cell index resolved from the live world X/Z (re-resolved by the respawn handler from `world_pos`/`move_target`). NOT a coordinate. The per-actor spatial-index field used for cell-bucketed lookups. |
| +0x3F0 | 12   | bytes  | (xform tail)      | partial    | Remainder of the transform/orientation working area. Opaque to the domain model. |
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
| +0x488 | 4    | int32  | `last_state_ms`   | confirmed  | Millisecond timestamp of last state change (the respawn handler preserves it). **Context-dependent reuse:** the char-preview path instead writes +0x488/+0x48C as a 70.0f float pair (an AoE/cell working value), so this 4-byte slot is `int32` on the live path and `f32` in the preview path. See open question 11. |
| +0x48C | 52   | bytes  | (rotation block)  | partial    | Rotation/transform working area that contains the yaw float below at byte +52. The leading bytes overlap the preview-path float reuse noted above. |
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

## Buff-slot table (Actor +0x208, 30 × 12 bytes) — CYCLE 7

The actor carries a fixed **30-slot buff/debuff table** based at **Actor +0x208 (520)**, 12 bytes per
slot (360 bytes total, spanning Actor +0x208 .. +0x36F). **`specs/buffs.md` is the authority for the
model** (tick, effect-kind dispatch, icon/visual resolution, the buff push); this table records only
the *layout* on the actor. `confirmed` (CYCLE 7 — slot count and stride corroborated by the per-tick
loop, the cleanse loop, and the 360-byte local-player mirror).

Per-slot record (naturally aligned; offsets within the slot are 0 / 4 / 8):

| Slot offset | Actor + (slot 0) | Size | Type | Field | Confidence | Meaning |
|---|---|---|---|---|---|---|
| +0x00 | +0x208 | 2 | u16 | `buff_id` | confirmed | Effect-kind code; the dispatch + icon/visual key. `> 0` = active slot. |
| +0x02 | +0x20A | 2 | u16 | (pad/high half) | partial | Id is read 16-bit; alignment pad. |
| +0x04 | +0x20C | 4 | i32 | `remaining_ticks` | confirmed | Duration in **4-second ticks**; decremented 1 per tick, `<= 0` → release, `== 0` → clear. |
| +0x08 | +0x210 | 2 | u16 | `param` | confirmed | Low word of the buff's 32-bit source value (per-tick visual spawn Value; packed summon code for buff_id 57). |
| +0x0A | +0x212 | 1 | u8 | `param_hi` | confirmed | High byte (byte 2) of the 32-bit source value. |
| +0x0B | +0x213 | 1 | u8 | (pad) | partial | Alignment tail. |

> **Overlap note (soft conflict — CYCLE 7 vs. earlier passes).** Actor +0x208 is *also* the offset the
> SpawnDescriptor table below labels `aura_pct_value` (SD +0x194), and the buff-slot array (Actor
> +0x208 .. +0x36F) lies inside the SpawnDescriptor's opaque `equip_stat_buff_block` (SD +0xD4 .. +0x32B,
> Actor +0x148 .. +0x39F). The two readings are not contradictory: the **wire-copied descriptor blob**
> and the **runtime buff-slot table** occupy the same actor bytes — the spawn copies the descriptor
> into +0x74.., and the buff subsystem then drives the 30×12 slots from that region at runtime (the
> per-actor status table is *resident in* the descriptor's buff block). The `aura_pct_value` /
> `buff_kind` interior points (SD +0x194 / +0x1AA) are individual fields the max-HP/MP formulas read;
> the buff-slot table is the structured 30×12 view the buff tick walks. Carried as a context-dependent
> overlay (the same actor bytes serve both the wire-copied descriptor blob and the runtime buff-slot
> table), not a hard conflict.

## Buff-related actor state fields (CYCLE 7)

The buff effect-kind dispatch (`specs/buffs.md §3.1`) writes these actor fields. They are **not** part
of the 12-byte slot record; they are discrete state bytes/words elsewhere on the actor. `confirmed`
(CYCLE 7) for the offset + which buff path writes each; the precise input-gating semantics of the
motion-state values are RUNTIME-ONLY (see `buffs.md §3.3`).

| Actor offset | Size | Type | Field | Confidence | Meaning |
|---|---|---|---|---|---|
| +0x3F5 (1013) | 1 | u8 | `motion_suppress_flag` | confirmed | Toggled 0/1 by stun/stance buffs (cleared by buff_id 43 / 131). |
| +0x58C (1420) | 4 | int32 | `lifecycle_state` / action-motion-state | confirmed | Same field as `lifecycle_state` above — the buff dispatch sets it to **1 / 11 / 12 / 13** (transform/stance poses); value **8** is the protected death/special state the buff code never overwrites. See the lifecycle enum. |
| +0x6E4 (1764) | 1 | u8 | `disguise_outfit_id` | confirmed | Outfit/disguise id read by the buff_id 44 (disguise/polymorph) path to restore appearance on expiry. **Overlap:** the live-state table lists `title_slot` (uint32) at +0x6E4; this disguise byte is the low byte of that 4-byte region on the buff path — carried as a context-dependent overlay, not a hard conflict. |
| +0x724 (1828) | 4 | int32 | `summon_state` | confirmed | Set on buff_id 57 expiry (mirror-clone summon state). |
| +0x728 (1832) | 4 | int32 | `clone_count` | confirmed | Number of mirror clones for buff_id 57, derived from the slot's `param`. (This is the AoE-member-count dword the AoE/split-clone loop reads — see the live-state table's +0x70C region note.) |
| +0x72C (1836) | 1 | u8 | `hidden_stealth_flag` | confirmed | Set by the buff_id 45 (stealth) path; also gates the id-47 per-tick visual. |
| +0x72D (1837) | 1 | u8 | `flag_id47` | confirmed | Set while a buff_id 47 (DoT/periodic-aura) slot is active. |
| +0x72E (1838) | 1 | u8 | `flag_id64` | confirmed | Set while a buff_id 64 slot is active AND its `param < 100` (param-gated threshold flag). |

## Death-state fields (CYCLE 7)

The death-motion routine (invoked from the inbound death handler) writes the death state onto the
actor. `confirmed` (CYCLE 7). On death the actor's 30-slot buff table (Actor +0x208) is **cleared** —
timed buffs are removed (see `specs/buffs.md` and the death/respawn behaviour in `world_systems.md`).

| Actor offset | Size | Type | Field | Confidence | Meaning |
|---|---|---|---|---|---|
| +0x590 (1424) | 4 | int32 | `alive` | confirmed | Alive/active gate: **0 = dead**, 1 = alive. (Reconciles the quick-reference `alive` at +0x6EC, which is the constructor-default alive byte; this +0x590 dword is the death-handler's authoritative alive gate that the movement/targeting/pickup paths early-return on. Carried as the death gate.) Set to 1 on spawn / visual revive. |
| +0x58C (1420) | 4 | int32 | `lifecycle_state` | confirmed | Action/motion-state — **8 = death/knockdown**, 1 = idle. Same field as above; the death value is the authoritative "is dead" test. |
| +0x5C8 (1480) | 4 | int32 | `death_time_ms` | confirmed | Millisecond timestamp set at the moment of death (engine ms clock). |

> HP/MP vitals are the 8-byte (qword) block at **Actor +0xB0** (`current_hp` / `current_mp`, see the
> quick-reference and SpawnDescriptor tables) — server-set; the death handler does not compute any
> penalty (XP / durability / drop magnitudes are RUNTIME-ONLY / server-authoritative).

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
| +0x39     | +0xAD   | 1    | uint8    | `secondary_level_byte` | confirmed | Second level/state byte, written by the 5/53 vitals handler from the next packet byte. |
| +0x3A     | +0xAE   | 2    | uint16   | `level`           | **draft**  | Character level. **May straddle the two state bytes above** — see open question 1. Do not hard-code as a clean u16 without a capture. |
| +0x3C     | +0xB0   | 4    | uint32   | `current_hp`      | confirmed  | Current hit points. Written by the 5/53 vitals handler; sometimes written as a qword pair with `current_mp`. |
| +0x40     | +0xB4   | 4    | uint32   | `current_mp`      | confirmed  | Current mana / ki points. |
| +0x44     | +0xB8   | 4    | uint32   | `current_stamina` | confirmed  | Current stamina (capped by the vitals path). |
| +0x48     | +0xBC   | 4    | uint32   | `scenario_state`  | partial    | Four bytes immediately before the world coordinates. Meaning unverified; present in the wire data. See open question 5. |
| +0x4C     | +0xC0   | 4    | f32      | `world_x`         | confirmed  | World X (float). Extracted into the live position on spawn. |
| +0x50     | +0xC4   | 4    | f32      | `world_z`         | confirmed  | World Z (float). World Y forced to 0 on spawn. |
| +0x54     | +0xC8   | 4    | bytes    | (gap)             | partial    | Tail of the coordinate region; one path reuses part of it as a move-speed override for a special sort. |
| +0x58     | +0xCC   | 320  | slot[20] | `equip_ref_table` | confirmed  | Visible-gear / equipment reference table: 20 entries of 16 bytes each, **walked 20× with a 16-byte stride** in both the live spawn-extended handler and the char-preview lineup builder (each leading dword is passed to "find actor by id"). The **leading 4-byte dword of each entry is a worn-item actor id (part gid)**. The renderer attaches **overlay slots {3,4,6,2,11,14}**, mapping each entry's gid to `data/char/skin/g{gid}.skn` to layer the worn outfit. The remaining 12 bytes per entry are unverified. Spans SD +0x58 .. SD +0x197. |
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
| +0x2EE    | +0x362  | 1    | uint8    | `motion_state_byte` | confirmed | Read by the PC spawn branch (case 1) and the special sort-17 path as a motion selector; a `0xFF` sentinel triggers the mob move-speed / animation-tag fast-path. Interior of the buff block. (Confirmed from the consuming side this pass.) |
| +0x304    | +0x378  | 1    | uint8    | `buff_block_byte_304` | confirmed (offset) / capture-pending (meaning) | Written PC-side from a wire byte on the 5/1 spawn-extended path. A newly-mapped point inside the otherwise-opaque buff block; its protocol meaning needs a capture. |
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

## 4/4 area-entity-snapshot actor record (the on-area-entry spawn carrier)

> **Verification:** confirmed — the exact 892-byte read, the 8-byte prefix split, the 880-byte
> descriptor offset, the 4-byte trailer, and the composite-key derivation are control-flow-confirmed
> and independently counter-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3). VALUE meanings of the
> kind/relation/trailer bytes are capture-pending. Spec path to cite: `// spec: Docs/RE/structs/actor.md`.

When the player enters or refreshes a visible area, each nearby actor arrives as one **892-byte**
record inside the 4/4 SmsgAreaEntitySnapshot tag loop (tags 1, 2, and 3 all carry this record;
framing is owned by `packets/4-4_area_entity_snapshot.yaml`). The record wraps the shared 880-byte
SpawnDescriptor with an 8-byte prefix and a 4-byte trailer. This is the wrapper only — the 880-byte
interior is the SpawnDescriptor documented above and in `structs/spawn_descriptor.md`.

| Record off | Size | Type | Field | Notes |
|-----------|------|------|-------|-------|
| +0x000 | 4 | u32 | ActorId | actor id; the id half of the (id, sort) actor-manager key. Stored to the Actor identity id field. |
| +0x004 | 1 | u8 | KindByte | a kind/variant byte; value 5 gates a visual-only refresh of an existing actor (weapon/joint refresh) rather than a full spawn. VALUE-pending. |
| +0x005 | 1 | u8 | RelationVisual | a relation / visual byte; copied to the Actor relation/visual field. VALUE-pending. |
| +0x006 | 2 | — | pad | two padding bytes (not consumed). |
| +0x008 | 880 | — | SpawnDescriptor | the 880-byte descriptor core; copied wholesale into the Actor at Actor +0x74. World X/Z land at record +0x54 / +0x58. |
| +0x378 | 1 | u8 | TrailerVisual | a visual byte; copied to an Actor visual field (with companion propagation). VALUE-pending. |
| +0x379 | 1 | — | pad | not consumed. |
| +0x37A | 1 | u8 | CombatTimerFlag | when non-zero, arms the Actor combat timer to a fixed duration. VALUE-pending. |
| +0x37B | 1 | — | pad | final read byte, not consumed. |

Σ = 8 + 880 + 4 = 892 (0x37C), the exact tag-1/2/3 read length.

**No Sort dword in the prefix.** Unlike the 5/3 SmsgCharSpawn carrier (whose prefix is
`Sort u32 @+0 / ActorId u32 @+4`), the 4/4 record's first dword IS the ActorId, and there is **no
sort field in the body**. The actor **sort** is carried out-of-band by the **leading tag byte**:
tag 1 implies sort 1 (player); tags 2/3 pass the tag value as the sort. The composite key used to
evict an existing same-key actor and to insert the new one is therefore
**(ActorId from record +0x00, sort from the tag byte)**. This is how nearby actors populate on area
entry: evict same key → spawn factory copies the 880-byte descriptor into the Actor → store id/sort
→ insert keyed by (id, sort) → seed world XZ from record +0x54 / +0x58 (world Y forced 0).

The 4/4 record is the **shortest** member of the spawn-carrier family — its 4-byte trailer holds only
a visual byte and a combat-timer flag, with **no** 17-byte CP949 name region (the 5/3 carrier's
trailer has one). On the 4/4 path, an actor's secondary name/area-name update arrives instead via the
separate 36-byte tag-6 record.

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

The client keeps all in-world actors in a flat id-keyed map plus a secondary index keyed by the
composite `(id, sort)`. Two recovered access paths: a **cached composite-key lookup** keyed on
`(id, sort)`, and an **id-only lookup** (the busiest entity accessor in the client). Spawn handlers
insert via an `insert(map, id, sort, actor)` call and write the wire fields into the new Actor;
despawn handlers erase the current entry and clear it. The re-implementation should mirror this with
an id-keyed dictionary plus, if needed, a per-`(id,sort)` index — it does not need to reproduce the
legacy container's internal node layout.

## Local-player global slot (context, not an Actor field)

The local player's own Actor is referenced by **a single dedicated client global pointer**, set to
the player's Actor object. It is **not** a field of any Actor and **not** a slot in the network
handler. It is the busiest entity reference in the client (on the order of a thousand cross-uses),
which is consistent with "the local player is checked everywhere".

Two confirmed uses pin its role:
- The **respawn / warp-to-point** handler skips re-creating position state when the Actor being
  processed **is** the local-player global (i.e. "don't reset my own position from this packet").
- The **spawn** handler selects the *self* spawn-effect branch when the spawned Actor **equals** the
  local-player global.

Domain-engineer guidance: keep one `LocalPlayerActor` reference on the side (set when the player's
own spawn arrives), and gate self-only logic on identity against it — do not search the actor map to
find "me" each frame, and do not model this as a struct field. (Working name in the glossary:
`g_LocalPlayerActor`.)

---

## Open questions

1. **`level` byte boundary (static-hypothesis; wire capture-pending).** `state_byte` (SD +0x38) and
   `secondary_level_byte` (SD +0x39) are written by the runtime-table-dispatched 5/53 vitals handler
   (not statically reachable). A `level` u16 is labelled at SD +0x3A. **New IDB evidence:** the
   char-select **display** path reads a **clean u16 at SD +0x3A** and renders it as the level
   number. That is one (display) read site — it shifts `level @ SD +0x3A` from "draft" toward a
   likely clean u16, but it does **not** prove the *wire* encoding, because the 5/53 writes at
   SD +0x38/+0x39 are runtime-dispatched. **Resolve the wire boundary with a real 5/53 vitals
   capture for a character of known level before hard-coding a 16-bit `level` field.**
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
    handler control flow, not from a live capture. The position floats, the vitals-mirror offsets,
    identity, the sort/spawn-kind switch, the equip-id table, the cell index, and the lifecycle enum
    are exercised by multiple handlers and are confirmed; everything marked `partial` / `draft`, and
    every *wire VALUE meaning*, should be re-checked against a real CharSpawn / 5-53 capture before
    an engineer hard-codes it. The 5/53 vitals and 5/13 movement handlers are runtime-table-dispatched
    (their dispatch table is null at static time), so their live writes are debugger/capture-only.
11. **+0x488/+0x48C context-dependent reuse (soft).** The live path stores `last_state_ms` (int32) at
    +0x488; the char-preview path writes +0x488 and +0x48C as a 70.0f float pair (an AoE / cell
    working value). Both readings are valid in their own path — recorded as a context-dependent
    overlay, not a hard conflict.
12. **RESOLVED (re-verified in IDA, CYCLE 7) — the locked battle target is NOT an actor field.**
    An earlier CYCLE 7 note cited a "locked / picked battle target id at Actor +444". That reading is
    **REFUTED**: there is **no actor-side +444 battle-target field**. The death handler's control flow
    settles it against the binary — the locked / current **battle** target is owned by the
    **battle-controller singleton**, not the actor:
    - controller **+9** (dword index) = the locked battle target's **id** (dword).
    - controller **+40** = the locked battle target's **sort** byte.

    On an actor's death the handler compares `controller_target_id == dying_actor.id` **and**
    `controller_target_sort == dying_actor.sort`, and on a match zeroes both — i.e. the lock is cleared
    only when the dying actor *is* the currently locked battle target. This matches the combat dossier's
    "BC+9 / BC+40" reading. See **`specs/combat.md`** for the battle-controller state model.

    The actor's own **UI / current `target_id` at +0x6E8** (`confirmed`) is a **distinct** field and is
    untouched by this resolution: the two targets are different things held on different objects — one on
    the battle-controller singleton (the locked battle target), one on the actor (the UI/current target).
    No actor `world_pos` (+0x444) reading is affected; there is no longer any "+444" battle-target
    candidate on the actor in any table.
