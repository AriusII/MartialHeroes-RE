---
verification: confirmed
ida_reverified: 2026-06-19
ida_anchor: 263bd994
ida_cycle7: re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
evidence: [static-ida]
conflicts: the 0x117C..0x1377 region between the 5×880-byte sub-struct array and the response table (contents not written by the visible ctor stores) and the +0x38 reserved slot are carried as UNVERIFIED gaps; the embedded Guest/Member sub-objects are confirmed present but minimal (vftable-only) and their runtime access path is via globals rather than a literal `this+0x28` reference
---

# NetHandler object layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's **S2C dispatch / handler singleton** (the
object whose two 154-slot function-pointer tables route every server response and server push).
Promoted from dirty-room notes; **rewritten** — no decompiler identifiers, no binary addresses.
This file is the offset-table backing for the IDB struct typing of the network dispatch object and
the design input for the `Client.Application` packet-handler layer.

All offsets are expressed **relative to the start of the object** (the `NetHandler` instance,
addressed as `this`). They are never binary addresses. A few "dword-index" notes are given where
the original code stepped the object pointer in 4-byte units; the **byte offset is authoritative**
(byte = dword-index × 4).

Cross-references: `specs/handlers.md` and `specs/network_dispatch.md` (the opcode-dispatch
behaviour and the three thread procs); `opcodes.md` (the full opcode catalogue);
`structs/runtime_singletons.md §3.4` (the same object in the runtime-singleton census, where it is
listed at 6 220 bytes).

## Status header

| Aspect | State |
|---|---|
| Overall object size | **CONFIRMED** — ≈ 6 220 bytes. The two 154-slot tables end at the last written dword (the push table runs to its 154th slot), pinning the tail of the object. |
| Base class | **CONFIRMED** — derives from a shared command-handler base ("Cmdhandler"): a name `std::string` and two scalar parameters occupy the base region. The same base underlies `NetClient` (see `structs/net_client.md`). |
| Vtable layout | **CONFIRMED** — a primary vtable pointer at +0x00 plus two further interface vtable slots at +0x28 / +0x2C (the two embedded sub-objects each carry their own vtable). |
| Response / push tables | **CONFIRMED** — two abutting 154-slot (616-byte) function-pointer tables at +0x1378 and +0x15E0, each NoOp-prefilled then overwritten by an installer. Major-4 indexed `(table_index − 1246) = minor`; major-5 indexed `(table_index − 1400) = minor`. The master dispatcher reads major (u16) at frame +4, minor (u16) at frame +6; majors 1/3 are inline-switch routed. |
| Keepalive frame / timer arm | **CONFIRMED (CYCLE 7)** — the (2,10000) keepalive frame is built once, pre-compressed, at construction; the keepalive timer is armed here with arg 20 (→ 20 s interval). |
| 5×880-byte sub-struct array | **CONFIRMED (span)** — five inline 880-byte sub-structures from +0x3C; inner shape PENDING. |
| 0x117C..0x1377 gap | **UNVERIFIED** — region between the sub-struct array end and the response table; not written by the visible ctor stores. |
| +0x38 reserved | **UNVERIFIED** — zeroed in the constructor; runtime role not proven statically. |

Confidence per field is given inline in each table (`CONFIRMED`, `UNVERIFIED`). Field VALUE
semantics inside opaque sub-objects/regions remain PENDING.

---

## Object model overview

`NetHandler` is a single singleton object of **≈ 6 220 bytes** with the following top-level regions:

| Range | Size | Region |
|---|---|---|
| +0x00 .. +0x3B | 0x3C | Header: primary vtable, the shared command-handler base (name string + two params), the two embedded sub-objects, and two cached/reserved pointer slots. |
| +0x3C .. +0x117B | 0x1140 | **5 × 880-byte (0x370) inline sub-structures** (`sub_struct_array[5]`). Inner shape PENDING. |
| +0x117C .. +0x1377 | 0x01FC | **Gap region** — UNVERIFIED (not written by the visible constructor stores). |
| +0x1378 .. +0x15DF | 0x0268 | **`response_handler_table`** — 154 function-pointer slots (major-4 S2C responses). |
| +0x15E0 .. +0x1F43 | 0x0964 | **`push_handler_table`** — 154 function-pointer slots (major-5 S2C pushes); abuts the response table. |

The first 4-byte slot at +0x00 is the object-type pointer (a C++ virtual-table pointer); the
client identifies the object by the run-time type name "Cmdhandler". A managed re-implementation
does not reproduce these pointers — it models the two handler tables as two arrays of 154
opcode→delegate entries.

---

## Full NetHandler field table

Offsets relative to the start of the object.

