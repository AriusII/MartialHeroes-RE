---
verification: re-confirmed 2026-06-22 against doida.exe binary IDB SHA 263bd994 (static IDA).
  Substate ladder re-keyed to binary-confirmed numbering (substates 1-5 corrected per dossier
  reconciliation pass 2026-06-22): 1=intro-start+SFX→2; 2=curtain slide; 3=curtain snap; 4=auto-advance
  (CORRECTED 2026-06-22 LOGIN-VISUAL: was "enter-edge" — binary-won, counter-check IDB 263bd994; state 4
  is unconditionally auto-advancing in the tick; the opening is fully non-interactive 1→2→3→4→5→6);
  5=show-form→6; 6=form active. Worker thread count corrected to TWO real procs + one NULL slot.
  Field +0x554 / +341 clarified as the selected-server-index marker (not FSM seed). PIN keypad
  build-order actions 0..99 clarified (digit mapping is handler-time, not action==digit). 22 notice
  labels re-attributed to the notice panel (+0x2BC), distinct from the server-listbox container.
  2026-06-22 field-cap correction (binary-won, IDB SHA 263bd994): ID textbox per-keystroke length cap
  = 16 (GUTextbox field +0xD0); charset mask = 6 (field +0xA4, alphanumeric filter, NOT a length).
  Password textbox per-keystroke length cap = 12 (GUTextbox field +0xD0). Prior readings of "6 chars"
  for account and "129 chars" for password in §2.1 / §7.5 are corrected throughout this spec.
  2026-06-22 LOGIN-LAYOUT promote: GU coordinate system (root-anchor centering formula, local-vs-absolute
  cascade, top-left Y-down origin, 1:1 source-rect contract) promoted to
  specs/frontend_layout_tables.md §0a; cross-reference added to §7 (IDB SHA 263bd994).
ida_reverified: 2026-06-22
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory - subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
conflicts: happy-path GameState=7 reachability (connect-watchdog vs connect-success race) is capture/debugger-pending; the credential wire opcode value is capture/debugger-pending; PIN scramble seed/permutation (clock-seeded, debugger-pending); 8-byte server-record field widths/signedness at roster breakpoint (debugger-pending); msg 4001..4022 semantic content (CP949 strings in data/script/msg.xdb — not statically recoverable)
promoted_from: >
  Dirty-room notes login_curtain_credentials.md, login_pin_modal.md, login_server_list.md
  (2026-06-21): curtain animation positional-translate mechanics confirmed (sub-state slide dst_y
  formulas, reveal/end thresholds, snap coordinates); GUComponent geometry field contract added;
  credential-form widget construction order (build indices 56-65), IME charset modes 16/12,
  shared src rect (615,404), three-branch saved-ID prefill logic documented; PIN modal lifecycle
  call order, Reset=clear-and-reshuffle (not no-op), 100-button over-build structure, and
  on-show clear-then-scramble-then-show ordering added. All load-bearing numeric values
  pre-existing in the spec; this pass adds construction-order tables and mechanism clarifications.
  mask glyph px/char advance and caret blink cadence remain render-path unknowns (not in BuildScene).
  atlas sample cross-check (login_slice1.dds dimensions vs src rects) remains sample-unverified.
  Reconciled dossier (login_reconciled_263bd994.md, 2026-06-22): substate ladder off-by-one corrected;
  worker-thread count corrected; field +0x554 role clarified; PIN action-to-digit mapping clarified;
  notice-label parent corrected.
---

# Login Scene State Machine, Login-String Contract, and the Legacy Lobby Handshake

> Clean-room neutral spec. Promoted from dirty-room analyst notes (Campaign 10 Block B lanes B7/B9,
> a control-flow + immediate-operand read of the login window ctor, BuildScene, Tick, OnEvent, the two
> lobby worker threads, the secure-context builder, the PIN keypad class, and WinMain) plus an earlier
> live IDA-debugger login confirmation, by the protocol spec-author. No code, no decompiler
> identifiers, no addresses. Behaviour, state tables, structure offsets, and constants only.
>
> **Scope.** This spec owns the *front-end login scene flow*: the boot -> login-form ->
> credential-validate -> PIN-modal -> server-list -> channel-endpoint -> credential-submit ->
> enter-load sub-state machine, the tab-joined login-string contract, the anti-keylogger PIN keypad,
> the code-baked LoginWindow element layout (73 widgets), and the legacy port-10000 server-list /
> channel-endpoint handshake. It is the companion to:
>   - `specs/login_flow.md` -- the broader end-to-end login/char-select/enter-game behaviour.
>   - `specs/frontend_scenes.md` -- the front-end window framework and atlas inventory.
>   - `specs/crypto.md` section 6 / section 6a -- the secure-context RSA credential encryption.
>   - `packets/login.yaml` -- the credential wire pre-image (account + PIN) and RSA framing.
>   - `opcodes.md` Appendix A -- the lobby record formats.
>
> **Implementation targets:** the state machine and string contract are realized in
> `Client.Application`; the legacy lobby fetches in `Network.Transport` / `Client.Infrastructure`;
> the code-baked widget layout in `Client.Godot` (layer 05).

---

## 0. Status header -- confidence per claim

The campaign is **static-only** by design (no debugger this pass). Facts derived from IDB
control-flow + immediate operands are **[confirmed]** -- absence of a live debugger does NOT downgrade
a control-flow-confirmed fact to a hypothesis. Only genuinely runtime-dependent items are flagged
capture/debugger-pending.

