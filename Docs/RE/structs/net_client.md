---
verification: confirmed
ida_reverified: 2026-06-19
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: the +0x141B4 reserved slot (zeroed in the constructor, paired with the send timestamp — possibly a last-receive stamp) is carried as UNVERIFIED; the inner field layout of the embedded connection sub-object (+0x48) is recovered at the construction sites but the exact span of the large socket slot is a span hypothesis
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
| Overall object size | **CONFIRMED** — ≈ 82 368 bytes; the last written byte is the init-gate at +0x141FC (decimal 82 364). |
| Base class | **CONFIRMED** — derives from the same shared command-handler base as `NetHandler`: a name `std::string` (`"Network"`) and two scalar parameters in the base region. |
| Connection sub-object | **CONFIRMED (base) / span hypothesis** — embedded at +0x48; constructed by its own routine. It holds the socket slot (with the large recv buffer) plus scalars, two further sub-objects, a string, and a critical section. It spans from +0x48 up into the 0x14xxx region. |
| Keepalive | **CONFIRMED** — keepalive packet buffer pointer at +0x141A8, interval at +0x141AC seeded to **20000** (milliseconds). |
| Secure-context pointer | **CONFIRMED** — a pointer at +0x141F8 to the **separately-allocated** `SecureContext` (not embedded). |
| Init / connected gate | **CONFIRMED** — the one-byte gate at +0x141FC. |
| +0x141B4 reserved | **UNVERIFIED** — zeroed in the constructor; runtime role not proven statically. |

Confidence per field is given inline in each table (`CONFIRMED`, `UNVERIFIED`). Field VALUE
semantics inside the embedded socket slot remain PENDING.

---

## Object model overview

`NetClient` is a single singleton object of **≈ 82 368 bytes** with the following top-level regions:

| Range | Size | Region |
|---|---|---|
| +0x00 .. +0x47 | 0x48 | Header: primary vtable, the shared command-handler base (name string + two params), a host/address string. |
| +0x48 .. ~+0x14177 | large | **Embedded connection sub-object** (socket slot + recv buffer + scalars + two sub-objects + string + critical section). This is what drives the ≈ 80 KB object size. |
| +0x14178 .. +0x141FC | ≈ 0x84 | Connection / worker / keepalive / timestamp / secure-context tail and the init gate. |

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
| +0x00028 | 40 | ~28 | std::string | `host_or_addr_string` | CONFIRMED | `std::string` caching the last-connected server host/address. |
| +0x00048 | 72 | large | obj | `connection_subobject` | CONFIRMED (base) / span hypothesis | Embedded connection (socket) sub-object; built by its own routine. Holds the socket slot and the large recv buffer; see the sub-table below. Spans from +0x48 up into the 0x14xxx region. |
| +0x14178 | 82 296 | 1 | uint8 | `conn_flag_a` | CONFIRMED | Zeroed in the constructor. State flag for the connection tail. |
| +0x14188 | 82 312 | ~16 | obj | `recv_worker` | CONFIRMED | Worker slot bound to the **receive** thread/pump (worker #1). |
| +0x14194 | 82 324 | 1 | uint8 | `worker_flag_a` | CONFIRMED | Zeroed in the constructor. |
| +0x14198 | 82 328 | ~16 | obj | `second_worker` | CONFIRMED | Worker slot for the second pump (worker #2). With the recv-completion I/O thread, the connection's two workers are the recv consumer and the keepalive timer — **three thread procs total** (cross-ref `specs/network_dispatch.md`; do not re-derive here). |
| +0x141A4 | 82 340 | 1 | uint8 | `worker_flag_b` | CONFIRMED | Zeroed in the constructor. |
| +0x141A8 | 82 344 | 4 | ptr | `keepalive_packet_buf` | CONFIRMED | Pointer to the compressed keepalive frame. |
| +0x141AC | 82 348 | 4 | uint32 | `keepalive_interval` | CONFIRMED | Keepalive interval, seeded to **20000** (milliseconds). The keepalive arm path scales a seconds value by 1000 into this slot, corroborating the millisecond unit. (The (2,10000) keepalive frame at 20 s.) |
| +0x141B0 | 82 352 | 4 | uint32 | `send_timestamp` | CONFIRMED | Millisecond send timestamp; the send path writes the current tick count here. Seeded to 0 in the constructor. |
| +0x141B4 | 82 356 | 4 | uint32 | (reserved) | **UNVERIFIED** | Zeroed in the constructor; paired with the send timestamp — possibly a last-receive stamp. Role not proven statically. |
| +0x141F8 | 82 360 | 4 | ptr | `secure_context_ptr` | CONFIRMED | Pointer to the separately-allocated `SecureContext` (see `structs/secure_context.md`). Passed to the secure-auth-reply builder and the inbound key-exchange parser. |
| +0x141FC | 82 364 | 1 | uint8 | `init_gate` / `connected_flag` | CONFIRMED | The one-byte connected/initialised gate. Zeroed in the constructor; set to 1 by the dispatcher after the (0,0) handshake; cleared to 0 on enter-world by the char-spawn handler. |

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
> resolves to the same field as `NetClient +0x141F8` (`secure_context_ptr`). It is one and the same
> pointer.

---

## Notes for the network / transport engineer

- **The connection state lives in the embedded sub-object; the link gate is a single byte.** Treat
  `init_gate` (+0x141FC) as the authoritative "connected" flag — it is set after the (0,0)
  handshake and cleared on enter-world.
- **Keepalive is 20000 ms.** The interval field is seeded to 20000 and the arm path scales seconds
  by 1000 into it; the keepalive carries the (2,10000) frame.
- **Three thread procs total.** Recv-completion I/O thread + the two connection workers
  (recv consumer + keepalive timer). The full thread model is owned by `specs/network_dispatch.md`.
- **The secure context is a separate allocation**, reachable only via `secure_context_ptr`
  (+0x141F8) — see `structs/secure_context.md`. It is not embedded in `NetClient`.
- A managed re-implementation models the socket slot, the critical section, and the worker slots
  with the pipeline transport's own primitives; it should keep only the protocol-meaningful fields
  (host string, keepalive interval, send timestamp, secure-context handle, connected gate).

---

## Open questions (UNVERIFIED / PENDING)

1. **+0x141B4 reserved slot (UNVERIFIED).** Zeroed in the constructor and paired with the send
   timestamp; possibly a last-receive stamp. Role not proven statically.
2. **Socket-slot interior (PENDING).** The large socket slot's internal layout (recv-buffer header,
   async I/O state) is not mapped here.
3. **Connection sub-object span (span hypothesis).** The exact byte span of the socket slot inside
   the connection sub-object is inferred from the deep field offsets, not from a single confirmed
   end marker.
