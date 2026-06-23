# Login Flow Specification — Login, Character Select, and Enter-Game

<!--
verification: confirmed (lobby host-resolution order, registry keys, the 0/0->1/4 handshake
  structure, and the game-server DNS connect are all static-control-flow-confirmed against the
  binary); static-hypothesis (in-record field order; full status enum); capture/debugger-pending
  (every on-wire byte VALUE -- no .pcapng present).
ida_reverified: 2026-06-23   # server-list re-derive (263bd994, static IDA): server-list §2.1 reconfirmed end-to-end; added the empty/error count branches (0->msg 4027, -1 sentinel->msg 4028, >0->show), the single-server (count==1) auto-select path, and the 10000 ms refresh debounce. No wire-byte change; the 8-byte record decode + commit gate + channel-endpoint shape all reconfirmed against the binary. # CYCLE 9 Phase 1 (263bd994): server-list 8-byte record signedness CONFLICT corrected -- all four fields are signed i16 (server_id +0 was wrongly u16; binary reads sign-extending loads, commit gate load<2400 is a signed branch); channel-endpoint parse shape (single SPACE delim, 30=copy-cap, single endpoint), lobby host-resolution (ip.txt/list.dat/211.196.150.4 + inet_addr), login ladder (29->31 PIN / 32->33 fetch / 37->38 commit / 40 hand-off), PIN srand(time()) ascending shuffle, curtain +5/>222 + SFX 861010105, game.ver index-5 gate, Loading (2 ortho quads + BGM 920100100) and 3/1 char-select (hard 5-slot loop) all static-RE-CONFIRMED. CYCLE 8 (263bd994): lobby inet_addr (dotted-quad, port 10000, no DNS) vs game gethostbyname re-confirmed; 0/0->1/4 handshake re-confirmed. Prior: scene re-confirmation campaign 2026-06-18
ida_anchor: 263bd994
evidence: [static-ida]
capture_verified: false
reconfirmed 2026-06-18 (static): the lobby host-resolution chain (ip.txt -> list.dat CIPList keyed
  by registry servername -> hardcoded 211.196.150.4) with inet_addr + connect (no DNS on the
  lobby socket); the GAME server is dynamic -- host:port from the channel-endpoint "<host> <port>"
  string, connected via gethostbyname (DNS), so the host may be a name OR a dotted quad; the
  selected server_id persists to HKLM\SOFTWARE\crspace\do : Lastserver (REG_DWORD); and the
  62-byte 0/0 key-exchange blob = 54-byte RSA import (modulus then exponent, big-endian digits,
  LE length prefixes, L1+L2=42) + scalar#1 (4B) + scalar#2 (4B) + a local timestamp.
-->

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

`runtime_observed: login blob (1/4)`. **A live debugger session drove a real login through the
client and read the assembled login blob out of the packet buffer at build time** — a runtime
(code-confirmed) observation of the live process, not a network capture. It pins the login blob's
field order, the **u32 little-endian** length-prefix width, the **NUL-inclusive** prefixed lengths,
the three field capacities (account / password / PIN), and the identity of the previously-unnamed
optional field as the **second-password / PIN**. It also re-confirms the plaintext-header /
encrypted-payload framing rule owned by `specs/crypto.md`. These claims are graded RUNTIME-CONFIRMED
below; they carry **no addresses** — only observed behaviour and observed byte layout. (The login
credential rides the secure **1/4** auth-reply; **1/6 is character-create only** — there is no
shared login/create opcode and no collision. See Section 4.2 and Section 9.)

| Claim | Confidence | Basis |
|---|---|---|
| Two distinct socket surfaces exist: a synchronous lobby discovery channel and the main overlapped game connection. | HIGH | Static — two independent connect paths and frame-handling regimes. |
| The lobby mini-protocol reuses the 8-byte frame wrapper + inbound LZ4, but **no** `(major:minor)` dispatch and **no** byte cipher. | HIGH | Static. |
| Lobby base port = **10000**; channel port = `10000 + selected_offset`. | HIGH | Static constant. |
| Default fallback server IP **`211.196.150.4`**; IP override file **`ip.txt`** (single token, ≤19 chars). | HIGH | Static constant / string evidence. |
| The full login/char/enter-game flow runs **inside the game client process itself** in this build (no separate lobby/online child executables are spawned). | HIGH | Static — no references to sibling lobby/online executables as spawned children. |
| Overall ordered flow (Section 1) and its UI/net state transitions. | MEDIUM–HIGH | Static behavioral read of the login window state machine. |
| The `0/0 → 1/4` RSA handshake and credential staging. | HIGH (cited) | `specs/crypto.md` — not re-derived here. |
| Login blob (rides the secure 1/4) starts with sub-opcode byte **`0x2B` (43)** then a length-prefixed account string and a length-prefixed **second-password / PIN** string; the account password travels via the RSA 1/4 ciphertext. | HIGH (runtime) | RUNTIME-CONFIRMED — the assembled blob was read out of the live client's packet buffer; field order, prefix width, and field identities observed directly (no addresses). |
| Login blob length-prefix width = **u32 little-endian**, and each string's prefixed length **includes its trailing NUL**. | HIGH (runtime) | RUNTIME-CONFIRMED — observed in the assembled bytes. |
| Login field capacities: account **< 20** chars, password **< 17** (staged in an exactly-17-byte zero-padded buffer), second-password / PIN **< 5** (≤ 4 chars + NUL). | HIGH (runtime) | RUNTIME-CONFIRMED — observed validation bounds + the staged buffer size. |
| PIN modal scrambles its on-screen keypad on every show: a time-seeded shuffle of a 10-digit permutation (anti-keylogger); PIN is masked, capped at 4 digits, and becomes the optional second-password blob field. | HIGH | Static control-flow recovery (recovered via static RE, CAMPAIGN 9); see §4.2a. |
| Server-list record (8 bytes) decodes to **{server id, status, load, open-time}** with the load thresholds and status sentinels of Section 2.1. | MEDIUM–HIGH | Static — recovered from the server-list render path plus a debug format literal; the open-time packing and full status enum are **capture-unverified**. |
| Character-list (3/1) shape: 3-byte header + per-slot **981-byte** records gated by a slot bitmask. | MEDIUM–HIGH | 981-byte stride is dispatch-path-confirmed; field internals capture-unverified. |
| The character list supports a **maximum of 5 slots** (slot indices 0..4). | HIGH (static) | The parse loop is a **hard, bounded iteration of exactly 5**, not an open-ended scan — promoted from "inferred ≤5" to a fixed constant. |
| Character-select C2S create = **1/6** (52-byte body), slot-select = **1/7** (2-byte body). | HIGH (static) | Static control-flow recovery (recovered via static RE, CAMPAIGN 9); see §3.6 and `specs/frontend_scenes.md` §4 / §8. |
| Enter-game request (1/9) = **40-byte** body; slot index at +0; version token elsewhere in the buffer. | MEDIUM | 40-byte total + slot@0 are firm; token offset capture-unverified. |
| Enter-game version token derivation `10 × versionField + 9`, and its concrete value **21149** for the sampled `game.ver`. | HIGH (sample) | `sample_verified` — computed from the real on-disk `data/cursor/game.ver` field value 2114. |
| Enter-game ack (3/5) = **44-byte** block; name@0, billing u32 @ +28, char-count u32 @ +40. | MEDIUM–HIGH | 44-byte total dispatch-confirmed; block internals partly capture-unverified. |
| Char-manage result (3/7) = **8-byte** block; result + subtype + ready-time; decrements the account char-count on a delete-confirm. | MEDIUM–HIGH | 8-byte total dispatch-confirmed (front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed); subtype internals capture-unverified. |
| Char enter-into-world bridge (3/14) = **16-byte** block; spawn-confirm that re-enters the enter-builder. The local-player **world** spawn is driven by 4/1, not 3/14. | MEDIUM | 16-byte total dispatch-confirmed (front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed); param meaning capture-unverified. |
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
   packs the form into a single **TAB-delimited string** of **four** tokens, of the shape
   `account ⟨TAB⟩ password ⟨TAB⟩ PIN ⟨TAB⟩ host␠port` (a literal U+0009 between tokens; the final
   token holds the discovered game endpoint as `host` and `port` separated by a space). The first two
   tokens are read from the two credential edit boxes (box-keyed: field #1 = first box, field #2 =
   second box; the conventional account-then-password order is a static hypothesis — the binary names
   neither box), the third is the second-password / PIN (step 1a), and the fourth is the channel
   endpoint. This string is handed to the secure-context builder (Section 4). (CAMPAIGN 16: corrected
   from a vague 3-slot shape to the confirmed 4-token form, consistent with §4.2a and
   `frontend_scenes.md §1.5`.)