| Claim | Confidence | Basis |
|---|---|---|
| One logical login substate field drives the scene; the per-tick driver and the input handler are two views of the **same dword** (the tick reads the primary window's substate field; the event handler reaches the identical field through the secondary event-handler sub-object). | [confirmed] | Static control-flow: tick and event handler resolve to the same object offset. |
| Sub-state transition table (Section 1), including the previously-missing **29 (credential validation), 30 (quit), 31 (show PIN modal)** and the corrected **32 (PIN-modal poll)**. | [confirmed] | Static -- the tick driver and the input handler corroborate every labelled transition. |
| The login string is `account \t password \t PIN \t host SP port`, in that physical field order. | [confirmed] | Static control-flow (string assembly + the builder's tab-split chain); earlier debugger run corroborated the field identities. |
| Submit-time validation caps: account staged at <= 20, password staged in a 17-byte RSA-M buffer, PIN <= 5 (4 digits + NUL). The text-INPUT caps are different (see Section 2.1). | [confirmed] | Static -- the three capacity bounds are immediate operands at the builder call. |
| The PIN comes from an anti-keylogger scrambled numeric keypad (the `LoginSecondPassword` modal child) and is gated by whether the user committed a PIN. | [confirmed] | Static -- the keypad shuffle + commit path + the empty-vs-filled PIN slot are explicit. |
| The two lobby fetches (server-list, channel-endpoint) run on dedicated blocking-socket worker threads on port 10000 / 10000+id, with an 8-byte wrapper + LZ4 body, NO byte cipher, NO (major:minor) dispatch. | [confirmed] | Static. |
| Server-list record stride = 8 bytes; channel-endpoint string = first 30 bytes of the body, ASCII `host SP port`. | [confirmed] | Static -- recovered from the worker copy loops and the builder's space-split. |
| Server-list record presentation fields (status / load / open-time) decode. | [static-hypothesis] | Static -- the record layout is recovered; the exact presentation rules are capture-unverified. |
| Whether `GameState = 7` is ever reached on a *successful* login (the 30s connect-watchdog vs connect-success race). | [capture/debugger-pending] | Static cannot prove the override timing. |
| The credential wire OPCODE VALUE (the byte that frames the credential pre-image). | [capture/debugger-pending] | Owned by `packets/login.yaml`; the value is not pinned by this static pass. |

---

## 1. The login scene sub-state machine

The login scene runs as a single per-tick state machine over **one** substate field on the login
window object. Two related window objects cooperate as two views of the same embedded hierarchy:

- a **base login window** that owns the credential text fields and arms most transitions from input
  events (the input handler reads the substate through its secondary event-handler sub-object), and
- a **derived per-tick driver** whose tick routine consumes the substate and fires the network
  actions (it reads the substate through the primary window object).

Both reach **the same logical substate dword** -- the secondary sub-object's substate field and the
primary window's substate field resolve to one and the same object offset. The substate values below
are the canonical scene substates the tick driver reads/writes.

| Substate | Phase / screen | Action this tick | Network call | Next |
|---|---|---|---|---|
| 1 | **intro start** | plays the intro stinger SFX id **861010105** (category 2), resets the two curtain images, sets the curtain offset to 0, hides widgets, kicks the effect manager | none (local) | 2 |
| 2 | **curtain slide** | adds **+5** per tick to the global curtain offset; slides the TOP curtain panel up (`dst_y = -offset`) and the BOTTOM curtain panel down (`dst_y = offset + 326`); when offset exceeds **200** snaps the **form decorative plate** (credential-area chrome, atlas A1 src 0,469 size 494×113) to screen position **(494, 469)** (CORRECTED 2026-06-22: was "login-background plate" — binary-won; the snap target is the form plate member +0x27C, not the background); when offset exceeds **222**, advances to 3 | -- | 3 |
| 3 | **curtain snap / hide** | one-shot: snaps the TOP curtain to `dst_y = -222` and BOTTOM to `dst_y = 548`; resets the curtain offset to 0; hides the two curtain panels and several worker widgets; advances to 4 | -- | 4 |
| 4 | **auto-advance** | the tick's substate-4 case **unconditionally writes 5** with no key check and no event guard — CORRECTED 2026-06-22 (was "Enter edge"; binary-won, counter-check IDB SHA 263bd994). The opening is fully non-interactive; state 4 is never dwell-occupied. | -- | 5 (auto) |
| 5 | **show form** | shows the login-form widgets + the cursor widget; advances to 6 | -- | 6 |
| 6 | **login form** (account/password entry) | waits for input. Enter / the Login button runs the **`game.ver` client-version gate** (Section 1.1) then advances to 29. Errors from later states land back here with a field-empty validation popup | -- | event-driven -> 29 |
| **29** | **credential validation** | requires account length **>= 4** (else return to 6 with popup msg **4025**) and password length **>= 1** (else return to 6 with popup msg **4026**); if the save-ID checkbox is set, persists the account name to the INI (Section 1.3); on success advances to 31 | -- | 31 (ok) / 6 (reject) |
| **30** | **quit** | sets the quit/teardown scene indices (drives GameState quit -> the terminal sub-state) | -- | exit |
| **31** | **show PIN modal** | hides the form's confirm widget and **shows the PIN/second-password modal child** (the `LoginSecondPassword` child at object offset +0x550); advances to 32 | -- | 32 |
| **32** | **poll PIN modal** | reads the modal child's *visible* flag (child +0x8C) AND its *submitted* flag (child +0x2B4); when **both** are set, the PIN has been committed -> advances to 33. This is the click-vs-submit discriminator for the modal, NOT an "OK gate" | -- | 33 |
| 33 | **PIN satisfied** | seeds the server-list flow: prepares the worker widgets, advances to 34 | -- | 34 |
| 34 | **start server-list fetch** | resets list/scroll widgets, **spawns the server-list worker thread** (the thread flips the substate to 35 on entry) | spawns server-list worker (connects port 10000) | 35 |
| 35 | **wait for server list** | tick idles while the worker runs; a re-fetch is refused while here | (worker running) | 36 |
| 36 | **consume server list** | the worker delivered the record count + the record-array pointer. If count == 1, auto-select the single server (store the selected id from `record[0]`, write the last-server preference, jump to 38). Error counts: **0** raises popup msg **4027**, **-1** raises popup msg **4028** | -- | 37 (manual) or 38 (auto) |
| 37 | **server selected / list shown** | the user picks a server plate (Section 1.2); store the selected id, write the last-server preference, advance | -- | 38 |
| 38 | **start channel-endpoint fetch** | **spawns the channel-endpoint worker thread** (the thread flips the substate to 39 on entry) | spawns channel-endpoint worker (connects port 10000 + selected id) | 39 |
| 39 | **wait for channel endpoint** | tick idles; the worker fetches the `host SP port` endpoint string | (worker running) | 40 |
| 40 | **submit trigger** | the submit block is reached on the next tick | -- | 41 |
| 41 | **credential submit + enter-load** | builds the tab-joined login string (Section 2), records the endpoint, hands the login string to the secure-context builder (the credential-submit path; internals in `specs/crypto.md` section 6 / `packets/login.yaml`), sets the global game state and **arms a 30-second connect watchdog** (Section 1.4), and kicks the scene/texture transition + load widget | the secure-context credential build (the normal game-connection secure session) | enter-load |

Notes:
- States 34..41 are the linear "lobby handshake -> endpoint -> submit" slice. The two lobby fetches
  (34/35/36 and 38/39) run on **dedicated worker threads** over **blocking** sockets (Section 3); only
  the **final** credential submit (41) goes through the real game-connection secure session.
- The intro/animation states (1..5) are local UI only -- no socket traffic.
- **There is NO EULA / terms-agreement gate in this flow.** The previous revision modelled
  substates 32/33 as an "accept/OK gate (shows OK widget)"; that was wrong. 31 *shows* the PIN modal
  and 32 *polls* it. See Section 4 and the explicit no-EULA note in Section 1.5.

### 1.1 The client-version (`game.ver`) gate (in front of 6 -> 29)

Before the login form can advance to credential validation, the Enter key / Login button at substate
6 runs a client-version check: it reads the on-disk `data/cursor/game.ver` resource and compares it
against the expected `game.ver` value. On a mismatch it shows message **2204** ("ERROR") in a modal
box and refuses to advance; on a match it sets the tick substate to **29**.

### 1.2 Server-plate pick (substate 37)

At substate 37 the server list is shown as two name-plate columns (two plates per page; a pager
selects the page). A plate pick is a click event whose action id is 400 or 401 (left/right column).
The selected record index is `(action - 400) + 2 * page`. The pick is committed only if the record
passes the guard `record.status == 0 && record.load < 2400`; on commit the client stores the selected
server id, writes the last-server preference, and advances to 38. The pager buttons (action ids
115..124) set `page = action - 115`, repaint the two-plate view, and make no state change.

### 1.3 Remember-username (INI persistence)

When the save-ID checkbox is set at substate 29, the client writes the account name into the client
INI under section `[DO_OPTION]`, key `OPTION_ID`. At BuildScene time the saved id is read back and, if
present and **not** the literal sentinel `"(null)"`, is pre-filled into the account textbox and focus
moves to the password textbox; if absent/empty/`"(null)"`, the account textbox is left empty and
focus stays on the account textbox. (See the conditional default-focus note in Section 7.5.)

### 1.4 The substate-41 connect watchdog (GameState=7, timer 10001)

At submit (substate 41), after building the secure context, the client sets the global game state to
**7** **and** enqueues a **30000 ms** timer event with id **10001**. Under the GameState scene model
(Section 5.2), scene index 7 is the **error** scene. This `7` write is therefore best read as a
**30-second connect watchdog**: it is pre-armed at submit and is expected to be overwritten by the
connection-success path before the 30 s timer fires. If the connection never succeeds, the watchdog
timer (event 10001) fires, breaks the login loop, and the error scene is shown.

> **[capture/debugger-pending]** Whether `GameState = 7` is ever observed on a *clean, successful*
> login (i.e. whether connect-success always wins the race against the 30 s watchdog) cannot be
> proven by static analysis. The control-flow facts (the `7` write, the 30 s timer, the watchdog
> handler breaking the loop) are all [confirmed]; only the happy-path reachability of `7` is pending.

### 1.5 No EULA / agreement panel in the login flow

The previous revision (and a separate analysis lane) inferred a "built-but-hidden EULA / terms-of-use
panel" in the login construct. **The element-by-element construct walk supersedes that reading: there
is NO EULA / agreement panel built anywhere in the LoginWindow construct.** The 22 text labels
populated from message-db ids **4001..4022** -- which the inference mistook for EULA body text -- are
in fact the **server-list / channel ROW CAPTIONS**, parented to the server-listbox container (Section
7.1). No substate ever shows an agreement panel and no event handler routes one. A faithful port
must NOT present an EULA/agreement step as part of login.

---

## 2. The tab-joined login-string contract

At the submit state (41), the client assembles a single **tab-delimited** login string and hands it
to the secure-context credential builder. The four fields, in physical order:

```
account  \t  password  \t  PIN  \t  host SP port
```

| Field | Source | Notes |
|---|---|---|
| 1. account | the login form's account text field (object +0x2A8) | also copied into the billing/session object. Submit-time bound: length >= 2 and <= 20. (Text-input cap is tighter -- see Section 2.1.) |
| 2. password | the login form's password text field (object +0x2AC) | becomes the staged RSA plaintext M (a fixed 17-byte zero-padded buffer). Submit-time bound: length >= 2 and < 17. Conveyed only by the RSA reply -- never in the plaintext pre-image. |
| 3. PIN / second-password | the committed PIN keypad value (Section 4); empty if no PIN was entered | the **optional** length-prefixed field of the credential pre-image (`packets/login.yaml`). Length < 5 (<= 4 chars + NUL). |
| 4. host port | the channel-endpoint string fetched by the worker (Section 3.2) | a **space-delimited** ASCII token: a host string, a single space (0x20), then a decimal port. The builder splits on the space and parses the port with a decimal-string-to-long conversion. Recorded as the login endpoint. |

The credential builder splits this string on TAB (and the final field on the SPACE), records the
host + numeric port as the login endpoint, and stages the plaintext credential pre-image + RSA
plaintext M. The wire result is the secure credential carrier -- see `packets/login.yaml` and
`specs/crypto.md` section 6. (The exact wire opcode value is owned by `packets/login.yaml` and is
**[capture/debugger-pending]** for this static pass.)

**Builder argument contract (structure only):** the builder is invoked with the login string plus
three capacity bounds -- max account length **0x14 (20)**, max password length **0x11 (17)** (the
RSA-M buffer width), and a PIN gate of **5**. When the PIN gate is nonzero (as in the live login
flow), the optional PIN field is split out and written; when zero, it is omitted.

### 2.1 Field caps: text-INPUT cap vs submit-time validation cap

The text **input** caps baked into the BuildScene textboxes differ from the submit-time validation
caps, and a faithful port must respect both:

| Field | Text-input cap (BuildScene) | Submit-time cap (builder) | Net effective |
|---|---|---|---|
| account | textbox per-keystroke cap = **16** chars (GUTextbox field +0xD0; charset mask field +0xA4 = 6, meaning alphanumeric-only filter, NOT a length) | validation requires >= 2 and <= 20 | account is effectively **2..16** chars (textbox cap is the binding constraint) |
| password | textbox per-keystroke cap = **12** chars (GUTextbox field +0xD0; masked render via mask bit) | validation rejects length >= 17; staged in the 17-byte RSA-M buffer | password is effectively **2..12** chars (textbox cap is the binding constraint) |

> **Correction (binary-won, static IDA, IDB SHA 263bd994).** Prior revisions recorded the account
> textbox cap as 6 chars and the password textbox cap as 129 chars; those were wrong. The per-keystroke
> length cap lives in GUTextbox field +0xD0 (offset 208): ID textbox has +0xD0 = **16**, password
> textbox has +0xD0 = **12**. Both caps are enforced by the per-keystroke input handler and the paste
> handler (a character or paste that would exceed the cap is rejected). The value **6** quoted by
> earlier readings was the ID field's charset filter bitmask (+0xA4 = 0x06, alphanumeric-only), not a
> length. The value **129** (0x81) was the password field's charset mask (+0xA4, with the allow-all bit
> set so any printable character is accepted), not a length. A faithful reimplementation must cap the
> ID field at 16 characters and the password field at 12 characters during input.

---

## 3. The legacy lobby handshake (port 10000, blocking, LZ4)

The two lobby fetches do **not** use the game connection's (major:minor) dispatcher or its byte
cipher. They speak a **legacy login wire**: a plain blocking-socket worker connects, receives an
**8-byte frame wrapper**, reads `wrapper.size - 8` more bytes as the body, then **LZ4-decompresses**
the body (raw block, same variant as the game connection -- see `specs/crypto.md` section 3.2). No
byte cipher is applied on this path. The full record formats are catalogued in `opcodes.md`
Appendix A; this section documents the two fetches' behaviour.

8-byte wrapper layout: `+0 u32 size` (= 8 + payload bytes); `+4 i16` reinterpreted as the
**server count** on the server-list query (signed); `+6 u16` unused.

### 3.1 Server-list fetch (port 10000)

Spawned by substate 34. On entry the worker flips the substate to 35 and stamps a start time, then:

1. Connects to the lobby on **fixed port 10000**.
2. Receives the 8-byte wrapper, reads the body, LZ4-decompresses it.
3. Interprets the wrapper's signed 16-bit count field as the **server count**.
4. On a positive count, allocates an array of `count` records and copies `8 * count` bytes of
   **8-byte server-entry records** (starting at body offset +8) into it; stores the count and the
   array pointer on the window.
5. On finish, flips the substate to 36 (the tick driver then consumes the list).

Error counts (0 = no servers -> popup msg 4027; -1 = error -> popup msg 4028) each surface a distinct
popup. The 8-byte record decodes to a server id + status / load / open-time presentation fields (full
layout in `opcodes.md` Appendix A; presentation rules capture-unverified). The wire server id is the
first 16-bit field of the 8-byte record; the auto-select path (count == 1) reads `record[0]`'s id.

### 3.2 Channel-endpoint fetch (port 10000 + selected id)

Spawned by substate 38. On entry the worker flips the substate to 39, then:

1. Connects to **port 10000 + selected server id** (the selected id, stashed at select time, is the
   channel port offset; ids 1..40 imply ports 10001..10040).
2. Receives the same 8-byte wrapper + LZ4 body.
3. Zero-fills a 30-byte endpoint buffer, then copies the first **30 bytes** of the decompressed body
   (from body offset +8) into it as an ASCII **`host SP port`** endpoint string (a host string, a
   single space, then a decimal port -- not guaranteed NUL-terminated).
4. On finish, flips the substate to 40 (the tick driver then submits at 41, using this endpoint as
   the login string's field 4 -- Section 2).

> The selected **server id** is the only server identity on the wire (1..40); the localized server
> **display name** is client-local and must be supplied by a fresh implementation -- see
> `opcodes.md` Appendix A.

> **Correction.** The previous revision marked the field-4 endpoint delimiter "needs-capture". The
> builder splits field 4 on a literal **space (0x20)** then parses the port as a decimal string, so
> the delimiter is **confirmed = SPACE**.

---

## 4. The anti-keylogger PIN keypad (the optional second-password)

The PIN / second-password is entered through a dedicated keypad sub-window -- the `LoginSecondPassword`
modal, embedded as a child at login-window object offset **+0x550** and shown/polled by substates 31
and 32 (Section 1). It is modelled as a first-class input concept (the input record carries an
explicit "is-PIN" flag), distinct from the account and the password. Behaviour:

- **Scrambled face.** The on-screen 0-9 keypad is **re-shuffled on every show** (a time-seeded
  Fisher-Yates permutation of the digit-button textures) -- a classic anti-keylogger scrambled PIN
  pad, so screen positions do not map to fixed digits.
- **Digit press.** Each press appends a digit to the PIN collection, capped at a **maximum of 4
  digits**, and updates a masked display (one `*` per entered digit).
- **OK / commit.** Commit formats the up-to-4 collected digits into a contiguous decimal string and
  writes it (bounded copy: max 4 chars + NUL into a 5-byte slot) into the billing/session PIN slot,
  sets a "PIN-ready"/submitted flag, and re-shuffles the pad.
- **Cancel.** Clears the collection and hides the sub-window.

**Gate.** The PIN is **optional**: if the user never committed the keypad, the PIN slot stays empty
and field 3 of the login string collapses to a blank token between the tabs. When a PIN is present, it
becomes the optional length-prefixed PIN field in the credential pre-image (`packets/login.yaml`; the
builder's PIN gate is active). The modal's visible/submitted flags are exactly what substate 32 polls
to know the PIN was committed.

The keypad's full element layout (100 digit buttons, Reset/OK/Cancel, the masked echo label, the
nested close panel) is in Section 7.6.

---

## 5. Server-list fetch + enter-world handshake

This section spans the two out-of-band lobby fetches (server list, channel endpoint) and the
char-select -> world enter-game handshake. The lobby wire records themselves live in
`packets/lobby.yaml`; this section gives the scene-level flow and the scene-state model.

### 5.1 The two blocking worker-thread fetches (recap)

Both lobby fetches run on **dedicated blocking-socket worker threads** (NOT the main game
dispatch loop). Each connects, receives an **8-byte frame wrapper** (`+0 u32 size` = 8 + payload,
`+4 i16` reinterpreted as the server-entry **record count** on the server-list query, `+6 u16`
unused), reads `size - 8` payload bytes, and **LZ4-decompresses** them (raw block; NO byte cipher).

- **Server-list fetch:** fixed port **10000**. The decompressed payload is `count` packed 8-byte
  server-entry records (record shape + caption mapping in `packets/lobby.yaml`).
- **Channel-endpoint fetch:** port **10000 + selected channel offset**. The decompressed payload's
  first 30 bytes are copied as a fixed 30-byte ASCII `host SP port` endpoint token (delimiter is a
  space -- Section 3.2).

### 5.2 The GameState scene model

The app's top-level driver is a `while`-loop switch on a single **scene index** value (here called
GameState). Each value names ONE scene the driver builds and runs; on that scene's main loop exiting,
the driver re-reads the value to pick the next scene. The recovered scene-index map (8 cases,
GameState 0..7; the value **8** is a terminal sub-state, not a scene):

| GameState | Scene | Role |
|---:|---|---|
| 0 | cold bootstrap | one-time startup (mounts the VFS once, before the loop) |
| 1 | **Login window** | login / server-select / PIN (loads the message catalog) |
| 2 | **Loading window** | the loading screen (asset preload + progress bar); also the opening-skip gate |
| 3 | Opening window | opening cinematic (skippable) |
| 4 | **Char-select** | character selection (SelectWindow) |
| 5 | **In-world** | the world scene (FROZEN; not specced here) |
| 6 | quit -> 8 | teardown (drives the terminal sub-state 8) |
| 7 | **error** | shows an error message box, then exits |

> **Note.** **Login is GameState 1; char-select is GameState 4.** (An earlier stray reading that put
> "login = 4" was wrong.) The terminal value **8** is reached from the quit path (6 -> 8), not a
> distinct scene.

> **CORRECTION (retained).** An earlier dirty note read the login credential-submit path as
> "GameState = 7 = world-entry". Under this scene-index model **7 is the ERROR/abort scene, not
> world-entry.** The normal forward driver into the world is the handshake chain in 5.3, NOT a literal
> "7 = world". The login-time `7` write at submit is the **30-second connect watchdog** (Section 1.4):
> `GameState = 7` + timer event 10001, pre-armed at submit, expected to be overridden by
> connect-success. Whether 7 is ever taken on the happy path is **[capture/debugger-pending]**.

### 5.3 The happy-path enter-world handshake

After the player presses Enter on a filled char-select slot, the sequence is:

1. **C2S CmsgEnterGameRequest** (`packets/cmsg_char_enter.yaml`): 40-byte body (slot index +
   33-byte launcher session token + 4-byte derived version token). The enter handler also caches the
   chosen slot's spawn descriptor + stat block into globals and tears down the prior world scene,
   before any reply.
2. **S2C SmsgEnterGameAck** (`packets/3-5_enter_game_response.yaml`): the account/billing confirm
   (name + billing flag + account character count). Its side effect sets the scene state to
   **loading (2)** and ends the char-select scene's main loop. This is NOT spawn data.
3. **S2C SmsgGameStateTick** (`packets/4-1_game_state_tick.yaml`): builds the **LocalPlayer** from the
   staged spawn descriptor, sets area/region + world X/Z, and sets the select scene's
   **enter-world-ready flag**.
4. When the char-select scene's end routine runs and sees the enter-world-ready flag set, it sets the
   scene state to **in-world (5)**, and the driver builds the world scene.

> **[capture/debugger-pending]:** (a) the **relative arrival ORDER** of the enter-game ack vs the
> game-state tick -- the single load-bearing ordering fact static cannot pin (the enter-world-ready
> flag set by the tick must arrive before the select-end routine reads it, or the path routes via the
> loading scene instead); (b) the enter-game-request **version-token offset** inside the 40-byte body
> (register-staged).

### 5.4 The loading screen (its own scene)

The loading screen is its **own scene** (GameState 2), distinct from the login-state fade VFX. On
start it picks ONE random background from `data/ui/loading.dds` / `data/ui/loading06.dds` /
`data/ui/loading08.dds`, plays loading SFX id **920100100**, and draws a progress bar. Its progress
and its exit gate are driven by the **VFS asset PRELOAD finishing** (progress -> 100%) via a bulk
asset-loader worker thread -- it is **NOT** a wait on the enter-game network ack. The bar fill is the
VFS load progress, not a net timer.

---

## 6. The action-id dispatch (UI event routing)

The login window's input handler is the secondary event-handler sub-object's dispatch routine; it
reads the substate through the same field the tick uses (Section 1). UI events carry a leading event
type byte (1 = key-down, 2 = key-up, 3 = mouse-move, 4 = button-press, 5 = button-release, 6 = click
[synthesised only when a release lands on the same widget that was pressed], 7 = double-click,
8 = wheel). Keyboard events come from a DirectInput keyboard thread; mouse events come from the window
procedure. Dispatch is topmost-child-first, first-consumer-wins, before the 3D world view.

The login window dispatches on the widget's command/action id. The recovered action map:

| Event class | Action id | Effect |
|---|---|---|
| key (1) | 9 | Tab: swap focus between the account and password field panels |
| key (1) | 10 | Enter: if substate == 6, run the `game.ver` gate (Section 1.1) then set substate **29**; if substate == 4, set substate **5** (dead path in practice — the tick auto-advances state 4 unconditionally before Enter could dwell there; see substate table row 4 CORRECTED 2026-06-22) |
| click (6) | 101 | quit |
| click (6) | 102 | reveal the server-list panel |
| click (6) | 103 | **Login / OK**: run the `game.ver` gate, persist OPTION_ID if save-id set, set substate **29** |
| click (6) | 104 | toggle the save-ID checkbox (writes the OPTION_ID context) |
| click (6) | 105 | server-list **re-fetch** (Refresh/Help strip): no-op if substate == 35 or within 10000 ms of the last fetch; else set substate **34** |
| click (6) | 106 | server-listbox scroll-up |
| click (6) | 107 | server-listbox scroll-down |
| click (6) | 108 | server-listbox scrollbar thumb |
| click (6) | 109 | account field focus (modal register) |
| click (6) | 110 | password field focus (modal register) |
| click (6) | 111 | option / PIN-confirm route (window-level) |
| click (6) | 112 | option / PIN-cancel route (window-level) |
| click (6) | 113 | notice dialog #1 OK (msg 4023) |
| click (6) | 114 | error dialog #2 OK (msg 4024) |
| plate (7) | 400 / 401 | server-plate pick at substate 37 (Section 1.2): commit -> substate **38** + last-server preference |
| pager (6) | 115..124 | server-list pager at substate 37: `page = action - 115`, repaint only, no commit |
| timer (13) | 10001 | the 30 s connect watchdog firing -> break the login loop (Section 1.4) |

---

## 7. The code-baked LoginWindow element layout (73 widgets)

> **GU coordinate system** (origin, local vs absolute, root anchor, 1:1 blit contract): see
> `specs/frontend_layout_tables.md §0a` — the authoritative statement. Every `(x, y)` value in the
> widget tables below is **LOCAL to the named parent panel**, not an absolute screen pixel. The root
> anchor `(screenW/2−512, screenH/2−384)` is added to all children through the parent cascade.

There is **no on-disk UI layout manifest** for the login window. The login window is a 1368-byte heap
object built once from WinMain (GameState case 1): the ctor installs the two vtables (primary at
object +0x00, secondary event-handler at +0xBC), seeds the flow-substate field (+0x238) to **1** via
the base ctor, seeds the selected-server-index marker (+0x554) to **5** (a placeholder — BuildScene
overwrites it with `NEW_SERVER_INDEX`), seeds **TWO** real lobby worker proc slots (server-list worker
at field+240 / `+0x3C0`, channel-endpoint worker at field+243 / `+0x3CC`) plus **one NULL slot**
(field+246 / `+0x3D8`) — and builds NO widgets. BuildScene (the primary vtable's build slot, invoked
once from WinMain) constructs every child with **integer-literal coordinates** via the shared GU
builders. The tick and event routines only reference already-built children -- they build nothing.

> **Field +0x554 — the selected-server-index marker (CORRECTED).** This field is seeded to 5 by the
> ctor and then overwritten by BuildScene from `NEW_SERVER_INDEX` (the integer read from
> `data/script/uiconfig.lua`). In `PaintServerList` it is compared against each record's `server_id`
> to determine the selection highlight. **It is NOT a seed of the flow-substate field (+0x238)** — the
> substate field is at a different offset and is seeded to 1 by the base ctor. Do not conflate the two.

**Window-level facts (from the build head):**
- **Canvas:** the window anchor is `(screenW/2 - 512, screenH/2 - 384)` -- a **1024x768** surface,
  centred on screen.
- **Config read:** the build loads `data/script/uiconfig.lua` and reads the integer key
  **`NEW_SERVER_INDEX`**, storing it on the window (object +0x554) and into a UI singleton slot;
  it is the "NEW" server badge index used by the server plates — it overwrites the ctor's placeholder 5.
- **Worker threads seeded in the base ctor:** exactly **TWO** real blocking-socket worker procs (slot
  field+240 = server-list fetch thread, field+243 = channel-endpoint fetch thread) plus **one NULL
  slot** (field+246). The NULL slot is not a third worker. See Section 3.
- **Atlas preload order** (4 textures into the window's texture list, each loaded from VFS-or-disk
  with format tag 894720068):
  1. `data/ui/login_slice1.dds` (atlas **A**)
  2. `data/ui/loginwindow.dds` (atlas **B**)
  3. `data/ui/InventWindow.dds` (atlas **C**)
  4. `data/ui/loginwindow_02.dds` (atlas **D**)
- **No login BGM/SFX in the build.** The only LoginWindow sound is the substate-1 intro stinger
  (SFX id **861010105**, category 2) emitted by the tick on the 1→2 edge. The build emits no sound.
- **Fonts:** 15 font slots (0..14) are registered in WinMain (not in the build). Slot 0 = "DotumChe"
  12px. The build only ever overrides a label font once -- font **slot 4** on the channel-block body
  label; all other login labels/fields use the default slot 0.

**Builder contract (shared GU builders).** Every element is built with a 1:1 dest/source rect -- the
builder takes `(texHandle, dstX, dstY, W, H, srcU, srcV[, ...])` where the source rect's W/H equals the
widget W/H (NO scaling, NO separate src-W/src-H argument). 3-state buttons bake three source origins
(normal / hover / pressed). Checkboxes bake two (off / on). Labels carry no atlas. Children are added
to a parent panel, optionally with an action id.

**GUComponent geometry field contract.** The image builder writes positional and source-rect values
into fixed fields of the GUComponent object. These are the fields the tick mutates at runtime (e.g.
to slide the curtain panels) and the fields an implementation must mirror per-widget:

| Field name | Meaning |
|---|---|
| `dst_x` | Destination X (left edge); runtime-mutable |
| `dst_y` | Destination Y (top edge); runtime-mutable |
| `dst_w` / `dst_h` | Destination width / height (equals source W/H; 1:1, no scaling) |
| `src_u` / `src_v` | Source-rect top-left into the atlas |
| `src_u_end` / `src_v_end` | Source-rect bottom-right (builder stores `src + wh`) |
| `tex_handle` | Atlas/texture handle |

The curtain animation is a **positional translate** of the two curtain panels every tick -- the tick
writes only `dst_x`/`dst_y`; it does NOT change any source-rect field. This is confirmed by reading
the image builder and the tick slide loop.

Coordinates below are the literal builder arguments. Atlas A = `login_slice1.dds`, B = `loginwindow.dds`,
C = `InventWindow.dds`, D = `loginwindow_02.dds`. "src" = (srcU, srcV) into that atlas; widget W/H = src
W/H. "field" = the login-window object offset the handle is stored at.

### 7.1 Server-listbox group (built first)

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) | action | field | notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | main panel art | image | window | 0,110,1024,490 | B | 0,0 | -- | +0x270 | hidden at build |
| 2 | notice / agreement panel (`eulaPanel`) | panel | (held) | 270,85,483,490 | B | 0,490 | -- | +0x2BC | host of rows 3..6 + the 22 notice labels (field 175 of the window object; DISTINCT from the server-select grid panel at +0x328 / field 202) |
| 3 | notice scroll-UP arrow | button | notice panel | 467,86,13,10 | B | 483,490 | 106 | +0x318 | |
| 4 | notice scroll-DOWN arrow | button | notice panel | 467,455,13,10 | B | 505,490 | 107 | +0x31C | |
| 5 | notice scrollbar thumb dot | button | notice panel | 469,98,9,9 | B | 496,490 | 108 | +0x320 | |
| 6 | notice panel header strip | image | notice panel | 207,44,70,17 | B | 70,980 | -- | +0x324 | |
| 7..28 | **22 notice body labels** | label | notice panel (+0x2BC / field 175) | X=50, Y=100 step **+18**, 383x50 | -- | -- | -- | +0x2C0 + 4*i | text = message-db id **4001 + i**, i = 0..21 (Y 100->478, < 496) |

