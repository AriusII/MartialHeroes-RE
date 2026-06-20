---
status: confirmed-routing-capture-pending-semantics
sample_verified: false
build: 263bd994   # static-recovered on doida.exe build 263bd994 (Campaign 7 Wave B)
subsystems: [receive_dispatch, handler_table_install, netclient_lifecycle, connection_state, pipeline_placement]
networked: yes
verification: routing/sizes [confirmed] (routing opcode->handler, frame-header layout, packet read sizes, struct field offsets, table bases, install counts, pipeline-stage placement are all control-flow-confirmed on anchor 263bd994) · evidence [static-ida] · value-semantics [capture/debugger-pending] (every packet field VALUE SEMANTICS, the connection-state code meanings 201/202/203/232, the keepalive on-wire cadence, the inbound-cipher-omission generalised across all inbound types) · static-hypothesis (the blanket "every installed handler opens with a bounded read")
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: RESOLVED this pass — (a) major-0 is a hardwired (0,0) handshake branch, NOT an inline switch (doc reworded); (b) install raw-store counts corrected to Response 102 / Push 65 (was 101/66; derived counts unchanged: 100 Response slots / 99 distinct handlers / 2 NULL slots 0,27 / one live default); (c) keepalive duality recorded — the ctor-armed (2,10000)@20s frame AND the runtime C2S 2/112 toggle are BOTH real, neither's on-wire cadence pinned (capture-pending). CYCLE 2 EXTENSION (2026-06-19): (d) the open "second worker" item is RESOLVED — there is a THREE-thread model (recv consumer + keepalive timer on the NetClient side, and a THIRD socket-I/O thread spawned by the connection's connect routine that does WSARecv-completion → frame reassembly → recv queue → re-arm, and also services the send-signal → WSASend); (e) the 2/10000 keepalive body is 4 bytes (one zero u32), NOT header-only; (f) the exhaustive Response/Push install slot maps are folded in (§2a/§2b), cross-referencing opcodes.md for handler NAMES; (g) the 5/146→C2S 2/146 ack-request handshake is recorded; (h) {202/203/232} 3/100 codes prime GameState=2 (cross-link to §5); (i) PHANTOM REFUTED — there is NO 5000/10000/10001 string-id class (5000=display-duration ms; 10000=keepalive minor + 10 s timer; 10001=timed-event tag)
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
> dispatcher, the two handler-table installers **and their installed-slot maps**, the network-event
> entry and handler object, the network-client connection lifecycle (start / the **three** worker
> threads / receive loop / keepalive / disconnect), the link-health ack handshake, the
> connection-state machine, and where the cipher/compression stages sit in the send/receive
> pipeline. The install-slot maps record **slot occupancy** (installed / NULL / no-op); the per-opcode
> handler **names, sizes and behaviour** are owned by `opcodes.md` and `handlers.md` and are cited,
> not duplicated.
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
   scalars**, and stamps a local time. So the **S2C `(0,0)` body read order is
   `[54-byte key blob][u32][u32]`**. *([confirmed] read order / sizes; the field MEANINGS are
   `[capture/debugger-pending]`.)*

   > **Two distinct diagnostic log sites (corrected attribution — no wire impact).** The failure
   > diagnostics on this branch come from **two different routines** and were previously conflated.
   > The dispatcher-side **wrapper** (the routine that invokes the key-exchange parser) emits the
   > single coarse **"read packet version"** style message, and only when the parser returns failure
   > — it is a version/length-level diagnostic of the wrapper. The **parser itself** emits the
   > finer-grained per-field key-recovery diagnostics (one message per failed field read or failed
   > blob import — distinct messages for the 54-byte blob read, the blob import, and each of the two
   > trailing `u32` reads). So the coarse "read packet version" message belongs to the *wrapper*; the
   > granular key-recovery messages belong to the *parser*. This is a pure log-attribution correction
   > with no effect on the wire layout or read order above. *([confirmed].)*
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

| Installer | Family | Raw store count | Derived live handlers | Role |
|---|---|---|---|---|
| Response-family installer | major 4 | **102 stores** | **99 distinct / 100 slots** | Wires each Response per-opcode handler into the client handler table at boot. |
| Push-family installer | major 5 | **65 stores** | **65 distinct / 65 slots** | Wires each Push per-opcode handler into the client handler table at boot. |

