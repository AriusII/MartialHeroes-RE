# Login Scene State Machine, Login-String Contract, and the Legacy Lobby Handshake

> Clean-room neutral spec. Promoted from dirty-room analyst notes and a live IDA-debugger login
> confirmation, by the protocol spec-author. No code, no decompiler identifiers, no addresses.
> Behaviour, state tables, and constants only.
>
> **Scope.** This spec owns the *front-end login scene flow*: the boot -> login-form ->
> server-list -> channel-endpoint -> credential-submit -> enter-load sub-state machine, the
> tab-joined login-string contract, the anti-keylogger PIN keypad, and the legacy port-10000
> server-list / channel-endpoint handshake. It is the companion to:
>   - `specs/login_flow.md` -- the broader end-to-end login/char-select/enter-game behaviour.
>   - `specs/crypto.md` section 6 / section 6a -- the secure-context RSA credential encryption.
>   - `packets/login.yaml` -- the credential wire pre-image (0x2B + account + PIN) and RSA framing.
>   - `opcodes.md` Appendix A -- the lobby record formats.
>
> **Implementation targets:** the state machine and string contract are realized in
> `Client.Application`; the legacy lobby fetches in `Network.Transport` / `Client.Infrastructure`.

---

## 0. Status header -- confidence per claim

| Claim | Confidence | Basis |
|---|---|---|
| Two cooperating substate values (a base login window + a derived tick driver) drive one logical login substate. | HIGH | Static behavioural read of both the per-tick driver and the input handler. |
| Sub-state transition table (Section 1): connect -> intro -> login form -> server-list (spawn/wait/consume/select) -> channel-endpoint (spawn/wait) -> submit -> enter-load. | HIGH | Static -- the tick driver and the input handler corroborate every labelled transition. |
| The login string is `account \t password \t PIN \t host:port`, in that physical field order. | HIGH (runtime) | DEBUGGER-VERIFIED -- the three field pointers handed to the credential builder were account, password, PIN, in order. |
| Field capacities: account < 20, password < 17 (staged in an exactly-17-byte zero-padded buffer), PIN < 5. | HIGH (runtime) | DEBUGGER-VERIFIED -- observed validation bounds + the staged buffer size. |
| The PIN comes from an anti-keylogger scrambled numeric keypad and is gated by whether the user committed a PIN. | HIGH | Static -- the keypad shuffle + commit path + the empty-vs-filled PIN slot are explicit. |
| The two lobby fetches (server-list, channel-endpoint) run on dedicated blocking-socket worker threads on port 10000 / 10000+id, with an 8-byte wrapper + LZ4 body, NO byte cipher, NO (major:minor) dispatch. | HIGH | Static. |
| Server-list record stride = 8 bytes; channel-endpoint string = first 30 bytes of the body. | MEDIUM-HIGH | Static -- recovered from the worker copy loops; the exact record fields and endpoint format are capture-unverified. |

> **Capture status.** `capture_verified: false` for the lobby wire shapes and the record/endpoint
> internals. The login-string field order, capacities, and the PIN feed were confirmed against a
> **live IDA-debugger login** (maintainer-driven; never `dbg_start`). The credential byte VALUES were
> never recorded -- only structure, lengths, and field identities.

---

## 1. The login scene sub-state machine

The login scene runs as a single per-tick state machine. Two related window objects cooperate:

- a **base login window** that owns the credential text fields and arms most transitions from input
  events, and
- a **derived "Loginer" window** whose per-tick driver consumes the substate and fires the network
  actions.

Both manipulate **one logical login substate** (the two objects are two views of the same embedded
window hierarchy; the exact embedding is a struct-cartographer detail, out of scope here). The
substate values below are the canonical scene substates the tick driver reads/writes.