> **CORRECTION (reconciled dossier 2026-06-22, binary-won).** The 22 labels (message ids 4001..4022)
> are a **static stacked notice/agreement text column** parented to the notice/agreement panel at
> +0x2BC (object field 175). They are **NOT** server-list row captions, and their parent panel
> (+0x2BC) is **DISTINCT** from the server-select grid panel (+0x328 / field 202). Actions 106/107/108
> are the scroll controls for this notice panel, not EULA accept/scroll. There is no EULA gating in
> the login flow. See `specs/frontend_scenes.md §1.4c`.

### 7.2 Backgrounds + the two channel-selector blocks

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) | flag | notes |
|---|---|---|---|---|---|---|---|---|
| 29 | full upper backdrop | panel | window | 0,0,1024,398 | A | 0,0 | 0 | visible; the carved-iron frame baked art |
| 30 | second main-panel layer | panel | (held +0x328) | 270,85,483,490 | B | 0,490 | 0 | the channel-block host |
| 31 | header strip (2nd) | image | +0x328 | 207,44,70,17 | B | 0,980 | -- | hidden |

**Channel-block loop -- 2 iterations** (X0 = 30, step **+233**; body srcV0 = 448, step **+124**;
action id 400 then 401). Per block it builds 5 children into the channel host (+0x328):