- **Raw store counts (corrected — binary wins).** The Response installer emits **exactly 102 store
  instructions**; the Push installer emits **exactly 65 store instructions**. Earlier wording put the
  Response installer at 101 stores and the Push installer at 66; both were off by one and are
  corrected here. The Push "+1" was a miscount of the function's `ret` epilogue (control flow, not a
  table store); the Response figure rises to 102 because the handler shared between slots `143` and
  `144` is **two distinct store instructions** in the actual code stream (the handler is loaded into
  a register once, then stored twice), not the single store it was previously counted as. *([confirmed].)*
- **Derived counts (unchanged — they reconcile with the raw counts).** The Response installer's 102
  stores decompose as **2 zero-clears + 100 handler-pointer stores**:
  - the **2 zero-clears** target minors **0** and **27** (each NULL-ed via an and-with-zero store, not
    the no-op pointer) — leaving **2 NULL slots**;
  - the **100 handler-pointer stores** land in 100 occupied slots, but minors **143** and **144**
    receive the **same** handler (two stores into one shared handler), so the installer wires
    **99 distinct live Response handlers across 100 slots**.
  The Push installer's 65 stores are all live handler installs — **no zero-clears, no shared slots** —
  giving **65 distinct live Push handlers across 65 slots** (Push minor `0` is a live handler, not a
  zero-clear). The default-route slot is the single live default fill (the inert no-op handler) on
  every uninstalled minor. So the derived picture is **100 Response slots / 99 distinct handlers /
  2 NULL slots (minors 0 and 27) / one live default**, all reconciling with the **102** raw Response
  stores and **65** raw Push stores. These are the same counts `handlers.md` §1 reports; they are
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

### 2a. Response table (major 4) — installed-slot map

The exhaustive Response slot map below records **which of the 154 slots the installer writes** and
**how** (live handler / explicit NULL / no-op fill). It is the *dispatch surface*, not the opcode
catalogue: the per-opcode **handler NAMES, sizes and behaviour** live in `opcodes.md` (the
`4/<minor>` rows) and `handlers.md` — they are **not duplicated here**. *([confirmed]* routing /
slot occupancy; every wire-byte VALUE semantic remains `[capture/debugger-pending]`.)*

**Installed live-handler minors (99 distinct handlers across 100 slots):**

> `1, 2, 3, 4, 5, 12, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 24, 25, 28, 29, 30, 35, 36, 37, 39,
> 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 54, 55, 56, 57, 58, 60, 61, 62, 63, 64, 65, 66, 70,
> 71, 72, 74, 75, 76, 78, 79, 80, 81, 82, 83, 84, 93, 95, 96, 97, 99, 100, 101, 102, 103, 105, 107,
> 108, 109, 113, 114, 115, 120, 122, 123, 125, 126, 132, 133, 134, 135, 137, 138, 139, 140, 142,
> 143, 144, 146, 148, 149, 150, 151, 152, 153`

Each of these minors maps to a `4/<minor>` row in `opcodes.md`; look the handler name up there. Slot
occupancy notes:

| Slot kind | Minors | Behaviour |
|---|---|---|
| Live handler | the 99/100 minors listed above | Installer overwrites the no-op fill with the per-opcode handler. |
| **Shared handler** | **143 and 144** | A single handler is written to **both** adjacent slots (one handler, two stores); it branches internally on the frame's minor. This is why "100 handler-pointer stores" but "99 distinct handlers". |
| **Explicit NULL (zero-clear)** | **0 and 27** | The installer's first two stores are explicit zero-writes (NOT the no-op pointer). These slots are **reserved/unused** — distinct from no-op-filled slots; nothing dispatches them in normal traffic. *([confirmed].)* |
| Inert no-op | every other minor in `0..153` | Keeps the constructor's no-op fill (returns success, no action). |
| Out of range | `≥ 154` | Not dispatched through the table (see §1.3 / §5 specials). |

**Routed OUTSIDE the table** (special-case dispatcher branches, NOT installed slots): `4/500` and
`4/50000` — these exceed the 154 bound by construction and are matched by literal compares (see
§1.3, last bullet). They have `opcodes.md` rows but no table slot.

### 2b. Push table (major 5) — installed-slot map

The Push installer writes **65 live handler slots and no zero-clears**; every store is a live
handler. As above, handler **names/sizes/behaviour** are owned by `opcodes.md` (the `5/<minor>`
rows) and `handlers.md` — **not duplicated here**. *([confirmed]* routing / occupancy; VALUE
semantics `[capture/debugger-pending]`.)*

**Installed live-handler minors (65):**

> `0, 1, 3, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 18, 21, 26, 28, 31, 32, 33, 34, 38, 39, 42, 51,
> 52, 53, 55, 57, 59, 61, 64, 65, 67, 68, 73, 76, 77, 79, 80, 85, 86, 87, 88, 89, 90, 91, 92, 93,
> 94, 98, 106, 121, 123, 124, 126, 127, 129, 130, 131, 136, 139, 146, 147`