| Offset | dword-idx | Size | Type | Field | Confidence | Notes |
|--------|-----------|------|------|-------|------------|-------|
| +0x0000 | 0 | 4 | ptr | `vtable` | CONFIRMED | Primary virtual-table pointer (the dispatch interface). |
| +0x0004 | 1..7 | ~28 | std::string | `name_string` | CONFIRMED | Shared command-handler-base name string, set to `"nethandler"` by the base constructor. MSVC `std::string` ≈ 28 bytes in this build. |
| +0x0020 | 8 | 4 | uint32 | `cmdhandler_param_a` | CONFIRMED | First base-class scalar parameter (value 2000). Role inside the base class PENDING. |
| +0x0024 | 9 | 4 | uint32 | `cmdhandler_param_b` | CONFIRMED | Second base-class scalar parameter (value 49152). Role PENDING. |
| +0x0028 | 10 | ~4 | obj | `guest_subobject` | CONFIRMED | Embedded "Guest" sub-object. The constructor builds it; its body writes only its own vtable at +0 → effectively a single-field (4-byte) polymorphic stub. A secondary interface vtable is later stored here. |
| +0x002C | 11 | ~4 | obj | `member_subobject` | CONFIRMED | Embedded "Member" sub-object. Built by the constructor; its body writes only its own vtable and caches the actor-manager and an actor-visual reference into **client globals** (not into this object) → also a ~4-byte polymorphic stub. A tertiary interface vtable is later stored here. |
| +0x0030 | 12 | 4 | ptr | `cached_actor_manager` | CONFIRMED | Cached pointer to the `ActorManager` singleton; written during construction. This store is the first point at which the `ActorManager` singleton is created (cross-ref `structs/runtime_singletons.md §3.7`). |
| +0x0034 | 13 | 4 | ptr | `cached_window_handle` | CONFIRMED (slot) / PENDING (producer) | Zeroed in the constructor; populated later. Handlers read it as the character-select / enter-world / text-sink window handle. The exact producer is unverified. |
| +0x0038 | 14 | 4 | ptr/uint32 | (reserved) | **UNVERIFIED** | Zeroed in the constructor; runtime role not proven statically. Likely a second cached reference. |
| +0x003C | 15 | 4 400 | obj[5] | `sub_struct_array` | CONFIRMED (span) | Five inline 880-byte (0x370) sub-structures, built by a 5-trip constructor loop with an 880-byte stride. Spans +0x003C .. +0x117B. Inner shape PENDING. (In `structs/runtime_singletons.md §3.4` this region is described as five 880-byte character-select scratch slots.) |
| +0x117C | 16..1245 | 0x01FC | bytes | (gap) | **UNVERIFIED** | Region between the sub-struct array end and the response table; not written by the visible constructor stores. Contents unproven by this lane. |
| +0x1378 | 1246 | 616 | ptr[154] | `response_handler_table` | CONFIRMED | 154 function-pointer slots for **major-4** server-response (S2C) handlers. Pre-filled with a single no-op handler, then overwritten by the response-table installer. The dispatcher routes major-4 by indexing this per-instance table as `(table_index − 1246) = minor` — i.e. slot `(minor)` of this table. |
| +0x15E0 | 1400 | 616 | ptr[154] | `push_handler_table` | CONFIRMED | 154 function-pointer slots for **major-5** server-push (S2C push) handlers. Same initialisation pattern (no-op fill → push-table installer). The dispatcher routes major-5 by indexing as `(table_index − 1400) = minor`. Abuts the response table (1400 − 1246 = 154). Ends at the object tail. |

> **Table sizing.** 154 slots × 4 bytes = 616 bytes per table; the two tables together account for
> 1 232 bytes. The remaining bytes are the five 880-byte sub-structures (4 400 bytes) plus the
> ≈ 0x3C-byte header and the UNVERIFIED gap.

---

## Notes for the network / application engineer

- **Two dispatch tables, 154 slots each, NoOp-prefilled.** The constructor pre-fills both tables with
  one inert no-op handler (a slot that simply returns success), then runs two installer routines that
  overwrite the real handler slots. The master dispatcher reads the **major (u16) at frame +4** and
  the **minor (u16) at frame +6**, then routes: major-4 through `response_handler_table` indexed
  `(table_index − 1246) = minor`; major-5 through `push_handler_table` indexed
  `(table_index − 1400) = minor`. Majors 1 and 3 are routed by an **inline switch / if-chain**, not
  table driven (cross-ref `specs/network_dispatch.md`).
- **Three thread procs total (cross-ref `specs/network_dispatch.md`).** The recv side is the
  recv-completion I/O thread; the connection owner's "two workers" (documented in
  `structs/net_client.md`) are the recv consumer and the keepalive timer. Do **not** re-derive the
  thread model from this object — `NetHandler` is the dispatch table owner, not the connection
  owner.
- **The two embedded sub-objects are minimal.** Both the Guest (+0x28) and Member (+0x2C)
  sub-objects are confirmed present but are vtable-only stubs; the "member" behaviour is reached at
  runtime through cached client **globals** the Member constructor installs, not via a literal
  reference to this object's +0x28 region.
- **Keepalive frame built once at construction; timer armed to 20 s here.** The constructor builds
  the keepalive frame (**major 2, minor 10000**) a single time at handler-table construction,
  **pre-compressed**, and at the same construction point arms the keepalive timer with **arg 20**
  (interval = 1000 × 20 = **20 s**) on the connection owner. The timer thread then re-enqueues the
  cached pre-compressed frame verbatim (bypassing the normal send builder). Cross-ref
  `structs/net_client.md` (keepalive interval +0x141AC, in-flight latch +0x141BC) and
  `specs/login_flow.md` / `specs/network_dispatch.md`.
- A managed re-implementation should model the two handler tables as `Dictionary`/array of
  154 `(minor → delegate)` entries each, keyed by minor opcode, rather than reproducing the inline
  function-pointer arrays.

---

## Open questions (UNVERIFIED / PENDING)

1. **0x117C..0x1377 gap (UNVERIFIED).** The 0x01FC-byte region between the sub-struct array end and
   the response table is not written by the visible constructor stores; its contents/role are
   unproven.
2. **+0x38 reserved slot (UNVERIFIED).** Zeroed in the constructor; runtime role not proven
   statically.
3. **Sub-struct-array interior (PENDING).** The shape of each of the five 880-byte sub-structures is
   not individually mapped here.
4. **`cached_window_handle` producer (PENDING).** The +0x34 slot is read by several handlers as a
   window handle but the exact write site that populates it was not pinned.