1a. **Second-password / PIN entry.** After the primary account + password submit, and **before** the
   login blob is built and sent, the client raises a dedicated **second-password / PIN** input (a
   modal collecting a short numeric PIN of **≤ 4 characters**). The client models this PIN as a
   **first-class input concept** (an input/name object carries an explicit "is-PIN" flag), distinct
   from the account and the password. The PIN's value becomes the **optional length-prefixed field
   of the login blob** (Section 4.2); it is *not* the account password (the password is staged
   separately as the RSA 1/4 plaintext). The front-end shape of this modal is owned by
   `specs/frontend_scenes.md` §1 (its "Second-password / PIN" subsection); this spec owns where the
   PIN lands on the wire. The modal's **anti-keylogger scrambled keypad** and the PIN → wire hand-off
   mechanism are described in §4.2a.

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

   **Login outcome surface (no dedicated login-result opcode).** After sending `1/4` the client is
   **fully passive** — it sends nothing and waits for the server to drive the outcome by *which frame
   it pushes next* (static-IDA confirmed CYCLE 4; wire VALUE semantics live/capture-pending):
   - **SUCCESS** ⟹ an **unsolicited `3/1 SmsgCharacterList` push** → character-select (step 7).
   - **Hard FAILURE** ⟹ a connection-state event (network event **type-15 / sub-code 102**) →
     disconnect, bounded by a **~30s watchdog**.
   - **Coded outcome** ⟹ a `3/100 SmsgCharActionResult` carrying a 4-byte code (select-screen
     message / connect-progress prime; see `net_contracts.md` Appendix A.4). A coded `3/100` is
     **neither** a char-list **nor** a disconnect — the connection stays open and a message is shown.

   *(Live note, CYCLE 4: a `1/4` to the replica with a valid dev account returned `3/100 code=10`,
   password-independent — under live investigation as duplicate-session vs a 1/4 build defect; a
   successful login would instead surface as an unsolicited `3/1`.)*

7. **Character list.** Server → client the **character roster** (3-byte header
   `[form_byte0][channel_byte1][slot_mask_byte2]` + `popcount(slot_mask) × 981`-byte records, §5.1)
   drives the **character-select scene**. **LIVE + IDA CONFIRMED (CYCLE 4):** the same roster decode is
   reached by **two** minors — `3/1` (force-select: zeroes the scratch, always populates, forces
   GameState→char-select) and **`3/4`** (in-place refill, gated by `form_byte0 == 1`, no forced scene
   change). The live replica delivers the post-login roster **unsolicited on `3/4`**, immediately
   followed by **`3/5`** — a 44-byte post-login **account-ack** (name@0, billing@28, char_count@40; +4
   trailing) that nudges the scene toward Load — itself unsolicited (NOT gated on a prior `1/9`). A live
   login (dev account) returned `3/4` (2946B = `3 + 3×981`, mask `0x07` = 3 chars, first name "jeonsa")
   → `3/5` ("xwdvg26") → later `3/100` code 21 (generic select-screen notice; meaning live-pending).
   Both `3/1` and `3/4` MUST route to the same 3+N×981 roster decode + char-select population. (The
   earlier CYCLE-10 "3/4 = SmsgSceneEntityUpdate" label was a misname — the handler decodes the roster,
   not a scene-entity update; proposed canonical name `SmsgCharacterListUpdate`.)

8. **Slot management (optional).** From the select scene the player may **create**, **delete /
   manage**, or **rename** a character; the server answers with the corresponding character-management
   responses (Section 5).

9. **Enter game (CORRECTED 2026-06-21 against the live server `211.196.150.4`).** The player confirms a
   slot. The client sends `1/9 CmsgEnterGameRequest` (40-byte body, Section 3) and **caches the chosen
   slot's character record locally** for the world load. The server answers directly with
   `4/1 SmsgGameStateTick` (the ~9100-byte world-entry snapshot) — **the `4/1` IS the enter
   confirmation**, with **NO enter-ladder `3/5` in between**. On a **`4/1` received while the enter
   request (`1/9`) is in flight**, the client transitions to the **in-world game state** and spawns the
   local player from the cached descriptor. **The live enter ladder is `1/9 → 4/1`, NOT
   `1/9 → 3/5 → 4/1`** — a live headless run observed `… roster (3/4) → 3/5 (post-login account-ack,
   BEFORE the 1/9) → 1/9 (enter request) → 4/1 (enter confirmation) → 5/121`. The `3/5 SmsgEnterGameAck`
   is **only the unsolicited post-login account-ack** (it arrives **before** any `1/9`, step 7); it is
   **not** a step in the enter ladder and must **not**, by itself, enter the world. (A
   `3/14 SmsgCharSpawnResponse` (16 bytes) may also arrive as an enter-into-world *bridge* that
   re-enters the enter-builder, but it is **not** the local-player world spawn.) Ongoing world state
   thereafter is maintained by the major-5 Push family (out of scope here).

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

The lobby connect helper resolves **only the lobby host** (the game server is resolved separately —
see §2.2 / §3.0). It:

1. Initializes Winsock (version 2.2).
2. Resolves the **lobby host IP**, in strict priority order (first hit wins):
   - **Tier 1 — `ip.txt` override.** If a local **`ip.txt`** file is present, reads a single
     whitespace-free token from it, truncated to **19 characters**, and uses it.
   - **Tier 2 — `list.dat` connection-IP list (CIPList) keyed by the registry server name.** If
     `ip.txt` is absent, loads the loose client-root file **`list.dat`** (binary): a 4-byte record
     **count** followed by `count` × **768-byte** records (an internal length field must equal
     `768 × count + 4` or the load is rejected). The active record is selected by the registry value
     **`HKLM\SOFTWARE\crspace\do : servername`** (`REG_SZ`); when the key/value is missing, a
     CP949 default server name baked into the client is used as the key. The selected record's
     **host string is read at record offset +256** and becomes the lobby host. (Per-record full
     layout is otherwise unmapped — see `packets/lobby.yaml` record shape C.)
   - **Tier 3 — hardcoded fallback.** If `ip.txt` is absent **and** the `list.dat` load fails,
     falls back to the hardcoded default **`211.196.150.4`**.
3. Builds the socket address with **`inet_addr`** — i.e. the resolved lobby host must be a
   **dotted-decimal IPv4 literal**; the lobby socket does **NOT** run DNS (`gethostbyname`). Opens a
   TCP stream socket and connects to `{ host, port }`.
4. Connection failure is **non-fatal to the helper** — it logs the error and still returns the
   socket; the caller detects failure by the absence of a valid reply.

> **Re-confirmed (Phase 2b, build 263bd994).** Static IDA re-traced the lobby connect path: it uses
> **`inet_addr`** on a dotted-quad string (NO `gethostbyname`, NO DNS, `getaddrinfo` absent;
> `inet_ntoa` appears only for diagnostic peer-IP display). The separate **game-server** connect
> path uses **`gethostbyname` (DNS)** on a dynamic host string (§2.2 / §3.0). The `211.196.150.4`
> literal is referenced from exactly one function (the lobby connect routine); all call sites traced.

> **Registry note.** `servername` (Tier 2 selector) and `Lastserver` (the selected-server persist of
> §2.1) live under the **same** key `HKLM\SOFTWARE\crspace\do`. The write path uses the lowercase
> `software\crspace\do` spelling, the read paths use `SOFTWARE\crspace\do`; the registry is
> case-insensitive so they address the same key.

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

Each record is four little-endian **signed** 16-bit fields (record stride 8), **all four `i16`**
(SIGNEDNESS static-CONFIRMED CYCLE 9, 263bd994 — see the signedness note below). The meanings were
recovered from the login window's server-list **render** path: a debug format literal of the shape
`"i %d status %d count %d"` ties the fields at record offsets +2 and +4 to "status" and "count"
(load), and the field at +0 is the index fed to the client-local server-name lookup. **Wire-relevant
fields only** — the localized name itself is *not* on the wire (see the name-table note below).

