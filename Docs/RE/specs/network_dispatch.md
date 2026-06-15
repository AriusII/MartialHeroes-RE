---
status: hypothesis
sample_verified: false
build: 263bd994   # static-recovered on doida.exe build 263bd994 (Campaign 7 Wave B)
subsystems: [receive_dispatch, handler_table_install, netclient_lifecycle, connection_state, pipeline_placement]
networked: yes
---

# Network Dispatch & Connection Lifecycle — Clean-Room Specification

> Neutral, rewritten architectural specification, promoted from dirty-room analyst notes under
> **EU Software Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve
> interoperability). It contains **no decompiler output, no pseudo-code, no legacy symbol names,
> and no binary virtual addresses**. Every structure and behaviour below is re-expressed in this
> author's own words and tables.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/network_dispatch.md`
>
> **Scope.** This document describes the *plumbing under the handlers*: the master receive
> dispatcher, the two handler-table installers, the network-event entry and handler object, the
> network-client connection lifecycle (start / worker threads / receive loop / keepalive /
> disconnect), the connection-state machine, and where the cipher/compression stages sit in the
> send/receive pipeline.
>
> **It deliberately does not duplicate its neighbours:**
> - `opcodes.md` owns the **wire frame header** (size + major + minor layout) and the per-opcode
>   **routing catalogue** — cited here, not re-derived.
> - `handlers.md` owns the **per-handler behaviour** (what each opcode tuple does) and the
>   **dispatch model summary** (the 154-slot bound, the inline-switch enumerations, the install
>   counts) — cited here, not restated handler-by-handler.
> - `crypto.md` owns the **byte cipher and the LZ4 codec algorithms** — cited here; this spec only
>   places those stages in the pipeline.
>
> Where the three overlap, this spec adds the **machinery** (installer routines, lifecycle, the
> connection-state machine) that the other three do not cover.

---

## Status header — confidence and build delta

- **Build delta.** Every fact below is **static-recovered on build `263bd994`** (Campaign 7,
  Wave B). Earlier campaign notes targeted a different build and are treated as stale; the
  structures here were re-located by behavioural signal on the current build. Facts that are solid
  structural reads on this build are tagged **`verified on build 263bd994`**.
- **No live capture this pass.** No network capture (`.pcapng`/`.tsv`) was decoded for the
  dispatch architecture. The routing/lifecycle structure is a static read; wire-byte confirmation
  is deferred. `sample_verified: false`.
- **Connection-state code meanings are UNVERIFIED beyond the bare numeric codes.** The codes
  themselves (`201 / 202 / 203 / 232`, plus the timed-event tag `10001`) are recovered facts; the
  precise game-state semantics attached to each code are inferred and tagged `UNVERIFIED`.
- **Confidence tags used below:** `STRUCTURE-HIGH` (the routine's shape / counts / control
  surface are unambiguous on this build) · `LIKELY` (one consistent read, plausible role) ·
  `UNVERIFIED` (inferred meaning, not pinned — needs a capture or a debugger trace).

---

## 1. Master receive dispatcher

A single function is the entry point for every framed game packet that has been received,
decompressed, and is ready to act on. Its job is to read the opcode out of the frame header and
fan out to the correct family handler. *(STRUCTURE-HIGH; build 263bd994.)*

### 1.1 Frame header read

The dispatcher reads the **major opcode** and the **minor opcode** straight out of the frame
header. The header layout (total size, major, minor) is owned by `opcodes.md`, "Wire frame
header" — this spec does not re-derive it. In header-relative terms the dispatcher takes the
**major** from header offset `+4` and the **minor** from header offset `+6`, matching the
`opcodes.md` header table exactly. A full message opcode is the `(major, minor)` pair, written on
the wire as `[u16 major][u16 minor]` and reasoned about as `(major << 16) | minor`.

### 1.2 Decompress-then-route

Before routing, any compressed frame is **expanded first** (the inbound LZ4 decompress stage;
see §6 and `crypto.md`). Header-only frames (total length exactly 8, no payload — heartbeats /
keepalives) carry no payload to expand and are passed through. Only after expansion does the
dispatcher branch on the **major family**.

### 1.3 Routing fan-out by major family

The dispatcher routes on the major opcode. Two styles are used: small **inline switches** for the
low and character-management families, and **table-driven** lookups for the two high-volume
families. This agrees with the dispatch model summarised in `handlers.md` §1 — that section owns
the per-opcode list; the structure is repeated here only to anchor the installer/lifecycle facts.

