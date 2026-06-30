<!--
verification: routing/teardown-control-flow [confirmed] (the existence of TWO distinct world-exit
  opcodes built by TWO distinct teardown routines, the guard on the heavier path, the keepalive-disarm
  ordering, the shared exit tail, and the convergence on scene state 6 sub-state 8 are
  control-flow-confirmed on build 263bd994);
  the deferred completion timed-event in the leave-world tail is id 10001 / 1500 ms (static-confirmed
  on build 263bd994, 2026-06-24 audit -- distinct from the death-countdown event id 10003 of §7);
  the concrete in-game UI control that fires each opcode, whether the SERVER closes the socket on the
  lighter path, and which agent issues the real socket close are static-hypothesis / live-pending (6-D);
  the "4/137" server message is flagged NEEDS-REVIEW (its label is suspect).
  Death & respawn (§7): the death-notification opcode/size/cause-selector, the death actor-state field
  values, the level>=36 modal split, the countdown timed-event id/duration, the respawn-request opcode,
  and the two respawn-response opcodes/sizes are static-confirmed on build 263bd994;
  the concrete meaning of each respawn-choice value (0/1/2/3) and the server-decided respawn location +
  restored HP are RUNTIME-ONLY (server-authoritative / capture-debugger-pending).
  Death-state / PvP death FX (§7.8–§7.9): SmsgActorDeathState (5/79) mode bytes and effect-id
  constants (base 350000038 + variant), and SmsgPvpDeathFx (5/80) PvP engage/disengage effect
  constants (371003701 / 371003702), are CONSUMER-CONFIRMED on build f61f66a9 (CYCLE 15, 2026-06-30);
  body sizes (20 / 16 bytes) and mode-byte body-offsets are capture-UNVERIFIED R-CAP (non-blocking).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-30  # CYCLE 15 P-worldexit: added §7.8–§7.10 (5/79 SmsgActorDeathState, 5/80 SmsgPvpDeathFx); prior: 2026-06-27
evidence: [static-ida, consumer-confirmed]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — appended §7 "Local death & respawn" (death notification
  5/10, respawn request 2/3, respawn responses 4/28 + 5/28, the level>=36 respawn-modal split, and the
  timed-event 10003 death countdown). Death is NOT a world exit — the actor stays in the world session;
  the section is recorded here because world_exit.md owns the death-and-respawn behavioural spec.
  IDB SHA f61f66a9, CYCLE 15 (2026-06-30) — appended §7.8–§7.10: SmsgActorDeathState (5/79) and
  SmsgPvpDeathFx (5/80) death-state push and PvP death FX opcodes; body sizes and mode-byte offsets
  remain R-CAP pending pcap cross-check.
-->

# World Exit — Logout & Leave-World Teardown — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/world_exit.md`
>
> **Scope.** How the client leaves the in-world session and returns to the login screen: the **two
> distinct world-exit opcodes**, the **two distinct teardown routines** that send them, why they are
> never sent together, the shared exit tail, and where the actual session drop happens — **plus the
> local death & respawn flow (§7)**, which this spec owns even though death does **not** leave the
> world (the actor stays in the session and respawns into it). The opcode framing/dispatch and the
> connection lifecycle are owned by neighbours and cited, not duplicated:
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue.
> - `connection_topology.md` — the single persistent opcode socket A and why leave-world does **not**
>   reconnect it (§6: "leave-world / logout-to-menu does NOT close A").
> - `network_dispatch.md` — the dispatcher, the keepalive toggles, and the scene/link reconciler.
> - `scenes/scene_state_machine.md` — the 8-state scene machine the teardown converges on.

---

## 1. Two world-exit opcodes — one or the other, never both

The client has **two distinct ways to leave the in-world session**, each emitting a **different
header-only opcode** from a **different teardown routine**. Exactly one path runs per exit; the choice
is made by the scene/lifecycle state at the moment of exit. *([confirmed]* that two separate routines
and two separate opcodes exist and are mutually exclusive.)*