| Offset | Size | Field | Meaning | Confidence |
|---|---|---|---|---|
| +0 | 2 (i16) | `server_id` | Index **1..40** into the client-local localized server-name table. Values outside 1..40 are out of range. Read **signed** at every value use (sign-extending load; the record base is read through a signed-16 element). | HIGH |
| +2 | 2 (i16) | `status_code` | Server availability state. A value `≤ 0` or `> 40` is treated as **invalid / unavailable** (an error label is shown). Special values: **3** = "open-time scheduled" (the open time is rendered from `open_time` at +6); **24** = a distinct "preparing / check" fixed label; **100** = "this is the connected / current selection" sentinel that, with a UI flag set, auto-advances into the channel-connect step. (Loaded **signed**; its branches are equality / small-enum tests, so sign is behaviorally inert, but the field type is `i16`.) | MEDIUM–HIGH |
| +4 | 2 (i16) | `load` | Population / load gauge. Compared against thresholds **1200 / 800 / 500** to choose a colored "load" label and text color (e.g. red above the high threshold, intermediate colors below). The commit gate's `load < 2400` comparison is a **signed strict-less-than** — this is the decisive evidence the field is `i16`. | MEDIUM–HIGH |
| +6 | 2 (i16) | `open_time` | Used **only** when `status_code == 3`: rendered as a packed open-hour / open-minute schedule using a `×10` split (the field, and the `load` field, are each split as `value/10` and `value%10` to feed an `HH:MM`-style display). The exact packing is **inferred, not pinned**. (Read **signed** — the minute use is a signed load + signed division by 10.) | LOW–MEDIUM |

> **Signedness (CONFLICT corrected — binary-won, CYCLE 9, 263bd994).** All four record fields are
> **signed `i16`**. An earlier version of this table listed `server_id (+0)` as `u16`; the binary
> reads it (and every record field) with a sign-extending load, and the only decisive numeric ordering
> branch — the commit gate's `load < 2400` — is a **signed** comparison. The `server_id` **1..40**
> range check is emitted as an unsigned-style range idiom (`(unsigned)(server_id − 1) > 39`), which is
> the usual compiler form for a bounded range test; **do not infer `u16` from that guard** — the field
> is `i16`. This makes the table agree with `packets/lobby.yaml` Record Shape A and
> `specs/frontend_layout_tables.md §4.1`, which already record all four fields as `i16`.

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

> **Empty / error handling (static-CONFIRMED, server-list re-derive 2026-06-23).** The worker stores
> the count and the consuming UI tick branches on it: **count == 0** ⇒ a "no servers" message (UI msg
> id **4027**); **count == −1** (the connect/recv-failure sentinel the worker writes) ⇒ a "fetch error"
> message (UI msg id **4028**); **count > 0** ⇒ the list is shown. Both messages route through the
> auto-counting message box, not the connecting popup. (These ids are presentation; the wire fact is the
> `−1` failure sentinel and the `> 0` gate.)

> **Single-server auto-select (static-CONFIRMED, server-list re-derive 2026-06-23).** When the stored
> count is exactly **1**, the client **skips the manual pick**: it reads record[0], writes its `server_id`
> (+0) into the selected-id / channel-port field, and — if `status_code (+2) == 0` — advances directly to
> the channel-endpoint fetch (§2.2) and persists `Lastserver`. A single-entry roster therefore connects
> with no user interaction.

> **Refresh debounce (static-CONFIRMED).** The list re-fetch control is **debounced to 10 000 ms**: a
> refresh request is a no-op while a fetch is already in flight or if the last fetch completed within the
> last 10 s; otherwise it re-arms the server-list fetch. (Render-side throttle; documented for a faithful
> re-implementation.)

### 2.2 Channel-endpoint fetch (port `10000 + selected server_id`)

After server selection (the commit guard `status_code == 0 && load < 2400` passes), the client:

- **Persists the selection.** The selected **server_id** — read from a **separate parallel `u32`
  array** indexed by `(page, plate)`, *not* from byte +0 of the 8-byte list record — is written to
  the registry value **`HKLM\SOFTWARE\crspace\do : Lastserver`** (`REG_DWORD`). On the next launch
  this is the remembered default selection.
- **Connects the channel query** to `10000 + selected_server_id` against the resolved lobby host (the
  same lobby host as §2.0, `htons(10000 + server_id)`). The channel port offset **is the selected
  server_id itself**. The receive sequence is identical to §2.1 (8-byte wrapper + LZ4).

After decompression:

- The client zero-fills a 30-byte buffer, then takes the **first 30 (0x1E) bytes** of the
  decompressed payload as a **NUL-padded ASCII endpoint token**. The 30 bytes are **not guaranteed
  NUL-terminated**.
- The token is the game server's endpoint, of the form **`"<host> <port>"` with a single SPACE
  (`0x20`) delimiter** (NOT colon, NOT NUL — it is NUL-*padded* but SPACE-*delimited*). It is consumed
  opaquely here; the host/port split happens later (§3.0): host = text before the space, port = the
  decimal-ASCII text after the space (parsed with `atol`).

> **Channel-endpoint payload (wire):** at least **30 bytes**; the first 30 are the ASCII
> `"<host> <port>"` (SPACE-delimited, NUL-padded) endpoint string. Whether a trailing channel array
> follows is **UNVERIFIED** (only the first 30 bytes are read).
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

### 3.0 The game-server connect (dynamic host:port, via DNS)

The game server the client logs into is **NOT a hardcoded literal** — it is whatever the
channel-endpoint query (§2.2) returned. The `"<host> <port>"` token is spliced into the credential
key string handed to the secure-context builder, which:

- splits the token on the single SPACE: **host** = text before the space, **port** = the decimal-ASCII
  text after the space (`atol`);
- stores host + port on the game-connection object;
- connects with **`gethostbyname` (DNS)** — *unlike* the lobby socket, which uses `inet_addr` and
  requires a dotted quad. Because the game connect resolves through `gethostbyname`, the returned
  host token may be **either a DNS name or a dotted-quad IPv4 literal** (both forms resolve).

So the two address resolutions are distinct and must not be conflated:

| Resolution | Host source | Resolver | Form |
|---|---|---|---|
| **Lobby / login server** | `ip.txt` → `list.dat`+`servername` → `211.196.150.4` (§2.0) | `inet_addr` (no DNS) | dotted-decimal IPv4 only |
| **Game server** | the channel-endpoint `"<host> <port>"` token (§2.2) | `gethostbyname` (DNS) | DNS name **or** dotted quad |

> The concrete endpoint string the live server hands out (DNS name vs dotted quad) is
> **CAPTURE-PENDING** — no `.pcapng` is present.

### 3.1 Handshake and authentication (cited)

- Server → client **`0/0 SmsgKeyExchange`** delivers the server's asymmetric public-key material.
- The client immediately replies with **`1/4 CmsgAuthReply`**, which carries the RSA-encrypted
  **staged login credential (the password)**.
- The session is then marked secure.

The full cryptographic detail (PKCS#1 v1.5 type-2 padding, modular exponentiation, reply whitening,
big-endian digit order) is **owned entirely by `specs/crypto.md`** and is not restated here. The
login flow only needs to know that the credential conveyed by `1/4` is the password staged at
login-form time — **the plaintext login blob never carries the password.**

**0/0 payload composition (62 bytes, fixed; root of the per-session cipher).** The 0/0 frame
delivers, in order:

1. a **54-byte key blob** = the server's RSA public key, imported as `(modulus, exponent)`: a
   per-value serialization tag for each value, then for each value a **little-endian `u32` length
   prefix** followed by that many **big-endian digit bytes** (modulus first, then exponent). The two
   digit lengths satisfy **L1 + L2 = 42** (the blob's fixed body budget; the individual split is
   server wire data, read live);
2. **server scalar #1** (4 bytes);
3. **server scalar #2** (4 bytes) — on success the client also stamps a local timestamp into an
   adjacent slot.

Total = 54 + 4 + 4 = **62 bytes**. The two scalars are stored but not interpreted by the client
crypto (their server-side meaning is capture-pending). Layout owned by `specs/crypto.md §6.2`;
field shape mirrored in `packets/0-0_key_exchange.yaml`.

### 3.2 Character list and scene transition

`3/1 SmsgCharacterList` (S2C) is the message that **actually switches the client into the
character-select scene**. Its shape:

- a **3-byte header**: two leading **context bytes** (a server-id context byte and a channel-id
  context byte) followed by a **SlotMask** byte whose **bits 0..4** each indicate whether that slot's
  981-byte record follows (bit *i* set ⇒ slot *i* is occupied; only bits 0..4 are meaningful — see the
  hard 5-slot bound below);
