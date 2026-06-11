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

`capture_verified: false`. **No live network capture was available for this analysis.** Opcode
routing is a hard static fact (corroborated by `opcodes.md`, which reads the client's dispatch
installers). All field offsets/sizes and the lobby (port-10000) mini-protocol are static inferences
unless explicitly marked otherwise.

| Claim | Confidence | Basis |
|---|---|---|
| Two distinct socket surfaces exist: a synchronous lobby discovery channel and the main overlapped game connection. | HIGH | Static — two independent connect paths and frame-handling regimes. |
| The lobby mini-protocol reuses the 8-byte frame wrapper + inbound LZ4, but **no** `(major:minor)` dispatch and **no** byte cipher. | HIGH | Static. |
| Lobby base port = **10000**; channel port = `10000 + selected_offset`. | HIGH | Static constant. |
| Default fallback server IP **`211.196.150.4`**; IP override file **`ip.txt`** (single token, ≤19 chars). | HIGH | Static constant / string evidence. |
| The full login/char/enter-game flow runs **inside the game client process itself** in this build (no separate lobby/online child executables are spawned). | HIGH | Static — no references to sibling lobby/online executables as spawned children. |
| Overall ordered flow (Section 1) and its UI/net state transitions. | MEDIUM–HIGH | Static behavioral read of the login window state machine. |
| The `0/0 → 1/4` RSA handshake and credential staging. | HIGH (cited) | `specs/crypto.md` — not re-derived here. |
| Login blob (1/6) starts with sub-opcode byte **`0x2B` (43)** then length-prefixed account/optional strings; password travels only via the RSA 1/4 reply. | MEDIUM | Static; length-prefix width and optional-string presence are **capture-unverified**. |
| Character-list (3/1) shape: 3-byte header + per-slot **981-byte** records gated by a slot bitmask. | MEDIUM–HIGH | 981-byte stride is dispatch-path-confirmed; field internals capture-unverified. |
| Enter-game request (1/9) = **40-byte** body; slot index at +0; version token elsewhere in the buffer. | MEDIUM | 40-byte total + slot@0 are firm; token offset capture-unverified. |
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

1. **Login form.** The player enters an account name, a password, and an optional auxiliary field.
   On submit, the client packs the form into a single **TAB-delimited string** of the shape
   `account ⟨TAB⟩ [optional] ⟨TAB⟩ field ⟨TAB⟩ host␠port` (a literal U+0009 between tokens; the
   final token holds the discovered game endpoint as `host` and `port` separated by a space). This
   string is handed to the secure-context builder (Section 4).

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
   the port parsed from the endpoint string), and stages the account-login blob. **This is the join
   point** where the flow leaves the lobby surface and engages the **main game connection** and its
   `0/0` / `1/4` RSA handshake (cited from `specs/crypto.md`).

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
| Credential submit | build secure context, set endpoint, stage login blob | hand off to game connection | join `0/0` handshake |

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

The base port is **10000**. Both lobby fetches use the same wrapper + LZ4 receive pattern below.

### 2.1 Server-list fetch (port 10000)

Receive sequence:

1. Block until the **8-byte frame wrapper** has been fully read (with cooperative back-off on
   would-block).
2. From the wrapper, read the **frame size** (low 16 bits) and the **`major`** field.
3. Block until the remaining `size − 8` payload bytes are read.
4. **LZ4-decompress** the payload (inbound, raw block).
5. Interpret `major` as the **server-record count**. Read `count` fixed-size records of **8 bytes**
   each into the login window's server array, recording the count.

> **Server-list record (wire):** `count = wrapper.major`; each record is **8 bytes**. The record's
> internal field layout is **UNVERIFIED** (likely a server id plus load/status fields). Only the
> 8-byte stride and the `count = major` relationship are firm. A live lobby capture is required to
> decode record internals.

### 2.2 Channel-endpoint fetch (port `10000 + offset`)

After server selection, the client connects to `10000 + selected_offset`, where `selected_offset` is
the chosen server/channel index. The receive sequence is identical to §2.1 (8-byte wrapper + LZ4).
After decompression:

- The client takes the **first 30 (0x1E) bytes** of the decompressed payload as a **NUL-padded ASCII
  `host port` string**. The buffer is zero-filled first; the 30 bytes are **not guaranteed
  NUL-terminated**.
- This string is the game server's endpoint. It is later split on the space into host and a numeric
  port (parsed as a decimal integer).

> **Channel-endpoint payload (wire):** at least **30 bytes**; the first 30 are the ASCII `host port`
> endpoint string. Any trailing fields are **UNVERIFIED**.

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
| 0 | 880 (0x370) | Character descriptor | The full character/spawn record; layout in `structs/spawn_descriptor.md`. Holds the display name, level, hit points, etc. |
| 880 | 96 (0x60) | Stats block | Per-slot stat block; layout in `structs/stats.md`. |
| 976 | 1 | Slot flag | A per-slot flag byte (state / availability class). |
| 977 | 4 (u32) | Slot timing | A per-slot timing value (e.g. delete-cooldown / create-time class). |

