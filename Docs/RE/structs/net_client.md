---
verification: confirmed (with corrections — binary wins on three tail-offset drifts; see conflicts)
ida_reverified: 2026-06-24   # network-dispatch audit (263bd994): three tail-offset drifts corrected — init/connected gate is +0x14178 (not +0x141FC); stop-event handle is +0x141B4 (resolves open Q#1); secure_context_ptr is +0x141B8 (not +0x141F8); (0,0) handshake sets +0x141BC suppress latch (not a separate "key-exchange complete" flag at +0x141FC). Prior: CYCLE 7 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
layout: confirmed (tail corrected 2026-06-24)
value_semantics: capture/debugger-pending
conflicts: CORRECTED 2026-06-24 (binary wins) — (1) init/connected gate relocated from +0x141FC to +0x14178; (2) +0x141B4 is the manual-reset worker stop-event handle (CreateEventA in SpawnWorkerThreads), not a reserved last-receive stamp — resolves open Q#1; (3) secure_context_ptr relocated from +0x141F8 to +0x141B8; (4) the (0,0) handshake branch sets +0x141BC (the suppress latch), not a distinct "key-exchange complete flag" at +0x141FC; +0x141F8 and +0x141FC do not appear to be written by the constructor. Inner layout of the connection sub-object (+0x48) is still a span hypothesis.
---

# NetClient object layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's **connection-owner singleton** — the object
that owns the TCP socket, the receive buffer, the cipher/compression wrappers, and the keepalive
machinery. Promoted from dirty-room notes; **rewritten** — no decompiler identifiers, no binary
addresses. This file is the offset-table backing for the IDB struct typing of the connection object
and the design input for the `Network.Transport.Pipelines` / `Network.Crypto` layers.

The object is large (≈ 82 368 bytes) because it embeds the connection sub-object, which in turn
embeds the large receive-buffer slot. It is **distinct** from the dispatch-table singleton
(`structs/net_handler.md`).

All offsets are expressed **relative to the start of the object** (the `NetClient` instance,
addressed as `this`). They are never binary addresses. The decimal column is provided because
several deep offsets were specified in decimal in the source note (the offsets exceed 0x14000).

Cross-references: `specs/network_dispatch.md` (the send/recv framing and the three thread procs);
`specs/login_flow.md` (the handshake and the secure-context handoff);
`structs/secure_context.md` (the separately-allocated handshake/crypto context this object points
at); `structs/runtime_singletons.md §3.3` (the same object in the runtime-singleton census).

## Status header

| Aspect | State |
|---|---|
| Overall object size | **CONFIRMED** — ≈ 82 432 bytes; last written byte by the constructor is the suppress latch at +0x141BC (decimal 82 364). |
| Base class | **CONFIRMED** — derives from the same shared command-handler base as `NetHandler`: a name `std::string` (`"Network"`) and two scalar parameters in the base region. |
| Connection sub-object | **CONFIRMED (base) / span hypothesis** — embedded at +0x48; constructed by its own routine. It holds the socket slot (with the large recv buffer) plus scalars, two further sub-objects, a string, and a critical section. It spans from +0x48 up into the 0x14xxx region. |
| Keepalive | **CONFIRMED** — keepalive packet buffer pointer at +0x141A8, interval at +0x141AC = **1000 × arm-arg**, armed at **20000 ms (20 s)**; last-send tick stamp at +0x141B0. |
| Request-in-flight latch | **CONFIRMED (CYCLE 7)** — the suppress latch is the **separate one-byte field at +0x141BC** (NOT the init gate). Only the keepalive timer reads it. The (0,0) handshake branch sets this same latch (not a distinct "key-exchange complete" field). |
| Worker stop-event handle | **CONFIRMED (2026-06-24)** — manual-reset stop event at **+0x141B4**; created in SpawnWorkerThreads and waited by both network-client worker loops. Resolves open Q#1. |
| Secure-context pointer | **CONFIRMED (corrected 2026-06-24)** — pointer at **+0x141B8** (dword 20590) to the **separately-allocated** `SecureContext`; read by both the key-exchange parse wrapper and the secure-auth-reply builder. Previous doc claimed +0x141F8 — binary wins. |
| Init / connected gate | **CONFIRMED (corrected 2026-06-24)** — the one-byte connected/initialised gate is at **+0x14178** (decimal 82 296); set by StartNetworkEngine, cleared by Disconnect, watched by both worker loop conditions. Previous doc claimed +0x141FC — binary wins. |
| Staged connect host / port | **CONFIRMED (slot) / RUNTIME-ONLY (value)** — the connect command stages a host string (passed to DNS resolution at connect time) and the connect port at +0x44; both are runtime values, no static endpoint baked in. |