- for each set bit, one **per-slot record of 981 bytes**, read sequentially. The frame total is
  therefore `3 + popcount(SlotMask) × 981`.

Per-slot record (981 bytes total), as consumed by the handler:

| Sub-offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 880 (0x370) | Character descriptor | The full character/spawn record; layout in `structs/spawn_descriptor.md`. Holds the display name, level, hit points, etc. The descriptor's name is compared against the empty-slot sentinel (Section 3.5). |
| 880 | 96 (0x60) | Stats block | Per-slot stat block; layout in `structs/stats.md`. |
| 976 | 1 | Slot flag | A per-slot flag byte (state / availability class). |
| 977 | 4 (u32) | Slot timing | A per-slot timing value (e.g. delete-cooldown / create-time class). |

After parsing all occupied slots, the client derives a **CP949** display name (cleaned/truncated to
**17 bytes**) per slot, then forces the **character-select scene**.

#### 3.2.1 Per-slot record field map — char-select roster + appearance (consumer-confirmed)

The character-select roster and the previewed avatar are built **entirely from the server's per-slot
record**; there is no local appearance store. The fields below were re-confirmed from the **consumer
side** (the select-scene render + the enter-game stage that read them). All offsets are **relative to
the per-slot record / SpawnDescriptor start** (sub-offset 0 of the 981-byte record). The full
descriptor + skeleton chain is **owned by `structs/actor.md` and `specs/skinning.md`** and is not
restated here — only the slots this roster reads.

| Offset | Size | Field | Meaning |
|---|---|---|---|
| +0x00 | 17 | `name` | CP949, NUL-terminated cell (≤ 16 chars + NUL). Space-trimmed for display. Compared against the empty-slot sentinel `"@BLANK@"` (§3.5). |
| +0x2C | 1 (u8) | `appearance_variant` | Doubles as the **row-occupancy gate** (whether the slot renders) **and** as the `variant` input to the appearance formula below. |
| +0x34 | 2 (u16) | `internal_class` | Class id ∈ **{1,2,3,4}** = Musa / Salsu / Dosa / Monk. The `class` input to the appearance formula and the skeleton selector. |
| +0x3A | 2 (u16) | `level` (display) | The level value the **char-select roster renders**. See the level-divergence note below. |
| +0x4C | 4 (f32) | `world_x` | Position X (shown as an integer `"%d , %d"` position label). |
| +0x50 | 4 (f32) | `world_z` | Position Z (the second number of the `"%d , %d"` label). World **Y is not present** in the record. |
| +0x58 | 20 × 16 | `equip_ref_table` | Visible-gear table: 20 entries × 16 bytes; each entry's **leading dword** is a worn-item / part gid. The avatar preview overlays specific slots from this table. |

> **Level display-vs-enter-game divergence (flagged).** The char-select roster reads the displayed
> level from `level` @ **+0x3A**. The **enter-game path**, however, reads the local-player level from
> a **different descriptor region** — approximately **+0x300**, inside the equip / buff block — **not**
> from +0x3A. The two paths therefore source the player level from two distinct offsets. Which offset
> is **authoritative** for the live player level is **live-pending (6-D)**.

**Appearance preview construction.** The avatar shown for an existing list character is composed from
the server descriptor alone:

- **Skeleton selector.** The model identity is
  `model_class_id = 5 × (internal_class + 4 × appearance_variant) − 24`, which for the four starter
  classes yields an appearance-slot identity in **{1, 11, 16, 26}**. That identity selects the
  **catalog skeleton** (the data-driven edge `{1→g1, 26→g2, 11→g3, 16→g4}`; the underlying skeleton
  chain and the `SkinClassId → g{1..4}.bnd` rule are **owned by `specs/skinning.md`**).
- **Outfit overlay.** The visible outfit is layered on top of that skeleton from the
  `equip_ref_table` (+0x58) worn-item gids.

(Cross-links: `structs/actor.md` owns the full SpawnDescriptor layout; `specs/skinning.md` owns the
skeleton/appearance chain. This spec only records which descriptor slots the **char-select roster**
consumes.)

#### 3.2.2 StatBlock, SlotFlag, and Timing (record tail)

| Record sub-offset | Size | Field | Meaning |
|---|---|---|---|
| 880 (0x370) | 96 (0x60) | `StatBlock` | The slot's **primary-stat record**. On enter-game the chosen slot's 96-byte StatBlock is **copied verbatim** into the local-player stat globals — it *is* the character's stat record. Layout owned by `structs/stats.md`. |
| 976 | 1 | `SlotFlag` | Per-slot flag byte that **gates the slot button** (locked / second-password / restricted class). The exact relation/lock semantics are **live-pending (6-D)**. |
| 977 | 4 (u32) | `Timing` | Per-slot timing value, likely a cooldown / relation timestamp (e.g. delete-cooldown / create-time class). Exact meaning **live-pending (6-D)**. |

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

### 3.4 The `3/5 SmsgEnterGameAck` (unsolicited post-login account-ack) and the live enter ladder

> **CORRECTED 2026-06-21 against the live server `211.196.150.4`.** Earlier this section called `3/5`
> the "enter-game acknowledgement" and put it on the enter ladder as `1/9 → 3/5 → 4/1`. A live headless
> run **disproved** that ladder: the only `3/5` arrives as the **unsolicited post-login account-ack**
> right after the roster and **BEFORE any `1/9`**; the enter request itself is answered **directly by
> `4/1`**. So the live enter ladder is **`1/9 → 4/1`** (the `4/1` IS the enter confirmation), and
> **`3/5` is NOT a step in the enter ladder**.

The server pushes **`3/5 SmsgEnterGameAck`**, a **44-byte** payload, **once after login** (right after
the `3/4`/`3/1` roster, step 7) as the account-ack — it carries the account name/billing/char-count and
nudges the scene toward Load, but it does **not**, by itself, enter the world. Because it arrives before
any `1/9`, the in-flight latch is **not armed** when it is received, which is exactly how the client
distinguishes it from a (non-existent on this server) enter-ladder ack.

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | ~28 | Name + leading block | An ASCII/CP949 character name at offset 0, plus other bytes within the leading block. |
| 28 (0x1C) | 4 (u32) | Billing value | Fed to the billing subsystem. |
| 40 | 4 (u32) | Account character count | Trailing u32; the account's total character count. |

> Total = a 40-byte leading block + a trailing u32 = **44 bytes**. `opcodes.md` marks this 44-byte
> size as corroborated by two captures. Field internals of the 40-byte block beyond name@0 and
> billing@28 are **UNVERIFIED**. Field spec: `packets/3-5_enter_game_response.yaml`.

**The in-world transition (live).** The client enters the in-world game state on the **`4/1
SmsgGameStateTick` received while the `1/9` enter request is in flight** (the latch armed by the `1/9`
send). The `4/1` is `4/1`'s own enter confirmation: it clears the latch (its first statement) and drives
the scene Select/Load → InGame, then spawns the local player from the cached descriptor (§3.5). A
latch-**unarmed** `4/1` is an ordinary in-world tick and does not transition the scene; an unsolicited
`3/5` (latch unarmed) does not enter the world; and a `3/100 SmsgCharActionResult` during the enter
phase rejects the enter and keeps the client at char-select.

### 3.5 Local slot caching and the actual spawn

On confirming a slot, before/around sending `1/9`, the client:

- Guards the selection: the slot must be valid (not unselected, **within range — slot index ≤ 4**,
  not disabled, and not already entering). If the slot's character descriptor name equals the literal
  sentinel **`"@BLANK@"`**, the slot is **empty** and the client routes to **character creation**
  instead of entering.
- For a real character, it caches the chosen slot's data into the globals the world load consumes:
  the **880-byte character descriptor**, the **96-byte stats block**, and a couple of trailing
  name/flag bytes, plus a relation flag derived from the slot flag bits.

The local player's **actual world spawn** is then driven by **`4/1 SmsgGameStateTick`** (the
world-entry snapshot), which consumes the **cached descriptor** to materialize the local player in
the world. A `3/14 SmsgCharSpawnResponse` (16-byte payload, Section 5.6) may also arrive as an
enter-into-world *bridge* that re-enters the enter-builder, but it is **not** the local-player world
spawn. After spawn, the world entity is maintained by the major-5 Push handlers (out of scope; see
`opcodes.md` 5/x).

