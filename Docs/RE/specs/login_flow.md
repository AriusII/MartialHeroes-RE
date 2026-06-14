# Login Flow Specification — Login, Character Select, and Enter-Game

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the protocol spec-author.
> No code, no decompiler identifiers, no addresses. Behavior, state machines, tables, and constants
> only. Korean text on the wire is **CP949** (EUC-KR superset) — never a managed string in wire
> structs.
>
> **Scope:** the end-to-end client connection lifecycle from the login form through character
> selection to spawning into the world, plus the process/socket boundary between the legacy lobby
> discovery surface and the main game connection. This spec describes *behavior and state*; the
> per-message byte layouts it references live in `packets/*.yaml` and the opcode routing lives in
> `opcodes.md`. Cross-cutting cryptography is owned by `specs/crypto.md` and only cited here.
>
> **Implementation targets:** the state machine and flow are realized in `Client.Application`
> (use cases + packet handlers) and `Client.Domain`; the wire I/O is `Network.Protocol` /
> `Network.Transport.Pipelines`; the handshake is `Network.Crypto`.

---

## 0. Status header — confidence per claim

`capture_verified: false`. **No live network capture (`.pcapng` / `.tsv`) was available for this
analysis.** Opcode routing is a hard static fact (corroborated by `opcodes.md`, which reads the
client's dispatch installers). All field offsets/sizes and the lobby (port-10000) mini-protocol are
static inferences unless explicitly marked otherwise.

`sample_verified: partial`. **One real on-disk sample was available** — the local client version
file `data/cursor/game.ver` (28 bytes, 7 little-endian u32 fields). It pins the enter-game version
token value (Section 3.3) and is the only real-byte corroboration in this analysis. It is **not** a
network capture, so it does not validate any wire layout; it validates a single derived constant.

`runtime_observed: login blob (1/6)`. **A live debugger session drove a real login through the
client and read the assembled login blob out of the packet buffer at build time** — a runtime
(code-confirmed) observation of the live process, not a network capture. It pins the login blob's
field order, the **u32 little-endian** length-prefix width, the **NUL-inclusive** prefixed lengths,
the three field capacities (account / password / PIN), and the identity of the previously-unnamed
optional field as the **second-password / PIN**. It also re-confirms the plaintext-header /
encrypted-payload framing rule owned by `specs/crypto.md`. These claims are graded RUNTIME-CONFIRMED
below; they carry **no addresses** — only observed behaviour and observed byte layout. (Note: this
runtime read resolves the *login blob structure* only; it does **not** resolve the 1/6 opcode
collision with character-create — see Section 4.2 and Section 9.)

| Claim | Confidence | Basis |
|---|---|---|
| Two distinct socket surfaces exist: a synchronous lobby discovery channel and the main overlapped game connection. | HIGH | Static — two independent connect paths and frame-handling regimes. |
| The lobby mini-protocol reuses the 8-byte frame wrapper + inbound LZ4, but **no** `(major:minor)` dispatch and **no** byte cipher. | HIGH | Static. |
| Lobby base port = **10000**; channel port = `10000 + selected_offset`. | HIGH | Static constant. |
| Default fallback server IP **`211.196.150.4`**; IP override file **`ip.txt`** (single token, ≤19 chars). | HIGH | Static constant / string evidence. |
| The full login/char/enter-game flow runs **inside the game client process itself** in this build (no separate lobby/online child executables are spawned). | HIGH | Static — no references to sibling lobby/online executables as spawned children. |
| Overall ordered flow (Section 1) and its UI/net state transitions. | MEDIUM–HIGH | Static behavioral read of the login window state machine. |
| The `0/0 → 1/4` RSA handshake and credential staging. | HIGH (cited) | `specs/crypto.md` — not re-derived here. |
| Login blob (1/6) starts with sub-opcode byte **`0x2B` (43)** then a length-prefixed account string and a length-prefixed **second-password / PIN** string; the account password travels only via the RSA 1/4 reply. | HIGH (runtime) | RUNTIME-CONFIRMED — the assembled blob was read out of the live client's packet buffer; field order, prefix width, and field identities observed directly (no addresses). |
| Login blob length-prefix width = **u32 little-endian**, and each string's prefixed length **includes its trailing NUL**. | HIGH (runtime) | RUNTIME-CONFIRMED — observed in the assembled bytes. |
| Login field capacities: account **< 20** chars, password **< 17** (staged in an exactly-17-byte zero-padded buffer), second-password / PIN **< 5** (≤ 4 chars + NUL). | HIGH (runtime) | RUNTIME-CONFIRMED — observed validation bounds + the staged buffer size. |
| Server-list record (8 bytes) decodes to **{server id, status, load, open-time}** with the load thresholds and status sentinels of Section 2.1. | MEDIUM–HIGH | Static — recovered from the server-list render path plus a debug format literal; the open-time packing and full status enum are **capture-unverified**. |
| Character-list (3/1) shape: 3-byte header + per-slot **981-byte** records gated by a slot bitmask. | MEDIUM–HIGH | 981-byte stride is dispatch-path-confirmed; field internals capture-unverified. |
| The character list supports a **maximum of 5 slots** (slot indices 0..4). | HIGH (static) | The parse loop is a **hard, bounded iteration of exactly 5**, not an open-ended scan — promoted from "inferred ≤5" to a fixed constant. |
| Enter-game request (1/9) = **40-byte** body; slot index at +0; version token elsewhere in the buffer. | MEDIUM | 40-byte total + slot@0 are firm; token offset capture-unverified. |
| Enter-game version token derivation `10 × versionField + 9`, and its concrete value **21149** for the sampled `game.ver`. | HIGH (sample) | `sample_verified` — computed from the real on-disk `data/cursor/game.ver` field value 2114. |
| Enter-game ack (3/5) = **44-byte** block; name@0, billing u32 @ +28, char-count u32 @ +40. | MEDIUM–HIGH | 44-byte total dispatch-confirmed; block internals partly capture-unverified. |
| Char-spawn result (3/7) = **16-byte** block; result + slot + three spawn-param u32s. | MEDIUM | 16-byte total dispatch-confirmed; param meaning capture-unverified. |
| GameState transitions: char-list → select scene; enter-game ack → in-world. | MEDIUM–HIGH | Static behavioral. |

**Naming authority:** the legacy binary's internal handler *names* disagree with both the dispatch
arithmetic and `opcodes.md` (a known, flagged inconsistency). **This spec anchors every opcode to
`opcodes.md` by behavior, never to a legacy handler name.** See Section 6.

---

## 1. End-to-end flow (behavioral)

The client connection lifecycle is a single state machine. It tracks two independent state values:

- a **screen substate** (which UI page is shown), and
- a **net-handshake state** (which networking step is in progress).

The ordered lifecycle is:

1. **Login form.** The player enters an account name and a password (and the login window also
   carries a third input concept, the second-password / PIN — see step 1a). On submit, the client
   packs the form into a single **TAB-delimited string** of the shape
   `account ⟨TAB⟩ [optional] ⟨TAB⟩ field ⟨TAB⟩ host␠port` (a literal U+0009 between tokens; the
   final token holds the discovered game endpoint as `host` and `port` separated by a space). This
   string is handed to the secure-context builder (Section 4).

1a. **Second-password / PIN entry.** After the primary account + password submit, and **before** the
   login blob is built and sent, the client raises a dedicated **second-password / PIN** input (a
   modal collecting a short numeric PIN of **≤ 4 characters**). The client models this PIN as a
   **first-class input concept** (an input/name object carries an explicit "is-PIN" flag), distinct
   from the account and the password. The PIN's value becomes the **optional length-prefixed field
   of the login blob** (Section 4.2); it is *not* the account password (the password is staged
   separately as the RSA 1/4 plaintext). The front-end shape of this modal is owned by
   `specs/frontend_scenes.md` §1 (its "Second-password / PIN" subsection); this spec owns where the
   PIN lands on the wire.

2. **Server-list fetch (lobby).** A background worker performs a **synchronous, blocking** connect
   to the lobby on **port 10000** (Section 2.1), reads an 8-byte frame wrapper plus an LZ4-compressed
   payload, and decodes a list of fixed **8-byte server records**. The wrapper's `major` field
   carries the **server count**. Records and count are stored in the login window.

3. **Server select.** The player picks a server; the selection index is recorded.

4. **Channel-endpoint fetch (lobby).** A second background worker connects **synchronously** to
   **`port 10000 + selected_offset`** (Section 2.2), reads the same 8-byte wrapper + LZ4 payload, and
   takes the **first 30 decompressed bytes** as a NUL-padded ASCII **`host port`** endpoint string.
   This resolves the game server the client will connect to.

5. **Credential submit / join game connection.** The client builds the secure-session context from
   the TAB-delimited form string (Section 4), sets the resolved login endpoint (host + numeric port,
   the port parsed from the endpoint string), and stages the account-login blob (account + PIN,
   per step 1a / Section 4.2). **This is the join point** where the flow leaves the lobby surface
   and engages the **main game connection** and its `0/0` / `1/4` RSA handshake (cited from
   `specs/crypto.md`).

6. **Key exchange.** Server → client `0/0 SmsgKeyExchange` arrives on the game connection; the client
   replies inline with `1/4 CmsgAuthReply` (RSA-encrypted staged credential). The session is then
   marked secure. (Cited — `specs/crypto.md` §6.)

7. **Character list.** Server → client `3/1 SmsgCharacterList` drives the client into the
   **character-select scene** (Section 3). The list enumerates the account's character slots.

8. **Slot management (optional).** From the select scene the player may **create**, **delete /
   manage**, or **rename** a character; the server answers with the corresponding character-management
   responses (Section 5).

9. **Enter game.** The player confirms a slot. The client sends `1/9 CmsgEnterGameRequest`
   (40-byte body, Section 3) and **caches the chosen slot's character record locally** for the world
   load. The server answers `3/5 SmsgEnterGameAck` (44 bytes) → the client transitions to the
   **in-world game state**. A subsequent `3/7 SmsgCharSpawnResult` (16 bytes) then drives the actual
   spawn into the world, consuming the cached character record. Ongoing world state thereafter is
   maintained by the major-5 Push family (out of scope here).

### 1.1 Net-state progression (summary)

The lobby phases advance through a small set of "start → wait → consume" net states, one triplet per
synchronous fetch:

| Phase | Start | Wait (recv loop) | Consume |
|---|---|---|---|
| Server-list fetch | begin connect to 10000 | block until wrapper + payload read | parse records, advance |
| Channel-endpoint fetch | begin connect to `10000 + offset` | block until wrapper + payload read | extract endpoint string, advance |
| Credential submit | build secure context, set endpoint, stage login blob (account + PIN) | hand off to game connection | join `0/0` handshake |

The exact internal state indices are an implementation detail of the legacy client and are **not**
contractual; a fresh implementation should model these as its own well-named states.

---

## 2. The lobby mini-protocol (synchronous, port 10000)

The lobby surface is a **separate, plaintext-framed, non-dispatched** protocol, distinct from the
main game connection. It:

- runs on its **own synchronous blocking socket** (not the overlapped game-connection pipeline),
- does **not** use the `(major:minor)` dispatcher,
- does **not** apply the per-packet byte cipher,
- but **does** reuse the same **8-byte frame wrapper** and the same **inbound LZ4 decompression** as
  the game connection (see `specs/crypto.md` §3.2 for the LZ4 variant — raw block, no frame magic).

The 8-byte wrapper is read as: a **frame size** (low 16 bits at the start of the wrapper), a `major`
field (u16 at wrapper offset +4), and a `minor` field (u16 at wrapper offset +6). On the lobby
surface, **`major` is repurposed as a count/length signal** (Section 2.1) and **`minor` is unused**.

### 2.0 Connect behavior

The lobby connect helper:

1. Initializes Winsock (version 2.2).
2. Resolves the server IP, in priority order:
   - if a local **`ip.txt`** file is present, reads a single whitespace-free token from it,
     truncated to **19 characters**, and uses it;
   - otherwise attempts a connection-info resolve list and, on success, uses a selected record's
     host;
   - otherwise falls back to the hardcoded default **`211.196.150.4`**.
3. Opens a TCP stream socket and connects to `{ host, port }`.
4. Connection failure is **non-fatal to the helper** — it logs the error and still returns the
   socket; the caller detects failure by the absence of a valid reply.

The base port is **10000**. Both lobby fetches use the same wrapper + LZ4 receive pattern below. The
receive loop is a cooperative blocking read: it retries on a "would block" condition with a short
back-off sleep until the full wrapper, then the full `size − 8` payload, have arrived; then it LZ4-
decompresses.

### 2.1 Server-list fetch (port 10000)

Receive sequence:

1. Block until the **8-byte frame wrapper** has been fully read (with cooperative back-off on
   would-block).
2. From the wrapper, read the **frame size** (low 16 bits) and the **`major`** field.
3. Block until the remaining `size − 8` payload bytes are read.
4. **LZ4-decompress** the payload (inbound, raw block).
5. Interpret `major` as the **server-record count**. Read `count` fixed-size records of **8 bytes**
   each into the login window's server array, recording the count. (When the count is positive, any
   prior array is freed and a fresh `count × 8` buffer is allocated and filled from the decompressed
   payload. On connect failure the stored count is set to a sentinel `-1`.)