| sub-element | type | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | font | action |
|---|---|---|---|---|---|---|
| header label | label | X, 390, 174, 21 | -- | -- | 0 | (added with action a) |
| body image | image | X+47, 97, 100, 372 | D | (srcV, 6) | -- | -- |
| 3-state toggle | 3-state button | X-6, 97, 202, 372 | D | N(9,6) / H(220,6) / P(220,6) | -- | a = **400 / 401** |
| text label #1 | label | X, 410, 174, 20 | -- | -- | 0 | -- |
| text label #2 | label | X, 430, 174, 20 | -- | -- | **4** | -- |

### 7.3 Decoration sprites + dynamic scrollbar + server name-strip (pager) buttons

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) | action | notes |
|---|---|---|---|---|---|---|---|
| 38 | badge/arrow #1 | image | +0x328 | 0,0,60,39 | B | 500,786 | -- | |
| 39 | badge/arrow #2 | image | +0x328 | 0,0,60,39 | B | 500,786 | -- | |
| 40 | badge/arrow #3 | image | +0x328 | 0,0,60,39 | B | 500,786 | -- | three identical badge sprites |
| 41 | dynamic scrollbar thumb | image | +0x328 | 0, (runtime thumb.y + 8), 46, 168 | D | 700,18 | -- | Y computed from a child's runtime field |
| 42.. | **server name-strip (pager) buttons** | 3-state button | +0x328 | X = 13 **+ 47*i**, 66, 47, 18 | B | N(596,985)/H(643,985)/P(643,985) | **115 + i** | loop while X < 483 -> **10 buttons** (i = 0..9); these are the server-list PAGER buttons |