> **Enter-game, in one line (CORRECTED 2026-06-21, live):** send `1/9` (arms the in-flight latch) →
> cache the chosen slot's descriptor locally → on the **`4/1` world-entry snapshot received with the
> latch armed**, transition to the in-world state (Select/Load → InGame), clear the latch, and spawn
> the local player into the world from the cached descriptor. **There is no enter-ladder `3/5` between
> the `1/9` and the `4/1`** — the only `3/5` is the unsolicited post-login account-ack that precedes the
> `1/9`.

### 3.6 Character-select C2S senders (create / slot-select) — cross-reference

The character-select scene emits two additional C2S messages whose **field layouts are authoritative
in their own `packets/*.yaml`**; this is a brief cross-reference only (see also
`specs/frontend_scenes.md` §4 / §8):

- **Create character — `1/6 CmsgCreateCharacter` (52-byte body).** Sent when the player confirms the
  new-character form (after the client-side name gates: a min-length-2 charset rule allowing
  lowercase ASCII + digits + CP949 double-byte pairs, a banned-word filter, and an in-flight
  double-submit guard). The 52-byte body carries the CP949 name at offset 0 plus appearance and
  point-buy stat fields. Authoritative field spec: `packets/cmsg_char_create.yaml`. (The legacy
  `packets/1-6_login_or_create.yaml` is a **resolved tombstone** — opcode 1/6 is character-create
  only and never the login credential, which rides the secure `1/4`; see §4.2.)
- **Select / pre-stage a slot — `1/7` (2-byte body).** A short two-byte select/pre-stage message
  emitted from the per-slot select action before the enter-world request. Authoritative field spec:
  `packets/cmsg_char_select.yaml` (the legacy `packets/1-7_select_character.yaml` is a superseded
  tombstone that points there).

> Both senders are gated by the select screen's single net-busy guard, so only one character
> operation is ever in flight. The full per-field byte tables live in those packet YAMLs — they are
> the single source of truth; this spec does not restate them. The actual world-entry request is the
> separate `1/9` enter-game message (§3.3).

> **CYCLE 6b CORRECTION (2026-06-20) -- the full char-manage C2S/S2C family + the deferred-write rule
> (CODE-CONFIRMED, build 263bd994).** `// confirmed: static IDA 2026-06-20` The select screen emits
> five C2S messages, not two; delete has no dedicated opcode; and the local slot writers run only off
> the server ack:
> - **`1/7` carries a mode byte: `{slot, mode}` (2 bytes).** `mode = 1` = **select-and-play**;
>   `mode = 0` = **slot-lock / pre-play**. Both emit sites are character-**SELECT** code paths in the
>   select-window command handler (one builder, two call sites). **Phase 2b binary-won reversal
>   (build 263bd994):** the earlier reading on this line (`mode = 1` = **delete request**, "delete
>   multiplexed onto `1/7`") is **REFUTED** -- there is **no major-1 char-delete opcode** on this
>   build, and neither 1/7 site is a delete path. Character removal is surfaced only via the inbound
>   major-3 result ladder (`3/7 SmsgCharManageResult` subtype 2, below). The runtime *meaning* of
>   `mode 1` vs `mode 0` is the only part still capture/debugger-pending.
> - **`1/9` enter-game** (40 bytes) -- as `section 3.3`.
> - **`1/6` create character** (52 bytes) -- as above; class map UI{0,1,2,3} to internal {4,1,3,2}.
> - **`1/13` rename character** (18 bytes).
> - **`1/14` move/relocate slot** (1 byte: slot index).
> - **Result: `3/7 SmsgCharManageResult` (8-byte body, subtype byte).** Subtype 2 = **delete confirmed**
>   (decrements the account character count); other subtypes drive the create/rename slot-facing refresh
>   and a same-day delete-cooldown notice. The select scene is then reset/refreshed (it does **not**
>   re-transition; only `3/1` transitions into select -- `section 3.2`).
> - **Deferred-write rule.** The client writes a new slot record and clears a deleted slot **only from
>   the matching S2C result handler** (rename-result / `3/7` manage-result), **never** optimistically on
>   the C2S send. So a create/delete is not reflected locally until the server acknowledges.
> - **No corner-X close; window close = system-close to state 6 / sub-state 8.** The select window
>   builds no frame-X widget; an OS/system-close message routes the scene to **state 6, sub-state 8**
>   (back toward login). The "Back" tab does **not** transition to the server list -- in-window "back"
>   controls only dismiss the create/rename/move sub-panels. (See `ui_system.md section 8.2`,
>   `frontend_scenes.md section 3` / `section 5`.)
> - **Byte-granular blob layouts** of the 40B enter / 52B create / 18B rename / 8B `3/7` bodies remain
>   capture / debugger-pending; the opcode pairings and sizes above are static-confirmed.

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
| — | (separate region, NOT in this plaintext pre-image) | **Password / credential** | Copied into an **exactly-17-byte, zero-padded** buffer and recorded as the RSA plaintext; conveyed only by the `1/4` RSA ciphertext. Never serialized into the plaintext blob. |

**Field identity (runtime-confirmed).** A live debugger session drove a real login and read the
assembled blob out of the client's packet buffer. The previously-unnamed optional string **is the
second-password / PIN** (the value collected by the second-password input — see §1 step 1a and
`specs/frontend_scenes.md` §1). The login form therefore has **three** inputs: the account, the
account password (→ RSA 1/4 ciphertext), and the PIN (→ this optional blob field). The PIN is a
first-class input concept in the client (an input/name object carries an explicit "is-PIN" flag),
which corroborates that the optional field is the PIN rather than an arbitrary auxiliary string.

**Field capacities (runtime-confirmed).** The build validates, and the staged buffers size, as:

| Field | Capacity rule | Notes |
|---|---|---|
| Account | length **≥ 2** and **< 20** characters | Length-prefixed into the blob (incl. NUL). |
| Password | length **≥ 2** and **< 17** characters | **Not** in the plaintext blob; copied into a **zero-padded buffer sized to the password-field cap** (parameter-driven; the **runtime-observed width is 17 bytes** — recorded as a runtime value, not a static literal) held for the RSA 1/4 ciphertext. See `packets/login.yaml`. |
| Second-password / PIN | length **< 5** characters (so **≤ 4** chars + a NUL) | Length-prefixed into the optional blob field (incl. NUL). Numeric in practice. |

**Length-prefix encoding (runtime-confirmed).** Each length-prefixed string is `[u32 little-endian
length][bytes…][NUL]`, and the **u32 length includes the trailing NUL byte**. So a 7-character
account serializes as prefix `0x00000008` followed by 7 bytes + NUL; a 4-character PIN serializes as
prefix `0x00000005` followed by 4 bytes + NUL.

So the wire login pre-image is `[0x2B] [u32len account ] ([u32len PIN ])`, written into the
secure **`1/4`** payload ahead of the RSA ciphertext. The **password is never in this pre-image** --
it is the RSA plaintext `M` (a fixed 17-byte zero-padded buffer) encrypted into the same `1/4`
payload. This is consistent with `specs/crypto.md` (RSA plaintext = the staged login credential
string itself) and `packets/login.yaml` (the credential carrier).

> **Resolved (was an open item):** the identity of the optional middle field — it is the
> **second-password / PIN** — and the length-prefix width (**u32 LE**, NUL-inclusive). These were
> previously marked UNVERIFIED; a runtime read of the live client's assembled blob resolves them.
>
> **Resolved — the credential carrier is `1/4`, not `1/6`.** The front-end deep-fidelity pass
> (campaign4/frontend-deep), dispatch-table-confirmed, settles that the login credential rides the
> secure `1/4` auth-reply (the reactive answer to the inbound `0/0` key exchange) and that **`1/6` is
> character-create only** — there is no shared login/create opcode and no collision. The two builders
> are distinct: the secure auth-reply builder stamps `major=1 / minor=4`; a separate fixed-body
> sender stamps `major=1 / minor=6` for the 52-byte create body. Authored field specs:
> `packets/login.yaml` (the `1/4` credential carrier) and `packets/cmsg_char_create.yaml` (the `1/6`
> 52-byte create body). See Section 6 and Section 9 item 4.

### 4.2a PIN modal — scrambled keypad and the PIN → wire hand-off

This subsection documents the **mechanism** by which the second-password / PIN of §1a is collected
and how its value reaches the optional blob field of §4.2. It is the recovered behaviour behind the
previously-noted "optional second-password blob" / `isPin` gap. Recovered via static RE, CAMPAIGN 9
(static control-flow recovery, no addresses, no debugger).

**Anti-keylogger scrambled keypad (per-show).** The PIN modal presents a 10-digit on-screen keypad
(digits 0–9) whose **on-screen positions are reshuffled every time the modal opens**, so a digit's
screen location is unpredictable from one show to the next (defeating fixed-position keyloggers /
shoulder-surfing). The scramble works as follows, on each show:

1. **Seed the random generator with a 64-bit wall-clock time** (`srand` seeded from a 64-bit time
   value) — a fresh seed per show.
2. **Fisher-Yates shuffle a 10-element digit permutation** (the integers 0–9). The shuffle is the
   classic in-place Fisher-Yates / random-shuffle over the 10-entry permutation array.
3. **Apply the permutation to the keypad layout:** of the stacked digit buttons available at each of
   the 10 on-screen positions, exactly the one whose digit equals `permutation[position]` is made
   visible; the others are hidden. The net result is the digits 0–9 laid out in a fresh random order
   each show.

Because the permutation is regenerated every show, no static digit→position map exists; a faithful
re-implementation must reshuffle on every modal open.

**Entry and masking.** Pressing a visible digit key appends that digit to an entry buffer; the
on-screen field shows only a **masked `*` per entered digit**, never the digits themselves. The
entry is **capped at 4 digits** (a hard guard rejects a 5th), matching the §4.2 capacity rule
"second-password / PIN length **< 5** (≤ 4 chars + NUL)". A clear/backspace control and a cancel
control are distinct from the digit keys; an **OK / submit** control finalizes the entry.

**Submit and the wire hand-off (the `isPin` mechanism).** On submit:

1. The entered digits are serialized into a short numeric **PIN string** (0..4 characters).
2. That PIN string is stored as the login window's **second/middle login-key token**, i.e. it becomes
   the **third TAB-delimited token** of the TAB login-key string assembled at join
   (`account ⟨TAB⟩ password ⟨TAB⟩ PIN ⟨TAB⟩ host␠port`, §1 / §4.1).
3. The secure-context builder (§4.1) splits that TAB string; the PIN (its middle token) is the value
   that **populates the optional length-prefixed second-password blob field of the `1/4` login
   pre-image** (§4.2 field order, table row 3). The account password remains the separate RSA
   plaintext (§4.2) — the PIN is **not** the password.

So the end-to-end PIN path is: **scrambled keypad → masked 0..4-digit entry → submit → third TAB
token of the login-key string → optional second-password blob field of the secure `1/4` pre-image.**
This is the previously-noted `isPin` / "optional second-password blob" gap, now described as a
concrete mechanism.

> **Confidence.** The scramble mechanism (time-seeded Fisher-Yates per show), the 4-digit cap, the
> `*`-masking, and the submit → TAB-third-token → optional `1/4` blob hand-off are **recovered via
> static control-flow analysis (CAMPAIGN 9)** and are graded HIGH for the structural/mechanism
> claims. The exact on-wire **byte layout** of the optional blob field is the runtime-confirmed §4.2
> layout (u32-LE NUL-inclusive prefix). One detail remains **UNVERIFIED / debugger-pending**: the
> precise width and field offset of the login window's PIN-token storage slot (asserted to hold ≤ 4
> chars + NUL) — a single live read would byte-confirm it; it does not change the wire layout, which
> §4.2 already pins.

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