So: **server-list payload = `count` × 8-byte records, where `count = wrapper.major`.**

#### Server-list record (8 bytes) — decoded

Each record is four little-endian 16-bit fields (record stride 8). The meanings were recovered from
the login window's server-list **render** path: a debug format literal of the shape
`"i %d status %d count %d"` ties the fields at record offsets +2 and +4 to "status" and "count"
(load), and the field at +0 is the index fed to the client-local server-name lookup. **Wire-relevant
fields only** — the localized name itself is *not* on the wire (see the name-table note below).

| Offset | Size | Field | Meaning | Confidence |
|---|---|---|---|---|
| +0 | 2 (u16) | `server_id` | Index **1..40** into the client-local localized server-name table. Values outside 1..40 are out of range. | HIGH |
| +2 | 2 (i16) | `status_code` | Server availability state. A value `≤ 0` or `> 40` is treated as **invalid / unavailable** (an error label is shown). Special values: **3** = "open-time scheduled" (the open time is rendered from `open_time` at +6); **24** = a distinct "preparing / check" fixed label; **100** = "this is the connected / current selection" sentinel that, with a UI flag set, auto-advances into the channel-connect step. | MEDIUM–HIGH |
| +4 | 2 (i16) | `load` | Population / load gauge. Compared against thresholds **1200 / 800 / 500** to choose a colored "load" label and text color (e.g. red above the high threshold, intermediate colors below). | MEDIUM–HIGH |
| +6 | 2 (i16) | `open_time` | Used **only** when `status_code == 3`: rendered as a packed open-hour / open-minute schedule using a `×10` split (the field, and the `load` field, are each split as `value/10` and `value%10` to feed an `HH:MM`-style display). The exact packing is **inferred, not pinned**. | LOW–MEDIUM |

