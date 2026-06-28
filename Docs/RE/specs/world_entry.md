<!--
verification: confirmed (the enter-world opcode ladder 1/9 -> 3/5 -> 4/1, the single in-flight
  latch set/clear ownership, the 4/1 form-selector branch and world-entry materialize, the 4/4 tag
  loop, the 2/112 ENABLE on case-5 entry, and the one-persistent-socket continuation are all
  control-flow-confirmed on build 263bd994; the region-index -> zone-type resolution at world entry,
  the data-driven dungeon cell-list, and the destination-only no-navmesh initial movement model are
  static-control-flow-confirmed on IDB SHA 263bd994, CYCLE 7 (2026-06-20));
  static-hypothesis (the scene sub-state numbers attached to the join/enter steps);
  capture/debugger-pending (every concrete VALUE marked live-pending (6-D) in the closing section --
  the AreaId sent, the calendar-vs-epoch Year question, the 4/1 trailing-scalar region, the
  movement value->animation mapping, the 4/4 overlay code semantics, the real-wire keepalive
  cadence, which UI control fires 1/0 vs 2/0 and where the socket closes, the per-area/per-dungeon
  cell counts that live in each VFS .lst, and the data-driven walk/run speeds).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-27  # CYCLE 14 re-anchor: confirmatory — 1 re-confirmed SAME; prior: 2026-06-24 (CYCLE 12 / Phase 3 -- 4/1 interior tables (roster A, scene-entity B, hotbar init) + 4/4 892B actor-record framing folded in (263bd994); 2026-06-24 audit: Cmsg_KeepaliveToggle_Send tri-valued arg convention documented (§2.5))
evidence: [static-ida]
sample_verified: false
note: CYCLE 7 (2026-06-20) -- folded the world-entry-relevant region/zone-state resolution and the
  initial movement/position model (region-index 0..31 -> zone-type at region record +0x28; data-driven
  dungeon cell-list d<NNN>.lst; no-navmesh straight-line + reactive collision, server-authoritative
  position). Full region binary layout owned by formats/region_grid.md; movement detail by
  specs/world_systems.md; death/respawn by specs/world_exit.md. (IDB SHA 263bd994, CYCLE 7 (2026-06-20))
-->

# World Entry — Master Enter-World Lifecycle Specification

> **Clean-room neutral spec.** Promoted from dirty-room analyst notes under **EU Software Directive
> 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It contains **no
> decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual addresses**.
> Packet-body byte offsets are neutral wire-layout facts and are allowed; they are owned by the
> per-packet YAMLs cited inline.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/world_entry.md`
>
> **Purpose.** This is the **single barrier document for the enter-world flow** — the lifecycle the
> world-scene engineers build the "go into the game world" path from. It threads the complete chain
> from the client's enter-game request through the world-entry carrier packet to the in-world loop,
> in strict order, and names exactly which step owns which side effect (the in-flight latch, the
> heartbeat suppression, the scene advance, the area cold-start, the keepalive toggle). Deep field
> detail lives in the cited satellite specs and packet YAMLs; this document summarises and
> cross-links. Read this file first, then follow the cross-links for the precise bytes.
>
> **Scope boundary.** This spec covers **entry** only — the path that takes the player from
> char-select into the rendered world. The matching teardown (leave-world / logout) is owned by
> `specs/world_exit.md` and is summarised here only at the lifecycle's far end (§3.4).

---

## Table of contents

1. [Lifecycle overview](#1-lifecycle-overview)
2. [The lifecycle, step by step](#2-the-lifecycle-step-by-step)
   - 2.1 `1/9` CmsgEnterGame — request + latch arm
   - 2.2 `3/5` SmsgEnterGameAck — scene advance to Load
   - 2.3 `4/1` SmsgGameStateTick — the world-entry carrier
   - 2.4 `4/4` SmsgAreaEntitySnapshot — actors + overlays
   - 2.5 `2/112` ENABLE — keepalive toggle on
   - 2.6 In-world — the persistent connection continues
3. [Subsystems cross-reference](#3-subsystems-cross-reference)
4. [live-pending (6-D) — value-only unconfirmed items](#4-live-pending-6-d--value-only-unconfirmed-items)
5. [Cross-reference map](#5-cross-reference-map)

---

## 1. Lifecycle overview

Entering the world is a **fixed ordered ladder of one outbound request and three inbound packets**,
all carried on the **single persistent opcode connection** (socket A of
`specs/connection_topology.md §1` — there is no second connect, no "world server" socket; the World
Server's major-4 / major-5 traffic rides the same socket the login used). Char-select hands off into
this ladder; the world handler is built by the scene state machine; the player ends up in the
in-world loop.

```
[Char Select, scene state 4]
        |
        |  user confirms enter-game
        v