### 5.3 Scene / entity update — `3/4 SmsgSceneEntityUpdate`

A **variable-length** scene / entity / char-slot scratch refill (a slot-scratch refill / scene-clear
on the select surface). The minor-3 receive ladder routes minor **4** here — **not** the 8-byte
char-manage result (that is `3/7`, §5.5). Routing is dispatch-table-confirmed; the field layout is
**not yet specced** (no `packets/*.yaml` authored). `opcodes.md` carries this row as `confirmed`,
field-layout-unspecced.

> The earlier reading that 3/4 carried the 8-byte delete/manage result is **SUPERSEDED**: the
> front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed, places the 8-byte
> manage result at `3/7` and the variable scene-entity update here at `3/4`.

### 5.4 By-name character status patch — `3/23 SmsgCharStatusBytesByName`

A **28-byte** payload — a by-name character status / level patch (NOT a create result; see the
correction note below). The handler matches the CP949 character-name key against the lobby roster
slots and writes two trailing status bytes (a status/flag byte and the character level byte) into the
matched record (and into the local-player globals when the key is the local player).

| Offset (body) | Size | Field | Meaning |
|---|---|---|---|
| 0x00 | 8 | leading block | Copied but **not** used by the name matcher/writer. Its head bytes drive a custom-name-vs-code branch, but the by-name update keys on the name string below. |
| 0x08 | 17 | char[] (CP949) | Character-name **key**, string-matched against the roster names. **(CYCLE 8 binary re-confirm: the name is at body offset 0x08, not 0x00.)** |
| 0x19 (+25) | 1 (u8) | Status flag byte | Written into the matched roster record (and the local flag global when it is the local player). |
| 0x1A (+26) | 1 (u8) | Level byte | Written into the matched roster record (and the local-player level global when it is the local player). |
| 0x1B (+27) | 1 (u8) | Padding | Trailing reserved byte (unused). |

> Offsets are **body-relative** (the payload cursor that begins after the 8-byte wire header).
> Total body = 28 bytes. Roster scratch: ~5 slots, slot stride 880 bytes, name field at slot+0x3C.

> **There is NO dedicated `3/23` create-result message.** An earlier reading modelled `3/23` as a
> 12-byte `SmsgCharCreateResult`; the binary refutes this — the inbound major-3 dispatcher routes `3/23`
> to **`SmsgCharStatusBytesByName`** (28-byte by-name status/level patch). The character-create ack is
> the manage-result latch + **`3/7`** + a refreshed `3/1` character list (which increments the account
> character count) — there is no 12-byte create-result opcode at all. The full `3/23` field table is
> owned by `character_creation.md` §5.1 (the exact interior offsets of the two trailing bytes within the
> 28-byte read are static-partial). The error-string range `0xC8..0xD4` is the **create/rename UI
> error** range surfaced through the create flow (§5.7), not fields of `3/23`.

### 5.5 Character manage / delete result — `3/7 SmsgCharManageResult`

An **8-byte** payload — the delete / select / rename **manage** result. The minor-3 receive ladder
routes minor **7** here (RESOLVED by the front-end deep-fidelity pass (campaign4/frontend-deep),
dispatch-table-confirmed). This is the handler that **decrements the account character count** on a
delete-confirm.

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | 1 = success. |
| 1 | 1 | Padding | |
| 2 | 1 (u8) | Subtype | Manage action selector (0 / 1 / 2). Subtype 2 is **delete-confirm** (decrements the account character count). |
| 3 | 1 | Padding | |
| 4 | 4 (u32) | Ready time | When result indicates a pending state: a Unix time; if in the future, a "wait HH:MM" delete-cooldown message is formatted. |

> The 8-byte size is dispatch-confirmed. The subtype 0 / 1 semantics are **UNVERIFIED** (only subtype
> 2 = delete-confirm is inferred).
>
> The earlier reading that this 8-byte manage result lived at `3/4` is **SUPERSEDED**: it is `3/7`;
> the variable scene-entity update is `3/4` (§5.3), and the 16-byte enter-into-world bridge is `3/14`
> (§5.6). The local-player **world** spawn is driven by `4/1`, not by `3/7`.

### 5.6 Character enter-into-world bridge — `3/14 SmsgCharSpawnResponse`

A **16-byte** payload — an enter-into-world **bridge** / spawn confirm that re-enters the
select-window enter-builder. The minor-3 receive ladder routes minor **14** here (RESOLVED by the
front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed).

| Offset | Size | Field | Meaning |
|---|---|---|---|
| 0 | 1 (u8) | Result | 0 = failure (a timed message is shown); nonzero = proceed. |
| 1 | 1 (u8) | Slot | Character slot index. |
| 2 | 2 | Padding | Alignment. |
| 4 | 4 (u32) | Param 1 | Passed to the bridge routine. |
| 8 | 4 (u32) | Param 2 | Passed to the bridge routine. |
| 12 | 4 (u32) | Param 3 | Passed to the bridge routine. |