**Server-name table (client-local, NOT wire data).** The localized server name shown in the UI is
**not** transmitted. The wire carries only the numeric `server_id` (+0); the client resolves it
through a local **41-entry name table** built from UI string resources (one bank per language /
region). `server_id` indexes that table. A fresh implementation must supply its own name map; the
protocol only needs the numeric id.

**Render/UI constants (load-bearing for a faithful select screen, not for the wire codec):** the
load-color thresholds **1200 / 800 / 500** and the status sentinels **3 / 24 / 100**. These are
client-side presentation logic; they are documented here so a re-implemented select screen behaves
identically, but they are **not** part of decoding the 8 wire bytes.

> **Confidence note.** The 8-byte stride and `count = wrapper.major` are firm. The four-field
> decode is a static read of the render path corroborated by the debug literal; the `open_time`
> packing and the full `status_code` enum (beyond the special-cased 3 / 24 / 100) are
> **capture-unverified**. A live lobby capture is still required to confirm record internals.

### 2.2 Channel-endpoint fetch (port `10000 + offset`)

After server selection, the client connects to `10000 + selected_offset`, where `selected_offset` is
the chosen server/channel index recorded at server-select time. The receive sequence is identical to
§2.1 (8-byte wrapper + LZ4). After decompression:

- The client takes the **first 30 (0x1E) bytes** of the decompressed payload as a **NUL-padded ASCII
  `host port` string**. The buffer is zero-filled first; the 30 bytes are **not guaranteed
  NUL-terminated**.
- This string is the game server's endpoint. It is later split on the space into host and a numeric
  port (parsed as a decimal integer).

> **Channel-endpoint payload (wire):** at least **30 bytes**; the first 30 are the ASCII `host port`
> endpoint string. Any trailing fields are **UNVERIFIED**.
>
> **Selection-index provenance note (for `names.yaml` reconciliation).** The channel-port offset and
> the server-list **render page/scroll** offset are two *different* window fields. An earlier dirty
> note conflated them; the live re-read disambiguates: the **channel-port offset is its own selection
> field**, while the render page offset is a separate UI-paging field. A fresh implementation should
> keep "the selected server/channel index" and "the current select-screen scroll page" as distinct
> values. This is a glossary/naming concern only — it does not change any wire byte.

---

## 3. Game-connection flow and the enter-game exchange

Once the endpoint is resolved, the client connects to the game server and runs the main protocol.
This is the connection that uses the 8-byte header + `(major:minor)` dispatch, the send-path byte
cipher, and LZ4 (all owned by `specs/crypto.md` and `opcodes.md`).

### 3.1 Handshake and authentication (cited)