Slot occupancy notes:

| Slot kind | Minors | Behaviour |
|---|---|---|
| Live handler | the 65 minors listed above | Installer overwrites the no-op fill with the per-opcode handler. |
| **Push minor 0** | **0** | A **real** live handler — NOT a zero-clear (contrast the Response table, whose minor 0 IS a NULL zero-clear). *([confirmed].)* |
| Inert no-op | every other minor in `0..153` (e.g. 27) | Keeps the no-op fill. |
| Out of range | `≥ 154` | Not dispatched at all (the dispatcher jumps to the ignore/free path; §1.3). |

> **No-gap cross-check.** Every installed `4/<minor>` and `5/<minor>` slot above has a matching row
> in `opcodes.md`, and every catalogued `4/`·`5/` row is either an installed slot here or one of the
> two documented major-4 dispatcher specials (`4/500`, `4/50000`). The installed dispatch surface
> and the `opcodes.md` S2C catalogue are in exact agreement — neither over- nor under-claims routing.
> *([confirmed].)*

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
connection-state**.

> **One shared event factory, two sub-codes (corrected — binary wins).** There is exactly **one**
> type-`15` event factory, not two. The same factory and the same type-`15` type byte produce **both**
> events on this branch: the **receive worker** posts a type-`15` event with **sub-code `100`** (a
> reassembled inbound frame to dispatch), and the **disconnect / teardown path** (§4.6) posts a
> type-`15` event with **sub-code `102`** (a connection-state transition / link gone). Both enter this
> same gate and are routed by the same `100`-vs-`102` discriminator. The disconnect event is therefore
> **not** a separate "close-event factory tag also numbered 15" — it is simply the umbrella's
> sub-code-`102` case from the one shared factory. (The earlier wording that treated the §4.6 close
> event as a distinct factory is dropped.)

Field value semantics carried inside a dispatched packet remain `[capture/debugger-pending]`.

### 3.2 The handler object and its embedded sub-objects

A dedicated **network-handler object** carries the dispatch logic. At construction it builds two
**embedded sub-objects** that partition handling responsibility:

| Sub-object | Role (LIKELY) |
|---|---|
| Guest sub-object | The pre-authenticated / lobby-side handling surface — constructed first and wired into the handler object's table; its constructor only stamps its own vtable pointer (no manager caching). |
| Member sub-object | The in-session handling surface — constructed second; on construction it caches a pair of internal manager references (an actor manager and an animation catalog) into shared slots for the handlers to reach. |

The two sub-objects are constructed as part of the single handler-object construction; their exact
runtime placement inside the handler object is an in-process layout detail with no wire effect and
is not promoted here.

**Two distinct actor-manager caches are taken at construction (corrected — binary wins).** *([confirmed].)*
The actor-manager reference is cached **twice**, by two different routines, into two different places:

| Cache | Cached by | Destination | Source singleton |
|---|---|---|---|
| Member-sub-object cache | the **member sub-object** constructor | a process-lifetime **shared global slot** | the actor-manager singleton |
| Parent-handler cache | the **parent handler-object** constructor | the handler object's **own field** | the actor-manager singleton |

Both reach the **same** actor-manager singleton; they are distinct caches (member→shared-global vs
handler-object→own-field), and the parent's own-field cache was previously unrecorded. Alongside the
member sub-object's actor-manager cache, the member constructor also caches an **animation catalog**
reference into the adjacent shared slot — and that "anim catalog" is precisely the
**actor-visual-global singleton** (the animation/visual catalog surface). So the member sub-object
caches the **actor-manager + actor-visual-global (anim catalog)** pair into adjacent shared slots,
while the parent handler separately caches the actor-manager into its own object field. These are
in-process layout facts with no wire effect.

### 3.3 Singleton accessor pattern

The handler object is reached through a **singleton accessor**: the first caller constructs the
object (and registers its teardown for process exit), and every later caller receives the same
instance. This is the conceptual pattern; no offsets-as-addresses are recorded.

---

## 4. Network-client lifecycle

The network client is a single object that owns the connection sub-object, the worker threads, and
the init-gate. Its lifecycle is: **construct → start engine → spawn workers → run the receive loop
→ keepalive → disconnect**. *(STRUCTURE-HIGH; build 263bd994.)*