After parsing all occupied slots, the client derives a **CP949** display name (cleaned/truncated to
**17 bytes**) per slot, then forces the **character-select scene**.

> **Slot count.** The number of slots is the population count of the bitmask. The maximum slot index
> is **inferred to be 4 (i.e. up to 5 slots)** from the slot guards elsewhere in the client, but this
> is **not pinned to a hard constant** here — treat 5 as a working upper bound pending capture.
>
> **Capture corroboration.** `opcodes.md` notes a prior single observation of a **1965-byte** list,
> which equals `3 + 2 × 981` (a 3-byte header + two character slots), consistent with this shape. The
> 981-byte stride is dispatch-path-confirmed; deeper field internals beyond the descriptor/stats
> cross-references are **capture-unverified**.

This handler is the one that **transitions** into the select scene. The management responses of
Section 5 *refresh* an existing select scene but do not perform the transition.

### 3.3 Enter-game request (1/9, C2S)

When the player confirms a slot, the client emits **`1/9 CmsgEnterGameRequest`** with a **40-byte
(0x28) body**, then caches the chosen slot's record locally (Section 3.5). Header is `major = 1,
minor = 9`. Known body layout:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Selected slot index | The chosen character slot. |
| 1.. | rest of 40 | Version/token region | Zero-filled buffer into which a **client-version token** is stamped. The token is derived from a local version file (`data/cursor/game.ver`) as `10 × versionField + 9`. |

> The exact placement of the version token inside the 40-byte buffer is **UNVERIFIED**; only the slot
> index at offset 0 and the 40-byte total are firmly established. Field spec:
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

- Guards the selection: the slot must be valid (not unselected, within range, not disabled, and not
  already entering). If the slot's character descriptor name equals the literal sentinel
  **`"@BLANK@"`**, the slot is **empty** and the client routes to **character creation** instead of
  entering.
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
  an argument); the final token = **`host port`**, further split on the space, with the **port parsed
  as a decimal integer**,
- records the host and numeric port as the **login endpoint**,
- invokes the login-blob builder (§4.2).

### 4.2 Login packet body (1/6, C2S)

`opcodes.md` catalogs the account-login message as **`1/6 CmsgLoginRequest`** (a `~52-byte` form-
family blob). The body is assembled in this order:

| Order | Bytes | Field | Notes |
|---|---|---|---|
| 1 | 1 (u8) | Sub-opcode = **`0x2B` (43)** | Literal first payload byte. |
| 2 | length-prefixed | Account string + trailing NUL | Length-prefixed bytes. |
| 3 | length-prefixed (optional) | Auxiliary string + trailing NUL | Present only if the optional field is used. |
| — | (separate buffer, NOT in this body) | **Password / credential** | Copied into a zero-filled buffer and recorded as the RSA plaintext; conveyed only by the `1/4` handshake reply. |

Validation guards (build aborts unless all pass): account length ≥ 2, auxiliary length ≥ 2, and each
string below its respective capacity bound.

So the wire login body is `[0x2B] [len-prefixed account\0] ([len-prefixed optional\0])`. The
**password is never in this blob** — it travels only via the RSA `1/4` reply. This is consistent with
`specs/crypto.md` (RSA plaintext = the staged login credential string itself).

> The **exact length-prefix width** and whether the optional middle string is always present are
> **UNVERIFIED**. `opcodes.md` flags the `~52-byte` size as an estimate and asks the send site be
> re-probed before a struct is committed. **No `packets/*.yaml` is authored for 1/6 in this spec** —
> it is intentionally left unspecced pending capture (see Section 7).

### 4.3 Secure send

The `1/4` auth reply is built from the session key state and sent inline in the same dispatch branch
that reads `0/0`. This is the credential-bearing handshake reply; its construction is owned by
`specs/crypto.md` §6.3.

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
`opcodes.md`. Several handlers are **misnamed relative to the minor they are reached at**.

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
| lobby (port 10000) | server-list | S2C | 8-byte wrapper + N×8 | `wrapper.major` = count; 8-byte records |
| lobby (port `10000+off`) | channel-endpoint | S2C | 8-byte wrapper + ≥30 | first 30 bytes = `host port` ASCII |
| 0:0 | SmsgKeyExchange | S2C | 62 (cited) | RSA key material; triggers `1/4` (see `crypto.md`) |
| 1:4 | CmsgAuthReply | C2S | var (cited) | RSA of the staged password (see `crypto.md`) |
| 1:6 | CmsgLoginRequest | C2S | ~52 | `[0x2B][lenpfx account\0]([lenpfx opt\0])`; password via `1/4` |
| 1:9 | CmsgEnterGameRequest | C2S | 40 | slot@0 + version token; server → `3/5` |
| 3:1 | SmsgCharacterList | S2C | 3 + N×981 | header[srv, chan, mask] + per-slot {descriptor 880 + stats 96 + flag 1 + time 4}; enters select scene |
| 3:4 | SmsgCharManageResult | S2C | 8 | result + subtype + ready_time (delete cooldown) |
| 3:5 | SmsgEnterGameAck | S2C | 44 | name + billing@28 + char_count@40; transitions to in-world |
| 3:6 | SmsgRenameCharResult | S2C | 19 | result + (error code \| new name ASCIIZ[18]) |
| 3:7 | SmsgCharSpawnResult | S2C | 16 | result + slot + 3×u32 spawn params; drives the spawn |
| 3:23 | SmsgCharCreateResult | S2C | 12 | result + code + 2×u32; capture-verified sample |

(Major-2 `GameAction` is **C2S only** — there is no inbound handler for it.)

---

## 7. Recovered constants

| Constant | Value | Role |
|---|---|---|
| Lobby base port | **10000** | Server-list fetch; channel = `10000 + selected_offset`. |
| Default fallback IP | **`211.196.150.4`** | Used when `ip.txt` is absent and the resolve list fails. |
| IP override file | **`ip.txt`** | Single token, truncated to **19 characters**. |
| Login sub-opcode byte | **`0x2B` (43)** | First payload byte of the `1/6` login blob. |
| Channel-endpoint copy length | **30 (0x1E) bytes** | The leading `host port` ASCII string. |
| Server-list record size | **8 bytes** | Count = `wrapper.major`. |
| Char-list per-slot record | **981 bytes** | 880 descriptor + 96 stats + 1 flag + 4 timing. |
| Char-list header | **3 bytes** | server-id, channel-id, slot bitmask. |
| Display-name length (char list) | **17 bytes** | CP949, cleaned/truncated. |
| EnterGameRequest body (1/9) | **40 (0x28) bytes** | Slot index at offset 0. |
| EnterGameAck (3/5) | **44 bytes** | 40-byte block + trailing char-count u32; billing u32 @ +28. |
| CharCreateResult (3/23) | **12 bytes** | — |
| CharSpawnResult (3/7) | **16 bytes** | — |
| CharManageResult (3/4) | **8 bytes** | — |
| RenameCharResult (3/6) | **19 bytes** | Name field up to 18 bytes incl. NUL. |
| Char error-code range (create/rename UI strings) | **`0xC8..0xD4`** (200..212) | Mapped to UI strings. |
| Version token | `10 × versionField + 9` | Derived from `data/cursor/game.ver`. |
| Empty-slot sentinel | literal **`"@BLANK@"`** | Marks an unoccupied slot in the descriptor name. |

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
- The login form's TAB-delimited string carries the account, the optional field, and the discovered
  `host:port`; the **password is conveyed only by the RSA `1/4` reply**, never in a plaintext blob.

---

## 9. UNVERIFIED / open items (capture needed)

1. **Lobby server-list record internals** — the 8 bytes per record (server id / load / status). Only
   the 8-byte stride and `count = wrapper.major` are firm.
2. **Lobby channel-endpoint payload** beyond "first 30 bytes = `host port` ASCII" (any trailing
   fields).
3. **Login blob (1/6)** exact length-prefix width and whether the optional middle string is always
   present. Re-probe the send site before committing a struct. (`opcodes.md` already flags `~52B`;
   no `packets/*.yaml` authored here.)
4. **Enter-game request (1/9)** exact byte layout of the 40-byte body beyond slot@0 — specifically the
   version-token offset.
5. **Enter-game ack (3/5)** internals of the 40-byte leading block beyond name@0 and billing u32 @+28.
6. **Char-spawn result (3/7)** meaning of the three trailing spawn-param u32s.
7. **Char-manage result (3/4)** subtype 0 / 1 semantics (only subtype 2 = delete-confirm is inferred).
8. **Character-list maximum slot count** — inferred ≤ 5 (max slot index 4); not pinned to a hard
   constant.
9. **Opcode-naming inconsistency** (Section 6) — legacy handler names disagree with the dispatch
   arithmetic and `opcodes.md`. Anchored to behavior + `opcodes.md` here; flagged for `names.yaml`
   review. Do **not** promote minor→name mappings from legacy names.
10. **No live capture was loaded for this analysis.** All offsets/sizes are static reads. The lobby
    (port-10000) mini-protocol in particular has **no capture corroboration at all**.

---

## 10. Status

- Process/socket boundary, two-surface model, lobby protocol shape, base port, fallback IP, and the
  end-to-end ordered flow: **HIGH confidence** (static).
- Handshake (`0/0` → `1/4`) and credential staging: **HIGH confidence** (cited — `specs/crypto.md`).
- Character-list 981-byte slot stride and the major-3 response sizes (3/1, 3/4, 3/5, 3/6, 3/7, 3/23):
  **MEDIUM–HIGH** (dispatch-confirmed sizes; field internals capture-unverified).
- Login-blob (1/6) and enter-game-request (1/9) body internals: **MEDIUM** (totals firm; layouts
  partly unverified).
- End-to-end confirmation against captures: **not performed.** `capture_verified: false`.
