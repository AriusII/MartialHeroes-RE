---
status: confirmed-routing-capture-pending-semantics
sample_verified: false
build: 263bd994   # static-recovered on doida.exe build 263bd994 (Campaign 7 Wave B)
subsystems: [receive_dispatch, handler_table_install, netclient_lifecycle, connection_state, pipeline_placement]
networked: yes
verification: confirmed (routing opcode->handler, frame-header layout, packet read sizes, struct field offsets, table bases, install counts, pipeline-stage placement are all control-flow-confirmed) · static-hypothesis (the blanket "every installed handler opens with a bounded read") · capture/debugger-pending (every packet field VALUE SEMANTICS, the connection-state code meanings 201/202/203/232, the keepalive on-wire cadence, the inbound-cipher-omission generalised across all inbound types)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: RESOLVED this pass — (a) major-0 is a hardwired (0,0) handshake branch, NOT an inline switch (doc reworded); (b) Response install count refined ~98 -> ~99 (101 stores - 2 zero-clears); (c) keepalive duality recorded — the ctor-armed (2,10000)@20s frame AND the runtime C2S 2/112 toggle are BOTH real, neither's on-wire cadence pinned (capture-pending)
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

- **Re-verified 2026-06-16, anchor `263bd994`, evidence `[static-ida]`.** The dispatch architecture
  was re-confronted against the live IDB on the current build this pass. Routing (opcode→handler),
  the frame-header layout, packet read sizes/offsets, the two table bases, the install counts, and
  the pipeline-stage placement are all **control-flow-confirmed**. What a wire byte *means* (field
  value semantics) is NOT — it stays capture/debugger-pending.
- **Build delta.** Every fact below is **static-recovered on build `263bd994`** (Campaign 7,
  Wave B). Earlier campaign notes targeted a different build and are treated as stale; the
  structures here were re-located by behavioural signal on the current build. Facts that are solid
  structural reads on this build are tagged **`verified on build 263bd994`**.
- **No live capture this pass.** No network capture (`.pcapng`/`.tsv`) was decoded for the
  dispatch architecture. The routing/lifecycle structure is a static read; **wire-byte VALUE
  confirmation is deferred**. `sample_verified: false`.
- **Tier rule (honest scoping).** ROUTING (which `(major, minor)` reaches which handler), packet
  **sizes**, and field **offsets** are `[confirmed]` from control flow. Packet field **VALUE
  semantics** (what a given byte *means*) are `[capture/debugger-pending]` — this campaign has no
  wire. The spec deliberately does not over-claim the meaning of any wire value.
- **Connection-state code meanings are UNVERIFIED beyond the bare numeric codes.** The codes
  themselves (`201 / 202 / 203 / 232`, plus the timed-event tag `10001`) are recovered facts; the
  precise game-state semantics attached to each code are inferred and tagged `UNVERIFIED`.
- **Confidence tags used below:** `STRUCTURE-HIGH` / `[confirmed]` (the routine's shape / counts /
  control surface are unambiguous on this build) · `LIKELY` / `[static-hypothesis]` (one consistent
  read, plausible role) · `UNVERIFIED` / `[capture/debugger-pending]` (inferred meaning or wire
  value, not pinned — needs a capture or a debugger trace).

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
the wire as `[u16 major][u16 minor]` and reasoned about as `(major << 16) | minor`. *([confirmed]:
the dispatcher reads major as the u16 at `+4` and minor as the u16 at `+6`.)*

**Frame header total = 8 bytes: `[u32 size @+0][u16 major @+4][u16 minor @+6]`. *([confirmed]* —
this resolves the long-standing u16-vs-u32 size question: the size is a **u32 at offset +0**, not a
u16.)** This is independently witnessed at three sites that all key the header-only/payload split on
the **u32 at `+0`**: the inbound decompress stage (§6.2), the outbound compress stage, and the
outbound cipher gate (§6.1) each compare `size == 8` (the u32 at `+0`) to decide the bypass, and
each treats the payload as the bytes from `+8` over `size − 8`. The total header is therefore
`4 + 2 + 2 = 8`. *(In-process detail only: when the codec stages rebuild a frame they copy just the
major+minor words; the new buffer's size word is set from the appended payload length, not copied
from the source — no wire effect.)*

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

The dispatcher's top-level shape is **decompress-first → `if (major != 0) switch(major)` →
else the `(0,0)` handshake branch**, always ending with a free of the working buffer. *([confirmed].)*