> **Where this socket sits in the wider topology (cross-ref).** This lifecycle is the **single
> persistent game/world opcode connection** — it carries **all** opcode majors `0–5` (login + char-mgmt
> + game actions + the World Server Response/Push), connects **once** (after the lobby hands off the
> game address) and does **not** reconnect for enter-world. The client also opens two **non-opcode**
> socket families — the short-lived **login/lobby blocking queries** and the **XTrap** anti-cheat relay
> — which are NOT this connection. The full socket inventory, the `1/2`-ping resolution (it rides
> *this* socket, not a lobby socket), and the lobby→game address handoff live in
> **`connection_topology.md`** — cited here, not duplicated.

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

### 4.3 Thread model — THREE thread procedures (RESOLVES the old "second worker" open item)

There are **three distinct thread procedures**, owned by two distinct objects. The earlier wording
counted only the two threads the network-client itself spawns at start-engine, and left the genuine
socket I/O thread as an open "second worker" item (old §8). That item is now **resolved**: the
real socket worker is a **third** thread owned by the *embedded connection sub-object* and spawned
at connect time, not by the network-client's start-engine path. *([confirmed]; build 263bd994.)*

| # | Thread procedure | Spawned by | Owner object | Duty |
|---|---|---|---|---|
| 1 | **Receive consumer** ("network worker") | network-client start-engine spawn (worker slot at client `+82312`) | network-client | Pops a *fully-reassembled* frame off the connection's receive queue and posts a dispatch event (§4.4). Pure producer → dispatch; never touches the socket. |
| 2 | **Keepalive timer** ("network live") | network-client start-engine spawn (worker slot at client `+82328`) | network-client | A 1-second periodic timer loop; re-sends the cached `(2,10000)` keepalive frame when the link has been idle (§4.5a). Not a socket thread. |
| 3 | **Connection I/O thread** (the real socket worker) | the connection's **connect routine**, via the C runtime thread-spawn call (`_beginthreadex`) | embedded connection sub-object | Overlapped `WSARecv`-completion → **frame reassembly** → push complete frames onto the receive queue → re-arm `WSARecv`; also services the **send-signal** event (→ `WSASend`) and the shutdown/close events (§4.4a). |

The **start-engine spawn** (called only by §4.2) arms the *network-client's* two threads:

- Creates a **manual-reset stop event** the two network-client worker loops watch to know when to
  exit.
- Arms **two "alive" flags**, one per network-client worker slot.
- Starts **worker slot 1 (receive consumer)** and **worker slot 2 (keepalive timer)**.
- Applies a **thread priority** to each started thread (a generic "set priority if the handle is
  valid" step, not network-specific).

> **Resolution of the old "two worker thread slots" wording.** The two slots the network-client
> start-engine path creates are the **receive consumer** and the **keepalive timer** — NOT a
> socket worker. The genuine socket-read/reassembly/send-pump thread is thread #3, owned by the
> embedded connection sub-object and spawned in its **connect** routine via the C runtime
> thread-spawn call. The recv-consumer thread and the connection I/O thread hand off through the
> **connection's receive queue** (the connection I/O thread is the producer; the recv consumer is
> the consumer). *([confirmed].)*

### 4.4 The receive-consumer loop (thread #1)

The receive consumer is a thread procedure (tagged with a "network worker start/end" marker) that
runs until the stop event is signalled. Each iteration: *(STRUCTURE-HIGH; build 263bd994.)*

1. **Pop** one *already-reassembled* frame from the **connection's receive queue** (the queue owned
   by the embedded connection sub-object; a pop with a short spin-wait until an item is available).
2. **Push** a **dispatch event** onto the network-event sink (§3.1) so the dispatcher (§1) will act
   on that frame on the consuming side. This is the **type-15 / sub-code-100** event of §3.1.
3. **Dispose** the frame: the dispatched inbound packet buffer is freed (an empty no-op hook on the
   buffer-free path, then the raw block free). Disposal is the consumer's responsibility once the
   dispatch event has been posted.

So the receive consumer never reads the socket and never decodes packets — it is a pure producer
that moves an already-reassembled frame into a dispatch event and then releases the buffer; all
socket I/O and reassembly happen on thread #3, and all decode/routing happens on the dispatcher
side.

### 4.4a The connection I/O thread (thread #3 — the real socket worker)

The connection's I/O thread is spawned by the connection's **connect** routine: the connect routine
opens the socket, creates a small set of **auto-reset events** (receive-completion, send-signal,
shutdown, graceful-close), starts the I/O thread via the C runtime thread-spawn call, and kicks it.
The thread then loops on a wait over those events: *([confirmed]; build 263bd994.)*