| Opcode | Name | Sent by | Wire shape | Guarded? | Keepalive | Character |
|--------|------|---------|------------|----------|-----------|-----------|
| **1/0** | **CmsgLogout** | the **quit / logout** teardown routine | **header-only, 8 bytes**, no payload | no | does **not** disarm the keepalive | **fire-and-forget** — arms no in-flight latch; the server is expected to drop the session |
| **2/0** | **CmsgLeaveWorld** | the **leave-world transition** teardown routine | **header-only, 8 bytes**, no payload | **yes** | **disarms the keepalive toggle first** | heavier guarded transition with more elaborate UI/object teardown |

Both opcodes carry **no body** — the 8-byte frame header is the entire message (`opcodes.md §Wire
frame header`).

### 1.1 `1/0 CmsgLogout` — the in-world quit

Emitted when the in-world player **quits** — the ESC / quit-confirm gesture taken while already in the
world. The logout routine is the **lighter** of the two:

- It does **not** disarm the keepalive toggle.
- It arms **no in-flight latch** — it does not register a pending reply slot and does not wait for an
  acknowledgement. It is **fire-and-forget**; the session drop is left to the server (and/or to the
  scene reaching its return state — §3).

### 1.2 `2/0 CmsgLeaveWorld` — the guarded leave-world transition

Emitted by a **guarded** transition routine that runs **only when the entered-world flag is set and a
player-lifecycle field is in the right state**. When those conditions hold, this routine is the
**heavier** path and runs in a deliberate order:

1. **First, disarm the `2/112` keepalive toggle** (stop the in-world heartbeat before tearing down).
2. Perform the more elaborate **UI / object teardown** (more than the logout path does).
3. **Then** send `2/0 CmsgLeaveWorld`.

The keepalive disarm being **ordered before** the send is the defining structural difference from the
logout path. *([confirmed]* on the guard, the disarm-then-send ordering, and that this path is heavier.)*

---

## 2. Shared exit tail

Despite the two opcodes and two routines, both paths **converge on the same exit tail** after the
opcode is sent: *([confirmed]* on each step of the tail.)*

1. **Play an exit sound effect.**
2. **Tear down the in-world UI / panels.**
3. **Schedule a deferred completion event** — timed event **id `10001`**, **1500 ms** ahead (a delayed
   finish, not an immediate transition). This is the universal scene-bridge event id also used
   elsewhere in the scene state machine; it is **distinct** from the death-countdown event id `10003`
   of §7. *([confirmed]* event id 10001 / 1500 ms from `Scene_LeaveWorldToLogout`.)*
4. **Converge on scene state 6, sub-state 8** — the **return-to-login** scene
   (`scenes/scene_state_machine.md`).

So the *destination* is identical (back to login); only the *opcode sent* and the *amount of
teardown* differ between the two routines.

---

## 3. Who actually closes the socket — NOT these routines

**Neither teardown routine performs an explicit client socket close.** *([confirmed]* that no socket
close is issued from either routine.)* This is consistent with `connection_topology.md §6`, which
records that leave-world / logout-to-menu does **not** close the persistent opcode socket A from the
exit path itself.

The real session drop is therefore either:
- **server-driven** (the server drops the connection in response to the exit opcode), and/or
- **driven by the scene reaching state 6 / sub-state 8** (a return-state handler or the deferred timed
  event of §2 issues the actual teardown of the connection).

Exactly which of these performs the close — and whether the server closes on the lighter `1/0` path —
is **live-pending (6-D)** (see §5).

---

## 4. NEEDS-REVIEW — the suspect "4/137" server message

A **server-driven** message currently labelled **"4/137"** reads, on the routing side, like a
**scene-control message**: it routes **one subtype to the leave-world teardown** path and **another
subtype to the logout teardown** path. In other words, a single server message can drive **either**
exit path.

> **Do not over-assert what "4/137" is.** The label is **suspect** and is recorded here only as a
> NEEDS-REVIEW item. What is structurally observed is narrow: *a server message exists that can dispatch
> into either teardown routine by subtype.* Its true opcode identity, its canonical name, and its full
> field shape are **not** asserted by this spec and must be re-confirmed before being relied on
> (`opcodes.md` is the catalogue of record for any eventual identity).