Confidence per field is given inline in each table (`CONFIRMED`, `UNVERIFIED`). Field VALUE
semantics inside the embedded socket slot remain PENDING.

---

## Object model overview

`NetClient` is a single singleton object of **≈ 82 432 bytes** with the following top-level regions:

| Range | Size | Region |
|---|---|---|
| +0x00 .. +0x47 | 0x48 | Header: primary vtable, the shared command-handler base (name string + two params), a host/address string. |
| +0x48 .. ~+0x14177 | large | **Embedded connection sub-object** (socket slot + recv buffer + scalars + two sub-objects + string + critical section). This is what drives the ≈ 80 KB object size. |
| +0x14178 .. +0x141BC | ≈ 0x45 | Init/connected gate, worker slots, worker flags, keepalive buf/interval/timestamp, stop-event handle, secure-context pointer, and suppress latch. |

The first 4-byte slot at +0x00 is the object-type pointer (a C++ virtual-table pointer); the client
identifies the object by the run-time type name "Network". A managed re-implementation does not
reproduce the embedded socket slot or the critical section — those map to the pipeline transport's
own socket and synchronisation primitives.

---

## Top-level NetClient field table

Offsets relative to the start of the object.

| Offset | dec | Size | Type | Field | Confidence | Notes |
|--------|-----|------|------|-------|------------|-------|
| +0x00000 | 0 | 4 | ptr | `vtable` | CONFIRMED | Primary virtual-table pointer ("Network"). |
| +0x00004 | 4 | ~28 | std::string | `name_string` | CONFIRMED | Shared command-handler-base name string, set to `"Network"`. |
| +0x00020 | 32 | 4 | uint32 | `cmdhandler_param_a` | CONFIRMED | First base scalar parameter (value 1000). Role PENDING. |
| +0x00024 | 36 | 4 | uint32 | `cmdhandler_param_b` | CONFIRMED | Second base scalar parameter (value 57344). Role PENDING. |
| +0x00028 | 40 | ~28 | std::string | `host_or_addr_string` | CONFIRMED | `std::string` caching the last-connected server host/address; staged by the connect command and passed to DNS resolution (`gethostbyname`) at connect time. **RUNTIME-ONLY value** (no static IP baked in — see `specs/connection_topology.md`). |
| +0x00044 | 68 | 4 | uint32 | `connect_port` | CONFIRMED (slot) / RUNTIME-ONLY (value) | Connect port, staged by the connect command and used as the TCP port (passed to `htons`). The value is supplied at runtime by the lobby/handoff data, **not** a static immediate. |
| +0x00048 | 72 | large | obj | `connection_subobject` | CONFIRMED (base) / span hypothesis | Embedded connection (socket) sub-object; built by its own routine. Holds the socket slot and the large recv buffer; see the sub-table below. Spans from +0x48 up into the 0x14xxx region. |
| +0x14178 | 82 296 | 1 | uint8 | `init_gate` / `connected_flag` | **CONFIRMED (corrected 2026-06-24)** | The one-byte connected/initialised gate. Set by StartNetworkEngine, cleared by Disconnect, watched by both worker loop conditions. **This is the transport-level connected gate — NOT the request-in-flight latch** (that is the separate byte at +0x141BC). Previous doc placed this at +0x141FC — binary wins. |
| +0x14188 | 82 312 | ~16 | obj | `recv_worker` | CONFIRMED | Worker slot bound to the **receive** thread/pump (worker #1). |
| +0x14194 | 82 324 | 1 | uint8 | `worker_flag_a` | CONFIRMED | Zeroed in the constructor. |
| +0x14198 | 82 328 | ~16 | obj | `second_worker` | CONFIRMED | Worker slot for the second pump (worker #2). With the recv-completion I/O thread, the connection's two workers are the recv consumer and the keepalive timer — **three thread procs total** (cross-ref `specs/network_dispatch.md`; do not re-derive here). |
| +0x141A4 | 82 340 | 1 | uint8 | `worker_flag_b` | CONFIRMED | Zeroed in the constructor. |
| +0x141A8 | 82 344 | 4 | ptr | `keepalive_packet_buf` | CONFIRMED | Pointer to the compressed keepalive frame. |
| +0x141AC | 82 348 | 4 | uint32 | `keepalive_interval` | CONFIRMED | Keepalive interval in **milliseconds**, computed as **1000 × arm-arg**; the arm-arg is **20**, so the armed interval is exactly **20000 ms (20 s)**. The keepalive timer compares `(tick-count − send_timestamp) > this`. (Carries the (2,10000) keepalive frame.) |
| +0x141B0 | 82 352 | 4 | uint32 | `send_timestamp` / last-send tick | CONFIRMED | Millisecond last-send tick stamp; the send path writes the current tick count here. The keepalive timer compares `(tick-count − this) > keepalive_interval` to decide whether to emit the idle ping. Seeded to 0 in the constructor. |
| +0x141B4 | 82 356 | 4 | HANDLE | `worker_stop_event` | **CONFIRMED (2026-06-24 — resolves open Q#1)** | Manual-reset Win32 event handle, created by `CreateEventA` in SpawnWorkerThreads. Both network-client worker loops (`WaitForSingleObject`) watch this handle to know when to exit cleanly. Not a last-receive stamp. |
| +0x141B8 | 82 360 | 4 | ptr | `secure_context_ptr` | **CONFIRMED (corrected 2026-06-24)** | Pointer to the separately-allocated `SecureContext` (see `structs/secure_context.md`). Dereferenced by both the key-exchange parse wrapper and the secure-auth-reply builder (as `client + dword[20590]`). Previous doc placed this at +0x141F8 — binary wins. |
| +0x141BC | 82 364 | 1 | uint8 | `request_in_flight_latch` (suppress latch) | CONFIRMED | **The single one-deep request-in-flight latch** (CYCLE 7). **Only** the keepalive timer READS it, to skip the idle ping while a request is outstanding. SET by char-management sends (1/6, 1/7, 1/9, 1/13, 1/14, 2/2) and by the **(0,0) handshake branch's terminal store** (confirmed 2026-06-24 — the handshake arms the same latch the keepalive timer reads; there is no distinct "key-exchange complete flag" at +0x141FC). CLEARED by their results and **unconditionally by the enter-game world-state handler (response slot 4/1) as one of its first statements**. Zeroed in the constructor. See the in-flight-latch note below. |

---

## Connection sub-object (embedded at +0x48)

Offsets below are **relative to the connection sub-object base** (= NetClient +0x48). The original
code stepped this region in 4-byte (dword) units; byte offset = dword-index × 4. The leading socket
slot is large and holds the recv buffer, which is why the enclosing `NetClient` object is ≈ 80 KB.

| Rel offset | Size | Type | Field | Confidence | Notes |
|------------|------|------|-------|------------|-------|
| +0x00000 | large | obj | `socket_slot` | CONFIRMED (presence) / span hypothesis | Socket slot including the large receive buffer (drives the object's ≈ 80 KB size). Internal shape PENDING. |
| +0x14080 | 16 | uint32[4] | `conn_scalars` | CONFIRMED | Four zeroed dwords. |
| +0x14090 | ~12 | obj | `conn_subobj_a` | CONFIRMED | Embedded helper sub-object (built by its own routine). |
| +0x140C0 | ~12 | obj | `conn_subobj_b` | CONFIRMED | Second embedded helper sub-object. |
| +0x140F0 | ~28 | std::string | `conn_string` | CONFIRMED | A `std::string` member of the connection. |
| +0x14108 | 4 | uint32 | `conn_flag_c` | CONFIRMED | Zeroed in the connection constructor. |
| +0x1410C | 4 | uint32 | `conn_flag_d` | CONFIRMED | Zeroed in the connection constructor. |
| +0x14110 | ~24 | obj | `conn_critical_section` | CONFIRMED | A Win32 critical section, initialised by the connection constructor (guards the send/recv queues). A managed re-implementation maps this to a lock, not a struct field. |

> **Secure-context cross-check.** The recv-key parse path reads the secure-context pointer through
> this connection region at a deep dword offset that, when based at the enclosing `NetClient`,
> resolves to the same field as `NetClient +0x141B8` (`secure_context_ptr`). It is one and the same
> pointer. (Previous cross-check cited +0x141F8 — corrected 2026-06-24; binary wins.)

---

## Notes for the network / transport engineer

- **The connection state lives in the embedded sub-object; the init gate is a single byte at +0x14178 (corrected 2026-06-24).** Treat `init_gate` (+0x14178) as the authoritative transport-level "connected" flag — set by StartNetworkEngine, cleared on Disconnect, watched by both worker loops. (CYCLE 7: this byte is ONLY the connected gate; the request-in-flight latch is the separate byte at +0x141BC. Previous doc placed the init gate at +0x141FC — binary wins.)
- **The request-in-flight latch is the separate byte at +0x141BC (CYCLE 7 correction).** It is a
  single one-deep suppress latch read **only** by the keepalive timer (to skip the idle (2,10000)
  ping while a request is outstanding). It is SET by the char-management sends (1/6, 1/7, 1/9, 1/13,
  1/14, 2/2) **and by the (0,0) handshake branch's terminal store** (confirmed 2026-06-24 — the
  handshake arms the same latch; there is no distinct "key-exchange complete flag" at +0x141FC).
  CLEARED by their results and — load-bearing for the enter ladder —
  **unconditionally by the enter-game world-state handler (response slot 4/1) as one of its first
  statements**. (The 3/5 char-spawn push does NOT clear it; fire-and-forget sends such as logout set
  no latch.) A managed re-implementation models this as one "awaiting-server-response" flag, distinct
  from the transport-level connected state.
- **Keepalive is 20000 ms (20 s) exactly.** The interval field at +0x141AC is computed as
  `1000 × arm-arg` with arm-arg = 20, yielding 20000 ms; the timer compares
  `(tick-count − send_timestamp@+0x141B0) > interval` and then checks the +0x141BC latch is clear
  before emitting the (2,10000) frame.
- **Worker stop event at +0x141B4 (confirmed 2026-06-24 — resolves open Q#1).** A manual-reset Win32
  event (`CreateEventA`) created in SpawnWorkerThreads; both network-client worker loops
  (`WaitForSingleObject`) watch it to know when to exit. A managed re-implementation maps this to a
  cancellation token.
- **Secure-context pointer is at +0x141B8 (corrected 2026-06-24).** Dereferenced by both the
  key-exchange parse wrapper and the secure-auth-reply builder. Previous doc placed it at +0x141F8 —
  binary wins. See `structs/secure_context.md`.
- **Send-proxy worker (the idle-filler pump).** A separate send-proxy worker thread polls every
  10 ms and drives two idle fillers off per-slot gates: one slot for the **1/2** game-connection
  keepalive (header-only, 8-byte frame on the persistent socket) and another for the **2/13**
  move anti-idle filler. Each slot carries a **1-byte enable flag**, an **in-flight pending** marker,
  and a **last-send tick stamp**; the proxy sends when the slot is enabled, no send is pending, and
  the link is idle. There is **no fixed cadence immediate** — the on-wire spacing is governed by the
  10 ms poll + idle/in-flight gating (the 400 ms / 200 ms figures in older notes are WARN-log
  latency thresholds, not periods). Cross-ref `specs/network_dispatch.md`.
- **Three thread procs total.** Recv-completion I/O thread + the two connection workers
  (recv consumer + keepalive timer). The full thread model is owned by `specs/network_dispatch.md`.
- **The secure context is a separate allocation**, reachable only via `secure_context_ptr`
  (+0x141F8) — see `structs/secure_context.md`. It is not embedded in `NetClient`.
- A managed re-implementation models the socket slot, the critical section, and the worker slots
  with the pipeline transport's own primitives; it should keep only the protocol-meaningful fields
  (host string + connect port, keepalive interval + last-send tick, request-in-flight latch,
  secure-context handle, connected gate, and the send-proxy slot gates).

---

## Open questions (UNVERIFIED / PENDING)

1. **+0x141B4 worker stop-event handle — RESOLVED (2026-06-24).** Confirmed as the manual-reset
   Win32 event handle (`CreateEventA`) created in SpawnWorkerThreads and waited by both worker loops.
   Not a last-receive stamp.
2. **Socket-slot interior (PENDING).** The large socket slot's internal layout (recv-buffer header,
   async I/O state) is not mapped here.
3. **Connection sub-object span (span hypothesis).** The exact byte span of the socket slot inside
   the connection sub-object is inferred from the deep field offsets, not from a single confirmed
   end marker.