| Major family | Routing style | Notes (cross-ref `handlers.md` §1) |
|---|---|---|
| `0` (KeyExchange) | inline switch | small fixed set of minors wired |
| `1` (ServerCommand) | inline switch | only a fixed set of inbound minors is wired |
| `3` (CharacterMgmt) | inline switch | a fully enumerated set of minors (no hidden minors) |
| `4` (Response) | **table-driven** | fixed 154-slot table; unset slots are inert no-ops |
| `5` (Push) | **table-driven** | fixed 154-slot table; unset slots are inert no-ops |

- The **major-4 (Response)** and **major-5 (Push)** families are each dispatched through a
  **fixed-bound handler table of 154 slots**. A minor below the bound that was never installed
  resolves to an **inert no-op handler** (a slot pre-filled to do nothing — not a crash); a minor
  **at or above 154 is out of range and is not dispatched at all**. This caps the legal minor
  space per family at `0..153`. The per-handler enumeration of which slots are installed, and the
  recovered install counts, live in `handlers.md` §1 — see §2 below for the installer machinery
  that fills these tables.
- Two major-4 minors are routed **outside** the table by special-case branches (one shows a popup,
  one discards a text payload), per `handlers.md` §1; the machinery here is the table path.

---

## 2. The two handler-table installers

Two boot-time routines populate the Response and Push handler tables inside the network client.
They are the machinery that turns the empty 154-slot tables of §1.3 into the live routing surface
that `handlers.md` enumerates. *(STRUCTURE-HIGH; build 263bd994.)*

| Installer | Family | Recovered installed slots | Role |
|---|---|---|---|
| Response-family installer | major 4 | **~98 slots** | Wires each Response per-opcode handler into the client handler table at boot. |
| Push-family installer | major 5 | **~65 slots** | Wires each Push per-opcode handler into the client handler table at boot. |

- The two install counts (**~98 Response / ~65 Push**) are the same counts reported in
  `handlers.md` §1; they are reproduced here to characterise the installer routines, not to
  re-enumerate the handlers. Every minor not installed by these two routines keeps the inert no-op
  fill from §1.3.
- Each **installed handler** opens by reading from a **bounded receive buffer** — the handler's
  first act is a length-bounded read of its fixed payload prefix out of the inbound buffer. This
  "open with a bounded read" stereotype is what `handlers.md` records as each handler's
  *minimum fixed payload* / *fixed read* length; it is a structural property of the installed
  handler, and the bound is what makes an over-short frame safe to dispatch.
- The installers run **once at network-client boot**, before any traffic is dispatched, so by the
  time the master dispatcher (§1) runs, both tables are fully populated.

---

## 3. Network-event entry and handler object

Inbound dispatch is fronted by a **network-event entry point** and a **handler object** that owns
the dispatch behaviour. *(STRUCTURE-HIGH on construction shape; roles LIKELY; build 263bd994.)*

### 3.1 Network-event entry point

A single network-event entry point receives dispatch events (the events the receive worker posts —
see §4.4) and the connection/scene state events (§5), and routes each to the matching handling
path on the handler object. It is the convergence point between the worker-thread side (which only
*posts* events) and the dispatch side (which *acts* on them).

### 3.2 The handler object and its embedded sub-objects

A dedicated **network-handler object** carries the dispatch logic. At construction it builds two
**embedded sub-objects** that partition handling responsibility:

| Sub-object | Role (LIKELY) |
|---|---|
| Guest sub-object | The pre-authenticated / lobby-side handling surface — constructed first and wired into the handler object's table. |
| Member sub-object | The in-session handling surface — constructed second; on construction it also caches a pair of internal manager references (an actor manager and an animation catalog) into shared slots for the handlers to reach. |

The two sub-objects are constructed as part of the single handler-object construction; their exact
runtime placement inside the handler object is an in-process layout detail with no wire effect and
is not promoted here.

### 3.3 Singleton accessor pattern

The handler object is reached through a **singleton accessor**: the first caller constructs the
object (and registers its teardown for process exit), and every later caller receives the same
instance. This is the conceptual pattern; no offsets-as-addresses are recorded.

---

## 4. Network-client lifecycle

The network client is a single object that owns the connection sub-object, the worker threads, and
the init-gate. Its lifecycle is: **construct → start engine → spawn workers → run the receive loop
→ keepalive → disconnect**. *(STRUCTURE-HIGH; build 263bd994.)*

### 4.1 Construction

Constructing the network client creates an **embedded connection sub-object** (the connection /
transport spine) and clears an **init-gate** byte. The init-gate is a single boolean-style guard
that records whether the network engine has been started successfully; it is read by the accessors
and cleared on disconnect (§4.6).