(C2S) 1/9 CmsgEnterGame ............ SETS the single global in-flight latch
        |                            (latch suppresses the idle heartbeat while
        |                             the char-mgmt handshake is outstanding)
        v
(S2C) 3/5 SmsgEnterGameAck ......... drives the scene -> Load state
        |                            (NO latch role: does not set or clear it)
        v
[scene state machine arms state 5, builds the world handler]
        |
        |  on case-5 entry, BEFORE the in-world loop:
(C2S) 2/112 ENABLE ................. keepalive software toggle ON
        |                            (fired by the scene state machine, NOT by 4/1)
        v
(S2C) 4/1 SmsgGameStateTick ........ the WORLD-ENTRY CARRIER:
        |                            1) CLEARS the in-flight latch (first action)
        |                            2) un-suppresses the periodic idle heartbeat
        |                            3) branches on a FormSelector byte
        |                            4) on world-entry form: reads AreaId, cold-starts
        |                               the area by its 3-digit decimal directory,
        |                               reads SpawnX/SpawnZ, copies world tables A+B
        |                               and the hotbar slots into globals
        v
(S2C) 4/4 SmsgAreaEntitySnapshot ... tag loop populates actors + overlays
        |                            (tags {0,1,2,3,4,6,9})
        v
[In-world] ........................ persistent socket A continues to carry all
                                     opcode majors 0-5 for the whole session