- **Receive-completion event** → collect the overlapped `WSARecv` result, advance the fill count of
  the connection's **receive reassembly buffer**, then run a **frame-reassembly inner loop**: while
  at least a header's worth of bytes is buffered, read the **frame size word at the frame start**,
  and if a whole `size` bytes are present, copy the frame into a fresh frame buffer, **push it onto
  the receive queue** (under the receive-queue lock), fire an optional notify callback, and advance
  the consumed offset by `size`; otherwise break and **re-arm** the overlapped `WSARecv` (compacting
  the buffer first if it has filled past its compaction threshold).
  > **Reassembly reads the LOW 16 bits of the size word.** At the frame start the I/O thread reads
  > the frame length as a **u16** even though the wire/header size field is the u32 at `+0` (§1.1).
  > This is safe because frames never exceed the receive capacity (≈ 11.6 KB, well under 64 KB), so
  > the high word of the size is always zero. The header layout owned by `opcodes.md` is unaffected:
  > on the wire the size is a u32; the reassembler simply uses its low half as the frame length.
  > *([confirmed].)*
- **Send-signal event** → under the send-queue lock, if a frame is queued, perform the actual
  `WSASend` of the head frame (fill a socket buffer descriptor from the frame pointer and the frame's
  u32 size word, post the overlapped send, then free the frame). This is the same send-drain step
  the inline send path also triggers (§6.1), so queued sends are serviced **both inline from the
  send convergence and from this I/O thread**.
- **Shutdown event** → the thread returns (exits).
- **Graceful-close event** → re-arm receive; on failure run the connection shutdown, push a
  sentinel onto the receive queue, and fire the notify callback.
- On any receive error → connection shutdown + record the last-error string, and the loop exits.

> **Why the reassembly loop is mandatory — Nagle coalescing (interop note).** TCP framing here is
> **not** one-segment-per-message. The **server leaves Nagle's algorithm enabled** (it does **not** set
> `TCP_NODELAY`) on its game connection — a maintainer-observed runtime fact, and most pronounced for
> the **World Server** (the high-rate major-4 Response / major-5 Push traffic). Nagle batches small
> writes, so the server can **coalesce several application frames into a single TCP segment**, and a
> single frame can equally be **split across segments**. The client end is **symmetric**: the only
> socket option the client sets on its connection is the **receive-buffer size (`SO_RCVBUF`, at
> `SOL_SOCKET`)** — it **never sets `TCP_NODELAY`**, so the client's own send path is likewise
> Nagle-subject and may batch its outbound frames. *([confirmed]* on the client side — the sole
> `setsockopt` call sets `SO_RCVBUF`; there is **no** `TCP_NODELAY` call anywhere in the client. The
> server-side Nagle posture is a **maintainer-observed runtime fact**, not a static claim derived from
> the client.)* This is **precisely why** the inner loop is length-prefix-driven: it reads the 8-byte
> header's `size` word and emits a frame only once `size` bytes are buffered, then advances by `size` —
> tolerating **N frames per `recv`** and a **partial trailing frame**. A faithful re-implementation
> **must** frame on the header `size`, never assume "one `recv` = one message", size its receive
> reassembly buffer for coalesced bursts (the decode capacity is `0x2DA0` = 11680 bytes, §6.2), and
> leave its own send path free to coalesce — otherwise it diverges from the original's on-wire batching.