---

## 5. Open items (live-pending (6-D))

- **Which concrete in-game UI control fires `1/0` vs `2/0`** — the exact button / menu entry / ESC
  sub-state that selects the logout path versus the leave-world transition path.
- **Whether the server actually closes the socket on `1/0`** (the fire-and-forget assumption that the
  server drops the session on logout).
- **Whether a state-6/8 return handler or the deferred timed event (§2) issues the real socket close**
  on either path.
- **The "4/137" label review** (§4) — confirm or refute the opcode identity and the by-subtype dispatch
  into the two teardown routines.

---

## 7. Local death & respawn

Death is **not** a world exit: when the local player dies the actor **remains in the in-world session**
and respawns back into it — no exit opcode is sent, the persistent opcode socket stays open, and the
scene does **not** converge on the return-to-login state of §2. This section is recorded here because
this spec owns the **death-and-respawn behavioural flow**; the wire identities are catalogued in
`opcodes.md` and the field shapes in `packets/`.

The single decisive fact for the whole flow: **the server is authoritative over where the actor
respawns and with what restored HP.** The client only *requests a choice* of respawn option; the
server decides the location and vitals and pushes them back. *([confirmed]* that the client computes
no respawn position and no restored-HP value.)*

### 7.1 Death state on the actor

When death is processed, two fields on the actor object record the dead state, and both are read
elsewhere as the canonical "is this actor dead" tests:

| Actor field | Dead value | Alive/normal value | Role |
|---|---|---|---|
| **alive gate flag** (actor +1424) | **0** = dead | 1 = alive | generic "alive & interactable" guard — movement / targeting / pickup all early-return while it is `0` |
| **action-state** (actor +1420) | **8** = death state | 1 = idle/normal | the canonical "is this actor dead" enum value |

*([confirmed]* both field offsets and their dead values.)* The implicit input lockout follows from the
alive gate: while it reads `0`, the movement / targeting / pickup paths refuse to act, and the respawn
modal is shown — there is no separate "input disabled" boolean.

### 7.2 Death notification — inbound `5/10`

The server announces a death with **S2C `5/10`**, a **20-byte** body. The body carries a
**death-cause** selector that branches the local death reaction:

| death-cause value | Meaning |
|---|---|
| **0** | normal death |
| **1** | player-kill (PK), variant A |
| **2** | player-kill (PK), variant B |
| **3** | special death (no town-respawn option) |

*([confirmed]* opcode, 20-byte size, and the four-value cause selector.)*

### 7.3 The local death modal — level-gated layout

On the local player's death the client opens a respawn **modal**, and **the player's level selects
which modal layout is shown**:

- **level ≥ 36 → modal mode 1** (the higher-level respawn-option layout).
- **otherwise → modal mode 3** (the lower-level / alternate respawn layout).

*([confirmed]* the level-36 threshold and the mode-1-vs-mode-3 split.)* A special death (cause `3`)
also resolves to the mode-3 layout (no town-respawn option).

### 7.4 The death countdown — timed event `10003`

A respawn **countdown** drives the wait-to-respawn. It is a **timed event, id `10003`**, with a
**600-second** duration, and it is **region-gated** — it only runs where the region permits (some
regions present a different/shorter countdown context). When the countdown elapses, the client issues
the respawn request automatically (the default choice — §7.5). *([confirmed]* the event id, the
600-second duration, and the region gate.)*

### 7.5 Respawn request — outbound `2/3`

The client sends its respawn decision with **C2S `2/3`**, carrying a single **respawn-choice** field
(a 16-bit value). The choice takes one of **`0` / `1` / `2` / `3`**, each selecting a different respawn
option; the same request is sent both from the modal buttons and automatically when the countdown of
§7.4 reaches zero. *([confirmed]* opcode `2/3` and the single respawn-choice field.)*

> **RUNTIME-ONLY:** the concrete meaning of each choice value (e.g. town vs nearest point vs accept-a-
> revive vs respawn-in-place) is **not asserted** here — the exact value→option mapping is server-
> contract-dependent and stays capture/debugger-pending.