> **This is NOT the local-player world spawn.** The actual world spawn is driven by `4/1`
> (Section 3.5); `3/14` is the enter-into-world bridge only. The 16-byte size is dispatch-confirmed;
> the meaning of the three trailing u32s is **UNVERIFIED**. The prior reading that 3/14 was a variable
> scene-entity update is **SUPERSEDED** — the variable scene-entity update is `3/4` (§5.3). `opcodes.md`
> carries this row as `confirmed`, 16-byte field layout not yet specced.

### 5.7 Rename-character result — `3/6 SmsgRenameCharResult`

A **12-byte (0x0C)** payload:

| Offset (body) | Size | Field | Meaning |
|---|---|---|---|
| 0x00 | 1 (u8) | Result | 0 = failure, 1 = success, other = no-op. |
| 0x01 | 1 (u8) | ErrorCode | Failure-only error sub-code (range `0xC8..0xD4`, mapped to UI message buckets). |
| 0x02 | 2 | Padding | Alignment / unused. |
| 0x04 | 4 (f32) | PlacementValue0 | Loaded as an IEEE float on success; forwarded to the char-select slot-record writer. |
| 0x08 | 4 (f32) | PlacementValue1 | Second IEEE-float placement value; forwarded to the slot-record writer. |

> **CYCLE 8 correction (binary-won):** the true body is **12 bytes**, not 19. The handler reads a
> single fixed 12-byte block and performs **no** name string-copy on the success path — the earlier
> 19-byte / embedded-CP949-name reading is refuted. Success/failure is discriminated by the result
> byte at offset 0; the two trailing 4-byte values are IEEE floats (placement), not slot-index/unk
> integers. Offsets are body-relative (post-8-byte-header).

### 5.8 Other major-3 responses (catalog cross-reference, not specced here)

`opcodes.md` additionally lists `3/8 SmsgShopPageUpdate`, `3/13 SmsgCharStatusUpdate`,
`3/100 SmsgCharActionResult`, and `3/50000 SmsgGmChatMessage`. These are routing-confirmed but
**not part of the login/select/enter-game flow**, and their field layouts are not specced here.

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
  name suggesting otherwise), and the message whose handler reads the 28-byte by-name status patch is
  **`3/23 SmsgCharStatusBytesByName`** (NOT a create result — §5.4).
- **Do not** infer any minor→name mapping from legacy handler names. This inconsistency is flagged
  for the project glossary (`names.yaml`) review; it does not change any byte-level behavior in this
  spec.

The behavior-anchored opcode subset for this flow:

| Opcode (major:minor) | Catalog name | Dir | Size | Behavior anchor |
|---|---|---|---|---|
| lobby (port 10000) | server-list | S2C | 8-byte wrapper + N×8 | `wrapper.major` = count (signed `>0` gate, stored in a signed slot with a `−1` sentinel on connect-fail); 8-byte records {id i16, status i16, load i16, open-time i16} (all four **signed**, CYCLE 9) |
| lobby (port `10000+off`) | channel-endpoint | S2C | 8-byte wrapper + ≥30 | first 30 bytes = `host port` ASCII |
| 0:0 | SmsgKeyExchange | S2C | 62 (cited) | RSA key material; triggers `1/4` (see `crypto.md`) |
| 1:4 | CmsgAuthReply | C2S | var (cited) | **THE login credential send** — secure auth-reply to `0/0`; plaintext pre-image `[0x2B][u32len account\0]([u32len PIN\0])` + RSA ciphertext of the account password (runtime-confirmed) |
| 1:6 | CmsgCreateCharacter | C2S | 52 | **Character-create ONLY** — fixed 52-byte body, offset-0 is the CP949 name; NOT the login credential (that is `1/4`) |
| 1:7 | CmsgSelectCharacter | C2S | 2 | character-slot select / pre-stage on the select screen; see `packets/cmsg_char_select.yaml` |
| 1:9 | CmsgEnterGameRequest | C2S | 40 | slot@0 + version token (value 21149 for this build); server → `3/5` |
| 3:1 | SmsgCharacterList | S2C | 3 + N×981 (N ≤ 5) | header[srv, chan, mask] + per-slot {descriptor 880 + stats 96 + flag 1 + time 4}; enters select scene |
| 3:4 | SmsgSceneEntityUpdate | S2C | var | scene / entity / char-slot scratch refill / scene-clear; not yet specced |
| 3:5 | SmsgEnterGameAck | S2C | 44 | name + billing@28 + char_count@40; transitions to in-world |
| 3:6 | SmsgRenameCharResult | S2C | 19 | result + (error code \| new name ASCIIZ[18]) |
| 3:7 | SmsgCharManageResult | S2C | 8 | result + subtype + ready_time (delete cooldown); decrements the account char-count on a delete-confirm |
| 3:14 | SmsgCharSpawnResponse | S2C | 16 | enter-into-world bridge / spawn confirm (re-enters the enter-builder); NOT the local-player world spawn (that is `4/1`) |
| 3:23 | SmsgCharStatusBytesByName | S2C | 28 | by-name status/level patch (name key + status flag byte + level byte); NOT a create result (§5.4) |
| 4:1 | SmsgGameStateTick | S2C | var | world-entry snapshot — drives the **local-player world spawn** from the cached descriptor |

> **Note on the `1/4` / `1/6` rows above.** The login-blob structure (`[0x2B][u32len account\0]
> ([u32len PIN\0])`), the u32-LE NUL-inclusive prefix, and the optional-field identity (the
> second-password / PIN) are **runtime-confirmed** against the live client (no addresses); the
> credential rides the secure `1/4`, and `1/6` is **character-create only** — the front-end
> deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed, resolved the prior
> "1/6 login-or-create collision" (there is none). What this does **not** settle: the per-field
> internals of the manage/bridge results (3/4, 3/7, 3/14) beyond their sizes — those remain
> capture-pending. See Sections 4.2 and 9.

(Major-2 `GameAction` is **C2S only** — there is no inbound handler for it.)

---

## 7. Recovered constants