| Major family | Routing style | Notes (cross-ref `handlers.md` §1) |
|---|---|---|
| `0` (KeyExchange) | **hardwired `(0,0)` branch — NOT a switch** | the single `major==0 && minor==0` pair is handled in the else-branch of the `major != 0` test; no per-minor switch exists for major-0 (see §5) |
| `1` (Server/Billing) | inline switch on minor | only a fixed set of inbound minors is wired (see §1.3a) |
| `3` (CharacterMgmt) | inline if/else cascade (compiler-lowered switch) | a fully enumerated set of minors (no hidden minors) (see §1.3b) |
| `4` (Response) | **table-driven** | fixed 154-slot table; unset slots are inert no-ops; `4/500` and `4/50000` routed outside the table |
| `5` (Push) | **table-driven** | fixed 154-slot table; unset slots are inert no-ops |

- **Major-0 is a hardwired `(0,0)` handshake, not a routing family.** *([confirmed].)* There is no
  inline switch over major-0 minors. Only the exact pair `(0,0)` is handled — and that single
  branch does more than route: it *parses the inbound key-exchange blob and sends the `1/4`
  credential reply*. It is documented in full in §1.4. (Earlier prose called major-0 "an inline
  switch with a small fixed set of minors"; that overstated it and has been corrected.)
- The **major-4 (Response)** and **major-5 (Push)** families are each dispatched through a
  **fixed-bound handler table of 154 slots** (`0x9A`). *([confirmed]:* major-4 takes the table path
  when `minor < 154`, major-5 likewise; the two tables are physically adjacent inside the handler
  object — see §2). A minor below the bound that was never installed resolves to an **inert no-op
  handler** (a slot pre-filled to a routine that simply returns success — not a crash); a minor
  **at or above 154 is out of range and is not dispatched at all**. This caps the legal minor
  space per family at `0..153`. The per-handler enumeration of which slots are installed, and the
  recovered install counts, live in `handlers.md` §1 — see §2 below for the installer machinery
  that fills these tables.
- Two major-4 minors are routed **outside** the table by special-case branches — `4/500` (shows a
  popup by code) and `4/50000` (discards a text payload) — per `handlers.md` §1; the machinery here
  is the table path. *([confirmed]* routing; popup/discard roles are the handler behaviours owned by
  `handlers.md`.)

### 1.3a Major-1 inline switch (Server / Billing, S2C)

The major-1 family is a small inline switch; only four inbound minors are wired, all others fall
through to a no-op. *([confirmed]* routing; what each notice *means* is `handlers.md` territory.)*

| Minor (dec / hex) | Role (per `handlers.md`) |
|---|---|
| 16 / `0x10` | billing-deactivated notice |
| 17 / `0x11` | billing-activated notice |
| 19 / `0x13` | billing-expiry notice |
| 20 / `0x14` | letter-received notice |

### 1.3b Major-3 inline cascade (CharacterMgmt, S2C)

The major-3 minor switch is compiler-lowered into a subtract-chain but is a **fully enumerated**
set — there are no hidden minors. *([confirmed]* routing — this is the authoritative major-3 ladder;
it agrees with `opcodes.md`/`handlers.md`. Field value semantics stay capture-pending.)

| Minor | Routed handler (name per `handlers.md`) |
|---|---|
| 1 | `SmsgCharacterList` |
| 4 | `SmsgSceneEntityUpdate` |
| 5 | `SmsgEnterGameAck` (login-success ack) |
| 6 | `SmsgRenameCharResult` |
| 7 | `SmsgCharManageResult` |
| 8 | `SmsgShopPageUpdate` |
| 13 | `SmsgCharStatusUpdate` |
| 14 | `SmsgCharSpawnResponse` |
| 23 | major-3 minor-23 handler (behaviour TBD) |
| 100 | `SmsgCharActionResult` (generic result codes) |
| 50000 | `SmsgGmChatMessage` (high-minor GM channel) |

> **Anchor alignment (load-bearing).** `3/4 = SmsgSceneEntityUpdate` and `3/7 = SmsgCharManageResult`
> — *not* the reverse. Any neighbour doc that places `SceneEntityUpdate` at `3/14` or
> `CharManageResult` at `3/4` is wrong. `3/14 = SmsgCharSpawnResponse` (16-byte read). The packet
> sizes/offsets for these are owned by `handlers.md` / the `packets/*.yaml`.

### 1.4 The `(0,0)` key-exchange handshake branch

The `(0,0)` else-branch is **not** a routing stub — it is the **client's half of the secure
handshake**. *([confirmed]* control flow; the *meaning* of each blob field is
`[capture/debugger-pending]`.)* When the dispatcher sees `major == 0 && minor == 0` it, in order:

1. **Parses the inbound S2C key-exchange blob.** The parser reads, from the packet body in order: a
   **54-byte key blob** (imported as an RSA modulus/exponent), then **two trailing `u32` server
   scalars**, and stamps a local time. On a parse failure it logs a "read packet version" style
   diagnostic. So the **S2C `(0,0)` body read order is `[54-byte key blob][u32][u32]`**. *([confirmed]
   read order / sizes; the field MEANINGS are `[capture/debugger-pending]`.)*
2. **Builds and sends the C2S credential reply stamped opcode `(1, 4)`.** It constructs the secure
   auth reply (an RSA-encrypted credential, then a per-`u32` whitening pass) and hands it to the send
   convergence (§6.1). This is the structural link **dispatch `(0,0)` → emits the `1/4` login
   credential** — the same `1/4` opcode the login-credential spec documents. *([confirmed]* routing /
   send path; payload field semantics are `[capture/debugger-pending]`.)
3. **Sets a "key-exchange complete" flag** on the network-client object.

> **S2C `(0,0)` body — interoperability summary** (read order; sizes confirmed, semantics pending):
>
> | Field | Width | Note |
> |---|---|---|
> | RSA key blob | 54 bytes | imported as the RSA modulus/exponent for the credential encrypt |
> | server scalar A | `u32` | trailing value 1 — meaning capture-pending |
> | server scalar B | `u32` | trailing value 2 — meaning capture-pending |
>
> The whitening / RSA details of the outbound `1/4` reply are owned by `crypto.md` and the
> login-credential packet spec; cited here, not re-derived.

---

## 2. The two handler-table installers

Two boot-time routines populate the Response and Push handler tables inside the network client.
They are the machinery that turns the empty 154-slot tables of §1.3 into the live routing surface
that `handlers.md` enumerates. *(STRUCTURE-HIGH; build 263bd994.)*

| Installer | Family | Recovered installed slots | Role |
|---|---|---|---|
| Response-family installer | major 4 | **~99 slots** | Wires each Response per-opcode handler into the client handler table at boot. |
| Push-family installer | major 5 | **65 slots** | Wires each Push per-opcode handler into the client handler table at boot. |

- The two install counts (**~99 Response / 65 Push**) are control-flow counts on this build.
  *([confirmed].)* The Response figure is refined from the earlier `~98`: the installer issues 101
  store instructions, of which **2 are zero-clears** (slots `0` and `27`) and **99 install handler
  pointers** — and one of those handlers is written to **two adjacent slots** (`143` and `144`), so
  the installer wires ~99 live Response handlers. The Push installer issues 66 stores (65 handler
  installs + a final return). These are the same counts `handlers.md` §1 reports; they are
  reproduced here to characterise the installer routines, not to re-enumerate the handlers. Every
  minor not installed by these two routines keeps the inert no-op fill from §1.3.
- **The two tables are physically adjacent** inside the handler object: the Response (major-4) table
  begins at pointer-index `1246`, the Push (major-5) table at pointer-index `1400`, and
  `1400 − 1246 = 154 = 0x9A` — i.e. the two 154-slot tables abut, Response then Push. *([confirmed].)*
- **Single pre-fill loop.** In the handler-object constructor, **one 154-iteration loop pre-sets
  *every* slot of *both* tables to the inert no-op handler** (a routine that simply returns success)
  before either installer runs. The installers then overwrite only the live slots; any slot they do
  not touch keeps the no-op. *([confirmed]* — this is the concrete mechanism behind §1.3's "unset
  slots are inert no-ops".)
- Each **installed handler** opens by reading from a **bounded receive buffer** — the handler's
  first act is a length-bounded read of its fixed payload prefix out of the inbound buffer. This
  "open with a bounded read" stereotype is what `handlers.md` records as each handler's
  *minimum fixed payload* / *fixed read* length; it is a structural property of the installed
  handler, and the bound is what makes an over-short frame safe to dispatch. *([static-hypothesis]
  as a blanket claim across all installed handlers; `handlers.md` owns the per-handler read sizes,
  several of which are control-flow-confirmed there.)*
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

**The gate is a precise type+sub-code discriminator.** *([confirmed].)* The entry reads an
**event-type byte at the event's offset `+0`** and, when that type is **`15`** (the network-event
umbrella), reads a **sub-code word at `+4`**:

| Event type @+0 | Sub-code @+4 | Routed to |
|---|---|---|
| `15` | `100` | the master receive dispatcher (§1) — a received packet to route |
| `15` | `102` | the connection-state machine (§5) — a connection-state transition |
| (anything else) | — | ignored (returns without action) |

So **type `15` is the umbrella; sub-code `100` vs `102` discriminates packet-dispatch from
connection-state**. This is distinct from the close-event *factory* tag also numbered `15` in §4.6
(that tag identifies the disconnect event the teardown path *builds*; the `+0` type byte here is the
*umbrella* under which the `100`/`102` sub-codes live). Field value semantics carried inside a
dispatched packet remain `[capture/debugger-pending]`.

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

### 4.5 Keepalive — TWO distinct mechanisms

There are **two separate keepalive mechanisms**, both real; which one is actually sent on-wire (and
at what cadence) is `[capture/debugger-pending]`. Record both.

**(a) Ctor-armed compressed periodic frame.** *([confirmed]* it is armed; the period value is read
from the routine).* The client arms a **periodic compressed keepalive**: a supplied keepalive
packet is run once through the outbound compress stage (§6), the compressed buffer is stored in a
client slot, the original buffer is released if a new one was produced, and a **periodic interval**
is recorded (the recovered interval is `1000 ×` the routine's argument — i.e. a per-second-scaled
period). Thereafter the pre-compressed keepalive frame is sent on that interval. As constructed in
the network-handler constructor, this armed frame carries opcode **`(2, 10000)`** with an interval
argument of **20**, i.e. a **20000 ms (20 s) period**.

**(b) Runtime C2S `2/112` toggle.** *([confirmed]* it exists as a 1-byte C2S send gated by a master
flag; its cadence is capture-pending.)* A separate **C2S `2/112` one-byte toggle** is gated by a
**master-enable flag** that is *set on world-enter and cleared on leave*. This is the runtime,
in-session keepalive path, distinct from the `(2,10000)` frame armed at construction.

> These are **two distinct mechanisms**, not one read two ways: `(2,10000)@20 s` is the
> construction-time armed periodic frame; `2/112` is the world-session 1-byte toggle. They are
> **not reconciled to a single on-wire cadence** — the actual keepalive traffic and timing is
> `[capture/debugger-pending]`. The `opcodes.md` catalogue carries **both** rows (`2/10000` and a
> `2/112` keepalive-toggle row).

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

The state machine is reached via the network-event entry's **type-15 / sub-code-102** branch
(§3.1). Internally it **switches on the current game-state code** (a small enumerated set) and drives
connect/scene transitions from there — confirming the "no packet-body read" property: its inputs are
the game-state code, the timed-event tag, and the numeric state codes, never a wire payload field.
*([confirmed]* on the "no body read" control flow; code *meanings* `[capture/debugger-pending]`.)

### 5.1 Inputs

- A **timed-event tag `10001`** drives the time-based transitions (e.g. a connection-attempt
  timeout / retry tick); the machine arms `10001` timers (recovered as a ~5000 ms tick) on several
  state branches. *(LIKELY role; meaning `[capture/debugger-pending]`.)*
- The **current game-state code** selects which transition branch runs (the top-level switch).
- A small set of **numeric state codes** (§5.2) select the transition outcome. These codes are the
  recovered facts; their precise game-state meaning is inferred.

> **Confirmed control-flow detail (meaning inferred).** On one game-state branch, when the pending
> result is clear the machine *publishes* state code **`201`** (via a manager method) and arms a
> `10001` timer; codes **`202` / `203` / `232`** instead *clear* the pending-result slot. This is
> the shape of the transition logic; the game-meaning attached to publishing `201` vs clearing on
> `202/203/232` is inferred, not pinned.

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
   → timestamp            (GetTickCount stamped at the head of the send convergence)
   → byte cipher          (crypto.md §3.1 — keyless byte transform over the payload)
   → LZ4 compress         (crypto.md §3.2 — raw-block LZ4 over the post-header payload)
   → enqueue → send
```

The send-chain convergence point (the single routine that ~all send sites flow through) stamps a
`GetTickCount` timestamp, runs the **cipher gate**, then the **compress stage** (swapping in the
compressed buffer and freeing the original), then hands the frame to the queue/transport writer.
**The order is cipher THEN compress.** **Header-only frames (`size == 8`) bypass both stages.**
*([confirmed].)*

### 6.2 Inbound (server → client), before dispatch

An inbound packet is **LZ4-decompressed before dispatch** (the expand step of §1.2), then handed
to the master dispatcher. **Header-only frames bypass decompression** (the `size == 8` check on the
u32 at `+0`). The decompressor reads the LZ4 input from `+8` over `size − 8` and decodes into a
**fixed 11680-byte (`0x2DA0`) output buffer** — a concrete capacity bound (useful for sizing the
C# receive buffer). The decoder is a genuine raw-block LZ4 (token literal/match nibbles, the
255-extension scheme, little-endian `u16` match offsets, bounded output). *([confirmed].)*

**Inbound is decompress-only — there is NO inverse byte cipher on receive.** *([confirmed]* on the
client side, by a *positive single-caller proof*: the byte-cipher routine has **exactly one
cross-reference — the outbound send gate** — so it is structurally unreachable on the receive path.
This is stronger than "no inverse call was observed".) Send = encrypt **then** compress; receive =
decompress **only**. Whether the *server* likewise omits an inverse cipher on every inbound type,
generalised across the whole protocol, is the `crypto.md` §5 open question and remains
`[capture/debugger-pending]`; this spec only places the stage and records the client-side proof.

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
| `(0,0)` key-exchange handshake + emitted `1/4` reply (§1.4) | RSA / whitening of the `1/4` reply → `crypto.md` + the login-credential packet spec |
| Handler-table installers + install counts (§2) | Per-handler behaviour and the same counts → `handlers.md` §1 |
| Network-event entry (type-15 / 100 vs 102) + handler object (§3) | (machinery only here) |
| Network-client lifecycle + keepalive duality (§4) | `2/112` keepalive-toggle row + `2/10000` armed frame → `opcodes.md` |
| Connection-state machine + codes (§5) | Lobby action-code mapping (shared numbers, different path) → `handlers.md` §12 |
| Cipher/LZ4 placement (§6) | Cipher + LZ4 algorithms → `crypto.md` §3; inbound cipher-omission generalisation → `crypto.md` §5 |

---

## 8. Status of prior open questions (this pass)

### Resolved this pass (control-flow-confirmed)

- **Frame-header size width — RESOLVED.** The size is a **u32 at `+0`** (witnessed at three codec
  sites), settling the long-standing u16-vs-u32 question. Header = `[u32 size][u16 major][u16 minor]`
  = 8 bytes (§1.1).
- **Major-0 shape — RESOLVED (conflict).** Major-0 is a **hardwired `(0,0)` handshake branch, not an
  inline switch**; it parses the inbound key blob and emits the `1/4` reply (§1.4). The earlier
  "inline switch / small fixed set of minors" wording was an overstatement and is corrected.
- **Response install count — RESOLVED (conflict).** Refined from `~98` to **~99** (101 stores − 2
  zero-clears, one handler shared across slots `143`/`144`) (§2).
- **Inbound cipher omission on the CLIENT side — RESOLVED (positive single-caller proof).** The byte
  cipher has exactly one cross-reference (the outbound send gate), so it is structurally unreachable
  on receive; client inbound is **decompress-only** (§6.2). Crypto OQ#1 is resolved for the client
  receive path. (Generalising the omission across *all* inbound types on the wire stays
  capture-pending — `crypto.md` §5.)

### Still open (capture / debugger-pending)

- **No capture this pass.** The routing/lifecycle structure is a static read on build `263bd994`;
  **all packet field VALUE semantics** — what each wire byte *means* — are deferred to a live
  capture/debugger session. Routing, sizes, and offsets are confirmed; meanings are not.
- **`(0,0)` body field meanings.** The read order/sizes `[54B key blob][u32][u32]` are confirmed
  (§1.4); the meaning of the two trailing `u32` server scalars is `[capture/debugger-pending]`.
- **Connection-state code semantics.** `201 / 202 / 203 / 232` and the timed tag `10001` are
  recovered numeric facts; the precise meaning attached to each (connecting / connected /
  disconnected / error) is an inferred grouping and is `UNVERIFIED`. A debugger trace of a
  connect→play→disconnect cycle would pin each code to an observed transition.
- **Keepalive duality — on-wire cadence not pinned.** Two keepalive mechanisms are both real (§4.5):
  the ctor-armed `(2,10000)@20 s` frame and the runtime `2/112` toggle. Which is actually sent on the
  wire, and at what cadence, is `[capture/debugger-pending]`. (The `1000 ×` interval scaling is read
  from the routine; the wall-clock keepalive period is unconfirmed.)
- **Inbound cipher asymmetry (generalisation).** The client-side "decompress-only" placement is
  proven (§6.2); whether the server omits an inverse cipher across *every* inbound type is the
  `crypto.md` §5 open question this spec inherits but does not resolve.
- **Second worker's exact duties.** The receive worker is fully characterised (§4.4); the second
  spawned worker is the connection's I/O-side thread, but its loop is not decomposed here.