### 7.6 Respawn responses — inbound `4/28` and `5/28`

The server answers a respawn with **two distinct messages**, depending on which actor respawns:

| Opcode | Body | Role |
|---|---|---|
| **S2C `4/28`** | **20 bytes** | local-player respawn confirmation — relocates/recreates the local actor at the **server-chosen** position |
| **S2C `5/28`** | **12 bytes** | remote-actor respawn — recreates another actor at its respawn point |

*([confirmed]* both opcodes and both body sizes.)* The respawn **position and the restored HP** carried
by / following these messages are **server-authoritative** — the client applies what the server sends;
it does not choose the location and does not compute the restored HP (restored vitals arrive through
the ordinary actor-vitals push, not from a client-side respawn formula).

### 7.7 What the death flow does NOT do

- **No world exit.** No `1/0` / `2/0` is sent on death; §1–§3 do not run.
- **No client-side death-penalty arithmetic.** The client formats death **notice text only** — it
  computes no XP-loss, no durability-loss, and no item-drop list. Any penalty magnitudes and any
  dropped items are **server-authoritative (RUNTIME-ONLY)** and arrive as ordinary server-driven
  updates.

---

### 7.8 `SmsgActorDeathState` — inbound `5/79`

The server pushes actor death-state changes and death effects with **S2C `5/79`
`SmsgActorDeathState`**, a body of **20 bytes** [R-CAP: body size capture-pending]. The handler
resolves the target actor by key, skips if not found or if it is the local player, then dispatches
on a **DeathOp / mode** byte.

> **R-CAP — capture-pending (non-blocking):** the 20-byte body size and the body-offset of the
> DeathOp byte (0x08) are CONSUMER-CONFIRMED from the handler read sequence but have not been
> cross-checked against a live pcap. Confirm both before relying on them for a wire implementation.

#### Field table — 20-byte body (CONSUMER-CONFIRMED read sequence)

| Body offset | Width | Field | Notes |
|---|---|---|---|
| 0x00 | 4 | leading dword (not consumed) | ignored by this handler; likely a cross-handler serial or zone discriminator — see §7.10 |
| 0x04 | 4 | dying-actor key | actor lookup (kind = 1); also the effect anchor in mode 1 |
| 0x08 | 1 | **DeathOp / mode selector** (values 0 / 1 / 2 / 3) | dispatch byte [R-CAP: body-offset capture-pending] |
| 0x09–0x0B | 3 | alignment padding (not read) | |
| 0x0C | 4 | mode sub-selector | meaning is mode-dependent (see mode table below) |
| 0x10 | 4 | source / killer-actor key | kind = 3 lookup; consumed only in mode 0 |

#### DeathOp / mode table (CONSUMER-CONFIRMED)

| Mode | Behaviour | Sub-selector @0x0C | Killer key @0x10 |
|---|---|---|---|
| **0** | Killed-by visual setup: records the killer and puts the dying actor into its killed-by visual state. | yes — passed as killer-id argument | yes — kind = 3 actor lookup |
| **1** | Spawn a **death effect** anchored on the dying actor. Effect id selected by sub-selector — see §7.8.1. | yes — variant 1..7 | no |
| **2** | Set dying actor death sub-state: **6** if sub-selector == 1, else **7**. | yes — 1-vs-not test | no |
| **3** | Clear / revive: resets actor to alive/idle (action-state = 1, death sub-state = 0, releases and re-attaches effect / weapon nodes, snaps idle motion). | no | no |

#### §7.8.1 Mode-1 effect-id selector (CONSUMER-CONFIRMED)

Sub-selector (body offset 0x0C) in range 1..7 selects a death-effect catalogue id by:

**effect id = 350000038 + sub-selector** (sub-selector ∈ {1, 2, 3, 4, 5, 6, 7})

| Sub-selector | Effect id |
|---|---|
| 1 | 350000039 |
| 2 | 350000040 |
| 3 | 350000041 |
| 4 | 350000042 |
| 5 | 350000043 |
| 6 | 350000044 |
| 7 | 350000045 |

Sub-selector 0 or out of range leaves the effect id at 0 (the variant default).