So thread #3 is the **socket-read + frame-reassembly + send-pump** thread; thread #1 (the receive
consumer) only lifts *already-reassembled* frames into dispatch events. The two hand off through the
connection's **receive queue**; the send path hands off through the connection's **send queue**
(producer = the send convergence of §6.1; consumer = this I/O thread's send-signal step). The
queue/buffer object offsets are the P5-lane struct deliverable — see `structs/` (the network-client /
connection-sub-object layout) — and are **not** re-tabled here.

### 4.5 Keepalive — TWO distinct mechanisms

There are **two separate keepalive mechanisms**, both real; which one is actually sent on-wire (and
at what cadence) is `[capture/debugger-pending]`. Record both.

**(a) Ctor-armed compressed periodic frame.** *([confirmed]* it is armed; the period value, the body
width and the timer-send predicate are all read from the routines).* The client arms a **periodic
compressed keepalive**: a keepalive frame is built **once, inline in the network-handler
constructor** (there is no per-call builder), run once through the outbound compress stage (§6), the
compressed buffer is stored in a client slot, the original buffer is released if a new one was
produced, and a **periodic interval** is recorded (the recovered interval is `1000 ×` the routine's
argument — i.e. a per-second-scaled period). Thereafter the pre-compressed keepalive frame is sent
on that interval. As constructed in the network-handler constructor, this armed frame carries opcode
**`(2, 10000)`** with an interval argument of **20**, i.e. a **20000 ms (20 s) period**.

> **Body width — CORRECTED (binary wins).** The `(2,10000)` keepalive body is **4 bytes: one `u32`
> appended with value `0x00000000`** — NOT header-only. The total wire frame is therefore **12
> bytes** (`[u32 size=12][u16 major=2][u16 minor=10000][u32 body=0]`). Because the body is non-empty,
> the frame is genuinely compressed at arm time (it does *not* take the `size == 8` header-only
> bypass of §6). The cached frame is **pre-compressed** and re-sent verbatim by the timer thread;
> it is **not** re-ciphered or re-compressed per send. *([confirmed]* shape; the meaning of the zero
> body word is `[capture/debugger-pending]`.) `opcodes.md` carries the `2/10000` row with this
> corrected "body = `u32` (observed 0), 12-byte frame, pre-compressed, timer-sent at 20 s when idle"
> note.

**Timer-send predicate (thread #2).** The keepalive-timer thread (§4.3, thread #2) sleeps ~1 s per
iteration while a master "network-live" flag is set, and on each tick re-sends the cached frame
**only when the link has been idle** — i.e. when `now − last-send-tick > interval` and a suppression
flag is clear. The send convergence (§6.1) stamps the network-client's **last-send activity clock**
on *every* outbound packet, so any normal traffic in the preceding 20 s suppresses the redundant
ping. The timer re-sends the cached pre-compressed frame straight onto the send queue (bypassing the
send convergence so the cached bytes are not re-transformed). *([confirmed]* shape/predicate; the
wall-clock cadence on the wire is `[capture/debugger-pending]`.)

**(b) Runtime C2S `2/112` toggle.** *([confirmed]* it exists as a 1-byte C2S send gated by a master
flag; its cadence is capture-pending.)* A separate **C2S `2/112` one-byte toggle** is gated by a
**master-enable flag** that is *set on world-enter and cleared on leave*. This is the runtime,
in-session keepalive path, distinct from the `(2,10000)` frame armed at construction.

> These are **two distinct mechanisms**, not one read two ways: `(2,10000)@20 s` is the
> construction-time armed periodic frame; `2/112` is the world-session 1-byte toggle. They are
> **not reconciled to a single on-wire cadence** — the actual keepalive traffic and timing is
> `[capture/debugger-pending]`. The `opcodes.md` catalogue carries **both** rows (`2/10000` and a
> `2/112` keepalive-toggle row).

### 4.5a Link-health ack handshake — inbound `5/146` request → C2S `2/146` reply

Distinct from the two keepalive mechanisms above, the protocol carries a **server-initiated
ack-request / client-reply** round-trip used to confirm packet delivery. *([confirmed]* routing /
sizes / reply opcode; the field VALUE meanings are `[capture/debugger-pending]`.)*

- **Inbound `5/146`** routes through the Push table (§2b) to its installed handler. The handler reads
  an **8-byte body** — two `u32`s: a **request id / sequence** word and a **request token** word.
- The handler **validates the token against a locally-tracked pending-request list** (a list of
  fixed-size records keyed on the token, with a state filter). **If no matching pending request is
  found, the handler does nothing — no reply is sent.** The client only acks requests it actually has
  outstanding.
- On a match it surfaces a preset coloured system message (a message-DB string, id `[capture-pending]`)
  and then **builds and sends a C2S `2/146` reply** with an **8-byte body**: `[u32 echoed request id]`
  (the inbound id echoed back) followed by `[u32 local counter / state]` (a local ack counter). The
  reply goes out through the normal send convergence (§6.1 — cipher + compress + queue).

> **One-line summary.** Server sends **`5/146`** `[u32 req_id][u32 token]`; the client, *iff* it has
> that request pending, replies with **C2S `2/146`** `[u32 echoed_req_id][u32 local_counter]`. So
> **`5/146`'s reply opcode is `2/146`.** `opcodes.md` owns both catalogue rows; `handlers.md` owns
> the per-handler behaviour. *([confirmed]* shape; the meaning of `token`, `local_counter` and the
> system message id are `[capture/debugger-pending]`.)

### 4.6 Disconnect

Disconnect is the connection-teardown path:

| Step | Detail |
|---|---|
| Connection shutdown | Shuts down the embedded connection sub-object: signals its own stop event, waits its I/O thread, tears down and closes the socket, and closes its event/thread handles. |
| Post disconnect event | Builds a **disconnect/close network event** through the **same shared type-`15` event factory** the receive worker uses, but stamped with **sub-code `102`** (vs the receive worker's sub-code `100`), and pushes it onto the dispatch sink so the rest of the client learns the connection is gone. It enters the §3.1 gate and is routed by the sub-code-`102` branch into the connection-state machine (§5). |
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

> **Prime-then-resolve cross-link (load-bearing).** The `3/100` packet handler is what *posts* the
> codes **`202` / `203` / `232`** into the game-state machine's pending-result slot **and sets the
> game-state code to `2`** (the connect-progress state). The connection-state machine documented here
> then, on the next `102` connection event, **consumes and clears** exactly those `202/203/232` codes
> from that pending slot. So **`3/100` (packet path) PRIMES `GameState = 2`; this connection-state
> machine (the `102` path) RESOLVES it.** Code `201` is the only one this machine itself *publishes*
> (outward, via the shared select-screen publish method) — it never *stores* `201`; the machine only
> stores its own sentinel and clears the externally-posted `202/203/232`. This is the concrete edge
> between packet dispatch (§1) and the connection-state machine. The shared publish method and the
> full `3/100` code set are owned by `handlers.md` §12 — cited, not re-tabled. *([confirmed]* control
> flow; the meaning of each code is `[capture/debugger-pending]`.)

> **PHANTOM REFUTED — there is NO `5000 / 10000 / 10001` "string-id class".** An earlier framing
> grouped these three integers as a select-screen *message-string-id* family; the binary disproves
> it. They are **three different things**, none a string-id: **`5000`** is a UI **display-duration /
> timer-delay in milliseconds** (e.g. "show this status line for 5 s", or the 5 s arm for the timed
> event below); **`10000`** is the **opcode minor of the `2/10000` keepalive** *and* the 10 s
> timer-delay variant; **`10001`** is the **timed-event tag** (the connection/scene retry-or-timeout
> tick driven off the deferred timer, §5.1). Do **not** model any `5000/10000/10001` string-id class;
> the select-screen messages are driven by separate message-DB string-id handles (owned by
> `handlers.md`), not by these integers. *([confirmed]* integer roles; the human text of any message
> id remains `[capture/debugger-pending]`.)

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

> **Send fan-in (corrected count — binary wins).** The convergence is reached by **105 distinct
> builder functions emitting 104 unique `(major, minor)` opcodes** — 105 call sites, each a distinct
> caller, every opcode statically pinned. The **one opcode collision** is `2/52`: it is emitted by
> **two** different builders — a **short-form** skill-use request and a **full-form** skill-use
> request — so the 105 builders map onto 104 unique opcodes. (Earlier "~104 builders" wording is
> corrected to **105 builders / 104 unique opcodes**.) The per-builder opcode census is owned by the
> C2S catalogue in `opcodes.md`; what each builder's payload bytes *mean* is `[capture/debugger-pending]`.

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
RECEIVE:  socket → [I/O thread #3: WSARecv completion → frame reassembly] → conn recv queue
                 → [recv consumer thread #1 pops] → dispatch event (type-15 / sub-code-100)
                 → [LZ4 decompress if not header-only]   (crypto.md §3.2 inverse)
                 → master dispatcher (§1) → family routing → installed handler (§2/§2a/§2b)

SEND:     handler builds payload → send convergence (§6.1):
                   GetTickCount activity stamp → byte cipher (crypto.md §3.1)
                 → LZ4 compress (crypto.md §3.2) → conn send queue
                 → WSASend (inline kick AND/OR I/O thread #3 send-signal)
          (header-only frames, size==8, pass through both transforms untouched)
```

---

## 7. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| Master dispatcher routing fan-out (§1) | Per-opcode routing list + dispatch-model summary → `handlers.md` §1; wire header → `opcodes.md` |
| `(0,0)` key-exchange handshake + emitted `1/4` reply (§1.4) | RSA / whitening of the `1/4` reply → `crypto.md` + the login-credential packet spec |
| Handler-table installers + install counts (§2) + the **installed-slot maps** (§2a/§2b) | Per-handler **names / sizes / behaviour** for each `4/`·`5/` minor → `opcodes.md` rows + `handlers.md` §1 (NOT duplicated here) |
| Network-event entry (type-15 / 100 vs 102) + handler object (§3) | (machinery only here) |
| Three-thread model + lifecycle + keepalive duality + `5/146`→`2/146` ack (§4) | `2/112` toggle · `2/10000` armed frame · `5/146`/`2/146` ack rows → `opcodes.md`; per-handler ack behaviour → `handlers.md`; network-client / connection-sub-object / secure-handshake-context **struct offsets** → `structs/` (the P5-lane deliverable, cross-ref only) |
| Connection-state machine + codes + the `3/100`→`GameState=2` prime/resolve edge (§5) | Lobby action-code mapping + the full `3/100` code set + the shared publish method (shared numbers, different path) → `handlers.md` §12 |
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
- **Install raw-store counts — CORRECTED (binary wins).** The Response installer emits **102** store
  instructions (2 zero-clears at minors `0`/`27` + 100 handler-pointer stores, one handler shared
  across slots `143`/`144`); the Push installer emits **65** stores (all live, no zero-clears). Earlier
  101/66 counts were off by one (the Push "+1" was the `ret` epilogue; the Response "+1" is the second
  store of the shared `143`/`144` handler). Derived counts unchanged: **100 Response slots / 99 distinct
  handlers / 2 NULL slots (0, 27) / one live default** (§2).
- **Inbound cipher omission on the CLIENT side — RESOLVED (positive single-caller proof).** The byte
  cipher has exactly one cross-reference (the outbound send gate), so it is structurally unreachable
  on receive; client inbound is **decompress-only** (§6.2). Crypto OQ#1 is resolved for the client
  receive path. (Generalising the omission across *all* inbound types on the wire stays
  capture-pending — `crypto.md` §5.)

### Resolved this pass (CYCLE 2 extension — control-flow-confirmed)

- **"Second worker's exact duties" — RESOLVED.** The old open item is closed by the **three-thread
  model** (§4.3): the two threads the network-client spawns at start-engine are the **receive
  consumer** and the **keepalive timer**; the genuine socket worker is a **third** thread owned by
  the embedded connection sub-object and spawned by its **connect** routine (via the C runtime
  thread-spawn call). Thread #3 does `WSARecv`-completion → **frame reassembly** → receive queue →
  re-arm, and also services the **send-signal** event (→ `WSASend`); its loop is now decomposed in
  §4.4a. The recv consumer and thread #3 hand off through the connection's **receive queue**.
- **`2/10000` keepalive body — CORRECTED.** The body is **4 bytes (one zero `u32`)**, 12-byte wire
  frame, pre-compressed at arm time, timer-sent at 20 s **only when idle** (§4.5a). It is **not**
  header-only and does **not** take the `size == 8` bypass. `opcodes.md` carries the corrected row.
- **`5/146` ack-request — RESOLVED.** Inbound `5/146` `[u32 req_id][u32 token]`, validated against a
  local pending-request list, replies with **C2S `2/146`** `[u32 echoed_req_id][u32 local_counter]`
  (§4.5a). `5/146`'s reply opcode is `2/146`.
- **Install-table slot maps — ENUMERATED (§2a/§2b).** All **99 distinct Response handlers across 100
  slots** (with the shared `143`/`144` handler and the explicit NULL zero-clears at `0`/`27`) and all
  **65 Push handlers** (with live `5/0` and no zero-clears) are mapped slot-by-slot; the installed
  surface matches the `opcodes.md` S2C catalogue with **zero gap** (modulo the two `4/500`/`4/50000`
  dispatcher specials).
- **`3/100` → `GameState = 2` prime/resolve edge — RECORDED (§5).** `3/100` posts `202/203/232` into
  the pending slot and primes `GameState = 2`; the connection-state machine consumes/clears them on
  the next `102` event. Code `201` is published-outward only, never stored.
- **PHANTOM `5000/10000/10001` "string-id class" — REFUTED (§5).** No such string-id family exists:
  `5000` = display-duration / timer-delay (ms); `10000` = the `2/10000` keepalive minor + 10 s
  timer-delay; `10001` = the timed-event tag. Do not model a string-id class on these integers.

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
  the ctor-armed `(2,10000)@20 s` frame (body now confirmed = one zero `u32`, §4.5a) and the runtime
  `2/112` toggle. The *shape* of each (body, interval source, trigger sites) is confirmed; which is
  actually observed on the wire, and at what wall-clock cadence, is `[capture/debugger-pending]`.
- **Wire-VALUE meanings of the keepalive/ack fields.** The `2/10000` zero body word, the `2/112`
  toggle byte (observed `0x01`), and the `5/146`/`2/146` `token` / `local_counter` words are all
  `[capture/debugger-pending]` — only their widths and round-trip shape are confirmed.
- **Inbound cipher asymmetry (generalisation).** The client-side "decompress-only" placement is
  proven (§6.2); whether the server omits an inverse cipher across *every* inbound type is the
  `crypto.md` §5 open question this spec inherits but does not resolve.