| Substate | Phase / screen | Action this tick | Network call | Next |
|---|---|---|---|---|
| 1 | initial / idle | set by ctor and by the cancel action; window left idle | -- | event-driven |
| 2 | **connect / intro** | plays intro audio, resets animated UI, flushes the texture cache, kicks the intro animation | none (local) | 3 |
| 3 | intro animation A | scrolls two UI elements (clamped); per-tick render | -- | 3 -> 4 at clamp |
| 4 | intro animation B | positions elements, toggles widget visibility | -- | 5 |
| 5 | hand-off to form | shows the login-form widgets + the cursor widget | -- | 6 |
| 6 | **login form** (account/password entry) | waits for input; error returns from later states land back here with a field-empty validation popup | -- | event-driven -> server-list start |
| 32 | accept / OK gate | shows the OK widget | -- | seeds server-list flow when OK pressed |
| 33 | OK satisfied | seeds the server-list flow | -- | 34 |
| 34 | **start server-list fetch** | resets list/scroll widgets, **spawns the server-list worker thread** (the thread flips the substate to 35 on entry) | spawns server-list worker (connects port 10000) | 35 |
| 35 | **wait for server list** | tick idles while the worker runs; a re-fetch is refused while here | (worker running) | 36 |
| 36 | **consume server list** | the worker delivered the record count + the record array pointer. If count == 1, auto-select the single server (store the selected id, jump to 38). Error counts: 0 and -1 each raise a distinct popup | -- | 37 (manual) or 38 (auto) |
| 37 | **server selected / list shown** | the user picks a server row; store the selected id, advance | -- | 38 |
| 38 | **start channel-endpoint fetch** | **spawns the channel-endpoint worker thread** (the thread flips the substate to 39 on entry) | spawns channel-endpoint worker (connects port 10000 + selected id) | 39 |
| 39 | **wait for channel endpoint** | tick idles; the worker fetches the host:port endpoint string | (worker running) | 40 |
| 40 | **submit trigger** | the submit block is reached on the next tick | -- | 41 |
| 41 | **credential submit + enter-load** | builds the tab-joined login string (Section 2), records the endpoint, hands the login string to the secure-context builder (the credential-submit path; internals in `specs/crypto.md` section 6 / `packets/login.yaml`), sets the global game state to enter-load, and kicks the scene/texture transition + load widget | the secure-context credential build (the normal game-connection secure session) | enter-load |

Notes:
- States 34..41 are the linear "lobby handshake -> endpoint -> submit" slice. The two lobby fetches
  (34/35/36 and 38/39) run on **dedicated worker threads** over **blocking** sockets (Section 3); only
  the **final** credential submit (41) goes through the real game-connection secure session.
- The intro/animation states (2..5) and the OK gate (32/33) are local UI only -- no socket traffic.

---

## 2. The tab-joined login-string contract (DEBUGGER-VERIFIED)

At the submit state (41), the client assembles a single **tab-delimited** login string and hands it
to the secure-context credential builder. The four fields, in physical order:

```
account  \t  password  \t  PIN  \t  host:port
```

| Field | Source | Notes |
|---|---|---|
| 1. account | the login form's account text field | also copied into the billing/session object. Length >= 2 and < 20. |
| 2. password | the login form's password text field | becomes the staged RSA plaintext M (a fixed 17-byte zero-padded buffer). Length >= 2 and < 17. Conveyed only by the RSA reply -- never in the plaintext pre-image. |
| 3. PIN / second-password | the committed PIN keypad value (Section 4); empty if no PIN was entered | the **optional** length-prefixed field of the credential pre-image (`packets/login.yaml`). Length < 5 (<= 4 chars + NUL). |
| 4. host:port | the channel-endpoint string fetched by the worker (Section 3.2) | further split on a space into a host string and a decimal port; recorded as the login endpoint. |

The credential builder splits this string on TAB (and the final field on the space), records the
host + numeric port as the login endpoint, and stages the plaintext credential pre-image + RSA
plaintext M. The wire result is the secure 1/4 credential carrier -- see `packets/login.yaml` and
`specs/crypto.md` section 6.

**Builder argument contract (DEBUGGER-VERIFIED, structure only):** the builder is invoked with the
login string plus three capacity bounds -- max account length 20, max password length 17 (the
RSA-M buffer width), and a PIN gate of 5. When the PIN gate is nonzero (as in the live login flow),
the optional PIN field is split out and written; when zero, it is omitted.

---

## 3. The legacy lobby handshake (port 10000, blocking, LZ4)

The two lobby fetches do **not** use the game connection's (major:minor) dispatcher or its byte
cipher. They speak a **legacy login wire**: a plain blocking-socket worker connects, receives an
**8-byte frame wrapper**, reads `wrapper.size - 8` more bytes as the body, then **LZ4-decompresses**
the body (raw block, same variant as the game connection -- see `specs/crypto.md` section 3.2). No
byte cipher is applied on this path. The full record formats are catalogued in `opcodes.md`
Appendix A; this section documents the two fetches' behaviour.