- Server → client **`0/0 SmsgKeyExchange`** delivers the server's asymmetric public-key material.
- The client immediately replies with **`1/4 CmsgAuthReply`**, which carries the RSA-encrypted
  **staged login credential (the password)**.
- The session is then marked secure.

The full cryptographic detail (62-byte `0/0` payload, PKCS#1 v1.5 type-2 padding, modular
exponentiation, reply whitening, big-endian digit order) is **owned entirely by `specs/crypto.md`**
and is not restated here. The login flow only needs to know that the credential conveyed by `1/4` is
the password staged at login-form time — **the plaintext login blob never carries the password.**

### 3.2 Character list and scene transition

`3/1 SmsgCharacterList` (S2C) is the message that **actually switches the client into the
character-select scene**. Its shape:

- a **3-byte header**: a server-id context byte, a channel-id context byte, and a **slot bitmask**
  byte (bit *i* set ⇒ slot *i* is occupied);
- for each set bit, one **per-slot record of 981 bytes**, read sequentially.

Per-slot record (981 bytes total), as consumed by the handler:

| Sub-offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 880 (0x370) | Character descriptor | The full character/spawn record; layout in `structs/spawn_descriptor.md`. Holds the display name, level, hit points, etc. The descriptor's name is compared against the empty-slot sentinel (Section 3.5). |
| 880 | 96 (0x60) | Stats block | Per-slot stat block; layout in `structs/stats.md`. |
| 976 | 1 | Slot flag | A per-slot flag byte (state / availability class). |
| 977 | 4 (u32) | Slot timing | A per-slot timing value (e.g. delete-cooldown / create-time class). |

After parsing all occupied slots, the client derives a **CP949** display name (cleaned/truncated to
**17 bytes**) per slot, then forces the **character-select scene**.

> **Slot count (now a hard constant).** The slot-parse loop is a **fixed, bounded iteration of
> exactly 5** — it walks slot indices **0..4** and stops; it is not an open-ended scan driven only by
> the bitmask width. The client therefore supports a **maximum of 5 character slots**. (This is
> promoted from the earlier "inferred ≤ 5" to a firm static fact, corroborated by the slot-range
> guard `slot ≤ 4` in the enter-game path, Section 3.5.) The number of *occupied* slots in any given
> list is still the population count of the bitmask, but the bitmask never references a slot beyond 4.
>
> **Capture corroboration.** `opcodes.md` notes a prior single observation of a **1965-byte** list,
> which equals `3 + 2 × 981` (a 3-byte header + two character slots), consistent with this shape. The
> 981-byte stride and the 5-slot loop bound are dispatch-path-confirmed; deeper field internals beyond
> the descriptor/stats cross-references are **capture-unverified**.

This handler is the one that **transitions** into the select scene. The management responses of
Section 5 *refresh* an existing select scene but do not perform the transition.

### 3.3 Enter-game request (1/9, C2S)

