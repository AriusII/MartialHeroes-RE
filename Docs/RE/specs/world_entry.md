<!--
verification: confirmed (the enter-world opcode ladder 1/9 -> 3/5 -> 4/1, the single in-flight
  latch set/clear ownership, the 4/1 form-selector branch and world-entry materialize, the 4/4 tag
  loop, the 2/112 ENABLE on case-5 entry, and the one-persistent-socket continuation are all
  control-flow-confirmed on build 263bd994);
  static-hypothesis (the scene sub-state numbers attached to the join/enter steps);
  capture/debugger-pending (every concrete VALUE marked live-pending (6-D) in the closing section --
  the AreaId sent, the calendar-vs-epoch Year question, the 4/1 trailing-scalar region, the
  movement value->animation mapping, the 4/4 overlay code semantics, the real-wire keepalive
  cadence, and which UI control fires 1/0 vs 2/0 and where the socket closes).
ida_anchor: 263bd994
evidence: [static-ida]
sample_verified: false
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

### 2.5 `2/112` ENABLE — keepalive toggle on

The in-world keepalive **software toggle is ENABLED by the scene state machine on case-5 entry, just
before the in-world loop runs** — it is **NOT** fired by `4/1`. This is the in-world heartbeat
software switch (mechanism (b) of §3.2). The matching **DISABLE** is sent on the leave-world / logout
path (§3.4). See `specs/client_workflow.md §5.4.1` (step 0 of the world build) and
`specs/client_workflow.md §4.4` (the leave/logout DISABLE).

This `2/112` toggle is **independent** of the periodic idle heartbeat whose suppress flag `4/1`
clears (§2.3): the toggle is enabled by the scene machine; the suppress flag is cleared by the
packet handler. A faithful port must wire both, separately.

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
| One persistent socket carrying all majors (§2.6) | `specs/connection_topology.md §1–§2` |
| Camera follow-target staging (§2.3 note) | `specs/client_workflow.md §5.4.7`, `specs/camera_movement.md §A.4.1` |
| World exit — `1/0` vs `2/0`, convergence on state 6 / sub-state 8 (§3.4) | `specs/world_exit.md §1–§2` |