### 3.1 Server-list fetch (port 10000)

Spawned by substate 34. On entry the worker flips the substate to 35 and stamps a start time, then:

1. Connects to the lobby on **fixed port 10000**.
2. Receives the 8-byte wrapper, reads the body, LZ4-decompresses it.
3. Interprets the wrapper's "major" field (a signed 16-bit value) as the **server count**.
4. On a positive count, allocates an array of `count` records and copies `8 * count` bytes of
   **8-byte server-entry records** into it; stores the count and the array pointer on the window.
5. On finish, flips the substate to 36 (the tick driver then consumes the list).

Error counts (0 = no servers, -1 = error) each surface a distinct popup. The 8-byte record decodes
to a server id + status / load / open-time presentation fields (full layout in `opcodes.md`
Appendix A; presentation rules capture-unverified).

### 3.2 Channel-endpoint fetch (port 10000 + selected id)

Spawned by substate 38. On entry the worker flips the substate to 39, then:

1. Connects to **port 10000 + selected server id** (the selected id, stashed at select time, is the
   channel port offset; ids 1..40 imply ports 10001..10040).
2. Receives the same 8-byte wrapper + LZ4 body.
3. Zero-fills a 30-byte endpoint buffer, then copies the first **30 bytes** of the decompressed body
   into it as a (not guaranteed NUL-terminated) ASCII **`host port`** endpoint string.