After the strip loop, the last two strip buttons get frame-origin overrides (distinct scroll-arrow-like
faces): one set to N(690,985)/H(737,985)/P(737,985), the other to N(784,985)/H(831,985)/P(831,985).

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | action | notes |
|---|---|---|---|---|---|---|---|
| 43 | "Refresh/Help" strip btn | 3-state button | (to +0x278) | 456,-3,111,38 | A | N(792,398)/H(602,416)/P(602,416) | **105** | the throttled re-fetch / help button |
| 44 | its caption face plate | image | (to +0x328) | 407,-3,210,70 | A | 743,398 | -- | baked-art face for #43 |

### 7.4 Notice dialog #1 + Error dialog #2

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | align | action | notes |
|---|---|---|---|---|---|---|---|---|---|
| 45 | notice dialog panel | panel | window | 342,289,340,190 | C | 318,647 (flag=1) | -- | -- | shared dragon-frame dialog quad |
| 46 | notice body label | label | dialog#1 | 10,100,330,20 | -- | -- | center | -- | text = message id **4023** |
| 47 | notice OK button | 3-state button | dialog#1 | 120,136,113,40 | C | N(302,900)/H(302,900)/P(415,900) | -- | **113** | |
| 48 | error dialog panel | panel | window | 342,289,340,190 | C | 318,647 (flag=1) | -- | -- | |
| 49 | error body label | label | dialog#2 | 10,100,330,20 | -- | -- | left | -- | text = message id **4024** |
| 50 | error OK button | 3-state button | dialog#2 | 120,136,113,40 | C | N(302,860)/H(302,860)/P(415,860) | -- | **114** | |