### 4.2 Start the network engine

Starting the network engine performs the Windows sockets bring-up and arms the worker threads:

| Step | Detail |
|---|---|
| Winsock startup | Requests Windows Sockets version **`0x0202`** (decimal **514**, i.e. 2.2). |
| Version check | Verifies the negotiated `WSAData` version is **514**; on a version mismatch it tears down winsock (cleanup) and does **not** proceed. |
| Set init-gate | On success, sets the init-gate byte (§4.1) so the rest of the client treats the engine as up. |
| Spawn workers | Calls the worker-thread spawn routine (§4.3). |

### 4.3 Worker-thread spawn

The spawn routine arms the threading machinery and is **called only by start-engine** (§4.2):

- Creates a **manual-reset stop event** that the worker loops watch to know when to exit.
- Arms **two "alive" flags**, one per worker slot.
- Starts **two worker thread slots** — the **receive worker** (§4.4) and a **second worker**
  (the connection's I/O-side worker).
- Applies a **thread priority** to each started thread (a generic "set priority if the handle is
  valid" step, not network-specific).

### 4.4 The receive-worker loop

The receive worker is a thread procedure (tagged with a "network worker start/end" marker) that
runs until the stop event is signalled. Each iteration: *(STRUCTURE-HIGH; build 263bd994.)*

1. **Pop** one packet from the **connection's receive queue** (the queue owned by the embedded
   connection sub-object).
2. **Push** a **dispatch event** onto the network-event sink (§3.1) so the dispatcher (§1) will act
   on that packet on the consuming side.
3. **Dispose** the packet: the dispatched inbound packet buffer is freed (an empty no-op hook on
   the buffer-free path, then the raw block free). Disposal is the worker's responsibility once the
   dispatch event has been posted.

So the worker thread never decodes packets itself — it is a pure producer that moves a received
packet into a dispatch event and then releases the buffer; all decode/routing happens on the
dispatcher side.

### 4.5 Keepalive

The client arms a **periodic compressed keepalive**: a supplied keepalive packet is run once
through the outbound compress stage (§6), the compressed buffer is stored in a client slot, the
original buffer is released if a new one was produced, and a **periodic interval** is recorded
(the recovered interval is `1000 ×` the routine's argument — i.e. a per-second-scaled period).
Thereafter the pre-compressed keepalive frame is sent on that interval. *(LIKELY; build 263bd994.)*

### 4.6 Disconnect

Disconnect is the connection-teardown path:

| Step | Detail |
|---|---|
| Connection shutdown | Shuts down the embedded connection sub-object: signals its own stop event, waits its I/O thread, tears down and closes the socket, and closes its event/thread handles. |
| Post disconnect event | Builds a **disconnect/close network event** (a code-`15` close event via the event factory) and pushes it onto the dispatch sink so the rest of the client learns the connection is gone. The same post-disconnect path is also used by the receive worker. |
| Clear init-gate | Clears the init-gate byte (§4.1), so the client no longer treats the engine as up. |

---

## 5. Connection-state machine

Separate from the per-packet handlers, there is a **connection/scene state machine** driven off the
network-event entry (§3.1). It handles a **connection-state event** (recovered as the
"command-102" path) and drives connection/scene transitions **with no packet-body field reads** —
its inputs are **timed events and numeric state codes**, not wire payload fields. This is why it is
a *state machine*, not a packet handler. *(STRUCTURE-HIGH on "no body reads"; code meanings
UNVERIFIED; build 263bd994.)*

### 5.1 Inputs

- A **timed-event tag `10001`** drives the time-based transitions (e.g. a connection-attempt
  timeout / retry tick). *(LIKELY role; UNVERIFIED meaning.)*
- A small set of **numeric state codes** select the transition. These codes are the recovered
  facts; their precise game-state meaning is inferred.

### 5.2 Recovered state codes

The following numeric codes drive the transitions. **The numbers are recovered facts
(`verified on build 263bd994`); the meanings are an inferred neutral grouping and are
`UNVERIFIED`** — do not hard-code semantics beyond the numeric codes until a capture or a debugger
trace pins them. Model them as a neutral connection-state enum:

| State code | Inferred class (UNVERIFIED) | Notes |
|---|---|---|
| `201` | connecting / connection in progress | first of the contiguous `20x` group; exact meaning not pinned |
| `202` | connected / connection established | meaning inferred from ordering, not confirmed |
| `203` | disconnected / connection lost | meaning inferred from ordering, not confirmed |
| `232` | error / failure class | a separate (non-`20x`) code; treated as an error/abort outcome — UNVERIFIED |
| `10001` | timed-event tag (not a state) | the time-based driver above, listed for completeness |

> **Cross-reference:** the same family of select-screen action codes (including `201`, `203`,
> `232`) surfaces in `handlers.md` §12 (3/100 `SmsgCharActionResult`), which maps action/result
> codes to lobby outcomes. That handler is a **packet** handler keyed on a body code; the routine
> documented here is the **connection-state** path keyed on internal state codes with **no body
> read**. They are distinct paths that happen to share some numeric code values — do not conflate
> them.

### 5.3 What it is not

This routine is **not** a packet-body handler: it reads no payload fields, takes no actor key, and
does not appear in the 154-slot Response/Push tables. It is the connection/scene lifecycle driver
that reacts to connect/disconnect/timeout conditions and the disconnect event posted in §4.6.

---

## 6. Compression and cipher placement in the pipeline

This spec only **places** the cryptographic stages in the dispatch pipeline. The algorithms — the
keyless byte cipher and the raw-block LZ4 codec — are owned by `crypto.md` and are **not** restated
here. *(STRUCTURE-HIGH on placement; build 263bd994.)*

### 6.1 Outbound (client → server), before the queue

An outbound packet passes through, in order:

```
plaintext payload
   → byte cipher          (crypto.md §3.1 — keyless byte transform over the payload)
   → LZ4 compress         (crypto.md §3.2 — raw-block LZ4 over the post-header payload)
   → enqueue → send
```

The send-chain convergence point (the single routine ~all send sites flow through) runs the cipher
gate, then the compress stage (swapping in the compressed buffer and freeing the original), then
hands the frame to the queue/transport writer. **Header-only frames bypass both stages.**

### 6.2 Inbound (server → client), before dispatch

An inbound packet is **LZ4-decompressed before dispatch** (the expand step of §1.2), then handed
to the master dispatcher. **Header-only frames bypass decompression.** Per `crypto.md` §5, this
client's *receive* path applies **no inverse byte cipher** — inbound is decompress-only before
dispatch (this asymmetry is a `crypto.md`-owned, capture-gated open question; it is noted here only
to place the stage).

### 6.3 Pipeline diagram

```
RECEIVE:  socket → conn recv queue → [recv worker pops] → dispatch event
                 → [LZ4 decompress if not header-only]   (crypto.md §3.2 inverse)
                 → master dispatcher (§1) → family routing → installed handler (§2)

SEND:     handler builds payload → byte cipher (crypto.md §3.1)
                 → LZ4 compress (crypto.md §3.2) → send queue → socket
          (header-only frames pass through untouched)
```

---

## 7. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| Master dispatcher routing fan-out (§1) | Per-opcode routing list + dispatch-model summary → `handlers.md` §1; wire header → `opcodes.md` |
| Handler-table installers + install counts (§2) | Per-handler behaviour and the same counts → `handlers.md` §1 |
| Network-event entry + handler object (§3) | (machinery only here) |
| Network-client lifecycle (§4) | — |
| Connection-state machine + codes (§5) | Lobby action-code mapping (shared numbers, different path) → `handlers.md` §12 |
| Cipher/LZ4 placement (§6) | Cipher + LZ4 algorithms → `crypto.md` §3; inbound asymmetry → `crypto.md` §5 |

---

## 8. Unverified / open questions

- **No capture this pass.** The routing/lifecycle structure is a static read on build `263bd994`;
  wire-byte and timing confirmation are deferred.
- **Connection-state code semantics.** `201 / 202 / 203 / 232` and the timed tag `10001` are
  recovered numeric facts; the precise meaning attached to each (connecting / connected /
  disconnected / error) is an inferred grouping and is `UNVERIFIED`. A debugger trace of a
  connect→play→disconnect cycle would pin each code to an observed transition.
- **Keepalive interval scaling.** The `1000 ×` argument scaling (§4.5) is read from the routine;
  the actual configured argument (and therefore the wall-clock keepalive period) is not pinned.
- **Inbound cipher asymmetry.** The "decompress-only, no inverse cipher" inbound placement (§6.2)
  is structurally observed but capture-gated — see `crypto.md` §5. This spec inherits that open
  question, it does not resolve it.
- **Second worker's exact duties.** The receive worker is fully characterised (§4.4); the second
  spawned worker is the connection's I/O-side thread, but its loop is not decomposed here.