When the player confirms a slot, the client emits **`1/9 CmsgEnterGameRequest`** with a **40-byte
(0x28) body**, then caches the chosen slot's record locally (Section 3.5). Header is `major = 1,
minor = 9`. Known body layout:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Selected slot index | The chosen character slot (0..4). |
| 1.. | rest of 40 | Version/token region | Zero-filled buffer into which a **client-version token** is stamped. The token is derived from a local version file (`data/cursor/game.ver`) as `10 × versionField + 9`. |

**Version token (sample-verified value).** The local `data/cursor/game.ver` sample is 28 bytes = 7
little-endian u32 fields. The token uses the **6th field** (zero-based field index 5), whose sampled
value is **2114** (`0x0842`), giving a token of `10 × 2114 + 9 = ` **21149**. This concrete value is
`sample_verified` against the real on-disk file. (Caveat: the reader is a parsed accessor that opens
`game.ver` through the VFS and indexes the Nth field; field index 5 maps to the 6th u32 if the parse
is 1:1 with the raw layout.)

> The exact placement of the version token inside the 40-byte buffer is **UNVERIFIED**; only the slot
> index at offset 0 and the 40-byte total are firmly established. The token *value* (21149 for this
> client build) is firm; its *offset* within the body is not. Field spec:
> `packets/1-9_enter_game_request.yaml`.

### 3.4 Enter-game acknowledgement (3/5, S2C) and in-world transition

The server answers with **`3/5 SmsgEnterGameAck`**, a **44-byte** payload, after which the client
transitions into the **in-world game state**.

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | ~28 | Name + leading block | An ASCII/CP949 character name at offset 0, plus other bytes within the leading block. |
| 28 (0x1C) | 4 (u32) | Billing value | Fed to the billing subsystem. |
| 40 | 4 (u32) | Account character count | Trailing u32; the account's total character count. |

> Total = a 40-byte leading block + a trailing u32 = **44 bytes**. `opcodes.md` marks this 44-byte
> size as corroborated by two captures. Field internals of the 40-byte block beyond name@0 and
> billing@28 are **UNVERIFIED**. Field spec: `packets/3-5_enter_game_response.yaml`.

### 3.5 Local slot caching and the actual spawn

On confirming a slot, before/around sending `1/9`, the client:

- Guards the selection: the slot must be valid (not unselected, **within range — slot index ≤ 4**,
  not disabled, and not already entering). If the slot's character descriptor name equals the literal
  sentinel **`"@BLANK@"`**, the slot is **empty** and the client routes to **character creation**
  instead of entering.
- For a real character, it caches the chosen slot's data into the globals the world load consumes:
  the **880-byte character descriptor**, the **96-byte stats block**, and a couple of trailing
  name/flag bytes, plus a relation flag derived from the slot flag bits.

The actual spawn is then driven by **`3/7 SmsgCharSpawnResult`** (16-byte payload, Section 5.3),
which consumes the **cached descriptor** to materialize the local player in the world. After spawn,
the world entity is maintained by the major-5 Push handlers (out of scope; see `opcodes.md` 5/x).

> **Enter-game, in one line:** send `1/9` → cache the chosen slot's descriptor locally → on `3/5`,
> transition to the in-world state → on `3/7`, spawn into the world from the cached descriptor.

---

## 4. Account login and credential staging (C2S)

### 4.1 Secure-context build

The secure-context builder takes the **TAB-delimited form string** and:

- allocates a fresh secure-session object (the same object class used by the crypto handshake —
  `specs/crypto.md` §6),
- splits the form string on TAB: first token = **account name**; an optional middle token (gated by
  an argument) that carries the **second-password / PIN**; the final token = **`host port`**, further
  split on the space, with the **port parsed as a decimal integer**,
- records the host and numeric port as the **login endpoint**,
- invokes the login-blob builder (§4.2).

> **Caller chain (runtime-confirmed).** A live debugger session confirmed the architecture: the
> secure-context builder (this §4.1) parses the TAB form (account / PIN / host port), then invokes
> the login-blob builder (§4.2). The two-stage account → blob pipeline is observed against the live
> client, not just inferred.

### 4.2 Login packet body (secure 1/4, C2S)

> **RESOLVED -- prior '1/6' attribution was the false premise.** The deep workflow-spine analysis
> plus a live debugger login establish that the login credential is **NOT** a 1/6 message: it rides
> the **secure `1/4`** frame, and **1/6 is character-create only**. `opcodes.md` now catalogs the
> credential carrier on the `1/4` row (`packets/login.yaml`); `1/6` is `CmsgCreateCharacter`
> (`packets/cmsg_char_create.yaml`). The plaintext pre-image below is written into the `1/4`
> payload ahead of the RSA ciphertext (see `specs/crypto.md` section 6.6). The body is assembled in
> this order:

| Order | Bytes | Field | Notes |
|---|---|---|---|
| 1 | 1 (u8) | Sub-opcode = **`0x2B` (43)** | Literal first payload byte. |
| 2 | 4 (u32 LE) + N | Account string + trailing NUL | A **u32 little-endian length prefix** followed by the account bytes including the trailing NUL; the prefixed length **counts the NUL** (so prefix = `strlen(account) + 1`). |
| 3 | 4 (u32 LE) + N (optional) | **Second-password / PIN** + trailing NUL | Same encoding: a **u32 little-endian length prefix** that **includes the trailing NUL**, then the PIN bytes + NUL. Present when the second-password / PIN feature is active (the optional middle token of the TAB form, §4.1). **This is the field a prior version of this spec called the "optional auxiliary string" — runtime observation identifies it as the PIN / second-password.** |
| — | (separate buffer, NOT in this body) | **Password / credential** | Copied into an **exactly-17-byte, zero-padded** buffer and recorded as the RSA plaintext; conveyed only by the `1/4` handshake reply. Never serialized into this blob. |

**Field identity (runtime-confirmed).** A live debugger session drove a real login and read the
assembled blob out of the client's packet buffer. The previously-unnamed optional string **is the
second-password / PIN** (the value collected by the second-password input — see §1 step 1a and
`specs/frontend_scenes.md` §1). The login form therefore has **three** inputs: the account, the
account password (→ RSA 1/4), and the PIN (→ this optional blob field). The PIN is a first-class
input concept in the client (an input/name object carries an explicit "is-PIN" flag), which
corroborates that the optional field is the PIN rather than an arbitrary auxiliary string.

**Field capacities (runtime-confirmed).** The build validates, and the staged buffers size, as:

| Field | Capacity rule | Notes |
|---|---|---|
| Account | length **≥ 2** and **< 20** characters | Length-prefixed into the blob (incl. NUL). |
| Password | length **≥ 2** and **< 17** characters | **Not** in the blob; copied into an **exactly-17-byte zero-padded buffer** held for the RSA 1/4 send. |
| Second-password / PIN | length **< 5** characters (so **≤ 4** chars + a NUL) | Length-prefixed into the optional blob field (incl. NUL). Numeric in practice. |

**Length-prefix encoding (runtime-confirmed).** Each length-prefixed string is `[u32 little-endian
length][bytes…][NUL]`, and the **u32 length includes the trailing NUL byte**. So a 7-character
account serializes as prefix `0x00000008` followed by 7 bytes + NUL; a 4-character PIN serializes as
prefix `0x00000005` followed by 4 bytes + NUL.

So the wire login pre-image is `[0x2B] [u32len account ] ([u32len PIN ])`, written into the
secure **`1/4`** payload ahead of the RSA ciphertext. The **password is never in this pre-image** --
it is the RSA plaintext `M` (a fixed 17-byte zero-padded buffer) encrypted into the same `1/4`
payload. This is consistent with `specs/crypto.md` (RSA plaintext = the staged login credential
string itself) and `packets/login.yaml` (the credential carrier).

> **Resolved (was an open item):** the identity of the optional middle field — it is the
> **second-password / PIN** — and the length-prefix width (**u32 LE**, NUL-inclusive). These were
> previously marked UNVERIFIED; a runtime read of the live client's assembled blob resolves them.
>
> **Still open:** this runtime read confirms the **login-blob structure** (reading (A) of the 1/6
> collision) only. It does **NOT** resolve whether the **character-create** send (reading (B)) shares
> opcode 1/6, nor where `(major:minor)` is stamped for the login blob (the builder writes a `0/0`
> version pair; the `1/6` stamping must occur downstream in the send wrapper, or the `1/6`
> attribution is the known naming inconsistency of Section 6). `opcodes.md` still flags the
> `~52-byte` size as an estimate, and **no `packets/*.yaml` is authored for 1/6 in this spec** —
> the catalog row and the 1/6 collision stay as recorded pending capture (see Section 7 and
> Section 9, and `packets/1-6_login_or_create.yaml`).

### 4.3 Secure send

The `1/4` auth reply is built from the session key state and sent inline in the same dispatch branch
that reads `0/0`. This is the credential-bearing handshake reply; its construction is owned by
`specs/crypto.md` §6.3.

### 4.4 Packet framing (cited — `specs/crypto.md`)

The login blob is assembled inside an outbound packet buffer whose wire framing is an **8-byte
plaintext header** followed by the **payload**:

```
[u32 size][u16 major][u16 minor][payload…]
```

- `size` is the total length including the 8-byte header (it is the write cursor; it starts at 8
  and grows as fields are appended).
- The **8-byte header stays plaintext on the wire** — it is the transport framing the receiver needs
  in clear to deframe.
- The **payload** (everything after offset 8, i.e. `size − 8` bytes — here the `0x2B` blob of §4.2)
  is the region transformed by the outbound byte cipher.

**This runtime observation re-confirms the existing framing rule.** The cipher itself (the
XOR/ROL transform, its passes, and its constants) and the header/payload split are **owned by
`specs/crypto.md`** and are **not restated here**. The login flow only needs to know that the login
blob rides in the encrypted payload region behind a plaintext 8-byte header.

---

## 5. Character-management responses (major 3, S2C)

All offsets below are **payload-relative** (after the 8-byte frame header). Sizes are the literal read
sizes in each handler. **Opcode minors follow `opcodes.md`** (authoritative); see Section 6 for the
naming caveat.

### 5.1 Character list — `3/1 SmsgCharacterList`

Specified in Section 3.2 (it is the scene-transitioning message). Field spec:
`packets/3-1_character_list.yaml`.

### 5.2 Enter-game ack — `3/5 SmsgEnterGameAck`

Specified in Section 3.4 (44 bytes; transitions to in-world). Field spec:
`packets/3-5_enter_game_response.yaml`.

### 5.3 Character-spawn result — `3/7 SmsgCharSpawnResult`

A **16-byte** payload that drives the local spawn:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | 0 = failure (a timed message is shown); nonzero = proceed to spawn. |
| 1 | 1 (u8) | Slot | Character slot index. |
| 2 | 2 | Padding | Alignment. |
| 4 | 4 (u32) | Spawn param 1 | Passed to the spawn routine. |
| 8 | 4 (u32) | Spawn param 2 | Passed to the spawn routine. |
| 12 | 4 (u32) | Spawn param 3 | Passed to the spawn routine. |

On success, the client spawns the local player from the **cached** 880-byte descriptor (Section 3.5).

> The 16-byte size is dispatch-confirmed. The meaning of the three spawn-param u32s is **UNVERIFIED**.

### 5.4 Character create result — `3/23 SmsgCharCreateResult`

A **12-byte** payload:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | 1 = success, 0 = failure. |
| 1 | 1 (u8) | Code | On success: the assigned slot id. On failure: an error code (range `0xC8..0xD4`, mapped to UI strings; shared with rename, §5.6). |
| 2 | 2 | Padding | Alignment. |
| 4 | 4 (u32) | Value 1 | Passed to slot refresh on success. |
| 8 | 4 (u32) | Value 2 | Passed to slot refresh on success. |

On success the account character count is incremented. `opcodes.md` carries a **capture-verified**
12-byte example for this message. Field internals beyond result/code are otherwise **UNVERIFIED**.

### 5.5 Character manage / delete result — `3/4 SmsgCharManageResult`

An **8-byte** payload:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | 1 = success. |
| 1 | 1 | Padding | |
| 2 | 1 (u8) | Subtype | Manage action selector (0 / 1 / 2). Subtype 2 is **delete-confirm** (decrements the account character count). |
| 3 | 1 | Padding | |
| 4 | 4 (u32) | Ready time | When result indicates a pending state: a Unix time; if in the future, a "wait HH:MM" delete-cooldown message is formatted. |

> The 8-byte size is dispatch-confirmed. The subtype 0 / 1 semantics are **UNVERIFIED** (only subtype
> 2 = delete-confirm is inferred).

### 5.6 Rename-character result — `3/6 SmsgRenameCharResult`

A **19-byte (0x13)** payload:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | Nonzero = success. |
| 1 | 18 | Name **or** error | On success: the new character name as a CP949 ASCIIZ string, up to 18 bytes including the NUL. On failure: an error code in byte +1 (range `0xC8..0xD4`, mapped to UI strings). |

> The 19-byte size is dispatch-confirmed. The success/failure discrimination is by the result byte at
> offset 0.

### 5.7 Other major-3 responses (catalog cross-reference, not specced here)

`opcodes.md` additionally lists `3/8 SmsgShopPageUpdate`, `3/13 SmsgCharStatusUpdate`,
`3/14 SmsgSceneEntityUpdate`, `3/100 SmsgCharActionResult`, and `3/50000 SmsgGmChatMessage`. These are
routing-confirmed but **not part of the login/select/enter-game flow**, and their field layouts are
not specced here.

---

## 6. ⚠ Opcode-naming caveat (load-bearing)

The legacy binary's internal handler **names** for the major-3 family **disagree** with both (a) the
dispatch arithmetic that actually routes minors to handlers, and (b) the canonical names in
`opcodes.md`. Several handlers are **misnamed relative to the minor they are reached at**, and a few
even carry inline comments using yet a third numbering. There are therefore *three* disagreeing
sources of truth in the binary; only the **dispatch arithmetic + the byte-level behavior** is real.

**Resolution (and the rule this spec follows):**

- The **authoritative mapping is `opcodes.md`**, anchored to **handler behavior** (the byte-level
  payload each handler reads), not to any legacy name.
- This spec promotes opcodes **by behavior + `opcodes.md`** only. For example, the message whose
  handler reads the 44-byte enter-game block is **`3/5 SmsgEnterGameAck`** (regardless of any legacy
  name suggesting otherwise), and the message whose handler reads the 12-byte create result is
  **`3/23 SmsgCharCreateResult`**.
- **Do not** infer any minor→name mapping from legacy handler names. This inconsistency is flagged
  for the project glossary (`names.yaml`) review; it does not change any byte-level behavior in this
  spec.

The behavior-anchored opcode subset for this flow:

| Opcode (major:minor) | Catalog name | Dir | Size | Behavior anchor |
|---|---|---|---|---|
| lobby (port 10000) | server-list | S2C | 8-byte wrapper + N×8 | `wrapper.major` = count; 8-byte records {id u16, status u16, load u16, open-time u16} |
| lobby (port `10000+off`) | channel-endpoint | S2C | 8-byte wrapper + ≥30 | first 30 bytes = `host port` ASCII |
| 0:0 | SmsgKeyExchange | S2C | 62 (cited) | RSA key material; triggers `1/4` (see `crypto.md`) |
| 1:4 | CmsgAuthReply | C2S | var (cited) | RSA of the staged password (see `crypto.md`) |
| 1:6 | CmsgLoginRequest | C2S | ~52 | `[0x2B][u32len account\0]([u32len PIN\0])`; account password via `1/4`; optional field = second-password / PIN (runtime-confirmed) |
| 1:9 | CmsgEnterGameRequest | C2S | 40 | slot@0 + version token (value 21149 for this build); server → `3/5` |
| 3:1 | SmsgCharacterList | S2C | 3 + N×981 (N ≤ 5) | header[srv, chan, mask] + per-slot {descriptor 880 + stats 96 + flag 1 + time 4}; enters select scene |
| 3:4 | SmsgCharManageResult | S2C | 8 | result + subtype + ready_time (delete cooldown) |
| 3:5 | SmsgEnterGameAck | S2C | 44 | name + billing@28 + char_count@40; transitions to in-world |
| 3:6 | SmsgRenameCharResult | S2C | 19 | result + (error code \| new name ASCIIZ[18]) |
| 3:7 | SmsgCharSpawnResult | S2C | 16 | result + slot + 3×u32 spawn params; drives the spawn |
| 3:23 | SmsgCharCreateResult | S2C | 12 | result + code + 2×u32; capture-verified sample |

> **Note on the 1/6 row above.** The login-blob structure (`[0x2B][u32len account\0]([u32len
> PIN\0])`), the u32-LE NUL-inclusive prefix, and the optional-field identity (the second-password /
> PIN) are **runtime-confirmed** against the live client (no addresses). What this does **not**
> settle: the `~52-byte` size estimate, whether character-create shares opcode 1/6, and where
> `(major:minor)` is stamped for the login blob (the builder writes `0/0`). Those remain as recorded
> — see Sections 4.2 and 9, and `packets/1-6_login_or_create.yaml`.

(Major-2 `GameAction` is **C2S only** — there is no inbound handler for it.)

---

## 7. Recovered constants

| Constant | Value | Role |
|---|---|---|
| Lobby base port | **10000** | Server-list fetch; channel = `10000 + selected_offset`. |
| Default fallback IP | **`211.196.150.4`** | Used when `ip.txt` is absent and the resolve list fails. |
| IP override file | **`ip.txt`** | Single token, truncated to **19 characters**. |
| Login sub-opcode byte | **`0x2B` (43)** | First payload byte of the `1/6` login blob. |
| Login length-prefix width | **u32 little-endian** | Prefix for each login-blob string; **includes the trailing NUL** in its count. Runtime-confirmed. |
| Login account capacity | length **≥ 2** and **< 20** | Length-prefixed (incl. NUL) into the login blob. Runtime-confirmed. |
| Login password capacity | length **≥ 2** and **< 17** | **Not** in the blob; staged in an **exactly-17-byte zero-padded buffer** for the RSA `1/4` send. Runtime-confirmed. |
| Login second-password / PIN capacity | length **< 5** (≤ 4 chars + NUL) | The optional length-prefixed login-blob field. Runtime-confirmed. |
| Channel-endpoint copy length | **30 (0x1E) bytes** | The leading `host port` ASCII string. |
| Server-list record size | **8 bytes** | Count = `wrapper.major`. |
| Server-list record fields | id u16 @+0 (1..40), status u16 @+2, load u16 @+4, open-time u16 @+6 | See Section 2.1. |
| Server-list load thresholds (UI color) | **1200 / 800 / 500** | Client-side load-gauge coloring (presentation only). |
| Server-list status sentinels (UI) | **3** (open-time scheduled), **24** (preparing/check), **100** (current selection) | Client-side render special cases (presentation only). |
| Server-name table | **41 entries**, indexed by `server_id` (+0) | Client-local localized names; **not on the wire**. |
| Char-list per-slot record | **981 bytes** | 880 descriptor + 96 stats + 1 flag + 4 timing. |
| Char-list header | **3 bytes** | server-id, channel-id, slot bitmask. |
| Char-list maximum slots | **5** (slot indices 0..4) | Hard loop bound; also the enter-game slot-range guard (`slot ≤ 4`). |
| Display-name length (char list) | **17 bytes** | CP949, cleaned/truncated. |
| EnterGameRequest body (1/9) | **40 (0x28) bytes** | Slot index at offset 0. |
| Version token | `10 × versionField + 9` = **21149** (this build) | Derived from `data/cursor/game.ver` (field index 5 = 2114). `sample_verified`. |
| EnterGameAck (3/5) | **44 bytes** | 40-byte block + trailing char-count u32; billing u32 @ +28. |
| CharCreateResult (3/23) | **12 bytes** | — |
| CharSpawnResult (3/7) | **16 bytes** | — |
| CharManageResult (3/4) | **8 bytes** | — |
| RenameCharResult (3/6) | **19 bytes** | Name field up to 18 bytes incl. NUL. |
| Char error-code range (create/rename UI strings) | **`0xC8..0xD4`** (200..212) | Mapped to UI strings. |
| Empty-slot sentinel | literal **`"@BLANK@"`** | Marks an unoccupied slot in the descriptor name; routes to character creation on confirm. |
| game.ver sample | 28 bytes, 7 LE u32: `[4, 31, 35, 1027, 52, 2114, 8]` | Real on-disk sample; field index 5 = 2114 pins the version token. |

> Text encoding note: every name/string field above that originates from the Korean client is
> **CP949 (EUC-KR superset)** on the wire. Wire structs use fixed byte buffers (`bytes[N]` →
> `[InlineArray]`), never managed strings; decode to CP949 only at the presentation boundary.

---

## 8. Process and socket boundary

- In this build, the login form, lobby discovery, account auth, character list/create/delete/rename,
  and enter-game are **all performed inside the game client process itself**, over its own sockets.
  There is **no separate lobby/online child executable spawned** by this client.
- If a sibling lobby/online executable exists elsewhere in the wider product, it is **out of scope**:
  do not analyze those binaries and do not assume their wire format from this client. The
  **port-10000 mini-protocol described here is the only "lobby" surface this client exposes.**
- Two socket surfaces exist:
  1. **Lobby (synchronous, port 10000):** server-list and channel-endpoint discovery. 8-byte wrapper
     + LZ4; **no** `(major:minor)` dispatch; **no** byte cipher.
  2. **Game (overlapped):** the `(major:minor)` protocol of `opcodes.md`, with the `0/0` / `1/4` RSA
     handshake and the send-path byte cipher + LZ4 of `specs/crypto.md`, dispatched per Section 5/6.
- The login form's TAB-delimited string carries the account, the **second-password / PIN** (optional
  middle field), and the discovered `host:port`; the **account password is conveyed only by the RSA
  `1/4` reply**, never in a plaintext blob.

---

## 9. UNVERIFIED / open items (capture needed)

1. **Lobby server-list record `open_time` (+6) packing** — only the `status_code == 3` render path
   uses it (`×10` hour/min split); the exact packing is inferred, not pinned. The 8-byte stride,
   `count = wrapper.major`, and the id/status/load field positions are firm; the open-time encoding is
   not. Lobby capture required.
2. **Lobby server-list `status_code` (+2) full enum** — values **3 / 24 / 100** are special-cased in
   render; the meaning of other in-range values (beyond "valid index") is not enumerated. Capture
   required.
3. **Lobby channel-endpoint payload** beyond "first 30 bytes = `host port` ASCII" (any trailing
   fields).
4. **Login blob (1/6) — the 1/6 opcode collision (still open).** A runtime read **resolved** the
   *login-blob structure* (reading (A)): field order `[0x2B][u32len account\0]([u32len PIN\0])`, the
   u32-LE NUL-inclusive length prefix, and the identity of the optional field as the
   **second-password / PIN** (no longer UNVERIFIED — see §4.2). **Still open:** whether the
   **character-create** send (reading (B)) shares opcode 1/6 (that send site is server/select-gated
   and was not reachable in this session), and where `(major:minor)` is stamped for the login blob
   (the builder writes `0/0`; the `1/6` stamping must be downstream, or it is the Section 6 naming
   inconsistency — single observation, flagged not claimed). `opcodes.md` still flags the `~52-byte`
   size as an estimate; **no `packets/*.yaml` is authored here** — `packets/1-6_login_or_create.yaml`
   keeps the collision as recorded.
5. **Enter-game request (1/9)** exact byte **offset** of the version token inside the 40-byte body
   (the token *value* 21149 is pinned from the real `game.ver`; its placement is not). Other body
   bytes beyond slot@0.
6. **Enter-game ack (3/5)** internals of the 40-byte leading block beyond name@0 and billing u32 @+28.
7. **Char-spawn result (3/7)** meaning of the three trailing spawn-param u32s.
8. **Char-manage result (3/4)** subtype 0 / 1 semantics (only subtype 2 = delete-confirm is inferred).
9. **Selection-index vs. render-page field separation** (Section 2.2) — the channel-port selection
   index and the select-screen render-page offset are distinct window fields; an earlier note
   conflated them. Naming/glossary concern, flagged for `names.yaml`; no wire impact.
10. **Opcode-naming inconsistency** (Section 6) — legacy handler names disagree with the dispatch
    arithmetic and `opcodes.md`. Anchored to behavior + `opcodes.md` here; flagged for `names.yaml`
    review. Do **not** promote minor→name mappings from legacy names.
11. **No live network capture was loaded for this analysis.** All wire offsets/sizes are static reads,
    **except** the login blob (1/6) field layout, which is corroborated by a runtime read of the live
    client's assembled packet buffer (still not a network capture). The only on-disk real-byte
    corroboration is the local `data/cursor/game.ver` file (item 5 / Section 3.3). The lobby (port-
    10000) mini-protocol in particular has **no network-capture corroboration at all.**
    `capture_verified: false`; `sample_verified: partial` (game.ver only); `runtime_observed: login
    blob (1/6) field layout`.

---

## 10. Status

- Process/socket boundary, two-surface model, lobby protocol shape, base port, fallback IP, and the
  end-to-end ordered flow: **HIGH confidence** (static).
- Lobby server-list 8-byte record decode (id / status / load + thresholds / sentinels): **MEDIUM–HIGH**
  (static, from the render path + a debug literal; `open_time` packing and the full status enum are
  capture-unverified).
- Handshake (`0/0` → `1/4`) and credential staging: **HIGH confidence** (cited — `specs/crypto.md`).
- Character-list 981-byte slot stride **and the hard 5-slot loop bound**; the major-3 response sizes
  (3/1, 3/4, 3/5, 3/6, 3/7, 3/23): **MEDIUM–HIGH** (dispatch-confirmed sizes / bound; field internals
  capture-unverified).
- Login-blob (1/6) field layout — order, u32-LE NUL-inclusive length prefix, the three field
  capacities, and the optional field's identity as the **second-password / PIN**: **HIGH**
  (RUNTIME-CONFIRMED against the live client; no addresses). The `~52-byte` size estimate and the
  1/6-vs-character-create collision remain open.
- Enter-game-request (1/9) body internals: **MEDIUM** (40-byte total + slot@0 firm; token offset
  unverified). Enter-game **version token value 21149**: **HIGH** (`sample_verified` from the real
  `game.ver`).
- End-to-end confirmation against network captures: **not performed.** `capture_verified: false`;
  `sample_verified: partial` (local `game.ver` only); `runtime_observed: login-blob field layout`.