### 7.5 Bottom login bar + the ID/PW form

Build-sequence index (the `v185` counter in BuildScene) for the form region runs from **56** (inner
form sub-panel) to **64** (OK button + end of form children). This is the order in which widgets are
constructed, which determines paint order within the panel:

1. [56] Inner form sub-panel (+0x298) — built and set hidden.
2. [57] ID-caption art (+0x29C).
3. [58] PW-caption art (+0x2A0).
4. [59] Small decoration plate (+0x2A4).
5. [60] ID / account textbox (+0x2A8).
6. [61] Password textbox (+0x2AC).
7. [62] Save-ID checkbox (+0x2B0).
8. [63/64] Saved-ID prefill branch (no new widget; sets checkbox state + ID box caption + initial
   focus — see "Conditional default focus" below).
9. [65] OK / Login button (+0x2B4) — built AFTER the checkbox and the prefill branch.

Child-add order into the form panel (determines paint / z-order): ID-caption → PW-caption → deco
plate → ID textbox (action 109) → PW textbox (action 110) → checkbox (action 104) → OK button
(action 103). The form panel is then added to the bottom-bar panel.

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | action | field | notes |
|---|---|---|---|---|---|---|---|---|---|
| 51 | bottom login-bar panel | panel | window | 0, **326*screenH/768**, 1024,442 | A | 0,582 (flag=0) | -- | +0x278 | Y scales with screen height |
| 52 | server-list reveal button | 3-state button | bar | 456,166,112,39 | A | N(154,398)/H(378,398)/P(378,398) | **102** | +0x280 | |
| 53 | login background plate | image | bar | 265,0,494,113 | A | 0,469 | -- | +0x27C | the plate the ID/PW row sits on; repositioned to (494,469) by the curtain reveal edge (Section 1) |
| 54 | inner form sub-panel | panel | bar | 0,0,1024,100 | (none) | -- | -- | +0x298 | invisible layout container; hidden at build |
| 55 | account-label caption art | image | form | 340,30,38,13 | A | 0,398 | -- | +0x29C | baked Korean "ID" art |
| 56 | password-label caption art | image | form | 507,30,49,13 | A | 38,398 | -- | +0x2A0 | baked art |
| 57 | small decoration plate | image | form | 619,86,67,13 | A | 87,398 | -- | +0x2A4 | baked art |
| 58 | **ID / account textbox** | text box | form | 390,32,102,13 | A | 615,404 | **109** | +0x2A8 | per-keystroke length cap **16** chars (GUTextbox +0xD0 = 208); charset mask +0xA4 = 6 (alphanumeric-only filter, NOT a length); IME mode 16; both textboxes share the same src rect (615,404) |
| 59 | **password textbox** | text box | form | 568,32,102,13 | A | 615,404 | **110** | +0x2AC | per-keystroke length cap **12** chars (GUTextbox +0xD0 = 208); charset mask +0xA4 = 129 / 0x81 (allow-all, NOT a length); IME mode 12; masked render (mask bit set — renders each entered character as `*`); same src rect (615,404) as the ID box |
| 60 | **Save-ID checkbox** | 3-state checkbox | form | 694,86,13,13 | A | off(717,398)/on(730,398) | **104** | +0x2B0 | initial checked state + ID prefill (Section 1.3) |
| 61 | **OK / Login button** | 3-state button | form | 456,64,112,39 | A | N(266,398)/H(490,398)/P(490,398) | **103** | +0x2B4 | baked "Login" face; built after the checkbox and the prefill branch |