4. On finish, flips the substate to 40 (the tick driver then submits at 41, using this endpoint as
   the login string's field 4 -- Section 2).

> The selected **server id** is the only server identity on the wire (1..40); the localized server
> **display name** is client-local (a 41-entry name table) and must be supplied by a fresh
> implementation -- see `opcodes.md` Appendix A.

---

## 4. The anti-keylogger PIN keypad (the optional second-password)

The PIN / second-password is entered through a dedicated keypad sub-window, modelled as a
first-class input concept (the input object carries an explicit "is-PIN" flag), distinct from the
account and the password. Behaviour:

- **Scrambled face.** The on-screen 0-9 keypad is **re-shuffled on every show** (a time-seeded
  permutation of the digit-button textures) -- a classic anti-keylogger scrambled PIN pad, so screen
  positions do not map to fixed digits.
- **Digit press.** Each press appends a digit to the PIN collection, capped at a **maximum of 4
  digits**, and updates a masked display (one `*` per entered digit).
- **OK / commit.** Commit formats the up-to-4 collected digits into a contiguous decimal string and
  writes it (max 4 chars + NUL) into the billing/session PIN slot, sets a "PIN-ready" flag, and
  re-shuffles the pad.
- **Cancel.** Clears the collection and hides the sub-window.

**Gate.** The PIN is **optional**: if the user never opened and committed the keypad, the PIN slot
stays empty and field 3 of the login string collapses to a blank token between the tabs. When a PIN
is present, it becomes the optional length-prefixed PIN field in the credential pre-image
(`packets/login.yaml`; the builder's PIN gate is active). This is exactly the a7-gated optional PIN
field of the credential build.

---

## 5. Server-list fetch + enter-world handshake

This section spans the two out-of-band lobby fetches (server list, channel endpoint) and the
char-select -> world enter-game handshake. The lobby wire records themselves live in
`packets/lobby.yaml`; this section gives the scene-level flow and the corrected scene-state model.

### 5.1 The two blocking worker-thread fetches (recap)

Both lobby fetches run on **dedicated blocking-socket worker threads** (NOT the main game
dispatch loop). Each connects, receives an **8-byte frame wrapper** (`+0 u32 size` = 8 + payload,
`+4 u16` reinterpreted as the server-entry **record count** on the server-list query, `+6 u16`
unused), reads `size - 8` payload bytes, and **LZ4-decompresses** them (raw block; NO byte cipher).

- **Server-list fetch:** fixed port **10000**. The decompressed payload is `count` packed 8-byte
  server-entry records (record shape + caption mapping in `packets/lobby.yaml`).
- **Channel-endpoint fetch:** port **10000 + selected channel offset**. The decompressed payload's
  first 30 bytes are copied as a fixed `char[30]` ASCII endpoint token (delimiter is needs-capture).

### 5.2 The GameState scene model (the corrected scene-state machine)

The app's top-level driver is a `while`-loop switch on a single **scene index** value (here called
GameState). Each value names ONE scene the driver builds and runs; on that scene's main loop
exiting, the driver re-reads the value to pick the next scene. The recovered scene-index map:

| GameState | Scene | Role |
|---:|---|---|
| 1 | Login window | login / server-select / PIN (loads the message catalog) |
| 2 | **Load handler** | the loading screen (asset preload + progress bar) |
| 3 | Opening window | opening cinematic (skippable) |
| 4 | **Char-select** | character selection |
| 5 | **In-world** | the world scene (FROZEN; not specced here) |
| 6 / 8 | quit / exit | teardown |
| 7 | **error** | shows an error message, then exits |

> **CORRECTION (supersedes the earlier reading).** An earlier dirty note read the login
> credential-submit path as "GameState = 7 = world-entry". Under this scene-index model **7 is the
> ERROR/abort scene, not world-entry.** The normal forward driver into the world is the handshake
> chain in 5.3 (3/5 sets scene 2, then 4/1 sets the enter-world-ready flag, then the select scene's
> end routine sets scene 5), NOT a literal "7 = world". The login-time 7 write is most plausibly a
> guarded abort path; whether it is ever taken on the happy path is **needs-debugger**.

### 5.3 The happy-path enter-world handshake

After the player presses Enter on a filled char-select slot, the sequence is:

1. **C2S 1/9 CmsgEnterGameRequest** (`packets/cmsg_char_enter.yaml`): 40-byte body (slot index +
   33-byte launcher session token + 4-byte derived version token). The enter handler also caches the
   chosen slot's spawn descriptor + stat block into globals and tears down the prior world scene,
   before any reply.
2. **S2C 3/5 SmsgEnterGameAck** (`packets/3-5_enter_game_response.yaml`): the account/billing
   confirm (name + billing flag + account character count). Its side effect sets the scene state to
   **loading (2)** and ends the char-select scene's main loop. This is NOT spawn data.
3. **S2C 4/1 SmsgGameStateTick** (`packets/4-1_game_state_tick.yaml`): builds the **LocalPlayer**
   from the staged spawn descriptor, sets area/region + world X/Z, and sets the select scene's
   **enter-world-ready flag**.
4. When the char-select scene's end routine runs and sees the enter-world-ready flag set, it sets
   the scene state to **in-world (5)**, and the driver builds the world scene.

> **needs-capture / needs-debugger:** (a) the **relative arrival ORDER of 3/5 vs 4/1** -- the single
> load-bearing ordering fact static cannot pin (the enter-world-ready flag set by 4/1 must arrive
> before the select-end routine reads it, or the path routes via the loading scene instead);
> (b) the **1/9 version-dword offset** inside the 40-byte body (register-staged); (c) the
> **channel endpoint host:port format** (the 30-byte token's delimiter).

### 5.4 The loading screen (its own scene)

The loading screen is its **own scene** (the load handler, scene 2), distinct from the login-state
fade VFX. On start it picks ONE random background from `data/ui/loading.dds` /
`data/ui/loading06.dds` / `data/ui/loading08.dds`, plays loading SFX id **920100100**, and draws a
progress bar. Its progress and its exit gate are driven by the **VFS asset PRELOAD finishing**
(progress -> 100%) via a bulk asset-loader worker thread -- it is **NOT** a wait on the enter-game
network ack. The bar fill is the VFS load progress, not a net timer.

---

## 6. Cross-references

- Credential wire layout (0x2B plaintext pre-image + RSA ciphertext framing): `packets/login.yaml`.
- RSA credential encryption, modulus/exponent parse, PKCS#1 v1.5 type-2, 0x29 whitening, and the
  secure-context page-guard lifecycle: `specs/crypto.md` section 6 and section 6a.
- Broader end-to-end login / char-select / enter-game behaviour, server-name table, lobby host
  resolution: `specs/login_flow.md` and `opcodes.md` Appendix A.
- Char-management requests reachable after enter (create / select / enter / rename / slot-move /
  logout): `packets/cmsg_char_*.yaml`, `packets/cmsg_logout.yaml`.