```

> **Order note.** `2/112` ENABLE is owned by the **scene state machine on case-5 entry** and is
> therefore arranged *before* the world handler runs its loop; `4/1` arrives on the wire as a server
> push and is what actually materialises the world. The two are independent mechanisms (§3.2):
> `2/112` is the in-world software toggle; the periodic idle heartbeat that `4/1` un-suppresses is a
> separate timer. Do not conflate them.

---

## 2. The lifecycle, step by step

### 2.1 `1/9` CmsgEnterGame — request + latch arm

The client requests world entry by sending **C2S `1/9` CmsgEnterGame** (the enter-world request the
char-select confirm path emits). Sending it **SETS the single global in-flight latch**.

The latch is the protocol's only pending-request primitive — there is no request-id anywhere; a
single global boolean on the net-client singleton is armed by the outbound char-management builders
and its **sole consumer is the keepalive timer**, which suppresses the idle heartbeat while a
char-management request is outstanding (so the keepalive does not fire on top of an in-flight
request). The latch never selects a handler and never matches a response.

- *Latch ownership for this ladder:* the `1/9` send **arms** the enter-game latch; **`4/1` clears
  it** (§2.3), not `3/5`. See `specs/net_contracts.md §1.3` (the single in-flight latch) and
  `specs/handlers.md` (Group I — in-flight latch census).

### 2.2 `3/5` SmsgEnterGameAck — scene advance to Load

The server replies with **S2C `3/5` SmsgEnterGameAck**, which **drives the scene to the Load state**.
This is a **mid-ladder scene advance** and nothing more:

- It has **NO latch role** — it does **not** set the latch and does **not** clear it. (The latch set
  by `1/9` stays armed across `3/5` and is cleared only by `4/1`.)
- It carries the session token / acknowledgement that lets the scene state machine advance and begin
  bringing in the world. See `specs/client_workflow.md §4.3` (login → Load transition) and
  `specs/net_contracts.md §1.3`.

The scene state machine then arms the in-game state and builds the world handler (the case-5 build
sequence in `specs/client_workflow.md §5.4.1`), which fires `2/112` ENABLE (§2.5) before its loop.

### 2.3 `4/1` SmsgGameStateTick — the world-entry carrier

**S2C `4/1` SmsgGameStateTick is the world-entry carrier** — the packet that actually materialises
the world. Field layout is owned by **`packets/4-1_game_state_tick.yaml`**; handler behaviour by
**`specs/handlers.md §4/1`**. In order, the handler:

1. **CLEARS the in-flight latch — its very first action**, unconditional, *before* the form branch.
   This is what releases the enter-game latch armed by `1/9` (§2.1). `3/5` did not do this.
2. **Un-suppresses the periodic idle heartbeat** — it clears the heartbeat suppress flag so the
   periodic idle heartbeat (mechanism (a) of §3.2) resumes. This is a **separate** flag from the
   `2/112` software toggle (§2.5).
3. **Branches on a FormSelector byte** (the leading body byte). One form is a lightweight
   position / status update of the existing world (no spawn, no terrain re-init, early return); the
   other is the **world-entry full-materialize** form, which performs the steps below.
4. On the **world-entry form**, it:
   - reads **AreaId** at **body offset 12** and **cold-starts the area** by its **3-digit decimal
     directory** (the AreaId rendered as a zero-padded 3-digit decimal selects the area directory —
     §3.1);
   - reads **SpawnX** and **SpawnZ** (the world ground height is **not** carried — it is resolved
     later by terrain sampling at the spawn position);
   - **copies the two world-entry tables (A and B)** and the **hotbar slots** into their respective
     globals.

The packet also carries **date/time fields** (a calendar/clock block). These feed **only the world
clock / day-night driver** — they do **not** participate in the map-directory selection. (Earlier
notes that the area was chosen by date/time are superseded: the AreaId at body offset 12 is the map
selector; the date/time block drives the clock.)

> **Re-entry vs first entry.** The world-entry form distinguishes first entry from re-entry by
> whether the local-player pointer is already set; both paths run through this one handler — there is
> no separate spawn opcode. See `specs/handlers.md §4/1` and `specs/client_workflow.md §5.4.2`.

> **Camera follow target.** Materialising the world is necessary but not sufficient for a working
> camera: the local player actor must also be staged in the camera follow-target slot, or the world
> renders nothing. That mechanism is owned by `specs/client_workflow.md §5.4.7` and
> `specs/camera_movement.md §A.4.1`; it is noted here only as a workflow dependency.

### 2.3a `4/1` interior tables — roster, scene-entity slots, hotbar init — CONFIRMED

The three interior blocks the `4/1` world-entry form copies into globals (step 4, last bullet of
§2.3) are no longer opaque. Their internal record strides and roles are recovered from the consumers
the form runs immediately after each copy (two stale-slot sweeps and the verbatim hotbar copy);
control-flow-confirmed and counter-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3). Byte offsets
are owned by **`packets/4-1_game_state_tick.yaml`**; this is the behavioural summary.

- **World-entry table A — the roster table.** A flat array of **16-byte** records (capacity 193; the
  world-entry sweep walks 120). Each record carries an **actor id** and a **keep-guard** value; the
  sweep evicts a slot only when its keep-guard is 0 *and* its referenced actor's stale flag is set.
  The keep-guard value doubles as a displayed member number — this table feeds the roster/label panel.

- **World-entry table B — the scene tracked-entity / actor-slot table.** A **heterogeneous** block,
  not one flat array: a **240 × 16-byte** record array (same record shape and the same eviction
  predicate as table A) followed by a small unswept gap, a **21 × 8-byte** category-entry tail list
  (each entry pairs a category code with a value), and a 16-byte world-target selection record. This
  is the table the scene uses to track the actors/objects present in the active area; party and
  spawn-group queries also read from it. Role detail in `specs/world_systems.md`.

- **Hotbar init — the quick-slot bar seed.** The 1920-byte hotbar block is copied **verbatim** into
  the hotbar global, restoring the player's quick-slot bar on entry. It is **240 slots of 8 bytes**.
  Each slot carries an **entry key** (a value of 0 marks an empty slot) and a **count** (quantity or
  charge). There is **no inline type byte** — whether a slot is a skill or an item is decided by
  looking its entry key up in the skill/action catalogue and reading that catalogue record's category
  field (category value 5 means skill; the meaning of the other non-skill families is data-driven and
  capture-pending). The in-game hotbar therefore initialises directly from this `4/1` block; an
  engineer rebuilds it as a fixed 240-slot array of `{ entryKey, count }` and resolves the slot kind
  via the skill catalogue, not from any wire flag.

The eviction predicate shared by tables A and B: a slot is cleared only when its id is non-zero, its
referenced actor resolves, that actor's stale flag is set, and its keep-guard is 0. (The concrete
meaning of a category code, a hotbar non-skill family, or a keep-guard value beyond the membership
display is a data-driven VALUE detail, capture-pending — not blocking the layout.)

### 2.4 `4/4` SmsgAreaEntitySnapshot — actors + overlays

**S2C `4/4` SmsgAreaEntitySnapshot** populates the area's actors and overlays. After a short area
header (a flag byte, a viewer entity id, an area grid id, and an area-center Z/X pair — only the two
center floats are used, to recenter the actor grid) the body is a **tag loop** that runs until a zero
tag. Each iteration reads a **tag byte** and dispatches on it. Handler behaviour is owned by
**`specs/handlers.md §4/4`**; the ground-item sub-record by
**`packets/4-4_ground_item_tag4.yaml`**.

| Tag | Meaning |
|----:|---------|
| 0 | loop terminator (zero tag ends the stream) |
| 1 | actor record |
| 2 | actor record (second class) |
| 3 | actor record (third class) |
| **4** | **ground item** (a 24-byte sub-record handed to the world-item spawner — `packets/4-4_ground_item_tag4.yaml`) |
| 6 | **guild-name overlay** |
| 9 | **title / relation overlay** |

The tag set carried by the loop is **{0,1,2,3,4,6,9}**. The overlay tags (6 = guild-name, 9 =
title/relation) attach display strings/flags to an actor already introduced by an actor tag. The
**value semantics** of the overlay codes are not yet pinned — see §4.

> **Area-snapshot framing + records (CYCLE 12 / Phase 3).** The full `4/4` framing — a 17-byte area
> header (only its two center floats are used, to recenter the actor grid) then this tag loop — is
> owned by **`packets/4-4_area_entity_snapshot.yaml`**. The actor record carried by tags **1/2/3** is
> a fixed **892-byte** record (an 8-byte prefix + the 880-byte SpawnDescriptor + a 4-byte trailer);
> its full field table and the key point — the prefix has **no sort dword**, so the actor sort comes
> from the **tag byte** (tag 1 ⇒ player), with the composite key being (actor id from the prefix,
> sort from the tag) — are owned by **`structs/actor.md`** (the 4/4 area-actor record section). The
> tag-9 record is a 24-byte actor-state update, distinct from the tag-4 ground item of the same length.

### 2.5 `2/112` ENABLE — keepalive toggle on

The in-world keepalive **software toggle is ENABLED by the scene state machine on case-5 entry, just
before the in-world loop runs** — it is **NOT** fired by `4/1`. This is the in-world heartbeat
software switch (mechanism (b) of §3.2). The matching **DISABLE** is sent on the leave-world / logout
path (§3.4). See `specs/client_workflow.md §5.4.1` (step 0 of the world build) and
`specs/client_workflow.md §4.4` (the leave/logout DISABLE).

This `2/112` toggle is **independent** of the periodic idle heartbeat whose suppress flag `4/1`
clears (§2.3): the toggle is enabled by the scene machine; the suppress flag is cleared by the
packet handler. A faithful port must wire both, separately.

> **`Cmsg_KeepaliveToggle_Send` is tri-valued.** The underlying send routine accepts an argument
> that selects one of three behaviours — it is **not** a simple boolean enable/disable:
>
> | Argument | Behaviour |
> |---|---|
> | **1** | **Enable** — set `g_KeepaliveEnabled = 1`, then transmit the `2/112` frame immediately |
> | **2** | **Disable** — clear `g_KeepaliveEnabled` (no transmission; suppresses if already off) |
> | **other** | **Conditional send** — transmit the `2/112` frame **only if `g_KeepaliveEnabled` is already 1** (the periodic keepalive poll path); does not change the flag |
>
> The scene state machine calls with arg **1** on case-5 entry (this section). The leave-world /
> logout path calls with arg **2** (§3.4). The periodic keepalive timer calls with a non-1/non-2
> argument to send the heartbeat without changing the toggle state. A faithful port must model all
> three paths; do not reduce this to a boolean set/clear. *([confirmed]* static control-flow,
> build 263bd994.)*

### 2.6 In-world — the persistent connection continues

The player is now in the world. The **single persistent opcode connection** (socket A) continues to
carry **all opcode majors 0–5** for the whole session — game actions (major 2), char-mgmt/chat
(major 3), the World Server Response/Push families (majors 4 and 5), plus both keepalives and the
`5/146`→`2/146` link-health ack. There is **no reconnect** on entering the world and **no second
socket**: char-select → enter-world rode the already-open socket. See
`specs/connection_topology.md §1` (one persistent opcode connection) and §2 (the game/world socket).

---

## 3. Subsystems cross-reference

This section ties the four enter-world subsystems together. Each is owned in depth elsewhere; this
is the orientation map.

### 3.1 Area-load chain — AreaId → 3-digit directory → area cold-start

On the `4/1` world-entry form, the **AreaId at body offset 12** is rendered as a **zero-padded
3-digit decimal** to select the area's on-disk directory; the handler **cold-starts** that area,
which brings in its environment/terrain set and triggers the **terrain restream** (the first-ring
load centred on the spawn position read from the same packet). The date/time fields feed the world
clock, never this directory selection.

- The world-entry form's read of AreaId / SpawnX / SpawnZ and the cold-start call:
  `packets/4-1_game_state_tick.yaml`, `specs/handlers.md §4/1`.
- The terrain first-ring load + cell streaming centred on the spawn: `specs/client_workflow.md
  §5.4.2` and `specs/resource_pipeline.md §terrain_streaming`.

> **Per-area cell count is data-driven — there is no hard-coded cell count, and no special case for
> dungeons.** CONFIRMED. The area cold-start opens the area's per-area cell list from
> `data/map<NNN>/dat/d<NNN>.lst`, whose **leading `u32` is the cell count**, followed by that many
> `u32` cell keys (`[u32 count][count × u32 cell-key]`; equivalently `count = (filesize − 4) / 4`).
> The loader registers one cell per key. The same AreaId-string path and the same five per-area file
> families (cell list, map-option block, region table, region grid, NPC array) serve **both overworld
> areas and the dungeon area-ids 201–210** — there is **no `≥ 201` branch** and **no per-dungeon
> constant** anywhere in the loader. Dungeon areas differ from overworld areas only in their **data**
> (a usually smaller declared cell set, a different map-setting record, and whatever grid
> dimensions/origins their region file declares), never in their load path. Any implementation that
> asserts a fixed dungeon cell count is wrong; the count must be read from each `.lst`. The concrete
> per-area / per-dungeon counts live in the VFS and are **capture/sample-pending (6-D)** — they
> cannot be derived from the binary. The `.lst` byte format is owned by `formats/region_grid.md`
> (or the cell-list format note it cross-references); this spec only records that the count source is
> the file, not a constant.

### 3.1a Region / zone state resolved at world entry — CONFIRMED

When the area cold-starts (§3.1), the per-area **region grid** and **region table** are loaded
alongside the terrain, and the actor's initial **zone-type / combat-mode** is resolved from them.
This is the world-state the entry path establishes before the in-world loop runs; the **full binary
layout of the region grid and region table is owned by `formats/region_grid.md`** and is only
summarised here at the level the entry flow consults it.

- **The map cells carry only a region INDEX, not the zone-type.** The per-area region grid maps a
  world `(X, Z)` position to a single-byte **region index in the range 0..31**. The zone-type is
  **not** stored on the cell — the index is a lookup key into the region table.
- **The zone-type lives on the region-table record.** The region table is a fixed block of
  **32 records × 48 bytes**. The region index selects a record; that record's **`u32` at field
  offset `+0x28`** is the **zone-type**:

  | Zone-type (record `+0x28`, `u32`) | Meaning | Effect at / after entry |
  |----------------------------------:|---------|--------------------------|
  | 0 | SAFE (town / peace) | no PvP; combat-mode resolves to safe |
  | 1 | PVP (free-fight) | PvP permitted; combat-mode opens the player-attack path |
  | 2 | CLOSED (restricted) | movement into such a cell is blocked/limited |

  A value of `9` appears in some sample data with no distinguishing branch in the resolver (it falls
  into the "nonzero, not 1" → restricted bucket); treat it as **UNVERIFIED / sample-only**.
- **The combat-mode is the OR of two region records.** The active combat-mode is resolved from
  **two** region-table records — the actor's cached current region and the region looked up at the
  actor's live world `(X, Z)` — taking the zone-type of each (defaulting to PVP when a record pointer
  is absent). If **either** endpoint is zone-type 1 the result is **PvP (1)**; else if both are
  nonzero the result is **CLOSED (2)**; otherwise **SAFE (0)**. PvP "wins" a boundary crossing. This
  resolved combat-mode is what later gates player-vs-player attacks. The gameplay detail (the
  attack/movement gates and the map-wide peace-policy layer) is owned by `specs/world_systems.md`;
  this spec records only that entry establishes the region/zone state from these two structures.
- **Region grid scale is distinct from the terrain grid.** The region grid uses a **256-world-unit
  cell stride** with its own width/height/origins read from the per-area region file — this is a
  coarse overlay and **must not be confused with the 1024-unit, 65×65 terrain cell grid** the world
  geometry uses. An out-of-bounds region lookup resolves to region index 0. Full constants and the
  `[u32 width][u32 height][width·height u8 region-index][i32 originX][i32 originZ]` body layout are
  owned by `formats/region_grid.md`.

### 3.1b Initial movement / position model — CONFIRMED

The position state the actor enters the world with comes from the `4/1` SpawnX / SpawnZ (§2.3, ground
height resolved by terrain sampling at the spawn). From the first in-world frame onward the client
moves the local player under a **local-prediction-with-server-reconciliation** model — there is **no
precomputed navmesh and no client-side path graph**:

- **No navmesh / no A\* / no waypoint graph.** CONFIRMED (no such code or strings exist). Movement is
  **straight-line toward the destination** with **reactive collision** only — the recovered `.sod`
  XZ wall segments (ray-parity point-in-polygon) and the `.ted` bilinear ground-height snap (the same
  collision/ground chain already noted in CLAUDE.md and `specs/world_systems.md`). The client follows
  a single pending destination directly; any real routing is the server's concern.
- **The server is authoritative for position.** The client predicts its own motion locally and
  *announces* heading + destination + run-state to the server; the server pushes authoritative actor
  positions back, which the client absorbs as interpolation for small drift and as a hard snap once
  drift grows large. The outbound periodic move emitter (`2/13`) and the inbound authoritative
  position push (`5/13`) — and the full reconciliation bands — are owned by `specs/world_systems.md`;
  they are **not** restated here. Entry only needs to establish that the world starts the actor at the
  `4/1` spawn and that position authority lives on the server from frame one.

The concrete walk/run speeds (resolved from the actor's motion record, not inline constants) and the
movement-value → animation mapping are **capture/data-pending (6-D)**.

### 3.2 Three-mechanism keepalive subsystem

Three independent link-keepalive mechanisms run during a session; two of them are touched by the
enter-world ladder:

| Mechanism | What it is | Touched by enter-world |
|-----------|-----------|------------------------|
| (a) periodic idle heartbeat | a timer-driven idle heartbeat, suppressed while the in-flight latch is armed | `4/1` **un-suppresses** it (clears the suppress flag) on world entry (§2.3) |
| (b) `2/112` software toggle | the in-world keepalive software switch | the scene state machine sends **ENABLE** on case-5 entry (§2.5); the leave/logout path sends **DISABLE** (§3.4) |
| (c) link-health ack | the `5/146`→`2/146` reactive acknowledgement | rides the persistent socket like every other major (§2.6) |

Detail owner: `specs/network_dispatch.md` (keepalives) and `specs/client_workflow.md §6.4.1`.

### 3.3 The single global in-flight latch

There is exactly one pending-request primitive: a **single global boolean in-flight latch** on the
net-client singleton (no request-ids exist). It is armed by the outbound char-management builders and
its **only consumer is the keepalive timer**, which suppresses mechanism (a) while a request is
outstanding. For this ladder: **`1/9` arms it; `4/1` clears it (first statement); `3/5` does not
touch it.** Detail owner: `specs/net_contracts.md §1.3` and `specs/handlers.md` (Group I).

### 3.4 World exit — the far end of the lifecycle

Leaving the world is the matching teardown, owned by **`specs/world_exit.md`**. Two outbound
opcodes, both header-only (8 bytes), both eventually converging on **scene state 6, sub-state 8**
(the return-to-menu scene state):

| Opcode | Name | Disarms `2/112`? | In-flight latch | Character |
|-------:|------|:----------------:|-----------------|-----------|
| **1/0** | CmsgLogout | no | sets none (fire-and-forget; server expected to drop the session) | the in-world quit |
| **2/0** | CmsgLeaveWorld | **yes** (disarms the `2/112` keepalive toggle first, then sends `2/0`) | — | the guarded leave-world transition with heavier UI/object teardown |

Both paths converge on scene state 6 / sub-state 8. See `specs/world_exit.md §1` (the two opcodes
and their teardown), §1.2 (the `2/112` DISABLE before `2/0`), and §2 (convergence on state 6 /
sub-state 8).

---

## 4. live-pending (6-D) — value-only unconfirmed items

Every item below is a **VALUE** that the static control-flow does not settle; the *behaviour* and
*layout* around each are confirmed (cited above). Each requires a real network capture or a live
`?ext=dbg` debugger trace of an actual login → char-select → enter-world cycle to pin. Implement the
confirmed structure now; treat these values as tunable until captured.

- **Concrete AreaId sent** — the actual AreaId value carried on a real `4/1` world-entry form, **and**
  the confirmation that its zero-padded 3-digit decimal directory resolves to a real `<id>.lst` on
  disk. live-pending (6-D).
- **Year field meaning** — whether the date/time block's Year field is a **calendar year** or an
  **epoch index**. live-pending (6-D).
- **`4/1` trailing-scalar region** — the meaning/labelling of the trailing scalar region of the
  `4/1` body (past the named world-entry fields). live-pending (6-D).
- **Movement value→animation mapping** — the value→animation mapping for the movement/state fields
  (which value drives which actor animation). live-pending (6-D).
- **`4/4` overlay code semantics** — the value semantics of the overlay codes in the tag loop:
  specifically the **tag-6** overlay's `+0x04` byte, and the **tag-9** overlay's **RelationState**
  and **OverlaySubCode** values. live-pending (6-D).
- **Real-wire keepalive cadence** — the actual on-wire cadence of the idle heartbeat (mechanism (a))
  and the `2/112` toggle traffic. live-pending (6-D).
- **World-exit UI binding + socket close** — which concrete in-game UI control fires **`1/0`** vs
  **`2/0`**, and **whether / where the socket actually closes** on the `1/0` fire-and-forget path.
  live-pending (6-D). (Owned by `specs/world_exit.md` open items.)
- **Per-area / per-dungeon cell counts** — the concrete cell count for each area (overworld and
  dungeons 201–210) declared in the leading `u32` of its `data/map<NNN>/dat/d<NNN>.lst`. The structure
  is confirmed; the values live in the VFS and must be read per file. capture/sample-pending (6-D).
- **Zone-type `9`** — whether the `9` value seen in some region-record sample data is a real distinct
  zone-type (no resolver branch distinguishes it). capture/sample-pending (6-D).
- **Walk / run speeds + movement→animation mapping** — the per-actor walk/run speeds (resolved from
  the motion record, not inline) and the movement-value → animation mapping. capture/data-pending (6-D).

---

## 5. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The enter-world lifecycle order + each step's side-effect ownership (§1–§2) | the per-step satellites below |
| `1/9` request + latch arm (§2.1) | `specs/net_contracts.md §1.3`, `specs/handlers.md` (Group I) |
| `3/5` scene advance to Load (§2.2) | `specs/client_workflow.md §4.3` |
| `4/1` world-entry carrier — fields (§2.3) | `packets/4-1_game_state_tick.yaml` |
| `4/1` world-entry carrier — handler behaviour, latch clear, form branch, materialize (§2.3) | `specs/handlers.md §4/1`, `specs/client_workflow.md §5.4.2` |
| `4/4` tag loop — handler (§2.4) | `specs/handlers.md §4/4` |
| `4/4` tag-4 ground-item sub-record (§2.4) | `packets/4-4_ground_item_tag4.yaml` |
| `2/112` ENABLE on case-5 entry (§2.5) | `specs/client_workflow.md §5.4.1`, `§4.4` |
| Keepalive subsystem (§3.2) | `specs/network_dispatch.md`, `specs/client_workflow.md §6.4.1` |
| The in-flight latch (§3.3) | `specs/net_contracts.md §1.3` |
| Area-load chain + terrain restream (§3.1) | `specs/resource_pipeline.md §terrain_streaming`, `specs/client_workflow.md §5.4.2` |
| Per-area cell list / dungeon cell counts data-driven via `d<NNN>.lst` (§3.1) | `formats/region_grid.md` (region/cell-list binary layout) |
| Region/zone state at entry — index 0..31 → zone-type at record `+0x28`; 32×48 table; 256-unit grid (§3.1a) | `formats/region_grid.md` (full layout + constants), `specs/world_systems.md` (zone gating + combat-mode) |
| Initial movement/position model — no navmesh, straight-line + reactive `.sod`/`.ted` collision, server-authoritative (§3.1b) | `specs/world_systems.md` (`2/13` move emitter, `5/13` position push, reconciliation bands) |
| One persistent socket carrying all majors (§2.6) | `specs/connection_topology.md §1–§2` |
| Camera follow-target staging (§2.3 note) | `specs/client_workflow.md §5.4.7`, `specs/camera_movement.md §A.4.1` |
| World exit — `1/0` vs `2/0`, convergence on state 6 / sub-state 8 (§3.4) | `specs/world_exit.md §1–§2` |