---

### 7.9 `SmsgPvpDeathFx` — inbound `5/80`

The server pushes PvP duel death FX (persistent aura engage / disengage burst) with **S2C `5/80`
`SmsgPvpDeathFx`**, a body of **16 bytes** [R-CAP: body size capture-pending]. The handler resolves
a primary actor by key, skips if not found or local player, then dispatches on a **DeathOp / mode**
byte.

> **R-CAP — capture-pending (non-blocking):** the 16-byte body size and the body-offset of the
> DeathOp byte (0x0A) are CONSUMER-CONFIRMED from the handler read sequence but have not been
> cross-checked against a live pcap. Confirm both before relying on them for a wire implementation.

#### Field table — 16-byte body (CONSUMER-CONFIRMED read sequence)

| Body offset | Width | Field | Notes |
|---|---|---|---|
| 0x00 | 4 | leading dword (not consumed) | ignored by this handler; same pattern as 5/79 — see §7.10 |
| 0x04 | 4 | primary-actor key | kind = 1 lookup (the PvP subject) |
| 0x08 | 1 | **gate flag** | must equal 1 for the effect-spawn step; both modes check this |
| 0x09 | 1 | padding (not read) | |
| 0x0A | 1 | **DeathOp / mode selector** (values 1 or 6) | dispatch byte [R-CAP: body-offset capture-pending] |
| 0x0B | 1 | padding (not read) | |
| 0x0C | 4 | opponent / source-actor key | kind = 3 lookup; also used as the effect anchor |

#### DeathOp / mode table (CONSUMER-CONFIRMED)

| Mode | Behaviour | Gate @0x08 | Effect id(s) |
|---|---|---|---|
| **1** | PvP **engage**: orientates both actors toward each other, puts both into a motion-fx state, then — if gate == 1 — spawns a **persistent anchored aura** on the opponent actor key. | must be 1 to spawn the aura; no-op after state changes otherwise | **371003701** (engage aura) |
| **6** | PvP **disengage/end**: clears both actors' motion-fx state (motion = 0), **deactivates** any active 371003701 on the opponent, then — if gate == 1 — spawns a disengage burst on the opponent. | controls the burst spawn only | deactivate **371003701**, then spawn **371003702** |
| (other) | no-op | — | — |

The persistent aura spawned by mode 1 (`371003701`) is the same effect id explicitly deactivated by mode 6; `371003702` is the mode-6 closing burst.

---

### 7.10 Open items — death-family opcodes 5/79 and 5/80

- **Leading dword at body offset 0x00 (both 5/79 and 5/80):** both handlers leave this field unread. It likely acts as a cross-handler serial, echo, or zone discriminator shared across the Push-major-5 family. RUNTIME-ONLY — not determinable from the two consumers alone; not a blocker for the DeathOp / effect facts.
- **R-CAP — body sizes and mode-byte body-offsets:** the 20-byte / 16-byte body sizes and the DeathOp body-offsets (0x08 for 5/79, 0x0A for 5/80) are CONSUMER-CONFIRMED from the handler read sequence but remain **capture-pending** for wire-level cross-check (tagged R-CAP, non-blocking).

---

## 6. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The two world-exit opcodes `1/0` and `2/0` and their teardown routines (§1) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| Why leave-world does not reconnect / the persistent socket lifecycle (§3) | `connection_topology.md §6` |
| The `2/112` keepalive toggle disarmed before `2/0` (§1.2) | `network_dispatch.md` (keepalives) |
| The return scene state 6 / sub-state 8 (§2) | `scenes/scene_state_machine.md` |
| Death/respawn opcodes `5/10`, `2/3`, `4/28`, `5/28` (§7) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| Death-state actor fields, respawn modal, the `10003` countdown engine (§7.1–§7.4) | `specs/combat.md` (death-state convention), `specs/world_systems.md` (death/respawn timer + modal) |
| Death-state push `5/79` (`SmsgActorDeathState`) and PvP death FX `5/80` (`SmsgPvpDeathFx`) (§7.8–§7.9) | `opcodes.md` (catalogue), `packets/` (field specs) |