(The Refresh/Help strip button #43 is added to the bar with action 105; the bar is then added to the
window.)

> **IME mode vs charset mask vs length cap.** Three separate GUTextbox fields govern different
> aspects of each credential box:
> - **+0xD0 (offset 208) — per-keystroke length cap:** ID = **16**, password = **12** (binary-won;
>   these are the caps enforced at every keystroke and on paste). The earlier readings of "6" for ID
>   and "129" for password were the charset mask, not the length.
> - **+0xA4 (offset 164) — charset filter bitmask:** ID = **6** (bits 1+2 = digits + letters,
>   i.e. alphanumeric-only); password = **129 / 0x81** (bit 0 = allow-all, so any printable character
>   is accepted regardless of the other bits).
> - **IME mode field:** ID = 16 (plain-charset entry); password = 12 (masked render mode).
> The masked-render behaviour is a property of the mask bit in the length/flags field (see §2.1
> correction note), not of the IME mode. The mask glyph and its per-character pixel advance are
> internal to the GUTextbox draw path and are not BuildScene constants.

**Conditional default focus (the saved-id branch).** After the checkbox is built [62], and before
the OK button is built [65], the build reads the persisted account id from a UI singleton. Three
branches:
- **Absent / empty:** checkbox unchecked (checked-flag = 0); focus to the **ID** textbox.
- **Present and not `"(null)"`:** checkbox checked (checked-flag = 1); the ID box caption is set to
  the saved account id; focus to the **password** textbox.
- **Present and equal to `"(null)"`:** checkbox checked; ID box caption set to empty; focus to the
  **ID** textbox.

The chosen focus target is passed to the modal-focus call immediately, before the OK button is built.
(See Section 1.3.)

### 7.6 The PIN / second-password modal (child at +0x550)

The PIN modal is a 696-byte aux child window built at object offset **+0x550** with its own texture
list, positioned at dest `(347, 173, 329, 422)`, initially hidden. Its keypad build loads its OWN two
atlases into the modal's texture list: `data/ui/password.dds` (digit/button art, atlas **P**) and
`data/ui/InventWindow.dds` (dragon-frame, atlas **I**), in that order. It then builds, in order:

**Construction order (BuildKeypad):**

1. Masked-echo label (built first; passive).
2..101. 100 digit buttons (double loop: 10 positions × 10 digit glyphs).
102. Reset button (action 11).
103. OK button (action 12).
104. Cancel button (action 13).
105. Nested close panel (atlas I; built hidden; added last, topmost).

Each child is added to the modal panel immediately after it is built (paint / z-order = construction
order).

**Lifecycle (call order):**

- **On LoginWindow build:** modal is constructed, `BuildKeypad` runs, the modal is hidden (via its
  vtable hide slot), and it is added as a child of the LoginWindow. It is built hidden and parented.