| Constant | Value | Role |
|---|---|---|
| Lobby base port | **10000** | Server-list fetch; channel = `10000 + selected_offset`. |
| Default fallback IP | **`211.196.150.4`** | Used when `ip.txt` is absent and the resolve list fails. |
| IP override file | **`ip.txt`** | Single token, truncated to **19 characters**. |
| Login sub-opcode byte | **`0x2B` (43)** | First payload byte of the `1/4` login blob pre-image. |
| Login length-prefix width | **u32 little-endian** | Prefix for each login-blob string; **includes the trailing NUL** in its count. Runtime-confirmed. |
| Login account capacity | length **≥ 2** and **< 20** | Length-prefixed (incl. NUL) into the login blob. Runtime-confirmed. |
| Login password capacity | length **≥ 2** and **< 17** | **Not** in the plaintext blob; staged in an **exactly-17-byte zero-padded buffer** for the RSA `1/4` ciphertext. Runtime-confirmed. |
| Login second-password / PIN capacity | length **< 5** (≤ 4 chars + NUL) | The optional length-prefixed login-blob field. Runtime-confirmed. |
| PIN keypad scramble | **time-seeded Fisher-Yates shuffle of a 10-digit (0–9) permutation, per show** | Anti-keylogger random on-screen keypad layout; PIN masked with `*`, capped at 4 digits; submit → 3rd TAB token → optional `1/4` blob field. Static recovery, CAMPAIGN 9. See §4.2a. |
| Channel-endpoint copy length | **30 (0x1E) bytes** | The leading `host port` ASCII string. |
| Server-list record size | **8 bytes** | Count = `wrapper.major`. |
| Server-list record fields | id i16 @+0 (1..40), status i16 @+2, load i16 @+4, open-time i16 @+6 — **all four signed** (CYCLE 9, 263bd994) | See Section 2.1. |
| Server-list load thresholds (UI color) | **1200 / 800 / 500** | Client-side load-gauge coloring (presentation only). |
| Server-list status sentinels (UI) | **3** (open-time scheduled), **24** (preparing/check), **100** (current selection) | Client-side render special cases (presentation only). |
| Server-name table | **41 entries**, indexed by `server_id` (+0) | Client-local localized names; **not on the wire**. |
| Char-list per-slot record | **981 bytes** | 880 descriptor + 96 stats + 1 flag + 4 timing. |
| Char-list header | **3 bytes** | server-id, channel-id, slot bitmask. |
| Char-list maximum slots | **5** (slot indices 0..4) | Hard loop bound; also the enter-game slot-range guard (`slot ≤ 4`). |
| Display-name length (char list) | **17 bytes** | CP949, cleaned/truncated. |
| CreateCharacter body (1/6) | **52 (0x34) bytes** | CP949 name @ offset 0 + appearance + point-buy stats; see `packets/cmsg_char_create.yaml` (§3.6). |
| SelectCharacter body (1/7) | **2 bytes** | Slot select / pre-stage; see `packets/cmsg_char_select.yaml` (§3.6). |
| EnterGameRequest body (1/9) | **40 (0x28) bytes** | Slot index at offset 0. |
| Version token | `10 × versionField + 9` = **21149** (this build) | Derived from `data/cursor/game.ver` (field index 5 = 2114). `sample_verified`. |
| EnterGameAck (3/5) | **44 bytes** | 40-byte block + trailing char-count u32; billing u32 @ +28. |
| CharStatusBytesByName (3/23) | **28 bytes** | By-name status/level patch; NOT a create result (create acked via 3/7 + 3/1 refresh). |
| SceneEntityUpdate (3/4) | **var** | Scene / entity / char-slot scratch refill; not yet specced. |
| CharManageResult (3/7) | **8 bytes** | Result + subtype + ready-time; decrements the account char-count on a delete-confirm. |
| CharSpawnResponse (3/14) | **16 bytes** | Enter-into-world bridge / spawn confirm; NOT the local-player world spawn (that is 4/1). |
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
  `1/4` ciphertext**, never in a plaintext blob.

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
4. **Login vs character-create opcode separation — RESOLVED (statically settled, no capture needed).**
   The front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed, settled this:
   **the login credential send is `1/4`** (the reactive secure auth-reply to the inbound `0/0` key
   exchange, whose RSA pre-image is the `[0x2B][u32len account\0]([u32len PIN\0])` blob, with the
   account password riding inside the `1/4` ciphertext), and **`1/6` is character-create ONLY** (a
   fixed 52-byte body whose offset-0 is the CP949 character name, never the `0x2B` login blob). There
   is **no shared login/create opcode and therefore no collision** — the prior "whether
   character-create shares opcode 1/6" question is closed: it does not. The login-blob *structure*
   (field order `[0x2B][u32len account\0]([u32len PIN\0])`, the u32-LE NUL-inclusive length prefix,
   and the optional field's identity as the **second-password / PIN**) was already runtime-confirmed
   (see §4.2); it is carried by `1/4`, not `1/6`. The two builders are distinct (the secure
   auth-reply builder stamps major=1/minor=4; a separate fixed-body sender stamps major=1/minor=6).
   Authored field specs: `packets/login.yaml` (the `1/4` credential carrier) and
   `packets/cmsg_char_create.yaml` (the `1/6` 52-byte create body).
5. **Enter-game request (1/9)** exact byte **offset** of the version token inside the 40-byte body
   (the token *value* 21149 is pinned from the real `game.ver`; its placement is not). Other body
   bytes beyond slot@0.
6. **Enter-game ack (3/5)** internals of the 40-byte leading block beyond name@0 and billing u32 @+28.
7. **Char-manage result (3/7)** subtype 0 / 1 semantics (only subtype 2 = delete-confirm is inferred);
   the 8-byte size is dispatch-confirmed (front-end deep-fidelity pass (campaign4/frontend-deep),
   dispatch-table-confirmed).
8. **Char enter-into-world bridge (3/14)** meaning of the three trailing u32 params (the 16-byte size
   is dispatch-confirmed; front-end deep-fidelity pass (campaign4/frontend-deep), dispatch-table-confirmed).
   Note this is the enter-into-world *bridge*, **not** the local-player world spawn (that is `4/1`).
9. **Scene / entity update (3/4)** field layout — a variable-length scene / entity / char-slot scratch
   refill; routing is dispatch-table-confirmed (front-end deep-fidelity pass (campaign4/frontend-deep))
   but the body is not yet specced.
10. **Selection-index vs. render-page field separation** (Section 2.2) — the channel-port selection
    index and the select-screen render-page offset are distinct window fields; an earlier note
    conflated them. Naming/glossary concern, flagged for `names.yaml`; no wire impact.
11. **Opcode-naming inconsistency** (Section 6) — legacy handler names disagree with the dispatch
    arithmetic and `opcodes.md`. Anchored to behavior + `opcodes.md` here; flagged for `names.yaml`
    review. Do **not** promote minor→name mappings from legacy names.
12. **PIN-token storage slot width/offset** (§4.2a) — the login window's slot that holds the entered
    PIN string before the join hand-off (asserted ≤ 4 chars + NUL). The scramble mechanism and the
    submit → TAB-third-token → optional `1/4` blob path are static-confirmed (CAMPAIGN 9); the exact
    storage slot width/offset is **UNVERIFIED / debugger-pending** (a single live read would confirm
    it). This does not change the §4.2 wire layout.
13. **Char-list per-slot level offset — display vs enter-game (which is authoritative)** (§3.2.1). The
    char-select roster reads the displayed level from `level` @ +0x3A; the enter-game path reads the
    local-player level from a **different** descriptor region (≈ +0x300, inside the equip / buff block).
    Which offset is the **authoritative** live player level is **live-pending (6-D)**.
14. **Char-list per-slot `SlotFlag` (record +976) relation/lock semantics** (§3.2.2) — gates the slot
    button (locked / second-password / restricted); exact relation semantics **live-pending (6-D)**.
15. **Char-list per-slot `Timing` (record +977, u32) meaning** (§3.2.2) — likely a cooldown / relation
    timestamp; exact meaning **live-pending (6-D)**.
16. **Char-list 3-byte header context-byte VALUES** (§3.2) — the two leading context bytes (server-id /
    channel-id context preceding the SlotMask byte) — their concrete on-wire values are **live-pending
    (6-D)**.
17. **No live network capture was loaded for this analysis.** All wire offsets/sizes are static reads,
    **except** the login blob (carried by `1/4`) field layout, which is corroborated by a runtime read
    of the live client's assembled packet buffer (still not a network capture). The only on-disk
    real-byte corroboration is the local `data/cursor/game.ver` file (item 5 / Section 3.3). The lobby
    (port-10000) mini-protocol in particular has **no network-capture corroboration at all.**
    `capture_verified: false`; `sample_verified: partial` (game.ver only); `runtime_observed: login
    blob (1/4) field layout`.

---

## 10. Status

- Process/socket boundary, two-surface model, lobby protocol shape, base port, fallback IP, and the
  end-to-end ordered flow: **HIGH confidence** (static).
- Lobby server-list 8-byte record decode (id / status / load + thresholds / sentinels): **MEDIUM–HIGH**
  (static, from the render path + a debug literal; `open_time` packing and the full status enum are
  capture-unverified).
- Handshake (`0/0` → `1/4`) and credential staging: **HIGH confidence** (cited — `specs/crypto.md`).
- PIN modal scrambled keypad (time-seeded Fisher-Yates per show), `*`-masking, 4-digit cap, and the
  submit → TAB-third-token → optional `1/4` second-password blob hand-off (§4.2a): **HIGH** for the
  mechanism (static control-flow recovery, CAMPAIGN 9); the exact PIN-token storage slot width/offset
  is debugger-pending.
- Character-select C2S create (`1/6`, 52 B) / slot-select (`1/7`, 2 B) cross-reference (§3.6): **HIGH**
  (static control-flow recovery, CAMPAIGN 9); per-field byte layouts are authoritative in
  `packets/cmsg_char_create.yaml` / `packets/cmsg_char_select.yaml`.
- Character-list 981-byte slot stride **and the hard 5-slot loop bound**; the major-3 response sizes
  (3/1, 3/5, 3/6, 3/7, 3/14, 3/23): **MEDIUM–HIGH** (dispatch-confirmed sizes / bound; field internals
  capture-unverified).
- Login-blob (carried by `1/4`) field layout — order, u32-LE NUL-inclusive length prefix, the three
  field capacities, and the optional field's identity as the **second-password / PIN**: **HIGH**
  (RUNTIME-CONFIRMED against the live client; no addresses). The login-vs-create opcode separation
  (`1/4` credential, `1/6` create-only) is dispatch-table-confirmed (front-end deep-fidelity pass).
- Enter-game-request (1/9) body internals: **MEDIUM** (40-byte total + slot@0 firm; token offset
  unverified). Enter-game **version token value 21149**: **HIGH** (`sample_verified` from the real
  `game.ver`).
- End-to-end confirmation against network captures: **not performed.** `capture_verified: false`;
  `sample_verified: partial` (local `game.ver` only); `runtime_observed: login-blob field layout`.
