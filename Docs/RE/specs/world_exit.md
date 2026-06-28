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
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-27  # CYCLE 14 re-anchor: confirmatory — 1 re-confirmed SAME; prior: 2026-06-24
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — appended §7 "Local death & respawn" (death notification
  5/10, respawn request 2/3, respawn responses 4/28 + 5/28, the level>=36 respawn-modal split, and the
  timed-event 10003 death countdown). Death is NOT a world exit — the actor stays in the world session;
  the section is recorded here because world_exit.md owns the death-and-respawn behavioural spec.
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

## 6. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The two world-exit opcodes `1/0` and `2/0` and their teardown routines (§1) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| Why leave-world does not reconnect / the persistent socket lifecycle (§3) | `connection_topology.md §6` |
| The `2/112` keepalive toggle disarmed before `2/0` (§1.2) | `network_dispatch.md` (keepalives) |
| The return scene state 6 / sub-state 8 (§2) | `scenes/scene_state_machine.md` |
| Death/respawn opcodes `5/10`, `2/3`, `4/28`, `5/28` (§7) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| Death-state actor fields, respawn modal, the `10003` countdown engine (§7.1–§7.4) | `specs/combat.md` (death-state convention), `specs/world_systems.md` (death/respawn timer + modal) |