- **On show (substate 31):** `SetVisible(true)` first hides the nested close panel, then clears the
  entered-digit collection, then clears the submitted flag, then calls `ScrambleKeypad`, then chains
  to the base widget show. The order is **clear → scramble → show** and must be reproduced faithfully
  (a freshly-shown modal always starts empty with a fresh random layout).
- **Per click:** `OnEvent` runs the base window input dispatch first, then -- only for a click event
  whose qualifier is a confirmed click -- switches on the pressed button's action id (see dispatch
  table below).
- **On poll (substate 32):** the login tick reads both the modal's visible flag and its submitted
  flag; both set ⇒ PIN committed ⇒ advance.

**Action dispatch:**

| Action id | Route | Effect |
|---|---|---|
| 0..99 (build-order) | digit-press handler (OnEvent) | the keypad's own OnEvent resolves the pressed button's build-order action to a digit value via the live scramble permutation; guards < 4 digits; appends the resolved digit; rebuilds the `*`-masked echo. **Do not infer the digit from `action mod 10`** — the mapping is permutation-time, not action-time. |
| **11** | `ScrambleKeypad` | **clear-AND-reshuffle** (clears the entered collection, then re-randomizes the digit layout) — NOT a no-op "reset" |
| **12** | `SubmitOk` | serialize ≤ 4 digits into the PIN string, write to the session PIN slot (max 4 chars + NUL into a 5-byte slot), set submitted flag, re-scramble |
| **13** | `Cancel` | clear entered-digit collection, re-scramble, hide the modal (submitted flag stays 0; login tick will not advance) |

**Scramble mechanism (per show / per Reset / per OK / per Cancel):**

1. Seed the C runtime random generator with wall-clock time (`srand(time(NULL))`; 1-second
   granularity — reproduce as a per-show/per-action reshuffle).
2. Fisher-Yates shuffle a 10-integer digit permutation (one entry per keypad position, values 0..9).
3. For each of the 10 positions, of the 10 stacked buttons at that position, set visible only the one
   whose digit value equals `permutation[position]`; hide the other 9.

**Placement table:**

| sub-element | type | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | tag/action | notes |
|---|---|---|---|---|---|---|
| masked-echo label | label | 81,138,150,22 | -- | -- | (none) | built FIRST; echoes the typed PIN as `*` per entered digit; cleared on show |
| **100 digit buttons** | 3-state button ×100 | X = **55*(p%5) + 28**, Y = **170** (p<5) / **230** (p≥5), 52×52 | P (password) | for digit value v (0..9): N(52*v,560) / H(52*v,664) / P(52*v,612) | build-order action = outer_pos × 10 + inner_digit (range 0..99) | outer loop over 10 keypad positions p=0..9 × inner loop over 10 digit glyphs v=0..9 → 100 buttons total; the 2×5 visible grid is realized at runtime by the scramble (one button visible per position); srcU = `v×52` selects the glyph face; srcV selects the state band. **The digit a button press appends is decided by the keypad's own OnEvent handler consulting the live scramble permutation — it is NOT simply `action mod 10` or `action == digit`.** A port must track which digit each visible button represents via the scramble, not infer it from the action value. |
| Reset | 3-state button | 243,133,58,30 | P | N(663,8)/H(663,88)/P(663,48) | **11** | routes to ScrambleKeypad (clears + reshuffles) |
| OK | 3-state button | 90,290,154,58 | P | N(330,0)/H(330,116)/P(330,58) | **12** | |
| Cancel | 3-state button | 90,350,154,58 | P | N(486,0)/H(486,116)/P(486,58) | **13** | |
| nested close panel | close/exit panel | ((329-340)/2, (422-190)/2, 340,190) ≈ (-5, 116, 340, 190) | I (InventWindow) | 318,647 (flag=1) | -- | centred on the modal's own dims (340 wide > 329 modal width, so overhangs left); built hidden; added last |

> **The "100 digit buttons" are a 10×10 over-build, not a flat grid of 10.** There are 10 keypad
> positions (a 5-column × 2-row pad) and at each position 10 stacked buttons (one per digit glyph
> 0..9); the scramble shows exactly ONE of the 10 stacked buttons per position and hides the other 9.
> The port should replicate this structure so the scramble can swap visible/hidden without rebuilding
> buttons.

> Digit-glyph axis: `srcU = digit_value * 52` selects the glyph column (digit art); `srcV` selects
> the button STATE row (560 = normal / 664 = hover / 612 = pressed); tile 52×52.

### 7.7 Small option sub-panel + trailing 111/112 buttons

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) N/H/P | action | field | notes |
|---|---|---|---|---|---|---|---|---|
| 67 | option sub-panel | panel | window | 356,531,313,132 | (none) | -- | -- | +0x284 | invisible container |
| 68 | image plate #1 | image | sub-panel | 67,48,178,13 | A | 0,437 | -- | +0x294 | baked art |
| 69 | image plate #2 | image | sub-panel | 0,100,313,32 | A | 289,437 | -- | +0x288 | baked art |
| 70 | button #1 | 3-state button | sub-panel | 40,82,110,38 | B | N(520,492)/H(520,492)/P(635,492) | **111** | +0x28C | PIN-confirm route (window level) |
| 71 | button #2 | 3-state button | sub-panel | 164,82,110,38 | B | N(750,492)/H(750,492)/P(865,492) | **112** | +0x290 | PIN-cancel route (window level) |

### 7.8 Quit-confirm + generic Error panel (added last, topmost)

| # | element | type | parent | dest (X,Y,W,H) | atlas | src (U,V) | field | notes |
|---|---|---|---|---|---|---|---|
| 72 | Quit-confirm panel | close/exit panel | window | 342,289,340,190 | C | 318,647 (flag=1) | +0x3B4 | hidden quit-confirm modal |
| 73 | generic Error panel | error panel | window | 342,289,340,190 | C | 318,647 (flag=1) | +0x3B8 | hidden generic error modal |

Both share the same dragon-frame quad (atlas C @ 318,647) and are added last, so they composite over
the form.

> **[capture/debugger-pending]** Whether the small option sub-panel (#67) and its 111/112 buttons are
> a resting "option page" or the PIN-route shims is not statically disambiguated (both the
> window-level 111/112 and the keypad-internal 11/12/13 are wired). A live click trace would pin the
> actual routing edge.

---

## 8. Cross-references

- Credential wire layout (plaintext pre-image + RSA ciphertext framing; the wire opcode value is
  pending): `packets/login.yaml`.
- RSA credential encryption, modulus/exponent parse, PKCS#1 v1.5 type-2, whitening, and the
  secure-context page-guard lifecycle: `specs/crypto.md` section 6 and section 6a.
- Front-end window framework, GU builder contract, atlas inventory, GUComponent geometry offsets:
  `specs/frontend_scenes.md`.
- Broader end-to-end login / char-select / enter-game behaviour, server-name table, lobby host
  resolution: `specs/login_flow.md` and `opcodes.md` Appendix A.
- Char-management requests reachable after enter (create / select / enter / rename / slot-move /
  logout): `packets/cmsg_char_*.yaml`, `packets/cmsg_logout.yaml`.
